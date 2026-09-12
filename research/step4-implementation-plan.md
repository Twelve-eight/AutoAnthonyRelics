# 第 4 步实施计划 - 新 mod `AutoAnthonyRelics` (真正的东尼算法-遗物)

依据: `HANDOFF-2026-09-12-PT2.md` D1-D7 + 第 4 步;
机制契约: `research/original-autoauthony-contract.md` §9 (8 条验收条件);
数据证据: `G:/omp works/.tmp/aa-decompile/src/` 四个内嵌 JSON。

---

## 0. 两条已裁定的前提

| 项 | 裁定 | 来源 |
| --- | --- | --- |
| mod id | **`AutoAnthonyRelics`** | 用户裁定 (AskUserQuestion) |
| 显示名 | **`东尼算法 - 遗物`** / `Anthony Algorithm - Relics` | 交接文档第 4 步标题 + D1 把旧名腾出来 |
| 代码来源 | **思想重写, 不移植反编译代码** | `HANDOFF-2026-09-12.md` 0.2 原文: "用户要求**思想重写**一个新 mod" |
| 数据来源 | **复用原版生成池** (效率优先) | 用户裁定 "向效率看齐" |
| 仓库位置 | **新仓库 `G:/omp works/sts2-autoanthony-relics/`** | 沿用 `sts2-mpconfigsync` / `sts2-perfect` / `sts2-spire1` 的兄弟仓库惯例; 与 Qurious 仓库 (`G:/omp works/AutoAnthonyRelics/`, 根目录保持不动) 区分 |

**署名与衍生声明是硬要求**: 数据复用 Alriph 的离线产出, 必须在 manifest `description`、
workshop 描述、`DEVELOP.md` 三处写明数据来源与原作者, 并说明本 mod 与 `AutoAnthony`
的关系。这不是可选的美化。

---

## 1. 数据侧实测 (本次盘点结果, 不是估计)

`catalog_recipes.json` 481 条配方 / 931 个原子实例;
`catalog_runtime_specs.json` 931 条, 与原子 **1:1 且零孤儿** (931/931 join,
`atom 无 spec = 0`, `spec 未被引用 = 0`)。

角色分布: Silent 86 / Defect 86 / Necrobinder 86 / Regent 86 / Ironclad 85 / Colorless 52。

Scope 分布 (931): `NonTargeted` 426 / `SingleEnemyOnly` 205 / `Independent` 84 /
`AbilityTrigger` 77 / `Modifier` 60 / `ConditionalTrigger` 58 / `AbilityRule` 21。

Template 前缀分布: `N` 292 / `T` 179 / `NCR` 91 / `A` 89 / `D` 73 / `R` 60 / `I` 47 /
`C` 46 / `CL` 39 / `M` 15。

### 1.1 解释器的真实实现面 (关键数字)

| opcode | variant 数 | 实例数 |
| --- | --- | --- |
| `template_self_action` | 72 | 130 |
| `template_independent_action` | 70 (含 19 个 `proxyatomic`) | 72 |
| `template_modifier` | 42 | 46 |
| `combat_rule` | 21 (含 6 个 `proxyatomic`) | 21 |
| `apply_power` | 18 | 76 |
| `condition` | 18 | 23 |
| `template_target_action` | 17 | 33 |
| `deal_damage` | 7 | 174 |
| `create_card` / `exhaust_card` / `modify_damage` | 4 / 4 / 4 | 7 / 11 / 4 |
| `trigger` | 2 | 116 |
| 其余 21 个 opcode | 各 1-3 | 各 1-13 |

合计 **33 个 opcode / 291 个 variant / 306 个 (opcode,variant) 组合**。

辅助维度 (口径必须说清, 否则会数错):
- **86 个 flag**
- **18 个 condition kind**, 且 18 个完整 condition spec (23 个实例)
- **55 个 trigger kind**, 但 **57 个完整 trigger spec** (= 57 个不同 `(Kind, Lifetime)` 对);
  116 个 trigger 实例; **6 个 Lifetime 取值** (`combat` / `delayed` / `immediate` /
  `next_n_turns` / `next_turn` / `this_turn`)。**解释器要按完整 trigger spec 绑定,
  不能只按 Kind** —— 只按 Kind 会丢 2 个区分。
- **15 个 target** / **7 个 source zone** / **4 个 destination zone** / **5 个 card filter**

`Values` 长度分布: 0 个 254 条, 1 个 607 条, 2 个 70 条。

### 1.0 生成器实际迭代的池子是按角色分的**片段**池 (不是配方集)

阶段 A 探针实测 (单位是片段实例, 不是配方数):

| 角色 | 配方数 | 片段池大小 |
| --- | --- | --- |
| Regent | 86 | 178 |
| Necrobinder | 86 | 176 |
| Ironclad | 85 | 171 |
| Defect | 86 | 170 |
| Silent | 86 | 149 |
| Colorless | 52 | 87 |
| 合计 | 481 | 931 |

同一角色的配方数相近, 但片段池差了 29 (Silent 149 vs Regent 178) —— 即每条配方的原子数
并不均匀。契约 §4.1 的"遍历**该角色整个原子池**"指的是右列。

