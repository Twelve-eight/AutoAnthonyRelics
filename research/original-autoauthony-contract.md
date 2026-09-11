# 原版 AutoAnthony (Auto-Anthonyology) 机制契约

纯研究文档. 不含任何游戏代码, 未修改任何既有文件.

反编译证据根目录: `G:/omp works/.tmp/aa-decompile/`
下文所有 `src/...` 路径均相对于该根目录. 数据证据为 DLL 内嵌 JSON, 反编译时被导出到 `src/` 下.

---

## 0. 材料与方法

| 项目 | 值 |
| --- | --- |
| 原版 DLL | `g:/steam/steamapps/workshop/content/2868840/3786611028/AutoAnthony.dll` (6274048 字节) |
| 同目录 pck | `AutoAnthony.pck` (231388 字节) |
| manifest | `AutoAnthony.json` |
| mod id / 名称 | `AutoAnthony` / `Auto-Anthonyology` |
| 作者 | `Alriph` |
| 版本 | `0.3.81` |
| min_game_version | `0.111.0` |
| 标志 | `has_pck=true`, `has_dll=true`, `affects_gameplay=true` |
| Harmony 实例 id | `autoanthony.v111` (证据: `src/AutoAnthony/ChaosBootstrap.cs:44`) |
| 反编译工具 | `ilspycmd` 9.1 |
| 类型总数 | 1428 (证据: `.tmp/aa-decompile/types.txt`) |

复现命令 (全部输出到 `G:`):

```
mkdir -p "G:/omp works/.tmp/aa-decompile"
# 类型清单
"C:/Users/o_Obl/.dotnet/tools/ilspycmd.exe" -l c "<dll>" > types.txt
# 全量 C# 反编译 (会同时导出内嵌 JSON 数据资源)
"C:/Users/o_Obl/.dotnet/tools/ilspycmd.exe" -p -o src "<dll>"
# 全量 IL (用于还原 ilspycmd 无法解码的 HarmonyPatch 特性参数)
"C:/Users/o_Obl/.dotnet/tools/ilspycmd.exe" -il -o il "<dll>"
# 单个类型
"C:/Users/o_Obl/.dotnet/tools/ilspycmd.exe" -t <FullTypeName> "<dll>" -o single
```

反编译产物规模: 602 个 C# 文件, 129 个文件带 `HarmonyPatch`; `AutoAnthony.il` 约 25 MB.
DLL 引用的引擎程序集为 `sts2` (报告为 `Version=0.1.0.0`), 另有 `0Harmony` 与 `GodotSharp`.

### 内嵌数据资源 (关键)

`src/` 下四个 JSON 是 mod 作者离线产出的数据, 也是本次研究的核心证据:

| 文件 | 内容 | 规模 |
| --- | --- | --- |
| `src/AutoAnthony.Data.catalog_recipes.json` | 原版卡牌 -> 原子片段配方表 | 481 条配方, 931 个原子实例 |
| `src/AutoAnthony.Data.catalog_runtime_specs.json` | 每个原子的结构化 RuntimeSpec | 931 条, 与配方一一对应 (按 SemanticId) |
| `src/AutoAnthony.Data.native_reference_cards.json` | 原版卡的引用壳 | 约 2.4 MB |
| `src/AutoAnthony.Data.native_reference_components.json` | 原版卡组件引用 | 约 0.55 MB |

加载点证据: `src/ChaosCardGenerator/StructuredComponentCatalogRegistry.cs:57` (catalog_recipes.json), `src/ChaosCardGenerator/CatalogRuntimeSpecRegistry.cs:81` (runtime specs), `src/ChaosCardGenerator/NativeCardDecompositionApi.cs:383,412` (native reference).

---

## 1. 一句话结论

AutoAnthony **不是"就地改写已有卡牌", 而是"把原版卡牌集离线拆成原子片段目录, 再按 角色 + 种子 重新采样, 重新拼装出一整套全新的卡池"**; 条件(触发器)与效果在数据层就是同一池子里的两类**独立**片段, 只在拼装末尾用运行期 `triggerIndex` 事后绑定, 因此任何条件都可以挂任何合法效果 -- 这就是"打乱条件"的实现方式.

需要注意: 该结论修正了一个常见误解. 它不改牌组里的卡, 也不改原版卡的数值; 它替换的是**角色卡池**与(可选)**起始牌组**.

---

## 2. 机制总览

流程 (5 个阶段):

| 阶段 | 时机 | 做什么 | 证据 |
| --- | --- | --- | --- |
| 1 离线拆解 | 作者开发期, 不在游戏内发生 | 把 481 张原版卡逐张拆成原子片段, 标注 模板 / 作用域 / 触发归属, 序列化进 DLL 内嵌 JSON | `src/AutoAnthony.Data.catalog_recipes.json` |
| 2 载入建目录 | `ModelDb.InitIds` 之后 | 读 JSON, 按角色建立 `ImmutableComponentCatalog`(原子池) 与配方表 | `src/AutoAnthony.Patches/ChaosModelDbReadyPatch.cs:36`; `src/ChaosCardGenerator/CharacterComponentCatalogs.cs:11` |
| 3 按种子生成整套卡池 | 开新局 (单人/多人) 时 | `System.Random(SHA256(character+seed))` 驱动采样, 逐槽位拼出该角色全部新卡 | `src/AutoAnthony.Patches/SeedBeforeSingleplayerPatch.cs:14`; `src/AutoAnthony/ChaosRunDefinitions.cs:691` |
| 4 卡池替换 | 卡池被查询时 | 用生成的卡池替换原版角色卡池; 可选替换起始牌组; 可选保留原版卡 | `src/AutoAnthony.Patches/ColorlessPoolContentsPatch.cs:12`; `src/AutoAnthony.Patches/IroncladPoolPatch.cs`; `src/AutoAnthony.Patches/CharacterPoolPatchRouting.cs:21,30` |
| 5 运行期解释 | 战斗内 | 逐条解释拼装出的操作序列, 触发时执行所有 `triggerIndex` 指向该触发的操作 | `src/AutoAnthony/ChaosOperationExecutor.cs:286-377` |

规模事实:

* 生成的固定卡槽共 **514** 个类型 (`ChaosCard000..091` 及各角色 `ChaosXxxCardNNN`), 证据 `src/AutoAnthony/ChaosCardRegistry.cs:10`, 以及 `ChaosBootstrap.cs:57` 的日志字符串 `Initialized with 514 fixed card slots`.
* 每角色期望稀有度分布: Basic 若干 + Common 20 + Uncommon 35 + Rare 25 + Ancient 2; Colorless 为 Uncommon 31 + Rare 21. 证据 `src/AutoAnthony/ChaosRunDefinitions.cs:960` (`ExpectedRarities`).
* 拼装出的卡**只有 Attack / Skill / Power 三类** (`GeneratedCardType` 枚举, `src/ChaosCardGenerator/GeneratedCardType.cs:3`).

---

## 3. 原子片段的分类表

### 3.1 片段的数据结构 (不是枚举, 是"字符串模板 ID + 结构化语义对象")

