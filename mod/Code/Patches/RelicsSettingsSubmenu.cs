using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace QuriousCraftingRelics.Patches;

internal sealed partial class RelicsSettingsSubmenu : NSubmenu
{
    private const double AutosaveDelay = 5.0;
    private Control? _initialFocus;
    private ModConfig? _config;
    private BudgetEditorPanel? _budgetEditor;
    private VBoxContainer _basic = null!;
    private Label _status = null!;
    private ScrollContainer _scroll = null!;
    private Button _basicTab = null!;
    private Button _editorTab = null!;
    private readonly List<Action> _refreshRules = new();
    private double _saveTimer = -1;
    private bool _dirty;
    private bool _refreshing;
    protected override Control? InitialFocusedControl => _initialFocus;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Theme = QuriousSettingsStyle.CreateTheme();
        var background = new ColorRect { Color = QuriousSettingsStyle.Background,
            MouseFilter = MouseFilterEnum.Ignore };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var layout = new VBoxContainer { AnchorLeft = 0.06f, AnchorRight = 0.94f,
            AnchorTop = 0, AnchorBottom = 1, OffsetTop = 24, OffsetBottom = -100 };
        layout.AddThemeConstantOverride("separation", 12);
        AddChild(layout);
        layout.AddChild(QuriousSettingsStyle.Label(TextOf("SETTINGS_PAGE_TITLE"), 30));
        var hint = QuriousSettingsStyle.Label(TextOf("UI_GUIDE"), 16);
        hint.AddThemeColorOverride("font_color", QuriousSettingsStyle.Muted);
        layout.AddChild(hint);
        var navigation = new HBoxContainer();
        _basicTab = new Button { Text = TextOf("UI_BASIC"), ToggleMode = true };
        _editorTab = new Button { Text = TextOf("UI_EDITOR"), ToggleMode = true };
        navigation.AddChild(_basicTab);
        navigation.AddChild(_editorTab);
        _status = QuriousSettingsStyle.Label(TextOf("UI_READY"), 16);
        _status.HorizontalAlignment = HorizontalAlignment.Right;
        navigation.AddChild(_status);
        layout.AddChild(navigation);
        _initialFocus = _basicTab;
        _scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill, FollowFocus = true,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        layout.AddChild(_scroll);
        var pages = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _scroll.AddChild(pages);
        _basic = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _basic.AddThemeConstantOverride("separation", 10);
        pages.AddChild(_basic);
        try
        {
            _config = ModConfigRegistry.Get(MainFile.ModId) ?? ModConfigRegistry.Get<QuriousCraftingRelicsConfig>();
            if (_config is null) throw new InvalidOperationException("No registered config");
            AddRule(nameof(QuriousCraftingRelicsConfig.EnableChaosRelics), "ENABLE_CHAOS_RELICS");
            AddRule(nameof(QuriousCraftingRelicsConfig.EnableExtraPool), "ENABLE_EXTRA_POOL");
            foreach (string rarity in new[] { "Common", "Uncommon", "Rare" })
            {
                AddRule("ChaosRelicBudget" + rarity, "CHAOS_RELIC_BUDGET_" + rarity.ToUpperInvariant());
                AddRule("ChaosRelicNegativeChance" + rarity, "CHAOS_RELIC_NEGATIVE_CHANCE_" + rarity.ToUpperInvariant());
            }
            _budgetEditor = new BudgetEditorPanel(_config, ScheduleSave) { Visible = false };
            pages.AddChild(_budgetEditor);
            _config.ConfigChanged += OnConfigChanged;
            RefreshValues();
        }
        catch (Exception e)
        {
            _basic.AddChild(QuriousSettingsStyle.Label(TextOf("SETTINGS_PAGE_UNAVAILABLE")));
            SetStatus("UI_ERROR", true);
            _editorTab.Disabled = true;
            MainFile.Logger.Error($"[QuriousCraftingRelics] settings build failed: {e}");
        }
        _basicTab.Pressed += () => ShowPage(false);
        _editorTab.Pressed += () => ShowPage(true);
        ShowPage(false);
        var back = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache
            .GetScene(MegaCrit.Sts2.Core.Helpers.SceneHelper.GetScenePath("ui/back_button"))
            .Instantiate<NBackButton>();
        back.Name = "BackButton";
        AddChild(back);
        // NSubmenu._Ready must not be called by derived classes.
        ConnectSignals();
    }

    private void AddRule(string name, string labelKey)
    {
        var property = typeof(QuriousCraftingRelicsConfig).GetProperty(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("Missing config property: " + name);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", QuriousSettingsStyle.Box(QuriousSettingsStyle.Surface));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        panel.AddChild(row);
        row.AddChild(QuriousSettingsStyle.Label(TextOf(labelKey)));
        if (property.PropertyType == typeof(bool))
        {
            var toggle = new CheckButton();
            row.AddChild(toggle);
            _refreshRules.Add(() => toggle.SetPressedNoSignal((bool)property.GetValue(null)!));
            toggle.Toggled += value =>
            {
                if (_refreshing || (bool)property.GetValue(null)! == value) return;
                property.SetValue(null, value);
                _config!.Changed();
            };
        }
        else
        {
            var input = QuriousSettingsStyle.Number(property);
            row.AddChild(input);
            _refreshRules.Add(() => input.SetValueNoSignal((int)property.GetValue(null)!));
            input.ValueChanged += value =>
            {
                if (_refreshing) return;
                int next = QuriousSettingsStyle.EditedNumber(input, value);
                if ((int)property.GetValue(null)! == next) return;
                property.SetValue(null, next);
                _config!.Changed();
            };
        }
        _basic.AddChild(panel);
    }

    private void ShowPage(bool editor)
    {
        _budgetEditor?.ClearHoverTips();
        _basic.Visible = !editor;
        if (_budgetEditor is not null) _budgetEditor.Visible = editor;
        _basicTab.SetPressedNoSignal(!editor);
        _editorTab.SetPressedNoSignal(editor);
        _scroll.ScrollVertical = 0;
    }

    private void RefreshValues()
    {
        _refreshing = true;
        try
        {
            foreach (var refresh in _refreshRules) refresh();
            _budgetEditor?.RefreshValues();
        }
        finally { _refreshing = false; }
    }

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        ScheduleSave();
        RefreshValues();
    }

    private void ScheduleSave()
    {
        _dirty = true;
        _saveTimer = AutosaveDelay;
        SetStatus("UI_PENDING");
    }

    private void SetStatus(string key, bool error = false)
    {
        if (_status is null) return;
        _status.Text = TextOf(key);
        _status.AddThemeColorOverride("font_color", error ? QuriousSettingsStyle.Error : QuriousSettingsStyle.Gold);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_saveTimer < 0) return;
        _saveTimer -= delta;
        if (_saveTimer <= 0) SaveNow();
    }

    private void SaveNow()
    {
        _saveTimer = -1;
        if (!_dirty || _config is null) return;
        try
        {
            // BaseLib.Save swallows I/O errors and lock timeouts. Verify its existing
            // output before claiming success; never introduce a second writer.
            var path = typeof(ModConfig).GetField("_path", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(_config) as string ?? throw new InvalidOperationException("Config path unavailable");
            var properties = typeof(ModConfig).GetField("ConfigProperties", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(_config) as IEnumerable<PropertyInfo>
                ?? throw new InvalidOperationException("Config properties unavailable");
            var expected = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in properties)
            {
                var text = TypeDescriptor.GetConverter(property.PropertyType).ConvertToInvariantString(property.GetValue(null));
                if (text is null) throw new InvalidOperationException("Config conversion failed: " + property.Name);
                expected.Add(property.Name, text);
            }
            _config.Save();
            var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                ?? throw new IOException("Config readback failed");
            foreach (var pair in expected)
                if (!saved.TryGetValue(pair.Key, out string? value) || value != pair.Value)
                    throw new IOException("Config readback mismatch: " + pair.Key);
            _dirty = false;
            SetStatus("UI_SAVED");
        }
        catch (Exception e)
        {
            SetStatus("UI_ERROR", true);
            MainFile.Logger.Error($"[QuriousCraftingRelics] config save not confirmed: {e.Message}");
        }
    }

    protected override void OnSubmenuShown()
    {
        base.OnSubmenuShown();
        RefreshValues();
    }

    protected override void OnSubmenuHidden()
    {
        _budgetEditor?.ClearHoverTips();
        SaveNow();
        base.OnSubmenuHidden();
    }

    public override void _ExitTree()
    {
        SaveNow();
        if (_config is not null) _config.ConfigChanged -= OnConfigChanged;
        _budgetEditor?.ClearHoverTips();
        base._ExitTree();
    }
    /// <summary>
    /// Lazily create/attach the page into a submenu stack - the
    /// GetSubmenuType interception target (AutoAnthony registry pattern).
    /// </summary>
    internal static bool GetOrCreate(NSubmenuStack stack, Type type, ref NSubmenu result)
    {
        if (type != typeof(RelicsSettingsSubmenu))
        {
            return true; // not ours: let vanilla handle it
        }
        if (!Pages.TryGetValue(stack, out var holder))
        {
            var page = new RelicsSettingsSubmenu();
            page.Visible = false;
            page.SetAnchorsPreset(LayoutPreset.FullRect, false);
            page.Position = Vector2.Zero;
            page.Size = stack.Size;
            MegaCrit.Sts2.Core.Helpers.GodotTreeExtensions.AddChildSafely(stack, page);
            page.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, (LayoutPresetMode)0, 0);
            holder = new PageHolder(page);
            Pages.Add(stack, holder);
        }
        result = holder.Page;
        return false;
    }

    private sealed class PageHolder(RelicsSettingsSubmenu page)
    {
        internal RelicsSettingsSubmenu Page { get; } = page;
    }

    private static readonly ConditionalWeakTable<NSubmenuStack, PageHolder> Pages = new();

    /// <summary>
    /// Settings string lookup. BaseLib resolves config labels from the
    /// <c>settings_ui</c> table under <c>{ModPrefix}{NAME}.title</c>; the first
    /// version queried <c>gameplay_ui</c> with no suffix and always missed.
    /// </summary>
    private static string TextOf(string name)
    {
        string key = ModPrefix + name + ".title";
        var loc = LocString.GetIfExists("settings_ui", key);
        return loc?.GetFormattedText() ?? key;
    }

    private static string ModPrefix =>
        typeof(QuriousCraftingRelicsConfig).Namespace is { } ns && ns.Length > 0
            ? ns.Split('.')[0].ToUpperInvariant() + "-"
            : "QURIOUSCRAFTINGRELICS-";
}
