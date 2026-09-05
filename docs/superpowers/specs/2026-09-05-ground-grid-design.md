# 拾荒地面网格放大设计

## 目标

进入拾荒（Dumping Grounds）界面时，把地面战利品网格（Ground）高度放大到配置倍数，使一次拾荒能堆放更多物品。当前地面网格尺寸固定，拾荒次数提升后空间成为实际瓶颈。家中地板、探险途中普通房间不受影响；`MaxAttempts` 等现有逻辑不变。

## 方案

沿用现有 `ScavengingTweaks.dll`，新增双路径放大，互为保险：

- **路径 1（设置路径）**：进入拾荒界面时把游戏设置 `UISettings.current.floorLootGridHeight` 改为目标值。若游戏每次创建地面网格时读取该设置，即自动生效且窗口尺寸跟随。
- **路径 2（兜底重塑）**：同一时机直接重塑实际网格。采用 `ContainerUpgrade.dll` 已在同版本游戏验证过的序列：
  1. `ForceRebuild(floorInventory)` 清除渲染缓存（按内存偏移清 `_lastShapeHash` 等字段）；
  2. `SetShape(原宽, 目标高度)`；
  3. 改前后读 `widthPixels/heightPixels` 求像素增量；
  4. `groundLootWindow.ResizePixels(旧宽+Δ宽, 旧高+Δ高)` + `Validate()` 让窗口跟随。

获取链：`GameMaster.current → raidScene.groundLootWindow（窗口跟随用）+ raidManager.currentRoom → floorInventory`。任一环节为 null 则跳过路径 2，不影响路径 1。

## 触发时机

- `MapUIManager.VisitScavenging` Postfix：进入拾荒界面时执行两条路径。
- 现有 `ScavengePrefix`（`ScavengeDumpingGrounds` 前缀）：每次点击拾荒前再确保一次，防止网格被游戏中途重建。

两条路径均幂等：高度已达标就不动。

## 配置

配置分类仍为 `ScavengingTweaks`，新增：

- `GroundGridMultiplier`：float，默认 `3.0`，最小 `1.0`。目标高度 = 原始高度 × 倍率，运行时读取原版高度而非写死，设为 `1.0` 即等于关闭功能。

原始高度确定规则：会话内按 `Room.roomId` 记录首次观察到的 `floorInventory` 高度作为原版值，缓存复用；放大永不以已放大的值为基数重复相乘。

## 代码组织

- `Mod.cs`：注册配置项；`InstallPatches` 增加 `MapUIManager.VisitScavenging` Postfix；`ScavengePrefix` 内追加一次确保调用。
- 新文件 `GroundGrid.cs`：`GroundGridEnlarger` 静态类，包含原始高度缓存、两条路径执行、`ForceRebuild` 偏移清除；纯算法函数 `ComputeTargetHeight(originalHeight, multiplier)` 与游戏类型解耦，供离线测试。

## 失败处理与兼容性

- 所有 Il2Cpp 访问套 try/catch 并记录日志（含原始与目标网格尺寸、生效路径），失败不影响游戏。
- `ForceRebuild` 的内存偏移（360/440/448/452）为版本相关；失效表现仅为格子不刷新，不会崩溃，日志可定位，届时对照新版本偏移。
- `SetShape` 生成矩形网格；若原地面为不规则形状（可能性低），日志打印原始 width/height 供排查。
- 地面网格每次进场景由游戏重建，放大无需存档持久化，会话内重放即可。
- 不修改原始 DLL、资源包或存档。

## 验收

1. 进入拾荒界面，日志显示原始高度 → 目标高度被应用，并记录哪条路径生效。
2. 地面网格可容纳物品数量约为原来的 3 倍（可调），满地不再是拾荒中断的原因。
3. `groundLootWindow` 窗口完整显示放大后的网格，无显示不全或格子不刷新。
4. 家中地板与探险途中普通房间的地面尺寸不变。
5. 离线测试通过：`ComputeTargetHeight` 覆盖 3×放大、倍率 1（不变）、原始高度 0/负（返回 -1 表示不放大）。