| 类型 | 定义 | 说明 | 证据 |
| --- | --- | --- | --- |
| `ComponentAtom` | `record ComponentAtom(string Template, OperationScope Scope, string ChineseText, bool RequiresSingleTarget, CardReferenceRequirement CardReference)` | 运行期原子. 另有 `SemanticId`, `RuntimeSpec`, `LocalizedText`, `Category` | `src/ChaosCardGenerator/ComponentAtom.cs:3` |
| `OperationRuntimeSpec` | `record OperationRuntimeSpec(int SchemaVersion, string Opcode, string Variant, string Target, string SourceZone, string DestinationZone, string CardFilter, IReadOnlyList<string> Flags, IReadOnlyList<RuntimeValueSlot> Values, RuntimeConditionSpec? Condition, RuntimeTriggerSpec? Trigger)` | 原子的机器语义. 当前 `SchemaVersion = 1` | `src/ChaosCardGenerator/OperationRuntimeSpec.cs:11,26` |
| `RuntimeConditionSpec` | `record RuntimeConditionSpec(string Kind, string Subject = "none", string? ValueSlot = null)` | **条件**片段 | `src/ChaosCardGenerator/RuntimeConditionSpec.cs:3` |
| `RuntimeTriggerSpec` | `record RuntimeTriggerSpec(string Kind, string Lifetime, string? ThresholdSlot = null, string? DurationSlot = null)` | **触发**片段 | `src/ChaosCardGenerator/RuntimeTriggerSpec.cs:3` |
| `RuntimeValueSlot` | `record RuntimeValueSlot(string Id, int BaseValue, string Source = "fixed", int Offset = 0, bool Upgradable = true, bool Explicit = true)` | 数值槽 | `src/ChaosCardGenerator/RuntimeValueSlot.cs:3` |
| `IroncladCardRecipe` | `record IroncladCardRecipe(string Id, string ChineseTitle, int Cost, GeneratedCardType Type, TargetMode Target, GeneratedRarity OriginalRarity, IReadOnlyList<CardTag> Tags, IReadOnlyList<ComponentAtom> Atoms, IReadOnlyList<int> TriggerOwners, ...)` | 一张原版卡拆解后的配方 | `src/ChaosCardGenerator/IroncladCardRecipe.cs:5` |
| `NativeCardDecomposition` | `record NativeCardDecomposition(string CatalogId, string NativeId, string ClassName, string Pool, string SourceKind, bool ReferenceOnly, bool GenerationEligible, bool ShouldShowInLibrary, NativeCardLocalizedText Title, NativeCardLocalizedText DescriptionTemplate, NativeCardShellDescriptor Base, NativeCardUpgradeDescriptor Upgrade, IReadOnlyList<NativeCardComponentDescriptor> Components, ...)` | 原版卡的完整拆解记录 | `src/ChaosCardGenerator/NativeCardDecomposition.cs:6` |
| `NativeCardComponentDescriptor` | `record NativeCardComponentDescriptor(string ComponentId, string? SemanticId, int TriggerOwner, string? CardReference, bool RequiresSingleTarget, IReadOnlyDictionary<string, NativeCardComponentArgument> Arguments, OperationRuntimeSpec? RuntimeSpec, NativeCardLocalizedText? Text)` | 原版卡的单个组件, 含 `TriggerOwner` | `src/ChaosCardGenerator/NativeCardComponentDescriptor.cs:5` |
| `GeneratorOperation` | `record GeneratorOperation(string Template, OperationScope Scope, string ChineseText, IReadOnlyDictionary<string,int> Parameters, string? CardTargetSlot, bool RequiresSingleTarget, string? DerivativeId, string? DerivativeEnchantmentId, string? OrbSourceId, string? OrbOutputId, int? DerivativeEnchantmentAmount, OperationRuntimeSpec? RuntimeSpec, OperationLocalizedText? LocalizedText, string? LocalizationId)` | 拼装输出时的操作. `Parameters["triggerIndex"]` 就是条件绑定 | `src/ChaosCardGenerator/GeneratorOperation.cs:6` |
| `GeneratedCard` | `record GeneratedCard(int Cost, GeneratedCardType Type, TargetMode Target, GeneratedRarity Rarity, string ChineseDescription, IReadOnlyList<CardTag> Tags, IReadOnlyList<GeneratorOperation> Operations, GeneratedCardName? Name, CardUpgradePlan? Upgrade, ...)` | 拼装出的整张新卡 | `src/ChaosCardGenerator/GeneratedCard.cs:6` |

`OperationScope` 枚举顺序 (决定触发/效果分类, 也决定"能否被绑定"):

```
0 SingleEnemyOnly   1 NonTargeted   2 Modifier
3 AbilityTrigger    4 ConditionalTrigger   5 AbilityRule
6 Independent
```

证据 `src/ChaosCardGenerator/OperationScope.cs:3-12`.

**关键判据**: 代码中反复出现的 `(uint)(scope - 3) <= 1u` 等价于 `scope == AbilityTrigger || scope == ConditionalTrigger`, 即"这是一个触发片段"; `(uint)(scope - 3) <= 2u` 额外含 `AbilityRule`.

### 3.2 模板前缀分类表 (基于 931 个原子实例的实测统计)

模板 ID 形如 `<前缀>:<名字>`, 前缀既是命名空间也是角色归属标记.

| 类别(前缀) | 语义 | 实测作用域分布 | 操作码(opcode)分布 | 角色归属 | 实例数 | 去重模板数 | 证据 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `A` | 能力触发器 / 战斗规则 | AbilityTrigger 70, AbilityRule 19 | trigger 70, combat_rule 19 | 全角色共享 (Colorless 4) | 89 | 21 | 数据 `catalog_recipes.json`; 样例 `A:turnStart`, `A:rule` |
| `C` | 条件触发器 | ConditionalTrigger 46 | trigger 34, condition 12 | 全角色共享 | 46 | 20 | 样例 `C:untilTurnEnd`, `C:ifOwnerLostHpThisTurn` |
| `N` | 非指向性效果(主力池) | NonTargeted 291, Independent 1 | gain_block 78, draw_cards 50, deal_damage 39, apply_power 37, gain_energy 30 | 全角色共享 | 292 | 约 140 | 样例 `N:B`, `N:Self`, `N:AllD`, `N:HP-`, `N:Move`, `N:Exhaust` |
| `T` | 指向性(单体敌人)效果 | SingleEnemyOnly 179 | deal_damage 134, apply_power 34 | 全角色共享 | 179 | 约 40 | 样例 `T:D`, `T:Apply` |
| `I` | 独立动作 | Independent 47 | template_independent_action 41 | 全角色共享 | 47 | 约 20 | 样例 `I:UpgradeThatCard`, `I:Upgrade`, `I:Transform` |
| `M` | 修饰符(倍率/次数/数值改写) | Modifier 15 | template_modifier 7, modify_damage 4, modify_hits 3 | 全角色共享 | 15 | 约 10 | 样例 `M:repeat`, `M:value`, `M:base` |
| `D` | Defect 专属 | NonTargeted 53, Modifier 8, Independent 7 | template_self_action 48 | 仅 Defect | 73 | 约 40 | 样例 `D:ChannelLightning`, `D:ShuffleAllUnexhaustedIntoDraw` |
| `NCR` | Necrobinder 专属 | NonTargeted 39, SingleEnemyOnly 23, Modifier 19 | template_self_action 34, template_target_action 23 | 仅 Necrobinder | 91 | 约 60 | 样例 `NCR:Summon`, `NCR:CreateSoulInDraw` |
| `R` | Regent 专属 | NonTargeted 35, Modifier 15 | template_self_action 22, gain_stars 13 | 仅 Regent | 60 | 约 40 | 样例 `R:Forge`, `R:GainStars` |
| `CL` | Colorless 专属 | Independent 18, NonTargeted 8, AbilityTrigger 6 | template_independent_action 14 | 仅 Colorless | 39 | 约 30 | 样例 `CL:ProxyAtomic_Alchemize`, `CL:GainGold` |

汇总: 358 个去重模板; 全部实例共 931 个, 落在 481 条配方上. 按角色拆分: Ironclad 85, Silent 86, Defect 86, Necrobinder 86, Regent 86, Colorless 52 条配方.

按 `ImmutableComponentCatalog` 的 `SchemaKey` 口径去重后 (即真正可抽样的候选池大小):

| 角色 | 去重原子数 |
| --- | --- |
| Ironclad | 86 |
| Silent | 72 |
| Defect | 81 |
| Necrobinder | 95 |
| Regent | 79 |
| Colorless | 56 |

按"触发 / 效果"二分 (触发 = scope 3/4/5):

| 角色 | 触发模板数 | 效果模板数 |
| --- | --- | --- |
| Ironclad | 25 | 39 |
| Silent | 21 | 48 |
| Defect | 16 | 62 |
| Necrobinder | 18 | 74 |
| Regent | 17 | 58 |
| Colorless | 12 | 43 |
| 全体并集 | 90 | 268 |

### 3.3 条件与触发的实际取值集合 (来自 931 条 RuntimeSpec)

