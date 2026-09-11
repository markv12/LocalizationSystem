using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.UIElements;

/// <summary>
/// Localization for UI Toolkit. Keys are derived from the UXML file name and the element's
/// name attribute ("pausemenu.resume-button"), so UXML keeps its English text and stays
/// readable in UI Builder. Add the "no-loc" USS class to any element whose text must not be translated.
/// </summary>
public static class UILocalization {
    public const string NO_LOC_CLASS = "no-loc";
    public const string TOOLTIP_SUFFIX = ".tooltip";

    public static string DocumentPrefix(string visualTreeAssetName) =>
        visualTreeAssetName == null ? string.Empty : visualTreeAssetName.ToLowerInvariant();

    public static string Key(string documentPrefix, string elementName) => documentPrefix + "." + elementName;

    /// <summary>
    /// Localizes every named, text-bearing element under <paramref name="root"/>. Elements with no
    /// name, no text, or the "no-loc" class are left alone, as are keys with no database entry.
    /// </summary>
    public static void Apply(VisualElement root, string documentPrefix) {
        if (root == null || string.IsNullOrEmpty(documentPrefix)) return;
        Walk(root, documentPrefix);
    }

    private static void Walk(VisualElement element, string prefix) {
        Localize(element, prefix);
        for (int i = 0; i < element.childCount; i++) {
            Walk(element[i], prefix);
        }
    }

    private static void Localize(VisualElement element, string prefix) {
        if (string.IsNullOrEmpty(element.name) || element.ClassListContains(NO_LOC_CLASS)) return;

        string key = Key(prefix, element.name);

        PropertyInfo labelProperty = LabelProperty(element.GetType());
        if (labelProperty != null) {
            string current = (string)labelProperty.GetValue(element);
            if (!string.IsNullOrEmpty(current) && Localizer.TryGetText(key, out string label))
                labelProperty.SetValue(element, RTLHelper.Shape(label));
        } else if (element is TextElement text) {
            if (!string.IsNullOrEmpty(text.text) && Localizer.TryGetText(key, out string value))
                text.text = RTLHelper.Shape(value);
        }

        if (!string.IsNullOrEmpty(element.tooltip) && Localizer.TryGetText(key + TOOLTIP_SUFFIX, out string tip))
            element.tooltip = RTLHelper.Shape(tip);
    }

    private static readonly Dictionary<Type, PropertyInfo> _labelProperties = new Dictionary<Type, PropertyInfo>();

    private static PropertyInfo LabelProperty(Type type) {
        if (_labelProperties.TryGetValue(type, out PropertyInfo cached)) return cached;

        PropertyInfo property = type.GetProperty("label", BindingFlags.Public | BindingFlags.Instance);
        if (property != null && (property.PropertyType != typeof(string) || !property.CanWrite)) property = null;
        _labelProperties[type] = property;
        return property;
    }

    public static string Humanize(string elementName) =>
        elementName == null ? string.Empty : elementName.Replace('-', ' ').Replace('_', ' ');
}
