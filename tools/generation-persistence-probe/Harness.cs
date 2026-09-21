using System.Globalization;
using MegaCrit.Sts2.Core.Runs;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GenerationPersistenceProbe;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Runner.Run(args);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FATAL " + e);
            return 1;
        }
    }
}

/// <summary>
/// Result sink. Every assertion goes through Check; nothing is swallowed.
/// </summary>
internal static class Log
{
    private static readonly List<string> Failures = new();

    internal static int Checks { get; private set; }

    internal static bool Check(bool ok, string id, string detail)
    {
        Checks++;
        if (!ok)
        {
            Failures.Add(id);
        }
        Console.WriteLine((ok ? "PASS " : "FAIL ") + id + (detail.Length == 0 ? "" : "  |  " + detail));
        return ok;
    }

    private static readonly List<string> Skips = new();

    internal static void Note(string message) => Console.WriteLine("NOTE " + message);

    /// <summary>
    /// A case the probe could NOT drive (missing member, or a malformed payload
    /// it cannot construct without format knowledge). Skips are printed and
    /// counted in the summary; they are never reported as passes.
    /// </summary>
    internal static void Skip(string id, string reason)
    {
        Skips.Add(id);
        Console.WriteLine("SKIP " + id + "  |  " + reason);
    }

    internal static void Warn(string message) => Console.WriteLine("WARN " + message);

    internal static int Finish()
    {
        Console.WriteLine("RESULT checks=" + Checks.ToString(CultureInfo.InvariantCulture)
            + " failures=" + Failures.Count.ToString(CultureInfo.InvariantCulture)
            + " skipped=" + Skips.Count.ToString(CultureInfo.InvariantCulture)
            + (Failures.Count == 0 ? "" : " failedIds=" + string.Join(",", Failures))
            + (Skips.Count == 0 ? "" : " skippedIds=" + string.Join(",", Skips)));
        return Failures.Count == 0 ? 0 : 1;
    }
}

/// <summary>
/// Reflection surface over the built mod DLL. Members that are not part of the
/// agreed PLAN contract are looked up by name and REPORTED when missing; the
/// probe never invents a substitute for a member that is not there.
/// </summary>
internal static class Refs
{
    private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly Dictionary<string, PropertyInfo?> PropCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, FieldInfo?> FieldCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MethodInfo?> MethodCache = new(StringComparer.Ordinal);

    internal static Assembly ModAssembly = null!;
    internal static Assembly Sts2Assembly = null!;
    internal static Type Cfg = null!;
    internal static Type Registry = null!;
    internal static Type Codec = null!;
    internal static Type SavedGeneration = null!;
    internal static Type SnapshotType = null!;
    internal static Type CostsType = null!;
    internal static Type IdentityType = null!;
    internal static Type GeneratorType = null!;
    internal static Type ContextType = null!;

    /// <summary>
    /// Resolved lazily: the ledger is an internal helper, and a missing one must
    /// fail only the case that needs it, not the whole probe run.
    /// </summary>
    internal static Type LedgerType
    {
        get
        {
            if (_ledgerType is null)
            {
                _ledgerType = ModAssembly.GetType("QuriousCraftingRelics.Chaos.RunIdentityLedger`1", throwOnError: false)
                    ?? throw new TypeLoadException("RunIdentityLedger`1 not found in " + ModAssembly.Location);
            }
            return _ledgerType;
        }
    }

    private static Type? _ledgerType;

    internal static void Init(Assembly asm)
    {
        ModAssembly = asm;
        Sts2Assembly = typeof(MegaCrit.Sts2.Core.Runs.IRunState).Assembly;
        Cfg = Require("QuriousCraftingRelics.QuriousCraftingRelicsConfig");
        Registry = Require("QuriousCraftingRelics.Chaos.ChaosRelicRunRegistry");
        Codec = Require("QuriousCraftingRelics.Chaos.QuriousGenerationPersistence");
        SavedGeneration = Require("QuriousCraftingRelics.Chaos.QuriousSavedGeneration");
        SnapshotType = Require("QuriousCraftingRelics.Chaos.QuriousGenerationSnapshot");
        CostsType = Require("QuriousCraftingRelics.Chaos.ChaosPointCosts");
        IdentityType = Require("QuriousCraftingRelics.Chaos.ChaosRunIdentity");
        GeneratorType = Require("QuriousCraftingRelics.Chaos.ChaosRelicGenerator");
        ContextType = Require("QuriousCraftingRelics.Chaos.GenerationContext");

    }

