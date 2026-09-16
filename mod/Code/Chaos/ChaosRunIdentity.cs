using System;
using System.Globalization;
using System.Text;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// Durable per-save run identity (WS-0916-06).
///
/// WHY: the registry used to decide which save a cached relic belonged to with
/// the most recent capture's seed string (the old <c>LastRunSeed</c> slot). That
/// is not a save identity:
/// - save A -&gt; save B (different config) -&gt; save A replaced the single
///   frozen snapshot with B's, and returning to A re-froze it from the LIVE
///   config (A's seed was no longer the last captured one), so held relics
///   changed meaning;
/// - a clean process exit followed by continuing A always re-froze from live
///   config, with no way to tell "same save" from "same seed string".
///
/// The identity is therefore minted once per run and PERSISTED with the run save
/// (see <see cref="ChaosRunIdentitySave"/>), so it survives save switching,
/// process restart and the multiplayer packet path unchanged. The capture rule
/// that consumes it lives in <see cref="RunIdentityCapture{TContext}"/>.
///
/// TOKEN SHAPE (all fields ASCII, ':'-separated):
///   qcr1:&lt;nonce&gt;:&lt;fingerprintTag&gt;:&lt;modVersion&gt;:&lt;seed&gt;
///   qcr0:&lt;startTimeUnix&gt;:&lt;seed&gt;                     (legacy / no token)
/// - nonce: 16 hex chars, unique per run; this is the identity proper. It does
///   NOT depend on config or mod version, so a config edit or a mod update never
///   renames a save's identity - those are DETECTED by comparing the recorded
///   metadata instead (see <see cref="Drift"/>).
/// - fingerprintTag: 16 hex chars of the canonical generation-config
///   fingerprint the run was frozen with.
/// - modVersion: manifest version that minted the token.
/// - seed is LAST because it is the only user-controlled field and may itself
///   contain ':'.
///
/// The legacy form is derived from data the basegame already persists (run seed
/// + SerializableRun.StartTime, written once at run creation and identical on
/// every reload of the same save), so saves written before this change still get
/// a stable identity instead of a per-process one.
///
/// This file is engine-free on purpose: it and
/// <see cref="RunIdentityCapture{TContext}"/> are the whole decision, and
/// tools/save-identity-probe compiles both directly to exercise them.
/// </summary>
internal static class ChaosRunIdentity
{
    internal const string MintedPrefix = "qcr1";
    internal const string LegacyPrefix = "qcr0";
    internal const char Separator = ':';

    /// <summary>Maximum length of the sanitized mod-version field.</summary>
    private const int VersionFieldMaxLength = 64;

