# 实体持久化接口与默认实现（IEntityPersistence）

`IEntityPersistence` 定义了 XCode 的实体落库协议，`EntityPersistence` 是默认实现。它负责将“实体对象状态”转换成可执行 SQL，并处理参数化、主键条件、批量删除、脏字段更新等细节。

---

## 1. 接口能力总览

`IEntityPersistence` 包含四类能力：

1. **对象级 CRUD**：`Insert/Update/Delete`（同步+异步）
2. **集合/条件级更新删除**：`Update(set,where)`、`Delete(where,maxRows)`
3. **SQL 生成**：`GetSql(...)`、`InsertSQL(...)`
4. **主键条件构造**：`GetPrimaryCondition(entity)`

这使得你可以替换默认持久化逻辑，例如：
- 审计字段自动注入
- 自定义软删除 SQL
- 特定数据库语法增强

---

## 2. 默认实现核心行为

## 2.1 Insert

- 自动处理 Guid 字段（`AutoSetGuidField`）
- 生成 Insert SQL（必要时参数化）
- 对自增主键：
  - 默认 `InsertAndGetIdentity`
  - 将返回 Id 回写到实体
- 最后清空 `Dirtys`

SqlServer 且允许插入自增列时，会包裹：

```sql
SET IDENTITY_INSERT [Table] ON;
...insert...
SET IDENTITY_INSERT [Table] OFF;
```

## 2.2 Update

- 若 `HasDirty=false` 直接返回 0
- 双重检查并加锁防止并发重复提交
- 仅更新脏字段
- 支持“累加字段”生成 `Col=Col+v` / `Col=Col-v`
- 成功后清空 `Dirtys`

## 2.3 Delete

- 通过 `GetPrimaryCondition` 生成 where 条件
- 执行后清空 `Dirtys`

---

## 3. SQL 生成策略

内部统一入口：

```csharp
SQL(session, entity, methodType, ref parameters)
```

根据 `methodType` 分派：
- `InsertSQL(...)`
- `UpdateSQL(...)`
- `DeleteSQL(...)`

### 3.1 参数化规则（UseParam）

- 全局 `db.UseParameter=true` 时全参数化
- 大字段（>4000）优先参数化
- 小字段可直接内联，减少参数对象开销

这是“安全性 + 性能”的折中策略。

---

## 4. 主键条件构造

`GetPrimaryCondition(entity)` 规则：

1. 有标识列（Identity）→ 用标识列
2. 否则用主键集合
3. 若无主键 → 回退为全部字段（极端情况）

该条件用于 Update/Delete 的 where 生成。

---

## 5. 分批删除能力

`Delete(session, whereClause, maximumRows)` 支持：
- 自动获取批大小（默认上限 10_000）
- 循环调用数据库 `BuildDeleteSql(..., size)` 分批执行
- 每批可按配置 `BatchInterval` 休眠，减小数据库冲击
- 不支持分批的数据库则回退为整句删除

适合清理历史日志、行为记录等大表数据。

---

## 6. 自定义持久化实现示例

```csharp
public class SoftDeletePersistence : EntityPersistence
{
    public SoftDeletePersistence(IEntityFactory factory) : base(factory) { }

    public override Int32 Delete(IEntitySession session, IEntity entity)
    {
        // 软删除：改状态字段，不做物理删除
        return Update(session,
            new[] { "IsDeleted", "UpdateTime" },
            new Object[] { true, DateTime.Now },
            new[] { "Id" },
            new Object[] { entity["Id"]! });
    }
}
```

然后在自定义工厂中替换 `Persistence`。

---

## 7. 注意事项

1. `Update` 依赖脏字段，业务代码不要随意 `Dirtys.Clear()`。
2. 条件更新接口会校验非法表达式，避免误传 `or` 导致大面积更新。
3. 异步方法内部使用 `ConfigureAwait(false)`，适合库层。
4. 如果你重写 SQL 生成，务必保留参数化能力，避免注入风险。
