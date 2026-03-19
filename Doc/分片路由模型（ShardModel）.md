# 分片路由模型（ShardModel）

`ShardModel` 是 XCode 分库分表路由的最小结果对象：

```csharp
public record ShardModel(String ConnName, String TableName);
```

含义非常直接：
- `ConnName`：目标连接名
- `TableName`：目标物理表名

---

## 1. 为什么要单独建模

把路由结果抽象成 record 有三个好处：

1. 统一 `IShardPolicy` 输出类型
2. 可在上层做去重、排序、批量路由
3. record 天生值语义，便于比较与调试

---

## 2. 在策略中的位置

`IShardPolicy` 的三个方法最终都围绕 `ShardModel`：

- `Shard(value)`：单值路由 -> 单个 `ShardModel`
- `Shards(start,end)`：范围路由 -> `ShardModel[]`
- `Shards(expression)`：条件路由 -> `ShardModel[]`

例如 `TimeShardPolicy` 就用 `ShardModel` 承载时间映射后的连接与表。

---

## 3. 使用示例

```csharp
var model = Meta.ShardPolicy.Shard(DateTime.Now);
if (model != null)
{
    var session = Meta.Factory.GetSession(model.ConnName, model.TableName);
    // 在指定分片上执行查询/写入
}
```

范围查询：

```csharp
var shards = Meta.ShardPolicy.Shards(start, end);
foreach (var sm in shards)
{
    var session = Meta.Factory.GetSession(sm.ConnName, sm.TableName);
    // 聚合每个分片结果
}
```

---

## 4. 调试与日志建议

建议打印 `ConnName#TableName` 作为路由键：

```csharp
var key = $"{sm.ConnName}#{sm.TableName}";
```

这与 `TimeShardPolicy` 内部去重键一致，便于排查重复分片扫描问题。

---

## 5. 注意事项

1. `ConnName/TableName` 任何一项为空都应视为无效路由。
2. 路由结果应尽量稳定（相同输入得到相同输出），否则缓存与幂等会失效。
3. 上层聚合查询时要对 `ShardModel[]` 去重，避免重复扫同表。
