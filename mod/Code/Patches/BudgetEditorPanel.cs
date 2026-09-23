using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using BaseLib.Config;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using QuriousCraftingRelics.Chaos;

namespace QuriousCraftingRelics.Patches;

internal sealed partial class BudgetEditorPanel : VBoxContainer
{
    private sealed record Row(Control Root, string SearchText, int Category, bool Inactive, Action Refresh);
    private readonly List<Row> _rows = new();
    private readonly List<Control> _chips = new();
    private readonly ModConfig _config;
    private readonly Action _scheduleSave;
    private LineEdit _search = null!;
    private OptionButton _category = null!;
    private Label _count = null!;
    private Label _extraNotice = null!;
    private bool _refreshing;

    public BudgetEditorPanel(ModConfig config, Action scheduleSave)
    {
        _config = config;
        _scheduleSave = scheduleSave;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 12);
        var tools = new HBoxContainer();
        _search = new LineEdit { PlaceholderText = Loc("UI_SEARCH"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill, ClearButtonEnabled = true };
        _category = new OptionButton();
        foreach (string key in new[] { "UI_ALL", "BUDGET_SECTION_CORE", "BUDGET_SECTION_NEGATIVE",
                     "BUDGET_SECTION_EXTRA", "UI_INACTIVE" }) _category.AddItem(Loc(key));
        tools.AddChild(_search);
        tools.AddChild(_category);
        AddChild(tools);
        _count = QuriousSettingsStyle.Label("");
        AddChild(_count);
        _extraNotice = QuriousSettingsStyle.Label(Loc("UI_EXTRA_OFF"));
        _extraNotice.AddThemeColorOverride("font_color", QuriousSettingsStyle.Gold);
        AddChild(_extraNotice);
        AddRows(ChaosRelicCatalog.PositiveTemplates, 1);
        AddRows(ChaosRelicCatalog.NegativeTemplates, 2);
        AddRows(ChaosRelicExtraCatalog.PositiveTemplates.Concat(ChaosRelicExtraCatalog.NegativeTemplates), 3);
        _search.TextChanged += _ => ApplyFilter();
        _category.ItemSelected += _ => ApplyFilter();
        RefreshValues();
    }

    private void AddRows(IEnumerable<string> templates, int category)
    {
        foreach (string template in templates)
        {
            var raw = ChaosTemplates.Spec(template);
            bool inactive = ChaosRelicGenerator.UniqueOnly.Contains(template);
            string text = ChaosRelicGenerator.RenderEditorText(raw);
            var card = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            card.AddThemeStyleboxOverride("panel", QuriousSettingsStyle.Box(QuriousSettingsStyle.Surface));
            var body = new VBoxContainer();
            body.AddThemeConstantOverride("separation", 8);
            card.AddChild(body);
            body.AddChild(QuriousSettingsStyle.Label(text));
            if (inactive)
            {
                var notice = QuriousSettingsStyle.Label(Loc("UI_INACTIVE_NOTE"), 16);
                notice.AddThemeColorOverride("font_color", QuriousSettingsStyle.Muted);
                body.AddChild(notice);
            }
            var refs = VanillaRelicMapping.For(template);
            var referenceRow = new HFlowContainer();
            foreach (var reference in refs) referenceRow.AddChild(MakeRelicChip(reference));
            body.AddChild(referenceRow);
            var inputs = new HBoxContainer();
            inputs.AddThemeConstantOverride("separation", 20);
            var rangeColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            rangeColumn.AddChild(QuriousSettingsStyle.Label(Loc("BUDGET_RANGE"), 16));
            var slider = new RangeSlider { SizeFlagsHorizontal = SizeFlags.ExpandFill, Editable = !inactive };
            rangeColumn.AddChild(slider);
            inputs.AddChild(rangeColumn);
            var costColumn = new VBoxContainer();
            costColumn.AddChild(QuriousSettingsStyle.Label(Loc(raw.IsNegative ? "UI_REFUND" : "UI_COST"), 16));
            string propertyName = raw.IsNegative ? ConfigKeyNaming.RefundProperty(template) : ConfigKeyNaming.CostProperty(template);
            var property = typeof(QuriousCraftingRelicsConfig).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException("Missing config property: " + propertyName);
            var price = QuriousSettingsStyle.Number(property);
            price.Editable = !inactive;
            costColumn.AddChild(price);
            inputs.AddChild(costColumn);
            body.AddChild(inputs);
            AddChild(card);
            void Refresh()
            {
                // Editor state is always LIVE, never the run-frozen SpecOf/Effective.
                var spec = QuriousCraftingRelicsConfig.ApplyUserBounds(ChaosTemplates.Spec(template));
                int ceiling = (int)Math.Min(int.MaxValue, Math.Max(8L, spec.Max) * 2L);
                slider.Configure(Math.Min(0, spec.Min), ceiling, spec.Min, spec.Max);
                price.SetValueNoSignal((int)property.GetValue(null)!);
            }
            _rows.Add(new Row(card, template + " " + text + " " + string.Join(" ", refs.Select(r => r.DisplayName)),
                category, inactive, Refresh));
            slider.RangeChanged += (low, high) =>
            {
                if (_refreshing || inactive) return;
                QuriousCraftingRelicsConfig.SetTemplateBounds(template, low, high);
                _config.Changed();
                _scheduleSave();
            };
            price.ValueChanged += value =>
            {
                if (_refreshing || inactive) return;
                int next = QuriousSettingsStyle.EditedNumber(price, value);
                if ((int)property.GetValue(null)! == next) return;
                property.SetValue(null, next);
                _config.Changed();
                _scheduleSave();
            };
        }
    }

