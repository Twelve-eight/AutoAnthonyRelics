using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoAnthonyRelics.Chaos;

/// <summary>One entry (词条) on a generated relic: opcode + single amount + rendered text.</summary>
public sealed record ChaosRelicOperation(string Template, int Amount, string Text);

/// <summary>A generated relic: slot marker + rarity + name + entry list.</summary>
public sealed record ChaosRelicDefinition(int Slot, global::MegaCrit.Sts2.Core.Entities.Relics.RelicRarity Rarity,
    string Name, IReadOnlyList<ChaosRelicOperation> Operations)
{
    public ChaosRelicOperation? First(string template) =>
        Operations.FirstOrDefault(op => op.Template == template);

    public IReadOnlyList<ChaosRelicOperation> All(string template) =>
        Operations.Where(op => op.Template == template).ToArray();
}
