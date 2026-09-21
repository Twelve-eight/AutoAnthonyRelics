using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace QuriousCraftingRelics.Patches;

/// <summary>
/// Tracks the active run for ChaosRelicRunRegistry (canonical-model-safe:
/// RelicModel.Owner asserts mutable and throws on canonical instances, so the
/// registry reads the run seed from here instead of from the relic's owner).
///
/// Three capture points:
/// 1. Prefix on SetUpNewSingleplayer/SetUpNewMultiplayer - BEFORE
///    InitializeNewRun populates the shared grab bag, so
///    ChaosRelicModel.Rarity resolves real Definitions instead of the Common
///    fallback (rarity spread across the reward deques). A genuinely NEW run
///    always mints a fresh durable identity here, freezes its inputs, generates
///    the 60 definitions and prepares the save payload.
/// 2. Postfix on RunState.FromSerializable, explicitly
///    <see cref="Priority.Last"/> - the LOAD path, where the BaseLib setters have
///    already restored the save's identity token and generation payload onto the
///    RunState. Runs before Launch, which is not guaranteed to happen on every
///    load path.
/// 3. Postfix on RunManager.Launch - the general funnel fallback (also fires
///    RunStarted with State fully populated), idempotent with 1 and 2 because a
///    capture of an already-retained identity resumes it.
///
/// WS-0916-06 / R04-01: which save a cached relic belongs to is decided by the
/// durable identity (<see cref="Chaos.ChaosRunIdentity"/>), and the definitions
/// come from the save's own generation payload when it carries one.
///
/// A REFUSED capture (payload rejected, known drift, persistence unavailable) is
/// not an exception: the registry publishes no snapshot/definitions and reports
/// the reason, so no chaos relic is served and nothing is regenerated from the
/// live config.
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
/// Records the persisted run start time of a save being deserialized, keyed by
/// the RunState it produced.
///
/// WHY here and not on SetUpSavedSingleplayer/SetUpSavedMultiplayer: those are
/// <c>async Task</c> methods, which Harmony rewrites through their state
/// machine, so injecting their parameters is fragile. <see cref="RunState.FromSerializable"/>
/// is a plain static method that every load path calls (main menu, multiplayer
/// load, custom/daily load, file drop, replay) and that sees the whole
/// SerializableRun, and the RunState it returns is the very instance
/// RunManager.Launch later reports - so the start time is attributed to exactly
/// one run and cannot leak into another.
///
/// Only a save with NO identity token needs it: it is what distinguishes two
/// token-less saves that share a seed string.
/// </summary>
[HarmonyPatch(typeof(RunState), nameof(RunState.FromSerializable))]
internal static class RunStartTimeCapturePatch
{
    /// <summary>Persisted start time per deserialized run; weak, so it dies
    /// with the run it describes.</summary>
    private static readonly ConditionalWeakTable<IRunState, object> StartTimes = new();

    private static void Postfix(SerializableRun save, RunState __result)
    {
        try
        {
            if (__result is not null && save is not null)
            {
                StartTimes.AddOrUpdate(__result, save.StartTime);
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] run start-time capture failed: {e.Message}");
        }
    }

    /// <summary>Persisted start time of a loaded run, or 0 when unknown (a new
    /// run, or a load path that bypassed FromSerializable).</summary>
    internal static long Of(IRunState? runState)
    {
        if (runState is null)
        {
            return 0;
        }
        return StartTimes.TryGetValue(runState, out object? startTime) && startTime is long value ? value : 0;
    }
}

/// <summary>
/// Load-time restore of the run's generation state, on the SAME hook the
/// BaseLib save setters run on.
///
/// ORDER (load-bearing, measured): BaseLib's <c>LoadExtendedRunData</c> postfix
/// has default priority and restores the token/payload onto the RunState. This
/// postfix is declared <see cref="Priority.Last"/> so it always observes those
/// values, independent of mod install order - the measured order on this engine
/// is original body, then default-priority setters, then Last (see
/// harmony-order-repro.json).
///
/// WHY NOT Launch ONLY: <c>RunState.FromSerializable</c> constructs players,
/// decks and relics inside its own body, i.e. before ANY postfix - and
/// RunManager.Launch is not guaranteed to be the first thing that runs
/// afterwards. Restoring here publishes the run's definitions as early as the
/// save channel allows. LIMIT (reported, not hidden): a consumer invoked from
/// INSIDE FromSerializable's body (a model constructor or deserializer that
/// reads ChaosRelicRunRegistry) still sees no active run, because the values
/// physically arrive in the postfix phase. Nothing in this mod reads the
/// registry from a model constructor, and the early capture point for NEW runs
/// (point 1) already covers the grab-bag population that did read it.
/// </summary>
[HarmonyPatch(typeof(RunState), nameof(RunState.FromSerializable))]
[HarmonyPriority(Priority.Last)]
internal static class RunGenerationRestorePatch
{
    private static void Postfix(RunState __result)
    {
        try
        {
            RunSeedEarlyTrackPatch.CaptureSeed(
                Chaos.ChaosRelicRunRegistry.RunSeedOf((IRunState?)__result),
                runState: __result);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] run restore failed: {e.Message}");
        }
    }
}

