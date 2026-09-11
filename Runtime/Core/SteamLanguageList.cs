using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public readonly struct SteamLanguage {
    public readonly string displayName;
    public readonly string langCode;

    public SteamLanguage(string displayName, string langCode) {
        this.displayName = displayName;
        this.langCode = langCode;
    }

    public void Deconstruct(out string displayName, out string langCode) {
        displayName = this.displayName;
        langCode = this.langCode;
    }
}

public static class SteamLanguageList {
    private static readonly List<SteamLanguage> _languages = new List<SteamLanguage> {
        // 1. Baseline / Fallback (Pinned to top)
        new SteamLanguage("English", "english"),

        // 2. Latin Script (Alphabetical A-Z)
        new SteamLanguage("Bahasa Indonesia", "indonesian"),
        new SteamLanguage("Bahasa Melayu", "malay"),
        new SteamLanguage("Čeština", "czech"),
        new SteamLanguage("Dansk", "danish"),
        new SteamLanguage("Deutsch", "german"),
        new SteamLanguage("Español (España)", "spanish"),
        new SteamLanguage("Español (Latinoamérica)", "latam"),
        new SteamLanguage("Français", "french"),
        new SteamLanguage("Italiano", "italian"),
        new SteamLanguage("Magyar", "hungarian"),
        new SteamLanguage("Nederlands", "dutch"),
        new SteamLanguage("Norsk", "norwegian"),
        new SteamLanguage("Polski", "polish"),
        new SteamLanguage("Português (Brasil)", "brazilian"),
        new SteamLanguage("Português (Portugal)", "portuguese"),
        new SteamLanguage("Română", "romanian"),
        new SteamLanguage("Suomi", "finnish"),
        new SteamLanguage("Svenska", "swedish"),
        new SteamLanguage("Tiếng Việt", "vietnamese"),
        new SteamLanguage("Türkçe", "turkish"),

        // 3. Greek Script
        new SteamLanguage("Ελληνικά", "greek"),

        // 4. Cyrillic Script
        new SteamLanguage("Български", "bulgarian"),
        new SteamLanguage("Русский", "russian"),
        new SteamLanguage("Українська", "ukrainian"),

        // 5. Arabic Script
        new SteamLanguage("العربية", "arabic"),

        // 6. Thai Script
        new SteamLanguage("ภาษาไทย", "thai"),

        // 7. CJK Scripts (Chinese, Japanese, Korean)
        new SteamLanguage("简体中文", "schinese"),
        new SteamLanguage("繁體中文", "tchinese"),
        new SteamLanguage("日本語", "japanese"),
        new SteamLanguage("한국어", "koreana")
    };

    public static IReadOnlyList<SteamLanguage> All => _languages;

    public static string GetDisplayName(string code) {
        return _languages.FirstOrDefault(l => l.langCode.Equals(code, StringComparison.OrdinalIgnoreCase)).displayName ?? code;
    }

    public static int GetSortIndex(string langCode) {
        for (int i = 0; i < _languages.Count; i++) {
            if (_languages[i].langCode.Equals(langCode, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return int.MaxValue;
    }

    public static bool IsRTL(string langCode) {
        return langCode.Equals("arabic", StringComparison.OrdinalIgnoreCase);
    }

    public static string FromSystemLanguage(SystemLanguage language) {
        switch (language) {
            case SystemLanguage.Indonesian: return "indonesian";
            case SystemLanguage.Czech: return "czech";
            case SystemLanguage.Danish: return "danish";
            case SystemLanguage.German: return "german";
            case SystemLanguage.Spanish: return "spanish";
            case SystemLanguage.French: return "french";
            case SystemLanguage.Italian: return "italian";
            case SystemLanguage.Hungarian: return "hungarian";
            case SystemLanguage.Dutch: return "dutch";
            case SystemLanguage.Norwegian: return "norwegian";
            case SystemLanguage.Polish: return "polish";
            case SystemLanguage.Portuguese: return "brazilian";
            case SystemLanguage.Romanian: return "romanian";
            case SystemLanguage.Finnish: return "finnish";
            case SystemLanguage.Swedish: return "swedish";
            case SystemLanguage.Vietnamese: return "vietnamese";
            case SystemLanguage.Turkish: return "turkish";
            case SystemLanguage.Greek: return "greek";
            case SystemLanguage.Bulgarian: return "bulgarian";
            case SystemLanguage.Russian: return "russian";
            case SystemLanguage.Ukrainian: return "ukrainian";
            case SystemLanguage.Arabic: return "arabic";
            case SystemLanguage.Thai: return "thai";
            case SystemLanguage.ChineseSimplified: return "schinese";
            case SystemLanguage.ChineseTraditional: return "tchinese";
            case SystemLanguage.Chinese: return "schinese";
            case SystemLanguage.Japanese: return "japanese";
            case SystemLanguage.Korean: return "koreana";
            default: return Localizer.DEFAULT_LANGUAGE;
        }
    }

    public static bool IsLatinBasic(string langCode) {
        if (string.IsNullOrWhiteSpace(langCode))
            return false;

        return latinBasicLanguages.Contains(langCode);
    }

    private static readonly HashSet<string> latinBasicLanguages = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "english",
        "french",
        "german",
        "italian",
        "spanish",
        "latam",
        "portuguese",
        "brazilian",
        "dutch",
        "danish",
        "norwegian",
        "swedish",
        "finnish",
        "turkish",
        "indonesian",
        "malay"
    };
}
