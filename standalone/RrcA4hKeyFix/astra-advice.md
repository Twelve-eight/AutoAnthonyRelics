# Astra advice - RrcA4hKeyFix 独立包

日期: 2026-09-12. 本包与 Qurious 主包共用 `../../mod/Code/Compat/RrcTreasureKeyCompat.cs`, 不维护第二份复制源码.

本轮独立包构建 0 警告/0 错误; 未做实际宝箱/双端联机. 完整建议见 [Qurious 建议](../../astra-advice.md) 的 QCR-6.

## 必須保持的区别

- standalone manifest 依赖 RelicRewardChoices 和 Act4Heart, 主包只依赖 BaseLib. 因此主包的初始化顺序问题不能从 standalone 构建成功推定已解决.
- Harmony registry 是跨程序集的顺序去重证据; `static InstallGate` 是每个程序集一份, 不能声称跨程序集并发原子性.
- 只有宝箱来源奖励被跳过才补发 Sapphire Key. 普通战斗奖励/领取遗物/已有钥匙/keys disabled 不发.
- 同步来自 RewardsSetSynchronizer 的 OnSkipped 调用链, 不是 RelicCmd.Obtain 自带网络广播.
- RelicCmd.Obtain 在首个 await 前 AddRelicInternal, 所以单纯见到 fire-and-forget 也不能断言必定重复拿钥匙.

## 验收

主包单装, 独立包单装, 双装两种顺序; 精确检查 OnSkipped 上兼容 postfix 数量; 宝箱跳过一次, key 数量增加一次; 领取不给; 非宝箱不给; 开关关闭不给; 两端个人宝箱场景同步, 存读档不重复触发.

若 RRC 字段 TreasureLifetime/_sharedTreasurePoolOnly 变动, 记录 incompatible, 不把 null 字段当普通非宝箱而静默误宣称修好了. 不随意改 RRC/A4H 原工坊文件.

本轮建议不构成发布授权. 发布前 DLL/manifest/依赖版本/描述与实际行为一起核对.

## 附录: 相同源码不等于相同运行前提

通用方法见 [总建议附录](../../../astra-advice.md).

- 共享源码解决代码漂移, 不自动解决主包/独立包的加载顺序, 依赖, 静态字段实例和订阅次数. 分别列出入口前提, 再判断哪些保证能够共用.
- 去重需要共享的身份与原子边界. 名字相同的两个 static lock 不是同一个锁; 同时也别在当前顺序 loader 上臆造已发生的并发事故.
- 判断奖励是否网络一致, 沿 OnSkipped 的外层同步链追, 不因调用 RelicCmd 就结束. 判断是否重复, 找首次状态提交的时点, 不因看见异步就推定重复.
- 用不该发钥匙的场景检验作用范围: 普通战斗奖励, 已领取, 已有钥匙, keys disabled. 核心动作能发一次, 不代表排除条件正确.

最短复验: 主包/独立包/双包实际 patch 数量 -> 两种合法加载顺序 -> 宝箱跳过发一次 -> 非宝箱/领取不发 -> 双端状态一致. 每个结论只借用相同前提下的证据.
