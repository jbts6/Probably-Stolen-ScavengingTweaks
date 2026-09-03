# ScavengingTweaks 调试交接

更新时间：2026-09-03

## 当前状态：发现价值判断错误

**关键问题**：权重调整机制工作正常（`m_BaseProbability` 从 0.03 放大到 3.0，100倍），但**被放大的物品是垃圾**。

- Git 基线：`6ffdc0f` + 本地未提交修改
- 构建状态：✅ 编译通过，✅ 测试通过
- Mod DLL：`Mods\ScavengingTweaks.dll` 已更新（新增价值诊断日志）
- 测试指引：见 `TESTING_INSTRUCTIONS.md`

### 本次诊断结果（15:18 日志分析）

✅ **权重调整机制正常工作**：
- `m_BaseProbability` 调整生效：0.0300 → 3.0000（100倍）
- `Probability` 字段同步更新
- 52个物品中有23个被调整（44.2%，接近预期的50%）

❌ **价值判断逻辑错误**：
- 被放大的样本物品：`system_module_ruined`（损坏的系统模块）
- 这是典型的垃圾物品，却被算法判定为"高价值"
- **根本原因**：`TryGetBaseValue` 读取的价值字段可能不正确

### 本次修改（未提交）

在 `SpawnFromTableGroupPrefix` 中新增价值诊断日志：
- 首次调用时显示TOP 5最高价值物品和BOTTOM 5最低价值物品
- 每个物品显示：排名、物品ID、数值价值
- 用于验证价值读取逻辑是否正确

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

### 下一步行动

**立即验证**：用户在游戏内按F8拾荒一次，查看日志中的价值诊断输出，确认：
1. TOP 5最高价值物品是什么（应该是电子零件、稀有材料等）
2. BOTTOM 5最低价值物品是什么（应该是垃圾）
3. `system_module_ruined` 在排序中的实际位置

**可能的修复方向**：

如果价值诊断显示排序错误：
- **方案A**：修正 `TryGetBaseValue` 的价值读取逻辑，使用正确的字段
- **方案B**：添加物品ID黑名单，手动排除已知的垃圾物品
- **方案C**：改用游戏内的品质等级（Quality）而非价格来判断

如果价值诊断显示排序正确但 `system_module_ruined` 仍被选中：
- 检查 `FindHighestValueHalf` 的排序逻辑
- 可能是同价值物品的tie-breaking规则有问题

### 测试方法

1. 启动游戏并加载存档
2. 按F7跳到晚上（开启拾荒）
3. 按F8执行一次拾荒
4. 退出游戏
5. 检查最新日志中的 `ScavengingTweaks value diagnostic` 部分

预期日志格式：
```
ScavengingTweaks value diagnostic - TOP 5 highest-value items:
  #1: item_id = value
  #2: item_id = value
  ...
ScavengingTweaks value diagnostic - BOTTOM 5 lowest-value items:
  ...
```



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
