using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using TextCoreFontAsset = UnityEngine.TextCore.Text.FontAsset;

[CreateAssetMenu(fileName = "FontLocalizationSettings", menuName = "Localization/Font Localization Settings")]
public class FontLocalizationSettings : ScriptableObject {
    [FormerlySerializedAs("latinBasic")]
    public FontLocalizationSetting english;
    [FormerlySerializedAs("latinExtended")]
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

        [FormerlySerializedAs("regularUIToolkitFont")]
        public TextCoreFontAsset uiToolkitFont;

        public TMP_FontAsset titleFont;
        public TextCoreFontAsset titleUIToolkitFont;
        public TMP_FontAsset paragraphFont;
        public TextCoreFontAsset paragraphUIToolkitFont;
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
        TMP_FontAsset font = LangFont(sl, type);

        switch (type) {
            case FontLocalizer.Type.BlackOutline:
                return LocalizationMaterialManager.GetBlackOutline(font);
            case FontLocalizer.Type.WhiteOutline:
                return LocalizationMaterialManager.GetWhiteOutline(font);
            case FontLocalizer.Type.Regular:
            default:
                return font?.material;
        }
    }

    public Material LanguageFontMaterial(string sl, FontLocalizer.OutlineStyle outline, FontLocalizer.Type type = FontLocalizer.Type.Regular) {
        TMP_FontAsset font = LangFont(sl, type);

        switch (outline) {
            case FontLocalizer.OutlineStyle.Black:
                return LocalizationMaterialManager.GetBlackOutline(font);
            case FontLocalizer.OutlineStyle.White:
                return LocalizationMaterialManager.GetWhiteOutline(font);
            case FontLocalizer.OutlineStyle.None:
            default:
                return font?.material;
        }
    }

    public TextCoreFontAsset LangUIToolkitFont(string sl, FontLocalizer.Type type = FontLocalizer.Type.Regular) {
        FontLocalizationSetting fls = SettingForLanguage(sl);
        switch (type) {
            case FontLocalizer.Type.Title:
                return fls.titleUIToolkitFont != null ? fls.titleUIToolkitFont : fls.uiToolkitFont;
            case FontLocalizer.Type.Paragraph:
                return fls.paragraphUIToolkitFont != null ? fls.paragraphUIToolkitFont : fls.uiToolkitFont;
            default:
                return fls.uiToolkitFont;
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

        if (SteamLanguageList.IsLatinBasic(sl)) {
            return english;
        }

        return other.FontAsset != null ? other : latinExtended;
    }

    private static bool IsLatinExtended(string sl) {
        return sl.Equals("czech", StringComparison.OrdinalIgnoreCase)
            || sl.Equals("polish", StringComparison.OrdinalIgnoreCase)
            || sl.Equals("romanian", StringComparison.OrdinalIgnoreCase)
            || sl.Equals("hungarian", StringComparison.OrdinalIgnoreCase)
            || sl.Equals("vietnamese", StringComparison.OrdinalIgnoreCase);
    }
}


