using System;
using System.Collections.Generic;
using System.Linq;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// Single resolution point for chaos relic templates across BOTH pools
/// (core + extra).
///
/// Why this exists: the core catalog and the extra catalog each used to be
/// consulted independently, so every consumer had to remember which pool a
/// template came from. Two call sites forgot - <see cref="ChaosPointCosts"/>
/// (threw InvalidOperationException on any extra-pool template while
/// EnableExtraPool was on) and <c>ChaosRelicCatalog.CheapestPositiveUnit</c>
/// (computed the budget floor from core positives only, so the generator
/// could conclude nothing was affordable while extra positives were). Both
/// failures were reproduced by the isolated probe on 2026-09-12.
///
/// Every consumer now goes through this class:
///   - <see cref="Spec"/>        raw catalog spec, no user overlay
///   - <see cref="Effective"/>   catalog spec with the user Min/Max bounds applied
///   - <see cref="PositiveTemplates"/> / <see cref="NegativeTemplates"/>
///                               the ACTIVE pool (extra templates join only
///                               while EnableExtraPool is on)
///   - <see cref="CheapestPositiveUnit"/> / <see cref="CheapestPositiveFloor"/>
///                               budget floor, computed over the active pool
/// </summary>
internal static class ChaosTemplates
{
    /// <summary>Does this template id exist in either pool?</summary>
    internal static bool Has(string template) =>
        ChaosRelicCatalog.HasTemplate(template) || ChaosRelicExtraCatalog.HasTemplate(template);

    /// <summary>Raw spec from whichever pool owns the template. Throws on unknown ids.</summary>
    internal static ChaosRelicCatalog.TemplateSpec Spec(string template) =>
        ChaosRelicExtraCatalog.HasTemplate(template)
            ? ChaosRelicExtraCatalog.Spec(template)
            : ChaosRelicCatalog.Spec(template);

    /// <summary>Raw spec with the user's Min/Max bounds overlay applied.</summary>
    internal static ChaosRelicCatalog.TemplateSpec Effective(string template) =>
        QuriousCraftingRelicsConfig.ApplyUserBounds(Spec(template));

    /// <summary>Negative lookup across both pools.</summary>
    internal static bool IsNegative(string template) =>
        ChaosRelicExtraCatalog.HasTemplate(template)
            ? ChaosRelicExtraCatalog.IsNegative(template)
            : ChaosRelicCatalog.IsNegative(template);

    /// <summary>Watcher-mod presence probe (assembly by name).</summary>
    internal static bool WatcherModLoaded =>
        AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Watcher");

    /// <summary>
    /// Active positive pool: core always, extra only while the extra pool is
    /// enabled, stance templates only while the Watcher mod is loaded.
    /// Deterministically ordered (core order first, then extra order) because
    /// the generator indexes this list with the seeded RNG.
    /// </summary>
    internal static IReadOnlyList<string> PositiveTemplates =>
        (QuriousCraftingRelicsConfig.EnableExtraPool
            ? ChaosRelicCatalog.PositiveTemplates.Concat(ChaosRelicExtraCatalog.PositiveTemplates)
            : ChaosRelicCatalog.PositiveTemplates)
        .Where(t => !ChaosRelicExtraCatalog.WatcherTemplates.Contains(t) || WatcherModLoaded)
        .ToList();

    /// <summary>Active negative pool: core always, extra only while enabled.</summary>
    internal static IReadOnlyList<string> NegativeTemplates =>
        QuriousCraftingRelicsConfig.EnableExtraPool
            ? ChaosRelicCatalog.NegativeTemplates.Concat(ChaosRelicExtraCatalog.NegativeTemplates).ToList()
            : ChaosRelicCatalog.NegativeTemplates;

    /// <summary>
    /// Cheapest single unit of any positive template, using LIVE per-point
    /// prices over the ACTIVE pool. One unit is the template's minimum amount.
    /// </summary>
    internal static int CheapestPositiveUnit(ChaosPointCosts costs)
    {
        int cheapest = int.MaxValue;
        foreach (var template in PositiveTemplates)
        {
            cheapest = Math.Min(cheapest, costs.CostPerPoint(template));
        }
        return cheapest == int.MaxValue ? 1 : cheapest;
    }

    /// <summary>
    /// Cheapest amount of points that buys at least one complete positive
    /// entry: min over the active pool of Cost(spec.Min). This is the hard
    /// budget floor - a relic budget below it could not produce a single
    /// positive entry, which is the degenerate configuration the probe
    /// reproduced (budget 1 with every positive priced 20 produced 60 empty
    /// relics).
    ///
    /// CONTRACT (explicit, not a silent clamp): every generated relic carries
    /// at least one positive entry. A configured budget below this floor is
    /// raised to the floor for generation. The user-facing budget sliders stay
    /// untouched; the raise happens inside the generator and is logged once
    /// per generation when it actually triggers.
    /// </summary>
    internal static int CheapestPositiveFloor(ChaosPointCosts costs)
    {
        int floor = int.MaxValue;
        foreach (var template in PositiveTemplates)
        {
            var spec = Effective(template);
            floor = Math.Min(floor, PriceOf(spec, costs, spec.Min));
        }
        return floor == int.MaxValue ? 1 : Math.Max(1, floor);
    }

    /// <summary>
    /// Point price of an amount for a spec, using the LIVE per-point table and
    /// the spec's own pricing shape (triangular when Decaying).
    /// </summary>
    internal static int PriceOf(ChaosRelicCatalog.TemplateSpec spec, ChaosPointCosts costs, int amount) =>
        spec.Decaying
            ? costs.CostPerPoint(spec.Template) * amount * (amount + 1) / 2
            : costs.CostPerPoint(spec.Template) * amount;

    /// <summary>
    /// Points REFUNDED by an amount of a negative spec, using the LIVE
    /// per-point table. Mirrors <see cref="PriceOf"/> for the negative side:
    /// <see cref="ChaosPointCosts.CostPerPoint"/> deliberately returns 0 for
    /// negatives, so a consumer that priced a negative through PriceOf would
    /// silently show a 0-point refund. Refunds are linear (see
    /// <c>TemplateSpec.Refund</c>); no negative template is Decaying.
    /// </summary>
    internal static int RefundOf(ChaosRelicCatalog.TemplateSpec spec, ChaosPointCosts costs, int amount) =>
        costs.RefundPerPoint(spec.Template) * amount;
}
