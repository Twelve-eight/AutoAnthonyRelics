# DEVLOG - AutoAnthonyRelics

## Session 1 - 2026-09-07 - v0.1.0 first playable build

User order: "写一个使遗物也被东尼算法随机的mod,不过,每个遗物将获得
以前3倍数量的词条.就叫东尼算法 - 遗物(AutoAnthony - Relics)吧"

### What shipped (3f84277)

Standalone mod (id AutoAnthonyRelics, depends BaseLib 3.4.5):
- Code/Chaos/ChaosRelicGenerator: seeded per-run generation.
  Entry count = 3 x card-baseline weighted rank (AutoAnthony
  PickComponentCount weights Common/Uncommon/Rare), clamp 3..15.
  4-attempt unique name+effect-set recovery, widen-on-final-attempt
  (AdaptiveEffectCountWindow mirror). StableSeed = string hash.
- Code/Chaos/ChaosRelicCatalog: 16 entry templates across 6 hooks
  (BeforeCombatStart x8, AfterPlayerTurnStartLate x3, AfterCardPlayed
  x2, ModifyDamageAdditive, ModifyMaxEnergy, AfterCombatVictory).
- Code/Models/ChaosRelicModel: executes entries via CreatureCmd/
  PowerCmd/PlayerCmd/CardPileCmd. Rarity from definition. Icons
  res://AutoAnthonyRelics/images/relics/chaos_relicNNN.png.
- 60 slot markers (ChaosRelic000..059) in one file, block namespace.
- Pools/ChaosSharedRelicPool: IsShared -> BaseLib shared pool
  registration; no engine Harmony patches needed.
- Config: EnableChaosRelics (bool, default true) +
  ChaosRelicMultiplier (int, default 3).

### Entry-count distribution (JS mirror of C# roll, 3000 samples)

Common:    3条43.5% 6条40.7% 9条12.2% 12条2.7% 15条0.9%
Uncommon:  3条28.0% 6条33.9% 9条28.6% 12条7.6% 15条1.9%
Rare:      3条20.0% 6条27.6% 9条35.4% 12条13.4% 15条3.6%

(card baseline 1-5 entries x3 -> 3/6/9/12/15.)

### Build/deploy pipeline

- dotnet build -> CopyToModsFolder target deploys dll+json+pdb to
  <mods>/AutoAnthonyRelics/. PCK packed separately:
  dotnet .nuget/packages/bschneppe.sts2.pckpacker/0.1.1/tools/net9.0/
  any/StS2PckPacker.dll "AutoAnthonyRelics/" "AutoAnthonyRelics"
  <mods>/AutoAnthonyRelics/AutoAnthonyRelics.pck
  (pck source folder = mod/AutoAnthonyRelics/ containing images/ +
  localization/. NOT run by csproj yet - manual step after asset
  changes.)

### Incidents fixed this session

1. "PCK not found" at startup: manifest has_pck=true but no pck
   packed -> packed it (580KB with 180 icon files).
2. Icons 404: RemovePrefix keeps the underscore -> entry
   CHAOS_RELIC000 -> chaos_relic000.png. Renamed all files from
   chaosrelicNNN to chaos_relicNNN.
3. Startup crash (TargetInvocationException): BaseLib
   CustomRelicModel autoAdd requires [Pool] attribute - added
   [Pool(typeof(ChaosSharedRelicPool))] on the base class
   (attribute inheritance works; Spire1 does the same).
4. Push 404: repo not created on github -> gh repo create
   Twelve-eight/AutoAnthonyRelics --public, then push OK.

### Smoke state

- Game launches to main menu (16.5s) with mod loaded:
  "60 chaos relic slots, multiplier x3, enabled=True".
  godot.log errors unrelated to this mod (Spire1 Watcher bridge
  missing pool + AutoAnthony workshop mod's own Colorless-count
  audit failure, both pre-existing and non-blocking).
- NOT yet verified in-game: relic spawn via console, entry list
  display, per-run generation in an actual run. Next session:
  console `relic add` a CHAOS_RELIC id mid-run and check tooltip.

### Known limitations (v0.1.0)

- Static loc per slot (title/flavor/generic description); entry
  detail not rendered in description (count shown via counter).
  Follow-up: dynamic hover tip with the seed's entry list.
- AfterCardPlayed random target uses new Random() (not seeded) -
  MP desync risk; replace with combat-state Rng before MP testing.
- Placeholder icons (Spire1 akabeko art x60).
- IsAllowed gates slots to < TotalSlots but does not yet restrict
  per-slot by run seed selection set (all 60 of the run's seed
  pass; intended: all slots ARE the pool for that seed).
