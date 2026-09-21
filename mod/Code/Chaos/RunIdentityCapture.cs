using System;
using System.Collections.Generic;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// How the generation state of a capture was obtained (R04-01/02/04). Reported
/// by <see cref="RunIdentityCapture{TContext}.Capture"/> so the engine side can
/// log the exact outcome instead of a boolean.
/// </summary>
internal enum GenerationOutcome
{
    /// <summary>No payload work: the run resumed a context already retained in this process.</summary>
    None = 0,

    /// <summary>New run: inputs frozen, definitions generated, payload prepared for saving.</summary>
    Frozen = 1,

    /// <summary>Loaded save: the saved payload was decoded and published verbatim. No RNG ran.</summary>
    Restored = 2,

    /// <summary>
    /// Token-only save whose recorded fingerprint and version match this
    /// process: the pool was rebuilt from the (provably unchanged) inputs and a
    /// payload was written so the next save carries the definitions.
    /// </summary>
    Migrated = 3,

    /// <summary>
    /// Save with no recorded metadata (legacy qcr0 token, or no token at all):
    /// the pool was rebuilt from current inputs and reported, but NO payload was
    /// written - the historical definitions cannot be verified, so the save must
    /// not claim to carry them.
    /// </summary>
    LegacyRebuild = 4,

    /// <summary>
    /// Refused: a saved payload failed validation, or the save records a known
    /// input/version drift. The identity is kept, nothing is regenerated and no
    /// active context is published.
    /// </summary>
    Blocked = 5,

    /// <summary>
    /// Refused before any generation ran (persistence unavailable, or freezing
    /// itself failed). Nothing is published and no identity is pretended.
    /// </summary>
    Aborted = 6,
}

/// <summary>
/// The capture rule of WS-0916-06 plus the R04-01 generation persistence
/// decision, in engine-free form: given a run's persisted identity token and its
/// persisted generation payload (or their absence) it decides the run's durable
/// identity and where its frozen generation state comes from.
/// <typeparamref name="TContext"/> is the frozen context type - in the game it
/// is the registry's snapshot+definitions+payload state; the probe substitutes a
/// plain value so the rule can be exercised without loading the engine.
///
/// CONTRACT (the whole point of the fix):
/// - a NEW run always mints and always freezes: a repeated seed string must
///   never inherit another save's context. When persistence is unavailable the
///   capture ABORTS instead of pretending to have a durable identity;
/// - a LOADED save with a payload restores it VERBATIM: no RNG, no live config,
///   no silent rebuild. A payload that fails validation BLOCKS the run (the
///   identity is kept, nothing is republished, the previous run's state is
///   dropped) rather than falling back to the live configuration;
/// - a run whose identity is already retained RESUMES that context, so save A
///   -&gt; save B -&gt; save A gives back A's ORIGINAL definitions;
/// - a token-only save is migrated ONLY when its recorded fingerprint tag and
///   mod version match this process (a verifiable rebuild); a KNOWN drift
///   blocks it and keeps the original token;
/// - a save with no recorded metadata gets a deterministic identity derived
///   from persisted data (never a random one) and a reported rebuild that is
///   NOT persisted as if it were the original;
/// - nothing here reads a process-local "last run" slot.
///
/// RETENTION: bounded FIFO by identity, oldest first, and re-entering an
/// already-retained identity refreshes it IN PLACE (its original position is
/// kept) - so returning to a save can never evict a different save's context.
/// Blocked identities are retained the same way, so a later capture of the same
/// save cannot quietly regenerate what was already refused.
/// </summary>
internal sealed class RunIdentityCapture<TContext> where TContext : class
{
    private readonly RunIdentityLedger<TContext> _ledger;
    private readonly Func<string?, TContext> _freeze;
    private readonly Func<TContext, string> _fingerprintTagOf;
    private readonly Func<TContext, string, string> _payloadOf;
    private readonly Func<string, string, string, TContext> _restore;
    private readonly Func<string> _modVersion;

    internal RunIdentityCapture(
        int retainedLimit,
        Func<string?, TContext> freeze,
        Func<TContext, string> fingerprintTagOf,
        Func<TContext, string, string> payloadOf,
        Func<string, string, string, TContext> restore,
        Func<string> modVersion)
    {
        _ledger = new RunIdentityLedger<TContext>(retainedLimit);
        _freeze = freeze;
        _fingerprintTagOf = fingerprintTagOf;
        _payloadOf = payloadOf;
        _restore = restore;
        _modVersion = modVersion;
    }

    /// <summary>Identity of the active run; null outside runs and when a capture was refused.</summary>
    internal string? ActiveIdentity { get; private set; }

    /// <summary>Frozen context of the active run; null outside runs and when a capture was refused.</summary>
    internal TContext? ActiveContext { get; private set; }

