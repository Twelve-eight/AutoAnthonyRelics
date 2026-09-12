# Astra advice - Qurious Crafting - Relics

日期: 2026-09-12. 目录仍叫 AutoAnthonyRelics, 但当前产品 id/命名空间是 QuriousCraftingRelics. 不要与兄弟目录 sts2-autoanthony-relics 混淆.

本轮只评估和写建议. 构建副本成功, 0 警告/0 错误. 直接调用构建 DLL 做了配置迁移, 定义变化和分配探针; 未运行新游戏战斗/UI. 证据见 [总索引](../astra-advice.md) 与 `../astra-advice-evidence/2026-09-12/`.

## 保留的设计优点

- 60 固定 slot, 20/20/20 稀有度, 模型身份稳定可枚举.
- 目录, 预算生成, 运行期执行有明确分层.
- core/extra 的解析和计价已统一到 ChaosTemplates.
- 显式模板顺序替代 Dictionary 枚举依赖.
- 独立 RrcA4hKeyFix 与本 mod 编译同一份兼容源码, 用 Harmony registry 识别重复补丁.
- 既有 BeforeCombatStart 能量丢失, owner guard, retain 预览消耗等修复有具体源和证据. 不应全部重写回旧实现.

## P1 QCR-1: 迁移器会误搬新模组配置, 覆盖原备份

位置: `mod/Code/ConfigMigration.cs:42-44,67-85,112-116`, `MainFile.cs:34`.

REPRO: 新模组配置 `AutoAnthonyRelics.cfg={"Enabled":"False"}`; 已有 Qurious cfg 和旧 `.v0.5.1.bak`. 调用真实 MigrateLegacyConfig(tempDir):

- newModConfigStillExists=false.
- oldBackupPreserved=false.
- Qurious 文件出现陌生 Enabled=False.
- 原 Qurious 预算值仍在, 但真正的旧备份被新模组文件覆盖.

原因: 每次启动只按文件名判断旧配置, 没有 schema/完成标志, File.Move overwrite=true.

修复建议:

1. 识别可证明属于旧 Qurious 的 key/schema, 拒绝把只有新模组键的文件当旧配置.
2. 成功迁移后有单独完成记录. 旧文件名后来重用, 也不能再迁移它.
3. 备份不可覆盖; 写新 cfg 采用临时文件+原子替换, 验证成功后才搬源文件.
4. 目标已存在时的合并策略保留用户新值, 但未知来源文件不能污染目标.

验收: 真旧配置完整保值; 重跑幂等; 新模组 cfg 原位不动; 已有备份 byte-identical; 损坏输入不毁源; 中断恢复不把默认值固化为成功.

不要通过改新模组 id 回避这个缺陷. 用户已经决定 id 腾给新项目.

## P1 QCR-2: 每次 live 配置查询重生成, 没有本局不可变定义

位置: `Chaos/ChaosRelicRunRegistry.cs:35-88,93-101`; `Models/ChaosRelicModel.cs:81-84,145`; `Patches/RunSeedTrackPatch.cs:50-58`.

REPRO: ASTRA-SEED-1, 先用默认预算生成, 再把 C/U/R 预算全部设为 1, 再查同 seed. 60/60 槽位改变, 缓存对象不同.

这修复了 "只按 seed 缓存旧值" 的表象, 却没有定义本局配置的生效边界. 旧 DEVLOG Session 42 F04 已明确警告不能简单清缓存让持有遗物变义; 后续 fingerprint 方案没有完成这个契约.

直接后果/风险:

- 中途在设置页调价, 已持有 relic 的效果/名称随下一次查询变化.
- 一次性 MaxHp 负面在 AfterObtained 只扣过一次, 但当前定义之后可能丢掉该负面或换成另一条.
- 本机 cfg 改动/更新算法后读档, 旧 slot 重新解释为新遗物.
- MpConfigSync 晚到也会更换定义, 但已经填充的 bag, 描述缓存, 已执行一次性效果不会一并回滚.
- CurrentRunSeed 在新局 prefix/Launch 写入, 没有 CleanUp 清零. 菜单查询继续引用上一局定义, 和注释 "菜单 null" 不一致.

推荐契约:

- 开局/读档时建立不可变 ActiveRunContext: seed, 生成版本, catalog hash, 有效配置快照, 全部定义.
- 实际遗物按 slot 从该上下文取定义, 不读取 live 偏好.
- 修改偏好作用于下一局; 如果要局内重铸, 作为明确命令另行设计, 不能隐式发生.
- 存档保存精确定义或足以严格重建且有版本支持的快照; 恢复失败要显式处理, 不静默重抽.
- CleanUp 清 active context; 本局定义/历史预览另分生命周期.

