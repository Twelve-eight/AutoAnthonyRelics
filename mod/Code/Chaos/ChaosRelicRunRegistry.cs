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
/// </summary>
public static class ChaosRelicRunRegistry
{
    private const int CacheLimit = 8;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IReadOnlyList<ChaosRelicDefinition>> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();

    /// <summary>
    /// Generation inputs frozen at run-seed capture; null outside runs.
    /// Set by the seed-tracking patches alongside <see cref="CurrentRunSeed"/>.
    /// </summary>
    public static QuriousGenerationSnapshot? CurrentSnapshot { get; internal set; }

    /// <summary>Generation config fingerprint for the current process state.</summary>
    private static string ConfigFingerprint()
    {
        var snapshot = CurrentSnapshot;
        var sb = new StringBuilder(256);
        sb.Append(snapshot?.BudgetCommon ?? QuriousCraftingRelicsConfig.ChaosRelicBudgetCommon).Append('/')
          .Append(snapshot?.BudgetUncommon ?? QuriousCraftingRelicsConfig.ChaosRelicBudgetUncommon).Append('/')
          .Append(snapshot?.BudgetRare ?? QuriousCraftingRelicsConfig.ChaosRelicBudgetRare).Append('/')
          .Append(snapshot?.NegativeChanceCommon ?? QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceCommon).Append('/')
          .Append(snapshot?.NegativeChanceUncommon ?? QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceUncommon).Append('/')
          .Append(snapshot?.NegativeChanceRare ?? QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceRare).Append('/')
          .Append(snapshot?.EnableExtraPool ?? QuriousCraftingRelicsConfig.EnableExtraPool ? '1' : '0').Append('/')
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
              .Append(':').Append(Costs().CostPerPoint(template))
              .Append(':').Append(Costs().RefundPerPoint(template))
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
        string key = seed + "\u0000" + ConfigFingerprint();
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
    /// owner has no run seed yet (menus, previews).</summary>
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
