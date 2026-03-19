# 跨数据库 SQL 模板（SqlTemplate）

`SqlTemplate` 允许为同一逻辑操作提供**多个数据库方言**的 SQL 实现，运行时自动选择当前数据库对应的版本。

---

## 1. 使用场景

当某个 SQL 语句在不同数据库下语法差异较大（如窗口函数、日期函数、字符串函数），且无法用 XCode 内置的 `FormatValue / FormatName` 抹平时，用 `SqlTemplate` 管理多套 SQL：

- 特定的分析或聚合查询
- 跨数据库的存储过程/函数调用
- 统计报表中需要数据库原生语法的复杂 SQL

---

## 2. 模板格式

```
-- 默认 SQL（适用于所有数据库）
SELECT * FROM Orders WHERE ...

-- [MySql]
SELECT * FROM Orders LIMIT 10

-- [SqlServer]
SELECT TOP 10 * FROM Orders

-- [PostgreSQL]
SELECT * FROM Orders LIMIT 10
```

- 以 `-- [DbType]` 行作为分隔符，`[...]` 内写 `DatabaseType` 枚举名
- 第一个 `-- [...]` 之前的内容为默认 SQL
- 每个命名块直到下一个 `-- [...]` 为止

---

## 3. 读取与使用

### 3.1 直接解析字符串

```csharp
var tpl = new SqlTemplate();
tpl.Parse("""
    SELECT * FROM Orders WHERE Status = 1
    -- [MySql]
    SELECT * FROM Orders WHERE Status = 1 LIMIT 1000
    -- [SqlServer]
    SELECT TOP 1000 * FROM Orders WHERE Status = 1
    """);

// 获取当前数据库对应的 SQL
var dal = DAL.Create("ConnName");
var sql = tpl.GetSql(dal.DbType);
```

### 3.2 从程序集嵌入资源加载（推荐）

适合 SQL 较长时，把 `.sql` 文件设为嵌入资源：

```csharp
var tpl = new SqlTemplate();
tpl.ParseEmbedded(Assembly.GetExecutingAssembly(), "MyApp.Sql", "ComplexReport.sql");
var sql = tpl.GetSql(dal.DbType);
```

### 3.3 绑定到实体工厂

`IEntityFactory.Template` 属性持有一个 `SqlTemplate`，代码生成器会自动填充：

```csharp
var tpl = Entity<MyEntity>.Meta.Factory.Template;
if (tpl != null)
{
    var sql = tpl.GetSql(dal.DbType);
    dal.Execute(sql);
}
```

---

## 4. `GetSql` 回退规则

```
GetSql(DatabaseType.MySql)
  └─ 检查 Sqls["MySql"] → 有 → 返回
  └─ 无匹配 → 返回默认 Sql
```

未注册某数据库的 SQL 时，自动回退到默认版本，不会报错。

---

## 5. 使用建议

1. 默认 SQL 应覆盖最通用场景（如 PostgreSQL / SQLite 通用语法）
2. 优先用 XCode DAL 层提供的格式化方法（`FormatName` / `PageSplit`）解决方言问题，仅在无法自动适配时才引入 `SqlTemplate`
3. 命名资源文件以 `.tpl.sql` 或 `.cross.sql` 后缀区分，便于识别

---

## 6. 关联阅读

- `/xcode/idatabase_dbbase`
- `/xcode/dal_sql_builders`
- `/xcode/ms_page_split`
