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

## Session 38 (2026-09-08 21:1x) - entry counts 1/3/5 + icon + transform dedup

### Entry counts: literal 1/3/5 (user order "改为1-3-5")
rank tier: 0 -> 1, 1 -> 3, 2+ -> 5 on the same card-baseline weights.
Simulated: Common 43/42/15, Uncommon 28/35/37, Rare 20/27/53 (%).
Bounds 1..5 (was 3..7). Auto-deployed (dll 12:52Z build, hash-verified).

### Perfect enchantment icon (sts2-perfect, e2d325f)
User: "完美现在的卡图已经是那张图了" - the card portrait already IS the
architect art; copied card_portraits/perfect.png over
enchantments/perfect_enchantment.png. Deployed + committed.

### Double-generated-card dedup (ChaosBridge aaab5d3)
Root cause from run history (seed AZ49CAAUZK0F): PandorasBox at floor 18
transformed 2 strikes; CreateRandomCardForTransform samples WITH
replacement -> two identical CHAOS_CARD069. (Other decks' "duplicates"
were the player picking the same reward twice - normal.)
Fix: TransformBatchDedup in ChaosBridge - CardCmd.Transform prefix/finalizer
push/pop a thread-static batch exclusion; CreateRandomCardForTransform
prefix re-implements sampling over options minus already-produced ids.
Deterministic for MP. Deployed to mods/ChaosBridge (verified in dll).

### Note
User switched Steam account - game cannot be launched by the agent right
now; all three changes are deployed and await the next playtest.

## Session 39 (2026-09-11) - v0.5: point-budget entries + RRC/A4H key fix

### User orders this session
1. Relics are ALWAYS active (vs cards needing draw/play), so 1-3-5 entries
   is severely OP. Adopt MH-Rise qurious-crafting: rarity-scaled point
   budget, positives cost points, negatives refund points, final relic =
   "a few positives + one negative". Full template list to the user for
   default point assignment; everything player-configurable.
2. NEW subscribed mod RelicRewardChoices (3795496596, three-choice relic
   rewards) to be integrated.
3. Bug: RelicRewardChoices + Act4Heart - opened chests never award the
   Sapphire Key (skip or take). Fix INSIDE our mod.
4. Ship that fix ALSO as a standalone workshop mod (for RRC+A4H-only
   players); our package keeps it built-in by default.
5. Workshop push: dedicated push script (other sessions share
   triple2-push.ps1 - do not touch it).

### RRC + Act4Heart root cause (byte-verified, both dlls decompiled)
Vanilla+Act4Heart: chest skip -> SkipRelicLocally() -> OnPicked(null)
(A4H IL-patches the singleplayer guard) -> AwardRelics() -> A4H postfix
GiveKey_On_AwardRelics grants SapphireKey to no-vote players.
RRC replaces NTreasureRoom.OpenChest wholesale and ends the synchronizer
via CompleteWithNoRelics(); OnPicked/AwardRelics never run -> key never
granted, while A4H's skip-button key icon still renders.
Fix: postfix RelicRewardChoiceReward.OnSkipped, gated on chest-flow
(_sharedTreasurePoolOnly or TreasureLifetime), A4H keys_enable respected,
duplicate-grant guarded; grant = RelicCmd.Obtain(ModelDb.Relic<SapphireKey>()
.ToMutable(), player, -1) - exact A4H TryGiveKey semantics.

### Dual shipping (commits f9d7d6e, 1682690)
Shared source mod/Code/Compat/RrcTreasureKeyCompat.cs compiled by BOTH:
- AutoAnthonyRelics (MainFile TryInstall, default-on)
- standalone/RrcA4hKeyFix (workshop mod id RrcA4hKeyFix, own manifest,
  BaseLib+RRC+A4H deps) - StandaloneMain provides the namespace bridge.
Process-wide named mutex (Global\AutoAnthonyRelics.RrcTreasureKeyCompat.v1)
guarantees single install when both mods loaded. Both builds 0warn/0err.
Workshop staging: workshop-keyfix/ (vdf no fileid yet = new item, preview
key-blue, bilingual description) + .tmp/a4hkeyfix-push.ps1 (dedicated).

### v0.5 budget system (commit 1682690 + clamps/staging this session)
- Catalog: 27 positives (11 new: regen/thorns/artifact/poison-all/plating/
  turn-draw/block-add/victory-gold/passive-gold/rest-heal) + 10 negatives
  (frail/sloth self, per-turn HP loss, energy/draw/gold/attack/rest down,
  potion block, max-hp down on obtain via CreatureCmd.LoseMaxHp).
- Generator: budget spend -> negative roll (C/U/R 35/55/75%) -> refund buys
  more positives; unique-effect-set 4-attempt recovery kept. Defaults
  C/U/R budgets 10/16/24; per-template Cost_/Refund_ cfg overrides.
- Clamps: hand draw floor 1 (0-card hand bricks run), max energy floor 0.
- Config sliders (ConfigSlider) + zhs/eng loc for 6 new keys; multiplier
  key now legacy/idle (save compat).
- Smoke (throwaway .tmp/budget-smoke, 45 seeds): determinism, spend<=
  budget+refund, <=1 negative, <=6 positives, 20/20/20 rarity, unique
  names, in-band amounts - ALL PASS. Avg positives 2.8/3.9/5.0; negative
  rate 38/58/76%.
- ENGINE FACTS (all byte-verified): FrailPower = 0.75 block mult debuff;
  SlothPower limits cards/turn; NoDrawPower removes on turn end;
  PlatingPower = metallicize equivalent; Player.GetRelic<T>();
  TaskHelper.RunSafely; ModifyHandDraw/ModifyGoldGained/
  ModifyRestSiteHealAmount/ShouldProcurePotion all on AbstractModel.

### RRC interop with chaos pool (verified by code path)
RRC EnumerateAvailableCandidates -> IsAllowed(runState) -> our slot gate
(60/60 with seed) -> bag Remove -> 3 chaos candidates. All engine calls,
no unseeded randomness; structural compatibility, no code needed.

### Deploy state (03:31 build, v0.5.0)
mods/ + mods_disabled/ + workshop/content refreshed; pck repacked
(484277 B). VDF: description updated to v0.5 + keyfix compat note,
changenote v0.5.0. description-bbcode-v05.txt extracted for web edit.
TEMPLATE-POINTS-LIST.md = user tuning deliverable.

### Awaiting user
- Default point assignment review (TEMPLATE-POINTS-LIST.md) - budgets,
  chances, per-template costs; will apply as new defaults.
- Publish go: main item (v0.5.0) + NEW RrcA4hKeyFix item. VDF description
  field STRIP decision before push (web manual edits policy).
- Live verify: chest skip under RRC+A4H grants Sapphire Key; relic tooltips
  show pos+neg entries.


## Session 40 (2026-09-11) - 设置菜单平齐 (commit 73eb2cd)
- 专属设置页 RelicsSettingsSubmenu + RelicsSettingsScreenPatch: NSettingsScreen._Ready postfix 加组行 (Modding 行复制, 插在 AutoAnthony 本体组行后), NMainMenuSubmenuStack.GetSubmenuType prefix + ConditionalWeakTable registry 拦截, 页面内嵌 BaseLib SetupConfigUI (46 滑条原样复用).
- 效果: 退出 BaseLib "Mod 设置" 列表, 入口只在原版设置屏常规页 (与本体行为一致).