`Condition.Kind` 实测 18 种 (各 1-5 次):

```
fatal, no_attacks_in_hand, hand_empty, cards_played_this_turn_below,
enemy_intends_attack, has_frost_orb, target_has_vulnerable,
card_exhausted_this_turn, exhaust_pile_minimum, owner_lost_hp_this_turn,
osty_alive, doom_applied_this_turn, first_play_of_this_card_this_turn,
osty_attacked_this_turn, cards_played_this_turn_at_least, target_has_poison,
last_drawn_card_is_skill, draw_pile_empty
```

`Trigger.Kind` 实测 58 种, 出现频次最高的:

```
turn_start 29, next_turn_start 16, card_played 6, turn_end 4,
attack_played 2, attack_received 1, owner_hp_lost_during_turn 2,
card_exhausted 2, power_played 2, status_generated 2, ...
```

`Trigger.Lifetime` 取值: `combat 78, next_turn 16, immediate 12, this_turn 8, delayed 1, next_n_turns 1`.

`Opcode` 出现频次最高的: `deal_damage 174, template_self_action 130, trigger 116, gain_block 78, apply_power 76, template_independent_action 72, draw_cards 50, template_modifier 46, template_target_action 33, gain_energy 30, condition 23, combat_rule 21, gain_stars 13, exhaust_card 11, lose_hp 11, discard_card 8, create_card 7, move_card 5, modify_cost 4, modify_damage 4`.

统计命令可复现 (Python 读 `catalog_recipes.json` + `catalog_runtime_specs.json`, 按 `SemanticId` 关联).

### 3.4 触发归属字段

配方里的 `TriggerOwners` 是与 `Atoms` 等长的 `int` 列表:

* `-1` = 该片段不属于任何触发 (自身是触发器, 或独立效果)
* `0 / 1 / 2` = 指向同配方中第 N 个原子 (即它的触发宿主)

实测分布: `-1: 785, 0: 102, 1: 42, 2: 2`. 该字段在**原版配方的自校验**里使用 (`CanAssemble`, `src/ChaosCardGenerator/ComponentAssemblyGenerator.cs:2190`), 在**生成路径**里被替换成动态计算的 `triggerIndex`.

---

## 4. 重组规则

### 4.1 生成循环 (逐槽位采样)

核心循环 `TryGenerate`, 证据 `src/ChaosCardGenerator/ComponentAssemblyGenerator.cs:213-342`.

对每个槽位 `j`:

1. 清空候选表, 遍历**该角色整个原子池** `_componentCatalog.Atoms` (`:249`).
2. 逐个通过 `IsCompatible(type, target, cost, hasStarCostX, atom, previous)` 过滤 (`:260`, 定义在 `:2633`).
3. 通过 `PickForRarity(...)` 按稀有度加权随机抽一个 (`:281`, 定义在 `:3440`).
4. 抽中后立即实例化数值槽 `InstantiateNumericSlotsStructured` (`:281`, 定义在 `:3579`).
5. 调 `LinkedTriggerIndex(list, atom)` 决定是否绑定到之前的某个触发 (`:289`, 定义在 `:2112`), 结果写入 `parameters["triggerIndex"]`.
6. 追加进 `list`.

槽位数量 (效果条数) 由稀有度与成本决定, 并在 `AdaptiveEffectCountWindow` 的动态窗口内 (`:228`, `:568`), 常见 2-5 条.

### 4.2 哪些片段可以互换

`IsCompatible` 的判定集合 (证据 `:2633-2760`), 概括为"结构合法性"而非"保持原配对":

* 不允许重复的卡级唯一效果 (`WouldDuplicateCardUniqueEffect`, `:2639`).
* 同一字段最多出现 2 次 (`FieldOccurrenceCount >= 2`, `:2643`).
* X 资源一致性 (`XRequirement`, `:2647`).
* 无费用卡 / X 星费卡不允许自费改动 (`IsSelfCostChange`, `:2651`).
* "下一次攻击" 类触发必须紧跟合法 payoff (`:2679-2683`).
* 依赖前缀 (如 `D:ForEachEnergySpentThisTurn`) 必须紧跟合法 payoff (`:2778-2794`).
* 触发后不允许接"独立事件依赖前缀" (`:2783`).
* 极端生命周期副作用不能挂在触发之后 (`:2799`).
* 若干 `HasValid*` 组合校验 (最终统一在 `HasValidOperationAssembly`, `:2375`).

**没有任何一条规则要求"效果必须是它原来那个触发的搭档"**, 也没有规则禁止把触发 A 的效果换成效果 B. 这是"条件重组"得以成立的前提.

### 4.3 条件绑定的随机性 (核心)

`LinkedTriggerIndex(operations, atom)` 的判定顺序, 证据 `src/ChaosCardGenerator/ComponentAssemblyGenerator.cs:2112-2188`:

| 条件 | 返回 |
| --- | --- |
| 原子是 `D:IncreaseThisCardCost` | -1 (不绑定) |
| 原子是受限效果且已有重复/乘法 payoff 的触发 | -1 |
| 原子是"独立事件依赖前缀" | -1 |
| 上一个操作是 `D:ShuffleAllUnexhaustedIntoDraw` | 沿用其 `triggerIndex` |
| 上一个操作是"需要挂效果的触发" (`TriggerNeedsLinkedEffect`) | 返回 `count-1` (绑定到它), 但若原子需要玩家选择而触发不支持则 -1 |
| 上一个操作是依赖前缀 | 沿用其 `triggerIndex` |
| **原子自身 scope 属于 3/4/5 (即它自己是触发/规则)** | **-1 (触发永远不绑定到另一个触发)** |
| 上一个操作没有 `triggerIndex` | -1 |
| 同一 `triggerIndex` 已被 2 个操作占用 | -1 |
| 触发是 `fatal` 且原子是"双倍易伤" | -1 |
| `N:RetaliateDamage` 而触发不是 `attack_received` | -1 |
| 触发难度等级 `DifficultConditionTier >= 2` | 一定绑定 |
| 否则 (普通触发) | `_random.Next(2) != 0` 时绑定, 即**约 50% 概率** |

最后一条是决定性证据: 普通触发与效果的绑定是**掷硬币决定的**, 而不是由数据里原有的配对决定的.

`TriggerNeedsLinkedEffect` 定义: `scope in {AbilityTrigger, ConditionalTrigger}` 且不是 `C:whileInCombat` 类, 证据 `src/ChaosCardGenerator/CardEffectRules.cs:3654-3678`.

### 4.4 同 seed 两端是否一致

| 随机源 | 用途 | 是否同 seed 一致 |
| --- | --- | --- |
| `System.Random(SHA256("AutoAnthony/v111/all-pools-v2/{character}/{seed}"))` | **卡池构成**(哪些原子拼成哪张卡) | 是, 完全确定 |
| `System.Random` 的派生流 (`random.Next()` 再喂给 `RandomCardGenerator`) | 逐卡采样 | 是, 确定 |
| `System.Random(SHA256("AutoAnthony/CardName/v2|{character}|{value}|{text}"))` | 卡名生成 | 是, 确定 |
| 引擎 `RunState.Rng.CombatTargets / CombatCardGeneration / CombatCardSelection / Shuffle / CombatPotionGeneration` | **战斗内**效果的随机目标/随机卡 | 由引擎标准种子机制决定, 非 mod 自建 |

证据: `src/AutoAnthony/ChaosRunDefinitions.cs:1661-1664` (种子派生), `:691` (生成入口), `:618` (衍生约束修复的独立派生流); `src/ChaosCardGenerator/ComponentAssemblyGenerator.cs:618,678` (卡名派生流); `src/AutoAnthony/ChaosOperationExecutor.cs:314,499,984,1515,1692,3147` 等 (引擎 RNG 使用点).

种子盐串里含 `v111`, 且 `ChaosBootstrap` 的 Harmony id 也是 `autoanthony.v111`, 因此**可复现性绑定在 mod 版本 + 游戏版本上**. 升级 mod 后同 seed 不再保证同结果.

