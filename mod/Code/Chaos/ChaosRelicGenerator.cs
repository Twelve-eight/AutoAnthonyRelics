using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Relics;

namespace AutoAnthonyRelics.Chaos;

/// <summary>
/// Seeded chaos relic generator: the relic-side mirror of AutoAnthony's
/// RandomCardGenerator. Given a run seed, produces one definition per slot.
///
/// v0.5 budget system (user order 2026-09-11): relics are always active, so
/// entries are no longer free. Each relic gets a rarity-scaled point budget;
/// positives cost CostPerPoint x Amount, one negative refunds points which
/// can buy an extra positive - the MH-Rise qurious-crafting /
/// Black-Ring-red-quality feel. Generation is fully deterministic from the
/// seed + the SAME config cost table on both MP ends.
///
/// Legacy counts (1/3/5 free picks) replaced wholesale; the multiplier
/// config key stays for save compat but no longer drives counts.
/// </summary>
public static class ChaosRelicGenerator
{
    public const int SlotsPerRarity = 20;
    public const int TotalSlots = SlotsPerRarity * 3; // Common, Uncommon, Rare

    /// <summary>Cap on positives per relic (readability of tooltip + perf).</summary>
    public const int MaxPositives = 6;

    /// <summary>Chaos relic naming: adjective + noun, seeded pick.</summary>
    private static readonly string[] NamePrefix =
    {
        "混沌", "随机", "无序", "狂野", "古怪", "变化", "陌生", "编织", "扭曲", "重组",
        "怪诞", "任意", "失序", "拼凑", "偶发", "跳变", "偶得", "杂乱", "奇想", "拼贴",
    };

    private static readonly string[] NameNoun =
    {
        "齿轮", "棱镜", "沙漏", "怀表", "骰子", "罗盘", "天平", "墨水瓶", "蜡烛", "铃铛",
        "面具", "卷轴", "符文", "水晶", "瓶中船", "鸟笼", "怀炉", "烟斗", "放大镜", "钥匙",
        "棋子", "纽扣", "羽毛", "鳞片", "书签", "墨水", "香炉", "怀剑", "星盘", "念珠",
    };

    public static IReadOnlyList<ChaosRelicDefinition> Generate(string seed, int budgetCommon,
        int budgetUncommon, int budgetRare, ChaosPointCosts costs,
        int negativeChancePercentCommon, int negativeChancePercentUncommon, int negativeChancePercentRare)
    {
        Random random = new(StableSeed(seed));
        var definitions = new List<ChaosRelicDefinition>(TotalSlots);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var usedEffectSets = new HashSet<string>(StringComparer.Ordinal);

        // Budget floor contract (F08, explicit - not a silent clamp): a relic
        // always carries at least one positive entry, so a configured budget
        // below the price of the cheapest minimum-amount positive is raised to
        // that price for generation. The probe reproduced the degenerate case
        // this prevents: budget 1 with every positive priced 20 and negative
        // chance 0 produced 60 relics with zero entries.
        int floor = ChaosTemplates.CheapestPositiveFloor(costs);

        for (int slot = 0; slot < TotalSlots; slot++)
        {
            RelicRarity rarity = RarityForSlot(slot);
            int configured = BudgetFor(rarity, budgetCommon, budgetUncommon, budgetRare);
            int budget = Math.Max(configured, floor);
            int negativeChance = NegativeChanceFor(rarity,
                negativeChancePercentCommon, negativeChancePercentUncommon, negativeChancePercentRare);
            ChaosRelicDefinition definition = GenerateOne(random, slot, rarity, budget, negativeChance,
                costs, usedNames, usedEffectSets);
            definitions.Add(definition);
        }
        return definitions;
    }

    internal static int BudgetFor(RelicRarity rarity, int common, int uncommon, int rare) => rarity switch
    {
        RelicRarity.Common => Math.Max(1, common),
        RelicRarity.Uncommon => Math.Max(1, uncommon),
        RelicRarity.Rare => Math.Max(1, rare),
        _ => Math.Max(1, common),
    };

