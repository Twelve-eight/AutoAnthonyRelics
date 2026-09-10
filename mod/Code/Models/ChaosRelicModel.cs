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
using MegaCrit.Sts2.Core.Models.Enchantments;
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
[Pool(typeof(MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool))]
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
        foreach (var op in definition.All(ChaosRelicCatalog.StartRegen))
        {
            if (!flashed) { Flash(); flashed = true; }
            await PowerCmd.Apply<RegenPower>(new ThrowingPlayerChoiceContext(), owner.Creature,
                op.Amount, owner.Creature, null);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.StartThorns))
        {
            if (!flashed) { Flash(); flashed = true; }
            await PowerCmd.Apply<ThornsPower>(new ThrowingPlayerChoiceContext(), owner.Creature,
                op.Amount, owner.Creature, null);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.StartArtifact))
        {
            if (!flashed) { Flash(); flashed = true; }
            await PowerCmd.Apply<ArtifactPower>(new ThrowingPlayerChoiceContext(), owner.Creature,
                op.Amount, owner.Creature, null);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.StartPoisonAll))
        {
            if (!flashed) { Flash(); flashed = true; }
            await PowerCmd.Apply<PoisonPower>(new ThrowingPlayerChoiceContext(),
                owner.Creature.CombatState?.HittableEnemies ?? Array.Empty<MegaCrit.Sts2.Core.Entities.Creatures.Creature>(), op.Amount, owner.Creature, null);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.StartPlating))
        {
            if (!flashed) { Flash(); flashed = true; }
            await PowerCmd.Apply<PlatingPower>(new ThrowingPlayerChoiceContext(), owner.Creature,
                op.Amount, owner.Creature, null);
        }
        // Negatives: self-applied debuffs at combat start.
        // (Sloth is NOT a power here anymore: the VelvetChoker-style
        // card-cap lives in ShouldPlay below.)
        foreach (var op in definition.All(ChaosRelicCatalog.NegStartFrail))
        {
            if (!flashed) { Flash(); flashed = true; }
            await PowerCmd.Apply<FrailPower>(new ThrowingPlayerChoiceContext(), owner.Creature,
                op.Amount, owner.Creature, null);
        }
        // Extra pool: one-shot hand enchants (opt-in pool).
        if (ExtraPoolActive)
        {
            RunExtraCombatStartEnchants(owner);
        }
    }

    // ---------- Sloth (VelvetChoker-style card cap; user order 2026-09-11) ----------

    private int _slothCardsPlayedThisTurn;

    /// <summary>Max cards playable per turn from all sloth entries: 7 - sum(N).</summary>
    private int SlothCardCap
    {
        get
        {
            var definition = Definition;
            if (definition is null)
            {
                return int.MaxValue;
            }
            int n = definition.All(ChaosRelicCatalog.NegStartSloth).Sum(op => op.Amount);
            return n <= 0 ? int.MaxValue : Math.Max(1, 7 - n);
        }
    }

    public override bool ShouldPlay(CardModel card, AutoPlayType _)
    {
        var definition = Definition;
        var owner = Owner;
        if (definition is null || owner is null || card.Owner != owner
            || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return true;
        }
        return _slothCardsPlayedThisTurn < SlothCardCap;
    }

    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        _slothCardsPlayedThisTurn = 0;
        return Task.CompletedTask;
    }

    public override Task AfterRoomEntered(AbstractRoom room)
    {
        _slothCardsPlayedThisTurn = 0;
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        _slothCardsPlayedThisTurn = 0;
        return Task.CompletedTask;
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
        foreach (var op in definition.All(ChaosRelicCatalog.TurnStartDraw))
        {
            Flash();
            await CardPileCmd.Draw(choiceContext, op.Amount, player);
        }
        foreach (var op in definition.All(ChaosRelicCatalog.NegTurnLoseHp))
        {
            Flash();
            await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), player.Creature,
                op.Amount, ValueProp.Unpowered, player.Creature, null, null);
        }
        // Extra pool: per-turn hand keywords + stance entry (opt-in pool).
        if (ExtraPoolActive)
        {
            await RunExtraTurnStart(player);
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
        // Sloth cap counter (velvet-choker style; only when the relic has one).
        if (SlothCardCap != int.MaxValue && cardPlay.Card.Owner == owner)
        {
            _slothCardsPlayedThisTurn++;
        }
        foreach (var op in definition.All(ChaosRelicCatalog.PlayDamageRandom))
        {
            var enemies = owner.Creature.CombatState?.HittableEnemies ?? Array.Empty<MegaCrit.Sts2.Core.Entities.Creatures.Creature>();
            if (enemies.Count == 0)
            {
                continue;
            }
            Flash();
            var target = enemies[owner.RunState.Rng.CombatTargets.NextInt(enemies.Count)];
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
        if (owner is null || dealer != owner.Creature || target == owner.Creature)
        {
            return 0m;
        }
        int bonus = definition.All(ChaosRelicCatalog.PassiveAttackDamage).Sum(op => op.Amount);
        int malus = definition.All(ChaosRelicCatalog.NegAttackDamageDown).Sum(op => op.Amount);
        return bonus - malus + ConsumeRetainAttackBuff(cardSource);
    }

    public override decimal ModifyMaxEnergy(Player player, decimal amount)
    {
        var definition = Definition;
        if (definition is null || player != Owner || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return amount;
        }
        return Math.Max(0m, amount
            + definition.All(ChaosRelicCatalog.PassiveMaxEnergy).Sum(op => op.Amount)
            - definition.All(ChaosRelicCatalog.NegTurnEnergyDown).Sum(op => op.Amount));
    }

    public override decimal ModifyHandDraw(Player player, decimal count)
    {
        var definition = Definition;
        if (definition is null || player != Owner || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return count;
        }
        // Floor 1: a 0-card hand would brick the run; NoDraw semantics.
        return Math.Max(1m, count - definition.All(ChaosRelicCatalog.NegTurnDrawDown).Sum(op => op.Amount));
    }

    public override decimal ModifyGoldGained(Player player, decimal amount)
    {
        var definition = Definition;
        if (definition is null || player != Owner || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return amount;
        }
        return amount
            + definition.All(ChaosRelicCatalog.PassiveGoldGain).Sum(op => op.Amount)
            - definition.All(ChaosRelicCatalog.NegGoldDown).Sum(op => op.Amount);
    }

    public override decimal ModifyRestSiteHealAmount(Creature creature, decimal amount)
    {
        var definition = Definition;
        var owner = Owner;
        if (definition is null || owner is null || creature != owner.Creature
            || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return amount;
        }
        return amount
            + definition.All(ChaosRelicCatalog.RestHealBonus).Sum(op => op.Amount)
            - definition.All(ChaosRelicCatalog.NegRestHealDown).Sum(op => op.Amount);
    }

    public override bool ShouldProcurePotion(PotionModel potion, Player player)
    {
        var definition = Definition;
        if (definition is null || player != Owner || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return true;
        }
        // Negative: cannot acquire potions at all.
        return definition.All(ChaosRelicCatalog.NegPotionBlock).Count == 0;
    }

    public override decimal ModifyBlockAdditive(Creature? target, decimal block, ValueProp props,
        CardModel? cardSource, CardPlay? cardPlay)
    {
        var definition = Definition;
        var owner = Owner;
        if (definition is null || owner is null || target != owner.Creature
            || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return 0m;
        }
        return definition.All(ChaosRelicCatalog.PassiveBlockAdd).Sum(op => op.Amount);
    }

    // ---------- Obtain hook (one-shot negatives) ----------

    public override async Task AfterObtained()
    {
        var definition = Definition;
        var owner = Owner;
        if (definition is null || owner is null || !AutoAnthonyRelicsConfig.EnableChaosRelics)
        {
            return;
        }
        foreach (var op in definition.All(ChaosRelicCatalog.NegMaxHpDown))
        {
            Flash();
            await CreatureCmd.LoseMaxHp(new ThrowingPlayerChoiceContext(), owner.Creature,
                op.Amount, isFromCard: false);
        }
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
        foreach (var op in definition.All(ChaosRelicCatalog.VictoryGold))
        {
            Flash();
            await PlayerCmd.GainGold(op.Amount, owner);
        }
    }

    // ======================================================================
    // EXTRA pool execution (card keywords / enchants / watcher stances).
    // All gated on EnableExtraPool; stance entries also require the Watcher
    // mod (reflection; no compile-time dependency).
    // ======================================================================

    private static bool ExtraPoolActive =>
        AutoAnthonyRelicsConfig.EnableChaosRelics && AutoAnthonyRelicsConfig.EnableExtraPool;

    /// <summary>First N hand cards (combat RNG; deterministic both ends).</summary>
    private static IReadOnlyList<CardModel> TakeFirstN(Player owner, int n)
    {
        var hand = owner.PlayerCombatState?.Hand.Cards;
        if (hand is null || hand.Count == 0)
        {
            return Array.Empty<CardModel>();
        }
        return hand.Take(n).ToList();
    }

    /// <summary>Combat-start: enchants on hand cards (one-shot).</summary>
    private void RunExtraCombatStartEnchants(Player owner)
    {
        foreach (var op in Definition?.All(ChaosRelicExtraCatalog.EnchantSharp) ?? Array.Empty<ChaosRelicOperation>())
        {
            foreach (var card in TakeFirstN(owner, op.Amount).Where(c => c.Type == CardType.Attack))
            {
                Flash();
                CardCmd.Enchant<MegaCrit.Sts2.Core.Models.Enchantments.Sharp>(card, 1);
            }
        }
        foreach (var op in Definition?.All(ChaosRelicExtraCatalog.EnchantNimble) ?? Array.Empty<ChaosRelicOperation>())
        {
            foreach (var card in TakeFirstN(owner, op.Amount))
            {
                Flash();
                CardCmd.Enchant<MegaCrit.Sts2.Core.Models.Enchantments.Nimble>(card, 1);
            }
        }
        foreach (var op in Definition?.All(ChaosRelicExtraCatalog.EnchantImbued) ?? Array.Empty<ChaosRelicOperation>())
        {
            foreach (var card in TakeFirstN(owner, op.Amount))
            {
                Flash();
                CardCmd.Enchant<MegaCrit.Sts2.Core.Models.Enchantments.Imbued>(card, 1);
            }
        }
    }

    /// <summary>Per-turn: hand keywords + stance entry (re-applied each turn).</summary>
    private async Task RunExtraTurnStart(Player player)
    {
        var definition = Definition;
        if (definition is null)
        {
            return;
        }
        foreach (var op in definition.All(ChaosRelicExtraCatalog.HandRetain))
        {
            foreach (var card in TakeFirstN(player, op.Amount))
            {
                Flash();
                card.GiveSingleTurnRetain();
            }
        }
        foreach (var op in definition.All(ChaosRelicExtraCatalog.HandSly))
        {
            foreach (var card in TakeFirstN(player, op.Amount))
            {
                Flash();
                card.GiveSingleTurnSly();
            }
        }
        foreach (var op in definition.All(ChaosRelicExtraCatalog.NegHandEthereal))
        {
            foreach (var card in TakeFirstN(player, op.Amount))
            {
                Flash();
                card.AddKeyword(CardKeyword.Ethereal);
            }
        }
        await RunStanceEntry(player, ChaosRelicExtraCatalog.StanceWrathStart, "EnterWrath");
        await RunStanceEntry(player, ChaosRelicExtraCatalog.StanceCalmStart, "EnterCalm");
        await RunStanceEntry(player, ChaosRelicExtraCatalog.StanceDivinityStart, "EnterDivinity");
    }

    /// <summary>Watcher-mod stance entry via reflection (skipped when absent).</summary>
    private async Task RunStanceEntry(Player player, string template, string helperMethod)
    {
        var definition = Definition;
        if (definition is null || definition.All(template).Count == 0)
        {
            return;
        }
        var watcher = System.AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Watcher");
        var helper = watcher?.GetType("WatcherMod.WatcherCombatHelper");
        var method = helper?.GetMethod(helperMethod,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        if (method is null)
        {
            return; // Watcher absent: template was filtered at generation too.
        }
        Flash();
        var task = method.Invoke(null, new object?[] { player, null }) as Task;
        if (task is not null)
        {
            await task;
        }
    }

    /// <summary>Retain-triggered effects (called from AfterCardChangedPiles).</summary>
    private void RunExtraRetainTriggers(Player owner, CardModel card)
    {
        var definition = Definition;
        if (definition is null)
        {
            return;
        }
        foreach (var op in definition.All(ChaosRelicExtraCatalog.RetainEnergyDiscount))
        {
            Flash();
            // Relative modifier, until played, reduce-only via negative amount.
            card.EnergyCost.AddUntilPlayed(-op.Amount, reduceOnly: true);
        }
        foreach (var op in definition.All(ChaosRelicExtraCatalog.RetainAttackBuff))
        {
            Flash();
            _retainAttackBuff += op.Amount;
        }
    }

    private int _retainAttackBuff;

    /// <summary>Retain detection: a card staying in hand at end of turn.</summary>
    public override Task AfterCardChangedPiles(CardModel card, PileType oldPile, AbstractModel? clonedBy)
    {
        if (ExtraPoolActive && oldPile == PileType.Hand
            && card.Pile?.Type == PileType.Hand && card.Owner == Owner
            && card.ShouldRetainThisTurn)
        {
            RunExtraRetainTriggers(Owner!, card);
        }
        return Task.CompletedTask;
    }
    /// <summary>
    /// Retained-attack buff (extra pool): next attack this turn gains the
    /// stacked bonus, then resets (StS "next attack" semantics).
    /// </summary>
    private int ConsumeRetainAttackBuff(CardModel? cardSource)
    {
        if (!ExtraPoolActive || _retainAttackBuff <= 0 || cardSource is null
            || cardSource.Type != CardType.Attack)
        {
            return 0;
        }
        int bonus = _retainAttackBuff;
        _retainAttackBuff = 0;
        return bonus;
    }
}
