using System;
using System.Collections.Generic;
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

    private static FontLocalizationSettings instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState() {
        if (instance != null) instance._fontSupportCache.Clear();
        instance = null;
    }
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

        [Tooltip("Optional outline width for this font. Zero uses the shared default.")]
        [Range(0f, 1f)]
        public float outlineWidth;
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
        FontLocalizationSetting setting = SettingForLanguage(sl);
        TMP_FontAsset font = FontForSetting(setting, type);
        float outlineWidth = OutlineWidthFor(setting);

        switch (type) {
            case FontLocalizer.Type.BlackOutline:
                return LocalizationMaterialManager.GetBlackOutline(font, outlineWidth);
            case FontLocalizer.Type.WhiteOutline:
                return LocalizationMaterialManager.GetWhiteOutline(font, outlineWidth);
            case FontLocalizer.Type.Regular:
            default:
                return font?.material;
        }
    }

    public Material LanguageFontMaterial(string sl, FontLocalizer.OutlineStyle outline, FontLocalizer.Type type = FontLocalizer.Type.Regular) {
        FontLocalizationSetting setting = SettingForLanguage(sl);
        TMP_FontAsset font = FontForSetting(setting, type);
        float outlineWidth = OutlineWidthFor(setting);

        switch (outline) {
            case FontLocalizer.OutlineStyle.Black:
                return LocalizationMaterialManager.GetBlackOutline(font, outlineWidth);
            case FontLocalizer.OutlineStyle.White:
                return LocalizationMaterialManager.GetWhiteOutline(font, outlineWidth);
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

    private static TMP_FontAsset FontForSetting(FontLocalizationSetting setting, FontLocalizer.Type type) {
        switch (type) {
            case FontLocalizer.Type.Title:
                return setting.titleFont != null ? setting.titleFont : setting.FontAsset;
            case FontLocalizer.Type.Paragraph:
                return setting.paragraphFont != null ? setting.paragraphFont : setting.FontAsset;
            default:
                return setting.FontAsset;
        }
    }

    private static float OutlineWidthFor(FontLocalizationSetting setting) =>
        setting.outlineWidth > 0f ? setting.outlineWidth : LocalizationMaterialManager.DefaultOutlineWidth;

    private readonly Dictionary<(TMP_FontAsset font, string lang), bool> _fontSupportCache =
        new Dictionary<(TMP_FontAsset font, string lang), bool>();

    public bool FontSupportsLanguage(TMP_FontAsset font, string sl) {
        if (font == null) return false;
        if (string.IsNullOrEmpty(sl) || sl.Equals(Localizer.DEFAULT_LANGUAGE, StringComparison.OrdinalIgnoreCase)) return true;
        if (SteamLanguageList.IsComplexOrAsianScript(sl)) return false;

        var key = (font, sl.ToLowerInvariant());
        if (_fontSupportCache.TryGetValue(key, out bool cached)) return cached;

        string requiredGlyphs = SteamLanguageList.GetRequiredCharacters(sl);
        bool supported = true;

        if (!string.IsNullOrEmpty(requiredGlyphs)) {
            for (int i = 0; i < requiredGlyphs.Length; i++) {
                if (!font.HasCharacter(requiredGlyphs[i], searchFallbacks: false)) {
                    supported = false;
                    break;
                }
            }
        } else if (!SteamLanguageList.IsLatinScript(sl)) {
            // Non-Latin language without a glyph definition is assumed unsupported by a standard font
            supported = false;
        }

        _fontSupportCache[key] = supported;
        return supported;
    }

    public FontLocalizationSetting SettingForLanguage(string sl) {
        if (string.IsNullOrEmpty(sl)) sl = Localizer.DEFAULT_LANGUAGE;

        if (sl.Equals(Localizer.DEFAULT_LANGUAGE, StringComparison.OrdinalIgnoreCase)) {
            return english;
        }

        // Complex and Asian scripts (Arabic, Thai, CJK) always route directly to Noto / other font
        if (SteamLanguageList.IsComplexOrAsianScript(sl)) {
            return other.FontAsset != null ? other : english;
        }

        // If English font asset exists and contains all required glyphs for this language, use it!
        if (english.FontAsset != null && FontSupportsLanguage(english.FontAsset, sl)) {
            return english;
        }

        // Fallback to other (Noto / global font)
        return other.FontAsset != null ? other : english;
    }
}


