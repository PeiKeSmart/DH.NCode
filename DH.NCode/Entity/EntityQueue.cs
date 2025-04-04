﻿using System.Collections.Concurrent;
using System.Diagnostics;
using NewLife;
using NewLife.Log;
using NewLife.Threading;
using XCode.DataAccessLayer;

namespace XCode;

/// <summary>实体队列。支持凑批更新数据，包括Insert/Update/Delete/Upsert</summary>
public class EntityQueue : DisposeBase
{
    #region 属性
    /// <summary>需要近实时保存的实体队列</summary>
    private ConcurrentDictionary<IEntity, IEntity> Entities { get; set; } = new ConcurrentDictionary<IEntity, IEntity>();

    /// <summary>需要延迟保存的实体队列</summary>
    private ConcurrentDictionary<IEntity, DateTime> DelayEntities { get; } = new ConcurrentDictionary<IEntity, DateTime>();

    /// <summary>调试开关，默认false</summary>
    public Boolean Debug { get; set; }

    /// <summary>数据会话，分表分库时使用</summary>
    public IEntitySession Session { get; }

    /// <summary>是否仅插入。默认false</summary>
    public Boolean InsertOnly { get; set; }

    /// <summary>数据更新方法。Insert/Update/Delete/Upsert/Replace</summary>
    public DataMethod Method { get; set; }

    /// <summary>
    /// 是否显示SQL
    /// </summary>
    public Boolean? ShowSQL { get; set; }

    /// <summary>周期。默认1000毫秒，根据繁忙程度动态调节，尽量靠近每次持久化1000个对象</summary>
    public Int32 Period { get; set; } = 1000;

    /// <summary>最大个数，超过该个数时，进入队列将产生堵塞。默认1_000_000</summary>
    public Int32 MaxEntity { get; set; } = 1_000_000;

    /// <summary>保存速度，每秒保存多少个实体</summary>
    public Int32 Speed { get; private set; }

    /// <summary>链路追踪</summary>
    public ITracer? Tracer { get; set; } = DAL.GlobalTracer;

    private TimerX? _Timer;
    private String? _lastTraceId;
    #endregion

    #region 构造
    /// <summary>实例化实体队列</summary>
    public EntityQueue(IEntitySession session) => Session = session;

    /// <summary>销毁时，持久化队列</summary>
    /// <param name="disposing"></param>
    protected override void Dispose(Boolean disposing)
    {
        base.Dispose(disposing);

        _Timer.TryDispose();

        var count = DelayEntities.Count + Entities.Count;
        if (count > 0)
        {
            var task = Task.Run(() => Work(null));
            if (!task.Wait(3_000))
            {
                using var span = DefaultTracer.Instance?.NewError($"db:EntityQueueNotEmptyOnDispose", $"{Session.TableName}实体队列退出时，稍有[{count:n0}]条数据没有保存");
            }
        }
    }
    #endregion

    #region 方法
    /// <summary>添加实体对象进入队列</summary>
    /// <param name="entity">实体对象</param>
    /// <param name="msDelay">延迟保存的时间</param>
    /// <returns>返回是否添加成功，实体对象已存在于队列中则返回false</returns>
    public Boolean Add(IEntity entity, Int32 msDelay)
    {
        // 首次使用时初始化定时器
        if (_Timer == null)
        {
            lock (this)
            {
                _Timer ??= new TimerX(Work, null, Period, Period, "EQ")
                {
                    Async = true,
                    //CanExecute = () => DelayEntities.Any() || Entities.Any()
                };
            }
        }

        _lastTraceId = DefaultSpan.Current?.ToString();

        var rs = false;
        if (msDelay <= 0)
            rs = Entities.TryAdd(entity, entity);
        else
            rs = DelayEntities.TryAdd(entity, TimerX.Now.AddMilliseconds(msDelay));
        if (!rs) return false;

        Interlocked.Increment(ref _count);

        // 超过最大值时，堵塞一段时间，等待消费完成
        if (_count >= MaxEntity)
        {
            var ss = Session;
            using var span = DefaultTracer.Instance?.NewError($"db:MaxQueueOverflow", $"{ss.TableName}实体队列溢出，超过最大值{MaxEntity:n0}");
            while (_count >= MaxEntity)
            {
                Thread.Sleep(100);
            }
        }

        return true;
    }

    /// <summary>当前缓存个数</summary>
    private Int32 _count;

