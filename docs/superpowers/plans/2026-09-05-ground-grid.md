# 拾荒地面网格放大 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 进入拾荒界面时把地面战利品网格高度放大到配置倍数（默认 3 倍），解除地面空间对拾荒收获量的限制。

**Architecture:** 双路径放大互为保险：路径 1 改 `UISettings.current.floorLootGridHeight`（游戏自建网格时读取）；路径 2 参照 ContainerUpgrade 的验证序列直接重塑 `Room.floorInventory`（ForceRebuild 清缓存 → SetShape → ResizePixels 让 groundLootWindow 跟随）。纯算法（目标高度计算、原始高度会话缓存）放无游戏依赖的 `GroundGrid.cs`，进离线测试；游戏访问代码进 `Mod.cs`，跟随现有 Patches 模式。

**Tech Stack:** .NET 6 类库（MelonLoader mod）、HarmonyLib、Il2Cpp 互操作（Assembly-CSharp 代理）、net8.0 控制台离线测试。

**规格：** `docs/superpowers/specs/2026-09-05-ground-grid-design.md`

## Global Constraints

- 工作目录：`C:\Program Files\Steam\steamapps\common\Probably Stolen Playtest`（下称游戏根）。
- 配置分类固定为 `ScavengingTweaks`；新配置项 `GroundGridMultiplier`（float，默认 `3.0`，下限 `1.0`，`1.0` 等于关闭）。
- 游戏类型在 `Il2Cpp` 命名空间（Mod.cs 已有 `using Il2Cpp;`）。
- 所有 Il2Cpp 访问必须 try/catch + MelonLogger 日志，失败不得影响游戏。
- 不修改游戏原始 DLL、资源包或存档。
- 构建产物自动输出到 `Mods\ScavengingTweaks.dll`（csproj `OutputPath` 已配置）。
- 工作区存在他人未提交的修改（`Mod.cs`、`LootWeighting.cs`、`LootWeightingTests.cs`、`SCAVENGING_TWEAKS_HANDOFF.md`）；Task 1 先整体快照提交，保证后续功能提交干净。
- 测试项目 `ScavengingTweaks.Tests` 只编译纯逻辑文件（通过 csproj `<Compile Include>` Link），游戏依赖代码不得放进被 Link 的文件。

---

### Task 1: 纯逻辑 GroundGrid.cs（TDD）+ 工作区快照

**Files:**
- Create: `ScavengingTweaks/GroundGrid.cs`
- Modify: `ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj`
- Modify: `ScavengingTweaks.Tests/LootWeightingTests.cs`（文件虽名为 LootWeighting，实际承载整个 mod 的离线测试，沿用）

**Interfaces:**
- Consumes: 无（纯逻辑，零游戏依赖）
- Produces:
  - `ScavengingTweaks.GroundGridMath.ComputeTargetHeight(int originalHeight, double multiplier) -> int`：`originalHeight <= 0` 或 `multiplier <= 1.0` 返回 `-1`（不放大）；否则返回 `(int)Math.Ceiling(originalHeight * multiplier)`，若不大于原高也返回 `-1`。
  - `ScavengingTweaks.GroundGridState.GetOriginalSettingHeight(int observed) -> int`：会话级缓存 `UISettings.floorLootGridHeight` 首次观察到的正值；未记录或观察到非正值返回 `-1`。
  - `ScavengingTweaks.GroundGridState.GetOriginalRoomHeight(int roomId, int observed) -> int`：按 `roomId` 缓存首次观察到的正值；未记录返回 `-1`。

- [ ] **Step 1: 快照工作区未提交修改**

```bash
cd "C:\Program Files\Steam\steamapps\common\Probably Stolen Playtest"
git status --short
git add -A
git commit -m "wip: snapshot pending diagnostic changes before ground-grid work"
```

Expected: 提交成功，`git status` 干净。

- [ ] **Step 2: 测试项目加入 GroundGrid.cs 链接**

修改 `ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj` 的 `<ItemGroup>`（在现有 LootWeighting.cs 那行之后插入）：

```xml
    <Compile Include="..\ScavengingTweaks\GroundGrid.cs" Link="GroundGrid.cs" />
```

- [ ] **Step 3: 写失败测试**

在 `ScavengingTweaks.Tests/LootWeightingTests.cs` 的 `Main` 方法 try 块内、`Console.WriteLine("LootWeighting tests passed.")` 之前加入调用：

```csharp
            GroundGridTargetHeightTripledByDefault();
            GroundGridMultiplierOneMeansOff();
            GroundGridInvalidOriginalRejected();
            GroundGridFractionalMultiplierRoundsUp();
            GroundGridSettingHeightCachedOnFirstObservation();
            GroundGridRoomHeightCachedPerRoom();
```

在类末尾（`Ensure` 方法之后）加入测试方法：

