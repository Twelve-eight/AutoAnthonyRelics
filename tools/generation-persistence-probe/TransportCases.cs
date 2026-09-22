using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using BaseLib.Patches.Saves;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace GenerationPersistenceProbe;

/// <summary>
/// BaseLib extended-save transport (PLAN item 5).
///
/// WHAT IS REAL HERE: the BaseLib registration entry point
/// (ExtendedSaveTypes.RegisterSavedValue), the ExtendedSaveHandlers data
/// holders, the JSON Load path (which has NO engine gate), the registered
/// per-key Serializer/Deserializer delegates, and the real PacketWriter /
/// PacketReader / SerializableRun types from the game assembly. All of those
/// are managed code and load outside the game.
///
/// WHAT IS NOT EXERCISED HERE (reported as SKIP, never as a pass): the
/// PostModInitPatch.CanModifyGameplay gate that makes
/// ExtendedSaveHandlers.Write/Read return early, RunManager.ToSave,
/// RunState.FromSerializable, the real save JSON pipeline, and the multiplayer
/// message bus. Those need the Godot/engine runtime.
///
/// The probe registers its OWN key through the real API rather than the mod's
/// private key: the mod's delegates are internal, and this probe must not
/// fabricate a same-shaped copy of them. What this proves is that the transport
/// the mod relies on carries a payload of the real encoded size byte-exactly
/// through the real registered delegates and the real packet codec.
/// </summary>
internal static class TransportCases
{
    private const string ProbeSaveKey = "probe_qurious_generation_transport";

