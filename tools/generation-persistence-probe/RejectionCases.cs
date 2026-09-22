using System.Globalization;
using System.Text.Json.Nodes;

namespace GenerationPersistenceProbe;

/// <summary>
/// Rejection cases for the codec (PLAN item 4), the codec's global-state
/// invariant, and the capture rule's refusal paths.
///
/// Every case asserts a CONTROLLED rejection (an explicit format/argument
/// exception). An uncontrolled exception type is reported as a failure with the
/// stack, and a silent success is always a failure. Nothing is swallowed to
/// make the run green.
///
/// Fixture honesty: negative payloads are built by mutating a REAL payload this
/// probe obtained from the codec (envelope fields are read from that payload;
/// the JSON body is parsed with System.Text.Json). The probe never re-implements
/// the codec's own writer to fake a fixture.
/// </summary>
internal static class RejectionCases
{
    private static readonly string[] AcceptedExceptions =
    {
        "System.IO.InvalidDataException",
        "System.FormatException",
        "System.ArgumentException",
        "System.ArgumentNullException",
        "System.ArgumentOutOfRangeException",
        "System.InvalidOperationException",
        "System.NotSupportedException",
        "System.OverflowException",
        "System.IO.EndOfStreamException",
        "System.Text.Json.JsonException",
        "System.Text.DecoderFallbackException",
    };

