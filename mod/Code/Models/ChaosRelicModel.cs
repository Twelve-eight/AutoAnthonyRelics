using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AutoAnthonyRelics.Chaos;
using AutoAnthonyRelics.Extensions;
using BaseLib.Abstracts;
using BaseLib.Extensions;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Commands;

namespace AutoAnthonyRelics.Models;

/// <summary>
/// A generated chaos relic. Slot-marker subclasses (ChaosRelic000...) point at
/// their slot; the definition (rarity, name, entries) is regenerated per run
/// seed, exactly like AutoAnthony's ChaosCardModel resolves Definition/Card.
///
/// Entry execution: every catalog template binds to exactly one hook here;
/// hooks iterate their Operations and execute via engine commands.
/// </summary>
[Pool(typeof(Pools.ChaosSharedRelicPool))]
public abstract class ChaosRelicModel : CustomRelicModel
{
    protected abstract int Slot { get; }

    public override bool IsAllowed(IRunState runState)
    {
        // Never allow at Neow / ancient pools: the run seed may not exist yet
        // there, and ancient (先古之民) relics must stay vanilla per user order.
        // Chaos relics live ONLY in the Common/Uncommon/Rare reward deques.
        return base.IsAllowed(runState)
            && AutoAnthonyRelicsConfig.EnableChaosRelics
            && ChaosRelicRunRegistry.IsSlotAllowedInRun(Slot, runState);
    }

    /// <summary>
    /// Chaos relics never appear at Neow (NeowsBones-style pools query this;
    /// the run seed rarely exists at character-select time, and ancient/
    /// Neow rewards must stay vanilla per user order 2026-09-07).
    /// </summary>
    public override bool IsAllowedAtNeow(Player player)
    {
        return false;
    }

    protected ChaosRelicDefinition? Definition =>
        ChaosRelicRunRegistry.DefinitionFor(this, Slot);

    public override RelicRarity Rarity => Definition?.Rarity ?? RelicRarity.Common;

    // ---------- Dynamic description (per-run entry list) ----------

    /// <summary>
    /// BaseLib ILocalizationProvider: ModelLocPatch copies these entries into
    /// the "relics" loc table at ModelDb init, overriding the static JSON
    /// fallback. The canonical (menu/preview) instance has no run yet, so it
    /// shows a generic line; in a run the definition's entry texts are joined
    /// into the description. Rendered fresh on table rebuild (language switch,
    /// save load), and the tooltip reads the table - good enough for v0.2.
    public override List<(string, string)>? Localization
    {
        get
        {
            var definition = Definition;
            if (definition is null)
            {
                return new RelicLoc(
                    "Chaos Relic " + (Slot + 1),
                    "A relic randomly generated for this run. The counter shows its entry count.",
                    "It seems different every run.");
            }
            string description = string.Join("\n", definition.Operations.Select(op => op.Text));
            return new RelicLoc(definition.Name, description, "It seems different every run.");
        }
    }

    public override bool ShowCounter => true;


    // ---------- Icons (Spire1Relic pattern; placeholder art per slot) ----------

    public override string PackedIconPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".RelicImagePath();