/// <summary>
/// Run-scope cleanup (astra third review, QCR-2026-09-14-01): CleanUp fires
/// when a run ends (finish/abandon/disconnect/return to menu - the same
/// surface MpConfigSync's restore uses). Without this, CurrentRunSeed survived
/// into menus and menu/canonical queries could still resolve the previous run's
/// definitions, contradicting the "null outside runs" contract - reproduced by
/// registry-baseline-repro.json (snapshotCleared=false, foreignUsesLive=false,
/// menuUsesLive=false).
///
/// The frozen context, its definitions and its payload are deliberately KEPT in
/// the registry's bounded ledger, so reloading the same save resumes its
/// ORIGINAL generation; the ACTIVE fields are all cleared together.
/// </summary>
[HarmonyPatch(typeof(RunManager), "CleanUp")]
internal static class RunSeedCleanUpPatch
{
    private static void Postfix()
    {
        try
        {
            if (Chaos.ChaosRelicRunRegistry.CurrentRunSeed is not null
                || Chaos.ChaosRelicRunRegistry.CurrentIdentity is not null)
            {
                Chaos.ChaosRelicRunRegistry.ClearActiveRun();
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
/// Shared capture logic. MUST live in separate single-target patch classes:
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
            CaptureSeed(Chaos.ChaosRelicRunRegistry.RunSeedOf((IRunState?)state),
                runStart: true, runState: state);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] early seed capture failed: {e.Message}");
        }
    }

    /// <summary>
    /// Resolves the run's durable identity and publishes its generation state.
    ///
    /// FREEZE-ONCE semantics (astra-advice item 5): a run's generation inputs are
    /// frozen on the FIRST capture of its identity. Launch fires on EVERY
    /// room-transition save-load, and re-capturing there used to re-read the LIVE
    /// config - a mid-run rebalance silently regenerated every definition of the
    /// SAME run on the next reload, so held relics' effects drifted away from
    /// their descriptions (observed 2026-09-13). A later capture of the same
    /// identity now RESUMES the retained context (including its restored
    /// definitions and payload) and only refreshes the loc table.
    ///
    /// CONTINUATION (astra third review QCR-2026-09-14-01; WS-0916-06; R04-01):
    /// after CleanUp drops CurrentRunSeed, reloading the same save must resume its
    /// ORIGINAL frozen state - including when a DIFFERENT save was loaded in
    /// between, and including after a clean process exit (then from the save's own
    /// payload). A REFUSED capture is reported, never silently degraded.
    /// </summary>
    internal static void CaptureSeed(string? seed, bool runStart = false, IRunState? runState = null)
    {
        if (string.IsNullOrEmpty(seed))
        {
            // Leaving a run (menu): drop the seed/identity so canonical models
            // render the generic text again. The retained contexts stay.
            Chaos.ChaosRelicRunRegistry.ClearActiveRun();
            return;
        }

        Chaos.ChaosRelicRunRegistry.CaptureRun(
            runState, seed, runStart, RunStartTimeCapturePatch.Of(runState));

        var outcome = Chaos.ChaosRelicRunRegistry.LastOutcome;
        if (outcome.Refused)
        {
            // No snapshot and no definitions were published; the registry
            // already logged the reason. Chaos relics stay out of this run, and
            // the loc dedupe is dropped so a later capture rewrites the table
            // instead of being skipped as "already done" for the old run.
            ChaosRelicLocUpdater.OnSeedCaptured(null);
            return;
        }

        ChaosRelicLocUpdater.OnSeedCaptured(seed);
        MainFile.Logger.Info(
            $"[QuriousCraftingRelics] run seed captured: {seed} (identity {outcome.Identity}, " +
            $"{Describe(outcome)})");
    }

    private static string Describe(Chaos.ChaosRelicRunRegistry.CaptureResult result) => result.Generation switch
    {
        Chaos.GenerationOutcome.Restored => "definitions restored from the save payload",
        Chaos.GenerationOutcome.Frozen => "generation config frozen, definitions generated",
        Chaos.GenerationOutcome.Migrated => "payload rebuilt from matching recorded inputs",
        Chaos.GenerationOutcome.LegacyRebuild => "pool rebuilt (no saved definitions to restore)",
        _ => result.Resumed ? "snapshot resumed" : "generation config frozen",
    };
}

[HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
internal static class RunSeedTrackPatch
{
    private static void Postfix(RunState __result)
    {
        try
        {
            // Save-load funnel: Launch fires after both new runs and loads; the
            // early-capture path already handled the same run (its identity
            // resolves to the same retained context, so this is idempotent),
            // this covers load-without-setup.
            RunSeedEarlyTrackPatch.CaptureSeed(
                Chaos.ChaosRelicRunRegistry.RunSeedOf((IRunState?)__result),
                runState: __result);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] run seed capture failed: {e.Message}");
        }
    }
}