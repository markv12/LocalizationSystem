using System.Collections.Generic;
using TMPro;
using UnityEngine;

public static class LocalizationMaterialManager {
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private const string OutlineKeyword = "OUTLINE_ON";

    public const float DefaultOutlineWidth = 0.25f;

    private static readonly Dictionary<TMP_FontAsset, Material> blackOutlineCache = new Dictionary<TMP_FontAsset, Material>();
    private static readonly Dictionary<TMP_FontAsset, Material> whiteOutlineCache = new Dictionary<TMP_FontAsset, Material>();
    private static readonly Dictionary<(TMP_FontAsset font, Color color, int widthInt), Material> customOutlineCache =
        new Dictionary<(TMP_FontAsset font, Color color, int widthInt), Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ClearCache() {
        blackOutlineCache.Clear();
        whiteOutlineCache.Clear();
        customOutlineCache.Clear();
    }

    public static Material GetBlackOutline(TMP_FontAsset font, float width = DefaultOutlineWidth) {
        if (font == null || font.material == null) return null;

        if (blackOutlineCache.TryGetValue(font, out Material mat) && mat != null) {
            return mat;
        }

        mat = CreateOutlineMaterial(font, Color.black, width, "BlackOutline");
        blackOutlineCache[font] = mat;
        return mat;
    }

    public static Material GetWhiteOutline(TMP_FontAsset font, float width = DefaultOutlineWidth) {
        if (font == null || font.material == null) return null;

        if (whiteOutlineCache.TryGetValue(font, out Material mat) && mat != null) {
            return mat;
        }

        mat = CreateOutlineMaterial(font, Color.white, width, "WhiteOutline");
        whiteOutlineCache[font] = mat;
        return mat;
    }

    public static Material GetOutlineMaterial(TMP_FontAsset font, Color color, float width = DefaultOutlineWidth) {
        if (font == null || font.material == null) return null;
        if (color == Color.black) return GetBlackOutline(font, width);
        if (color == Color.white) return GetWhiteOutline(font, width);

        int widthKey = Mathf.RoundToInt(width * 1000f);
        var key = (font, color, widthKey);
        if (customOutlineCache.TryGetValue(key, out Material mat) && mat != null) {
            return mat;
        }

        mat = CreateOutlineMaterial(font, color, width, $"Outline_{color}");
        customOutlineCache[key] = mat;
        return mat;
    }

    private static Material CreateOutlineMaterial(TMP_FontAsset font, Color color, float width, string suffix) {
        Material mat = new Material(font.material);
        mat.name = $"{font.name}_{suffix}";
        mat.hideFlags = HideFlags.DontSave;
        mat.EnableKeyword(OutlineKeyword);
        mat.SetColor(OutlineColorId, color);
        mat.SetFloat(OutlineWidthId, width);
        return mat;
    }
}
