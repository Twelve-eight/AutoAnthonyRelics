using System;
using System.Reflection;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Localization;

namespace QuriousCraftingRelics.Patches;

internal static class QuriousSettingsStyle
{
    internal static readonly Color Background = new(0.085f, 0.09f, 0.10f);
    internal static readonly Color Surface = new(0.13f, 0.14f, 0.15f);
    internal static readonly Color Text = new(0.92f, 0.90f, 0.84f);
    internal static readonly Color Muted = new(0.67f, 0.68f, 0.66f);
    internal static readonly Color Gold = new(0.78f, 0.65f, 0.39f);
    internal static readonly Color Error = new(1f, 0.48f, 0.42f);

    internal static string Loc(string key) =>
        LocString.GetIfExists("settings_ui", "QURIOUSCRAFTINGRELICS-" + key + ".title")?.GetFormattedText() ?? key;

    internal static StyleBoxFlat Box(Color color, bool border = false)
    {
        var box = new StyleBoxFlat { BgColor = color, BorderColor = Gold,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 8, ContentMarginBottom = 8 };
        box.SetCornerRadiusAll(6);
        box.SetBorderWidthAll(border ? 2 : 0);
        return box;
    }

    internal static Theme CreateTheme()
    {
        var theme = new Theme { DefaultFontSize = 18 };
        foreach (string type in new[] { "Label", "Button", "CheckButton", "LineEdit", "OptionButton" })
        {
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_hover_color", type, Text);
            theme.SetColor("font_focus_color", type, Gold);
            theme.SetStylebox("focus", type, Box(new Color(0, 0, 0, 0), true));
        }
        foreach (string type in new[] { "Button", "LineEdit", "OptionButton" })
        {
            theme.SetStylebox("normal", type, Box(Surface));
            theme.SetStylebox("hover", type, Box(new Color(0.20f, 0.20f, 0.19f)));
            theme.SetStylebox("pressed", type, Box(new Color(0.24f, 0.22f, 0.17f), true));
        }
        return theme;
    }

    internal static Label Label(string text, int size = 18) 
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    internal static SpinBox Number(PropertyInfo property)
    {
        // Match BaseLib NConfigSlider.Initialize for properties without metadata.
        // Allow existing out-of-band config values to display without writing them.
        var limits = property.GetCustomAttribute<ConfigSliderAttribute>();
        return new SpinBox { MinValue = limits?.Min ?? 0, MaxValue = limits?.Max ?? 100, Step = limits?.Step ?? 1,
            AllowGreater = true, AllowLesser = true, CustomMinimumSize = new Vector2(140, 36) };
    }

    internal static int EditedNumber(SpinBox input, double value)
    {
        int result = checked((int)Math.Clamp(value, input.MinValue, input.MaxValue));
        input.SetValueNoSignal(result);
        return result;
    }
}