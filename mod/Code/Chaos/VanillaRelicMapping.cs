using System;
using System.Collections.Generic;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// Mapping of our chaos templates to vanilla relics with the same effect
/// (user order 2026-09-11: the point editor shows "[effect text]([vanilla
/// relic 1], [vanilla relic 2], ...)"; hovering a vanilla relic name shows
/// its full description, its rarity, and what generating that same effect
/// would cost in OUR point pricing).
///
/// Data provenance (2026-09-12 rewrite): engine anchor is release_info.json
/// commit 41cef1ea / v0.111.0, main_assembly_hash 222455745,
/// sts2.dll md5 a91f9e7be785f504205313f3aff55854. Every row was re-verified
/// against BOTH the pck loc table (localization/zhs/relics.json, 991 entries
/// - names and descriptions are the engine's own text, BBCode stripped and
/// {Var} placeholders substituted with the decompiled values) and the
/// decompiled RelicModel subclasses. The full verified table is
/// HANDOFF-2026-09-12.md section 7.1.
///
/// Corrections applied by this rewrite (each was wrong in the old 153-line
/// table):
///   DaughterOfTheWind  Rarity.Event (was 罕见), BlockVar(1m) per attack
///                      (was "每次攻击 3")
///   TuningFork         Uncommon, CardsVar(10) + BlockVar(7m) (was 罕见/3/4)
///   RingOfTheSnake     Starter + CardsVar(2), FIRST TURN ONLY (was 普通)
///   BeltBuckle         Shop + DexterityPower(2m) gated on holding no potion
///                      (was 普通, unconditional)
///   Lantern            Common + EnergyVar(1) gated turn&lt;=1, i.e. a ONE-SHOT
///                      first-turn grant - it was wrongly listed under
///                      PassiveMaxEnergy, which claims a sustained energy-CAP
///                      raise. No vanilla relic raises the energy cap.
///   EmberTea           StrengthPower(2m) with _combatsLeft = 5 (was "前 2 场")
///   PhilosophersStone / BlessedAntler  Ancient + EnergyVar(1) via
///                      ModifyMaxEnergy (rarity was labelled 远古, a rarity
///                      that does not exist in the engine)
///   Names              Orichalcum 奥利哈钢 (was 山铜), BlessedAntler 赐福鹿角
///                      (was 祝福鹿角), DaughterOfTheWind 风的女儿 (was 风之女),
///                      RingOfTheSnake 蛇之戒指 (was 蛇之戒), BeltBuckle 腰带扣
///                      (was 皮带扣)
///
/// Rarity labels are the engine's own zhs gameplay_ui.json strings:
/// 初始遗物 / 普通遗物 / 罕见遗物 / 稀有遗物 / 商店遗物 / 事件遗物 / 先古遗物.
/// The old table used "远古", which does not exist in the engine (the label is
/// 先古遗物).
///
/// Pricing: <see cref="OurPointsFor"/> resolves through
/// <see cref="ChaosTemplates"/> (both pools, not the core catalog) and prices
/// with the LIVE per-point config table (<c>QuriousCraftingRelicsConfig.PointCosts</c>)
/// via <see cref="ChaosTemplates.PriceOf"/> / <see cref="ChaosTemplates.RefundOf"/>,
/// so a Decaying template gets its triangular price and a user-tuned per-point
/// cost is honoured. The old code used <c>spec.CostPerPoint</c> (the catalog
/// default) and <c>ChaosRelicCatalog.Spec</c> (core-only) - both defects were
/// flagged by the F05 verification.
/// </summary>
public static class VanillaRelicMapping
{
    /// <summary>One vanilla relic reference for a template.</summary>
    public sealed record VanillaRef(
        string RelicId,        // engine relic id, e.g. "VAJRA"
        string DisplayName,    // zhs name, e.g. "金刚杵"
        string Description,    // full zhs description (hover text)
        string Rarity,         // engine zhs rarity label, e.g. "普通遗物"
        string EffectNote);    // its numeric value, e.g. "战斗开始+1力量"

    // ---- Engine rarity labels (zhs gameplay_ui.json, v0.111.0) ----
    private const string RarityStarter = "初始遗物";
    private const string RarityCommon = "普通遗物";
    private const string RarityUncommon = "罕见遗物";
    private const string RarityRare = "稀有遗物";
    private const string RarityShop = "商店遗物";
    private const string RarityEvent = "事件遗物";
    private const string RarityAncient = "先古遗物";

