using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum ReasoningEffort {
    Low,
    Medium,
    High,
}

public enum TranslationStatus {
    Untranslated,
    OutForBatch,
    Translated,
    Dirty,
    NativeApproved,
}

[Serializable]
public class LanguageTranslation {
    public string LanguageCode;
    public string TranslatedText;
    public TranslationStatus Status;
    [HideInInspector] public string SourceHash;
    [HideInInspector] public string BatchID;
}

[Serializable]
public class LocalizationEntry {
    public string Key;
    [TextArea]
    public string EnglishText;
    public string Context;
    public List<LanguageTranslation> Translations = new List<LanguageTranslation>();

    public LanguageTranslation GetTranslation(string langCode) {
        return Translations.Find(t => t.LanguageCode.Equals(langCode, StringComparison.OrdinalIgnoreCase));
    }

    public void SetTranslation(string langCode, LanguageTranslation translation) {
        var found = Translations.Find(t => t.LanguageCode.Equals(langCode, StringComparison.OrdinalIgnoreCase));
        if (found != null) {
            int index = Translations.IndexOf(found);
            Translations[index] = translation;
        } else {
            Translations.Add(translation);
        }
    }
}

[Serializable]
public class LocalizationCategory {
    public string CategoryName;
    public List<LocalizationEntry> Entries = new List<LocalizationEntry>();

    public LocalizationCategory() { }

    public LocalizationCategory(string name) {
        CategoryName = name;
    }
}

[CreateAssetMenu(fileName = "LocalizationMasterDatabase", menuName = "Localization/Master Database")]
public class LocalizationMasterDatabase : ScriptableObject, ISerializationCallbackReceiver {
    public string GameGenre;
    public string GameTone;
    [TextArea(3, 10)]
    public string GlobalContext;

    public List<LocalizationEntry> Entries = new List<LocalizationEntry>();

    [Tooltip("Additional entry collections for external systems (e.g. Items, Dialogue, Instruments, Scales)")]
    public List<LocalizationCategory> AdditionalCategories = new List<LocalizationCategory>();

    // Backward compatibility for legacy assets that serialized direct lists
    [SerializeField, HideInInspector]
    private List<LocalizationEntry> InstrumentEntries;

    [SerializeField, HideInInspector]
    private List<LocalizationEntry> ScaleEntries;

    [SerializeField, HideInInspector]
    private List<LocalizationEntry> ItemEntries;

    [LanguageList]
    public List<string> TargetLanguages = new List<string>();

    [OpenAIModelList]
    public string OpenAIModel = "gpt-5.6-luna";

    public ReasoningEffort Effort = ReasoningEffort.Low;

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize() {
        if (AdditionalCategories == null) AdditionalCategories = new List<LocalizationCategory>();

        if (InstrumentEntries != null && InstrumentEntries.Count > 0) {
            var cat = AdditionalCategories.Find(c => c.CategoryName.Equals("Instruments", StringComparison.OrdinalIgnoreCase));
            if (cat == null) {
                cat = new LocalizationCategory("Instruments");
                AdditionalCategories.Add(cat);
            }
            if (cat.Entries == null || cat.Entries.Count == 0) {
                cat.Entries = new List<LocalizationEntry>(InstrumentEntries);
            }
        }

        if (ScaleEntries != null && ScaleEntries.Count > 0) {
            var cat = AdditionalCategories.Find(c => c.CategoryName.Equals("Scales", StringComparison.OrdinalIgnoreCase));
            if (cat == null) {
                cat = new LocalizationCategory("Scales");
                AdditionalCategories.Add(cat);
            }
            if (cat.Entries == null || cat.Entries.Count == 0) {
                cat.Entries = new List<LocalizationEntry>(ScaleEntries);
            }
        }

        if (ItemEntries != null && ItemEntries.Count > 0) {
            var cat = AdditionalCategories.Find(c => c.CategoryName.Equals("Items", StringComparison.OrdinalIgnoreCase));
            if (cat == null) {
                cat = new LocalizationCategory("Items");
                AdditionalCategories.Add(cat);
            }
            if (cat.Entries == null || cat.Entries.Count == 0) {
                cat.Entries = new List<LocalizationEntry>(ItemEntries);
            }
        }
    }

    public IEnumerable<LocalizationEntry> AllEntries {
        get {
            IEnumerable<LocalizationEntry> all = Entries;
            if (AdditionalCategories != null) {
                foreach (var cat in AdditionalCategories) {
                    if (cat?.Entries != null) {
                        all = all.Concat(cat.Entries);
                    }
                }
            }
            return all;
        }
    }

    public LocalizationEntry FindEntry(string key) {
        if (string.IsNullOrEmpty(key)) return null;
        var found = Entries.Find(e => e.Key == key);
        if (found != null) return found;

        if (AdditionalCategories != null) {
            foreach (var cat in AdditionalCategories) {
                if (cat?.Entries != null) {
                    found = cat.Entries.Find(e => e.Key == key);
                    if (found != null) return found;
                }
            }
        }
        return null;
    }

    public List<LocalizationEntry> GetOrCreateCategory(string categoryName) {
        if (AdditionalCategories == null) AdditionalCategories = new List<LocalizationCategory>();
        var cat = AdditionalCategories.Find(c => c.CategoryName.Equals(categoryName, StringComparison.OrdinalIgnoreCase));
        if (cat == null) {
            cat = new LocalizationCategory(categoryName);
            AdditionalCategories.Add(cat);
        }
        return cat.Entries;
    }

    public void AddAllLanguages() {
        foreach (var lang in SteamLanguageList.All) {
            if (lang.langCode.Equals(Localizer.DEFAULT_LANGUAGE, StringComparison.OrdinalIgnoreCase)) continue;
            if (!TargetLanguages.Exists(code => code.Equals(lang.langCode, StringComparison.OrdinalIgnoreCase)))
                TargetLanguages.Add(lang.langCode);
        }
        TargetLanguages.Sort((a, b) =>
            SteamLanguageList.GetSortIndex(a).CompareTo(SteamLanguageList.GetSortIndex(b)));
    }
}
