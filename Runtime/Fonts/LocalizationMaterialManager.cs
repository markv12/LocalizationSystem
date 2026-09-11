using System.Collections.Generic;
using TMPro;
using UnityEngine;

public static class LocalizationMaterialManager {
    private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
    private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
    private static readonly int FaceDilateId = Shader.PropertyToID("_FaceDilate");
    private const string OutlineKeyword = "OUTLINE_ON";

    public const float DefaultOutlineWidth = 0.35f;

    private static readonly Dictionary<(TMP_FontAsset font, Color color, int widthInt), Material> outlineCache =
        new Dictionary<(TMP_FontAsset font, Color color, int widthInt), Material>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ClearCache() {
        outlineCache.Clear();
    }

    public static Material GetBlackOutline(TMP_FontAsset font, float width = DefaultOutlineWidth) =>
        GetOutlineMaterial(font, Color.black, width);

    public static Material GetWhiteOutline(TMP_FontAsset font, float width = DefaultOutlineWidth) =>
        GetOutlineMaterial(font, Color.white, width);

    public static Material GetOutlineMaterial(TMP_FontAsset font, Color color, float width = DefaultOutlineWidth) {
        if (font == null || font.material == null) return null;

        int widthKey = Mathf.RoundToInt(width * 1000f);
        var key = (font, color, widthKey);
        if (outlineCache.TryGetValue(key, out Material mat) && mat != null) {
            return mat;
        }

        string suffix = color == Color.black ? "BlackOutline" : (color == Color.white ? "WhiteOutline" : $"Outline_{color}");
        mat = CreateOutlineMaterial(font, color, width, suffix);
        outlineCache[key] = mat;
        return mat;
    }

    private static Material CreateOutlineMaterial(TMP_FontAsset font, Color color, float width, string suffix) {
        Material mat = new Material(font.material);
        mat.name = $"{font.name}_{suffix}";
        mat.hideFlags = HideFlags.DontSave;
        mat.EnableKeyword(OutlineKeyword);
        mat.SetColor(OutlineColorId, color);
        mat.SetFloat(OutlineWidthId, width);
        float baseDilate = font.material.HasProperty(FaceDilateId) ? font.material.GetFloat(FaceDilateId) : 0f;
        mat.SetFloat(FaceDilateId, baseDilate + width);
        return mat;
    }
}
