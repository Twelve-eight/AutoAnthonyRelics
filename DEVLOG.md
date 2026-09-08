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

## Session 35 (2026-09-08 03:4x) - v0.3: reward-pool integration fix

### Incident (user report 2026-09-07 23:5x)
"我并不觉得有任何遗物效果被随机" - run seed AZ49CAAUZK0F, 11 relic
obtains, ZERO chaos relics in rewards or console add. Log showed no
AUTOANTHONYRELICS obtain lines at all.

### Root cause (byte-verified against engine decompile)
Reward path: RunManager.InitializeNewRun ->
SharedGrabBag.Populate(ModelDb.RelicPool<SharedRelicPool>()
.GetUnlockedRelics(..)). That queries ONLY the ENGINE SharedRelicPool
singleton; its AllRelics = GenerateAllRelics() then
ModHelper.ConcatModelsFromMods(typeof(SharedRelicPool), ..) which
consumes ModHelper.AddModelToPool(typeof(SharedRelicPool), type).

Our v0.2 used [Pool(typeof(ChaosSharedRelicPool))] (own pool type) +
BaseLib IsShared registration -> appended to ModelDb.AllSharedRelicPools
(COMPENDIUM list) only. Never reached the reward deques. Same class of
bug would hit any BaseLib mod confusing the two registration surfaces.

### Fix (commit 45b2b5c)
1. [Pool(typeof(MegaCrit.Sts2.Core.Models.RelicPools.SharedRelicPool))]
   on ChaosRelicModel - engine-pool injection, the Spire1-mod pattern
   (its cards demonstrably reach rewards this way).
2. Early seed capture: Prefix on SetUpNewSingleplayer/SetUpNewMultiplayer
   (before InitializeNewRun populates the bag) so ChaosRelicModel.Rarity
   resolves real Definitions instead of Common fallback. Launch postfix
   kept as save-load fallback. Without this all 60 slots flood Common.
3. AfterCardPlayed random target: new Random() ->
   owner.PlayerRng.Rewards.NextInt (MP-desync fix, was TODO).
4. Removed dead ChaosSharedRelicPool class (registry kept, file now
   registry-only with full rationale doc).

### Verified
Build clean (0 warn/0 err), deployed 03:35, pushed 45b2b5c.
Game NOT running at deploy time (no dll lock).

### Awaiting live verification (next run)
- Chaos relics appear in elite/combat/shop relic rewards.
- Rarity spread matches generator (20 Common/20 Uncommon/20 Rare).
- Console: relic add AUTOANTHONYRELICS-CHAOS_RELIC005 -> Chinese name,
  entry-list description, counter, effects fire.
- Different seed -> different entries for same slot.

## Session 36 (2026-09-08 16:3x) - v0.3.1: dual-target patch bug, live evidence, icons, workshop staging

### Live evidence from user run (seed E1VC64KM4M7V, 15:21-15:22)
- User console `relic add AUTOANTHONYRELICS-CHAOS_RELIC005`: WORKED. Save file
  shows CHAOS_RELIC005 in players[0].relics between BURNING_BLOOD and
  GOLDEN_PEARL. User could not SEE it: the shared akabeko placeholder icon
  (all 60 icons byte-identical) rendered as an unfamiliar tiny blob.
- Grab bag save data: 59 chaos relics ALL in the Common deque, 0 Uncommon,
  0 Rare. Reward-pool injection itself works (relics enter the bag), but
  rarity resolution failed for every slot.

