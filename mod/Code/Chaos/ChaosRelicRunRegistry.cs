using System;
using System.Collections.Generic;
using System.Text;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Runs;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// Per-run registry of generated relic definitions. Keyed by the run seed
/// PLUS a fingerprint of every config value that feeds generation: same seed
/// AND same fingerprint -> same relic pool on both MP ends (deterministic
/// regeneration, the same contract AutoAnthony uses for its pool snapshots
/// minus transport). Lazily generated on first query; cache bounded to a few
/// runs.
///
/// Why the fingerprint (F04, probe 2026-09-12): the cache used to be keyed by
/// the seed alone while the generated content depends on the budgets, the
/// negative chances, the per-template costs, the extra-pool switch and the
/// Min/Max bounds. The probe changed every budget after a first generation for
/// the same seed and got back the OLD pool (cacheUnchanged=true) while a fresh
/// generator call produced a different one. In multiplayer that means a client
/// whose config sync arrives late keeps serving a pool generated from its own
/// pre-sync config, and the two ends diverge.
///
/// The fingerprint is a stable string hash over the generation inputs, so the
/// key is (seed, inputs) rather than (seed, first-writer-wins).
///
/// RUN-EFFECTIVE FREEZE (astra-advice 2026-09-12 item 5): inside a run every
/// generation input comes from <see cref="CurrentSnapshot"/>, frozen once at
/// seed capture - NOT from live config. Editing a budget mid-run therefore
/// leaves the (seed, inputs) key and the generated pool unchanged; the
/// already-obtained relics keep their identity for the whole run. Live config
/// is only consulted outside runs (menus/previews). The MP divergence above
/// still needs config sync to deliver host values before run start; the
/// freeze only guarantees THIS process cannot change its mind mid-run.
///
/// FROZEN WARM LOOKUP (QCR-1, QCR-R4-02): the snapshot precomposes the
/// canonical cache key, the ordered active catalogs and the effective template
/// metadata once at capture. A warm ForSeed/DefinitionFor hit inside the
/// active run is therefore a plain dictionary lookup with ZERO allocation -
/// no fingerprint string building, no list rebuild/sort, no Watcher assembly
/// probe. Menu/preview/foreign-seed queries rebuild the key from live state
/// on every call (allocations accepted there; separately invalidated, and
/// they never reuse a prior run's composed context).
///
/// LIFECYCLE of the bounded cache: Cache/Order are owned by this static class
/// for the whole process; entries are keyed by (seed, fingerprint), evicted
/// FIFO beyond CacheLimit, and survive CleanUp by design - keys can only be
/// reached with a matching seed, so a kept entry cannot leak into menus
/// (DefinitionFor returns null whenever CurrentRunSeed is null).
///
/// PER-SAVE IDENTITY (WS-0916-06): which save a cached relic belongs to is
/// decided by a DURABLE identity persisted with the run save
/// (<see cref="ChaosRunIdentity"/>, <see cref="ChaosRunIdentitySave"/>), never
/// by the most recent capture in this process. Frozen contexts are retained by
/// <see cref="RunIdentityCapture{TContext}"/> under that identity, so save A
/// -&gt; save B (different config) -&gt; save A resumes A's ORIGINAL generation
/// context instead of re-freezing A from whatever the live config says on the
/// way back, and a clean process exit followed by continuing A resolves to the
/// same identity. What the identity can and cannot reproduce across processes is
/// stated on <see cref="ChaosRunIdentity"/>.
/// </summary>
public static class ChaosRelicRunRegistry
{
    private const int CacheLimit = 8;

    /// <summary>How many runs' frozen contexts stay resumable. Same bound as
    /// the definition cache; the oldest identity is evicted first.</summary>
    private const int RetainedContextLimit = CacheLimit;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, IReadOnlyList<ChaosRelicDefinition>> Cache = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();

    /// <summary>
    /// Generation inputs frozen at run-seed capture; null outside runs.
    /// Set by the seed-tracking patches alongside <see cref="CurrentRunSeed"/>;
    /// the snapshot carries the precomposed cache key, ordered active catalogs
    /// and effective template metadata (QCR-1) and is only published after
    /// those are fully built. Deliberately kept after CleanUp as continuation
    /// evidence (see <see cref="CurrentIdentity"/> and
    /// <see cref="RunIdentityCapture{TContext}"/>).
    /// </summary>
    public static QuriousGenerationSnapshot? CurrentSnapshot { get; internal set; }

