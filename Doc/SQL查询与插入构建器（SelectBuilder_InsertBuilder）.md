# SQL 查询与插入构建器（SelectBuilder / InsertBuilder）

XCode 在 `DataAccessLayer` 层提供两个低层 SQL 构建器：

- **`SelectBuilder`**：SELECT 语句的结构化表示与联合操作
- **`InsertBuilder`**：INSERT / Upsert 语句生成

它们是 DAL 分页、批量写入、跨库迁移等功能的真正底座。

---

## 1. SelectBuilder

### 1.1 结构字段

| 属性 | 说明 |
|------|------|
| `Column` | 选择列，默认 `*` |
| `Table` | 数据表（可含别名、子查询） |
| `Where` | 条件（自动过滤 `1=1`） |
| `GroupBy` | 分组 |
| `Having` | 分组过滤 |
| `OrderBy` | 排序（自动提取为 `Key`） |
| `Limit` | 分页 Limit 子句（SQLite 等） |
| `Key` | 分页主键（通常由 OrderBy 自动解析） |
| `Parameters` | 参数化查询的参数集合 |

### 1.2 SQL 解析能力

`SelectBuilder(string sql)` 或 `Parse(sql)` 能把原始 SQL 解析回各个结构字段，支持复杂嵌套圆括号（平衡组正则）。

### 1.3 常用操作

```csharp
// 克隆后追加条件
var sb = originalBuilder.Clone().AppendWhereAnd($"UserId > 0");

// 生成计数语句（含 GroupBy 时自动变子查询）
var countSb = sb.SelectCount();

// 分页：由 IDatabase.PageSplit 接收 builder 生成方言 SQL
var pageSql = db.PageSplit(sb, 0, 20);
```

### 1.4 `SelectCount` 细节

含 `GroupBy` 时不能直接 `Count(*)`，`SelectCount()` 会把原查询作为子查询包一层，再外层加 `Count(*)`，保证分页计数正确。

### 1.5 在分页场景中的地位

| 层次 | 位置 |
|------|------|
| 实体 `FindAll()` | 上层入口 |
| `DAL.FindAll(builder, ...)` | 中层汇聚 |
| `IDatabase.PageSplit(builder, ...)` | 各数据库方言适配 |
| `SelectBuilder` | 结构承载对象 |

---

## 2. InsertBuilder

### 2.1 核心功能

接收 `IDataTable`、列描述与一行 `IModel`，输出 INSERT 语句（含参数化支持）。

### 2.2 支持的 `SaveModes`

| 模式 | 输出 SQL |
|------|---------|
| `Insert` | `Insert Into ...` |
| `InsertIgnore` | `Insert Ignore Into ...` |
| `Replace` | `Replace Into ...` |
| `Upsert` | `Upsert Into ...` |

### 2.3 自增列处理

默认跳过 `Identity=true` 的字段；
设置 `AllowInsertIdentity=true` 时允许显式插入自增值（如数据迁移场景）。

### 2.4 参数化支持

`UseParameter=true` 时为每列创建 `IDataParameter`，防止 SQL 注入，同时改善执行计划缓存。

---

## 3. 相互关系

- `SelectBuilder` 负责**读**路径的 SQL 结构表达
- `InsertBuilder` 负责**写**路径的 SQL 组装
- 两者都被 `DAL` 驱动层调用，业务代码一般不直接使用

---

## 4. 实战建议

1. 自定义 ETL 抽取时，可直接操作 `SelectBuilder` 追加条件：

   ```csharp
   var sb = new SelectBuilder { Table = "Order" };
   sb = sb.AppendWhereAnd("Status = 1");
   ```

2. 数据迁移需要插入含自增主键的记录时，用 `InsertBuilder` 并开启 `AllowInsertIdentity`。

3. 参数化查询建议在对外 API 层统一开启 `UseParameter`。
