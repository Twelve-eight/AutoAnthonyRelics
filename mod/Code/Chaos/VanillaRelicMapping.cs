using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoAnthonyRelics.Chaos;

/// <summary>
/// Mapping of our chaos templates to vanilla relics with the same effect
/// (user order 2026-09-11: the point editor shows "[effect text]([vanilla
/// relic 1], [vanilla relic 2], ...)"; hovering a vanilla relic name shows
/// its full description, its rarity, and what generating that same effect
/// would cost in OUR point pricing).
///
/// All data byte-verified against the game (loc zhs dump 2026-09-11 +
/// ilspy decompile of RelicModel subclasses):
///   values: Anchor BlockVar(10), Vajra PowerVar(Strength,1),
///   BronzeScales Thorns(3), BagOfPreparation Cards(2),
///   OddlySmoothStone Dexterity(1), Orichalcum Block(6, end-of-turn-if-none),
///   Kunai Dexterity(1) per 3 attacks, Shuriken Strength(1) per 3 attacks,
///   PhilosophersStone Energy(1)/turn + enemies Strength(1),
///   BlessedAntler Energy(1)/turn + 3 Dazed, Sai Block(7)/turn,
///   EmberTea Strength(2) for next 2 combats, SwordOfJade Strength(3),
///   Fiddle Cards(2)/turn + no mid-turn draws, SneckoEye Cards(2)/turn +
///   Confused, Lantern Energy(1) first turn, Girya (rest-site strength).
/// Value baseline for pricing (user order 2026-09-11): potions - Strength
/// Potion = 2 Strength, Dexterity Potion = 2 Dexterity (both Common);
/// Artifact has NO vanilla potion - CoreSurge (Spire1 mod) pairs 1 Artifact
/// with 11 damage, and enemies apply 1-3 to themselves: Artifact is the
/// most premium stat per point.
/// </summary>
public static class VanillaRelicMapping
{
    /// <summary>One vanilla relic reference for a template.</summary>
    public sealed record VanillaRef(
        string RelicId,        // engine relic id, e.g. "VAJRA"
        string DisplayName,    // zhs name, e.g. "金刚杵"
        string Description,    // full zhs description (hover text)
        string Rarity,         // zhs rarity label: 普通/罕见/稀有/远古/事件
        string EffectNote);    // its numeric value, e.g. "每场战斗开始时+1力量"

    private static readonly Dictionary<string, VanillaRef[]> Map = new(StringComparer.Ordinal)
    {
        // ---- Combat-start one-shots ----
        [ChaosRelicCatalog.StartBlock] = new[]
        {
            V("ANCHOR", "锚", "每场战斗开始时获得10点格挡.", "普通", "战斗开始+10格挡"),
        },
        [ChaosRelicCatalog.StartStrength] = new[]
        {
            V("VAJRA", "金刚杵", "在每场战斗开始时,获得1点力量.", "普通", "战斗开始+1力量"),
            V("SWORD_OF_JADE", "玉之剑", "在每场战斗开始时,获得3点力量.", "事件", "战斗开始+3力量"),
            V("EMBER_TEA", "余烬茶", "在接下来的2场战斗开始时,获得2点力量.", "事件", "前2场战斗+2力量"),
        },
        [ChaosRelicCatalog.StartDexterity] = new[]
        {
            V("ODDLY_SMOOTH_STONE", "意外光滑的石头", "在每场战斗开始时,获得1点敏捷.", "普通", "战斗开始+1敏捷"),
            V("BELT_BUCKLE", "皮带扣", "当你没有药水时,你额外拥有2点敏捷.", "普通", "无药水时+2敏捷"),
        },
        [ChaosRelicCatalog.StartDraw] = new[]
        {
            V("BAG_OF_PREPARATION", "准备背包", "在每场战斗开始时,额外抽2张牌.", "普通", "战斗开始多抽2张"),
            V("RING_OF_THE_SNAKE", "蛇之戒", "在每场战斗开始时,额外抽2张牌.", "普通", "战斗开始多抽2张"),
        },
        [ChaosRelicCatalog.StartThorns] = new[]
        {
            V("BRONZE_SCALES", "铜质鳞片", "在每场战斗开始时,获得3点荆棘.", "普通", "战斗开始+3荆棘"),
        },
        // ---- Per-turn ----
        [ChaosRelicCatalog.TurnStartBlock] = new[]
        {
            V("SAI", "钗", "在你的回合开始时,获得7点格挡.", "远古", "每回合+7格挡"),
            V("ORICHALCUM", "山铜", "如果你在回合结束时没有任何格挡,获得6点格挡.", "罕见", "回合末无格挡则+6"),
        },
        [ChaosRelicCatalog.TurnStartEnergy] = new[]
        {
            V("PHILOSOPHERS_STONE", "贤者之石", "在每回合开始时获得1点能量.所有敌人初始获得1点力量.", "远古", "每回合+1能量(敌人+1力量)"),
            V("BLESSED_ANTLER", "祝福鹿角", "在每回合开始时获得1点能量.在战斗开始时,将3张晕眩放入你的抽牌堆.", "远古", "每回合+1能量(战斗开始3晕眩)"),
        },
        [ChaosRelicCatalog.TurnStartDraw] = new[]
        {
            V("SNECKO_EYE", "异蛇之眼", "每回合多抽2张牌.每场战斗开始时获得混乱效果.", "远古", "每回合多抽2张(获得混乱)"),
            V("FIDDLE", "小提琴", "在每个回合开始时,额外抽2张牌.你在回合进行中不再能抽任何牌.", "远古", "每回合多抽2张(回合中不能抽牌)"),
        },
        // ---- Card-play triggers ----
        [ChaosRelicCatalog.PlayBlock] = new[]
        {
            V("DAUGHTER_OF_THE_WIND", "风之女", "每当你打出一张攻击牌时,获得3点格挡.", "罕见", "每打攻击牌+3格挡"),
            V("TUNING_FORK", "音叉", "你每打出3张技能牌,获得4点格挡.", "罕见", "每3技能牌+4格挡"),
        },
        // ---- Passives ----
        [ChaosRelicCatalog.PassiveMaxEnergy] = new[]
        {
            V("LANTERN", "灯笼", "在你的第一回合开始时,获得1点能量.", "普通", "第1回合+1能量(一次性)"),
        },
        [ChaosRelicCatalog.StartArtifact] = Array.Empty<VanillaRef>(), // no vanilla relic grants Artifact; CoreSurge (Spire1) = 1 Artifact + 11 dmg
        [ChaosRelicCatalog.StartVulnAll] = Array.Empty<VanillaRef>(),  // vanilla has no combat-start-apply-debuff relic
        [ChaosRelicCatalog.StartWeakAll] = Array.Empty<VanillaRef>(),
        [ChaosRelicCatalog.StartRegen] = Array.Empty<VanillaRef>(),
        [ChaosRelicCatalog.StartPoisonAll] = Array.Empty<VanillaRef>(),
        [ChaosRelicCatalog.StartPlating] = Array.Empty<VanillaRef>(),
        [ChaosRelicCatalog.StartDamageAll] = Array.Empty<VanillaRef>(),
        [ChaosRelicCatalog.StartEnergy] = new[]
        {
            V("BOOMING_CONCH", "轰鸣海螺", "在精英战的战斗开始时,额外抽2张牌并获得1点能量.", "远古", "精英战开始+1能量+2抽"),
        },
    };