## Session 41 (2026-09-11) - 汉化对齐 + 重定价 + 预算编辑器 (commit 56ff101)
- **pck 破解**: Godot 4.5 pck v3 目录在尾部 (flags=2 = REL_FILEBASE 非加密). 工具 pckv3.py 提取主 pck 全部 zhs loc (49 表) + 各 mod pck. 详见 HANDOFF-2026-09-11.md.
- **locdump 命令**: 游戏内 dump powers/relics/keywords/cards/potions zhs 表 -> G:/omp works/.tmp/locdump/. 实测成功.
- **术语对齐**: 护体->人工制品, 镀层(残余)->覆甲, 怠惰->懒惰; 附魔: 锋利/灵巧/注能 (旧文案机制错误已修); 姿态: 愤怒双倍/平静离开时2能量/神格三倍+3能量+自动退出; 虚无/保留/奇巧/消耗文案对齐原版 keywords.
- **重定价**: 药水基准核实 (力量=2/敏捷=2/Common, 无人工制品药水; 核心电涌=1人工制品+11伤). StartArtifact 5->9 (catalog+config 同步), Min/Max 1,1->1,2.
- **VanillaRelicMapping**: 19 原版遗物 -> 词条映射 (数值反编译核实), 负面+额外池待扩充.
- **BudgetEditorPanel**: 每词条行 = 效果文本+悬停遗物chip+Min/Max滑条+每点计价 (live). 94 Min_/Max_ config 属性 + ApplyUserBounds. 构建 0/0, 已部署三处.
- **待办**: 实机验证编辑器 UI; 双端滑条改造 (用户原意一条线段两端滑块, 现为两个独立滑条); 映射表扩充; 全面重定价复审; 工坊发布确认.
- **交接**: 会话污染, 交接文档 HANDOFF-2026-09-11.md 已写.

## Session 42 (2026-09-12) - 主会话单线项目评估,未改产品代码

### 范围和结论
- 用户要求展开 HANDOFF 并单线审视项目全貌.本轮没有派子代理,没有把 Pending 当作实施授权,没有启动游戏/部署/发布,没有改任何产品 C# 或价格.
- 结论: 保留核心架构,但当前新增功能未达到发布状态.旧奖励池替换已有历史实机基础,核心预算生成本轮可运行;配置编辑器/额外池/计价参考/联机时序/双包钥匙补丁存在断点.先修契约,后调价格,不能只把两个滑条改成双端滑条就发布.
- 评估基线 HEAD=0d4304b,当时 master 与本地 origin/master 对齐且工作区干净.交接头部56ff101只是产品代码提交,后面已有交接/DEVLOG提交.原 Pending第6项已由Session40/41完成.
- 架构: Config -> Catalog/PointCosts -> Generator(60槽位) -> RunRegistry -> ChaosRelicModel/LocUpdater -> 奖励池与引擎hooks.设置页为横向配置能力,钥匙补丁为共享源码双程序集交付.无须整体重写.

### 本轮验证和边界
- 实际执行两次构建,均带 --no-restore -p:CopyToModsFolderOnBuild=false: mod/AutoAnthonyRelics.csproj 和 standalone/RrcA4hKeyFix/RrcA4hKeyFix.csproj.两包均0警告0错误,主包输出 PCK packed.所有环境/缓存/临时输出重定向G盘,未安装依赖.
- 隔离探针 G:/omp works/.tmp/aar-assessment-20260912/Probe.csproj 引用当前构建DLL及实际sts2/BaseLib/GodotSharp程序集.执行 dotnet run --project Probe.csproj --no-restore,退出0.探针输出存入仓库 assessment-2026-09-12-results.txt;脚本和反编译中间文件在上述临时目录,不承诺长期留存.
- 核心池额外池关闭,固定种子 AAR-ASSESS-0..44,共2700件:确定性,60件/种子,20/20/20稀有度,名字唯一,最多6正1负,三角计价预算不超支,数值在范围内,生成文本无占位符残留均无失败.范围属性未接通,故这里实际检查的是目录范围.
- 本轮正词条均值 C/U/R=2.906/3.843/4.836;负面率37.556/57.444/76.333%;正面花费均值14.456/22.713/31.869.花费包含负面返还,大于10/16/24不代表超支.这些结果不是战斗正确性/平衡/全配置可玩性证明.
- 游戏未运行,本轮也未启动.没有预算页视觉或拖动实测,没有实际宝箱发钥匙测试,没有联机整局测试.模型hook用受控内存fixture,失血检查是引擎结算分段调用,不冒称完整战斗.
- 当前引擎 release_info.json: v0.111.0,commit41cef1ea.编译BaseLib3.4.5,安装包/历史加载日志BaseLib3.4.6.依赖实现通过ilspycmd9.1定向反编译核对.
- 历史 godot.log mtime=2026-09-10T21:41:20.491587Z.其中618/625行分别记录主包/独立包 OnSkipped patched,作为历史双安装证据;并非本轮运行日志.另有 AutoAnthony 自身启动异常,不能归咎本mod.

### F01 - 额外池生成路径被计价入口截断 [已复现,发布阻断]
- ChaosRelicCatalog.cs:181-198 的 ChaosPointCosts.CostPerPoint/RefundPerPoint 只查核心目录.虽然 Generator.SpecOf 已解析额外目录,但可负担集合评估调用计价时仍抛异常.
- EnableExtraPool=true 后 Generate("AAR-EXTRA") -> InvalidOperationException: Unknown chaos relic template X_HAND_RETAIN.
- 需统一两个目录的计价与查询,不能只修正面,遗漏额外负面 Refund 路径.

### F02 - 范围配置和编辑器没有闭环 [已复现/依赖合约,发布阻断]
- AutoAnthonyRelicsConfig.cs:184-277 属性叫 Min_StartStrength/Max_StartStrength;309-351用模板值 C_START_STRENGTH 拼 Min_C_START_STRENGTH/Max_C_START_STRENGTH.47模板94预期键命中0.
- 直接调用 SetTemplateBounds(C_START_STRENGTH,2,2),SpecOf仍1..10,存储仍1..10.直接改Min_StartStrength/Max_StartStrength为2,SpecOf也仍1..10.
- BudgetEditorPanel.cs:252-260 裸 new NSlider.实际引擎 NSlider._Ready 必须 GetNode<Control>("%Handle"),_Process会使用该handle.当前未建立该子节点.这是明确场景合约不匹配,尚未打开预算页观察报错.
- BudgetEditorPanel.LocOf / RelicsSettingsSubmenu.TextOf / RelicsSettingsScreenPatch.TextOf 查 gameplay_ui 且没有.title;相关资源实际在 settings_ui,键带.title.引擎LocString按table/key精确取值,不存在自动转换.
- 94个bounds属性没有ConfigHideInUI;实际BaseLib SimpleModConfig会为所有未隐藏int属性生成滑条,不是注释所述仅作存储.主配置也未重写VisibleInModList,仍注册且可见,因此"退出BaseLib列表"的文档保证不成立.
- 自定义submenu另new配置实例,没有复用ModConfigRegistry.Get,没有接BaseLib NModConfigSubmenu的ConfigChanged保存计时或OnSubmenuHidden保存.自定义Persist也仅写静态属性.正常退出游戏仍有BaseLib全局保存兜底,不能说绝对不持久化;页面离开/重新实例化/异常退出可靠性仍未闭合.
- 懒惰编辑行用通用Render,遗留{M};有效生成路径有专门替换所以核心生成检查没有发现.当前确实仍是两个独立NSlider,不是要求的一条线段双端手柄.

