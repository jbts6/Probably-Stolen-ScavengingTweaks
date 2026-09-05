# 拾荒掉落类型配额 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用分层配额分布替换双层倍乘：高价值层（价值 ≥ 60）占 85% 按类型配额分配，低端填充 15%，消除单物品垄断且保证高价值出现率。

**Architecture:** 纯算法进 `DropSharing.cs`（万分比精确分配、最大余数校正，离线可测）；`Mod.cs` 在 `SpawnFromTableGroupPrefix` 中加 `QuotaModeEnabled` 分支——跳过外层倍乘、在物品收集后直接写入每件物品与外层表的目标概率（`m_BaseProbability`），恢复机制沿用 `pendingRestores`（精度提升为万分比）。

**Tech Stack:** .NET 6 类库（MelonLoader mod）、HarmonyLib、Il2Cpp 互操作（Assembly-CSharp / Il2CppRNGNeeds）、net8.0 控制台离线测试。

**规格：** `docs/superpowers/specs/2026-09-05-drop-shares-design.md`

## Global Constraints

- 工作目录：游戏根 `C:\Program Files\Steam\steamapps\common\Probably Stolen Playtest`。
- 配置分类 `ScavengingTweaks`；新配置 `QuotaModeEnabled`（bool，默认 true）、`DropTypeShares`（默认 `"MODULE:25,TOOL:25,FOOD:10,ALCOHOL:10,MEDICAL:5,MATERIAL:5,WEAPON:5"`）、`LowTierShare`（int，默认 15）、`HighValueFloor`（int，默认 60）。
- 分配语义：价值 < 10 排除；高价值层占比恒为 100 − LowTierShare；空桶（无合格物品）清零后在层内按比例归一；多类型物品按配置串顺序取最先命中类型；同 itemId 多表重复出现只分配一次（首次有效）。
- 万分比整数，Σ 恰好 = 10000（最大余数法校正）。
- 所有 Il2Cpp 访问 try/catch + 日志；`QuotaModeEnabled=false` 必须完整保留现有倍乘路径。
- 测试项目只编译纯逻辑文件；游戏依赖代码不得进入被 Link 的文件。
- 构建产物自动输出 `Mods\ScavengingTweaks.dll`。

---

### Task 1: DropSharing.cs 纯逻辑（TDD）

**Files:**
- Create: `ScavengingTweaks/DropSharing.cs`
- Modify: `ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj`
- Modify: `ScavengingTweaks.Tests/LootWeightingTests.cs`

**Interfaces:**
- Consumes: 无（纯逻辑）
- Produces:
  - `DropSharing.ParseShares(string? config) -> List<KeyValuePair<string, double>>`：解析 `TYPE:pct` 逗号串，保留顺序、类型大写、去重、忽略非法项。
  - `DropSharing.ResolveBucket(IReadOnlyList<string> itemTypes, List<KeyValuePair<string, double>> shares) -> string?`：按配置顺序返回物品类型列表第一个命中的桶名，无命中 null。
  - `DropSharing.ComputeShares(IReadOnlyList<string> ids, IReadOnlyDictionary<string, long> values, Func<string, IReadOnlyList<string>?> typesResolver, string sharesConfig, int lowTierSharePercent, int highValueFloor, int excludeBelowValue = 10) -> ShareResult`；`ShareResult` 含 `List<int> SharesBp`（按 ids 出现顺序，Σ=10000）与 `List<string> Diagnostics`。

- [ ] **Step 1: 测试项目加入 DropSharing.cs 链接**

`ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj` 的 `<ItemGroup>` 中 GroundGrid.cs 行后插入：

```xml
    <Compile Include="..\ScavengingTweaks\DropSharing.cs" Link="DropSharing.cs" />
```

- [ ] **Step 2: 写失败测试**

`LootWeightingTests.cs` 的 `Main` 中 `GroundGridZeroExtraMeansOff();` 之后加入：

```csharp
            DropSharesParseBasic();
            DropSharesTotalExactly10000();
            DropSharesHighValueDominatesLowTier();
            DropSharesExcludeBelowValue();
            DropSharesMultiTypePriorityFollowsConfigOrder();
            DropSharesDuplicateIdAssignedOnce();
```