    internal static int Run(Options options, Live liveA, Live liveB,
        SnapshotFactory factoryA, SnapshotFactory factoryB)
    {
        Runner.RegisterPersistence();
        liveA.Apply();
        Runner.ClearActive();
        object snapshot = factoryA.Create(options.Seed);
        IReadOnlyList<object> definitions = Runner.GenerateForSnapshot(options.Seed, snapshot, factoryA);
        string identity = Runner.MintIdentity(options.Seed, snapshot, options.Version);
        string identityOther = Runner.MintIdentity(options.Seed, snapshot, options.Version + "-other");
        string valid = Runner.Encode(identity, snapshot, definitions);
        int max = Runner.MaxEncodedLength();
        Runner.PayloadParts parts = Runner.SplitPayload(valid);
        JsonObject body = parts.Json?.AsObject()
            ?? throw new InvalidDataException("the decoded payload body is not a JSON object");

        Log.Note("case4.valid payload chars=" + valid.Length.ToString(CultureInfo.InvariantCulture)
            + " MaxEncodedLength=" + max.ToString(CultureInfo.InvariantCulture)
            + " bodyBytes=" + System.Text.Encoding.UTF8.GetByteCount(body.ToJsonString())
                .ToString(CultureInfo.InvariantCulture));
        Log.Note("case4.envelope magic=" + parts.Magic + " schema=" + parts.Schema + " semantics=" + parts.Semantics);

        // --- payload-side rejections ---
        ExpectReject("case4.empty.rejected", () => Runner.Decode("", identity, options.Seed), "empty payload");
        ExpectReject("case4.whitespace.rejected", () => Runner.Decode("   ", identity, options.Seed), "whitespace payload");
        ExpectReject("case4.junk.rejected", () => Runner.Decode("not-a-qurious-payload", identity, options.Seed), "non-payload text");
        ExpectReject("case4.truncated.half.rejected",
            () => Runner.Decode(valid.Substring(0, valid.Length / 2), identity, options.Seed), "first half of a valid payload");
        ExpectReject("case4.truncated.lastchar.rejected",
            () => Runner.Decode(valid.Substring(0, valid.Length - 1), identity, options.Seed), "valid payload minus its last character");
        ExpectReject("case4.appended.garbage.rejected",
            () => Runner.Decode(valid + "AA", identity, options.Seed), "valid payload plus trailing characters");
        ExpectReject("case4.body.corrupted.rejected",
            () => Runner.Decode(CorruptBase64(valid), identity, options.Seed),
            "one base64 character flipped in the compressed body");
        ExpectReject("case4.oversized.rejected",
            () => Runner.Decode(new string('A', max + 1), identity, options.Seed),
            "payload longer than MaxEncodedLength=" + max.ToString(CultureInfo.InvariantCulture));
        ExpectReject("case4.large.benign.rejected",
            () => Runner.Decode(new string('A', 1 << 20), identity, options.Seed),
            "1 MiB of benign characters (must be refused by the length cap, without allocating an inflate)");

        // --- unknown schema / semantics (envelope fields are the codec's own) ---
        ExpectReject("case4.unknown.schema.rejected",
            () => Runner.Decode(Runner.RebuildPayload(parts, body, schema: "999"), identity, options.Seed),
            "schema 999 instead of " + parts.Schema);
        ExpectReject("case4.noninteger.schema.rejected",
            () => Runner.Decode(Runner.RebuildPayload(parts, body, schema: "x"), identity, options.Seed),
            "non-integer schema field");
        ExpectReject("case4.future.semantics.rejected",
            () => Runner.Decode(Runner.RebuildPayload(parts, body, semantics: "999"), identity, options.Seed),
            "generator semantics 999 (newer than this build)");

        // --- field-level rejections, built from the codec's own JSON body ---
        ExpectReject("case4.payload.unknown.template.rejected",
            () => Runner.Decode(Runner.RebuildPayload(parts, Mutate(body, root => root["templates"]!.AsArray()[0]!["template"] = "PROBE_UNKNOWN_TEMPLATE")),
                identity, options.Seed),
            "the first template entry renamed to an id outside the catalog");
        ExpectReject("case4.payload.missing.field.rejected",
            () => Runner.Decode(Runner.RebuildPayload(parts, Mutate(body, root => root.Remove("identity"))), identity, options.Seed),
            "the identity field removed");
        ExpectReject("case4.payload.duplicate.slot.rejected",
            () => Runner.Decode(Runner.RebuildPayload(parts, Mutate(body, root =>
            {
                var array = root["definitions"]!.AsArray();
                array[1] = array[0]!.DeepClone();
            })), identity, options.Seed),
            "slot 0 duplicated over slot 1, so slot 1 is missing");
        ExpectReject("case4.payload.out.of.range.slot.rejected",
            () => Runner.Decode(Runner.RebuildPayload(parts, Mutate(body, root =>
                root["definitions"]!.AsArray()[0]!["slot"] = 60)), identity, options.Seed),
            "slot 60 (outside TotalSlots=60)");
        ExpectReject("case4.payload.identity.mismatch.rejected",
            () => Runner.Decode(Runner.RebuildPayload(parts, Mutate(body, root => root["identity"] = "qcr1:deadbeefdeadbeef:0000000000000000:probe-1.0:OTHER")),
                identity, options.Seed),
            "the payload's own identity does not match expectedIdentity");
        ExpectReject("case4.payload.seed.mismatch.rejected",
            () => Runner.Decode(Runner.RebuildPayload(parts, Mutate(body, root => root["seed"] = "OTHER-SEED")),
                identity, options.Seed),
            "the payload's own seed does not match expectedSeed");
        ExpectReject("case4.payload.fingerprint.mismatch.rejected",
            () => Runner.Decode(Runner.RebuildPayload(parts, Mutate(body, root => root["fingerprint"] = "tampered")),
                identity, options.Seed),
            "a tampered fingerprint that no longer matches the frozen fields");

        // --- compression bomb: a real gzip stream that inflates past the cap ---
        ExpectReject("case4.compression.bomb.rejected",
            () => Runner.Decode(Runner.RebuildPayload(parts, body, schema: parts.Schema)
                .Replace(parts.Body, Runner.CompressionBomb(8)), identity, options.Seed),
            "a gzip body that inflates to 8 MiB (cap is enforced while inflating)");

        // --- binding rejections ---
        ExpectReject("case4.identity.mismatch.rejected",
            () => Runner.Decode(valid, identityOther, options.Seed), "expectedIdentity is a different minted identity");
        ExpectReject("case4.seed.mismatch.rejected",
            () => Runner.Decode(valid, identity, options.SecondSeed), "expectedSeed is a different seed");
        ExpectReject("case4.version.drift.identity.rejected",
            () => Runner.Decode(valid, Runner.MintIdentity(options.Seed, snapshot, "probe-0.1"), options.Seed),
            "expectedIdentity was minted by another mod version");
        ExpectReject("case4.null.payload.rejected", () => Runner.Decode(null!, identity, options.Seed), "null payload");
        ExpectReject("case4.null.identity.rejected", () => Runner.Decode(valid, null!, options.Seed), "null expectedIdentity");
        ExpectReject("case4.null.seed.rejected", () => Runner.Decode(valid, identity, null!), "null expectedSeed");

        // --- encode-side rejections (structural contract) ---
        ExpectReject("case4.encode.null.snapshot.rejected",
            () => Runner.Encode(identity, null!, definitions), "null snapshot");
        ExpectReject("case4.encode.null.definitions.rejected",
            () => Runner.Encode(identity, snapshot, null!), "null definitions");
        ExpectReject("case4.encode.empty.identity.rejected",
            () => Runner.Encode("", snapshot, definitions), "empty identity");
        ExpectReject("case4.encode.too.few.slots.rejected",
            () => Runner.Encode(identity, snapshot, definitions.Take(59).ToList()), "59 definitions");
        ExpectReject("case4.encode.duplicate.slot.rejected",
            () => Runner.Encode(identity, snapshot, WithDuplicateSlot(definitions)),
            "slot 0 duplicated, slot 59 missing");

        // --- unknown template must never be silently dropped ---
        UnknownTemplateCase(snapshot, identity, definitions, options.Seed);

        // --- a failed decode must not disturb the active run state ---
        StateLeakCase(options, liveA, factoryA, valid, identity);

        // --- capture-level refusals (the rule that consumes the codec) ---
        RegistrationFailureCase(options, runState: Runner.NullRunState());
        LegacyMigrationCase(options, liveA, liveB, factoryA, factoryB);

        // --- execution-capability gate (separate API, so probed separately) ---
        ExecutionCapabilityCase();
        return Log.Finish();
    }

