using System;
using System.Collections.Generic;
using System.Linq;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// Effect catalog: every entry template binds to exactly one relic hook and knows
/// how to render its Chinese text, its balance band (min/max amount) and its
/// point cost (v0.5 budget system).
///
/// v0.5 (user order 2026-09-11, MH-Rise qurious-crafting style): relics are
/// ALWAYS active (unlike cards), so the old free 1/3/5-entry relics were
/// severely overpowered. Each relic now gets a rarity-scaled POINT BUDGET;
/// positive entries COST points (CostPerPoint x Amount), negative entries
/// REFUND points (RefundPerPoint x Amount), and the generator assembles
/// "a few positives + one negative" within budget. Every cost is a config
/// key (user-tunable default; see QuriousCraftingRelicsConfig).
/// </summary>
public static class ChaosRelicCatalog
{
    // ---------- Positives (cost points) ----------

    public const string StartDamageAll = "C_START_DAMAGE_ALL";
    public const string StartBlock = "C_START_BLOCK";
    public const string StartStrength = "C_START_STRENGTH";
    public const string StartDexterity = "C_START_DEXTERITY";
    public const string StartDraw = "C_START_DRAW";
    public const string StartEnergy = "C_START_ENERGY";
    public const string StartVulnAll = "C_START_VULN_ALL";
    public const string StartWeakAll = "C_START_WEAK_ALL";
    public const string StartRegen = "C_START_REGEN";
    public const string StartThorns = "C_START_THORNS";
    public const string StartArtifact = "C_START_ARTIFACT";
    public const string StartPoisonAll = "C_START_POISON_ALL";
    public const string StartPlating = "C_START_PLATING";
    public const string TurnStartBlock = "T_START_BLOCK";
    public const string TurnStartEnergy = "T_START_ENERGY";
    public const string TurnStartHeal = "T_START_HEAL";
    public const string TurnStartDraw = "T_START_DRAW";
    public const string PlayDamageRandom = "PLAY_DAMAGE_RANDOM";
    public const string PlayBlock = "PLAY_BLOCK";
    public const string PassiveAttackDamage = "PASSIVE_ATTACK_DAMAGE";
    public const string PassiveMaxEnergy = "PASSIVE_MAX_ENERGY";
    public const string PassiveBlockAdd = "PASSIVE_BLOCK_ADD";
    public const string VictoryHeal = "VICTORY_HEAL";
    public const string VictoryGold = "VICTORY_GOLD";
    public const string PassiveGoldGain = "PASSIVE_GOLD_GAIN";
    public const string RestHealBonus = "REST_HEAL_BONUS";

    // ---------- Negatives (refund points) ----------

    public const string NegStartFrail = "N_START_FRAIL_SELF";
    public const string NegTurnLoseHp = "N_TURN_LOSE_HP";
    public const string NegTurnEnergyDown = "N_TURN_ENERGY_DOWN";
    public const string NegTurnDrawDown = "N_TURN_DRAW_DOWN";
    public const string NegGoldDown = "N_GOLD_DOWN";
    public const string NegPotionBlock = "N_POTION_BLOCK";
    public const string NegStartSloth = "N_START_SLOTH_SELF";
    public const string NegRestHealDown = "N_REST_HEAL_DOWN";
    public const string NegAttackDamageDown = "N_ATTACK_DAMAGE_DOWN";
    public const string NegMaxHpDown = "N_MAX_HP_DOWN";

    /// <summary>
    /// Template spec: balance band + point economics.
    /// CostPerPoint: points charged per unit of Amount (positives).
    /// RefundPerPoint: points refunded per unit of Amount (negatives).
    /// Flat templates (PotionBlock): Amount is always 1, cost/refund is the
    /// flat value.
    ///
    /// Decaying powers (poison/regen/plating - engine-verified: trigger for
    /// Amount then Amount-1, ..): the Nth stack is worth MORE than the 1st
    /// (total value = N+(N-1)+..+1, triangular), so cost is triangular:
    /// total(N) = CostPerPoint * (N*(N+1)/2) - each extra point of Amount
    /// costs CostPerPoint more than the previous one. PowerModel spec
    /// amounts roll freely in-band; the generator clamps to budget.
    /// </summary>
    public sealed record TemplateSpec(
        string Template, bool IsNegative, int Min, int Max,
        int CostPerPoint, int RefundPerPoint, string TextPattern,
        bool Decaying = false)
    {
        public string Render(int amount) => TextPattern.Replace("{N}", amount.ToString());

        public int Cost(int amount) => Decaying
            ? CostPerPoint * amount * (amount + 1) / 2
            : CostPerPoint * amount;

        public int Refund(int amount) => RefundPerPoint * amount;
    }

