# 批量主键查找器（BatchFinder）

`BatchFinder<TKey, TEntity>` 解决"拿到一批 ID，依次获取实体"场景中的**N+1 查询**问题：先收集所有 ID，再按批（默认 500 条）一次性 `IN` 查询，查询结果缓存在内存中，后续按 ID 取用。

---

## 1. 典型场景

```csharp
// 反例：日志列表中，每条日志都单独查询 User
foreach (var log in logs)
{
    var user = User.FindById(log.UserId); // N 次查询 ❌
    log.UserName = user?.Name;
}

// 正例：先收集所有 UserId，再批量查
var finder = new BatchFinder<Int32, User>(logs.Select(e => e.UserId));
foreach (var log in logs)
{
    var user = finder.FindByKey(log.UserId); // 缓存命中 ✅
    log.UserName = user?.Name;
}
```

---

## 2. 工作原理

1. `Add(keys)` / 构造时传入：收集主键列表，自动去重，跳过零值和空字符串
2. `FindByKey(key)` 调用时：向前（顺序）扫描未查询的 key 批次，每次 `Take(BatchSize)` 条批量 IN 查询
3. 查询结果存入 `Cache`，后续同一 key 直接命中，不再访问数据库

**惰性加载**：只有真正调用 `FindByKey` 时才触发查询，不会提前查询整个列表。

---

## 3. 与 EntityCache / SingleCache 的差异

| | `BatchFinder` | `SingleCache` | `EntityCache` |
|---|---|---|---|
| 生命周期 | 方法/请求内，用完即弃 | 长期缓存 | 长期缓存 |
| 数据量 | 本次操作的子集 | 整表 | 整表 |
| 适合场景 | ETL/报表/批处理 | 高频单 ID 读 | 小表全量缓存 |

---

## 4. 配置项

| 属性 | 默认值 | 说明 |
|------|--------|------|
| `BatchSize` | 500 | 每次 IN 查询的最大条数 |
| `Callback` | null | 自定义查询函数，替代默认的 `FindAll(_.Id.In(ks))` |
| `Cache` | `ConcurrentDictionary` | 存储查询结果，可预先填充防止重复查询 |

---

## 5. 自定义查询

```csharp
var finder = new BatchFinder<Int32, User>
{
    BatchSize = 200,
    Callback = keys => User.FindAll(User._.Id.In(keys) & User._.IsActive == 1)
};
finder.Add(userIds);

var user = finder.FindByKey(42);
```

使用 `Callback` 可追加额外过滤条件（如仅查活跃用户），或从从库查询。

---

## 6. 预填充 Cache

```csharp
// 如果部分数据已在内存中，直接填充缓存，避免重复查询
var finder = new BatchFinder<Int32, User>(allIds);
foreach (var cached in cachedUsers)
{
    finder.Cache[cached.Id] = cached;
}
// FindByKey 只查询未命中 Cache 的 key
```

---

## 7. 关联阅读

- `/xcode/entity_factory_interface`（`Factory.FindAll` 批量查询接口）
- `/xcode/find`（`FindAll` 的完整签名）
- `/xcode/where_expression`（构建 IN 条件：`_.Id.In(keys)`）