### F03 - 核心战斗效果边界不正确 [隔离复现/引擎调用链,发布阻断]
- ChaosRelicModel.cs:287-291 N_TURN_LOSE_HP使用ValueProp.Unpowered,缺Unblockable.5格挡承受3点此标志伤害,引擎分段调用观测blocked=3,hpLost=0,hp=50,block=2.同一hook267-270先发回合格挡,负面可被同件遗物正面直接抵消.
- ModifyDamageAdditive:335-350 不检查攻击/卡源/ValueProp.受控模型配置攻击牌伤害+3,无卡源Unpowered伤害查询也返回+3.实际StrengthPower会检查IsPoweredAttack.
- AfterCardPlayed:302-330 只有懒惰计数检查card.Owner,出牌伤害与格挡未检查.实际Hook.AfterCardPlayed对所有战斗监听者广播,原版DaughterOfTheWind自己检查Owner.因此存在队友出牌触发自己的效果的语义错误,尚未双端实战.
- [INFERENCE] C_START_ENERGY 在BeforeCombatStart加能量,实际CombatManager之后首回合ResetEnergy=MaxEnergy,正常重置路径会覆盖此增益.原版Lantern在AfterSideTurnStart加能量.需实际首回合场景验收,不要仅凭签名匹配宣称生效.
- ShowCounter=true但未覆盖DisplayAmount;受控2词条遗物显示值查询为0.这是角标契约遗漏,不是主要发布阻断.

### F04 - 配置冻结/存档/联机时序没有定义完整 [已复现+推断,高风险]
- RunRegistry:20-43 仅按seed缓存,预算/单价/额外池/bounds/算法版本不在键内.同种子改预算后cacheUnchanged=true,直接Generate却freshGeneratorDifferent=true.
- 引擎SetUpNewMultiplayer:328-344内部才调用InitializeShared;本mod prefix已通过Capture -> OnSeedCaptured -> ForSeed生成缓存.邻接sts2-mpconfigsync在InitializeShared postfix广播配置,明显晚于本mod首次生成;接收端只是写属性及发Changed/ConfigReloaded,本mod不监听缓存失效.
- [INFERENCE] 主客机原始配置不同可能先生成不同遗物,后续同步属性也不能纠正已缓存定义.同配置同种子可重复不等于完整联机安全.需要真实双端不同初始配置场景.
- 定义没有写入存档,重启后按新配置/算法重建,旧存档持有的槽位可能变义.应先明确本局快照/冻结/版本约定.不能简单设置变动就清缓存,否则在役遗物会突然改变,一次性最大生命负面尤其危险.

### F05 - 原版参考表不是可信定价基准 [当前引擎反编译/已复现,发布阻断]
- 当前sts2.dll的DaughterOfTheWind=RelicRarity.Event,每攻击牌1格挡;映射写罕见/3格挡.TuningFork=每10技能牌7格挡;映射写每3技能牌4格挡.RingOfTheSnake=Starter;映射写普通.至少这三项已反证"全部数值字节核实"的当前适用性.
- VanillaRelicMapping.cs:91-94把Lantern首回合一次性能量挂到持续MaxEnergy模板.其他条件/频率不同引用可作类比,不能把N相乘叫同效果价格.
- OurPointsFor:121-134用catalog默认价格.力量live cost改19后Vajra reference仍5.价格显示和生成消费不一致.日后增加衰减映射还需统一三角计价.
- 先重建绑定引擎版本/来源的事实基准,区分完全对应/条件对应/仅类比,再完整重定价.本轮没有擅自改变用户已定人工制品9点.
- 当前PlatingPower首回合不减层,后续回合减层,三角累计结构可成立;但目录注释N=4 -> 12点与当前2*N(N+1)/2=20点不一致,不能复用旧注释作为证据.

### F06 - 双包安装去重失效 [已复现+历史日志,发布阻断]
- RrcTreasureKeyCompat.cs:95-136 using Mutex安装后释放/销毁.顺序两次同名Mutex创建均createdNew=true;历史godot.log618/625又分别记录两包均成功patch.
- 反证的是单次安装保证,没有证明已重复获得钥匙.发放为TaskHelper.RunSafely(RelicCmd.Obtain),已有钥匙检查与异步取得之间仍需验证竞态.
- 修复时覆盖主包单装/独立包单装/双装,跳过/领取,已有钥匙,keys_enable关闭,多人个人宝箱.在这些实际路径闭合前不发布独立条目.

### F07 - 额外池运行时状态存在第二层问题 [隔离复现/源码,高风险]
- ChaosRelicModel.cs:615-624在ModifyDamageAdditive计算期间清空_retainAttackBuff.受控攻击牌连续两次查询返回7,3,额外4点已消耗,未执行出牌.引擎Hook.ModifyDamage用于预览,会进入同一modifier管线.
- _retainAttackBuff设4后调用AfterCombatEnd仍4.本回合/本战斗的清零边界缺失.多段/多目标攻击及预览都需要实际场景验证.
- BeforeCombatStart对手牌附魔,早于常规首手抽牌;有提前抽牌效果才可能有目标.Nimble/Imbued循环未按CanEnchant筛选,Sharp先Take N再筛攻击也不等于前N张合法目标.
- 修好F01只让这些路径可达,不代表额外池完成.保留触发的hand->hand假设与Watcher反射调用仍需运行验收,本轮未确认它们有效.

### F08 - 可配置边界与平衡 [已复现,需明确契约]
- 滑条允许预算1/正面单价20/负面概率0.实际生成60件无词条遗物.预算不超支检查不能保证可玩结果;是否拒绝该组合/允许空遗物需要先明确,本轮不擅自添加策略.
- 正负效果抵消,多件永久引擎叠加,大额一次性负面退款,每回合治疗拖回合获利都不是当前生成不变量能覆盖的平衡问题.需按每战/每回合/每牌/全局/一次性分层,药水/卡牌只能当有条件的参考,不能直接按稀有度换价格.
- Generation的剩余初始预算未结转到refund阶段,注释"最多再买一条"也与循环可买多条不一致.这属于算法与描述的约定漂移,不要当作已经完整分配点数.

### F09 - 文档/证据/版本/交付漂移 [发布门禁]
- 当前实际26核心正面+10核心负面,额外10正面+1负面,共47模板.清单/DEVLOG旧文仍27正面;DEVELOP前半仍3倍/旧档位,后半才预算;TEMPLATE-POINTS-LIST人工制品仍5且1..1;英文工坊文本仍1/3/5.Manifest仍0.5.0而文档记v0.5.1.
- zhs/settings_ui.json仍有护体/怠惰/锋锐/轻盈/灌注,所以全量术语对齐未完成.动态名字/效果文本硬编码中文,不能把有eng JSON等同于完整双语支持.
- 交接指定 .tmp/budget-smoke/,.tmp/locdump/,.tmp/pck-extract/当前找不到;再次限定路径检索仍无相关smoke.csproj/pckv3.py.旧证据不可直接复跑,本轮重新建立隔离探针.项目.omp/hooks/pre/backup.ts缺失,本轮评估没有顺便修复备份机制.
- mods,mods_disabled,workshop/content三处已有DLL/PCK/manifest各自互相哈希一致.本轮禁部署新构建PCK相同,DLL不同;只报告字节差异,不据此推断部署语义陈旧.未覆盖三处.
- 维护性优点:目录/生成/执行大体分离,缓存有界,兼容补丁共享源码.风险:属性名反射无编译期保障,价格/范围/文案多份事实源,Definition.All每个hook反复分配数组,额外模板列表反复Concat/ToList.未做性能剖析,不把分配风险夸大为实际卡顿.
- 主工坊publishedfileid=3798163198;独立工坊无fileid.发布前仍需用户决定是否剥离VDF description以保护网页手改.未动任何push脚本,尤其未触碰triple2-push.ps1.

