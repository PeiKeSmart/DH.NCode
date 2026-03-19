# 脏数据跟踪机制（DirtyCollection）

XCode 的“脏数据”核心由 `DirtyCollection` 承载，用来记录实体中哪些字段被修改过，以及修改前的旧值。`Update()` 时只更新脏字段，这是高性能与并发友好的关键。

---

## 1. 设计目标

源码注释明确了目标：
- 并发安全
- 低锁开销
- 低内存占用
- 允许少量重复，换取更低复杂度

因此没有使用 `ConcurrentDictionary`，而是采用两段数组：

```csharp
private String?[] _keys = new String?[8];
private Object?[] _values = new Object?[8];
```

配合 `_length` + `_count` 管理有效位。

---

## 2. 内部结构与行为

| 字段 | 作用 |
|------|------|
| `_keys[]` | 脏字段名数组 |
| `_values[]` | 字段旧值数组（与 `_keys` 同索引） |
| `_length` | 已占用槽位长度（只增不减） |
| `_count` | 当前有效脏字段数量 |

### 2.1 Add 逻辑

1. 先 `Contains(key)` 去重。
2. `Interlocked.Increment(ref _length)` 抢占一个新槽位。
3. 如果数组不足，进入 `lock(this)` 扩容为两倍。
4. 写入 key/oldValue，并 `Interlocked.Increment(ref _count)`。

### 2.2 Remove 逻辑

不移动数组元素，只把命中的 key 置 null：

```csharp
ms[i] = null;
Interlocked.Decrement(ref _count);
```

这会留下“洞”，但避免了数组搬迁和并发冲突。

### 2.3 Clear 逻辑

- `_length=0`
- `_count=0`
- `Array.Clear(_keys, ...)`

> 注意：源码只清 `_keys`，不清 `_values`，是有意保留（减少清理成本，旧值数组会被后续覆盖）。

---

## 3. 在实体生命周期中的作用

实体属性 setter 通常会：
1. 比较新旧值
2. 若变化，调用 DirtyCollection 记录字段名和旧值
3. `HasDirty=true`

当执行 `entity.Update()`：
- SQL 仅包含脏字段（以及必要系统字段）
- 可显著降低数据库写压力
- 降低并发更新“互相覆盖”的概率

---

## 4. 常用访问方式

```csharp
// 判断字段是否为脏
var changed = entity.Dirtys[User._.RoleId.Name];

// 枚举所有脏字段
foreach (var name in entity.Dirtys)
{
    Console.WriteLine(name);
}

// 获取字段旧值字典
var olds = entity.Dirtys.GetDictionary();
```

`GetDictionary()` 适合审计日志：可构造“变更前快照”。

---

## 5. 性能特性

### 优点

- 数组结构在“每个实体只有少量脏字段”场景非常省内存
- 去掉复杂并发容器后，GC 压力低
- `Interlocked` + 局部锁，适合高频写字段场景

### 代价

- 删除不压缩数组，长生命周期对象会有空洞
- `Contains` 是线性扫描，字段很多时退化为 O(n)

这与 XCode 的典型场景匹配：实体字段修改数通常很少，脏集合长度有限。

---

## 6. 实战建议

1. **不要手工频繁 Clear/重建实体**，会影响脏追踪语义。
2. 批量更新时优先使用 `AdditionalFields` 做累加字段，避免大量字段进入脏集合。
3. 如果你在 `Valid()` 中自动修正字段（如 `Name.Trim()`），会额外制造脏字段；注意顺序和必要性。
4. 需要“全字段更新”时，可使用实体工厂设置或批量原语，而不是人为构造全部脏字段。

---

## 7. 与缓存联动

脏字段只控制 SQL 更新列；缓存失效由 `EntitySession` 的 `DataChange/ClearCache` 负责。两者分工明确：
- `DirtyCollection`：减少写入列
- `EntitySession`：保证读缓存一致性
