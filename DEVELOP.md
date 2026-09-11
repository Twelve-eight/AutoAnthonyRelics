# AutoAnthony - Relics (东尼算法 - 遗物) - DEVELOP.md

Design and contract document. User order 2026-09-07:
"写一个使遗物也被东尼算法随机的mod,不过,每个遗物将获得以前3倍数量的词条.
就叫东尼算法 - 遗物(AutoAnthony - Relics)吧"
(The 3x entry count from that order is superseded - see Entry economics.)

## Goal

Randomize RELICS the same way AutoAnthony randomizes cards: per-run
seeded generation of a chaos relic pool where each generated relic
carries a list of 词条 (entries). Entry COUNT is no longer the card 3x
rule: since v0.5 each relic gets a rarity-scaled POINT BUDGET, positive
entries cost points and negative entries refund them (see Entry
economics below).

This is a STANDALONE mod (own manifest id AutoAnthonyRelics), no
compile-time dependency on the AutoAnthony workshop mod. The
"Anthony algorithm" is reimplemented at relic scale, not referenced.

## Contracts

### Entry (词条)

    sealed record ChaosRelicOperation(string Template, int Amount, string Text)

- Template: opcode bound to exactly one relic hook (see catalog below).
- Amount: the single numeric parameter (damage, block, energy...).
- Text: rendered Chinese description fragment, e.g. "战斗开始时,造成X点伤害."

### Generated relic definition

    sealed record ChaosRelicDefinition(int Slot, RelicRarity Rarity,
        string Name, IReadOnlyList<ChaosRelicOperation> Operations)

### Entry economics (v0.5: point budget, not entry count)

History, kept short because both earlier rules are DEAD CODE paths:
- 2026-09-07 order: "3x the entry count cards get"
  (clamp(3*(1+rank), 3, 15) -> 3/6/9/12/15).
- 2026-09-08 playtest: 6/9 entries too bloated -> banded count
  (clamp(band-1+rank, 3, band+2) driven by ChaosRelicMultiplier).
- 2026-09-11 (CURRENT): relics are ALWAYS active (unlike cards, which must
  be drawn and played), so a free 1/3/5-entry relic is severely
  overpowered. Replaced wholesale by a Monster-Hunter-Rise
  qurious-crafting style POINT BUDGET. `ChaosRelicMultiplier` is retained
  ONLY as an idle save-compat key - it no longer feeds generation.

Generation algorithm (ChaosRelicGenerator.GenerateOne / AssembleOperations),
deterministic from the run seed plus the live cost table:

    budget = max(configured budget for rarity, cheapest positive floor)
    floor  = min over the ACTIVE positive pool of PriceOf(spec, spec.Min)

1. Phase 1 - spend: while `spendable >= cheapest unit` and positives <
   MaxPositives (6) and not every template taken, pick uniformly among
   templates whose MINIMUM amount fits, roll the amount in-band, clamp it
   down to what the budget affords, charge `PriceOf(spec, amount)`.
2. Phase 2 - negative roll: with the rarity's configured probability
   (defaults 35/55/75%), add exactly ONE negative entry, amount rolled
   in-band. Refund = `RefundPerPoint x amount`.
3. Phase 3 - refund spend: the refund buys more positives through the
   same Phase-1 loop (the "red quality" feel).
4. Guarantee: at least one positive entry always. Normally the budget
   floor does it; a pathological cost table (every positive priced above
   the budget) is caught by an explicit fallback that inserts the
   cheapest minimum-amount positive, ordered by template id so both MP
   ends pick identically.

Pricing shape (`TemplateSpec.Cost` / `ChaosTemplates.PriceOf`):

    linear:     cost(N) = CostPerPoint * N
    decaying:   cost(N) = CostPerPoint * N * (N+1) / 2     (triangular)

Decaying templates are the powers whose stacks trigger for Amount, then
Amount-1, .. (poison / regen / plating): the Nth stack is worth MORE than
the first, so the price is triangular. See "Triangular decay pricing".