多人场景: mod 会让主机生成后把**权威卡池快照**分块下发给客户端, 并用指纹校验:

* 指纹算法 `MultiplayerGameplayFingerprint` 遍历所有角色所有卡槽, 把 Cost / StarCost / Type / Target / Rarity / Tags / Operations (含每个 operation 的 `Template`, `Scope`, `DerivativeId`, 以及 `Parameters` 里的 `triggerIndex`) 全部串进 SHA256. 证据 `src/AutoAnthony/ChaosPoolSnapshot.cs:98-142`, 指纹哈希 `:93-96`.
* 客户端收到后本地重算并比对, 不一致就抛 `The restored host snapshot has gameplay fingerprint ...` 证据 `src/AutoAnthony.Patches/MultiplayerGenerationModePatch.cs:398-401`.
* 结论: 设计意图是"同 seed 两端一致", 且有一致性校验 + 主机快照兜底. 注意 `triggerIndex` 参与指纹, 说明**条件-效果配对也被纳入一致性要求**.

### 4.5 生成路径 vs 原版配方校验路径

两条路径必须区分:

* **生成路径** `TryGenerate` (`:213`): 从全池自由采样, 用 `LinkedTriggerIndex` 动态绑定. 这是新卡来源.
* **配方校验路径** `CanAssemble(recipe)` (`:2190`): 校验内嵌配方自身是否合法, 用 `recipe.TriggerOwners[i]` 映射到已放置位置 (`:2241`), 再走 `CanChooseTriggerOwner` (`:2280`). 这条路径用于把原版卡"物化"回可执行定义 (`AutoAnthonyNativeCardApi`), 不是随机生成.

---

## 5. 条件与效果如何解耦 (新 mod 复现"条件重组"的关键)

### 5.1 解耦的机制本质

1. **数据层就分开了**: 一个配方里, 触发是一个独立原子 (`TriggerOwner = -1`), 效果是另外的原子 (`TriggerOwner = 该触发下标`). 触发原子自身只有 `RuntimeSpec.Trigger`, 效果原子自身只有 `RuntimeSpec.Opcode/Values`. 两者没有共享的可变状态.
2. **采样时各抽各的**: 循环里每个槽位都从同一个 `_componentCatalog.Atoms` 抽, 触发和效果在同一个池子里竞争, 没有任何"必须成对出现"的约束.
3. **绑定时机在最后**: 原子被抽中之后才调 `LinkedTriggerIndex` 决定挂不挂. 触发本身 (scope 3/4/5) 永远返回 -1, 即**触发不携带效果**.
4. **绑定是随机的**: 普通触发 50% 概率被绑定 (`:2183`), 困难触发 100% 绑定 (`:2179-2182`).
5. **运行期按索引分发**: `ExecuteTriggered` 在触发激活时, 扫描触发之后所有 `triggerIndex == 该触发下标` 的操作并依次执行. 证据 `src/AutoAnthony/ChaosOperationExecutor.cs:286-377` (尤其 `:291-292` 的 LINQ 过滤).

因此: **条件跟着"触发原子"走, 效果跟着"效果原子"走, 二者在生成时才随机配对.**

### 5.2 反编译里真实存在的配对例子 (用户所举情形的直接对应)

原版数据里同一个触发模板已经与不同效果模板配对:

| 原版卡 | 触发原子 | 效果原子 | 英文卡面 |
| --- | --- | --- | --- |
| `Ironclad/Rupture` (撕裂) | `A:whenOwnerHpLostDuringTurn`, Trigger.Kind = `owner_hp_lost_during_turn`, Lifetime = `combat` | `N:Self`, opcode = `apply_power`, variant = `strength` | `Whenever you lose HP during your turn.` / `Gain 1 Strength.` |
| `Ironclad/Inferno` (狱火) | `A:whenOwnerHpLostDuringTurn` (同一模板) | `N:AllD`, opcode = `deal_damage`, variant = `all` | `Whenever you lose HP during your turn.` / `Deal 6 damage to ALL enemies.` |

而"失去生命"这个效果本身也是池子里一个普通效果原子:

| 原版卡 | 原子 | opcode | 英文卡面 |
| --- | --- | --- | --- |
| `Ironclad/Inferno` | `N:HP-` | `lose_hp`, variant = `immediate` | `Lose 1 HP.` |

把 `A:whenOwnerHpLostDuringTurn` 与 `N:HP-` 绑在一起, 得到的就是:

```
Whenever you lose HP during your turn.  ->  Lose 1 HP.
```

这正是用户描述的"每在你的回合失去生命, 失去1点生命". 该组合在生成器里是**可达的**: 两者都在同一角色池内, `N:HP-` 的 scope 是 `NonTargeted` (属效果类), `A:whenOwnerHpLostDuringTurn` 的 scope 是 `AbilityTrigger` (属触发类), 二者通过 `triggerIndex` 绑定即可.

### 5.3 另一个条件解耦的证据: 条件型片段 (`C:` 前缀)

`C:` 前缀是"条件触发器", 语义是"当 X 时"或"若 X". 实测样例:

| 卡 | 原子 | RuntimeSpec | 英文卡面 |
| --- | --- | --- | --- |
| `Ironclad/Spite` (怨恨) | `C:ifOwnerLostHpThisTurn` (scope `ConditionalTrigger`) | opcode = `condition`, variant = `owner_lost_hp_this_turn`, Condition.Kind = `owner_lost_hp_this_turn` | `If you lost HP this turn.` |
| `Ironclad/Spite` | `T:D` (scope `SingleEnemyOnly`) | opcode = `deal_damage`, variant = `selected` | `Deal 5 damage.` |
| `Ironclad/Spite` | `M:repeat` (scope `Modifier`) | opcode = `modify_hits`, variant = `flat_extra` | `This card deals damage 1 additional time.` |

注意 `Spite` 里 `C:ifOwnerLostHpThisTurn` 的 `TriggerOwner = -1`, 而 `M:repeat` 的 `TriggerOwner = 1`. 也就是说"若你本回合失去过生命"这个条件被当成**独立可复用的片段**, 不绑定到具体伤害. 这正是"条件独立打乱"的数据层证据.

### 5.4 已确认的统计事实

内嵌数据中, **69 个触发模板**在原版卡里就已经各自搭配过多个不同效果模板. 例如 `A:whenCardPlayed` 实测搭配 `N:B` / `N:RandomD` / `R:GainStars`. 生成器把这个"一对多"关系进一步放开成"多对多".

---

## 6. 引擎 API 与 Harmony 补丁点清单

### 6.1 引擎 API 使用面

DLL 引用 **391 个不同的 sts2 类型**. 主要命名空间分布:

