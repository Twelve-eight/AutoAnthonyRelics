using System;
using System.Collections.Generic;
using BaseLib.Config;
using AutoAnthonyRelics.Chaos;

namespace AutoAnthonyRelics;

/// <summary>
/// Runtime toggles + budget-system tuning (Settings -> Mod Settings),
/// Spire1Config pattern.
///
/// v0.5 point budgets (user order 2026-09-11, MH qurious-crafting style):
/// every template's cost/refund is individually tunable IN-GAME, grouped
/// into collapsible settings sections (ConfigSection = collapsible in
/// BaseLib's ModConfig UI). Defaults mirror ChaosRelicCatalog /
/// ChaosRelicExtraCatalog.
///
/// MP DETERMINISM (Tier-1 keys, see sts2-mpconfigsync incident 2026-09-10):
/// budgets, negative chances, per-template costs, EnableExtraPool and
/// EnableChaosRelics MUST match on both ends - they feed the seeded
/// generator.
/// </summary>
[ConfigHoverTipsByDefault]
internal class AutoAnthonyRelicsConfig : SimpleModConfig
{
    /// <summary>Master switch. When false, chaos relics never generate or apply.</summary>
    public static bool EnableChaosRelics { get; set; } = true;

    /// <summary>
    /// EXTRA effect pool (card keywords/enchants/watcher stances). Off by
    /// default (user order 2026-09-11: non-vanilla-relic effects are opt-in).
    /// Tier-1 MP determinism key.
    /// </summary>
    public static bool EnableExtraPool { get; set; } = false;

    /// <summary>
    /// Legacy entry-count key. v0.5 budget system ignores it (kept for save
    /// compat and registry call-shape stability).
    /// </summary>
    public static int ChaosRelicMultiplier { get; set; } = 3;

    // ---------- Point budgets by rarity ----------

    [ConfigSection("BUDGET_SECTION")]
    [ConfigSlider(1, 60, 1)]
    public static int ChaosRelicBudgetCommon { get; set; } = 10;

    [ConfigSlider(1, 80, 1)]
    public static int ChaosRelicBudgetUncommon { get; set; } = 16;

    [ConfigSlider(1, 120, 1)]
    public static int ChaosRelicBudgetRare { get; set; } = 24;

    // ---------- Negative-entry chances (percent) by rarity ----------

    [ConfigSlider(0, 100, 5)]
    public static int ChaosRelicNegativeChanceCommon { get; set; } = 35;

    [ConfigSlider(0, 100, 5)]
    public static int ChaosRelicNegativeChanceUncommon { get; set; } = 55;

    [ConfigSlider(0, 100, 5)]
    public static int ChaosRelicNegativeChanceRare { get; set; } = 75;

    // ======================================================================
    // Per-template point settings (in-game, collapsible sections).
    // BaseLib ConfigSection renders as a collapsible group, so the ~50
    // template cost sliders live collapsed under three headers instead of
    // flooding the settings list. Property names map to loc keys
    // AUTOANTHONYRELICS-COST_<TEMPLATE>.title; the value feeds the SAME
    // ChaosPointCosts lookup the cfg-file Cost_<TEMPLATE> keys use (these
    // properties ARE the config values - no reflection bridge needed).
    // ======================================================================

    // ---------- Section: positive core-pool costs ----------

