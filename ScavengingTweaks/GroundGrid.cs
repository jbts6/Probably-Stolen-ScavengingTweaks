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
        if (OriginalHeightsByRoom.TryGetValue(roomId, out var height))
        {
            return height;
        }

        if (observed > 0)
        {
            OriginalHeightsByRoom[roomId] = observed;
            return observed;
        }

        return -1;
    }
}
