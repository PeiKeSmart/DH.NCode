# 驱动自动加载与下载（DriverLoader）

`DriverLoader` 是 XCode 的驱动自举机制：当特定数据库驱动 DLL 不在本地时，自动从网络下载并加载，实现"零配置启动"。

---

## 1. 适用场景

XCode 核心包故意不内置所有第三方驱动（如 MySQL.Data、Npgsql），以避免版本锁定。
`DriverLoader` 允许用户**按需加载**：

- 首次运行某数据库时自动检测是否有驱动
- 没有则从配置的插件服务器下载
- 下载后加载到进程中，无需重启

---

## 2. 加载流程

```
Load(typeName, disname, dll, linkName, urls, minVersion)
   │
   ├─ 1. Type.GetType(typeName) → 已在进程中 → 返回
   │
   ├─ 2. 多路径搜索 DLL 文件
   │     (EntryAssemblyDir → CWD → BaseDir → PluginPath)
   │
   ├─ 3. 找到 → Assembly.LoadFrom → GetType → 返回
   │
   ├─ 4. DLL 未找到 + linkName 有值
   │     → 锁定 typeName (15秒超时，并发安全)
   │     → WebClientX.DownloadLinkAndExtract(urls, linkName, dir, true)
   │     → 解压到插件目录
   │
   └─ 5. 再次 Load → 返回 Type 或 null
```

---

## 3. 参数说明

| 参数 | 说明 |
|------|------|
| `typeName` | 完整类型名（如 `MySql.Data.MySqlClient.MySqlClientFactory, MySql.Data`） |
| `disname` | 友好显示名（日志用） |
| `dll` | 本地 DLL 文件名（如 `MySql.Data.dll`） |
| `linkName` | 下载页面上供搜索的链接关键词 |
| `urls` | 提供下载页面 URL；为空则用 `NewLife.Setting.PluginServer` |
| `minVersion` | 最低版本限制（`Version`），过滤旧驱动 |

---

## 4. 并发安全

- 使用 `Monitor.TryEnter(typeName, 15_000)` 锁住类型名字符串
- 同一驱动的并发加载请求最多等 15 秒
- 超时放弃，不抛出异常

---

## 5. 自定义下载客户端

```csharp
// 默认使用 WebClientX + NewLife AuthKey
// 可替换为自定义客户端（如代理场景）
DriverLoader.CreateClient = linkName => new WebClientX
{
    AuthKey = "MyKey",
    Proxy = myProxy,
};
```

---

## 6. 驱动搜索路径优先级

1. 入口程序集所在目录
2. 调用程序集所在目录
3. 执行程序集所在目录
4. 当前工作目录
5. AppDomain 基础目录
6. `NewLife.Setting.PluginPath`（可配置）

---

## 7. 在各数据库驱动中的使用示例

每个数据库驱动的 `CreateFactory()` 方法调用 `DriverLoader.Load`：

```csharp
// MySQL 驱动示例
protected override DbProviderFactory? CreateFactory()
{
    return DriverLoader.Load(
        "MySql.Data.MySqlClient.MySqlClientFactory, MySql.Data",
        "MySql",
        "MySql.Data.dll",
        "MySql.Data",
        null,
        new Version("8.0")
    ) as DbProviderFactory;
}
```

---

## 8. 关联阅读

- `/xcode/idatabase_dbbase`
- `/xcode/connection_string_builder`
