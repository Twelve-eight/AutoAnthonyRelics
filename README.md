# Qurious Crafting - Relics & RrcA4hKeyFix

本仓库包含两个《杀戮尖塔 2》模组：

1. **Qurious Crafting - Relics（怪异炼化 - 遗物）** — 主目录 `mod/`
2. **RrcA4hKeyFix（三选一蓝钥匙修复）** — 独立包 `standalone/RrcA4hKeyFix/`

> 注：早期旧版 `sts2-autoanthony-relics` 仓库已于 2026-09-22 停更并归档，当前活跃维护均在本仓库进行。

---

## 1. Qurious Crafting - Relics（怪异炼化 - 遗物）

每局依据本局种子确定性地生成 60 件混沌遗物（20 普通 / 20 罕见 / 20 稀有）替换随机遗物掉落；采用点数预算制，正面词条消耗点数，负面词条返还点数以换取更强组合。

- **Current Version**: `0.5.10`（rebuilt & verified against Slay the Spire 2 **`v0.114.0`**, `min_game_version: 0.111.0`）
- **Steam Workshop**: [3798163198](https://steamcommunity.com/sharedfiles/filedetails/?id=3798163198)
- **Dependency**: [BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127) `>= 3.4.5`（不依赖 Auto-Anthonyology，可自由同装）
- **联机提示**：多人局两端配置须保持一致，推荐配合 [MpConfigSync](https://github.com/Twelve-eight/sts2-mpconfigsync) 自动同步主机配置。

## 2. RrcA4hKeyFix（三选一蓝钥匙修复）

独立轻量修复包：解决同时安装 **Relic Reward Choices（遗物三选一）** 与 **Act 4 Heart** 时，跳过宝箱无法获得蓝钥匙的问题；与 Qurious Crafting - Relics 同装时自动按独立包优先去重，绝不双发钥匙。

- **Current Version**: `0.1.3`（rebuilt & verified against Slay the Spire 2 **`v0.114.0`**, `min_game_version: 0.111.0`）
- **Steam Workshop**: [3801352124](https://steamcommunity.com/sharedfiles/filedetails/?id=3801352124)
- **Dependencies**: [BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127) `>= 3.4.5`、[Relic Reward Choices](https://steamcommunity.com/sharedfiles/filedetails/?id=3795496596) `>= 0.3.4`、[Act 4 Heart](https://steamcommunity.com/sharedfiles/filedetails/?id=3747537811) `>= 1.1.7`（缺任一前置时自动休眠）

---

## 安装 / Installation

- **创意工坊**：按上方链接分别订阅所需模组与前置。
- **手动安装（GitHub Releases）**：在 [Releases](https://github.com/Twelve-eight/AutoAnthonyRelics/releases) 中分别下载 `QuriousCraftingRelics-*.zip` 或 `RrcA4hKeyFix-*.zip`，解压至 `<Slay the Spire 2>/mods/`（勿与工坊版同时启用）。

## 从源码构建 / Building from source

```bash
# QuriousCraftingRelics
dotnet build mod/QuriousCraftingRelics.csproj -c Release \
  -p:Sts2Path="<path-to-Slay-the-Spire-2>" \
  -p:CopyToModsFolderOnBuild=false

# RrcA4hKeyFix (standalone)
dotnet build standalone/RrcA4hKeyFix/RrcA4hKeyFix.csproj -c Release \
  -p:Sts2Path="<path-to-Slay-the-Spire-2>" \
  -p:CopyToModsFolderOnBuild=false
```

详细设计与验证记录见 [DEVELOP.md](DEVELOP.md) 与 [DEVLOG.md](DEVLOG.md)。
