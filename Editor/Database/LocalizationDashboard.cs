using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public class LocalizationDashboard : EditorWindow {
    private LocalizationMasterDatabase database;
    private Vector2 scrollPos;
    private int _page;
    private GUIStyle _richTextStyle;

    private struct DisplayEntry {
        public LocalizationEntry Entry;
        public string Category;
    }

    private List<DisplayEntry> _cachedEntries = new List<DisplayEntry>();
    private int _selectedCategoryIndex = 0;
    private string _searchFilter = "";

    private const int PageSize         = 20;
    private const int CategoryColWidth = 100;
    private const int KeyColWidth      = 150;
    private const int EngColWidth      = 200;
    private const int LangColWidth     = 150;

    [MenuItem("Window/Localization/Localization Dashboard")]
    public static void ShowWindow() {
        GetWindow<LocalizationDashboard>("Localization Dashboard");
    }

    private void OnEnable() {
        _richTextStyle = null;
        if (database == null) {
            database = LocalizationMasterDatabase.LoadDatabase();
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

        var categories = GetCategoryList();
        if (_selectedCategoryIndex >= categories.Count) {
            _selectedCategoryIndex = 0;
        }

        string currentCategoryName = categories[_selectedCategoryIndex];
        bool isAllCategories = _selectedCategoryIndex == 0;
        var currentEntries = isAllCategories ? null : GetEntriesForCategory(currentCategoryName);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button(isAllCategories ? "Reset Pending Statuses (All)" : $"Reset Pending Statuses ({currentCategoryName})", GUILayout.ExpandWidth(false))) {
            LocalizationProcessor.ResetPendingStatuses(database, currentEntries);
        }
        if (GUILayout.Button(isAllCategories ? "Clear BatchIDs (All)" : $"Clear BatchIDs ({currentCategoryName})", GUILayout.ExpandWidth(false))) {
            LocalizationProcessor.ClearBatchIDs(database, currentEntries);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();
        if (isAllCategories) {
            if (GUILayout.Button("Prepare Translation Batch (All)")) {
                LocalizationProcessor.PrepareTranslationBatch(database);
            }
            if (GUILayout.Button("Send to AI (All)")) {
                Debug.Log("[Localization] Send to AI (All) button clicked.");
                BatchHandler.SendBatchRequest(database, database.TargetLanguages);
            }
        } else {
            if (GUILayout.Button($"Prepare Batch ({currentCategoryName})")) {
                LocalizationProcessor.PrepareTranslationBatch(database, currentEntries);
            }
            if (GUILayout.Button($"Send to AI ({currentCategoryName})")) {
                Debug.Log($"[Localization] Send to AI ({currentCategoryName}) button clicked.");
                BatchHandler.SendBatchRequest(database, database.TargetLanguages, currentEntries);
            }
            if (GUILayout.Button("Prepare All", GUILayout.Width(90))) {
                LocalizationProcessor.PrepareTranslationBatch(database);
            }
            if (GUILayout.Button("Send All", GUILayout.Width(80))) {
                BatchHandler.SendBatchRequest(database, database.TargetLanguages);
            }
        }

        if (GUILayout.Button("Check API Status")) {
            var targetEntries = currentEntries ?? database.AllEntries;
            var pendingBatchIds = targetEntries
                .SelectMany(e => e.Translations)
                .Select(t => t.BatchID)
                .Where(id => !string.IsNullOrEmpty(id))
                .Distinct()
                .ToList();
            if (pendingBatchIds.Count == 0) {
                Debug.LogWarning("[Localization] No active Batch IDs found in the selected entries. (Prepare and send a batch first).");
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

        EditorGUILayout.Space(8);

        // Filter and Search Toolbar
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        
        GUILayout.Label("Category:", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
        string[] categoryLabels = GetCategoryLabelsWithCounts();
        int newCatIndex = EditorGUILayout.Popup(_selectedCategoryIndex, categoryLabels, EditorStyles.toolbarPopup, GUILayout.Width(220));
        if (newCatIndex != _selectedCategoryIndex) {
            _selectedCategoryIndex = newCatIndex;
            _page = 0;
        }

        GUILayout.Space(10);

        GUILayout.Label("Search:", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
        string newSearch = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(200));
        if (newSearch != _searchFilter) {
            _searchFilter = newSearch;
            _page = 0;
        }
        if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(20))) {
            _searchFilter = "";
            _page = 0;
            GUI.FocusControl(null);
        }

        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        RebuildCacheIfNeeded();

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
        DrawEntriesTable(isAllCategories, currentEntries);
        EditorGUILayout.EndScrollView();
    }

    private List<string> GetCategoryList() {
        var list = new List<string> { "All Categories", "Main" };
        if (database?.AdditionalCategories != null) {
            foreach (var cat in database.AdditionalCategories) {
                string name = string.IsNullOrWhiteSpace(cat?.CategoryName) ? "Uncategorized" : cat.CategoryName;
                if (!list.Contains(name)) {
                    list.Add(name);
                }
            }
        }
        return list;
    }

    private string[] GetCategoryLabelsWithCounts() {
        var list = GetCategoryList();
        string[] labels = new string[list.Count];
        int allCount = database.AllEntries != null ? database.AllEntries.Count() : 0;
        int mainCount = database.Entries != null ? database.Entries.Count : 0;

        labels[0] = $"All Categories ({allCount})";
        labels[1] = $"Main ({mainCount})";

        for (int i = 2; i < list.Count; i++) {
            string catName = list[i];
            int count = 0;
            if (database.AdditionalCategories != null) {
                foreach (var cat in database.AdditionalCategories) {
                    if (cat == null) continue;
                    string name = string.IsNullOrWhiteSpace(cat.CategoryName) ? "Uncategorized" : cat.CategoryName;
                    if (name.Equals(catName, StringComparison.OrdinalIgnoreCase)) {
                        count += (cat.Entries != null ? cat.Entries.Count : 0);
                    }
                }
            }
            labels[i] = $"{catName} ({count})";
        }
        return labels;
    }

    private IEnumerable<LocalizationEntry> GetEntriesForCategory(string categoryName) {
        if (categoryName == "Main") {
            return database.Entries ?? Enumerable.Empty<LocalizationEntry>();
        }
        if (database.AdditionalCategories != null) {
            foreach (var cat in database.AdditionalCategories) {
                if (cat == null) continue;
                string name = string.IsNullOrWhiteSpace(cat.CategoryName) ? "Uncategorized" : cat.CategoryName;
                if (name.Equals(categoryName, StringComparison.OrdinalIgnoreCase)) {
                    return cat.Entries ?? Enumerable.Empty<LocalizationEntry>();
                }
            }
        }
        return Enumerable.Empty<LocalizationEntry>();
    }

    private void RebuildCacheIfNeeded() {
        _cachedEntries.Clear();
        if (database == null) return;

        var categories = GetCategoryList();
        if (_selectedCategoryIndex >= categories.Count) _selectedCategoryIndex = 0;
        bool isAll = _selectedCategoryIndex == 0;
        string selectedCat = isAll ? "" : categories[_selectedCategoryIndex];

        var candidates = new List<DisplayEntry>();
        if (isAll || selectedCat == "Main") {
            if (database.Entries != null) {
                foreach (var e in database.Entries) {
                    if (e != null) candidates.Add(new DisplayEntry { Entry = e, Category = "Main" });
                }
            }
        }
        if (database.AdditionalCategories != null) {
            foreach (var cat in database.AdditionalCategories) {
                if (cat?.Entries == null) continue;
                string catName = string.IsNullOrWhiteSpace(cat.CategoryName) ? "Uncategorized" : cat.CategoryName;
                if (!isAll && !catName.Equals(selectedCat, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var e in cat.Entries) {
                    if (e != null) candidates.Add(new DisplayEntry { Entry = e, Category = catName });
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(_searchFilter)) {
            string filter = _searchFilter.Trim();
            foreach (var item in candidates) {
                var entry = item.Entry;
                if (entry == null) continue;
                bool matchKey = entry.Key != null && entry.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchEng = entry.EnglishText != null && entry.EnglishText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchContext = entry.Context != null && entry.Context.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                bool matchTrans = entry.Translations != null && entry.Translations.Any(t => t?.TranslatedText != null && t.TranslatedText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);

                if (matchKey || matchEng || matchContext || matchTrans) {
                    _cachedEntries.Add(item);
                }
            }
        } else {
            _cachedEntries.AddRange(candidates);
        }
    }

    private void DrawEntriesTable(bool showCategoryCol, IEnumerable<LocalizationEntry> currentEntries) {
        if (_cachedEntries == null || _cachedEntries.Count == 0) {
            EditorGUILayout.HelpBox("No entries match the current filter.", MessageType.Info);
            return;
        }

        int totalPages = Mathf.CeilToInt(_cachedEntries.Count / (float)PageSize);
        _page = Mathf.Clamp(_page, 0, totalPages - 1);

        // Pagination controls
        EditorGUILayout.BeginHorizontal();
        GUI.enabled = _page > 0;
        if (GUILayout.Button("<", GUILayout.Width(30))) _page--;
        GUI.enabled = true;
        GUILayout.Label($"Page {_page + 1} / {totalPages} ({_cachedEntries.Count} entries)", GUILayout.Width(180));
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
        if (showCategoryCol) {
            EditorGUILayout.LabelField("Category", EditorStyles.boldLabel, GUILayout.Width(CategoryColWidth));
        }
        EditorGUILayout.LabelField("Key", EditorStyles.boldLabel, GUILayout.Width(KeyColWidth));
        EditorGUILayout.LabelField("English Text", EditorStyles.boldLabel, GUILayout.Width(EngColWidth));
        foreach (var lang in database.TargetLanguages) {
            EditorGUILayout.BeginVertical(GUILayout.Width(LangColWidth));
            EditorGUILayout.LabelField(lang, EditorStyles.boldLabel, GUILayout.Width(LangColWidth));
            if (GUILayout.Button("Export", GUILayout.ExpandWidth(false)))
                LocalizationProcessor.ExportLanguageCSV(database, lang, currentEntries);
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
        int end   = Mathf.Min(start + PageSize, _cachedEntries.Count);
        for (int i = start; i < end; i++) {
            var item = _cachedEntries[i];
            var entry = item.Entry;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("✎", GUILayout.Width(30))) {
                LocalizationEntryEditorWindow.ShowWindow(entry, database);
            }
            if (showCategoryCol) {
                EditorGUILayout.LabelField(item.Category ?? "", GUILayout.Width(CategoryColWidth));
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
