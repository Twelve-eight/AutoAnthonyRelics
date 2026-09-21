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

    internal static int Run(Options options, Live liveA, Live liveB, SnapshotFactory factoryA)
    {
        liveA.Apply();
        Runner.ClearActive();
        object snapshot = factoryA.Create(options.Seed);
        IReadOnlyList<object> definitions = Runner.Generate(options.Seed, snapshot, factoryA);
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
        Log.Skip("transport.mod.registered.key",
            "the mod's own key and internal delegates need the game mod-init path; this probe uses its own key on the same API");
        Log.Skip("transport.runmanager.json.save",
            "RunManager.ToSave / RunState.FromSerializable / the save JSON pipeline need the engine");
        Log.Skip("transport.multiplayer.bus",
            "ClientLoadJoinResponseMessage / NetMessageBus need the engine and a live session");
        Log.Skip("transport.foreign.writer.key",
            "a payload written by another mod's key cannot be constructed here; cross-mod forward compatibility stays unproven");
        return Log.Finish();
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