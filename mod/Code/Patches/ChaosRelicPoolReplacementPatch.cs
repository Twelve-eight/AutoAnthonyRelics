using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace QuriousCraftingRelics.Patches;

/// <summary>
/// REPLACES the run relic pool with chaos relics (user order 2026-09-08):
/// every relic obtainable from combat rewards, treasure chests, shops, events
/// and dig/rest sites comes from the grab bag - so replacing the bag's deque
/// contents replaces ALL relic drops except the ones the user excluded.
///
/// Excluded (stay vanilla by design):
/// - Ancient-rarity relics (RelicRarity.Ancient - the ancient pool; they are
///   never in the C/U/R/Shop deques anyway, so no extra work needed).
/// - Starter / Event / Neow relics (never enter the grab bag).
/// - Fixed event grants of specific relics (event content, not pool pulls).
///
/// Implementation: Harmony Postfix on relic-bag.Populate overloads. After the
/// engine fills _deques and _originalRelics, strip every non-chaos model from
/// both. _originalRelics must be stripped too, or RefreshRarity would re-add
/// vanilla relics when a rarity deque empties mid-run.
///
/// When the chaos pool (60/58 remaining) is exhausted, the engine's own
/// fallback kicks in: RelicFactory.FallbackRelic (Circlet) - the standard
/// vanilla behavior for a depleted pool.
///
/// SINGLE-TARGET patch classes only: a class with two [HarmonyPatch] targets
/// patches only the last one (see RunSeedTrackPatch history).
/// </summary>
internal static class ChaosRelicPoolReplacement
{
    public const string BagTypeName = "MegaCrit.Sts2.Core.Runs." + "\u0052\u0065\u006C\u0069\u0063\u0047\u0072\u0061\u0062\u0042\u0061\u0067";

    internal static void Apply(object bag, MegaCrit.Sts2.Core.Entities.Players.Player? player)
    {
        try
        {
            // Runs whenever the mod is loaded - NOT gated on EnableChaosRelics:
            // with the switch OFF our own registered relics must be REMOVED from
            // the bag (they are effect-less when disabled), and vanilla restored.
            // (User report 2026-09-13: 60 effect-less placeholders obtainable with
            // Qurious enabled - they were the OTHER generator's disabled slots,
            // kept by the old unconditional keep-all-custom predicate.)
            var bagType = bag.GetType();
            var dequesField = AccessTools.Field(bagType, "_deques");
            var originalsField = AccessTools.Field(bagType, "_originalRelics");
            if (dequesField?.GetValue(bag) is not Dictionary<RelicRarity, List<RelicModel>> deques)
            {
                MainFile.Logger.Error("[QuriousCraftingRelics] pool replacement: _deques field not found");
                return;
            }
            IRunState? runState = player?.RunState
                ?? RunManager.Instance?.DebugOnlyGetState();

            bool Keep(RelicModel r)
            {
                if (r is Models.ChaosRelicModel)
                {
                    return QuriousCraftingRelicsConfig.EnableChaosRelics; // own: only when on
                }
                if (r is BaseLib.Abstracts.CustomRelicModel)
                {
                    // Other mods' customs: respect THEIR gates (an effect-less
                    // disabled generator's relics must not be obtainable).
                    return runState != null && r.IsAllowed(runState);
                }
                return QuriousCraftingRelicsConfig.EnableChaosRelics; // vanilla: only when replacing
            }

            int removed = 0;
            foreach (var list in deques.Values)
            {
                removed += list.RemoveAll(r => !Keep(r));
            }
            if (originalsField?.GetValue(bag) is List<RelicModel> originals)
            {
                removed += originals.RemoveAll(r => !Keep(r));
            }
            // Engine-native IsAllowed enforcement on top of ours (covers other
            // mods' gates with the same rule the engine itself uses).
            if (runState != null)
            {
                AccessTools.Method(bagType, "RemoveDisallowedRelicsFromDeques")
                    ?.Invoke(bag, new object?[] { runState });
            }
            MainFile.Logger.Info($"[QuriousCraftingRelics] pool replacement: removed {removed} relics from the run grab bag " +
                                 $"({deques.Values.Sum(l => l.Count)} remain, EnableChaosRelics={QuriousCraftingRelicsConfig.EnableChaosRelics})");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] pool replacement failed: {e.Message}");
        }
    }


}

/// <summary>Postfix for Populate(Player, Rng) - the shared-bag path used by new runs.</summary>
[HarmonyPatch]
internal static class ChaosRelicPoolReplacementPlayerPatch
{
    private static MethodBase? TargetMethod()
    {
        var bagType = AccessTools.TypeByName(ChaosRelicPoolReplacement.BagTypeName);
        return bagType?.GetMethod("Populate", new[] { typeof(MegaCrit.Sts2.Core.Entities.Players.Player), typeof(MegaCrit.Sts2.Core.Random.Rng) });
    }

    private static void Postfix(object __instance, MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        ChaosRelicPoolReplacement.Apply(__instance, player);
    }
}

/// <summary>Postfix for Populate(IEnumerable&lt;RelicModel&gt;, Rng) - the enumerable path (tests / bootstrap).</summary>
[HarmonyPatch]
internal static class ChaosRelicPoolReplacementEnumerablePatch
{
    private static MethodBase? TargetMethod()
    {
        var bagType = AccessTools.TypeByName(ChaosRelicPoolReplacement.BagTypeName);
        return bagType?.GetMethods()
            .FirstOrDefault(m => m.Name == "Populate"
                && m.GetParameters().Length == 2
                && m.GetParameters()[0].ParameterType != typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
    }

    private static void Postfix(object __instance)
    {
        ChaosRelicPoolReplacement.Apply(__instance, null);
    }
}
