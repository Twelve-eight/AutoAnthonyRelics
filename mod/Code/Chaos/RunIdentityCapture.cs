using System;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// The capture rule of WS-0916-06, in engine-free form: given a run's persisted
/// identity token (or its absence) it decides the run's durable identity and
/// whether the caller must RESUME a retained generation context or FREEZE a new
/// one. <typeparamref name="TContext"/> is the frozen context type - in the game
/// it is <c>QuriousGenerationSnapshot</c>; the probe substitutes a plain value
/// so the rule can be exercised without loading the engine.
///
/// CONTRACT (the whole point of the fix):
/// - a NEW run always mints and always freezes: a repeated seed string must
///   never inherit another save's context;
/// - a LOADED save resolves its identity from its persisted token, so the
///   identity is stable across save switching and across process restarts;
/// - a run whose identity is already retained RESUMES that context, so save A
///   -&gt; save B -&gt; save A gives back A's ORIGINAL definitions even though B
///   was loaded in between and even though the live config changed;
/// - a save with no token gets a deterministic identity derived from persisted
///   data (never a random one), and is REPORTED as such;
/// - nothing here reads a process-local "last run" slot.
///
/// RETENTION: bounded FIFO by identity, oldest first, and re-entering an
/// already-retained identity refreshes it IN PLACE (its original position is
/// kept) - so returning to a save can never evict a different save's context.
/// </summary>
internal sealed class RunIdentityCapture<TContext> where TContext : class
{
    private readonly RunIdentityLedger<TContext> _ledger;
    private readonly Func<string?, TContext> _freeze;
    private readonly Func<TContext, string> _fingerprintOf;
    private readonly Func<string> _modVersion;

    internal RunIdentityCapture(
        int retainedLimit,
        Func<string?, TContext> freeze,
        Func<TContext, string> fingerprintOf,
        Func<string> modVersion)
    {
        _ledger = new RunIdentityLedger<TContext>(retainedLimit);
        _freeze = freeze;
        _fingerprintOf = fingerprintOf;
        _modVersion = modVersion;
    }

    /// <summary>Identity of the active run; null outside runs.</summary>
    internal string? ActiveIdentity { get; private set; }

    /// <summary>Frozen context of the active run; null outside runs.</summary>
    internal TContext? ActiveContext { get; private set; }

    /// <summary>How many runs' contexts stay resumable.</summary>
    internal int RetainedLimit => _ledger.Limit;

    /// <summary>Number of retained contexts.</summary>
    internal int RetainedCount => _ledger.Count;

    /// <summary>Retained context for an identity, if any (diagnostics/probe).</summary>
    internal bool TryGetRetained(string identity, out TContext? context) => _ledger.TryGet(identity, out context);

    /// <summary>Leaving a run: forget the ACTIVE identity/context. Retained
    /// contexts stay, so returning to the save resumes its original generation.</summary>
    internal void ClearActive()
    {
        ActiveIdentity = null;
        ActiveContext = null;
    }

