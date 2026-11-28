using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace QuriousCraftingRelics.Compat;

/// <summary>
/// Cross-mod bugfix: RelicRewardChoices (workshop 3795496598, "遗物奖励改为三选一")
/// + Act4Heart (workshop 3747537811) - opened chests never award the Sapphire Key,
/// whether the offered relic choice reward is taken or skipped (user report
/// 2026-09-11).
///
/// Root cause (byte-verified against both mods' decompiled dlls + engine):
/// - Vanilla+Act4Heart path: chest skip -> TreasureRoomRelicSynchronizer.
///   SkipRelicLocally() -> OnPicked(null) (Act4Heart IL-patches the singleplayer
///   guard to fall through) -> AwardRelics() -> Act4Heart postfix
///   GiveKey_On_AwardRelics awards SapphireKey to every player whose vote has
///   no index (the skippers).
/// - RelicRewardChoices replaces NTreasureRoom.OpenChest wholesale (prefix,
///   returns false) and terminates the synchronizer via CompleteWithNoRelics()
///   before showing its own reward screen. OnPicked/AwardRelics NEVER run, so
///   Act4Heart's postfix never fires. The skip-button key icon it adds in its
///   OpenChest postfix still renders, so the UI promises a key that never comes.
/// - Taking (not skipping) the chest reward is equally keyless: vanilla grants
///   the key only to players who did NOT vote for a relic, and under RRC the
///   pick happens outside the synchronizer entirely. StS1 semantics preserved
///   here: the blue key compensates the player for passing on the chest relic;
///   a player who takes a relic from the chest has voted "index set" and gets
///   nothing. RelicRewardChoices' own skip (reward left on the table) is the
///   "no vote" case.
///
/// Fix: patch RelicRewardChoiceReward's two completion paths, because the
/// skipped reward is the "no index" vote Act4Heart compensates:
/// - OnSkipped: the rewards screen closed / skip button. RRC early-returns when
///   the TreasureLifetime ended or the reward was already skipped/finalized, so
///   a prefix records the pre-call state and the postfix pays only a real
///   transition into Skipped. Granting on a finalized reward would key a player
///   who already took the chest relic.
/// - OnSelect: an empty candidate list makes RRC finalize the skip inside this
///   method without ever calling OnSkipped. The postfix observes the returned
///   task (pass-through: result, faults and cancellations reach the caller
///   unchanged) and pays the key when the call ended in Skipped.
/// - Both: RRC's FinalizeSkip accepts the Choosing and Selected states, so an
///   OnSkipped that lands while an OnSelect call is still in flight (window A:
///   selection UI open; window B: Selected, awaiting Obtain's AfterObtained)
///   would otherwise pay the key and then let the same call continue to a real
///   relic. The decision is therefore deferred to the LAST in-flight OnSelect
///   completion for that reward instance: a settled skip pays only when no
///   relic can follow (faulted/cancelled/returned false, or the call ended in
///   Skipped); a call that ends Finalized pays nothing.
/// Both paths are gated on:
/// - the reward IS a chest-flow instance (singleplayer chests construct with
///   sharedTreasurePoolOnly:true; multiplayer personal chests carry a
///   TreasureLifetime property) - ordinary combat-reward choices must NOT grant
///   keys;
/// - Act4Heart is loaded, its key models are registered, and its live config
///   keys_enable is true (read via reflection; default true when absent);
/// - the owning package's live gate is enabled (standalone has no gate);
/// - the player does not already hold a SapphireKey.
///
/// The grant itself re-implements Act4Heart.TryGiveKey exactly:
/// RelicCmd.Obtain(ModelDb.Relic&lt;SapphireKey&gt;().ToMutable(), player, -1)
/// run via TaskHelper.RunSafely. MP-safe: Obtain is an engine command; both
/// ends run the same postfix on their own reward instance.
///
/// SHIPPED TWICE on purpose (user order 2026-09-11): inside QuriousCraftingRelics
/// (default-on for its users) and as the standalone workshop mod
/// RrcA4hKeyFix for players who only run RRC + Act4Heart. Both builds compile
/// this exact source, so TryInstall must stay idempotent across two assemblies
/// in one process. The per-assembly statics cannot do that: each package gets
/// its own copy of _installed. Two process-wide shared objects do it instead:
/// - typeof(Harmony) / typeof(RelicCmd): one Type object each for the whole
///   process (both packages reference the same 0Harmony / sts2 assembly), so a
///   lock on them serializes the registry probe + Patch calls and the
///   already-holds check + Obtain across the twin package.
/// - Harmony's own patch registry: Harmony.GetPatchInfo(original) is shared
///   process-wide (HarmonySharedState, keyed by module GUID + metadata token),
///   so the second package sees the first package's postfix and declines. The
///   match requires our declaring type/method plus the owner Harmony id being
///   one of the two shipped package names, so the two packages recognise each
///   other while an unrelated same-named type cannot suppress the install.
/// A registry that carries only the OnSkipped postfix is an OLDER build of this
/// same compat (the old build had no OnSkipped prefix and no OnSelect patch). It
/// is taken over: every compat patch on OnSkipped is removed, the removal is
/// re-read from the registry, and only then is the current pair installed. A
/// clean upgrade (removing the old DLL) is still preferable; the mixed-version
/// install keeps the old package's ConfigChanged from re-adding its postfix only
/// because the old installer's own _installed fast path is already set.
///
/// Late dependencies: the Qurious package intentionally does not declare
/// RelicRewardChoices/Act4Heart as manifest dependencies, and ModManager calls
/// each mod's initializer right after loading that DLL. If Qurious is processed
/// first, the probe is dormant. One idempotent AppDomain.AssemblyLoad handler
/// (per assembly) re-runs TryInstall when either dependency loads; the happy
/// path is still the immediate probe, and a failed resolution is never cached.
/// </summary>
internal static class RrcTreasureKeyCompat
{
    private const string RrcModId = "RelicRewardChoices";
    private const string Act4HeartModId = "Act4Heart";
    private const string SapphireKeyModelName = "Act4Heart.Keys.SapphireKey";
    private const string QuriousAssemblyName = "QuriousCraftingRelics";
    private const string StandaloneAssemblyName = "RrcA4hKeyFix";
    private const string StandaloneActiveSlot = "RrcA4hKeyFix.StandaloneActive.v1";

