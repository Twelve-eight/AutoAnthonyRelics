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
            "每回合开始时,你手牌中的至多{N}张牌获得奇巧(回合结束时未被使用的奇巧牌返还)."),
        [RetainEnergyDiscount] = new(RetainEnergyDiscount, false, 1, 1, CostPerPoint: 4, RefundPerPoint: 0,
            "每当你保留一张牌时,该牌费用-{N}."),
        [RetainAttackBuff] = new(RetainAttackBuff, false, 1, 3, CostPerPoint: 3, RefundPerPoint: 0,
            "每当你保留一张牌时,本回合你的下一张攻击牌伤害+{N}."),
        // Enchants: applied ONCE to random eligible hand cards at combat start.
        [EnchantSharp] = new(EnchantSharp, false, 1, 2, CostPerPoint: 5, RefundPerPoint: 0,
            "战斗开始时,为你手牌中的至多{N}张攻击牌附加锋锐附魔(伤害+2)."),
        [EnchantNimble] = new(EnchantNimble, false, 1, 2, CostPerPoint: 5, RefundPerPoint: 0,
            "战斗开始时,为你手牌中的至多{N}张牌附加轻盈附魔(费用-1)."),
        [EnchantImbued] = new(EnchantImbued, false, 1, 2, CostPerPoint: 4, RefundPerPoint: 0,
            "战斗开始时,为你手牌中的至多{N}张牌附加灌注附魔(升级该牌)."),
        // Watcher stances (require the Watcher mod; skipped otherwise).
        [StanceWrathStart] = new(StanceWrathStart, false, 1, 1, CostPerPoint: 4, RefundPerPoint: 0,
            "每回合开始时,进入愤怒姿态(造成的伤害+50%,受到的伤害+50%)."),
        [StanceCalmStart] = new(StanceCalmStart, false, 1, 1, CostPerPoint: 3, RefundPerPoint: 0,
            "每回合开始时,进入平静姿态(若在回合结束时仍处于该姿态,获得2点能量)."),
        [StanceDivinityStart] = new(StanceDivinityStart, false, 1, 1, CostPerPoint: 7, RefundPerPoint: 0,
            "每回合开始时,进入神格姿态(造成的伤害翻倍,获得3点能量)."),
        // Negatives.
        [NegHandEthereal] = new(NegHandEthereal, true, 1, 3, CostPerPoint: 0, RefundPerPoint: 4,
            "每回合开始时,你手牌中的至多{N}张牌获得虚无(回合结束时消耗)."),
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