Per-relic caps: MaxPositives = 6; one negative maximum; each template at
most once per relic (energy-per-turn / draw-per-turn / max-energy are
hard-excluded from repeat picks). Duplicate effect SETS across relics are
re-rolled up to 4 times, then accepted.

### Seeding and run binding

Mirror AutoAnthony's StableSeed idea, simplified:
seed string = run seed (RunState.Rng seed string). Generate pool lazily
on first RelicGrabBag.Populate of the run; store
IReadOnlyList<ChaosRelicDefinition> in a static registry keyed by seed.
Deterministic per seed: same seed -> same relics (MP replay safety).

### Runtime model

ChaosRelicModel : BaseLib CustomRelicModel (shared pool).
- abstract int Slot (slot-marker subclasses ChaosRelic000..NNN, thin
  classes exactly like AutoAnthony's ChaosCard000 pattern).
- Definition resolved from registry by (seed, slot).
- Overrides the hooks used by the catalog; iterates Operations with
  matching hook and executes via engine commands.
- Description: join operation Texts with newline (relic tooltip shows
  the full list; 3-15 entries visible).
- Counter/Flash on execution (Flash() like Anchor.cs).

Slot classes: 60 slots (20 per rarity). All registered in a shared
CustomRelicPoolModel (IsShared -> true path via BaseLib
ModelDbSharedRelicPoolsPatch).

### Integration (v0.4: pool REPLACEMENT)

User order 2026-09-08: "你应当替换除了先古之民遗物以外的任何遗物" -
every pool-sourced relic drop (combat rewards, treasure chests, shops,
events that pull random relics, dig/rest sites) becomes a chaos relic.

- Chaos relics enter via [Pool(typeof(SharedRelicPool))] (engine pool,
  v0.3 fix) so Populate already mixes them into the C/U/R/Shop deques.
- ChaosRelicPoolReplacementPatch: Harmony Postfix on BOTH Populate
  overloads strips every non-ChaosRelicModel from _deques AND
  _originalRelics (the RefreshRarity re-fill source). Single-target
  patch classes only (dual-target classes patch only the last target -
  see RunSeedTrackPatch history).
- Ancient / Starter / Event / Neow relics never enter the grab bag
  (Populate filters to _rarities = C/U/Rare/Shop), so the ancient pool
  stays vanilla by construction. Fixed event grants (NeowsBones etc.)
  are event content, not pool pulls - untouched.
- Pool exhaustion (all 60 chaos taken) falls back to the engine's own
  RelicFactory.FallbackRelic (Circlet) - standard vanilla depletion
  behavior.
- MP: same bag contents on both ends (seed-deterministic replacement of
  deterministic contents).

### Descriptions (v0.4: live per-run loc rewrite)

BaseLib ModelLocPatch evaluates ILocalizationProvider.Localization ONCE
at ModelDb.Init (no run seed yet) - the loc table gets the generic
fallback baked in, so tooltips showed no effects. ChaosRelicLocUpdater
(called from both seed-capture points) rewrites the "relics" table
entries (title/description/flavor per slot) for the live seed via the
same LocTable._translations reflection BaseLib uses. Idempotent per
seed; save-load covered by the Launch postfix call.

### Config (BaseLib SimpleModConfig)

- EnableChaosRelics (default true): gate all content (pool replacement
  + loc updates + hooks).
- ChaosRelicMultiplier (default 3): entry-count band base; band =
  clamp(multiplier, 3, 5), entries = clamp(band-1+rank, 3, band+2).

## File layout (mod/)

- AutoAnthonyRelics.csproj (copied from Spire1.csproj pattern:
  Godot.NET.Sdk 4.5.1, Publicize, BaseLib 3.4.5, no AutoAnthony ref)
- AutoAnthonyRelics.json (manifest, id AutoAnthonyRelics,
  depends BaseLib min 3.4.5, has_pck true)
- project.godot (config/name AutoAnthonyRelics)
- Directory.Build.props (GodotPath, Sts2Path discovery import)
- Sts2PathDiscovery.props (copied)
- NuGet.config (G: cache redirect, copied)
- Code/ (namespace AutoAnthonyRelics)
  - MainFile.cs (ModInitializer; config register; no Harmony needed v1)
  - Config.cs
  - Chaos/ChaosRelicOperation.cs, ChaosRelicDefinition.cs,
    ChaosRelicGenerator.cs, ChaosRelicCatalog.cs,
    ChaosRelicRunRegistry.cs
  - Models/ChaosRelicModel.cs + ChaosRelic000..059.cs
  - Pools/ChaosSharedRelicPool.cs
- images/relics/*.png (one placeholder icon set for all slots in v1:
  same PNG reused; distinct icons later)

## Verification plan

1. dotnet build -> zero errors; CopyToModsFolderOnBuild deploys to
   mods/AutoAnthonyRelics.
2. Launch game (modded), check godot.log for:
   - ModManager load of AutoAnthonyRelics
   - no initializer errors
3. Console: spawn a chaos relic (dev console AbstractConsoleCmd;
   BaseLib auto-registers) and inspect tooltip entries.
4. Start a run; confirm reward pool contains chaos relics and each
   shows 3-15 entries.
5. MP: out of scope for v1 smoke; deterministic-by-seed contract
   documented.

## Non-goals (v1)

- No upgrade paths, no counter displays, no run-history snapshot
  transport, no relic-specific art, no localization beyond zh + en
  description strings, no per-character pools (shared only).

## v0.5 budget system (point budget + negative entries)

User order 2026-09-11: relics are ALWAYS fully active (unlike cards which
must be drawn/played), so 1-3-5 entries is severely overpowered. New model,
Monster Hunter Rise qurious-crafting style: each relic gets a POINT BUDGET
set by rarity; positive entries COST points; negative entries REFUND points
(enabling more positives, like Black Ring red-quality / MH anomaly
augmentation); the final relic is "a few positives + one negative".

### Point budget contract

    BudgetFor(rarity) = ChaosRelicBudgetCommon / Uncommon / Rare (config)

Generation loop per relic (deterministic from seed, same recovery rules):
1. Start with budget = BudgetFor(rarity).
2. While budget >= cheapest positive AND positives < MaxPositives:
   pick a positive template weighted by rarity, roll amount within its band,
   cost = CostPerPoint(template) * Amount (config per template), clamp to
   remaining budget by re-rolling amount downward when needed.
3. Roll one negative entry (probability NegativeChance, config; default
   always for Rare, less for Common). Negative refund = its point value;
   the refund can then buy one more positive (the "red quality" feel).
4. Net spend never exceeds initial budget + refunds actually taken.

Config carries every knob as a static property (BaseLib SimpleModConfig):
- ChaosRelicBudgetCommon / Uncommon / Rare (defaults user-tunable)
- Per-template CostPerPoint_<TEMPLATE> (defaults assigned by user)
- ChaosRelicNegativeChanceCommon / Uncommon / Rare

Tier-1 MP determinism: all budget keys join EnableChaosRelics/Multiplier as
must-match config (see sts2-mpconfigsync incident doc).

### Full entry-template list (v0.5 catalog)

POSITIVES (cost points; band = amount range):
| Template | Hook | Band | Text (zh) |
|----------|------|------|-----------|
| C_START_DAMAGE_ALL | BeforeCombatStart | 3-8 | 战斗开始时,对所有敌人造成{N}点伤害. |
| C_START_BLOCK | BeforeCombatStart | 4-10 | 战斗开始时,获得{N}点格挡. |
| C_START_STRENGTH | BeforeCombatStart | 1-3 | 战斗开始时,获得{N}点力量. |
| C_START_DEXTERITY | BeforeCombatStart | 1-3 | 战斗开始时,获得{N}点敏捷. |
| C_START_DRAW | BeforeCombatStart | 1-3 | 战斗开始时,抽{N}张牌. |
| C_START_ENERGY | BeforeCombatStart | 1-3 | 战斗开始时,获得{N}点能量. |
| C_START_VULN_ALL | BeforeCombatStart | 1-3 | 战斗开始时,对所有敌人施加{N}层易伤. |
| C_START_WEAK_ALL | BeforeCombatStart | 1-3 | 战斗开始时,对所有敌人施加{N}层虚弱. |
| C_START_REGEN | BeforeCombatStart | 1-4 | 战斗开始时,获得{N}层再生. |
| C_START_THORNS | BeforeCombatStart | 1-3 | 战斗开始时,获得{N}点荆棘. |
| C_START_ARTIFACT | BeforeCombatStart | 1-1 | 战斗开始时,获得{N}层护体(抵消负面效果). |
| C_START_POISON_ALL | BeforeCombatStart | 2-6 | 战斗开始时,对所有敌人施加{N}层中毒. |
| C_START_METALLICIZE | BeforeCombatStart | 1-4 | 战斗开始时,获得{N}点巩固(每回合获得格挡). |
| T_START_BLOCK | AfterPlayerTurnStartLate | 2-5 | 每回合开始时,获得{N}点格挡. |
| T_START_ENERGY | AfterPlayerTurnStartLate | 1-1 | 每回合开始时,获得{N}点能量. |
| T_START_HEAL | AfterPlayerTurnStartLate | 1-3 | 每回合开始时,回复{N}点生命. |
| T_START_DRAW | AfterPlayerTurnStartLate | 1-1 | 每回合开始时,抽{N}张牌. |
| PLAY_DAMAGE_RANDOM | AfterCardPlayed | 1-4 | 每当你打出一张牌,对随机一名敌人造成{N}点伤害. |
| PLAY_BLOCK | AfterCardPlayed | 1-3 | 每当你打出一张牌,获得{N}点格挡. |
| PASSIVE_ATTACK_DAMAGE | ModifyDamageAdditive | 1-4 | 你的攻击牌伤害+{N}. |
| PASSIVE_MAX_ENERGY | ModifyMaxEnergy | 1-1 | 每回合能量上限+{N}. |
| PASSIVE_BLOCK_ADD | ModifyBlockAdditive | 1-2 | 你获得格挡时,格挡值+{N}. |
| VICTORY_HEAL | AfterCombatVictory | 2-8 | 战斗胜利后,回复{N}点生命. |
| VICTORY_GOLD | AfterCombatVictory | 5-20 | 战斗胜利后,获得{N}金币. |
| PASSIVE_GOLD_GAIN | ModifyGoldGained | 1-3 | 你获得的金币+{N}. |
| REST_HEAL_BONUS | ModifyRestSiteHealAmount | 1-5 | 营火休息时,额外回复{N}点生命. |

NEGATIVES (refund points):
| Template | Hook | Band | Text (zh) |
|----------|------|------|-----------|
| N_START_FRAIL_SELF | BeforeCombatStart | 1-2 | 战斗开始时,你获得{N}层脆弱. |
| N_TURN_LOSE_HP | AfterPlayerTurnStartLate | 1-3 | 每回合开始时,失去{N}点生命. |
| N_TURN_ENERGY_DOWN | ModifyMaxEnergy | 1-1 | 每回合能量上限-{N}. |
| N_TURN_DRAW_DOWN | ModifyHandDraw | 1-1 | 每回合抽牌数-{N}. |
| N_CARDS_COST_UP | ModifyEnergyCostInCombat | 1-1 | 你手牌中的技能牌费用+{N}. |
| N_GOLD_DOWN | ModifyGoldGained | 1-3 | 你获得的金币-{N}. |
| N_POTION_BLOCK | ShouldProcurePotion | - | 你无法获得药水. |
| N_START_SLOTH_SELF | BeforeCombatStart | 1-1 | 战斗开始时,你获得{N}层怠惰(每回合打出的牌数受限). |
| N_REST_HEAL_DOWN | ModifyRestSiteHealAmount | 1-4 | 营火休息时,回复的生命-{N}. |
| N_ATTACK_DAMAGE_DOWN | ModifyDamageAdditive | 1-2 | 你的攻击牌伤害-{N}. |
| N_MAX_HP_DOWN | AfterObtained | 1-4 | 获得此遗物时,最大生命值-{N}. |

Engine facts used (byte-verified against sts2.dll):
- FrailPower: Debuff, 25% block reduction (ModifyBlockMultiplicative 0.75).
- SlothPower: Debuff, limits cards playable per turn to Amount.
- PowerCmd.Apply<T>(ctx, targets, amount, applier, cardSource, silent).
- ModifyHandDraw(player, count) additive on draw count.
- ModifyEnergyCostInCombat(card, cost, inHand, combatState) cost delta.
- ShouldProcurePotion(potion, player): false blocks potion acquisition.
- ModifyRestSiteHealAmount(creature, amount) additive.
- AfterObtained + PlayerCmd/ModifyMaxHp: max-hp down via CreatureCmd or
  direct player stat command (verify exact API in code before use).
- RegenPower, ThornsPower, ArtifactPower, PoisonPower, MetallicizePower
  all exist as player-appliable powers.

## v0.5.1 refinements (user session 2026-09-11 #2)

### User-tuned point sync
All 16 user edits from workshop/TEMPLATE-POINTS-LIST.md are now the catalog
defaults AND in-game config sliders (4 collapsible ConfigSections in
settings: Budget / Positive costs / Negative refunds / Extra pool - BaseLib
ConfigSection renders collapsible). Config properties Cost_<TEMPLATE> /
Refund_<TEMPLATE> ARE the live values the generator reads (property-name
lookup; the old cfg-key override bridge was deleted - clean cutover).

### Sloth redesign (velvet-choker)
1-stack SlothPower = "1 card per turn" = run-killing strength (user).
Redesigned after the vanilla VelvetChoker pattern: relic-side ShouldPlay
counter caps cards per turn at 7 - N (N = sloth amount, band 1-5), refund
6N points. Text renders the cap ({M} = 7-N). No power involved.

### Triangular decay pricing
Decaying powers (poison/regen/plating - engine-verified: trigger for
Amount then Amount-1, ..): Nth stack worth more than 1st (total value
triangular), so total(N) = perPoint * N*(N+1)/2. Regen/Plating perPoint 2
(N=4 -> 20/12 pts), Poison perPoint 1 (N=6 -> 21 pts, includes the
all-enemies premium). Generator walks N down to fit budget.
Also renamed 镀层->覆甲 (plating; user correction).

### EXTRA effect pool (opt-in)
Non-vanilla-relic effects, OFF by default (EnableExtraPool; Tier-1 MP key):
- X_HAND_RETAIN / X_HAND_SLY: GiveSingleTurnRetain/GiveSingleTurnSly on
  first N hand cards each turn (per-turn hook re-applies).
- X_HAND_ETHEREAL (negative): AddKeyword(CardKeyword.Ethereal) on first N
  hand cards each turn (exhaust at end of turn).
- X_ENCHANT_SHARP / NIMBLE / IMBUED: CardCmd.Enchant on first N hand
  cards at combat start (Sharp attack-only; NOT awaited - returns
  EnchantmentModel, not Task).
- X_RETAIN_ENERGY_DISCOUNT: on retain (AfterCardChangedPiles hand->hand
  with ShouldRetainThisTurn): EnergyCost.AddUntilPlayed(-N, reduceOnly).
- X_RETAIN_ATTACK_BUFF: on retain, next attack this turn +N (stacked,
  consumed inside ModifyDamageAdditive - engine has no Late variant).
- X_STANCE_WRATH/CALM/DIVINITY: reflection into Watcher mod
  WatcherCombatHelper.Enter* (only when that mod is loaded; templates are
  generation-filtered by assembly probe otherwise).

Engine facts (byte-verified): CardModel.Type (not CardType) / .Pile?.Type /
EnergyCost.AddUntilPlayed(relative, reduceOnly) / Enchant<T> is sync /
Sharp resolves ambiguously without full qualification.

### Smoke state (45 seeds, user-tuned values)
ALL PASS: determinism, triangular spend<=budget+refund, <=1 negative,
<=6 positives, sloth cap text, 20/20/20 rarity, unique names, in-band.
avgSpend 14.3/22.5/31.1 vs budgets 10/16/24 (refunds recycle fully).
