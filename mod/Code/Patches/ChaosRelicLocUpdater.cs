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

    /// <summary>
    /// Generic title/description the table held BEFORE our first successful
    /// rewrite of each slot key, captured per table generation. Restoring is
    /// only allowed from here, and only while the current value still equals
    /// what we wrote (see <see cref="RestoreGenericText"/>) - a foreign writer
    /// that took the key over is never overwritten.
    /// </summary>
    private static Dictionary<string, (string Title, string Description)>? _originalValues;

    /// <summary>Exactly what this class last wrote per slot key, so the
    /// restore can prove ownership before touching a value.</summary>
    private static Dictionary<string, (string Title, string Description)>? _lastWritten;

    /// <summary>Table instance the originals were captured from. A partial
    /// write (throw before commit) must NOT discard the originals on retry:
    /// the retry would otherwise capture our own generated text as the
    /// "generic" value. Reset only when the table instance really changed.</summary>
    private static LocTable? _originalsTable;

    /// <summary>Seed just captured (may be null when leaving a run).</summary>
    internal static void OnSeedCaptured(string? seed)
    {
        // CONTENT-GATE CONTRACT (2026-10-02): with the master switch or the
        // runtime-hooks group off, the table must go back to the GENERIC
        // fallback text - merely dropping the dedupe key leaves our rewritten
        // entries visible. Restore first (ownership-checked), then reset the
        // dedupe identity so a later re-enable refreshes exactly once.
        if (!QuriousCraftingRelicsConfig.RuntimeHooksActive)
        {
            RestoreGenericText();
            return;
        }

        try
        {
            if (seed is null)
            {
                // Leaving a run: restore the generic text and drop the
                // committed identity so the next capture always refreshes
                // (menu canonical text comes back through ModelDb's own
                // table init).
                RestoreGenericText();
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
            // A new table generation invalidates the previous originals: the
            // table was rebuilt from the localization files (BaseLib wrote the
            // generic text again), so capture the CURRENT values as the
            // originals to restore to.
            if (!ReferenceEquals(relicsTable, _originalsTable) || _originalValues is null)
            {
                _originalValues = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
                _lastWritten = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
                _originalsTable = relicsTable;
            }
            foreach (var (slotModel, definition) in Pools.ChaosRelicRegistry.SlotModels.Zip(definitions, (m, d) => (m, d)))
            {
                string key = slotModel.Id.Entry;
                string titleKey = $"{key}.title";
                string descriptionKey = $"{key}.description";
                string title = definition.Name;
                string description = string.Join("\n", definition.Operations.Select(op => op.Text));
                if (!_originalValues.ContainsKey(key))
                {
                    // Only a value that exists can be restored; a missing key
                    // means another writer removed it - record nulls and skip
                    // the restore for it instead of fabricating text.
                    _originalValues[key] = (
                        dict.TryGetValue(titleKey, out string? beforeTitle) ? beforeTitle : string.Empty,
                        dict.TryGetValue(descriptionKey, out string? beforeDescription) ? beforeDescription : string.Empty);
                }
                dict[titleKey] = title;
                dict[descriptionKey] = description;
                _lastWritten![key] = (title, description);
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

    /// <summary>
    /// Puts the generic title/description back for every slot this class
    /// rewrote. OWNERSHIP-CHECKED: a value is only restored when the current
    /// entry still equals exactly what this class last wrote - if any other
    /// writer (another mod, a localization override, a manual edit) took the
    /// key over, that value is left alone and reported.
    ///
    /// Failure is never reported as success: a missing/regenerated table or a
    /// failed restore keeps the committed identity so a later capture can
    /// retry, and logs the reason.
    /// </summary>
    private static void RestoreGenericText()
    {
        var originals = _originalValues;
        var written = _lastWritten;
        try
        {
            if (originals is null || written is null || originals.Count == 0)
            {
                // Nothing this class ever wrote: only drop the identity.
                _committedKey = null;
                _committedTable = null;
                _originalsTable = null;
                _originalValues = null;
                _lastWritten = null;
                return;
            }

            // Nothing was committed (partial write that threw before the
            // commit): restore whatever entries are still exactly ours.
            var locManager = LocManager.Instance;
            LocTable? relicsTable = null;
            try
            {
                relicsTable = locManager?.GetTable("relics");
            }
            catch
            {
                relicsTable = null;
            }
            var dict = relicsTable is null
                ? null
                : LocDictionaryField?.GetValue(relicsTable) as Dictionary<string, string>;
            if (dict is null)
            {
                // Cannot restore right now. Keep the committed state so the
                // next capture retries; do NOT claim success.
                MainFile.Logger.Error("[QuriousCraftingRelics] loc restore: relics table not found; generic text NOT restored");
                return;
            }

            // A rebuilt table carries the generic text already; the captured
            // originals belong to the old instance and must not be written
            // into the new one. Clear the stale bookkeeping and report. (A
            // partial write that never committed still has _originalsTable ==
            // relicsTable, so it falls through to the ownership-checked
            // restore below instead of being skipped.)
            if (!ReferenceEquals(relicsTable, _originalsTable))
            {
                _committedKey = null;
                _committedTable = null;
                _originalsTable = null;
                _originalValues = null;
                _lastWritten = null;
                MainFile.Logger.Info("[QuriousCraftingRelics] loc restore: table regenerated; generic text already present");
                return;
            }

            bool sameTable = ReferenceEquals(relicsTable, _committedTable);
            int restored = 0;
            int foreign = 0;
            int untouched = 0;
            foreach (var (key, original) in originals)
            {
                if (!written.TryGetValue(key, out var last))
                {
                    continue;
                }
                // Title: restore only if the current value is exactly ours.
                if (dict.TryGetValue($"{key}.title", out string? currentTitle))
                {
                    if (!string.Equals(currentTitle, last.Title, StringComparison.Ordinal))
                    {
                        foreign++; // another writer owns this value now
                    }
                    else if (original.Title.Length > 0)
                    {
                        dict[$"{key}.title"] = original.Title;
                        restored++;
                    }
                    else
                    {
                        dict.Remove($"{key}.title"); // original key did not exist
                        restored++;
                    }
                }
                else
                {
                    untouched++; // someone removed it: nothing to restore
                }
                // Description: same ownership rule.
                if (dict.TryGetValue($"{key}.description", out string? currentDescription))
                {
                    if (!string.Equals(currentDescription, last.Description, StringComparison.Ordinal))
                    {
                        foreign++; // another writer owns this value now
                    }
                    else if (original.Description.Length > 0)
                    {
                        dict[$"{key}.description"] = original.Description;
                        restored++;
                    }
                    else
                    {
                        dict.Remove($"{key}.description"); // original key did not exist
                        restored++;
                    }
                }
                else
                {
                    untouched++; // someone removed it: nothing to restore
                }
            }

            // Success = the restore actually ran on the current table. The
            // committed identity is cleared only here.
            _committedKey = null;
            _committedTable = null;
            _originalsTable = null;
            _originalValues = null;
            _lastWritten = null;
            MainFile.Logger.Info(
                $"[QuriousCraftingRelics] loc restore: generic text restored for {restored} value(s); " +
                $"{foreign} value(s) left to another writer; {untouched} missing" +
                (sameTable ? "" : " (table was regenerated)"));
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] loc restore failed: {e.Message}; generic text NOT restored");
        }
    }
}
