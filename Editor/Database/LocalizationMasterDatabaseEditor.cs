using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LocalizationMasterDatabase))]
public class LocalizationMasterDatabaseEditor : Editor {
    public override void OnInspectorGUI() {
        if (GUILayout.Button("Open Localization Dashboard", GUILayout.Height(24))) {
            LocalizationDashboard.ShowWindow();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Import New Strings", GUILayout.Height(22))) {
            LocalizationCsvImporter.ImportNewStrings();
        }
        if (GUILayout.Button("Import CSV...", GUILayout.Height(22))) {
            string path = EditorUtility.OpenFilePanel("Import Localization CSV", "Assets/Resources", "csv");
            if (!string.IsNullOrEmpty(path)) {
                LocalizationCsvImporter.Import((LocalizationMasterDatabase)target, path);
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add All Languages", GUILayout.Height(22))) {
            if (EditorUtility.DisplayDialog("Add All Languages", "Add all supported Steam languages to TargetLanguages?", "Yes", "No")) {
                var db = (LocalizationMasterDatabase)target;
                db.AddAllLanguages();
                EditorUtility.SetDirty(db);
                AssetDatabase.SaveAssets();
            }
        }
        if (GUILayout.Button("Rebuild Localization Font", GUILayout.Height(22))) {
            LocalizationFontBuilder.Rebuild((LocalizationMasterDatabase)target);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Log Unique Characters", GUILayout.Height(22))) {
            var codePoints = LocalizationFontBuilder.CollectCodePoints((LocalizationMasterDatabase)target);
            var visible = new System.Text.StringBuilder();
            var whitespace = new List<string>();
            foreach (int codePoint in codePoints) {
                if (codePoint <= char.MaxValue && char.IsWhiteSpace((char)codePoint))
                    whitespace.Add($"U+{codePoint:X4} ({char.GetUnicodeCategory((char)codePoint)})");
                else
                    visible.Append(char.ConvertFromUtf32(codePoint));
            }
            string whitespaceNote = whitespace.Count > 0 ? $"\nWhitespace: {string.Join(", ", whitespace)}" : "";
            Debug.Log($"Unique characters across all translations ({codePoints.Count}):\n{visible}{whitespaceNote}");
        }

        if (GUILayout.Button("Clear Machine Translated", GUILayout.Height(22))) {
            if (EditorUtility.DisplayDialog("Clear Machine Translated", "Reset all 'Translated' entries back to 'Untranslated'?", "Yes", "No")) {
                var db = (LocalizationMasterDatabase)target;
                int count = 0;
                foreach (var entry in db.AllEntries) {
                    foreach (var translation in entry.Translations) {
                        if (translation.Status == TranslationStatus.Translated) {
                            translation.TranslatedText = string.Empty;
                            translation.Status = TranslationStatus.Untranslated;
                            translation.SourceHash = string.Empty;
                            count++;
                        }
                    }
                }
                if (count > 0) {
                    EditorUtility.SetDirty(db);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[Localization] Cleared {count} machine-translated strings.");
                } else {
                    Debug.Log("[Localization] No machine-translated strings found to clear.");
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8);
        DrawDefaultInspector();
    }
}
