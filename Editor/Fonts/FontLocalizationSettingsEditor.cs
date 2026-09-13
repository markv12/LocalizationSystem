using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(FontLocalizationSettings))]
public class FontLocalizationSettingsEditor : Editor {
    private SerializedProperty _englishProp;
    private SerializedProperty _otherProp;

    private static bool _englishUiFoldout = false;
    private static bool _otherUiFoldout = false;

    private void OnEnable() {
        _englishProp = serializedObject.FindProperty("english");
        _otherProp = serializedObject.FindProperty("other");
    }

    public override void OnInspectorGUI() {
        serializedObject.Update();

        DrawSettingGroup("Default / English Font Settings", _englishProp, ref _englishUiFoldout);
        EditorGUILayout.Space(14);
        DrawSettingGroup("Fallback / Non-Latin Font Settings (Noto / Global)", _otherProp, ref _otherUiFoldout);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawSettingGroup(string header, SerializedProperty groupProp, ref bool foldout) {
        if (groupProp == null) return;

        EditorGUILayout.LabelField(header, EditorStyles.boldLabel);
        EditorGUI.indentLevel++;

        SerializedProperty fontAsset = groupProp.FindPropertyRelative("FontAsset");
        SerializedProperty titleFont = groupProp.FindPropertyRelative("titleFont");
        SerializedProperty paragraphFont = groupProp.FindPropertyRelative("paragraphFont");

        SerializedProperty uiToolkitFont = groupProp.FindPropertyRelative("uiToolkitFont");
        SerializedProperty titleUiToolkitFont = groupProp.FindPropertyRelative("titleUIToolkitFont");
        SerializedProperty paragraphUiToolkitFont = groupProp.FindPropertyRelative("paragraphUIToolkitFont");

        // Main TMP Fonts
        EditorGUILayout.PropertyField(fontAsset, new GUIContent("TMP Regular Font", "Primary TextMesh Pro font asset for this language."));
        EditorGUILayout.PropertyField(titleFont, new GUIContent("TMP Title Font", "Optional title TextMesh Pro font variant."));
        EditorGUILayout.PropertyField(paragraphFont, new GUIContent("TMP Paragraph Font", "Optional paragraph/body TextMesh Pro font variant."));

        // Collapsible UI Toolkit Fonts
        bool hasUiFonts = (uiToolkitFont != null && uiToolkitFont.objectReferenceValue != null) ||
                          (titleUiToolkitFont != null && titleUiToolkitFont.objectReferenceValue != null) ||
                          (paragraphUiToolkitFont != null && paragraphUiToolkitFont.objectReferenceValue != null);

        EditorGUILayout.Space(2);
        foldout = EditorGUILayout.Foldout(foldout || hasUiFonts, "UI Toolkit Fonts", true);
        if (foldout) {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(uiToolkitFont, new GUIContent("UI Toolkit Regular", "Primary UI Toolkit font asset for this language."));
            EditorGUILayout.PropertyField(titleUiToolkitFont, new GUIContent("UI Toolkit Title", "Optional UI Toolkit title font variant."));
            EditorGUILayout.PropertyField(paragraphUiToolkitFont, new GUIContent("UI Toolkit Paragraph", "Optional UI Toolkit paragraph font variant."));
            EditorGUI.indentLevel--;
        }

        EditorGUI.indentLevel--;
    }
}

