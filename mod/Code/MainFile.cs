using System;
using System.Reflection;
using BaseLib.Config;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace AutoAnthonyRelics;

/// <summary>
/// Mod entry point. v1 needs no Harmony patches: chaos relics reach the run
/// purely through the BaseLib shared relic pool (see ChaosSharedRelicPool).
/// </summary>
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "AutoAnthonyRelics";
    public const string ResPath = $"res://{ModId}";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        try
        {
            // Settings -> Mod Settings UI registration.
            ModConfigRegistry.Register(ModId, new AutoAnthonyRelicsConfig());

            // Godot scenes shipped in the .pck (v1: none, but register anyway -
            // costs nothing and future-proof for icon-atlas scenes).

            // Apply Harmony patches (Spire1 per-type try/catch pattern: one
            // bad patch must never abort the set).
            Harmony harmony = new(ModId);
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                {
                    continue;
                }
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (Exception e)
                {
                    Logger.Error($"Harmony patch {type.Name} failed: {e.Message}");
                }
            }

            // Touch the registry so slot-marker types resolve at startup: any
            // type-load failure surfaces in the log immediately instead of on
            // first pool generation mid-run.
            int slots = Pools.ChaosRelicRegistry.Types.Count;
            Logger.Info($"[AutoAnthonyRelics] initialized: {slots} chaos relic slots, " +
                        $"multiplier x{AutoAnthonyRelicsConfig.ChaosRelicMultiplier}, " +
                        $"enabled={AutoAnthonyRelicsConfig.EnableChaosRelics}");
        }
        catch (Exception e)
        {
            Logger.Error($"[AutoAnthonyRelics] initializer failed: {e}");
            throw;
        }
    }
}