    private static Type Require(string fullName) =>
        ModAssembly.GetType(fullName, throwOnError: false)
        ?? throw new TypeLoadException("type not found in " + ModAssembly.Location + ": " + fullName);

    internal static bool HasType(string fullName) => ModAssembly.GetType(fullName, throwOnError: false) is not null;

    private static string Key(Type type, string name) => type.FullName + "|" + name;

    internal static PropertyInfo? Prop(Type type, string name)
    {
        string key = Key(type, name);
        if (PropCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var prop = type.GetProperty(name, AnyStatic) ?? type.GetProperty(name, AnyInstance);
        PropCache[key] = prop;
        return prop;
    }

    internal static FieldInfo? Field(Type type, string name)
    {
        string key = Key(type, name);
        if (FieldCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var field = type.GetField(name, AnyStatic) ?? type.GetField(name, AnyInstance);
        FieldCache[key] = field;
        return field;
    }

    internal static MethodInfo? Method(Type type, string name, int parameterCount)
    {
        string key = Key(type, name) + "/" + parameterCount.ToString(CultureInfo.InvariantCulture);
        if (MethodCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var method = type.GetMethods(AnyStatic)
            .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == parameterCount)
            ?? type.GetMethods(AnyInstance)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == parameterCount);
        MethodCache[key] = method;
        return method;
    }

    internal static bool HasMember(Type type, string name) =>
        Prop(type, name) is not null || Field(type, name) is not null;

    internal static object? StaticGet(Type type, string name)
    {
        var prop = type.GetProperty(name, AnyStatic);
        if (prop is not null)
        {
            return prop.GetValue(null);
        }
        var field = type.GetField(name, AnyStatic);
        if (field is not null)
        {
            return field.GetValue(null);
        }
        throw new MissingMemberException(type.FullName, name);
    }

    internal static object? Get(Type type, string name)
    {
        var prop = Prop(type, name);
        if (prop is not null)
        {
            return prop.GetValue(null);
        }
        var field = Field(type, name);
        if (field is not null)
        {
            return field.GetValue(null);
        }
        throw new MissingMemberException(type.FullName, name);
    }

    internal static bool TryGet(Type type, string name, out object? value)
    {
        var prop = Prop(type, name);
        if (prop is not null)
        {
            value = prop.GetValue(null);
            return true;
        }
        var field = Field(type, name);
        if (field is not null)
        {
            value = field.GetValue(null);
            return true;
        }
        value = null;
        return false;
    }

    internal static void Set(Type type, string name, object? value)
    {
        var prop = Prop(type, name);
        if (prop is not null && prop.CanWrite)
        {
            prop.SetValue(null, value);
            return;
        }
        var field = Field(type, name);
        if (field is not null)
        {
            field.SetValue(null, value);
            return;
        }
        throw new MissingMemberException(type.FullName, name);
    }

    internal static object? Call(Type type, string name, params object?[] args)
    {
        var method = Method(type, name, args.Length)
            ?? throw new MissingMethodException(type.FullName + "." + name + "/" + args.Length);
        return method.Invoke(null, args);
    }

    internal static object? InstanceGet(object instance, string name)
    {
        var type = instance.GetType();
        var prop = type.GetProperty(name, AnyInstance) ?? Prop(type, name);
        if (prop is not null)
        {
            return prop.GetValue(instance);
        }
        var field = type.GetField(name, AnyInstance) ?? Field(type, name);
        if (field is not null)
        {
            return field.GetValue(instance);
        }
        throw new MissingMemberException(type.FullName, name);
    }

    /// <summary>Sets an instance property or field (init-only included).</summary>
    internal static bool TrySetMember(object target, string name, object? value)
    {
        var type = target.GetType();
        var prop = type.GetProperty(name, AnyInstance) ?? Prop(type, name);
        if (prop is not null && (prop.CanWrite || prop.SetMethod is not null))
        {
            prop.SetValue(target, value);
            return true;
        }
        var field = type.GetField(name, AnyInstance) ?? Field(type, name);
        if (field is not null && !field.IsInitOnly)
        {
            field.SetValue(target, value);
            return true;
        }
        return false;
    }

    /// <summary>Invokes an instance method on the concrete runtime type.</summary>
    internal static object? InstanceCall(object instance, string name, params object?[] args)
    {
        var method = instance.GetType().GetMethods(AnyInstance)
            .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length)
            ?? Method(instance.GetType(), name, args.Length)
            ?? throw new MissingMethodException(instance.GetType().FullName + "." + name);
        return method.Invoke(instance, args);
    }

