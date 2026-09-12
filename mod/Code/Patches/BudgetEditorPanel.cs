using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using BaseLib.Config;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using QuriousCraftingRelics.Chaos;
using MegaCrit.Sts2.addons.mega_text;

namespace QuriousCraftingRelics.Patches;

/// <summary>
/// Visual point-budget editor for all chaos relic templates (user order
/// 2026-09-11). One row per template showing:
///   [effect text]([vanilla relic 1],[vanilla relic 2],..)
///   - hovering a vanilla relic name shows its full description, rarity,
///     and its cost under OUR pricing (VanillaRelicMapping.OurPointsFor);
///   - one double-ended range slider (single track, two handles) for the
///     amount band;
///   - per-point cost label.
///
/// Backed by config keys Min_&lt;TEMPLATE&gt; / Max_&lt;TEMPLATE&gt; /
/// Cost_&lt;TEMPLATE&gt; / Refund_&lt;TEMPLATE&gt; (persisted through BaseLib
/// like every other key; MP Tier-1).
///
/// The panel owns no config instance: it takes the one registered in
/// <see cref="ModConfigRegistry"/> and a save callback, so edits land on the
/// live config and are persisted through the same debounce path BaseLib's own
/// settings page uses. Building a fresh <c>new QuriousCraftingRelicsConfig()</c>
/// here (as the first version did) mutated a throwaway object: BaseLib writes
/// the registered instance to disk, so nothing the user did in the editor
/// survived a restart.
/// </summary>
internal sealed partial class BudgetEditorPanel : VBoxContainer
{
    private sealed class TemplateRow
    {
        internal required string Template;
        internal required RangeSlider Slider;
        internal required MegaLabel CostLabel;
    }

    private readonly List<TemplateRow> _rows = new();
    private readonly List<Action> _rowRefresh = new();
    private readonly ModConfig _config;
    private readonly Action _scheduleSave;

    public BudgetEditorPanel(ModConfig config, Action scheduleSave)
    {
        _config = config;
        _scheduleSave = scheduleSave;
    }

