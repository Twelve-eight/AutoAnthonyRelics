using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using AutoAnthonyRelics.Chaos;
using MegaCrit.Sts2.addons.mega_text;

namespace AutoAnthonyRelics.Patches;

/// <summary>
/// Visual point-budget editor for all chaos relic templates (user order
/// 2026-09-11). One row per template showing:
///   [effect text]([vanilla relic 1],[vanilla relic 2],..)
///   - hovering a vanilla relic name shows its full description, rarity,
///     and its cost under OUR pricing (VanillaRelicMapping.OurPointsFor);
///   - Min/Max double-ended slider (left = Min, right = Max);
///   - per-point cost label.
///
/// Backed by config keys Min_<T> / Max_<T> / Cost_<T> / Refund_<T>
/// (persisted through BaseLib like every other key; MP Tier-1).
/// Uses engine NHoverTipSet.CreateAndShow for the hover popups and NSlider
/// (Godot.Range) for the sliders - both byte-verified against sts2.dll.
/// </summary>
internal sealed partial class BudgetEditorPanel : VBoxContainer
{
    private sealed class TemplateRow
    {
        internal required string Template;
        internal required NSlider MinSlider;
        internal required NSlider MaxSlider;
        internal required MegaLabel CostLabel;
    }

    private readonly List<TemplateRow> _rows = new();

    public override void _Ready()
    {
        try
        {
            AddThemeConstantOverride("separation", 14);
            Build();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[AutoAnthonyRelics] budget editor build failed: {e}");
        }
    }

