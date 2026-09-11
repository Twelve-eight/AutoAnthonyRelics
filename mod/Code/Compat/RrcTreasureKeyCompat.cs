using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
/// Fix: postfix RelicRewardChoiceReward.OnSkipped. That method runs when the
/// RRC reward goes unclaimed (rewards screen closed / skipped), exactly the
/// "no index" vote Act4Heart compensates. We gate on:
/// - the reward IS a chest-flow instance (singleplayer chests construct with
///   sharedTreasurePoolOnly:true; multiplayer personal chests carry a
///   TreasureLifetime) - ordinary combat-reward choices must NOT grant keys;
/// - Act4Heart is loaded, its key models are registered, and its live config
///   keys_enable is true (read via reflection; default true when absent);
/// - the local player does not already hold a SapphireKey.
///
/// The grant itself re-implements Act4Heart.TryGiveKey exactly:
/// RelicCmd.Obtain(ModelDb.Relic&lt;SapphireKey&gt;().ToMutable(), player, -1)
/// run via TaskHelper.RunSafely. MP-safe: Obtain is an engine command; both
/// ends run the same postfix on their own reward instance.
///
/// SHIPPED TWICE on purpose (user order 2026-09-11): inside QuriousCraftingRelics
/// (default-on for its users) and as the standalone workshop mod
/// RelicRewardChoicesKeyFix for players who only run RRC + Act4Heart. Both
/// builds compile this exact source, so TryInstall must stay idempotent across
/// two assemblies in one process. The per-assembly statics cannot do that:
/// each package gets its own copy of _installed. The cross-package guard is
/// Harmony's own patch registry - Harmony.GetPatchInfo(original) is shared
/// process-wide (HarmonySharedState), so the second package sees the first
/// package's postfix and declines. _installed is only the fast path.
/// </summary>
internal static class RrcTreasureKeyCompat
{
    private const string RrcModId = "RelicRewardChoices";
    private const string Act4HeartModId = "Act4Heart";
    private const string SapphireKeyModelName = "Act4Heart.Keys.SapphireKey";

    /// <summary>
    /// Serializes the patch-info probe and the Patch() call inside one process,
    /// so two threads (e.g. two mod initializers racing) cannot both observe an
    /// unpatched target. Cross-assembly ownership is decided by Harmony's own
    /// patch registry, not by this lock.
    /// </summary>
    private static readonly object InstallGate = new();

    /// <summary>
    /// Fast path for THIS assembly only: true once this package installed the
    /// postfix. Never trusted as the cross-package guard - the twin package has
    /// its own copy of this field.
    /// </summary>
    private static bool _installed;

    /// <summary>Logger line prefix: the standalone build overrides with its own mod id.</summary>
    internal static string LogPrefix = "[QuriousCraftingRelics] RRC key compat";

    private static Type? _rrcChoiceRewardType;
    private static FieldInfo? _rrcTreasureLifetimeField;
    private static FieldInfo? _rrcSharedTreasurePoolOnlyField;

    private static bool _resolved;
    private static bool _compatible;

