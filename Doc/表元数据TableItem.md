# 表元数据 TableItem

`TableItem` 是 XCode 运行时最关键的表级元数据对象。它把实体类型反射成结构化表模型，并提供高频查找与字段索引能力。

## 1）创建与缓存

使用 `TableItem.Create(type)` 获取实例，内部有并发缓存：

- 缓存键：`type.FullName`
- 不允许无 `[BindTable]` 类型创建
- 首次创建时构建 `DataTable/Fields/Indexes`

这保证了元数据初始化成本只付一次。

## 2）核心属性

- `EntityType`：实体类型
- `TableName`：运行时表名（可修改，自动同步 `DataTable.TableName`）
- `ConnName`：运行时连接名（可修改，自动同步 `DataTable.ConnName`）
- `Fields`：数据字段（DataObject 字段）
- `AllFields`：全部字段（含扩展属性）
- `Identity`：自增列
- `PrimaryKeys`：主键集合
- `Master`：主字段（业务识别字段）
- `DataTable`：DAL 层表结构对象

## 3）字段集合特性

- `FieldNames`：大小写不敏感哈希集合，若仅大小写不同会写日志告警
- `ExtendFieldNames`：扩展属性名集合（非数据列）

这两组集合可用于：
- 动态字段白名单校验
- API 层过滤非法排序/筛选字段

## 4）索引构建与去重

`InitFields()` 会从 `[BindIndex]` 构建索引，并做两类保护：

1. 若索引列全是主键，跳过重复索引
2. 按“最左前缀”检查潜在重复索引，输出提示日志

这有助于在开发阶段发现索引冗余。

## 5）FindByName 查找策略

`FindByName(name)` 内部是字典 + 回退扫描：

查找顺序大致为：
1. 字典命中（Name）
2. `Fields.Name`
3. `Fields.ColumnName`
4. `AllFields.Name`
5. `AllFields.FormatedName`

并且“未命中结果也缓存”，减少重复查找开销。

## 6）运行时改表名/连接名

可在应用启动后按租户/环境改写：

```csharp
var table = User.Meta.Table;
table.ConnName = "TenantA";
table.TableName = "User_202603";
```

`TableItem` 会同步 `DataTable`，后续 SQL 生成自动生效。

> 建议：这类改写集中在启动阶段或明确作用域内执行，避免并发请求互相污染。

## 7）模型检查模式

`ModelCheckMode` 默认 `CheckAllTablesWhenInit`，可通过特性覆盖，用于控制启动时模型校验行为。

## 8）常见坑

- 实体缺少 `[BindTable]`：无法创建 `TableItem`
- 动态拼字段名未走 `FindByName`：容易因大小写或列名映射失败
- 扩展属性误当数据列：会影响插入/更新 SQL

## 9）最佳实践

- 永远优先使用 `Meta.Table.FindByName()`，不要手写字段字符串拼 SQL。
- 大型系统中，把 `TableName/ConnName` 改写策略与 `ShardPolicy` 统一设计。
- 对外开放动态查询接口时，基于 `FieldNames` 做字段白名单。