| 命名空间 | 类型数 | 代表性类型 |
| --- | --- | --- |
| `MegaCrit.Sts2.Core.Models.Powers` | 56 | 各类 Power 模型 |
| `MegaCrit.Sts2.Core.Models.Cards` | 38 | `Guilty`, `Regret`, `Shiv`, `Burn`, `Void`, `Soul`, ... |
| `MegaCrit.Sts2.Core.Models` | 20 | `CardModel`, `CardPoolModel`, `CharacterModel`, `ModelDb`, `PowerModel`, `RelicModel`, `PotionModel` |
| `MegaCrit.Sts2.Core.Entities.Cards` | 20 | `CardType`, `CardRarity`, `CardTag`, `CardKeyword`, `PileType`, `CardPile`, `TargetType`, `CardPlay`, `CostModifiers` |
| `MegaCrit.Sts2.Core.Models.Enchantments` | 19 | `Adroit`, `Corrupted`, `Glam`, `Inky`, `Sharp`, ... |
| `MegaCrit.Sts2.Core.Localization.DynamicVars` | 13 | `DynamicVar`, `DynamicVarSet`, `DamageVar`, `BlockVar`, `EnergyVar`, `HpLossVar`, `MaxHpVar`, `OstyDamageVar`, `CalculatedVar` |
| `MegaCrit.Sts2.Core.Commands` | 13 | `CardCmd`, `CardPileCmd`, `CardSelectCmd`, `CreatureCmd`, `DamageCmd`, `PowerCmd`, `PotionCmd`, `RelicCmd`, `OrbCmd`, `OstyCmd`, `ForgeCmd`, `PlayerCmd`, `Cmd`, `Builders.AttackCommand` |
| `MegaCrit.Sts2.Core.Combat.History.*` | 12 | `CombatHistory`, `CombatHistoryEntry`, `DamageReceivedEntry`, `CardDrawnEntry`, `CardPlayFinishedEntry`, `PowerReceivedEntry`, `BlockGainedEntry`, `EnergySpentEntry`, `StarsModifiedEntry`, `OrbChanneledEntry`, `CreatureAttackedEntry`, `CardDiscardedEntry`, `CardExhaustedEntry`, `CardGeneratedEntry` |
| `MegaCrit.Sts2.Core.Runs` | 11 | `RunState`, `RunManager`, `CardCreationOptions`, `IRunState`, `ICardScope`, `RunRngSet`, `RunHistory`, `GameMode` |
| `MegaCrit.Sts2.Core.Models.Relics` | 11 | `ArchaicTooth`, `DustyTome`, `GhostSeed`, `PandorasBox`, `NeowsTalisman`, `NutritiousSoup`, `PaelsClaw`, `LargeCapsule`, `LeafyPoultice`, `MassiveScroll`, `TouchOfOrobas` |
| `MegaCrit.Sts2.Core.Saves.Runs` | 9 | 存档/快照序列化 |
| `MegaCrit.Sts2.Core.Models.CardPools` | 8 | `IroncladCardPool`, `SilentCardPool`, `DefectCardPool`, `NecrobinderCardPool`, `RegentCardPool`, `ColorlessCardPool`, ... |
| `MegaCrit.Sts2.Core.Models.Events` | 7 | `Darv`, `Orobas`, `ColorfulPhilosophers`, `TheFutureOfPotions` |
| `MegaCrit.Sts2.Core.Models.Characters` | 6 | `Ironclad`, `Silent`, `Defect`, `Necrobinder`, `Regent` |
| `MegaCrit.Sts2.Core.Models.Orbs` | 5 | `LightningOrb`, `FrostOrb`, `DarkOrb`, `PlasmaOrb`, `GlassOrb` |
| `MegaCrit.Sts2.Core.Hooks` | 2 | `Hook`, `ModifyDamageHookType` |
| `MegaCrit.Sts2.Core.ValueProps` | 2 | `ValueProp`, `ValuePropExtensions` |

生成卡自身的引擎重写面 (`ChaosCardModel`), 证据 `src/AutoAnthony/ChaosCardModel.cs`:

`CanonicalStarCost`, `HasStarCostX`, `Pool`, `Type`, `Rarity`, `TargetType`, `PortraitPath`, `BetaPortraitPath`, `AllPortraitPaths`, `GainsBlock`, `CanonicalKeywords`, `Tags`, `AfterAutoPrePlayPhaseEnteredEarly`, `AfterAutoPostPlayPhaseEntered`, `AfterCardDrawn`, `BeforeHandDraw`, `AfterCardEnteredCombat`, `AfterAttack`, `BeforeCardPlayed`, `AfterDeath`, `AfterCardPlayedLate`, `AfterCardExhausted`, `AfterCardGeneratedForCombat` (行号 358-1384).

### 6.2 Harmony 补丁点完整清单 (128 个补丁类)

补丁类总数 128 (由 `ChaosBootstrap.cs:44-58` 用 `PatchClassProcessor` 反射批量应用).
"补丁类型"列取自各类中实际存在的方法名 (Prefix / Postfix / Transpiler / Finalizer / ReversePatch).

