using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace QuriousCraftingRelics.Patches;

/// <summary>
/// Tracks the active run seed for ChaosRelicRunRegistry (canonical-model-safe:
/// RelicModel.Owner asserts mutable and throws on canonical instances, so the
/// registry reads the seed from here instead of from the relic's owner).
///
/// Two capture points:
/// 1. Prefix on SetUpNewSingleplayer/SetUpNewMultiplayer - BEFORE
///    InitializeNewRun populates the shared grab bag, so
///    ChaosRelicModel.Rarity resolves real Definitions instead of the Common
///    fallback (rarity spread across the reward deques).
/// 2. Postfix on RunManager.Launch - the save-load / general funnel fallback
///    (also fires RunStarted with State fully populated).
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewSingleplayer))]
internal static class RunSeedEarlyTrackSingleplayerPatch
{
    private static void Prefix(RunState state) => RunSeedEarlyTrackPatch.Capture(state);
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
internal static class RunSeedEarlyTrackMultiplayerPatch
{
    private static void Prefix(RunState state) => RunSeedEarlyTrackPatch.Capture(state);
}

/// <summary>
/// Shared early-capture logic. MUST live in separate single-target patch classes:
/// a patch class with TWO [HarmonyPatch] attributes only patches the LAST target
/// (verified offline against sts2.dll with Harmony 2.4.2 - SetUpNewSingleplayer got
/// no patch info while SetUpNewMultiplayer did).
/// </summary>
internal static class RunSeedEarlyTrackPatch
{
    /// <summary>
    /// Capture the seed BEFORE InitializeNewRun populates the shared grab bag:
    /// relic-bag.Populate reads ChaosRelicModel.Rarity -&gt; Definition, which needs
    /// the run seed. Without this, all 60 chaos relics fall back to Common and
    /// flood the Common deque (they still spawn, but with wrong rarity spread).
    /// Launch postfix stays as the save-load / late-capture fallback.
    /// </summary>
    internal static void Capture(RunState state)
    {
        try
        {
            CaptureSeed(state?.Rng?.StringSeed);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] early seed capture failed: {e.Message}");
        }
    }

    /// <summary>
    /// Freeze-ONCE semantics (astra-advice item 5): the FIRST capture of a
    /// seed owns the run's generation config. Launch fires on EVERY
    /// room-transition save-load, and re-capturing there re-read the LIVE
    /// config - a mid-run rebalance silently regenerated every definition of
    /// the SAME seed on the next reload, so held relics' effects drifted away
    /// from their descriptions (observed 2026-09-13). A later capture of the
    /// same seed keeps the existing snapshot and only refreshes the loc table.
    /// </summary>
    internal static void CaptureSeed(string? seed)
    {
        if (string.IsNullOrEmpty(seed))
        {
            // Leaving a run (menu): drop the seed so canonical models render
            // the generic text again; the snapshot is kept so a continuation
            // of the same run resumes its original generation.
            Chaos.ChaosRelicRunRegistry.CurrentRunSeed = null;
            return;
        }
        bool sameRun = Chaos.ChaosRelicRunRegistry.CurrentRunSeed == seed
            && Chaos.ChaosRelicRunRegistry.CurrentSnapshot is not null;
        Chaos.ChaosRelicRunRegistry.CurrentRunSeed = seed;
        if (!sameRun)
        {
            Chaos.ChaosRelicRunRegistry.CurrentSnapshot = Chaos.QuriousGenerationSnapshot.Capture();
        }
        ChaosRelicLocUpdater.OnSeedCaptured(seed);
        MainFile.Logger.Info($"[QuriousCraftingRelics] run seed captured: {seed} (generation config frozen{(sameRun ? ", snapshot kept" : "")})");
    }
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
internal static class RunSeedTrackPatch
{
    private static void Postfix(RunState __result)
    {
        try
        {
            // Save-load funnel: Launch fires after both new runs and loads; the
            // early-capture path already handled the same seed (freeze-once +
            // idempotent loc update), this covers load-without-setup.
            RunSeedEarlyTrackPatch.CaptureSeed(__result?.Rng?.StringSeed);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] run seed capture failed: {e.Message}");
        }
    }
}
