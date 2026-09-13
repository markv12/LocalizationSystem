using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Merges a id,english,context[,<language>...] CSV into LocalizationMasterDatabase.
/// </summary>
[InitializeOnLoad]
public static class LocalizationCsvImporter {
    static LocalizationCsvImporter() {
        if (!File.Exists(Path.GetFullPath(NewStringsPath))) return;
        EditorApplication.delayCall += ImportNewStrings;
    }

    public const string NewStringsPath = "Localization/NewStrings.csv";
    private const string ArchiveFolder = "Localization/Imported";

    [MenuItem("Tools/Localization/Import New Strings")]
    public static void ImportNewStrings() {
        string path = Path.GetFullPath(NewStringsPath);
        if (!File.Exists(path)) {
            Debug.Log($"[Localization] No new strings to import ({NewStringsPath} does not exist).");
            return;
        }

        var database = LocalizationMasterDatabase.LoadDatabase();
        if (database == null) {
            return;
        }

        if (!Import(database, path)) return;

        Directory.CreateDirectory(ArchiveFolder);
        string archived = Path.Combine(ArchiveFolder, $"NewStrings-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        File.Move(path, archived);
        Debug.Log($"[Localization] Archived imported strings to {archived}.");
    }

    public static bool Import(LocalizationMasterDatabase database, string csvPath) {
        if (database == null || string.IsNullOrEmpty(csvPath)) return false;

        string csvContent = File.ReadAllText(csvPath);
        var grid = CSVParser.LoadFromString(csvContent);

        if (grid == null || grid.Count < 2) {
            Debug.LogWarning("[Localization] CSV is empty or invalid.");
            return false;
        }

        List<string> headers = grid[0];
        var columns = LocalizationCsv.FindColumns(headers);
        if (columns == null) {
            Debug.LogError("[Localization] CSV must contain 'id' and 'english' columns.");
            return false;
        }

        var (keyIndex, englishIndex, contextIndex) = columns.Value;

        var headerToCode = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "spain spanish", "spanish" },
            { "latam spanish", "latam" },
            { "brazilian portuguese", "brazilian" },
            { "simplified chinese", "schinese" },
            { "traditional chinese", "tchinese" }
        };

        foreach (var lang in SteamLanguageList.All) {
            if (!headerToCode.ContainsKey(lang.langCode)) headerToCode[lang.langCode] = lang.langCode;
            if (!headerToCode.ContainsKey(lang.displayName)) headerToCode[lang.displayName] = lang.langCode;
        }

        var languageMappings = new List<(int index, string code)>();
        for (int i = 0; i < headers.Count; i++) {
            if (i == keyIndex || i == englishIndex || i == contextIndex) continue;

            string header = headers[i].Trim();
            if (headerToCode.TryGetValue(header, out string code)) {
                languageMappings.Add((i, code));
                if (!database.TargetLanguages.Contains(code, StringComparer.OrdinalIgnoreCase)) {
                    database.TargetLanguages.Add(code);
                }
            }
        }

        int added = 0, updated = 0;
        for (int i = 1; i < grid.Count; i++) {
            var row = grid[i];
            if (row.Count <= Mathf.Max(keyIndex, englishIndex)) continue;

            string key = row[keyIndex];
            string english = row[englishIndex];

            if (string.IsNullOrWhiteSpace(key)) continue;

            var entry = database.FindEntry(key);
            if (entry == null) {
                entry = new LocalizationEntry { Key = key };
                database.Entries.Add(entry);
                added++;
            } else {
                string context = contextIndex >= 0 && row.Count > contextIndex ? row[contextIndex] : entry.Context;
                if (entry.EnglishText != english.Replace("\\n", Environment.NewLine) || entry.Context != context) updated++;
            }

            entry.EnglishText = english.Replace("\\n", Environment.NewLine);
            if (contextIndex >= 0 && row.Count > contextIndex) entry.Context = row[contextIndex];
            string englishHash = LocalizationProcessor.GenerateHash(entry.EnglishText, entry.Context);

            foreach (var mapping in languageMappings) {
                if (row.Count <= mapping.index) continue;

                string translatedText = row[mapping.index].Replace("\\n", Environment.NewLine);
                if (string.IsNullOrWhiteSpace(translatedText) || translatedText.Equals("x", StringComparison.OrdinalIgnoreCase)) continue;

                var translation = entry.GetTranslation(mapping.code);
                if (translation == null) {
                    translation = new LanguageTranslation { LanguageCode = mapping.code };
                    entry.Translations.Add(translation);
                }

                translation.TranslatedText = translatedText;
                translation.Status = TranslationStatus.Translated;
                translation.SourceHash = englishHash;
            }
        }

        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();
        LocalizationProcessor.BakeAssets(database);
        Debug.Log($"[Localization] Imported {grid.Count - 1} rows from {csvPath}: {added} new, {updated} changed.");
        return true;
    }
}
