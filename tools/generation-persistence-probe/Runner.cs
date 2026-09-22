using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GenerationPersistenceProbe;

internal sealed class Options
{
    internal string ModDll = "";
    internal string WorkDir = "";
    internal string Mode = "case1-write";
    internal string? LiveA;
    internal string? LiveB;
    internal string? Payload;
    internal string? Manifest;
    internal string? ModManifest;
    internal string? Golden;
    internal string? Overrides;
    internal string? OutPath;
    internal string Seed = "PROBE-SEED-A";
    internal string SecondSeed = "PROBE-SEED-B";
    internal string Version = "probe-1.0";
    internal string ModManifestPath => ModManifest ?? Path.Combine(
        Path.GetDirectoryName(Path.GetFullPath(ModDll)) ?? ".", "QuriousCraftingRelics.json");

    internal string PayloadPath => Payload ?? Path.Combine(WorkDir, "encoded.txt");
    internal string ManifestPath => Manifest ?? Path.Combine(WorkDir, "case1-a.json");
    internal string OutPathValue => OutPath ?? Path.Combine(WorkDir, "live-config.json");

    internal static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string key = args[i];
            string Next()
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException("missing value for " + key);
                }
                return args[++i];
            }
            switch (key)
            {
                case "--mod-dll": o.ModDll = Next(); break;
                case "--workdir": o.WorkDir = Next(); break;
                case "--mode": o.Mode = Next(); break;
                case "--live-a": o.LiveA = Next(); break;
                case "--live-b": o.LiveB = Next(); break;
                case "--payload": o.Payload = Next(); break;
                case "--manifest": o.Manifest = Next(); break;
                case "--mod-manifest": o.ModManifest = Next(); break;
                case "--golden": o.Golden = Next(); break;
                case "--overrides": o.Overrides = Next(); break;
                case "--out": o.OutPath = Next(); break;
                case "--seed": o.Seed = Next(); break;
                case "--second-seed": o.SecondSeed = Next(); break;
                case "--mod-version": o.Version = Next(); break;
                case "--help":
                case "-h":
                    throw new ShowUsageException();
                default:
                    throw new ArgumentException("unknown argument: " + key);
            }
        }
        return o;
    }

    internal static string Usage => string.Join(Environment.NewLine, new[]
    {
        "usage: GenerationPersistenceProbe --mod-dll <QuriousCraftingRelics.dll> --workdir <G: dir> --mode <mode> [options]",
        "  modes: dump-live | case1-write | case1-check | case2 | case3-golden | case4-rejects | transport",
        "  options: --live-a <json> --live-b <json> --payload <file> --manifest <file> --golden <file>",
        "           --overrides <json> --out <file> --seed <s> --second-seed <s> --mod-version <v>",
        "           --mod-manifest <QuriousCraftingRelics.json>  (default: next to --mod-dll)",
        "  dump-live writes a COMPLETE live-config fixture from the DLL's own defaults (plus --overrides);",
        "  it never reads the user's real mod_configs. All writes stay under --workdir.",
    });
}

