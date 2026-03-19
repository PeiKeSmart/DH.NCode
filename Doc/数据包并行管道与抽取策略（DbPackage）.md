# 数据包并行管道与抽取策略（DbPackage）

`DbPackage` 是 XCode 数据迁移内核，负责：
- 备份（Backup）
- 恢复（Restore）
- 跨库同步（Sync）

相比 `DAL_Backup` 的门面 API，本篇聚焦其内部并行机制与可扩展点。

---

## 1. 总体架构

`DbPackage` 采用“抽取 + Actor 消费”的流水线：

1. 抽取器 `IExtracter<DbTable>` 分页读取源库
2. `WriteFileActor` 或 `WriteDbActor` 异步消费
3. `OnPage` 事件上报进度
4. `Tracer` + `Log` 记录吞吐与错误

这是一种典型生产者-消费者模型，能把 IO 与 DB 写入并行化。

---

## 2. 核心可配置项

| 属性 | 默认 | 作用 |
|------|------|------|
| `BatchSize` | 0 | 批大小，0 时走 DAL 默认 |
| `IgnoreError` | true | 批量处理时忽略单表错误 |
| `IgnorePageError` | false | 批量写入时忽略单页错误 |
| `BatchInsert` | true | 写库时是否启用批量插入 |
| `Mode` | `Insert` | 单行写库保存模式 |
| `CreateExtracterCallback` | 内置 | 自定义抽取器 |
| `WriteFileCallback` | 内置 | 自定义写文件 Actor |
| `WriteDbCallback` | 内置 | 自定义写库 Actor |

---

## 3. 抽取策略选择（GetExtracter）

内置优先级：

1. **自增/数字主键** -> `IdExtracter`
2. **时间索引** -> `TimeExtracter`
3. **主键分页** -> `PagingExtracter(pk)`
4. **索引分页** -> `PagingExtracter(index)`
5. **兜底首列分页**

这能在不同表结构下尽量选择“稳定且高效”的分页方式。

---

## 4. 备份流程细节

`Backup(table, stream)`：
- 创建 `WriteFileActor`（默认 `BoundedCapacity=4`）
- 先写头部（总行数、列、类型）
- 分页抽取数据并推送 Actor
- 字段名自动从数据库列名映射到实体属性名
- 关闭 SQL 日志后高速执行，结束再恢复

输出格式支持：
- 原生 `.table`
- `.gz` 压缩
- `BackupAll` 使用 `.zip` + 可选 `.xml` 架构文件

---

## 5. 恢复流程细节

`Restore(stream, table)`：
- 创建 `WriteDbActor`
- 读取二进制头部与数据页
- 每页触发 `OnProcess` -> Actor 入队
- `WriteDbActor` 按列匹配后写入目标表

关键点：
- 可 `setSchema=true` 自动建表
- 页错误可按 `IgnorePageError` 决定是否中断
- 支持取消令牌用于长任务中断

---

## 6. 同步流程细节

`Sync(table, connName)` 本质是：
- 源库抽取
- 目标库写入
- 可选 `syncSchema`

`SyncAll` 支持多表并按 `IgnoreError` 容错。

---

## 7. Actor 机制亮点

### 7.1 WriteFileActor

- 首次写入 Header
- 后续页按字段索引映射写 Data
- 每页 `FlushAsync`

### 7.2 WriteDbActor

- 首次匹配“数据页列名 -> 目标表列”
- `BatchInsert=true` 时走批量插入最高吞吐
- 否则逐行插入并可忽略单行失败

---

## 8. 扩展建议

1. 自定义 `CreateExtracterCallback`：对超大表按业务分区抽取。
2. 自定义 `WriteDbCallback`：增加行级重试、死信记录。
3. 用 `OnPage` 做实时进度推送（WebSocket/控制台进度条）。
4. 对跨地域同步开启压缩流并控制批大小，平衡网络与 DB 压力。

---

## 9. 线上实践建议

- 先用小表验证链路，再放量。
- 开启 `Tracer` 观察每页耗时与 tps。
- 恢复任务尽量在只读窗口或低峰执行。
- 对关键表同步后做行数与抽样校验。
