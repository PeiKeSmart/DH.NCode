# 实体工厂完整接口（IEntityFactory）

`IEntityFactory` 是 XCode 实体类泛型操作的核心接口，由 `EntityFactory<TEntity>` 实现，也是外部通过 `Entity<T>.Meta.Factory` 拿到的操作对象。

与 [实体工厂初始化与建模（EntityFactory）](/xcode/entity_factory) 介绍架构细节不同，本文聚焦在**接口契约层面**：每个方法/属性的语义、参数约定与典型用法。

---

## 1. 实体元数据属性

| 属性 | 说明 |
|------|------|
| `EntityType` | 实体类的 `Type` |
| `Session` | 实体对应的数据库会话（`IEntitySession`） |
| `Persistence` | 持久化策略，可替换 |
| `Accessor` | DataRow → 实体映射器，可替换 |
| `Default` | 默认实体实例，用于初始化/扩展操作 |
| `Table` | 表元数据（`TableItem`） |
| `AllFields` / `Fields` | 所有字段 / 映射到柱的字段 |
| `FieldNames` | 字段名集合（不区分大小写哈希表） |
| `Unique` | 唯一键（首个标识列或唯一主键） |
| `Master` | 主字段（代表当前行业务含义的字段） |
| `ConnName` / `TableName` | 当前线程正在使用的连接名/表名 |

---

## 2. 创建实体

```csharp
// forEdit=true 时会执行额外初始化（如填充默认时间、关联字段）
IEntity entity = factory.Create(forEdit: true);

// 从 DataSet 批量加载
IList<IEntity> list = factory.LoadData(ds);
```

---

## 3. 查询方法

### 3.1 按单条件查

```csharp
factory.Find("Name", "Alice")        // 按属性名+值
factory.Find(new Expression(...))    // 按表达式
factory.FindByKey(42)                // 按主键
factory.FindByKeyForEdit(42)         // 按主键，for edit（含额外初始化）
```

### 3.2 按批量/分页查

```csharp
factory.FindAll()                                    // 全表（慎用）
factory.FindAll(where, order, selects, 0, 20)        // 带分页
factory.FindAll(expression, order, selects, 0, 20)   // 表达式+分页
factory.FindAllWithCache()                           // 走实体缓存
```

### 3.3 计数

```csharp
factory.FindCount()                // 全表计数
factory.FindCount(where, ...)      // 带条件计数
factory.FindCount(expression)      // 表达式计数
```

---

## 4. 分表分库操作

```csharp
// 手动切换到指定分片并立即还原
using var sp = factory.CreateSplit("db_202501", "Order_20250101");

// 按对象/时间/雪花Id自动计算分片
using var sp2 = factory.CreateShard(entity);
using var sp3 = factory.CreateShard(DateTime.Today);

// 时间区间顺序跨多分片查询
var results = factory.AutoShard(start, end, () => factory.FindAll());
```

---

## 5. 高级写入

### 5.1 GetOrAdd

常用于统计更新场景，线程安全地按业务键拿到或创建一条记录：

```csharp
var stat = factory.GetOrAdd(
    key: userId,
    find: (k, cache) => factory.Find("UserId", k),
    create: k => { var e = factory.Create(); e["UserId"] = k; return e; }
);
```

### 5.2 Merge 合并导入

```csharp
// source: 新数据列表（实体或模型）
// targets: 已有数据（为空时全表 ≤ 10000 行自动拉取）
// fields: 要合并的字段，null 代表所有字段
int changed = factory.Merge(source, targets: null, fields: null, match: null);
```

场景：
1. **备份恢复**：按主键匹配，存在则更新，不存在则插入。
2. **异地导入**：清空主键值 → 按业务唯一键匹配。
3. **Excel 导入**：按业务主字段匹配，存在则合并更新。

---

## 6. 行为设置

| 属性 | 默认 | 说明 |
|------|------|------|
| `AutoIdentity` | true | 自增列插入后获取返回值 |
| `AllowInsertIdentity` | false | 允许显式插入自增值（仅本线程） |
| `AutoSetGuidField` | null | 插入时自动填充 Guid 的字段 |
| `AdditionalFields` | 空集 | 默认累加字段（免加锁更新） |
| `MasterTime` | - | 主时间字段，标记记录最后更新时间 |
| `Selects` | null | 默认选择列 |
| `FullInsert` | false | 是否插入所有字段（含未脏字段） |
| `Snow` | - | 雪花 Id 生成器，Int64 主键非自增时自动填充 |
| `OrderByKey` | true | 查询未指定排序时，添加主键排序 |
| `Interceptors` | - | 实体拦截器集合 |

---

## 7. 与 EntityFactory 的关系

| 对比点 | `IEntityFactory` | `EntityFactory<TEntity>` |
|--------|-----------------|--------------------------|
| 类型 | 接口 | 泛型实现类 |
| 用途 | 多态/通用操作 | 运行时强类型操作 |
| 获取方式 | `Entity<T>.Meta.Factory` | 同上，强转 |

---

## 8. 关联阅读

- `/xcode/entity_factory`
- `/xcode/entity_session`
- `/xcode/entity_persistence`
- `/xcode/entity_interceptor`
- `/xcode/shards_routing`
