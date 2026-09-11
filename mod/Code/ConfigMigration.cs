using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace QuriousCraftingRelics;

/// <summary>
/// One-time cfg migration for the v0.6.0 rename (AutoAnthonyRelics ->
/// QuriousCraftingRelics). Two things move at the same time:
///
/// 1. the cfg FILENAME. BaseLib derives it from the config class's ROOT
///    NAMESPACE, not from the mod id (BaseLib-StS2 Config/ModConfig.cs:102-120:
///    <c>SpecialCharRegex().Replace(rootNamespace, "")</c> then <c>+ ".cfg"</c>),
///    so the rename to QuriousCraftingRelics silently points BaseLib at a file
///    that does not exist and every user edit falls back to defaults.
/// 2. every template-scoped KEY (Cost_/Refund_/Min_/Max_). The property rename
///    to Title_Snake form (see <see cref="ConfigKeyNaming"/>) changes the
///    serialized name, and BaseLib has NO migration hook to fall back on -
///    grep of ModConfig/SimpleModConfig finds no Migrate, no OnLoad and no
///    version field; the only related member is the virtual
///    RestoreDefaultsNoConfirm.
///
/// Therefore this must run BEFORE <c>new QuriousCraftingRelicsConfig()</c>.
/// MainFile.Initialize calls it immediately before ModConfigRegistry.Register,
/// and the ModConfig constructor chain (CheckConfigProperties -> Init -> Load)
/// is synchronous, so the ordering is deterministic rather than lucky.
///
/// Safety properties:
/// - the old file is never deleted, only renamed to a .bak beside itself;
/// - a value already present in the new-format file is never overwritten
///   (merge by absence), so a half-migrated profile converges instead of
///   losing edits;
/// - idempotent: after the first run no legacy file remains, and
///   ConfigKeyNaming.MigrateKey is a fixed point on migrated keys;
/// - fully swallowed on failure: it returns a report instead of throwing, and
///   it touches no Godot API, so a corrupt cfg can never abort mod init.
/// </summary>
internal static class ConfigMigration
{
    private const string LegacyFileName = "AutoAnthonyRelics.cfg";
    private const string CurrentFileName = "QuriousCraftingRelics.cfg";
    private const string BackupSuffix = ".v0.5.1.bak";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    internal static ConfigMigrationReport MigrateLegacyConfig() => MigrateLegacyConfig(null);

    /// <summary>
    /// Test seam: <paramref name="configDir"/> overrides the live mod_configs
    /// directory so the isolated probe can replay the migration against a copy
    /// of a real cfg instead of the user's own file.
    ///
    /// Returns a report instead of logging. This is not cosmetic: MainFile.Logger
    /// is a static whose initializer reaches into the Godot runtime (Logger's
    /// own static ctor calls OS.GetCmdlineArgs), and under a non-Godot host that
    /// is a NATIVE access violation, not a managed exception - a try/catch
    /// cannot contain it and the process dies. Keeping the migration free of
    /// Godot APIs is the only way it stays verifiable in isolation, and it lets
    /// the caller - which really is inside Godot - own the logging.
    /// </summary>
    internal static ConfigMigrationReport MigrateLegacyConfig(string? configDir)
    {
        try
        {
            string dir = configDir ?? Path.Combine(OS.GetUserDataDir(), "mod_configs");
            string legacyPath = Path.Combine(dir, LegacyFileName);
            string currentPath = Path.Combine(dir, CurrentFileName);
            if (!File.Exists(legacyPath))
            {
                return ConfigMigrationReport.None(); // fresh install, or already migrated.
            }

            Dictionary<string, string>? legacy = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(legacyPath));
            if (legacy is null || legacy.Count == 0)
            {
                return ConfigMigrationReport.None();
            }

            var renamed = new Dictionary<string, string>(legacy.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in legacy)
            {
                renamed[ConfigKeyNaming.MigrateKey(pair.Key)] = pair.Value;
            }

            // Merge target: whatever the new-format file already carries wins.
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            if (File.Exists(currentPath))
            {
                Dictionary<string, string>? existing = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(currentPath));
                if (existing is not null)
                {
                    foreach (KeyValuePair<string, string> pair in existing)
                    {
                        merged[pair.Key] = pair.Value;
                    }
                }
            }

            int carried = 0;
            foreach (KeyValuePair<string, string> pair in renamed)
            {
                if (merged.TryAdd(pair.Key, pair.Value))
                {
                    carried++;
                }
            }

            Directory.CreateDirectory(dir);
            File.WriteAllText(currentPath, JsonSerializer.Serialize(merged, WriteOptions));
            File.Move(legacyPath, legacyPath + BackupSuffix, overwrite: true);

            return ConfigMigrationReport.Success(renamed.Count, carried, LegacyFileName + BackupSuffix);
        }
        catch (Exception e)
        {
            // Never rethrow: a broken cfg must not abort mod init. The caller
            // logs this (it is inside Godot and has a working logger).
            return ConfigMigrationReport.Failure(e);
        }
    }
}

/// <summary>Outcome of one <see cref="ConfigMigration.MigrateLegacyConfig"/> call.</summary>
internal readonly record struct ConfigMigrationReport(
    bool LegacyFileFound, bool Succeeded, int LegacyKeyCount, int CarriedOver,
    string BackupName, Exception? Error)
{
    internal static ConfigMigrationReport None() =>
        new(false, true, 0, 0, string.Empty, null);

    internal static ConfigMigrationReport Success(int legacyKeys, int carried, string backupName) =>
        new(true, true, legacyKeys, carried, backupName, null);

    internal static ConfigMigrationReport Failure(Exception error) =>
        new(true, false, 0, 0, string.Empty, error);

    /// <summary>One-line summary for the mod log, or null when nothing happened.</summary>
    internal string? Describe()
    {
        if (!Succeeded)
        {
            return $"cfg migration failed (continuing with defaults): {Error}";
        }
        if (!LegacyFileFound)
        {
            return null;
        }
        return $"cfg migrated: {LegacyKeyCount} legacy keys, {CarriedOver} carried over " +
               $"({LegacyKeyCount - CarriedOver} already present); old file kept as {BackupName}";
    }
}