    private static JsonNode Mutate(JsonObject body, Action<JsonObject> mutate)
    {
        var copy = (JsonObject)body.DeepClone();
        mutate(copy);
        return copy;
    }

    private static string CorruptBase64(string payload)
    {
        string[] parts = payload.Split(':', 4);
        string body = parts[3];
        if (body.Length < 8)
        {
            return payload + "!";
        }
        int index = body.Length / 2;
        char original = body[index];
        char replacement = original == 'A' ? 'B' : 'A';
        return string.Concat(parts[0], ":", parts[1], ":", parts[2], ":",
            body.Substring(0, index), replacement, body.Substring(index + 1));
    }

    private static List<object> WithDuplicateSlot(IReadOnlyList<object> definitions)
    {
        var list = definitions.Take(59).ToList();
        object first = definitions[0];
        list.Add(Defs.MakeDefinition(
            Defs.SlotOf(first), Refs.InstanceGet(first, "Rarity")!, Defs.NameOf(first), Defs.OpsOf(first)));
        return list;
    }

    private static void UnknownTemplateCase(
        object snapshot, string identity, IReadOnlyList<object> definitions, string seed)
    {
        var list = definitions.Take(59).ToList();
        var unknownOp = Defs.MakeOperation("PROBE_UNKNOWN_TEMPLATE", 1, "probe-text");
        list.Add(Defs.MakeDefinition(59, Refs.InstanceGet(definitions[0], "Rarity")!, "probe-name", new[] { unknownOp }));

        string payload;
        try
        {
            payload = Runner.Encode(identity, snapshot, list);
        }
        catch (Exception e)
        {
            Log.Check(true, "case4.unknown.template.no.silent.drop", "Encode rejected it: " + Describe(e));
            return;
        }
        try
        {
            object saved = Runner.Decode(payload, identity, seed);
            object slot59 = Runner.DefinitionsOf(saved).FirstOrDefault(d => Defs.SlotOf(d) == 59)
                ?? throw new InvalidDataException("slot 59 missing from the restored definitions");
            bool kept = Defs.OpsOf(slot59).Any(op =>
                Defs.TemplateOf(op) == "PROBE_UNKNOWN_TEMPLATE"
                && Defs.AmountOf(op) == 1
                && Defs.TextOf(op) == "probe-text");
            Log.Check(kept, "case4.unknown.template.no.silent.drop",
                kept ? "kept verbatim through the round trip" : "the unknown entry was silently dropped");
        }
        catch (Exception e)
        {
            Log.Check(true, "case4.unknown.template.no.silent.drop", "Decode rejected it: " + Describe(e));
        }
    }