### 后续建议顺序 (仅评估,非本轮执行授权)
1. 冻结引擎/有效配置/旧存档契约,定义本局配置何时生效和存档恢复规则.
2. 修F01/F02:统一模板解析和范围键,复用注册配置,保存/重开闭环,精确loc键,真实双端滑条,实际预算页冒烟.
3. 修F03/F04/F06/F07:效果时点/拥有者/纯查询/状态清理/双包去重/联机冻结.按真实战斗,首回合,存读档,双端和宝箱矩阵验收.
4. 修F05/F08:版本化原版基准,全部模板统一消费/展示计价,再执行全面重定价和组合平衡检查.
5. 统一DEVELOP/清单/术语/manifest/工坊说明,持久保留复现工具与来源,修备份hook,实际场景通过后才部署/发布.
- 完整分类评估另存 assessment-2026-09-12.canvas.tsx.当前Windows会话没有对应G盘Canvas宿主,C盘禁止写入,故仅提供G盘Canvas源码与本DEVLOG文本,不宣称已在IDE内视觉验证.

## Session 43 - 2026-09-12 (F01-F08 修复 + 跨项目侦察)

### 结论
F01/F02/F03/F07/F08 已修复并构建通过(0 警告 0 错误, PCK packed), 探针全部断言翻转. F05/F06/F09 由子代理并行完成. 部署三处完成.

### 修复内容(commit 13ff225)
契约层:
- 新增 `ChaosTemplates`: 两池统一解析器(spec/pool/price). `ChaosPointCosts` 与生成器预算下限都走它; 原来只查核心池, 额外池任何模板都会抛 InvalidOperationException(探针 X_HAND_RETAIN).
- 94 个 Min_/Max_ 属性改名为模板 id 形式并加 `[ConfigHideInUI]`: 原名与查找键不一致(0/94 命中), 且 BaseLib 会把它们全渲染成设置滑块.
- 属性反射从每次 Array.Find 改为一次性 name->PropertyInfo 索引.
- `ChaosRelicRunRegistry` 缓存键改为 seed + 配置指纹: 原来只按 seed, 配置变更后仍返回旧池(探针 cacheUnchanged=true).
- 删除重复的 `X_HAND_ETHEREAL_NEG` 常量陷阱.
- uniqueOnly 模板集真正生效; 每件遗物保证至少一条正面词条, 预算低于下限时抬到下限(F08 契约).

战斗语义:
- 战斗开始能量从 BeforeCombatStart 移到 AfterSideTurnStart(vanilla Lantern): SetupPlayerTurn 的 ResetEnergy 会覆盖.
- 战斗开始抽牌改为回合 1 的 ModifyHandDraw 加成(vanilla BagOfPreparation).
- `N_TURN_LOSE_HP` 加 `ValueProp.Unblockable`: 原来被格挡完全吸收(探针 blocked=3, hpLost=0).
- `ModifyDamageAdditive` 门控为已充能攻击 + 所有者可变的攻击牌(vanilla StrengthPower), 且改为纯读取; 保留增伤改在 AfterAttack 消费, 伤害预览不再吃掉它.
- `AfterCardPlayed` 自门控所有者(vanilla DaughterOfTheWind).
- ShowCounter/DisplayAmount 报告词条数.
- 额外池附魔改到玩家第一回合(此时手牌存在), 每个候选都先用附魔自己的 CanEnchant 校验; Sharp 先过滤再取 N; 虚无跳过已有该关键词的牌.
- 保留触发改挂 AfterFlush(引擎真正携带保留牌列表的钩子); 原来挂 AfterCardChangedPiles, 保留不产生手牌到手的移动, 所以从未触发.
- 保留增伤与延迟能量在战斗结束时清零.

UI:
- 新增 `RangeSlider`: 单轨双端手柄, 自包含. NSlider._Ready 要求 `%Handle` 子节点且每帧解引用, 裸 `new NSlider` 无法渲染.
- `BudgetEditorPanel` 编辑注册的配置实例并安排保存; 设置页接上 BaseLib 的防抖计时器 + OnSubmenuHidden 落盘.
- 本地化查询改到 `settings_ui` 并带 `.title` 后缀(BaseLib 自己的约定); 原来查 `gameplay_ui` 且无后缀, 必然落空.
- 怠惰行用生成器的渲染器显示真实手牌上限.

### 探针对比(修复前 -> 修复后)
```
boundKeys                0/94  -> 94/94
boundsAfterEditorCall    [1,10]->[2,2]
extraPool                InvalidOperationException -> completed
sameSeedConfigChange     cacheUnchanged=true -> false
legalUnaffordableConfig  emptyRelics=60 -> 0
unpoweredDamageBonus     3 -> 0
displayCounter           shown=0 -> shown=2
loseHpFlagSemantics      blocked=3,hpLost=0 -> withUnblockableBlocked=0
damageQueryConsumes...   first=7,second=3,remaining=0 -> first=4,second=4,remaining=4
retainedBonusAfterCombatEnd  4 -> 0
slothEditorText          含 {M} -> 显示 2
```
新增断言: `installationDedup={"mutexMembers":0,"usesPatchInfo":true}`, `cheapestFloor=20`.
探针输出存档: `probe-2026-09-12-after-fix.txt`.

### 实机日志证据(修复前, godot.log 618/626 行)
两个包都报告 `active: ... OnSkipped patched`, 确认 F06 双重打补丁. 修复后第二个包会走 patch-info 分支转为 dormant.

### 跨项目侦察结果
- sts2-heartshake: 已完成并发布(fileid 3799286717 已核实), 仅文档漂移.
- sts2-mpconfigsync: MP 接收路径从未在真实第二端执行过; 文档仍描述已废弃的 Save() 设计.
- sts2-regentfxfastboot + sts2-boottimer: project.godot 的 config/name 与 assembly_name 仍是脚手架残留 "Perfect", GlobalUsings 注释指向 MpConfigSync. 已修复并推送(afa6e60).
- chaosbridge: DEVELOP.md 文件布局漏 TransformBatchDedup.cs; DEVLOG 缺 2026-09-08 条目.
- aftp: 好友包内的 AFTP dll 落后于 fork 构建(MD5 317ad034 vs 58310ad9).
- sts2-spire1: v1.1.0 已构建但 DEVLOG 未记录; workshop VDF 描述仍是 v1.0.0 的 233 卡/28 遗物.

## Session 43 补记 - 跨项目审查与修复

### 已完成的其他项目修复
- sts2-regentfxfastboot + sts2-boottimer: `mod/project.godot` 的 `config/name` 与
  `project/assembly_name` 仍是脚手架残留 "Perfect", `GlobalUsings.cs` 注释指向
  MpConfigSync. 已改名为各自 mod 名并重新构建通过(regentfx 0 错误, boottimer 0 错误).
  regentfx 已提交推送 (afa6e60); boottimer 不是 git 仓库, 仅本地修改.
- sts2-spire1: manifest 描述与 workshop VDF 描述/changenote 仍写 v1.0.0 的
  233 卡/28 遗物/53 事件, 而版本字段已是 1.1.0. 已同步为 230 卡/22 遗物/6 独有事件
  并更新 changenote (396806e, 已推送).
