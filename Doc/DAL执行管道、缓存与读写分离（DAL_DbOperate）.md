# DAL执行管道、缓存与读写分离（DAL_DbOperate）

`DAL_DbOperate` 是 `DAL` 的执行核心，串起了：
- 查询/执行 API
- 读写分离
- 数据层缓存
- 追踪埋点
- 统计计数

---

## 1. API 分层

主要分三层：

1. **高层语义 API**：`Query/Select/Execute/ExecuteScalar`
2. **包装层**：`QueryWrap / ExecuteWrap / QueryAsyncWrap / ExecuteAsyncWrap`
3. **会话层**：`IDbSession/IAsyncDbSession` 真正执行 SQL

包装层是最关键的横切逻辑入口。

---

## 2. 读写分离策略

查询包装里会先尝试：

```csharp
Strategy.TryGet(this, sql, action, out rd)
```

命中后查询转发到只读 DAL；写操作始终走主库（且 `Db.Readonly` 会阻止写入）。

可通过：
- `SuspendReadOnly(delay)` 临时停用从库
- `ResumeReadOnly()` 恢复从库

用于从库异常时快速降级。

---

## 3. 数据层缓存

`GetCache()` 会按配置创建 `MemoryCache`：
- 优先 `DAL.Expire`
- 其次 `Db.DataCache`
- 再次 `XCodeSetting.Current.DataCacheExpire`

查询会：
1. 根据 action+参数构造 key
2. 命中则直接返回
3. 未命中执行 SQL 并写回缓存

执行（写）会：
- 成功后 `GetCache()?.Clear()` 清缓存

---

## 4. 埋点与追踪

每次执行都走 `Invoke/InvokeAsync`：
- 构造 traceName：`db:{ConnName}:{Action}:{Tables}`
- 自动解析 SQL 表名（正则）
- 记录返回值/行数到 `span.Value`
- 记录结果摘要到 `span.Tag`

`SetSpanTag(value)` 可附加上下文标签，便于把 SQL 与业务请求关联。

---

## 5. 查询/执行计数

使用线程上下文计数：
- `QueryTimes`
- `ExecuteTimes`

可在请求结束时读取这两个计数，用于“单请求 SQL 成本”统计。

---

## 6. 分页与表名解析

- `PageSplit(SelectBuilder, start,max)` 统一分页 SQL
- `GetTables(sql, trimShard)` 可从 SQL 提取表名
- `TrimTableName` 可去掉末尾分片数字（如 `Log_202603` -> `Log`）

这对埋点聚合（按逻辑表名）非常实用。

---

## 7. 实战建议

1. 高并发读多写少场景启用数据层缓存 + 读写分离。
2. 写入频繁场景把 `Expire` 控制在较小值，减少清缓存抖动。
3. 对关键接口记录 `QueryTimes/ExecuteTimes`，建立 SQL 成本基线。
4. 从库不稳定时优先 `SuspendReadOnly(秒)` 自动恢复，避免手工忘记恢复。