    internal static int NegativeChanceFor(RelicRarity rarity, int c, int u, int r) => rarity switch
    {
        RelicRarity.Common => ClampPercent(c),
        RelicRarity.Uncommon => ClampPercent(u),
        RelicRarity.Rare => ClampPercent(r),
        _ => ClampPercent(c),
    };

    private static int ClampPercent(int value) => Math.Clamp(value, 0, 100);

    /// <summary>
    /// Budget-driven generation for one slot. Structure: positives until the
    /// budget runs dry (or the cap), then a chance of exactly one negative
    /// whose refund may buy one more positive. Duplicate-effect-set avoidance
    /// mirrors the legacy 4-attempt recovery.
    /// </summary>
    internal static ChaosRelicDefinition GenerateOne(Random random, int slot, RelicRarity rarity,
        int budget, int negativeChancePercent, ChaosPointCosts costs,
        HashSet<string> usedNames, HashSet<string> usedEffectSets)
    {
        for (int attempt = 0; attempt < 4; attempt++)
        {
            bool finalAttempt = attempt == 3;
            var operations = AssembleOperations(random, rarity, budget, negativeChancePercent,
                costs, finalAttempt);
            string name = GenerateName(random, usedNames);
            string signature = string.Join("|", operations
                .Select(op => op.Template)
                .OrderBy(t => t, StringComparer.Ordinal));
            if (!usedEffectSets.Add(signature) && !finalAttempt)
            {
                continue; // duplicate effect set: retry
            }
            return new ChaosRelicDefinition(slot, rarity, name, operations);
        }
        // finalAttempt path always returns inside the loop.
        throw new InvalidOperationException("unreachable");
    }

    /// <summary>
    /// The budget algorithm. Deterministic given (random state, rarity,
    /// budgets, cost table). Order: spend on positives, then the negative
    /// roll, then refund-bought positives. Template picks are uniform over
    /// the affordable set; amounts are rolled inside the band and clamped
    /// down to what the remaining budget affords.
    /// </summary>
    internal static IReadOnlyList<ChaosRelicOperation> AssembleOperations(Random random,
        RelicRarity rarity, int budget, int negativeChancePercent, ChaosPointCosts costs,
        bool finalAttempt)
    {
        var operations = new List<ChaosRelicOperation>();
        var positivesTaken = new HashSet<string>(StringComparer.Ordinal);
        var negativesTaken = new HashSet<string>(StringComparer.Ordinal);

        // Phase 1: spend the initial budget on positives.
        SpendOnPositives(random, budget, rarity, costs, positivesTaken, operations, finalAttempt);

        // Phase 2: negative roll - at most one negative per relic.
        if (random.Next(100) < negativeChancePercent)
        {
            string? negative = PickNegative(random, costs, negativesTaken);
            if (negative is not null)
            {
                var spec = SpecOf(negative);
                int amount = RollAmount(random, spec);
                operations.Add(new ChaosRelicOperation(negative, amount, RenderOperation(spec, amount)));
                negativesTaken.Add(negative);

                // Phase 3: refund buys more positives.
                int refund = costs.RefundPerPoint(negative) * amount;
                SpendOnPositives(random, refund, rarity, costs, positivesTaken, operations, finalAttempt);
            }
        }

        // Guarantee: at least one positive entry, always. Normally phase 1
        // already produced one (the caller raises the budget to the floor), but
        // a pathological cost table can still leave the pool unaffordable, and
        // an entry-less relic is not a relic. The fallback picks the cheapest
        // minimum-amount positive, ordered by template id so the choice is
        // identical on both MP ends.
        if (!operations.Any(op => !ChaosTemplates.IsNegative(op.Template)))
        {
            string? cheapest = null;
            int cheapestPrice = int.MaxValue;
            foreach (var template in ChaosTemplates.PositiveTemplates)
            {
                if (UniqueOnly.Contains(template))
                {
                    continue;
                }
                var candidate = ChaosTemplates.Spec(template);
                int price = ChaosTemplates.PriceOf(candidate, costs, candidate.Min);
                bool cheaper = price < cheapestPrice
                    || (price == cheapestPrice && cheapest is not null
                        && string.CompareOrdinal(template, cheapest) < 0);
                if (cheaper)
                {
                    cheapest = template;
                    cheapestPrice = price;
                }
            }
            if (cheapest is not null)
            {
                var spec = ChaosTemplates.Spec(cheapest);
                operations.Insert(0, new ChaosRelicOperation(cheapest, spec.Min, RenderOperation(spec, spec.Min)));
            }
        }

        return operations;
    }

