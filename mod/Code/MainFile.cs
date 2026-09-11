using System;
using System.Reflection;
using BaseLib.Config;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Modding;

namespace QuriousCraftingRelics;

/// <summary>
/// Mod entry point. v1 needs no Harmony patches: chaos relics reach the run
/// purely through the engine SharedRelicPool (see ChaosRelicRegistry doc).
/// </summary>
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "QuriousCraftingRelics";
    public const string ResPath = $"res://{ModId}";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        try
        {
            // MUST run before the config instance exists: the ModConfig
            // constructor chain (CheckConfigProperties -> Init -> Load) is
            // synchronous and reads the cfg file by root-namespace-derived
            // path, so a post-construction migration would be too late.
            // The migration never throws and touches no Godot API (see its
            // doc), so it cannot abort init; we log its report from here
            // because this method really does run inside Godot.
            ConfigMigrationReport migration = ConfigMigration.MigrateLegacyConfig();
            if (migration.Describe() is { } migrationLine)
            {
                if (migration.Succeeded)
                {
                    Logger.Info($"[QuriousCraftingRelics] {migrationLine}");
                }
                else
                {
                    Logger.Error($"[QuriousCraftingRelics] {migrationLine}");
                }
            }

            // Settings -> Mod Settings UI registration.
            ModConfigRegistry.Register(ModId, new QuriousCraftingRelicsConfig());

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

            // Cross-mod bugfix (RelicRewardChoices + Act4Heart Sapphire Key):
            // installs only when both mods are present and no twin instance
            // already claimed it (see RrcTreasureKeyCompat doc).
            Compat.RrcTreasureKeyCompat.TryInstall(harmony);

            // Touch the registry so slot-marker types resolve at startup: any
            // type-load failure surfaces in the log immediately instead of on
            // first pool generation mid-run.
            int slots = Pools.ChaosRelicRegistry.Types.Count;
            Logger.Info($"[QuriousCraftingRelics] initialized: {slots} chaos relic slots, " +
                        $"multiplier x{QuriousCraftingRelicsConfig.ChaosRelicMultiplier}, " +
                        $"enabled={QuriousCraftingRelicsConfig.EnableChaosRelics}");
        }
        catch (Exception e)
        {
            Logger.Error($"[QuriousCraftingRelics] initializer failed: {e}");
            throw;
        }
    }
}