```csharp
    private static void GroundGridTargetHeightTripledByDefault()
    {
        Ensure(GroundGridMath.ComputeTargetHeight(3, 3.0) == 9, "3 rows at 3x should become 9");
    }

    private static void GroundGridMultiplierOneMeansOff()
    {
        Ensure(GroundGridMath.ComputeTargetHeight(3, 1.0) == -1, "multiplier 1.0 should disable enlargement");
        Ensure(GroundGridMath.ComputeTargetHeight(3, 0.5) == -1, "multiplier below 1.0 should disable enlargement");
    }

    private static void GroundGridInvalidOriginalRejected()
    {
        Ensure(GroundGridMath.ComputeTargetHeight(0, 3.0) == -1, "zero original height should be rejected");
        Ensure(GroundGridMath.ComputeTargetHeight(-2, 3.0) == -1, "negative original height should be rejected");
    }

    private static void GroundGridFractionalMultiplierRoundsUp()
    {
        Ensure(GroundGridMath.ComputeTargetHeight(3, 2.1) == 7, "fractional target should round up to keep capacity");
    }

    private static void GroundGridSettingHeightCachedOnFirstObservation()
    {
        Ensure(GroundGridState.GetOriginalSettingHeight(3) == 3, "first positive observation should be recorded");
        Ensure(GroundGridState.GetOriginalSettingHeight(9) == 3, "later enlarged observation must not overwrite the original");
        Ensure(GroundGridState.GetOriginalSettingHeight(0) == 3, "non-positive observation must not clear the cache");
    }

    private static void GroundGridRoomHeightCachedPerRoom()
    {
        Ensure(GroundGridState.GetOriginalRoomHeight(7001, 4) == 4, "room cache should record the first positive height");
        Ensure(GroundGridState.GetOriginalRoomHeight(7001, 12) == 4, "room cache must return the original on later calls");
        Ensure(GroundGridState.GetOriginalRoomHeight(7002, 6) == 6, "different rooms should cache independently");
        Ensure(GroundGridState.GetOriginalRoomHeight(7003, 0) == -1, "unrecorded room with non-positive height should return -1");
    }
```

注意：`GroundGridState` 的静态缓存在整个测试进程存活，room 7001/7002/7003 为测试专用 ID；`GetOriginalSettingHeight` 只能依赖首次调用，测试断言顺序即文档行为。

- [ ] **Step 4: 运行测试确认失败**

```bash
dotnet run --project ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj --no-restore
```

Expected: 编译错误 `CS0103: The name 'GroundGridMath' does not exist`（GroundGrid.cs 尚未创建）。

- [ ] **Step 5: 实现 GroundGrid.cs**

创建 `ScavengingTweaks/GroundGrid.cs`：

```csharp
using System;
using System.Collections.Generic;

namespace ScavengingTweaks;

// 纯逻辑，无游戏类型依赖；由 ScavengingTweaks.Tests 直接编译。
public static class GroundGridMath
{
    public static int ComputeTargetHeight(int originalHeight, double multiplier)
    {
        if (originalHeight <= 0 || multiplier <= 1.0)
        {
            return -1;
        }

        var target = (int)Math.Ceiling(originalHeight * multiplier);
        return target > originalHeight ? target : -1;
    }
}

public static class GroundGridState
{
    private static readonly Dictionary<int, int> OriginalHeightsByRoom = new();
    private static int originalSettingHeight = -1;

    public static int GetOriginalSettingHeight(int observed)
    {
        if (originalSettingHeight < 0 && observed > 0)
        {
            originalSettingHeight = observed;
        }

        return originalSettingHeight;
    }

    public static int GetOriginalRoomHeight(int roomId, int observed)
    {
        if (!OriginalHeightsByRoom.TryGetValue(roomId, out var height) && observed > 0)
        {
            height = observed;
            OriginalHeightsByRoom[roomId] = height;
        }

        return height;
    }
}
```

- [ ] **Step 6: 运行测试确认通过**

```bash
dotnet run --project ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj --no-restore
```

Expected: 输出 `LootWeighting tests passed.`，退出码 0。

- [ ] **Step 7: 提交**

```bash
git add ScavengingTweaks/GroundGrid.cs ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj ScavengingTweaks.Tests/LootWeightingTests.cs
git commit -m "feat: add ground grid target-height math with session cache"
```

---

### Task 2: Mod.cs 接线（配置 + 双路径放大 + 补丁）

**Files:**
- Modify: `ScavengingTweaks/Mod.cs`（配置区 ~L20-33、`OnInitializeMelon` ~L38-100、`InstallPatches` ~L281-301、`Patches.ScavengePrefix` ~L426-438、文件尾部辅助区）