    private void Build()
    {
        var title = new MegaLabel { Text = LocOf("AUTOANTHONYRELICS-BUDGET_TITLE") };
        title.AddThemeFontSizeOverride("font_size", 26);
        AddChild(title);

        var hint = new Label
        {
            Text = LocOf("AUTOANTHONYRELICS-BUDGET_HINT"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(760f, 0f),
        };
        hint.AddThemeFontSizeOverride("font_size", 14);
        AddChild(hint);

        // Core pool
        AddSection(LocOf("AUTOANTHONYRELICS-BUDGET_SECTION_CORE"));
        AddRows(ChaosRelicCatalog.PositiveTemplates);
        AddSection(LocOf("AUTOANTHONYRELICS-BUDGET_SECTION_NEGATIVE"));
        AddRows(ChaosRelicCatalog.NegativeTemplates);
        if (AutoAnthonyRelicsConfig.EnableExtraPool)
        {
            AddSection(LocOf("AUTOANTHONYRELICS-BUDGET_SECTION_EXTRA"));
            AddRows(ChaosRelicExtraCatalog.PositiveTemplates
                .Concat(ChaosRelicExtraCatalog.NegativeTemplates));
        }
    }

    private void AddSection(string header)
    {
        var label = new Label { Text = header };
        label.AddThemeFontSizeOverride("font_size", 20);
        AddChild(label);
    }

    private void AddRows(IEnumerable<string> templates)
    {
        foreach (var t in templates)
        {
            // SpecOf resolves across BOTH pools (core + extra) and applies
            // user Min/Max bounds; ChaosRelicCatalog.Spec alone throws for
            // extra-pool templates.
            AddChild(BuildRow(t, ChaosRelicGenerator.SpecOf(t)));
        }
    }

    /// <summary>One template row: effect text + vanilla refs + sliders + cost.</summary>
    private Control BuildRow(string template, ChaosRelicCatalog.TemplateSpec spec)
    {
        var row = new VBoxContainer
        {
            Name = template,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        row.AddThemeConstantOverride("separation", 2);

        // ---- Line 1: effect text with vanilla-relic refs inline ----
        var textLine = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var effect = new MegaLabel
        {
            Text = spec.Min == spec.Max && !spec.TextPattern.Contains("{N}")
                ? spec.Render(spec.Min)
                : spec.Render(spec.Max),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        textLine.AddChild(effect);

        // Vanilla relic chips: [effect]([relic1],[relic2],..)
        var refs = VanillaRelicMapping.For(template);
        if (refs.Count > 0)
        {
            var open = new MegaLabel { Text = " (" };
            textLine.AddChild(open);
            for (int i = 0; i < refs.Count; i++)
            {
                if (i > 0)
                {
                    textLine.AddChild(new MegaLabel { Text = "," });
                }
                textLine.AddChild(MakeRelicChip(refs[i]));
            }
            textLine.AddChild(new MegaLabel { Text = ")" });
        }
        row.AddChild(textLine);

        // ---- Line 2: Min/Max sliders + per-point cost ----
        var sliderLine = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var minLabel = new MegaLabel { Text = LocOf("AUTOANTHONYRELICS-BUDGET_MIN") };
        sliderLine.AddChild(minLabel);
        var minSlider = MakeSlider(spec.Min);
        sliderLine.AddChild(minSlider);
        var maxLabel = new MegaLabel { Text = LocOf("AUTOANTHONYRELICS-BUDGET_MAX") };
        sliderLine.AddChild(maxLabel);
        var maxSlider = MakeSlider(spec.Max);
        sliderLine.AddChild(maxSlider);
        var costLabel = new MegaLabel();
        sliderLine.AddChild(costLabel);
        row.AddChild(sliderLine);

        var rw = new TemplateRow
        {
            Template = template,
            MinSlider = minSlider,
            MaxSlider = maxSlider,
            CostLabel = costLabel,
        };
        _rows.Add(rw);

        // Slider ranges: engine allows any band; Min<=Max enforced on commit.
        int band = Math.Max(spec.Max, 8) * 2;
        minSlider.MinValue = Math.Max(0, spec.Min - 2);
        minSlider.MaxValue = band;
        maxSlider.MinValue = Math.Max(0, spec.Min - 2);
        maxSlider.MaxValue = band;
        minSlider.Step = 1;
        maxSlider.Step = 1;

        void Refresh()
        {
            int mn = (int)minSlider.Value, mx = (int)maxSlider.Value;
            if (mn > mx)
            {
                (mn, mx) = (mx, mn);
                (minSlider.Value, maxSlider.Value) = (mn, mx);
            }
            // Live config values (ChaosPointCosts resolves user-tuned costs);
            // negatives show refund per point.
            int per = spec.IsNegative
                ? AutoAnthonyRelicsConfig.PointCosts.RefundPerPoint(template)
                : AutoAnthonyRelicsConfig.PointCosts.CostPerPoint(template);
            costLabel.SetTextAutoSize(
                LocOf("AUTOANTHONYRELICS-BUDGET_PERPOINT").Replace("{P}", per.ToString()));
        }
        minSlider.ValueChanged += _ => { Refresh(); Persist(rw, spec); };
        maxSlider.ValueChanged += _ => { Refresh(); Persist(rw, spec); };
        Refresh();
        return row;
    }

    private void Persist(TemplateRow rw, ChaosRelicCatalog.TemplateSpec spec)
    {
        try
        {
            int mn = (int)rw.MinSlider.Value, mx = (int)rw.MaxSlider.Value;
            if (mn > mx) (mn, mx) = (mx, mn);
            AutoAnthonyRelicsConfig.SetTemplateBounds(rw.Template, mn, mx);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[AutoAnthonyRelics] persist bounds {rw.Template}: {e.Message}");
        }
    }

    /// <summary>Hoverable vanilla-relic name chip: hover = full desc + rarity + our cost.</summary>
    private static Control MakeRelicChip(VanillaRelicMapping.VanillaRef vref)
    {
        var chip = new MegaLabel
        {
            Text = vref.DisplayName,
            MouseFilter = MouseFilterEnum.Stop,
        };
        chip.AddThemeColorOverride("font_color", new Color(0.98f, 0.84f, 0.25f));
        chip.AddThemeColorOverride("font_hover_color", new Color(1f, 1f, 1f));

        var tip = BuildRelicTip(vref);
        chip.MouseEntered += () =>
        {
            try
            {
                NHoverTipSet.CreateAndShow(chip, tip)?
                    .SetGlobalPosition(chip.GlobalPosition + new Vector2(0f, 40f));
            }
            catch (Exception e)
            {
                MainFile.Logger.Error($"[AutoAnthonyRelics] hover tip: {e.Message}");
            }
        };
        chip.MouseExited += () =>
        {
            try { NHoverTipSet.Remove(chip); }
            catch { /* tip already gone */ }
        };
        return chip;
    }

    /// <summary>Hover content: description + rarity + our-points-for-same-effect.</summary>
    private static HoverTip BuildRelicTip(VanillaRelicMapping.VanillaRef vref)
    {
        string our = VanillaRelicMapping.OurPointsFor(vref) is int pts
            ? LocOf("AUTOANTHONYRELICS-BUDGET_OURCOST").Replace("{P}", pts.ToString())
            : LocOf("AUTOANTHONYRELICS-BUDGET_OURCOST_NA");
        string desc =
            $"[b]{vref.DisplayName}[/b]\n{vref.Description}\n" +
            $"[color=#c9a227]{LocOf("AUTOANTHONYRELICS-BUDGET_RARITY")}: {vref.Rarity}[/color]\n" +
            $"[color=#8fd48f]{our}[/color]\n" +
            $"[color=#9e9e9e]{vref.EffectNote}[/color]";
        return new HoverTip(new LocString("gameplay_ui", "AUTOANTHONYRELICS-BUDGET_TITLE"), desc);
    }

    private static NSlider MakeSlider(double initial)
    {
        var s = new NSlider
        {
            CustomMinimumSize = new Vector2(180f, 24f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Value = initial,
        };
        return s;
    }

    private static string LocOf(string key)
    {
        try
        {
            return new LocString("gameplay_ui", key).GetRawText();
        }
        catch
        {
            return key;
        }
    }
}