    /// <summary>
    /// Registry cache key for the CURRENT run (seed + config fingerprint).
    /// Also the loc-updater dedupe key: the definitions can change without the
    /// seed changing, and a seed-only skip left tooltips stale while effects
    /// drifted (2026-09-13 report).
    /// Active-run fast path (QCR-1): the key was precomposed on the frozen
    /// snapshot at capture, so this getter allocates nothing inside a run.
    /// Every other state (no snapshot, or seed/snapshot mismatch) composes the
    /// key from the current process state exactly as before.
    /// </summary>
    internal static string CurrentCacheKey
    {
        get
        {
            var snapshot = CurrentSnapshot;
            if (snapshot is not null && CurrentRunSeed is not null
                && string.Equals(snapshot.RunSeed, CurrentRunSeed, StringComparison.Ordinal))
            {
                return snapshot.CanonicalCacheKey;
            }
            return (CurrentRunSeed ?? "") + "\0" + ConfigFingerprint();
        }
    }

    /// <summary>Generation config fingerprint for the current process state.
    /// Active or kept snapshot: the canonical fingerprint frozen at capture
    /// (identical content to the previous per-call construction, no rebuild).
    /// Otherwise the live menu/preview build, which re-reads live config on
    /// every call so preview inputs stay separately invalidated.</summary>
    private static string ConfigFingerprint()
    {
        var snapshot = CurrentSnapshot;
        if (snapshot is not null)
        {
            return snapshot.CanonicalFingerprint;
        }
        return BuildLiveFingerprint();
    }

