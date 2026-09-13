using System.Reflection;
using System.Text.Json;
using QuriousCraftingRelics.Chaos;

// Deterministic reproduction of the live generation pipeline.
// Loads the user's LIVE cfg (mod_configs/QuriousCraftingRelics.cfg) into the
// config class's static properties (the same storage the game's lookups
// read), then runs ChaosRelicGenerator.Generate exactly as the registry does.
var modAsm = typeof(ChaosRelicGenerator).Assembly;
var cfgType = modAsm.GetType("QuriousCraftingRelics.QuriousCraftingRelicsConfig")
    ?? throw new InvalidOperationException("config type not found");

string cfgPath = args.Length > 2
    ? args[2]
    : @"C:\Users\o_Obl\AppData\Roaming\SlayTheSpire2\mod_configs\QuriousCraftingRelics.cfg";
var cfgJson = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(cfgPath))
    ?? throw new InvalidOperationException("cfg parse failed");

int applied = 0;
foreach (var (key, value) in cfgJson)
{
    var prop = cfgType.GetProperty(key, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
    if (prop is null || prop.PropertyType != typeof(int)) continue;
    var raw = value.ToString();
    if (!int.TryParse(raw, out var parsed)) continue;
    prop.SetValue(null, parsed);
    applied++;
}
Console.WriteLine($"cfg: applied {applied} int properties from {Path.GetFileName(cfgPath)}");

static object? StaticProp(Type t, string name) =>
    t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
var costs = (ChaosPointCosts?)StaticProp(cfgType, "PointCosts")
    ?? throw new InvalidOperationException("PointCosts unavailable");
int Get(string name) => (int?)StaticProp(cfgType, name) ?? throw new InvalidOperationException(name);

string seed = args.Length > 0 ? args[0] : "E39VJ85JZRBN";
var pool = ChaosRelicGenerator.Generate(seed,
    Get("ChaosRelicBudgetCommon"),
    Get("ChaosRelicBudgetUncommon"),
    Get("ChaosRelicBudgetRare"),
    costs,
    Get("ChaosRelicNegativeChanceCommon"),
    Get("ChaosRelicNegativeChanceUncommon"),
    Get("ChaosRelicNegativeChanceRare"));

int[] owned = args.Length > 1
    ? args[1].Split(',').Select(int.Parse).ToArray()
    : new[] { 13, 34, 36, 52, 54 };

Console.WriteLine($"seed={seed} budgets={Get("ChaosRelicBudgetCommon")}/{Get("ChaosRelicBudgetUncommon")}/{Get("ChaosRelicBudgetRare")} slots={pool.Count}");
foreach (var def in owned.Select(i => pool[i]).OrderBy(d => d.Slot))
{
    Console.WriteLine($"--- CHAOS_RELIC{def.Slot:D3} [{def.Rarity}] {def.Name}");
    foreach (var op in def.Operations)
    {
        Console.WriteLine($"    {op.Template} x{op.Amount}   {op.Text}");
    }
}

Console.WriteLine("=== energy entries across owned slots ===");
foreach (var def in owned.Select(i => pool[i]).OrderBy(d => d.Slot))
{
    int c = def.Sum("C_START_ENERGY");
    int t = def.Sum("T_START_ENERGY");
    int me = def.Sum("PASSIVE_MAX_ENERGY");
    if (c > 0 || t > 0 || me > 0)
    {
        Console.WriteLine($"CHAOS_RELIC{def.Slot:D3}: C_START_ENERGY={c} T_START_ENERGY={t} PASSIVE_MAX_ENERGY={me}");
    }
}

Console.WriteLine("=== hand-selection entries across owned slots ===");
foreach (var def in owned.Select(i => pool[i]).OrderBy(d => d.Slot))
{
    var hand = def.Operations.Where(o => o.Template.StartsWith("X_")).ToList();
    if (hand.Count > 0)
    {
        Console.WriteLine($"CHAOS_RELIC{def.Slot:D3}: {string.Join("; ", hand.Select(o => $"{o.Template} x{o.Amount}"))}");
    }
}
