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
/// generation input comes from the ACTIVE frozen context, published once at
/// capture - NOT from live config. Editing a budget mid-run therefore leaves the
/// (seed, inputs) key and the generated pool unchanged; the already-obtained
/// relics keep their identity for the whole run. Live config is only consulted
/// outside runs (menus/previews).
///
/// PERSISTED DEFINITIONS (R04-01): the frozen context is not only in memory. A
/// new run freezes its inputs, generates all 60 definitions ONCE and encodes the
/// payload that the run save carries; a loaded save with a valid payload restores
/// those definitions verbatim instead of regenerating them, so a process
/// restart, a ledger eviction or a preference edit cannot reroll a run. A payload
/// that fails validation BLOCKS the run: the identity is kept, the previous
/// active state is dropped and no pool is regenerated from the live config. The
/// identity of the run is the DURABLE per-save identity
/// (<see cref="ChaosRunIdentity"/>, <see cref="ChaosRunIdentitySave"/>), never
/// the most recent capture in this process.
///
/// CACHE DOMAINS (must not be conflated): the definition cache below is keyed by
/// (seed, fingerprint), and a RESTORED payload is keyed by the payload's OWN
/// canonical cache key plus the identity it was validated against - two saves
/// with the same seed and the same fingerprint but different saved definitions
/// can therefore never be served each other's pool.
///
/// FROZEN WARM LOOKUP (QCR-1, QCR-R4-02): the active context carries the
/// precomposed canonical cache key, the ordered active catalogs and the effective
/// template metadata. A warm ForSeed/DefinitionFor hit inside the active run is a
/// plain dictionary lookup with ZERO allocation - no fingerprint string building,
/// no list rebuild/sort, no Watcher assembly probe. Menu/preview/foreign-seed
/// queries use an explicit LIVE context built for that call (allocations accepted
/// there, and they never read a prior run's frozen inputs - the global snapshot
/// is never swapped to serve a query).
///
/// LIFECYCLE: <see cref="CurrentSnapshot"/>, <see cref="CurrentDefinitions"/> and
/// the identity describe the ACTIVE run only and are cleared together by
/// <see cref="ClearActiveRun"/>. The bounded ledger keeps a few runs' contexts
/// (including their payloads) as continuation evidence: keys can only be reached
/// with a matching identity/seed, so a kept entry cannot leak into menus.
/// </summary>
public static class ChaosRelicRunRegistry
{
    private const int CacheLimit = 8;

    /// <summary>How many runs' frozen contexts stay resumable. Same bound as
    /// the definition cache; the oldest identity is evicted first.</summary>
    private const int RetainedContextLimit = CacheLimit;

    private static readonly object Gate = new();

    /// <summary>
    /// Definition cache for the ACTIVE run's identity, plus the seeded cache for
    /// menu/preview lookups. Values are the immutable definition lists.
    /// </summary>
    private static readonly Dictionary<string, IReadOnlyList<ChaosRelicDefinition>> Cache =
        new(StringComparer.Ordinal);

    private static readonly Queue<string> Order = new();

    /// <summary>
    /// Snapshot of the ACTIVE run; null outside runs and after a refused capture.
    /// Set by the seed-tracking patches together with
    /// <see cref="CurrentDefinitions"/>. Deliberately kept after CleanUp as
    /// continuation evidence (the ledger holds it; the ACTIVE fields are
    /// cleared) - see <see cref="RunIdentityCapture{TContext}"/>.
    /// </summary>
    public static QuriousGenerationSnapshot? CurrentSnapshot { get; private set; }

