# 实体事务（EntityTransaction）

XCode 通过 `EntityTransaction<TEntity>` 封装了"区域事务"模式 — 进入 `using` 块即自动开始事务，显式调用 `Commit()` 才提交，否则离开块时自动回滚。

## 1. 快速开始

### 1.1 强类型事务（推荐）

```csharp
using var et = new EntityTransaction<Order>();

var order = Order.FindByKey(1001);
order.Status = 2;
order.Update();

var detail = new OrderDetail { OrderId = 1001, Qty = 5 };
detail.Insert();

et.Commit();   // 两个操作在同一事务中提交
// 离开 using 块前未调用 Commit 则自动回滚
```

### 1.2 指定数据库连接事务

当跨多个实体但同属一个连接时，可直接用 `DAL` 构建事务：

```csharp
var dal = DAL.Create("MyDb");
using var et = new EntityTransaction(dal);

Order.Update(Order._.Status == 2, Order._.Id == 1001);
Log.Insert(new Log { Action = "关闭订单" });

et.Commit();
```

### 1.3 指定 IsolationLevel

```csharp
using var et = new EntityTransaction(dal.Session, IsolationLevel.Serializable);
// ...
et.Commit();
```

## 2. 自动回滚保证

`EntityTransaction` 继承自 `DisposeBase`，在 `Dispose` 阶段若还未完成（`!hasFinish`），会调用 `Rollback()`：

```csharp
// 异常发生时 using 块离开 → Dispose → 自动回滚
using var et = new EntityTransaction<Order>();
order.Qty -= 5;
order.Update();
if (stock < 5) throw new Exception("库存不足");   // 异常 → 自动回滚
et.Commit();
```

## 3. 缓存与事务的关系

实体缓存在事务提交/回滚时有明确规则（见源码注释）：

| 情形 | 缓存行为 |
|------|---------|
| 事务内直接执行裸 SQL | 强制清空实体缓存、单对象缓存 |
| 事务内用实体对象操作 | 不主动清空，等事务结束 |
| 事务提交成功 | 按正常写入后逻辑更新缓存 |
| 事务回滚 | 一律强制清空所有缓存（无法跟踪中间状态） |

## 4. 嵌套事务与分布式

- **同一连接嵌套**：内层 `BeginTransaction` 会增加计数，最外层 `Commit` 才真正提交。
- **跨库事务**：XCode 不提供分布式事务封装，需自行协调多个 `EntityTransaction` 并处理补偿。

> 如果需要"跨库最终一致"，建议应用层采用消息队列 + 本地事务 + 幂等消费的模式，而不是分布式两阶段提交。

## 5. 事务内的读写分离

事务进行时，`ReadWriteStrategy.Validate()` 检测到 `Session.Transaction != null`，自动将**所有查询路由到主库**，避免主从延迟导致读到旧数据。

## 6. 链路追踪

`EntityTransaction` 会在开始时通过 `DAL.GlobalTracer?.NewSpan()` 创建追踪 Span，提交或回滚时关闭，方便在分布式追踪平台（如 SkyWalking）中观察事务范围。

## 7. 常见错误

- **忘记 Commit 导致长事务**：确保在 `try-catch-finally` 或 `using` 内正确调用 `Commit`。
- **Commit 后再操作**：Commit 后不要继续使用同一 `EntityTransaction`，应创建新事务。
- **跨连接事务**：`EntityTransaction<TEntity>` 只覆盖 `TEntity` 所在的连接，跨连接必须用多个 `EntityTransaction`。
- **只读副本不参与事务**：事务内所有操作强制走主库，读操作亦然。
