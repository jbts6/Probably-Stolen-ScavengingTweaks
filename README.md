# Probably Stolen Scavenging Tweaks

面向 Probably Stolen Playtest 的 MelonLoader/Harmony Mod，调整垃圾场拾荒行为：

- 轻伤和重伤概率固定为 `0%`。
- 每日拾荒次数可配置，默认 `10` 次。
- 高价值掉落倍率可配置，默认 `2.0x`。
- 提供独立的 `InventorySorterFix` 兼容补丁，阻止原整理器退出时写入含 `U+0000` 的偏好，并默认使用密集布局。

## 配置

首次启动 Mod 后，MelonLoader 会在 `UserData/MelonPreferences.cfg` 写入：

```toml
[ScavengingTweaks]
MaxAttempts = 10
HighValueMultiplier = 2.0

[InventorySorterFix]
ForceDenseLayout = true
```

`HighValueMultiplier` 只作用于垃圾场掉落表中按基础价值排序后最高的一半物品。`2.0` 会为这些条目各追加一份；`3.0` 会各追加两份。倍率小于或等于 `1.0` 时保持原始权重。

`ForceDenseLayout = true` 时，兼容补丁只在内存中关闭 InventorySorter 的 `GroupByTag`，让原 Mod 使用更紧凑的 `LayoutDense` 路径；设为 `false` 可恢复标签分组。补丁只拦截 `InventorySorter.Core` 发起的全局偏好保存，其他 Mod 的保存仍会执行。

## 目录

- `ScavengingTweaks/`：Mod 源码和构建项目。
- `ScavengingTweaks.Tests/`：不依赖游戏程序集的离线测试。
- `InventorySorterFix/`：InventorySorter 兼容补丁源码和构建项目。
- `InventorySorterFix.Tests/`：兼容补丁的离线纯逻辑测试。
- `docs/`：设计与实现记录。

## 构建

需要 .NET 6 SDK，以及本地游戏目录中的 MelonLoader 和 IL2CPP 程序集。`GameRoot` 指向游戏根目录：

```powershell
dotnet run --project ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj -c Release
dotnet run --project InventorySorterFix.Tests/InventorySorterFix.Tests.csproj -c Release
$GAME_ROOT = "<游戏根目录>"
dotnet build ScavengingTweaks/ScavengingTweaks.csproj -c Release -p:GameRoot="$GAME_ROOT"
dotnet build InventorySorterFix/InventorySorterFix.csproj -c Release -p:GameRoot="$GAME_ROOT"
```

构建得到的 `Mods/ScavengingTweaks.dll` 和 `Mods/InventorySorterFix.dll` 复制到游戏的 `Mods/` 目录后，MelonLoader 会在启动时加载。保留原始 `Mods/InventorySorter.dll`，安装顺序为原整理器、`ScavengingTweaks`、`InventorySorterFix`；不要用补丁 DLL 替换原 Mod。

InventorySorterFix 会在配置确实需要清理时先保留一次 `UserData/MelonPreferences.cfg.inventory-sorter-fix.bak`，然后删除 `U+0000`，只去掉由损坏键名产生的重复“物品整理”TOML 表，并以无 BOM UTF-8 写回；其他 Mod 的表保持原样。它通过文件保存后的监视器覆盖其他 Mod 的最后一次全局保存，但不会阻止这些 Mod 保存自己的设置。它不会保存 InventorySorter 的窗口拖动位置；如果用户拖动原生窗口，位置仍由原 Mod 自己处理。

## 兼容性

项目针对当前 Probably Stolen Playtest Beta 构建验证。游戏更新后，`Assembly-CSharp.dll` 的类型或方法签名变化可能需要重新调整补丁目标。
