# 垃圾场拾荒增强 Mod Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为当前 Unity IL2CPP Beta 构建增加可配置的垃圾场拾荒次数、零伤害概率和约两倍高价值掉落权重。

**Architecture:** 创建独立 MelonLoader Mod。Harmony 直接补丁 `ScavHelper` 的次数/伤害入口，并在 `ExpeditionLocationList.DumpingGrounds` 返回时复制高价值掉落条目；纯算法（价值分层、权重复制、配置边界）拆成无游戏依赖的 helper，便于离线测试。

**Tech Stack:** C#、.NET 6、MelonLoader、HarmonyLib、Il2CppInterop、自包含控制台测试夹具。

## Global Constraints

- 只修改运行时行为，不改动 `GameAssembly.dll`、`Assembly-CSharp.dll`、资源包和存档。
- 当前构建使用 `MelonLoader/Il2CppAssemblies/Assembly-CSharp.dll` 与 `MelonLoader/net6/0Harmony.dll`。
- 伤害概率固定为 `0f`；`MaxAttempts` 默认 `10`；`HighValueMultiplier` 默认 `2.0`。
- 每次修改后运行对应的离线测试或构建检查。

### Task 1: 创建可测试的掉落权重 helper

**Files:**
- Create: `ScavengingTweaks/ScavengingTweaks.csproj`
- Create: `ScavengingTweaks/LootWeighting.cs`
- Create: `ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj`
- Create: `ScavengingTweaks.Tests/LootWeightingTests.cs`

- [x] **Step 1: Write the failing tests**

覆盖空列表、最高一半判定、倍率 2 的复制数量和倍率下限。

- [x] **Step 2: Run tests and verify RED**

Run: `dotnet run --project ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj -c Release`

Expected: 因 `LootWeighting` 尚未存在而失败。

- [x] **Step 3: Implement minimal helper**

实现纯 C# `ExpandHighValueEntries(IReadOnlyList<string>, IReadOnlyDictionary<string,long>, double)`，保持原顺序，将高价值条目追加 `floor(multiplier)-1` 份，异常倍率按下限处理。

- [x] **Step 4: Run tests and verify GREEN**

Run: `dotnet run --project ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj -c Release`

Expected: 所有 helper 测试通过。

### Task 2: 实现 MelonLoader/Harmony Mod

**Files:**
- Create: `ScavengingTweaks/Mod.cs`
- Create: `ScavengingTweaks/ScavengingTweaks.csproj`
- Modify: `Mods/ScavengingTweaks.dll` (build output)

- [x] **Step 1: Add configuration and patch registration**

注册 `MaxAttempts` 与 `HighValueMultiplier`，为每个目标方法安装 Prefix/Postfix，补丁缺失时记录错误。

- [x] **Step 2: Add reset and drop-list hooks**

在 `ResetScavenging`、`PlayerStore.BeginDay` 后写入 `scavengingAttempts`，在 `ExpeditionLocationList.DumpingGrounds` 后只处理一次并复制高价值 ID。

- [x] **Step 3: Build the Mod**

Run: `dotnet build ScavengingTweaks/ScavengingTweaks.csproj -c Release`

Expected: `Mods/ScavengingTweaks.dll` 生成且退出码为 0。

### Task 3: 交付前 QA

**Files:**
- Create: `.helloagents/artifacts/qa-review.json`
- Modify: `.helloagents/sessions/workspace/default/STATE.md`

- [x] **Step 1: Verify assembly references and target methods**

使用 `ilspycmd -l c` 检查输出类型，确认 DLL 引用 `Assembly-CSharp`、`0Harmony` 和 `MelonLoader`。

- [x] **Step 2: Verify installed files**

检查 `Mods/ScavengingTweaks.dll`、源码、测试和配置默认值存在，确认未修改原始游戏程序集。

- [x] **Step 3: Run QA review state writer**

Run: `node C:/Users/fh345/.codex/plugins/cache/local-plugins/helloagents/3.1.9/scripts/qa-review-state.mjs write`

Expected: `.helloagents/artifacts/qa-review.json` 写入本次验证结果。
