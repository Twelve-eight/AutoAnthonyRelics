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

History, kept short because every earlier rule is a DEAD path:
- 2026-09-07 order: "3x the entry count cards get"
  (clamp(3*(1+rank), 3, 15) -> 3/6/9/12/15).
- 2026-09-08 playtest: 6/9 entries too bloated -> first a banded count
  (clamp(band-1+rank, 3, band+2) driven by ChaosRelicMultiplier), then
  the literal 1/3/5 tier (rank 0 -> 1, 1 -> 3, 2+ -> 5). The 1/3/5 rule
  is what the old workshop text advertised.
- 2026-09-11 (CURRENT): relics are ALWAYS active (unlike cards, which must
  be drawn and played), so a free 1/3/5-entry relic is severely
  overpowered. Both count rules were replaced wholesale by a
  Monster-Hunter-Rise qurious-crafting style POINT BUDGET.
  `ChaosRelicMultiplier` is retained ONLY as an idle save-compat key - it
  no longer feeds generation.

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
most once per relic. `ChaosRelicGenerator.UniqueOnly` (T_START_ENERGY /
T_START_DRAW / PASSIVE_MAX_ENERGY) is removed from the candidate set
ENTIRELY - repeatable engines would stack degenerately. Duplicate effect
SETS across relics are re-rolled up to 4 times, then accepted.

### Seeding and run binding

Mirror AutoAnthony's StableSeed idea, simplified:
seed string = run seed (RunState.Rng seed string). Generate pool lazily
on first relic-bag.Populate of the run; store
IReadOnlyList<ChaosRelicDefinition> in a static registry (bounded to 8
runs) keyed by (seed, config fingerprint). The fingerprint is a stable
string over every value that feeds generation - budgets, negative
chances, per-template costs/refunds, Min/Max bounds, EnableExtraPool,
Watcher-mod presence - so a config change for the same seed regenerates
instead of serving the stale pool (F04 probe, 2026-09-12: the seed-only
cache returned the old pool after budgets changed). Deterministic per
(seed, config): same inputs -> same relics (MP replay safety).

### Runtime model

ChaosRelicModel : BaseLib CustomRelicModel (shared pool).
- abstract int Slot (slot-marker subclasses ChaosRelic000..059, thin
  classes exactly like AutoAnthony's ChaosCard000 pattern).
- Definition resolved from registry by (seed, slot); registry key is
  (seed, config fingerprint) - see Seeding and run binding.
- Overrides the hooks used by the catalog; iterates Operations with
  matching hook and executes via engine commands.
- Description: join operation Texts with newline (relic tooltip shows
  the full list, positives and the negative alike).
- Counter: the badge shows the entry COUNT (DisplayAmount = number of
  operations), gated on an in-progress combat like vanilla VelvetChoker.
- Flash() on each executed entry.

Slot classes: 60 slots (20 per rarity). All routed into the engine
SharedRelicPool via the [Pool(typeof(SharedRelicPool))] attribute on
ChaosRelicModel (see Pools/ChaosSharedRelicPool.cs for why the earlier
BaseLib IsShared registration never reached the reward deques).

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

Registered from MainFile via ModConfigRegistry.Register; rendered both by
BaseLib's own settings page and by the dedicated page (see Settings UI).
- EnableChaosRelics (default true): gate all content (pool replacement
  + loc updates + hooks).
- EnableExtraPool (default false): the extra (non-vanilla) template pool.
- ChaosRelicBudgetCommon / Uncommon / Rare (defaults 10 / 16 / 24):
  point budget per rarity. Generation raises a configured budget to the
  cheapest-positive floor (see Entry economics).
- ChaosRelicNegativeChanceCommon / Uncommon / Rare (defaults 35 / 55 /
  75, percent): chance of the single negative entry.
- Cost_<TEMPLATE> / Refund_<TEMPLATE> (int properties, one per
  template): the LIVE per-point economics. Resolved by property name
  through ChaosPointCosts - no separate override bridge.
- Min_<TEMPLATE> / Max_<TEMPLATE> (int properties, [ConfigHideInUI]):
  user amount bounds set by the budget editor; overlaid on the catalog
  spec by ApplyUserBounds / ChaosTemplates.Effective.
- ChaosRelicMultiplier (default 3): LEGACY / IDLE. Kept only for save
  compatibility (the cfg file already has the key, and dropping a
  property makes BaseLib's loader warn and rewrite); it does NOT affect
  generation since the v0.5 point-budget system.

### Settings UI

- BaseLib's mod-settings list still shows the config (it has visible
  sliders), and the dedicated page is the primary entry: vanilla
  Settings -> General gains an "AutoAnthony - Relics" group row next to
  AutoAnthony's own row (RelicsSettingsScreenPatch + submenu-stack
  registration), pushing RelicsSettingsSubmenu.
