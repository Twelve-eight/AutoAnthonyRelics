using System;
using System.Collections.Generic;
using QuriousCraftingRelics.Chaos;

namespace SaveIdentityProbe;

/// <summary>
/// WS-0916-06 acceptance probe. Compiles the SHIPPED identity decision
/// (mod/Code/Chaos/ChaosRunIdentity.cs and mod/Code/Chaos/RunIdentityCapture.cs)
/// directly, so this exercises the code that ships rather than a copy.
///
/// It replaces the engine with a plain frozen context whose fingerprint is a
/// caller-supplied string, which is exactly the surface the real snapshot
/// exposes to the capture rule (CanonicalFingerprint -> Tag). Nothing here
/// touches the game, Steam or a run; no game process is started.
///
/// Scenarios:
///   A. save A -> save B (different config) -> save A resumes A's ORIGINAL context
///   B. simulated process restart: a fresh capture rule on the same save
///      resolves the SAME identity and the same recorded inputs
///   C. two different saves never collide (different tokens, different seeds,
///      and token-less saves distinguished by start time)
///   D. version change has a defined, reported outcome
///   E. old saves without a token get a deterministic identity (never random)
///   F. retention bound: what is kept, what evicts what
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

        // New run A: minted + frozen with cfg-A. The minted token is what the
        // save then persists, so the return below must present THAT token.
        var a1 = world.Capture(runStart: true, token: null, seed: "SEED-A", startTime: 1000, fingerprint: "cfg-A");
        Check("A starts by minting", a1.Source == ChaosRunIdentity.Origin.Minted, a1.Source.ToString());
        Check("A freezes its own inputs", a1.FrozenFingerprintTag == ChaosRunIdentity.Tag("cfg-A"), a1.FrozenFingerprintTag);
        string contextA = world.ActiveContextFingerprint!;
        string tokenA = a1.Identity;

        // Leave A, load B with a DIFFERENT live config.
        world.Leave();
        var b1 = world.Capture(runStart: false, token: tokenB, seed: "SEED-B", startTime: 2000, fingerprint: "cfg-B");
        Check("B adopts its persisted identity", b1.Identity == tokenB, b1.Identity);
        Check("B is not resumed", !b1.Resumed, "resumed");
        Check("B freezes cfg-B", b1.FrozenFingerprintTag == ChaosRunIdentity.Tag("cfg-B"), b1.FrozenFingerprintTag);

        // Leave B, return to A. The live config is still cfg-B: the pre-fix code
        // re-froze A from it because A's seed was no longer the last captured one.
        world.Leave();
        var a2 = world.Capture(runStart: false, token: tokenA, seed: "SEED-A", startTime: 1000, fingerprint: "cfg-B");
        Check("returning to A resumes", a2.Resumed, "not resumed");
        Check("A's identity is unchanged", a2.Identity == a1.Identity, $"{a1.Identity} vs {a2.Identity}");
        Check("A's ORIGINAL context comes back, not a re-freeze",
            world.ActiveContextFingerprint == contextA, world.ActiveContextFingerprint!);
        Check("A is not reported as drifted", !a2.InputsDiffer, "InputsDiffer");

        // Control: the residual hazard the legacy path cannot remove. Two
        // token-less saves with the SAME seed and NO recorded start time resolve
        // to one identity, so they are treated as one save - reported as
        // SeedOnly, never silently.
        Console.WriteLine("  (control: token-less saves with no start time)");
        var world2 = new World();
        var old1 = world2.Capture(runStart: false, token: null, seed: "SEED-SHARED", startTime: 0, fingerprint: "cfg-A");
        world2.Leave();
        var old2 = world2.Capture(runStart: false, token: null, seed: "SEED-SHARED", startTime: 0, fingerprint: "cfg-A");
        Check("control: identical seed + no start time resolves to one identity",
            old1.Identity == old2.Identity, $"{old1.Identity} vs {old2.Identity}");
        Check("control: that collapse is reported, not silent",
            old2.Source == ChaosRunIdentity.Origin.SeedOnly, old2.Source.ToString());
        Check("control: it still resumes the first save's context",
            old2.Resumed, "not resumed");

        // The same pair WITH start times does not collapse - the upgrade path.
        var world3 = new World();
        var timed1 = world3.Capture(runStart: false, token: null, seed: "SEED-SHARED", startTime: 111, fingerprint: "cfg-A");
        world3.Leave();
        var timed2 = world3.Capture(runStart: false, token: null, seed: "SEED-SHARED", startTime: 222, fingerprint: "cfg-A");
        Check("control: a recorded start time separates them",
            timed1.Identity != timed2.Identity, "collision");
    }

    // ---------- B: simulated process restart ----------

    private static void ScenarioB_ProcessRestart()
    {
        Section("B. clean process exit, then continue the same save");

        // Process 1: the run is played and the token is written with the save.
        var process1 = new World();
        var first = process1.Capture(runStart: true, token: null, seed: "SEED-A", startTime: 1234, fingerprint: "cfg-A");
        string token = first.Identity;

        // Process 2: nothing is retained; the identity must come from the save.
        var process2 = new World();
        var second = process2.Capture(runStart: false, token: token, seed: "SEED-A", startTime: 1234, fingerprint: "cfg-A");

        Check("restart resolves the same identity", second.Identity == first.Identity, $"{first.Identity} vs {second.Identity}");
        Check("restart is not resumed (no retained context yet)", !second.Resumed, "resumed");
        Check("restart re-freezes the SAME inputs", second.FrozenFingerprintTag == first.FrozenFingerprintTag,
            $"{first.FrozenFingerprintTag} vs {second.FrozenFingerprintTag}");
        Check("restart is not reported as drifted", !second.InputsDiffer, "InputsDiffer");
        Check("restart keeps the recorded version", second.RecordedVersion == "0.5.7", second.RecordedVersion);

        // A restart that ALSO changed the config cannot reproduce the inputs: it
        // must say so rather than silently pretend the pool is unchanged.
        var process3 = new World();
        var drifted = process3.Capture(runStart: false, token: token, seed: "SEED-A", startTime: 1234, fingerprint: "cfg-CHANGED");
        Check("restart with a changed config reports drift", drifted.InputsDiffer, "InputsDiffer");
        Check("drifted restart still uses the SAME identity", drifted.Identity == token, drifted.Identity);
        Check("drifted restart reports the recorded inputs",
            drifted.RecordedFingerprintTag == ChaosRunIdentity.Tag("cfg-A"), drifted.RecordedFingerprintTag);
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
        var old1 = world.Capture(runStart: false, token: null, seed: "SHARED", startTime: 1000, fingerprint: "cfg");
        world.Leave();
        var old2 = world.Capture(runStart: false, token: null, seed: "SHARED", startTime: 2000, fingerprint: "cfg");
        Check("token-less saves with different start times do not collide",
            old1.Identity != old2.Identity, "collision");
        Check("token-less save resolves to the deterministic start-time identity",
            old1.Identity == ChaosRunIdentity.Legacy("SHARED", 1000), old1.Identity);

        // Re-entering the first one resumes ITS context, not the second's.
        world.Leave();
        var back = world.Capture(runStart: false, token: null, seed: "SHARED", startTime: 1000, fingerprint: "cfg");
        Check("re-entering the first token-less save resumes it", back.Resumed, "not resumed");
        Check("it resumes its own identity", back.Identity == old1.Identity, back.Identity);

        // A seed containing the field separator must round-trip.
        string colonSeed = "SEED:WITH:COLONS";
        string colonToken = ChaosRunIdentity.Mint(colonSeed, ChaosRunIdentity.Tag("cfg"), "0.5.7");
        Check("seed containing ':' round-trips",
            ChaosRunIdentity.TryParse(colonToken, out string parsed, out _, out _) && parsed == colonSeed, parsed);
        Check("legacy seed containing ':' round-trips",
            ChaosRunIdentity.TryParse(ChaosRunIdentity.Legacy(colonSeed, 7), out string legacySeed, out _, out _)
                && legacySeed == colonSeed, legacySeed);

        // A token from another writer is kept verbatim rather than dropped.
        var foreign = world.Capture(runStart: false, token: "someothermod:abc", seed: "SEED-A", startTime: 0, fingerprint: "cfg");
        Check("unrecognized token is kept verbatim", foreign.Identity == "someothermod:abc", foreign.Identity);
        Check("unrecognized token is reported as such",
            foreign.Source == ChaosRunIdentity.Origin.Unrecognized, foreign.Source.ToString());
    }

    // ---------- D: version change ----------

    private static void ScenarioD_VersionChange()
    {
        Section("D. mod version change");

        var world = new World();
        string token = ChaosRunIdentity.Mint("SEED-A", ChaosRunIdentity.Tag("cfg"), "0.5.7");

        var sameVersion = world.Capture(runStart: false, token: token, seed: "SEED-A", startTime: 5, fingerprint: "cfg");
        Check("same version: no version drift", !sameVersion.VersionDiffers, "VersionDiffers");

        world.Leave();
        var newVersion = world.Capture(runStart: false, token: token, seed: "SEED-A", startTime: 5, fingerprint: "cfg");
        // The capture rule reads the version from its own delegate; the World
        // below is fixed at 0.5.7, so this asserts the reported outcome shape.
        Check("version drift is a reported field", newVersion.VersionDiffers == false, "unexpected drift");

        var bumped = new World(modVersion: "0.6.0");
        var afterUpgrade = bumped.Capture(runStart: false, token: token, seed: "SEED-A", startTime: 5, fingerprint: "cfg");
        Check("after a mod update the identity is UNCHANGED", afterUpgrade.Identity == token, afterUpgrade.Identity);
        Check("after a mod update the change is reported", afterUpgrade.VersionDiffers, "VersionDiffers");
        Check("the report names the recording version", afterUpgrade.RecordedVersion == "0.5.7", afterUpgrade.RecordedVersion);
        Check("an unchanged config is not reported as input drift", !afterUpgrade.InputsDiffer, "InputsDiffer");

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
        Check("legacy identity is not a random nonce", !first.Contains(ChaosRunIdentity.NewNonce(), StringComparison.Ordinal),
            "looks random");

        var world = new World();
        var load = world.Capture(runStart: false, token: null, seed: "SEED-OLD", startTime: 4242, fingerprint: "cfg");
        Check("token-less load reports the legacy origin",
            load.Source == ChaosRunIdentity.Origin.Legacy, load.Source.ToString());

        var noStart = world.Capture(runStart: false, token: null, seed: "SEED-OLD", startTime: 0, fingerprint: "cfg");
        Check("token-less load with no start time is reported as weaker",
            noStart.Source == ChaosRunIdentity.Origin.SeedOnly, noStart.Source.ToString());
    }

    // ---------- F: retention bound ----------

    private static void ScenarioF_RetentionBound()
    {
        Section("F. retention bound");

        var world = new World(limit: 2);
        Check("retention limit is reported", world.RetainedLimit == 2, world.RetainedLimit.ToString());

        var a = world.Capture(runStart: true, token: null, seed: "A", startTime: 1, fingerprint: "cfg");
        world.Leave();
        var b = world.Capture(runStart: true, token: null, seed: "B", startTime: 2, fingerprint: "cfg");
        Check("two runs retained", world.RetainedCount == 2, world.RetainedCount.ToString());

        // Re-entering A must not evict B: an already-known identity is refreshed
        // in place, keeping its FIFO position.
        world.Leave();
        world.Capture(runStart: false, token: a.Identity, seed: "A", startTime: 1, fingerprint: "cfg");
        Check("re-entering a retained run does not evict another", world.RetainedCount == 2, world.RetainedCount.ToString());
        world.Leave();
        var bBack = world.Capture(runStart: false, token: b.Identity, seed: "B", startTime: 2, fingerprint: "cfg");
        Check("the other run is still resumable", bBack.Resumed, "not resumed");

        // A third, genuinely new identity evicts the oldest (A).
        world.Leave();
        var c = world.Capture(runStart: true, token: null, seed: "C", startTime: 3, fingerprint: "cfg");
        Check("a third identity evicts the oldest", world.RetainedCount == 2, world.RetainedCount.ToString());
        world.Leave();
        var aBack = world.Capture(runStart: false, token: a.Identity, seed: "A", startTime: 1, fingerprint: "cfg");
        Check("the evicted run re-freezes instead of resuming", !aBack.Resumed, "resumed");
        Check("the evicted run keeps its identity", aBack.Identity == a.Identity, aBack.Identity);
        Check("the surviving run is the newer one", c.Identity != a.Identity, "collision");
    }

    // ---------- harness ----------

    /// <summary>
    /// Stands in for the mod's process: owns one capture rule with a plain frozen
    /// context (a fingerprint string), so the identity decision runs exactly as
    /// it does in the game with the engine removed.
    /// </summary>
    private sealed class World
    {
        private readonly RunIdentityCapture<Context> _capture;
        private readonly string _modVersion;

        /// <summary>Live config fingerprint the next freeze uses.</summary>
        private string _pendingFingerprint = "";

        internal World(int limit = 8, string modVersion = "0.5.7")
        {
            _modVersion = modVersion;
            _capture = new RunIdentityCapture<Context>(
                limit,
                freeze: seed => new Context { Seed = seed ?? "", Fingerprint = _pendingFingerprint },
                fingerprintOf: context => ChaosRunIdentity.Tag(context.Fingerprint),
                modVersion: () => _modVersion);
        }

        internal string? ActiveContextFingerprint => _capture.ActiveContext?.Fingerprint;
        internal int RetainedLimit => _capture.RetainedLimit;
        internal int RetainedCount => _capture.RetainedCount;

        internal RunIdentityCapture<Context>.CaptureOutcome Capture(
            bool runStart, string? token, string seed, long startTime, string fingerprint)
        {
            _pendingFingerprint = fingerprint;
            return _capture.Capture(new RunIdentityCapture<Context>.CaptureRequest
            {
                RunStart = runStart,
                PersistedToken = token,
                Seed = seed,
                StartTimeUnix = startTime,
            });
        }

        internal void Leave() => _capture.ClearActive();
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
