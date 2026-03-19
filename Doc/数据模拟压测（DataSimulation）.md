# 数据模拟压测（DataSimulation）

`XCode.Common.DataSimulation` 是仓库内置的轻量写入压测器，用于快速评估某个实体在当前数据库上的插入吞吐能力（TPS）。

它不是 BenchmarkDotNet 基准框架，而是偏工程化的“现场跑分工具”。

---

## 1. 核心能力

- 自动生成随机实体数据（`Int32/String/DateTime`）
- 支持并行准备数据（按 CPU 核心分片）
- 支持多线程写入（`Threads`）
- 支持事务分批提交（`BatchSize`）
- 可选直接执行原始 SQL（`UseSql=true`）
- 输出最终写入速度并给出 `Score`

---

## 2. 关键参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| `Factory` | 必填 | 目标实体工厂 |
| `BatchSize` | 1000 | 每多少条提交一次事务 |
| `Threads` | 1 | 写入并发线程数 |
| `UseSql` | false | true=先生成 SQL 再 `dal.Execute` |
| `Score` | 只读 | 最终写入速度（TPS） |

泛型版本更易用：

```csharp
var sim = new DataSimulation<VisitLog>();
```

会自动把 `Factory` 设为 `Entity<VisitLog>.Meta.Factory`。

---

## 3. 快速示例

```csharp
var sim = new DataSimulation<Order>
{
    Threads = Environment.ProcessorCount,
    BatchSize = 2000,
    UseSql = false,
    Log = XTrace.Log
};

sim.Run(100_000);
Console.WriteLine($"TPS={sim.Score:n0}");
```

执行过程：
1. 关闭 SQL 日志 (`session.Dal.Db.ShowSQL=false`)。
2. 预热并读取当前表行数。
3. 并行构造 `count` 条随机实体。
4. 多线程分段写入，每 `BatchSize` 条提交一次事务。
5. 统计耗时并换算 TPS：

$$TPS = \frac{写入总条数 \times 1000}{耗时毫秒}$$

---

## 4. UseSql 模式对比

### UseSql = false（默认）
- 调 `entity.Insert()`
- 走完整实体管道（校验、拦截器、缓存逻辑）
- 更接近真实业务链路

### UseSql = true
- 先 `pst.GetSql(...Insert)` 生成 SQL
- 直接 `dal.Execute(sql)`
- 更贴近纯 SQL 写入上限

> 对比两种模式的分数，可量化“ORM 层带来的额外开销”。

---

## 5. 结果解读建议

- 同机对比：仅在同一数据库、同一配置下比较不同版本代码
- 同版本对比：固定数据量，只调 `Threads/BatchSize` 找到最佳区间
- 数据库对比：同实体同参数测试 MySQL/SqlServer/PostgreSQL 的差异

建议至少跑 3 次取中位数，避免首次建表、缓存预热导致偏差。

---

## 6. 注意事项

1. `Run()` 会真实写库，请使用测试库或临时表。
2. 该工具默认随机字符串长度 8，不覆盖所有业务字段约束；复杂实体请先调整模型允许随机数据写入。
3. 压测前建议关闭慢 SQL 阈值和调试日志，避免日志 IO 干扰。
4. 压测结束后记得清理测试数据，避免污染统计和缓存。

---

## 7. 推荐组合

把 `DataSimulation` 与以下能力联用，形成“性能三件套”：
- `EntityQueue`：验证异步批量写入峰值
- `BatchInsert/BatchUpsert`：验证批量原语极限
- `ReadWriteStrategy`：验证读写分离场景下主库写入压力

这样可快速得到：
- 链路级吞吐（真实业务）
- SQL 级吞吐（原始写入）
- 架构级吞吐（读写隔离 + 缓存）
