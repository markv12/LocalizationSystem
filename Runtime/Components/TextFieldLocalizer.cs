using TMPro;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

public class TextFieldLocalizer : LanguageChangedHandler {
    public TMP_Text textField;
    public string locKey;
    public FontLocalizer.Type type;

    protected override void Awake() {
        if (textField == null) textField = GetComponent<TMP_Text>();
        if (string.IsNullOrWhiteSpace(locKey) && textField != null) {
            locKey = textField.text;
        }
        base.Awake();
    }

    protected override void Refresh() {
        if (textField == null) return;
        RTLHelper.SetText(textField, Localizer.GetText(locKey));
        TMP_FontAsset font = Localizer.CurrentLangFontFor(type);
        if (font != null) {
            textField.font = font;
        }
        Material mat = Localizer.CurrentLangFontMaterial(type);
        if (mat != null) {
            textField.fontMaterial = mat;
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(TextFieldLocalizer))]
[CanEditMultipleObjects]
[ExecuteInEditMode]
public class TextFieldLocalizerViewer : Editor {
    public override void OnInspectorGUI() {
        base.OnInspectorGUI();

        TextFieldLocalizer model = (TextFieldLocalizer)target;
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
