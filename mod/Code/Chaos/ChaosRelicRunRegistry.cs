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
/// </summary>
public static class ChaosRelicRunRegistry
{
    private const int CacheLimit = 8;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IReadOnlyList<ChaosRelicDefinition>> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();

    /// <summary>Generation config fingerprint for the current process state.</summary>
    private static string ConfigFingerprint()
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

    public static IReadOnlyList<ChaosRelicDefinition> ForSeed(string seed, int multiplier)
    {
        string key = seed + "\u0000" + ConfigFingerprint();
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
            var generated = ChaosRelicGenerator.Generate(seed,
                QuriousCraftingRelicsConfig.ChaosRelicBudgetCommon,
                QuriousCraftingRelicsConfig.ChaosRelicBudgetUncommon,
                QuriousCraftingRelicsConfig.ChaosRelicBudgetRare,
                QuriousCraftingRelicsConfig.PointCosts,
                QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceCommon,
                QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceUncommon,
                QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceRare);
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
