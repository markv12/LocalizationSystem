using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(fileName = "FontLocalizationSettings", menuName = "Localization/Font Localization Settings")]
public class FontLocalizationSettings : ScriptableObject {
    [FormerlySerializedAs("latinBasic")]
    public FontLocalizationSetting english;
    public FontLocalizationSetting other;

    [Tooltip("Enable if your project distinguishes Latin Basic from Latin Extended languages with diacritics.")]
    public bool useLatinExtendedSplit = false;
    public FontLocalizationSetting latinExtended;

    private static FontLocalizationSettings instance;
    public static FontLocalizationSettings Instance {
        get {
            if (instance == null) {
                instance = Resources.Load<FontLocalizationSettings>("FontLocalizationSettings");
            }
            return instance;
        }
    }

    [Serializable]
    public struct FontLocalizationSetting {
        [FormerlySerializedAs("regularFont")]
        [FormerlySerializedAs("fontAsset")]
        public TMP_FontAsset FontAsset;

        public Material regularPreset;
        public Material blackOutlinePreset;
        public Material whiteOutlinePreset;

        [FormerlySerializedAs("regularUIToolkitFont")]
        public Font uiToolkitFont;

        public TMP_FontAsset titleFont;
        public Font titleUIToolkitFont;
        public TMP_FontAsset paragraphFont;
        public Font paragraphUIToolkitFont;
    }

    public TMP_FontAsset LangFont(string sl, FontLocalizer.Type type = FontLocalizer.Type.Regular) {
        FontLocalizationSetting fls = SettingForLanguage(sl);
        switch (type) {
            case FontLocalizer.Type.Title:
                return fls.titleFont != null ? fls.titleFont : fls.FontAsset;
            case FontLocalizer.Type.Paragraph:
                return fls.paragraphFont != null ? fls.paragraphFont : fls.FontAsset;
            default:
                return fls.FontAsset;
        }
    }

    public Material LanguageFontMaterial(string sl, FontLocalizer.Type type) {
        FontLocalizationSetting fls = SettingForLanguage(sl);
        switch (type) {
            case FontLocalizer.Type.BlackOutline:
                return fls.blackOutlinePreset;
            case FontLocalizer.Type.WhiteOutline:
                return fls.whiteOutlinePreset;
            case FontLocalizer.Type.Regular:
            default:
                return fls.regularPreset;
        }
    }

    public Font LangUIToolkitFont(string sl, FontLocalizer.Type type = FontLocalizer.Type.Regular) {
        FontLocalizationSetting fls = SettingForLanguage(sl);
        switch (type) {
            case FontLocalizer.Type.Title:
                if (fls.titleUIToolkitFont != null) return fls.titleUIToolkitFont;
                return fls.titleFont != null ? fls.titleFont.sourceFontFile : (fls.uiToolkitFont != null ? fls.uiToolkitFont : fls.FontAsset?.sourceFontFile);
            case FontLocalizer.Type.Paragraph:
                if (fls.paragraphUIToolkitFont != null) return fls.paragraphUIToolkitFont;
                return fls.paragraphFont != null ? fls.paragraphFont.sourceFontFile : (fls.uiToolkitFont != null ? fls.uiToolkitFont : fls.FontAsset?.sourceFontFile);
            default:
                return fls.uiToolkitFont != null ? fls.uiToolkitFont : fls.FontAsset?.sourceFontFile;
        }
    }

    public FontLocalizationSetting SettingForLanguage(string sl) {
        if (string.IsNullOrEmpty(sl)) sl = Localizer.DEFAULT_LANGUAGE;

        if (useLatinExtendedSplit) {
            if (SteamLanguageList.IsLatinBasic(sl)) {
                return english;
            }
            if (latinExtended.FontAsset != null && IsLatinExtended(sl)) {
                return latinExtended;
            }
            return other;
        }

        return sl.Equals(Localizer.DEFAULT_LANGUAGE, StringComparison.OrdinalIgnoreCase) ? english : other;
    }

    private static bool IsLatinExtended(string sl) {
        return sl.Equals("czech", StringComparison.OrdinalIgnoreCase)
            || sl.Equals("polish", StringComparison.OrdinalIgnoreCase)
            || sl.Equals("romanian", StringComparison.OrdinalIgnoreCase)
            || sl.Equals("hungarian", StringComparison.OrdinalIgnoreCase)
            || sl.Equals("vietnamese", StringComparison.OrdinalIgnoreCase);
    }
}