    /// <summary>Resolves and publishes the run described by
    /// <paramref name="request"/>; returns what happened for reporting.</summary>
    internal CaptureOutcome Capture(CaptureRequest request)
    {
        if (request.RunStart)
        {
            // Freeze first: the minted token records the fingerprint tag of the
            // context this run actually got, and that tag cannot be known before
            // the context exists.
            TContext fresh = _freeze(request.Seed);
            string tag = _fingerprintOf(fresh);
            string minted = ChaosRunIdentity.Mint(request.Seed ?? "", tag, _modVersion());
            _ledger.Publish(minted, fresh);
            ActiveIdentity = minted;
            ActiveContext = fresh;
            return new CaptureOutcome
            {
                Identity = minted,
                Source = ChaosRunIdentity.Origin.Minted,
                Resumed = false,
                FrozenFingerprintTag = tag,
            };
        }

        ChaosRunIdentity.Decision decision = ChaosRunIdentity.Resolve(new ChaosRunIdentity.IdentityInputs
        {
            PersistedToken = request.PersistedToken,
            Seed = request.Seed ?? "",
            StartTimeUnix = request.StartTimeUnix,
        });

        // Ledger lookup decides RESUME vs FREEZE; it never changes the identity.
        bool retained = _ledger.TryGet(decision.Identity, out TContext? retainedContext);
        TContext context;
        if (retained)
        {
            // A retained context keeps the ORIGINAL frozen inputs, so this run's
            // held relics keep the definitions they were generated from even
            // when another save was loaded in between.
            context = retainedContext!;
        }
        else
        {
            context = _freeze(request.Seed);
            _ledger.Publish(decision.Identity, context);
        }

        ActiveIdentity = decision.Identity;
        ActiveContext = context;

        string frozenTag = _fingerprintOf(context);
        var (inputsDiffer, versionDiffers) = ChaosRunIdentity.Drift(
            decision, frozenTag, _modVersion(), retained);
        return new CaptureOutcome
        {
            Identity = decision.Identity,
            Source = decision.Source,
            Resumed = retained,
            FrozenFingerprintTag = frozenTag,
            RecordedFingerprintTag = decision.RecordedFingerprintTag,
            RecordedVersion = decision.RecordedVersion,
            InputsDiffer = inputsDiffer,
            VersionDiffers = versionDiffers,
        };
    }

    /// <summary>What a capture was told about the run.</summary>
    internal readonly struct CaptureRequest
    {
        /// <summary>True for SetUpNew* (a genuinely new run).</summary>
        public bool RunStart { get; init; }

        /// <summary>Token persisted with the loaded save; null when absent.</summary>
        public string? PersistedToken { get; init; }

        /// <summary>Run seed string.</summary>
        public string? Seed { get; init; }

        /// <summary>Persisted run start time, or 0 when unavailable.</summary>
        public long StartTimeUnix { get; init; }
    }

    /// <summary>What a capture did, for the (engine-side) report.</summary>
    internal readonly struct CaptureOutcome
    {
        public string Identity { get; init; }
        public ChaosRunIdentity.Origin Source { get; init; }
        public bool Resumed { get; init; }

        /// <summary>Tag of the inputs the run is now frozen with.</summary>
        public string FrozenFingerprintTag { get; init; }

        /// <summary>Tag the token recorded; empty when it recorded none.</summary>
        public string RecordedFingerprintTag { get; init; }

        /// <summary>Version the token recorded; empty when none.</summary>
        public string RecordedVersion { get; init; }

        /// <summary>True when the token's inputs are not reproducible here, so
        /// the pool was regenerated from the current configuration.</summary>
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
/// different save's context.
/// </summary>
internal sealed class RunIdentityLedger<T> where T : class
{
    private readonly int _limit;
    private readonly System.Collections.Generic.Dictionary<string, T> _entries =
        new(StringComparer.Ordinal);
    private readonly System.Collections.Generic.Queue<string> _order = new();

    internal RunIdentityLedger(int limit)
    {
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }
        _limit = limit;
    }

    /// <summary>How many identities can be retained at once.</summary>
    internal int Limit => _limit;

    /// <summary>Number of retained entries.</summary>
    internal int Count => _entries.Count;

    /// <summary>Retained value for an identity, if any.</summary>
    internal bool TryGet(string identity, out T? value) => _entries.TryGetValue(identity, out value);

    /// <summary>Publishes (or replaces) the value of an identity, evicting the
    /// oldest identity beyond the bound.</summary>
    internal void Publish(string identity, T value)
    {
        if (!_entries.ContainsKey(identity))
        {
            _order.Enqueue(identity);
        }
        _entries[identity] = value;
        while (_order.Count > _limit)
        {
            _entries.Remove(_order.Dequeue());
        }
    }
}
