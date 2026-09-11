using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public static partial class Localizer {
    public const string DEFAULT_LANGUAGE = "english";
    public const string LANGUAGE_LIST_KEY = "LanguageList";
    public const string LANGUAGE_PREF_KEY = "SelectedLanguage";

    public static event Action LanguageChangedEvent;

    public static Func<string> GetSavedLanguage = () => PlayerPrefs.GetString(LANGUAGE_PREF_KEY, DEFAULT_LANGUAGE);
    public static Action<string> SetSavedLanguage = (lang) => {
        PlayerPrefs.SetString(LANGUAGE_PREF_KEY, lang);
        PlayerPrefs.Save();
    };

    private static LanguageData currentLanguageAsset;
    private static Dictionary<string, string> englishLanguageDictionary;
    private static Dictionary<string, string> currentLanguageDictionary;
    public static string currentLanguage;
    private static List<string> languageList;
    public static bool languageLoaded = false;

    public static List<string> LanguageList {
        get {
            if (languageList == null) {
                PopulateLanguageList();
            }
            return languageList;
        }
    }

    private static void PopulateLanguageList() {
        LanguageListAsset listAsset = Resources.Load<LanguageListAsset>(LANGUAGE_LIST_KEY);
        if (listAsset != null && listAsset.LanguageCodes != null && listAsset.LanguageCodes.Length > 0) {
            languageList = new List<string>(listAsset.LanguageCodes);
        } else {
            languageList = new List<string>();
        }

        if (!languageList.Contains(DEFAULT_LANGUAGE)) {
            languageList.Add(DEFAULT_LANGUAGE);
        }
    }

    public static void EnsureLoaded() {
        if (!languageLoaded) {
            string saved = GetSavedLanguage != null ? GetSavedLanguage() : PlayerPrefs.GetString(LANGUAGE_PREF_KEY, DEFAULT_LANGUAGE);
            LoadLanguageInternal(string.IsNullOrEmpty(saved) ? DEFAULT_LANGUAGE : saved);
        }
    }

    public static void LoadLanguage(string sl) {
        if (string.IsNullOrEmpty(sl)) sl = DEFAULT_LANGUAGE;

        if (sl != currentLanguage || !languageLoaded) {
            if (SetSavedLanguage != null) {
                SetSavedLanguage(sl);
            } else {
                PlayerPrefs.SetString(LANGUAGE_PREF_KEY, sl);
                PlayerPrefs.Save();
            }
            LoadLanguageInternal(sl);
        }
    }

    private static void LoadLanguageInternal(string sl) {
        if (currentLanguageAsset != null) {
            Resources.UnloadAsset(currentLanguageAsset);
        }

        currentLanguage = sl;
        currentLanguageAsset = Resources.Load<LanguageData>("Languages/" + sl);

        if (currentLanguageAsset != null) {
            Dictionary<string, string> langDict = currentLanguageAsset.ToDictionary();
            currentLanguageDictionary = langDict;
            if (currentLanguage.Equals(DEFAULT_LANGUAGE, StringComparison.OrdinalIgnoreCase)) {
                englishLanguageDictionary = langDict;
            }
        } else {
            Debug.LogWarning("[Localizer] Language asset not found for: " + sl + ". Using empty dictionary.");
            currentLanguageDictionary = new Dictionary<string, string>();
        }

        languageLoaded = true;
        LanguageChangedEvent?.Invoke();
    }

    public static string GetText(string key) {
        if (!TryGetText(key, out string result)) {
            Debug.LogError($"[Localizer] Key not found in language dictionary: '{key}' (Language: {currentLanguage})");
            return key;
        }
        return result;
    }

    public static bool TryGetText(string key, out string result) {
        if (!languageLoaded) {
            EnsureLoaded();
        }
        return TryGetText(currentLanguageDictionary, key, out result);
    }

    private static bool TryGetText(Dictionary<string, string> languageDict, string key, out string result) {
        if (languageDict != null && key != null && languageDict.TryGetValue(key, out result)) {
            return true;
        } else {
            result = "";
            return false;
        }
    }

    public static bool TryGetEnglishText(string key, out string result) {
        if (englishLanguageDictionary == null) {
            LanguageData asset = Resources.Load<LanguageData>("Languages/" + DEFAULT_LANGUAGE);
            englishLanguageDictionary = asset != null ? asset.ToDictionary() : new Dictionary<string, string>();
        }
        return TryGetText(englishLanguageDictionary, key, out result);
    }

    public static string GetTextOrEnglish(string key) {
        if (TryGetText(key, out string result)) return result;
        if (TryGetEnglishText(key, out string english)) return english;
        return key;
    }

    public static string Format(string key, params object[] args) {
        string pattern = GetTextOrEnglish(key);
        if (args == null) args = Array.Empty<object>();
        try {
            return string.Format(pattern, args);
        } catch (FormatException) {
            Debug.LogError($"[Localizer] Malformed format placeholders in '{currentLanguage}' translation of key: {key}");
            return pattern;
        }
    }

    public static Dictionary<string, string> GetCurrentLanguage() {
        EnsureLoaded();
        return currentLanguageDictionary;
    }

    public static bool IsRTL => SteamLanguageList.IsRTL(currentLanguage ?? DEFAULT_LANGUAGE);

    public static TMP_FontAsset CurrentLangFont => 
        FontLocalizationSettings.Instance != null ? FontLocalizationSettings.Instance.LangFont(currentLanguage) : null;

    public static TMP_FontAsset CurrentLangFontFor(FontLocalizer.Type type) => 
        FontLocalizationSettings.Instance != null ? FontLocalizationSettings.Instance.LangFont(currentLanguage, type) : null;

    public static Material CurrentLangFontMaterial(FontLocalizer.Type type) => 
        FontLocalizationSettings.Instance != null ? FontLocalizationSettings.Instance.LanguageFontMaterial(currentLanguage, type) : null;

    public static Material CurrentLangFontMaterial(FontLocalizer.OutlineStyle outline, FontLocalizer.Type type = FontLocalizer.Type.Regular) => 
        FontLocalizationSettings.Instance != null ? FontLocalizationSettings.Instance.LanguageFontMaterial(currentLanguage, outline, type) : null;

    public static Font CurrentLangUIToolkitFont(FontLocalizer.Type type = FontLocalizer.Type.Regular) => 
        FontLocalizationSettings.Instance != null ? FontLocalizationSettings.Instance.LangUIToolkitFont(currentLanguage, type) : null;
}

