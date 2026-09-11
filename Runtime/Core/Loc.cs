using System.Collections.Generic;

/// <summary>
/// Localized text helper for UI built in C#. Resolves the key and pre-shapes RTL text in one step.
/// </summary>
public static class Loc {
    public static string Get(string key) => RTLHelper.Shape(Localizer.GetTextOrEnglish(key));

    public static string Format(string key, params object[] args) => RTLHelper.Shape(Localizer.Format(key, args));

    /// <summary>
    /// Falls back to <paramref name="fallback"/> rather than to the key itself, for data-driven keys
    /// where a missing entry should still read as English/fallback instead of raw key.
    /// </summary>
    public static string GetOr(string key, string fallback) {
        if (!string.IsNullOrEmpty(key)) {
            if (Localizer.TryGetText(key, out string text)) return RTLHelper.Shape(text);
            if (Localizer.TryGetEnglishText(key, out string english)) return RTLHelper.Shape(english);
        }
        return RTLHelper.Shape(fallback);
    }

    /// <summary>Dropdown choices from a key array, kept parallel to the enum the index maps to.</summary>
    public static List<string> Choices(params string[] keys) {
        if (keys == null) return new List<string>();
        var result = new List<string>(keys.Length);
        foreach (string key in keys) result.Add(Get(key));
        return result;
    }
}
