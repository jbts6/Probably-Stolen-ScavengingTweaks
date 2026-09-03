# ScavengingTweaks 调试交接

更新时间：2026-09-03

## 当前状态

已完成诊断日志增强，等待游戏内测试验证。

- Git 基线：`6ffdc0f docs: add diagnostic testing instructions`
- 构建状态：✅ 编译通过，✅ 测试通过
- Mod DLL：`Mods\ScavengingTweaks.dll` 已更新（包含探针日志）
- 测试指引：见 `TESTING_INSTRUCTIONS.md`
- 未执行远程 push

## 本次更新内容（2026-09-03）

### 增强的诊断日志

在 `SpawnFromTableGroupPrefix` 中新增探针机制：

1. **采样被调整的物品**：从首个被放大权重的物品中提取诊断样本
2. **三阶段记录**：
   - BEFORE：记录调整前的 `m_Weight`、`m_BaseProbability`、`BaseProbability`、`Probability`
   - AFTER set m_Weight：记录直接修改 `m_Weight` 后的字段状态
   - AFTER UpdateProperties：尝试反射调用 `RNGNeeds_IProbabilityItem_UpdateProperties()` 并记录结果
3. **保留物品引用**：改用 `List<ProbabilityItem<string>>` 保存实际对象引用，确保探针和恢复操作访问同一实例

### 核心验证目标

确认以下三种可能性之一：
- ✅ 修改 `m_Weight` 后 `Probability` 自动更新 → 当前方案有效
- ⚠️ 需要调用 `UpdateProperties` 才更新 → 需要在每次调整后显式调用
- ❌ `Probability` 始终不变 → 需要切换到备用方案（调整 `m_BaseProbability` 或结果替换）

### 相关文件

- `ScavengingTweaks\Mod.cs:394-481` - 增强的 SpawnFromTableGroupPrefix
- `TESTING_INSTRUCTIONS.md` - 用户测试指引和日志解读方法


用户把 `HighValueMultiplier` 设为 `10`，拾荒结果仍以垃圾为主；一次 10 次测试中只出现 8 次非空结果。最新相关日志是：

`MelonLoader\Logs\26-9-3_12-37-54.log`

该日志显示第 6、9 次为原生空结果，其余尝试正常返回物品。空结果来自原生 `PickValue`/`Spawn` 允许返回空，不应在插件中强行补物品。

## 已确认的真实调用链

反编译和运行时反射已经确认：

```text
ScavHelper.ScavengeDumpingGrounds
  -> ScavHelper.GetRandomScavengedItem
    -> ItemSpawner.SpawnFromTableGroup("dumpingGroundTG")
      -> TableGroupMaster.tableGroups[tableGroupID]
      -> TableGroup.tableGroup (ProbabilityList<LootTable>).PickValue()
      -> LootTable.table (ProbabilityList<string>).PickValue()
      -> ItemSpawner.Spawn(itemId)
```

关键证据文件（已用于分析，临时反编译目录已清理）：

- `ItemSpawner.txt`：`SpawnFromTableGroup` 在约 123-331 行直接读取两层概率表。
- `ScavHelper.txt`：约 1264 和 1531 行各有一次 `SpawnFromTableGroup("dumpingGroundTG")`。
- `TableGroupMaster.cs`：静态 `tableGroups` 字典。
- `TableGroup.cs`：`tableGroup` 为 `ProbabilityList<LootTable>`。
- `LootTable.cs`：`table` 为 `ProbabilityList<string>`。
- `ProbabilityList\`1`：`ProbabilityItems` 属性和 `PickValue`。

`possibleLoot` 是本地化显示名，不是物品 ID；`LootTable.Roll` 也没有命中拾荒路径。因此，补丁挂在这些位置都不能改变实际拾荒抽样。

## 当前实验性改动

`ScavengingTweaks\Mod.cs`：

- `InstallPatches` 新增 `ItemSpawner.SpawnFromTableGroup` 的 prefix、postfix、finalizer。
- prefix 遍历 `TableGroup.tableGroup.ProbabilityItems` 下每个 `LootTable.table.ProbabilityItems`，收集真实物品 ID、权重和物品价值。
- 调用 `LootWeighting.ScaleHighValueWeights` 临时放大最高价值一半的 `m_Weight`。
- postfix/finalizer 恢复所有原始权重，避免污染游戏全局表。
- 保留 `GetRandomScavengedItem` 结果日志，用于区分真实空结果和插件异常。

`ScavengingTweaks\LootWeighting.cs`：

- 新增 `ScaleHighValueWeights` 和溢出保护。
- 最高价值一半按唯一物品 ID 选择，未知价值不参与排序。

`ScavengingTweaks.Tests\LootWeightingTests.cs`：

- 覆盖 2 倍权重缩放和 `int.MaxValue` 溢出钳制。

## 必须优先验证的风险

sol 复核指出，当前实验性实现可能仍然无效：

- 运行时 `ProbabilityItem<T>.set_m_Weight` 虽然可调用，但反射/反编译显示它可能只是直接写序列化字段。
- `ProbabilityItem<T>.get_Probability` 实际读取的是 `m_BaseProbability`；`SelectionMethodBase`/`CumulativeProbability` 通过 `Probability` 参与抽样。
- 因此日志里的 `adjustedEntries > 0` 只能证明字段被改过，不能证明随机概率改变。
- 当前实现没有调整外层 `TableGroup.tableGroup` 权重；如果高价值物品分布由外层 LootTable 选择主导，10 倍效果仍可能很弱。

## 下一次继续工作的建议顺序

1. 在 `SpawnFromTableGroup` prefix 中增加一次性探针，逐项打印：物品 ID、`m_Weight`、`m_BaseProbability`、`BaseProbability`、`Probability`、`PropertiesUpdated`。
2. 分别执行 `m_Weight` setter 和显式 `RNGNeeds_IProbabilityItem_UpdateProperties()` 后再次读取上述字段，确认哪一步真正影响 `Probability`。
3. 如果 `Probability` 不变，改为临时缩放实际的 `m_BaseProbability`（必要时同时保存/恢复 `m_Weight`），或在 `SpawnFromTableGroup` postfix 基于真实候选做低价值结果替换。保留 `null`/空结果，不要强制每次产出。
4. 记录 `SpawnFromTableGroup` 的实际返回 ID、外层选中的 LootTable，以及每个候选的旧值/新值，确认是内层还是外层概率占主导。
5. 用 `HighValueMultiplier=10` 重新构建并让用户手动跑一轮；重点看日志是否出现权重调整、`Probability` 是否变化，以及高价值物品比例是否明显提升。

## 已通过的离线检查

```text
dotnet build ScavengingTweaks\ScavengingTweaks.csproj --no-restore
0 warnings, 0 errors

dotnet run --project ScavengingTweaks.Tests\ScavengingTweaks.Tests.csproj --no-restore
LootWeighting tests passed.

git diff --check
通过
```

临时反编译输出和 PowerShell 探针脚本已从 `docs\superpowers` 删除；方案文档仍保留。
