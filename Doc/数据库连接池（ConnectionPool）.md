# 数据库连接池（ConnectionPool）

`ConnectionPool` 是 XCode DAL 层的底层资源管理器，继承自 `ObjectPool<DbConnection>`，负责数据库连接的获取、归还与生命周期维护。

---

## 1. 默认参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `Min` | CPU数，[2, 8] | 最小空闲连接数 |
| `Max` | 1000 | 最大连接数 |
| `IdleTime` | 30s | 连接空闲超时 |
| `AllIdleTime` | 180s | 全部空闲后回收超时 |

---

## 2. 核心行为

### 2.1 创建连接（OnCreate）

- 用 `DbProviderFactory.CreateConnection()` 创建连接对象
- 设置 `ConnectionString` 后立即 `Open()`
- 失败时输出标准日志并上抛

### 2.2 借出（Get）

- 从对象池取连接
- 若状态已关闭（网络断开），自动尝试重新 `Open()`

### 2.3 归还（Put / OnPut）

- 检查连接状态：`Open` 才接受归还，否则丢弃
- 防止已断开的连接回流污染池

### 2.4 Execute 模式

```csharp
var result = pool.Execute(conn => conn.ExecuteScalar(sql));
```

借出 → 执行 → 自动归还，减少外部遗忘 `Put` 的风险。

---

## 3. 与 XCode DAL 的关系

- 每个 `DAL`（即每个连接名）对应一个 `IDatabase` 实例，`IDatabase` 实例内含一个 `ConnectionPool`
- 业务代码通过 `Entity.Find/Save` → `DAL` → `IDbSession` → `ConnectionPool.Get` 取连接
- 事务场景下 Session 持有连接直到事务提交/回滚，不经过池

---

## 4. 调优建议

1. **`Min`**：高并发服务可适当调高，降低冷启动延迟。
2. **`Max`**：根据数据库服务端最大连接数配置，留出运维余量。
3. **`IdleTime`**：夜间低谷时连接会主动回收，早高峰到来时会有短暂预热，可适当提高 Min 对冲。
4. 若使用 MySQL 等有 `wait_timeout` 的数据库，`IdleTime` 应小于服务端超时值。

---

## 5. 线上观测

- XCode 日志中 `ObjectPool: ...` 开头的行即为连接池日志
- 监控"借出失败率"（即等待超时）作为连接池压力核心指标
- 若大量 `Open错误` 日志出现，先检查连接串与网络，再排查池配置

---

## 6. 关联阅读

- `/xcode/db_info`
- `/xcode/connection_string_builder`
- `/xcode/dal_db_operate`