按契约 §9 条件 1 的分法实测: **效果侧片段 775 / 触发侧片段 156**, 合计 931;
触发侧有 **90 个不同模板**。所以生成器可以仅凭片段自身的 scope 就判定"这是触发,
不许再被绑到另一个触发上" (条件 5)。

**结论: 要手写的是 201 个 template variant** (72+70+42+17), 其中 25 个
`*_proxyatomic_*` 是"转交给引擎原生卡", 实现成本≈查表。
剩余 ~176 个是角色专属的具体效果 (如 `d_channelfrost` = 引导冰球,
`n_createshiv` = 生成飞刀, `ncr_summon` = 召唤 Osty, `r_forge` = 锻造),
每个是一小段引擎 API 调用。

### 1.2 数据能自证到哪一步

- `native_reference_cards.json` (2.4 MB, `ReferenceOnly: true`,
  `GenerationEligible: false`) 是**引擎原生卡**在同一套 opcode/variant 语言下的完整分解,
  用途写明是 "Complete non-multiplayer native-card reconstruction reference"。
  它是逐条对照的黄金参照, 也是 §9 条件 6 验收样例的来源。
- `native_reference_components.json` (451 条组件) 中 **56 条**是
  `RuntimeContract: {Kind: ...}` 形态的组件契约 (带 `Parameters` + `ExampleText`),
  另 395 条的 `RuntimeContract` 与 runtime spec 同形 (是原生卡的样例 spec)。
  **它不给出 201 个 template variant 的可执行语义** —— 那部分只在原版代码里。
  所以解释器必须自写, 不能靠数据驱动。

---

## 2. 分阶段实施 (每阶段有可判定的验收)

### 阶段 A - 骨架 + 数据层  **[已完成 2026-09-12]**

- 新仓库 `G:/omp works/sts2-autoanthony-relics/`, 按 `QuriousCraftingRelics.csproj`
  模板建 csproj (Publicize sts2, BaseLib 3.4.5 NuGet, PckPacker, 部署 targets) +
  `project.godot` + `AutoAnthonyRelics.json` manifest + `MainFile.cs`。
- 两个 generation-eligible JSON **作为嵌入资源打进 dll** (不是 `res://` 资源)。
  理由: 隔离探针要能在**不触碰 Godot** 的前提下读池子 (Godot 静态构造在引擎外是
  原生访问违例, try/catch 接不住), 且顺带把 pck 打包路径从数据层移除。
- C# typed model: `AtomRecipe` / `AtomFragment` / `RuntimeSpec` / `NumericSlot` /
  `ConditionSpec` / `TriggerSpec` + 闭集枚举 (7 scope / 33 opcode / 9 target /
  7 zone / 5 card filter)。**所有枚举解析都是全函数**, 未知值降级到 `Unknown`
  而不抛异常 —— 数据升级不得让 mod 初始化或某一局崩掉。
- **验收 (已通过)**: 探针 `G:/omp works/.tmp/aar-step4-probe/` **62/62 断言全绿**,
  与独立 Python 实现 (`G:/omp works/AutoAnthonyRelics/.tmp/step4/inventory*.py`)
  逐项交叉核对: 481/931/931、6 角色分布、7 scope、33 opcode (含逐项实例数)、
  291 variant / 306 组合 / 86 flag / 18 condition / 55 trigger kind / 57 trigger
  spec / 6 lifetime / 15 target / 7+4 zone / 5 filter、Values 长度分布。
  另: 池子摘要 SHA256 在进程内稳定 (`78F50545917EE265…`), 数据层是纯函数。
- 构建 `0 警告 / 0 错误` + `PCK packed`; dll 内可见两个嵌入资源
  (512500 / 618101 字节)。
- 过程中修正了一处**自己的度量口径错误**: 原稿写"57 个 trigger"是按完整 trigger
  对象去重, 而 `Kind` 去重只有 55。已在 §1 写明两个口径。

### 阶段 B - 生成器核心 (条件重组机制本体)

按契约 §4.1-4.3 实现:

- 逐槽采样: 清空候选表 -> 遍历该角色整个原子池 -> `IsCompatible(type, target,
  cost, hasStarCostX, atom, previous)` 过滤 -> 按稀有度加权随机抽
  (`PickForRarity`) -> 实例化数值槽 -> `LinkedTriggerIndex` 决定绑定 -> 追加。
- `AdaptiveEffectCountWindow` 动态槽位数 (常见 2-5 条)。
- `LinkedTriggerIndex` 的判定顺序照契约 §4.3 的表逐条实现, 含
  **普通触发 `_random.Next(2) != 0` 即约 50% 绑定**。
- 确定性: `System.Random(SHA256("{ModId}/v1/all-pools/{character}/{seed}"))`,
  版本串参与盐值。

**验收 (对应 §9 条件 1-5 + 7, 全部可由探针判定, 不需要解释器)**:

1. 条件与效果在数据层是两类独立片段, 且携带 opcode+目标+作用域+数值槽+条件+触发。
2. 采样从同一池子独立抽条件与效果, 不存在"条件携带原搭档效果"。
3. 绑定是生成期的独立步骤, 结果写入 `triggerIndex` 可被运行期按索引分发。
4. 普通条件的绑定率实测落在 50% 附近 (统计 N 个种子的绑定/不绑定比)。
5. 触发片段自身 (`Scope ∈ {AbilityTrigger, ConditionalTrigger, AbilityRule}`)
   **从未**被绑定到另一个触发 (探针遍历全部种子断言 `triggerIndex == -1`)。
7. 同 (角色, 种子) 跨进程字节一致; 换版本串结果改变。

### 阶段 C - 解释器 (最大的一块, 按 opcode 分片)

按 33 个 opcode 分片实现, 优先顺序按实例数:

1. `deal_damage` (174) / `gain_block` (78) / `apply_power` (76) / `draw_cards` (50) /
   `gain_energy` (30) / `gain_stars` (13) / `lose_hp` (11) —— 先让卡能打出伤害与格挡。
2. `trigger` (116) + `condition` (23) —— `triggerIndex` 的运行期分发本体。
3. 201 个 template variant, 按角色分批 (Ironclad 85 -> Silent 86 -> Defect 86 ->
   Regent 86 -> Necrobinder 86 -> Colorless 52), 每批用
   `native_reference_cards.json` 的原生卡逐条对照。
4. 21 个 `combat_rule`。

`proxyatomic` 一律委派引擎原生卡模型, 不重写效果。

**验收**: 每批实现后, 用探针对该角色的原生参照卡做"同一 spec 解释结果一致"的断言。

### 阶段 D - §9 条件 6 的验收样例 (可判定的里程碑)

必须能生成出 **"每当你在回合内失去生命" + "失去 1 点生命"** ——
即把原版 `Rupture` 的触发接到原版 `Inferno` 的 `N:HP-` 效果上。
触发取 `Trigger.Kind = owner_hp_lost_during_turn`, 效果取 `lose_hp`。
探针需要: 固定角色 + 遍历种子直到生成该组合, 打印该卡的完整 spec 与渲染文本。

### 阶段 E - 可复现性 + 联机一致性

- 版本盐 + (角色, 种子) 派生, 与阶段 B 的确定性合并验证。
- 指纹: 遍历全部卡槽, 把 Cost / StarCost / Type / Target / Rarity / Tags /
  Operations (含 `Template` / `Scope` / `DerivativeId` / `Parameters.triggerIndex`)
  串进 SHA256 —— 照原版 `ChaosPoolSnapshot.cs` 的字段集。
- 主机权威快照 + 分块下发 + 客户端重算比对 (契约 §4.4)。

### 阶段 F - 悔恨式额外池

引擎参考 `MegaCrit.Sts2.Core.Models.Cards/Guilty.cs`:
`[SavedProperty] public int CombatsSeen`, `>= 5` 且在牌组里时
`CardPileCmd.RemoveFromDeck`。额外池与主随机池分离, 独立开关。
**不得复制/覆盖引擎对 `Guilty.CombatsSeen` 的既有处理** (§9 条件 8 后半)。

### 阶段 G - 诅咒

按 §9 条件 8: 诅咒不进随机池, 只作衍生槽 (derivative slot) 的低频产物。
原版有 33 条衍生槽定义 (其中诅咒 17 条), 见契约 §7.2。

---

## 3. 阶段依赖

```
A (骨架+数据层)
 └─> B (生成器核心) ──> D (验收样例, 需要 C 的 deal_damage/lose_hp/trigger)
      │                    ▲
      └─> C (解释器) ──────┘
           └─> E (可复现 + MP)
      F, G 可与 C 并行 (F 独立, G 依赖衍生槽)
```

D 只依赖 C 的第 1-2 片, 所以 **C 分片实现到第 2 片就能做 D** —— 这是最早的
"能证明条件重组真的成立"的里程碑, 优先做。

---

## 4. 与原版的关系声明 (必写进 manifest / workshop / DEVELOP)

- 数据 (`catalog_recipes.json` / `catalog_runtime_specs.json`) 源自
  **Alriph** 的 `Auto-Anthonyology` (mod id `AutoAnthony`, v0.3.81)。
- 代码 (生成器 / 解释器 / 渲染) 为本仓库思想重写, 不移植反编译产物。
- 两个 reference JSON (`native_reference_cards` / `native_reference_components`)
  本身是引擎卡牌的转储, 随数据一并携带以便对照。
- 本 mod 与 `AutoAnthony` 是独立 mod; 同装时数据重复但行为互不依赖。

---

## 5. 未决项

- 中文显示名最终形态 (`东尼算法 - 遗物` 是交接文档的写法, 未单独拍板)。
- 是否保留原版的英文卡名生成器 (`CardNameGenerator.cs`, 44 KB) 的行为 ——
  它有自己的 SHA256 派生流, 影响卡名但不影响机制。
- 阶段 C 的 201 个模板里, 哪些可以合理地"降级"为文本 + 近似效果
  (原版有少数效果依赖引擎内部状态, 重写成本极高)。需要逐条评估后回报。