- sts2-spire1/dist: 记录好友包重建待办 (dist/REBUILD-PENDING.md) - 包内 Spire1 是
  0.9.2(现 1.1.0), AFTP fork dll MD5 317ad034 落后于当前 58310ad9(family-C/D 联机
  极性修复), 已附重建步骤 (492f0e5, 已推送).
- sts2-heartshake: DEVELOP.md 音频设计段落仍在讲 NDebugAudioManager 与未决的
  [INFERENCE], 实际实现是 FileAccess 原始字节 + AudioStreamOggVorbis.LoadFromBuffer;
  验证清单仍标"无法自动验证"而用户早已双确认; 配置注释把音量归因于调试音频管理器.
  全部修正 (99dec48, 已推送).
- sts2-mpconfigsync: DEVELOP.md 三处描述已被 772958af 取代的 Save() 设计(状态行、
  应用步骤、联机冒烟观察点), 且未记录会话级改造丢掉了启动期键的落盘兜底. 已修正并
  把该缺口写成明确待决项 (c5ef568, 已推送).
- chaosbridge: DEVELOP.md 第 6 节工程结构漏了 src/TransformBatchDedup.cs (规则 D).
  已补入 (9d9ad4a, 已推送).
- sts2-perfect: DEVELOP.md 头部把 CombatScout 标为 pending(实际已交付), 开放问题未记
  游戏内获取仍未验证. 已修正 (95cd793, 已推送).

### 实机冒烟的关键发现
autoslay 跑了两局(seed AARFIX1 / AARFIX2), 均推进到第二章宝箱房后在
"Proceed button not enabled after picking relics" 处失败. 做了 A/B 对照: 把
`EnableChaosRelics` 关掉用同一种子重跑, 仍在同一步失败, 且日志中奖励池替换次数为 0,
宝箱全部由第三方 mod RelicRewardChoices 接管(候选是纯原版遗物).
结论: 该失败属于 RelicRewardChoices 与引擎 TreasureRoomHandler 的交互, 与本模组无关.
本模组自身在整局中零报错.

### 仍未验证 (不声称通过)
- 设置页 UI 的实际渲染与拖拽(autoslay 不进设置界面).
- 额外效果池 (EnableExtraPool=true) 的实机行为.
- 联机双端一致性(需要第二个客户端).

## Session 44 - 2026-09-12 - 改名 / id 迁移 / 文本修复的调研 (未实现, 仅落盘)

用户指令: 从 HANDOFF-2026-09-12.md 展开工作. 后续追加两条:
每个步骤的修改过程只写 DEVLOG, 聊天只报方向/决断/问题; 然后整理已有工作,
落盘断点全貌, 交由其它 agent 继续开发.

本轮**零代码改动**(只有 research/ 下的调研与验证产物). 以下为全部已验证结论.

### 0. 本轮用户裁定的方向 (覆盖交接 0.1 的约束)
- 显示名: zh `怪异炼化 - 遗物`, en `Qurious Crafting - Relics`.
- **mod id 要改**: `AutoAnthonyRelics` -> `QuriousCraftingRelics`.
  这推翻交接 0.1 的 "只改显示名, 不要改 id"; 理由是研究完原版机制后要做
  真正复原特性的东尼算法遗物, 旧 id 腾给它.
- id 迁移现在就做, 与改名/文本修复一起, 只构建部署一轮.
- 范围: 交接第 8 节 8 步按顺序全做.

### 1. (A) 配置文本显示为变量名 - 根因实测确认
- BaseLib `ModConfig.GetLabelText` (BaseLib-StS2/Config/ModConfig.cs:499-503)
  用 `StringHelper.Slugify(名字)` 拼 `{ModPrefix}{slug}.title`, 查不到就显示原名.
- `Slugify` = CamelCaseRegex `([A-Za-z0-9]|\G(?!^))([A-Z])` -> `$1_$2`,
  再 `\s+` -> `_`, 再删 `[^A-Z0-9_]`, 全大写.
- 152 个候选名中 145 个失效; 只有 7 个驼峰名正常
  (ChaosRelicBudgetCommon/Uncommon/Rare, ChaosRelicMultiplier,
  ChaosRelicNegativeChanceCommon/Uncommon/Rare).
- **修法 (已验证)**: 名字按下划线切段, 每段首字母大写其余小写. 改名后 Slugify
  是不动点, 恰好等于**现有** loc 键 => **loc 文件零改动**.
  已核对: 47 个 Cost_/Refund_ + 4 个区块名 = 51/51 命中现有 loc 键;
  94 个 Min_/Max_ 无 loc 键且带 [ConfigHideInUI], 不渲染, 无需 loc.
- 区块名同理: `[ConfigSection("BUDGET_SECTION")]` -> `"Budget_Section"`.
- 孤儿键: `AUTOANTHONYRELICS-RESTORE_DEFAULTS_BUTTON.title` 是死键,
  BaseLib 的恢复默认按钮走 `GetBaseLibLabelText` -> `BASELIB-RestoreDefaultsButton`.

### 2. BaseLib 没有配置迁移钩子 (新发现)
grep 过 `Config/ModConfig.cs` 与 `Config/SimpleModConfig.cs`: 无 Migrate,
无 OnLoad / 版本号. 唯一相关是 `RestoreDefaultsNoConfirm` (ModConfig.cs:179, virtual).
=> 改属性名 = 旧 cfg 键失效回退默认值, 必须自己写迁移.

### 3. 配置加载时机 (决定迁移放哪)
`ModConfig` 构造函数 -> `CheckConfigProperties(); Init();`
`Init()` (ModConfig.cs:193-203): `if (File.Exists(_path)) Load(); else Save();`
触发点: `MainFile.Initialize()` 第 28 行 `new AutoAnthonyRelicsConfig()`, 同步执行.
=> 迁移必须放在第 28 行**之前**; 顺序是确定的, 不依赖任何巧合.
`_path = Path.Combine(OS.GetUserDataDir(), "mod_configs", filename)`,
filename = 根命名空间(去特殊字符) + ".cfg".

### 4. ModPrefix 与 cfg 文件名来自根命名空间, 不是 mod id
`BaseLib.Extensions.TypePrefix.cs`: `GetPrefix()` = 命名空间首段大写 + "-";
`GetRootNamespace()` = 命名空间首段. => 改命名空间会同时改 loc 键前缀与 cfg
文件名; 只改 mod id 不会.

### 5. id 迁移的连带面 (已查清, 尚未执行)
- 引擎 `ModManager.ReadModsInDirRecursive`: 递归扫 mods/ 找 *.json, 目录名不必等于 id.
- `TryLoadMod`: `Path.Combine(mod.path, modId + ".dll")` (ModManager.cs:796)
  与 `... + ".pck"` (:818) => **dll/pck 文件名必须等于 manifest id**.
- csproj `<ModId>` 驱动: `$(ModId).json` / `$(ModId)/localization/**` /
  `$(ModId)/**` 编译排除 / pck 内容 / 拷贝目标 `$(ModsPath)$(ModId)/`.
- 程序集名默认来自 csproj 文件名 => `AutoAnthonyRelics.csproj` 也要改名.
- `project.godot`: `config/name`, `config/icon` 的 res:// 路径,
  `[dotnet] project/assembly_name`.
- `MainFile.ModId` 与 `ResPath = res://{ModId}`.