| 补丁类 | 目标类型 | 目标方法 | 补丁类型 |
| --- | --- | --- | --- |
| AfterCombatEndTimingPatch | Hook | AfterCombatEnd | Prefix,Postfix |
| AfterCombatVictoryTimingPatch | Hook | AfterCombatVictory | Prefix,Postfix |
| AfterDeathTimingPatch | Hook | AfterDeath | Prefix,Postfix |
| AncientRelicOptionUiAuditPatch | NEventRoom | SetOptions | Finalizer |
| ArchaicToothObtainedPatch | ArchaicTooth | AfterObtained | Prefix |
| ArchaicToothSentinelHoverTipPatch | ArchaicTooth | UpdateHoverTips | Postfix |
| ArchaicToothSetupPatch | ArchaicTooth | SetupForPlayer | Prefix |
| AutoAnthonyMainMenuSettingsSubmenuRegistrationPatch | NMainMenuSubmenuStack | GetSubmenuType | Prefix |
| AutoAnthonyRunSettingsSubmenuRegistrationPatch | NRunSubmenuStack | GetSubmenuType | Prefix |
| CanonicalizedMultiplayerSnapshotPatch | RunManager | CanonicalizeSave | Postfix |
| CapturedRewardPoolPatch | CardCreationOptions | GetPossibleCards | Prefix |
| CardGenerationModeHoverTipPatch | CardModel | HoverTips | Postfix |
| CardInternalIdHoverTipPatch | CardModel | HoverTips | Postfix |
| ChaosAbandonRunCleanupPatch | NMainMenu | AbandonRun | Prefix,Finalizer |
| ChaosAbilityTriggerGameActionBoundaryPatch | GameAction | Execute | Prefix |
| ChaosAbilityTriggerMonsterActionBoundaryPatch | Creature | TakeTurn | Prefix |
| ChaosAbilityTriggerTurnEndBoundaryPatch | Hook | BeforeSideTurnEnd | Prefix |
| ChaosAbilityTriggerTurnStartBoundaryPatch | Hook | BeforeSideTurnStart | Prefix |
| ChaosAfflictionApplyCompatibilityPatch | CardModel | AfflictInternal | Finalizer |
| ChaosAfflictionClearCompatibilityPatch | CardModel | ClearAfflictionInternal | Prefix,Finalizer |
| ChaosAncientRelicDescriptionPatch | RelicModel | DynamicDescription | Prefix |
| ChaosAncientRelicEventDescriptionPatch | RelicModel | DynamicEventDescription | Prefix |
| ChaosBasicCardAncientRelicDescriptionPatch | RelicModel | DynamicDescription | Prefix |
| ChaosBasicCardAncientRelicEventDescriptionPatch | RelicModel | DynamicEventDescription | Prefix |
| ChaosCardLibraryPoolPatch | NCardLibrary | _Ready | Postfix |
| ChaosCardLibraryVisibilityPatch | NCardLibraryGrid | RefreshVisibility | Postfix |
| ChaosCardTitlePatch | CardModel | Title | Prefix |
| ChaosDetachedDerivativePreviewPatch | CardModel | UpdateDynamicVarPreview | Postfix |
| ChaosDirectPortraitCompatibilityPatch | NCard | UpdateVisuals | Postfix |
| ChaosFullCombatStateAnonymizedPatch | NetFullCombatState | Anonymized | Postfix |
| ChaosFullCombatStateCapturePatch | NetFullCombatState | FromRun | Postfix |
| ChaosFullCombatStateDeserializePatch | NetFullCombatState | Deserialize | Postfix |
| ChaosFullCombatStateSerializePatch | NetFullCombatState | Serialize | Postfix |
| ChaosModelDbReadyPatch | ModelDb | InitIds | Postfix |
| ChaosOrbChannelContextPatch | OrbCmd | Channel | Prefix,Postfix |
| ChaosOrbEnqueueCompletionPatch | OrbQueue | TryEnqueue | Prefix,Postfix |
| ChaosPortraitTextureCachePatch | CardModel | Portrait (getter) | Prefix |
| ChaosPowerBigIconPatch | PowerModel | BigIcon | Prefix |
| ChaosPowerBigIconPathPatch | PowerModel | ResolvedBigIconPath | Postfix |
| ChaosPowerIconPatch | PowerModel | Icon | Prefix |
| ChaosPowerIconPathPatch | PowerModel | IconPath | Postfix |
| ChaosPowerPackedIconPatch | PowerModel | PackedIconPath | Postfix |
| ChaosPowerVisualPatch | NPower | Reload | Postfix |
| ChaosSettingsScreenPatch | NSettingsScreen | _Ready | Postfix |
| ChaosUnseenLibraryCardCostPatch | NCard | UpdateVisuals | Prefix,Postfix |
| ColorfulPhilosophersChaosPoolPatch | ColorfulPhilosophers | GenerateInitialOptions | Prefix |
| ColorlessPoolContentsPatch | CardPoolModel | AllCards (getter) | Prefix |
| CompactGeneratedPoolHistoryPatch | SaveManager | SaveRunHistory | Prefix,Postfix |
| CustomRunSnapshotModifierUiPatch | NCustomRunLoadScreen | OnSubmenuOpened | Prefix,Postfix,Finalizer |
| DailyRunSnapshotModifierUiPatch | NDailyRunLoadScreen | InitializeDisplay | Prefix,Postfix,Finalizer |
| DarvOptionGenerationAuditPatch | Darv | GenerateInitialOptions | Postfix,Finalizer |
| DefectPoolPatch | Defect | CardPool (getter) | Prefix |
| DefectStartingDeckPatch | Defect | StartingDeck (getter) | Prefix |
| DustyTomeObtainedPatch | DustyTome | AfterObtained | Prefix |
| DustyTomeSetupPatch | DustyTome | SetupForPlayer | Prefix |
| FutureOfPotionsChaosRewardCoveragePatch | TheFutureOfPotions | get_PotionToCardType | Postfix |
| GenerateRoomRewardsTimingPatch | RewardsCmd | GenerateForRoomEnd | Prefix,Postfix |
| GeneratedCardHistoryPlaceholderPatch | SaveUtil | CardOrDeprecated | Prefix |
| GeneratedCardHistoryScopePatch | NDeckHistory | LoadDeck | Prefix,Finalizer |
| GeneratedCardHistorySnapshotCleanupPatch | NRunHistory | OnSubmenuHidden | Postfix |
| GeneratedCardHistorySnapshotPatch | NRunHistory | DisplayRun | Prefix |
| GhostSeedChaosAfterObtainedPatch | RelicModel | AfterObtained | Prefix |
| GhostSeedChaosCardEnteredCombatPatch | GhostSeed | AfterCardEnteredCombat | Prefix |
| GhostSeedChaosRoomEnteredPatch | GhostSeed | AfterRoomEntered | Prefix |
| GhostSeedChaosUponPickupPatch | RelicModel | HasUponPickupEffect | Postfix |
| GhostSeedEtherealLoadPatch | CardModel | FromSerializable | Postfix |
| GhostSeedEtherealSavePatch | CardModel | ToSerializable | Postfix |
| GoopyChaosEligibilityPatch | Goopy | CanEnchant | Prefix |
| HangPowerPatch | HangPower | ModifyDamageMultiplicative | Postfix |
| IroncladPoolPatch | Ironclad | CardPool (getter) | Prefix |
| IroncladStartingDeckPatch | Ironclad | StartingDeck (getter) | Prefix |
| LargeCapsuleChaosPatch | LargeCapsule | AfterObtained | Prefix |
| LeafyPoulticeChaosPatch | LeafyPoultice | AfterObtained | Prefix |
| LegacyArchaicToothSerializationRepairPatch | RelicModel | FromSerializable | Postfix |
| MassiveScrollChaosAvailabilityPatch | MassiveScroll | IsAllowed | Postfix |
| ModInfoLocalizationPatch | NModInfoContainer | Fill | Prefix |
| ModMenuNameLocalizationPatch | NModMenuRow | _Ready | Prefix |
| MultiplayerGenerationMarkerTransportPatch | StartRunLobby | BeginRunLocally | Prefix |
| MultiplayerGenerationModePatch | StartRunLobby | BeginRunForAllPlayers | Prefix |
| MultiplayerHostCreationGuardPatch | NMultiplayerHostSubmenu | StartHostAsync | Prefix,Postfix,Finalizer |
| MultiplayerLoadLobbySnapshotPatch | LoadRunLobby | (constructors) | Postfix |
| MultiplayerPreparationClientLobbyRegistrationPatch | StartRunLobby | InitializeFromMessage | Postfix |
| MultiplayerPreparationDisconnectPatch | StartRunLobby | OnDisconnectedFromClientAsHost | Prefix |
| MultiplayerPreparationHostLobbyRegistrationPatch | StartRunLobby | AddLocalHostPlayer | Postfix |
| MultiplayerPreparationJoinPatch | StartRunLobby | HandlePlayerJoinedMessage | Postfix |
| MultiplayerPreparationLobbyCleanupPatch | StartRunLobby | CleanUp | Prefix |
| MultiplayerPreparationReadyChangePatch | StartRunLobby | HandlePlayerReadyMessage | Postfix |
| MultiplayerRejoinSnapshotPatch | ClientRejoinResponseMessage | Deserialize | Postfix |
| NativeCardPrivateDescriptionPatch | CardModel | GetDescriptionForPile (private, 3 参数) | Prefix |
| NativeCardUpgradeDescriptionContextPatch | CardModel | GetDescriptionForUpgradePreview | Prefix,Finalizer |
| NecrobinderPoolPatch | Necrobinder | CardPool (getter) | Prefix |
| NecrobinderStartingDeckPatch | Necrobinder | StartingDeck (getter) | Prefix |
| NeowsTalismanChaosPatch | NeowsTalisman | AfterObtained | Prefix |
| NutritiousSoupChaosPatch | NutritiousSoup | AfterObtained | Prefix |
| OrobasOptionGenerationAuditPatch | Orobas | GenerateInitialOptions | Postfix,Finalizer |
| PaelsClawChaosPatch | PaelsClaw | AfterObtained | Prefix |
| PandorasBoxChaosEventHoverTipsPatch | RelicModel | HoverTipsExcludingRelic | Postfix |
| PandorasBoxChaosHoverTipsPatch | RelicModel | HoverTips | Postfix |
| PandorasBoxChaosPatch | PandorasBox | AfterObtained | Prefix |
| RegentPoolPatch | Regent | CardPool (getter) | Prefix |
| RegentStartingDeckPatch | Regent | StartingDeck (getter) | Prefix |
| SaveGeneratedPoolPatch | RunManager | ToSave | Postfix |
| SaveProgressTimingPatch | SaveManager | SaveProgressFile | Prefix,Postfix |
| SaveRunTimingPatch | SaveManager | SaveRun | Prefix,Postfix |
| SeedBeforeLoadPatch | RunState | FromSerializable | Prefix,Postfix |
| SeedBeforeMultiplayerPatch | NGame | StartNewMultiplayerRun | Prefix |
| SeedBeforeSingleplayerPatch | NGame | StartNewSingleplayerRun | Prefix |
| SelfExhaustTriggerPatch | Hook | AfterCardExhausted | Postfix |
| SilentPoolPatch | Silent | CardPool (getter) | Prefix |
| SilentStartingDeckPatch | Silent | StartingDeck (getter) | Prefix |
| SurpriseModeBundleInspectPatch | NChooseABundleSelectionScreen | OpenPreviewScreen | Prefix |
| SurpriseModeCardDiscoveryPatch | SaveManager | MarkCardAsSeen | Prefix |
| SurpriseModeCardHolderHoverTipsPatch | NCardHolder | CreateHoverTips | Prefix |
| SurpriseModeCardVisualPatch | NCard | UpdateVisuals | Prefix,Postfix |
| SurpriseModeGridInspectPatch | NCardGridSelectionScreen | ShowCardDetail | Prefix |
| SurpriseModeMerchantHoverTipsPatch | NMerchantCard | CreateHoverTip | Prefix |
| SurpriseModeMerchantInspectPatch | NMerchantCard | OnPreview | Prefix |
| SurpriseModePooledCardRestorePatch | NCard | OnReturnedFromPool | Prefix |
| SurpriseModePreviewCardHolderHoverTipsPatch | NPreviewCardHolder | CreateHoverTips | Prefix |
| SurpriseModeRewardInspectPatch | NCardRewardSelectionScreen | InspectCard | Prefix |
| SurpriseModeSingleChoiceInspectPatch | NChooseACardSelectionScreen | OpenPreviewScreen | Prefix |
| SurpriseModeTrackSeenCardsPatch | CardPileCmd | Add | Postfix |
| TouchOfOrobasMultiplayerOwnerPatch | TouchOfOrobas | AfterObtained | Prefix |
| UltimateChaosHoverTipPatch | NFastModeHoverTip | OnHovered | Prefix |
| UltimateChaosOnTickPatch | NFastModeTickbox | OnTick | Prefix |
| UltimateChaosOnUntickPatch | NFastModeTickbox | OnUntick | Prefix |
| UltimateChaosSetFromSettingsPatch | NFastModeTickbox | SetFromSettings | Prefix |
| WriteReplayTimingPatch | RunManager | WriteReplay | Prefix,Postfix |