    [ConfigSection("COST_POSITIVE_SECTION")]
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_DAMAGE_ALL { get; set; } = 2;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_BLOCK { get; set; } = 2;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_STRENGTH { get; set; } = 5;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_DEXTERITY { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_DRAW { get; set; } = 3;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_ENERGY { get; set; } = 6;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_VULN_ALL { get; set; } = 3;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_WEAK_ALL { get; set; } = 3;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_REGEN { get; set; } = 2; // 衰减: 三角计价 2*N(N+1)/2
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_THORNS { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_ARTIFACT { get; set; } = 9;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_POISON_ALL { get; set; } = 1; // 衰减: 三角计价 1*N(N+1)/2
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_START_PLATING { get; set; } = 2; // 衰减: 三角计价 2*N(N+1)/2
    [ConfigSlider(1, 20, 1)]
    public static int Cost_T_START_BLOCK { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_T_START_ENERGY { get; set; } = 10;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_T_START_HEAL { get; set; } = 7;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_T_START_DRAW { get; set; } = 9;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_PLAY_DAMAGE_RANDOM { get; set; } = 2;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_PLAY_BLOCK { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_PASSIVE_ATTACK_DAMAGE { get; set; } = 7;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_PASSIVE_MAX_ENERGY { get; set; } = 8;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_PASSIVE_BLOCK_ADD { get; set; } = 6;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_VICTORY_HEAL { get; set; } = 3;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_VICTORY_GOLD { get; set; } = 1;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_PASSIVE_GOLD_GAIN { get; set; } = 2;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_REST_HEAL_BONUS { get; set; } = 1;

    // ---------- Section: negative core-pool refunds ----------

    [ConfigSection("REFUND_NEGATIVE_SECTION")]
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_START_FRAIL_SELF { get; set; } = 4;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_TURN_LOSE_HP { get; set; } = 3;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_TURN_ENERGY_DOWN { get; set; } = 6;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_TURN_DRAW_DOWN { get; set; } = 6;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_GOLD_DOWN { get; set; } = 2;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_POTION_BLOCK { get; set; } = 30;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_START_SLOTH_SELF { get; set; } = 6; // 每点返还 6N; 上限=7-N 张牌
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_REST_HEAL_DOWN { get; set; } = 2;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_ATTACK_DAMAGE_DOWN { get; set; } = 3;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_MAX_HP_DOWN { get; set; } = 4;

    // ---------- Section: extra pool (costs AND refunds) ----------

    [ConfigSection("COST_EXTRA_SECTION")]
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_HAND_RETAIN { get; set; } = 3;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_HAND_SLY { get; set; } = 3;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_RETAIN_ENERGY_DISCOUNT { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_RETAIN_ATTACK_BUFF { get; set; } = 3;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_ENCHANT_SHARP { get; set; } = 5;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_ENCHANT_NIMBLE { get; set; } = 5;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_ENCHANT_IMBUED { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_STANCE_WRATH { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_STANCE_CALM { get; set; } = 3;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_STANCE_DIVINITY { get; set; } = 7;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_X_HAND_ETHEREAL { get; set; } = 4;


    // ---------- Template bounds (budget editor storage; defaults = catalog) ----------

    public static int Min_StartDamageAll { get; set; } = 3;
    public static int Max_StartDamageAll { get; set; } = 8;
    public static int Min_StartBlock { get; set; } = 4;
    public static int Max_StartBlock { get; set; } = 10;
    public static int Min_StartStrength { get; set; } = 1;
    public static int Max_StartStrength { get; set; } = 10;
    public static int Min_StartDexterity { get; set; } = 1;
    public static int Max_StartDexterity { get; set; } = 3;
    public static int Min_StartDraw { get; set; } = 1;
    public static int Max_StartDraw { get; set; } = 3;
    public static int Min_StartEnergy { get; set; } = 1;
    public static int Max_StartEnergy { get; set; } = 3;
    public static int Min_StartVulnAll { get; set; } = 1;
    public static int Max_StartVulnAll { get; set; } = 3;
    public static int Min_StartWeakAll { get; set; } = 1;
    public static int Max_StartWeakAll { get; set; } = 3;
    public static int Min_StartRegen { get; set; } = 1;
    public static int Max_StartRegen { get; set; } = 4;
    public static int Min_StartThorns { get; set; } = 1;
    public static int Max_StartThorns { get; set; } = 3;
    public static int Min_StartArtifact { get; set; } = 1;
    public static int Max_StartArtifact { get; set; } = 2;
    public static int Min_StartPoisonAll { get; set; } = 2;
    public static int Max_StartPoisonAll { get; set; } = 6;
    public static int Min_StartPlating { get; set; } = 1;
    public static int Max_StartPlating { get; set; } = 4;
    public static int Min_TurnStartBlock { get; set; } = 2;
    public static int Max_TurnStartBlock { get; set; } = 5;
    public static int Min_TurnStartEnergy { get; set; } = 1;
    public static int Max_TurnStartEnergy { get; set; } = 1;
    public static int Min_TurnStartHeal { get; set; } = 1;
    public static int Max_TurnStartHeal { get; set; } = 3;
    public static int Min_TurnStartDraw { get; set; } = 1;
    public static int Max_TurnStartDraw { get; set; } = 1;
    public static int Min_PlayDamageRandom { get; set; } = 1;
    public static int Max_PlayDamageRandom { get; set; } = 4;
    public static int Min_PlayBlock { get; set; } = 1;
    public static int Max_PlayBlock { get; set; } = 3;
    public static int Min_PassiveAttackDamage { get; set; } = 1;
    public static int Max_PassiveAttackDamage { get; set; } = 8;
    public static int Min_PassiveMaxEnergy { get; set; } = 1;
    public static int Max_PassiveMaxEnergy { get; set; } = 1;
    public static int Min_PassiveBlockAdd { get; set; } = 1;
    public static int Max_PassiveBlockAdd { get; set; } = 2;
    public static int Min_VictoryHeal { get; set; } = 2;
    public static int Max_VictoryHeal { get; set; } = 8;
    public static int Min_VictoryGold { get; set; } = 5;
    public static int Max_VictoryGold { get; set; } = 20;
    public static int Min_PassiveGoldGain { get; set; } = 1;
    public static int Max_PassiveGoldGain { get; set; } = 3;
    public static int Min_RestHealBonus { get; set; } = 1;
    public static int Max_RestHealBonus { get; set; } = 5;
    public static int Min_NegStartFrail { get; set; } = 1;
    public static int Max_NegStartFrail { get; set; } = 2;
    public static int Min_NegTurnLoseHp { get; set; } = 1;
    public static int Max_NegTurnLoseHp { get; set; } = 3;
    public static int Min_NegTurnEnergyDown { get; set; } = 1;
    public static int Max_NegTurnEnergyDown { get; set; } = 1;
    public static int Min_NegTurnDrawDown { get; set; } = 1;
    public static int Max_NegTurnDrawDown { get; set; } = 5;
    public static int Min_NegGoldDown { get; set; } = 1;
    public static int Max_NegGoldDown { get; set; } = 3;
    public static int Min_NegPotionBlock { get; set; } = 1;
    public static int Max_NegPotionBlock { get; set; } = 1;
    public static int Min_NegStartSloth { get; set; } = 1;
    public static int Max_NegStartSloth { get; set; } = 5;
    public static int Min_NegRestHealDown { get; set; } = 1;
    public static int Max_NegRestHealDown { get; set; } = 4;
    public static int Min_NegAttackDamageDown { get; set; } = 1;
    public static int Max_NegAttackDamageDown { get; set; } = 2;
    public static int Min_NegMaxHpDown { get; set; } = 1;
    public static int Max_NegMaxHpDown { get; set; } = 20;
    public static int Min_HandRetain { get; set; } = 1;
    public static int Max_HandRetain { get; set; } = 3;
    public static int Min_HandSly { get; set; } = 1;
    public static int Max_HandSly { get; set; } = 3;
    public static int Min_RetainEnergyDiscount { get; set; } = 1;
    public static int Max_RetainEnergyDiscount { get; set; } = 1;
    public static int Min_RetainAttackBuff { get; set; } = 1;
    public static int Max_RetainAttackBuff { get; set; } = 3;
    public static int Min_EnchantSharp { get; set; } = 1;
    public static int Max_EnchantSharp { get; set; } = 2;
    public static int Min_EnchantNimble { get; set; } = 1;
    public static int Max_EnchantNimble { get; set; } = 2;
    public static int Min_EnchantImbued { get; set; } = 1;
    public static int Max_EnchantImbued { get; set; } = 2;
    public static int Min_StanceWrathStart { get; set; } = 1;
    public static int Max_StanceWrathStart { get; set; } = 1;
    public static int Min_StanceCalmStart { get; set; } = 1;
    public static int Max_StanceCalmStart { get; set; } = 1;
    public static int Min_StanceDivinityStart { get; set; } = 1;
    public static int Max_StanceDivinityStart { get; set; } = 1;
    public static int Min_NegHandEthereal { get; set; } = 1;
    public static int Max_NegHandEthereal { get; set; } = 3;
    // ---------- Cost lookup (properties above ARE the storage) ----------

    /// <summary>
    /// Live cost table for the generator: resolves per-template cost/refund
    /// from THIS config class's static properties (Cost_<TEMPLATE> /
    /// Refund_<TEMPLATE>), falling back to catalog defaults. Property lookup
    /// by name - no hand-maintained dictionary.
    /// </summary>
    internal static ChaosPointCosts PointCosts { get; } = new(
        LookUpCost, LookUpRefund);

    private static readonly System.Reflection.PropertyInfo[] CostProps =
        typeof(AutoAnthonyRelicsConfig).GetProperties();

    private static int? LookUpCost(string template)
    {
        var prop = System.Array.Find(CostProps,
            p => p.Name == "Cost_" + template && p.PropertyType == typeof(int));
        return prop?.GetValue(null) as int?;
    }

    // ---------- Template Min/Max bounds (budget editor, user order 2026-09-11) ----------

    /// <summary>
    /// Per-template Min/Max amount bounds set from the visual budget editor
    /// (BudgetEditorPanel). Not SimpleModConfig sliders - persisted via
    /// BaseLib's config JSON through the generic property mechanism:
    /// we expose Min_<T>/Max_<T> as int properties discovered by name,
    /// mirroring Cost_<T>/Refund_<T>. Generator reads via ApplyUserBounds.
    /// Tier-1 MP determinism: must match on both ends.
    /// </summary>
    internal static void SetTemplateBounds(string template, int min, int max)
    {
        if (min > max)
        {
            (min, max) = (max, min);
        }
        SetBoundProp("Min_", template, min);
        SetBoundProp("Max_", template, max);
    }

    private static void SetBoundProp(string prefix, string template, int value)
    {
        var prop = System.Array.Find(CostProps,
            p => p.Name == prefix + template && p.PropertyType == typeof(int));
        prop?.SetValue(null, value);
    }

    /// <summary>
    /// Overlay user Min/Max bounds on a catalog spec (null-safe: no bound
    /// properties exist for a template until the editor first moves a
    /// slider; then BaseLib persists them like every other property).
    /// </summary>
    internal static Chaos.ChaosRelicCatalog.TemplateSpec ApplyUserBounds(
        Chaos.ChaosRelicCatalog.TemplateSpec spec)
    {
        int min = LookUpBound("Min_", spec.Template) ?? spec.Min;
        int max = LookUpBound("Max_", spec.Template) ?? spec.Max;
        if (min > max)
        {
            (min, max) = (max, min);
        }
        if (min == spec.Min && max == spec.Max)
        {
            return spec;
        }
        return spec with { Min = min, Max = max };
    }

    private static int? LookUpBound(string prefix, string template)
    {
        var prop = System.Array.Find(CostProps,
            p => p.Name == prefix + template && p.PropertyType == typeof(int));
        return prop?.GetValue(null) as int?;
    }

    private static int? LookUpRefund(string template)
    {
        var prop = System.Array.Find(CostProps,
            p => p.Name == "Refund_" + template && p.PropertyType == typeof(int));
        return prop?.GetValue(null) as int?;
    }
}