### 6. 用户 cfg 里不能丢的调价 (迁移必须保住)
取自 `C:/Users/o_Obl/AppData/Roaming/SlayTheSpire2/mod_configs/AutoAnthonyRelics.cfg`:
`ChaosRelicBudgetRare=30`(默认 24), `Cost_C_START_ARTIFACT=5`(默认 9),
负面概率 10/30/75(默认 35/55/75), `ChaosRelicMultiplier=0`.

### 7. (B)(C) 修复点
- `BudgetEditorPanel.EffectText` (BudgetEditorPanel.cs:196-197) 调
  `ChaosRelicGenerator.RenderOperation(spec, spec.Max)`, 所以显示具体数字.
- `RenderOperation` (ChaosRelicGenerator.cs:231-234) 对懒惰用 `{M}` -> `7-amount`.
- 期望: 编辑器行显示字面 `N`; 懒惰显示 `(7-N)`. 生成出的遗物实际描述仍显示具体数字.
- `RenderOperation` 的三处生成器调用 (:148, :187, :217) 必须保持原语义.

### 8. 原版东尼算法反编译研究 (已完成)
`research/original-autoauthony-contract.md` (647 行). 要点: 481 张卡离线拆成
931 个原子片段; 条件 `RuntimeTriggerSpec` 与效果 `OperationRuntimeSpec.Opcode`
在数据层独立, 由 `LinkedTriggerIndex` 事后绑定, 普通触发有 50% 概率不绑定;
随机源 `System.Random(SHA256("AutoAnthony/v111/all-pools-v2/{角色}/{seed}"))`, 可复现;
诅咒不在随机池; `CombatsSeen` 在原版 IL 中出现 0 次.

### 9. 本轮新增落盘产物
- research/original-autoauthony-contract.md
- research/config-slug-map.tsv (152 行 x 4 列: 现名 / 现 slug / 建议新名 / 新 slug)
- research/tools/slug-map.ps1 (重新生成上表)
- research/tools/verify-loc-keys.py (核对新名是否命中现有 loc 键)
- research/tools/slug-ground-truth.txt (真 .NET 正则实测样本)

## Session 45 - 2026-09-12 - 改名 / id 迁移 / 文本修复 (实现) + 原版映射回填

依据: `HANDOFF-2026-09-12-PT2.md` 的 D1-D7, 范围 = `HANDOFF-2026-09-12.md`
第 8 节 8 步. 本会话完成第 1-2 步, 并实现第 5 步; 第 3 步(契约)已在上一会话完成.

### 第 1 步 - 改名 + 三处文本修复 + id 迁移 (完成)

- 机械改名: 43 个文本文件 + 5 处重命名 + 4 个 payload 重命名, 残留 0.
  `AUTOANTHONYRELICS-` -> `QURIOUSCRAFTINGRELICS-` (loc 前缀),
  `AutoAnthonyRelics` -> `QuriousCraftingRelics` (id/命名空间/csproj/程序集),
  `东尼算法 - 遗物` -> `怪异炼化 - 遗物`, `AutoAnthony - Relics` -> `Qurious Crafting - Relics`.
- 仓库根目录 `G:/omp works/AutoAnthonyRelics` 保持不动 (用户裁定).
- 属性名迁移: 146 处, 152 个名字全部命中 (0 ZERO-HIT).
  规则 = `TitleSnake` (按 `_` 切段, 每段首字母大写其余小写), 使 BaseLib 的
  Slugify 成为不动点, 因此 loc 键零改动. 规则收在 `Code/ConfigKeyNaming.cs`.
- cfg 迁移: `Code/ConfigMigration.cs` (新建). BaseLib 无迁移钩子, 迁移必须在
  `MainFile.Initialize()` 里 `new QuriousCraftingRelicsConfig()` **之前**执行.
  **关键教训**: 迁移函数一度用 `MainFile.Logger` 打日志, 在 Godot 外触发
  `Godot.OS..cctor()` 原生访问违例 (0xC0000005) —— try/catch 接不住原生 AV.
  已改为 `MigrateLegacyConfig()` 只返回 `ConfigMigrationReport`, 由调用方
  (真的在 Godot 内) 打日志.
- 文本修复 (A)(B)(C): 新增 `ChaosRelicGenerator.RenderEditorText`, 编辑器行显示
  字面 `N` / `(7-N)`; `RenderOperation` 三处生成器调用语义不变; 删除死键
  `RESTORE_DEFAULTS_BUTTON` (zhs/eng 各 1 行).
- loc 覆盖校验: 154 行中 94 个 Min_/Max_ 隐藏不渲染, 60 个非隐藏全部命中,
  未解析的非隐藏键 = 0.

### 第 2 步 - 构建 + 探针 + 部署三处 + 实机冒烟 (完成)

- 构建: 主 mod 与孪生包 `RrcA4hKeyFix` 均 0 警告 0 错误, 主 mod 打印 `PCK packed`.
  **环境坑**: 本 Bash 沙箱缺 `APPDATA`/`PROGRAMDATA`/`ProgramFiles*`, 导致
  NuGet 报 `Value cannot be null. (Parameter 'path1')` + `MSB4236 Godot.NET.Sdk 找不到`.
  修法 = `mod/.tmp/dotnet-env.py` 包装器补齐环境变量 (bash 无法 export 带括号的名字).
- 隔离探针 (全绿): catalog 计数正确; 94 个 Min/Max 键全部命中; 编辑器写入路径通;
  2700 次核心 4-5 槽生成 0 违例 0 不确定; 编辑器文本含占位符 = false;
  cfg 迁移四段验收全通 (150 键 150 保留, 5 项用户调价原样, 幂等, 合并保留已有,
  损坏文件不吞异常且不删旧文件).
- 部署三处: `mods/`, `mods_disabled/`, `workshop/content/`; 旧副本移到
  `.tmp/removed-AutoAnthonyRelics-deploy/` (保留可回滚).
- 实机冒烟 (seed QCRREN1): 第 1 轮全部 mod 被 "user has not yet seen the mods
  warning" 跳过 —— autoslay 在该弹窗上点了"加载 mod"后退出, 确认写入 settings;
  第 2 轮 mod 真正加载. 引擎层证据:
  `Found mod manifest file ...\mods\QuriousCraftingRelics\QuriousCraftingRelics.json`,
  排序表第 12 位 `Qurious Crafting - Relics (怪异炼化 - 遗物) (QuriousCraftingRelics)`,
  日志中 `AutoAnthonyRelics` 残留计数 = 0.
  运行期证据: `initialized: 60 chaos relic slots, multiplier x3, enabled=True`;
  `RRC key compat: dormant: RelicRewardChoices and/or Act4Heart not loaded`;
  `relic descriptions updated for seed QCRREN1 (60 slots)`;
  `pool replacement: removed 236 / 244 vanilla relics from the run grab bag`;
  `run seed captured: QCRREN1`; 本 mod `[ERROR]` 计数 = 0.
- **事故 (已处置)**: 冒烟快照 (06:20) 之后, 实机目录
  `SlayTheSpire2/mod_configs/AutoAnthonyRelics.cfg` 不再位于原位, 且无
  `.v0.5.1.bak`. 因此第 2 轮 `MainFile.Initialize` 未找到旧文件,
  `QuriousCraftingRelics.cfg` 以**默认值**新建 (用户调价丢失: BudgetRare 30->24,
  Multiplier 0->3, NegativeChanceCommon 10->35, Uncommon 30->55,
  Cost_C_Start_Artifact 5->9). 用户调价完整保存在
  `.tmp/smoke-prep/AutoAnthonyRelics.cfg.orig` (06:20, 权威) 与
  `.tmp/AutoAnthonyRelics.cfg.bak` (04:28, 旧). 根因未定位, 疑为本会话的准备步骤
  把旧文件移走; 教训是**实机状态快照要连 cfg 一起做双份 (原位 + 仓库存档)**.
  处置: 从 06:20 快照把旧文件放回原位, 把默认值文件移到
  `.tmp/smoke-prep/QuriousCraftingRelics.cfg.defaults-created-by-run2` 留证,
  然后跑第 3 轮验证迁移.