**Interfaces:**
- Consumes: Task 1 的 `GroundGridMath.ComputeTargetHeight(int, double)`、`GroundGridState.GetOriginalSettingHeight(int)`、`GroundGridState.GetOriginalRoomHeight(int, int)`；游戏类型 `Il2Cpp.UISettings`、`Il2Cpp.GameMaster`、`Il2Cpp.RaidManager`、`Il2Cpp.Room`、`Il2Cpp.GameGridInventory`、`Il2Cpp.GridShape`、`Il2Cpp.PixelWindow`、`Il2Cpp.MapUIManager`。
- Produces: `Mod.EnsureGroundGridEnlarged()`（private static，供 `Patches` 嵌套类两处调用）；配置项 `groundGridMultiplier`。

已确认的游戏 API（Il2Cpp 代理，签名逐一核实过）：
- `UISettings.current` (static)、`UISettings.floorLootGridHeight` (int get/set)
- `GameMaster.current` (static)、`GameMaster.raidScene -> RaidScene`、`GameMaster.raidManager -> RaidManager`
- `RaidScene.groundLootWindow -> PixelWindow`
- `RaidManager.currentRoom -> Room`、`Room.roomId -> int`、`Room.floorInventory -> GameGridInventory`
- `GameGridInventory.inventoryShape -> GridShape`、`GridShape.width/height -> int`、`GameGridInventory.SetShape(int width, int height)`、`GameGridInventory.widthPixels/heightPixels -> int`
- `PixelWindow.widthPixels/heightPixels -> int`、`PixelWindow.ResizePixels(int width, int height)`、`PixelWindow.Validate()`
- `MapUIManager.VisitScavenging()` (public void)

- [ ] **Step 1: 注册配置项**

`Mod.cs` 文件头 using 区加入：

```csharp
using System.Runtime.InteropServices;
```

字段声明区（`itemTypeMultipliers` 声明之后）加入：

```csharp
    private static MelonPreferences_Entry<float> groundGridMultiplier = null!;
```

`OnInitializeMelon` 中 `itemTypeMultipliers = category.CreateEntry<string>(...)` 之后加入：

```csharp
        groundGridMultiplier = category.CreateEntry<float>(
            "GroundGridMultiplier",
            3.0f,
            "Ground grid height multiplier",
            "Multiplier applied to the dumping-grounds ground loot grid height. 1.0 keeps the vanilla size.",
            false,
            false,
            null);
```

属性区（`HighValueMultiplier` 属性之后）加入：

```csharp
    private static double GroundGridMultiplier => Math.Max(1.0, groundGridMultiplier.Value);
```

- [ ] **Step 2: 实现双路径放大与 ForceRebuild**

`Mod` 类内（`ParseItemTypeMultipliers` 方法之后）加入：

```csharp
    private static void EnsureGroundGridEnlarged()
    {
        try
        {
            var settings = UISettings.current;
            if (settings != null)
            {
                var originalSetting = GroundGridState.GetOriginalSettingHeight(settings.floorLootGridHeight);
                var settingTarget = GroundGridMath.ComputeTargetHeight(originalSetting, GroundGridMultiplier);
                if (settingTarget > 0)
                {
                    settings.floorLootGridHeight = settingTarget;
                }
            }

            var master = GameMaster.current;
            if (master == null)
            {
                return;
            }

            var raidManager = master.raidManager;
            var room = raidManager?.currentRoom;
            var floor = room?.floorInventory;
            var shape = floor?.inventoryShape;
            if (room == null || floor == null || shape == null)
            {
                return;
            }

            var originalRoomHeight = GroundGridState.GetOriginalRoomHeight(room.roomId, shape.height);
            var target = GroundGridMath.ComputeTargetHeight(originalRoomHeight, GroundGridMultiplier);
            if (target < 0 || shape.height >= target)
            {
                return;
            }

            var width = shape.width;
            var window = master.raidScene?.groundLootWindow;
            var windowWidth = window?.widthPixels ?? 0;
            var windowHeight = window?.heightPixels ?? 0;
            var gridWidthBefore = floor.widthPixels;
            var gridHeightBefore = floor.heightPixels;

            ForceRebuildGrid(floor);
            floor.SetShape(width, target);

            var widthDelta = floor.widthPixels - gridWidthBefore;
            var heightDelta = floor.heightPixels - gridHeightBefore;
            if (window != null && (widthDelta != 0 || heightDelta != 0))
            {
                window.ResizePixels(windowWidth + widthDelta, windowHeight + heightDelta);
                window.Validate();
            }

            MelonLogger.Msg(
                "ScavengingTweaks ground grid enlarged: room={0} {1}x{2} -> {1}x{3} (window {4}x{5} -> {6}x{7}).",
                room.roomId,
                width,
                shape.height,
                target,
                windowWidth,
                windowHeight,
                windowWidth + widthDelta,
                windowHeight + heightDelta);
        }
        catch (Exception exception)
        {
            MelonLogger.Error("ScavengingTweaks could not enlarge the ground grid.", exception);
        }
    }

    // 偏移取自 ContainerUpgrade.dll 在当前游戏版本的验证实现（_lastShapeHash 等渲染缓存字段）。
    // 版本升级后失效的表现只是网格不刷新，不会崩溃。
    private static void ForceRebuildGrid(GameGridInventory grid)
    {
        var pointer = grid.Pointer;
        Marshal.WriteByte(pointer, 360, 1);
        Marshal.WriteByte(pointer, 452, 0);
        Marshal.WriteInt64(pointer, 440, -1L);
        Marshal.WriteInt32(pointer, 448, 0);
    }
```

