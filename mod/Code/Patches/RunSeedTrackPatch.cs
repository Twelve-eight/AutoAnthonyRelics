using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace AutoAnthonyRelics.Patches;

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
            var seed = state?.Rng?.StringSeed;
            if (!string.IsNullOrEmpty(seed))
            {
                Chaos.ChaosRelicRunRegistry.CurrentRunSeed = seed;
                ChaosRelicLocUpdater.OnSeedCaptured(seed);
                MainFile.Logger.Info($"[AutoAnthonyRelics] run seed early-captured: {seed}");
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[AutoAnthonyRelics] early seed capture failed: {e.Message}");
        }
    }
}
[HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
internal static class RunSeedTrackPatch
{
    private static void Postfix(RunState __result)
    {
        try
        {
            var seed = __result?.Rng?.StringSeed;
            Chaos.ChaosRelicRunRegistry.CurrentRunSeed = seed;
            // Save-load funnel: Launch fires after both new runs and loads; the
            // early-capture path already updated the loc table for the same seed
            // (OnSeedCaptured is idempotent per seed), this covers load-without-setup.
            ChaosRelicLocUpdater.OnSeedCaptured(seed);
            MainFile.Logger.Info($"[AutoAnthonyRelics] run seed captured: {Chaos.ChaosRelicRunRegistry.CurrentRunSeed ?? "(null)"}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[AutoAnthonyRelics] run seed capture failed: {e.Message}");
        }
    }
}