    /// <summary>
    /// Rows are capped at 4 relics per template: the editor renders the chips
    /// inline in a non-wrapping HBoxContainer next to the effect text, so a
    /// longer row overflows the panel. Selection prefers unconditional and
    /// sustained effects over conditional or turn-limited ones.
    /// </summary>
    private static readonly Dictionary<string, VanillaRef[]> Map = new(StringComparer.Ordinal)
    {
        // ---- Combat-start one-shots ----
        [ChaosRelicCatalog.StartBlock] = new[]
        {
            V("ANCHOR", "锚", "每场战斗开始时获得10点格挡。", RarityCommon, "战斗开始+10格挡"),
            V("FAKE_ANCHOR", "锚？？？", "在每场战斗开始时，获得4点格挡。", RarityEvent, "战斗开始+4格挡"),
        },
        [ChaosRelicCatalog.StartStrength] = new[]
        {
            V("VAJRA", "金刚杵", "在每场战斗开始时，获得1点力量。", RarityCommon, "战斗开始+1力量"),
            V("SWORD_OF_JADE", "玉之剑", "在每场战斗开始时，获得3点力量。", RarityEvent, "战斗开始+3力量"),
            V("EMBER_TEA", "余烬茶", "在接下来的5场战斗开始时，获得2点力量。", RarityEvent, "前5场战斗+2力量"),
        },
        [ChaosRelicCatalog.StartDexterity] = new[]
        {
            V("ODDLY_SMOOTH_STONE", "意外光滑的石头", "在每场战斗开始时，获得1点敏捷。", RarityCommon, "战斗开始+1敏捷"),
            V("BELT_BUCKLE", "腰带扣", "当你没有药水时，你额外拥有2点敏捷。", RarityShop, "无药水时+2敏捷"),
        },
        [ChaosRelicCatalog.StartDraw] = new[]
        {
            V("BAG_OF_PREPARATION", "准备背包", "在每场战斗开始时，额外抽2张牌。", RarityCommon, "战斗开始多抽2张"),
            V("RING_OF_THE_SNAKE", "蛇之戒指", "在每场战斗开始时，额外抽2张牌。", RarityStarter, "战斗开始多抽2张"),
        },
        [ChaosRelicCatalog.StartEnergy] = new[]
        {
            // One-shot first-turn energy. Lantern used to sit under
            // PassiveMaxEnergy; it does not raise the cap.
            V("LANTERN", "灯笼", "在每场战斗的第一回合获得1点能量。", RarityCommon, "第1回合+1能量(一次性)"),
            V("VERY_HOT_COCOA", "烫嘴可可", "在每场战斗的第一回合额外获得4点能量。", RarityAncient, "第1回合+4能量(一次性)"),
            V("BOOMING_CONCH", "轰鸣海螺", "在精英战的战斗开始时，额外抽2张牌并获得1点能量。", RarityAncient, "精英战开始+1能量+2抽"),
        },
        [ChaosRelicCatalog.StartVulnAll] = new[]
        {
            V("BAG_OF_MARBLES", "弹珠袋", "在每场战斗开始时，给予所有敌人1层易伤。", RarityCommon, "战斗开始全体1易伤"),
        },
        [ChaosRelicCatalog.StartWeakAll] = new[]
        {
            V("RED_MASK", "红面具", "在每场战斗开始时，给予所有敌人1层虚弱。", RarityCommon, "战斗开始全体1虚弱"),
        },
        [ChaosRelicCatalog.StartThorns] = new[]
        {
            V("BRONZE_SCALES", "铜质鳞片", "在每场战斗开始时，获得3点荆棘。", RarityCommon, "战斗开始+3荆棘"),
        },
        [ChaosRelicCatalog.StartPlating] = new[]
        {
            V("GORGET", "护喉甲", "在每场战斗开始时，获得4层覆甲。", RarityCommon, "战斗开始+4覆甲"),
        },
        // ---- Per-turn ----
        [ChaosRelicCatalog.TurnStartBlock] = new[]
        {
            V("SAI", "钗", "在你的回合开始时，获得7点格挡。", RarityAncient, "每回合+7格挡"),
            V("ORICHALCUM", "奥利哈钢", "如果你在回合结束时没有任何格挡，获得6点格挡。", RarityUncommon, "回合末无格挡则+6"),
            V("FAKE_ORICHALCUM", "奥利哈钢？？？", "如果你在回合结束时没有任何格挡，获得3点格挡。", RarityEvent, "回合末无格挡则+3"),
        },
        [ChaosRelicCatalog.TurnStartEnergy] = new[]
        {
            // Sustained +1 energy with a drawback - the closest vanilla shape
            // to our drawback-free template. Turn-limited energy relics
            // (Candelabra/Chandelier/VeryHotCocoa/VenerableTeaSet/ArtOfWar/
            // HappyFlower/PaelsTears/SealOfGold/Bread) are deliberately not
            // listed: none of them grants energy on EVERY turn.
            V("PHILOSOPHERS_STONE", "贤者之石", "在每回合开始时获得1点能量。所有敌人初始获得1点力量。", RarityAncient, "每回合+1能量(敌人+1力量)"),
            V("BLESSED_ANTLER", "赐福鹿角", "在每回合开始时获得1点能量。在战斗开始时，将3张晕眩放入你的抽牌堆。", RarityAncient, "每回合+1能量(战斗开始3晕眩)"),
            V("VELVET_CHOKER", "天鹅绒颈圈", "在每回合开始时获得1点能量。你每回合不能打出超过6张牌。", RarityAncient, "每回合+1能量(每回合最多6张牌)"),
            V("SOZU", "添水", "在每回合开始时获得1点能量。你无法再获得药水。", RarityAncient, "每回合+1能量(无法获得药水)"),
        },
        [ChaosRelicCatalog.TurnStartDraw] = new[]
        {
            V("SNECKO_EYE", "异蛇之眼", "每回合多抽2张牌。每场战斗开始时获得混乱效果。", RarityAncient, "每回合多抽2张(获得混乱)"),
            V("FIDDLE", "小提琴", "在每个回合开始时，额外抽2张牌。你在回合进行中不再能抽任何牌。", RarityAncient, "每回合多抽2张(回合中不能抽牌)"),
            V("PENDULUM", "摆动球", "每3个回合，抽1张牌。", RarityCommon, "每3回合抽1张"),
            V("POLLINOUS_CORE", "花粉核心", "每4个回合，额外抽2张牌。", RarityEvent, "每4回合多抽2张"),
        },
        // ---- Card-play triggers ----
        [ChaosRelicCatalog.PlayBlock] = new[]
        {
            V("DAUGHTER_OF_THE_WIND", "风的女儿", "每当你打出一张攻击牌时，获得1点格挡。", RarityEvent, "每打攻击牌+1格挡"),
            V("ORNAMENTAL_FAN", "精致折扇", "你每在同一回合内打出3张攻击牌，就获得4点格挡。", RarityUncommon, "每3张攻击牌+4格挡"),
            V("TUNING_FORK", "音叉", "你每打出10张技能牌，获得7点格挡。", RarityUncommon, "每10张技能牌+7格挡"),
        },
        [ChaosRelicCatalog.PlayDamageRandom] = new[]
        {
            V("TINGSHA", "铜钹", "你每在你的回合丢弃一张牌，就对一名随机敌人造成3点伤害。", RarityUncommon, "每弃1张牌随机3伤害"),
            V("KUSARIGAMA", "锁镰", "你每在同一回合内打出3张攻击牌，就随机对一名敌人造成6点伤害。", RarityUncommon, "每3张攻击牌随机6伤害"),
            V("LETTER_OPENER", "开信刀", "你每在同一回合内打出3张技能牌，就对所有敌人造成5点伤害。", RarityUncommon, "每3张技能牌全体5伤害"),
        },
        // ---- Passives ----
        [ChaosRelicCatalog.PassiveAttackDamage] = new[]
        {
            V("STRIKE_DUMMY", "打击木偶", "名字中有“打击”的卡牌造成3点额外伤害。", RarityCommon, "打击牌+3伤害"),
            V("MINIATURE_CANNON", "微型大炮", "升级的攻击牌额外造成3点伤害。", RarityUncommon, "升级攻击牌+3伤害"),
            V("MYSTIC_LIGHTER", "神秘打火机", "有附魔的攻击牌额外造成9点伤害。", RarityShop, "附魔攻击牌+9伤害"),
            V("FAKE_STRIKE_DUMMY", "打击木偶？？？", "名字中有“打击”的卡牌造成1点额外伤害。", RarityEvent, "打击牌+1伤害"),
        },
        [ChaosRelicCatalog.PassiveGoldGain] = new[]
        {
            V("BOWLER_HAT", "圆顶礼帽", "额外获得25%的金币。", RarityUncommon, "金币获取+25%"),
        },
        [ChaosRelicCatalog.VictoryHeal] = new[]
        {
            V("BURNING_BLOOD", "燃烧之血", "在战斗结束时，回复6点生命。", RarityStarter, "战斗胜利回复6"),
            V("BLACK_BLOOD", "黑暗之血", "在战斗结束时，回复12点生命。", RarityStarter, "战斗胜利回复12"),
            V("MEAT_ON_THE_BONE", "带骨肉", "如果你在战斗结束时生命值等于或低于50%，回复12点生命。", RarityRare, "生命≤50%时胜利回复12"),
        },
        [ChaosRelicCatalog.RestHealBonus] = new[]
        {
            V("REGAL_PILLOW", "皇家枕头", "在休息时，额外回复15点生命。", RarityCommon, "休息额外回复15"),
        },
        // ---- Negatives ----
        [ChaosRelicCatalog.NegMaxHpDown] = new[]
        {
            V("LEAFY_POULTICE", "树叶药膏", "拾起时，变化你的1张打击和1张防御，然后失去12点最大生命。", RarityAncient, "拾取时最大生命-12"),
            V("SERE_TALON", "原初之爪", "拾起时，失去9点最大生命值，将3张许愿加入你的牌组。", RarityAncient, "拾取时最大生命-9"),
        },
        // ---- Verified empty groups ----
        // These arrays are intentionally empty; the empty result was verified
        // by grepping every RelicModel subclass in
        // sts2-spire1/research/engine-dllsrc/MegaCrit.Sts2.Core.Models.Relics/.
        // StartRegen        no relic touches RegenPower.
        // StartArtifact     the only relic touching ArtifactPower is
        //                   UnsettlingLamp, and it DOUBLES an existing debuff
        //                   rather than granting Artifact. The nearest analogue
        //                   is CoreSurge in the Spire1 mod (1 Artifact + 11
        //                   damage).
        // StartPoisonAll    no relic applies poison at combat start (SneckoSkull
        //                   only amplifies poison you already apply).
        // StartDamageAll    no relic deals combat-start damage to all enemies.
        // PassiveMaxEnergy  no relic raises the energy CAP. Lantern and
        //                   VeryHotCocoa are one-shot first-turn grants and live
        //                   under StartEnergy.
        // PassiveBlockAdd   no relic overrides ModifyBlockAdditive.
        // VictoryGold       no relic grants gold on combat victory.
        [ChaosRelicCatalog.StartRegen] = Array.Empty<VanillaRef>(),
        [ChaosRelicCatalog.StartArtifact] = Array.Empty<VanillaRef>(),
        [ChaosRelicCatalog.StartPoisonAll] = Array.Empty<VanillaRef>(),
        [ChaosRelicCatalog.StartDamageAll] = Array.Empty<VanillaRef>(),
        [ChaosRelicCatalog.PassiveMaxEnergy] = Array.Empty<VanillaRef>(),
        [ChaosRelicCatalog.PassiveBlockAdd] = Array.Empty<VanillaRef>(),
        [ChaosRelicCatalog.VictoryGold] = Array.Empty<VanillaRef>(),
    };

