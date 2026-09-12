using System.Collections.Generic;
using System.Linq;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// Run-effective generation config, frozen once at run-seed capture.
///
/// WHY (astra-advice 2026-09-12 item 5): the registry cache used to be keyed
/// by (seed, live-config fingerprint). Editing a budget mid-run changed the
/// fingerprint, the same seed regenerated a DIFFERENT pool, and every already
/// held relic, its text and its one-shot effects changed meaning - there was
/// no immutable per-run definition. Now the seed capture freezes every
/// generation input here; definitions are a pure function of
/// (seed, frozen snapshot). Preference edits only affect the NEXT run.
///
/// Multiplayer note: this snapshot is what the local process saw when the run
/// started. Keeping host/client pools identical still requires config sync to
/// deliver the host's values before the run starts (MpConfigSync's contract).
/// </summary>
public sealed class QuriousGenerationSnapshot
{
    public int Multiplier { get; private init; }
    public int BudgetCommon { get; private init; }
    public int BudgetUncommon { get; private init; }
    public int BudgetRare { get; private init; }
    public int NegativeChanceCommon { get; private init; }
    public int NegativeChanceUncommon { get; private init; }
    public int NegativeChanceRare { get; private init; }
    public bool EnableExtraPool { get; private init; }
    public bool WatcherModLoaded { get; private init; }

    private IReadOnlyDictionary<string, int> Costs { get; init; } =
        new Dictionary<string, int>();

    private IReadOnlyDictionary<string, int> Refunds { get; init; } =
        new Dictionary<string, int>();

    private IReadOnlyDictionary<string, (int Min, int Max)> Bounds { get; init; } =
        new Dictionary<string, (int, int)>();

    /// <summary>Frozen per-point price table, same shape as the live one.</summary>
    public ChaosPointCosts FrozenCosts { get; private init; } = null!;

    public static QuriousGenerationSnapshot Capture()
    {
        // Enumerate the union of both catalogs so a mid-run flag combination
        // can still resolve any template it activates.
        var templates = ChaosRelicCatalog.PositiveTemplates
            .Concat(ChaosRelicCatalog.NegativeTemplates)
            .Concat(ChaosRelicExtraCatalog.PositiveTemplates)
            .Concat(ChaosRelicExtraCatalog.NegativeTemplates)
            .Distinct()
            .ToList();

        var costs = new Dictionary<string, int>(templates.Count, System.StringComparer.Ordinal);
        var refunds = new Dictionary<string, int>(templates.Count, System.StringComparer.Ordinal);
        var bounds = new Dictionary<string, (int, int)>(templates.Count, System.StringComparer.Ordinal);
        foreach (string template in templates)
        {
            var spec = ChaosTemplates.Spec(template);
            costs[template] = QuriousCraftingRelicsConfig.LookUpInt(ConfigKeyNaming.CostProperty(template))
                ?? spec.CostPerPoint;
            refunds[template] = QuriousCraftingRelicsConfig.LookUpInt(ConfigKeyNaming.RefundProperty(template))
                ?? spec.RefundPerPoint;
            int min = QuriousCraftingRelicsConfig.LookUpInt(ConfigKeyNaming.MinProperty(template)) ?? spec.Min;
            int max = QuriousCraftingRelicsConfig.LookUpInt(ConfigKeyNaming.MaxProperty(template)) ?? spec.Max;
            if (min > max)
            {
                (min, max) = (max, min);
            }
            bounds[template] = (min, max);
        }

        return new QuriousGenerationSnapshot
        {
            Multiplier = QuriousCraftingRelicsConfig.ChaosRelicMultiplier,
            BudgetCommon = QuriousCraftingRelicsConfig.ChaosRelicBudgetCommon,
            BudgetUncommon = QuriousCraftingRelicsConfig.ChaosRelicBudgetUncommon,
            BudgetRare = QuriousCraftingRelicsConfig.ChaosRelicBudgetRare,
            NegativeChanceCommon = QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceCommon,
            NegativeChanceUncommon = QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceUncommon,
            NegativeChanceRare = QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceRare,
            EnableExtraPool = QuriousCraftingRelicsConfig.EnableExtraPool,
            WatcherModLoaded = ChaosTemplates.WatcherModLoadedProbe,
            Costs = costs,
            Refunds = refunds,
            Bounds = bounds,
            FrozenCosts = new ChaosPointCosts(
                t => costs.TryGetValue(t, out int c) ? c : (int?)null,
                t => refunds.TryGetValue(t, out int r) ? r : (int?)null),
        };
    }

    public (int Min, int Max)? BoundsFor(string template) =>
        Bounds.TryGetValue(template, out var bounds) ? bounds : null;
}
