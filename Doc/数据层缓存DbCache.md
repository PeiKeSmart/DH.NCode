# 数据层缓存（DbCache）

`DbCache` 是基于数据库表实现的键值缓存，位于 `NewLife.Caching.DbCache`。适合在“无 Redis/跨进程共享”场景下提供统一缓存能力。

## 1）设计定位

- 存储介质：数据库表（默认 `MyDbCache`）
- 接口形态：实现 `NewLife.Caching.Cache`
- 值序列化：JSON
- 本地热点：内置 `MemoryCache`（默认 60 秒）

## 2）实体要求

缓存实体默认需实现 `IDbCache`：

- `Name`（键）
- `Value`（值）
- `CreateTime`
- `ExpiredTime`
- `SaveAsync()`

并要求主键（或指定键字段）为 `String` 类型。

## 3）构造与字段选择

```csharp
var cache = new DbCache(factory, keyName: "Name", timeName: "ExpiredTime");
```

内部会解析：
- `KeyField`
- `TimeField`

并降低 SQL 日志噪音（`ShowSQL=false`）以降低缓存场景开销。

## 4）基础操作

- `Set<T>(key, value, expire)`：存在则更新，不存在则创建
- `Get<T>(key)`：反序列化 JSON 返回对象
- `Add<T>(...)`：仅当不存在时添加（可用于锁争夺）
- `Remove(key/keys)`：删除并清内存热点
- `SetExpire/GetExpire`
- `Clear()`：清空整表

## 5）过期清理

`Init()` 启动定时器，每 60 秒执行：

- 查询 `ExpiredTime < now`
- 删除数据库项
- 同步移除本地热点

因此它是“懒访问 + 周期清理”的组合策略。

## 6）使用建议

适用：
- 多进程共享少量配置/令牌
- 不依赖外部缓存组件的中小系统

不适合：
- 超高 QPS 热点缓存
- 大对象频繁写入（数据库压力大）

## 7）最佳实践

- 缓存值尽量扁平化，减少 JSON 序列化成本。
- key 命名加入业务前缀：`Order:State:xxx`。
- 过期时间不要太短，避免数据库抖动。
- 与 `MemoryCache` 组合使用，DbCache 作为兜底。

## 8）常见问题

- **Get 取不到值？**
  - 检查是否过期已被清理。
- **缓存表很大？**
  - 检查是否设置了合理 `Expire`，并确认清理任务运行。