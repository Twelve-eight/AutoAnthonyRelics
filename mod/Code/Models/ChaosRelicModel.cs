using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using QuriousCraftingRelics.Chaos;
using QuriousCraftingRelics.Extensions;
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
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Commands;

namespace QuriousCraftingRelics.Models;

/// <summary>
/// A generated chaos relic. Slot-marker subclasses (ChaosRelic000..) point at
/// their slot; the definition (rarity, name, entries) is regenerated per run
/// seed, exactly like AutoAnthony's ChaosCardModel resolves Definition/Card.
///
/// Entry execution: every catalog template binds to exactly one hook here;
/// hooks iterate their Operations and execute via engine commands.
///
/// TIMING AND OWNERSHIP CONTRACT (F03/F07, verified against the engine
/// decompile under sts2-spire1/research/engine-dllsrc on 2026-09-12):
/// - Combat-start ENERGY cannot be granted from BeforeCombatStart:
///   SetupPlayerTurn calls PlayerCombatState.ResetEnergy() right before the
///   opening draw, which overwrites whatever was added. Vanilla's Lantern
///   grants it from AfterSideTurnStart gated on TurnNumber &lt;= 1, and that is
///   the pattern used here.
/// - Combat-start DRAW is a ModifyHandDraw bonus gated on turn 1 (vanilla's
///   BagOfPreparation / RingOfTheSnake), not a separate draw call, so it
///   cannot be swallowed by Fiddle-style draw prevention.
/// - Combat-start BLOCK stays in BeforeCombatStart (vanilla Anchor): block is
///   not cleared on turn 1 (Creature.AfterTurnStart returns early while
///   PlayerCombatState.TurnNumber == 1).
/// - Every hook that fires for ALL listeners must self-gate on owner: the
///   engine broadcasts AfterCardPlayed / AfterFlush / ModifyDamageAdditive to
///   every listener with no owner filter.
/// - ModifyDamageAdditive is a pure READ. It is called during card-hover
///   previews as well as real damage, so consuming state there (as the retain
///   attack buff used to) means a preview eats the buff. Consumption happens
///   in AfterAttack instead, mirroring vanilla VigorPower.
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
            && QuriousCraftingRelicsConfig.EnableChaosRelics
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
    /// save load), and the tooltip reads the table.
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

    /// <summary>
    /// Hover the RELIC -> also show tooltips for every power it grants
    /// (vanilla Akabeko pattern, user request 2026-09-13): players should not
    /// have to guess what Vigor/Thorns/Artifact do from the entry text alone.
    /// Only powers the definition actually carries get a tip.
    /// </summary>
    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            var definition = Definition;
            if (definition is null)
            {
                yield break;
            }
            foreach (var (template, powerType) in PowerTipMap)
            {
                if (definition.Sum(template) > 0)
                {
                    var tip = typeof(HoverTipFactory)
                        .GetMethod(nameof(HoverTipFactory.FromPower))
                        ?.MakeGenericMethod(powerType)
                        .Invoke(null, null) as IHoverTip;
                    if (tip is not null)
                    {
                        yield return tip;
                    }
                }
            }
        }
    }

    private static readonly (string Template, Type PowerType)[] PowerTipMap =
    {
        (ChaosRelicCatalog.StartStrength, typeof(StrengthPower)),
        (ChaosRelicCatalog.StartDexterity, typeof(DexterityPower)),
        (ChaosRelicCatalog.StartRegen, typeof(RegenPower)),
        (ChaosRelicCatalog.StartThorns, typeof(ThornsPower)),
        (ChaosRelicCatalog.StartArtifact, typeof(ArtifactPower)),
        (ChaosRelicCatalog.StartPoisonAll, typeof(PoisonPower)),
        (ChaosRelicCatalog.StartPlating, typeof(PlatingPower)),
        (ChaosRelicCatalog.StartVulnAll, typeof(VulnerablePower)),
        (ChaosRelicCatalog.StartWeakAll, typeof(WeakPower)),
    };

    // ---------- Counter (entry count) ----------

    /// <summary>
    /// The relic icon badge shows how many entries this relic rolled. Without
    /// an override the engine's RelicModel.DisplayAmount is 0, so the badge
    /// the description promises rendered as a literal zero (probe 2026-09-12:
    /// show=true, shown=0, entries=2).
    ///
    /// Gated on Definition rather than on combat: outside a run the canonical
    /// instance has no definition, so there is nothing to count and no badge
    /// appears. Inside a run the count is meaningful in every room, not only
    /// mid-combat.
    /// </summary>
    public override bool ShowCounter => Definition is not null;

    public override int DisplayAmount => Definition?.Operations.Count ?? 0;

    // ---------- Icons (Spire1Relic pattern; placeholder art per slot) ----------

    public override string PackedIconPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".RelicImagePath();

    protected override string PackedIconOutlinePath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}_outline.png".RelicImagePath();

    protected override string BigIconPath => $"{Id.Entry.RemovePrefix().ToLowerInvariant()}.png".BigRelicImagePath();

    // ---------- Per-slot amount lookup ----------

    /// <summary>
    /// Total amount of one template on this relic. Sums the operations without
    /// allocating: the previous `definition.All(template)` built a fresh array
    /// on every hook call, and ModifyDamageAdditive runs for every damage
    /// instance including card-hover previews.
    /// </summary>
    private int AmountOf(string template) => Definition?.Sum(template) ?? 0;

    /// <summary>Combat-start one-shots that must NOT be granted from BeforeCombatStart.</summary>
    private int _startEnergyPending;

    // ---------- Enemy-debuff merge (one Apply per power per combat) ----------
    // Static because the merge spans MULTIPLE relic instances: each instance
    // accumulates its amounts during BeforeCombatStart, and the first
    // owner-side turn start flushes them as single applications. Otherwise
    // separate low-count applications each burn one enemy Artifact charge
    // (user order 2026-09-13). Cleared on flush and on combat end.
    private static readonly object DebuffGate = new();
    private static readonly Dictionary<Type, int> PendingEnemyDebuffs = new();

    private void AccumulateEnemyDebuff<T>(int amount) where T : PowerModel
    {
        if (amount <= 0)
        {
            return;
        }
        lock (DebuffGate)
        {
            PendingEnemyDebuffs[typeof(T)] = PendingEnemyDebuffs.GetValueOrDefault(typeof(T)) + amount;
        }
    }

    private static async Task FlushEnemyDebuffs(Player owner, PlayerChoiceContext context)
    {
        KeyValuePair<Type, int>[] pending;
        lock (DebuffGate)
        {
            if (PendingEnemyDebuffs.Count == 0)
            {
                return;
            }
            pending = PendingEnemyDebuffs.ToArray();
            PendingEnemyDebuffs.Clear();
        }
        var enemies = owner.Creature.CombatState?.HittableEnemies ?? Array.Empty<Creature>();
        if (enemies.Count == 0)
        {
            return;
        }
        foreach (var entry in pending)
        {
            // PowerCmd.Apply is generic over the power type; the type is only
            // known at runtime here, so close it via reflection (the overload
            // with IEnumerable<Creature> targets).
            var closed = s_applyTargets.MakeGenericMethod(entry.Key);
            var task = (Task)closed.Invoke(null,
                new object?[] { context, enemies, (decimal)entry.Value, owner.Creature, null, false })!;
            await task;
        }
    }

    private static readonly MethodInfo s_applyTargets =
        typeof(PowerCmd).GetMethods()
            .Single(m => m.Name == nameof(PowerCmd.Apply)
                && m.IsGenericMethod
                && m.GetParameters()[1].ParameterType == typeof(IEnumerable<Creature>));

    // ---------- Combat-start hooks ----------

    public override async Task BeforeCombatStart()
    {
        var definition = Definition;
        if (definition is null || !QuriousCraftingRelicsConfig.EnableChaosRelics)
        {
            return;
        }
        var owner = Owner;
        if (owner is null)
        {
            return;
        }
        var context = new ThrowingPlayerChoiceContext();

        int damageAll = AmountOf(ChaosRelicCatalog.StartDamageAll);
        if (damageAll > 0)
        {
            Flash();
            await CreatureCmd.Damage(context,
                owner.Creature.CombatState?.HittableEnemies ?? Array.Empty<Creature>(),
                damageAll, ValueProp.Unpowered, owner.Creature, null, null);
        }
        int block = AmountOf(ChaosRelicCatalog.StartBlock);
        if (block > 0)
        {
            Flash();
            await CreatureCmd.GainBlock(owner.Creature, block, ValueProp.Unpowered, null);
        }
        int strength = AmountOf(ChaosRelicCatalog.StartStrength);
        if (strength > 0)
        {
            Flash();
            await PowerCmd.Apply<StrengthPower>(context, owner.Creature, strength, owner.Creature, null);
        }
        int dexterity = AmountOf(ChaosRelicCatalog.StartDexterity);
        if (dexterity > 0)
        {
            Flash();
            await PowerCmd.Apply<DexterityPower>(context, owner.Creature, dexterity, owner.Creature, null);
        }
        AccumulateEnemyDebuff<VulnerablePower>(AmountOf(ChaosRelicCatalog.StartVulnAll));
        AccumulateEnemyDebuff<WeakPower>(AmountOf(ChaosRelicCatalog.StartWeakAll));
        int regen = AmountOf(ChaosRelicCatalog.StartRegen);
        if (regen > 0)
        {
            Flash();
            await PowerCmd.Apply<RegenPower>(context, owner.Creature, regen, owner.Creature, null);
        }
        int thorns = AmountOf(ChaosRelicCatalog.StartThorns);
        if (thorns > 0)
        {
            Flash();
            await PowerCmd.Apply<ThornsPower>(context, owner.Creature, thorns, owner.Creature, null);
        }
        int artifact = AmountOf(ChaosRelicCatalog.StartArtifact);
        if (artifact > 0)
        {
            Flash();
            await PowerCmd.Apply<ArtifactPower>(context, owner.Creature, artifact, owner.Creature, null);
        }
        AccumulateEnemyDebuff<PoisonPower>(AmountOf(ChaosRelicCatalog.StartPoisonAll));
        int plating = AmountOf(ChaosRelicCatalog.StartPlating);
        if (plating > 0)
        {
            Flash();
            await PowerCmd.Apply<PlatingPower>(context, owner.Creature, plating, owner.Creature, null);
        }
        int frail = AmountOf(ChaosRelicCatalog.NegStartFrail);
        if (frail > 0)
        {
            Flash();
            await PowerCmd.Apply<FrailPower>(context, owner.Creature, frail, owner.Creature, null);
        }

        // Combat-start energy is DEFERRED to AfterSideTurnStart: ResetEnergy
        // runs in SetupPlayerTurn, between this hook and the player's turn
        // start, and would discard anything granted here.
        _startEnergyPending = AmountOf(ChaosRelicCatalog.StartEnergy);
    }

    // ---------- Sloth (VelvetChoker-style card cap; user order 2026-09-11) ----------

    private int _slothCardsPlayedThisTurn;

    /// <summary>Max cards playable per turn from all sloth entries: 7 - sum(N).</summary>
    private int SlothCardCap
    {
        get
        {
            int n = AmountOf(ChaosRelicCatalog.NegStartSloth);
            return n <= 0 ? int.MaxValue : Math.Max(1, 7 - n);
        }
    }

    public override bool ShouldPlay(CardModel card, AutoPlayType _)
    {
        var owner = Owner;
        if (owner is null || card.Owner != owner || !QuriousCraftingRelicsConfig.EnableChaosRelics)
        {
            return true;
        }
        return _slothCardsPlayedThisTurn < SlothCardCap;
    }

    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        // Owner gate: this hook fires for both sides and for every listener.
        if (Owner is not null && participants.Contains(Owner.Creature))
        {
            _slothCardsPlayedThisTurn = 0;
        }
        return Task.CompletedTask;
    }

    public override Task AfterRoomEntered(AbstractRoom room)
    {
        if (room is CombatRoom)
        {
            _slothCardsPlayedThisTurn = 0;
        }
        return Task.CompletedTask;
    }

    public override Task AfterCombatEnd(CombatRoom room)
    {
        _slothCardsPlayedThisTurn = 0;
        // Combat-scoped extra-pool state must not leak into the next fight.
        // The probe found the retain attack buff surviving combat end with a
        // value of 4 still attached to the relic instance.
        _retainAttackBuff = 0;
        _startEnergyPending = 0;
        return Task.CompletedTask;
    }

    // ---------- Turn-start hooks ----------

    /// <summary>
    /// Vanilla Lantern pattern: the player's turn start is the only point at
    /// which granted energy survives (ResetEnergy already ran).
    /// </summary>
    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants,
        ICombatState combatState)
    {
        var owner = Owner;
        if (owner is null || !QuriousCraftingRelicsConfig.EnableChaosRelics
            || !participants.Contains(owner.Creature))
        {
            return;
        }
        if (owner.PlayerCombatState?.TurnNumber <= 1)
        {
            await FlushEnemyDebuffs(owner, new ThrowingPlayerChoiceContext());
        }
        int pending = _startEnergyPending;
        if (pending > 0 && owner.PlayerCombatState?.TurnNumber <= 1)
        {
            _startEnergyPending = 0;
            Flash();
            await PlayerCmd.GainEnergy(pending, owner);
        }
    }

    public override async Task AfterPlayerTurnStartLate(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner || !QuriousCraftingRelicsConfig.EnableChaosRelics)
        {
            return;
        }
        var definition = Definition;
        if (definition is null)
        {
            return;
        }

        // Extra pool: combat-start enchants run HERE, not in BeforeCombatStart.
        // BeforeCombatStart fires before the opening draw, so the hand is empty
        // and every enchant loop iterated zero cards.
        if (ExtraPoolActive && player.PlayerCombatState?.TurnNumber <= 1)
        {
            RunExtraCombatStartEnchants(player);
        }

        int energy = AmountOf(ChaosRelicCatalog.TurnStartEnergy);
        if (energy > 0)
        {
            Flash();
            await PlayerCmd.GainEnergy(energy, player);
        }
        int heal = AmountOf(ChaosRelicCatalog.TurnStartHeal);
        if (heal > 0)
        {
            Flash();
            await CreatureCmd.Heal(player.Creature, heal);
        }
        int draw = AmountOf(ChaosRelicCatalog.TurnStartDraw);
        if (draw > 0)
        {
            Flash();
            await CardPileCmd.Draw(choiceContext, draw, player);
        }
        int loseHp = AmountOf(ChaosRelicCatalog.NegTurnLoseHp);
        if (loseHp > 0)
        {
            Flash();
            // Unblockable, mirroring the engine's own "HP loss like Poison"
            // convention (CreatureCmd self-damage uses Unblockable |
            // Unpowered). With Unpowered alone the loss was fully absorbed by
            // block (probe: blocked=3, hpLost=0) - a negative entry that did
            // nothing whenever the player held block.
            await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), player.Creature,
                loseHp, ValueProp.Unblockable | ValueProp.Unpowered, player.Creature, null, null);
        }

        // Extra pool: per-turn hand keywords + stance entry (opt-in pool).
        if (ExtraPoolActive)
        {
            await RunExtraTurnStart(player);
        }
    }

    /// <summary>
    /// Per-turn BLOCK is granted at the player's TURN END (user order
    /// 2026-09-13; was turn start). Vanilla CloakClasp pattern: BeforeSideTurnEnd
    /// with the owner-participant gate; block persists through the enemy turn.
    /// </summary>
    public override Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
        IEnumerable<Creature> participants)
    {
        var owner = Owner;
        if (owner is null || !QuriousCraftingRelicsConfig.EnableChaosRelics
            || !participants.Contains(owner.Creature))
        {
            return Task.CompletedTask;
        }
        int block = AmountOf(ChaosRelicCatalog.TurnStartBlock);
        if (block > 0)
        {
            Flash();
            return CreatureCmd.GainBlock(owner.Creature, block, ValueProp.Unpowered, null);
        }
        return Task.CompletedTask;
    }

    // ---------- Card-play hooks ----------

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        var owner = Owner;
        if (owner is null || !QuriousCraftingRelicsConfig.EnableChaosRelics
            || cardPlay.Card.Owner != owner)
        {
            return;
        }
        if (SlothCardCap != int.MaxValue)
        {
            _slothCardsPlayedThisTurn++;
            InvokeDisplayAmountChanged();
        }

        int damage = AmountOf(ChaosRelicCatalog.PlayDamageRandom);
        if (damage > 0)
        {
            var enemies = owner.Creature.CombatState?.HittableEnemies ?? Array.Empty<Creature>();
            if (enemies.Count > 0)
            {
                Flash();
                var target = enemies[owner.RunState.Rng.CombatTargets.NextInt(enemies.Count)];
                await CreatureCmd.Damage(choiceContext, target, damage, ValueProp.Unpowered, owner.Creature, null, null);
            }
        }
        int block = AmountOf(ChaosRelicCatalog.PlayBlock);
        if (block > 0)
        {
            Flash();
            await CreatureCmd.GainBlock(owner.Creature, block, ValueProp.Unpowered, null);
        }
    }

    // ---------- Passive hooks ----------

    /// <summary>
    /// Pure read (see the class contract). Gated like vanilla StrengthPower:
    /// only powered attacks (ValueProp.Move without Unpowered) from a real
    /// attack card owned by this relic's owner. Without the gate the bonus was
    /// added to relic damage, poison ticks and every other Unpowered source
    /// (probe: unpoweredDamageBonus=3).
    /// </summary>
    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        var owner = Owner;
        if (owner is null || !QuriousCraftingRelicsConfig.EnableChaosRelics
            || dealer != owner.Creature || target == owner.Creature)
        {
            return 0m;
        }
        // IsMutable gate before touching cardSource.Owner: that getter asserts
        // mutable and throws CanonicalModelException on a canonical card, the
        // same trap RunSeedOf documents for RelicModel.Owner. A canonical card
        // is never in a live combat, so it can never be the source here.
        if (!props.IsPoweredAttack() || cardSource is null || !cardSource.IsMutable
            || cardSource.Type != CardType.Attack || cardSource.Owner != owner)
        {
            return 0m;
        }
        int bonus = AmountOf(ChaosRelicCatalog.PassiveAttackDamage);
        int malus = AmountOf(ChaosRelicCatalog.NegAttackDamageDown);
        int retainBonus = ExtraPoolActive ? _retainAttackBuff : 0;
        return bonus - malus + retainBonus;
    }

    /// <summary>
    /// Consumes the retain attack buff once the attack it empowered has fully
    /// resolved. Vanilla VigorPower consumes in AfterAttack for the same
    /// reason: ModifyDamageAdditive is also called for previews, so the
    /// decrement cannot live there.
    /// </summary>
    public override Task AfterAttack(PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Commands.Builders.AttackCommand command)
    {
        if (ExtraPoolActive && _retainAttackBuff > 0
            && Owner is not null && command.Attacker == Owner.Creature
            && command.DamageProps.IsPoweredAttack()
            && command.ModelSource is CardModel)
        {
            _retainAttackBuff = 0;
        }
        return Task.CompletedTask;
    }

    public override decimal ModifyMaxEnergy(Player player, decimal amount)
    {
        var owner = Owner;
        if (owner is null || player != owner || !QuriousCraftingRelicsConfig.EnableChaosRelics)
        {
            return amount;
        }
        return Math.Max(0m, amount
            + AmountOf(ChaosRelicCatalog.PassiveMaxEnergy)
            - AmountOf(ChaosRelicCatalog.NegTurnEnergyDown));
    }

    /// <summary>
    /// Turn-1 draw bonus, vanilla BagOfPreparation / RingOfTheSnake pattern.
    /// </summary>
    public override decimal ModifyHandDraw(Player player, decimal count)
    {
        var owner = Owner;
        if (owner is null || player != owner || !QuriousCraftingRelicsConfig.EnableChaosRelics)
        {
            return count;
        }
        decimal result = count;
        if (player.PlayerCombatState?.TurnNumber <= 1)
        {
            result += AmountOf(ChaosRelicCatalog.StartDraw);
        }
        // Floor 1: a 0-card hand would brick the run; NoDraw semantics.
        return Math.Max(1m, result - AmountOf(ChaosRelicCatalog.NegTurnDrawDown));
    }

    public override decimal ModifyGoldGained(Player player, decimal amount)
    {
        var owner = Owner;
        if (owner is null || player != owner || !QuriousCraftingRelicsConfig.EnableChaosRelics)
        {
            return amount;
        }
        return amount
            + AmountOf(ChaosRelicCatalog.PassiveGoldGain)
            - AmountOf(ChaosRelicCatalog.NegGoldDown);
    }

    public override decimal ModifyRestSiteHealAmount(Creature creature, decimal amount)
    {
        var owner = Owner;
        if (owner is null || creature != owner.Creature || !QuriousCraftingRelicsConfig.EnableChaosRelics)
        {
            return amount;
        }
        return amount
            + AmountOf(ChaosRelicCatalog.RestHealBonus)
            - AmountOf(ChaosRelicCatalog.NegRestHealDown);
    }

    public override bool ShouldProcurePotion(PotionModel potion, Player player)
    {
        var owner = Owner;
        if (owner is null || player != owner || !QuriousCraftingRelicsConfig.EnableChaosRelics)
        {
            return true;
        }
        // Negative: cannot acquire potions at all.
        return AmountOf(ChaosRelicCatalog.NegPotionBlock) == 0;
    }

    public override decimal ModifyBlockAdditive(Creature? target, decimal block, ValueProp props,
        CardModel? cardSource, CardPlay? cardPlay)
    {
        var owner = Owner;
        if (owner is null || target != owner.Creature || !QuriousCraftingRelicsConfig.EnableChaosRelics)
        {
            return 0m;
        }
        return AmountOf(ChaosRelicCatalog.PassiveBlockAdd);
    }

    // ---------- Obtain hook (one-shot negatives) ----------

    public override async Task AfterObtained()
    {
        var owner = Owner;
        if (owner is null || !QuriousCraftingRelicsConfig.EnableChaosRelics)
        {
            return;
        }
        // Pickup enchants (extra pool, form B, user order 2026-09-13): one
        // random eligible DECK card per entry, at {N} levels, same-type
        // stacking via EnchantWithStacking.
        int pickupSharp = AmountOf(ChaosRelicExtraCatalog.PickupEnchantSharp);
        if (pickupSharp > 0)
        {
            await ApplyPickupEnchant<Sharp>(owner, pickupSharp);
        }
        int pickupNimble = AmountOf(ChaosRelicExtraCatalog.PickupEnchantNimble);
        if (pickupNimble > 0)
        {
            await ApplyPickupEnchant<Nimble>(owner, pickupNimble);
        }
        int pickupImbued = AmountOf(ChaosRelicExtraCatalog.PickupEnchantImbued);
        if (pickupImbued > 0)
        {
            await ApplyPickupEnchant<Imbued>(owner, pickupImbued);
        }

        int maxHpDown = AmountOf(ChaosRelicCatalog.NegMaxHpDown);
        if (maxHpDown > 0)
        {
            Flash();
            await CreatureCmd.LoseMaxHp(new ThrowingPlayerChoiceContext(), owner.Creature,
                maxHpDown, isFromCard: false);
        }
    }

    // ---------- Victory hooks ----------

    public override async Task AfterCombatVictory(CombatRoom room)
    {
        var owner = Owner;
        if (owner is null || !QuriousCraftingRelicsConfig.EnableChaosRelics)
        {
            return;
        }
        int heal = AmountOf(ChaosRelicCatalog.VictoryHeal);
        if (heal > 0)
        {
            Flash();
            await CreatureCmd.Heal(owner.Creature, heal);
        }
        int gold = AmountOf(ChaosRelicCatalog.VictoryGold);
        if (gold > 0)
        {
            Flash();
            await PlayerCmd.GainGold(gold, owner);
        }
    }

    // ======================================================================
    // EXTRA pool execution (card keywords / enchants / watcher stances).
    // All gated on EnableExtraPool; stance entries also require the Watcher
    // mod (reflection; no compile-time dependency).
    // ======================================================================

    private static bool ExtraPoolActive =>
        QuriousCraftingRelicsConfig.EnableChaosRelics && QuriousCraftingRelicsConfig.EnableExtraPool;

    /// <summary>
    /// First N hand cards that satisfy <paramref name="eligible"/>. The
    /// eligibility filter is applied BEFORE the take: filtering afterwards
    /// (as the Sharp loop did) means requesting 2 attack cards can enchant
    /// fewer than 2 while non-attack cards ahead of them in hand order are
    /// counted against the quota.
    /// </summary>
    private static IEnumerable<CardModel> TakeEligible(Player owner, int n, Func<CardModel, bool> eligible)
    {
        var hand = owner.PlayerCombatState?.Hand.Cards;
        if (hand is null || hand.Count == 0 || n <= 0)
        {
            return Array.Empty<CardModel>();
        }
        return hand.Where(eligible).Take(n);
    }

    /// <summary>
    /// Combat-start enchants (one-shot). Called from the first player turn so
    /// the opening hand exists, and every candidate is validated with the
    /// enchantment's own CanEnchant before CardCmd.Enchant - which THROWS
    /// InvalidOperationException on an ineligible card (Nimble requires
    /// GainsBlock, Imbued requires a Skill) rather than returning null.
    /// </summary>
    private void RunExtraCombatStartEnchants(Player owner)
    {
        int sharp = AmountOf(ChaosRelicExtraCatalog.EnchantSharp);
        if (sharp > 0)
        {
            ApplyEnchant<Sharp>(owner, sharp);
        }
        int nimble = AmountOf(ChaosRelicExtraCatalog.EnchantNimble);
        if (nimble > 0)
        {
            ApplyEnchant<Nimble>(owner, nimble);
        }
        int imbued = AmountOf(ChaosRelicExtraCatalog.EnchantImbued);
        if (imbued > 0)
        {
            ApplyEnchant<Imbued>(owner, imbued);
        }
    }

    private void ApplyEnchant<T>(Player owner, int count) where T : EnchantmentModel
    {
        var canonical = ModelDb.Enchantment<T>();
        foreach (var card in TakeEligible(owner, count, c => CanTakeEnchant<T>(c)))
        {
            Flash();
            EnchantWithStacking<T>(card, 1);
        }
    }

    /// <summary>
    /// Eligibility for the stacking enchant entry (user rule 2026-09-13): a
    /// card with a DIFFERENT enchantment is NOT eligible (one enchantment slot
    /// per card), a card with the SAME enchantment or none is.
    /// EnchantmentModel.CanEnchant alone would reject same-type re-runs on
    /// non-stackable enchantments, so the type check is done here.
    /// </summary>
    private static bool CanTakeEnchant<T>(CardModel card) where T : EnchantmentModel
    {
        // Same-type re-run: level up (bypasses vanilla's non-stackable
        // rejection, which lives inside CanEnchant's last branch).
        if (card.Enchantment is T)
        {
            return true;
        }
        // Empty slot or different type: vanilla checks (card-type
        // restrictions, unplayable deck cards, one-enchant-per-card).
        return ModelDb.Enchantment<T>().CanEnchant(card);
    }

    /// <summary>
    /// Enchant with SAME-TYPE LEVEL STACKING (user order 2026-09-13: "每张牌
    /// 只能附魔一次且相同附魔可以提升等级"). The engine's own
    /// CardCmd.Enchant already implements the stack branch
    /// (card.Enchantment.Amount += amount) but its CanEnchant gate rejects
    /// same-type re-runs unless the enchantment is IsStackable, so the
    /// same-type path is executed directly: Amount += N on the card's live
    /// enchantment instance, mirroring CardCmd.Enchant's stack branch.
    /// Different-type: unreachable here (filtered by CanTakeEnchant).
    /// </summary>
    private void EnchantWithStacking<T>(CardModel card, int amount) where T : EnchantmentModel
    {
        if (card.Enchantment is T existing)
        {
            existing.Amount += amount;
            card.FinalizeUpgradeInternal();
            return;
        }
        CardCmd.Enchant<T>(card, amount);
    }

    /// <summary>
    /// Pickup enchant (form B): one random eligible card in the owner's MASTER
    /// DECK gets <paramref name="levels"/> levels of T. RNG channel UpFront
    /// (run-level, deterministic for the seed); the enchant command itself
    /// syncs through the normal card-command pipeline in multiplayer.
    /// </summary>
    private async Task ApplyPickupEnchant<T>(Player owner, int levels) where T : EnchantmentModel
    {
        var deck = PileType.Deck.GetPile(owner).Cards;
        if (deck.Count == 0)
        {
            return;
        }
        var canonical = ModelDb.Enchantment<T>();
        var eligible = deck.Where(c => CanTakeEnchant<T>(c) && canonical.CanEnchantCardType(c.Type)).ToList();
        if (eligible.Count == 0)
        {
            MainFile.Logger.Info($"[QuriousCraftingRelics] pickup enchant {typeof(T).Name}: no eligible deck card");
            return;
        }
        Flash();
        var card = owner.RunState.Rng.UpFront.NextItem(eligible);
        EnchantWithStacking<T>(card, levels);
        await Task.CompletedTask;
    }

    /// <summary>Per-turn: hand keywords + stance entry (re-applied each turn).</summary>
    private async Task RunExtraTurnStart(Player player)
    {
        int retain = AmountOf(ChaosRelicExtraCatalog.HandRetain);
        if (retain > 0)
        {
            foreach (var card in TakeEligible(player, retain, static _ => true))
            {
                Flash();
                card.GiveSingleTurnRetain();
            }
        }
        int sly = AmountOf(ChaosRelicExtraCatalog.HandSly);
        if (sly > 0)
        {
            foreach (var card in TakeEligible(player, sly, static _ => true))
            {
                Flash();
                card.GiveSingleTurnSly();
            }
        }
        int ethereal = AmountOf(ChaosRelicExtraCatalog.NegHandEthereal);
        if (ethereal > 0)
        {
            foreach (var card in TakeEligible(player, ethereal,
                static c => !c.Keywords.Contains(CardKeyword.Ethereal)))
            {
                Flash();
                card.AddKeyword(CardKeyword.Ethereal);
            }
        }
        // Stance entries are TURN-SCHEDULED, not every-turn (user order
        // 2026-09-13): calm at the player's FIRST turn start, wrath at turn 2,
        // divinity at turn 3. The old code re-entered each stance on EVERY
        // turn start, which overwrote the player's chosen stance every turn.
        int turn = player.PlayerCombatState?.TurnNumber ?? 0;
        if (turn == 2)
        {
            await RunStanceEntry(player, ChaosRelicExtraCatalog.StanceWrathStart, "EnterWrath");
        }
        if (turn == 1)
        {
            await RunStanceEntry(player, ChaosRelicExtraCatalog.StanceCalmStart, "EnterCalm");
        }
        if (turn == 3)
        {
            await RunStanceEntry(player, ChaosRelicExtraCatalog.StanceDivinityStart, "EnterDivinity");
        }
    }

    /// <summary>Watcher-mod stance entry via reflection (skipped when absent).</summary>
    private async Task RunStanceEntry(Player player, string template, string helperMethod)
    {
        if (AmountOf(template) == 0)
        {
            return;
        }
        var watcher = AppDomain.CurrentDomain.GetAssemblies()
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

    /// <summary>
    /// Retain-triggered effects. Driven by AfterFlush, which the engine calls
    /// with the exact list of cards that survived the end-of-turn hand flush -
    /// the definition of "this card was retained". The previous implementation
    /// listened to AfterCardChangedPiles looking for a hand-to-hand move,
    /// which the engine never produces for a retain, so these entries were
    /// inert.
    /// </summary>
    public override Task AfterFlush(PlayerChoiceContext choiceContext, Player player,
        IReadOnlyCollection<CardModel> flushedCards, IReadOnlyCollection<CardModel> retainedCards)
    {
        var owner = Owner;
        if (!ExtraPoolActive || owner is null || player != owner || retainedCards.Count == 0)
        {
            return Task.CompletedTask;
        }
        int discount = AmountOf(ChaosRelicExtraCatalog.RetainEnergyDiscount);
        int attackBuff = AmountOf(ChaosRelicExtraCatalog.RetainAttackBuff);
        if (discount == 0 && attackBuff == 0)
        {
            return Task.CompletedTask;
        }
        foreach (var card in retainedCards)
        {
            if (discount > 0)
            {
                Flash();
                // Relative modifier, until played, reduce-only via negative amount.
                card.EnergyCost.AddUntilPlayed(-discount, reduceOnly: true);
            }
            if (attackBuff > 0)
            {
                Flash();
                _retainAttackBuff += attackBuff;
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Retained-attack buff (extra pool): the next powered attack card gains
    /// the stacked bonus, then resets. Consumption happens in AfterAttack, not
    /// here - see the class contract.
    /// </summary>
    private int _retainAttackBuff;
}