    private static void SpendOnPositives(Random random, int budget, RelicRarity rarity,
        ChaosPointCosts costs, HashSet<string> taken, List<ChaosRelicOperation> operations, bool finalAttempt)
    {
        int cheapestUnit = ChaosTemplates.CheapestPositiveUnit(costs);
        int spendable = budget;
        int positivesSoFar = operations.Count(op => !ChaosTemplates.IsNegative(op.Template));
        while (spendable >= cheapestUnit && taken.Count < ChaosTemplates.PositiveTemplates.Count
               && positivesSoFar < MaxPositives)
        {
            string? template = PickAffordablePositive(random, spendable, rarity, costs, taken);
            if (template is null)
            {
                break; // nothing affordable left
            }
            var spec = SpecOf(template);
            int amount = RollAmountWithinBudget(random, spec, spendable, costs);
            if (amount < spec.Min)
            {
                // Even the minimum does not fit - skip this template entirely.
                taken.Add(template);
                continue;
            }
            int cost = ChaosTemplates.PriceOf(spec, costs, amount);
            operations.Add(new ChaosRelicOperation(template, amount, RenderOperation(spec, amount)));
            taken.Add(template);
            positivesSoFar++;
            spendable -= cost;
        }
    }

    /// <summary>
    /// Text for one entry. Sloth is the one template whose rendered number is
    /// not its own amount: the relic caps cards played per turn at 7 - N, and
    /// the tooltip must show the cap, not N. The generator used to substitute
    /// it here only, so the budget editor (which calls TemplateSpec.Render
    /// directly) displayed the literal placeholder to the user.
    /// </summary>
    internal static string RenderOperation(ChaosRelicCatalog.TemplateSpec spec, int amount) =>
        spec.Template == ChaosRelicCatalog.NegStartSloth
            ? spec.TextPattern.Replace("{M}", Math.Max(1, 7 - amount).ToString())
            : spec.Render(amount);

    /// <summary>
    /// Positive templates that may appear at most once per relic: repeatable
    /// engines would stack degenerately (two "gain 1 energy each turn" entries
    /// on one relic is a strictly better relic, not a more interesting one).
    /// </summary>
    internal static readonly IReadOnlySet<string> UniqueOnly =
        new HashSet<string>(StringComparer.Ordinal)
        {
            ChaosRelicCatalog.TurnStartEnergy,
            ChaosRelicCatalog.PassiveMaxEnergy,
            ChaosRelicCatalog.TurnStartDraw,
        };

    /// <summary>
    /// Spec resolution across BOTH pools (core + extra) with the user Min/Max
    /// bounds overlay. Delegates to <see cref="ChaosTemplates"/> so the
    /// generator, the cost table and the budget editor cannot drift apart.
    /// </summary>
    internal static ChaosRelicCatalog.TemplateSpec SpecOf(string template) =>
        ChaosTemplates.Effective(template);

