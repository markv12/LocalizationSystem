using UnityEditor;
using UnityEngine;

public class LocalizationEntryEditorWindow : EditorWindow {
    private LocalizationEntry _entry;
    private LocalizationMasterDatabase _database;
    private Vector2 _scrollPos;

    public static void ShowWindow(LocalizationEntry entry, LocalizationMasterDatabase database) {
        LocalizationEntryEditorWindow window = GetWindow<LocalizationEntryEditorWindow>(true, "Edit Localization Entry", true);
        window._entry = entry;
        window._database = database;
        window.minSize = new Vector2(500, 400);
        window.Show();
    }

    private void OnGUI() {
        if (_entry == null || _database == null) {
            EditorGUILayout.HelpBox("No entry or database selected.", MessageType.Warning);
            return;
        }

        EditorGUI.BeginChangeCheck();

        EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(10, 10, 10, 10) });

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        EditorGUILayout.LabelField("Base Information", EditorStyles.boldLabel);
        _entry.Key = EditorGUILayout.TextField("Key", _entry.Key);

        EditorGUILayout.LabelField("English Text");
        _entry.EnglishText = EditorGUILayout.TextArea(_entry.EnglishText, GUILayout.MinHeight(60));

        EditorGUILayout.LabelField("Context (Notes for Translator)");
        _entry.Context = EditorGUILayout.TextArea(_entry.Context, GUILayout.MinHeight(40));

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Translations", EditorStyles.boldLabel);

        foreach (var lang in _database.TargetLanguages) {
            var t = _entry.GetTranslation(lang);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(lang, EditorStyles.boldLabel);

            if (t != null) {
                t.Status = (TranslationStatus)EditorGUILayout.EnumPopup(t.Status, GUILayout.Width(120));
            } else {
                if (GUILayout.Button("Create Translation", GUILayout.Width(120))) {
                    t = new LanguageTranslation { LanguageCode = lang, Status = TranslationStatus.Untranslated };
                    _entry.Translations.Add(t);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (t != null) {
                EditorGUILayout.LabelField("Translated Text");
                t.TranslatedText = EditorGUILayout.TextArea(t.TranslatedText, GUILayout.MinHeight(40));
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(2);
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();

        if (EditorGUI.EndChangeCheck()) {
            EditorUtility.SetDirty(_database);
            var dashboard = GetWindow<LocalizationDashboard>(false, null, false);
            if (dashboard != null) dashboard.Repaint();
        }
    }
}
