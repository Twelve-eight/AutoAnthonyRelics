using System;
using System.Collections.Generic;
using System.Linq;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// Explicit generation context (R04-02): every generation input the query
/// surface needs, carried as a value instead of being read from the process-wide
/// <see cref="ChaosRelicRunRegistry.CurrentSnapshot"/>.
///
/// WHY: menu previews and foreign-seed queries used to be served from whatever
/// snapshot happened to be installed globally - including a snapshot retained
/// after a run ended (reproduced by registry-baseline-repro.json:
/// snapshotCleared=false, foreignUsesLive=false, menuUsesLive=false). Temporarily
/// swapping the global to serve such a query would be a cross-thread write to a
/// shared field. A context is passed down instead, so a query can never observe
/// another run's frozen inputs.
///
/// The ACTIVE run's context is built once per frozen/restored snapshot
/// (<see cref="ForSnapshot"/>) and its lookups are dictionary hits on the
/// snapshot's precomposed tables - zero allocation, unchanged from QCR-1.
/// <see cref="Live"/> re-reads live config on every construction and is what
/// menus/previews use.
/// </summary>
internal sealed class GenerationContext
{
    private readonly QuriousGenerationSnapshot? _snapshot;
    private readonly IReadOnlyList<string> _positives;
    private readonly IReadOnlyList<string> _negatives;

    private GenerationContext(
        QuriousGenerationSnapshot? snapshot,
        IReadOnlyList<string> positives,
        IReadOnlyList<string> negatives)
    {
        _snapshot = snapshot;
        _positives = positives;
        _negatives = negatives;
    }

    /// <summary>Context of a frozen (active run) or freshly captured (live) snapshot.</summary>
    internal static GenerationContext ForSnapshot(QuriousGenerationSnapshot snapshot) =>
        new(snapshot, snapshot.ActivePositiveTemplates, snapshot.ActiveNegativeTemplates);

    /// <summary>
    /// Context built from LIVE config, never from a retained run. Sampling order
    /// is rebuilt exactly as the pre-QCR-1 live getters did.
    /// </summary>
    internal static GenerationContext Live()
    {
        bool extraPool = QuriousCraftingRelicsConfig.EnableExtraPool;
        return new GenerationContext(
            null,
            ChaosTemplates.BuildLivePositiveTemplates(extraPool, ChaosTemplates.WatcherModLoadedProbe),
            ChaosTemplates.BuildLiveNegativeTemplates(extraPool));
    }

    /// <summary>
    /// Context of the ACTIVE run, or the live one outside a run. Only the
    /// active snapshot is consulted - a run that has ended clears it
    /// (<see cref="ChaosRelicRunRegistry.ClearActiveRun"/>), so this cannot
    /// serve a previous run's inputs to a menu.
    /// </summary>
    internal static GenerationContext Ambient =>
        ChaosRelicRunRegistry.CurrentSnapshot is { } snapshot
            ? ForSnapshot(snapshot)
            : Live();

    /// <summary>Frozen snapshot this context came from; null for live.</summary>
    internal QuriousGenerationSnapshot? Snapshot => _snapshot;

    /// <summary>Active positive pool in sampling order for this context.</summary>
    internal IReadOnlyList<string> Positives => _positives;

    /// <summary>Active negative pool in sampling order for this context.</summary>
    internal IReadOnlyList<string> Negatives => _negatives;

    internal int BudgetCommon => _snapshot?.BudgetCommon ?? QuriousCraftingRelicsConfig.ChaosRelicBudgetCommon;
    internal int BudgetUncommon => _snapshot?.BudgetUncommon ?? QuriousCraftingRelicsConfig.ChaosRelicBudgetUncommon;
    internal int BudgetRare => _snapshot?.BudgetRare ?? QuriousCraftingRelicsConfig.ChaosRelicBudgetRare;
    internal int NegativeChanceCommon =>
        _snapshot?.NegativeChanceCommon ?? QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceCommon;
    internal int NegativeChanceUncommon =>
        _snapshot?.NegativeChanceUncommon ?? QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceUncommon;
    internal int NegativeChanceRare =>
        _snapshot?.NegativeChanceRare ?? QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceRare;

