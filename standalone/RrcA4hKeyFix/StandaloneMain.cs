using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace AutoAnthonyRelics;

/// <summary>
/// Standalone entry point for the RelicRewardChoices + Act4Heart Sapphire Key
/// fix (see Compat.RrcTreasureKeyCompat for the full bug analysis).
///
/// This assembly provides AutoAnthonyRelics.MainFile (ModId/Logger) purely as
/// the compile-time home of the SHARED compat source; the real game mod id
/// is RrcA4hKeyFix.
/// </summary>
[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "RrcA4hKeyFix";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } =
        new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        try
        {
            Harmony harmony = new(ModId);
            // Chest-flow gate + Act4Heart presence/config checks + process-wide
            // mutex all live inside the shared source.
            Compat.RrcTreasureKeyCompat.LogPrefix = "[RrcA4hKeyFix]";
            bool installed = Compat.RrcTreasureKeyCompat.TryInstall(harmony);
            if (installed)
            {
                Logger.Info("[RrcA4hKeyFix] initialized: RelicRewardChoices + Act4Heart Sapphire Key fix active");
            }
            else
            {
                Logger.Info("[RrcA4hKeyFix] initialized dormant (mods missing, keys disabled, or AutoAnthonyRelics already carries the fix)");
            }
        }
        catch (Exception e)
        {
            Logger.Error($"[RrcA4hKeyFix] initializer failed: {e}");
            throw;
        }
    }
}