说明: `ChaosPortraitTextureCachePatch` 等使用 `MethodType.Getter`; `NativeCardPrivateDescriptionPatch`, `ChaosOrbChannelContextPatch`, `MultiplayerLoadLobbySnapshotPatch` 使用 `[HarmonyPatch]` 空构造 + `TargetMethod()/TargetMethods()` 动态指定. 这些是从 IL 特性 blob 与 C# 源码交叉确认的.

### 6.3 卡池替换的具体机制

| 补丁 | 目标 | 行为 |
| --- | --- | --- |
| IroncladPoolPatch / SilentPoolPatch / DefectPoolPatch / NecrobinderPoolPatch / RegentPoolPatch | `<角色>.CardPool` getter | 用 `ModelDb.CardPool<ChaosXxxCardPool>()` 替换返回值 (`CharacterPoolPatchRouting.ReplacePool<T>`, `:21`) |
| ColorlessPoolContentsPatch | `CardPoolModel.AllCards` getter | 对无色池与 5 个 Chaos 池重写内容; 支持 `preserveOriginalCards` (`:12`) |
| `<角色>StartingDeckPatch` | `<角色>.StartingDeck` getter | 当 `ActiveReplaceStartingCards` 为真时, 用生成卡的前 `BasicCountFor(character)` 张替换起始牌组 (`CharacterPoolPatchRouting.ReplaceStartingDeck`, `:30`) |
| CapturedRewardPoolPatch | `CardCreationOptions.GetPossibleCards` | 奖励池归一化 |
| ChaosCardLibraryPoolPatch | `NCardLibrary._Ready` | 卡牌图鉴展示 |

**结论: AutoAnthony 不修改任何原版卡的数值或效果.** 它替换卡池对象与起始牌组引用. 唯一的"接触原版卡"的开关 `DecomposeOriginalCards` 只改变**描述文本渲染** (把原版卡面文字按组件拆行显示), 不改变行为. 证据: `src/AutoAnthony/AutoAnthonyNativeCardApi.cs:22` (`ComponentDescriptionsEnabled`), `src/AutoAnthony.Patches/NativeCardComponentDescription.cs:59`, `src/AutoAnthony.Patches/NativeCardPrivateDescriptionPatch.cs`.

### 6.4 Ultimate Chaos 会跨角色混池

`CharacterComponentCatalogs.BuildUnlockedCatalog` 在 `unlockComponentRoles` 为真时, 把**全部 6 个角色的配方合并**成一个目录:

```
recipes = Enum.GetValues<GeneratedCharacter>().Select(Get).SelectMany(catalog => catalog.Recipes)
```

证据 `src/ChaosCardGenerator/CharacterComponentCatalogs.cs:26-33`.

`unlockComponentRoles` 的传入值是 `ultimateChaos` (位置参数第 3 个): `src/AutoAnthony/ChaosRunDefinitions.cs` 中 `new RandomCardGenerator(character, random.Next(), ultimateChaos, ...)`, 构造签名见 `src/ChaosCardGenerator/RandomCardGenerator.cs:35`. 即 **Ultimate Chaos 设置打开后, 原子池变成跨角色并集**, 重组自由度更大.

---

## 7. 诅咒 / 悔恨 是否在随机池中

### 7.1 结论

**诅咒不在生成的随机卡池里.** 但诅咒可以通过"衍生槽 (derivative slot)"作为**效果产物**被创建出来, 有一条 1% 的彩蛋通道.

### 7.2 证据

(1) 生成的卡类型与稀有度枚举不含诅咒/状态:

```
GeneratedCardType = { Attack, Skill, Power }          src/ChaosCardGenerator/GeneratedCardType.cs:3
GeneratedRarity   = { Basic, Common, Uncommon, Rare, Ancient }   src/ChaosCardGenerator/GeneratedRarity.cs:3
```

(2) 卡池期望分布只由上述稀有度构成, 证据 `src/AutoAnthony/ChaosRunDefinitions.cs:960` (`ExpectedRarities`).

(3) 514 个生成卡类型全部是 `ChaosCard*`, 没有 Curse 卡, 证据 `src/AutoAnthony/ChaosCardRegistry.cs:10,12`.

(4) 诅咒只作为**衍生槽定义**存在, 与状态牌并列, 共 33 条衍生槽定义, 其中诅咒 17 条. 证据 `src/ChaosCardGenerator/DerivativeSlotCatalog.cs:16-54`:

```
new DerivativeSlotDefinition("curse_guilty", Colorless, "愧疚", "Guilty", "Guilty", ...)   :47
new DerivativeSlotDefinition("curse_regret", Colorless, "悔恨", "Regret", "Regret", ...)   :51
```

另有 `curse_ascenders_bane, curse_bad_luck, curse_clumsy, curse_bell, curse_debt, curse_decay, curse_doubt, curse_enthralled, curse_folly, curse_greed, curse_injury, curse_normality, curse_poor_sleep, curse_shame, curse_spore_mind, curse_writhe`.

(5) 正常路径**禁止**诅咒: `DerivativeSlotCatalog.CanUse` 对 `IsCurse(definition)` 直接返回 false, 证据 `:404-406`. `Candidates` 也只走 `CanUse` 通过的项, 证据 `:448-460`.

(6) 唯一入口是彩蛋掷骰, 证据 `src/ChaosCardGenerator/DerivativeSlotCatalog.cs:465-469`:

```
public const double StatusCurseEasterEggChance = 0.01;      // :14
public static DerivativeSlotDefinition Roll(Random random, GeneratedCharacter currentCharacter, bool ultimateChaos, string template)
{
    if (IsStatusProducer(template) && random.NextDouble() < 0.01)
        return CurseEasterEggs[random.Next(CurseEasterEggs.Length)];
    ...
}
```

即: 当某个效果原子是"状态生产者" (`IsStatusProducer`, 定义 `:261`, 要求 `Usage == Produce` 且 capability 含 `Status`) 时, 有 **1% 概率** 改成随机一个诅咒. 触发后 `IsCurse` 被记录到 `ResolvedSlot.IsCurse`, 并使该卡的效果条数上限 +2 (`value = Math.Min(5, value + 2)`), 证据 `src/ChaosCardGenerator/ComponentAssemblyGenerator.cs:283-287`.

(7) 诅咒被实际创建成引擎卡模型的映射表, 证据 `src/AutoAnthony/ChaosDerivativeResolver.cs:230-266` (规范模型) 与 `:329-361` (战斗内创建), 其中:

```
"curse_guilty" => cardScope.CreateCard<Guilty>(owner)      :345
"curse_regret" => cardScope.CreateCard<Regret>(owner)      :355
```

### 7.3 Guilty (愧疚) 的 CombatsSeen 是否被处理

引擎参考实现 `G:/omp works/sts2-spire1/research/engine-dllsrc/MegaCrit.Sts2.Core.Models.Cards/Guilty.cs`:

* `:25-38` `[SavedProperty] public int CombatsSeen`, setter 里 `base.DynamicVars["Combats"].BaseValue = 5 - CombatsSeen;`
* `:45-56` `AfterCombatEnd(CombatRoom)`: 若 `base.Pile.Type == PileType.Deck` 则 `CombatsSeen++`, 达到 5 且仍在牌组时 `await CardPileCmd.RemoveFromDeck(this);`
* `:41` 构造为 `CardType.Curse, CardRarity.Curse, TargetType.None`, `:19` `MaxUpgradeLevel => 0`.

