# DAL调试开关与批处理参数（DAL_Setting）

`DAL_Setting`（`DAL` 的设置/辅助分部）集中定义了运行时常用能力：
- 调试日志开关
- SQL 局部拦截器
- 链路追踪器
- 批处理能力判断与批大小获取

这些能力直接影响线上可观测性与批量写性能。

---

## 1. Debug 与日志输出

### 1.1 全局调试开关

```csharp
DAL.Debug = true;
```

默认值来自 `XCodeSetting.Current.Debug`。

### 1.2 日志输出 API

- `DAL.WriteLog(...)`：运行时受 `DAL.Debug` 控制
- `DAL.WriteDebugLog(...)`：仅 `DEBUG` 编译符号下生效

初始化时（`InitLog()`）会输出组件版本；若 `ShowSQL=true` 会提示 SQL 日志开关信息。

---

## 2. SQL 局部拦截

`DAL.LocalFilter` 是线程/异步上下文本地 SQL 过滤器：

- .NET 4.5：`ThreadLocal<Action<String>>`
- 其它：`AsyncLocal<Action<String>>`

可用于：
- 单请求采样 SQL
- 临时脱敏日志
- A/B 实验观测

```csharp
DAL.LocalFilter = sql =>
{
    if (sql.Contains("Password", StringComparison.OrdinalIgnoreCase))
        XTrace.WriteLine("[敏感SQL已拦截]");
};
```

---

## 3. APM 追踪器

- `DAL.GlobalTracer`：全局追踪器（默认 `DefaultTracer.Instance`）
- `dal.Tracer`：实例级追踪器，可覆盖全局

配合 `EntityTransaction`、`QueryCountFast` 等路径能输出 `db:*` span，便于定位慢 SQL 与热点表。

---

## 4. 批处理能力判断

`DAL.SupportBatch` 用于判断当前数据库是否支持批操作。

当前逻辑支持：
- MySql
- Oracle
- SQLite
- PostgreSQL
- SqlServer
- NovaDb

> 实际是否走批量，还取决于具体操作类型和 SQL 生成路径。

---

## 5. 批大小获取规则

`GetBatchSize(defaultSize=5000)` 的优先级：

1. `Db.BatchSize`
2. `XCodeSetting.Current.BatchSize`
3. 方法默认参数
4. 最终兜底 5000

这让你可以按“连接级”与“全局级”分别调优。

---

## 6. 推荐配置策略

### 6.1 开发环境

- `Debug=true`
- `ShowSQL=true`
- `BatchSize=1000~3000`

### 6.2 生产环境

- `Debug=false`（默认）
- `ShowSQL=false`（必要时按请求采样）
- `BatchSize` 根据数据库能力压测后设定（常见 2000~10000）

---

## 7. 常见误区

1. **线上长期开启 Debug+ShowSQL**：日志 IO 会显著影响吞吐。
2. **盲目拉高 BatchSize**：单批过大可能触发锁等待与长事务。
3. **只看批量速度，不看失败重试成本**：批量失败回滚会放大损失面。

---

## 8. 最小可观测实践

- 线上默认关闭 SQL 明细
- 通过 `LocalFilter` 对慢请求做短时采样
- 对关键路径开启 `GlobalTracer`，观察 `db:*` span
- 每次改 `BatchSize` 后回归压测并记录 TPS/延迟
