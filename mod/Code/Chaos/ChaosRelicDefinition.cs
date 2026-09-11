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
    /// <summary>
    /// Total amount of one template across this relic's entries.
    /// Allocation-free: the previous <c>All(template)</c> built a fresh array
    /// on every call, and the combat hooks call it per hook invocation
    /// (ModifyDamageAdditive runs for every damage instance, including
    /// card-hover previews).
    /// </summary>
    public int Sum(string template)
    {
        int total = 0;
        for (int i = 0; i < Operations.Count; i++)
        {
            var op = Operations[i];
            if (string.Equals(op.Template, template, StringComparison.Ordinal))
            {
                total += op.Amount;
            }
        }
        return total;
    }

    /// <summary>Is this template present on the relic at all?</summary>
    public bool Has(string template) => Sum(template) > 0;
}
