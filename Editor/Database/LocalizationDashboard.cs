using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class LocalizationDashboard : EditorWindow {
    private LocalizationMasterDatabase database;
    private Vector2 scrollPos;
    private int _page;
    private GUIStyle _richTextStyle;

    private List<LocalizationEntry> _cachedAllEntries;

    private const int PageSize     = 20;
    private const int KeyColWidth  = 150;
    private const int EngColWidth  = 200;
    private const int LangColWidth = 150;

    [MenuItem("Window/Localization/Localization Dashboard")]
    public static void ShowWindow() {
        GetWindow<LocalizationDashboard>("Localization Dashboard");
    }

    private void OnEnable() {
        _richTextStyle = null;
        if (database == null) {
            string[] guids = AssetDatabase.FindAssets("t:LocalizationMasterDatabase");
            if (guids.Length > 0) {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                database = AssetDatabase.LoadAssetAtPath<LocalizationMasterDatabase>(path);
            }
        }
    }

    private GUIStyle GetRichTextStyle() {
        if (_richTextStyle != null) return _richTextStyle;
        _richTextStyle = new GUIStyle(EditorStyles.label) { richText = true };
        return _richTextStyle;
    }

    private static string FirstLine(string s, int maxChars) {
        if (string.IsNullOrEmpty(s)) return "";
        int nl = s.IndexOfAny(new[] { '\n', '\r' });
        string line = nl >= 0 ? s.Substring(0, nl) : s;
        return line.Length > maxChars ? line.Substring(0, maxChars) : line;
    }

    private void OnGUI() {
        database = (LocalizationMasterDatabase)EditorGUILayout.ObjectField(
            "Master Database", database, typeof(LocalizationMasterDatabase), false);

        if (database == null) {
            EditorGUILayout.HelpBox("Please select a Localization Master Database.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset Pending Statuses", GUILayout.ExpandWidth(false))) {
            LocalizationProcessor.ResetPendingStatuses(database);
        }
        if (GUILayout.Button("Clear BatchIDs", GUILayout.ExpandWidth(false))) {
            LocalizationProcessor.ClearBatchIDs(database);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Prepare Translation Batch")) {
            LocalizationProcessor.PrepareTranslationBatch(database);
        }
        if (GUILayout.Button("Send to AI")) {
            Debug.Log("[Localization] Send to AI button clicked.");
            BatchHandler.SendBatchRequest(database, database.TargetLanguages);
        }
        if (GUILayout.Button("Check API Status")) {
            var pendingBatchIds = database.AllEntries
                .SelectMany(e => e.Translations)
                .Select(t => t.BatchID)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();
            if (pendingBatchIds.Count == 0) {
                Debug.LogWarning("[Localization] No active Batch IDs found in the database. (Prepare and send a batch first).");
            } else {
                Debug.Log($"[Localization] Checking status for {pendingBatchIds.Count} batch(es): {string.Join(", ", pendingBatchIds)}");
                foreach (var id in pendingBatchIds)
                    BatchHandler.CheckBatchStatus(database, id);
            }
        }
        if (GUILayout.Button("Bake")) {
            LocalizationProcessor.BakeAssets(database);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        RebuildCacheIfNeeded();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        DrawEntriesTable();
        EditorGUILayout.EndScrollView();
    }

    private void RebuildCacheIfNeeded() {
        _cachedAllEntries = new List<LocalizationEntry>(database.AllEntries);
    }

    private void DrawEntriesTable() {
        if (_cachedAllEntries == null || _cachedAllEntries.Count == 0) return;

        int totalPages = Mathf.CeilToInt(_cachedAllEntries.Count / (float)PageSize);
        _page = Mathf.Clamp(_page, 0, totalPages - 1);

        // Pagination controls
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = _page > 0;
        if (GUILayout.Button("<", GUILayout.Width(30))) _page--;
        GUI.enabled = true;
        GUILayout.Label($"Page {_page + 1} / {totalPages}", GUILayout.Width(90));
        GUI.enabled = _page < totalPages - 1;
        if (GUILayout.Button(">", GUILayout.Width(30))) _page++;
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // Color legend
        GUILayout.Label(
            "<color=#AAFFAA>■ Native Approved</color>   " +
            "<color=#00FF00>■ Translated</color>   " +
            "<color=#FFFF00>■ Dirty</color>   " +
            "<color=#00FFFF>■ Out for Batch</color>   " +
            "<color=#808080>■ Untranslated</color>",
            GetRichTextStyle());

        EditorGUILayout.Space(4);

        // Header row
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("", GUILayout.Width(30));
        EditorGUILayout.LabelField("Key", EditorStyles.boldLabel, GUILayout.Width(KeyColWidth));
        EditorGUILayout.LabelField("English Text", EditorStyles.boldLabel, GUILayout.Width(EngColWidth));
        foreach (var lang in database.TargetLanguages) {
            EditorGUILayout.BeginVertical(GUILayout.Width(LangColWidth));
            EditorGUILayout.LabelField(lang, EditorStyles.boldLabel, GUILayout.Width(LangColWidth));
            if (GUILayout.Button("Export", GUILayout.ExpandWidth(false)))
                LocalizationProcessor.ExportLanguageCSV(database, lang);
            if (GUILayout.Button("Import", GUILayout.ExpandWidth(false))) {
                string path = EditorUtility.OpenFilePanel($"Import {lang}", "Assets", "csv");
                if (!string.IsNullOrEmpty(path))
                    LocalizationProcessor.ImportLanguageCSV(database, lang, path);
            }
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();

        // Data rows
        int start = _page * PageSize;
        int end   = Mathf.Min(start + PageSize, _cachedAllEntries.Count);
        for (int i = start; i < end; i++) {
            var entry = _cachedAllEntries[i];
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("✎", GUILayout.Width(30))) {
                LocalizationEntryEditorWindow.ShowWindow(entry, database);
            }
            EditorGUILayout.LabelField(entry.Key ?? "", GUILayout.Width(KeyColWidth));
            EditorGUILayout.LabelField(FirstLine(entry.EnglishText ?? "", 20), GUILayout.Width(EngColWidth));
            foreach (var lang in database.TargetLanguages) {
                var t = entry.GetTranslation(lang);
                string rawText = t != null ? FirstLine(t.TranslatedText ?? "", 20) : "";
                string cellText = string.IsNullOrWhiteSpace(rawText) ? "■" : rawText;
                Color color = Color.white;
                if (t != null) {
                    switch (t.Status) {
                        case TranslationStatus.Untranslated:   color = Color.gray;   break;
                        case TranslationStatus.OutForBatch:    color = Color.cyan;   break;
                        case TranslationStatus.Translated:     color = Color.green;  break;
                        case TranslationStatus.Dirty:          color = Color.yellow; break;
                        case TranslationStatus.NativeApproved: color = new Color(0.666667f, 1f, 0.666667f, 1f); break;
                    }
                }
                var prev = GUI.color;
                GUI.color = color;
                EditorGUILayout.LabelField(cellText, GUILayout.Width(LangColWidth));
                GUI.color = prev;
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