    // RewardChoiceState member names of RelicRewardChoices that mark a finished
    // reward. Compared by name, not by numeric value: a reordered enum in a
    // future RelicRewardChoices build must make the probe dormant (fail closed),
    // never silently re-enable the grant.
    private const string SkippedStateName = "Skipped";
    private const string FinalizedStateName = "Finalized";

    /// <summary>
    /// Fast path for THIS assembly only: true once this package installed the
    /// patch pair. Never trusted as the cross-package guard - the twin package
    /// has its own copy of this field.
    /// </summary>
    private static bool _installed;

    /// <summary>One idempotent AssemblyLoad retry hook per assembly (late dependencies).</summary>
    private static bool _loadHookRegistered;

    /// <summary>The Harmony instance this assembly installed (or tried to install) with.</summary>
    private static Harmony? _installHarmony;

    /// <summary>Logger line prefix: the standalone build overrides with its own mod id.</summary>
    internal static string LogPrefix = "[QuriousCraftingRelics] RRC key compat";

    /// <summary>Live package gate; null means enabled for the standalone build.</summary>
    internal static Func<bool>? EnabledGate { get; set; }

    /// <summary>True only when compiled/initialized as the standalone RrcA4hKeyFix package.</summary>
    internal static bool IsStandalonePackage { get; set; }

    // Reflection handles of the last SUCCESSFUL dependency probe. They are only
    // assigned when every member below checked out, because a dormant call (one
    // of the two mods not loaded yet) must not cache the absence: the next
    // TryInstall call (initializer, config change or AssemblyLoad retry) has to
    // probe again, so a dependency that loads later can still be picked up.
    private static FieldInfo? _sharedTreasurePoolOnlyField;
    private static FieldInfo? _stateField;
    private static PropertyInfo? _treasureLifetimeProperty;
    private static PropertyInfo? _lifetimeEndedProperty;

