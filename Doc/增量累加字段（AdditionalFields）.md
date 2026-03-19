# 增量累加字段（AdditionalFields）

`AdditionalFields` 是 XCode 面向并发场景的**无锁字段累加**机制，可将 UPDATE 从

```sql
-- 普通 Update（可能丢失）
UPDATE Visit SET Count=1024 WHERE Id=1
```

自动改写为

```sql
-- 原子差值累加（并发安全）
UPDATE Visit SET Count=Count+1 WHERE Id=1
```

---

## 1. 工作原理

```
加载实体 → SetField 记录 baseline[Count=1023]
           ↓
业务代码 entity.Count += 1  →  Count=1024
           ↓
Save()  计算 delta = 1024 - 1023 = 1
           ↓
生成 SET Count=Count+1  ← 原子操作，并发安全
```

关键路径：
- 实体从数据库加载时，框架自动调用 `EntityAddition.SetField()` 记录累加字段基准值
- 保存时按 `当前值 − 基准值` 计算差值，生成 `Count=Count+delta` SQL
- 若差值为 0，该字段从 SET 中省略

---

## 2. 注册累加字段

在实体类静态构造函数中声明：

```csharp
static UserStat()
{
    // 对 Total/Count 等字段启用原子累加
    var df = Meta.Factory.AdditionalFields;
    df.Add(nameof(Total));
    df.Add(nameof(Count));
}
```

注册后，所有通过 `Find*` 方法从数据库加载的实体，保存时均会自动使用 `COUNT=COUNT+n` 形式。

---

## 3. 典型用法

### 3.1 浏览量计数器

```csharp
// 查找或创建今日统计记录
var stat = PageStat.FindOrAdd(DateTime.Today);
stat.Views++;
stat.Save();
// → UPDATE PageStat SET Views=Views+1 WHERE date='2025-01-01'
```

### 3.2 库存扣减

```csharp
var goods = Goods.FindById(goodsId);
goods.Stock -= qty;            // 当前值 = 100 - 3 = 97
goods.Save();
// → UPDATE Goods SET Stock=Stock-3 WHERE Id=42
```

### 3.3 货币精度累加

```csharp
static Account()
{
    Meta.Factory.AdditionalFields.Add(nameof(Balance));
}

// 多线程同时充值，互不覆盖
account.Balance += 100.00m;
account.Save();
```

---

## 4. 注意事项

| 场景 | 说明 |
|------|------|
| 必须先加载 | `AdditionalFields` 仅对**从数据库加载**的实体有效；直接 `new` 创建的实体无基准值，仍用绝对赋值 |
| 仅限数值字段 | 适合 `Int32/Int64/Decimal/Double`；不适合字符串 |
| 批量操作兼容 | `EntityExtension.Update()`、`Upsert()` 批量方法会读取 `AdditionalFields` 并生成批量形式的差值更新 |
| 与事务配合 | 在事务内仍保持差值语义，不影响事务提交/回滚 |
| 静态构造函数位置 | **必须**放在所有 `Meta.*` 初始化之前，建议紧随 `#region 对象操作` |

---

## 5. 与 Upsert 结合

批量 Upsert 时也使用累加语义：

```csharp
// AddColumns 自动取自 AdditionalFields
list.Upsert(
    updateColumns: [nameof(UserStat.Total), nameof(UserStat.Count)],
    addColumns:    null   // null → 取 Meta.Factory.AdditionalFields
);
```

---

## 关联阅读

- `/xcode/entity_extension_batch`（批量操作API，包含 AddColumns 用法）
- `/xcode/entity_interceptor`（实体拦截器，其与累加流程的执行顺序）
