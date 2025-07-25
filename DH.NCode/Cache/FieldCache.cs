﻿using System.ComponentModel;
using NewLife;
using XCode.Configuration;

namespace XCode.Cache;

/// <summary>统计字段缓存</summary>
/// <typeparam name="TEntity"></typeparam>
[DisplayName("统计字段")]
public class FieldCache<TEntity> : EntityCache<TEntity> where TEntity : Entity<TEntity>, new()
{
    /// <summary>最大行数。默认50</summary>
    public Int32 MaxRows { get; set; } = 50;

    /// <summary>数据源条件</summary>
    public WhereExpression? Where { get; set; }

    /// <summary>排序子句。默认按照分组计数降序</summary>
    public String OrderBy { get; set; } = "group_count desc";

    /// <summary>获取显示名的委托</summary>
    public Func<TEntity, String>? GetDisplay { get; set; }

    /// <summary>显示名格式化字符串，两个参数是名称和个数</summary>
    public String DisplayFormat { get; set; } = "{0} ({1:n0})";

    private readonly String _fieldName;
    private FieldItem _field = null!;
    private FieldItem _Unique = null!;
    private Boolean _inited;

    /// <summary>对指定字段使用实体缓存</summary>
    /// <param name="fieldName"></param>
    public FieldCache(String fieldName)
    {
        WaitFirst = false;
        //Expire = 10 * 60;
        FillListMethod = Search;
        _fieldName = fieldName;

        LogPrefix = $"FieldCache<{typeof(TEntity).Name}+{_fieldName}>";
    }

    private void Init()
    {
        if (_inited) return;

        if (_field == null && !_fieldName.IsNullOrEmpty()) _field = Entity<TEntity>.Meta.Table.FindByName(_fieldName)!;

        if (_field != null && _Unique == null)
        {
            var tb = _field.Table;
            var id = tb.Identity;
            if (id == null && tb.PrimaryKeys.Length == 1) id = tb.PrimaryKeys[0];
            _Unique = id ?? throw new Exception($"{tb.TableName}缺少唯一主键，无法使用缓存");
        }

        // 数据量较小时，缓存时间较短
        var count = Entity<TEntity>.Meta.Count;
        if (count < 100_000)
            Expire = 600;
        else
        {
            var exp = XCodeSetting.Current.FieldCacheExpire;
            if (exp <= 0) exp = 3600;

            Expire = exp;
        }

        _inited = true;
    }

    private IList<TEntity> Search()
    {
        // 这里也要初始化，因为外部可能直接访问Entities属性
        Init();

        Expression? exp = Where?.GroupBy(_field);
        exp ??= _field.GroupBy();
        return Entity<TEntity>.FindAll(exp, OrderBy, _Unique.Count("group_count")! & _field, 0, MaxRows);
    }

    private IDictionary<String, String> GetAll()
    {
        //var id = _field.Table.Identity;
        var list = Entities.Take(MaxRows).ToList();

        var dic = new Dictionary<String, String>();
        foreach (var entity in list)
        {
            var k = entity[_field.Name] + "";
            var v = k;
            if (GetDisplay != null)
            {
                v = GetDisplay(entity);
                if (v.IsNullOrEmpty()) v = $"[{k}]";
            }

            dic[k] = String.Format(DisplayFormat, v, entity["group_count"]);
        }

        // 更新缓存
        if (dic.Count > 0)
        {
            var key = $"{typeof(TEntity).Name}_{_field?.Name}";
            var dc = DataCache.Current;

            dc.FieldCache[key] = dic;
            dc.SaveAsync();
        }

        _task = null;

        return dic;
    }

    private Task<IDictionary<String, String>>? _task;
    /// <summary>获取所有类别名称</summary>
    /// <returns></returns>
    public IDictionary<String, String> FindAllName()
    {
        Init();

        var key = $"{typeof(TEntity).Name}_{_field?.Name}";
        var dc = DataCache.Current;

        if (_task == null || _task.IsCompleted) _task = Task.Run(GetAll);

        // 优先从缓存读取
        if (dc.FieldCache.TryGetValue(key, out var rs)) return rs;

        return _task.Result;
    }

    #region 辅助
    /// <summary>输出名称</summary>
    /// <returns></returns>
    public override String ToString()
    {
        var type = GetType();
        var name = type.GetDisplayName() ?? type.Name;
        return $"{name}<{typeof(TEntity).FullName}>[{_field.Name}]";
    }
    #endregion
}