# 读写分离策略（ReadWriteStrategy）

XCode 内置读写分离机制，通过 `ReadWriteStrategy` 控制哪些查询走只读副本、哪些操作强制走主库，而无需修改业务代码。

## 1. 工作原理

DAL 持有两个连接：

```
DAL.ConnStr   ← 主库（读+写）
DAL.ReadOnly  ← 只读副本（仅 SELECT）
```

每次执行查询时，`ReadWriteStrategy.Validate()` 判断当前操作能否走只读库：

- **可以走只读**：`SELECT` + 无事务 + 不在忽略时间段 + 不在忽略表列表
- **必须走主库**：增删改、事务内操作、忽略时间段内的查询、忽略表的查询

## 2. 快速配置

```csharp
var dal = DAL.Create("MyDb");
dal.ReadOnly = DAL.Create("MyDbReadonly");

// 可选：排除某些时间段（如数据库备份窗口）
dal.Strategy.AddIgnoreTimes("00:30-00:50,02:00-02:30");

// 可选：排除某些表（如实时性要求极高的表）
dal.Strategy.IgnoreTables.Add("OrderStatus");
```

或通过连接字符串中约定的 `Readonly` 附加名：

```json
{
  "ConnectionStrings": {
    "MyDb": "Server=master;Database=App;...",
    "MyDbReadonly": "Server=slave;Database=App;..."
  }
}
```

两个连接名存在 `主名` + `主名Readonly` 的关系，XCode 会自动关联。

## 3. ReadWriteStrategy 配置项

| 属性/方法 | 类型 | 说明 |
|----------|------|------|
| `IgnoreTimes` | `IList<TimeRegion>` | 不走只读的时间段集合 |
| `IgnoreTables` | `ICollection<String>` | 不走只读的表名集合（大小写不敏感） |
| `AddIgnoreTimes(string)` | 方法 | 解析 `"HH:mm-HH:mm"` 格式，批量追加忽略时间段 |

### 时间段格式

```
"00:30-00:50"          单段
"00:30-00:50,02:00-02:30"  多段，逗号分隔
```

时间段采用闭开区间 `[start, end)`。

## 4. Validate 判断逻辑

```csharp
public virtual Boolean Validate(DAL dal, String sql, String action)
{
    // 事务中强制走主库
    if (dal.Session.Transaction != null) return false;

    // 仅 SELECT / SelectCount / Query 可走只读
    if (!action.EqualIgnoreCase("Select", "SelectCount", "Query")) return false;

    // ExecuteScalar 中的 SELECT 也可走只读
    if (action == "ExecuteScalar" && !sql.TrimStart().StartsWithIgnoreCase("select ")) return false;

    // 在忽略时间段内，走主库
    var span = DateTime.Now - DateTime.Today;
    foreach (var item in IgnoreTimes)
    {
        if (span >= item.Start && span < item.End) return false;
    }

    // 命中忽略表，走主库
    if (IgnoreTables.Count > 0)
    {
        var tables = DAL.GetTables(sql, false);
        foreach (var item in tables)
        {
            if (IgnoreTables.Contains(item)) return false;
        }
    }

    return true;
}
```

## 5. 自定义策略

继承 `ReadWriteStrategy` 可扩展任意判断逻辑：

```csharp
public class TenantReadWriteStrategy : ReadWriteStrategy
{
    // 多租户场景：VIP 租户查询始终走主库
    private readonly HashSet<Int32> _vipTenants = new();

    public TenantReadWriteStrategy(IEnumerable<Int32> vipTenants)
    {
        _vipTenants.UnionWith(vipTenants);
    }

    public override Boolean Validate(DAL dal, String sql, String action)
    {
        // 先调用基类逻辑
        if (!base.Validate(dal, sql, action)) return false;

        // VIP 租户走主库（假设租户 ID 存于 AsyncLocal 上下文）
        var tenantId = TenantContext.Current?.TenantId ?? 0;
        if (_vipTenants.Contains(tenantId)) return false;

        return true;
    }
}
```

注册：

```csharp
dal.Strategy = new TenantReadWriteStrategy([1001, 1002]);
```

## 6. 读写分离监控

DAL 提供了统计字段可用于监控：

```csharp
var dal = DAL.Create("MyDb");
Console.WriteLine($"查询次数: {dal.QueryTimes}");
Console.WriteLine($"执行次数: {dal.ExecuteTimes}");
```

配合 `ShowSQL=true` 日志可观察每条 SQL 是否路由到了只读库。

## 7. 典型场景建议

| 场景 | 建议 |
|------|------|
| 读多写少的列表/详情查询 | 默认走只读 |
| 下单/支付等关键写入 | 自动走主库 |
| 数据库备份窗口（主库压力高） | `AddIgnoreTimes("01:00-03:00")` |
| 高频更新的状态表 | `IgnoreTables.Add("OrderStatus")` |
| 事务内的从库读 | 自动强制走主库，无需配置 |

## 8. 常见问题

- **只读副本未生效**：检查 `DAL.ReadOnly` 是否已赋值，且连接字符串指向不同的服务器。
- **事务内读到旧数据**：不会发生 — 事务中自动走主库。
- **刚写入的数据查不到**：主从复制延迟（通常毫秒级），如果实时性要求高，把该表加入 `IgnoreTables`。
- **时间段设置无效**：注意时间格式需严格为 `HH:mm-HH:mm`，区间必须 `start < end`。