- RelicsSettingsSubmenu hosts BaseLib's SetupConfigUI for the registered
  config instance plus BudgetEditorPanel: one row per template with the
  rendered effect text, vanilla-relic hover chips, a double-ended
  RangeSlider for the amount band, and the live per-point price.
- Persistence mirrors BaseLib's own page: config Changed() arms a 5 s
  debounce, OnSubmenuHidden flushes immediately.

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
  - MainFile.cs (ModInitializer; config register; Harmony patch sweep;
    RrcTreasureKeyCompat.TryInstall)
  - AutoAnthonyRelicsConfig.cs (SimpleModConfig: budgets, chances,
    Cost_/Refund_/Min_/Max_ per template)
  - Chaos/ChaosRelicDefinition.cs (ChaosRelicOperation + definition),
    ChaosRelicCatalog.cs (core pool + TemplateSpec),
    ChaosRelicExtraCatalog.cs (extra pool),
    ChaosTemplates.cs (cross-pool spec/price/floor resolution),
    ChaosRelicGenerator.cs (budget generation + naming),
    ChaosRelicRunRegistry.cs (seed+fingerprint pool cache),
    VanillaRelicMapping.cs (template -> vanilla relic hover refs)
  - Models/ChaosRelicModel.cs + ChaosRelicSlots.cs (ChaosRelic000..059)
  - Pools/ChaosSharedRelicPool.cs (slot registry + pool attribute doc)
  - Patches/ (pool replacement, seed tracking, loc updater, settings
    screen + submenu, BudgetEditorPanel, RangeSlider)
  - Compat/RrcTreasureKeyCompat.cs (shared with standalone/RrcA4hKeyFix)
  - DevConsole/LocDumpConsoleCmd.cs (locdump tool)
  - Extensions/StringExtensions.cs