类末尾（`Ensure` 之前）加入测试方法与共享测试数据构造：

```csharp
    // 模拟真实物品池：3 件 T1 模块(100)、提取器(100)/焊接器(80)、nudka(85)、
    // 红啤酒(18, FOOD+ALCOHOL)、杯面(20)，外加垃圾(3)与跨表重复的管道武器(15)
    private static (List<string> ids, Dictionary<string, long> values, Func<string, IReadOnlyList<string>?> types) BuildQuotaScenario()
    {
        var ids = new List<string>
        {
            "t1a", "t1b", "t1c",          // MODULE 100
            "extractor", "welder",        // TOOL 100/80
            "nudka",                      // ALCOHOL 85
            "red_beer", "cup_noodle",     // 18 / 20
            "junk",                       // 3
            "pipe_weapon", "pipe_weapon"  // 15，跨表重复
        };
        var values = new Dictionary<string, long>
        {
            ["t1a"] = 100, ["t1b"] = 100, ["t1c"] = 100,
            ["extractor"] = 100, ["welder"] = 80,
            ["nudka"] = 85, ["red_beer"] = 18, ["cup_noodle"] = 20,
            ["junk"] = 3, ["pipe_weapon"] = 15
        };
        var typesByItem = new Dictionary<string, string[]>
        {
            ["t1a"] = new[] { "MODULE" }, ["t1b"] = new[] { "MODULE" }, ["t1c"] = new[] { "MODULE" },
            ["extractor"] = new[] { "TOOL" }, ["welder"] = new[] { "TOOL" },
            ["nudka"] = new[] { "ALCOHOL" },
            ["red_beer"] = new[] { "FOOD", "ALCOHOL" },
            ["cup_noodle"] = new[] { "FOOD" },
            ["junk"] = new[] { "MISC" },
            ["pipe_weapon"] = new[] { "WEAPON" }
        };
        Func<string, IReadOnlyList<string>?> resolver = id => typesByItem.TryGetValue(id, out var t) ? t : null;
        return (ids, values, resolver);
    }

    private static void DropSharesParseBasic()
    {
        var shares = DropSharing.ParseShares("module:25, TOOL:25, bad, TOOL:10, FOOD:x, FOOD:-1");
        Ensure(shares.Count == 2, "invalid share entries should be skipped and types deduped");
        Ensure(shares[0].Key == "MODULE" && Math.Abs(shares[0].Value - 25) < 0.001, "first entry should keep config order and uppercase the type");
        Ensure(shares[1].Key == "TOOL", "second entry should be TOOL");
    }

    private static void DropSharesTotalExactly10000()
    {
        var (ids, values, resolver) = BuildQuotaScenario();
        var result = DropSharing.ComputeShares(ids, values, resolver, "MODULE:25,TOOL:25,FOOD:10,ALCOHOL:10,MEDICAL:5,MATERIAL:5,WEAPON:5", 15, 60);
        var total = 0;
        foreach (var bp in result.SharesBp)
        {
            total += bp;
        }

        Ensure(total == 10000, $"shares must sum to exactly 10000 but was {total}");
    }

    private static void DropSharesHighValueDominatesLowTier()
    {
        var (ids, values, resolver) = BuildQuotaScenario();
        var result = DropSharing.ComputeShares(ids, values, resolver, "MODULE:25,TOOL:25,FOOD:10,ALCOHOL:10,MEDICAL:5,MATERIAL:5,WEAPON:5", 15, 60);

        var moduleBucket = result.SharesBp[0] + result.SharesBp[1] + result.SharesBp[2];
        Ensure(Math.Abs(moduleBucket - 3542) <= 2, $"MODULE bucket should be ~25/60 of 8500 but was {moduleBucket}");
        Ensure(result.SharesBp[3] > result.SharesBp[6], "extractor (high tier) must exceed red_beer (low tier)");
        Ensure(result.SharesBp[5] > result.SharesBp[6], "nudka (high tier) must exceed red_beer (low tier)");
        Ensure(result.SharesBp[6] == result.SharesBp[7], "low-tier items should split LowTierShare evenly");
        Ensure(result.SharesBp[0] == result.SharesBp[1] || Math.Abs(result.SharesBp[0] - result.SharesBp[1]) == 1, "same-bucket items should be near-equal (largest remainder)");
        Ensure(result.SharesBp[9] == 0 && result.SharesBp[10] == 0, "duplicate pipe_weapon occurrences beyond the first must get 0");
    }

    private static void DropSharesExcludeBelowValue()
    {
        var (ids, values, resolver) = BuildQuotaScenario();
        var junkIndex = ids.IndexOf("junk");
        var result = DropSharing.ComputeShares(ids, values, resolver, "MODULE:25,TOOL:25,FOOD:10,ALCOHOL:10", 15, 60);
        Ensure(result.SharesBp[junkIndex] == 0, "items below the exclusion floor must get 0 share");
    }

    private static void DropSharesMultiTypePriorityFollowsConfigOrder()
    {
        // red_beer(65) 同属 FOOD+ALCOHOL：桶归属由配置串顺序决定
        var ids = new List<string> { "strong_beer", "nudka" };
        var values = new Dictionary<string, long> { ["strong_beer"] = 65, ["nudka"] = 85 };
        Func<string, IReadOnlyList<string>?> resolver = _ => new[] { "FOOD", "ALCOHOL" };

        var alcoholFirst = DropSharing.ComputeShares(ids, values, resolver, "ALCOHOL:10,FOOD:10", 0, 60);
        Ensure(alcoholFirst.SharesBp[0] == alcoholFirst.SharesBp[1], "ALCOHOL-first config puts strong_beer in the same bucket as nudka");

        var foodFirst = DropSharing.ComputeShares(ids, values, resolver, "FOOD:10,ALCOHOL:10", 0, 60);
        Ensure(foodFirst.SharesBp[0] == 0 || foodFirst.SharesBp[0] != alcoholFirst.SharesBp[0], "FOOD-first config must move strong_beer out of the ALCOHOL bucket");
        Ensure(foodFirst.SharesBp[1] == 5000, "nudka alone in ALCOHOL bucket should take the whole ALCOHOL share");
    }

    private static void DropSharesDuplicateIdAssignedOnce()
    {
        var (ids, values, resolver) = BuildQuotaScenario();
        var result = DropSharing.ComputeShares(ids, values, resolver, "WEAPON:100", 0, 1, 10);
        Ensure(result.SharesBp[9] == 10000, "first pipe_weapon occurrence should take the whole WEAPON share");
        Ensure(result.SharesBp[10] == 0, "second pipe_weapon occurrence must get 0");
    }
```

