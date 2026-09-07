using System;
using System.Collections.Generic;

namespace AutoAnthonyRelics.Chaos;

/// <summary>
/// Effect catalog: every entry template binds to exactly one relic hook and knows
/// how to render its Chinese text and its balance band (min/max amount).
/// v1: 16 templates across 6 hooks (design DEVELOP.md "Effect catalog").
/// </summary>
public static class ChaosRelicCatalog
{
    public const string StartDamageAll = "C_START_DAMAGE_ALL";
    public const string StartBlock = "C_START_BLOCK";
    public const string StartStrength = "C_START_STRENGTH";
    public const string StartDexterity = "C_START_DEXTERITY";
    public const string StartDraw = "C_START_DRAW";
    public const string StartEnergy = "C_START_ENERGY";
    public const string StartVulnAll = "C_START_VULN_ALL";
    public const string StartWeakAll = "C_START_WEAK_ALL";
    public const string TurnStartBlock = "T_START_BLOCK";
    public const string TurnStartEnergy = "T_START_ENERGY";
    public const string TurnStartHeal = "T_START_HEAL";
    public const string PlayDamageRandom = "PLAY_DAMAGE_RANDOM";
    public const string PlayBlock = "PLAY_BLOCK";
    public const string PassiveAttackDamage = "PASSIVE_ATTACK_DAMAGE";
    public const string PassiveMaxEnergy = "PASSIVE_MAX_ENERGY";
    public const string VictoryHeal = "VICTORY_HEAL";

    public sealed record TemplateSpec(string Template, int Min, int Max, string TextPattern)
    {
        public string Render(int amount) => TextPattern.Replace("{N}", amount.ToString());
    }

    private static readonly Dictionary<string, TemplateSpec> Specs = new(StringComparer.Ordinal)
    {
        // Combat-start one-shot effects: generous bands.
        [StartDamageAll] = new(StartDamageAll, 3, 8, "战斗开始时,对所有敌人造成{N}点伤害."),
        [StartBlock] = new(StartBlock, 4, 10, "战斗开始时,获得{N}点格挡."),
        [StartStrength] = new(StartStrength, 1, 3, "战斗开始时,获得{N}点力量."),
        [StartDexterity] = new(StartDexterity, 1, 3, "战斗开始时,获得{N}点敏捷."),
        [StartDraw] = new(StartDraw, 1, 3, "战斗开始时,抽{N}张牌."),
        [StartEnergy] = new(StartEnergy, 1, 3, "战斗开始时,获得{N}点能量."),
        [StartVulnAll] = new(StartVulnAll, 1, 3, "战斗开始时,对所有敌人施加{N}层易伤."),
        [StartWeakAll] = new(StartWeakAll, 1, 3, "战斗开始时,对所有敌人施加{N}层虚弱."),
        // Per-turn effects: small bands (they repeat every turn).
        [TurnStartBlock] = new(TurnStartBlock, 2, 5, "每回合开始时,获得{N}点格挡."),
        [TurnStartEnergy] = new(TurnStartEnergy, 1, 1, "每回合开始时,获得{N}点能量."),
        [TurnStartHeal] = new(TurnStartHeal, 1, 3, "每回合开始时,回复{N}点生命."),
        // Card-play triggers: small bands.
        [PlayDamageRandom] = new(PlayDamageRandom, 1, 4, "每当你打出一张牌,对随机一名敌人造成{N}点伤害."),
        [PlayBlock] = new(PlayBlock, 1, 3, "每当你打出一张牌,获得{N}点格挡."),
        // Passives: fixed-ish.
        [PassiveAttackDamage] = new(PassiveAttackDamage, 1, 4, "你的攻击牌伤害+{N}."),
        [PassiveMaxEnergy] = new(PassiveMaxEnergy, 1, 1, "每回合能量上限+{N}."),
        [VictoryHeal] = new(VictoryHeal, 2, 8, "战斗胜利后,回复{N}点生命."),
    };

    public static IReadOnlyList<string> AllTemplates { get; } = Specs.Keys.ToArray();

    public static TemplateSpec Spec(string template) =>
        Specs.TryGetValue(template, out var spec) ? spec
            : throw new InvalidOperationException($"Unknown chaos relic template {template}.");

    /// <summary>
    /// Card-baseline component-count weights (AutoAnthony PickComponentCount,
    /// ComponentCountRarityWeight). Rank 0-4 maps to card entry count 1-5.
    /// Relic entry count = 3 x (1 + weighted rank), clamped 3..15.
    /// </summary>
    public static readonly int[] CardBaselineCommon = { 110, 100, 30, 8, 2 };
    public static readonly int[] CardBaselineUncommon = { 90, 105, 85, 25, 7 };
    public static readonly int[] CardBaselineRare = { 70, 95, 120, 45, 14 };

    public const int EntryMultiplierDefault = 3;
    public const int MinEntries = 3;
    public const int MaxEntries = 15;
}
