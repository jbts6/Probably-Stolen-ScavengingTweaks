# 垃圾场拾荒增强 Mod 设计

## 目标

在当前 Steam Beta 构建中，通过运行时 Harmony 补丁调整垃圾场拾荒：每日最多 10 次、轻伤和重伤概率为 0、高价值掉落权重约提高 2 倍。其他场景的伤害、掉落和存档结构保持原样。

## 方案

新增独立 `ScavengingTweaks.dll`，由 MelonLoader 从 `Mods` 自动加载。Mod 在初始化时创建 MelonPreferences 配置并安装 Harmony 补丁，目标类型或方法缺失时记录错误并停用，不阻塞游戏启动。

补丁点：

- `ScavHelper.GetMaxScavAttempts`：返回 `MaxAttempts` 配置值。
- `ScavHelper.GetMinorWoundChance` / `GetMajorWoundChance`：返回 `0f`，同步拾荒窗口显示。
- `ScavHelper.RollMinorWound` / `RollMajorWound`：Prefix 直接返回 `false`，防止实际伤害路径绕过显示 getter。
- `ScavHelper.ResetScavenging` 与 `PlayerStore.BeginDay`：在原逻辑完成后把当前 `scavengingAttempts` 设为配置值，保证每日重置和新存档入口一致。
- `ExpeditionLocationList.DumpingGrounds`：Postfix 读取 `possibleLoot`，通过 `DirectoryMaster.Item(id, false).GetBaseValue()` 计算基础价值，将价值处于列表最高一半的条目按 `HighValueMultiplier` 复制到同一列表。每个 location 实例只处理一次。

## 配置

配置分类为 `ScavengingTweaks`：

- `MaxAttempts`：默认 `10`，最小值限制为 `1`。
- `HighValueMultiplier`：默认 `2.0`，最小值限制为 `1.0`，用于掉落表权重复制次数。

伤害概率固定为 0，不提供可重新开启的配置，避免界面与实际判定不一致。

## 失败处理与兼容性

- 初始化逐个查找类型和方法；任一补丁失败只记录具体目标，其他补丁继续安装。
- 物品价值读取失败时跳过该条目，不修改原列表。
- 使用 `HashSet<int>` 按对象实例去重，避免窗口刷新或重复调用导致权重无限膨胀。
- 所有 Harmony 回调捕获异常，日志包含 Mod 名和目标方法。
- 首版面向当前 `Assembly-CSharp.dll` 与 MelonLoader 运行环境，不修改原始 DLL、资源包或存档。

## 验收

1. 拾荒界面显示剩余次数上限为 10，重置后实际可执行 10 次。
2. 拾荒界面显示轻伤、重伤均为 0.00%。
3. 连续执行拾荒不会触发轻伤或重伤。
4. 垃圾场掉落列表中高价值条目权重约为原来的 2 倍，其他条目权重不变。
5. 非垃圾场的伤害和掉落行为不受影响。

