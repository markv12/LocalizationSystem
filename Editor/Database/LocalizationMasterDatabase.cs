using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum ReasoningEffort {
    Low = 0,
    Medium = 1,
    High = 2,
    None = 3,
    XHigh = 4,
    Max = 5,
}

public enum TranslationStatus {
    Untranslated,
    OutForBatch,
    Translated,
    Dirty,
    NativeApproved,
}

[Serializable]
public class LanguageTranslation : ISerializationCallbackReceiver {
    public string LanguageCode;
    public string TranslatedText;
    public TranslationStatus Status;
    [HideInInspector] public string SourceHash;
    // One serialized field, populated only while a translation is out for a batch.
    [SerializeField, HideInInspector]
    private string BatchTracking;
    private static readonly string[] EmptyTracking = { "", "", "" };

    public string BatchID => ReadTracking()[0];
    public string BatchRequestId => ReadTracking()[1];
    public string BatchSourceHash => ReadTracking()[2];

    public bool MatchesBatchRequest(string batchId, string requestId, string sourceHash) {
        if (Status != TranslationStatus.OutForBatch) return false;
        var parts = ReadTracking();
        return parts[0].Length > 0 && parts[0] == batchId && parts[1] == requestId && parts[2] == sourceHash;
    }

    public void SetBatchTracking(string batchId, string requestId, string sourceHash) {
        if (string.IsNullOrEmpty(batchId)) throw new ArgumentException("Batch ID is required.");
        if (string.IsNullOrEmpty(requestId)) throw new ArgumentException("Batch request ID is required.");
        BatchTracking = PackBatchTracking(batchId, requestId, sourceHash);
    }

    private static string PackBatchTracking(string batchId, string requestId, string sourceHash) {
        string[] parts = { batchId ?? "", requestId ?? "", sourceHash ?? "" };
        return "v1:" + string.Concat(parts.Select(p => p.Length.ToString(CultureInfo.InvariantCulture) + ":" + p));
    }

    public void ClearBatchTracking() { BatchTracking = string.Empty; }

    public bool ResetBatchTracking() {
        bool wasPending = Status == TranslationStatus.OutForBatch;
        if (wasPending)
            Status = string.IsNullOrEmpty(TranslatedText) ? TranslationStatus.Untranslated : TranslationStatus.Dirty;
        ClearBatchTracking();
        return wasPending;
    }

    private string[] ReadTracking() {
        if (string.IsNullOrEmpty(BatchTracking)) return EmptyTracking;
        if (!BatchTracking.StartsWith("v1:", StringComparison.Ordinal)) return EmptyTracking;
        var parts = new string[3];
        int offset = 3;
        for (int i = 0; i < parts.Length; i++) {
            int separator = BatchTracking.IndexOf(':', offset);
            if (separator < offset || !int.TryParse(BatchTracking.Substring(offset, separator - offset),
                NumberStyles.None, CultureInfo.InvariantCulture, out int length) || length > BatchTracking.Length - separator - 1)
                return EmptyTracking;
            offset = separator + 1;
            parts[i] = BatchTracking.Substring(offset, length);
            offset += length;
        }
        return offset == BatchTracking.Length && !string.IsNullOrEmpty(parts[0]) && !string.IsNullOrEmpty(parts[1])
            ? parts : EmptyTracking;
    }

    public void OnBeforeSerialize() { NormalizeBatchTracking(); }
    public void OnAfterDeserialize() { NormalizeBatchTracking(); }

    private void NormalizeBatchTracking() {
        if (Status != TranslationStatus.OutForBatch) {
            ClearBatchTracking();
        } else if (string.IsNullOrEmpty(BatchID)) {
            // Missing/corrupt tracking cannot retrieve a result; make the entry retryable.
            ResetBatchTracking();
        }
    }
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

[Serializable]
public sealed class SteamTranslationSettings {
    public string GameTitle = "";
    [TextArea(3, 10)] public string GameContext = "";
    [Tooltip("One name per line. These names and the game title are preserved exactly.")]
    [TextArea(2, 6)] public string ProtectedNames = "";
    [OpenAIModelList] public string Model = "gpt-6.1-sol";
    public ReasoningEffort Effort = ReasoningEffort.Medium;
    public int OutputTokenBudget = OpenAITranslationModels.MaxCompletionTokens;
    [LanguageList] public List<string> TargetLanguages = SteamLanguageList.All
        .Where(l => l.langCode != "english").Select(l => l.langCode).ToList();
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

    [SerializeField, HideInInspector]
    private List<LocalizationEntry> DialogueEntries;

    [LanguageList]
    public List<string> TargetLanguages = new List<string>();

    [OpenAIModelList]
    public string OpenAIModel = OpenAITranslationModels.DefaultModel;

    public ReasoningEffort Effort = ReasoningEffort.Low;
    [Tooltip("Output token budget per chunk, including reasoning tokens. Increase if responses exhaust the budget.")]
    public int MaxCompletionTokens = OpenAITranslationModels.MaxCompletionTokens;

    [Tooltip("Shared settings for Steam store page and event translations.")]
    public SteamTranslationSettings SteamTranslation = new SteamTranslationSettings();

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize() {
        if (SteamTranslation == null) SteamTranslation = new SteamTranslationSettings();
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

        if (DialogueEntries != null && DialogueEntries.Count > 0) {
            var cat = AdditionalCategories.Find(c => c.CategoryName.Equals("Dialogue", StringComparison.OrdinalIgnoreCase));
            if (cat == null) {
                cat = new LocalizationCategory("Dialogue");
                AdditionalCategories.Add(cat);
            }
            if (cat.Entries == null || cat.Entries.Count == 0) {
                cat.Entries = new List<LocalizationEntry>(DialogueEntries);
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

#if UNITY_EDITOR
    private static LocalizationMasterDatabase _cachedInstance;

    /// <summary>
    /// Finds and loads the LocalizationMasterDatabase asset anywhere in the project.
    /// Prioritizes an asset named "LocalizationMasterDatabase" if multiple instances exist.
    /// </summary>
    public static LocalizationMasterDatabase LoadDatabase(bool forceReload = false) {
        if (!forceReload && _cachedInstance != null) return _cachedInstance;

        string[] guids = AssetDatabase.FindAssets("t:LocalizationMasterDatabase");
        if (guids == null || guids.Length == 0) {
            Debug.LogError("[Localization] No LocalizationMasterDatabase found in the project. Create one via Assets -> Create -> Localization -> Master Database.");
            return null;
        }

        string selectedPath = null;
        if (guids.Length == 1) {
            selectedPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        } else {
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path).Equals("LocalizationMasterDatabase", StringComparison.OrdinalIgnoreCase)) {
                    selectedPath = path;
                    break;
                }
            }
            if (string.IsNullOrEmpty(selectedPath)) {
                selectedPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            }
            Debug.LogWarning($"[Localization] Multiple LocalizationMasterDatabase assets found ({guids.Length}). Using '{selectedPath}'.");
        }

        _cachedInstance = AssetDatabase.LoadAssetAtPath<LocalizationMasterDatabase>(selectedPath);
        return _cachedInstance;
    }

    /// <summary>
    /// Clears the cached database instance reference.
    /// </summary>
    public static void ClearCachedDatabase() {
        _cachedInstance = null;
    }
#endif
}
