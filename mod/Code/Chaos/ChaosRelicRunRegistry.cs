using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Runs;

namespace AutoAnthonyRelics.Chaos;

/// <summary>
/// Per-run registry of generated relic definitions. Keyed by run seed string:
/// same seed -> same relic pool on both MP ends (deterministic regeneration,
/// the same contract AutoAnthony uses for its pool snapshots minus transport).
/// Lazily generated on first query for a seed; cache bounded to a few runs.
/// </summary>
public static class ChaosRelicRunRegistry
{
    private const int CacheLimit = 8;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IReadOnlyList<ChaosRelicDefinition>> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();

    public static IReadOnlyList<ChaosRelicDefinition> ForSeed(string seed, int multiplier)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(seed, out var cached))
            {
                return cached;
            }
            var generated = ChaosRelicGenerator.Generate(seed, multiplier);
            Cache[seed] = generated;
            Order.Enqueue(seed);
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
        var pool = ForSeed(seed, AutoAnthonyRelicsConfig.ChaosRelicMultiplier);
        return slot >= 0 && slot < pool.Count ? pool[slot] : null;
    }

    /// <summary>Run seed string from the owning player's run state (null outside runs).</summary>
    public static string? RunSeedOf(RelicModel relic)
    {
        var owner = relic.Owner;
        if (owner is null)
        {
            return null;
        }
        var runState = owner.RunState;
        return RunSeedOf(runState);
    }

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
