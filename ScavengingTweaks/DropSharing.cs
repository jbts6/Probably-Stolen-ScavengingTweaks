using System;
using System.Collections.Generic;
using System.Linq;

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

    /// <summary>解析逗号分隔的物品 ID 列表：去空白、去重、保序。</summary>
    public static List<string> ParseIdList(string? config)
    {
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(config))
        {
            return ids;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in config.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var id = raw.Trim();
            if (id.Length == 0 || !seen.Add(id))
            {
                continue;
            }

            ids.Add(id);
        }

        return ids;
    }

    /// <summary>按类型挑出最高价值物品（每类型一个；价值 ≤ 0 跳过；平值取先见者，输出按类型名排序保证稳定）。</summary>
    public static List<string> PickPerTypeTopItems(IReadOnlyList<(string Id, IReadOnlyList<string> Types, long Value)> items)
    {
        var best = new Dictionary<string, (long Value, string Id)>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || item.Value <= 0 || item.Types == null)
            {
                continue;
            }

            foreach (var type in item.Types)
            {
                if (string.IsNullOrWhiteSpace(type))
                {
                    continue;
                }

                if (!best.TryGetValue(type, out var current) || item.Value > current.Value)
                {
                    best[type] = (item.Value, item.Id);
                }
            }
        }

        return best.Keys
            .OrderBy(k => k, StringComparer.Ordinal)
            .Select(k => best[k].Id)
            .ToList();
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
