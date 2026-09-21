using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using BaseLib.Patches.Saves;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;

namespace QuriousCraftingRelics.Chaos;

/// <summary>
/// Persists the run identity (WS-0916-06) and the frozen generation payload
/// (R04-01) with the run save, through BaseLib's existing per-save extension
/// surface - no parallel persistence layer.
///
/// WHY THIS IS ENOUGH: BaseLib 3.4.5 already patches every place the basegame
/// moves a run, so one registration covers all of them:
/// - <c>RunManager.ToSave</c> postfix builds the extended data from
///   <c>State</c> -&gt; our getters, so both values land in the run save JSON;
/// - <c>RunState.FromSerializable</c> postfix -&gt; our setters, so a load
///   restores them;
/// - <c>SerializableRun.Serialize/Deserialize</c> postfixes, so the same values
///   ride the multiplayer packet path (both ends agree on the identity);
/// - <c>Anonymized</c>/<c>CanonicalizeSave</c> postfixes, so they are not
///   dropped by save canonicalization.
/// Values are stored per RunState instance in weak tables, so nothing survives
/// a run it does not belong to.
///
/// TWO KEYS, ONE SHARED DICTIONARY: both values are strings, so BaseLib keeps
/// them in one <c>save_dict_String</c> object, ordered by the ORDINAL order of
/// the ids - <see cref="GenerationSaveKey"/> ("..._generation") sorts BEFORE
/// <see cref="SaveKey"/> ("..._run_identity"), so the payload setter runs first
/// on a load. Neither setter depends on the other (they write different weak
/// tables), and a key that is ABSENT from a save simply never calls its setter -
/// every consumer must treat "absent" as "this save predates the value", which
/// is why the payload lookup is null-tolerant and never fabricates a value.
///
/// COST: BaseLib calls the getters on EVERY <c>ToSave</c> (every room
/// transition). Both getters are weak-table lookups of values that were computed
/// once at capture; nothing is re-serialized, re-generated or re-compressed here.
///
/// LENGTH CAPS: BaseLib reads/writes strings through <c>PacketReader.ReadString</c>
/// / <c>PacketWriter.WriteString</c> (int32 UTF-8 byte count + bytes) with NO
/// bound, so a hostile length would be allocated before it could be rejected.
/// Both keys therefore use the bounded helpers below, which reproduce the exact
/// wire layout (int32 little-endian byte count + UTF-8 bytes) while refusing a
/// length above the key's cap and above the reader's remaining buffer BEFORE
/// allocating. The identity token is capped at <see cref="MaxTokenBytes"/>; the
/// payload at <see cref="QuriousGenerationPersistence.MaxEncodedLength"/> (ASCII
/// base64, so characters and bytes coincide).
///
/// REGISTRATION TIMING (load-bearing): BaseLib materializes the extended JSON
/// properties lazily and then freezes the list
/// (ExtendedSaveHandlers._initializedSaveProps) - a registration performed
/// after that point is dropped with a warning and the value would never be
/// written or read. <see cref="Register"/> therefore runs from
/// <c>MainFile.Initialize</c>, before any run can be saved or loaded.
///
/// FAILURE CONTRACT: if BaseLib refuses either registration the run cannot be
/// persisted. That is reported as an Error and <see cref="RegistrationSucceeded"/>
/// stays false, which makes <see cref="ChaosRelicRunRegistry"/> ABORT a new run
/// instead of pretending it has a durable identity, and refuse to rebuild a
/// loaded one.
/// </summary>
internal static class ChaosRunIdentitySave
{
    /// <summary>
    /// Save key of the identity value. Prefixed with the mod id so it cannot
    /// collide with another mod's entry in BaseLib's shared per-type
    /// dictionary.
    /// </summary>
    internal const string SaveKey = "quriouscraftingrelics_run_identity";

    /// <summary>
    /// Save key of the frozen generation payload (R04-01). Same dictionary as
    /// the identity; sorts before it ordinally, which is fine because the two
    /// setters are independent.
    /// </summary>
    internal const string GenerationSaveKey = "quriouscraftingrelics_generation";

    /// <summary>Cap on the identity token, in UTF-8 bytes.</summary>
    internal const int MaxTokenBytes = 512;

    /// <summary>Identity per RunState instance (weak: dies with the run).</summary>
    private static readonly ConditionalWeakTable<IRunState, string> Tokens = new();

    /// <summary>Frozen generation payload per RunState instance (weak).</summary>
    private static readonly ConditionalWeakTable<IRunState, string> Payloads = new();

    /// <summary>True once BaseLib accepted BOTH registrations.</summary>
    internal static bool RegistrationSucceeded { get; private set; }

    /// <summary>True when the identity key was accepted.</summary>
    internal static bool IdentityRegistered { get; private set; }

    /// <summary>True when the generation payload key was accepted.</summary>
    internal static bool GenerationRegistered { get; private set; }

    /// <summary>
    /// Whether a run can be persisted at all. A new run is ABORTED when this is
    /// false (R04-05): without the channel there is no durable identity, and a
    /// process-local one would silently redefine the run on the next start.
    /// </summary>
    internal static bool PersistenceAvailable => RegistrationSucceeded;