AutoAnthony 对它的处理:

* **没有任何补丁以 `Guilty` 为目标** (见 6.2 完整清单).
* **DLL 中 `CombatsSeen` 出现 0 次** (对 `AutoAnthony.il` 做字符串计数 = 0). 即 mod 从不读写该字段.
* `Guilty` 作为类型引用只出现 5 次, 全部在 `ChaosDerivativeResolver.cs` 与 `DerivativeSlotCatalog.cs`, 都是"把衍生槽映射成引擎卡"的用途.

因此结论: **悔恨类诅咒(含 Guilty) 的 `CombatsSeen >= 5` 从牌组移除行为完全由引擎原生逻辑承担, mod 不干预, 不复制, 不覆盖.** 由 chaos 卡效果生成的 Guilty 一旦进入牌组, 仍然会按原版规则在 5 场战斗后自动移除.

需要注意"愧疚"与"悔恨"是两个不同诅咒: `Guilty` 中文为"愧疚", `Regret` 中文为"悔恨". 用户提到的 `CombatsSeen` 逻辑属于 `Guilty`.

### 7.4 一个易混淆点

`AfterCombatEndTimingPatch` 补丁了 `Hook.AfterCombatEnd`, 但它只做计时 (Stopwatch 包裹 + 记录耗时), 不改逻辑, 证据 `src/AutoAnthony.Patches/AfterCombatEndTimingPatch.cs` (Prefix 仅 `CombatEndTiming.Start()`, Postfix 仅 `CombatEndTiming.Observe(...)`). 不会影响 Guilty 的 `AfterCombatEnd`.

---

## 8. 不确定项清单

明确区分"从代码/数据确证"与"推断或未验证".

### 8.1 从代码或数据确证

* 原子片段的数据结构 (`ComponentAtom` / `OperationRuntimeSpec` / `RuntimeConditionSpec` / `RuntimeTriggerSpec` / `RuntimeValueSlot` / `IroncladCardRecipe` / `GeneratedCard`) -- 全部由反编译源码直接读出.
* 触发与效果在数据层独立, 在生成时通过 `triggerIndex` 随机绑定 -- 由 `LinkedTriggerIndex` 的控制流与 `ExecuteTriggered` 的分发逻辑确证.
* 普通触发 50% 绑定概率 -- 由 `_random.Next(2) != 0` 直接确证.
* 同 seed 卡池可复现 -- 由 `StableSeed` = SHA256 派生 + 全程只用 `System.Random(seed)` 确证 (但见 8.2 的版本绑定前提).
* 诅咒不在生成池 -- 由 `GeneratedCardType` / `GeneratedRarity` 枚举, `ChaosCardRegistry` 的 514 个类型, `ExpectedRarities` 三处一致确证.
* 诅咒仅通过 1% 彩蛋进入 -- 由 `DerivativeSlotCatalog.Roll` 与 `CanUse` 确证.
* Guilty 的 `CombatsSeen` 未被 mod 触碰 -- 由 IL 字符串计数 0 与补丁清单无 Guilty 确证.
* 128 个补丁类的目标类型/方法/补丁类型 -- 由 IL 特性 blob 解码 + C# 源码交叉确认.
* Ultimate Chaos 会合并全部角色的原子池 -- 由 `BuildUnlockedCatalog` 确证; `ultimateChaos -> unlockComponentRoles` 的实参映射由构造调用点确证.

### 8.2 推断或未验证

* **多人两端实际一致性未经实机验证.** 代码显示"同 (mod 版本, 游戏版本, 角色, seed) 应产生同一卡池", 且有一致性指纹校验与主机快照兜底, 但我没有运行双客户端测试. 另外种子盐串硬编码 `v111` / `all-pools-v2`, 说明作者自己也在用版本串规避跨版本不一致.
* **`StableSeed` 的盐串是否与游戏版本强绑定未验证.** 盐串只含 `AutoAnthony/v111/all-pools-v2` 与角色名, 不含游戏版本号; manifest 声明 `min_game_version 0.111.0`. 若游戏版本升级而 mod 未更新, 是否仍逐位一致属未验证.
* **`Roll` 的完整调用链未逐一跟踪.** 我确认了 `Roll` 的判据与概率, 但"哪些模板会被判定为 `IsStatusProducer`"只做了抽样 (`Sources` 字典中 `Usage == Produce` 且 capability 含 `Status` 的项), 没有穷举全部 33 条衍生槽的 `Status` 标记.
* **`CanAssemble` 与生成路径的边界.** 我确认 `CanAssemble` 使用 `recipe.TriggerOwners` 做原版配方自校验, 而 `TryGenerate` 用动态 `triggerIndex`. 但"生成路径是否会在某些分支回退到配方路径"(例如 `GenerateEmergencyFallback`, `:1095`)我只读了入口, 没有逐行跟踪全部回退分支.
* **`ChaosCompositePower` 的触发宿主机制未完全展开.** 触发在运行期似乎被建模为复合 Power (存在 `ChaosCompositePower` 与 `ChaosAbilityTriggerLimiter`, 单效果最多激活 20 次). 触发如何从"卡上的操作序列"变成"战斗中的 Power 实例", 我只确认了 `ExecuteTriggered` 的入口与索引分发, 没有读完整个注册/注销生命周期.
* **诅咒彩蛋与 `value + 2` 的交互未实机验证.** `IsCurse` 为真时效果条数上限放宽, 这会影响拼装循环的后续行为, 但实际手感未验证.
* **部分 Harmony 特性 blob 的解码存在字符长度歧义.** 少数条目 (如 `SurpriseModeGridInspectPatch`, `MultiplayerRejoinSnapshotPatch`, `ChaosOrbChannelContextPatch`, `MultiplayerLoadLobbySnapshotPatch`, `NativeCardPrivateDescriptionPatch`) 的 IL blob 解析出现字节错位, 我改用 C# 源码中的 `TargetMethod()` / `Accessor` 交叉确认后填入, 置信度较高但非纯自动解析结果.
* **`ilspycmd` 反编译的可读性损失.** 大量方法体含 `//IL_xxxx: Unknown result type` 注释与 `(uint)(scope - 3) <= 1u` 这类优化后形式; 行为语义我按枚举取值还原, 未与原始 IL 逐条比对.
* **行号是反编译产物的行号, 不是原始源码行号.** 引用时请以类型名 + 方法名为主键, 行号仅作定位辅助.

---

## 9. 对新 mod 的机制契约要点 (研究结论, 非代码)

如果新 mod 要"忠于东尼算法原始机制", 必须满足以下可检验条件:

1. **必须在数据层把"条件/触发"与"效果"拆成两类独立片段**, 且片段的数据表示要携带足以重建语义的结构 (opcode + 目标 + 作用域 + 数值槽 + 条件 + 触发), 而不只是文本模板.
2. **采样时必须从同一池子里独立抽条件与效果**, 不允许"条件携带其原有搭档效果".
3. **绑定必须是生成期的独立步骤**, 且绑定结果要能在运行期被解释器按索引分发 (`triggerIndex` 语义).
4. **普通条件的绑定应当带随机性** (原版约 50%), 否则复现不出"条件重组"的观感.
5. **触发片段自身不得被再次绑定到另一个触发** (原版 scope 3/4/5 一律返回 -1).
6. **"条件重组"的验收样例**: 必须能生成出"每当你在回合内失去生命" + "失去 1 点生命" 这种组合, 即把原版 `Rupture` 的触发接到原版 `Inferno` 的 `N:HP-` 效果上.
7. **可复现性**: 卡池构成必须完全由 (角色, 种子) 决定, 且派生自哈希而非 `Random.Shared`; 版本串要参与盐值.
8. **诅咒**: 若要保持原版观感, 诅咒不应进入随机池, 只应作为低频效果产物; 且不应复制/覆盖引擎对 `Guilty.CombatsSeen` 的处理.

---

文档结束.
