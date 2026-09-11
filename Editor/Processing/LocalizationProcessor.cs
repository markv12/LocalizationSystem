using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class LocalizationProcessor {
    public static string GenerateHash(string text, string context = null) {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        string combined = string.IsNullOrEmpty(context) ? text : text + "\x00" + context;
        using (MD5 md5 = MD5.Create()) {
            byte[] inputBytes = Encoding.UTF8.GetBytes(combined);
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++) {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }

    public static void PrepareTranslationBatch(LocalizationMasterDatabase database, IEnumerable<LocalizationEntry> entriesToProcess = null) {
        if (database == null) return;

        database.TargetLanguages.Sort((a, b) =>
            SteamLanguageList.GetSortIndex(a).CompareTo(SteamLanguageList.GetSortIndex(b)));

        var targetEntries = (entriesToProcess ?? database.AllEntries).ToList();

        int newEntries = 0;
        int dirtyEntries = 0;
        int totalStringsToTranslate = 0;
        int removedTranslations = 0;
        var languagesAffected = new HashSet<string>();

        // Cleanup: remove translations for languages no longer in TargetLanguages
        foreach (var entry in targetEntries) {
            int initialCount = entry.Translations.Count;
            entry.Translations.RemoveAll(t => !database.TargetLanguages.Contains(t.LanguageCode, System.StringComparer.OrdinalIgnoreCase));
            removedTranslations += (initialCount - entry.Translations.Count);
        }

        if (removedTranslations > 0) {
            Debug.Log($"[Localization] Removed {removedTranslations} translations for languages no longer in TargetLanguages list.");
        }

        foreach (var lang in database.TargetLanguages) {
            foreach (var entry in targetEntries) {
                string currentHash = GenerateHash(entry.EnglishText, entry.Context);
                var translation = entry.GetTranslation(lang);
                bool isPending = false;

                if (translation == null) {
                    entry.SetTranslation(lang, new LanguageTranslation {
                        LanguageCode = lang,
                        Status = TranslationStatus.Untranslated
                    });
                    newEntries++;
                    isPending = true;
                } else if (translation.Status == TranslationStatus.Translated && translation.SourceHash != currentHash) {
                    translation.Status = TranslationStatus.Dirty;
                    dirtyEntries++;
                    isPending = true;
                } else if (translation.Status == TranslationStatus.Untranslated || translation.Status == TranslationStatus.Dirty) {
                    isPending = true;
                }

                if (isPending) {
                    totalStringsToTranslate++;
                    languagesAffected.Add(lang);
                }
            }
        }

        foreach (var entry in targetEntries) {
            entry.Translations.Sort((a, b) =>
                SteamLanguageList.GetSortIndex(a.LanguageCode).CompareTo(SteamLanguageList.GetSortIndex(b.LanguageCode)));
        }

        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();

        var requests = BatchHandler.BuildRequests(database, database.TargetLanguages, out _, targetEntries);
        if (requests.Count > 0) {
            string tempDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp"));
            Directory.CreateDirectory(tempDir);
            string jsonlPath = Path.Combine(tempDir, "batch_preview.jsonl");

            string jsonlContent = string.Join("\n", requests.Select(r => Newtonsoft.Json.JsonConvert.SerializeObject(r)));
            File.WriteAllText(jsonlPath, jsonlContent);
            Debug.Log($"<b>[Localization] Batch Preview Saved</b>: {jsonlPath}");
        }

        Debug.Log($"<b>[Localization] Translation Batch Prepared</b>\n" +
                  $"- Total Strings to Translate: {totalStringsToTranslate}\n" +
                  $"- New Strings: {newEntries}\n" +
                  $"- Dirty (Modified) Strings: {dirtyEntries}\n" +
                  $"- Languages Affected: {string.Join(", ", languagesAffected)}\n" +
                  $"- Estimated Batch Size: ~{(totalStringsToTranslate * 50)} tokens (rough estimate)");
    }

    public static void ResetPendingStatuses(LocalizationMasterDatabase database, IEnumerable<LocalizationEntry> entriesToProcess = null) {
        if (database == null) return;

        var targetEntries = entriesToProcess ?? database.AllEntries;
        int resetCount = 0;
        foreach (var entry in targetEntries) {
            string currentHash = GenerateHash(entry.EnglishText, entry.Context);
            foreach (var lang in database.TargetLanguages) {
                var translation = entry.GetTranslation(lang);
                if (translation != null && translation.Status == TranslationStatus.OutForBatch) {
                    if (!string.IsNullOrEmpty(translation.SourceHash) && translation.SourceHash != currentHash) {
                        translation.Status = TranslationStatus.Dirty;
                    } else {
                        translation.Status = TranslationStatus.Untranslated;
                    }
                    translation.BatchID = string.Empty;
                    resetCount++;
                }
            }
        }

        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Localization] Reset {resetCount} stuck entries back to Untranslated/Dirty.");
    }

    public static void ClearBatchIDs(LocalizationMasterDatabase database, IEnumerable<LocalizationEntry> entriesToProcess = null) {
        if (database == null) return;
        var targetEntries = entriesToProcess ?? database.AllEntries;
        foreach (var entry in targetEntries) {
            foreach (var lang in database.TargetLanguages) {
                var translation = entry.GetTranslation(lang);
                if (translation != null) translation.BatchID = string.Empty;
            }
        }

        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Localization] Cleared All Batch IDs.");
    }

    public static void ExportLanguageCSV(LocalizationMasterDatabase database, string langCode, IEnumerable<LocalizationEntry> entriesToProcess = null) {
        string path = Path.Combine(Application.dataPath, $"{langCode}.csv");
        var rows = new List<List<string>> { new List<string> { "key", "english", langCode } };
        var targetEntries = entriesToProcess ?? database.AllEntries;
        foreach (var entry in targetEntries) {
            var translation = entry.GetTranslation(langCode);
            rows.Add(new List<string> {
                entry.Key ?? "",
                entry.EnglishText ?? "",
                translation?.TranslatedText ?? ""
            });
        }
        File.WriteAllText(path, CSVParser.WriteToString(rows), Encoding.UTF8);
        AssetDatabase.Refresh();
        Debug.Log($"[Localization] Exported {langCode} to {path}");
    }

    public static void ImportLanguageCSV(LocalizationMasterDatabase database, string langCode, string csvPath) {
        string csvContent = File.ReadAllText(csvPath);
        var grid = CSVParser.LoadFromString(csvContent);
        if (grid == null || grid.Count < 2) {
            Debug.LogWarning("[Localization] CSV is empty or invalid.");
            return;
        }
        int imported = 0;
        foreach (var row in grid.Skip(1)) {
            if (row.Count < 1 || string.IsNullOrWhiteSpace(row[0])) continue;
            string key = row[0];
            string translatedText = row.Count >= 3 ? row[2] : "";
            var entry = database.FindEntry(key);
            if (entry == null) continue;
            bool hasText = !string.IsNullOrWhiteSpace(translatedText);
            var translation = entry.GetTranslation(langCode);
            if (translation == null) {
                translation = new LanguageTranslation { LanguageCode = langCode };
                entry.Translations.Add(translation);
            }
            translation.TranslatedText = hasText ? translatedText : "";
            translation.Status = hasText ? TranslationStatus.Translated : TranslationStatus.Untranslated;
            translation.SourceHash = hasText ? GenerateHash(entry.EnglishText, entry.Context) : "";
            translation.BatchID = "";
            imported++;
        }
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Localization] Imported {imported} translations for {langCode}.");
    }

    [MenuItem("Tools/Localization/Bake Language Assets")]
    public static void BakeAssetsFromMenu() {
        var db = Resources.Load<LocalizationMasterDatabase>("LocalizationMasterDatabase");
        if (db != null) {
            BakeAssets(db);
        } else {
            Debug.LogError("[Localization] Could not find LocalizationMasterDatabase in Resources.");
        }
    }

    public static void BakeAssets(LocalizationMasterDatabase database) {
        if (database == null) return;

        string folderPath = "Assets/Resources/Languages";
        if (!AssetDatabase.IsValidFolder(folderPath)) {
            string[] folders = folderPath.Split('/');
            string currentPath = folders[0];
            for (int i = 1; i < folders.Length; i++) {
                string nextPath = currentPath + "/" + folders[i];
                if (!AssetDatabase.IsValidFolder(nextPath)) {
                    AssetDatabase.CreateFolder(currentPath, folders[i]);
                }
                currentPath = nextPath;
            }
        }

        var languagesToBake = new HashSet<string>(database.TargetLanguages, System.StringComparer.OrdinalIgnoreCase);
        languagesToBake.Add(Localizer.DEFAULT_LANGUAGE);

        foreach (var lang in languagesToBake) {
            string assetPath = $"{folderPath}/{lang}.asset";
            LanguageData data = AssetDatabase.LoadAssetAtPath<LanguageData>(assetPath);

            bool isNew = false;
            if (data == null) {
                data = ScriptableObject.CreateInstance<LanguageData>();
                isNew = true;
            }

            data.LanguageCode = lang;

            bool isEnglish = lang.Equals(Localizer.DEFAULT_LANGUAGE, System.StringComparison.OrdinalIgnoreCase);

            var bakedData = database.AllEntries
                .Select(e => {
                    if (isEnglish) {
                        return new { e.Key, Text = e.EnglishText, ShouldInclude = !string.IsNullOrEmpty(e.EnglishText) };
                    } else {
                        var translation = e.GetTranslation(lang);
                        return new {
                            e.Key,
                            Text = translation?.TranslatedText,
                            ShouldInclude = translation != null && (translation.Status == TranslationStatus.Translated || translation.Status == TranslationStatus.NativeApproved)
                        };
                    }
                })
                .Where(x => x.ShouldInclude)
                .ToList();

            data.Keys = bakedData.Select(x => x.Key).ToArray();
            data.Values = bakedData.Select(x => x.Text).ToArray();

            if (isNew) {
                AssetDatabase.CreateAsset(data, assetPath);
            } else {
                EditorUtility.SetDirty(data);
            }
        }

        string[] sortedCodes = languagesToBake.OrderBy(l => SteamLanguageList.GetSortIndex(l)).ToArray();
        string listAssetPath = "Assets/Resources/" + Localizer.LANGUAGE_LIST_KEY + ".asset";
        LanguageListAsset listAsset = AssetDatabase.LoadAssetAtPath<LanguageListAsset>(listAssetPath);
        if (listAsset == null) {
            listAsset = ScriptableObject.CreateInstance<LanguageListAsset>();
            listAsset.LanguageCodes = sortedCodes;
            AssetDatabase.CreateAsset(listAsset, listAssetPath);
        } else {
            listAsset.LanguageCodes = sortedCodes;
            EditorUtility.SetDirty(listAsset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Localization] Bake complete (including English)!");
    }
}