    private static VanillaRef V(string id, string name, string desc, string rarity, string note) =>
        new(id, name, desc, rarity, note);

    /// <summary>Vanilla relics with the same effect as our template (may be empty).</summary>
    public static IReadOnlyList<VanillaRef> For(string template) =>
        Map.TryGetValue(template, out var refs) ? refs : Array.Empty<VanillaRef>();

    /// <summary>
    /// What generating the SAME effect as a vanilla relic costs in our
    /// pricing: for each mapped template, points = CostPerPoint * N where N
    /// matches the vanilla relic's magnitude (approximated for triggers).
    /// Returns null when the vanilla magnitude has no direct N equivalent.
    /// </summary>
    public static int? OurPointsFor(VanillaRef vref)
    {
        // find the template that owns this relic id, then price its amount
        foreach (var (template, refs) in Map)
        {
            if (refs.Any(r => r.RelicId == vref.RelicId))
            {
                var spec = ChaosRelicCatalog.Spec(template);
                int n = MagnitudeOf(vref.RelicId);
                if (n <= 0)
                {
                    return null;
                }
                return spec.CostPerPoint * n;
            }
        }
        return null;
    }

    /// <summary>Vanilla magnitude N for relics mapped to our N-based templates.</summary>
    private static readonly Dictionary<string, int> Magnitudes = new(StringComparer.Ordinal)
    {
        ["ANCHOR"] = 10, ["VAJRA"] = 1, ["SWORD_OF_JADE"] = 3, ["EMBER_TEA"] = 2,
        ["ODDLY_SMOOTH_STONE"] = 1, ["BELT_BUCKLE"] = 2, ["BAG_OF_PREPARATION"] = 2,
        ["RING_OF_THE_SNAKE"] = 2, ["BRONZE_SCALES"] = 3, ["SAI"] = 7,
        ["ORICHALCUM"] = 6, ["PHILOSOPHERS_STONE"] = 1, ["BLESSED_ANTLER"] = 1,
        ["SNECKO_EYE"] = 2, ["FIDDLE"] = 2, ["DAUGHTER_OF_THE_WIND"] = 3,
        ["TUNING_FORK"] = 4, ["LANTERN"] = 1, ["BOOMING_CONCH"] = 1,
    };

    private static int MagnitudeOf(string relicId) =>
        Magnitudes.TryGetValue(relicId, out var n) ? n : 0;
}
