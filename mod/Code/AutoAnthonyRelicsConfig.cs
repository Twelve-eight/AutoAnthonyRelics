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
/// every template's cost/refund is individually tunable; defaults live in
/// ChaosRelicCatalog and can be overridden per template here.
///
/// MP DETERMINISM (Tier-1 keys, see sts2-mpconfigsync incident 2026-09-10):
/// budgets, negative chances, per-template costs and EnableChaosRelics MUST
/// match on both ends - they feed the seeded generator.
/// </summary>
[ConfigHoverTipsByDefault]
internal class AutoAnthonyRelicsConfig : SimpleModConfig
{
    /// <summary>Master switch. When false, chaos relics never generate or apply.</summary>
    public static bool EnableChaosRelics { get; set; } = true;

    /// <summary>
    /// Legacy entry-count key. v0.5 budget system ignores it (kept for save
    /// compat and registry call-shape stability).
    /// </summary>
    public static int ChaosRelicMultiplier { get; set; } = 3;

    // ---------- Point budgets by rarity ----------

    [ConfigSection("预算 / Budget")]
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

    // ---------- Per-template cost overrides ----------
    // Config key naming: Costs_<TEMPLATE>. Only non-default values need an
    // entry here; ChaosPointCosts falls back to the catalog default.

    /// <summary>
    /// Config-name -> cost/refund override map, built from the static
    /// Cost_<TEMPLATE> / Refund_<TEMPLATE> properties via reflection when
    /// the config registers. Avoids 37 hand-written properties in this file
    /// while keeping everything player-editable in the .cfg JSON.
    /// </summary>
    internal static readonly Dictionary<string, int> TemplateCostOverrides = new(StringComparer.Ordinal);
    internal static readonly Dictionary<string, int> TemplateRefundOverrides = new(StringComparer.Ordinal);

    /// <summary>
    /// Live cost table handed to the generator: reads overrides registered
    /// from the config file (SettingsUI reflects Cost_*/Refund_* keys).
    /// </summary>
    internal static ChaosPointCosts PointCosts { get; } = new(
        template => TemplateCostOverrides.TryGetValue(template, out var c) ? c : null,
        template => TemplateRefundOverrides.TryGetValue(template, out var r) ? r : null);

    /// <summary>
    /// Apply a Cost_<TEMPLATE> override from the config file. Called by the
    /// settings loader bridge; clamped to >= 1.
    /// </summary>
    internal static void SetCostOverride(string template, int value)
    {
        if (ChaosRelicCatalog.PositiveTemplates.Contains(template) && value > 0)
        {
            TemplateCostOverrides[template] = value;
        }
    }

    /// <summary>Apply a Refund_<TEMPLATE> override. Clamped to >= 1.</summary>
    internal static void SetRefundOverride(string template, int value)
    {
        if (ChaosRelicCatalog.NegativeTemplates.Contains(template) && value > 0)
        {
            TemplateRefundOverrides[template] = value;
        }
    }
}