    /// <summary>Frozen per-point table inside a run, live table otherwise.</summary>
    internal ChaosPointCosts Costs => _snapshot?.FrozenCosts ?? QuriousCraftingRelicsConfig.PointCosts;

    /// <summary>Watcher-mod presence, run-frozen inside a run.</summary>
    internal bool WatcherModLoaded => _snapshot?.WatcherModLoaded ?? ChaosTemplates.WatcherModLoadedProbe;
}

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
///   - <see cref="Effective(string)"/>   catalog spec with the user Min/Max bounds applied
///   - <see cref="PositiveTemplates"/> / <see cref="NegativeTemplates"/>
///                               the ACTIVE pool (extra templates join only
///                               while EnableExtraPool is on)
///   - <see cref="CheapestPositiveUnit"/> / <see cref="CheapestPositiveFloor"/>
///                               budget floor, computed over the active pool
///
/// R04-02: the pool/spec/bounds queries that depend on run state take an
/// explicit <see cref="GenerationContext"/>; the parameterless members remain
/// for catalog-only consumers and delegate to the AMBIENT context (the active
/// run, else live config). Generation itself always runs on an explicit
/// context (see <see cref="ChaosRelicGenerator.Generate(string, int, int, int,
/// ChaosPointCosts, int, int, int, GenerationContext)"/>).
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

    /// <summary>
    /// Raw spec with the context's Min/Max bounds overlay applied: the frozen
    /// snapshot's precomputed immutable map inside a run, live config otherwise.
    /// QCR-1: with a frozen context the effective spec is a dictionary hit - no
    /// per-call record allocation, no live-config read.
    /// </summary>
    internal static ChaosRelicCatalog.TemplateSpec Effective(string template, GenerationContext context)
    {
        if (context.Snapshot?.EffectiveSpecFor(template) is { } frozen)
        {
            return frozen;
        }
        return QuriousCraftingRelicsConfig.ApplyUserBounds(Spec(template));
    }

    /// <summary>Ambient (active run, else live) variant of <see cref="Effective(string, GenerationContext)"/>.</summary>
    internal static ChaosRelicCatalog.TemplateSpec Effective(string template) =>
        Effective(template, GenerationContext.Ambient);

    /// <summary>Negative lookup across both pools.</summary>
    internal static bool IsNegative(string template) =>
        ChaosRelicExtraCatalog.HasTemplate(template)
            ? ChaosRelicExtraCatalog.IsNegative(template)
            : ChaosRelicCatalog.IsNegative(template);

    /// <summary>Raw assembly probe, no snapshot. Use <see cref="WatcherModLoaded"/> for generation.</summary>
    internal static bool WatcherModLoadedProbe =>
        AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Watcher");

    /// <summary>
    /// Extra-pool gate, run-frozen while a run is ACTIVE (the active template
    /// lists must not reshuffle mid-run), live config otherwise (menus). A
    /// retained-but-inactive context never answers this: the active snapshot is
    /// cleared when the run ends.
    /// </summary>
    private static bool ExtraPoolActive =>
        ChaosRelicRunRegistry.CurrentSnapshot?.EnableExtraPool ?? QuriousCraftingRelicsConfig.EnableExtraPool;

    /// <summary>Watcher-mod presence, run-frozen while a run is active.</summary>
    internal static bool WatcherModLoaded =>
        ChaosRelicRunRegistry.CurrentSnapshot?.WatcherModLoaded ?? WatcherModLoadedProbe;

    /// <summary>
    /// Active positive pool: core always, extra only while the extra pool is
    /// enabled, stance templates only while the Watcher mod is loaded.
    /// Deterministically ordered (core order first, then extra order) because
    /// the generator indexes this list with the seeded RNG.
    /// QCR-1: with a frozen snapshot this returns the snapshot's prebuilt
    /// sampling-order list - no Concat/Where/ToList per access and no Watcher
    /// assembly probe. Without a snapshot (menus/previews, before any run) the
    /// live list is rebuilt from live config on each access, so preview inputs
    /// stay separately invalidated. Content is identical to the pre-QCR-1
    /// construction for the same state.
    /// </summary>
    internal static IReadOnlyList<string> PositiveTemplates =>
        ChaosRelicRunRegistry.CurrentSnapshot?.ActivePositiveTemplates
        ?? BuildLivePositiveTemplates(ExtraPoolActive, WatcherModLoaded);

    internal static IReadOnlyList<string> BuildLivePositiveTemplates(bool extraPoolActive, bool watcherLoaded) =>
        (extraPoolActive
            ? ChaosRelicCatalog.PositiveTemplates.Concat(ChaosRelicExtraCatalog.PositiveTemplates)
            : ChaosRelicCatalog.PositiveTemplates)
        .Where(t => !ChaosRelicExtraCatalog.WatcherTemplates.Contains(t) || watcherLoaded)
        .ToList();

    /// <summary>
    /// Active negative pool: core always, extra only while enabled (no Watcher
    /// filter - matches the historical contract). Frozen sampling-order list
    /// while a run snapshot exists; live rebuild otherwise.
    /// </summary>
    internal static IReadOnlyList<string> NegativeTemplates =>
        ChaosRelicRunRegistry.CurrentSnapshot?.ActiveNegativeTemplates
        ?? BuildLiveNegativeTemplates(ExtraPoolActive);

    internal static IReadOnlyList<string> BuildLiveNegativeTemplates(bool extraPoolActive) =>
        extraPoolActive
            ? ChaosRelicCatalog.NegativeTemplates.Concat(ChaosRelicExtraCatalog.NegativeTemplates).ToList()
            : ChaosRelicCatalog.NegativeTemplates;

    /// <summary>
    /// Cheapest single unit of any positive template, using the context's
    /// per-point prices over the context's active pool. One unit is the
    /// template's minimum amount.
    /// </summary>
    internal static int CheapestPositiveUnit(ChaosPointCosts costs, GenerationContext context)
    {
        int cheapest = int.MaxValue;
        foreach (var template in context.Positives)
        {
            cheapest = Math.Min(cheapest, costs.CostPerPoint(template));
        }
        return cheapest == int.MaxValue ? 1 : cheapest;
    }

    /// <summary>
    /// Cheapest amount of points that buys at least one complete positive
    /// entry: min over the context's active pool of Cost(spec.Min). This is the
    /// hard budget floor - a relic budget below it could not produce a single
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
    internal static int CheapestPositiveFloor(ChaosPointCosts costs, GenerationContext context)
    {
        int floor = int.MaxValue;
        foreach (var template in context.Positives)
        {
            var spec = Effective(template, context);
            floor = Math.Min(floor, PriceOf(spec, costs, spec.Min));
        }
        return floor == int.MaxValue ? 1 : Math.Max(1, floor);
    }

    /// <summary>
    /// Point price of an amount for a spec, using the given per-point table and
    /// the spec's own pricing shape (triangular when Decaying).
    /// </summary>
    internal static int PriceOf(ChaosRelicCatalog.TemplateSpec spec, ChaosPointCosts costs, int amount) =>
        spec.Decaying
            ? costs.CostPerPoint(spec.Template) * amount * (amount + 1) / 2
            : costs.CostPerPoint(spec.Template) * amount;

    /// <summary>
    /// Points REFUNDED by an amount of a negative spec, using the given
    /// per-point table. Mirrors <see cref="PriceOf"/> for the negative side:
    /// <see cref="ChaosPointCosts.CostPerPoint"/> deliberately returns 0 for
    /// negatives, so a consumer that priced a negative through PriceOf would
    /// silently show a 0-point refund. Refunds are linear (see
    /// <c>TemplateSpec.Refund</c>); no negative template is Decaying.
    /// </summary>
    internal static int RefundOf(ChaosRelicCatalog.TemplateSpec spec, ChaosPointCosts costs, int amount) =>
        costs.RefundPerPoint(spec.Template) * amount;
}