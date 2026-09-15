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
/// Run-scope cleanup (astra third review, QCR-2026-09-14-01): CleanUp fires
/// when a run ends (finish/abandon/disconnect/return to menu - the same
/// surface MpConfigSync's restore uses). Without this, CurrentRunSeed
/// survived into menus and menu/canonical queries could still resolve the
/// previous run's definitions, contradicting the "null outside runs"
/// contract. The frozen snapshot is deliberately KEPT as continuation
/// evidence (guarded by LastRunSeed) so reloading the same run in this
/// process resumes its original generation; see CaptureSeed.
/// </summary>
[HarmonyPatch(typeof(RunManager), "CleanUp")]
internal static class RunSeedCleanUpPatch
{
    private static void Postfix()
    {
        try
        {
            if (Chaos.ChaosRelicRunRegistry.CurrentRunSeed is not null)
            {
                Chaos.ChaosRelicRunRegistry.CurrentRunSeed = null;
                MainFile.Logger.Info("[QuriousCraftingRelics] run cleaned up: active seed reset");
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] run cleanup failed: {e.Message}");
        }
    }
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
            // runStart: a genuinely NEW run re-freezes even when its seed
            // string repeats a previous run's seed.
            CaptureSeed(state?.Rng?.StringSeed, runStart: true);
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
    ///
    /// Continuation (astra third review, QCR-2026-09-14-01): after CleanUp
    /// drops <see cref="Chaos.ChaosRelicRunRegistry.CurrentRunSeed"/>, a
    /// reload of the SAME run (same seed, same process) must resume its
    /// ORIGINAL frozen snapshot, not re-freeze the live config - a mid-run
    /// rebalance would otherwise redefine held relics on reload. Hence the
    /// same-run check compares against <c>LastRunSeed</c>, which survives
    /// CleanUp; run-start captures (SetUpNew) always re-freeze, so rerolling
    /// a new run with a repeated seed string still picks up current config.
    /// Cross-process continuation still needs persisted definitions (QCR-2).
    /// </summary>
    internal static void CaptureSeed(string? seed, bool runStart = false)
    {
        if (string.IsNullOrEmpty(seed))
        {
            // Leaving a run (menu): drop the seed so canonical models render
            // the generic text again; the snapshot is kept as continuation
            // evidence (guarded by LastRunSeed, see above).
            Chaos.ChaosRelicRunRegistry.CurrentRunSeed = null;
            return;
        }
        bool sameRun = !runStart
            && Chaos.ChaosRelicRunRegistry.LastRunSeed == seed
            && Chaos.ChaosRelicRunRegistry.CurrentSnapshot is not null;
        Chaos.ChaosRelicRunRegistry.CurrentRunSeed = seed;
        Chaos.ChaosRelicRunRegistry.LastRunSeed = seed;
        if (!sameRun)
        {
            // QCR-1: the seed is stamped into the snapshot so the frozen
            // context can precompose the canonical cache key. CaptureSeed sets
            // CurrentRunSeed above and is the only freeze producer.
            Chaos.ChaosRelicRunRegistry.CurrentSnapshot = Chaos.QuriousGenerationSnapshot.Capture(seed);
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
