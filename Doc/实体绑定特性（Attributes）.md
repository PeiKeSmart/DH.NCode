# 实体绑定特性（Attributes）

XCode 通过四个 `Attribute` 将实体类与数据库表结构关联：`BindTable`、`BindColumn`、`BindIndex`、`Map`。代码生成工具（xcode）从 Model.xml 自动生成这些标注，但理解其含义有助于排查问题与手写高级场景。

## 1. BindTableAttribute

标注在实体类上，声明该类对应的数据表。

```csharp
[BindTable("User", Description = "用户。系统管理用户", ConnName = "Sys", DbType = DatabaseType.None)]
public partial class User : Entity<User> { }
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | `String` | 数据库表名 |
| `Description` | `String?` | 表描述 |
| `ConnName` | `String?` | 连接名（运行时实际连接由 `Meta.ConnName` 控制） |
| `DbType` | `DatabaseType` | 记录生成时的数据库类型，反向工程时优先沿用对应 `RawType` |
| `IsView` | `Boolean` | 是否为视图（视图不参与反向工程建表） |

**关键说明**：
- 多数场景 `DbType` 设 `None`，代表跨库通用。
- 特定数据库生成的实体若 `DbType` 与运行时数据库相同，反向工程会使用字段的原始类型（`RawType`），以保持最佳兼容。

## 2. BindColumnAttribute

标注在实体属性上，声明该属性对应的数据列。

```csharp
[BindColumn("UserName", "用户名。登录账号", "", Master = true)]
public String? UserName { get; set; }

[BindColumn("LastLoginIP", "最后登录IP", "varchar(50)", ShowIn = "-EditForm,-AddForm")]
public String? LastLoginIP { get; set; }
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | `String?` | 列名（为空则同属性名） |
| `Description` | `String?` | 字段描述 |
| `RawType` | `String?` | 原始数据库类型，仅当 BindTable.DbType 与运行时匹配时使用 |
| `Master` | `Boolean` | 是否主字段（代表该行业务主要含义） |
| `DataScale` | `String?` | 数据规模标记（`"time"` / `"timeShard:yyMMdd"`） |
| `ItemType` | `String?` | 元素类型（`image`/`file`/`url`/`GMK`/`html`/`json`...），控制前端渲染 |
| `ShowIn` | `String?` | 显示选项（`"List,Search"` / `"-EditForm"` / `"11100"`） |
| `DefaultValue` | `String?` | 字段默认值 |
| `Precision` | `Int32` | 数值精度 |
| `Scale` | `Int32` | 小数位数 |

**DataScale 用法**：
- `"time"` — 大数据单表时间字段，配合雪花 ID（主键 `Int64`）。
- `"timeShard:yyMMdd"` — 分表字段，格式决定分表名后缀。

## 3. BindIndexAttribute

标注在实体类上（可多次），声明数据表索引。

```csharp
[BindIndex("IU_User_Name", true, "Name")]
[BindIndex("IX_User_RoleId", false, "RoleId")]
public partial class User : Entity<User> { }
```

| 属性 | 类型 | 说明 |
|------|------|------|
| `Name` | `String` | 索引名称（DDL 中使用） |
| `Unique` | `Boolean` | 是否唯一索引 |
| `Columns` | `String` | 列名，逗号分隔（如 `"Page,Level,Time"`） |

**命名惯例**（xcode 生成）：
- 唯一索引：`IU_{表名}_{列名列表}`
- 普通索引：`IX_{表名}_{列名列表}`

反向工程启动时，会对比实体上的 `BindIndex` 与数据库实际索引，自动补全缺失的索引。

## 4. MapAttribute

标注在外键属性上（如 `RoleId`），声明关联实体的映射关系，用于自动提供下拉数据源与扩展属性。

```csharp
[Map(nameof(RoleId), typeof(Role), "Id")]
public Int32 RoleId { get; set; }
```

常用重载：

```csharp
// 关联另一实体类型，默认以主键关联
[Map("RoleId", typeof(Role))]

// 显式指定关联键
[Map("DepartmentId", typeof(Department), "Id")]

// 自定义提供者（继承 MapProvider）
[Map("TenantId", typeof(MyTenantProvider))]
```

`MapProvider` 默认返回指定实体工厂的 `Key→Master` 字典，可重写 `GetDataSource()` 实现自定义映射数据。

## 5. 与 Model.xml 的对应关系

| Model.xml 属性 | 生成哪个 Attribute |
|--------------|-----------------|
| `Table.Name` / `ConnName` / `IsView` | `[BindTable]` |
| `Column.Name` / `Description` / `RawType` / `Master` / `DataScale` / `ShowIn` | `[BindColumn]` |
| `Index.Columns` / `Unique` | `[BindIndex]` |
| `Column.Map` | `[Map]` |

## 6. 常见使用场景

### 运行时切换表名

```csharp
// 临时切换当前线程的表名（如多租户分表）
using var split = EntitySplit.CreateSplit<Order>("Order_2025");
// 代码块内所有 Order 操作实际访问 Order_2025
```

### 查看实体绑定信息

```csharp
var table = Order.Meta.Table.DataTable;
Console.WriteLine(table.Name);           // 表名
foreach (var col in table.Columns)
{
    Console.WriteLine($"{col.Name}: {col.DataType}, {col.Description}");
}
```

## 7. 注意事项

- `BindTableAttribute` 的 `ConnName` 是代码生成时的默认值，运行时可通过 `Meta.ConnName` 或配置文件覆盖。
- `BindColumnAttribute` 不指定 `Name` 时，列名与属性名相同。
- 修改 Model.xml 重新运行 `xcode` 会覆盖 `*.cs` 中的特性，切勿在自动生成文件里手工改特性。
- 手写实体时若不使用 `BindColumn`，XCode 会按属性名（区分大小写）自动匹配列名。