- AutoAnthonyRelics/localization/{zhs,eng}/relics.json + settings_ui.json
- images/relics/*.png (placeholder icon set, one per slot)

## Verification plan

1. dotnet build -> zero errors; CopyToModsFolderOnBuild deploys to
   mods/AutoAnthonyRelics.
2. Launch game (modded), check godot.log for:
   - ModManager load of AutoAnthonyRelics
   - no initializer errors
3. Console: spawn a chaos relic (dev console AbstractConsoleCmd;
   BaseLib auto-registers) and inspect tooltip entries.
4. Start a run; confirm the reward pool contains chaos relics and each
   tooltip shows its positive entries plus at most one negative.
5. Settings -> General -> "AutoAnthony - Relics": config sliders render,
   budget editor rows show effect text + range slider, edits persist to
   mod_configs/AutoAnthonyRelics.cfg.
6. Isolated probe (G:/omp works/.tmp/aar-assessment-20260912): 45 seeds
   x 60 relics - determinism, budget spend <= budget + refund, <= 1
   negative, <= 6 positives, 20/20/20 rarity, unique names, in-band
   amounts. Results in assessment-2026-09-12-results.txt.
7. MP: full two-end session still unverified; the deterministic-by-(seed,
   config) contract is documented and both ends must carry identical
   budget/cost/bounds/extra-pool config (Tier-1 keys).

## Non-goals (v1)

- No upgrade paths, no run-history snapshot transport, no relic-specific
  art (placeholder icon set only), no per-character pools (shared only).
  The entry-count counter on the relic badge IS implemented
  (DisplayAmount = operation count).

## v0.5 budget system (point budget + negative entries)

User order 2026-09-11: relics are ALWAYS fully active (unlike cards which
must be drawn/played), so 1-3-5 entries is severely overpowered. New model,
Monster Hunter Rise qurious-crafting style: each relic gets a POINT BUDGET
set by rarity; positive entries COST points; negative entries REFUND points
(enabling more positives, like Black Ring red-quality / MH anomaly
augmentation); the final relic is "a few positives + one negative".

### Point budget contract (summary)

Algorithm and pricing shape: see "Entry economics" above - one
implementation (ChaosRelicGenerator + ChaosTemplates), not a second copy
of the rules. The knobs:

    BudgetFor(rarity) = ChaosRelicBudgetCommon / Uncommon / Rare (config)
    PriceOf(spec, N)  = CostPerPoint x N            (linear)
    PriceOf(spec, N)  = CostPerPoint x N x (N+1)/2  (decaying/triangular)

Config carries every knob as a static property (BaseLib SimpleModConfig):
- ChaosRelicBudgetCommon / Uncommon / Rare (defaults 10 / 16 / 24)
- ChaosRelicNegativeChanceCommon / Uncommon / Rare (defaults 35 / 55 / 75)
- Cost_<TEMPLATE> / Refund_<TEMPLATE> per-template economics
- Min_<TEMPLATE> / Max_<TEMPLATE> amount bounds (budget editor)

Tier-1 MP determinism: all budget/chance/cost/bound keys join
EnableChaosRelics and EnableExtraPool as must-match config; the
legacy-idle ChaosRelicMultiplier does NOT feed generation (see
sts2-mpconfigsync incident doc).

### Full entry-template list (v0.5 catalog)

Source of truth: ChaosRelicCatalog.cs (core pool) and
ChaosRelicExtraCatalog.cs (extra pool). Totals: 26 core positives +
10 core negatives + 10 extra positives + 1 extra negative = 47
templates. Extra templates join the generation pool only while
EnableExtraPool is on; the three stance templates additionally require
the Watcher mod (assembly probe).

CORE POSITIVES (26) - cost = CostPerPoint x Amount (triangular when decaying):

| Template | Hook | Band | CostPerPoint | Text (zh) |
|----------|------|------|--------------|-----------|
| C_START_DAMAGE_ALL | BeforeCombatStart | 3-8 | 2 | 战斗开始时,对所有敌人造成{N}点伤害. |
| C_START_BLOCK | BeforeCombatStart | 4-10 | 2 | 战斗开始时,获得{N}点格挡. |
| C_START_STRENGTH | BeforeCombatStart | 1-10 | 5 | 战斗开始时,获得{N}点力量. |
| C_START_DEXTERITY | BeforeCombatStart | 1-3 | 4 | 战斗开始时,获得{N}点敏捷. |
| C_START_DRAW | ModifyHandDraw (turn 1) | 1-3 | 3 | 战斗开始时,抽{N}张牌. |
| C_START_ENERGY | BeforeCombatStart -> AfterSideTurnStart (turn 1) | 1-3 | 6 | 战斗开始时,获得{N}点能量. |
| C_START_VULN_ALL | BeforeCombatStart | 1-3 | 3 | 战斗开始时,对所有敌人施加{N}层易伤. |
| C_START_WEAK_ALL | BeforeCombatStart | 1-3 | 3 | 战斗开始时,对所有敌人施加{N}层虚弱. |
| C_START_REGEN | BeforeCombatStart | 1-4 | 2 (decaying) | 战斗开始时,获得{N}层再生. |
| C_START_THORNS | BeforeCombatStart | 1-3 | 4 | 战斗开始时,获得{N}点荆棘. |
| C_START_ARTIFACT | BeforeCombatStart | 1-2 | 9 | 战斗开始时,获得{N}层人工制品. |
| C_START_POISON_ALL | BeforeCombatStart | 2-6 | 1 (decaying) | 战斗开始时,对所有敌人施加{N}层中毒. |
| C_START_PLATING | BeforeCombatStart | 1-4 | 2 (decaying) | 战斗开始时,获得{N}层覆甲. |
| T_START_BLOCK | AfterPlayerTurnStartLate | 2-5 | 4 | 每回合开始时,获得{N}点格挡. |
| T_START_ENERGY | AfterPlayerTurnStartLate | 1-1 | 10 | 每回合开始时,获得{N}点能量. |
| T_START_HEAL | AfterPlayerTurnStartLate | 1-3 | 7 | 每回合开始时,回复{N}点生命. |
| T_START_DRAW | AfterPlayerTurnStartLate | 1-1 | 9 | 每回合开始时,抽{N}张牌. |
| PLAY_DAMAGE_RANDOM | AfterCardPlayed | 1-4 | 2 | 每当你打出一张牌,对随机一名敌人造成{N}点伤害. |
| PLAY_BLOCK | AfterCardPlayed | 1-3 | 4 | 每当你打出一张牌,获得{N}点格挡. |
| PASSIVE_ATTACK_DAMAGE | ModifyDamageAdditive | 1-8 | 7 | 你的攻击牌伤害+{N}. |
| PASSIVE_MAX_ENERGY | ModifyMaxEnergy | 1-1 | 8 | 每回合能量上限+{N}. |
| PASSIVE_BLOCK_ADD | ModifyBlockAdditive | 1-2 | 6 | 你获得格挡时,格挡值+{N}. |
| VICTORY_HEAL | AfterCombatVictory | 2-8 | 3 | 战斗胜利后,回复{N}点生命. |
| VICTORY_GOLD | AfterCombatVictory | 5-20 | 1 | 战斗胜利后,获得{N}金币. |
| PASSIVE_GOLD_GAIN | ModifyGoldGained | 1-3 | 2 | 你获得的金币+{N}. |
| REST_HEAL_BONUS | ModifyRestSiteHealAmount | 1-5 | 1 | 营火休息时,额外回复{N}点生命. |

CORE NEGATIVES (10) - refund = RefundPerPoint x Amount:

| Template | Hook | Band | RefundPerPoint | Text (zh) |
|----------|------|------|----------------|-----------|
| N_START_FRAIL_SELF | BeforeCombatStart | 1-2 | 4 | 战斗开始时,你获得{N}层脆弱. |
| N_TURN_LOSE_HP | AfterPlayerTurnStartLate | 1-3 | 3 | 每回合开始时,失去{N}点生命. |
| N_TURN_ENERGY_DOWN | ModifyMaxEnergy | 1-1 | 6 | 每回合能量上限-{N}. |
| N_TURN_DRAW_DOWN | ModifyHandDraw | 1-5 | 6 | 每回合抽牌数-{N}. |
| N_GOLD_DOWN | ModifyGoldGained | 1-3 | 2 | 你获得的金币-{N}. |
| N_POTION_BLOCK | ShouldProcurePotion | 1-1 | 30 | 你无法获得药水. |
| N_START_SLOTH_SELF | ShouldPlay counter (relic-side cap) | 1-5 | 6 | 每回合你无法打出超过{M}张牌. |
| N_REST_HEAL_DOWN | ModifyRestSiteHealAmount | 1-4 | 2 | 营火休息时,回复的生命-{N}. |
| N_ATTACK_DAMAGE_DOWN | ModifyDamageAdditive | 1-2 | 3 | 你的攻击牌伤害-{N}. |
| N_MAX_HP_DOWN | AfterObtained | 1-20 | 4 | 获得此遗物时,最大生命值-{N}. |

EXTRA POSITIVES (10; EnableExtraPool only):

| Template | Hook | Band | CostPerPoint | Text (zh) |
|----------|------|------|--------------|-----------|
| X_HAND_RETAIN | AfterPlayerTurnStartLate | 1-3 | 3 | 每回合开始时,你手牌中的至多{N}张牌获得保留. |
| X_HAND_SLY | AfterPlayerTurnStartLate | 1-3 | 3 | 每回合开始时,你手牌中的至多{N}张牌获得奇巧(如果这张牌在你的回合结束前从你的手牌中被丢弃,则免费将其打出). |
| X_RETAIN_ENERGY_DISCOUNT | AfterFlush | 1-1 | 4 | 每当你保留一张牌时,该牌费用-{N}. |
| X_RETAIN_ATTACK_BUFF | AfterFlush | 1-3 | 3 | 每当你保留一张牌时,本回合你的下一张攻击牌伤害+{N}. |
| X_ENCHANT_SHARP | AfterPlayerTurnStartLate (turn 1) | 1-2 | 5 | 战斗开始时,为你手牌中的至多{N}张攻击牌附加锋利附魔(这张牌上的伤害值+1). |
| X_ENCHANT_NIMBLE | AfterPlayerTurnStartLate (turn 1) | 1-2 | 5 | 战斗开始时,为你手牌中的至多{N}张获得格挡的牌附加灵巧附魔(这张牌获得的格挡值+1). |
| X_ENCHANT_IMBUED | AfterPlayerTurnStartLate (turn 1) | 1-2 | 4 | 战斗开始时,为你手牌中的至多{N}张技能牌附加注能附魔(这张牌在每场战斗开始时自动打出). |
| X_STANCE_WRATH | AfterPlayerTurnStartLate | 1-1 | 4 | 每回合开始时,进入愤怒姿态(你的攻击造成双倍伤害,你从攻击中受到双倍伤害). |
| X_STANCE_CALM | AfterPlayerTurnStartLate | 1-1 | 3 | 每回合开始时,进入平静姿态(离开这一姿态时,获得2点能量). |
| X_STANCE_DIVINITY | AfterPlayerTurnStartLate | 1-1 | 7 | 每回合开始时,进入神格姿态(你的攻击造成三倍伤害,进入时获得3点能量,下回合开始时自动离开). |

EXTRA NEGATIVES (1; EnableExtraPool only):

| Template | Hook | Band | RefundPerPoint | Text (zh) |
|----------|------|------|----------------|-----------|
| X_HAND_ETHEREAL | AfterPlayerTurnStartLate | 1-3 | 4 | 每回合开始时,你手牌中的至多{N}张牌获得虚无(如果这张牌在这个回合结束时留在你的手牌中,则将其消耗). |

Engine facts used (byte-verified against sts2.dll):
- FrailPower: Debuff, 25% block reduction (ModifyBlockMultiplicative 0.75).
- SlothPower: Debuff, limits cards playable per turn to Amount - NOT used
  any more; the sloth negative is a relic-side ShouldPlay counter (see
  Sloth redesign below).
- PowerCmd.Apply<T>(ctx, targets, amount, applier, cardSource, silent).
- ModifyHandDraw(player, count) additive on draw count (turn-1 gate for
  the combat-start draw entry; floor 1 against the draw-down negative).
- ShouldProcurePotion(potion, player): false blocks potion acquisition.
- ModifyRestSiteHealAmount(creature, amount) additive.
- CreatureCmd.LoseMaxHp(ctx, creature, amount, isFromCard) for the
  max-HP-down negative on obtain.
- CreatureCmd.Damage with ValueProp.Unblockable | Unpowered for the
  per-turn HP-loss negative (Unpowered alone was absorbed by block).
- RegenPower, ThornsPower, ArtifactPower, PoisonPower, PlatingPower all
  exist as player-appliable powers.

## v0.5.1 refinements (user session 2026-09-11 #2)

### User-tuned point sync
The user's tuning session produced 16 value edits (artifact 5->9 with band
1-1 -> 1-2, the decaying repricing, the sloth refund, etc.); those values
ARE the catalog defaults AND the in-game config defaults (verified equal
property-by-property on 2026-09-12). 4 collapsible ConfigSections in
settings: Budget / Positive costs / Negative refunds / Extra pool - BaseLib
ConfigSection renders collapsible. Config properties Cost_<TEMPLATE> /
Refund_<TEMPLATE> ARE the live values the generator reads (property-name
lookup; the old cfg-key override bridge was deleted - clean cutover).
workshop/TEMPLATE-POINTS-LIST.md is now GENERATED from the two catalog
files rather than hand-maintained, so it cannot drift again.

### Sloth redesign (velvet-choker)
1-stack SlothPower = "1 card per turn" = run-killing strength (user).
Redesigned after the vanilla VelvetChoker pattern: relic-side ShouldPlay
counter caps cards per turn at 7 - N (N = sloth amount, band 1-5), refund
6N points. Text renders the cap ({M} = 7-N). No power involved.

### Triangular decay pricing
Decaying powers (poison/regen/plating - engine-verified: trigger for
Amount then Amount-1, ..): the Nth stack is worth more than the 1st
(total value triangular), so total(N) = perPoint * N*(N+1)/2.
Per-point defaults from the catalog: Regen 2 (N=4 -> 20 pts),
Plating 2 (N=4 -> 20 pts), Poison 1 (N=6 -> 21 pts, includes the
all-enemies premium). Generator walks N down to fit the budget.
Also renamed 镀层->覆甲 (plating; user correction).

### EXTRA effect pool (opt-in)
Non-vanilla-relic effects, OFF by default (EnableExtraPool; Tier-1 MP key).
10 positives + 1 negative (X_HAND_ETHEREAL, refund 4/pt, band 1-3).
- X_HAND_RETAIN / X_HAND_SLY: GiveSingleTurnRetain/GiveSingleTurnSly on
  first N hand cards each turn (per-turn hook re-applies).
- X_HAND_ETHEREAL (negative): AddKeyword(CardKeyword.Ethereal) on first N
  hand cards each turn (exhaust at end of turn).
- X_ENCHANT_SHARP / NIMBLE / IMBUED: CardCmd.Enchant<T>(card, 1) on first
  N eligible hand cards. Applied on the FIRST player turn start (not
  BeforeCombatStart: the opening hand does not exist yet, so the loops
  iterated zero cards). Candidates are filtered with the enchantment's own
  CanEnchant - CardCmd.Enchant THROWS on an ineligible card (Nimble
  requires GainsBlock, Imbued requires a Skill).
- X_RETAIN_ENERGY_DISCOUNT: driven by AfterFlush (the engine hands it the
  exact retained-card list); EnergyCost.AddUntilPlayed(-N, reduceOnly).
  The earlier AfterCardChangedPiles hand-to-hand listener never fired,
  because the engine produces no such pile move for a retain.
- X_RETAIN_ATTACK_BUFF: on retain, next attack this turn +N (stacked,
  read inside ModifyDamageAdditive, CONSUMED in AfterAttack - not in the
  additive hook, which is also called for damage previews).
- X_STANCE_WRATH/CALM/DIVINITY: reflection into Watcher mod
  WatcherCombatHelper.Enter* (only when that mod is loaded; templates are
  generation-filtered by assembly probe otherwise).

Engine facts (byte-verified): CardModel.Type (not CardType) / .Pile?.Type /
EnergyCost.AddUntilPlayed(relative, reduceOnly) / Enchant<T> is sync /
Sharp resolves ambiguously without full qualification.

### Smoke state (45 seeds, user-tuned values)
ALL PASS: determinism, triangular spend <= budget + refund, <= 1 negative,
<= 6 positives, sloth cap text, 20/20/20 rarity, unique names, in-band.
Probe 2026-09-12 (2700 relics, extra pool off): avg positives
2.91/3.84/4.84; negative rate 37.6/57.4/76.3%; avg spend 14.46/22.71/31.87
vs budgets 10/16/24 (spend includes refunds, so it may exceed the budget).