    /// <summary>
    /// Definitions of the ACTIVE run, in slot order. Restored from the save's
    /// payload when it carried one, otherwise the ones generated at capture.
    /// Null outside runs and after a refused capture - and never a previous
    /// run's list.
    /// </summary>
    public static IReadOnlyList<ChaosRelicDefinition>? CurrentDefinitions { get; private set; }

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
            return (CurrentRunSeed ?? "") + "\0" + BuildLiveFingerprint();
        }
    }

    /// <summary>
    /// LIVE fingerprint - the menu/preview/foreign-seed path. Allocates by
    /// design: it re-reads live config on every call so a preference edit
    /// between two preview lookups is never served from a stale composed key.
    ///
    /// FULLY LIVE BY CONTRACT (R04-02): it deliberately does NOT go through the
    /// template getters that consult the active snapshot, because a foreign-seed
    /// or menu query must be answered from THIS process's current inputs - the
    /// pre-fix behavior served the previous run's economics to both
    /// (registry-baseline-repro.json: foreignUsesLive=false, menuUsesLive=false).
    /// Construction mirrors the pre-QCR-1 live fingerprint exactly: same fields,
    /// same order, sorted template ids, live pools and live point-cost table.
    /// </summary>
    private static string BuildLiveFingerprint()
    {
        bool extraPool = QuriousCraftingRelicsConfig.EnableExtraPool;
        bool watcherLoaded = ChaosTemplates.WatcherModLoadedProbe;

        var sb = new StringBuilder(256);
        sb.Append(QuriousCraftingRelicsConfig.ChaosRelicBudgetCommon).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicBudgetUncommon).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicBudgetRare).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceCommon).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceUncommon).Append('/')
          .Append(QuriousCraftingRelicsConfig.ChaosRelicNegativeChanceRare).Append('/')
          .Append(extraPool ? '1' : '0').Append('/')
          .Append(watcherLoaded ? '1' : '0');
        // Per-template economics and bounds over the LIVE active pool. Ordered
        // by template id so the fingerprint does not depend on collection
        // iteration order.
        var templates = new List<string>(ChaosTemplates.BuildLivePositiveTemplates(extraPool, watcherLoaded));
        templates.AddRange(ChaosTemplates.BuildLiveNegativeTemplates(extraPool));
        templates.Sort(StringComparer.Ordinal);
        foreach (var template in templates)
        {
            var spec = QuriousCraftingRelicsConfig.ApplyUserBounds(ChaosTemplates.Spec(template));
            sb.Append('|').Append(template)
              .Append(':').Append(QuriousCraftingRelicsConfig.PointCosts.CostPerPoint(template))
              .Append(':').Append(QuriousCraftingRelicsConfig.PointCosts.RefundPerPoint(template))
              .Append(':').Append(spec.Min)
              .Append(':').Append(spec.Max);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Context of the ACTIVE run, built ONCE at capture (so the warm query path
    /// allocates nothing); null outside runs and after a refused capture.
    /// </summary>
    private static GenerationContext? ActiveContext { get; set; }

    /// <summary>
    /// Active run's frozen context, else the live one. The context is what the
    /// generator and the template queries consume, so a query never has to swap
    /// a global snapshot to run.
    /// </summary>
    internal static GenerationContext Context => ActiveContext ?? GenerationContext.Live();

    public static IReadOnlyList<ChaosRelicDefinition> ForSeed(string seed, int multiplier)
    {
        // ACTIVE-RUN FAST PATH (QCR-1 / QCR-R4-02): the canonical key was
        // precomposed on the frozen snapshot at capture. A warm hit performs
        // no string building, no list rebuild, no sorting and no assembly
        // probing - the astra round-4 typed-delegate probe measured 7,208
        // bytes/call for the old per-hit rebuild; the acceptance contract for
        // this path is 0 bytes after warm-up.
        var definitions = CurrentDefinitions;
        var snapshot = CurrentSnapshot;
        if (snapshot is not null && definitions is not null
            && string.Equals(snapshot.RunSeed, seed, StringComparison.Ordinal))
        {
            // ACTIVE RUN: the definitions were produced (frozen) or decoded
            // (restored) once at capture. Returning them directly is a field
            // read - no lock, no dictionary, no string building, no generator -
            // and it is what makes a restored payload immune to a later
            // algorithm change. They are deliberately NOT stored in the
            // menu cache below: that cache is keyed by (seed, fingerprint)
            // alone, and two saves sharing both can carry different saved
            // definitions, so keying restored data by it would cross-serve
            // one save's relics to the other.
            return definitions;
        }
        if (CurrentBlockedReason is not null)
        {
            // A refused run has no definitions by design: serve nothing rather
            // than falling through to a live pool for a run that was blocked.
            return Array.Empty<ChaosRelicDefinition>();
        }
        // MENU / PREVIEW / foreign-seed path: the key is rebuilt from LIVE
        // config on every call and generation uses a LIVE context, so this
        // never reuses a prior run's composed context or frozen inputs.
        var live = GenerationContext.Live();
        return Lookup(seed + "\u0000" + BuildLiveFingerprint(), seed, live);
    }

    /// <summary>
    /// Cache lookup + bounded generation, shared by both key paths. Generation
    /// reads the supplied context only - never the global active snapshot.
    /// </summary>
    private static IReadOnlyList<ChaosRelicDefinition> Lookup(string key, string seed, GenerationContext context)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                return cached;
            }
            var generated = ChaosRelicGenerator.Generate(seed,
                context.BudgetCommon,
                context.BudgetUncommon,
                context.BudgetRare,
                context.Costs,
                context.NegativeChanceCommon,
                context.NegativeChanceUncommon,
                context.NegativeChanceRare,
                context);
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
    /// owner has no run seed yet (menus, previews), or when the run was refused.
    /// Inside the active run the ForSeed hit is allocation-free (frozen canonical
    /// key + bounded cache), so per-read DefinitionFor calls cost a dictionary
    /// lookup (QCR-1).</summary>
    public static ChaosRelicDefinition? DefinitionFor(RelicModel relic, int slot)
    {
        string? seed = RunSeedOf(relic);
        if (seed is null || CurrentBlockedReason is not null)
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
    public static string? CurrentRunSeed { get; private set; }

    /// <summary>
    /// Durable identity of the active run (WS-0916-06); null in menus. Resolved
    /// at capture from the save's persisted token, never from a process-local
    /// slot, so save switching and process restarts cannot hand one save's
    /// frozen context to another. See <see cref="ChaosRunIdentity"/> for the
    /// token shape and what it does and does not reproduce.
    /// </summary>
    public static string? CurrentIdentity { get; private set; }

    /// <summary>
    /// Why the active run has no usable generation state (payload refused,
    /// known drift, persistence unavailable); null when the run is healthy.
    /// Set only by a refused capture, cleared by the next capture or
    /// <see cref="ClearActiveRun"/>.
    /// </summary>
    public static string? CurrentBlockedReason { get; private set; }

    /// <summary>
    /// Captures the run at <paramref name="runState"/>: resolves the DURABLE
    /// identity and either resumes the retained context for that identity,
    /// restores the saved payload, or freezes a new one. Returns the outcome so
    /// the caller can report it.
    ///
    /// The decision itself lives in <see cref="RunIdentityCapture{TContext}"/>
    /// (engine-free, probe-covered); this method only supplies the engine-side
    /// inputs (the BaseLib-restored token and payload, the freeze/restore
    /// delegates, the mod version) and publishes the result ATOMICALLY: the
    /// active seed/identity/snapshot/definitions/blocked-reason are written
    /// together, only after everything succeeded, and a refused capture clears
    /// all of them so the previous run can never leak into this one.
    ///
    /// <paramref name="runStart"/> is true only for a genuinely NEW run
    /// (SetUpNew*), which always mints a fresh identity and freezes immediately:
    /// rerolling with a repeated seed string must never inherit another save's
    /// context. <paramref name="startTimeUnix"/> is the loaded save's persisted
    /// run start time, or 0 when the caller could not supply it.
    /// </summary>
    internal static (string? Identity, bool Resumed) CaptureRun(
        IRunState? runState, string? seed, bool runStart, long startTimeUnix)
    {
        var outcome = CaptureRule.Capture(new RunIdentityCapture<FrozenRun>.CaptureRequest
        {
            RunStart = runStart,
            PersistedToken = runStart ? null : ChaosRunIdentitySave.TokenOf(runState),
            PersistedPayload = runStart ? null : ChaosRunIdentitySave.PayloadOf(runState),
            Seed = seed,
            StartTimeUnix = startTimeUnix,
            PersistenceAvailable = ChaosRunIdentitySave.PersistenceAvailable,
        });

        PublishActive(outcome, seed);

        if (outcome.Generation != GenerationOutcome.Blocked
            && outcome.Generation != GenerationOutcome.Aborted)
        {
            // The identity is persisted on the RunState so the next save write
            // carries it; a loaded save already supplied it, and re-storing it
            // keeps the round-trip idempotent. The payload is stored ONCE here
            // (never rebuilt by the save getter).
            ChaosRunIdentitySave.Remember(runState, outcome.Identity);
            ChaosRunIdentitySave.RememberPayload(runState, outcome.Payload);
        }

        LastOutcome = new CaptureResult(outcome.Identity, outcome.Resumed, outcome.Generation, outcome.BlockedReason);
        ReportCapture(outcome, seed);
        return (outcome.Identity, outcome.Resumed);
    }

    /// <summary>
    /// Outcome of the most recent capture in this process (never of a
    /// different run): how its generation state was obtained and, for a
    /// refusal, why. <see cref="CurrentBlockedReason"/> mirrors the active
    /// run's refusal so a caller can gate on it without reading this.
    /// </summary>
    internal static CaptureResult LastOutcome { get; private set; }

    /// <summary>
    /// Publishes a capture outcome as the ACTIVE run state, atomically. A
    /// refused outcome (blocked/aborted) clears everything and keeps only the
    /// identity plus the reason, so no query can read the previous run's state.
    /// </summary>
    private static void PublishActive(RunIdentityCapture<FrozenRun>.CaptureOutcome outcome, string? seed)
    {
        if (outcome.Generation == GenerationOutcome.Blocked
            || outcome.Generation == GenerationOutcome.Aborted)
        {
            CurrentRunSeed = seed;
            CurrentIdentity = outcome.Identity;
            CurrentSnapshot = null;
            CurrentDefinitions = null;
            ActiveContext = null;
            CurrentBlockedReason = outcome.BlockedReason
                ?? "the run's generation state was refused";
            return;
        }

        var active = CaptureRule.ActiveContext;
        CurrentRunSeed = seed;
        CurrentIdentity = outcome.Identity;
        CurrentSnapshot = active?.Snapshot;
        CurrentDefinitions = active?.Definitions;
        ActiveContext = active is null ? null : GenerationContext.ForSnapshot(active.Snapshot);
        CurrentBlockedReason = null;
    }

    /// <summary>
    /// What one capture produced, for the caller's report.
    /// </summary>
    internal readonly struct CaptureResult
    {
        internal CaptureResult(
            string? identity, bool resumed, GenerationOutcome generation, string? blockedReason)
        {
            Identity = identity;
            Resumed = resumed;
            Generation = generation;
            BlockedReason = blockedReason;
        }

        internal string? Identity { get; }
        internal bool Resumed { get; }

        /// <summary>How the generation state was obtained (restored/frozen/migrated/...).</summary>
        internal GenerationOutcome Generation { get; }

        /// <summary>Why the run has no usable state; null unless refused.</summary>
        internal string? BlockedReason { get; }

        /// <summary>True when the run must not proceed with chaos relics.</summary>
        internal bool Refused =>
            Generation == GenerationOutcome.Blocked || Generation == GenerationOutcome.Aborted;
    }

    /// <summary>
    /// The frozen state of one run as the capture rule sees it: the immutable
    /// snapshot plus the definitions that belong to it (generated or restored).
    /// </summary>
    internal sealed class FrozenRun
    {
        internal FrozenRun(
            QuriousGenerationSnapshot snapshot, IReadOnlyList<ChaosRelicDefinition> definitions)
        {
            Snapshot = snapshot;
            Definitions = definitions;
        }

        internal QuriousGenerationSnapshot Snapshot { get; }
        internal IReadOnlyList<ChaosRelicDefinition> Definitions { get; }
    }

    /// <summary>
    /// The capture rule for this mod's context type. One instance for the whole
    /// process: it owns the bounded retention of frozen contexts.
    ///
    /// FREEZE: generates all 60 definitions once from the freshly captured
    /// snapshot, then encodes the payload that the run save will carry.
    /// RESTORE: decodes the payload against the identity and seed; every
    /// validation failure throws and is turned into a BLOCKED capture by the rule
    /// (never a live rebuild).
    /// </summary>
    private static readonly RunIdentityCapture<FrozenRun> CaptureRule =
        new(
            RetainedContextLimit,
            freeze: seed => FreezeNewRun(seed),
            fingerprintTagOf: frozen => ChaosRunIdentity.Tag(frozen.Snapshot.CanonicalFingerprint),
            payloadOf: (frozen, identity) =>
                QuriousGenerationPersistence.Encode(identity, frozen.Snapshot, frozen.Definitions),
            restore: (payload, identity, seed) =>
            {
                QuriousSavedGeneration saved = QuriousGenerationPersistence.Decode(payload, identity, seed);
                return new FrozenRun(saved.Snapshot, saved.Definitions);
            },
            modVersion: ModVersion);

    private static FrozenRun FreezeNewRun(string? seed)
    {
        var snapshot = QuriousGenerationSnapshot.Capture(seed);
        var definitions = ChaosRelicGenerator.Generate(
            seed ?? "",
            snapshot.BudgetCommon,
            snapshot.BudgetUncommon,
            snapshot.BudgetRare,
            snapshot.FrozenCosts,
            snapshot.NegativeChanceCommon,
            snapshot.NegativeChanceUncommon,
            snapshot.NegativeChanceRare,
            GenerationContext.ForSnapshot(snapshot));
        return new FrozenRun(snapshot, definitions);
    }

    /// <summary>
    /// Leaving a run: drop the active seed/identity/snapshot/definitions so
    /// canonical models render generic text again and no menu query can read
    /// this run's frozen inputs. The retained contexts stay (bounded), so
    /// returning to the save resumes its original generation.
    /// </summary>
    internal static void ClearActiveRun()
    {
        CurrentRunSeed = null;
        CurrentIdentity = null;
        CurrentSnapshot = null;
        CurrentDefinitions = null;
        ActiveContext = null;
        CurrentBlockedReason = null;
        CaptureRule.ClearActive();
    }

    /// <summary>
    /// Reports how the identity was obtained and what happened to the generation
    /// state. Loud by contract (WS-0916-06, R04-04): a save whose generation
    /// inputs or mod version no longer match is never redefined silently, and a
    /// refused payload is reported as a refusal - never as a successful restore.
    /// </summary>
    private static void ReportCapture(RunIdentityCapture<FrozenRun>.CaptureOutcome outcome, string? seed)
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
                    MainFile.Logger.Info(
                        outcome.Resumed
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

            switch (outcome.Generation)
            {
                case GenerationOutcome.Restored:
                    MainFile.Logger.Info(
                        $"[QuriousCraftingRelics] run identity {outcome.Identity}: restored all " +
                        $"{CurrentDefinitions?.Count ?? 0} saved relic definitions from the save payload; the " +
                        "generator did not run for this save.");
                    break;
                case GenerationOutcome.Frozen:
                    MainFile.Logger.Info(
                        $"[QuriousCraftingRelics] run identity {outcome.Identity}: generated " +
                        $"{CurrentDefinitions?.Count ?? 0} relic definitions and prepared the save payload.");
                    break;
                case GenerationOutcome.Migrated:
                    MainFile.Logger.Info(
                        $"[QuriousCraftingRelics] run identity {outcome.Identity}: the save carried no payload, " +
                        "but its recorded inputs and version match this process, so the pool was rebuilt from the " +
                        "unchanged inputs and a payload was added for the next save write.");
                    break;
                case GenerationOutcome.LegacyRebuild:
                    MainFile.Logger.Warn(
                        $"[QuriousCraftingRelics] run identity {outcome.Identity}: the save records no generation " +
                        "metadata, so the pool was rebuilt from the current configuration and is NOT persisted as " +
                        "the original. The definitions that were generated when this save was created cannot be " +
                        "recovered - only saves written from now on carry them.");
                    break;
                case GenerationOutcome.Blocked:
                    MainFile.Logger.Error(
                        $"[QuriousCraftingRelics] run identity {outcome.Identity} was REFUSED: " +
                        $"{outcome.BlockedReason}. The save keeps its data; chaos relics will not be " +
                        "generated or served for this run, and nothing was regenerated from the live config.");
                    break;
                case GenerationOutcome.Aborted:
                    MainFile.Logger.Error(
                        $"[QuriousCraftingRelics] run refused before generation: {outcome.BlockedReason}. " +
                        "No identity and no relic pool were published.");
                    break;
            }

            if (outcome.InputsDiffer && outcome.Generation != GenerationOutcome.Blocked)
            {
                MainFile.Logger.Info(
                    $"[QuriousCraftingRelics] run identity {outcome.Identity}: this save was frozen with generation " +
                    $"inputs {outcome.RecordedFingerprintTag}, but this process uses {outcome.FrozenFingerprintTag}. " +
                    "The frozen inputs are not reproducible here, so the pool is regenerated from the current " +
                    "configuration; already held relics keep their slot identity, but their effects follow the " +
                    "current configuration.");
            }
            if (outcome.VersionDiffers && outcome.Generation != GenerationOutcome.Blocked)
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
        // A refused capture publishes no definitions, so no chaos relic may
        // enter a run whose generation state could not be established.
        return slot >= 0 && slot < ChaosRelicGenerator.TotalSlots && CurrentDefinitions is not null;
    }
}