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

### Entry count = 3x card baseline

AutoAnthony card component count (PickComponentCount) rolls a weighted
rank 0-4 (count = min + rank, min = catalog min = 1):

  Basic    [70,125,5,1,1]
  Common   [110,100,30,8,2]
  Uncommon [90,105,85,25,7]
  Rare     [70,95,120,45,14]
  Ancient  [55,85,130,65,24]

Relic entry count = 3 * (1 + weighted rank roll), clamped 3..15.
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

### Integration (how chaos relics enter the run)

Harmony Postfix on RelicGrabBag.Populate(Player, Rng):
- If config enabled: append generated relics of each rarity to the
  rarity deques BEFORE shuffle (via the Populate(IEnumerable, Rng)
  overload? No: Postfix runs after populate; instead use a Prefix that
  pre-populates? Simplest robust approach: Postfix on Populate(Player,
  Rng) that appends our relics to the internal _deques through the
  public PullFromFront-compatible path - NOT reachable. Therefore:
  Transpiler-free approach: Prefix on SharedRelicGrabBag getter is
  overkill; instead patch Populate(Player, Rng) with a FINALIZER-free
  Postfix that calls the existing public method Populate(IEnumerable,
  Rng)? It throws if already populated.
- DECISION: Harmony Prefix on RelicGrabBag.Populate(Player, Rng):
  when chaos enabled, we cannot easily replace the whole bag (vanilla
  relics should still drop). Alternative minimal-risk integration:
  our relics live in the shared pool via BaseLib (CustomRelicPoolModel
  IsShared=true -> SharedRelicPool registration), so
  Populate(Player,Rng) ALREADY includes them through
  SharedRelicPool.GetUnlockedRelics. That requires no Harmony patch on
  the engine at all. Rarity comes from ChaosRelicModel.Rarity override
  (deques keyed by rarity). Filtering by run seed happens at
  IsAllowed(IRunState): only slots rolled for this run's seed are
  allowed; other slots return false (they never appear).
- MP: IsAllowed is run-state based on both ends with same seed ->
  same allowed set. Same deterministic contract as AutoAnthony
  snapshots (without snapshot transport; both ends regenerate from
  seed).

### Config (BaseLib SimpleModConfig)

- EnableChaosRelics (default true): gate all content.
- ChaosRelicMultiplier (default 3): entry multiplier vs card baseline
  (1 = card-like counts).

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