    private static VanillaRef V(string id, string name, string desc, string rarity, string note) =>
        new(id, name, desc, rarity, note);

    /// <summary>
    /// Relic id -> owning template, built once from <see cref="Map"/>.
    ///
    /// Declared AFTER Map on purpose: C# runs static field initialisers in
    /// declaration order, so Map is populated by the time this runs. This index
    /// also removes the old linear scan over a Dictionary (enumeration-order
    /// dependent, and a needless MP-determinism smell).
    /// </summary>
    private static readonly Dictionary<string, string> TemplateByRelicId = BuildReverseIndex();

    private static Dictionary<string, string> BuildReverseIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (template, refs) in Map)
        {
            foreach (var vref in refs)
            {
                index[vref.RelicId] = template;
            }
        }
        return index;
    }

    /// <summary>Vanilla relics with the same effect as our template (may be empty).</summary>
    public static IReadOnlyList<VanillaRef> For(string template) =>
        Map.TryGetValue(template, out var refs) ? refs : Array.Empty<VanillaRef>();

    /// <summary>
    /// What generating the SAME effect as a vanilla relic costs in OUR
    /// pricing: the owning template priced at the vanilla magnitude N, using
    /// the LIVE per-point config table and the template's own pricing shape
    /// (triangular when Decaying). Negatives return the REFUND instead.
    ///
    /// Returns null when the relic has no direct N equivalent in our system:
    ///   - its magnitude is not a flat count (BowlerHat: +25% gold), or
    ///   - its N means something different from our template's N, because the
    ///     trigger is a different event (TuningFork/OrnamentalFan: every N
    ///     CARDS rather than every card; Tingsha: on discard;
    ///     Kusarigama/LetterOpener: every 3 cards; Pendulum/PollinousCore:
    ///     every N TURNS rather than every turn).
    /// A relic whose scope is merely narrower than our template still gets a
    /// price (StrikeDummy: Strike-named cards only; MiniatureCannon: upgraded
    /// only; MysticLighter: enchanted only) - the EffectNote states the
    /// restriction.
    ///
    /// N is NOT clamped into the user's Min/Max band: the point of the hover is
    /// to price the vanilla magnitude as-is, even when the user has narrowed
    /// the band below it.
    /// </summary>
    public static int? OurPointsFor(VanillaRef vref)
    {
        if (!TemplateByRelicId.TryGetValue(vref.RelicId, out string? template))
        {
            return null;
        }
        if (!Magnitudes.TryGetValue(vref.RelicId, out int n) || n <= 0)
        {
            return null;
        }

        ChaosRelicCatalog.TemplateSpec spec;
        try
        {
            spec = ChaosTemplates.Spec(template);
        }
        catch (InvalidOperationException)
        {
            // Unknown template id: never surface a pricing failure into the UI.
            return null;
        }

        ChaosPointCosts costs = QuriousCraftingRelicsConfig.PointCosts;
        return spec.IsNegative
            ? ChaosTemplates.RefundOf(spec, costs, n)
            : ChaosTemplates.PriceOf(spec, costs, n);
    }

    /// <summary>
    /// Vanilla magnitude N for relics whose effect maps one-to-one onto an
    /// N-based template of ours. Absence means "no direct N equivalent"
    /// (see <see cref="OurPointsFor"/>), not an oversight.
    /// </summary>
    private static readonly Dictionary<string, int> Magnitudes = new(StringComparer.Ordinal)
    {
        // Combat-start one-shots
        ["ANCHOR"] = 10, ["FAKE_ANCHOR"] = 4,
        ["VAJRA"] = 1, ["SWORD_OF_JADE"] = 3, ["EMBER_TEA"] = 2,
        ["ODDLY_SMOOTH_STONE"] = 1, ["BELT_BUCKLE"] = 2,
        ["BAG_OF_PREPARATION"] = 2, ["RING_OF_THE_SNAKE"] = 2,
        ["LANTERN"] = 1, ["VERY_HOT_COCOA"] = 4, ["BOOMING_CONCH"] = 1,
        ["BAG_OF_MARBLES"] = 1, ["RED_MASK"] = 1,
        ["BRONZE_SCALES"] = 3, ["GORGET"] = 4,
        // Per-turn
        ["SAI"] = 7, ["ORICHALCUM"] = 6, ["FAKE_ORICHALCUM"] = 3,
        ["PHILOSOPHERS_STONE"] = 1, ["BLESSED_ANTLER"] = 1,
        ["VELVET_CHOKER"] = 1, ["SOZU"] = 1,
        ["SNECKO_EYE"] = 2, ["FIDDLE"] = 2,
        // Card-play triggers (per-card unit)
        ["DAUGHTER_OF_THE_WIND"] = 1,
        // Passives
        ["STRIKE_DUMMY"] = 3, ["MINIATURE_CANNON"] = 3,
        ["MYSTIC_LIGHTER"] = 9, ["FAKE_STRIKE_DUMMY"] = 1,
        ["BURNING_BLOOD"] = 6, ["BLACK_BLOOD"] = 12, ["MEAT_ON_THE_BONE"] = 12,
        ["REGAL_PILLOW"] = 15,
        // Negatives (refund side)
        ["LEAFY_POULTICE"] = 12, ["SERE_TALON"] = 9,
        // Intentionally absent (no direct N equivalent):
        //   TUNING_FORK, ORNAMENTAL_FAN   every-N-cards trigger
        //   TINGSHA, KUSARIGAMA, LETTER_OPENER  non-per-card trigger
        //   PENDULUM, POLLINOUS_CORE      every-N-turns trigger
        //   BOWLER_HAT                    multiplicative, not a flat count
    };
}
