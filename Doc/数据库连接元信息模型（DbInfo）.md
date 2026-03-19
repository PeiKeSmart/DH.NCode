# 数据库连接元信息模型（DbInfo）

`DbInfo` 是 DataAccessLayer 中的内部连接描述对象，用于承载一条数据库连接的元信息。

```csharp
internal class DbInfo
{
    public String? Name { get; set; }
    public String? ConnectionString { get; set; }
    public Type? Type { get; set; }
    public String? Provider { get; set; }
}
```

---

## 1. 字段语义

| 字段 | 说明 |
|------|------|
| `Name` | 连接名（如 `Membership`） |
| `ConnectionString` | 原始连接串 |
| `Type` | 提供者类型（数据库驱动类型） |
| `Provider` | 提供者名称（如 `SqlServer` / `MySql`） |

`Type` 与 `Provider` 允许“强类型 + 文本配置”双模式并存。

---

## 2. 典型用途

虽然 `DbInfo` 是内部类型，但在连接初始化与路由阶段通常用于：

1. 从配置中心解析连接配置
2. 缓存连接定义
3. 延迟创建 `DAL/IDatabase`
4. 支持多数据库驱动识别

---

## 3. 设计价值

- 把“连接名、连接串、驱动信息”集中封装，避免多个字典并行维护。
- 与 `ConnectionStringBuilder`、`DAL.InitConnections` 等流程自然衔接。
- 便于未来扩展更多元字段（如租户标识、只读标记、超时配置）。

---

## 4. 实战建议

1. 外部配置应优先维护 `Provider`，`Type` 由框架内部解析。
2. 日志输出 `DbInfo` 时必须脱敏 `ConnectionString` 密码字段。
3. 多环境切换建议保持 `Name` 稳定，只替换连接串内容。

---

## 5. 关联阅读

- `/xcode/connection_string_builder`
- `/xcode/dal_setting`
- `/xcode/dal_db_operate`