    protected override string PackedIconOutlinePath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}_outline.png".RelicImagePath();

    protected override string BigIconPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".BigRelicImagePath();

    // ---------- Combat-start hooks ----------

    public override async Task BeforeCombatStart()
    {
        var definition = Definition;
        if (definition is null || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return;
        }
        var owner = Owner;
        if (owner is null)
        {
            return;
        }
        bool flashed = false;
        foreach (var op in definition.All(ChaosRelicCatalog.StartDamageAll))
        {
            if (!flashed) { Flash(); flashed = true; }
            await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), owner.Creature.CombatState?.HittableEnemies ?? Array.Empty<Creature>(),
                op.Amount, ValueProp.Unpowered, owner.Creature, null, null);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.StartBlock))
        {
            if (!flashed) { Flash(); flashed = true; }
            await CreatureCmd.GainBlock(owner.Creature, op.Amount, ValueProp.Unpowered, null);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.StartStrength))
        {
            if (!flashed) { Flash(); flashed = true; }
            await PowerCmd.Apply<StrengthPower>(new ThrowingPlayerChoiceContext(), owner.Creature,
                op.Amount, owner.Creature, null);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.StartDexterity))
        {
            if (!flashed) { Flash(); flashed = true; }
            await PowerCmd.Apply<DexterityPower>(new ThrowingPlayerChoiceContext(), owner.Creature,
                op.Amount, owner.Creature, null);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.StartVulnAll))
        {
            if (!flashed) { Flash(); flashed = true; }
            await PowerCmd.Apply<VulnerablePower>(new ThrowingPlayerChoiceContext(),
                owner.Creature.CombatState?.HittableEnemies ?? Array.Empty<MegaCrit.Sts2.Core.Entities.Creatures.Creature>(), op.Amount, owner.Creature, null);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.StartWeakAll))
        {
            if (!flashed) { Flash(); flashed = true; }
            await PowerCmd.Apply<WeakPower>(new ThrowingPlayerChoiceContext(),
                owner.Creature.CombatState?.HittableEnemies ?? Array.Empty<MegaCrit.Sts2.Core.Entities.Creatures.Creature>(), op.Amount, owner.Creature, null);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.StartDraw))
        {
            if (!flashed) { Flash(); flashed = true; }
            await CardPileCmd.Draw(new ThrowingPlayerChoiceContext(), op.Amount, owner);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.StartEnergy))
        {
            if (!flashed) { Flash(); flashed = true; }
            await PlayerCmd.GainEnergy(op.Amount, owner);
        }
    }

    // ---------- Turn-start hooks ----------

    public override async Task AfterPlayerTurnStartLate(PlayerChoiceContext choiceContext, Player player)
    {
        var definition = Definition;
        if (definition is null || player != Owner || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return;
        }
        foreach (var op in definition.All(ChaosRelicCatalog.TurnStartBlock))
        {
            Flash();
            await CreatureCmd.GainBlock(player.Creature, op.Amount, ValueProp.Unpowered, null);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.TurnStartEnergy))
        {
            Flash();
            await PlayerCmd.GainEnergy(op.Amount, player);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.TurnStartHeal))
        {
            Flash();
            await CreatureCmd.Heal(player.Creature, op.Amount);
        }
    }

    // ---------- Card-play hooks ----------

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var definition = Definition;
        var owner = Owner;
        if (definition is null || owner is null || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return;
        }
        foreach (var op in definition.All(ChaosRelicCatalog.PlayDamageRandom))
        {
            var enemies = owner.Creature.CombatState?.HittableEnemies ?? Array.Empty<MegaCrit.Sts2.Core.Entities.Creatures.Creature>();
            if (enemies.Count == 0)
            {
                continue;
            }
            Flash();
            var target = enemies[new Random().Next(enemies.Count)];
            await CreatureCmd.Damage(choiceContext, target, op.Amount, ValueProp.Unpowered, owner.Creature, null, null);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.PlayBlock))
        {
            Flash();
            await CreatureCmd.GainBlock(owner.Creature, op.Amount, ValueProp.Unpowered, null);
        }
    }

    // ---------- Passive hooks ----------

    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        var definition = Definition;
        if (definition is null || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return 0m;
        }
        var owner = Owner;
        // Player-attacks only: dealer is the player, target is an enemy.
        if (owner is null || dealer != owner.Creature || target == owner.Creature)
        {
            return 0m;
        }
        return definition.All(ChaosRelicCatalog.PassiveAttackDamage).Sum(op => op.Amount);
    }

    public override decimal ModifyMaxEnergy(Player player, decimal amount)
    {
        var definition = Definition;
        if (definition is null || player != Owner || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return amount;
        }
        return amount + definition.All(ChaosRelicCatalog.PassiveMaxEnergy).Sum(op => op.Amount);
    }

    // ---------- Victory hooks ----------

    public override async Task AfterCombatVictory(CombatRoom room)
    {
        var definition = Definition;
        var owner = Owner;
        if (definition is null || owner is null || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return;
        }
        foreach (var op in definition.All(ChaosRelicCatalog.VictoryHeal))
        {
            Flash();
            await CreatureCmd.Heal(owner.Creature, op.Amount);
        }
    }
}