    internal static bool TryInstanceCall(object instance, string name, object?[] args, out object? result)
    {
        var method = instance.GetType().GetMethods(AnyInstance)
            .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length);
        if (method is null)
        {
            result = null;
            return false;
        }
        result = method.Invoke(instance, args);
        return true;
    }
}

/// <summary>
/// Canonical text form of generated definitions. Field names and the slot order
/// match the coordinator's frozen golden JSON, so the two are byte-comparable.
/// </summary>
internal static class Defs
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        // Default encoder: non-ASCII is escaped, so probe output stays ASCII.
    };

    internal static JsonObject ToJson(object definition)
    {
        var obj = new JsonObject
        {
            ["Slot"] = Defs.SlotOf(definition),
            ["Rarity"] = Defs.RarityOf(definition),
            ["Name"] = Defs.NameOf(definition),
        };
        var ops = new JsonArray();
        foreach (object op in Defs.OpsOf(definition))
        {
            ops.Add(new JsonObject
            {
                ["Template"] = Defs.TemplateOf(op),
                ["Amount"] = Defs.AmountOf(op),
                ["Text"] = Defs.TextOf(op),
            });
        }
        obj["Operations"] = ops;
        return obj;
    }

    internal static string FormatOne(object definition) => ToJson(definition).ToJsonString();

    internal static string Format(IEnumerable<object> definitions)
    {
        var obj = new JsonObject();
        foreach (object definition in definitions)
        {
            obj["slot_" + SlotOf(definition).ToString(CultureInfo.InvariantCulture)] = ToJson(definition);
        }
        return obj.ToJsonString();
    }

    internal static string FormatPool(IEnumerable<object> definitions)
    {
        var arr = new JsonArray();
        foreach (object definition in definitions)
        {
            arr.Add(ToJson(definition));
        }
        return arr.ToJsonString();
    }

    internal static string FormatGolden(IEnumerable<object> definitions)
    {
        // Same shape as the golden file value: an array of definition objects.
        return FormatPool(definitions);
    }

    internal static int SlotOf(object definition) =>
        Convert.ToInt32(Refs.InstanceGet(definition, "Slot"), CultureInfo.InvariantCulture);

    internal static int RarityOf(object definition) =>
        Convert.ToInt32(Refs.InstanceGet(definition, "Rarity"), CultureInfo.InvariantCulture);

    internal static string NameOf(object definition) =>
        (string?)Refs.InstanceGet(definition, "Name") ?? "";

    internal static IReadOnlyList<object> OpsOf(object definition)
    {
        var raw = Refs.InstanceGet(definition, "Operations");
        if (raw is System.Collections.IEnumerable enumerable)
        {
            var list = new List<object>();
            foreach (object? item in enumerable)
            {
                if (item is not null)
                {
                    list.Add(item);
                }
            }
            return list;
        }
        throw new InvalidOperationException("definition.Operations is not enumerable");
    }

    internal static string TemplateOf(object op) => (string?)Refs.InstanceGet(op, "Template") ?? "";
    internal static int AmountOf(object op) => Convert.ToInt32(Refs.InstanceGet(op, "Amount"), CultureInfo.InvariantCulture);
    internal static string TextOf(object op) => (string?)Refs.InstanceGet(op, "Text") ?? "";

    internal static IReadOnlyList<object> AsObjectList(object raw)
    {
        if (raw is System.Collections.IEnumerable enumerable)
        {
            var list = new List<object>();
            foreach (object? item in enumerable)
            {
                if (item is not null)
                {
                    list.Add(item);
                }
            }
            return list;
        }
        throw new InvalidOperationException("value is not enumerable: " + raw.GetType().FullName);
    }

    /// <summary>Builds a ChaosRelicDefinition instance (records: public ctor).</summary>
    internal static object MakeDefinition(int slot, object rarity, string name, IReadOnlyList<object> ops)
    {
        var type = Refs.ModAssembly.GetType("QuriousCraftingRelics.Chaos.ChaosRelicDefinition", throwOnError: true)!;
        var opType = Refs.ModAssembly.GetType("QuriousCraftingRelics.Chaos.ChaosRelicOperation", throwOnError: true)!;
        var listType = typeof(List<>).MakeGenericType(opType);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (object op in ops)
        {
            list.Add(op);
        }
        var ctor = type.GetConstructors().Single();
        return ctor.Invoke(new object?[] { slot, rarity, name, list });
    }

    /// <summary>
    /// Converts a definition list into the exact List&lt;ChaosRelicDefinition&gt;
    /// type the codec's IReadOnlyList parameter expects (reflection Invoke does
    /// not accept List&lt;object&gt; there).
    /// </summary>
    internal static object TypedDefinitions(IEnumerable<object> definitions)
    {
        var type = Refs.ModAssembly.GetType("QuriousCraftingRelics.Chaos.ChaosRelicDefinition", throwOnError: true)!;
        var listType = typeof(List<>).MakeGenericType(type);
        var list = (System.Collections.IList)Activator.CreateInstance(listType)!;
        foreach (object definition in definitions)
        {
            list.Add(definition);
        }
        return list;
    }

    internal static object MakeOperation(string template, int amount, string text)
    {
        var opType = Refs.ModAssembly.GetType("QuriousCraftingRelics.Chaos.ChaosRelicOperation", throwOnError: true)!;
        var ctor = opType.GetConstructors().Single();
        return ctor.Invoke(new object?[] { template, amount, text });
    }
}