    internal static int Run(Options options, Live liveA, SnapshotFactory factoryA)
    {
        liveA.Apply();
        Runner.ClearActive();
        object snapshot = factoryA.Create(options.Seed);
        IReadOnlyList<object> definitions = Runner.GenerateForSnapshot(options.Seed, snapshot, factoryA);
        string identity = Runner.MintIdentity(options.Seed, snapshot, options.Version);
        string payload = Runner.Encode(identity, snapshot, definitions);
        Log.Note("transport.payload chars=" + payload.Length.ToString(CultureInfo.InvariantCulture)
            + " utf8Bytes=" + System.Text.Encoding.UTF8.GetByteCount(payload).ToString(CultureInfo.InvariantCulture)
            + " sha256=" + Runner.Sha256OfString(payload));

        string? delivered = null;
        bool registered;
        try
        {
            registered = ExtendedSaveTypes.RegisterSavedValue<IRunState, string>(
                ProbeSaveKey,
                static _ => (string?)null,
                (_, value) => delivered = value,
                static (value, writer) => writer.WriteString(value),
                static reader => reader.ReadString());
        }
        catch (Exception e)
        {
            Log.Check(false, "transport.registration", "RegisterSavedValue threw: " + e);
            return Log.Finish();
        }
        Log.Check(registered, "transport.registration",
            "ExtendedSaveTypes.RegisterSavedValue returned " + registered);

        var info = ExtendedSaveHandlers<IRunState, SerializableRun>.RegisteredSaves
            .FirstOrDefault(e => string.Equals(e.Id, ProbeSaveKey, StringComparison.Ordinal));
        Log.Check(info is not null, "transport.registration.entry",
            "RegisteredSaves entry for " + ProbeSaveKey + " (count="
            + ExtendedSaveHandlers<IRunState, SerializableRun>.RegisteredSaves.Count.ToString(CultureInfo.InvariantCulture) + ")");
        if (info is null)
        {
            return Log.Finish();
        }

        // --- JSON property surface: what the value will be written under ---
        try
        {
            var jsonOptions = new JsonSerializerOptions();
            var properties = ExtendedSaveHandlers<IRunState, SerializableRun>
                .CreateExtendedProperties(jsonOptions).ToList();
            bool hasStringDict = properties.Any(p =>
                string.Equals(p.Name, "save_dict_String", StringComparison.Ordinal));
            Log.Check(hasStringDict, "transport.json.property.present",
                "properties=" + string.Join(",", properties.Select(p => p.Name)));
        }
        catch (Exception e)
        {
            Log.Skip("transport.json.property.present",
                "CreateExtendedProperties needs the game serializer context here: " + e.GetType().Name + ": " + Runner.Short(e.Message));
        }

        // --- JSON load path (no engine gate in BaseLib.Load) ---
        var save = new SerializableRun();
        ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[save]
            .DictForType<string>()[ProbeSaveKey] = payload;
        ExtendedSaveHandlers<IRunState, SerializableRun>.Load(save, NullRunState.Instance);
        Log.Check(delivered == payload, "transport.json.load.delivers.payload",
            "delivered chars=" + (delivered?.Length ?? -1).ToString(CultureInfo.InvariantCulture));

        // --- the MOD'S OWN registered keys, through the same BaseLib surface ---
        // The probe above proves the API carries a payload; this proves the mod's
        // real keys and real delegates do, which is the claim that matters. The
        // mod's registration runs the production entry point (the same call
        // MainFile.Initialize makes), and its entries are then driven directly.
        ModKeyCases(options, identity, payload);

        // --- packet path through the REAL registered delegates ---
        var data = ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[save];
        var writer = new PacketWriter();
        info.Serializer(data, writer);
        Log.Check(writer.BitPosition > 0, "transport.packet.delegate.wrote.bits",
            "bits=" + writer.BitPosition.ToString(CultureInfo.InvariantCulture)
            + " bytes=" + writer.BytePosition.ToString(CultureInfo.InvariantCulture));
        var loadedData = ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[new SerializableRun()];
        var reader = new PacketReader();
        reader.Reset(writer.Buffer);
        info.Deserializer(loadedData, reader);
        // PacketWriter.BytePosition rounds the bit cursor UP to a byte, and
        // PacketReader.ReadBytes advances 8 bits per byte, so the reader may end
        // up to 7 bits ahead. The meaningful assertion is that the reader never
        // overruns what was written and that the VALUE matches below.
        bool consumedWithinWritten = reader.BitPosition >= writer.BitPosition
            && reader.BitPosition <= writer.BitPosition + 7;
        Log.Check(consumedWithinWritten, "transport.packet.reader.within.written",
            "written=" + writer.BitPosition.ToString(CultureInfo.InvariantCulture)
            + " read=" + reader.BitPosition.ToString(CultureInfo.InvariantCulture));
        bool carried = loadedData.DictForType<string>().TryGetValue(ProbeSaveKey, out string? roundTripped);
        Log.Check(carried, "transport.packet.key.present", "key=" + ProbeSaveKey);
        string got = roundTripped ?? "";
        Log.Check(got == payload, "transport.packet.byte.exact",
            "chars=" + got.Length.ToString(CultureInfo.InvariantCulture)
            + " sha256=" + Runner.Sha256OfString(got));
        if (carried)
        {
            try
            {
                object decoded = Runner.Decode(got, identity, options.Seed);
                Log.Check(Runner.DefinitionsOf(decoded).Count == definitions.Count,
                    "transport.payload.decodable",
                    "definitions=" + Runner.DefinitionsOf(decoded).Count.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception e)
            {
                Log.Check(false, "transport.payload.decodable",
                    "Decode failed after transport: " + e.GetType().FullName);
            }
        }

        // --- the whole-registered-set path, which IS gated by the engine flag ---
        bool canModifyGameplay = ReadCanModifyGameplay();
        var writerAll = new PacketWriter();
        ExtendedSaveHandlers<IRunState, SerializableRun>.Write(save, writerAll);
        if (canModifyGameplay)
        {
            Log.Check(writerAll.BitPosition > 0, "transport.write.all.keys.bits",
                "CanModifyGameplay=true, bits=" + writerAll.BitPosition.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            Log.Skip("transport.write.all.keys.bits",
                "PostModInitPatch.CanModifyGameplay=false in this process, so BaseLib.Write returns early (bits="
                + writerAll.BitPosition.ToString(CultureInfo.InvariantCulture)
                + "). This is the engine gate only the running game can open.");
        }

        // --- explicit list of what remains unverified ---
        Log.Skip("transport.engine.gate",
            "ExtendedSaveHandlers.Write/Read early-return unless PostModInitPatch.CanModifyGameplay (set by LocManager.Initialize in the game)");
        Log.Skip("transport.mod.engine.entry",
            "the engine-side callers of the mod's registered entries (RunManager.ToSave's postfix that invokes the "
            + "getters, and RunState.FromSerializable's postfix that invokes the setters) need the engine; the mod's "
            + "own entries themselves ARE registered and driven above via Load and the packet delegates");
        Log.Skip("transport.multiplayer.bus",
            "ClientLoadJoinResponseMessage / NetMessageBus need the engine and a live session");
        Log.Skip("transport.foreign.writer.key",
            "a payload written by another mod's key cannot be constructed here; cross-mod forward compatibility stays unproven");
        return Log.Finish();
    }

    /// <summary>
    /// Drives the MOD'S OWN two registered save keys through the real BaseLib
    /// extended-save surface: registration through the production entry point,
    /// then the engine's Load (JSON path), Serializer and Deserializer (packet
    /// path) for those entries, with the mod's own bounded string codec.
    ///
    /// WHAT THIS ADDS over the probe-key case above: the probe key uses the
    /// engine's unbounded WriteString/ReadString and a delegate the probe owns.
    /// The mod registers different keys with its own bounded codec, so only
    /// driving THOSE entries shows the values a real save would carry actually
    /// round-trip, and that the mod's length caps accept them.
    ///
    /// WHAT IT STILL CANNOT SHOW: RunManager.ToSave and the JSON file pipeline
    /// need the engine (the getters are called by a RunManager postfix), and the
    /// whole-set Write/Read are gated on PostModInitPatch.CanModifyGameplay,
    /// which only LocManager.Initialize sets in a running game. Both stay
    /// reported as skips.
    /// </summary>
    private static void ModKeyCases(Options options, string identity, string payload)
    {
        var saveType = Runner.SaveType;
        string identityKey = (string)Refs.Get(saveType, "SaveKey")!;
        string generationKey = (string)Refs.Get(saveType, "GenerationSaveKey")!;

        Runner.RegisterPersistence();
        if (Refs.Get(saveType, "PersistenceAvailable") is not true)
        {
            Log.Check(false, "transport.mod.registered",
                "the mod's own registration did not succeed, so its keys cannot be exercised");
            return;
        }
        Log.Check(true, "transport.mod.registered",
            "mod keys registered: " + identityKey + ", " + generationKey);
        var entries = ExtendedSaveHandlers<IRunState, SerializableRun>.RegisteredSaves;
        var identityEntry = entries.FirstOrDefault(e => string.Equals(e.Id, identityKey, StringComparison.Ordinal));
        var generationEntry = entries.FirstOrDefault(e => string.Equals(e.Id, generationKey, StringComparison.Ordinal));
        Log.Check(identityEntry is not null, "transport.mod.key.identity.present",
            "RegisteredSaves contains " + identityKey);
        Log.Check(generationEntry is not null, "transport.mod.key.generation.present",
            "RegisteredSaves contains " + generationKey);
        if (identityEntry is null || generationEntry is null)
        {
            return;
        }

        // JSON load path: the engine's own Load walks every registered entry and
        // calls the mod's setter when the save carries the key. The save is built
        // with the values a real save file would have.
        var source = new SerializableRun();
        object holder = Runner.NewRunState();
        var dict = ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[source].DictForType<string>();
        dict[identityKey] = identity;
        dict[generationKey] = payload;
        ExtendedSaveHandlers<IRunState, SerializableRun>.Load(source, (IRunState)holder);
        Log.Check(Runner.TokenOf(holder) == identity, "transport.mod.json.load.identity",
            "the save's identity reached the mod's own setter: "
            + Runner.Short(Runner.TokenOf(holder) ?? "<null>"));
        Log.Check(Runner.PayloadOf(holder) == payload, "transport.mod.json.load.payload",
            "chars=" + (Runner.PayloadOf(holder)?.Length ?? -1).ToString(CultureInfo.InvariantCulture));

        // Packet path: the mod's own Serializer/Deserializer delegates, i.e. its
        // bounded Write/Read helpers, not the engine's unbounded ones.
        // ExtendedData is keyed by the SERIALIZABLE side (SerializableRun); the
        // holder passed to Load is the IRunState the mod's setters write to.
        var holderData = ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[source];
        var writer = new PacketWriter();
        generationEntry.Serializer(holderData, writer);
        identityEntry.Serializer(holderData, writer);
        int writtenBits = writer.BitPosition;
        Log.Check(writtenBits > 0, "transport.mod.packet.wrote.bits",
            "bits=" + writtenBits.ToString(CultureInfo.InvariantCulture)
            + " bytes=" + writer.BytePosition.ToString(CultureInfo.InvariantCulture));

        var loadedData = ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[new SerializableRun()];
        var reader = new PacketReader();
        reader.Reset(writer.Buffer);
        generationEntry.Deserializer(loadedData, reader);
        identityEntry.Deserializer(loadedData, reader);
        bool generationCarried = loadedData.DictForType<string>().TryGetValue(generationKey, out string? gotPayload);
        bool identityCarried = loadedData.DictForType<string>().TryGetValue(identityKey, out string? gotIdentity);
        Log.Check(generationCarried && gotPayload == payload, "transport.mod.packet.payload.exact",
            "chars=" + (gotPayload?.Length ?? -1).ToString(CultureInfo.InvariantCulture)
            + " sha256=" + (gotPayload is null ? "<none>" : Runner.Sha256OfString(gotPayload)));
        Log.Check(identityCarried && gotIdentity == identity, "transport.mod.packet.identity.exact",
            "identity=" + Runner.Short(gotIdentity ?? "<none>"));

        // The restored payload must still decode against the run it belongs to.
        if (generationCarried && gotPayload is not null)
        {
            try
            {
                object decoded = Runner.Decode(gotPayload, identity, options.Seed);
                Log.Check(Runner.DefinitionsOf(decoded).Count == 60, "transport.mod.payload.decodable",
                    "definitions=" + Runner.DefinitionsOf(decoded).Count.ToString(CultureInfo.InvariantCulture));
            }
            catch (Exception e)
            {
                Log.Check(false, "transport.mod.payload.decodable",
                    "Decode failed after the mod's own transport: " + e.GetType().FullName);
            }
        }

        // The mod's caps must refuse an over-long value BEFORE allocating, which
        // is the whole reason it reimplements the string layout.
        var oversized = new string('A', Runner.MaxEncodedLength() + 1);
        var oversizedWriter = new PacketWriter();
        var oversizedData = ExtendedSaveHandlers<IRunState, SerializableRun>.ExtendedData[new SerializableRun()];
        oversizedData.DictForType<string>()[generationKey] = oversized;
        try
        {
            generationEntry.Serializer(oversizedData, oversizedWriter);
            Log.Check(false, "transport.mod.oversized.refused",
                "an over-cap payload was written (" + oversizedWriter.BitPosition + " bits)");
        }
        catch (Exception e)
        {
            Log.Check(e is InvalidDataException, "transport.mod.oversized.refused",
                e.GetType().FullName + " | msg=" + Runner.Short(e.Message));
        }

        // A desynchronized length must be refused before the read allocates.
        var hostile = new PacketWriter();
        hostile.WriteInt(int.MaxValue);
        hostile.WriteInt(0);
        var hostileReader = new PacketReader();
        hostileReader.Reset(hostile.Buffer);
        try
        {
            generationEntry.Deserializer(loadedData, hostileReader);
            Log.Check(false, "transport.mod.hostile.length.refused",
                "a hostile length was accepted");
        }
        catch (Exception e)
        {
            Log.Check(e is InvalidDataException, "transport.mod.hostile.length.refused",
                e.GetType().FullName + " | msg=" + Runner.Short(e.Message));
        }
    }

    private static bool ReadCanModifyGameplay()
    {
        try
        {
            var type = typeof(ExtendedSaveTypes).Assembly.GetType("BaseLib.Patches.PostModInitPatch", throwOnError: false);
            var prop = type?.GetProperty(
                "CanModifyGameplay",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return prop?.GetValue(null) is true;
        }
        catch
        {
            return false;
        }
    }
}