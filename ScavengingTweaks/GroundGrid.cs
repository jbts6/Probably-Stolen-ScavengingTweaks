using System;
using System.Collections.Generic;

namespace ScavengingTweaks;

// 纯逻辑，无游戏类型依赖；由 ScavengingTweaks.Tests 直接编译。
public static class GroundGridMath
{
    /// <summary>
    /// 目标尺寸 = 原始尺寸 + 新增格数。-1 表示不调整（新增格数 &lt;= 0 或原始尺寸非法）。
    /// </summary>
    public static int ComputeTargetDimension(int original, int extra)
    {
        if (original <= 0 || extra <= 0)
        {
            return -1;
        }

        return original + extra;
    }
}

public static class GroundGridState
{
    private static readonly Dictionary<string, int> OriginalHeightsByKey = new();
    private static int originalSettingHeight = -1;

    public static int GetOriginalSettingHeight(int observed)
    {
        if (originalSettingHeight < 0 && observed > 0)
        {
            originalSettingHeight = observed;
        }

        return originalSettingHeight;
    }

    public static int GetOriginalHeight(string key, int observed)
    {
        if (OriginalHeightsByKey.TryGetValue(key, out var height))
        {
            return height;
        }

        if (observed > 0)
        {
            OriginalHeightsByKey[key] = observed;
            return observed;
        }

        return -1;
    }
}