### 第 2 步收尾 - 第 3 轮实机冒烟 (seed QCRMIG1): cfg 迁移端到端验证通过

日志 (真实游戏内):
```
[QuriousCraftingRelics] cfg migrated: 150 legacy keys, 150 carried over
    (0 already present); old file kept as AutoAnthonyRelics.cfg.v0.5.1.bak
[QuriousCraftingRelics] RRC key compat: active: RelicRewardChoices + Act4Heart
    detected, OnSkipped patched
[QuriousCraftingRelics] initialized: 60 chaos relic slots, multiplier x0, enabled=True
```
`multiplier x0` 即用户调价 (默认 x3), 说明迁移无损. 逐键全量比对:
旧 150 键 -> 141 个被重命名 -> 丢失 0 / 值不一致 0 / 多出 0.
`AutoAnthonyRelics.cfg.v0.5.1.bak` 与新的 `QuriousCraftingRelics.cfg` 均已生成.

三轮冒烟把孪生包的两条分支都跑通了: 第 2 轮 `dormant`, 第 3 轮 `active:
... OnSkipped patched`.

**状态恢复**: 按承诺把 mod 列表恢复原样 —— `BaseLib` 与 `Spire1` 从 `mods/` 移回
`mods_disabled/`, 与 `.tmp/smoke-prep/` 的两个快照 `diff` 逐行一致 (11 / 17 项).

### 第 6 步 - 消除字典枚举顺序依赖 (完成, 零行为变化)

- **先取证再改**: 探针新增 `templateOrder` 转储, 三个独立进程跑出的顺序
  字节级一致 (`md5sum` 相同), 且等于源码声明顺序 —— 证实 .NET 9 的 Dictionary
  在实践中按插入顺序枚举. 但"实践上稳定"不是契约.
- **改法**: 两个 catalog 的 `Specs` 从 `Dictionary<string, TemplateSpec>` 字面量
  改为**显式顺序数组** (`TemplateSpec[]`), 另建 `SpecsById` 只做查找.
  核心 36 条 + 额外 11 条机械转换.
  - 顺序成为单一来源的契约, 不再依赖字典枚举.
  - 数组顺序**刻意等于**旧字典的枚举顺序, 所以**没有任何 seed 的产出改变** ——
    这一点用改造前的 `templateOrder` 作黄金参照回归验证, 字节一致.
  - 新增模板必须**追加**到所属段落末尾 (写进代码注释); 插在中间会改变
    seed->遗物 的对应关系.
- `ConfigFingerprint` 早已对模板列表 `Sort(StringComparer.Ordinal)`, 不依赖顺序,
  故无需改动.
- 回归: `core45Seeds` 的 total/deterministicMismatches/violations 与各项均值
  改造前后完全相同.

### 第 7 步 - 7.2 遗留三项 (完成)

1. `X_RETAIN_ATTACK_BUFF` 文案与时机不符 **[已修]**.
   实测: buff 由 `AfterFlush` (回合结束) 发放, 且 `_retainAttackBuff` 只在
   `AfterCombatEnd` 清零 (不按回合). 所以旧文案"本回合你的下一张攻击牌"是错的
   (它指的那个回合已经结束), 而"下个回合"又过窄 (不攻击就会一直留着).
   改为无时间限定: `每当你保留一张牌时,你的下一张攻击牌伤害+{N}.`
2. `OurPointsFor` 用 live 定价 **[已在第 5 步修]**.
3. `PickAffordablePositive` 的 `UniqueOnly` 是否真生效 **[已实测确认生效]**.
   代码里 `UniqueOnly.Contains(t)` 会 `continue` (硬排除). 探针在
   `EnableExtraPool=true` 下生成 400 个种子, `UniqueOnly`
   = [T_START_ENERGY, PASSIVE_MAX_ENERGY, T_START_DRAW] 三个模板
   **一次都没有被选中** (`pickedAny=[]`).
   另: `PlayerCombatState?.TurnNumber <= 1` 的 null 语义也做了实测 ——
   C# 提升关系运算符在任一操作数为 null 时返回 `false`, 探针确认
   `null<=1=false` / `null>1=false`, 所以非战斗场景不会误发首回合能量
   (三处调用点: ChaosRelicModel.cs:324, :347, :505).


### 第 5 步 - 原版映射回填 (已实现, 待构建验证)

- `Code/Chaos/ChaosTemplates.cs`: 新增 `RefundOf(spec, costs, amount)`.
  `ChaosPointCosts.CostPerPoint` 对负向模板刻意返回 0, 所以负向必须走
  `RefundPerPoint`, 否则悬停会显示 0 点返还.
- `Code/Chaos/VanillaRelicMapping.cs`: 按 §7.1 实测数据集整体重写.
  - 修正 8 处矛盾: DaughterOfTheWind(事件遗物/每次攻击+1格挡)、
    TuningFork(罕见/每10张技能牌+7格挡)、RingOfTheSnake(初始遗物/仅首回合)、
    BeltBuckle(商店遗物/无药水为条件)、**Lantern 从 PASSIVE_MAX_ENERGY 移到
    StartEnergy** (原版无任何遗物提升能量上限)、EmberTea(接下来 5 场)、
    PhilosophersStone/BlessedAntler(先古遗物).
  - 中文名修正: 奥利哈钢 / 赐福鹿角 / 风的女儿 / 蛇之戒指 / 腰带扣.
  - 稀有度标签改用引擎自己的 zhs 文案: 初始/普通/罕见/稀有/商店/事件/先古遗物
    (旧表的"远古"在引擎里不存在).
  - 补齐实测有对应物的空组: StartVulnAll(弹珠袋)、StartWeakAll(红面具)、
    StartPlating(护喉甲)、RestHealBonus(皇家枕头)、NegMaxHpDown(树叶药膏/原初之爪),
    并扩充 TurnStartEnergy/TurnStartDraw/PlayBlock/PlayDamageRandom/
    PassiveAttackDamage/VictoryHeal.
  - 逐条渲染 zhs 描述 (BBCode 已剥离, `{Var}` 按反编译数值代入), 不再照抄占位符.
  - `OurPointsFor` 改走 `ChaosTemplates.Spec` (双池) + live
    `QuriousCraftingRelicsConfig.PointCosts` + `PriceOf`/`RefundOf`
    (Decaying 走三角定价); 旧的 `ChaosRelicCatalog.Spec` + `spec.CostPerPoint`
    两处缺陷消除.
  - 新增 `TemplateByRelicId` 反向索引, 取代对 Dictionary 的线性扫描.
  - **决断**: 每个模板最多挂 4 枚芯片 —— 编辑器芯片是不换行的 HBoxContainer
    (hint 宽 760), 更多会横向溢出. 选取偏好无条件/持续效果.
  - **决断**: 只有"N 的含义与我们的模板一致"的原版遗物才给价格. 因此
    TuningFork / OrnamentalFan (每 N 张牌)、Tingsha (弃牌时)、Kusarigama /
    LetterOpener (每 3 张牌)、Pendulum / PollinousCore (每 N 回合)、
    BowlerHat (百分比) 不给价格, 悬停显示"不适用", 但 EffectNote 仍列出原版数值.
    作用范围更窄但同含义的 (打击木偶/微型大炮/神秘打火机) 仍给价格, 由
    EffectNote 说明限制.
  - 已验证为空的原版组保留空数组并写明核实依据 (Regen/Artifact/战斗开始群体伤害
    与中毒/能量上限/格挡加成/战斗胜利金币).