验收: 同局改预算/开关不改变已持有定义; 新局使用新设置; 新进程读旧档相同; 两局之间不串 seed; 旧算法不支持时不误读为新版本.

## P1 QCR-3: 缓存命中仍有重分配热路径

位置: 同上 ConfigFingerprint; `ChaosTemplates.cs:51-72`; `QuriousCraftingRelicsConfig.cs:405-412,457-470`.

REPRO: 已生成且命中缓存, 1000 次 ForSeed 直接 delegate 调用分配 83,968,000 字节, 约 83,968 B/次. 没有把 MethodInfo.Invoke 分配算进去. 时间只作本机探针记录, 不宣称等同实际帧耗时.

Rarity/ShowCounter/DisplayAmount/AmountOf 都会进入这条路. 每个伤害查询还可能多次 AmountOf. 缓存容器是 O(1), 但每次命中前已经构建/排序列表, 格式化全量配置, 多次反射取值和构造属性名.

先实现 QCR-2 的冻结上下文, 稳态查询按 slot 直接取记录. 不要只缓存 StringBuilder 或换 ConcurrentDictionary. 不要把可变 DynamicVar/有 owner 的模型静态共享.

验收: 相同 active context 内万次定义查询无按模板数增长的分配, 无反射/排序/配置 IO; 修改下局配置不污染当前定义.

## P2 QCR-4: 文案按 seed 缓存, 定义按 seed+config 缓存

位置: `Patches/ChaosRelicLocUpdater.cs:28-40`; `RunSeedTrackPatch.cs:57-58,76-80`.

SOURCE: _lastSeed 相同就返回, 且在真正写表前就置位. 定义变化而 seed 不变时, 显示仍是旧效果; 首次写表失败后同 seed 不再重试. 语言切换重建表也没有这个缓存的有效性维度.

不要单独扩大 _lastSeed key 然后宣称解决: 先让 QCR-2 保证运行期定义不漂移. 再按上下文/语言/表重建版本刷新, 成功提交后才更新缓存状态. 检查退出本局后的菜单/历史描述.

验收: 描述解释的就是实际执行的同一份 Definition; 读档, 语言切换, 表重新加载, 首次失败后重试均一致.

## P1 QCR-5: cfg 迁移不是存档 ID 迁移

SOURCE: 命名空间/模型前缀从 AUTOANTHONYRELICS- 改为 QURIOUSCRAFTINGRELICS-. 当前源码只有 cfg key/文件迁移, 没有 SerializableRelic 旧 ID 转换. 引擎 `RelicModel.FromSerializable -> SaveUtil.RelicOrDeprecated` 对旧 ID 返回 DeprecatedRelic.

INFERENCE: 旧局持有的混沌遗物/遗物袋槽位可能变成无效占位, 新项目以后复用旧前缀时还可能误解析为另一产品. 本轮没有拿用户真实存档执行迁移.

建议在保存结构边界迁移旧 Qurious 模型 ID, 同时处理持有遗物与 bag/original 列表, 记录来源生成版本. 用户已授权改 id, 不等于授权丢弃已有遗物. 如果产品明确不支持旧局, 必须说明并提供备份/阻止误读, 不宣称无损迁移.

## P2 QCR-6: 独立蓝钥匙修复的加载与同步契约

位置: `Compat/RrcTreasureKeyCompat.cs:103-109,240-263`.

- 主模组仅依赖 BaseLib. TryInstall 只在 initializer 调一次; ResolveTypes 第一次就置 _resolved=true. 若 RRC/A4H 后加载, 本次永久 dormant, 不会重试.
- standalone manifest 依赖 RRC/A4H, 因此顺序前提与主包不同. 两包共用源码不代表入口时机相同.
- InstallGate 是每程序集对象, 不是跨程序集锁. 真实 loader 现在顺序调用, Harmony registry 能防顺序重复; 注释里的跨线程原子保证并不成立. 不需要为了理论线程竞争新增复杂锁, 先如实声明线程前提.
- OnSkipped 在 RewardsSetSynchronizer 的同步跳过路径执行, 这才是对端执行的来源. RelicCmd.Obtain 本身不是网络广播. 其 AddRelicInternal 在首个 await 前发生, 所以也不能只凭 fire-and-forget 就断言一定双发.

建议主包用已知 post-mod-init 或精确依赖就绪事件重试; 只在成功解析后缓存成功, 缺依赖不是永久完成. 保留共享 patch identity 去重.

