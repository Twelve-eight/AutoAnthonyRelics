using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Relics;

namespace AutoAnthonyRelics.Chaos;

/// <summary>
/// Seeded chaos relic generator: the relic-side mirror of AutoAnthony's
/// RandomCardGenerator. Given a run seed, produces one definition per slot.
///
/// Entry count contract (user order 2026-09-07): each relic gets 3x the
/// entry count the card algorithm would roll. Card algorithm rolls a
/// weighted rank 0-4 (entry count = 1 + rank); we roll the SAME weighted
/// distribution then multiply by the configured multiplier (default 3),
/// clamped to [MinEntries, MaxEntries].
/// </summary>
public static class ChaosRelicGenerator
{
    public const int SlotsPerRarity = 20;
    public const int TotalSlots = SlotsPerRarity * 3; // Common, Uncommon, Rare

    /// <summary>Chaos relic naming: adjective + noun, seeded pick.</summary>
    private static readonly string[] NamePrefix =
    {
        "混沌", "随机", "无序", "狂野", "古怪", "变化", "陌生", "编织", "扭曲", "重组",
        "怪诞", "任意", "失序", "拼凑", "偶发", "跳变", "偶得", "杂乱", "奇想", "拼贴",
    };

    private static readonly string[] NameNoun =
    {
        "齿轮", "棱镜", "沙漏", "怀表", "骰子", "罗盘", "天平", "墨水瓶", "蜡烛", "铃铛",
        "面具", "卷轴", "符文", "水晶", "瓶中船", "鸟笼", "怀炉", "烟斗", "放大镜", "钥匙",
        "棋子", "纽扣", "羽毛", "鳞片", "书签", "墨水", "香炉", "怀剑", "星盘", "念珠",
    };

    public static IReadOnlyList<ChaosRelicDefinition> Generate(string seed, int multiplier)
    {
        Random random = new(StableSeed(seed));
        var definitions = new List<ChaosRelicDefinition>(TotalSlots);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var usedEffectSets = new HashSet<string>(StringComparer.Ordinal);
        for (int slot = 0; slot < TotalSlots; slot++)
        {
            RelicRarity rarity = RarityForSlot(slot);
            ChaosRelicDefinition definition = GenerateOne(random, slot, rarity, multiplier, usedNames, usedEffectSets);
            definitions.Add(definition);
        }
        return definitions;
    }

    internal static ChaosRelicDefinition GenerateOne(Random random, int slot, RelicRarity rarity,
        int multiplier, HashSet<string> usedNames, HashSet<string> usedEffectSets)
    {
        // 4 attempts for a unique name + unique effect-set signature (mirrors
        // AutoAnthony's GenerateCardCandidates recovery attempts).
        for (int attempt = 0; attempt < 4; attempt++)
        {
            bool finalAttempt = attempt == 3;
            int entryCount = RollEntryCount(random, rarity, multiplier, finalAttempt);
            IReadOnlyList<string> templates = PickTemplates(random, rarity, entryCount, finalAttempt);
            var operations = new List<ChaosRelicOperation>(entryCount);
            foreach (string template in templates)
            {
                var spec = ChaosRelicCatalog.Spec(template);
                int amount = spec.Min + random.Next(spec.Max - spec.Min + 1);
                operations.Add(new ChaosRelicOperation(template, amount, spec.Render(amount)));
            }
            string name = GenerateName(random, usedNames);
            string signature = string.Join("|", templates.OrderBy(t => t, StringComparer.Ordinal));
            if (!usedEffectSets.Add(signature) && !finalAttempt)
            {
                continue; // duplicate effect set: retry
            }
            return new ChaosRelicDefinition(slot, rarity, name, operations);
        }
        // finalAttempt path always returns inside the loop.
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>
    /// Roll the card-baseline weighted rank, then apply the 3x multiplier.
    /// On the final attempt the rank distribution is flattened toward higher
    /// counts (mirrors AutoAnthony's AdaptiveEffectCountWindow growth) so
    /// duplicate-avoidance cannot loop on low-entropy pools.
    /// </summary>
    internal static int RollEntryCount(Random random, RelicRarity rarity, int multiplier, bool widen)
    {
        int[] weights = rarity switch
        {
            RelicRarity.Common => ChaosRelicCatalog.CardBaselineCommon,
            RelicRarity.Uncommon => ChaosRelicCatalog.CardBaselineUncommon,
            RelicRarity.Rare => ChaosRelicCatalog.CardBaselineRare,
            _ => ChaosRelicCatalog.CardBaselineCommon,
        };
        if (widen)
        {
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = 10 + weights[i];
            }
        }
        int rank = PickWeighted(random, weights);
        // User order 2026-09-08: 3 entries is the norm; the old 3x-card formula
        // (clamp(3*(1+rank),3,15)) produced 6/9/12/15 relics and read as bloated.
        // New band: clamp(2 + rank, 3, 5) on the same weighted rank - mostly 3,
        // occasionally 4, rarely 5. Multiplier now shifts the band instead of
        // multiplying it, so config values stay meaningful (default 3 -> band 3..5;
        // multiplier 4 -> 4..6; clamped to [MinEntries, MaxEntries]).
        int band = Math.Max(3, Math.Min(multiplier, ChaosRelicCatalog.MaxEntries - 2));
        int entries = Math.Clamp(band - 1 + rank, ChaosRelicCatalog.MinEntries, band + 2);
        return entries;
    }

