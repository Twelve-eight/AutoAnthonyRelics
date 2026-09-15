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
///
/// DEDUPE COMMIT ORDER (QCR-R4-03, astra round-4): the dedupe identity is
/// committed ONLY after the table write actually succeeded. The previous code
/// set the key up front, so the first call that found no LocManager/table
/// logged an error and every later call with the same key was skipped forever
/// - stuck on a stale success key. LIFECYCLE of the committed identity:
/// - Producer: this class, after a successful write loop.
/// - Owner: process-static, main thread only (called from run-capture postfixes).
/// - Identity: definition context (registry cache key: seed + config
///   fingerprint) + localization/table generation (language code AND the
///   relics LocTable INSTANCE reference).
/// - Invalidation: leaving a run (seed null resets both), a different cache
///   key, a language change, or any table replacement. LocManager.SetLanguage
///   and the English-override toggles rebuild every LocTable object
///   (SetLanguageInternal replaces the whole tables dictionary), so the
///   reference check fires on ANY table regeneration and the next capture
///   refreshes exactly once instead of being stuck on the old success key.
/// </summary>
internal static class ChaosRelicLocUpdater
{
    private static readonly FieldInfo? LocDictionaryField =
        AccessTools.Field(typeof(LocTable), "_translations");

    /// <summary>Dedupe key committed after the last SUCCESSFUL table write:
    /// cacheKey + '\0' + language. Null when no write has succeeded since the
    /// last reset (run leave, or a failed attempt).</summary>
    private static string? _committedKey;

    /// <summary>Relics LocTable instance the last successful write targeted.
    /// A new instance means the table (and its dictionary) was regenerated -
    /// our entries are gone from it and must be rewritten once.</summary>
    private static LocTable? _committedTable;

    /// <summary>Seed just captured (may be null when leaving a run).</summary>
    internal static void OnSeedCaptured(string? seed)
    {
        try
        {
            if (seed is null)
            {
                // Leaving a run: drop the committed identity so the next
                // capture always refreshes (menu canonical text comes back
                // through ModelDb's own table init).
                _committedKey = null;
                _committedTable = null;
                return;
            }

            // Dedupe on seed + config fingerprint, not the seed alone: the
            // definitions can change without the seed changing, and a
            // seed-only skip left tooltips stale while effects drifted
            // (mid-run rebalance + reload re-freeze, 2026-09-13 report).
            // The table identity is part of the key so a language switch or
            // table replacement refreshes exactly once (QCR-R4-03).
            var locManager = LocManager.Instance;
            string? language = locManager?.Language;
            // GetTable throws when the relics table does not exist yet; that
            // lands in the catch below WITHOUT committing the key, so the
            // next capture retries (table-missing then table-ready case).
            LocTable? relicsTable = locManager?.GetTable("relics");
            var dict = relicsTable is null
                ? null
                : LocDictionaryField?.GetValue(relicsTable) as Dictionary<string, string>;

            string cacheKey = ChaosRelicRunRegistry.CurrentCacheKey;
            string attemptKey = cacheKey + "\0" + (language ?? "");
            if (attemptKey == _committedKey && ReferenceEquals(relicsTable, _committedTable))
            {
                return;
            }

            if (locManager is null || dict is null)
            {
                // QCR-R4-03: do NOT commit the dedupe identity here. The
                // first attempt that finds no table must be retried by the
                // next capture instead of being skipped as "already done".
                MainFile.Logger.Error("[QuriousCraftingRelics] loc update: relics table not found");
                return;
            }

            var definitions = ChaosRelicRunRegistry.ForSeed(seed, QuriousCraftingRelicsConfig.ChaosRelicMultiplier);
            int entries = 0;
            foreach (var (slotModel, definition) in Pools.ChaosRelicRegistry.SlotModels.Zip(definitions, (m, d) => (m, d)))
            {
                string key = slotModel.Id.Entry;
                dict[$"{key}.title"] = definition.Name;
                dict[$"{key}.description"] = string.Join("\n", definition.Operations.Select(op => op.Text));
                entries++;
            }

            // Commit ONLY after the write loop succeeded (QCR-R4-03): a
            // failure above leaves the identity uncommitted so the next
            // capture retries instead of skipping.
            _committedKey = attemptKey;
            _committedTable = relicsTable;
            MainFile.Logger.Info($"[QuriousCraftingRelics] relic descriptions updated for seed {seed} ({entries} slots)");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] loc update failed: {e.Message}");
        }
    }
}
