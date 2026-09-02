# InventorySorter 兼容补丁实现报告

## 实现

- 新增 `InventorySorterFix` MelonLoader/Harmony Mod，不替换原始 `InventorySorter.dll`。
- `ForceDenseLayout` 默认开启，初始化时将原整理器的 `GroupByTag` 设为 `false`，使用原有 `LayoutDense` 紧凑布局；关闭配置即可恢复标签分组。
- 对 `InventorySorter.Core` 的保存调用增加 Harmony 前缀和转译器兜底，只跳过该 Mod 自己的全局偏好保存。
- 对 `MelonLoader.Preferences.IO.File.Save()` 增加保存后修复，并增加配置文件监视器，覆盖其他 Mod 或 MelonLoader 在退出阶段的最后一次保存。
- 初始化转译器先清洗原整理器的字符串常量，从源头避免继续注册带 NUL 的键名。
- 修复器删除 `U+0000`，并且只删除由损坏键名产生的重复“物品整理”TOML 表；其他 Mod 的普通表和数组表保持原样，首次修改前只创建一次备份。

## 验证

- `dotnet run --project InventorySorterFix.Tests/InventorySorterFix.Tests.csproj -c Release`：通过。
- `dotnet run --project ScavengingTweaks.Tests/ScavengingTweaks.Tests.csproj -c Release`：通过。
- 游戏目录 `InventorySorterFix.csproj` Release 构建：0 警告，0 错误。
- 公共仓库 `InventorySorterFix.csproj` Release 构建：0 警告，0 错误。
- 运行日志确认 `InventorySorterFix.dll` 已加载、`post-save preference repair` 已挂载、`GroupByTag` 已禁用。
- 正常关闭游戏后，`UserData/MelonPreferences.cfg` 检查结果：`NUL=0`，`物品整理` 表数量为 1；日志出现 `file-watch repair`。
- 定向修复测试覆盖：重复“物品整理”表被移除，其后的 `[[array-table]]` 和其他 Mod 重复表保持不变。

## 已知限制

- 当前游戏的 `MelonPreferences.Save()` 本身无法被 Harmony 直接 detour，日志会显示 fallback 警告；最终 `File.Save()` postfix 和文件监视器仍可用。
- 原 InventorySorter 的字符串常量含有嵌入 NUL，补丁不会持久化它的窗口拖动位置；其他 Mod 的配置保存不会被阻止。
- Unity IL2CPP 反射扫描产生的 `ReflectionTypeLoadException` 警告来自其他组件，与本补丁加载无关。
