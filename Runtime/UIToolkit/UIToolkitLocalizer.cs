using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Dynamic element styling and localization helpers for UI Toolkit.
/// </summary>
public static class UIToolkitLocalizer {
    public const string TITLE_FONT_CLASS = "text-title";

    public static void SetLocalizedText(Label label, string locKey) {
        if (label == null) return;
        label.text = RTLHelper.Shape(Localizer.GetText(locKey));
    }

    public static void ApplyFont(VisualElement element, FontLocalizer.Type type) {
        if (element == null) return;
        Font font = Localizer.CurrentLangUIToolkitFont(type);
        if (font == null) {
            Debug.LogWarning($"[UIToolkitLocalizer] No UIToolkit font configured for {type} / language '{Localizer.currentLanguage}'. Set the uiToolkitFont slot on FontLocalizationSettings.");
            return;
        }
        element.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
    }

    public static void ApplyFonts(VisualElement root) {
        if (root == null) return;
        Font regular = Localizer.CurrentLangUIToolkitFont(FontLocalizer.Type.Regular);
        Font title = Localizer.CurrentLangUIToolkitFont(FontLocalizer.Type.Title);
        if (regular == null && title == null) {
            ApplyFont(root, FontLocalizer.Type.Regular);
            return;
        }

        Apply(root);

        void Apply(VisualElement element) {
            bool isTitle = element.ClassListContains(TITLE_FONT_CLASS);
            Font font = isTitle ? (title ?? regular) : (regular ?? title);
            if (font != null) {
                element.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            }
            for (int i = 0; i < element.childCount; i++) {
                Apply(element[i]);
            }
        }
    }

    public static void SetLocalizedTextAndFont(Label label, string locKey, FontLocalizer.Type type) {
        SetLocalizedText(label, locKey);
        ApplyFont(label, type);
    }
}