    /// <summary>
    /// Pick a positive template whose MINIMUM amount fits the remaining
    /// budget. Uniform over the affordable set - the RNG draw count is
    /// therefore a function of the affordable-set SIZE, which is deterministic
    /// for a given (seed, config) pair on both MP ends.
    ///
    /// uniqueOnly templates (one-per-turn energy/draw, max-energy) are removed
    /// from the candidate set here. They used to be excluded only by the
    /// `taken` set, which is populated AFTER the amount roll - so a template
    /// whose minimum did not fit was added to `taken` and retried, but a
    /// template could still be picked twice in the same relic if the first
    /// roll was skipped. Hard-excluding them keeps the "repeatable engines
    /// would stack degenerately" rule true.
    /// </summary>
    private static string? PickAffordablePositive(Random random, int spendable, RelicRarity rarity,
        ChaosPointCosts costs, HashSet<string> taken)
    {
        var affordable = new List<string>();
        foreach (var t in ChaosTemplates.PositiveTemplates)
        {
            if (taken.Contains(t) || UniqueOnly.Contains(t))
            {
                continue;
            }
            var s = SpecOf(t);
            if (ChaosTemplates.PriceOf(s, costs, s.Min) <= spendable)
            {
                affordable.Add(t);
            }
        }
        if (affordable.Count == 0)
        {
            return null;
        }
        return affordable[random.Next(affordable.Count)];
    }

    private static string? PickNegative(Random random, ChaosPointCosts costs, HashSet<string> taken)
    {
        var available = ChaosTemplates.NegativeTemplates
            .Where(t => !taken.Contains(t))
            .ToList();
        if (available.Count == 0)
        {
            return null;
        }
        return available[random.Next(available.Count)];
    }

    private static int RollAmount(Random random, ChaosRelicCatalog.TemplateSpec spec)
    {
        return spec.Min + random.Next(spec.Max - spec.Min + 1);
    }

    /// <summary>
    /// Roll in-band, then clamp down to what the remaining budget affords.
    /// Linear templates: max = spendable / perPoint. Decaying templates
    /// (triangular cost total(N) = perPoint * N*(N+1)/2): walk N downward
    /// until it fits.
    /// </summary>
    private static int RollAmountWithinBudget(Random random, ChaosRelicCatalog.TemplateSpec spec,
        int spendable, ChaosPointCosts costs)
    {
        int amount = RollAmount(random, spec);
        int perPoint = costs.CostPerPoint(spec.Template);
        int maxAffordable;
        if (spec.Decaying)
        {
            maxAffordable = 0;
            for (int n = spec.Max; n >= spec.Min; n--)
            {
                if (perPoint * n * (n + 1) / 2 <= spendable)
                {
                    maxAffordable = n;
                    break;
                }
            }
        }
        else
        {
            maxAffordable = spendable / perPoint;
        }
        return Math.Min(amount, Math.Max(0, maxAffordable));
    }

    internal static string GenerateName(Random random, HashSet<string> used)
    {
        for (int i = 0; i < 32; i++)
        {
            string name = NamePrefix[random.Next(NamePrefix.Length)] + NameNoun[random.Next(NameNoun.Length)];
            if (used.Add(name))
            {
                return name;
            }
        }
        // Fallback: numbered variant.
        string baseName = NamePrefix[random.Next(NamePrefix.Length)] + NameNoun[random.Next(NameNoun.Length)];
        int n = 2;
        while (!used.Add(baseName + n))
        {
            n++;
        }
        return baseName + n;
    }

    public static RelicRarity RarityForSlot(int slot)
    {
        int inRarity = slot % SlotsPerRarity;
        return inRarity switch
        {
            < 20 and >= 0 when slot < SlotsPerRarity => RelicRarity.Common,
            _ when slot < SlotsPerRarity * 2 => RelicRarity.Uncommon,
            _ => RelicRarity.Rare,
        };
    }

    /// <summary>AutoAnthony StableSeed mirror: stable string hash for a run seed.</summary>
    internal static int StableSeed(string seed)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in seed)
            {
                hash = hash * 31 + c;
            }
            return hash;
        }
    }
}