    /// <summary>
    /// Default point economics (user-tunable via config; see config class).
    ///
    /// DECLARED AS AN ARRAY, NOT A DICTIONARY, ON PURPOSE. The generator
    /// indexes the derived template lists with the seeded RNG
    /// (PickAffordablePositive / PickNegative), so the list ORDER is part of
    /// the generation contract. A Dictionary enumerates in insertion order
    /// only as an implementation detail, not as a contract - the probe
    /// measured it as byte-identical across three separate processes on
    /// .NET 9, but "stable in practice" is not something two MP clients may
    /// rely on. This array is the explicit single source of order;
    /// SpecsById only serves lookups.
    ///
    /// The order below is identical to the order the old Dictionary literal
    /// enumerated in, so no seed produces different relics than before.
    ///
    /// ADDING A TEMPLATE: append it at the end of its section (positives
    /// first, then negatives, as laid out below). Inserting in the middle
    /// changes which template a given seed picks.
    /// </summary>
    private static readonly TemplateSpec[] Specs =
    {
        // Combat-start one-shot effects: generous bands, moderate cost.
        new(StartDamageAll, false, 3, 8, CostPerPoint: 2, RefundPerPoint: 0, "战斗开始时,对所有敌人造成{N}点伤害."),
        new(StartBlock, false, 4, 10, CostPerPoint: 2, RefundPerPoint: 0, "战斗开始时,获得{N}点格挡."),
        new(StartStrength, false, 1, 10, CostPerPoint: 5, RefundPerPoint: 0, "战斗开始时,获得{N}点力量."),
        new(StartDexterity, false, 1, 3, CostPerPoint: 4, RefundPerPoint: 0, "战斗开始时,获得{N}点敏捷."),
        new(StartDraw, false, 1, 3, CostPerPoint: 3, RefundPerPoint: 0, "战斗开始时,抽{N}张牌."),
        new(StartEnergy, false, 1, 3, CostPerPoint: 6, RefundPerPoint: 0, "战斗开始时,获得{N}点能量."),
        new(StartVulnAll, false, 1, 3, CostPerPoint: 3, RefundPerPoint: 0, "战斗开始时,对所有敌人施加{N}层易伤."),
        new(StartWeakAll, false, 1, 3, CostPerPoint: 3, RefundPerPoint: 0, "战斗开始时,对所有敌人施加{N}层虚弱."),
        new(StartRegen, false, 1, 4, CostPerPoint: 2, RefundPerPoint: 0, "战斗开始时,获得{N}层再生.", Decaying: true), // 三角定价: 总花费=2*N(N+1)/2; N=4 -> 20点
        new(StartThorns, false, 1, 3, CostPerPoint: 4, RefundPerPoint: 0, "战斗开始时,获得{N}点荆棘."),
        new(StartArtifact, false, 1, 2, CostPerPoint: 9, RefundPerPoint: 0, "战斗开始时,获得{N}层人工制品."), // 原版无遗物给人工制品; 核心电涌(Spire1 mod)=1层+11伤害, 层价值极高: 9/层 (用户裁定: 人工制品显著贵于力量5/敏捷4)
        new(StartPoisonAll, false, 2, 6, CostPerPoint: 1, RefundPerPoint: 0, "战斗开始时,对所有敌人施加{N}层中毒.", Decaying: true), // 三角定价: N=6 -> 21点 (全体敌人,已含群体溢价)
        new(StartPlating, false, 1, 4, CostPerPoint: 2, RefundPerPoint: 0, "战斗开始时,获得{N}层覆甲.", Decaying: true), // 覆甲=PlatingPower; 三角定价 N=4 -> 20点; 机制详情悬停可见 (原版描述: 回合结束时获得格挡, 回合开始时层数-1)
        // Per-turn effects: small bands, high cost (they repeat every turn).
        new(TurnStartBlock, false, 2, 5, CostPerPoint: 4, RefundPerPoint: 0, "每回合开始时,获得{N}点格挡."),
        new(TurnStartEnergy, false, 1, 1, CostPerPoint: 10, RefundPerPoint: 0, "每回合开始时,获得{N}点能量."),
        new(TurnStartHeal, false, 1, 3, CostPerPoint: 7, RefundPerPoint: 0, "每回合开始时,回复{N}点生命."),
        new(TurnStartDraw, false, 1, 1, CostPerPoint: 9, RefundPerPoint: 0, "每回合开始时,抽{N}张牌."),
        // Card-play triggers: small bands.
        new(PlayDamageRandom, false, 1, 4, CostPerPoint: 2, RefundPerPoint: 0, "每当你打出一张牌,对随机一名敌人造成{N}点伤害."),
        new(PlayBlock, false, 1, 3, CostPerPoint: 4, RefundPerPoint: 0, "每当你打出一张牌,获得{N}点格挡."),
        // Passives: fixed-ish, priced by permanence.
        new(PassiveAttackDamage, false, 1, 8, CostPerPoint: 7, RefundPerPoint: 0, "你的攻击牌伤害+{N}."),
        new(PassiveMaxEnergy, false, 1, 1, CostPerPoint: 8, RefundPerPoint: 0, "每回合能量上限+{N}."),
        new(PassiveBlockAdd, false, 1, 2, CostPerPoint: 6, RefundPerPoint: 0, "你获得格挡时,格挡值+{N}."),
        new(VictoryHeal, false, 2, 8, CostPerPoint: 3, RefundPerPoint: 0, "战斗胜利后,回复{N}点生命."),
        new(VictoryGold, false, 5, 20, CostPerPoint: 1, RefundPerPoint: 0, "战斗胜利后,获得{N}金币."),
        new(PassiveGoldGain, false, 1, 3, CostPerPoint: 2, RefundPerPoint: 0, "你获得的金币+{N}."),
        new(RestHealBonus, false, 1, 5, CostPerPoint: 1, RefundPerPoint: 0, "营火休息时,额外回复{N}点生命."),
        // Negatives: refund points. Bands sized so refunds matter but stay playable.
        new(NegStartFrail, true, 1, 2, CostPerPoint: 0, RefundPerPoint: 4, "战斗开始时,你获得{N}层脆弱."),
        new(NegTurnLoseHp, true, 1, 3, CostPerPoint: 0, RefundPerPoint: 3, "每回合开始时,失去{N}点生命."),
        new(NegTurnEnergyDown, true, 1, 1, CostPerPoint: 0, RefundPerPoint: 6, "每回合能量上限-{N}."),
        new(NegTurnDrawDown, true, 1, 5, CostPerPoint: 0, RefundPerPoint: 6, "每回合抽牌数-{N}."),
        new(NegGoldDown, true, 1, 3, CostPerPoint: 0, RefundPerPoint: 2, "你获得的金币-{N}."),
        new(NegPotionBlock, true, 1, 1, CostPerPoint: 0, RefundPerPoint: 30, "你无法获得药水."),
        new(NegStartSloth, true, 1, 5, CostPerPoint: 0, RefundPerPoint: 6, "每回合你无法打出超过{M}张牌."), // 原版SlothPower文案对齐; VelvetChoker式: 上限=7-N, 返还6N点; 文案由模型层渲染(7-N)
        new(NegRestHealDown, true, 1, 4, CostPerPoint: 0, RefundPerPoint: 2, "营火休息时,回复的生命-{N}."),
        new(NegAttackDamageDown, true, 1, 2, CostPerPoint: 0, RefundPerPoint: 3, "你的攻击牌伤害-{N}."),
        new(NegMaxHpDown, true, 1, 20, CostPerPoint: 0, RefundPerPoint: 4, "获得此遗物时,最大生命值-{N}."),
    };