    /// <summary>Payload the active run must persist; null when there is none.</summary>
    internal string? ActivePayload { get; private set; }

    /// <summary>Why the active run has no usable context; null when it has one.</summary>
    internal string? ActiveBlockedReason { get; private set; }

    /// <summary>How many runs' contexts stay resumable.</summary>
    internal int RetainedLimit => _ledger.Limit;

    /// <summary>Number of retained identities (contexts and refusals).</summary>
    internal int RetainedCount => _ledger.Count;

    /// <summary>Retained context for an identity, if any (diagnostics/probe).</summary>
    internal bool TryGetRetained(string identity, out TContext? context) => _ledger.TryGet(identity, out context);

    /// <summary>Leaving a run: forget the ACTIVE identity/context/payload so a
    /// later query cannot read this run's state. Retained contexts stay, so
    /// returning to the save resumes its original generation.</summary>
    internal void ClearActive()
    {
        ActiveIdentity = null;
        ActiveContext = null;
        ActivePayload = null;
        ActiveBlockedReason = null;
    }

    /// <summary>Resolves and publishes the run described by
    /// <paramref name="request"/>; returns what happened for reporting.</summary>
    internal CaptureOutcome Capture(CaptureRequest request)
    {
        string seed = request.Seed ?? "";

        if (request.RunStart)
        {
            return CaptureNewRun(request, seed);
        }

        ChaosRunIdentity.Decision decision = ChaosRunIdentity.Resolve(new ChaosRunIdentity.IdentityInputs
        {
            PersistedToken = request.PersistedToken,
            Seed = seed,
            StartTimeUnix = request.StartTimeUnix,
        });

        // 1) A saved payload is AUTHORITATIVE: it decides restore-or-block, and
        //    a failure must never be answered with a live rebuild.
        if (!string.IsNullOrEmpty(request.PersistedPayload))
        {
            return RestorePayload(request, decision, seed);
        }

        // 2) No payload in this save: a context retained in this process is the
        //    run's own (the save simply has not been rewritten yet).
        if (_ledger.TryGetEntry(decision.Identity, out RunIdentityLedger<TContext>.Entry retained))
        {
            if (retained.BlockedReason is { } blockedReason)
            {
                return Block(decision, blockedReason, request, seed);
            }
            TContext resumed = retained.Value!;
            PublishActive(decision.Identity, resumed, retained.Payload);
            return new CaptureOutcome
            {
                Identity = decision.Identity,
                Source = decision.Source,
                Resumed = true,
                Generation = GenerationOutcome.None,
                Payload = retained.Payload,
                FrozenFingerprintTag = _fingerprintTagOf(resumed),
                RecordedFingerprintTag = decision.RecordedFingerprintTag,
                RecordedVersion = decision.RecordedVersion,
            };
        }

        // 3) No payload and nothing retained: the legacy migration rules.
        return RebuildLegacy(request, decision, seed);
    }

    private CaptureOutcome CaptureNewRun(CaptureRequest request, string seed)
    {
        if (!request.PersistenceAvailable)
        {
            // R04-05: without the persistence channel a new run must not be
            // given a process-local identity that dies with the process - the
            // capture is refused outright and reported by the caller.
            ClearActive();
            return new CaptureOutcome
            {
                Generation = GenerationOutcome.Aborted,
                Aborted = true,
                BlockedReason = "identity persistence is unavailable; a new run was not generated",
            };
        }

        // Freeze first: the minted token records the fingerprint tag of the
        // context this run actually got, and that tag cannot be known before
        // the context exists.
        TContext fresh;
        try
        {
            fresh = _freeze(seed);
        }
        catch (Exception e)
        {
            ClearActive();
            return new CaptureOutcome
            {
                Generation = GenerationOutcome.Aborted,
                Aborted = true,
                BlockedReason = "freezing the generation inputs failed: " + e.Message,
            };
        }

        string tag = _fingerprintTagOf(fresh);
        string minted = ChaosRunIdentity.Mint(seed, tag, _modVersion());

        string payload;
        try
        {
            payload = _payloadOf(fresh, minted);
        }
        catch (Exception e)
        {
            // The run exists but could not be persisted. Publishing it would
            // create a run whose relics change meaning on the next load.
            ClearActive();
            return new CaptureOutcome
            {
                Generation = GenerationOutcome.Aborted,
                Aborted = true,
                BlockedReason = "the new run's generation payload could not be encoded: " + e.Message,
            };
        }

        _ledger.PublishContext(minted, fresh, payload);
        PublishActive(minted, fresh, payload);
        return new CaptureOutcome
        {
            Identity = minted,
            Source = ChaosRunIdentity.Origin.Minted,
            Resumed = false,
            Generation = GenerationOutcome.Frozen,
            Payload = payload,
            FrozenFingerprintTag = tag,
        };
    }

