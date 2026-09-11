using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public class FontLocalizer : LanguageChangedHandler {
    public TMP_Text textField;

    [Tooltip("Typeface variant (Regular, Title, Paragraph). Also supports legacy BlackOutline/WhiteOutline.")]
    public Type type;

    [Tooltip("Outline styling (None, Black, White). Automatically generates outline material without presets.")]
    public OutlineStyle outline = OutlineStyle.None;

    protected override void Refresh() {
        if (textField == null) return;

        TMP_FontAsset font = Localizer.CurrentLangFontFor(type);
        if (font != null) {
            textField.font = font;
        }

        OutlineStyle effectiveOutline = outline;
        if (effectiveOutline == OutlineStyle.None) {
            if (type == Type.BlackOutline) effectiveOutline = OutlineStyle.Black;
            else if (type == Type.WhiteOutline) effectiveOutline = OutlineStyle.White;
        }

        if (effectiveOutline != OutlineStyle.None) {
            Material mat = Localizer.CurrentLangFontMaterial(effectiveOutline, type);
            if (mat != null) {
                textField.fontMaterial = mat;
            }
        } else if (type == Type.Regular) {
            Material mat = Localizer.CurrentLangFontMaterial(Type.Regular);
            if (mat != null) {
                textField.fontMaterial = mat;
            }
        }
    }

    public enum OutlineStyle {
        None = 0,
        Black = 1,
        White = 2
    }

    public enum Type {
        Regular = 0,
        Title = 1,
        Paragraph = 2,
        BlackOutline = 3,
        WhiteOutline = 4
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(FontLocalizer))]
[CanEditMultipleObjects]
[ExecuteInEditMode]
public class FontLocalizerViewer : Editor {
    public override void OnInspectorGUI() {
        base.OnInspectorGUI();

        FontLocalizer model = (FontLocalizer)target;
        GUILayout.BeginHorizontal();
        GUILayout.Space(10);
        if (GUILayout.Button("Get Component", GUILayout.Width(EditorGUIUtility.currentViewWidth * .45f))) {
            model.textField = model.GetComponent<TMP_Text>();
            EditorUtility.SetDirty(model);
        }
        GUILayout.EndHorizontal();
    }
}
#endif
