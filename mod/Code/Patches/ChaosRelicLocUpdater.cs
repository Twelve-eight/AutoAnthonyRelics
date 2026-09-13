using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using QuriousCraftingRelics.Chaos;

namespace QuriousCraftingRelics.Patches;

/// <summary>
/// Live relic descriptions (user order 2026-09-08: "遗物需要在描述中显示它的效果").
///
/// BaseLib's ModelLocPatch evaluates ILocalizationProvider.Localization ONCE at
/// ModelDb.Init - at that point CurrentRunSeed is null, so the loc table gets the
/// generic fallback text baked in, and in-game tooltips show no effects.
///
/// Fix: whenever the run seed is captured (new run or save load), rewrite the
/// "relics" loc-table entries for all 60 chaos slots from the LIVE definitions -
/// same mechanism BaseLib itself uses (reflection into LocTable._translations).
/// Called from RunSeedEarlyTrackPatch.Capture and the Launch postfix.
/// </summary>
internal static class ChaosRelicLocUpdater
{
    private static readonly FieldInfo? LocDictionaryField =
        AccessTools.Field(typeof(LocTable), "_translations");

    private static string? _lastKey;

    /// <summary>Seed just captured (may be null when leaving a run).</summary>
    internal static void OnSeedCaptured(string? seed)
    {
        try
        {
            if (seed is null)
            {
                _lastKey = null;
                return;
            }
            // Dedupe on seed + config fingerprint, not the seed alone: the
            // definitions can change without the seed changing, and a
            // seed-only skip left tooltips stale while effects drifted
            // (mid-run rebalance + reload re-freeze, 2026-09-13 report).
            string cacheKey = ChaosRelicRunRegistry.CurrentCacheKey;
            if (cacheKey == _lastKey)
            {
                return;
            }
            _lastKey = cacheKey;
            var definitions = ChaosRelicRunRegistry.ForSeed(seed, QuriousCraftingRelicsConfig.ChaosRelicMultiplier);
            if (LocManager.Instance is null || LocDictionaryField?.GetValue(LocManager.Instance.GetTable("relics"))
                    is not Dictionary<string, string> dict)
            {
                MainFile.Logger.Error("[QuriousCraftingRelics] loc update: relics table not found");
                return;
            }
            int entries = 0;
            foreach (var (slotModel, definition) in Pools.ChaosRelicRegistry.SlotModels.Zip(definitions, (m, d) => (m, d)))
            {
                string key = slotModel.Id.Entry;
                dict[$"{key}.title"] = definition.Name;
                dict[$"{key}.description"] = string.Join("\n", definition.Operations.Select(op => op.Text));
                entries++;
            }
            MainFile.Logger.Info($"[QuriousCraftingRelics] relic descriptions updated for seed {seed} ({entries} slots)");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] loc update failed: {e.Message}");
        }
    }
}
