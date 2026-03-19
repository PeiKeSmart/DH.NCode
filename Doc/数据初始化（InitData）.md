# 数据初始化（InitData）

`InitData()` 是 XCode 实体类预置的**首次建表后自动填充默认数据**钩子，只在每个表**第一次初始化**时执行一次，适合写入管理员账号、系统字典、默认配置等种子数据。

---

## 1. 执行时机

```
应用启动
  ↓
第一次访问该实体
  ↓
EntitySession.WaitForInitData()
  ↓
CheckModel()          ← 反向工程：建表/改表
  ↓
entity.InitData()     ← 此处调用（仅执行一次）
```

- 由框架在 `EntitySession` 初始化时自动调用
- 线程安全：多线程首次访问时，框架使用 `Monitor` 保证只执行一次
- 若表是视图（`DataTable.IsView = true`），跳过

---

## 2. 基本用法

在业务类（`xxx.Biz.cs`）中重写 `InitData()`：

```csharp
public partial class Role : Entity<Role>
{
    protected override void InitData()
    {
        if (Meta.Count > 0) return;  // 已有数据则不重复写入

        var roles = new[]
        {
            new Role { Name = "管理员", Enable = true, IsSystem = true, Sort = 1 },
            new Role { Name = "普通用户", Enable = true, Sort = 2 },
        };
        roles.Insert();  // 批量插入
    }
}
```

---

## 3. 典型示例

### 3.1 系统字典

```csharp
protected override void InitData()
{
    if (Meta.Count > 0) return;

    var items = new List<Dictionary_>
    {
        new() { Name = "性别", Code = "Sex", Enable = true },
        new() { Name = "状态", Code = "Status", Enable = true },
    };
    items.ForEach(e => e.Insert());
}
```

### 3.2 管理员账号

```csharp
protected override void InitData()
{
    if (FindByName("admin") != null) return;

    var su = new User
    {
        Name     = "admin",
        Password = ManageProvider.Provider.PasswordProvider.Hash("admin"),
        Enable   = true,
        RoleID   = Role.GetOrAdd("管理员")?.ID ?? 1,
    };
    su.Insert();
}
```

### 3.3 嵌套依赖初始化

当 `InitData()` 中需要查询其他表时，被查询的表也会触发其自己的 `WaitForInitData()`。框架通过线程 ID 防止死锁（同一线程再次进入会直接返回）。

```csharp
protected override void InitData()
{
    var roleId = Role.FindByName("管理员")?.ID ?? 1;  // 触发 Role 的 WaitForInitData
    // ...
}
```

---

## 4. 注意事项

| 要点 | 说明 |
|------|------|
| 先检查数据是否存在 | 每次发布都可能触发 `CheckModel`，老库不会再调用 `InitData`，但本地开发时删表重建会重新触发；加 `if Count > 0 return` 更安全 |
| 避免事务包裹 | `InitData` 在框架锁内执行，内部不要再开启长事务 |
| 不放在 Entity 层 | 应放在 `Biz.cs`（业务层），才能使用完整业务逻辑（如密码哈希、关联关系） |
| 支持批量插入 | 可直接用 `list.Insert()` 批量写入提升性能 |
| 初始化失败只打日志 | 框架捕获异常仅写 `XTrace.WriteLine`，不会阻断应用启动 |

---

## 关联阅读

- `/xcode/entity_session`（实体会话与初始化流程）
- `/xcode/additional_fields`（增量累加字段，也在静态构造函数中注册）
- `/xcode/entity_factory`（EntityFactory 初始化流程）