/// <summary>
/// Live generation config values, applied to the mod's static config surface.
/// Loaded from a JSON file supplied by the caller - the probe never reads the
/// user's real mod_configs.
/// </summary>
internal sealed class Live
{
    private readonly Dictionary<string, int> _ints = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _bools = new(StringComparer.Ordinal);

    internal JsonObject CostsJson { get; private set; } = new();

    internal void Load(JsonObject json, string origin)
    {
        foreach (var (key, value) in json)
        {
            if (key.StartsWith("_", StringComparison.Ordinal))
            {
                // Documentation-only keys ("_comment", "_notes", ...) are ignored.
                continue;
            }
            if (key == "Costs")
            {
                CostsJson = value as JsonObject
                    ?? throw new InvalidDataException(origin + ": Costs must be an object");
                continue;
            }
            if (value is null)
            {
                continue;
            }
            switch (value.GetValueKind())
            {
                case JsonValueKind.True:
                case JsonValueKind.False:
                    _bools[key] = value.GetValue<bool>();
                    break;
                case JsonValueKind.Number:
                    _ints[key] = value.GetValue<int>();
                    break;
                default:
                    throw new InvalidDataException(origin + ": unsupported value for " + key);
            }
        }
    }

    internal int Int(string name) =>
        _ints.TryGetValue(name, out int value)
            ? value
            : throw new KeyNotFoundException("live value not provided: " + name);

    internal bool Bool(string name) =>
        _bools.TryGetValue(name, out bool value)
            ? value
            : throw new KeyNotFoundException("live bool not provided: " + name);

    internal IReadOnlyDictionary<string, int> Ints => _ints;
    internal IReadOnlyDictionary<string, bool> Bools => _bools;

    /// <summary>Applies the values through the mod's own static members.</summary>
    internal void Apply()
    {
        foreach (var (key, value) in _ints)
        {
            Refs.Set(Refs.Cfg, key, value);
        }
        foreach (var (key, value) in _bools)
        {
            Refs.Set(Refs.Cfg, key, value);
        }
    }

