# 缓存基类与惰性消费者（CacheBase / LazyConsumer）

XCode 所有缓存类（`EntityCache`、`SingleEntityCache`、`DbCache`、`FieldCache`）都继承自 `CacheBase`，并拥有一个内置的 `LazyConsumer` 用于异步串行刷新。

---

## 1. CacheBase 双层结构

```
CacheBase                          // 基础行为：Debug 开关、统计显示、日志
  └── CacheBase<TEntity>           // 泛型扩展：连接名/表名上下文切换
        ├── EntityCache<TEntity>
        ├── SingleEntityCache<TKey, TEntity>
        ├── DbCache
        └── FieldCache<TKey, TEntity>
```

### 1.1 非泛型 CacheBase

- `Debug`：静态开关，DEBUG 编译时默认 true
- `Period`：显示统计日志的周期（来自 `XCodeSetting.CacheStatPeriod`）
- `WriteLog()`：仅在 `Debug=true` 时输出
- `CheckShowStatics()`：定期输出所有注册缓存实例的命中统计

### 1.2 泛型 CacheBase<TEntity>

- `ConnName` / `TableName`：缓存绑定的连接名和表名
- `Consumer`：内置 `LazyConsumer`，供子类触发异步刷新
- `Invoke<T, TResult>(callback, arg)`：

  在执行数据查询委托前，临时将 `Entity<TEntity>.Meta.ConnName/TableName` 切换为本缓存所绑定的值，查询完成后自动还原。

  这是支持"同一个实体类绑定多个不同分片缓存"的关键。

---

## 2. LazyConsumer（惰性串行消费者）

### 2.1 设计目标

缓存刷新必须是串行的（防止多次并发从 DB 拉数据），但不应阻塞业务线程。

`LazyConsumer` 的解法：

- 任务入队 `ConcurrentQueue<Action>`
- 若当前无活跃后台任务，则通过 `Task.Run` 启动一个
- 启动权用 `Interlocked.CompareExchange` 原子争夺，保证只有一个后台任务在跑
- 后台任务 drain（排空）队列后自动退出
- 空闲期不占任何线程

### 2.2 线程模型

```
业务线程 A --Run(刷新)--> Queue
业务线程 B --Run(刷新)--> Queue
                         ↓
                     仅一个 Task.Run 起来
                         ↓
                   按入队顺序依次执行
                         ↓
                    Queue 空 → 后台退出
```

### 2.3 错误处理

单个任务 `action()` 抛异常时，会被 `catch` 吞掉（记录日志可选），不会中断整个队列。

---

## 3. 缓存更新的典型链路

以 `EntityCache` 为例：

1. 数据变更触发 `EntitySession.Flush()` 发出信号
2. 缓存监听到信号，调用 `Consumer.Run(() => { /* 从 DB 重新拉数据 */ })`
3. `LazyConsumer` 把任务入队；如果已有后台任务在跑，则合并到当前批
4. 后台任务执行 `Invoke` → 临时切 ConnName/TableName → `FindAll()` → 还原

---

## 4. 统计与可见性

- 设置 `CacheBase.Debug = true` 可看到每次缓存命中/失效日志
- `Period` 控制定期输出所有缓存的命中统计（仅输出前 10 个已注册缓存）
- 生产环境建议关闭 `Debug`，仅保留 `Period` 观测

---

## 5. 关联阅读

- `/xcode/cache_overview`
- `/xcode/entity_cache`
- `/xcode/single_cache`
- `/xcode/db_cache`
- `/xcode/field_cache`