- [ ] **Step 3: 安装补丁并接入两个触发点**

`InstallPatches` 中 `Patch(harmony, typeof(ScavHelper), "ScavengeDumpingGrounds", ...)` 一行之后加入：

```csharp
        Patch(harmony, typeof(MapUIManager), "VisitScavenging", postfix: nameof(Patches.VisitScavengingPostfix));
```

`Patches` 类中 `ScavengePrefix` 方法体开头（`activeScavengeAttempt = ...` 之前）加入：

```csharp
            EnsureGroundGridEnlarged();
```

`Patches` 类中新增方法（`ScavengePostfix` 之后）：

```csharp
        public static void VisitScavengingPostfix()
        {
            EnsureGroundGridEnlarged();
        }
```

- [ ] **Step 4: 构建验证**

```bash
dotnet build ScavengingTweaks/ScavengingTweaks.csproj --no-restore
```

Expected: `0 warnings, 0 errors`（与既有基线一致）。构建产物已写入 `Mods/ScavengingTweaks.dll`。

- [ ] **Step 5: 回归离线测试**

```bash
dotnet run --project ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj --no-restore
```

Expected: `LootWeighting tests passed.`

- [ ] **Step 6: 提交**

```bash
git add ScavengingTweaks/Mod.cs
git commit -m "feat: enlarge dumping-grounds ground grid via settings and reshape paths"
```

---

### Task 3: 游戏内验收与交接记录

**Files:**
- Modify: `SCAVENGING_TWEAKS_HANDOFF.md`（验收结论追加到文件顶部状态区）

**Interfaces:**
- Consumes: Task 2 构建出的 `Mods/ScavengingTweaks.dll`
- Produces: 验收结论（规格 5 条验收的核对结果）

- [ ] **Step 1: 确认部署**

```bash
ls -l Mods/ScavengingTweaks.dll
```

Expected: 修改时间为 Task 2 构建时间。

- [ ] **Step 2: 游戏内验证**

通过 Steam 启动游戏，进入拾荒界面后查看 `MelonLoader/Latest.log`，核对规格验收 1-4：

1. 日志出现 `ground grid enlarged: room=... HxW -> ...x目标高度`（记录了生效路径与原始/目标尺寸）。
2. 地面可堆放物品数约为原来的 3 倍；地面堆满后仍会正常提示空间不足而不是丢失物品。
3. `groundLootWindow` 完整显示放大后的网格（无显示不全/格子不刷新；若格子不刷新，是 ForceRebuild 偏移失效，记录日志回炉）。
4. 家中地板、探险途中普通房间地面尺寸不变（打开对应界面目测）。

任何一条不满足：把 `Latest.log` 中 `ScavengingTweaks` 相关行与异常栈带回，按日志修路径再验。

- [ ] **Step 3: 更新交接文档并提交**

在 `SCAVENGING_TWEAKS_HANDOFF.md` 顶部状态区追加小节「地面网格放大（2026-09-05）」，记录：配置项名与默认值、两条路径哪条生效、实测原始→目标高度、遗留问题（如有）。

```bash
git add SCAVENGING_TWEAKS_HANDOFF.md
git commit -m "docs: record ground-grid verification results in handoff"
```

---

## 自审记录

- **规格覆盖**：设置路径（Task 2 Step 2 settings 段）、兜底重塑含 ForceRebuild/ResizePixels（Task 2 Step 2）、两个触发时机（Task 2 Step 3）、GroundGridMultiplier 配置（Task 2 Step 1）、原始高度会话缓存按 roomId（Task 1）、ComputeTargetHeight 语义含 0/负/倍率 1（Task 1 测试）、验收 5 条（Task 3）——无遗漏。
- **占位符扫描**：无 TBD/TODO；所有代码步骤含完整代码。
- **类型一致性**：`ComputeTargetHeight(int, double)` 在 Task 1 定义、Task 2 以 `GroundGridMultiplier`（double）调用；`GetOriginalSettingHeight`/`GetOriginalRoomHeight` 名称两处一致；`EnsureGroundGridEnlarged` 在 Step 2 定义、Step 3 两处调用。
