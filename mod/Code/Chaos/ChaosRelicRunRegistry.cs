using System;
using System.Collections.Generic;
using System.Text;
using MegaCrit.Sts2.Core.Runs;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// Per-run registry of generated relic definitions. Keyed by the run seed
/// PLUS a fingerprint of every config value that feeds generation: same seed
/// AND same fingerprint -> same relic pool on both MP ends (deterministic
/// regeneration, the same contract AutoAnthony uses for its pool snapshots
/// minus transport). Lazily generated on first query; cache bounded to a few
/// runs.
///
/// Why the fingerprint (F04, probe 2026-09-12): the cache used to be keyed by
/// the seed alone while the generated content depends on the budgets, the
/// negative chances, the per-template costs, the extra-pool switch and the
/// Min/Max bounds. The probe changed every budget after a first generation for
/// the same seed and got back the OLD pool (cacheUnchanged=true) while a fresh
/// generator call produced a different one. In multiplayer that means a client
/// whose config sync arrives late keeps serving a pool generated from its own
/// pre-sync config, and the two ends diverge.
///
/// The fingerprint is a stable string hash over the generation inputs, so the
/// key is (seed, inputs) rather than (seed, first-writer-wins).
///
/// RUN-EFFECTIVE FREEZE (astra-advice 2026-09-12 item 5): inside a run every
/// generation input comes from <see cref="CurrentSnapshot"/>, frozen once at
/// seed capture - NOT from live config. Editing a budget mid-run therefore
/// leaves the (seed, inputs) key and the generated pool unchanged; the
/// already-obtained relics keep their identity for the whole run. Live config
/// is only consulted outside runs (menus/previews). The MP divergence above
/// still needs config sync to deliver host values before run start; the
/// freeze only guarantees THIS process cannot change its mind mid-run.
///
/// FROZEN WARM LOOKUP (QCR-1, QCR-R4-02): the snapshot precomposes the
/// canonical cache key, the ordered active catalogs and the effective template
/// metadata once at capture. A warm ForSeed/DefinitionFor hit inside the
/// active run is therefore a plain dictionary lookup with ZERO allocation -
/// no fingerprint string building, no list rebuild/sort, no Watcher assembly
/// probe. Menu/preview/foreign-seed queries rebuild the key from live state
/// on every call (allocations accepted there; separately invalidated, and
/// they never reuse a prior run's composed context).
///
/// LIFECYCLE of the bounded cache: Cache/Order are owned by this static class
/// for the whole process; entries are keyed by (seed, fingerprint), evicted
/// FIFO beyond CacheLimit, and survive CleanUp by design - keys can only be
/// reached with a matching seed, so a kept entry cannot leak into menus
/// (DefinitionFor returns null whenever CurrentRunSeed is null).
/// </summary>
public static class ChaosRelicRunRegistry
{
    private const int CacheLimit = 8;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IReadOnlyList<ChaosRelicDefinition>> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();

    /// <summary>
    /// Generation inputs frozen at run-seed capture; null outside runs.
    /// Set by the seed-tracking patches alongside <see cref="CurrentRunSeed"/>;
    /// the snapshot carries the precomposed cache key, ordered active catalogs
    /// and effective template metadata (QCR-1) and is only published after
    /// those are fully built. Deliberately kept after CleanUp as continuation
    /// evidence (see LastRunSeed).
    /// </summary>
    public static QuriousGenerationSnapshot? CurrentSnapshot { get; internal set; }

    /// <summary>
    /// Registry cache key for the CURRENT run (seed + config fingerprint).
    /// Also the loc-updater dedupe key: the definitions can change without the
    /// seed changing, and a seed-only skip left tooltips stale while effects
    /// drifted (2026-09-13 report).
    /// Active-run fast path (QCR-1): the key was precomposed on the frozen
    /// snapshot at capture, so this getter allocates nothing inside a run.
    /// Every other state (no snapshot, or seed/snapshot mismatch) composes the
    /// key from the current process state exactly as before.
    /// </summary>
    internal static string CurrentCacheKey
    {
        get
        {
            var snapshot = CurrentSnapshot;
            if (snapshot is not null && CurrentRunSeed is not null
                && string.Equals(snapshot.RunSeed, CurrentRunSeed, StringComparison.Ordinal))
            {
                return snapshot.CanonicalCacheKey;
            }
            return (CurrentRunSeed ?? "") + "\0" + ConfigFingerprint();
        }
    }

