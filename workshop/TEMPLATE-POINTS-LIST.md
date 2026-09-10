# 词条点数清单 - v0.5 预算系统 (给用户定默认值)

生成规则: 每件遗物按稀有度获得点数预算, 正词条消耗 = 每点成本 x 数值,
负词条返还 = 每点返还 x 数值 (可再买一条正词条). 生成顺序: 先花预算买正词条
(数量在区间内随机, 受剩余预算压缩) -> 掷负面概率 -> 若出负面, 返还点数继续买.

当前默认预算: 普通 10 / 罕见 16 / 稀有 24
当前默认负面概率: 普通 35% / 罕见 55% / 稀有 75%
实测分布 (40 种子): 正词条均值 2.8 / 3.9 / 5.0, 负面率 38% / 58% / 76%

## 正面词条 (27 种) - "每点成本" = 每单位数值消耗的点数

| 模板 | 效果 | 数值区间 | 每点成本(默认) |
|------|------|---------|--------------|
| C_START_DAMAGE_ALL | 战斗开始: 对全体敌人伤害 | 3-8 | 2 |
| C_START_BLOCK | 战斗开始: 获得格挡 | 4-10 | 2 |
| C_START_STRENGTH | 战斗开始: 力量 | 1-3 | 4 |
| C_START_DEXTERITY | 战斗开始: 敏捷 | 1-3 | 3 |
| C_START_DRAW | 战斗开始: 抽牌 | 1-3 | 3 |
| C_START_ENERGY | 战斗开始: 能量 | 1-3 | 5 |
| C_START_VULN_ALL | 战斗开始: 全体敌人易伤 | 1-3 | 3 |
| C_START_WEAK_ALL | 战斗开始: 全体敌人虚弱 | 1-3 | 3 |
| C_START_REGEN | 战斗开始: 再生 | 1-4 | 3 |
| C_START_THORNS | 战斗开始: 荆棘 | 1-3 | 3 |
| C_START_ARTIFACT | 战斗开始: 护体 | 1-1 | 5 |
| C_START_POISON_ALL | 战斗开始: 全体敌人中毒 | 2-6 | 2 |
| C_START_PLATING | 战斗开始: 镀层(回合结束获得格挡) | 1-4 | 2 |
| T_START_BLOCK | 每回合: 格挡 | 2-5 | 4 |
| T_START_ENERGY | 每回合: 能量 | 1-1 | 8 |
| T_START_HEAL | 每回合: 回复生命 | 1-3 | 5 |
| T_START_DRAW | 每回合: 抽牌 | 1-1 | 8 |
| PLAY_DAMAGE_RANDOM | 出牌: 对随机敌人伤害 | 1-4 | 3 |
| PLAY_BLOCK | 出牌: 获得格挡 | 1-3 | 3 |
| PASSIVE_ATTACK_DAMAGE | 攻击牌伤害+ | 1-4 | 3 |
| PASSIVE_MAX_ENERGY | 能量上限+ | 1-1 | 8 |
| PASSIVE_BLOCK_ADD | 获得格挡时格挡值+ | 1-2 | 4 |
| VICTORY_HEAL | 胜利: 回复生命 | 2-8 | 1 |
| VICTORY_GOLD | 胜利: 金币 | 5-20 | 1 |
| PASSIVE_GOLD_GAIN | 金币获得+ | 1-3 | 2 |
| REST_HEAL_BONUS | 营火休息额外回复 | 1-5 | 1 |

## 负面词条 (10 种) - "每点返还" = 每单位数值返还的点数

| 模板 | 效果 | 数值区间 | 每点返还(默认) |
|------|------|---------|--------------|
| N_START_FRAIL_SELF | 战斗开始: 自身脆弱(-25%格挡) | 1-2 | 4 |
| N_TURN_LOSE_HP | 每回合: 失去生命 | 1-3 | 3 |
| N_TURN_ENERGY_DOWN | 能量上限- | 1-1 | 6 |
| N_TURN_DRAW_DOWN | 每回合抽牌数- | 1-1 | 6 |
| N_GOLD_DOWN | 金币获得- | 1-3 | 2 |
| N_POTION_BLOCK | 无法获得药水 | - | 6 (固定) |
| N_START_SLOTH_SELF | 战斗开始: 怠惰(限制出牌数) | 1-1 | 5 |
| N_REST_HEAL_DOWN | 营火休息回复- | 1-4 | 2 |
| N_ATTACK_DAMAGE_DOWN | 攻击牌伤害- | 1-2 | 3 |
| N_MAX_HP_DOWN | 获得时最大生命值- | 1-4 | 3 |

## 调整方式

- 预算与负面概率: 游戏内 设置 -> Mod 设置 -> AutoAnthony - Relics
  (或直接改 `mod_configs/AutoAnthonyRelics.cfg`)
- 每词条点数: 同 cfg 文件内加 `Cost_<模板名>` / `Refund_<模板名>` 键
  (例: `"Cost_C_START_STRENGTH": "4"`)
- 注意: 联机时两端预算/概率/点数必须一致 (确定性键)
