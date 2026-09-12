using System.Reflection;
using System.Text;
using System.Text.Json;

namespace MigrationProbe;

/// <summary>
/// Isolated probe for the cfg-migration ownership fix (astra-advice item 1).
/// Uses the ConfigMigration configDir test seam; no Godot is touched. The
/// report type is internal, so results are read via reflection.
///
/// Scenarios (each in its own fresh directory):
/// 1. A REAL Qurious legacy cfg (template-scoped + scalar keys) migrates:
///    keys renamed, merged into QuriousCraftingRelics.cfg, legacy file moved
///    to a backup that is NOT overwritten on a second round.
/// 2. The new AutoAnthonyRelics relic mod's cfg (Enabled / ReplaceVanillaRelics
///    / KeepModdedRelics) is LEFT UNTOUCHED - the old code stole this file.
/// 3. An unrelated cfg with none of Qurious's keys is left untouched too.
/// </summary>
internal static class Program
{
    private static int _failures;
    private static MethodInfo _migrate = null!;

    private static void Check(bool condition, string label, string detail = "")
    {
        Console.WriteLine($"{(condition ? "PASS" : "FAIL")}  {label}  {detail}");
        if (!condition)
        {
            _failures++;
        }
    }

    public static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        var asm = typeof(QuriousCraftingRelics.MainFile).Assembly;
        var migration = asm.GetType("QuriousCraftingRelics.ConfigMigration")!;
        _migrate = migration.GetMethod("MigrateLegacyConfig",
            BindingFlags.NonPublic | BindingFlags.Static, new[] { typeof(string) })!;
        var reportType = asm.GetType("QuriousCraftingRelics.ConfigMigrationReport")!;

        string root = Path.Combine(Path.GetTempPath(), "qurious-migration-probe-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);

        // ---- 1. Genuine Qurious legacy cfg migrates.
        string dir1 = Path.Combine(root, "qurious");
        Directory.CreateDirectory(dir1);
        string legacy1 = Path.Combine(dir1, "AutoAnthonyRelics.cfg");
        File.WriteAllText(legacy1, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["EnableChaosRelics"] = "True",
            ["ChaosRelicBudgetCommon"] = "12",
            ["Cost_C_START_DAMAGE_ALL"] = "3",
            ["Cost_C_Start_Block"] = "5", // already Title_Snake: fixed point
        }));
        object report1 = _migrate.Invoke(null, new object?[] { dir1 })!;
        Check(Prop<bool>(reportType, report1, "LegacyFileFound")
            && Prop<bool>(reportType, report1, "Succeeded")
            && !Prop<bool>(reportType, report1, "SkippedNotOurs"),
            "genuine legacy cfg migrates", Describe(reportType, report1));
        var current1 = Read(Path.Combine(dir1, "QuriousCraftingRelics.cfg"));
        Check(current1["EnableChaosRelics"] == "True", "scalar key carried");
        Check(current1["Cost_C_Start_Damage_All"] == "3", "template key renamed to Title_Snake");
        Check(current1["Cost_C_Start_Block"] == "5", "already-migrated key is a fixed point");
        Check(!File.Exists(legacy1) && Directory.GetFiles(dir1, "*.bak").Length == 1,
            "legacy file moved to exactly one backup");
        // Second migration round with a NEW legacy file: existing backup must survive.
        File.WriteAllText(legacy1, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["ChaosRelicBudgetRare"] = "30",
        }));
        object report1b = _migrate.Invoke(null, new object?[] { dir1 })!;
        Check(Prop<bool>(reportType, report1b, "Succeeded")
            && Directory.GetFiles(dir1, "AutoAnthonyRelics.cfg.v0.5.1.bak*").Length == 2,
            "second round keeps the first backup (no overwrite)", Describe(reportType, report1b));

        // ---- 2. The relic mod's cfg is left untouched.
        string dir2 = Path.Combine(root, "relicmod");
        Directory.CreateDirectory(dir2);
        string relicCfg = Path.Combine(dir2, "AutoAnthonyRelics.cfg");
        File.WriteAllText(relicCfg, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["Enabled"] = "True",
            ["ReplaceVanillaRelics"] = "True",
            ["KeepModdedRelics"] = "True",
        }));
        object report2 = _migrate.Invoke(null, new object?[] { dir2 })!;
        Check(Prop<bool>(reportType, report2, "SkippedNotOurs"), "relic mod cfg skipped as not ours", Describe(reportType, report2));
        Check(File.Exists(relicCfg) && Directory.GetFiles(dir2).Length == 1,
            "relic mod cfg untouched: no backup, no merge target written");
        Check(!File.Exists(Path.Combine(dir2, "QuriousCraftingRelics.cfg")),
            "no Qurious cfg materialized from a foreign file");

        // ---- 3. An unrelated cfg is left untouched too.
        string dir3 = Path.Combine(root, "unrelated");
        Directory.CreateDirectory(dir3);
        string otherCfg = Path.Combine(dir3, "AutoAnthonyRelics.cfg");
        File.WriteAllText(otherCfg, JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["SomeOtherModSetting"] = "42",
            ["AnotherToggle"] = "False",
        }));
        object report3 = _migrate.Invoke(null, new object?[] { dir3 })!;
        Check(Prop<bool>(reportType, report3, "SkippedNotOurs"), "unrelated cfg skipped", Describe(reportType, report3));
        Check(File.Exists(otherCfg), "unrelated cfg untouched");

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "PROBE OK" : $"PROBE FAILED: {_failures} check(s)");
        return _failures == 0 ? 0 : 1;
    }

    private static T Prop<T>(Type type, object instance, string name) =>
        (T)type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(instance)!;

    private static string Describe(Type type, object instance) =>
        type.GetMethod("Describe", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(instance, Array.Empty<object>())?.ToString() ?? "";

    private static Dictionary<string, string> Read(string path) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
}