    /// <summary>
    /// Pick entry templates. v1 policy: at most one copy of each passive
    /// template, at most one copy of TurnStartEnergy and PassiveMaxEnergy
    /// (avoid stacking degenerate relics), everything else freely repeatable.
    /// </summary>
    internal static IReadOnlyList<string> PickTemplates(Random random, RelicRarity rarity, int count, bool finalAttempt)
    {
        var all = ChaosRelicCatalog.AllTemplates;
        var uniqueOnly = new HashSet<string>(StringComparer.Ordinal)
        {
            ChaosRelicCatalog.TurnStartEnergy,
            ChaosRelicCatalog.PassiveMaxEnergy,
        };
        var picked = new List<string>(count);
        // First pass: favor distinct templates for variety.
        var pool = all.OrderBy(_ => random.Next()).ToArray();
        int pi = 0;
        while (picked.Count < count && pi < pool.Length)
        {
            string t = pool[pi++];
            if (uniqueOnly.Contains(t) && picked.Contains(t))
            {
                continue;
            }
            picked.Add(t);
        }
        // Second pass: fill remaining by random re-picks.
        while (picked.Count < count)
        {
            string t = all[random.Next(all.Count)];
            if (uniqueOnly.Contains(t) && picked.Contains(t))
            {
                continue;
            }
            picked.Add(t);
        }
        return picked;
    }

    internal static string GenerateName(Random random, HashSet<string> used)
    {
        for (int i = 0; i < 32; i++)
        {
            string name = NamePrefix[random.Next(NamePrefix.Length)] + NameNoun[random.Next(NameNoun.Length)];
            if (used.Add(name))
            {
                return name;
            }
        }
        // Fallback: numbered variant.
        string baseName = NamePrefix[random.Next(NamePrefix.Length)] + NameNoun[random.Next(NameNoun.Length)];
        int n = 2;
        while (!used.Add(baseName + n))
        {
            n++;
        }
        return baseName + n;
    }

    public static RelicRarity RarityForSlot(int slot)
    {
        int inRarity = slot % SlotsPerRarity;
        return inRarity switch
        {
            < 20 and >= 0 when slot < SlotsPerRarity => RelicRarity.Common,
            _ when slot < SlotsPerRarity * 2 => RelicRarity.Uncommon,
            _ => RelicRarity.Rare,
        };
    }

    /// <summary>AutoAnthony StableSeed mirror: stable string hash for a run seed.</summary>
    internal static int StableSeed(string seed)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in seed)
            {
                hash = hash * 31 + c;
            }
            return hash;
        }
    }

    internal static int PickWeighted(Random random, int[] weights)
    {
        long total = 0;
        foreach (int w in weights)
        {
            total += w;
        }
        long roll = (long)(random.NextDouble() * total);
        for (int i = 0; i < weights.Length; i++)
        {
            roll -= weights[i];
            if (roll < 0)
            {
                return i;
            }
        }
        return weights.Length - 1;
    }
}