    internal void RefreshValues()
    {
        if (_search is null) return;
        _refreshing = true;
        try
        {
            foreach (var row in _rows) row.Refresh();
            _extraNotice.Visible = !QuriousCraftingRelicsConfig.EnableExtraPool;
        }
        finally { _refreshing = false; }
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        ClearHoverTips();
        string query = _search.Text.Trim();
        int category = _category.Selected;
        int visible = 0;
        foreach (var row in _rows)
        {
            bool show = (category == 0 || (category == 4 ? row.Inactive : row.Category == category))
                && row.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
            row.Root.Visible = show;
            if (show) visible++;
        }
        _count.Text = visible == 0 ? Loc("UI_NO_RESULTS") : Loc("UI_RESULTS") + " " + visible + " / " + _rows.Count;
    }

    internal void ClearHoverTips()
    {
        foreach (var chip in _chips)
            if (GodotObject.IsInstanceValid(chip)) NHoverTipSet.Remove(chip);
    }

    public override void _ExitTree()
    {
        ClearHoverTips();
        base._ExitTree();
    }
    /// <summary>Hoverable vanilla-relic name chip: hover = full desc + rarity + our cost.</summary>
    private Control MakeRelicChip(VanillaRelicMapping.VanillaRef vref)
    {
        var chip = new Label
        {
            Text = vref.DisplayName,
            MouseFilter = MouseFilterEnum.Stop,
            FocusMode = FocusModeEnum.All,
        };
        chip.AddThemeColorOverride("font_color", new Color(0.98f, 0.84f, 0.25f));
        chip.AddThemeColorOverride("font_hover_color", new Color(1f, 1f, 1f));

        _chips.Add(chip);
        void ShowTip()
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
        }
        chip.FocusEntered += ShowTip;
        chip.MouseEntered += ShowTip;
        chip.FocusExited += () => NHoverTipSet.Remove(chip);
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
        return new HoverTip(new LocString("settings_ui", LocKey("BUDGET_TITLE") + ".title"), desc);
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
        if (loc is null)
        {
            // Missing key: fall through to the raw key instead of throwing on
            // the null deref (build warning CS8602) or a silent empty label.
            return LocKey(name);
        }
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