注意：`DropSharesDuplicateIdAssignedOnce` 用 `HighValueFloor=1` 使 15 价值的管道武器进入高价值层，验证跨表去重；`IndexOf` 需要 `System.Linq`（文件已引用）。

- [ ] **Step 3: 运行测试确认失败**

```bash
dotnet run --project ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj --no-restore
```

Expected: 编译错误 `CS0246: 未能找到类型或命名空间名"DropSharing"`。

- [ ] **Step 4: 实现 DropSharing.cs**

创建 `ScavengingTweaks/DropSharing.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace ScavengingTweaks;

// 纯逻辑，无游戏类型依赖；由 ScavengingTweaks.Tests 直接编译。
public static class DropSharing
{
    public const int TotalBasisPoints = 10000;

    public sealed class ShareResult
    {
        public List<int> SharesBp = new();
        public List<string> Diagnostics = new();
    }

    public static List<KeyValuePair<string, double>> ParseShares(string? config)
    {
        var shares = new List<KeyValuePair<string, double>>();
        if (string.IsNullOrWhiteSpace(config))
        {
            return shares;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawPair in config.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = rawPair.Split(':');
            if (parts.Length != 2)
            {
                continue;
            }

            var type = parts[0].Trim().ToUpperInvariant();
            if (type.Length == 0 || !seen.Add(type) || !double.TryParse(parts[1].Trim(), out var share) || share < 0)
            {
                continue;
            }

            shares.Add(new KeyValuePair<string, double>(type, share));
        }

        return shares;
    }

    /// <summary>物品归桶：按配置顺序取物品类型列表中第一个命中的类型；无命中返回 null。</summary>
    public static string? ResolveBucket(IReadOnlyList<string> itemTypes, List<KeyValuePair<string, double>> shares)
    {
        foreach (var share in shares)
        {
            for (var i = 0; i < itemTypes.Count; i++)
            {
                if (string.Equals(itemTypes[i], share.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return share.Key;
                }
            }
        }

        return null;
    }

    public static ShareResult ComputeShares(
        IReadOnlyList<string> ids,
        IReadOnlyDictionary<string, long> values,
        Func<string, IReadOnlyList<string>?> typesResolver,
        string sharesConfig,
        int lowTierSharePercent,
        int highValueFloor,
        int excludeBelowValue = 10)
    {
        var result = new ShareResult();
        var sharesBp = new int[ids.Count];
        if (ids.Count == 0)
        {
            return result;
        }

        var lowBp = Math.Clamp(lowTierSharePercent, 0, 100) * 100;
        var highBp = TotalBasisPoints - lowBp;
        var shares = ParseShares(sharesConfig);

        // 同一 itemId 跨表重复出现只分配一次份额（首次有效）
        var assigned = new HashSet<string>(StringComparer.Ordinal);
        var highByBucket = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var lowIndexes = new List<int>();

        for (var index = 0; index < ids.Count; index++)
        {
            var id = ids[index];
            if (string.IsNullOrWhiteSpace(id) || !assigned.Add(id))
            {
                continue;
            }

            var value = values.TryGetValue(id, out var v) ? v : 0L;
            if (value < excludeBelowValue)
            {
                continue;
            }

            var types = typesResolver(id);
            if (value >= highValueFloor)
            {
                var bucket = types == null ? null : ResolveBucket(types, shares);
                if (bucket == null)
                {
                    continue;
                }

                if (!highByBucket.TryGetValue(bucket, out var list))
                {
                    list = new List<int>();
                    highByBucket[bucket] = list;
                }

                list.Add(index);
            }
            else
            {
                lowIndexes.Add(index);
            }
        }

        // 高价值层：空桶（0 件合格物品）自动出局，非空桶按配置份额占比瓜分 highBp
        var nonEmptyWeight = 0.0;
        foreach (var bucket in highByBucket.Keys)
        {
            nonEmptyWeight += FindShare(shares, bucket);
        }

        if (nonEmptyWeight > 0)
        {
            foreach (var bucket in highByBucket.Keys)
            {
                var configured = FindShare(shares, bucket);
                var bucketBp = (int)Math.Round(configured / nonEmptyWeight * highBp);
                var list = highByBucket[bucket];
                DistributeEvenly(sharesBp, list, bucketBp);
                result.Diagnostics.Add(
                    $"quota bucket {bucket}: share={configured:0.##}, items={list.Count}, bucketBp={bucketBp}, perItem≈{bucketBp / (double)list.Count / 100.0:0.##}%");
            }
        }

        if (lowIndexes.Count > 0)
        {
            DistributeEvenly(sharesBp, lowIndexes, lowBp);
            result.Diagnostics.Add(
                $"quota low-tier: items={lowIndexes.Count}, totalBp={lowBp}, perItem≈{lowBp / (double)lowIndexes.Count / 100.0:0.##}%");
        }

        FixTotalWithLargestRemainder(sharesBp);
        for (var i = 0; i < ids.Count; i++)
        {
            result.SharesBp.Add(sharesBp[i]);
        }

        return result;
    }

    private static double FindShare(List<KeyValuePair<string, double>> shares, string bucket)
    {
        foreach (var share in shares)
        {
            if (share.Key == bucket)
            {
                return share.Value;
            }
        }

        return 0;
    }

    private static void DistributeEvenly(int[] sharesBp, List<int> indexes, int totalBp)
    {
        var baseBp = totalBp / indexes.Count;
        var remainder = totalBp % indexes.Count;
        for (var i = 0; i < indexes.Count; i++)
        {
            sharesBp[indexes[i]] = baseBp + (i < remainder ? 1 : 0);
        }
    }

    private static void FixTotalWithLargestRemainder(int[] sharesBp)
    {
        var total = 0;
        foreach (var bp in sharesBp)
        {
            total += bp;
        }

        if (total == 0)
        {
            return; // 全部被排除的退化配置：维持全 0，由游戏归一
        }

        var order = new List<int>();
        for (var i = 0; i < sharesBp.Length; i++)
        {
            order.Add(i);
        }

        order.Sort((a, b) => sharesBp[b].CompareTo(sharesBp[a]));

        var diff = TotalBasisPoints - total;
        while (diff > 0)
        {
            for (var i = 0; i < order.Count && diff > 0; i++)
            {
                sharesBp[order[i]]++;
                diff--;
            }
        }

        while (diff < 0)
        {
            for (var i = order.Count - 1; i >= 0 && diff < 0; i--)
            {
                if (sharesBp[order[i]] > 0)
                {
                    sharesBp[order[i]]--;
                    diff++;
                }
            }
        }
    }
}
```

