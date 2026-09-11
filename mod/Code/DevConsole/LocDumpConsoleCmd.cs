using System;
using System.IO;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Entities.Players;

namespace AutoAnthonyRelics.Code.DevConsole;

/// <summary>
/// Dev-only console command that dumps vanilla loc tables to JSON so we can
/// align our Chinese text with the game's official terminology
/// (user order 2026-09-11: full zhs alignment, e.g. 护体 -> 人工制品).
///
/// Usage (dev console, needs debug commands enabled - mods always get them):
///   locdump                    - dump all tables for current language
///   locdump <table>            - one table, current language
///   locdump <table> <lang>     - one table, specific language
/// Output: G:/omp works/.tmp/locdump/&lt;lang&gt;/&lt;table&gt;.json (sorted keys).
/// </summary>
public class LocDumpConsoleCmd : AbstractConsoleCmd
{
    private static readonly string OutRoot = "G:/omp works/.tmp/locdump";

    public override string CmdName => "locdump";
    public override string Args => "[table:string] [lang:string]";
    public override string Description => "Dumps vanilla loc tables to JSON (dev: terminology alignment)";
    public override bool IsNetworked => false;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        try
        {
            // Which language? A fresh LocManager load is heavyweight; dump the
            // ACTIVE language only (game runs in zhs for our user). Explicit
            // lang arg reloads via SetLanguage if different.
            string current = LocManager.Instance.Language;
            string lang = args.Length >= 2 ? args[1] : current;
            if (lang != current)
            {
                LocManager.Instance.SetLanguage(lang);
            }

            var tables = AccessTools.Field(typeof(LocManager), "_tables")
                .GetValue(LocManager.Instance) as System.Collections.Generic.Dictionary<string, LocTable>
                ?? throw new InvalidOperationException("LocManager._tables unavailable");

            string[] wanted = args.Length >= 1 && args[0] != "all"
                ? new[] { args[0] }
                : new[] { "powers", "relics", "keywords", "gameplay_ui", "cards", "potions" };

            int total = 0;
            foreach (string name in wanted)
            {
                if (!tables.TryGetValue(name, out var table))
                {
                    continue;
                }
                var translations = AccessTools.Field(typeof(LocTable), "_translations")
                    .GetValue(table) as System.Collections.Generic.Dictionary<string, string>
                    ?? throw new InvalidOperationException($"LocTable._translations unavailable for {name}");

                string dir = Path.Combine(OutRoot, lang);
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, name + ".json");

                // Sorted keys; strip SmartFormat braces noise? No - keep raw,
                // braces are part of the terminology we need to match.
                var sorted = new System.Collections.Generic.SortedDictionary<string, string>(translations);
                File.WriteAllText(path, JsonSerializer.Serialize(sorted, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                }));
                total += sorted.Count;
                MainFile.Logger.Info($"[locdump] {lang}/{name}: {sorted.Count} keys -> {path}");
            }

            if (lang != current)
            {
                LocManager.Instance.SetLanguage(current);
            }
            return new CmdResult(success: true, $"locdump: {total} keys written to {OutRoot}/{lang}/");
        }
        catch (Exception e)
        {
            return new CmdResult(success: false, $"locdump failed: {e.Message}");
        }
    }
}
