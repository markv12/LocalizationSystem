using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Compares text authored in UXML files against the localization database and writes
/// whatever is missing or has changed into LocalizationCsvImporter.NewStringsPath.
/// </summary>
public static class UxmlStringSync {
    [MenuItem("Tools/Localization/Sync UXML Strings")]
    public static void Sync() {
        var database = LocalizationMasterDatabase.LoadDatabase();
        if (database == null) {
            return;
        }

        if (!Directory.Exists("Assets")) return;

        var extracted = new List<(string key, string english, string context)>();
        var documentPrefixes = new HashSet<string>();

        foreach (string path in Directory.GetFiles("Assets", "*.uxml", SearchOption.AllDirectories).OrderBy(p => p)) {
            string prefix = UILocalization.DocumentPrefix(Path.GetFileNameWithoutExtension(path));
            documentPrefixes.Add(prefix);
            ExtractDocument(XDocument.Load(path), prefix, extracted);
        }

        var pending = LocalizationCsv.Read(LocalizationCsvImporter.NewStringsPath);
        int added = 0, changed = 0;
        foreach (var (key, english, seedContext) in extracted) {
            var entry = database.FindEntry(key);
            if (entry == null) {
                if (pending.Set(key, english, seedContext)) added++;
            } else if (entry.EnglishText != english) {
                pending.Set(key, english, entry.Context);
                changed++;
            }
        }

        var extractedKeys = new HashSet<string>(extracted.Select(e => e.key));
        var orphans = database.Entries
            .Select(e => e.Key)
            .Where(k => !extractedKeys.Contains(k) && documentPrefixes.Contains(k.Split('.')[0]))
            .ToList();

        if (added + changed > 0) LocalizationCsv.Write(LocalizationCsvImporter.NewStringsPath, pending);

        string orphanNote = orphans.Count > 0
            ? $"\n- <b>Orphaned UXML keys ({orphans.Count})</b>, no matching element any more: {string.Join(", ", orphans)}"
            : "";
        string next = added + changed > 0
            ? $"\nWritten to {LocalizationCsvImporter.NewStringsPath} — review translator context, then import."
            : "";
        Debug.Log($"<b>[Localization] Scanned {extracted.Count} UXML strings</b>\n" +
                  $"- New: {added}\n- Changed English: {changed}{orphanNote}{next}");
    }

    private static void ExtractDocument(XDocument doc, string prefix, List<(string, string, string)> into) {
        foreach (XElement element in doc.Descendants()) {
            string name = (string)element.Attribute("name");
            if (string.IsNullOrEmpty(name)) continue;

            string classes = (string)element.Attribute("class") ?? "";
            if (classes.Split(' ').Contains(UILocalization.NO_LOC_CLASS)) continue;

            string text = (string)element.Attribute("text") ?? (string)element.Attribute("label");
            string key = UILocalization.Key(prefix, name);
            if (!string.IsNullOrWhiteSpace(text))
                into.Add((key, text, Context(element, name)));

            string tooltip = (string)element.Attribute("tooltip");
            if (!string.IsNullOrWhiteSpace(tooltip))
                into.Add((key + UILocalization.TOOLTIP_SUFFIX, tooltip, $"Tooltip for the {UILocalization.Humanize(name)}."));
        }
    }

    private static string Context(XElement element, string name) {
        string type = element.Name.LocalName;
        string readable = UILocalization.Humanize(name);
        switch (type) {
            case "Button": return $"Button label ({readable}).";
            case "Toggle": return $"Checkbox label ({readable}).";
            case "DropdownField": return $"Dropdown label ({readable}).";
            case "SliderInt":
            case "Slider": return $"Slider label ({readable}).";
            case "TextField": return $"Text input label ({readable}).";
            default: return $"UI label ({readable}).";
        }
    }
}
