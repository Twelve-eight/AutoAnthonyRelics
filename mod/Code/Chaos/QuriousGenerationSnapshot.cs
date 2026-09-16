using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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
/// FROZEN LOOKUP CONTEXT (QCR-1, QCR-R4-02): the snapshot also carries
/// everything the warm lookup path used to rebuild on EVERY ForSeed hit -
/// the ordered active catalogs (sampling order), the effective immutable
/// template metadata, the canonical config fingerprint and the precomposed
/// cache key. Probes measured 7,208 bytes allocated per warm ForSeed call
/// because the fingerprint sorted and formatted all templates and the pool
/// getters rebuilt their lists each time; with the frozen context a warm hit
/// is a plain dictionary lookup and allocates nothing.
///
/// LIFECYCLE (per structure):
/// - Producer: QuriousGenerationSnapshot.Capture, called by
///   RunIdentityCapture.Capture when a run's identity has no retained context
///   (a new run, or the first capture of a save in this process).
/// - Owner: ChaosRelicRunRegistry.CurrentSnapshot (the active run) plus the
///   capture rule's bounded per-identity retention. Deliberately KEPT after
///   RunManager.CleanUp as continuation evidence (QCR-2026-09-14-01,
///   WS-0916-06) - returning to a save resumes its ORIGINAL context.
/// - First consumers: ChaosRelicRunRegistry.ForSeed / CurrentCacheKey,
///   ChaosTemplates' active-pool getters and Effective, then the generator
///   and the per-slot definition reads.
/// - Invalidation / cleanup point: replaced by the NEXT freeze (a new run, or a
///   save whose identity is not retained and whose retention slot was evicted).
///   There is no explicit cleanup - the kept snapshot is intentional (the
///   run-identity continuation contract); preview/menu definition lookups are
///   separately gated on CurrentRunSeed, so a kept snapshot cannot leak
///   definitions into menus.
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

    // ---------- Frozen lookup context (QCR-1) ----------

    /// <summary>
    /// Run seed this snapshot was frozen for, stamped by CaptureSeed. Part of
    /// the precomposed <see cref="CanonicalCacheKey"/>; immutable after
    /// capture. Null only if a future caller ever captures without a seed
    /// (CaptureSeed freezes only on a non-empty seed).
    /// </summary>
    public string? RunSeed { get; private init; }

    /// <summary>
    /// ACTIVE positive pool in CATALOG/SAMPLING order (core order first, then
    /// extra order; stance templates included only when the Watcher mod was
    /// loaded at capture). The generator indexes this list with the seeded
    /// RNG, so this order IS the sampling contract.
    /// This is deliberately NOT the fingerprint order (fingerprint entries are
    /// sorted by template id) - the two orderings are separate contracts and
    /// must never be conflated (QCR-1 requirement 1).
    /// </summary>
    public IReadOnlyList<string> ActivePositiveTemplates { get; private init; } =
        Array.Empty<string>();

    /// <summary>
    /// ACTIVE negative pool in catalog/sampling order (no Watcher filter -
    /// mirrors the live negative getter). Same sampling contract as
    /// <see cref="ActivePositiveTemplates"/>.
    /// </summary>
    public IReadOnlyList<string> ActiveNegativeTemplates { get; private init; } =
        Array.Empty<string>();

    /// <summary>
    /// Effective immutable template metadata: raw catalog spec with the frozen
    /// Min/Max bounds overlay applied (identity-preserving when the band is
    /// unchanged - exactly what the previous per-call
    /// ChaosTemplates.Effective produced inside a run). Pricing stays in
    /// <see cref="FrozenCosts"/>; these specs carry the frozen BANDS only.
    /// </summary>
    private IReadOnlyDictionary<string, ChaosRelicCatalog.TemplateSpec> EffectiveSpecs { get; init; } =
        new Dictionary<string, ChaosRelicCatalog.TemplateSpec>();

    /// <summary>
    /// Canonical config fingerprint, built ONCE at capture. Byte-identical to
    /// the previous per-call ConfigFingerprint() construction for the same
    /// inputs. Template ids appear here in SORTED (ordinal) order - fingerprint
    /// ordering, not sampling ordering.
    /// </summary>
    public string CanonicalFingerprint { get; private init; } = "";

    /// <summary>
    /// seed + '\0' + <see cref="CanonicalFingerprint"/>, precomposed at
    /// capture: the registry cache key for THIS run. Warm ForSeed /
    /// CurrentCacheKey hits return it directly - zero allocation.
    /// </summary>
    public string CanonicalCacheKey { get; private init; } = "";

    /// <summary>
    /// Frozen effective spec for a template id. With the full four-way union
    /// captured below, this hits for EVERY catalog template (core positives,
    /// core negatives, extra pool). Null means an id outside both catalogs -
    /// a defensive miss only; callers fall back to the old per-call overlay
    /// (two-catalog Spec() + ApplyUserBounds) instead of throwing.
    /// Allocation-free dictionary lookup.
    /// </summary>
    public ChaosRelicCatalog.TemplateSpec? EffectiveSpecFor(string template) =>
        EffectiveSpecs.TryGetValue(template, out var spec) ? spec : null;

    public static QuriousGenerationSnapshot Capture(string? runSeed)
    {
        // Enumerate the union of BOTH catalogs so a mid-run flag combination
        // can still resolve any template it activates. Order is HEAD's
        // baseline order (core positives, core negatives, extra positives,
        // extra negatives); the list only feeds the dictionaries below, so
        // content - not order - is the contract here.
        // REWORK 2026-09-15 (fixture qurious-lifecycle KeyNotFoundException
        // 'N_ATTACK_DAMAGE_DOWN'): the working tree this task started from
        // had DROPPED .Concat(ChaosRelicCatalog.NegativeTemplates) relative
        // to HEAD, starving Costs/Refunds/Bounds/EffectiveSpecs for the 10
        // core negatives. The frozen fingerprint then indexed a dict that
        // lacked them. HEAD's Capture always covered them, and the configured
        // Refund_ values (e.g. Refund_N_Attack_Damage_Down = 7 vs catalog 3)
        // feed generation - so full coverage is REQUIRED for byte-identity;
        // a miss-fallback alone would have silently drifted to catalog
        // defaults.
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

        var frozenCosts = new ChaosPointCosts(
            t => costs.TryGetValue(t, out int c) ? c : (int?)null,
            t => refunds.TryGetValue(t, out int r) ? r : (int?)null);

        // Gate values for THIS capture. Read as locals ONCE: the active-pool
        // builders below must NOT go through the ChaosTemplates getters, which
        // consult ChaosRelicRunRegistry.CurrentSnapshot - and during capture
        // that still points at the PREVIOUS run's snapshot (or none). The
        // published snapshot must agree with its own frozen lists.
        bool enableExtraPool = QuriousCraftingRelicsConfig.EnableExtraPool;
        bool watcherLoaded = ChaosTemplates.WatcherModLoadedProbe;

        // Active pools in SAMPLING order, with this snapshot's own gates.
        // Mirrors the previous live getters exactly (extra pool only while
        // enabled; stance templates filtered by Watcher presence on the
        // positives; no Watcher filter on negatives).
        var activePositives = (enableExtraPool
                ? ChaosRelicCatalog.PositiveTemplates.Concat(ChaosRelicExtraCatalog.PositiveTemplates)
                : ChaosRelicCatalog.PositiveTemplates)
            .Where(t => !ChaosRelicExtraCatalog.WatcherTemplates.Contains(t) || watcherLoaded)
            .ToList();
        var activeNegatives = enableExtraPool
            ? ChaosRelicCatalog.NegativeTemplates.Concat(ChaosRelicExtraCatalog.NegativeTemplates).ToList()
            : ChaosRelicCatalog.NegativeTemplates.ToList();

        // Effective immutable metadata: frozen bounds overlay on the raw spec,
        // identity-preserving when the band is unchanged (same shape as the
        // old in-run Effective).
        var effectiveSpecs = new Dictionary<string, ChaosRelicCatalog.TemplateSpec>(
            templates.Count, System.StringComparer.Ordinal);
        foreach (string template in templates)
        {
            var spec = ChaosTemplates.Spec(template);
            var (min, max) = bounds[template];
            effectiveSpecs[template] = (min == spec.Min && max == spec.Max)
                ? spec
                : spec with { Min = min, Max = max };
        }

        string fingerprint = BuildFingerprint(
            QuriousCraftingRelicsConfig.ChaosRelicBudgetCommon,
            QuriousCraftingRelicsConfig.ChaosRelicBudgetUncommon,
            QuriousCraftingRelicsConfig.ChaosRelicBudgetRare,
            QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceCommon,
            QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceUncommon,
            QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceRare,
            enableExtraPool,
            watcherLoaded,
            frozenCosts,
            activePositives,
            activeNegatives,
            effectiveSpecs);

        return new QuriousGenerationSnapshot
        {
            Multiplier = QuriousCraftingRelicsConfig.ChaosRelicMultiplier,
            BudgetCommon = QuriousCraftingRelicsConfig.ChaosRelicBudgetCommon,
            BudgetUncommon = QuriousCraftingRelicsConfig.ChaosRelicBudgetUncommon,
            BudgetRare = QuriousCraftingRelicsConfig.ChaosRelicBudgetRare,
            NegativeChanceCommon = QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceCommon,
            NegativeChanceUncommon = QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceUncommon,
            NegativeChanceRare = QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceRare,
            EnableExtraPool = enableExtraPool,
            WatcherModLoaded = watcherLoaded,
            Costs = costs,
            Refunds = refunds,
            Bounds = bounds,
            FrozenCosts = frozenCosts,
            RunSeed = runSeed,
            ActivePositiveTemplates = activePositives,
            ActiveNegativeTemplates = activeNegatives,
            EffectiveSpecs = effectiveSpecs,
            CanonicalFingerprint = fingerprint,
            CanonicalCacheKey = (runSeed ?? "") + "\0" + fingerprint,
        };
    }

    /// <summary>
    /// Canonical fingerprint construction - byte-identical to the previous
    /// per-call ChaosRelicRunRegistry.ConfigFingerprint for the same inputs.
    /// Template ids are SORTED here (fingerprint order); the sampling order of
    /// the active catalogs is a different contract and is never used for the
    /// fingerprint.
    /// </summary>
    private static string BuildFingerprint(
        int budgetCommon, int budgetUncommon, int budgetRare,
        int negativeChanceCommon, int negativeChanceUncommon, int negativeChanceRare,
        bool enableExtraPool, bool watcherLoaded,
        ChaosPointCosts costs,
        IReadOnlyList<string> activePositives, IReadOnlyList<string> activeNegatives,
        IReadOnlyDictionary<string, ChaosRelicCatalog.TemplateSpec> effectiveSpecs)
    {
        var sb = new StringBuilder(256);
        sb.Append(budgetCommon).Append('/')
          .Append(budgetUncommon).Append('/')
          .Append(budgetRare).Append('/')
          .Append(negativeChanceCommon).Append('/')
          .Append(negativeChanceUncommon).Append('/')
          .Append(negativeChanceRare).Append('/')
          .Append(enableExtraPool ? '1' : '0').Append('/')
          .Append(watcherLoaded ? '1' : '0');
        // Per-template economics and bounds, in SORTED template-id order so
        // the fingerprint does not depend on collection iteration order.
        var ordered = new List<string>(activePositives);
        ordered.AddRange(activeNegatives);
        ordered.Sort(StringComparer.Ordinal);
        foreach (var template in ordered)
        {
            // Belt-and-braces only: with the full four-way union above, every
            // template in the active pools is in effectiveSpecs, so this
            // fallback must never fire. If a future catalog/union divergence
            // ever produces an active id without a frozen entry, degrade to
            // the old per-call semantics (two-catalog Spec() + live
            // ApplyUserBounds, the exact shape ChaosTemplates.Effective used
            // pre-QCR-1) instead of throwing KeyNotFoundException.
            var spec = effectiveSpecs.TryGetValue(template, out var frozenSpec)
                ? frozenSpec
                : QuriousCraftingRelicsConfig.ApplyUserBounds(ChaosTemplates.Spec(template));
            sb.Append('|').Append(template)
              .Append(':').Append(costs.CostPerPoint(template))
              .Append(':').Append(costs.RefundPerPoint(template))
              .Append(':').Append(spec.Min)
              .Append(':').Append(spec.Max);
        }
        return sb.ToString();
    }

    public (int Min, int Max)? BoundsFor(string template) =>
        Bounds.TryGetValue(template, out var bounds) ? bounds : null;
}