- [ ] **Step 5: 运行测试确认通过**

```bash
dotnet run --project ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj --no-restore
```

Expected: `LootWeighting tests passed.`

- [ ] **Step 6: 提交**

```bash
git add ScavengingTweaks/DropSharing.cs ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj ScavengingTweaks.Tests/LootWeightingTests.cs
git commit -m "feat: add drop share quota math with basis-point precision"
```

---

### Task 2: Mod.cs 接线（配置 + 配额分支 + 类型解析）

**Files:**
- Modify: `ScavengingTweaks/Mod.cs`（字段区 ~L24-33、`OnInitializeMelon` ~L87-110、属性区 ~L257-260、`Patches.SpawnFromTableGroupPrefix` 内 ~L1305 与 ~L1420、`Patches` 类内新增 helper）

**Interfaces:**
- Consumes: Task 1 的 `DropSharing.ComputeShares(...)`、`DropSharing.ShareResult`；现有 `itemIdToTypes`、`InferTypesFromTableName`、`DirectoryMaster.Item(id, false)`、`__state.RestoreActions/OriginalWeights/AdjustedEntries`、局部变量 `tableEntries/itemEntries/ids/values/itemToTableName/tableItemCounts`。
- Produces: 配置 `QuotaModeEnabled/DropTypeShares/LowTierShare/HighValueFloor`；`Patches.ResolveItemTypes(string, string?) -> IReadOnlyList<string>`；`Patches.ApplyQuotaDistribution(...)`（私有，局部调用）。

