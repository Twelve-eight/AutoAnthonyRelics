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
/// Harmony postfix on RunManager.Launch: captures State.Rng.StringSeed for
/// the whole run. Prefix on SetUpNewSingleplayer/SetUpNewMultiplayer/
/// SetUpSavedSingleplayer would be racy (Launch is the single funnel that
/// fires RunStarted with State fully populated).
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
internal static class RunSeedTrackPatch
{
    private static void Postfix(RunState __result)
    {
        try
        {
            Chaos.ChaosRelicRunRegistry.CurrentRunSeed = __result?.Rng?.StringSeed;
            MainFile.Logger.Info($"[AutoAnthonyRelics] run seed captured: {Chaos.ChaosRelicRunRegistry.CurrentRunSeed ?? "(null)"}");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[AutoAnthonyRelics] run seed capture failed: {e.Message}");
        }
    }
}
