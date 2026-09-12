using System;
using System.Collections.Generic;
using System.Linq;
using BaseLib.Config;
using QuriousCraftingRelics.Chaos;

namespace QuriousCraftingRelics;

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
internal class QuriousCraftingRelicsConfig : SimpleModConfig
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

    [ConfigSection("Budget_Section")]
    [ConfigSlider(1, 60, 1)]
    public static int ChaosRelicBudgetCommon { get; set; } = 10;

    [ConfigSlider(1, 80, 1)]
    public static int ChaosRelicBudgetUncommon { get; set; } = 14;

    [ConfigSlider(1, 120, 1)]
    public static int ChaosRelicBudgetRare { get; set; } = 23;

    // ---------- Negative-entry chances (percent) by rarity ----------

    [ConfigSlider(0, 100, 5)]
    public static int ChaosRelicNegativeChanceCommon { get; set; } = 5;

    [ConfigSlider(0, 100, 5)]
    public static int ChaosRelicNegativeChanceUncommon { get; set; } = 20;

    [ConfigSlider(0, 100, 5)]
    public static int ChaosRelicNegativeChanceRare { get; set; } = 75;

    // ======================================================================
    // Per-template point settings (in-game, collapsible sections).
    // BaseLib ConfigSection renders as a collapsible group, so the ~50
    // template cost sliders live collapsed under three headers instead of
    // flooding the settings list. Property names map to loc keys
    // QURIOUSCRAFTINGRELICS-COST_<TEMPLATE>.title; the value feeds the SAME
    // ChaosPointCosts lookup the cfg-file Cost_<TEMPLATE> keys use (these
    // properties ARE the config values - no reflection bridge needed).
    //
    // NAMING RULE (v0.6.0, do not "simplify" back to ALL_CAPS): the property
    // name must be the Title_Snake form of the template id, because BaseLib
    // slugifies the property name to build the loc key and its slugifier is
    // lossy on ALL_CAPS_SNAKE (see ConfigKeyNaming for the measurement). The
    // lookup side therefore runs every template id through
    // ConfigKeyNaming.TitleSnake instead of concatenating the raw id.
    // ======================================================================

    // ---------- Section: positive core-pool costs ----------

    [ConfigSection("Cost_Positive_Section")]
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Damage_All { get; set; } = 2;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Block { get; set; } = 2;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Strength { get; set; } = 9;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Dexterity { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Draw { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Energy { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Vuln_All { get; set; } = 9;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Weak_All { get; set; } = 9;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Regen { get; set; } = 3; // 衰减: 三角计价 2*N(N+1)/2
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Thorns { get; set; } = 3;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Artifact { get; set; } = 12;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Poison_All { get; set; } = 2; // 衰减: 三角计价 1*N(N+1)/2
    [ConfigSlider(1, 20, 1)]
    public static int Cost_C_Start_Plating { get; set; } = 3; // 衰减: 三角计价 2*N(N+1)/2
    [ConfigSlider(1, 20, 1)]
    public static int Cost_T_Start_Block { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_T_Start_Energy { get; set; } = 12;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_T_Start_Heal { get; set; } = 12;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_T_Start_Draw { get; set; } = 12;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_Play_Damage_Random { get; set; } = 3;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_Play_Block { get; set; } = 3;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_Passive_Attack_Damage { get; set; } = 8;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_Passive_Max_Energy { get; set; } = 8;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_Passive_Block_Add { get; set; } = 7;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_Victory_Heal { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_Victory_Gold { get; set; } = 1;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_Passive_Gold_Gain { get; set; } = 2;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_Rest_Heal_Bonus { get; set; } = 1;

    // ---------- Section: negative core-pool refunds ----------

    [ConfigSection("Refund_Negative_Section")]
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_Start_Frail_Self { get; set; } = 6;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_Turn_Lose_Hp { get; set; } = 11;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_Turn_Energy_Down { get; set; } = 12;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_Turn_Draw_Down { get; set; } = 12;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_Gold_Down { get; set; } = 2;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_Potion_Block { get; set; } = 30;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_Start_Sloth_Self { get; set; } = 13; // 每点返还 6N; 上限=7-N 张牌
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_Rest_Heal_Down { get; set; } = 1;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_Attack_Damage_Down { get; set; } = 7;
    [ConfigSlider(1, 40, 1)]
    public static int Refund_N_Max_Hp_Down { get; set; } = 6;

    // ---------- Section: extra pool (costs AND refunds) ----------

    [ConfigSection("Cost_Extra_Section")]
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_Hand_Retain { get; set; } = 5;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_Hand_Sly { get; set; } = 5;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_Retain_Energy_Discount { get; set; } = 5;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_Retain_Attack_Buff { get; set; } = 5;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_Enchant_Sharp { get; set; } = 5;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_Enchant_Nimble { get; set; } = 5;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_Enchant_Imbued { get; set; } = 4;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_Stance_Wrath { get; set; } = 7;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_Stance_Calm { get; set; } = 7;
    [ConfigSlider(1, 20, 1)]
    public static int Cost_X_Stance_Divinity { get; set; } = 15;
    [ConfigSlider(1, 40, 1)]
    public static int Cost_X_Hand_Ethereal { get; set; } = 4;


    // ---------- Template bounds (budget editor storage; defaults = catalog) ----------

    [ConfigHideInUI]
    public static int Min_C_Start_Damage_All { get; set; } = 3;
    [ConfigHideInUI]
    public static int Max_C_Start_Damage_All { get; set; } = 8;
    [ConfigHideInUI]
    public static int Min_C_Start_Block { get; set; } = 4;
    [ConfigHideInUI]
    public static int Max_C_Start_Block { get; set; } = 10;
    [ConfigHideInUI]
    public static int Min_C_Start_Strength { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_C_Start_Strength { get; set; } = 10;
    [ConfigHideInUI]
    public static int Min_C_Start_Dexterity { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_C_Start_Dexterity { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_C_Start_Draw { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_C_Start_Draw { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_C_Start_Energy { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_C_Start_Energy { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_C_Start_Vuln_All { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_C_Start_Vuln_All { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_C_Start_Weak_All { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_C_Start_Weak_All { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_C_Start_Regen { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_C_Start_Regen { get; set; } = 4;
    [ConfigHideInUI]
    public static int Min_C_Start_Thorns { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_C_Start_Thorns { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_C_Start_Artifact { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_C_Start_Artifact { get; set; } = 2;
    [ConfigHideInUI]
    public static int Min_C_Start_Poison_All { get; set; } = 3;
    [ConfigHideInUI]
    public static int Max_C_Start_Poison_All { get; set; } = 6;
    [ConfigHideInUI]
    public static int Min_C_Start_Plating { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_C_Start_Plating { get; set; } = 4;
    [ConfigHideInUI]
    public static int Min_T_Start_Block { get; set; } = 2;
    [ConfigHideInUI]
    public static int Max_T_Start_Block { get; set; } = 5;
    [ConfigHideInUI]
    public static int Min_T_Start_Energy { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_T_Start_Energy { get; set; } = 1;
    [ConfigHideInUI]
    public static int Min_T_Start_Heal { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_T_Start_Heal { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_T_Start_Draw { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_T_Start_Draw { get; set; } = 1;
    [ConfigHideInUI]
    public static int Min_Play_Damage_Random { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_Play_Damage_Random { get; set; } = 4;
    [ConfigHideInUI]
    public static int Min_Play_Block { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_Play_Block { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_Passive_Attack_Damage { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_Passive_Attack_Damage { get; set; } = 8;
    [ConfigHideInUI]
    public static int Min_Passive_Max_Energy { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_Passive_Max_Energy { get; set; } = 1;
    [ConfigHideInUI]
    public static int Min_Passive_Block_Add { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_Passive_Block_Add { get; set; } = 2;
    [ConfigHideInUI]
    public static int Min_Victory_Heal { get; set; } = 2;
    [ConfigHideInUI]
    public static int Max_Victory_Heal { get; set; } = 8;
    [ConfigHideInUI]
    public static int Min_Victory_Gold { get; set; } = 5;
    [ConfigHideInUI]
    public static int Max_Victory_Gold { get; set; } = 20;
    [ConfigHideInUI]
    public static int Min_Passive_Gold_Gain { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_Passive_Gold_Gain { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_Rest_Heal_Bonus { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_Rest_Heal_Bonus { get; set; } = 5;
    [ConfigHideInUI]
    public static int Min_N_Start_Frail_Self { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_N_Start_Frail_Self { get; set; } = 2;
    [ConfigHideInUI]
    public static int Min_N_Turn_Lose_Hp { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_N_Turn_Lose_Hp { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_N_Turn_Energy_Down { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_N_Turn_Energy_Down { get; set; } = 1;
    [ConfigHideInUI]
    public static int Min_N_Turn_Draw_Down { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_N_Turn_Draw_Down { get; set; } = 5;
    [ConfigHideInUI]
    public static int Min_N_Gold_Down { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_N_Gold_Down { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_N_Potion_Block { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_N_Potion_Block { get; set; } = 1;
    [ConfigHideInUI]
    public static int Min_N_Start_Sloth_Self { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_N_Start_Sloth_Self { get; set; } = 2;
    [ConfigHideInUI]
    public static int Min_N_Rest_Heal_Down { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_N_Rest_Heal_Down { get; set; } = 4;
    [ConfigHideInUI]
    public static int Min_N_Attack_Damage_Down { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_N_Attack_Damage_Down { get; set; } = 2;
    [ConfigHideInUI]
    public static int Min_N_Max_Hp_Down { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_N_Max_Hp_Down { get; set; } = 20;
    [ConfigHideInUI]
    public static int Min_X_Hand_Retain { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Hand_Retain { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_X_Hand_Sly { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Hand_Sly { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_X_Retain_Energy_Discount { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Retain_Energy_Discount { get; set; } = 1;
    [ConfigHideInUI]
    public static int Min_X_Retain_Attack_Buff { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Retain_Attack_Buff { get; set; } = 3;
    [ConfigHideInUI]
    public static int Min_X_Enchant_Sharp { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Enchant_Sharp { get; set; } = 2;
    [ConfigHideInUI]
    public static int Min_X_Enchant_Nimble { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Enchant_Nimble { get; set; } = 2;
    [ConfigHideInUI]
    public static int Min_X_Enchant_Imbued { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Enchant_Imbued { get; set; } = 2;
    [ConfigHideInUI]
    public static int Min_X_Stance_Wrath { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Stance_Wrath { get; set; } = 1;
    [ConfigHideInUI]
    public static int Min_X_Stance_Calm { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Stance_Calm { get; set; } = 1;
    [ConfigHideInUI]
    public static int Min_X_Stance_Divinity { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Stance_Divinity { get; set; } = 1;
    [ConfigHideInUI]
    public static int Cost_X_Pickup_Sharp { get; set; } = 6;
    public static int Cost_X_Pickup_Nimble { get; set; } = 6;
    public static int Cost_X_Pickup_Imbued { get; set; } = 5;
    [ConfigHideInUI]
    public static int Min_X_Pickup_Sharp { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Pickup_Sharp { get; set; } = 16;
    [ConfigHideInUI]
    public static int Min_X_Pickup_Nimble { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Pickup_Nimble { get; set; } = 2;
    [ConfigHideInUI]
    public static int Min_X_Pickup_Imbued { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Pickup_Imbued { get; set; } = 2;
    [ConfigHideInUI]
    public static int Min_X_Hand_Ethereal { get; set; } = 1;
    [ConfigHideInUI]
    public static int Max_X_Hand_Ethereal { get; set; } = 3;
    // ---------- Cost / bounds lookup (the properties above ARE the storage) ----------

    /// <summary>
    /// Live cost table for the generator: resolves per-template cost/refund
    /// from THIS config class's static properties (Cost_&lt;TEMPLATE&gt; /
    /// Refund_&lt;TEMPLATE&gt;), falling back to catalog defaults. Property lookup
    /// by name - no hand-maintained dictionary.
    /// </summary>
    internal static ChaosPointCosts PointCosts { get; } = new(LookUpCost, LookUpRefund);

    /// <summary>
    /// Every static int property of this class, indexed by name ONCE. The
    /// previous implementation ran Array.Find over all ~200 properties on
    /// every lookup, and the generator performs several lookups per template
    /// per relic (60 relics per run, re-generated for every new seed), so the
    /// linear scan was on the hot path. Cost/Refund/Min/Max are the only
    /// consumers and all of them are int properties.
    /// </summary>
    private static readonly Dictionary<string, System.Reflection.PropertyInfo> IntPropsByName =
        typeof(QuriousCraftingRelicsConfig)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(int))
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    internal static int? LookUpInt(string name) =>
        IntPropsByName.TryGetValue(name, out var prop) ? (int?)prop.GetValue(null) : null;

    private static int? LookUpCost(string template) =>
        LookUpInt(ConfigKeyNaming.CostProperty(template));

    private static int? LookUpRefund(string template) =>
        LookUpInt(ConfigKeyNaming.RefundProperty(template));

    // ---------- Template Min/Max bounds (budget editor, user order 2026-09-11) ----------

    /// <summary>
    /// Per-template Min/Max amount bounds set from the visual budget editor
    /// (BudgetEditorPanel). Property names carry the TEMPLATE ID, not the C#
    /// constant name (Min_C_Start_Strength, not Min_StartStrength): the lookup
    /// key is built from the catalog's own template string, so a rename in the
    /// catalog can no longer silently orphan 94 config keys the way it did
    /// when the two naming schemes diverged (probe 2026-09-12: 0 of 94 keys
    /// resolved, so every editor slider wrote to a property nothing read).
    ///
    /// The id is Title_Snake-cased before the lookup - same reason as the
    /// Cost_/Refund_ pair above.
    ///
    /// [ConfigHideInUI] on each: these are editor storage, not settings. Without
    /// it BaseLib renders all 94 as sliders in the Mod Settings list.
    ///
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
        string propertyName = prefix + ConfigKeyNaming.TitleSnake(template);
        if (IntPropsByName.TryGetValue(propertyName, out var prop))
        {
            prop.SetValue(null, value);
        }
    }

    /// <summary>
    /// Overlay user Min/Max bounds on a catalog spec. Bounds properties exist
    /// for every template (defaults mirror the catalog), so this is a plain
    /// read; a missing property falls back to the catalog band.
    /// </summary>
    internal static Chaos.ChaosRelicCatalog.TemplateSpec ApplyUserBounds(
        Chaos.ChaosRelicCatalog.TemplateSpec spec)
    {
        int min = LookUpInt(ConfigKeyNaming.MinProperty(spec.Template)) ?? spec.Min;
        int max = LookUpInt(ConfigKeyNaming.MaxProperty(spec.Template)) ?? spec.Max;
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
}