- [ ] **Step 1: 注册配置项与属性**

字段区 `groundGridExtraRows` 声明后加入：

```csharp
    private static MelonPreferences_Entry<bool> quotaModeEnabled = null!;
    private static MelonPreferences_Entry<string> dropTypeShares = null!;
    private static MelonPreferences_Entry<int> lowTierShare = null!;
    private static MelonPreferences_Entry<int> highValueFloor = null!;
```

`OnInitializeMelon` 中 `groundGridExtraRows = category.CreateEntry<int>(...)` 之后加入：

```csharp
        quotaModeEnabled = category.CreateEntry<bool>(
            "QuotaModeEnabled",
            true,
            "Enable drop type quota mode",
            "true: drop rates follow DropTypeShares quotas. false: legacy multiplier logic.",
            false,
            false,
            null);
        dropTypeShares = category.CreateEntry<string>(
            "DropTypeShares",
            "MODULE:25,TOOL:25,FOOD:10,ALCOHOL:10,MEDICAL:5,MATERIAL:5,WEAPON:5",
            "High-tier drop type shares (percent)",
            "Percent of high-tier drops per item type. Items match the first type listed here. Empty buckets redistribute within the high tier.",
            false,
            false,
            null);
        lowTierShare = category.CreateEntry<int>(
            "LowTierShare",
            15,
            "Low-tier drops percent",
            "Percent of all drops for items below HighValueFloor (above the junk cutoff).",
            false,
            false,
            null);
        highValueFloor = category.CreateEntry<int>(
            "HighValueFloor",
            60,
            "High value floor",
            "Items worth at least this much belong to the high-value quota tier.",
            false,
            false,
            null);
```

属性区（`GroundGridExtraRows` 属性后）加入：

