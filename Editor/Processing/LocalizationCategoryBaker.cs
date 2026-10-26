using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Category membership is shared by all baked languages.
[InitializeOnLoad]
public class LocalizationCategoryBaker : AssetPostprocessor, IPreprocessBuildWithReport {
    private static bool pending;
    public int callbackOrder => -100;

    static LocalizationCategoryBaker() => ScheduleBake();

    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) {
        if (imported.Concat(moved).Any(p => AssetDatabase.GetMainAssetTypeAtPath(p) == typeof(LocalizationMasterDatabase))) {
            LocalizationMasterDatabase.ClearCachedDatabase();
            ScheduleBake();
        }
    }

    private static void ScheduleBake() {
        if (pending) return;
        pending = true;
        EditorApplication.delayCall += () => {
            pending = false;
            if (!EditorApplication.isPlayingOrWillChangePlaymode) BakeIfDatabaseExists();
        };
    }

    private static void BakeIfDatabaseExists() {
        if (AssetDatabase.FindAssets("t:LocalizationMasterDatabase").Length > 0) Bake();
    }

    public static void Bake() {
        Bake(LocalizationMasterDatabase.LoadDatabase());
    }

    public static void Bake(LocalizationMasterDatabase database) {
        LocalizationProcessor.BakeAssets(database);
    }

    internal static void WriteCatalogue(string json) {
        const string path = "Assets/Resources/" + Localizer.CATEGORY_RESOURCE_NAME + ".json";
        if (File.Exists(path) && File.ReadAllText(path) == json) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, json, new System.Text.UTF8Encoding(false));
        AssetDatabase.ImportAsset(path);
        Localizer.ClearCategoryCache();
    }

    public static string CreateCatalogue(LocalizationMasterDatabase database) {
        var allKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in database.AllEntries) {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Key) || !allKeys.Add(entry.Key)) {
                throw new BuildFailedException("Localization keys must be nonempty and globally unique before baking categories.");
            }
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<Localizer.CategoryKeys>();
        foreach (var category in database.AdditionalCategories ?? Enumerable.Empty<LocalizationCategory>()) {
            if (category == null || string.IsNullOrWhiteSpace(category.CategoryName) || !names.Add(category.CategoryName)) {
                throw new BuildFailedException("Localization category names must be nonempty and unique.");
            }
            groups.Add(new Localizer.CategoryKeys {
                Name = category.CategoryName,
                Keys = (category.Entries ?? Enumerable.Empty<LocalizationEntry>())
                    .Where(LocalizationProcessor.HasEnglishText).Select(e => e.Key).ToArray()
            });
        }
        return JsonUtility.ToJson(new Localizer.CategoryCatalogue { Categories = groups.ToArray() });
    }

    public void OnPreprocessBuild(BuildReport report) => BakeIfDatabaseExists();
}