    internal string Fingerprint()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var key in _ints.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            sb.Append(key).Append('=').Append(_ints[key]).Append(';');
        }
        foreach (var key in _bools.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            sb.Append(key).Append('=').Append(_bools[key] ? '1' : '0').Append(';');
        }
        sb.Append("costs=").Append(CostsJson.ToJsonString());
        return sb.ToString();
    }
}

/// <summary>Cost/refund lookup tables fed into a ChaosPointCosts instance.</summary>
internal sealed class CostsSource
{
    private readonly Dictionary<string, int> _costs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _refunds = new(StringComparer.Ordinal);

    internal int Count => _costs.Count;

    internal void Load(JsonObject json, string origin)
    {
        foreach (var (template, entry) in json)
        {
            if (entry is not JsonObject obj)
            {
                throw new InvalidDataException(origin + ": costs entry for " + template + " must be an object");
            }
            if (obj.TryGetPropertyValue("Cost", out var cost) && cost is not null)
            {
                _costs[template] = cost.GetValue<int>();
            }
            if (obj.TryGetPropertyValue("Refund", out var refund) && refund is not null)
            {
                _refunds[template] = refund.GetValue<int>();
            }
        }
    }

    public int? Cost(string template) => _costs.TryGetValue(template, out int value) ? value : null;

    public int? Refund(string template) => _refunds.TryGetValue(template, out int value) ? value : null;

    /// <summary>Builds a ChaosPointCosts over these tables (public ctor contract).</summary>
    internal object Build()
    {
        var ctor = Refs.CostsType.GetConstructors().Single();
        var parameters = ctor.GetParameters();
        if (parameters.Length != 2
            || parameters[0].ParameterType != typeof(Func<string, int?>)
            || parameters[1].ParameterType != typeof(Func<string, int?>))
        {
            throw new NotSupportedException(
                "ChaosPointCosts ctor shape changed: " + string.Join(", ", parameters.Select(p => p.ParameterType.FullName)));
        }
        var cost = (Func<string, int?>)Delegate.CreateDelegate(
            typeof(Func<string, int?>), this, nameof(Cost))!;
        var refund = (Func<string, int?>)Delegate.CreateDelegate(
            typeof(Func<string, int?>), this, nameof(Refund))!;
        return ctor.Invoke(new object?[] { cost, refund });
    }
}

/// <summary>Freezes snapshots through the mod's own Capture entry point.</summary>
internal sealed class SnapshotFactory
{
    private readonly Live _live;
    private object? _liveCosts;

    internal SnapshotFactory(Live live) => _live = live;

    internal object Create(string seed)
    {
        var method = Refs.Method(Refs.SnapshotType, "Capture", 1)
            ?? throw new MissingMethodException("QuriousGenerationSnapshot.Capture(string) not found");
        object? snapshot = method.Invoke(null, new object?[] { seed });
        return snapshot ?? throw new InvalidOperationException("Capture returned null for seed " + seed);
    }

    /// <summary>Frozen per-point table of a snapshot (falls back to the live one).</summary>
    internal object CostsOf(object snapshot) =>
        Refs.InstanceGet(snapshot, "FrozenCosts") ?? LiveCosts;

    /// <summary>Live per-point table built from the probe's own cost JSON.</summary>
    internal object LiveCosts
    {
        get
        {
            if (_liveCosts is null)
            {
                _liveCosts = BuildLiveCosts();
            }
            return _liveCosts;
        }
    }

    private object BuildLiveCosts()
    {
        var source = new CostsSource();
        if (_live.CostsJson.Count > 0)
        {
            source.Load(_live.CostsJson, "live.costs");
            return source.Build();
        }
        // No explicit table: read the live table the mod itself exposes.
        var costs = Refs.Get(Refs.Cfg, "PointCosts")
            ?? throw new InvalidOperationException("live costs unavailable: neither Costs nor PointCosts");
        Log.Warn("live.costs absent; using QuriousCraftingRelicsConfig.PointCosts for live generation");
        return costs;
    }
}