    /// <summary>
    /// Patch RelicRewardChoiceReward.OnSkipped if and only if both target mods
    /// are loaded and no instance of this compat patch - in this assembly or in
    /// the twin package - already claimed the job this process. Returns true
    /// when THIS call installed the patch.
    /// </summary>
    internal static bool TryInstall(Harmony harmony)
    {
        if (!ResolveTypes())
        {
            LogInfo("dormant: RelicRewardChoices and/or Act4Heart not loaded");
            return false;
        }

        lock (InstallGate)
        {
            if (_installed)
            {
                // This assembly already installed it (repeat initializer call).
                LogInfo("dormant: this package already installed the OnSkipped postfix");
                return false;
            }

            var original = AccessTools.Method(_rrcChoiceRewardType, "OnSkipped");
            if (original is null)
            {
                LogError("RelicRewardChoiceReward.OnSkipped not found; patch dormant");
                return false;
            }

            // Cross-package guard. Harmony keeps patch info in a process-wide
            // registry (HarmonySharedState, keyed by the original MethodBase and
            // serialized as module GUID + metadata token), so a postfix applied
            // by the twin package - a separate assembly that also compiles this
            // file - is visible here.
            var existing = Harmony.GetPatchInfo(original);
            if (existing is not null && HasOurPostfix(existing))
            {
                // Twin package (or a stale call in this one) owns the patch.
                LogInfo("dormant: OnSkipped already carries this compat postfix " +
                        "(the other package installed it)");
                return false;
            }

            var postfix = typeof(RrcTreasureKeyCompat).GetMethod(nameof(OnSkippedPostfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(original, postfix: new HarmonyMethod(postfix));
            _installed = true;
            LogInfo("active: RelicRewardChoices + Act4Heart detected, OnSkipped patched");
            return true;
        }
    }

    /// <summary>
    /// True when any postfix on the target was contributed by this compat type.
    ///
    /// Matched by type FULL NAME, not by <c>== typeof(RrcTreasureKeyCompat)</c>:
    /// the two packages are separate assemblies, so each owns its own Type
    /// object for this class and a reference/identity comparison would never
    /// see the twin's patch - exactly the double-install this guard exists to
    /// stop. Harmony's registry hands back the patch MethodInfo resolved from
    /// the *other* module, so FullName is the only stable cross-assembly key.
    /// The method name is checked too, so an unrelated same-named type in a
    /// future assembly cannot suppress this install.
    /// </summary>
    private static bool HasOurPostfix(HarmonyLib.Patches patchInfo)
    {
        string typeName = typeof(RrcTreasureKeyCompat).FullName!;
        return patchInfo.Postfixes.Any(p =>
            p.PatchMethod is { } method
            && method.Name == nameof(OnSkippedPostfix)
            && method.DeclaringType?.FullName == typeName);
    }

    private static void OnSkippedPostfix(object __instance)
    {
        try
        {
            MaybeGiveSapphireKey(__instance);
        }
        catch (Exception e)
        {
            LogError($"failed: {e.Message}");
        }
    }

    private static void MaybeGiveSapphireKey(object rrcReward)
    {
        // Chest-flow instance? Singleplayer chest: _sharedTreasurePoolOnly.
        // Multiplayer personal chest: TreasureLifetime set. Anything else is a
        // combat/shop/dig choice - Act4Heart never keys those.
        bool isChestFlow = false;
        if (_rrcSharedTreasurePoolOnlyField?.GetValue(rrcReward) is bool sharedOnly && sharedOnly)
        {
            isChestFlow = true;
        }
        else if (_rrcTreasureLifetimeField?.GetValue(rrcReward) is { } lifetime && lifetime is not null)
        {
            isChestFlow = true;
        }
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

        // Already holds the key? Mirror Act4Heart's per-player guard.
        var runState = player.RunState;
        if (runState is null)
        {
            return;
        }
        if (PlayerHoldsSapphireKey(player))
        {
            return;
        }

        var keyRelic = SapphireKeyModel();
        if (keyRelic is null)
        {
            return;
        }

        LogInfo("chest relic choice skipped, awarding Sapphire Key " +
                "(RelicRewardChoices + Act4Heart fix)");
        TaskHelper.RunSafely(RelicCmd.Obtain(keyRelic.ToMutable(), player, -1));
    }

    // ---------- Act4Heart integration (all reflection; no compile-time dep) ----------

    private static bool ResolveTypes()
    {
        if (_resolved)
        {
            return _compatible;
        }
        _resolved = true;

        var rrc = FindModAssembly(RrcModId);
        var a4h = FindModAssembly(Act4HeartModId);
        if (rrc is null || a4h is null)
        {
            return false; // One or both mods absent: stay dormant, silently.
        }

        _rrcChoiceRewardType = rrc.GetType("RelicRewardChoices.Rewards.RelicRewardChoiceReward");
        _rrcSharedTreasurePoolOnlyField = AccessTools.Field(_rrcChoiceRewardType, "_sharedTreasurePoolOnly");
        _rrcTreasureLifetimeField = AccessTools.Field(_rrcChoiceRewardType, "TreasureLifetime");

        bool keysModelPresent = a4h.GetType(SapphireKeyModelName) is not null;
        _compatible = _rrcChoiceRewardType is not null
            && _rrcSharedTreasurePoolOnlyField is not null
            && keysModelPresent;
        return _compatible;
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

    private static bool PlayerHoldsSapphireKey(Player player)
    {
        var a4h = FindModAssembly(Act4HeartModId);
        var keyType = a4h?.GetType(SapphireKeyModelName);
        if (keyType is null)
        {
            return true; // Cannot resolve: do not grant (fail closed).
        }
        return player.Relics.Any(r => keyType.IsInstanceOfType(r));
    }

    private static RelicModel? SapphireKeyModel()
    {
        var a4h = FindModAssembly(Act4HeartModId);
        var keyType = a4h?.GetType(SapphireKeyModelName);
        if (keyType is null)
        {
            return null;
        }
        // ModelDb.Relic<T>() via reflection: MakeGenericMethod(SapphireKey).
        var relicMethod = typeof(ModelDb).GetMethods()
            .FirstOrDefault(m => m.Name == "Relic" && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 1)
            ?.MakeGenericMethod(keyType);
        return relicMethod?.Invoke(null, null) as RelicModel;
    }

    private static void LogInfo(string message) => MainFile.Logger.Info($"{LogPrefix}: {message}");

    private static void LogError(string message) => MainFile.Logger.Error($"{LogPrefix}: {message}");
}