    /// <summary>Generation config fingerprint for the current process state.
    /// Active or kept snapshot: the canonical fingerprint frozen at capture
    /// (identical content to the previous per-call construction, no rebuild).
    /// Otherwise the live menu/preview build, which re-reads live config on
    /// every call so preview inputs stay separately invalidated.</summary>
    private static string ConfigFingerprint()
    {
        var snapshot = CurrentSnapshot;
        if (snapshot is not null)
        {
            return snapshot.CanonicalFingerprint;
        }
        return BuildLiveFingerprint();
    }

    /// <summary>
    /// Live (no snapshot) fingerprint build - the menu/preview path. Allocates
    /// by design: it must re-read live config each time so a preference edit
    /// between two preview lookups is never served from a stale composed key.
    /// Mirrors the pre-QCR-1 construction exactly (same fields, same order,
    /// sorted template ids, live point-cost table).
    /// </summary>
    private static string BuildLiveFingerprint()
    {
        var sb = new StringBuilder(256);
        sb.Append(QuriousCraftingRelicsConfig.ChaosRelicBudgetCommon).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicBudgetUncommon).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicBudgetRare).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceCommon).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceUncommon).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceRare).Append('/')
          .Append(QuriousCraftingRelicsConfig.EnableExtraPool ? '1' : '0').Append('/')
          .Append(ChaosTemplates.WatcherModLoaded ? '1' : '0');
        // Per-template economics and bounds. Ordered by template id so the
        // fingerprint does not depend on collection iteration order.
        var templates = new List<string>(ChaosTemplates.PositiveTemplates);
        templates.AddRange(ChaosTemplates.NegativeTemplates);
        templates.Sort(StringComparer.Ordinal);
        foreach (var template in templates)
        {
            var spec = ChaosTemplates.Effective(template);
            sb.Append('|').Append(template)
              .Append(':').Append(QuriousCraftingRelicsConfig.PointCosts.CostPerPoint(template))
              .Append(':').Append(QuriousCraftingRelicsConfig.PointCosts.RefundPerPoint(template))
              .Append(':').Append(spec.Min)
              .Append(':').Append(spec.Max);
        }
        return sb.ToString();
    }

    /// <summary>Frozen per-point table inside a run, live table otherwise.</summary>
    private static ChaosPointCosts Costs() =>
        CurrentSnapshot?.FrozenCosts ?? QuriousCraftingRelicsConfig.PointCosts;

    public static IReadOnlyList<ChaosRelicDefinition> ForSeed(string seed, int multiplier)
    {
        // ACTIVE-RUN FAST PATH (QCR-1 / QCR-R4-02): the canonical key was
        // precomposed on the frozen snapshot at capture. A warm hit performs
        // no string building, no list rebuild, no sorting and no assembly
        // probing - the astra round-4 typed-delegate probe measured 7,208
        // bytes/call for the old per-hit rebuild; the acceptance contract for
        // this path is 0 bytes after warm-up.
        var snapshot = CurrentSnapshot;
        if (snapshot is not null
            && string.Equals(snapshot.RunSeed, seed, StringComparison.Ordinal))
        {
            return Lookup(snapshot.CanonicalCacheKey, seed);
        }
        // MENU / PREVIEW / foreign-seed path: the key is rebuilt from the
        // current process state on every call. This never reuses a prior
        // run's composed context, so preview inputs are invalidated
        // independently of the frozen run context. (Allocations accepted and
        // reported separately from the warm in-run path.)
        return Lookup(seed + "\u0000" + ConfigFingerprint(), seed);
    }

    /// <summary>
    /// Cache lookup + bounded generation, shared by both key paths. The
    /// generation branch reads <see cref="CurrentSnapshot"/> under the lock,
    /// exactly as the pre-QCR-1 body did.
    /// </summary>
    private static IReadOnlyList<ChaosRelicDefinition> Lookup(string key, string seed)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
            var snapshot = CurrentSnapshot;
            var generated = ChaosRelicGenerator.Generate(seed,
                snapshot?.BudgetCommon ?? QuriousCraftingRelicsConfig.ChaosRelicBudgetCommon,
                snapshot?.BudgetUncommon ?? QuriousCraftingRelicsConfig.ChaosRelicBudgetUncommon,
                snapshot?.BudgetRare ?? QuriousCraftingRelicsConfig.ChaosRelicBudgetRare,
                Costs(),
                snapshot?.NegativeChanceCommon ?? QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceCommon,
                snapshot?.NegativeChanceUncommon ?? QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceUncommon,
                snapshot?.NegativeChanceRare ?? QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceRare);
            Cache[key] = generated;
            Order.Enqueue(key);
            while (Order.Count > CacheLimit)
            {
                Cache.Remove(Order.Dequeue());
            }
            return generated;
        }
    }

    /// <summary>The definition for a slot in the current run; null when the model's
    /// owner has no run seed yet (menus, previews). Inside the active run the
    /// ForSeed hit is allocation-free (frozen canonical key + bounded cache),
    /// so per-read DefinitionFor calls cost a dictionary lookup (QCR-1);
    /// consumers that read several amounts still resolve the definition once
    /// per operation (see ChaosRelicModel.SumOf).</summary>
    public static ChaosRelicDefinition? DefinitionFor(RelicModel relic, int slot)
    {
        string? seed = RunSeedOf(relic);
        if (seed is null)
        {
            return null;
        }
        var pool = ForSeed(seed, QuriousCraftingRelicsConfig.ChaosRelicMultiplier);
        return slot >= 0 && slot < pool.Count ? pool[slot] : null;
    }

    /// <summary>
    /// Run seed from the owning player's run state; null outside runs.
    /// NEVER touches RelicModel.Owner: that getter calls AssertMutable and
    /// THROWS CanonicalModelException on canonical (registry/menu) instances -
    /// which is exactly where ModelLocPatch invokes Localization at startup.
    /// Instead read the run seed from the global run context when present.
    /// </summary>
    public static string? RunSeedOf(RelicModel relic)
    {
        return CurrentRunSeed;
    }

    /// <summary>
    /// The active run's seed. ResolveAndRun/GameRun owns the current run; in
    /// menus and canonical contexts this is null. Updated by MainFile patch.
    /// </summary>
    public static string? CurrentRunSeed { get; internal set; }

    /// <summary>
    /// Seed of the most recently captured run in THIS process. Survives
    /// CleanUp (unlike <see cref="CurrentRunSeed"/>) so a reload of the same
    /// run resumes its original frozen snapshot instead of re-freezing the
    /// live config; run-start captures (SetUpNew) always re-freeze. Nothing
    /// consumes it without a seed arriving, so it cannot leak definitions
    /// into menus. Cross-process continuation needs persisted definitions
    /// (QCR-2, future).
    /// </summary>
    public static string? LastRunSeed { get; internal set; }

    public static string? RunSeedOf(IRunState? runState)
    {
        if (runState is null || runState is NullRunState)
        {
            return null;
        }
        try
        {
            // RunRngSet.StringSeed: original input seed string (hashed numeric
            // form is RunRngSet.Seed). Same string on both MP ends - the
            // deterministic regeneration contract.
            return runState.Rng.StringSeed;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Is this slot part of the allowed set for the given run?</summary>
    public static bool IsSlotAllowedInRun(int slot, IRunState? runState)
    {
        string? seed = RunSeedOf(runState);
        if (seed is null)
        {
            return false;
        }
        return slot >= 0 && slot < ChaosRelicGenerator.TotalSlots;
    }
}