```csharp
    private static bool QuotaModeEnabled => quotaModeEnabled.Value;
    private static int LowTierShare => Math.Clamp(lowTierShare.Value, 0, 100);
    private static int HighValueFloor => Math.Max(1, highValueFloor.Value);
```

- [ ] **Step 2: 新增类型解析 helper（Patches 类内）**

`Patches` 类内（`RestoreAllPendingWeights` 方法之前）加入：

```csharp
        private static IReadOnlyList<string> ResolveItemTypes(string itemId, string? tableName)
        {
            if (itemIdToTypes.TryGetValue(itemId, out var cached))
            {
                return cached;
            }

            var types = new List<string>();
            try
            {
                var item = DirectoryMaster.Item(itemId, false);
                if (item?.itemTypes != null)
                {
                    for (var i = 0; i < item.itemTypes.Count; i++)
                    {
                        var type = item.itemTypes[i];
                        if (!string.IsNullOrWhiteSpace(type))
                        {
                            types.Add(type.Trim().ToUpperInvariant());
                        }
                    }
                }
            }
            catch (Exception)
            {
            }

            if (types.Count == 0 && !string.IsNullOrWhiteSpace(tableName))
            {
                var inferred = InferTypesFromTableName(tableName);
                if (inferred != null)
                {
                    types.AddRange(inferred);
                }
            }

            itemIdToTypes[itemId] = types;
            return types;
        }
```

物品类型优先取游戏物品标签（`DirectoryMaster.Item(id, false).itemTypes`，`List<string>`），无标签数据时回退到现有 `InferTypesFromTableName` 表名推断；结果缓存进 `itemIdToTypes`。

- [ ] **Step 3: 外层倍乘加门禁 + 插入配额分支**

`SpawnFromTableGroupPrefix` 中，第二步外层调整循环（`var highValueThreshold = (long)(overallMaxValue * 0.6);` 起至该 for 循环结束）整体包进门禁：

```csharp
                if (!QuotaModeEnabled)
                {
                    var highValueThreshold = (long)(overallMaxValue * 0.6);
                    for (var tableIndex = 0; tableIndex < tableEntries.Count; tableIndex++)
                    {
                        // ...原循环体保持不变...
                    }
                }
```

物品收集完成后的 `if (ids.Count == 0) { return; }` 之后（值诊断代码之前）插入：

```csharp
                if (QuotaModeEnabled)
                {
                    var quotaShares = DropSharing.ComputeShares(
                        ids,
                        values,
                        id => ResolveItemTypes(id, itemToTableName.TryGetValue(id, out var tn) ? tn : null),
                        dropTypeShares.Value,
                        LowTierShare,
                        HighValueFloor);

                    for (var index = 0; index < itemEntries.Count; index++)
                    {
                        var itemEntry = itemEntries[index];
                        __state.RestoreActions.Add(new Action<int>(w => itemEntry.m_BaseProbability = w / 10000f));
                        __state.OriginalWeights.Add((int)Math.Round(itemEntry.m_BaseProbability * 10000f));
                        itemEntry.m_BaseProbability = quotaShares.SharesBp[index] / 10000f;
                        if (quotaShares.SharesBp[index] > 0)
                        {
                            __state.AdjustedEntries++;
                        }
                    }

                    var cursor = 0;
                    for (var tableIndex = 0; tableIndex < tableEntries.Count; tableIndex++)
                    {
                        var tableEntry = tableEntries[tableIndex];
                        var tableShareBp = 0;
                        for (var k = 0; k < tableItemCounts[tableIndex]; k++)
                        {
                            tableShareBp += quotaShares.SharesBp[cursor + k];
                        }

                        cursor += tableItemCounts[tableIndex];
                        if (tableEntry == null)
                        {
                            continue;
                        }

                        __state.RestoreActions.Add(new Action<int>(w => tableEntry.m_BaseProbability = w / 10000f));
                        __state.OriginalWeights.Add((int)Math.Round(tableEntry.m_BaseProbability * 10000f));
                        tableEntry.m_BaseProbability = tableShareBp / 10000f;
                        __state.AdjustedEntries++;
                    }

                    if (LoggedWeightTables.Add(tableGroupID))
                    {
                        foreach (var line in quotaShares.Diagnostics)
                        {
                            MelonLogger.Msg("ScavengingTweaks {0}", line);
                        }

                        MelonLogger.Msg(
                            "ScavengingTweaks quota distribution applied: group={0}, items={1}, lowTier={2}%, highFloor={3}.",
                            tableGroupID,
                            itemEntries.Count,
                            LowTierShare,
                            HighValueFloor);
                    }

                    return;
                }
```

