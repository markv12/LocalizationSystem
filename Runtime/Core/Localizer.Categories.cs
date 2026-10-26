using System;
using System.Collections.Generic;
using UnityEngine;

// Category membership is baked once, separately from the per-language text assets.
public static partial class Localizer {
    public const string CATEGORY_RESOURCE_NAME = "LocalizationCategories";

    [Serializable]
    public class CategoryCatalogue {
        public CategoryKeys[] Categories = Array.Empty<CategoryKeys>();
    }

    [Serializable]
    public class CategoryKeys {
        public string Name;
        public string[] Keys = Array.Empty<string>();
    }

    private static Dictionary<string, IReadOnlyList<string>> categoryKeys;

    public static void ClearCategoryCache() => categoryKeys = null;

    public static IReadOnlyList<string> GetCategoryKeys(string category) {
        if (categoryKeys == null) {
            categoryKeys = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            TextAsset asset = Resources.Load<TextAsset>(CATEGORY_RESOURCE_NAME);
            CategoryCatalogue catalogue = asset != null ? JsonUtility.FromJson<CategoryCatalogue>(asset.text) : null;
            if (catalogue?.Categories != null) {
                foreach (CategoryKeys group in catalogue.Categories) {
                    if (group != null && !string.IsNullOrWhiteSpace(group.Name)) {
                        categoryKeys[group.Name] = Array.AsReadOnly(group.Keys ?? Array.Empty<string>());
                    }
                }
            }
        }
        return category != null && categoryKeys.TryGetValue(category, out var keys) ? keys : Array.Empty<string>();
    }

    public static bool TryGetRandomKey(string category, out string key) {
        IReadOnlyList<string> keys = GetCategoryKeys(category);
        key = keys.Count > 0 ? keys[UnityEngine.Random.Range(0, keys.Count)] : null;
        return key != null;
    }

    public static bool TryGetRandomText(string category, out string text, params object[] args) {
        if (TryGetRandomKey(category, out string key)) {
            text = Localizer.Format(key, args);
            return true;
        }
        text = null;
        return false;
    }
}

