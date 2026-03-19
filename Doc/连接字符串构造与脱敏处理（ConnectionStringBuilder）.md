# 连接字符串构造与脱敏处理（ConnectionStringBuilder）

`ConnectionStringBuilder` 是 XCode 的轻量连接串操作器，提供“解析-修改-重组”闭环能力，适合运行时动态处理连接字符串。

---

## 1. 主要能力

- 解析：把 `key=value;...` 拆为字典
- 读取/写入：`this[key]`
- 查询：`TryGetValue`
- 读取并删除：`TryGetAndRemove`
- 条件新增：`TryAdd`
- 删除：`Remove`
- 重组：`ConnectionString`

---

## 2. 快速示例

```csharp
var b = new ConnectionStringBuilder("Server=127.0.0.1;Database=Test;User=sa;Password=123456");

b["Database"] = "Prod";
b.TryAdd("Pooling", "true");

if (b.TryGetAndRemove("Password", out var pwd))
{
    // 可在这里做脱敏日志或密钥托管
}

var conn = b.ConnectionString;
```

---

## 3. 典型用法

### 3.1 连接串脱敏输出

```csharp
var b = new ConnectionStringBuilder(connStr);
b.Remove("Password");
b.Remove("Pwd");
XTrace.WriteLine("ConnStr={0}", b.ConnectionString);
```

### 3.2 动态注入参数

```csharp
var b = new ConnectionStringBuilder(connStr);
b.TryAdd("Application Name", "OrderService");
```

### 3.3 多环境切换

```csharp
var b = new ConnectionStringBuilder(connStr);
b["Server"] = useReadOnly ? "db-ro" : "db-master";
```

---

## 4. 注意事项

1. `this[key]` 在键不存在时会抛异常，先 `TryGetValue` 更稳妥。
2. 键名大小写由底层字典行为决定，建议统一规范（如 `Server/Database/User/Password`）。
3. 连接字符串重组顺序不保证与原串一致（字典枚举顺序），但语义等价。

---

## 5. 与 DAL 配合建议

- 在 `DAL.InitConnections` 前对连接串做统一清洗（去无效参数、加应用名）
- 使用 `ProtectedKey` 保护密码后再落地配置
- 线上日志严禁输出明文密码，始终先 `TryGetAndRemove("Password")`