    /// <summary>
    /// Per-reward bookkeeping for deferring the key decision while an OnSelect
    /// call is in flight. Weak keys: a reward that is garbage collected takes
    /// its entry with it.
    /// </summary>
    private static readonly ConditionalWeakTable<object, Flight> Flights = new();

    private sealed class Flight
    {
        /// <summary>OnSelect calls currently in flight for this reward.</summary>
        internal int Calls;

        /// <summary>A real skip was observed while Calls &gt; 0; the last completion decides.</summary>
        internal bool SkipSettled;
    }

    /// <summary>
    /// Patch RelicRewardChoiceReward.OnSkipped + OnSelect if and only if both
    /// target mods are loaded and no instance of this compat patch - in this
    /// assembly or in the twin package - already claimed the job this process.
    /// An older build's OnSkipped-only patch is taken over. Returns true when
    /// THIS call installed the patch pair.
    /// </summary>
    internal static bool TryInstall(Harmony harmony)
    {
        lock (typeof(Harmony))
        {
            if (IsStandalonePackage)
            {
                AppDomain.CurrentDomain.SetData(StandaloneActiveSlot, true);
            }
            if (EnabledGate?.Invoke() == false)
            {
                return false;
            }

            _installHarmony ??= harmony;
            EnsureLoadHook();

            if (_installed)
            {
                // This assembly already installed it (repeat initializer call).
                LogInfo("dormant: this package already installed the chest-key patch pair");
                return false;
            }

            MethodInfo onSkipped;
            MethodInfo onSelect;
            try
            {
                if (!Resolve(out onSkipped, out onSelect))
                {
                    LogInfo("dormant: RelicRewardChoices and/or Act4Heart not loaded or not compatible");
                    return false;
                }
            }
            catch (Exception e)
            {
                // One unexpected upstream shape must not abort the whole mod
                // initializer; stay dormant but keep the failure visible.
                LogError($"dependency probe failed: {e}");
                return false;
            }

            HarmonyLib.Patches? existing;
            try
            {
                existing = Harmony.GetPatchInfo(onSkipped);
            }
            catch (Exception e)
            {
                LogError($"patch-registry probe failed: {e}");
                return false;
            }

            if (existing is not null && existing.Postfixes.Any(IsThisCompatPatch))
            {
                HarmonyLib.Patches? selectInfo;
                try
                {
                    selectInfo = Harmony.GetPatchInfo(onSelect);
                }
                catch (Exception e)
                {
                    LogError($"patch-registry probe failed: {e}");
                    return false;
                }
                bool twinOwnsPair = selectInfo is not null
                    && (selectInfo.Prefixes.Any(IsOurPatch) || selectInfo.Postfixes.Any(IsOurPatch));
                if (twinOwnsPair)
                {
                    // Twin package (this build) already owns the pair.
                    LogInfo("dormant: the twin package already installed the chest-key patch pair");
                    return false;
                }

                // OnSkipped-only registry: an older build of this compat. Remove
                // its patch(es) so the fixed pair can take over. Removal is
                // re-read from the registry before installing; if it did not
                // take effect, stay dormant instead of double-patching.
                try
                {
                    int removed = 0;
                    foreach (var patch in existing.Prefixes.Concat(existing.Postfixes).ToArray())
                    {
                        if (!IsOurPatch(patch) || patch.PatchMethod is not { } method)
                        {
                            continue;
                        }
                        harmony.Unpatch(onSkipped, method);
                        removed++;
                    }
                    HarmonyLib.Patches? after = Harmony.GetPatchInfo(onSkipped);
                    bool stillThere = after is not null
                        && (after.Prefixes.Any(IsOurPatch) || after.Postfixes.Any(IsOurPatch));
                    if (removed == 0 || stillThere)
                    {
                        LogError("takeover: an older package's OnSkipped patch could not be removed; " +
                                 "keeping this install dormant to avoid double patching");
                        return false;
                    }
                    LogInfo($"takeover: removed {removed} patch(es) from an older package build; " +
                            "installing the current pair");
                }
                catch (Exception e)
                {
                    LogError($"takeover failed: {e}");
                    return false;
                }
            }

            var skipPrefix = typeof(RrcTreasureKeyCompat).GetMethod(nameof(OnSkippedPrefix),
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var skipPostfix = typeof(RrcTreasureKeyCompat).GetMethod(nameof(OnSkippedPostfix),
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var selectPrefix = typeof(RrcTreasureKeyCompat).GetMethod(nameof(OnSelectPrefix),
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var selectPostfix = typeof(RrcTreasureKeyCompat).GetMethod(nameof(OnSelectPostfix),
                BindingFlags.Static | BindingFlags.NonPublic)!;
            try
            {
                harmony.Patch(onSkipped, prefix: new HarmonyMethod(skipPrefix), postfix: new HarmonyMethod(skipPostfix));
                harmony.Patch(onSelect, prefix: new HarmonyMethod(selectPrefix), postfix: new HarmonyMethod(selectPostfix));
            }
            catch
            {
                // Never leave half of the pair installed: an OnSkipped-only patch
                // would arm the skip probe without the OnSelect path that shares
                // its state gate, and the failure must stay visible instead of
                // looking like "nothing was patched".
                harmony.Unpatch(onSkipped, skipPrefix);
                harmony.Unpatch(onSkipped, skipPostfix);
                harmony.Unpatch(onSelect, selectPrefix);
                harmony.Unpatch(onSelect, selectPostfix);
                throw;
            }

            _installed = true;
            LogInfo("active: RelicRewardChoices + Act4Heart detected, OnSkipped/OnSelect patched");
            return true;
        }
    }

    /// <summary>
    /// Re-probe when one of the two dependencies is loaded after this mod's
    /// initializer ran. Registered once per assembly; failures are logged and
    /// never swallowed silently (the event callback must not take the loader
    /// down, but the reason stays in the log).
    /// </summary>
    private static void EnsureLoadHook()
    {
        if (_loadHookRegistered)
        {
            return;
        }
        try
        {
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            _loadHookRegistered = true;
        }
        catch (Exception e)
        {
            LogError($"assembly-load retry hook failed: {e}");
        }
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        try
        {
            string? name = args.LoadedAssembly.GetName().Name;
            if (!string.Equals(name, RrcModId, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, Act4HeartModId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (_installHarmony is { } harmony)
            {
                TryInstall(harmony);
            }
        }
        catch (Exception e)
        {
            LogError($"assembly-load retry failed: {e}");
        }
    }

    /// <summary>
    /// True when a patch method was contributed by this compat type from one of
    /// the two shipped packages.
    ///
    /// Matched by type FULL NAME plus the owning Harmony id, not by
    /// <c>== typeof(RrcTreasureKeyCompat)</c>: the two packages are separate
    /// assemblies, so each owns its own Type object for this class and a
    /// reference/identity comparison would never see the twin's patch. Harmony's
    /// registry hands back the patch MethodInfo resolved from the *other*
    /// module, so FullName plus the owning Harmony id are the only stable
    /// cross-assembly keys. The id must equal that module's assembly name and be
    /// one of the two shipped package names, so an unrelated assembly that
    /// merely declares a type with our full name cannot suppress this install.
    /// </summary>
    private static bool IsOurPatch(Patch patch)
    {
        if (patch.PatchMethod is not { } method
            || method.DeclaringType?.FullName != typeof(RrcTreasureKeyCompat).FullName)
        {
            return false;
        }
        string? assembly = method.DeclaringType?.Assembly.GetName().Name;
        bool shipped = string.Equals(assembly, QuriousAssemblyName, StringComparison.Ordinal)
                       || string.Equals(assembly, StandaloneAssemblyName, StringComparison.Ordinal);
        return shipped && patch.owner == assembly;
    }

    /// <summary>
    /// True when the patch is this build's OnSkipped postfix (used to recognise
    /// a twin that already installed the pair; an older build also carries this
    /// exact method name so the two directions still recognise each other).
    /// </summary>
    private static bool IsThisCompatPatch(Patch patch) =>
        patch.PatchMethod is { } method && method.Name == nameof(OnSkippedPostfix) && IsOurPatch(patch);

    private static void OnSkippedPrefix(object __instance, out bool __state)
    {
        __state = false;
        try
        {
            __state = RewardStillOpen(__instance);
        }
        catch (Exception e)
        {
            LogError($"skip state read failed: {e.Message}");
        }
    }

    private static void OnSkippedPostfix(object __instance, bool __state)
    {
        try
        {
            // RelicRewardChoices' own OnSkipped early-returns when the lifetime
            // ended or the reward is already Skipped/Finalized. Only a real
            // transition into Skipped is the "no vote" the key compensates.
            if (!__state || !RewardStateIs(__instance, SkippedStateName) || Ended(__instance))
            {
                return;
            }
            Flight flight = FlightOf(__instance);
            lock (flight)
            {
                if (flight.Calls > 0)
                {
                    // An OnSelect call is in flight; it may still obtain the
                    // relic (windows A and B). The last completion decides.
                    flight.SkipSettled = true;
                    return;
                }
            }
            MaybeGiveSapphireKey(__instance);
        }
        catch (Exception e)
        {
            LogError($"failed: {e.Message}");
        }
    }

    private static void OnSelectPrefix(object __instance, out bool __state)
    {
        OnSkippedPrefix(__instance, out __state);
        if (!__state)
        {
            return;
        }
        try
        {
            Flight flight = FlightOf(__instance);
            lock (flight)
            {
                flight.Calls++;
            }
        }
        catch (Exception e)
        {
            // Bookkeeping failure must not break the selection callback; the
            // completion path simply settles nothing.
            LogError($"selection bookkeeping failed: {e.Message}");
        }
    }

    private static void OnSelectPostfix(object __instance, bool __state, ref Task<bool> __result)
    {
        if (!__state)
        {
            return;
        }
        if (__result is null)
        {
            // Another prefix skipped the method; no selection ran. Settle the
            // bookkeeping without inventing a result.
            SettleQuietly(__instance, faulted: false, result: null);
            return;
        }
        __result = ObserveSelection(__instance, __result);
    }

    private static async Task<bool> ObserveSelection(object reward, Task<bool> selection)
    {
        // Pass-through wrapper: the caller still receives the original result,
        // and a faulted/cancelled task still faults/cancels the caller with the
        // original exception (await rethrows the same instance).
        bool result;
        try
        {
            result = await selection;
        }
        catch
        {
            // Settlement first, then rethrow unchanged. A fault/cancel means no
            // relic was obtained (RRC catches its own Obtain failures and the
            // cancellation path runs before any Obtain), so a settled skip can
            // still pay without double-rewarding.
            SettleQuietly(reward, faulted: true, result: null);
            throw;
        }
        SettleQuietly(reward, faulted: false, result: result);
        return result;
    }

    private static void SettleQuietly(object reward, bool faulted, bool? result)
    {
        try
        {
            Settle(reward, faulted, result);
        }
        catch (Exception e)
        {
            LogError($"selection settle failed: {e.Message}");
        }
    }

    /// <summary>
    /// Last-completion decision for a reward that had an OnSelect call in
    /// flight. Pays only when no relic can follow: a settled skip plus a
    /// fault/cancel/false result, or a call that ended in Skipped (empty
    /// candidate list, or a deferred skip whose selection returned null after
    /// the skip marked the state). A call that ended Finalized pays nothing.
    /// </summary>
    private static void Settle(object reward, bool faulted, bool? result)
    {
        bool pay = false;
        Flight flight = FlightOf(reward);
        lock (flight)
        {
            if (flight.Calls > 0)
            {
                flight.Calls--;
            }
            if (flight.Calls != 0)
            {
                return; // another call is still in flight; it decides
            }
            bool skipped = flight.SkipSettled;
            flight.SkipSettled = false;
            if (Ended(reward) || RewardStateIs(reward, FinalizedStateName))
            {
                // A reward that already paid out its relic must never also pay the
                // key, whichever completion happens to be the last one (duplicate,
                // false or faulted call included).
                return;
            }
            if (faulted)
            {
                pay = skipped;
            }
            else if (result == true)
            {
                pay = RewardStateIs(reward, SkippedStateName);
            }
            else
            {
                pay = skipped;
            }
        }
        if (pay)
        {
            MaybeGiveSapphireKey(reward);
        }
    }

    private static Flight FlightOf(object reward) =>
        Flights.GetValue(reward, static _ => new Flight());

    private static bool RewardStillOpen(object reward) =>
        !RewardStateIs(reward, SkippedStateName) &&
        !RewardStateIs(reward, FinalizedStateName) &&
        !Ended(reward);

    private static bool RewardStateIs(object reward, string name) =>
        _stateField!.GetValue(reward)?.ToString() == name;

    private static bool Ended(object reward)
    {
        object? lifetime = _treasureLifetimeProperty!.GetValue(reward);
        return lifetime is not null && _lifetimeEndedProperty!.GetValue(lifetime) is true;
    }

    private static void MaybeGiveSapphireKey(object rrcReward)
    {
        // Bookkeeping runs even while disabled so a later re-enable cannot lose
        // track of an in-flight selection. Only the key grant is gated.
        // If standalone RrcA4hKeyFix is active in the process, never suppress its
        // grant when QCR's EnableChaosRelics gate is disabled.
        if (EnabledGate?.Invoke() == false
            && !IsStandalonePackage
            && AppDomain.CurrentDomain.GetData(StandaloneActiveSlot) is not true)
        {
            return;
        }

        // Chest-flow instance? Singleplayer chest: _sharedTreasurePoolOnly.
        // Multiplayer personal chest: TreasureLifetime set. TreasureLifetime is
        // an auto-property - resolving it with AccessTools.Field silently yields
        // null and would leave every personal chest keyless. Anything else is a
        // combat/shop/dig choice - Act4Heart never keys those.
        bool isChestFlow = _sharedTreasurePoolOnlyField!.GetValue(rrcReward) is true
                           || _treasureLifetimeProperty!.GetValue(rrcReward) is not null;
        if (!isChestFlow)
        {
            return;
        }

        // The Reward base exposes Player; RRC's reward is built for one player.
        var playerProp = AccessTools.Property(rrcReward.GetType(), "Player");
        if (playerProp?.GetValue(rrcReward) is not Player player)
        {
            return;
        }

        // Act4Heart config: keys_enable (default true when the config object
        // cannot be read, matching Act4Heart's own default).
        if (!KeysEnabled())
        {
            return;
        }
        if (player.RunState is null)
        {
            return;
        }

        // RelicCmd.Obtain records history and adds to Player.Relics synchronously
        // before its first await. Serialize the already-holds check with that add
        // across both packages (same RelicCmd Type object) and do NOT wait for
        // the returned Task: two entries for the same player can then never both
        // observe an empty relic list.
        lock (typeof(RelicCmd))
        {
            Type? keyType = FindModAssembly(Act4HeartModId)?.GetType(SapphireKeyModelName);
            if (keyType is null)
            {
                return; // Cannot resolve: do not grant (fail closed).
            }
            if (player.Relics.Any(keyType.IsInstanceOfType))
            {
                return; // Mirror Act4Heart's per-player guard.
            }

            var keyRelic = SapphireKeyModel(keyType);
            if (keyRelic is null)
            {
                return;
            }

            // The switch may have changed while waiting for the shared lock or
            // resolving the model. Re-read it at the synchronous grant boundary.
            if (EnabledGate?.Invoke() == false)
            {
                return;
            }

            LogInfo("chest relic choice skipped, awarding Sapphire Key " +
                    "(RelicRewardChoices + Act4Heart fix)");
            TaskHelper.RunSafely(RelicCmd.Obtain(keyRelic.ToMutable(), player, -1));
        }
    }

    // ---------- dependency probe (all reflection; no compile-time dep) ----------

    private static bool Resolve(out MethodInfo onSkipped, out MethodInfo onSelect)
    {
        onSkipped = null!;
        onSelect = null!;

        var rrc = FindModAssembly(RrcModId);
        var a4h = FindModAssembly(Act4HeartModId);
        if (rrc is null || a4h is null)
        {
            return false;
        }

        var rewardType = rrc.GetType("RelicRewardChoices.Rewards.RelicRewardChoiceReward");
        var sharedField = rewardType is null ? null : AccessTools.Field(rewardType, "_sharedTreasurePoolOnly");
        var stateField = rewardType is null ? null : AccessTools.Field(rewardType, "_state");
        var lifetimeProperty = rewardType is null ? null : AccessTools.Property(rewardType, "TreasureLifetime");
        var endedProperty = lifetimeProperty is null ? null : AccessTools.Property(lifetimeProperty.PropertyType, "IsEnded");
        var skippedMethod = rewardType is null ? null : AccessTools.Method(rewardType, "OnSkipped");
        var selectMethod = rewardType is null ? null : AccessTools.Method(rewardType, "OnSelect");

        // Only a complete, expected-shape probe may arm the patch: a future
        // RelicRewardChoices layout change turns it dormant instead of making
        // the guards read the wrong member.
        if (rewardType is null
            || sharedField is null || sharedField.FieldType != typeof(bool)
            || stateField is null || !stateField.FieldType.IsEnum
            || lifetimeProperty is null
            || endedProperty is null || endedProperty.PropertyType != typeof(bool)
            || skippedMethod is null || skippedMethod.ReturnType != typeof(void)
            || selectMethod is null || selectMethod.ReturnType != typeof(Task<bool>)
            || a4h.GetType(SapphireKeyModelName) is null)
        {
            return false;
        }

        _sharedTreasurePoolOnlyField = sharedField;
        _stateField = stateField;
        _treasureLifetimeProperty = lifetimeProperty;
        _lifetimeEndedProperty = endedProperty;
        onSkipped = skippedMethod;
        onSelect = selectMethod;
        return true;
    }

    private static Assembly? FindModAssembly(string modId)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a =>
            {
                var name = a.GetName().Name;
                return string.Equals(name, modId, StringComparison.OrdinalIgnoreCase);
            });
    }

    /// <summary>Act4Heart live config keys_enable (default true when unreadable).</summary>
    private static bool KeysEnabled()
    {
        try
        {
            var a4h = FindModAssembly(Act4HeartModId);
            var modMain = a4h?.GetType("Act4Heart.ModMain");
            var configProp = modMain?.GetProperty("current_config",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, null, Type.EmptyTypes, null);
            var config = configProp?.GetValue(null);
            if (config is null)
            {
                return true;
            }
            var field = AccessTools.Field(config.GetType(), "keys_enable");
            if (field?.GetValue(config) is bool enabled)
            {
                return enabled;
            }
            return true;
        }
        catch
        {
            return true; // Act4Heart default is keys_enable = true.
        }
    }

    private static RelicModel? SapphireKeyModel(Type keyType)
    {
        // ModelDb.Relic<T>() via reflection: MakeGenericMethod(SapphireKey).
        var relicMethod = typeof(ModelDb).GetMethods()
            .FirstOrDefault(m => m.Name == "Relic" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1)
            ?.MakeGenericMethod(keyType);
        return relicMethod?.Invoke(null, null) as RelicModel;
    }

    private static void LogInfo(string message) => MainFile.Logger.Info($"{LogPrefix}: {message}");

    private static void LogError(string message) => MainFile.Logger.Error($"{LogPrefix}: {message}");
}
