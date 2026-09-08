# AutoAnthony - Relics (东尼算法 - 遗物) - DEVELOP.md

Design and contract document. User order 2026-09-07:
"写一个使遗物也被东尼算法随机的mod,不过,每个遗物将获得以前3倍数量的词条.
就叫东尼算法 - 遗物(AutoAnthony - Relics)吧"

## Goal

Randomize RELICS the same way AutoAnthony randomizes cards: per-run
seeded generation of a chaos relic pool where each generated relic
carries a list of 词条 (entries). Entry count per relic = 3x what the
card algorithm would roll for the same rarity.

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

### Entry count = banded (3 is the norm)

User order evolution: 2026-09-07 asked for "3x the entry count cards get"
(clamp(3*(1+rank), 3, 15) -> 3/6/9/12/15); live playtest 2026-09-08 judged
6/9 too bloated and 3 correct, so the band was reworked:

    entries = clamp(band - 1 + rank, MinEntries=3, band + 2)
    band    = clamp(ChaosRelicMultiplier, 3, MaxEntries - 2 = 5)
    rank    = weighted 0-4 roll on the card-baseline weights below

Simulated distribution at default multiplier 3 (3000/rarity):
Common 3@84%/4@12%/5@5%, Uncommon 3@62%/4@27%/5@10%,
Rare 3@47%/4@36%/5@17%. Config multiplier >3 shifts the whole band up
(multiplier 5 -> 4..7 entries).

Card-baseline weights (AutoAnthony PickComponentCount, rank 0-4):

  Basic    [70,125,5,1,1]
  Common   [110,100,30,8,2]
  Uncommon [90,105,85,25,7]
  Rare     [70,95,120,45,14]
  Ancient  [55,85,130,65,24]

Rarity mapping (StS2 RelicRarity): Common -> card Common weights,
Uncommon -> card Uncommon weights, Rare -> card Rare weights.
Shop relic rarity: roll as Rare.

### Effect catalog (v1, 16 templates)

Hook = BeforeCombatStart:
- C_START_DAMAGE_ALL: deal Amount damage to all enemies
- C_START_BLOCK: gain Amount block
- C_START_STRENGTH: gain Amount Strength
- C_START_DEXTERITY: gain Amount Dexterity
- C_START_DRAW: draw Amount cards
- C_START_ENERGY: gain Amount energy
- C_START_VULN_ALL: apply Amount Vulnerable to all enemies
- C_START_WEAK_ALL: apply Amount Weak to all enemies

Hook = AfterSideTurnStartLate (player turn start):
- T_START_BLOCK: gain Amount block
- T_START_ENERGY: gain Amount energy
- T_START_HEAL: heal Amount HP

Hook = AfterCardPlayed:
- PLAY_DAMAGE_RANDOM: deal Amount damage to 1 random enemy

Hook = ModifyDamageAdditive (passive, player->enemy only):
- PASSIVE_ATTACK_DAMAGE: + Amount to player attack damage

Hook = AfterCombatVictory:
- VICTORY_HEAL: heal Amount HP

Hook = ModifyMaxEnergy (passive):
- PASSIVE_MAX_ENERGY: + Amount max energy (Amount = 1 always)

Amount ranges per template (balance bands, small for per-turn, larger
for one-shot combat-start effects).

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