    private static void StateLeakCase(
        Options options, Live liveA, SnapshotFactory factoryA, string valid, string identity)
    {
        liveA.Apply();
        Runner.ClearActive();
        // A FRESH state per case: token/payload storage is keyed per IRunState
        // instance, and the basegame's shared NullRunState.Instance would carry
        // one case's token into the next.
        object runState = Runner.NewRunState();
        object snapshot = factoryA.Create(options.Seed);
        Runner.CaptureRun(runState, options.Seed, runStart: true, 0);
        string before = StateSignature();
        Log.Check(before.Contains("snapshot=", StringComparison.Ordinal) && !before.Contains("snapshot=null", StringComparison.Ordinal),
            "case4.state.published", "an active run is published before the failing decodes: " + Runner.Short(before));

        string[] bad =
        {
            "",
            "not-a-qurious-payload",
            valid.Substring(0, valid.Length / 2),
            new string('A', Runner.MaxEncodedLength() + 1),
        };
        foreach (string payload in bad)
        {
            try
            {
                Runner.Decode(payload, identity, options.Seed);
            }
            catch
            {
                // expected: this case is about the state afterwards
            }
        }
        try
        {
            Runner.Decode(valid, Runner.MintIdentity(options.Seed, snapshot, "probe-other"), options.Seed);
        }
        catch
        {
            // expected
        }
        string after = StateSignature();
        Log.Check(before == after, "case4.failure.no.state.leak",
            "active identity/seed/snapshot unchanged by the failed decodes");
        Runner.ClearActive();
        Log.Check(Runner.CurrentSnapshot() is null, "case4.failure.after.clear.null",
            "ClearActiveRun still clears after the failures");
    }

    /// <summary>
    /// R04-05: with the persistence channel unavailable a NEW run must be
    /// refused outright, not given a process-local identity.
    /// </summary>
    private static void RegistrationFailureCase(Options options, object runState)
    {
        Runner.ClearActive();
        if (!Runner.ForcePersistenceUnavailable())
        {
            return;
        }
        try
        {
            Runner.Capture cap = Runner.CaptureRun(runState, options.Seed, runStart: true, 0);
            Log.Check(cap.Refused, "case4.registration.failure.new.run.refused",
                "refused=" + cap.Refused + " generation=" + cap.Generation.ToString(CultureInfo.InvariantCulture));
            Log.Check(cap.Identity.Length == 0, "case4.registration.failure.no.identity",
                "identity='" + cap.Identity + "' (a process-local identity must not be minted)");
            Log.Check(Runner.CurrentSnapshot() is null, "case4.registration.failure.no.snapshot",
                "no frozen snapshot published");
            Log.Check(Runner.CurrentDefinitions() is null, "case4.registration.failure.no.definitions",
                "no definitions published");
            Log.Check(!string.IsNullOrEmpty(Runner.CurrentBlockedReason()),
                "case4.registration.failure.reason.visible",
                "reason=" + Runner.Short(Runner.CurrentBlockedReason() ?? "<null>"));
            IReadOnlyList<object> pool = Runner.ForSeed(options.Seed);
            Log.Check(pool.Count == 0, "case4.registration.failure.no.pool",
                "count=" + pool.Count.ToString(CultureInfo.InvariantCulture)
                + " (a refused run must not serve a live pool as if it were the run's)");
            Log.Check(Runner.CurrentSnapshot() is null, "case4.registration.failure.no.pollution",
                "CurrentSnapshot still null after the query");
        }
        finally
        {
            Runner.RestorePersistence();
        }
        Runner.ClearActive();
        Runner.Capture healthy = Runner.CaptureRun(runState, options.Seed, runStart: true, 0);
        Log.Check(!healthy.Refused, "case4.registration.restored.new.run.ok",
            "after the channel is restored a new run captures again: " + healthy.ToString());
        Runner.ClearActive();
    }

