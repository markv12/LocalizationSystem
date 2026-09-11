using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public class FontLocalizer : LanguageChangedHandler {
    public TMP_Text textField;
    public Type type;

    protected override void Refresh() {
        if (textField == null) return;
        TMP_FontAsset font = Localizer.CurrentLangFontFor(type);
        if (font != null) {
            textField.font = font;
        }
        Material mat = Localizer.CurrentLangFontMaterial(type);
        if (mat != null) {
            textField.fontMaterial = mat;
        }
    }

    public enum Type {
        Regular = 0,
        BlackOutline = 1,
        WhiteOutline = 2,
        Title = 3,
        Paragraph = 4
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
