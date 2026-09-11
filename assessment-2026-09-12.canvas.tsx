import { Stack, Row, Grid, H1, H2, H3, Text, Table, Code, Divider, Callout, useHostTheme } from "cursor/canvas";

const findings = [
  {
    id: "F01", area: "额外池", level: "发布阻断", evidence: "程序集复现", title: "打开 EnableExtraPool 即生成失败",
    fact: "当前程序集抛 InvalidOperationException: Unknown chaos relic template X_HAND_RETAIN. SpecOf 已支持两个目录,但 ChaosPointCosts.CostPerPoint / RefundPerPoint 仍只调用核心 ChaosRelicCatalog.Spec.",
    impact: "不是映射不足或数值待调,而是整个可选池无法走通.核心池默认关闭此功能,因此核心生成冒烟不会发现它.",
    source: "mod/Code/Chaos/ChaosRelicCatalog.cs:181-198; ChaosRelicGenerator.cs:196-243",
    next: "统一模板解析和计价入口,同时覆盖正面和负面额外模板.先验证生成闭环,再验证战斗效果."
  },
  {
    id: "F02", area: "设置与持久化", level: "发布阻断", evidence: "程序集复现 + 依赖反编译", title: "94 个范围键全部失联,编辑器不能按承诺工作",
    fact: "模板值 C_START_STRENGTH 被拼成 Min_C_START_STRENGTH,实际属性却叫 Min_StartStrength.94 个预期键匹配数为 0.调用 SetTemplateBounds(...,2,2) 后范围仍为 1..10;直接改属性也不影响 SpecOf.",
    impact: "拖动控件不改变生成范围.另有 UI 合约断点: new NSlider 没有引擎 _Ready 所需的 %Handle;LocOf/TextOf 查 gameplay_ui 无后缀键,资源在 settings_ui 且有 .title;默认 BaseLib UI 会暴露未加 ConfigHideInUI 的 94 个属性.单页外观未实机验证.",
    source: "mod/Code/AutoAnthonyRelicsConfig.cs:184-277,309-351; Patches/BudgetEditorPanel.cs:192-270; Patches/RelicsSettingsSubmenu.cs:97-109",
    next: "复用注册的配置实例,补齐提交/保存/重开读取流程,统一 ID 与属性约定,然后实现真正的一条线段双端手柄.不能只换滑条外观."
  },
  {
    id: "F03", area: "核心战斗", level: "发布阻断", evidence: "隔离引擎/模型调用 + 调用链", title: "实际效果与词条文本不一致",
    fact: "N_TURN_LOSE_HP 只用 Unpowered,没有 Unblockable.引擎分段调用复现:5 格挡承受 3 后,损失生命为 0.同一 hook 还先给回合格挡再结算此负面.PassiveAttackDamage 在无卡源 Unpowered 伤害查询仍返回 +3,并非只加攻击牌.",
    impact: "负面可能被正面直接抵消,攻击伤害价格却买到更广泛的伤害增益.引擎 AfterCardPlayed 广播给所有监听者,本 mod 的出牌伤害/格挡循环没有校验出牌者,队友出牌也会触发.联机整局未实测.",
    source: "mod/Code/Models/ChaosRelicModel.cs:267-291,302-350; 引擎 Creature.DamageBlockInternal / Hook.AfterCardPlayed",
    next: "先统一失去生命,攻击伤害,拥有者,首回合能量的语义与触发时间,再进行成本比较."
  },
  {
    id: "F04", area: "种子与联机", level: "高风险", evidence: "缓存复现 + 时序推断", title: "同种子缓存不包含配置,同步发生在首次生成之后",
    fact: "同种子改预算后 ForSeed 返回旧定义,直接 Generate 返回新定义.新多人局 prefix 中 Capture -> OnSeedCaptured -> ForSeed 已生成并缓存.另一个项目 MpConfigSync 在原方法内部 InitializeShared 之后才广播配置.",
    impact: "[INFERENCE] 主客机初始配置不同,即使随后同步属性,缓存也不会重建,可能保留不同遗物.存档只有槽位,没有本局定义快照;重启后按新配置/算法重建,同一遗物可能变义.不能把同种子可重复等同于联机安全.",
    source: "mod/Code/Chaos/ChaosRelicRunRegistry.cs:20-43; Patches/RunSeedTrackPatch.cs:29-32,50-59; Patches/ChaosRelicLocUpdater.cs:35-40; sts2-mpconfigsync/mod/MpConfigSyncCode/RunManagerInitializeSharedPatch.cs:28-57",
    next: "先定义本局何时冻结配置,存档如何恢复,联机何时准许生成.不应简单在设置变化时清缓存,以免在役遗物突然改义."
  },
  {
    id: "F05", area: "原版计价参考", level: "发布阻断", evidence: "当前引擎反编译 + 程序集复现", title: "参考值错误且非实时,不能支撑全面重定价",
    fact: "当前 sts2.dll: DaughterOfTheWind 为 Event,每攻击牌 1 格挡;TuningFork 为每 10 张技能牌 7 格挡;RingOfTheSnake 为 Starter.映射分别写成罕见/3格挡,3技能/4格挡,普通.力量单价改为19后,OurPointsFor(Vajra)仍返回5.",
    impact: "Lantern 的首回合一次性能量被挂到持续能量上限;按每张牌触发与每多张牌触发直接按 N 相乘,不可称为同效果价格.原始参考数据与断言缺少版本锚定.",
    source: "mod/Code/Chaos/VanillaRelicMapping.cs:59-104,121-148; 本轮 DaughterOfTheWind.txt, TuningFork.txt, RingOfTheSnake.txt",
    next: "重建已知版本的事实基准,区分完全对应,条件对应,仅类比;统一实时与三角计价.不要先在错误表上扩充负面映射."
  },
  {
    id: "F06", area: "双份钥匙修复", level: "发布阻断", evidence: "互斥锁复现 + 历史日志", title: "所谓只安装一次的进程锁没有保持存活",
    fact: "TryInstall 内 using Mutex 在退出时释放并销毁.同名锁顺序创建复现:两次 createdNew 均为 true.历史 godot.log 第618和625行分别记录主包和独立包均 OnSkipped patched.",
    impact: "双包共存的单次安装保证已经被反证.日志仅证明重复安装,没有证明实际发放两把钥匙.异步发放和已有钥匙检查之间的时序仍需真实宝箱场景确认.",
    source: "mod/Code/Compat/RrcTreasureKeyCompat.cs:95-136,190-203; 历史 godot.log:615-628",
    next: "修复双程序集共享安装所有权,验证单装/双装,跳过/领取,已有钥匙,开关关闭,多人宝箱."
  },
  {
    id: "F07", area: "额外池战斗状态", level: "高风险", evidence: "模型隔离复现 + 引擎时序", title: "保留加攻在伤害查询时消耗,结束战斗未清空",
    fact: "同一攻击牌连续两次 ModifyDamageAdditive 查询得到7和3,没有出牌也消费了额外4点.设置4点后调用 AfterCombatEnd,仍残留4点.附魔发生在 BeforeCombatStart,早于常规首手抽牌;Nimble/Imbued 未按 CanEnchant 筛选.",
    impact: "[INFERENCE] 伤害预览可吃掉增益,多段攻击行为与文案不一致,残留可跨战斗.缺少首手牌时附魔无目标.这些执行路径目前还被 F01 遮住,不能只修生成异常就宣布额外池可用.",
    source: "mod/Code/Models/ChaosRelicModel.cs:487-512,598-624; 引擎 CombatManager.cs:594,895-924",
    next: "查询函数保持纯读,明确增益获得/使用/清零事件;把附魔安排到正确时点并筛选合法目标."
  },
  {
    id: "F08", area: "余额与可配置边界", level: "需明确契约", evidence: "程序集复现", title: "合法配置能生成60件无词条遗物",
    fact: "预算1,各正面单价20,负面概率0,均在已提供的滑条范围内.生成结果60件全部无词条.默认配置的45种子不变量检查通过,但不是全配置可玩性证明.",
    impact: "预算模型防超支,没有保证有意义的结果.正负效果抵消,负面返还大小,重复引擎叠加均未由当前检查约束.本轮不自行裁定新价格或添加限制.",
    source: "mod/Code/Chaos/ChaosRelicGenerator.cs:119-187; mod/Code/AutoAnthonyRelicsConfig.cs:44-63,77-179",
    next: "选择并说明不合法组合的处理规则.平衡评估区分每战,每回合,每牌,全局,一次性代价,而非直接用药水稀有度换点数."
  },
  {
    id: "F09", area: "交付与恢复", level: "发布门禁", evidence: "仓库与文件快照", title: "文档和版本标识没有形成单一事实源",
    fact: "基线HEAD为0d4304b,非交接头部的56ff101;Session40/41已经补记.Pending第6项已过期.当前核心正面26项,负面10项,额外10正1负.清单仍写27正面和人工制品5,manifest仍0.5.0,英文工坊文案仍1/3/5.",
    impact: "设置文案仍残留护体/怠惰/锋锐/轻盈/灌注.历史budget-smoke,locdump,pck-extract证据路径当前缺失.项目备份hook缺失.三处已部署文件互相一致,但本轮新构建DLL哈希不同,未部署;不能据此证明代码语义漂移.",
    source: "DEVLOG.md:334-346; DEVELOP.md; workshop/TEMPLATE-POINTS-LIST.md; mod/AutoAnthonyRelics.json; mod/AutoAnthonyRelics/localization/zhs/settings_ui.json",
    next: "修复后统一设计,清单,术语,版本,发布说明和可恢复证据.工坊主条目3798163198,独立条目无fileid;发布前仍需确认description保护策略."
  }
];