    private CaptureOutcome RestorePayload(CaptureRequest request, ChaosRunIdentity.Decision decision, string seed)
    {
        try
        {
            TContext restored = _restore(request.PersistedPayload!, decision.Identity, seed);
            string payload = request.PersistedPayload!;
            _ledger.PublishContext(decision.Identity, restored, payload);
            PublishActive(decision.Identity, restored, payload);
            return new CaptureOutcome
            {
                Identity = decision.Identity,
                Source = decision.Source,
                Resumed = true,
                Generation = GenerationOutcome.Restored,
                Payload = payload,
                FrozenFingerprintTag = _fingerprintTagOf(restored),
                RecordedFingerprintTag = decision.RecordedFingerprintTag,
                RecordedVersion = decision.RecordedVersion,
            };
        }
        catch (Exception e)
        {
            // The save keeps its payload; nothing is regenerated and nothing is
            // published. The caller reports the refusal.
            return Block(decision,
                "the saved generation payload was rejected: " + e.Message, request, seed);
        }
    }

    private CaptureOutcome RebuildLegacy(CaptureRequest request, ChaosRunIdentity.Decision decision, string seed)
    {
        if (!request.PersistenceAvailable)
        {
            return Block(decision,
                "identity persistence is unavailable; refusing to rebuild a run whose state cannot be saved",
                request, seed);
        }

        TContext frozen;
        try
        {
            frozen = _freeze(seed);
        }
        catch (Exception e)
        {
            return Block(decision, "freezing the generation inputs failed: " + e.Message, request, seed);
        }

        string frozenTag = _fingerprintTagOf(frozen);
        var (inputsDiffer, versionDiffers) = ChaosRunIdentity.Drift(
            decision, frozenTag, _modVersion(), resumedRetained: false);

        if (inputsDiffer || versionDiffers)
        {
            string reason = inputsDiffer
                ? "this save was generated with different generation inputs (" +
                  decision.RecordedFingerprintTag + " vs " + frozenTag +
                  "); refusing to regenerate its relics"
                : "this save was generated by mod version " + decision.RecordedVersion +
                  " and this process runs " + _modVersion() + "; refusing to regenerate its relics";
            return Block(decision, reason, request, seed);
        }

        // A rebuild may be persisted ONLY when the save's own metadata proves
        // the inputs are unchanged (qcr1 token with a recorded tag AND version).
        bool verifiable = decision.Source == ChaosRunIdentity.Origin.Persisted
            && !string.IsNullOrEmpty(decision.RecordedFingerprintTag)
            && !string.IsNullOrEmpty(decision.RecordedVersion);

        string? payload = null;
        GenerationOutcome generation = GenerationOutcome.LegacyRebuild;
        if (verifiable)
        {
            try
            {
                payload = _payloadOf(frozen, decision.Identity);
                generation = GenerationOutcome.Migrated;
            }
            catch (Exception e)
            {
                // A verified rebuild that cannot be written is still served (the
                // definitions are correct) but the save is not upgraded; report
                // it rather than claiming the payload was stored.
                return Block(decision,
                    "the rebuilt generation payload could not be encoded: " + e.Message, request, seed);
            }
        }

        _ledger.PublishContext(decision.Identity, frozen, payload);
        PublishActive(decision.Identity, frozen, payload);
        return new CaptureOutcome
        {
            Identity = decision.Identity,
            Source = decision.Source,
            Resumed = false,
            Generation = generation,
            Payload = payload,
            FrozenFingerprintTag = frozenTag,
            RecordedFingerprintTag = decision.RecordedFingerprintTag,
            RecordedVersion = decision.RecordedVersion,
        };
    }

    private CaptureOutcome Block(
        ChaosRunIdentity.Decision decision, string reason, CaptureRequest request, string seed)
    {
        _ledger.PublishBlocked(decision.Identity, reason);
        ClearActive();
        ActiveIdentity = decision.Identity;
        ActiveBlockedReason = reason;

        // The recorded metadata is reported verbatim so the caller can say WHICH
        // kind of refusal this was; no drift comparison is invented here (a
        // refusal is not a rebuild, so "differ" would be meaningless).
        return new CaptureOutcome
        {
            Identity = decision.Identity,
            Source = decision.Source,
            Resumed = false,
            Generation = GenerationOutcome.Blocked,
            BlockedReason = reason,
            RecordedFingerprintTag = decision.RecordedFingerprintTag,
            RecordedVersion = decision.RecordedVersion,
        };
    }

    private void PublishActive(string identity, TContext context, string? payload)
    {
        ActiveIdentity = identity;
        ActiveContext = context;
        ActivePayload = payload;
        ActiveBlockedReason = null;
    }

