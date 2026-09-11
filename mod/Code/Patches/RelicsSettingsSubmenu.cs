using System;
using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace AutoAnthonyRelics.Patches;

/// <summary>
/// Dedicated settings page for AutoAnthonyRelics, OUTSIDE BaseLib's mod
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
/// - The page itself hosts BaseLib's SimpleModConfig UI for our existing
///   config class, so all 46 sliders/toggles render unchanged.
/// </summary>
internal sealed partial class RelicsSettingsSubmenu : NSubmenu
{
    private Control? _initialFocus;

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
                Text = TextOf("AUTOANTHONYRELICS-SETTINGS_PAGE_TITLE"),
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
                Name = "AutoAnthonyRelicsSettingsScroll",
                HorizontalScrollMode = (ScrollContainer.ScrollMode)0,
                VerticalScrollMode = (ScrollContainer.ScrollMode)1,
                AnchorLeft = 0.19f,
                AnchorRight = 0.81f,
                AnchorTop = 0f,
                AnchorBottom = 1f,
                OffsetTop = 115f,
                OffsetBottom = -105f,
            };
            AddChild(scroll, false, 0);
            scroll.CustomMinimumSize = new Vector2(800f, 0f);

            var options = new VBoxContainer
            {
                Name = "AutoAnthonyRelicsSettingsOptions",
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
            MainFile.Logger.Error($"[AutoAnthonyRelics] settings page build failed: {e}");
        }
    }

    /// <summary>Host BaseLib's config UI + the visual budget editor.</summary>
    private void BuildOptions(VBoxContainer options)
    {
        try
        {
            // SimpleModConfig.SetupConfigUI builds all rows (sections,
            // sliders, toggles) into a container; hand it ours directly.
            var config = new AutoAnthonyRelicsConfig();
            config.SetupConfigUI(options);

            // Visual budget editor (user order 2026-09-11): effect text +
            // vanilla relic refs with hover popups + Min/Max sliders.
            var editorHeader = new Godot.Label
            {
                Text = TextOf("AUTOANTHONYRELICS-BUDGET_TITLE"),
            };
            editorHeader.AddThemeFontSizeOverride("font_size", 24);
            options.AddChild(editorHeader, false, 0);
            var editor = new BudgetEditorPanel();
            options.AddChild(editor, false, 0);
            _initialFocus = options.GetChildOrNull<Godot.Control>(0);
        }
        catch (System.Exception e)
        {
            MainFile.Logger.Error($"[AutoAnthonyRelics] config UI build failed: {e}");
            options.AddChild(new Godot.Label
            {
                Text = TextOf("AUTOANTHONYRELICS-SETTINGS_PAGE_UNAVAILABLE"),
                HorizontalAlignment = Godot.HorizontalAlignment.Center,
            }, false, 0);
        }
    }

    private static string TextOf(string key)
    {
        try
        {
            return new MegaCrit.Sts2.Core.Localization.LocString("gameplay_ui", key).GetRawText();
        }
        catch
        {
            return key;
        }
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
}