验收矩阵: 主包单装/独立包单装/双装, 两种加载顺序, 领取不发钥匙/跳过发一次, 已有钥匙/keys disabled, 普通战斗奖励不发, 双端个人宝箱一致. 不再以一条 active 日志代替奖励效果.

## P2 QCR-7: 与其他模块的组合边界

- MpConfigSync 当前到达晚于 Capture/Populate; 不能指望它自动修复所有遗物确定性问题.
- Qurious 的 PoolReplacement 删除所有非 ChaosRelicModel, 包括将来新 Anthony 遗物. 两个全量替换器双开可能把对方池清空. 先决定互斥还是兼容, 别让 patch order 做产品决策.
- 设置页 ConfigChanged 订阅会触发保存. MpConfigSync 调 Changed 可能把会话值落盘. 不落盘承诺需要 effective overlay 或明确压制保存路径, 不能只少调用 Save.
- 稳定 seed 不等于数值平衡. 重复常驻触发, 获得时负面退款, 多件保留/恢复/金币引擎要按回合/战斗/整局估值, 不把药水或卡牌一次性数值直接相乘当遗物价格.
- `NamePrefix/NameNoun` 与 op.Text 动态生成是中文. 有 eng JSON 不等于完整英文; 若要支持英语, 翻译在渲染阶段, 不把本地化文本混进生成身份/指纹.

## 推荐修复顺序

1. QCR-1 + QCR-5: 保护配置与旧存档, 防止新旧产品互相误认.
2. 和 MpConfigSync 一起定义开局/读档/重连 effective context, 完成 QCR-2.
3. QCR-3/4: 稳态直接查定义, 显示与行为同源.
4. QCR-6: 加载时机和钥匙场景证明.
5. 再做模板覆盖, 平衡, 本地化和版本发布.

不要把这些全交给一个只会机械改名的任务. 主持者保留状态模型和集成裁决; 每个实现切片必须写清消费者与验收场景.

## 工程与证据

- manifest 当前 version=0.5.1, DEVELOP 顶部称 v0.6.0 迁移, PT3 又说用户未决定升版. 在得到决定前不擅自升版, 但必须区分 id 迁移事实与版本标记.
- 本轮证据: `probe-results.json` 的 config-migration-cross-mod, registry-config-change; `build-results.json`; `binary-inputs.json`.
- 检查时本仓已有未推送提交; 本轮不把既有未验证产品变更混入建议提交.
- 未执行预算 UI/战斗/旧存档/双端真机. 已知旧日志有 MegaLabel 主题告警, 但与本轮源码时间不同, 不重复宣称当前仍复现. 后续 UI 必须用真实页面验证, 不用 static label key 全绿代替.

## 附录: 从状态身份与时间切面发现问题

通用流程见 [总建议附录](../astra-advice.md). 不要只问 "生成器是否确定", 先问 **这一件已经获得的遗物, 何时允许成为另一件遗物?**

- **分清三种真值**: 用户偏好, 本局有效配置, 已生成定义. 写出每种的作者/生效时点/持久化位置. 任意 getter 同时把三者混起来, 都要检查中途修改和读档是否变义.
- **同名不是同身份**: 迁移不能只因路径仍叫 AutoAnthonyRelics.cfg 就认领. 用 "旧产品已经搬走, 新产品在同路径创建文件" 推演第二次启动, 同时检查旧备份有没有被覆盖.
- **事件需要追到订阅者**: 调 Changed 之后谁保存, 谁清缓存, 谁重算? 把保存订阅者接上再观察, 不以当前文件无 Save 调用证明无落盘.
- **命中缓存之前也算成本**: 从伤害预览/角标等真实入口一路算到 ForSeed, 不只测 Dictionary 查找. 分开冷生成, 热查询, 配置变更; 调用次数按拥有多件遗物的实际场景增长.
- **中间态也是结果**: 正常退出后文件恢复, 不能证明会话中没写过主机值. 在应用后/恢复前的切面观察, 再考虑进程中断. 恢复的是原值, 不等于撤销了此前一次性扣血/获得效果.

最短反例链: 首次生成 -> 获得一件带一次性效果的遗物 -> 改下局偏好 -> 连续看描述/触发效果 -> 保存 -> 新进程恢复. 契约要求不变的身份/数值每一步都应相同. 再用一份属于新产品的同名 cfg 测迁移, 对照已有备份保持字节不变. 把完整生命周期冻结后, 才优化缓存实现.
