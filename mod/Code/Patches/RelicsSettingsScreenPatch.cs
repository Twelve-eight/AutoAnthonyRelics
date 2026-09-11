using System;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace AutoAnthonyRelics.Patches;

/// <summary>
/// Adds the "AutoAnthony - Relics settings" entry row to the vanilla
/// settings screen's General panel, beside AutoAnthony's own group row -
/// both mods live OUTSIDE BaseLib's mod settings page (user order
/// 2026-09-11). Row pattern copied from AutoAnthony's AddGroupRow
/// (duplicate the Modding row, relabel, wire the button to push our
/// dedicated submenu page).
/// </summary>
[HarmonyPatch(typeof(NSettingsScreen), "_Ready")]
internal static class RelicsSettingsScreenPatch
{
    private static void Postfix(NSettingsScreen __instance)
    {
        try
        {
            var panel = __instance.GetNode<NSettingsPanel>("%GeneralSettings");
            var content = panel.Content;
            // Templates: the Modding group row (label + open-page button).
            var moddingRow = content.GetNodeOrNull<Control>("Modding");
            if (moddingRow is null || content.GetNodeOrNull("AutoAnthonyRelicsSettingsGroup") is not null)
            {
                return;
            }
            var divider = content.GetNodeOrNull<Node>("ModdingDivider");
            int insertionIndex = divider?.GetIndex(false) ?? content.GetChildCount();
            // Insert AFTER AutoAnthony's own group row when present, else
            // right after the Modding divider - visually "beside" it.
            var anthonyRow = content.GetNodeOrNull<Node>("AutoAnthonySettingsGroup");
            if (anthonyRow is not null)
            {
                insertionIndex = anthonyRow.GetIndex(false) + 1;
            }
            AddGroupRow(content, moddingRow, insertionIndex);
            panel.Call(NSettingsPanel.MethodName.RefreshSize);
            panel.Call(NSettingsPanel.MethodName.UpdateNavigation);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"[AutoAnthonyRelics] settings row patch failed: {e.Message}");
        }
    }

    private static void AddGroupRow(VBoxContainer content, Control source, int insertionIndex)
    {
        var row = (Control)source.Duplicate(6); // signals+groups, like AutoAnthony
        row.Name = "AutoAnthonyRelicsSettingsGroup";
        FixOwnerRecursive(row, row);
        content.AddChild(row, false, 0);
        content.MoveChild(row, insertionIndex);
        row.Visible = true;
        SetLabel(row.GetNodeOrNull<Node>("Label"), TextOf("SETTINGS_GROUP"));

        var button = row.GetNodeOrNull<NOpenModdingScreenButton>("ModdingButton");
        if (button is null)
        {
            MainFile.Logger.Error("[AutoAnthonyRelics] duplicated settings row has no button");
            return;
        }
        button.Name = "AutoAnthonyRelicsSettingsGroupButton";
        ((NClickableControl)button).Enable();
        button.Connect(NButton.SignalName.Released, Callable.From<NButton>(_ => OpenDedicatedPage(row)), 0u);
        SetLabel(button.GetNodeOrNull<Node>("Label"), TextOf("SETTINGS_OPEN"));
    }

    /// <summary>Walk up to the settings screen's submenu stack and push our page.</summary>
    private static void OpenDedicatedPage(Node node)
    {
        for (var current = node; current is not null; current = current.GetParent())
        {
            if (current is NSubmenuStack stack)
            {
                stack.PushSubmenuType(typeof(RelicsSettingsSubmenu));
                return;
            }
        }
        MainFile.Logger.Error("[AutoAnthonyRelics] could not locate a submenu stack for the settings page");
    }

    /// <summary>Re-own duplicated nodes (Godot Duplicate keeps old owner refs).</summary>
    private static void FixOwnerRecursive(Node node, Node newOwner)
    {
        if (node.Owner is not null && node.Owner != newOwner)
        {
            node.Owner = newOwner;
        }
        foreach (var child in node.GetChildren())
        {
            FixOwnerRecursive(child, newOwner);
        }
    }

    private static void SetLabel(Node? labelNode, string text)
    {
        if (labelNode is MegaCrit.Sts2.addons.mega_text.MegaLabel mega)
        {
            mega.SetTextAutoSize(text);
        }
        else if (labelNode is RichTextLabel rich)
        {
            rich.Text = text;
        }
    }

    /// <summary>
    /// Settings string lookup. BaseLib resolves config labels from the
    /// <c>settings_ui</c> table under <c>{ModPrefix}{NAME}.title</c>; the first
    /// version queried <c>gameplay_ui</c> with no suffix, so the row label and
    /// button text always rendered as raw keys.
    /// </summary>
    private static string TextOf(string name)
    {
        string key = ModPrefix + name + ".title";
        var loc = MegaCrit.Sts2.Core.Localization.LocString.GetIfExists("settings_ui", key);
        return loc?.GetFormattedText() ?? key;
    }

    private static string ModPrefix =>
        typeof(AutoAnthonyRelicsConfig).Namespace is { } ns && ns.Length > 0
            ? ns.Split('.')[0].ToUpperInvariant() + "-"
            : "AUTOANTHONYRELICS-";
}

/// <summary>
/// GetSubmenuType interception so our page type can be pushed into the
/// main-menu submenu stack (AutoAnthony registry pattern, byte-verified
/// against its dll: prefix returning false with __result set).
/// </summary>
[HarmonyPatch(typeof(NMainMenuSubmenuStack), "GetSubmenuType", new Type[] { typeof(Type) })]
internal static class RelicsSettingsSubmenuRegistrationPatch
{
    private static bool Prefix(NMainMenuSubmenuStack __instance, Type type, ref NSubmenu __result)
    {
        return RelicsSettingsSubmenu.GetOrCreate((NSubmenuStack)(object)__instance, type, ref __result);
    }
}
