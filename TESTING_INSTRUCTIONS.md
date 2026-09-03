# ScavengingTweaks 诊断测试指引

更新时间：2026-09-03

## 本次构建目标

确认 `HighValueMultiplier=10` 的权重调整是否真正影响了游戏抽样概率。

## 测试步骤

1. **确认 mod 已加载**
   - 启动游戏后查看 `MelonLoader\Logs\<最新>.log`
   - 应看到类似日志：
     ```
     ScavengingTweaks loaded: attempts=10, high-value multiplier=10.00x, wound mode=ScavengingOnly
     ScavengingTweaks test hotkeys armed: F7=skip to evening, F8=direct scavenge.
     ```

2. **执行 10 次拾荒测试**
   - 进入游戏后按 F7 跳过白天阶段
   - 连续按 F8 执行 10 次拾荒
   - 记录每次获得的物品

3. **收集关键日志**
   - 打开最新的 `MelonLoader\Logs\<日期时间>.log`
   - 搜索以下关键字并复制相关日志块：
     - `weight adjustment probe BEFORE`
     - `weight adjustment probe AFTER set m_Weight`
     - `weight adjustment probe AFTER UpdateProperties`
     - `applied dump weights`
     - `scavenge result: drops=`

## 关键验证点

### 1. 探针日志必须出现
应看到三组探针日志，格式类似：
```
ScavengingTweaks weight adjustment probe BEFORE: id=<物品ID>, m_Weight=<原始>, m_BaseProbability=<值>, BaseProbability=<值>, Probability=<值>.
ScavengingTweaks weight adjustment probe AFTER set m_Weight: id=<物品ID>, m_Weight=<调整后>, ...
ScavengingTweaks weight adjustment probe AFTER UpdateProperties: id=<物品ID>, m_Weight=<调整后>, ...
```

### 2. Probability 字段是否变化
- **如果 `Probability` 在 `AFTER set m_Weight` 后已变化** → 权重调整有效，无需 UpdateProperties
- **如果 `Probability` 在 `AFTER UpdateProperties` 后才变化** → 需要显式调用 UpdateProperties
- **如果 `Probability` 始终不变** → 当前方案无效，需要改用其他策略（见下方备用方案）

### 3. 高价值物品比例
如果 10 次拾荒中：
- **垃圾/低价值物品仍占 60% 以上** → 权重调整未生效或外层表权重占主导
- **高价值物品占 50% 以上** → 权重调整生效

## 备用方案（如果当前方案无效）

根据探针日志结果，可能需要：

### 方案 A：显式调用 UpdateProperties
如果探针显示需要调用 UpdateProperties 才能更新 Probability，需要在调整每个 m_Weight 后立即调用该方法。

### 方案 B：调整 m_BaseProbability
如果 Probability 实际读取的是 m_BaseProbability，需要改为调整该字段而非 m_Weight。

### 方案 C：结果替换策略
在 SpawnFromTableGroupPostfix 中检查返回结果，如果是低价值物品，按配置概率从高价值候选中重新抽取替换。

### 方案 D：外层表权重调整
如果日志显示高价值物品分散在多个 LootTable 中，需要同时调整外层 `TableGroup.tableGroup` 的权重。

## 提交日志格式

请提供以下信息：

1. **10 次拾荒结果列表**（物品名称或空结果）
2. **探针日志完整输出**（三组 BEFORE/AFTER）
3. **`applied dump weights` 日志**（确认 adjustedEntries 数量）
4. **您的观察**：高价值物品是否明显增多？
