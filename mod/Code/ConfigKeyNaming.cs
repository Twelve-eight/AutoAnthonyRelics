using System;
using System.Text;

namespace QuriousCraftingRelics;

/// <summary>
/// Config key / label naming for the point-budget system.
///
/// BaseLib builds a config label's loc key as
/// <c>{ModPrefix}{Slugify(propertyName)}.title</c>
/// (BaseLib-StS2 Config/ModConfig.cs GetLabelText), where Slugify is
/// <c>([A-Za-z0-9]|\G(?!^))([A-Z]) -> $1_$2</c>, then whitespace -> '_', then
/// everything outside <c>[A-Z0-9_]</c> stripped, then upper-cased. That
/// transform is LOSSY for an ALL_CAPS_SNAKE name: every underscore-free run of
/// capitals gets a '_' inserted before each capital, so
/// <c>Cost_C_START_DAMAGE_ALL</c> slugifies to
/// <c>COST_C_S_TA_R_T_D_AM_AG_E_A_L_L</c> - a key no loc table contains, and
/// BaseLib then falls back to rendering the raw property name. Measured with
/// the real .NET regex: 145 of 152 names were broken
/// (research/tools/slug-ground-truth.txt).
///
/// <see cref="TitleSnake"/> maps a template id (<c>C_START_DAMAGE_ALL</c>) to
/// the property-name form (<c>C_Start_Damage_All</c>) whose Slugify output is
/// exactly the existing loc key suffix (<c>COST_C_START_DAMAGE_ALL</c>). The
/// rename is a fixed point, so BOTH loc tables stay byte-identical - no loc
/// edit is needed for the property rename.
///
/// The SAME transform is applied to cfg-file keys by
/// <see cref="ConfigMigration"/>, so a pre-v0.6.0 cfg migrates losslessly.
/// </summary>
internal static class ConfigKeyNaming
{
    /// <summary>
    /// UPPER_SNAKE -> Title_Snake: split on '_', upper-case the first character
    /// of each segment and lower-case the rest. Empty segments are preserved so
    /// a malformed id round-trips instead of silently collapsing.
    /// </summary>
    internal static string TitleSnake(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return raw;
        }

        string[] segments = raw.Split('_');
        StringBuilder builder = new(raw.Length);
        for (int i = 0; i < segments.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('_');
            }
            string segment = segments[i];
            if (segment.Length == 0)
            {
                continue;
            }
            builder.Append(char.ToUpperInvariant(segment[0]));
            if (segment.Length > 1)
            {
                builder.Append(segment.Substring(1).ToLowerInvariant());
            }
        }
        return builder.ToString();
    }

    internal static string CostProperty(string template) => "Cost_" + TitleSnake(template);

    internal static string RefundProperty(string template) => "Refund_" + TitleSnake(template);

    internal static string MinProperty(string template) => "Min_" + TitleSnake(template);

    internal static string MaxProperty(string template) => "Max_" + TitleSnake(template);

    /// <summary>
    /// True for the four prefixes whose suffix is a template id (the only cfg
    /// keys the rename touches; every other key - budgets, chances, toggles -
    /// is a plain identifier and Slugify already leaves it alone).
    /// </summary>
    internal static bool IsTemplateScopedKey(string key) =>
        key.StartsWith("Cost_", StringComparison.Ordinal)
        || key.StartsWith("Refund_", StringComparison.Ordinal)
        || key.StartsWith("Min_", StringComparison.Ordinal)
        || key.StartsWith("Max_", StringComparison.Ordinal);

    /// <summary>
    /// Rename a legacy cfg key to its Title_Snake form. Idempotent: the
    /// already-migrated form is a fixed point of <see cref="TitleSnake"/>.
    /// </summary>
    internal static string MigrateKey(string key)
    {
        if (!IsTemplateScopedKey(key))
        {
            return key;
        }
        int underscore = key.IndexOf('_');
        return key.Substring(0, underscore + 1) + TitleSnake(key.Substring(underscore + 1));
    }
}
