# 排序与分页参数构造（SqlBuilder）

`XCode.Common.SqlBuilder` 提供了分页查询里最容易出问题的一环：**排序子句安全构造**。

核心目标：
- 允许前端传入排序字段
- 但必须校验字段属于实体元数据
- 防止非法字段导致 SQL 注入或运行时错误

---

## 1. BuildOrder：严格字段校验

```csharp
var orderBy = SqlBuilder.BuildOrder(page, User.Meta.Factory);
```

`BuildOrder(PageParameter, IEntityFactory)` 会：

1. 优先使用 `page.OrderBy`（已明确指定时直接返回）；
2. 否则解析 `page.Sort`（支持多字段逗号分隔）；
3. 识别后缀 `Asc/Desc`，没有后缀时用 `page.Desc` 默认方向；
4. 逐个字段 `factory.Table.FindByName(name)` 校验；
5. 用数据库方言 `FormatName(field)` 输出最终安全字段名。

若字段不存在，直接抛异常：

```text
实体类[User]不包含排序字段[HackField]
```

---

## 2. GetOrderBy：轻量拼接

`GetOrderBy(this PageParameter)` 是轻量版本：
- 优先 `OrderBy`
- 其次 `Sort`
- 若 `Desc=true` 且 `Sort` 未显式方向，则补 `Desc`

它不做实体字段校验，适合你已在上游做过白名单过滤的场景。

---

## 3. 推荐用法

### 3.1 API 层安全排序

```csharp
var page = new PageParameter
{
    PageIndex = 1,
    PageSize = 20,
    Sort = "CreateTime Desc,Id"
};

page.OrderBy = SqlBuilder.BuildOrder(page, User.Meta.Factory);
var list = User.Search(null, page);
```

### 3.2 后台任务快速排序

```csharp
var order = page.GetOrderBy();
```

---

## 4. 多字段排序行为

输入：`Sort = "Name Desc,CreateTime"`，`Desc=false`

输出：
- `Name` → `Desc`
- `CreateTime` → `Asc`（默认）

如果输入字段重复，后出现的覆盖前面的方向（字典行为）。

---

## 5. 常见坑

1. **直接拼接前端字段名**：绕过 `BuildOrder` 容易注入风险。
2. **字段是属性名还是列名**：`FindByName` 兼容常见命名，但建议统一用实体字段名。
3. **`OrderBy` 与 `Sort` 同时设置**：`OrderBy` 优先，`Sort` 会被忽略。
4. **数据库关键字字段**：`FormatName` 会自动加方言引号（如 `[]`/`` ` ``/`""`）。

---

## 6. 实战建议

- 对外 API 一律走 `BuildOrder`
- 前端只允许传“可排序字段别名”，后端映射到实体字段名
- 默认排序加主键兜底（避免翻页抖动）

```csharp
if (page.Sort.IsNullOrEmpty()) page.Sort = "Id Desc";
page.OrderBy = SqlBuilder.BuildOrder(page, User.Meta.Factory);
```
