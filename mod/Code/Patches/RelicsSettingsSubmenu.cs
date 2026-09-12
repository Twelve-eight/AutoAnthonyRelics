using System;
using System.Runtime.CompilerServices;
using BaseLib.Config;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace QuriousCraftingRelics.Patches;

/// <summary>
/// Dedicated settings page for QuriousCraftingRelics, OUTSIDE BaseLib's mod
/// settings screen - sitting beside AutoAnthony's own settings entry in the
/// vanilla settings screen (user order 2026-09-11: "东尼算法本体的设置菜单
/// 是在baselib设置页外的.稍后把我们的菜单与它平齐").
///
/// Pattern copied from AutoAnthony's implementation (decompiled + verified):
/// - NSettingsScreen._Ready postfix adds a group row to the General panel
///   (duplicate of the Modding row) that pushes this submenu.
/// - NMainMenuSubmenuStack.GetSubmenuType prefix intercepts our type and
///   lazily instantiates the page into the stack (AutoAnthony registry
///   pattern).
/// - The page itself hosts BaseLib's SimpleModConfig UI for the REGISTERED
///   config instance, so the sliders/toggles render and edit the live values.
///
/// Persistence follows BaseLib's own NModConfigSubmenu: the config's
/// Changed() event arms a debounce timer, the timer writes the file, and
/// OnSubmenuHidden flushes immediately. Without that wiring (the first
/// version) nothing the user changed on this page reached the config file.
/// </summary>
internal sealed partial class RelicsSettingsSubmenu : NSubmenu
{
    private const double AutosaveDelay = 5.0;

    private Control? _initialFocus;
    private ModConfig? _config;
    private double _saveTimer = -1;

    protected override Control? InitialFocusedControl => _initialFocus;

    public override void _Ready()
    {
        try
        {
            SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect, (LayoutPresetMode)0, 0);
            GrowHorizontal = GrowDirection.Both;
            GrowVertical = GrowDirection.Both;

            var title = new Label
            {
                Text = TextOf("SETTINGS_PAGE_TITLE"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(title, false, 0);
            title.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop, (LayoutPresetMode)0, 0);
            title.OffsetTop = 35f;
            title.OffsetBottom = 105f;
            title.AddThemeFontSizeOverride("font_size", 34);

            var scroll = new ScrollContainer
            {
                Name = "QuriousCraftingRelicsSettingsScroll",
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
                VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
                AnchorLeft = 0.19f,
                AnchorRight = 0.81f,
                AnchorTop = 0f,
                AnchorBottom = 1f,
                OffsetTop = 115f,
                OffsetBottom = -105f,
                CustomMinimumSize = new Vector2(800f, 0f),
            };
            AddChild(scroll, false, 0);

            var options = new VBoxContainer
            {
                Name = "QuriousCraftingRelicsSettingsOptions",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(800f, 0f),
            };
            options.AddThemeConstantOverride("separation", 4);
            scroll.AddChild(options, false, 0);

            BuildOptions(options);

            var back = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache
                .GetScene(MegaCrit.Sts2.Core.Helpers.SceneHelper.GetScenePath("ui/back_button"))
                .Instantiate<NBackButton>();
            back.Name = "BackButton";
            AddChild(back, false, 0);
            ConnectSignals();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] settings page build failed: {e}");
        }
    }

    /// <summary>Host BaseLib's config UI + the visual budget editor.</summary>
    private void BuildOptions(VBoxContainer options)
    {
        try
        {
            // The REGISTERED instance, not a fresh one: BaseLib persists the
            // registered config, so a throwaway copy would swallow every edit.
            _config = ModConfigRegistry.Get(MainFile.ModId)
                ?? ModConfigRegistry.Get<QuriousCraftingRelicsConfig>();
            if (_config is null)
            {
                MainFile.Logger.Error("[QuriousCraftingRelics] no registered config; settings page read-only");
            }
            else
            {
                _config.ConfigChanged += OnConfigChanged;
                _config.SetupConfigUI(options);
            }

            var editorHeader = new Label
            {
                Text = TextOf("BUDGET_TITLE"),
            };
            editorHeader.AddThemeFontSizeOverride("font_size", 24);
            options.AddChild(editorHeader, false, 0);

            if (_config is not null)
            {
                _budgetEditor = new BudgetEditorPanel(_config, ScheduleSave);
                options.AddChild(_budgetEditor, false, 0);
            }

            _initialFocus = options.GetChildOrNull<Control>(0);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] config UI build failed: {e}");
            options.AddChild(new Label
            {
                Text = TextOf("SETTINGS_PAGE_UNAVAILABLE"),
                HorizontalAlignment = HorizontalAlignment.Center,
            }, false, 0);
        }
    }

    private BudgetEditorPanel? _budgetEditor;

    private void OnConfigChanged(object? sender, EventArgs e)
    {
        ScheduleSave();
        _budgetEditor?.RefreshCosts();
    }

    private void ScheduleSave() => _saveTimer = AutosaveDelay;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_saveTimer <= 0)
        {
            return;
        }
        _saveTimer -= delta;
        if (_saveTimer <= 0)
        {
            SaveNow();
        }
    }

    private void SaveNow()
    {
        _saveTimer = -1;
        try
        {
            _config?.Save();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[QuriousCraftingRelics] config save failed: {e.Message}");
        }
    }

    /// <summary>Leaving the page flushes pending edits (BaseLib does the same).</summary>
    protected override void OnSubmenuHidden()
    {
        SaveNow();
        base.OnSubmenuHidden();
    }

    public override void _ExitTree()
    {
        SaveNow();
        if (_config is not null)
        {
            _config.ConfigChanged -= OnConfigChanged;
        }
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