    /// <summary>
    /// Stable 16-hex tag of a canonical fingerprint string (FNV-1a 64). Used only
    /// for the "same frozen inputs?" comparison, never for keying.
    /// </summary>
    internal static string Tag(string fingerprint)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in fingerprint)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Unique-per-run identity field: 16 hex chars of a v4 GUID.</summary>
    internal static string NewNonce() => Guid.NewGuid().ToString("N").Substring(0, 16);

    /// <summary>
    /// Drops the separator and control characters so the version field can never
    /// shift the token layout. Empty when unknown.
    /// </summary>
    internal static string SanitizeVersion(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return "";
        }
        var sb = new StringBuilder(version.Length);
        foreach (char c in version)
        {
            if (c == Separator || char.IsControl(c))
            {
                continue;
            }
            sb.Append(c);
            if (sb.Length >= VersionFieldMaxLength)
            {
                break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Mints the durable identity of a NEW run.</summary>
    internal static string Mint(string seed, string fingerprintTag, string? modVersion)
    {
        return string.Join(Separator.ToString(),
            MintedPrefix, NewNonce(), fingerprintTag, SanitizeVersion(modVersion), seed ?? "");
    }

    /// <summary>
    /// Identity of a save that carries no token (written before this change, or
    /// by a path that does not go through the basegame run save).
    /// </summary>
    internal static string Legacy(string? seed, long startTimeUnix)
    {
        return string.Join(Separator.ToString(),
            LegacyPrefix, startTimeUnix.ToString(CultureInfo.InvariantCulture), seed ?? "");
    }

    /// <summary>
    /// Parses a token. Returns false for anything that is not a QCR token (a
    /// foreign string, or null). <paramref name="fingerprintTag"/> and
    /// <paramref name="modVersion"/> stay empty for the legacy form, which
    /// recorded no metadata.
    /// </summary>
    internal static bool TryParse(string? identity, out string seed, out string fingerprintTag, out string modVersion)
    {
        seed = "";
        fingerprintTag = "";
        modVersion = "";
        if (string.IsNullOrEmpty(identity))
        {
            return false;
        }

        // Count-limited split: the seed (last field) may itself contain ':'.
        string[] minted = identity.Split(Separator, 5);
        if (minted.Length == 5 && minted[0] == MintedPrefix)
        {
            fingerprintTag = minted[2];
            modVersion = minted[3];
            seed = minted[4];
            return true;
        }

        string[] legacy = identity.Split(Separator, 3);
        if (legacy.Length == 3 && legacy[0] == LegacyPrefix)
        {
            seed = legacy[2];
            return true;
        }

        return false;
    }

    /// <summary>How an identity was obtained (reported by the capture).</summary>
    internal enum Origin
    {
        /// <summary>Freshly minted nonce for a new run.</summary>
        Minted,
        /// <summary>Adopted from the save's persisted token.</summary>
        Persisted,
        /// <summary>Derived from persisted run start time + seed (old save).</summary>
        Legacy,
        /// <summary>Derived from the seed alone; start time was unavailable.</summary>
        SeedOnly,
        /// <summary>Opaque string that is not a QCR token; kept verbatim.</summary>
        Unrecognized,
    }

    /// <summary>What <see cref="Resolve"/> needs, as plain values.</summary>
    internal readonly struct IdentityInputs
    {
        /// <summary>Token persisted with the loaded save; null when absent.</summary>
        public string? PersistedToken { get; init; }

        /// <summary>Run seed string of the run being captured.</summary>
        public string Seed { get; init; }

        /// <summary>
        /// Persisted run start time (SerializableRun.StartTime) when the load path
        /// could supply it; 0 when unknown.
        /// </summary>
        public long StartTimeUnix { get; init; }
    }

    internal readonly struct Decision
    {
        /// <summary>Identity to key the retained context by.</summary>
        public string Identity { get; init; }

        /// <summary>How the identity was obtained (reporting).</summary>
        public Origin Source { get; init; }

        /// <summary>Fingerprint tag recorded in the token; empty when the token
        /// recorded none (legacy/seed-only/unrecognized).</summary>
        public string RecordedFingerprintTag { get; init; }

        /// <summary>Mod version recorded in the token; empty when none.</summary>
        public string RecordedVersion { get; init; }
    }

    /// <summary>
    /// Resolves the identity of a LOADED save. A genuinely new run does not come
    /// through here: it always mints a fresh identity (<see cref="Mint"/>),
    /// because rerolling with a repeated seed string must never inherit another
    /// save's frozen context.
    ///
    /// Pure: no engine state, no logging, no side effects, so the probe can drive
    /// every branch directly. Whether the caller then RESUMES a retained context
    /// or freezes a new one is a separate decision, made by the ledger lookup on
    /// the returned identity.
    /// </summary>
    internal static Decision Resolve(IdentityInputs inputs)
    {
        if (TryParse(inputs.PersistedToken, out _, out string tag, out string version))
        {
            return new Decision
            {
                Identity = inputs.PersistedToken!,
                Source = Origin.Persisted,
                RecordedFingerprintTag = tag,
                RecordedVersion = version,
            };
        }

        if (!string.IsNullOrEmpty(inputs.PersistedToken))
        {
            // A token we cannot parse (foreign writer, future format). Key by it
            // verbatim rather than discarding it, so the save still has ONE
            // identity, and report it.
            return new Decision
            {
                Identity = inputs.PersistedToken!,
                Source = Origin.Unrecognized,
            };
        }

        // No token: an old save. Deterministic, so a restart resolves the same
        // identity - but only the start time distinguishes two saves sharing a
        // seed string, hence the two origins.
        return new Decision
        {
            Identity = Legacy(inputs.Seed, inputs.StartTimeUnix),
            Source = inputs.StartTimeUnix > 0 ? Origin.Legacy : Origin.SeedOnly,
        };
    }

    /// <summary>
    /// Compares a token's recorded metadata against the context this process
    /// actually froze. Pure, so the reporting contract is probe-testable.
    /// <paramref name="resumedRetained"/> forces both false: a resumed context IS
    /// the token's context by construction, so nothing can have drifted.
    ///
    /// Null-safe by contract: a Decision that recorded no metadata (legacy,
    /// seed-only, unrecognized) leaves the recorded fields null, and an absent
    /// record is "not recorded", never a change.
    /// </summary>
    internal static (bool InputsDiffer, bool VersionDiffers) Drift(
        Decision decision, string frozenFingerprintTag, string currentModVersion, bool resumedRetained)
    {
        if (resumedRetained)
        {
            return (false, false);
        }
        bool inputsDiffer = !string.IsNullOrEmpty(decision.RecordedFingerprintTag)
            && !string.Equals(decision.RecordedFingerprintTag, frozenFingerprintTag, StringComparison.Ordinal);
        bool versionDiffers = !string.IsNullOrEmpty(decision.RecordedVersion)
            && !string.Equals(decision.RecordedVersion, SanitizeVersion(currentModVersion), StringComparison.Ordinal);
        return (inputsDiffer, versionDiffers);
    }
}