    private void Work(Object? state)
    {
        var list = new List<IEntity>();
        var n = 0;

        // 检查是否有延迟保存
        var ds = DelayEntities;
        if (ds.Any())
        {
            var now = TimerX.Now;
            foreach (var item in ds)
            {
                if (item.Value < now && !list.Contains(item.Key)) list.Add(item.Key);

                n++;
            }
            // 从列表删除过期
            foreach (var item in list)
            {
                ds.Remove(item);
            }
        }

        // 检查是否有近实时保存
        var es = Entities;
        if (es.Any())
        {
            // 为了速度，不拷贝，直接创建一个新的集合
            Entities = new ConcurrentDictionary<IEntity, IEntity>();
            //list.AddRange(es.Keys);
            foreach (var item in es)
            {
                if (!list.Contains(item.Key)) list.Add(item.Key);
            }

            n += es.Count;
        }

        if (list.Count > 0)
        {
            Interlocked.Add(ref _count, -list.Count);

            var ss = Session;
            DefaultSpan.Current = null;
            using var span = Tracer?.NewSpan($"db:{ss.ConnName}:Queue:{ss.TableName}", list.Count, list.Count);
            if (_lastTraceId != null) span?.Detach(_lastTraceId);
            _lastTraceId = null;

            try
            {
                Process(list);
            }
            catch (Exception ex)
            {
                span?.SetError(ex, null);
                throw;
            }
        }
    }

    private void Process(Object state)
    {
        if (state is not ICollection<IEntity> list) return;

        var ss = Session;

        var speed = Speed;
        if (Debug || list.Count > 100_000)
        {
            var cost = speed == 0 ? 0 : list.Count * 1000 / speed;
            XTrace.WriteLine($"实体队列[{ss.TableName}/{ss.ConnName}]\t保存 {list.Count:n0}\t预测耗时 {cost:n0}ms");
        }

        var dss = ss.Dal.Session;
        var old = dss.ShowSQL;
        if (ShowSQL != null) dss.ShowSQL = ShowSQL.Value;

        var sw = Stopwatch.StartNew();

        // 分批
        var batchSize = 10_000;
        for (var i = 0; i < list.Count;)
        {
            var batch = list.Skip(i).Take(batchSize).ToList();
            DefaultSpan.Current?.AppendTag($"batch={batch.Count}");

            try
            {
                OnProcess(batch);
            }
            catch (Exception ex)
            {
                OnError(batch, ex);
            }

            i += batch.Count;
        }

        sw.Stop();

        dss.ShowSQL = old;

        // 根据繁忙程度动态调节
        // 大于1000个对象时，说明需要加快持久化间隔，缩小周期
        // 小于1000个对象时，说明持久化太快了，加大周期
        var p = Period;
        if (list.Count > 1000)
            p = p * 1000 / list.Count;
        else
            p = p * 1000 / list.Count;

        // 最小间隔
        if (p < 500) p = 500;
        // 最大间隔
        if (p > 5000) p = 5000;

        if (p != Period && _Timer != null)
        {
            Period = p;
            _Timer.Period = p;
        }

        var ms = sw.Elapsed.TotalMilliseconds;
        Speed = ms == 0 ? 0 : (Int32)(list.Count * 1000 / ms);
        if (Debug || list.Count > 10000)
        {
            var msg = $"实体队列[{ss.TableName}/{ss.ConnName}]\t保存 {list.Count:n0}\t耗时 {ms:n0}ms\t速度 {speed:n0}tps\t周期 {p:n0}ms";
            DefaultSpan.Current?.AppendTag($"Cost: {ms}ms");
            XTrace.WriteLine(msg);
        }

        // 马上再来一次，以便于连续处理数据
        _Timer?.SetNext(-1);
    }

    /// <summary>处理一批数据。插入或更新</summary>
    /// <param name="batch"></param>
    protected virtual void OnProcess(IList<IEntity> batch)
    {
        var ss = Session;

        if (Method > 0)
        {
            switch (Method)
            {
                case DataMethod.Insert:
                    batch.Insert(null, ss);
                    break;
                case DataMethod.Update:
                    batch.Update(null, ss);
                    break;
                case DataMethod.Delete:
                    batch.Delete(null, ss);
                    break;
                case DataMethod.Upsert:
                    batch.Upsert(null, null, null, ss);
                    break;
                case DataMethod.Replace:
                    batch.BatchReplace(option: null, ss);
                    break;
                default:
                    batch.SaveWithoutValid(null, ss);
                    break;
            }
        }
        else
        {
            // 实体队列SaveAsync异步保存时，如果只插入表，直接走批量Insert，而不是Upsert
            if (InsertOnly)
                batch.Insert(null, ss);
            else
                batch.SaveWithoutValid(null, ss);
        }
    }

    /// <summary>发生错误</summary>
    /// <param name="list"></param>
    /// <param name="ex"></param>
    protected virtual void OnError(IList<IEntity> list, Exception ex) => XTrace.WriteException(ex);
    #endregion
}