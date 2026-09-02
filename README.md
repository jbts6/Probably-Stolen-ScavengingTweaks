# Probably Stolen Scavenging Tweaks

面向 Probably Stolen Playtest 的 MelonLoader/Harmony Mod，调整垃圾场拾荒行为：

- 轻伤和重伤概率固定为 `0%`。
- 每日拾荒次数可配置，默认 `10` 次。
- 高价值掉落倍率可配置，默认 `2.0x`。

## 配置

首次启动 Mod 后，MelonLoader 会在 `UserData/MelonPreferences.cfg` 写入：

```toml
[ScavengingTweaks]
MaxAttempts = 10
HighValueMultiplier = 2.0
```

`HighValueMultiplier` 只作用于垃圾场掉落表中按基础价值排序后最高的一半物品。`2.0` 会为这些条目各追加一份；`3.0` 会各追加两份。倍率小于或等于 `1.0` 时保持原始权重。

## 目录

- `ScavengingTweaks/`：Mod 源码和构建项目。
- `ScavengingTweaks.Tests/`：不依赖游戏程序集的离线测试。
- `docs/`：设计与实现记录。

## 构建

需要 .NET 6 SDK，以及本地游戏目录中的 MelonLoader 和 IL2CPP 程序集。`GameRoot` 指向游戏根目录：

```powershell
dotnet run --project ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj -c Release
dotnet build ScavengingTweaks/ScavengingTweaks.csproj -c Release -p:GameRoot="C:\Program Files\Steam\steamapps\common\Probably Stolen Playtest"
```

构建得到的 `Mods/ScavengingTweaks.dll` 复制到游戏的 `Mods/` 目录后，MelonLoader 会在启动时加载。

## 兼容性

项目针对当前 Probably Stolen Playtest Beta 构建验证。游戏更新后，`Assembly-CSharp.dll` 的类型或方法签名变化可能需要重新调整补丁目标。
