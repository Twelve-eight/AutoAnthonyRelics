using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using QuriousCraftingRelics.Chaos;
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
            // RESPONSIBILITY-SCOPED STRIP (rewrite after the 2026-09-13
            // "empty bag / Circlet-only shop" incident): two generators each
            // RemoveAll-ing by their own global policy compose ORDER-DEPENDENTLY
            // (AAR's off-state pass stripped OUR enabled relics before we could
            // keep them, emptying the bag -> every shop offered the Circlet
            // fallback). Each patch now only removes what IT is responsible for:
            //
            //   1. our own slots when OUR switch is off (effect-less duds),
            //   2. vanilla relics when OUR switch is on (we are replacing),
            //
            // and NEVER touches other mods' customs: those are governed by
            // their own IsAllowed, enforced engine-natively by
            // RemoveDisallowedRelicsFromDeques below. The composition is then
            // order-independent: with AAR off + us on, AAR's patch removes
            // AAR's duds, ours removes vanilla, ours keep ours -> Qurious-only.
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

            // CONTENT-GATE CONTRACT (2026-10-02): three states.
            // - replace: master AND pool group AND seed capture AND a captured
            //   run seed AND the slots really registered at startup -> strip
            //   vanilla, keep customs (responsibility-scoped; the 2026-09-13
            //   empty-bag incident is why other mods' customs are never
            //   touched here).
            // - strip own slots: our slots ARE registered but the pool group is
            //   off (or no seed is captured yet) -> remove OUR slots so they
            //   can never enter the bag without generation support, and leave
            //   vanilla / other mods' relics untouched. With the pool group off
            //   at startup the registration gate keeps our slots out of the
            //   pool entirely and this patch class is not installed at all.
            // CONTENT-GATE CONTRACT: replacement also requires that the slots
            // were actually registered into the pool during ModelDb.Init. A
            // group turned ON after a start with the pool off cannot
            // retroactively register them; replacing then would strip vanilla
            // and leave an empty bag.
            bool master = QuriousCraftingRelicsConfig.EnableChaosRelics;
            bool replace = QuriousCraftingRelicsConfig.PoolReplacementActive
                && MainFile.PoolRegisteredAtStartup
                && ChaosRelicRunRegistry.CurrentRunSeed is not null;

            int removed = 0;
            if (replace)
            {
                // Replacing: strip vanilla. Our own slots + other mods' customs stay.
                foreach (var list in deques.Values)
                {
                    removed += list.RemoveAll(r => r is not BaseLib.Abstracts.CustomRelicModel);
                }
                if (originalsField?.GetValue(bag) is List<RelicModel> originals)
                {
                    removed += originals.RemoveAll(r => r is not BaseLib.Abstracts.CustomRelicModel);
                }
            }
            else
            {
                // Switch/group off (or no captured seed): strip ONLY our own
                // slots (effect-less duds). Vanilla and other mods' relics are
                // restored/untouched.
                foreach (var list in deques.Values)
                {
                    removed += list.RemoveAll(r => r is Models.ChaosRelicModel);
                }
                if (originalsField?.GetValue(bag) is List<RelicModel> originals)
                {
                    removed += originals.RemoveAll(r => r is Models.ChaosRelicModel);
                }
            }

            // Engine-native IsAllowed enforcement: covers other generators'
            // off-states through THEIR IsAllowed, with the engine's own rule.
            if (runState != null)
            {
                AccessTools.Method(bagType, "RemoveDisallowedRelicsFromDeques")
                    ?.Invoke(bag, new object?[] { runState });
            }
            MainFile.Logger.Info($"[QuriousCraftingRelics] pool replacement: removed {removed} relics from the run grab bag " +
                                 $"({deques.Values.Sum(l => l.Count)} remain, master={master}, " +
                                 $"pool={QuriousCraftingRelicsConfig.EnableChaosRelicPool}, " +
                                 $"seedCapture={QuriousCraftingRelicsConfig.EnableSeedCapture}, replacing={replace})");
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
