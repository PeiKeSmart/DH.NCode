# 键控锁与并发分段（KeyedLocker）

`KeyedLocker<TEntity>` 是一个极简但非常实用的并发工具：
- 把任意字符串键映射到固定数量锁对象
- 同键串行、异键高概率并行
- 用很小内存成本获得稳定的并发控制

---

## 1. 核心实现

源码要点：

```csharp
private static Object[] Lockers;

static KeyedLocker()
{
    var Length = 8;
    var temp = new Object[Length];
    for (var i = 0; i < Length; i++) temp[i] = new Object();
    Lockers = temp;
}

public static Object SharedLock(String key)
{
    var code = key.GetHashCode();
    return Lockers[Math.Abs(code % Lockers.Length)];
}
```

即：`key -> hash -> 槽位(0..7) -> lock对象`。

---

## 2. 使用方式

```csharp
var gate = KeyedLocker<User>.SharedLock(user.Name);
lock (gate)
{
    // 针对同一用户名的临界区
    DoUpdate(user);
}
```

典型场景：
- 同一租户同一时间只能做一次结算
- 同一订单号并发回调去重
- 同一账号并发登录写状态串行

---

## 3. 设计取舍

### 优点

- 常量级内存（默认 8 把锁）
- 无额外字典维护开销
- 锁对象长期稳定，不会频繁分配

### 代价

- 不同 key 可能哈希碰撞到同一槽（伪冲突）
- 不能做到“每个 key 独立锁”那样精细

这是典型的“空间换时间 + 少量冲突可接受”策略。

---

## 4. 何时不适用

- 热点 key 极多且冲突代价高
- 需要可重入、可超时、可取消的锁语义
- 需要分布式进程间互斥（应使用 Redis/DB 锁）

---

## 5. 可扩展建议

如果你的场景并发量更大，可把槽位数量从 8 提升到 32/64（需要改源码）。

经验上：
- 槽位越多，冲突概率越低
- 但内存和 CPU cache 访问开销略增

可通过压测挑一个平衡点。