    /// <summary>What a capture was told about the run.</summary>
    internal readonly struct CaptureRequest
    {
        /// <summary>True for SetUpNew* (a genuinely new run).</summary>
        public bool RunStart { get; init; }

        /// <summary>Token persisted with the loaded save; null when absent.</summary>
        public string? PersistedToken { get; init; }

        /// <summary>Generation payload persisted with the loaded save; null when absent.</summary>
        public string? PersistedPayload { get; init; }

        /// <summary>Run seed string.</summary>
        public string? Seed { get; init; }

        /// <summary>Persisted run start time, or 0 when unavailable.</summary>
        public long StartTimeUnix { get; init; }

        /// <summary>
        /// False when the persistence channel is not registered: a new run is
        /// then ABORTED and a legacy rebuild is refused, so the mod never
        /// presents a run whose relics cannot survive a restart.
        /// </summary>
        public bool PersistenceAvailable { get; init; }
    }

    /// <summary>What a capture did, for the (engine-side) report.</summary>
    internal readonly struct CaptureOutcome
    {
        public string? Identity { get; init; }
        public ChaosRunIdentity.Origin Source { get; init; }
        public bool Resumed { get; init; }

        /// <summary>How the generation state was obtained.</summary>
        public GenerationOutcome Generation { get; init; }

        /// <summary>Payload the caller must persist for this run; null when none.</summary>
        public string? Payload { get; init; }

        /// <summary>Why nothing was published; null unless blocked/aborted.</summary>
        public string? BlockedReason { get; init; }

        /// <summary>True when no identity and no context were published.</summary>
        public bool Aborted { get; init; }

        /// <summary>Tag of the inputs the run is now frozen with.</summary>
        public string FrozenFingerprintTag { get; init; }

        /// <summary>Tag the token recorded; empty when it recorded none.</summary>
        public string RecordedFingerprintTag { get; init; }

        /// <summary>Version the token recorded; empty when none.</summary>
        public string RecordedVersion { get; init; }

        /// <summary>True when the token's inputs are not reproducible here.</summary>
        public bool InputsDiffer { get; init; }

        /// <summary>True when the token was minted by a different mod version.</summary>
        public bool VersionDiffers { get; init; }
    }
}

/// <summary>
/// Bounded FIFO retention keyed by a durable identity - the same shape as
/// ChaosRelicRunRegistry's definition cache (Cache/Order/CacheLimit).
///
/// Publishing an already-known identity replaces its value IN PLACE (the original
/// FIFO position is kept), so re-entering the same save can never evict a
/// different save's context. A refusal is retained the same way: a run that was
/// blocked once is not silently regenerated by a later capture.
/// </summary>
internal sealed class RunIdentityLedger<T> where T : class
{
    private readonly int _limit;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    internal RunIdentityLedger(int limit)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
        _limit = limit;
    }

    /// <summary>One retained identity: a context, a refusal, or both empty.</summary>
    internal readonly struct Entry
    {
        internal T? Value { get; init; }
        internal string? Payload { get; init; }
        internal string? BlockedReason { get; init; }
    }

    /// <summary>How many identities can be retained at once.</summary>
    internal int Limit => _limit;

    /// <summary>Number of retained entries.</summary>
    internal int Count => _entries.Count;

    /// <summary>Retained context for an identity, if any.</summary>
    internal bool TryGet(string identity, out T? value)
    {
        if (_entries.TryGetValue(identity, out Entry entry))
        {
            value = entry.Value;
            return value is not null;
        }
        value = null;
        return false;
    }

    /// <summary>Retained entry (context or refusal) for an identity.</summary>
    internal bool TryGetEntry(string identity, out Entry entry) => _entries.TryGetValue(identity, out entry);

    /// <summary>Publishes (or replaces) the context of an identity, evicting the
    /// oldest identity beyond the bound.</summary>
    internal void PublishContext(string identity, T value, string? payload)
    {
        Store(identity, new Entry { Value = value, Payload = payload });
    }

    /// <summary>
    /// Publishes a context without a payload. Only used by tools that exercise
    /// the retention mechanics directly; the game's capture always has either a
    /// payload or a refusal.
    /// </summary>
    internal void Publish(string identity, T value) => PublishContext(identity, value, null);

    /// <summary>Publishes (or replaces) a refusal for an identity.</summary>
    internal void PublishBlocked(string identity, string reason)
    {
        Store(identity, new Entry { BlockedReason = reason });
    }

    private void Store(string identity, Entry entry)
    {
        if (!_entries.ContainsKey(identity))
        {
            _order.Enqueue(identity);
        }
        _entries[identity] = entry;
        while (_order.Count > _limit)
        {
            _entries.Remove(_order.Dequeue());
        }
    }
}