    /// <summary>
    /// Registers both values with BaseLib. Idempotent; called once from
    /// MainFile.Initialize.
    /// </summary>
    internal static void Register()
    {
        if (RegistrationSucceeded)
        {
            return;
        }
        IdentityRegistered = TryRegister(
            SaveKey, static runState => TokenOf(runState), static (runState, value) => Adopt(runState, value),
            MaxTokenBytes, "run identity");
        GenerationRegistered = TryRegister(
            GenerationSaveKey, static runState => PayloadOf(runState),
            static (runState, value) => AdoptPayload(runState, value),
            QuriousGenerationPersistence.MaxEncodedLength, "generation payload");
        RegistrationSucceeded = IdentityRegistered && GenerationRegistered;
        if (!RegistrationSucceeded)
        {
            MainFile.Logger.Error(
                "[QuriousCraftingRelics] run persistence could not be registered with BaseLib " +
                $"(identity={IdentityRegistered}, generation={GenerationRegistered}); new runs are refused " +
                "rather than started with a state that cannot be saved.");
        }
    }

    private static bool TryRegister(
        string key,
        Func<IRunState, string?> getter,
        Action<IRunState, string?> setter,
        int maxBytes,
        string what)
    {
        try
        {
            bool ok = ExtendedSaveTypes.RegisterSavedValue<IRunState, string>(
                key,
                getter,
                setter,
                (value, writer) => WriteBoundedString(writer, value, maxBytes, what),
                reader => ReadBoundedString(reader, maxBytes, what));
            if (!ok)
            {
                MainFile.Logger.Error(
                    $"[QuriousCraftingRelics] BaseLib refused the {what} registration (key {key}).");
            }
            return ok;
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] {what} registration failed: {e.Message}");
            return false;
        }
    }

    /// <summary>Token stored for a run state; null when none (BaseLib then
    /// writes nothing for this key).</summary>
    internal static string? TokenOf(IRunState? runState)
    {
        if (runState is null)
        {
            return null;
        }
        return Tokens.TryGetValue(runState, out string? token) ? token : null;
    }

    /// <summary>Payload stored for a run state; null when none.</summary>
    internal static string? PayloadOf(IRunState? runState)
    {
        if (runState is null)
        {
            return null;
        }
        return Payloads.TryGetValue(runState, out string? payload) ? payload : null;
    }

    /// <summary>Stores the identity for a run state.</summary>
    internal static void Remember(IRunState? runState, string? token)
    {
        if (runState is null || string.IsNullOrEmpty(token))
        {
            return;
        }
        Tokens.AddOrUpdate(runState, token);
    }

    /// <summary>Stores the payload the save must carry for a run state. Called
    /// ONCE per capture, never from the save getter.</summary>
    internal static void RememberPayload(IRunState? runState, string? payload)
    {
        if (runState is null || string.IsNullOrEmpty(payload))
        {
            return;
        }
        Payloads.AddOrUpdate(runState, payload);
    }

    /// <summary>Setter handed to BaseLib: a loaded save carried this token.</summary>
    private static void Adopt(IRunState? runState, string? value)
    {
        Remember(runState, value);
    }

    /// <summary>Setter handed to BaseLib: a loaded save carried this payload.
    /// Kept verbatim - a payload that fails validation later must not be
    /// rewritten or dropped, so the save keeps exactly what it had.</summary>
    private static void AdoptPayload(IRunState? runState, string? value)
    {
        RememberPayload(runState, value);
    }

    /// <summary>
    /// Writes a string exactly as <see cref="PacketWriter.WriteString"/> does
    /// (int32 UTF-8 byte count + raw bytes), after refusing an over-long value.
    /// </summary>
    private static void WriteBoundedString(PacketWriter writer, string value, int maxBytes, string what)
    {
        if (value is null)
        {
            throw new InvalidOperationException($"{what} is null; BaseLib only calls the writer for present values.");
        }
        int bytes = Encoding.UTF8.GetByteCount(value);
        if (bytes > maxBytes)
        {
            throw new InvalidDataException(
                $"{what} is {bytes} bytes, above the {maxBytes} byte cap; refusing to write it.");
        }
        writer.WriteString(value);
    }

    /// <summary>
    /// Reads a string in the exact layout of
    /// <see cref="PacketWriter.WriteString"/> while bounding the allocation:
    /// the length is validated against the key's cap AND the reader's remaining
    /// buffer BEFORE the byte array is created, so a hostile or desynchronized
    /// length cannot allocate.
    /// </summary>
    private static string ReadBoundedString(PacketReader reader, int maxBytes, string what)
    {
        int byteCount = reader.ReadInt();
        if (byteCount < 0)
        {
            throw new InvalidDataException($"{what} length {byteCount} is negative; refusing to read.");
        }
        if (byteCount > maxBytes)
        {
            throw new InvalidDataException(
                $"{what} length {byteCount} bytes is above the {maxBytes} byte cap; refusing to read.");
        }
        byte[]? buffer = reader.Buffer;
        int consumedBytes = reader.BitPosition / 8;
        if (buffer is null || byteCount > buffer.Length - consumedBytes)
        {
            throw new InvalidDataException(
                $"{what} length {byteCount} bytes does not fit the remaining packet buffer; refusing to read.");
        }
        var bytes = new byte[byteCount];
        reader.ReadBytes(bytes, byteCount);
        return Encoding.UTF8.GetString(bytes, 0, byteCount);
    }
}