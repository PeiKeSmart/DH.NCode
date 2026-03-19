# Shards 跨分片查询与 AutoShard

当查询跨越多个分片时，XCode 提供 `Meta.AutoShard` 与 `CreateSplit/CreateShard` 两组能力，实现“分片枚举 + 分片内执行 + 结果汇总”。

## 关键 API

- `Meta.CreateSplit(connName, tableName)`：临时切换当前实体的连接与表（`using` 结束自动恢复）
- `Meta.CreateShard(value)`：按策略自动算出目标分片并切换
- `Meta.AutoShard(start, end, Func<T>)`：枚举时间区间命中的分片并执行回调
- `Meta.AutoShard(start, end, Func<IEntitySession,T>)`：按分片会话执行回调

`Meta.InShard` 可用于判断当前是否处于分片上下文。

## 1）手动分片执行

```csharp
using var _ = Order.Meta.CreateSplit("Order_2026", "Order_202603");
var list = Order.FindAll(Order._.Status == 1, Order._.Id.Desc(), null, 0, 100);
```

适合：
- 已知目标分片
- 运维排障、精准数据修复

## 2）按对象自动路由

```csharp
using var _ = Order.Meta.CreateShard(order); // 或传 DateTime / 雪花Id
order.Save();
```

适合：
- 单对象写入
- 路由逻辑统一走策略

## 3）按时间区间跨分片遍历

```csharp
var total = Order.Meta.AutoShard(start, end, () =>
{
    return Order.FindCount(Order._.Status == 1);
}).Sum();
```

框架行为：

1. 由 `ShardPolicy.Shards(start, end)` 计算分片集合
2. 自动跳过不存在的目标表
3. 每个分片切换上下文执行回调
4. 回调结果由调用方聚合

## 4）会话级跨分片执行

```csharp
var rows = Order.Meta.AutoShard(start, end, session =>
{
    return session.QueryCount();
}).Sum();
```

适合：
- 需要直接访问 `IEntitySession`
- 想在回调里执行更底层 DAL 操作

## 5）自动分片查询触发条件

常规 `FindAll/FindCount` 也会尝试自动分片，但有前置条件：

- 未处于分片上下文（`Meta.InShard == false`）
- 存在 `Meta.ShardPolicy`
- 查询表达式能提取分片范围（见 `TimeShardPolicy.Shards(Expression)`）

如果表达式无法识别分片键，建议改用 `AutoShard` 明确给出时间区间。

## 6）典型模式：跨分片报表

```csharp
var result = new List<OrderStat>();
foreach (var rs in Order.Meta.AutoShard(start, end, () =>
         Order.Search(start, end, null, new PageParameter { RetrieveTotalCount = false })))
{
    result.AddRange(rs);
}
```

建议：
- 每片内分页，避免单片结果过大
- 跨片聚合放到业务层做二次归并

## 7）典型模式：按分片清理旧数据

生成器在 `timeShard` 场景可生成 `DropWith(start,end)`，内部即基于 `AutoShard` 逐表执行 `Drop Table`。

这类场景务必：
- 先在测试环境验证分片命中范围
- 加白名单/最小日期保护，避免误删当前分片

## 8）异常与边界

- 目标分片表不存在：框架会跳过，不报错中断。
- 分片策略为空：`AutoShard` 无结果。
- 时间参数无效（如默认值）：策略可能抛异常。

## 9）性能建议

- 区间跨度尽量收敛（先按月、再按天）
- 回调中避免 N+1 查询
- 跨片并发要结合数据库连接池评估（默认串行更稳）
- 大报表优先离线任务，减少在线接口抖动

## 10）与 Shards 策略文档配套

- 策略设计：见 `Shards分配策略与路由.md`
- 代码生成联动：见 `代码生成器与XCodeTool.md`（`DataScale=timeShard:*`）