    /// <summary>Lookup index only - never enumerated for generation order.</summary>
    private static readonly Dictionary<string, TemplateSpec> SpecsById =
        Specs.ToDictionary(s => s.Template, StringComparer.Ordinal);

    /// <summary>Every template id, in explicit generation order.</summary>
    public static IReadOnlyList<string> AllTemplates { get; } =
        Specs.Select(s => s.Template).ToArray();

    public static IReadOnlyList<string> PositiveTemplates { get; } =
        Specs.Where(s => !s.IsNegative).Select(s => s.Template).ToArray();

    public static IReadOnlyList<string> NegativeTemplates { get; } =
        Specs.Where(s => s.IsNegative).Select(s => s.Template).ToArray();

    public static TemplateSpec Spec(string template) =>
        SpecsById.TryGetValue(template, out var spec) ? spec
            : throw new InvalidOperationException($"Unknown chaos relic template {template}.");

    public static bool IsNegative(string template) => Spec(template).IsNegative;

    /// <summary>Does this template id live in the core pool?</summary>
    public static bool HasTemplate(string template) => SpecsById.ContainsKey(template);
}

/// <summary>
/// Config-facing cost table: resolves per-template CostPerPoint / RefundPerPoint
/// from the live SimpleModConfig values, falling back to catalog defaults.
/// Kept separate from TemplateSpec records so config changes apply without
/// regenerating the catalog dictionary.
/// </summary>
public sealed class ChaosPointCosts
{
    private readonly Func<string, int?> _costLookup;
    private readonly Func<string, int?> _refundLookup;

    public ChaosPointCosts(Func<string, int?> costLookup, Func<string, int?> refundLookup)
    {
        _costLookup = costLookup;
        _refundLookup = refundLookup;
    }

    /// <summary>
    /// Points charged per unit of Amount. Resolves across BOTH pools
    /// (core + extra) - the core-only lookup this replaced threw
    /// InvalidOperationException on every extra-pool template while
    /// EnableExtraPool was on (probe: X_HAND_RETAIN).
    /// </summary>
    public int CostPerPoint(string template)
    {
        var spec = ChaosTemplates.Spec(template);
        if (!spec.IsNegative && _costLookup(template) is int configured && configured > 0)
        {
            return configured;
        }
        return spec.CostPerPoint;
    }

    /// <summary>
    /// Points refunded per unit of Amount, across both pools. The extra pool's
    /// only negative (X_HAND_ETHEREAL) is a refund path, so a core-only lookup
    /// broke the negative roll too.
    /// </summary>
    public int RefundPerPoint(string template)
    {
        var spec = ChaosTemplates.Spec(template);
        if (spec.IsNegative && _refundLookup(template) is int configured && configured > 0)
        {
            return configured;
        }
        return spec.RefundPerPoint;
    }
}
