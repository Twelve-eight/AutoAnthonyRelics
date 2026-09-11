using System;
using System.Linq;
using System.Collections.Generic;

namespace AutoAnthonyRelics.Chaos;

/// <summary>
/// EXTRA effect pool (user order 2026-09-11): card-level and watcher-stance
/// effects that vanilla relics never do. OFF by default; enable via
/// EnableExtraPool in settings (Tier-1 MP determinism key - both ends must
/// match or relics differ).
///
/// All engine APIs byte-verified:
/// - Retain: CardModel.GiveSingleTurnRetain() (cleared end of turn; the
///   per-turn hook re-applies each turn).
/// - Sly (奇巧): CardModel.GiveSingleTurnSly() (same pattern).
/// - Ethereal (虚无): CardModel.AddKeyword(CardKeyword.Ethereal) - exhausts
///   at end of turn (keywords persist for the combat; applied at draw time).
/// - Enchant: CardCmd.Enchant&lt;T&gt;(card, amount) / Enchant(model, card, amount)
///   - 17 enchantment models exist (Sharp, Nimble, Imbued, ...).
/// - Stances (Watcher mod 3747526116 only): WatcherCombatHelper.EnterWrath/
///   EnterCalm/EnterDivinity(owner, source), reflection-invoked; templates
///   skipped when the Watcher mod is absent.
/// </summary>
public static class ChaosRelicExtraCatalog
{
    // ---------- Positives ----------

    public const string HandRetain = "X_HAND_RETAIN";
    public const string HandSly = "X_HAND_SLY";
    public const string HandEthereal = "X_HAND_ETHEREAL_NEG";
    public const string EnchantSharp = "X_ENCHANT_SHARP";
    public const string EnchantNimble = "X_ENCHANT_NIMBLE";
    public const string EnchantImbued = "X_ENCHANT_IMBUED";
    public const string RetainEnergyDiscount = "X_RETAIN_ENERGY_DISCOUNT";
    public const string RetainAttackBuff = "X_RETAIN_ATTACK_BUFF";
    public const string StanceWrathStart = "X_STANCE_WRATH";
    public const string StanceCalmStart = "X_STANCE_CALM";
    public const string StanceDivinityStart = "X_STANCE_DIVINITY";

    // ---------- Negatives ----------

    public const string NegHandEthereal = "X_HAND_ETHEREAL";

    /// <summary>See ChaosRelicCatalog.TemplateSpec for field semantics.</summary>
    private static readonly Dictionary<string, ChaosRelicCatalog.TemplateSpec> Specs = new(StringComparer.Ordinal)
    {
        // Per-turn hand effects (repeat every turn while the relic is held).
[HandRetain] = new(HandRetain, false, 1, 3, CostPerPoint: 3, RefundPerPoint: 0,
    "每回合开始时,你手牌中的至多{N}张牌获得保留."),
[HandSly] = new(HandSly, false, 1, 3, CostPerPoint: 3, RefundPerPoint: 0,
    "每回合开始时,你手牌中的至多{N}张牌获得奇巧(如果这张牌在你的回合结束前从你的手牌中被丢弃,则免费将其打出)."),
[RetainEnergyDiscount] = new(RetainEnergyDiscount, false, 1, 1, CostPerPoint: 4, RefundPerPoint: 0,
    "每当你保留一张牌时,该牌费用-{N}."),
[RetainAttackBuff] = new(RetainAttackBuff, false, 1, 3, CostPerPoint: 3, RefundPerPoint: 0,
    "每当你保留一张牌时,本回合你的下一张攻击牌伤害+{N}."),
// Enchants: applied ONCE to random eligible hand cards at combat start.
// Names/descriptions aligned to vanilla zhs enchantments loc (2026-09-11):
// SHARP=锋利(伤害+1), NIMBLE=灵巧(格挡+1, 只能附魔获得格挡的牌), IMBUED=注能
// (战斗开始时自动打出, 只能附魔技能牌) - code applies amount 1 (verified).
[EnchantSharp] = new(EnchantSharp, false, 1, 2, CostPerPoint: 5, RefundPerPoint: 0,
    "战斗开始时,为你手牌中的至多{N}张攻击牌附加锋利附魔(这张牌上的伤害值+1)."),
[EnchantNimble] = new(EnchantNimble, false, 1, 2, CostPerPoint: 5, RefundPerPoint: 0,
    "战斗开始时,为你手牌中的至多{N}张获得格挡的牌附加灵巧附魔(这张牌获得的格挡值+1)."),
[EnchantImbued] = new(EnchantImbued, false, 1, 2, CostPerPoint: 4, RefundPerPoint: 0,
    "战斗开始时,为你手牌中的至多{N}张技能牌附加注能附魔(这张牌在每场战斗开始时自动打出)."),
// Watcher stances (require the Watcher mod; skipped otherwise).
// Wording aligned to Watcher mod zhs loc: WRATH=愤怒(双倍), CALM=平静(离开时
// 获得2能量), DIVINITY=神格(三倍+进入时3能量+下回合自动退出).
[StanceWrathStart] = new(StanceWrathStart, false, 1, 1, CostPerPoint: 4, RefundPerPoint: 0,
    "每回合开始时,进入愤怒姿态(你的攻击造成双倍伤害,你从攻击中受到双倍伤害)."),
[StanceCalmStart] = new(StanceCalmStart, false, 1, 1, CostPerPoint: 3, RefundPerPoint: 0,
    "每回合开始时,进入平静姿态(离开这一姿态时,获得2点能量)."),
[StanceDivinityStart] = new(StanceDivinityStart, false, 1, 1, CostPerPoint: 7, RefundPerPoint: 0,
    "每回合开始时,进入神格姿态(你的攻击造成三倍伤害,进入时获得3点能量,下回合开始时自动离开)."),
// Negatives.
[NegHandEthereal] = new(NegHandEthereal, true, 1, 3, CostPerPoint: 0, RefundPerPoint: 4,
    "每回合开始时,你手牌中的至多{N}张牌获得虚无(如果这张牌在这个回合结束时留在你的手牌中,则将其消耗)."),
    };

    public static IReadOnlyList<string> PositiveTemplates { get; } =
        Specs.Values.Where(s => !s.IsNegative).Select(s => s.Template).ToArray();

    public static IReadOnlyList<string> NegativeTemplates { get; } =
        Specs.Values.Where(s => s.IsNegative).Select(s => s.Template).ToArray();

    /// <summary>Templates requiring the Watcher mod (stance entries).</summary>
    public static IReadOnlyList<string> WatcherTemplates { get; } =
        new[] { StanceWrathStart, StanceCalmStart, StanceDivinityStart };

    public static ChaosRelicCatalog.TemplateSpec Spec(string template) =>
        Specs.TryGetValue(template, out var spec) ? spec
            : throw new InvalidOperationException($"Unknown extra chaos relic template {template}.");

    public static bool IsNegative(string template) => Spec(template).IsNegative;

    /// <summary>Does this template id live in the extra pool?</summary>
    public static bool HasTemplate(string template) => Specs.ContainsKey(template);
}