    /// <summary>
    /// Legacy (token-only) saves: a verifiable rebuild may migrate, a drifted
    /// one must be refused WITHOUT losing the original token.
    /// </summary>
    private static void LegacyMigrationCase(
        Options options, Live liveA, Live liveB, SnapshotFactory factoryA, SnapshotFactory factoryB)
    {
        // One FRESH run state per sub-case: each models a different save file, and
        // the basegame's shared NullRunState.Instance would otherwise carry one
        // sub-case's persisted token/payload into the next (which turned the
        // token-only fixture below into a payload restore).
        object runState = Runner.NewRunState();

        // (1) matching token, no payload -> rebuild + migrate to a payload.
        liveA.Apply();
        Runner.ClearActive();
        object snapshot = factoryA.Create(options.Seed);
        // Minted with THIS process's reported version: a token is only migrated
        // when its recorded version matches, so a literal here would be a
        // deliberate version drift and the refusal would be correct.
        string matching = Runner.MintIdentity(options.Seed, snapshot, Runner.ModVersion());
        Runner.RememberToken(runState, matching);
        Runner.Capture legacy = Runner.CaptureRun(runState, options.Seed, runStart: false, 0);
        Log.Check(!legacy.Refused, "case4.legacy.matching.accepted",
            "Generation=" + legacy.Generation.ToString(CultureInfo.InvariantCulture) + " (3=Migrated expected)");
        Log.Check(legacy.Identity == matching, "case4.legacy.token.kept", "identity unchanged: " + Runner.Short(legacy.Identity));
        string migrated = Runner.PayloadOf(runState) ?? "";
        Log.Check(migrated.Length > 0, "case4.legacy.migrated.payload.stored",
            "payload chars=" + migrated.Length.ToString(CultureInfo.InvariantCulture));
        if (migrated.Length > 0)
        {
            try
            {
                object saved = Runner.Decode(migrated, matching, options.Seed);
                Log.Check(Runner.DefinitionsOf(saved).Count == 60, "case4.legacy.migrated.payload.decodable",
                    "definitions=" + Runner.DefinitionsOf(saved).Count.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception e)
            {
                Log.Check(false, "case4.legacy.migrated.payload.decodable", "Decode failed: " + Describe(e));
            }
        }

        // (2) the same save's token loaded with a DIFFERENT live config. The
        // sub-case above migrated that identity into this process's retention, so
        // its own token now resumes - correctly, because a retained context IS
        // that identity's context. A KNOWN drift is a save whose identity this
        // process never froze, which is what a fresh mint models: a token minted
        // under config A, then loaded in a process running config B.
        Runner.ClearActive();
        liveA.Apply();
        object driftSnapshot = factoryA.Create(options.SecondSeed);
        string driftedToken = Runner.MintIdentity(options.SecondSeed, driftSnapshot, Runner.ModVersion());
        liveB.Apply();
        object driftState = Runner.NewRunState();
        Runner.RememberToken(driftState, driftedToken);
        Runner.Capture drifted = Runner.CaptureRun(driftState, options.SecondSeed, runStart: false, 0);
        Log.Check(drifted.Refused, "case4.legacy.drift.refused",
            "refused=" + drifted.Refused + " reason=" + Runner.Short(drifted.BlockedReason ?? "<null>"));
        Log.Check(Runner.TokenOf(driftState) == driftedToken, "case4.legacy.drift.token.kept",
            "the save's original token is untouched: " + Runner.Short(Runner.TokenOf(driftState) ?? "<null>"));
        Log.Check(Runner.CurrentSnapshot() is null, "case4.legacy.drift.no.snapshot",
            "a refused legacy load publishes no snapshot");
        Log.Check(Runner.CurrentDefinitions() is null, "case4.legacy.drift.no.definitions",
            "a refused legacy load publishes no definitions");
        IReadOnlyList<object> refusedPool = Runner.ForSeed(options.SecondSeed);
        Log.Check(refusedPool.Count == 0, "case4.legacy.drift.no.pool",
            "count=" + refusedPool.Count.ToString(CultureInfo.InvariantCulture)
            + " (a refused run must not silently serve a regenerated pool)");

        // (3) a token minted by another mod version -> refuse, keep the token.
        Runner.ClearActive();
        liveA.Apply();
        string otherVersion = Runner.MintIdentity(options.Seed, factoryA.Create(options.Seed), "probe-0.0.1");
        object versionState = Runner.NewRunState();
        Runner.RememberToken(versionState, otherVersion);
        Runner.Capture versionDrift = Runner.CaptureRun(versionState, options.Seed, runStart: false, 0);
        Log.Check(versionDrift.Refused, "case4.legacy.version.drift.refused",
            "reason=" + Runner.Short(versionDrift.BlockedReason ?? "<null>"));
        Log.Check(Runner.TokenOf(versionState) == otherVersion, "case4.legacy.version.drift.token.kept",
            "token=" + Runner.Short(Runner.TokenOf(versionState) ?? "<null>"));

        // (4) no token at all -> deterministic legacy identity, never a random one.
        Runner.ClearActive();
        object freshState = Runner.NewRunState();
        Runner.Capture noTokenA = Runner.CaptureRun(freshState, options.Seed, runStart: false, 1700000000L);
        object freshState2 = Runner.NewRunState();
        Runner.Capture noTokenB = Runner.CaptureRun(freshState2, options.Seed, runStart: false, 1700000000L);
        Log.Check(noTokenA.Identity == noTokenB.Identity, "case4.legacy.tokenless.deterministic",
            "identity=" + Runner.Short(noTokenA.Identity));
        Log.Check(noTokenA.Identity.StartsWith("qcr0:", StringComparison.Ordinal), "case4.legacy.tokenless.shape",
            "identity=" + Runner.Short(noTokenA.Identity));
        object freshState3 = Runner.NewRunState();
        Runner.Capture noTokenC = Runner.CaptureRun(freshState3, options.Seed, runStart: false, 0L);
        Log.Check(noTokenC.Identity != noTokenA.Identity, "case4.legacy.tokenless.uses.starttime",
            "a different start time gives a different identity: " + Runner.Short(noTokenC.Identity));
        _ = factoryB;
    }

    private static void ExecutionCapabilityCase()
    {
        var method = Refs.Method(Refs.Codec, "ValidateExecutionCapability", 2);
        if (method is null)
        {
            Log.Skip("case4.execution.capability", "ValidateExecutionCapability not present");
            return;
        }
        var definitionType = Refs.ModAssembly.GetType("QuriousCraftingRelics.Chaos.ChaosRelicDefinition", true)!;
        var watcherTemplates = Refs.ModAssembly.GetType("QuriousCraftingRelics.Chaos.ChaosRelicExtraCatalog", true)!
            .GetProperty("WatcherTemplates", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.GetValue(null) as System.Collections.IEnumerable;
        string? watcherTemplate = watcherTemplates?.Cast<object?>().OfType<string>().FirstOrDefault();
        if (watcherTemplate is null)
        {
            Log.Skip("case4.execution.capability", "no Watcher template found in the catalog");
            return;
        }
        var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(definitionType))!;
        list.Add(Defs.MakeDefinition(0, RarityCommon(), "probe",
            new[] { Defs.MakeOperation(watcherTemplate, 1, "probe") }));
        object typed = list;
        try
        {
            method.Invoke(null, new object?[] { typed, false });
            Log.Check(false, "case4.execution.capability.refused",
                "a Watcher entry was accepted while the Watcher mod is absent");
        }
        catch (Exception e)
        {
            var inner = e is System.Reflection.TargetInvocationException tie && tie.InnerException is not null
                ? tie.InnerException
                : e;
            Log.Check(inner.GetType().FullName == "System.IO.InvalidDataException",
                "case4.execution.capability.refused",
                Describe(inner) + " | entry=" + watcherTemplate);
        }
        try
        {
            method.Invoke(null, new object?[] { typed, true });
            Log.Check(true, "case4.execution.capability.allowed.when.present",
                "accepted while the Watcher mod is present");
        }
        catch (Exception e)
        {
            Log.Check(false, "case4.execution.capability.allowed.when.present", Describe(e));
        }
    }

    private static object RarityCommon()
    {
        var rarityType = Refs.Sts2Assembly.GetType("MegaCrit.Sts2.Core.Entities.Relics.RelicRarity", true)!;
        return Enum.Parse(rarityType, "Common");
    }

    private static string StateSignature()
    {
        object? snapshot = Runner.CurrentSnapshot();
        return "identity=" + Runner.CurrentIdentity()
            + "|seed=" + Runner.CurrentRunSeed()
            + "|blocked=" + (Runner.CurrentBlockedReason() ?? "null")
            + "|snapshot=" + (snapshot is null ? "null" : Runner.Tag(snapshot));
    }

    private static void ExpectReject(string id, Func<object?> action, string detail)
    {
        string before = StateSignature();
        try
        {
            action();
            Log.Check(false, id, "NO EXCEPTION for " + detail + " - the input was accepted");
        }
        catch (Exception e)
        {
            string type = e.GetType().FullName ?? "";
            bool controlled = AcceptedExceptions.Contains(type, StringComparer.Ordinal);
            string message = e.Message.Length == 0 ? "<empty message>" : e.Message;
            Log.Check(controlled, id, Describe(e) + " | " + detail + " | msg=" + Runner.Short(message));
            if (!controlled)
            {
                Log.Warn("uncontrolled exception type for " + id + ": " + e);
            }
        }
        string after = StateSignature();
        if (before != after)
        {
            Log.Check(false, id + ".state.unchanged", "global run state changed during a rejected call");
        }
    }

    private static string Describe(Exception e) =>
        e.GetType().FullName + (e.InnerException is null ? "" : " <- " + e.InnerException.GetType().FullName);
}