### 第 8 步 - MpConfigSync 三缺陷 + 两项确认 (完成)

范围由用户裁定: **只做 MpConfigSync 的三个已确认缺陷**, 四个跨仓库审查
(Heartshake / MpConfigSync / Perfect / ChaosBridge) 不做.

- 缺陷 #1 `MainFile.cs`: `harmony.PatchAll` 外包**单个** try/catch (与注释"逐类型
  隔离"矛盾, 一类抛异常则整批失效) -> 逐类型 `CreateClassProcessor(type).Patch()` +
  每类独立 try/catch + `HasHarmonyPatch(Type)` 预筛 + 每类日志与末尾汇总.
- 缺陷 #2 `ConfigPropertyScanner.Scan`: `FlattenHierarchy` 会多返回**继承的**
  public static 属性 (BaseLib 的 `CheckConfigProperties` 不持久化它们) ->
  逐条对齐 `BaseLib/Config/ModConfig.cs:143-153`.
- 缺陷 #3 `ConfigSyncMessage.ShouldBuffer`: 保持 `ICustomMessage` 默认 `true`,
  **判定为刻意行为**, 只补 XML 文档不改代码. 证据链: 开缓冲
  `StartRunLobby.cs:498` / `LoadRunLobby.cs:320` -> 本 mod 在 `InitializeShared`
  postfix 发送 -> 释放于 `RunManager.Launch()` (`RunManager.cs:711-717`,
  `SetBufferMessages(false)`), 由 `NGame.LoadRun` / `NGame.StartRun` 调用, 晚于
  `SetUp*`. 改成 `false` 会在初始化中途投递, 严格更差.
- 两项"另需确认"均已由源码查实 (不再是推理):
  - `RunManager.CleanUp` 的恢复路径覆盖全部结束方式 —— 胜利/死亡、局内放弃
    (单机 + 联机主机 + 客户端, 均经 `GuaranteeKillAllPlayers` -> 死亡)、
    暂停菜单保存并退出、暂停菜单断开、本端掉线 (`LocalPlayerDisconnected`)、
    关窗口 (`NRun._Notification(1006)`)、Steam 覆盖层加入好友局、主菜单继续失败;
    唯一不经过的主菜单放弃存档局此时 `State == null`, 而覆盖只可能在
    `State != null` 期间存在 (apply 只发生在 `Launch()` 释放缓冲之后, `Launch()`
    晚于断言 `State` 非 null 的 `InitializeShared`), 不存在"覆盖活着但 CleanUp
    提前 return"的窗口.
  - `InitializeShared` postfix 与 BaseLib 注册补丁**顺序无关**: 发送端三件前置
    (`CustomMessageWrapper.Initialize()` 开机 `PostModInitPatch.cs:65`;
    `MessageTypes.Initialize()` 于 `OneTimeInitialization.cs:84` 且 wrapper id 由
    BaseLib 后置写入; `NetService` 于 `RunManager.cs:470` 赋值, 早于任何 postfix)
    全部开机期就绪; 投递只在 `NetMessageBus.Update()` 泵时发生 (`NRun._Process`,
    而 `NRun` 由 `Launch()` 之后创建), 必然晚于 BaseLib 的注册. 顺带核实主机广播门
    `readyForBroadcasting` 在大厅握手时置位 (`StartRunLobby.cs:272` /
    `LoadRunLobby.cs:204` / `RunLobby.cs:118`), 早于 run 启动.
- 构建: `0 个警告 / 0 个错误` + `PCK packed`.
- 过程细节、引擎行号表与新记录的已知限制 (重连对端拿不到快照, 未决) 见
  `G:/omp works/sts2-mpconfigsync/DEVLOG.md` Session 3.

---

## 2026-09-12 (夜) astra-advice 项 1 + 项 5 修复 (主会话单线)

### 项 1: cfg 迁移归属判断 (`ConfigMigration.cs`)

- **缺陷**: 迁移只认文件名 `AutoAnthonyRelics.cfg`。新的 AutoAnthonyRelics 遗物 mod
  (BaseLib 按根命名空间命名 cfg) 现在拥有同名文件, 旧迁移器每次启动都会把它整个
  搬走: 键并入 QuriousCraftingRelics.cfg, 原文件改名为 .bak 且**覆盖旧备份**。
- **修复**:
  - 归属判据 = 键集合: 至少一个模板作用域键 (`Cost_/Refund_/Min_/Max_` 前缀)
    或一个已知 Qurious 标量键 (EnableChaosRelics 等 9 个)。不匹配 → `SkipNotOurs`,
    文件一个字节都不动。
  - 备份不再覆盖: `UniqueBackupPath` 取第一个空闲的 `.bak.N` 后缀。
  - 日志口径如实: skip 有专门一行说明留给谁。
- **验证**: `tools/migration-probe` **11/11 PASS** (隔离, 无 Godot):
  真遗留 cfg 正常迁移 (含 Title_Snake 重命名 + 固定点); 遗物 mod cfg 与无关 cfg
  被完整跳过 (无备份/无合并目标); 二轮迁移不覆盖首个备份。
  注意: probe 断言 `.bak*` 前缀计数, 不是 `*.bak` 通配 (后者匹配不到 `.bak.2`)。

### 项 5: 本局有效配置冻结 (`QuriousGenerationSnapshot.cs` + 注册表 + 种子补丁)

- **缺陷**: 定义查找键 = (seed, live 配置指纹)。局内改预算 → 指纹变 → 同 seed
  重新生成不同池 → 已持有遗物/文案/一次性效果全部变义 (审查原话: 60 个槽位全部变义)。
- **修复**: 种子捕获点 (SetUpNew* prefix + Launch postfix) 同步调用
  `QuriousGenerationSnapshot.Capture()`: 冻结 预算×3 / 负面概率×3 / ExtraPool 开关 /
  Watcher 存在性 / 每模板 Cost+Refund+Min/Max (两目录并集, 修正值回退 spec 默认)。
  `ChaosRelicRunRegistry.CurrentSnapshot` 驱动 `ConfigFingerprint`/`ForSeed`
  的全部生成输入; `ChaosTemplates.Effective/PositiveTemplates/NegativeTemplates/
  WatcherModLoaded` 在局内一律读快照, 局外 (菜单) 回退 live 配置。
  生成器签名未动 —— 输入由注册表按"快照优先"注入。
- **语义边界 (如实)**: 冻结只保证本进程局内不自变; MP 两端一致仍依赖配置同步在
  开局前送达主机值 (MpConfigSync 的契约)。快照冻结的是"捕获时刻本机所见"。
- **验证**: 隔离构建 `0 警告 / 0 错误`; 迁移探针覆盖项 1。冻结行为的局内复验
  (开局 → 改预算 → 已持有遗物不变) 需要实机, 标记为未验边界。

### 状态

已部署实机 `mods/QuriousCraftingRelics/` (游戏未运行)。与 AutoAnthonyRelics
遗物 mod 双装的池共存由双方补丁谓词保证 (都保留 CustomRelicModel)。