internal sealed class ShowUsageException : Exception
{
}internal static class Runner
{
    internal static int Run(string[] args)
    {
        Console.WriteLine("=== generation-persistence-probe ===");
        Options options;
        try
        {
            options = Options.Parse(args);
        }
        catch (ShowUsageException)
        {
            Console.WriteLine(Options.Usage);
            return 2;
        }
        if (options.ModDll.Length == 0 || options.WorkDir.Length == 0)
        {
            Console.WriteLine(Options.Usage);
            return 2;
        }
        Directory.CreateDirectory(options.WorkDir);
        var assembly = Assembly.LoadFrom(options.ModDll);
        Refs.Init(assembly, options.ModManifestPath);
        Log.Note("mod assembly loaded: " + assembly.Location);
        Log.Note("mod dll sha256: " + Sha256OfFile(options.ModDll));

        switch (options.Mode)
        {
            case "dump-live":
                return DumpLive(options);
            case "case1-write":
            {
                Live liveA = LoadLive(options.LiveA, "live A");
                return Case1Write(options, liveA, new SnapshotFactory(liveA));
            }
            case "case1-check":
            {
                Live liveA = LoadLive(options.LiveA, "live A");
                Live liveB = LoadLive(options.LiveB, "live B");
                return Case1Check(options, liveB, new SnapshotFactory(liveA));
            }
            case "case2":
            {
                Live liveA = LoadLive(options.LiveA, "live A");
                Live liveB = LoadLive(options.LiveB, "live B");
                return Case2(options, liveA, liveB, new SnapshotFactory(liveA), new SnapshotFactory(liveB));
            }
            case "case3-golden":
            {
                Live liveA = LoadLive(options.LiveA, "live A");
                return Case3Golden(options, liveA, new SnapshotFactory(liveA));
            }
            case "case4-rejects":
            {
                Live liveA = LoadLive(options.LiveA, "live A");
                Live liveB = LoadLive(options.LiveB, "live B");
                return RejectionCases.Run(options, liveA, liveB, new SnapshotFactory(liveA), new SnapshotFactory(liveB));
            }
            case "transport":
            {
                Live liveA = LoadLive(options.LiveA, "live A");
                return TransportCases.Run(options, liveA, new SnapshotFactory(liveA));
            }
            default:
                Console.Error.WriteLine("unknown mode: " + options.Mode);
                Console.WriteLine(Options.Usage);
                return 2;
        }
    }

    internal static Live LoadLive(string? path, string label)
    {
        if (path is null)
        {
            throw new ArgumentException(label + " config not supplied (use --live-a / --live-b)");
        }
        var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException(label + ": expected a JSON object in " + path);
        var live = new Live();
        live.Load(json, label + " (" + path + ")");
        Log.Note(label + " fingerprint: " + Short(live.Fingerprint()));
        return live;
    }

    internal static string Short(string value) =>
        value.Length <= 96 ? value : value.Substring(0, 96) + "...";

    internal static string Sha256OfFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static string Sha256OfString(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    // ---------- persistence registration (the real path MainFile uses) ----------

    internal static Type SaveType => Refs.ModAssembly.GetType(
        "QuriousCraftingRelics.Chaos.ChaosRunIdentitySave", throwOnError: true)!;

    /// <summary>Runs the mod's own BaseLib registration (MainFile.Initialize calls this).</summary>
    internal static void RegisterPersistence()
    {
        Refs.Call(SaveType, "Register");
        bool ok = (bool?)Refs.Get(SaveType, "RegistrationSucceeded") ?? false;
        bool identity = (bool?)Refs.Get(SaveType, "IdentityRegistered") ?? false;
        bool generation = (bool?)Refs.Get(SaveType, "GenerationRegistered") ?? false;
        Log.Check(ok, "setup.persistence.registered",
            "RegistrationSucceeded=" + ok + " identity=" + identity + " generation=" + generation);
        Log.Check(identity, "setup.persistence.identity.key",
            "quriouscraftingrelics_run_identity accepted");
        Log.Check(generation, "setup.persistence.generation.key",
            "quriouscraftingrelics_generation accepted");
    }

    /// <summary>
    /// Forces the persistence-unavailable state the mod must refuse to run
    /// without. WHITE-BOX: writes the static property's backing field directly
    /// (there is no supported way to make BaseLib refuse the registration). The
    /// caller restores the real value afterwards.
    /// </summary>
    internal static bool ForcePersistenceUnavailable()
    {
        var field = BackingField("RegistrationSucceeded");
        if (field is null)
        {
            Log.Skip("case4.registration.failure", "RegistrationSucceeded backing field not found");
            return false;
        }
        field.SetValue(null, false);
        return true;
    }

    internal static void RestorePersistence()
    {
        BackingField("RegistrationSucceeded")?.SetValue(null, true);
    }

    /// <summary>
    /// Backing field of a static auto-property. The compiler emits it with
    /// STATIC | NonPublic, so the field lookup must not be restricted to
    /// instance fields - a lookup that only asks for instance fields silently
    /// returns null and would turn this case into a false skip.
    /// </summary>
    private static FieldInfo? BackingField(string propertyName) =>
        SaveType.GetField("<" + propertyName + ">k__BackingField",
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    internal static object NullRunState()
    {
        var type = Refs.Sts2Assembly.GetType("MegaCrit.Sts2.Core.Runs.NullRunState", throwOnError: true)!;
        return Refs.StaticGet(type, "Instance")
            ?? throw new InvalidOperationException("NullRunState.Instance is null");
    }

    /// <summary>
    /// A DISTINCT IRunState instance per call. The basegame's NullRunState has a
    /// private constructor and a single shared Instance; identity/token storage is
    /// keyed per instance (ConditionalWeakTable), so two saves must be modelled by
    /// two instances or they would share one token slot. Creating them through the
    /// non-public constructor is the only way to get that outside the engine.
    /// </summary>
    internal static object NewRunState()
    {
        var type = Refs.Sts2Assembly.GetType("MegaCrit.Sts2.Core.Runs.NullRunState", throwOnError: true)!;
        return Activator.CreateInstance(type, nonPublic: true)
            ?? throw new InvalidOperationException("NullRunState could not be constructed");
    }

    internal static void RememberToken(object runState, string token) =>
        Refs.Call(SaveType, "Remember", runState, token);

    internal static void RememberPayload(object runState, string payload) =>
        Refs.Call(SaveType, "RememberPayload", runState, payload);

    internal static string? TokenOf(object runState) =>
        (string?)Refs.Call(SaveType, "TokenOf", runState);

    internal static string? PayloadOf(object runState) =>
        (string?)Refs.Call(SaveType, "PayloadOf", runState);
    // ---------- mod-facing helpers ----------

    internal static void ClearActive() => Refs.Call(Refs.Registry, "ClearActiveRun");

    internal sealed class Capture
    {
        internal string Identity = "";
        internal bool Resumed;
        internal int Generation;
        internal string? BlockedReason;
        internal bool Refused;

        public override string ToString() =>
            "identity=" + Identity + " resumed=" + Resumed + " generation=" + Generation
            + (BlockedReason is null ? "" : " blocked=" + BlockedReason);
    }

    /// <summary>
    /// Drives the registry's capture entry point. It returns a
    /// <c>(string? Identity, bool Resumed)</c> tuple, whose element names exist
    /// only at compile time, so the tuple is read positionally and the richer
    /// outcome (generation kind, refusal reason) comes from the registry's own
    /// <c>LastOutcome</c> - the value the engine-side caller reports from.
    /// </summary>
    internal static Capture CaptureRun(object? runState, string seed, bool runStart, long startTimeUnix)
    {
        object? raw = Refs.Call(Refs.Registry, "CaptureRun", runState, seed, runStart, startTimeUnix);
        if (raw is null)
        {
            throw new InvalidOperationException("CaptureRun returned null");
        }
        object? outcome = Refs.Get(Refs.Registry, "LastOutcome")
            ?? throw new InvalidOperationException("registry.LastOutcome is null after a capture");
        return new Capture
        {
            Identity = (string?)Refs.InstanceGet(raw, "Item1") ?? "",
            Resumed = (bool?)Refs.InstanceGet(raw, "Item2") ?? false,
            Generation = Convert.ToInt32(Refs.InstanceGet(outcome, "Generation"), CultureInfo.InvariantCulture),
            BlockedReason = (string?)Refs.InstanceGet(outcome, "BlockedReason"),
            Refused = (bool?)Refs.InstanceGet(outcome, "Refused") ?? false,
        };
    }

    internal static string CurrentIdentity() => (string?)Refs.Get(Refs.Registry, "CurrentIdentity") ?? "";
    internal static string CurrentRunSeed() => (string?)Refs.Get(Refs.Registry, "CurrentRunSeed") ?? "";
    internal static string? CurrentBlockedReason() => (string?)Refs.Get(Refs.Registry, "CurrentBlockedReason");
    internal static object? CurrentSnapshot() => Refs.Get(Refs.Registry, "CurrentSnapshot");

    internal static IReadOnlyList<object>? CurrentDefinitions()
    {
        object? raw = Refs.Get(Refs.Registry, "CurrentDefinitions");
        return raw is null ? null : Defs.AsObjectList(raw);
    }

    internal static int Multiplier() =>
        Convert.ToInt32(Refs.Get(Refs.Cfg, "ChaosRelicMultiplier"), CultureInfo.InvariantCulture);

    /// <summary>
    /// The mod version THIS process reports (registry.ModVersion reads the
    /// loaded-mod manifest; outside the game no mod is loaded, so it is empty).
    /// Fixtures that must look like "a save written by this build" have to mint
    /// with this value, not with a literal - a hard-coded version would be a
    /// known version drift and be refused, which is the codec being right and
    /// the fixture being wrong.
    /// </summary>
    internal static string ModVersion() => (string?)Refs.Call(Refs.Registry, "ModVersion") ?? "";

    internal static IReadOnlyList<object> ForSeed(string seed) =>
        Defs.AsObjectList(Refs.Call(Refs.Registry, "ForSeed", seed, Multiplier())!);

    internal static string MintIdentity(string seed, object snapshot, string modVersion) =>
        (string)Refs.Call(Refs.IdentityType, "Mint", seed, Tag(snapshot), modVersion)!;

    internal static string Tag(object snapshot)
    {
        string fingerprint = (string?)Refs.InstanceGet(snapshot, "CanonicalFingerprint") ?? "";
        return (string)Refs.Call(Refs.IdentityType, "Tag", fingerprint)!;
    }

    internal static string Encode(string identity, object snapshot, IReadOnlyList<object>? definitions)
    {
        var method = Refs.Method(Refs.Codec, "Encode", 3)
            ?? throw new MissingMethodException("QuriousGenerationPersistence.Encode/3 not found");
        var parameters = method.GetParameters();
        var definitionType = Refs.ModAssembly.GetType("QuriousCraftingRelics.Chaos.ChaosRelicDefinition", true)!;
        if (parameters[0].ParameterType != typeof(string)
            || parameters[1].ParameterType != Refs.SnapshotType
            || !parameters[2].ParameterType.IsAssignableFrom(typeof(List<>).MakeGenericType(definitionType)))
        {
            throw new NotSupportedException("Encode signature changed: " + method);
        }
        // A null argument is part of the contract under test (the codec must
        // refuse it), so it is passed through untouched instead of being
        // dereferenced by the fixture builder.
        object? typed = definitions is null ? null : Defs.TypedDefinitions(definitions);
        try
        {
            object? payload = method.Invoke(null, new[] { (object?)identity, snapshot, typed });
            return payload as string ?? throw new InvalidOperationException("Encode returned no string");
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            // Reflection wraps the codec's own exception; the case under test is
            // the TYPE the codec threw, so unwrap it like Decode does.
            throw e.InnerException;
        }
    }

    internal static object Decode(string payload, string identity, string seed)
    {
        var method = Refs.Method(Refs.Codec, "Decode", 3)
            ?? throw new MissingMethodException("QuriousGenerationPersistence.Decode/3 not found");
        try
        {
            return method.Invoke(null, new object?[] { payload, identity, seed })
                ?? throw new InvalidOperationException("Decode returned null");
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            throw e.InnerException;
        }
    }

    internal static IReadOnlyList<object> DefinitionsOf(object saved)
    {
        object? raw = Refs.InstanceGet(saved, "Definitions")
            ?? throw new InvalidOperationException("saved.Definitions is null");
        return Defs.AsObjectList(raw);
    }

    internal static object SnapshotOf(object saved) =>
        Refs.InstanceGet(saved, "Snapshot") ?? throw new InvalidOperationException("saved.Snapshot is null");

    internal static string IdentityOf(object saved) =>
        (string?)Refs.InstanceGet(saved, "Identity") ?? "";

    internal static int MaxEncodedLength()
    {
        object? value = Refs.Get(Refs.Codec, "MaxEncodedLength");
        if (value is null)
        {
            throw new MissingMemberException("QuriousGenerationPersistence.MaxEncodedLength missing");
        }
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    internal static object GenerationContextFor(object snapshot) =>
        Refs.Call(Refs.ContextType, "ForSnapshot", snapshot)
        ?? throw new InvalidOperationException("GenerationContext.ForSnapshot returned null");

    internal static object GenerationContextLive() =>
        Refs.Call(Refs.ContextType, "Live")
        ?? throw new InvalidOperationException("GenerationContext.Live returned null");

    internal static IReadOnlyList<object> Generate(object context, object costs, string seed,
        int budgetCommon, int budgetUncommon, int budgetRare,
        int negCommon, int negUncommon, int negRare)
    {
        // The generator's context overload is internal to the mod (production
        // callers are inside it); the probe reaches it by name like every other
        // non-public member here.
        var method = Refs.GeneratorType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "Generate"
                && m.GetParameters().Length == 9
                && m.GetParameters()[8].ParameterType == Refs.ContextType)
            ?? throw new MissingMethodException("ChaosRelicGenerator.Generate/9 (explicit context) not found");
        object? raw = method.Invoke(null, new object?[]
        {
            seed, budgetCommon, budgetUncommon, budgetRare, costs, negCommon, negUncommon, negRare, context,
        });
        return Defs.AsObjectList(raw!);
    }

    /// <summary>Production-shaped generation for a frozen snapshot (what FreezeNewRun does).</summary>
    internal static IReadOnlyList<object> GenerateForSnapshot(string seed, object snapshot, SnapshotFactory factory) =>
        Generate(
            GenerationContextFor(snapshot),
            factory.CostsOf(snapshot),
            seed,
            Int(snapshot, "BudgetCommon"), Int(snapshot, "BudgetUncommon"), Int(snapshot, "BudgetRare"),
            Int(snapshot, "NegativeChanceCommon"), Int(snapshot, "NegativeChanceUncommon"),
            Int(snapshot, "NegativeChanceRare"));

    /// <summary>Menu/preview-shaped generation from the LIVE config (never a frozen snapshot).</summary>
    internal static IReadOnlyList<object> GenerateLive(string seed, Live live, SnapshotFactory factory) =>
        Generate(
            GenerationContextLive(),
            factory.LiveCosts,
            seed,
            live.Int("ChaosRelicBudgetCommon"), live.Int("ChaosRelicBudgetUncommon"), live.Int("ChaosRelicBudgetRare"),
            live.Int("ChaosRelicNegativeChanceCommon"), live.Int("ChaosRelicNegativeChanceUncommon"),
            live.Int("ChaosRelicNegativeChanceRare"));

    internal static int Int(object instance, string name) =>
        Convert.ToInt32(Refs.InstanceGet(instance, name), CultureInfo.InvariantCulture);
    internal static JsonArray FrozenCostsOf(object snapshot, IEnumerable<string> templates)
    {
        object costs = Refs.InstanceGet(snapshot, "FrozenCosts")
            ?? throw new InvalidOperationException("snapshot.FrozenCosts is null");
        var costMethod = costs.GetType().GetMethod("CostPerPoint")!;
        var refundMethod = costs.GetType().GetMethod("RefundPerPoint")!;
        var array = new JsonArray();
        foreach (string template in templates)
        {
            array.Add(new JsonObject
            {
                ["Template"] = template,
                ["Cost"] = Convert.ToInt32(costMethod.Invoke(costs, new object?[] { template }), CultureInfo.InvariantCulture),
                ["Refund"] = Convert.ToInt32(refundMethod.Invoke(costs, new object?[] { template }), CultureInfo.InvariantCulture),
            });
        }
        return array;
    }

    internal static JsonArray StringArray(object value)
    {
        var array = new JsonArray();
        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
            {
                array.Add((string?)item);
            }
        }
        return array;
    }

    internal static string ScalarJson(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        string s => JsonSerializer.Serialize(s),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        _ => JsonSerializer.Serialize(value.ToString()),
    };

    internal static IEnumerable<string> ActiveTemplates(object snapshot)
    {
        foreach (object? item in (System.Collections.IEnumerable)Refs.InstanceGet(snapshot, "ActivePositiveTemplates")!)
        {
            yield return (string)item!;
        }
        foreach (object? item in (System.Collections.IEnumerable)Refs.InstanceGet(snapshot, "ActiveNegativeTemplates")!)
        {
            yield return (string)item!;
        }
    }

    internal static JsonObject SnapshotSummary(object snapshot)
    {
        var templates = ActiveTemplates(snapshot).ToList();
        return new JsonObject
        {
            ["RunSeed"] = (string?)Refs.InstanceGet(snapshot, "RunSeed") ?? "",
            ["Multiplier"] = Int(snapshot, "Multiplier"),
            ["BudgetCommon"] = Int(snapshot, "BudgetCommon"),
            ["BudgetUncommon"] = Int(snapshot, "BudgetUncommon"),
            ["BudgetRare"] = Int(snapshot, "BudgetRare"),
            ["NegativeChanceCommon"] = Int(snapshot, "NegativeChanceCommon"),
            ["NegativeChanceUncommon"] = Int(snapshot, "NegativeChanceUncommon"),
            ["NegativeChanceRare"] = Int(snapshot, "NegativeChanceRare"),
            ["EnableExtraPool"] = (bool?)Refs.InstanceGet(snapshot, "EnableExtraPool") ?? false,
            ["WatcherModLoaded"] = (bool?)Refs.InstanceGet(snapshot, "WatcherModLoaded") ?? false,
            ["CanonicalFingerprint"] = (string?)Refs.InstanceGet(snapshot, "CanonicalFingerprint") ?? "",
            ["CanonicalCacheKey"] = (string?)Refs.InstanceGet(snapshot, "CanonicalCacheKey") ?? "",
            ["ActivePositiveTemplates"] = StringArray(Refs.InstanceGet(snapshot, "ActivePositiveTemplates")!),
            ["ActiveNegativeTemplates"] = StringArray(Refs.InstanceGet(snapshot, "ActiveNegativeTemplates")!),
            ["FrozenCosts"] = FrozenCostsOf(snapshot, templates),
        };
    }

    /// <summary>Compares the frozen inputs a restored snapshot must reproduce.</summary>
    internal static void CompareSnapshots(JsonObject expected, object actual, string prefix)
    {
        foreach (string field in new[]
        {
            "RunSeed", "Multiplier", "BudgetCommon", "BudgetUncommon", "BudgetRare",
            "NegativeChanceCommon", "NegativeChanceUncommon", "NegativeChanceRare",
            "EnableExtraPool", "WatcherModLoaded", "CanonicalFingerprint", "CanonicalCacheKey",
        })
        {
            string want = expected[field]?.ToJsonString() ?? "null";
            string got = ScalarJson(Refs.InstanceGet(actual, field));
            Log.Check(want == got, prefix + "." + field, "expected=" + want + " actual=" + got);
        }
        foreach (string field in new[] { "ActivePositiveTemplates", "ActiveNegativeTemplates" })
        {
            string want = expected[field]!.ToJsonString();
            string got = StringArray(Refs.InstanceGet(actual, field)!).ToJsonString();
            Log.Check(want == got, prefix + "." + field, "expected=" + Short(want) + " actual=" + Short(got));
        }
        string wantCosts = expected["FrozenCosts"]!.ToJsonString();
        string gotCosts = FrozenCostsOf(actual, ActiveTemplates(actual)).ToJsonString();
        Log.Check(wantCosts == gotCosts, prefix + ".FrozenCosts",
            "expected=" + Short(wantCosts) + " actual=" + Short(gotCosts));
    }

    internal static bool SameDefinitions(JsonNode? expected, IReadOnlyList<object> actual)
    {
        if (expected is null)
        {
            Log.Warn("definition mismatch: the expected side is not valid JSON");
            return false;
        }
        string want = expected.ToJsonString();
        string got = Defs.FormatPool(actual);
        if (want == got)
        {
            return true;
        }
        Log.Warn("definition mismatch: expected=" + Short(want));
        Log.Warn("definition mismatch: actual  =" + Short(got));
        return false;
    }

    // ---------- fixture dump ----------

    private static int DumpLive(Options options)
    {
        var json = new JsonObject();
        int ints = 0;
        int bools = 0;
        foreach (var prop in Refs.Cfg.GetProperties(
                     BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            if (!prop.CanRead)
            {
                continue;
            }
            if (prop.PropertyType == typeof(int))
            {
                json[prop.Name] = Convert.ToInt32(prop.GetValue(null), CultureInfo.InvariantCulture);
                ints++;
            }
            else if (prop.PropertyType == typeof(bool))
            {
                json[prop.Name] = (bool)prop.GetValue(null)!;
                bools++;
            }
        }
        var costs = new JsonObject();
        var costTable = Refs.Get(Refs.Cfg, "PointCosts")!;
        var costMethod = costTable.GetType().GetMethod("CostPerPoint")!;
        var refundMethod = costTable.GetType().GetMethod("RefundPerPoint")!;
        int templates = 0;
        foreach (string template in AllCatalogTemplates())
        {
            costs[template] = new JsonObject
            {
                ["Cost"] = Convert.ToInt32(costMethod.Invoke(costTable, new object?[] { template }), CultureInfo.InvariantCulture),
                ["Refund"] = Convert.ToInt32(refundMethod.Invoke(costTable, new object?[] { template }), CultureInfo.InvariantCulture),
            };
            templates++;
        }
        json["Costs"] = costs;
        json["_comment"] = "generated by GenerationPersistenceProbe dump-live from the DLL defaults; no user config was read";

        if (options.Overrides is not null)
        {
            var overrides = JsonNode.Parse(File.ReadAllText(options.Overrides)) as JsonObject
                ?? throw new InvalidDataException("overrides file is not an object: " + options.Overrides);
            foreach (var (key, value) in overrides)
            {
                json[key] = value?.DeepClone();
            }
            Log.Note("applied overrides from " + options.Overrides);
        }

        File.WriteAllText(options.OutPathValue, json.ToJsonString());
        Log.Check(ints > 0 && templates > 0, "dump-live.written",
            "ints=" + ints.ToString(CultureInfo.InvariantCulture)
            + " bools=" + bools.ToString(CultureInfo.InvariantCulture)
            + " templates=" + templates.ToString(CultureInfo.InvariantCulture)
            + " path=" + options.OutPathValue);
        return Log.Finish();
    }

    internal static IEnumerable<string> AllCatalogTemplates()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string typeName in new[]
        {
            "QuriousCraftingRelics.Chaos.ChaosRelicCatalog",
            "QuriousCraftingRelics.Chaos.ChaosRelicExtraCatalog",
        })
        {
            var type = Refs.ModAssembly.GetType(typeName, throwOnError: false);
            if (type is null)
            {
                continue;
            }
            foreach (string member in new[] { "PositiveTemplates", "NegativeTemplates" })
            {
                if (type.GetProperty(member, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    is System.Collections.IEnumerable list)
                {
                    foreach (object? item in list)
                    {
                        if (item is string template && seen.Add(template))
                        {
                            yield return template;
                        }
                    }
                }
            }
        }
    }
    // ---------- case 1 ----------

    private static int Case1Write(Options options, Live liveA, SnapshotFactory factoryA)
    {
        RegisterPersistence();
        liveA.Apply();
        ClearActive();
        object snapshot = factoryA.Create(options.Seed);
        IReadOnlyList<object> definitions = GenerateForSnapshot(options.Seed, snapshot, factoryA);
        Log.Check(definitions.Count == 60, "case1.generated.count",
            "count=" + definitions.Count.ToString(CultureInfo.InvariantCulture));
        string identity = MintIdentity(options.Seed, snapshot, options.Version);
        Log.Check(identity.StartsWith("qcr1:", StringComparison.Ordinal), "case1.identity.shape", "identity=" + identity);
        string payload = Encode(identity, snapshot, definitions);
        File.WriteAllText(options.PayloadPath, payload);

        var manifest = new JsonObject
        {
            ["mode"] = "case1-a",
            ["seed"] = options.Seed,
            ["identity"] = identity,
            ["fingerprintTag"] = Tag(snapshot),
            ["modVersion"] = options.Version,
            ["payloadLength"] = payload.Length,
            ["payloadSha256"] = Sha256OfString(payload),
            ["liveFingerprintSha256"] = Sha256OfString(liveA.Fingerprint()),
            ["snapshot"] = SnapshotSummary(snapshot),
            ["definitions"] = JsonNode.Parse(Defs.FormatPool(definitions)),
        };
        File.WriteAllText(options.ManifestPath, manifest.ToJsonString());
        Log.Note("payload chars=" + payload.Length.ToString(CultureInfo.InvariantCulture)
            + " sha256=" + Sha256OfString(payload));
        Log.Note("wrote payload " + options.PayloadPath);
        Log.Note("wrote manifest " + options.ManifestPath);
        return Log.Finish();
    }

    private static int Case1Check(Options options, Live liveB, SnapshotFactory factory)
    {
        var manifest = JsonNode.Parse(File.ReadAllText(options.ManifestPath)) as JsonObject
            ?? throw new InvalidDataException("manifest is not an object: " + options.ManifestPath);
        string identity = manifest["identity"]!.GetValue<string>();
        string seed = manifest["seed"]!.GetValue<string>();
        string payload = File.ReadAllText(options.PayloadPath);
        var expectedDefinitions = manifest["definitions"]!;
        var expectedSnapshot = manifest["snapshot"]!.AsObject();

        Log.Check(manifest["payloadSha256"]!.GetValue<string>() == Sha256OfString(payload),
            "case1.payload.sha256", "payload=" + options.PayloadPath);

        string liveBFingerprint = Sha256OfString(liveB.Fingerprint());
        Log.Check(liveBFingerprint != manifest["liveFingerprintSha256"]!.GetValue<string>(),
            "case1.live-b.differs",
            "this process runs a different live config (liveB=" + Short(liveBFingerprint) + ")");

        // This process only ever saw live B.
        liveB.Apply();
        ClearActive();
        object saved = Decode(payload, identity, seed);
        Log.Check(IdentityOf(saved) == identity, "case1.saved.identity", "identity=" + IdentityOf(saved));
        object snapshot = SnapshotOf(saved);
        CompareSnapshots(expectedSnapshot, snapshot, "case1.snapshot");
        Log.Check(SameDefinitions(expectedDefinitions, DefinitionsOf(saved)), "case1.definitions.equal",
            "definitions=" + DefinitionsOf(saved).Count.ToString(CultureInfo.InvariantCulture));

        IReadOnlyList<object> liveBPool = GenerateLive(seed, liveB, factory);
        string liveBJson = Defs.FormatPool(liveBPool);
        Log.Check(expectedDefinitions.ToJsonString() != liveBJson, "case1.live-b.pool.differs",
            "the same seed under live B really produces a different pool (otherwise this case is vacuous)");
        Log.Check(Defs.FormatPool(DefinitionsOf(saved)) != liveBJson, "case1.restored.not.live",
            "the restored pool is not a live-B regeneration");

        object liveBSnapshot = factory.Create(seed);
        Log.Check(Tag(snapshot) != Tag(liveBSnapshot), "case1.fingerprint.differs.from.liveb",
            "restored=" + Tag(snapshot) + " liveB=" + Tag(liveBSnapshot));
        string savedCosts = FrozenCostsOf(snapshot, ActiveTemplates(snapshot)).ToJsonString();
        Log.Check(savedCosts != "[]", "case1.frozen.costs.present",
            "templates=" + ActiveTemplates(snapshot).Count().ToString(CultureInfo.InvariantCulture));
        Log.Note("case1.saved.costs.sha256=" + Short(Sha256OfString(savedCosts)));
        return Log.Finish();
    }

    // ---------- case 3 ----------

    private static int Case3Golden(Options options, Live liveA, SnapshotFactory factoryA)
    {
        if (options.Golden is null)
        {
            throw new ArgumentException("case3-golden needs --golden <golden-baseline.json>");
        }
        liveA.Apply();
        ClearActive();
        var golden = JsonNode.Parse(File.ReadAllText(options.Golden)) as JsonObject
            ?? throw new InvalidDataException("golden file is not an object: " + options.Golden);
        foreach (var (seed, value) in golden)
        {
            object snapshot = factoryA.Create(seed);
            IReadOnlyList<object> pool = GenerateForSnapshot(seed, snapshot, factoryA);
            Log.Check(pool.Count == 60, "case3." + seed + ".count",
                "count=" + pool.Count.ToString(CultureInfo.InvariantCulture));
            Log.Check(SameDefinitions(value!, pool), "case3." + seed + ".golden.equal",
                "slots=" + pool.Count.ToString(CultureInfo.InvariantCulture));
        }
        return Log.Finish();
    }
    // ---------- case 2 ----------

    private static int Case2(Options options, Live liveA, Live liveB, SnapshotFactory factoryA, SnapshotFactory factoryB)
    {
        RegisterPersistence();
        object runState = NullRunState();

        // (a) bounded ledger mechanics, on the shipped RunIdentityLedger<T>.
        object ledger = NewLedger(2);
        liveA.Apply();
        ClearActive();
        object snapA = factoryA.Create(options.Seed);
        string idA = MintIdentity(options.Seed, snapA, options.Version);
        liveB.Apply();
        object snapB = factoryB.Create(options.SecondSeed);
        string idB = MintIdentity(options.SecondSeed, snapB, options.Version);

        PublishContext(ledger, idA, snapA, "payload-a");
        PublishContext(ledger, idB, snapB, "payload-b");
        Log.Check(LedgerCount(ledger) == 2, "case2.ledger.count",
            "count=" + LedgerCount(ledger).ToString(CultureInfo.InvariantCulture));
        PublishContext(ledger, idA, snapA, "payload-a");
        Log.Check(LedgerCount(ledger) == 2, "case2.ledger.refresh.inplace",
            "re-publishing A keeps the count at the limit");
        Log.Check(LedgerHas(ledger, idB), "case2.ledger.refresh.keeps.other", "re-entering A did not evict B");
        string idC = "qcr1:00000000000000c0:00000000000000c0:probe-1.0:PROBE-SEED-C";
        PublishContext(ledger, idC, factoryA.Create("PROBE-SEED-C"), "payload-c");
        Log.Check(!LedgerHas(ledger, idA), "case2.ledger.evicts.oldest", "A evicted at limit=2");
        Log.Check(LedgerHas(ledger, idB) && LedgerHas(ledger, idC), "case2.ledger.keeps.newest", "B+C retained");

        // (b) two payloads sharing ONE seed but different frozen inputs.
        liveA.Apply();
        ClearActive();
        object sameSeedA = factoryA.Create(options.Seed);
        IReadOnlyList<object> defsSameSeedA = GenerateForSnapshot(options.Seed, sameSeedA, factoryA);
        string payloadA = Encode(idA, sameSeedA, defsSameSeedA);
        liveB.Apply();
        object sameSeedB = factoryB.Create(options.Seed);
        IReadOnlyList<object> defsSameSeedB = GenerateForSnapshot(options.Seed, sameSeedB, factoryB);
        string payloadB = Encode(idB, sameSeedB, defsSameSeedB);
        Log.Check(Defs.FormatPool(defsSameSeedA) != Defs.FormatPool(defsSameSeedB),
            "case2.same.seed.different.payloads", "the two saved states really differ");
        object restoredA = Decode(payloadA, idA, options.Seed);
        object restoredB = Decode(payloadB, idB, options.Seed);
        Log.Check(SameDefinitions(JsonNode.Parse(Defs.FormatPool(defsSameSeedA)), DefinitionsOf(restoredA)),
            "case2.isolation.payload.a", "A restored verbatim");
        Log.Check(SameDefinitions(JsonNode.Parse(Defs.FormatPool(defsSameSeedB)), DefinitionsOf(restoredB)),
            "case2.isolation.payload.b", "B restored verbatim");
        CompareSnapshots(SnapshotSummary(sameSeedA), SnapshotOf(restoredA), "case2.isolation.a");
        CompareSnapshots(SnapshotSummary(sameSeedB), SnapshotOf(restoredB), "case2.isolation.b");

        // (c) A -> B -> A through the REAL capture path, using the save's own token/payload slots.
        liveA.Apply();
        ClearActive();
        Capture capA = CaptureRun(runState, options.Seed, runStart: true, 0);
        Log.Check(!capA.Refused && capA.Identity.StartsWith("qcr1:", StringComparison.Ordinal),
            "case2.capture.newrun", capA.ToString());
        string tokenA = TokenOf(runState) ?? "";
        string payloadStoredA = PayloadOf(runState) ?? "";
        Log.Check(tokenA == capA.Identity, "case2.capture.token.remembered", "token=" + Short(tokenA));
        Log.Check(payloadStoredA.Length > 0, "case2.capture.payload.remembered",
            "payload chars=" + payloadStoredA.Length.ToString(CultureInfo.InvariantCulture));
        IReadOnlyList<object> poolA1 = ForSeed(options.Seed);
        IReadOnlyList<object>? defsA1 = CurrentDefinitions();
        Log.Check(defsA1 is not null, "case2.capture.definitions.published",
            "count=" + (defsA1?.Count ?? -1).ToString(CultureInfo.InvariantCulture));

        Capture capB = CaptureRun(runState, options.SecondSeed, runStart: true, 0);
        IReadOnlyList<object> poolB = ForSeed(options.SecondSeed);
        Log.Check(capB.Identity != capA.Identity, "case2.newrun.never.inherits", "B got its own identity");
        Log.Check(Defs.FormatPool(poolA1) != Defs.FormatPool(poolB), "case2.ab.pools.differ", "A pool != B pool");

        // Back to A carrying A's OWN persisted token+payload, under live B.
        RememberToken(runState, tokenA);
        RememberPayload(runState, payloadStoredA);
        Capture capA2 = CaptureRun(runState, options.Seed, runStart: false, 0);
        Log.Check(capA2.Identity == capA.Identity, "case2.ab.a.identity", "A resumed its own identity");
        Log.Check(capA2.Generation == 2, "case2.ab.a.restored",
            "Generation=" + capA2.Generation.ToString(CultureInfo.InvariantCulture) + " (2=Restored)");
        IReadOnlyList<object> poolA2 = ForSeed(options.Seed);
        Log.Check(Defs.FormatPool(poolA2) == Defs.FormatPool(poolA1), "case2.ab.a.definitions.equal",
            "A's relics are the ones from before B was loaded, under a DIFFERENT live config");
        Log.Check(defsA1 is not null && SameDefinitions(JsonNode.Parse(Defs.FormatPool(defsA1)), CurrentDefinitions()!),
            "case2.ab.a.current.definitions.equal", "CurrentDefinitions survived the A->B->A cycle");

        // (d) leaving the run.
        ClearActive();
        Log.Check(CurrentSnapshot() is null, "case2.clear.snapshot.null", "CurrentSnapshot=null");
        Log.Check(CurrentDefinitions() is null, "case2.clear.definitions.null", "CurrentDefinitions=null");
        Log.Check(CurrentRunSeed().Length == 0, "case2.clear.seed.null", "CurrentRunSeed=null");
        Log.Check(CurrentIdentity().Length == 0, "case2.clear.identity.null", "CurrentIdentity=null");
        Log.Check(CurrentBlockedReason() is null, "case2.clear.blocked.null", "CurrentBlockedReason=null");
        Log.Check(LedgerCount(ledger) == 2, "case2.clear.ledger.retained",
            "the probe's ledger is untouched by ClearActiveRun");
        liveB.Apply();
        IReadOnlyList<object> foreign = ForSeed("FOREIGN-SEED-X");
        IReadOnlyList<object> expectedForeign = GenerateLive("FOREIGN-SEED-X", liveB, factoryB);
        Log.Check(Defs.FormatPool(foreign) == Defs.FormatPool(expectedForeign), "case2.foreign.uses.live",
            "a foreign seed is served from the live config");
        Log.Check(CurrentSnapshot() is null, "case2.foreign.no.pollution",
            "CurrentSnapshot still null after the foreign query");
        IReadOnlyList<object> menu = ForSeed(options.Seed);
        Log.Check(Defs.FormatPool(menu) != Defs.FormatPool(poolA1), "case2.menu.not.previous.run",
            "after the run ended, seed A serves the live config, not the ended run");
        Log.Check(CurrentSnapshot() is null, "case2.menu.no.pollution",
            "CurrentSnapshot still null after the menu query");

        // (e) eviction then reload: the payload survives without the retained context.
        Log.Check(!LedgerHas(ledger, idA), "case2.evicted.context.gone", "A's context is gone from the ledger");
        object reloadedA = Decode(payloadA, idA, options.Seed);
        Log.Check(DefinitionsOf(reloadedA).Count == 60, "case2.evicted.payload.still.decodable",
            "definitions=" + DefinitionsOf(reloadedA).Count.ToString(CultureInfo.InvariantCulture));
        return Log.Finish();
    }

    // ---------- ledger helpers ----------

    internal static object NewLedger(int limit)
    {
        var closed = Refs.LedgerType.MakeGenericType(Refs.SnapshotType);
        var ctor = closed.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(int) },
            null)
            ?? throw new MissingMethodException("RunIdentityLedger`1(int) not found on " + closed.FullName);
        return ctor.Invoke(new object?[] { limit })
            ?? throw new InvalidOperationException("RunIdentityLedger construction failed");
    }

    internal static void PublishContext(object ledger, string identity, object snapshot, string? payload) =>
        Refs.InstanceCall(ledger, "PublishContext", identity, snapshot, payload);

    internal static bool LedgerHas(object ledger, string identity)
    {
        object?[] args = { identity, null };
        Refs.TryInstanceCall(ledger, "TryGet", args, out _);
        return args[1] is not null;
    }

    internal static int LedgerCount(object ledger) =>
        Convert.ToInt32(Refs.InstanceGet(ledger, "Count"), CultureInfo.InvariantCulture);

    // ---------- payload fixture helpers ----------

    /// <summary>
    /// Splits a REAL payload produced by the codec into its envelope fields and
    /// its JSON body. Only used to build negative fixtures by mutating the
    /// codec's own output; the envelope shape (magic, schema, semantics, base64
    /// gzip) is READ from the payload, never re-implemented from memory.
    /// </summary>
    internal sealed class PayloadParts
    {
        internal string Magic = "";
        internal string Schema = "";
        internal string Semantics = "";
        internal string Body = "";
        internal JsonNode? Json;
    }

    internal static PayloadParts SplitPayload(string payload)
    {
        string[] parts = payload.Split(':', 4);
        if (parts.Length != 4)
        {
            throw new InvalidDataException("payload does not have 4 envelope fields");
        }
        var result = new PayloadParts
        {
            Magic = parts[0],
            Schema = parts[1],
            Semantics = parts[2],
            Body = parts[3],
        };
        byte[] compressed = Convert.FromBase64String(parts[3]);
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        result.Json = JsonNode.Parse(output.ToArray());
        return result;
    }

    internal static string RebuildPayload(
        PayloadParts parts, JsonNode json, string? schema = null, string? semantics = null)
    {
        byte[] raw = Encoding.UTF8.GetBytes(json.ToJsonString());
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(raw, 0, raw.Length);
        }
        return string.Concat(
            parts.Magic, ":", schema ?? parts.Schema, ":", semantics ?? parts.Semantics, ":",
            Convert.ToBase64String(output.ToArray()));
    }

    /// <summary>A gzip body that expands far past the codec's decompression cap.</summary>
    internal static string CompressionBomb(int megabytes)
    {
        byte[] raw = new byte[megabytes * 1024 * 1024];
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(raw, 0, raw.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }
}