采用此内联方案后，Step 2 的 `ApplyQuotaDistribution` 不再需要——只保留 `ResolveItemTypes` helper（Step 2 的 `ApplyQuotaDistribution` 代码跳过不实施）。

- [ ] **Step 4: 构建验证**

```bash
dotnet build ScavengingTweaks/ScavengingTweaks.csproj --no-restore
```

Expected: `0 errors`，仅既有的 1 条 CS8604 警告（诊断代码遗留）。

- [ ] **Step 5: 回归离线测试**

```bash
dotnet run --project ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj --no-restore
```

Expected: `LootWeighting tests passed.`

- [ ] **Step 6: 提交**

```bash
git add ScavengingTweaks/Mod.cs
git commit -m "feat: apply drop type quota distribution when QuotaModeEnabled"
```

---

### Task 3: 游戏内验收与交接记录

**Files:**
- Modify: `SCAVENGING_TWEAKS_HANDOFF.md`

**Interfaces:**
- Consumes: Task 2 构建的 `Mods/ScavengingTweaks.dll`
- Produces: 验收结论

- [ ] **Step 1: 确认部署**

```bash
ls -l --time-style=+"%Y-%m-%d %H:%M" Mods/ScavengingTweaks.dll
```

Expected: 修改时间为 Task 2 构建时间。

- [ ] **Step 2: 游戏内验收（用户执行）**

重启游戏，拾荒 20+ 次，对照 `MelonLoader/Latest.log`：

1. 首次拾荒时日志打印 `quota bucket MODULE: share=25, items=6, ...` 等分布表 + `quota distribution applied`。
2. 酒瓶不再垄断：nudka 实测频率 ≈ 14%（原 26%）；提取器/焊接器各 ≈ 17.7%。
3. 高价值（≥60）合计 ≈ 85%；垃圾（<10）不出现。
4. 把 `QuotaModeEnabled` 改为 `false` 重启：回到旧倍产行为（酒瓶恢复垄断），确认后备路径完好，改回 `true`。

- [ ] **Step 3: 更新交接文档并提交**

在 `SCAVENGING_TWEAKS_HANDOFF.md` 顶部状态区追加「掉落类型配额」小节：默认配置、实测分布 vs 目标分布、QuotaModeEnabled 后备验证结果、遗留问题。

```bash
git add SCAVENGING_TWEAKS_HANDOFF.md
git commit -m "docs: record drop quota verification results in handoff"
```

---

## 自审记录

- **规格覆盖**：四个配置项（Task 2 Step 1）、排除/分层/归桶/空桶再分配/归一/万分比（Task 1 算法 + 测试）、多类型优先级（DropSharesMultiTypePriorityFollowsConfigOrder）、跨表去重（DropSharesDuplicateIdAssignedOnce）、外层写入（Task 2 Step 3 内联块）、恢复机制沿用（同块 RestoreActions）、诊断分布表（同块日志）、QuotaModeEnabled=false 后备（第二步门禁保留原路径）、验收（Task 3）——无遗漏。
- **占位符扫描**：Task 2 Step 3 中"原循环体保持不变"指对既有代码仅加门禁不改内容，锚点文本在执行时从文件读取；无 TBD/TODO。
- **类型一致性**：`ComputeShares` 签名在 Task 1 定义、Task 2 内联块以 `ids/values/dropTypeShares.Value/LowTierShare/HighValueFloor` 调用一致；`SharesBp` 按出现顺序对齐 `ids`/`itemEntries`/`tableItemCounts` 前缀和；`ResolveItemTypes` 返回 `IReadOnlyList<string>`（lambda 推断为 `IReadOnlyList<string>?`）与 `ComputeShares` 的 `Func<string, IReadOnlyList<string>?>` 一致。
