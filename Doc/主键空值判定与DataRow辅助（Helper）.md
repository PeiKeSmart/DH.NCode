# 主键空值判定与DataRow辅助（Helper）

`XCode.Common.Helper` 提供了两个高频但容易踩坑的基础能力：

1. 主键空值判定（`IsNullKey/IsEntityNullKey`）
2. `DataRowCollection` 转数组（`ToArray`）

---

## 1. IsNullKey：主键是否为空

```csharp
var empty = Helper.IsNullKey(key, keyType);
```

判定规则：
- 整数类型：`0` 视为空
- 字符串：`null/""` 视为空
- `Guid`：`Guid.Empty` 视为空
- `Byte[]`：长度 0 视为空

这与业务系统“主键必须有效”的常见约束一致。

---

## 2. IsEntityNullKey：实体是否空主键

```csharp
var isNew = Helper.IsEntityNullKey(entity);
```

会遍历实体工厂字段，遇到 `PrimaryKey` 或 `Identity` 字段时，用 `IsNullKey` 判定。

常用于：
- 决定 `Insert` 还是 `Update`
- 防止误把空主键实体当更新对象

---

## 3. DataRowCollection 扩展

```csharp
DataRow[] rows = table.Rows.ToArray();
```

相比手写循环更简洁，也避免在上层重复 `foreach + as DataRow` 模板代码。

---

## 4. 注意事项

1. `type` 与 `key` 不一致时会先 `ChangeType`，不合法输入可能抛转换异常。
2. 某些业务主键允许 `0`（极少见）时，不应直接复用默认规则，需自定义判定。
3. `IsEntityNullKey` 依赖实体元数据，非 XCode 实体不适用。

---

## 5. 推荐实践

- 在仓储层统一用 `IsEntityNullKey` 做新增/更新分流。
- 在导入任务中，先判空主键再决定是插入还是跳过。
- 保持主键类型设计清晰，减少“字符串主键+空格值”这类边界输入。