const phases = [
  ["先冻结事实", "保留本局行为与版本基线,记录有效配置和引擎身份;不先调整价格.", "旧存档,同步,开关行为有明确约定"],
  ["修复配置闭环", "F01/F02.统一模板解析,范围键,注册配置实例,保存/重开,本地化和双端滑条.", "改值影响新局,重开读取一致,额外池可生成"],
  ["修复效果与兼容", "F03/F04/F06/F07.逐条对应引擎语义,拥有者和时点;处理缓存与安装去重.", "单人战斗,双端联机,宝箱矩阵均有实际证据"],
  ["建立可信价格基准", "F05/F08.原版版本化事实表,比较触发频次和条件;完整复审所有词条.", "显示点数等于生成扣费,引用可追溯"],
  ["发布收口", "F09.同步文档/版本,留存证据,更新三处工件;用户裁定工坊description策略.", "再验证UI,存档,联机;随后commit/push与发布"]
];

export default function AutoAnthonyRelicsAssessment() {
  const theme = useHostTheme();
  return <Stack gap={22} style={{ padding: 24, maxWidth: 1200, margin: "0 auto", color: theme.text.primary, background: theme.bg.editor }}>
    <Stack gap={8}>
      <Text tone="secondary" size="small">主会话单线评估 | 2026-09-12 | 基线 0d4304b | 未修改产品代码,未启动游戏,未部署,未发布</Text>
      <H1>AutoAnthonyRelics: 核心可运行,当前新增功能未达发布状态</H1>
      <Text>保留现有架构,不推倒重写.优先修复配置和效果契约,再重做数值与编辑体验.交接的“已构建”不能替代“已实现”.</Text>
    </Stack>
    <Callout tone="warning" title="决策" icon={null}>暂停发布,暂停以现有映射表为依据的全面重定价.双端滑条只是可见偏差,不是当前主要风险.</Callout>
    <Grid columns="repeat(auto-fit, minmax(170px, 1fr))" gap={18}>
      <Stack gap={4}><H2>2个构建通过</H2><Text size="small" tone="secondary">主包及独立包,0警告0错误;主包PCK已打包.禁用部署.</Text></Stack>
      <Stack gap={4}><H2>2700件</H2><Text size="small" tone="secondary">核心池45个固定种子,生成不变量0失败.不含战斗或UI.</Text></Stack>
      <Stack gap={4}><H2>0 / 94</H2><Text size="small" tone="secondary">范围配置预期键命中.2..2提交仍读到1..10.</Text></Stack>
      <Stack gap={4}><H2>47种模板</H2><Text size="small" tone="secondary">核心26正10负,额外10正1负;额外池生成异常.</Text></Stack>
    </Grid>
    <Divider />
    <H2>架构与成熟度</H2>
    <Text>配置 -> 目录/计价 -> 种子生成60个定义 -> 有界缓存 -> 槽位模型与描述 -> 奖励池替换 -> 引擎hooks.设置页和原版参考表是横向能力.钥匙补丁使用共享源码编入主包及独立包.</Text>
    <Table headers={["子系统", "结论", "证据边界"]} rows={[
      ["核心池生成", "可保留", "本轮真实程序集45种子;确定性,预算,数量,稀有度,名字和范围检查通过"],
      ["奖励池/图标/描述入口", "有历史实机基础", "DEVLOG Session36/37记录奖励,宝箱,商店与替换;本轮未重玩"],
      ["预算编辑器/可选效果池", "未完成闭环", "F01/F02/F07;编译通过不代表可操作"],
      ["联机/存档稳定性", "不能承诺", "同配置局部确定性成立;跨会话配置冻结与同步时序不成立或未验证"],
      ["独立钥匙修复", "去重保证失效", "源码,同名Mutex实验,历史日志双安装相互吻合"],
      ["维护成本", "可控但契约重复", "配置/目录/说明多处重复,反射键无类型校验;All按hook重复分配数组.未做性能剖析"]
    ]} />
    <Text tone="tertiary" size="small">来源: 当前源码及本轮构建/探针.历史实机记录不是当前版本的重新验收.</Text>
    <H2>发现与证据</H2>
    <Stack gap={0}>{findings.map((f) => <details key={f.id} open={f.id === "F01" || f.id === "F02"} style={{ borderTop: `1px solid ${theme.stroke.tertiary}`, padding: "14px 0" }}>
      <summary style={{ cursor: "pointer", fontWeight: 600, color: theme.text.primary }}>{f.id} | {f.level} | {f.title}</summary>
      <Stack gap={8} style={{ marginTop: 12, paddingLeft: 16 }}>
        <Text size="small" tone="secondary">{f.area} | {f.evidence}</Text>
        <Text>{f.fact}</Text>
        <Text tone="secondary">影响: {f.impact}</Text>
        <Text tone="secondary">处理: {f.next}</Text>
        <Text size="small" tone="tertiary">来源: {f.source}</Text>
      </Stack>
    </details>)}</Stack>
    <H2>核心池抽样分布</H2>
    <Table headers={["稀有度", "样本(件)", "正词条均值(条/件)", "负面出现率(%)", "正面花费均值(点/件)"]} columnAlign={["left", "right", "right", "right", "right"]} rows={[
      ["Common", 900, "2.906", "37.556", "14.456"],
      ["Uncommon", 900, "3.843", "57.444", "22.713"],
      ["Rare", 900, "4.836", "76.333", "31.869"]
    ]} />
    <Text size="small" tone="tertiary">来源: 本轮探针 AAR-ASSESS-0..44,默认配置,额外池关闭.均值四舍五入至三位小数.花费含负面返还购买部分,高于10/16/24不是超支.0失败仅指探针中实际执行的不变量,不是平衡或战斗正确性结论.</Text>
    <H2>后续顺序与验收门槛</H2>
    <Table headers={["顺序", "行动", "完成条件"]} rows={phases} />
    <H2>本轮行为复现摘要</H2>
    <Table headers={["检查", "实际观测", "解释"]} rows={[
      ["范围写入", "0/94键匹配;2..2写入后仍1..10", "直接调用产品方法,非界面测试"],
      ["可选池", "Unknown chaos relic template X_HAND_RETAIN", "调用当前程序集Generate"],
      ["实时引用价", "live=19,reference=5", "参考表用catalog默认值"],
      ["同种子改预算", "cacheUnchanged=true,freshGeneratorDifferent=true", "配置不在缓存键内"],
      ["可负担下界", "60件空词条遗物", "合法滑条组合,负面概率0"],
      ["互斥锁生命周期", "firstCreated=true,secondCreated=true", "复刻实际锁生命周期;历史日志验证双安装"],
      ["非攻击伤害加成", "+3", "无卡源Unpowered仍获攻击牌加成"],
      ["失去生命标志", "3被格挡,hpLost=0", "引擎分段调用,不是完整战斗"],
      ["伤害查询", "7,3;remaining=0", "两次查询消费4点保留增益"],
      ["战斗结束", "remaining=4", "增益清理缺失"],
      ["角标", "show=true,shown=0,entries=2", "没有覆盖DisplayAmount"]
    ]} />
    <H2>环境与交付边界</H2>
    <Stack gap={8}>
      <Text>游戏release_info为v0.111.0,commit41cef1ea.编译BaseLib3.4.5,历史加载日志为3.4.6.本轮没有安装依赖,没有写入C盘,没有触碰用户存档或工坊脚本.</Text>
      <Text>mod,mods_disabled,workshop/content三处已有DLL/PCK/manifest各自互相一致.禁用部署的新构建PCK与三处相同,DLL不同.没有声称新构建已交付或旧DLL语义不同.</Text>
      <Text>历史godot.log文件mtime为2026-09-10T21:41:20.491587Z,只作既有证据.本轮未启动游戏,所以编辑器视觉,实际宝箱发钥匙和多人整局未验证.</Text>
      <Text>可重复探针: G:/omp works/.tmp/aar-assessment-20260912/Probe.csproj.原始输出: 同目录probe-output.txt.完整恢复结论同步至DEVLOG Session42;临时目录不保证长期留存.</Text>
      <Text>此为G盘保存的Canvas源码.当前会话没有对应的G盘Canvas宿主目录,没有把它伪称为已在IDE呈现的交互页面.现有DEVLOG可直接阅读同一结论.</Text>
      <Text size="small" tone="tertiary">评估建议不是自动实施授权.本轮只增补评估记录,不更改产品行为.先修复并验收,再讨论价格与发布.</Text>
    </Stack>
  </Stack>;
}
