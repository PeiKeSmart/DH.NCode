# 实体工厂初始化与建模（EntityFactory）

`EntityFactory` 是 XCode 的实体元信息入口，负责：
- 创建/缓存 `IEntityFactory`
- 扫描连接下实体类
- 触发建表（反向工程）
- 触发实体初始化数据

它是 `Entity<T>.Meta` 背后最核心的运行时基础设施之一。

---

## 1. 工厂创建机制

### 1.1 CreateFactory

`CreateFactory(Type type)` 的关键点：

1. 从 `_factories` 并发字典读取已有工厂。
2. 处理“实体子类”场景，向上回溯到泛型实体基类。
3. 读取 `EntityFactoryAttribute`，可指定自定义工厂类型。
4. 未指定时使用默认 `Entity<>.DefaultEntityFactory<T>`。
5. 最终通过 `GetOrAdd` 保证并发下工厂唯一。

> 并发唯一很关键：例如雪花 ID 生成器等状态对象必须只有一份。

### 1.2 常用入口

```csharp
var factory = typeof(User).AsFactory();
// 等价于 EntityFactory.CreateFactory(typeof(User))
```

---

## 2. 实体扫描与表收集

### 2.1 LoadEntities(connName)

按连接名扫描实体：
- 从 `IEntity` 所有子类中筛选
- 仅保留基类为泛型实体的类型
- `TableItem.Create(type).ConnName == connName` 才返回

### 2.2 GetTables(connName, checkMode)

收集指定连接的数据表模型：
- 可按 `ModelCheckMode` 过滤
- 检查同名表是否被多个实体占用（冲突即抛异常）
- 返回 `IDataTable` 列表用于反向工程

---

## 3. 一次性初始化入口

### 3.1 InitAll / InitAllAsync

初始化所有连接：
- 扫描所有实体
- 仅处理 `CheckAllTablesWhenInit` 的实体
- 按连接分组执行 `Init(connName, ...)`

`InitAllAsync()` 为每个连接开一个 LongRunning 任务并行初始化。

### 3.2 InitConnection(connName)

只初始化一个连接，适合：
- 单库应用
- 后台作业启动时按需预热

### 3.3 InitEntity(type)

只初始化一个实体（会触发该实体 `Session.InitData()`）。

---

## 4. Init(connName, ...) 内部流程

`Init` 的核心执行顺序：

1. 找出连接名匹配且需要初始化的实体工厂。
2. `DAL.Create(connName)` 获取数据库访问对象。
3. 若 `Migration > Off`：
   - 跳过分片实体（`ShardPolicy` 或 `DataScale` 含 shard 标记）
   - 克隆实体表模型并纠正表名
   - `dal.SetTables(...)` 执行增量建模
4. 遍历工厂执行 `item.Session.InitData()` 初始化种子数据。

---

## 5. 何时手动调用

推荐在应用启动阶段显式调用其一：

```csharp
EntityFactory.InitAll();
// 或仅初始化主连接
EntityFactory.InitConnection("Membership");
```

这样可把“首次请求建表”的不确定性前置到启动期，减少首请求抖动。

---

## 6. 与 ModelCheckMode 的关系

| 模式 | 行为 |
|------|------|
| `CheckAllTablesWhenInit` | 启动初始化时参与建表 |
| `CheckTableWhenFirstUse` | 首次使用实体时由 `EntitySession` 触发建表 |
| 其它 / Off | 按配置可能跳过建模 |

如果希望“启动即完成所有库表准备”，需把关键实体设为 `CheckAllTablesWhenInit`。

---

## 7. 实战建议

1. **生产环境建议明确 Migration 策略**，避免误改表结构。
2. 分库分表实体通常不参与统一建模，需单独策略管理。
3. 初始化日志建议开启，便于定位哪个连接/实体卡住。
4. 若启动耗时长，可用 `InitAllAsync()` 并在健康检查中等待初始化完成。
