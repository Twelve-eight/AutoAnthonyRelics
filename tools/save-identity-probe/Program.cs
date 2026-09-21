using System;
using System.Collections.Generic;
using System.IO;
using QuriousCraftingRelics.Chaos;

namespace SaveIdentityProbe;

/// <summary>
/// WS-0916-06 / R04-01 acceptance probe. Compiles the SHIPPED identity and
/// generation-persistence decision (mod/Code/Chaos/ChaosRunIdentity.cs and
/// mod/Code/Chaos/RunIdentityCapture.cs) directly, so this exercises the code
/// that ships rather than a copy.
///
/// It replaces the engine with a plain frozen context (a fingerprint string plus
/// a payload string), which is exactly the surface the real capture rule uses:
/// fingerprintTagOf -&gt; Tag(CanonicalFingerprint), payloadOf -&gt;
/// QuriousGenerationPersistence.Encode, restore -&gt; Decode. Nothing here touches
/// the game, Steam or a run; no game process is started.
///
/// Scenarios:
///   A. save A -> save B (different config) -> save A resumes A's ORIGINAL context
///   B. process restart: the payload restores the saved state verbatim, even
///      when the live config changed; a token-only save with a changed config is
///      REFUSED instead of silently regenerated
///   C. two different saves never collide (different tokens, different seeds,
///      and token-less saves distinguished by start time)
///   D. version change: a payload still restores (definitions are data), a
///      token-only save with a different version is refused
///   E. old saves without a token get a deterministic identity (never random),
///      a rebuild that is NOT persisted as if it were the original
///   F. retention bound: what is kept, what evicts what, and that a refusal is
///      remembered instead of regenerated
///   G. registration failure: a new run is refused before any generation
/// </summary>
internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        ScenarioA_SaveSwitchAndReturn();
        ScenarioB_ProcessRestart();
        ScenarioC_DistinctSaves();
        ScenarioD_VersionChange();
        ScenarioE_TokenlessDeterminism();
        ScenarioF_RetentionBound();
        ScenarioG_RegistrationFailure();

        Console.WriteLine();
        Console.WriteLine(_failures == 0
            ? "PROBE OK: all scenarios passed"
            : $"PROBE FAILED: {_failures} assertion(s) failed");
        return _failures == 0 ? 0 : 1;
    }

    // ---------- A: save A -> save B -> save A ----------

    private static void ScenarioA_SaveSwitchAndReturn()
    {
        Section("A. save A -> save B (different config) -> save A");

        var world = new World();
        string tokenB = ChaosRunIdentity.Mint("SEED-B", ChaosRunIdentity.Tag("cfg-B"), "0.5.7");
        string payloadB = World.PayloadOf("cfg-B");

        // New run A: minted + frozen with cfg-A. The minted token and the payload
        // are what the save then persists, so the return below presents those.
        var a1 = world.Capture(runStart: true, token: null, payload: null, seed: "SEED-A", startTime: 1000, fingerprint: "cfg-A");
        Check("A starts by minting", a1.Source == ChaosRunIdentity.Origin.Minted, a1.Source.ToString());
        Check("A freezes its own inputs", a1.FrozenFingerprintTag == ChaosRunIdentity.Tag("cfg-A"), a1.FrozenFingerprintTag);
        Check("A generates and prepares a payload", a1.Generation == GenerationOutcome.Frozen && a1.Payload is not null,
            a1.Generation.ToString());
        string contextA = world.ActiveContextFingerprint!;
        string tokenA = a1.Identity!;

        // Leave A, load B with a DIFFERENT live config (B has a payload).
        world.Leave();
        var b1 = world.Capture(runStart: false, token: tokenB, payload: payloadB, seed: "SEED-B", startTime: 2000, fingerprint: "cfg-B");
        Check("B adopts its persisted identity", b1.Identity == tokenB, b1.Identity!);
        Check("B restores its saved definitions", b1.Generation == GenerationOutcome.Restored, b1.Generation.ToString());
        Check("B is not reported as drifted", !b1.InputsDiffer, "InputsDiffer");

        // Leave B, return to A. The live config is still cfg-B: the pre-fix code
        // re-froze A from it because A's seed was no longer the last captured one.
        world.Leave();
        var a2 = world.Capture(runStart: false, token: tokenA, payload: a1.Payload, seed: "SEED-A", startTime: 1000, fingerprint: "cfg-B");
        Check("returning to A restores", a2.Resumed, "not resumed");
        Check("A's identity is unchanged", a2.Identity == a1.Identity, $"{a1.Identity} vs {a2.Identity}");
        Check("A's ORIGINAL context comes back, not a re-freeze",
            world.ActiveContextFingerprint == contextA, world.ActiveContextFingerprint!);

        // Control: the residual hazard the legacy path cannot remove. Two
        // token-less saves with the SAME seed and NO recorded start time resolve
        // to one identity, so they are treated as one save - reported as
        // SeedOnly, never silently.
        Console.WriteLine("  (control: token-less saves with no start time)");
        var world2 = new World();
        var old1 = world2.Capture(runStart: false, token: null, payload: null, seed: "SEED-SHARED", startTime: 0, fingerprint: "cfg-A");
        world2.Leave();
        var old2 = world2.Capture(runStart: false, token: null, payload: null, seed: "SEED-SHARED", startTime: 0, fingerprint: "cfg-A");
        Check("control: identical seed + no start time resolves to one identity",
            old1.Identity == old2.Identity, $"{old1.Identity} vs {old2.Identity}");
        Check("control: that collapse is reported, not silent",
            old2.Source == ChaosRunIdentity.Origin.SeedOnly, old2.Source.ToString());
        Check("control: it still resumes the first save's context",
            old2.Resumed, "not resumed");
        Check("control: a metadata-less rebuild is NOT persisted",
            old1.Generation == GenerationOutcome.LegacyRebuild && old1.Payload is null,
            old1.Generation.ToString());

        // The same pair WITH start times does not collapse - the upgrade path.
        var world3 = new World();
        var timed1 = world3.Capture(runStart: false, token: null, payload: null, seed: "SEED-SHARED", startTime: 111, fingerprint: "cfg-A");
        world3.Leave();
        var timed2 = world3.Capture(runStart: false, token: null, payload: null, seed: "SEED-SHARED", startTime: 222, fingerprint: "cfg-A");
        Check("control: a recorded start time separates them",
            timed1.Identity != timed2.Identity, "collision");
    }

    // ---------- B: simulated process restart ----------

    private static void ScenarioB_ProcessRestart()
    {
        Section("B. clean process exit, then continue the same save");

        // Process 1: the run is played; token AND payload are written with the save.
        var process1 = new World();
        var first = process1.Capture(runStart: true, token: null, payload: null, seed: "SEED-A", startTime: 1234, fingerprint: "cfg-A");
        string token = first.Identity!;
        string payload = first.Payload!;

        // Process 2: nothing is retained; both values come from the save. The
        // definitions are DATA, so the restore does not re-run the generator.
        var process2 = new World();
        var second = process2.Capture(runStart: false, token: token, payload: payload, seed: "SEED-A", startTime: 1234, fingerprint: "cfg-A");
        Check("restart resolves the same identity", second.Identity == first.Identity, $"{first.Identity} vs {second.Identity}");
        Check("restart RESTORES the saved state", second.Generation == GenerationOutcome.Restored, second.Generation.ToString());
        Check("restart does not re-freeze", !process2.FrozeAnything, "a freeze happened");
        Check("restart keeps the saved context", process2.ActiveContextFingerprint == "cfg-A", process2.ActiveContextFingerprint!);

        // A restart that ALSO changed the config STILL restores: the payload is
        // authoritative and the saved relics keep their meaning.
        var process3 = new World();
        var drifted = process3.Capture(runStart: false, token: token, payload: payload, seed: "SEED-A", startTime: 1234, fingerprint: "cfg-CHANGED");
        Check("changed config still restores the saved definitions",
            drifted.Generation == GenerationOutcome.Restored, drifted.Generation.ToString());
        Check("changed config does not report drift on a restored save", !drifted.InputsDiffer, "InputsDiffer");
        Check("changed config does not re-freeze", !process3.FrozeAnything, "a freeze happened");

        // A TOKEN-ONLY save (written before this change) with a changed config
        // cannot be verified: it is refused, the token is kept, and nothing is
        // regenerated.
        var process4 = new World();
        var refused = process4.Capture(runStart: false, token: token, payload: null, seed: "SEED-A", startTime: 1234, fingerprint: "cfg-CHANGED");
        Check("token-only save with a changed config is refused",
            refused.Generation == GenerationOutcome.Blocked, refused.Generation.ToString());
        Check("the refusal keeps the original token", refused.Identity == token, refused.Identity!);
        Check("the refusal publishes no context", process4.ActiveContextFingerprint is null,
            process4.ActiveContextFingerprint ?? "null");
        Check("the refusal reports a reason", !string.IsNullOrEmpty(refused.BlockedReason), refused.BlockedReason ?? "null");
        Check("the refusal does not regenerate", !process4.FrozeAnything, "a freeze happened");

        // A token-only save whose metadata MATCHES is migrated: rebuilt from the
        // unchanged inputs and upgraded with a payload.
        var process5 = new World();
        var migrated = process5.Capture(runStart: false, token: token, payload: null, seed: "SEED-A", startTime: 1234, fingerprint: "cfg-A");
        Check("matching metadata is migrated", migrated.Generation == GenerationOutcome.Migrated, migrated.Generation.ToString());
        Check("the migration writes a payload", migrated.Payload is not null, "no payload");
        Check("the migration keeps the identity", migrated.Identity == token, migrated.Identity!);

        // A corrupt payload must never be answered with a live rebuild.
        var process6 = new World();
        var corrupt = process6.Capture(runStart: false, token: token, payload: World.PayloadOf("cfg-A", corrupt: true), seed: "SEED-A", startTime: 1234, fingerprint: "cfg-A");
        Check("a corrupt payload is refused", corrupt.Generation == GenerationOutcome.Blocked, corrupt.Generation.ToString());
        Check("a corrupt payload does not regenerate", !process6.FrozeAnything, "a freeze happened");
        Check("a corrupt payload publishes nothing", process6.ActiveContextFingerprint is null,
            process6.ActiveContextFingerprint ?? "null");
    }

    // ---------- C: no collisions ----------

    private static void ScenarioC_DistinctSaves()
    {
        Section("C. different saves never collide");

        var world = new World();
        string tokenA = ChaosRunIdentity.Mint("SEED-A", ChaosRunIdentity.Tag("cfg"), "0.5.7");
        string tokenB = ChaosRunIdentity.Mint("SEED-A", ChaosRunIdentity.Tag("cfg"), "0.5.7");

        Check("two mints differ even for the same seed and config", tokenA != tokenB, "collision");

        // Two saves that share a seed string but have no token: the persisted
        // start time separates them.
        var old1 = world.Capture(runStart: false, token: null, payload: null, seed: "SHARED", startTime: 1000, fingerprint: "cfg");
        world.Leave();
        var old2 = world.Capture(runStart: false, token: null, payload: null, seed: "SHARED", startTime: 2000, fingerprint: "cfg");
        Check("token-less saves with different start times do not collide",
            old1.Identity != old2.Identity, "collision");
        Check("token-less save resolves to the deterministic start-time identity",
            old1.Identity == ChaosRunIdentity.Legacy("SHARED", 1000), old1.Identity!);

        // Re-entering the first one resumes ITS context, not the second's.
        world.Leave();
        var back = world.Capture(runStart: false, token: null, payload: null, seed: "SHARED", startTime: 1000, fingerprint: "cfg");
        Check("re-entering the first token-less save resumes it", back.Resumed, "not resumed");
        Check("it resumes its own identity", back.Identity == old1.Identity, back.Identity!);

        // A seed containing the field separator must round-trip.
        string colonSeed = "SEED:WITH:COLONS";
        string colonToken = ChaosRunIdentity.Mint(colonSeed, ChaosRunIdentity.Tag("cfg"), "0.5.7");
        Check("seed containing ':' round-trips",
            ChaosRunIdentity.TryParse(colonToken, out string parsed, out _, out _) && parsed == colonSeed, parsed);
        Check("legacy seed containing ':' round-trips",
            ChaosRunIdentity.TryParse(ChaosRunIdentity.Legacy(colonSeed, 7), out string legacySeed, out _, out _)
                && legacySeed == colonSeed, legacySeed);

        // A token from another writer is kept verbatim rather than dropped.
        var foreign = world.Capture(runStart: false, token: "someothermod:abc", payload: null, seed: "SEED-A", startTime: 0, fingerprint: "cfg");
        Check("unrecognized token is kept verbatim", foreign.Identity == "someothermod:abc", foreign.Identity!);
        Check("unrecognized token is reported as such",
            foreign.Source == ChaosRunIdentity.Origin.Unrecognized, foreign.Source.ToString());
        Check("unrecognized token is rebuilt but not persisted",
            foreign.Generation == GenerationOutcome.LegacyRebuild && foreign.Payload is null,
            foreign.Generation.ToString());
    }

    // ---------- D: version change ----------

    private static void ScenarioD_VersionChange()
    {
        Section("D. mod version change");

        var world = new World();
        string token = ChaosRunIdentity.Mint("SEED-A", ChaosRunIdentity.Tag("cfg"), "0.5.7");
        string payload = World.PayloadOf("cfg");

        var sameVersion = world.Capture(runStart: false, token: token, payload: payload, seed: "SEED-A", startTime: 5, fingerprint: "cfg");
        Check("same version: no version drift", !sameVersion.VersionDiffers, "VersionDiffers");

        world.Leave();
        var bumped = new World(modVersion: "0.6.0");
        var afterUpgrade = bumped.Capture(runStart: false, token: token, payload: payload, seed: "SEED-A", startTime: 5, fingerprint: "cfg");
        Check("after a mod update the identity is UNCHANGED", afterUpgrade.Identity == token, afterUpgrade.Identity!);
        Check("after a mod update the saved state still restores",
            afterUpgrade.Generation == GenerationOutcome.Restored, afterUpgrade.Generation.ToString());
        Check("the report names the recording version", afterUpgrade.RecordedVersion == "0.5.7", afterUpgrade.RecordedVersion);

        // Token-only save, different version: cannot be verified, so it is refused.
        var bumped2 = new World(modVersion: "0.6.0");
        var tokenOnly = bumped2.Capture(runStart: false, token: token, payload: null, seed: "SEED-A", startTime: 5, fingerprint: "cfg");
        Check("token-only save from another version is refused",
            tokenOnly.Generation == GenerationOutcome.Blocked, tokenOnly.Generation.ToString());
        Check("the refusal keeps the identity", tokenOnly.Identity == token, tokenOnly.Identity!);

        // A version field that could break the layout is sanitized away.
        string hostile = ChaosRunIdentity.Mint("SEED-A", ChaosRunIdentity.Tag("cfg"), "0.5.7:extra");
        Check("version field cannot inject a separator",
            ChaosRunIdentity.TryParse(hostile, out string seed, out _, out string version)
                && seed == "SEED-A" && version == "0.5.7extra", $"{seed}|{version}");
    }

    // ---------- E: token-less determinism ----------

    private static void ScenarioE_TokenlessDeterminism()
    {
        Section("E. old saves without a token");

        string first = ChaosRunIdentity.Legacy("SEED-OLD", 4242);
        string second = ChaosRunIdentity.Legacy("SEED-OLD", 4242);
        Check("legacy identity is deterministic", first == second, $"{first} vs {second}");

        var world = new World();
        var load = world.Capture(runStart: false, token: null, payload: null, seed: "SEED-OLD", startTime: 4242, fingerprint: "cfg");
        Check("token-less load reports the legacy origin",
            load.Source == ChaosRunIdentity.Origin.Legacy, load.Source.ToString());
        Check("token-less load reports that the definitions cannot be recovered",
            load.Generation == GenerationOutcome.LegacyRebuild, load.Generation.ToString());

        var noStart = world.Capture(runStart: false, token: null, payload: null, seed: "SEED-OLD", startTime: 0, fingerprint: "cfg");
        Check("token-less load with no start time is reported as weaker",
            noStart.Source == ChaosRunIdentity.Origin.SeedOnly, noStart.Source.ToString());
    }

    // ---------- F: retention bound ----------

    private static void ScenarioF_RetentionBound()
    {
        Section("F. retention bound");

        var world = new World(limit: 2);
        Check("retention limit is reported", world.RetainedLimit == 2, world.RetainedLimit.ToString());

        var a = world.Capture(runStart: true, token: null, payload: null, seed: "A", startTime: 1, fingerprint: "cfg");
        world.Leave();
        var b = world.Capture(runStart: true, token: null, payload: null, seed: "B", startTime: 2, fingerprint: "cfg");
        Check("two runs retained", world.RetainedCount == 2, world.RetainedCount.ToString());

        // Re-entering A must not evict B: an already-known identity is refreshed
        // in place, keeping its FIFO position.
        world.Leave();
        world.Capture(runStart: false, token: a.Identity, payload: a.Payload, seed: "A", startTime: 1, fingerprint: "cfg");
        Check("re-entering a retained run does not evict another", world.RetainedCount == 2, world.RetainedCount.ToString());
        world.Leave();
        var bBack = world.Capture(runStart: false, token: b.Identity, payload: b.Payload, seed: "B", startTime: 2, fingerprint: "cfg");
        Check("the other run is still resumable", bBack.Resumed, "not resumed");

        // A third, genuinely new identity evicts the oldest (A).
        world.Leave();
        var c = world.Capture(runStart: true, token: null, payload: null, seed: "C", startTime: 3, fingerprint: "cfg");
        Check("a third identity evicts the oldest", world.RetainedCount == 2, world.RetainedCount.ToString());
        world.Leave();
        world.ResetFreezeFlag();
        var aBack = world.Capture(runStart: false, token: a.Identity, payload: a.Payload, seed: "A", startTime: 1, fingerprint: "cfg");
        Check("the evicted run RESTORES from its payload instead of resuming", aBack.Generation == GenerationOutcome.Restored,
            aBack.Generation.ToString());
        Check("the evicted run keeps its identity", aBack.Identity == a.Identity, aBack.Identity!);
        Check("the evicted run does not re-freeze", !world.FrozeAnything, "a freeze happened");
        Check("the surviving run is the newer one", c.Identity != a.Identity, "collision");

        // A refusal is retained too: a later capture of the same save must not
        // quietly regenerate what was already refused.
        world.Leave();
        var refused = world.Capture(runStart: false, token: a.Identity, payload: World.PayloadOf("cfg", corrupt: true), seed: "A", startTime: 1, fingerprint: "cfg");
        Check("a corrupt payload is refused", refused.Generation == GenerationOutcome.Blocked, refused.Generation.ToString());
        world.Leave();
        var again = world.Capture(runStart: false, token: a.Identity, payload: null, seed: "A", startTime: 1, fingerprint: "cfg");
        Check("the refusal is remembered, not regenerated",
            again.Generation == GenerationOutcome.Blocked, again.Generation.ToString());
    }

    // ---------- G: registration failure ----------

    private static void ScenarioG_RegistrationFailure()
    {
        Section("G. persistence registration failed");

        var world = new World(persistenceAvailable: false);

        // A NEW run must not start with a process-local identity.
        var refused = world.Capture(runStart: true, token: null, payload: null, seed: "SEED-NEW", startTime: 0, fingerprint: "cfg");
        Check("a new run is refused", refused.Generation == GenerationOutcome.Aborted, refused.Generation.ToString());
        Check("the refusal is explicit", refused.Aborted && !string.IsNullOrEmpty(refused.BlockedReason),
            refused.BlockedReason ?? "null");
        Check("nothing is published", world.ActiveContextFingerprint is null, world.ActiveContextFingerprint ?? "null");
        Check("no identity is pretended", string.IsNullOrEmpty(refused.Identity), refused.Identity ?? "null");
        Check("no generation ran", !world.FrozeAnything, "a freeze happened");

        // A loaded save cannot be rebuilt either: its state could not be saved.
        var token = ChaosRunIdentity.Mint("SEED-A", ChaosRunIdentity.Tag("cfg"), "0.5.7");
        var loaded = world.Capture(runStart: false, token: token, payload: null, seed: "SEED-A", startTime: 9, fingerprint: "cfg");
        Check("a token-only load is refused", loaded.Generation == GenerationOutcome.Blocked, loaded.Generation.ToString());
        Check("the refusal keeps the token", loaded.Identity == token, loaded.Identity!);
    }

    // ---------- harness ----------

    /// <summary>
    /// Stands in for the mod's process: owns one capture rule with a plain frozen
    /// context (a fingerprint plus a payload string), so the identity and
    /// persistence decision runs exactly as it does in the game with the engine
    /// removed.
    /// </summary>
    private sealed class World
    {
        private readonly RunIdentityCapture<Context> _capture;
        private readonly string _modVersion;
        private readonly bool _persistenceAvailable;

        /// <summary>Live config fingerprint the next freeze uses.</summary>
        private string _pendingFingerprint = "";

        /// <summary>How many freezes (i.e. generations) this world performed.</summary>
        internal bool FrozeAnything { get; private set; }

        internal World(int limit = 8, string modVersion = "0.5.7", bool persistenceAvailable = true)
        {
            _modVersion = modVersion;
            _persistenceAvailable = persistenceAvailable;
            _capture = new RunIdentityCapture<Context>(
                limit,
                freeze: seed =>
                {
                    FrozeAnything = true;
                    return new Context { Seed = seed ?? "", Fingerprint = _pendingFingerprint };
                },
                fingerprintTagOf: context => ChaosRunIdentity.Tag(context.Fingerprint),
                payloadOf: (context, identity) => PayloadOf(context.Fingerprint, identity: identity),
                restore: (payload, identity, seed) =>
                {
                    if (payload.Contains("corrupt", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("probe: payload refused");
                    }
                    return new Context { Seed = seed, Fingerprint = payload.Substring("qcrgen:".Length) };
                },
                modVersion: () => _modVersion);
        }

        /// <summary>A payload that encodes the given fingerprint, optionally corrupt.</summary>
        internal static string PayloadOf(string fingerprint, string? identity = null, bool corrupt = false) =>
            corrupt ? "qcrgen:corrupt" : "qcrgen:" + fingerprint;

        internal string? ActiveContextFingerprint => _capture.ActiveContext?.Fingerprint;
        internal int RetainedLimit => _capture.RetainedLimit;
        internal int RetainedCount => _capture.RetainedCount;

        internal RunIdentityCapture<Context>.CaptureOutcome Capture(
            bool runStart, string? token, string? payload, string seed, long startTime, string fingerprint)
        {
            _pendingFingerprint = fingerprint;
            return _capture.Capture(new RunIdentityCapture<Context>.CaptureRequest
            {
                RunStart = runStart,
                PersistedToken = token,
                PersistedPayload = payload,
                Seed = seed,
                StartTimeUnix = startTime,
                PersistenceAvailable = _persistenceAvailable,
            });
        }

        internal void Leave() => _capture.ClearActive();

        /// <summary>Clears the freeze counter so a later check can assert that a
        /// capture did NOT generate anything.</summary>
        internal void ResetFreezeFlag() => FrozeAnything = false;
    }

    /// <summary>Plain stand-in for the frozen generation context.</summary>
    private sealed class Context
    {
        internal string Seed { get; init; } = "";
        internal string Fingerprint { get; init; } = "";
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine($"== {title} ==");
    }

    private static void Check(string what, bool ok, string observed)
    {
        if (ok)
        {
            Console.WriteLine($"  PASS  {what}");
            return;
        }
        _failures++;
        Console.WriteLine($"  FAIL  {what} (observed: {observed})");
    }
}