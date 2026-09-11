[Steam Workshop item description - paste into the workshop upload form]

Title:
AutoAnthony - Relics (Chaos Relic Generator / Dongni Algorithm - Relics)

Tags: Relics, Gameplay, Balanced

English:

AutoAnthony - Relics: every run, 60 chaos relics are generated from the run seed with the Anthony algorithm. Each relic spends a rarity-scaled point budget (10 / 16 / 24 by default) on positive entries, and may roll one negative entry that refunds points for even more positives - Monster Hunter qurious-crafting style. Every relic gets a per-slot rarity, a name, and a dynamic entry-list description.

How it works
- 60 relic slots (Chaos Relic 1-60) injected into the shared relic pool: they show up in elite/combat rewards and shops like any vanilla relic.
- Rarity spread: 20 Common / 20 Uncommon / 20 Rare per run, regenerated per seed.
- 47 effect templates: 26 core positives + 10 core negatives, plus an optional extra pool (10 positives + 1 negative).
- Generation: buy positives until the budget runs dry (at most 6 per relic, one of each template), then roll the rarity's negative chance; a negative refunds points and the refund buys more positives.
- Decaying templates (Regen / Poison to all / Plating) are priced triangularly: total cost = per-point x N x (N+1) / 2.
- Same seed = same relics. Different seed = completely different entries for the same slot.
- Seed is captured at run setup (before the reward pool is populated) so rarity resolution matches the generator exactly.
- Ancient / Neow relic pools stay untouched (vanilla-only per design).
- Multiplayer-safe random targeting (uses the run's CombatTargets RNG channel); both ends must carry identical budget/cost/range config.

Requires: BaseLib (3.4.5+)

Configuration (Settings -> General -> AutoAnthony - Relics, its own page):
- Enable Chaos Relics: master switch (default on)
- Point budget per rarity (default 10 / 16 / 24)
- Negative-entry chance per rarity (default 35% / 55% / 75%)
- Budget editor: per-template point costs and double-ended amount-range sliders
- Enable Extra Effect Pool (default off): hand retain / sly / ethereal, enchantments, retain triggers, Watcher stances

Console verification (optional): in a run, open the dev console and use
  relic add AUTOANTHONYRELICS-CHAOS_RELIC005
to grant a chaos relic immediately; check the top-bar relic icons.

Compat: works alongside AutoAnthony (cards) but does NOT require it.

Chinese (Simplified):

AutoAnthony - Relics(东尼算法 - 遗物):每局游戏依据本局种子确定性地生成 60 件混沌遗物.每件遗物按稀有度获得点数预算(默认 普通 10 / 罕见 16 / 稀有 24),正词条消耗点数,并可能掷出一条负面词条返还点数换取更多正词条(怪猎炼化风格).每件都有独立稀有度,名字与动态效果描述.

Mechanics
- 60 个遗物槽位(混沌遗物 1-60)进入共享遗物池,像原版遗物一样出现在精英/战斗奖励与商店.
- 稀有度分布:每局 20 普通 / 20 罕见 / 20 稀有,逐种子重新生成.
- 47 个效果模板:核心 26 正 + 10 负,另有可选额外池(10 正 + 1 负).
- 生成顺序:先花预算买正词条(单件最多 6 条,每模板至多 1 条),再按稀有度概率掷一条负面;负面返还点数可继续买正词条.
- 衰减型词条(再生 / 全体中毒 / 覆甲)三角计价:总花费 = 每点成本 x N x (N+1) / 2.
- 同种子 = 同一批遗物;换种子 = 同一槽位完全不同的词条.
- 种子在开局时(奖励池填充前)捕获,稀有度解析与生成器完全一致.
- 先古之民 / Neow 遗物池保持原版(设计上不动).
- 联机安全的随机目标(使用本局 CombatTargets 随机通道);两端预算/点数/区间配置必须一致.

Requires: BaseLib (3.4.5+)

Configuration (设置 -> 常规 -> 东尼算法 - 遗物 专属设置页):
- 启用混沌遗物:总开关(默认开)
- 各稀有度点数预算(默认 10 / 16 / 24)
- 各稀有度负面词条概率(默认 35% / 55% / 75%)
- 点数预算编辑器:每模板点数与双手柄数值区间滑条
- 启用额外效果池(默认关):手牌保留 / 奇巧 / 虚无,附魔,保留触发,观者姿态

Console verification (optional): 局内按 ` 打开开发者控制台并输入
  relic add AUTOANTHONYRELICS-CHAOS_RELIC005
可直接获得一件混沌遗物,查看顶栏图标与提示框效果.

Compatibility: 可与 AutoAnthony(卡牌侧)联用,但不依赖它,可单独订阅.

Change log
v0.5.1 (2026-09-12)
- Documentation/terminology sync: workshop text and the points list now derive from the two catalog files (47 templates: 26 + 10 core, 10 + 1 extra); zhs settings text uses the game's official names (人工制品 / 懒惰 / 锋利 / 灵巧 / 注能).
- FIX: per-template point pricing and the cheapest-positive budget floor now resolve across BOTH pools, so the extra pool no longer throws while it is enabled.
- FIX: run registry caches by (seed, config fingerprint) instead of seed alone; a config change no longer serves a stale pool.
- FIX: budget editor rebuilt around a single double-ended range slider (two independent sliders could not express a range).
v0.5.0 - point-budget entry system (qurious-crafting style): rarity-scaled budgets, negative entries refund points for extra positives; built-in RelicRewardChoices + Act4Heart Sapphire Key fix; all budgets/chances/costs configurable.
v0.4.1 (2026-09-08 evening)
- FIX: chaos relics now actually enter the reward pool (engine SharedRelicPool injection; previously they only appeared in the compendium).
- FIX: run seed is captured before the reward pool is populated, so rarities resolve correctly (20/20/20 spread) instead of all-Common.
- FIX: split the dual-target Harmony patch class (only the last target was being patched).
- NEW: 60 distinct procedurally-generated placeholder icons + outlines + big icons.
- MP: random targeting now uses the seeded CombatTargets RNG channel.
v0.2.0 - entry generator, dynamic localization, config options.
v0.1.0 - initial slot scaffold.