    public override void _Ready()
    {
        try
        {
            AddThemeConstantOverride("separation", 14);
            Build();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] budget editor build failed: {e}");
        }
    }

    private void Build()
    {
        var title = new MegaLabel { Text = Loc("BUDGET_TITLE") };
        title.AddThemeFontSizeOverride("font_size", 26);
        AddChild(title);

        var hint = new Label
        {
            Text = Loc("BUDGET_HINT"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(760f, 0f),
        };
        hint.AddThemeFontSizeOverride("font_size", 14);
        AddChild(hint);

        AddSection(Loc("BUDGET_SECTION_CORE"));
        AddRows(ChaosRelicCatalog.PositiveTemplates);
        AddSection(Loc("BUDGET_SECTION_NEGATIVE"));
        AddRows(ChaosRelicCatalog.NegativeTemplates);
        // ALWAYS render the extra-pool section (user feedback 2026-09-13: the
        // bounds are config reference even while the pool switch is off).
        AddSection(Loc("BUDGET_SECTION_EXTRA")
            + (QuriousCraftingRelicsConfig.EnableExtraPool ? "" : Loc("BUDGET_SECTION_EXTRA_OFF")));
        AddRows(ChaosRelicExtraCatalog.PositiveTemplates
            .Concat(ChaosRelicExtraCatalog.NegativeTemplates));
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

    /// <summary>One template row: effect text + vanilla refs + range slider + cost.</summary>
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
            Text = EffectText(spec),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        textLine.AddChild(effect);

        // Vanilla relic chips: [effect]([relic1],[relic2],..)
        var refs = VanillaRelicMapping.For(template);
        if (refs.Count > 0)
        {
            textLine.AddChild(new MegaLabel { Text = " (" });
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

        // ---- Line 2: range slider + per-point cost ----
        var sliderLine = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sliderLine.AddChild(new MegaLabel { Text = Loc("BUDGET_RANGE") });
        var slider = new RangeSlider();
        sliderLine.AddChild(slider);
        var costLabel = new MegaLabel();
        sliderLine.AddChild(costLabel);
        row.AddChild(sliderLine);

        var rw = new TemplateRow
        {
            Template = template,
            Slider = slider,
            CostLabel = costLabel,
        };
        _rows.Add(rw);
        _rowRefresh.Add(Refresh);

        // Band: wide enough for the catalog range plus headroom, floored at 8
        // so a small template can still be widened by hand.
        int band = Math.Max(spec.Max, 8) * 2;
        slider.Configure(0, band, spec.Min, spec.Max);

        void Refresh()
        {
            // Live config values (ChaosPointCosts resolves user-tuned costs);
            // negatives show refund per point.
            int per = spec.IsNegative
                ? QuriousCraftingRelicsConfig.PointCosts.RefundPerPoint(template)
                : QuriousCraftingRelicsConfig.PointCosts.CostPerPoint(template);
            costLabel.SetTextAutoSize(Loc("BUDGET_PERPOINT") + " " + per);
        }

        slider.RangeChanged += (low, high) =>
        {
            Persist(template, low, high);
            Refresh();
        };
        Refresh();
        return row;
    }

    /// <summary>
    /// Rendered effect text for a row. Uses the EDITOR renderer, not the
    /// generator's: a row is a template, not a generated relic, so the amount
    /// is shown as the literal N that the row's range slider supplies (and
    /// sloth's derived cap as the expression 7-N). Passing spec.Max here - as
    /// this used to - printed the band's upper bound as if it were the value.
    /// </summary>
    private static string EffectText(ChaosRelicCatalog.TemplateSpec spec) =>
        ChaosRelicGenerator.RenderEditorText(spec);

    /// <summary>
    /// Re-read the LIVE per-point prices into every row's cost label. Called
    /// when the config changes so Cost_ edits made anywhere (BaseLib sliders,
    /// cfg file) are reflected without reopening the page (user request
    /// 2026-09-13).
    /// </summary>
    internal void RefreshCosts()
    {
        foreach (var refresh in _rowRefresh)
        {
            refresh();
        }
    }

    private void Persist(string template, int low, int high)
    {
        try
        {
            QuriousCraftingRelicsConfig.SetTemplateBounds(template, low, high);
            // Persist through the registered config: mark it dirty and let the
            // submenu's debounce timer write it out. BaseLib's own page does
            // exactly this (Changed() -> OnConfigChanged -> autosave).
            _config.Changed();
            _scheduleSave();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] persist bounds {template}: {e.Message}");
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

        chip.MouseEntered += () =>
        {
            try
            {
                NHoverTipSet.CreateAndShow(chip, BuildRelicTip(vref))?
                    .SetGlobalPosition(chip.GlobalPosition + new Vector2(0f, 40f));
            }
            catch (Exception e)
            {
                MainFile.Logger.Error($"[QuriousCraftingRelics] hover tip: {e.Message}");
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
            ? Loc("BUDGET_OURCOST") + " " + pts
            : Loc("BUDGET_OURCOST_NA");
        string desc =
            $"[b]{vref.DisplayName}[/b]\n{vref.Description}\n" +
            $"[color=#c9a227]{Loc("BUDGET_RARITY")}: {vref.Rarity}[/color]\n" +
            $"[color=#8fd48f]{our}[/color]\n" +
            $"[color=#9e9e9e]{vref.EffectNote}[/color]";
        return new HoverTip(new LocString("settings_ui", LocKey("BUDGET_TITLE")), desc);
    }

    /// <summary>
    /// Loc key for a settings string. BaseLib's own settings labels resolve
    /// <c>{ModPrefix}{SLUGIFIED_NAME}.title</c> out of the <c>settings_ui</c>
    /// table, where ModPrefix is the uppercased root namespace plus '-'. The
    /// first version queried the <c>gameplay_ui</c> table without the
    /// <c>.title</c> suffix, so every label fell through to its raw key.
    /// </summary>
    private static string LocKey(string name) => $"{ModPrefix}{name}";

    private static string ModPrefix =>
        typeof(QuriousCraftingRelicsConfig).Namespace is { } ns && ns.Length > 0
            ? ns.Split('.')[0].ToUpperInvariant() + "-"
            : "QURIOUSCRAFTINGRELICS-";

    private static string Loc(string name)
    {
        var loc = LocString.GetIfExists("settings_ui", LocKey(name) + ".title");
        try
        {
            // GetFormattedText parses {braces} as selectors and THROWS on keys
            // like "每点 {P}" when no variable is bound - the exception aborted
            // the editor build mid-way. Keep this path format-free.
            return loc.GetFormattedText();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] loc format failed for {name}: {e.Message}");
            return LocKey(name);
        }
    }
}
