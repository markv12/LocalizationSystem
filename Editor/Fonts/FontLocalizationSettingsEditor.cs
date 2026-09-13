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

#if UNITY_2023_2_OR_NEWER
        EditorGUILayout.HelpBox(
            "Unity 2023+ / Unity 6: Main Fonts serve both TextMesh Pro and UI Toolkit. " +
            "UI Toolkit overrides are collapsed below.", MessageType.Info);
        EditorGUILayout.Space(4);
#endif

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

        // Main Fonts (clean primary view)
        EditorGUILayout.PropertyField(fontAsset, new GUIContent("Regular Font", "Primary font asset. In Unity 2023+ / Unity 6, this serves both TextMesh Pro and UI Toolkit."));
        EditorGUILayout.PropertyField(titleFont, new GUIContent("Title Font", "Optional title font variant."));
        EditorGUILayout.PropertyField(paragraphFont, new GUIContent("Paragraph Font", "Optional paragraph/body font variant."));

        // Collapsible UI Toolkit Overrides
        bool hasOverrides = (uiToolkitFont != null && uiToolkitFont.objectReferenceValue != null) ||
                            (titleUiToolkitFont != null && titleUiToolkitFont.objectReferenceValue != null) ||
                            (paragraphUiToolkitFont != null && paragraphUiToolkitFont.objectReferenceValue != null);

        string foldoutLabel = hasOverrides
            ? "UI Toolkit Overrides (Active)"
            : "UI Toolkit Overrides (Optional)";

        EditorGUILayout.Space(2);
        foldout = EditorGUILayout.Foldout(foldout || hasOverrides, foldoutLabel, true);
        if (foldout) {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(uiToolkitFont, new GUIContent("UI Toolkit Regular", "Optional override. In Unity 2023+, leave empty to use the Regular Font above."));
            EditorGUILayout.PropertyField(titleUiToolkitFont, new GUIContent("UI Toolkit Title", "Optional override. In Unity 2023+, leave empty to use the Title Font above."));
            EditorGUILayout.PropertyField(paragraphUiToolkitFont, new GUIContent("UI Toolkit Paragraph", "Optional override. In Unity 2023+, leave empty to use the Paragraph Font above."));
            EditorGUI.indentLevel--;
        }

        EditorGUI.indentLevel--;
    }
}