### Root cause 2 (byte-verified via offline Harmony repro)
A patch class with TWO [HarmonyPatch] attributes only patches the LAST
target. Repro harness (.tmp/harmony-repro, net9.0, loads real sts2.dll +
deployed mod dll, runs MainFile's exact CreateClassProcessor loop):
RunSeedEarlyTrackPatch patched exactly 1 method; SetUpNewSingleplayer had
NO patch info, SetUpNewMultiplayer did. Harmony 2.4.2 PatchClassProcessor
merges container attributes into one HarmonyMethod (last name wins).
=> singleplayer runs never early-captured the seed; every Definition
resolved null during bag populate; Rarity fell back to Common for all 60.

### Fix (commits f308a86, b4c1235, 6227815, a535328)
1. Split into RunSeedEarlyTrackSingleplayerPatch +
   RunSeedEarlyTrackMultiplayerPatch, both delegating to a shared static
   Capture(state). Repro now shows "SP patch info: PRESENT".
2. Placeholder icons: 60 distinct procedurally generated (hue-spread
   plaque, per-slot polygon sigil 3-8 verts, rim pips = slot%10), outlines,
   3x big icons, generic relic.png fallback. Fallback was previously
   MISSING (path resolved but never existed).
3. csproj CopyToModsFolder now also copies the packed .pck (mods-dir pck
   had been stale since v0.2; icons never reached the game until this).
4. AfterCardPlayed random target: PlayerRng.Rewards ->
   RunState.Rng.CombatTargets (vanilla Kusarigama pattern; stops consuming
   the reward RNG channel).

### Session-file repair (user report: one session won't open)
2026-09-06 jsonl had 13 lines with unescaped inner double quotes (auto
title from a user message containing quotes, plus build-error tool
outputs). Repaired 12 via structural-quote heuristic; 1 unrepairable line
(duplicate of a repairable sibling) dropped. Backup .bak-corrupt. All 20
sessions now parse.

### Workshop staging (a535328)
workshop/ folder: content payload (dll/pck/json), 512x512 preview.png,
bilingual DESCRIPTION.md, steamcmd VDF (publishedfileid empty = new item),
UPLOAD-GUIDE.md. steamcmd installed at .tooling/steamcmd (self-updated OK).
Publish blocked ONLY on Steam credentials/2FA - user chose to defer.
Official CDN installer URLs all 404; working mirror:
https://media.st.dl.eccdnx.com/client/installer/steamcmd.zip

### Still open
- Live run verification (user plays): expect "run seed early-captured" in
  godot.log at run start, 20/20/20 rarity spread in the save's
  relic_id_lists, chaos relics visible in reward screens with new icons.
- Workshop publish (needs Steam login).

## Session 36 addendum (2026-09-08 19:44) - LIVE VERIFICATION PASS

User run seed D99G6QDXSPGE (Ironclad, A10), godot.log evidence:
- L2094 `[AutoAnthonyRelics] run seed early-captured: D99G6QDXSPGE`
  (immediately after Embarking L2093) - split-patch fix confirmed live.
- Save grab bag: Common 19 chaos / Uncommon 19 chaos / Rare 20 chaos +
  1 in shop deque + 1 console-added = 60/60, mixed WITH vanilla per
  rarity deque (design contract: vanilla still drops).
- L2542 `Player 1 obtained RELIC.AUTOANTHONYRELICS-CHAOS_RELIC004
  from relic reward` - combat reward path.
- CHAOS_RELIC031 from a treasure chest (speedx TreasureAutoProceed
  tracked the pick).
- L2763 CHAOS_RELIC017 from a second combat reward.
- Shop offered Chaos Relic 25 (slot 24) - user reported seeing it;
  PullFromBack shop path works.
- `Inspecting Relic: ... CHAOS_RELIC005.title` + `..024.title` -
  localization resolves; ZERO "Could not find relic image" lines -
  all new icons resolve from the fresh pck.

End-to-end status: reward pool + rarity spread + treasure + shop +
console add + names + icons ALL verified live. Remaining known gaps:
effects per-entry (definitions fire on combat hooks - covered by
generator tests, not yet individually eyeballed in combat) and MP
full-run (seeded channels verified by code review only).

## Session 37 (2026-09-08 20:0x) - v0.4 live + entry-count balance

### v0.4 live verification (seed JK7SSSJD1RV2, user playtest)
- L1501 `relic descriptions updated for seed .. (60 slots)` - loc rewrite fires.
- L1503/1504 `pool replacement: removed 236/244 vanilla relics .. (60 chaos
  remain)` - both Populate overloads (player bag + shared bag) stripped.
- User confirmed in-game: shop/rewards show chaos relics only; two relics
  with 3 entries read as correct.

### Entry-count balance (user feedback: 9 entries = way too many; 3 = normal)
Data points from user: most relics 9 entries, one 6, two 3 - exactly the old
formula's clamp(3*(1+rank),3,15) => rank0=3, rank1=6, rank2=9.
New: entries = clamp(band-1+rank, 3, band+2), band = clamp(multiplier, 3, 5).
Simulated distribution (3000/rarity): Common 3@84%/4@12%/5@5%,
Uncommon 3@62%/4@27%/5@10%, Rare 3@47%/4@36%/5@17%.
NOTE: definitions are deterministic per seed but NOT persisted - the active
test run's slot definitions re-roll under the new formula on next load
(same behavior class as AutoAnthony card snapshots).
