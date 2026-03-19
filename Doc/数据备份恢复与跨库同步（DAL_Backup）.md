# 数据备份恢复与跨库同步（DAL_Backup）

`DAL_Backup` 为 `DAL` 提供了三类高价值运维能力：

1. 备份（Backup）
2. 恢复（Restore）
3. 跨库同步（Sync）

底层由 `DbPackage` 统一实现，`DAL` 只是门面封装。

---

## 1. 备份能力

### 1.1 单表备份到流

```csharp
var rows = dal.Backup(table, stream, cancellationToken);
```

### 1.2 单表备份到文件

```csharp
var rows = dal.Backup(table, "order_20260313.gz");
```

`.gz` 后缀会启用压缩。

### 1.3 批量备份到 zip

```csharp
var count = dal.BackupAll(tables, "backup.zip", backupSchema: true, ignoreError: true);
```

参数说明：
- `backupSchema=true`：同时备份表结构
- `ignoreError=true`：单表失败不影响后续表

---

## 2. 恢复能力

### 2.1 从流恢复

```csharp
var rows = dal.Restore(stream, table, cancellationToken);
```

### 2.2 从文件恢复单表

```csharp
var rows = dal.Restore("order_20260313.gz", table, setSchema: true);
```

### 2.3 从压缩包恢复多表

```csharp
var tables = dal.RestoreAll("backup.zip", tables: null, setSchema: true, ignoreError: true);
```

当 `tables=null` 时，可由压缩包中的模型文件反推表结构。

---

## 3. 跨库同步能力

### 3.1 单表同步

```csharp
var rows = dal.Sync(table, "TargetConn", syncSchema: true);
```

### 3.2 多表同步

```csharp
var result = dal.SyncAll(tables, "TargetConn", syncSchema: true, ignoreError: true);
```

返回 `Dictionary<String,Int32>`，可按表统计同步条数。

---

## 4. 取消与容错

- 多数 API 支持 `CancellationToken`
- `ignoreError=true` 时，某一张表失败会继续后续任务
- 推荐在长任务中配合日志与追踪器观察进度

---

## 5. 追踪与日志

每次调用会创建 `DbPackage` 并注入：
- `Tracer = dal.Tracer ?? DAL.GlobalTracer`
- `Log = XTrace.Log`

因此可在 APM 中观察备份/恢复/同步耗时，并在日志中查看失败详情。

---

## 6. 生产实践建议

1. 备份先做小表演练，再全库任务。
2. 大表分批同步，结合业务低峰期执行。
3. 同步前后做行数校验与抽样校验。
4. 恢复前先落到临时库验证，不要直接覆盖生产。
5. 定期演练“备份可恢复性”，而不是只看备份文件是否存在。
