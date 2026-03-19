# 字段缓存（FieldCache）

`FieldCache<TEntity>` 是统计型字段缓存，典型用于“分类下拉、标签云、TopN 聚合”场景。它在实体缓存基础上做 `group by + count` 聚合，并把结果写入运行时 `DataCache`。

## 1）定位

- 继承：`EntityCache<TEntity>`
- 输入：某个字段名（如 `Category`、`Type`）
- 输出：`IDictionary<String,String>`（键=原始值，值=格式化显示）

## 2）关键属性

- `MaxRows`：最多返回行数（默认 50）
- `Where`：预过滤条件
- `OrderBy`：默认 `group_count desc`
- `GetDisplay`：显示名转换委托
- `DisplayFormat`：默认 `"{0} ({1:n0})"`

## 3）初始化逻辑

`Init()` 会：

1. 解析目标字段 `_field`
2. 解析唯一键 `_Unique`（Identity 或单主键）
3. 根据表规模动态设置缓存过期
   - 小表（<100000）默认 600 秒
   - 大表用 `XCodeSetting.FieldCacheExpire`

## 4）查询逻辑

`Search()` 内部生成聚合表达式：

- `Where.GroupBy(_field)` 或 `_field.GroupBy()`
- 选择列：`_Unique.Count("group_count") & _field`
- 排序：`OrderBy`
- 限制：`MaxRows`

等价目标 SQL 语义：

- `SELECT Count(pk) as group_count, field FROM table ... GROUP BY field ORDER BY group_count desc LIMIT N`

## 5）结果缓存到 DataCache

`FindAllName()` 会把聚合结果写入：

- `DataCache.Current.FieldCache[$"{Entity}_{Field}"]`

并异步保存到 `DataCache.config`，用于加速冷启动。

## 6）典型用法

```csharp
var fc = new FieldCache<User>("RoleID")
{
    MaxRows = 20,
    GetDisplay = e => e["RoleName"] + "",
};

var dic = fc.FindAllName();
// 结果：{"1":"管理员 (120)", "2":"访客 (56)"}
```

## 7）适用场景

- 后台筛选下拉（按出现次数排序）
- 报表侧边分组统计
- 标签/类别 TopN 展示

## 8）实践建议

- 仅对基数适中的字段使用（几十到几千类）。
- 高基数字段务必加 `Where` 限制范围（如时间窗口）。
- `GetDisplay` 里尽量避免二次数据库查询。

## 9）常见问题

- **报缺少唯一主键**：实体表无 Identity 且非单主键。
- **统计慢**：字段基数过高且无过滤条件。
- **显示不友好**：配置 `GetDisplay` + `DisplayFormat`。