    /// <summary>
    /// Live (no snapshot) fingerprint build - the menu/preview path. Allocates
    /// by design: it must re-read live config each time so a preference edit
    /// between two preview lookups is never served from a stale composed key.
    /// Mirrors the pre-QCR-1 construction exactly (same fields, same order,
    /// sorted template ids, live point-cost table).
    /// </summary>
    private static string BuildLiveFingerprint()
    {
        var sb = new StringBuilder(256);
        sb.Append(QuriousCraftingRelicsConfig.ChaosRelicBudgetCommon).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicBudgetUncommon).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicBudgetRare).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceCommon).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceUncommon).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceRare).Append('/')
          .Append(QuriousCraftingRelicsConfig.EnableExtraPool ? '1' : '0').Append('/')
          .Append(ChaosTemplates.WatcherModLoaded ? '1' : '0');
        // Per-template economics and bounds. Ordered by template id so the
        // fingerprint does not depend on collection iteration order.
        var templates = new List<string>(ChaosTemplates.PositiveTemplates);
        templates.AddRange(ChaosTemplates.NegativeTemplates);
        templates.Sort(StringComparer.Ordinal);
        foreach (var template in templates)
        {
            var spec = ChaosTemplates.Effective(template);
            sb.Append('|').Append(template)
              .Append(':').Append(QuriousCraftingRelicsConfig.PointCosts.CostPerPoint(template))
              .Append(':').Append(QuriousCraftingRelicsConfig.PointCosts.RefundPerPoint(template))
              .Append(':').Append(spec.Min)
              .Append(':').Append(spec.Max);
        }
        return sb.ToString();
    }

    /// <summary>Frozen per-point table inside a run, live table otherwise.</summary>
    private static ChaosPointCosts Costs() =>
        CurrentSnapshot?.FrozenCosts ?? QuriousCraftingRelicsConfig.PointCosts;

    public static IReadOnlyList<ChaosRelicDefinition> ForSeed(string seed, int multiplier)
    {
        // ACTIVE-RUN FAST PATH (QCR-1 / QCR-R4-02): the canonical key was
        // precomposed on the frozen snapshot at capture. A warm hit performs
        // no string building, no list rebuild, no sorting and no assembly
        // probing - the astra round-4 typed-delegate probe measured 7,208
        // bytes/call for the old per-hit rebuild; the acceptance contract for
        // this path is 0 bytes after warm-up.
        var snapshot = CurrentSnapshot;
        if (snapshot is not null
            && string.Equals(snapshot.RunSeed, seed, StringComparison.Ordinal))
        {
            return Lookup(snapshot.CanonicalCacheKey, seed);
        }
        // MENU / PREVIEW / foreign-seed path: the key is rebuilt from the
        // current process state on every call. This never reuses a prior
        // run's composed context, so preview inputs are invalidated
        // independently of the frozen run context. (Allocations accepted and
        // reported separately from the warm in-run path.)
        return Lookup(seed + "\u0000" + ConfigFingerprint(), seed);
    }

    /// <summary>
    /// Cache lookup + bounded generation, shared by both key paths. The
    /// generation branch reads <see cref="CurrentSnapshot"/> under the lock,
    /// exactly as the pre-QCR-1 body did.
    /// </summary>
    private static IReadOnlyList<ChaosRelicDefinition> Lookup(string key, string seed)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
            var snapshot = CurrentSnapshot;
            var generated = ChaosRelicGenerator.Generate(seed,
                snapshot?.BudgetCommon ?? QuriousCraftingRelicsConfig.ChaosRelicBudgetCommon,
                snapshot?.BudgetUncommon ?? QuriousCraftingRelicsConfig.ChaosRelicBudgetUncommon,
                snapshot?.BudgetRare ?? QuriousCraftingRelicsConfig.ChaosRelicBudgetRare,
                Costs(),
                snapshot?.NegativeChanceCommon ?? QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceCommon,
                snapshot?.NegativeChanceUncommon ?? QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceUncommon,
                snapshot?.NegativeChanceRare ?? QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceRare);
            Cache[key] = generated;
            Order.Enqueue(key);
            while (Order.Count > CacheLimit)
            {
                Cache.Remove(Order.Dequeue());
            }
            return generated;
        }
    }

    /// <summary>The definition for a slot in the current run; null when the model's
    /// owner has no run seed yet (menus, previews). Inside the active run the
    /// ForSeed hit is allocation-free (frozen canonical key + bounded cache),
    /// so per-read DefinitionFor calls cost a dictionary lookup (QCR-1);
    /// consumers that read several amounts still resolve the definition once
    /// per operation (see ChaosRelicModel.SumOf).</summary>
    public static ChaosRelicDefinition? DefinitionFor(RelicModel relic, int slot)
    {
        string? seed = RunSeedOf(relic);
        if (seed is null)
        {
            return null;
        }
        var pool = ForSeed(seed, QuriousCraftingRelicsConfig.ChaosRelicMultiplier);
        return slot >= 0 && slot < pool.Count ? pool[slot] : null;
    }

    /// <summary>
    /// Run seed from the owning player's run state; null outside runs.
    /// NEVER touches RelicModel.Owner: that getter calls AssertMutable and
    /// THROWS CanonicalModelException on canonical (registry/menu) instances -
    /// which is exactly where ModelLocPatch invokes Localization at startup.
    /// Instead read the run seed from the global run context when present.
    /// </summary>
    public static string? RunSeedOf(RelicModel relic)
    {
        return CurrentRunSeed;
    }

    /// <summary>
    /// The active run's seed. ResolveAndRun/GameRun owns the current run; in
    /// menus and canonical contexts this is null. Updated by MainFile patch.
    /// </summary>
    public static string? CurrentRunSeed { get; internal set; }

    /// <summary>
    /// Durable identity of the active run (WS-0916-06); null in menus. Resolved
    /// at capture from the save's persisted token, never from a process-local
    /// slot, so save switching and process restarts cannot hand one save's
    /// frozen context to another. See <see cref="ChaosRunIdentity"/> for the
    /// token shape and what it does and does not reproduce.
    /// </summary>
    public static string? CurrentIdentity { get; internal set; }

    /// <summary>
    /// Captures the run at <paramref name="runState"/>: resolves the DURABLE
    /// identity and either resumes the retained frozen context for that identity
    /// or freezes a new one. Returns the resolved identity plus whether the
    /// context was resumed, so the caller can report the outcome.
    ///
    /// The decision itself lives in <see cref="RunIdentityCapture{TContext}"/>
    /// (engine-free, probe-covered); this method only supplies the engine-side
    /// inputs (the BaseLib-restored token, the freeze delegate, the mod version)
    /// and reports the outcome.
    ///
    /// <paramref name="runStart"/> is true only for a genuinely NEW run
    /// (SetUpNew*), which always mints a fresh identity and freezes immediately:
    /// rerolling with a repeated seed string must never inherit another save's
    /// context. <paramref name="startTimeUnix"/> is the loaded save's persisted
    /// run start time, or 0 when the caller could not supply it.
    /// </summary>
    internal static (string Identity, bool Resumed) CaptureRun(
        IRunState? runState, string? seed, bool runStart, long startTimeUnix)
    {
        CurrentRunSeed = seed;

        var outcome = CaptureRule.Capture(new RunIdentityCapture<QuriousGenerationSnapshot>.CaptureRequest
        {
            RunStart = runStart,
            PersistedToken = runStart ? null : ChaosRunIdentitySave.TokenOf(runState),
            Seed = seed,
            StartTimeUnix = startTimeUnix,
        });

        CurrentIdentity = outcome.Identity;
        CurrentSnapshot = CaptureRule.ActiveContext;
        // The identity is persisted on the RunState so the next save write
        // carries it; a loaded save already supplied it, and re-storing it keeps
        // the round-trip idempotent.
        ChaosRunIdentitySave.Remember(runState, outcome.Identity);
        ReportCapture(outcome, seed);
        return (outcome.Identity, outcome.Resumed);
    }

    /// <summary>
    /// The capture rule for this mod's context type. One instance for the whole
    /// process: it owns the bounded retention of frozen contexts.
    /// </summary>
    private static readonly RunIdentityCapture<QuriousGenerationSnapshot> CaptureRule =
        new(
            RetainedContextLimit,
            freeze: seed => QuriousGenerationSnapshot.Capture(seed),
            fingerprintOf: snapshot => ChaosRunIdentity.Tag(snapshot.CanonicalFingerprint),
            modVersion: ModVersion);

    /// <summary>Leaving a run: drop the active seed/identity so canonical models
    /// render generic text again. The retained contexts stay, so returning to the
    /// save resumes its original generation.</summary>
    internal static void ClearActiveRun()
    {
        CurrentRunSeed = null;
        CurrentIdentity = null;
        CaptureRule.ClearActive();
    }

    /// <summary>
    /// Reports how the identity was obtained and what changed since it was
    /// minted. Loud by contract (WS-0916-06): a save whose generation inputs or
    /// mod version no longer match is never redefined silently.
    /// </summary>
    private static void ReportCapture(
        RunIdentityCapture<QuriousGenerationSnapshot>.CaptureOutcome outcome, string? seed)
    {
        try
        {
            switch (outcome.Source)
            {
                case ChaosRunIdentity.Origin.Minted:
                    MainFile.Logger.Info(
                        $"[QuriousCraftingRelics] run identity minted for seed {seed} " +
                        $"(mod {ModVersion()}, inputs {outcome.FrozenFingerprintTag})");
                    break;
                case ChaosRunIdentity.Origin.Persisted:
                    MainFile.Logger.Info(outcome.Resumed
                        ? $"[QuriousCraftingRelics] run identity {outcome.Identity} resumed with its original frozen context (seed {seed})"
                        : $"[QuriousCraftingRelics] run identity {outcome.Identity} adopted from the save (seed {seed})");
                    break;
                case ChaosRunIdentity.Origin.Legacy:
                    MainFile.Logger.Info(
                        $"[QuriousCraftingRelics] save carries no identity token; using the deterministic " +
                        $"start-time identity {outcome.Identity} (seed {seed}). It survives a restart but cannot " +
                        "be distinguished from another save that shares this seed and start time.");
                    break;
                case ChaosRunIdentity.Origin.SeedOnly:
                    MainFile.Logger.Warn(
                        "[QuriousCraftingRelics] save carries no identity token and no start time; falling back to " +
                        $"the seed-derived identity {outcome.Identity} (seed {seed}). Two different saves that share " +
                        "this seed string will be treated as the same save.");
                    break;
                default:
                    MainFile.Logger.Warn(
                        $"[QuriousCraftingRelics] save carries an unrecognized identity token '{outcome.Identity}'; " +
                        "keeping it verbatim so the save still has one identity.");
                    break;
            }

            if (outcome.InputsDiffer)
            {
                MainFile.Logger.Info(
                    $"[QuriousCraftingRelics] run identity {outcome.Identity}: this save was frozen with generation " +
                    $"inputs {outcome.RecordedFingerprintTag}, but this process uses {outcome.FrozenFingerprintTag}. " +
                    "The frozen inputs are not reproducible here, so the pool is regenerated from the current " +
                    "configuration; already held relics keep their slot identity, but their effects follow the " +
                    "current configuration.");
            }
            if (outcome.VersionDiffers)
            {
                MainFile.Logger.Info(
                    $"[QuriousCraftingRelics] run identity {outcome.Identity} was minted by mod version " +
                    $"{outcome.RecordedVersion}; this process runs {ModVersion()}. The identity is unchanged; the " +
                    "recorded version is informational.");
            }
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] identity report failed: {e.Message}");
        }
    }

    /// <summary>Version of the running mod (manifest); empty when unknown.</summary>
    private static string ModVersion()
    {
        try
        {
            foreach (var mod in ModManager.GetLoadedMods())
            {
                if (string.Equals(mod.manifest?.id, MainFile.ModId, StringComparison.Ordinal))
                {
                    return ChaosRunIdentity.SanitizeVersion(mod.manifest?.version);
                }
            }
        }
        catch
        {
            // Manifest unavailable: the version field stays empty, which the
            // drift report treats as "not recorded" rather than a change.
        }
        return "";
    }

    public static string? RunSeedOf(IRunState? runState)
    {
        if (runState is null || runState is NullRunState)
        {
            return null;
        }
        try
        {
            // RunRngSet.StringSeed: original input seed string (hashed numeric
            // form is RunRngSet.Seed). Same string on both MP ends - the
            // deterministic regeneration contract.
            return runState.Rng.StringSeed;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Is this slot part of the allowed set for the given run?</summary>
    public static bool IsSlotAllowedInRun(int slot, IRunState? runState)
    {
        string? seed = RunSeedOf(runState);
        if (seed is null)
        {
            return false;
        }
        return slot >= 0 && slot < ChaosRelicGenerator.TotalSlots;
    }
}
