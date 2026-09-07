using System.Collections.Generic;
using System.Linq;
using System.Text;
using AutoAnthonyRelics.Chaos;
using BaseLib.Abstracts;
using BaseLib.Patches.Content;
using MegaCrit.Sts2.Core.Models;

namespace AutoAnthonyRelics.Pools;

/// <summary>
/// Shared relic pool carrying all chaos relic slots. BaseLib registers shared
/// pools into the engine's SharedRelicPool, so RelicGrabBag.Populate(Player, Rng)
/// automatically includes our slots (rarity from ChaosRelicModel.Rarity), and
/// IsAllowed filters them to the current run's seed.
/// </summary>
public class ChaosSharedRelicPool : CustomRelicPoolModel
{
    public override bool IsShared => true;

    /// <summary>
    /// BaseLib's CustomRelicPoolModel routes here via ModHelper.ConcatModelsFromMods:
    /// return the slot-marker types (ChaosRelic000..) resolved to canonical
    /// instances, ordered by slot for stable logging.
    /// </summary>
    protected override IEnumerable<RelicModel> GenerateAllRelics()
    {
        foreach (var type in ChaosRelicRegistry.Types)
        {
            yield return ModelDb.GetById<RelicModel>(ModelDb.GetId(type));
        }
    }
}

/// <summary>
/// Slot-marker type registry (AutoAnthony ChaosCardRegistry mirror): every
/// concrete ChaosRelicModel subclass in this assembly, ordered by name.
/// </summary>
public static class ChaosRelicRegistry
{
    public const int Count = 60;

    public static IReadOnlyList<System.Type> Types { get; } = (
        from type in typeof(ChaosRelicRegistry).Assembly.GetTypes()
        where !type.IsAbstract && type.BaseType == typeof(Models.ChaosRelicModel)
        orderby type.Name
        select type).ToArray();
}
