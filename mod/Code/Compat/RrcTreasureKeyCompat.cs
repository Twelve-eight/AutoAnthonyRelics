using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace AutoAnthonyRelics.Compat;

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
/// SHIPPED TWICE on purpose (user order 2026-09-11): inside AutoAnthonyRelics
/// (default-on for its users) and as the standalone workshop mod
/// RelicRewardChoicesKeyFix for players who only run RRC + Act4Heart. Both
/// builds compile this exact source; TryInstall's process-wide named mutex
/// guarantees only the first installed instance patches, so double-installing
/// both mods cannot double-grant the key.
/// </summary>
internal static class RrcTreasureKeyCompat
{
    private const string RrcModId = "RelicRewardChoices";
    private const string Act4HeartModId = "Act4Heart";
    private const string SapphireKeyModelName = "Act4Heart.Keys.SapphireKey";

    /// <summary>Named-mutex name shared by every build of this patch.</summary>
    private const string InstallMutexName = @"Global\AutoAnthonyRelics.RrcTreasureKeyCompat.v1";

    /// <summary>Logger line prefix: the standalone build overrides with its own mod id.</summary>
    internal static string LogPrefix = "[AutoAnthonyRelics] RRC key compat";

    private static Type? _rrcChoiceRewardType;
    private static FieldInfo? _rrcTreasureLifetimeField;
    private static FieldInfo? _rrcSharedTreasurePoolOnlyField;

    private static bool _resolved;
    private static bool _compatible;

    /// <summary>
    /// Patch RelicRewardChoiceReward.OnSkipped if and only if both target mods
    /// are loaded and no other instance of this compat patch already claimed
    /// the job this session. Returns true when THIS call installed the patch.
    /// </summary>
    internal static bool TryInstall(Harmony harmony)
    {
        if (!ResolveTypes())
        {
            return false;
        }

        // Process-wide double-install guard: first caller wins, others stay
        // dormant (AutoAnthonyRelics and the standalone fix mod may coexist).
        bool createdNew;
        using var mutex = new Mutex(true, InstallMutexName, out createdNew);
        try
        {
            if (!createdNew)
            {
                // Another instance (this mod or the standalone twin) owns it.
                return false;
            }

            var original = AccessTools.Method(_rrcChoiceRewardType, "OnSkipped");
            if (original is null)
            {
                LogError("RelicRewardChoiceReward.OnSkipped not found; patch dormant");
                return false;
            }
            var postfix = typeof(RrcTreasureKeyCompat).GetMethod(nameof(OnSkippedPostfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            harmony.Patch(original, postfix: new HarmonyMethod(postfix));
            LogInfo("active: RelicRewardChoices + Act4Heart detected, OnSkipped patched");
            return true;
        }
        finally
        {
            // The Mutex guards installation only; Harmony patches outlive it.
            // Abandon ownership deliberately: the loser must NOT release the
            // winner's hold, and the winner releasing on dispose is harmless
            // because nothing re-checks after install.
            try
            {
                if (createdNew)
                {
                    mutex.ReleaseMutex();
                }
            }
            catch (ApplicationException)
            {
                // Not owned on this thread after AbandonMutex; ignore.
            }
        }
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
