using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.RelicPools;

namespace AutoAnthonyRelics.Pools;

/// <summary>
/// Slot-marker type registry: every concrete ChaosRelicModel subclass in this
/// assembly, ordered by name. The [Pool(typeof(SharedRelicPool))] attribute on
/// ChaosRelicModel routes each slot into the ENGINE SharedRelicPool via
/// ModHelper.AddModelToPool -> ConcatModelsFromMods, which is the only pool the
/// reward path queries (RunManager.InitializeNewRun -> SharedGrabBag.Populate(
/// ModelDb.RelicPool&lt;SharedRelicPool&gt;().GetUnlockedRelics(...))). The former
/// custom ChaosSharedRelicPool (BaseLib IsShared registration) only appended to
/// ModelDb.AllSharedRelicPools - the compendium list - and never reached the
/// reward deques; removed in v0.3 after live testing showed zero chaos relics
/// in rewards (user report 2026-09-07, run seed AZ49CAAUZK0F).
/// </summary>
public static class ChaosRelicRegistry
{
    public const int Count = 60;

    public static IReadOnlyList<Type> Types { get; } = (
        from type in typeof(ChaosRelicRegistry).Assembly.GetTypes()
        where !type.IsAbstract && type.BaseType == typeof(Models.ChaosRelicModel)
        orderby type.Name
        select type).ToArray();
}
