using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public class SteamContentTranslatorWindow : EditorWindow {
    [SerializeField] private string _csvPath = "";
    [SerializeField] private string _batchId = "";
    [SerializeField] private SteamContentType _contentType;
    [SerializeField] private LocalizationMasterDatabase _database;
    [SerializeField] private string _eventNotes = "";
    [SerializeField] private string _submittedBatchId;
    [SerializeField] private List<SubmittedSource> _submittedSources = new List<SubmittedSource>();
    [SerializeField] private string _submittedPath;
    [SerializeField] private SteamContentType _submittedType;
    [SerializeField] private bool _hasDocumentSnapshot;
    private string _status = "Ready";
    private bool _isProcessing;
    private Vector2 _scroll;
    private SteamTranslationSettings Settings => _database.SteamTranslation;

    [Serializable]
    private class SubmittedSource {
        public string Key;
        public string Hash;
    }

    [MenuItem("Window/Localization/Steam Content Translator")]
    public static void ShowWindow() {
        GetWindow<SteamContentTranslatorWindow>("Steam Content Translator");
    }

    private void OnEnable() {
        titleContent = new GUIContent("Steam Content Translator");
        if (_database == null && AssetDatabase.FindAssets("t:LocalizationMasterDatabase").Length > 0)
            _database = LocalizationMasterDatabase.LoadDatabase();
    }

    private void OnGUI() {
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        using (new EditorGUI.DisabledScope(_isProcessing)) {
            GUILayout.Label("Steam Content Translator", EditorStyles.boldLabel);
            _database = (LocalizationMasterDatabase)EditorGUILayout.ObjectField("Master Database", _database, typeof(LocalizationMasterDatabase), false);
            if (_database == null) {
                EditorGUILayout.HelpBox("Select a Localization Master Database. Create one via Assets → Create → Localization → Master Database if needed.", MessageType.Info);
            } else {
                DrawTranslator();
            }
        }
        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(_status, MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    private void DrawTranslator() {
        _contentType = (SteamContentType)EditorGUILayout.EnumPopup("Content Type", _contentType);
        EditorGUILayout.HelpBox(_contentType == SteamContentType.Event
            ? "Use Steam's event CSV export with field, language, value columns."
            : "Use Steam's store page CSV export with key, language, text columns.", MessageType.Info);
        EditorGUILayout.BeginHorizontal();
        string path = EditorGUILayout.TextField("CSV Path", _csvPath);
        if (GUILayout.Button("Browse", GUILayout.Width(60))) {
            string selected = EditorUtility.OpenFilePanel("Select Steam CSV", string.IsNullOrEmpty(_csvPath) ? Application.dataPath : Path.GetDirectoryName(_csvPath), "csv");
            if (!string.IsNullOrEmpty(selected)) path = selected;
        }
        if (path != _csvPath) { _csvPath = path; _eventNotes = ""; }
        EditorGUILayout.EndHorizontal();

        var serialized = new SerializedObject(_database);
        serialized.Update();
        var settings = serialized.FindProperty("SteamTranslation");
        EditorGUILayout.PropertyField(settings.FindPropertyRelative("GameTitle"));
        EditorGUILayout.PropertyField(settings.FindPropertyRelative("GameContext"));
        EditorGUILayout.PropertyField(settings.FindPropertyRelative("ProtectedNames"));
        EditorGUILayout.PropertyField(settings.FindPropertyRelative("Model"));
        EditorGUILayout.PropertyField(settings.FindPropertyRelative("Effort"));
        EditorGUILayout.PropertyField(settings.FindPropertyRelative("OutputTokenBudget"));
        EditorGUILayout.PropertyField(settings.FindPropertyRelative("TargetLanguages"), true);
        serialized.ApplyModifiedProperties();
        AssetDatabase.SaveAssetIfDirty(_database);
        if (_contentType == SteamContentType.Event) {
            EditorGUILayout.LabelField("Event Notes (optional)");
            _eventNotes = EditorGUILayout.TextArea(_eventNotes, GUILayout.MinHeight(50));
        }
        EditorGUILayout.Space(6);
        _batchId = EditorGUILayout.TextField("Batch ID", _batchId);
        if (!string.IsNullOrEmpty(_batchId) && (_submittedBatchId != _batchId || !_hasDocumentSnapshot))
            EditorGUILayout.HelpBox("This batch has no saved submission snapshot. Source changes and original target requests cannot be verified.", MessageType.Warning);
        using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_csvPath))) {
            if (GUILayout.Button("Submit Batch")) SubmitBatch();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_batchId))) {
                if (GUILayout.Button("Check/Process Batch")) CheckAndProcessBatch();
            }
        }
    }

    private async void SubmitBatch() {
        _isProcessing = true;
        _status = "Submitting batch...";
        Repaint();
        try {
            string sourcePath = Path.GetFullPath(_csvPath);
            var type = _contentType;
            var document = new SteamContentCsv(CSVParser.LoadFromPath(sourcePath), type);
            var requests = SteamContentTranslation.CreateRequests(document, Settings, _eventNotes);
            if (requests.Count == 0) { _status = "No translations needed for the selected languages."; return; }
            if (requests.Count > 50000) throw new InvalidOperationException("A batch supports at most 50,000 requests.");
            var sources = requests.Select(r => new SubmittedSource {
                Key = r.CustomId,
                Hash = SteamContentTranslation.SourceHash(document, document.Entries[r.CustomId.Split('|')[0]]["english"], Settings, _eventNotes)
            }).ToList();
            var apiKey = OpenAIBatchAPI.GetApiKey();
            if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("OPENAI_API_KEY missing in .env");
            string jsonlPath = Path.Combine(Path.GetTempPath(), $"translation_batch_{Guid.NewGuid():N}.jsonl");
            try {
                File.WriteAllLines(jsonlPath, requests.Select(r => r.Json), new UTF8Encoding(false));
                if (new FileInfo(jsonlPath).Length > 200L * 1000 * 1000) throw new InvalidOperationException("Batch input exceeds 200 MB.");
                string fileId = await OpenAIBatchAPI.UploadFileAsync(apiKey, jsonlPath);
                if (string.IsNullOrEmpty(fileId)) throw new InvalidOperationException("Batch upload failed; see Console.");
                string batchId = await OpenAIBatchAPI.CreateBatchAsync(apiKey, fileId);
                if (string.IsNullOrEmpty(batchId)) throw new InvalidOperationException("Batch creation failed; see Console.");
                _batchId = batchId;
                _submittedBatchId = batchId;
                _submittedSources = sources;
                _submittedPath = sourcePath;
                _submittedType = type;
                _hasDocumentSnapshot = true;
            } finally {
                if (File.Exists(jsonlPath)) File.Delete(jsonlPath);
            }
            _status = $"Batch submitted! ID: {_batchId}";
        } catch (Exception e) { _status = $"Error: {e.Message}"; }
        finally { _isProcessing = false; Repaint(); }
    }

    private async void CheckAndProcessBatch() {
        _isProcessing = true;
        _status = "Checking batch status...";
        Repaint();
        try {
            string sourcePath = Path.GetFullPath(_csvPath);
            if (_hasDocumentSnapshot && _submittedBatchId == _batchId &&
                (_submittedType != _contentType || !string.Equals(_submittedPath, sourcePath, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Select the original submitted CSV and content type before processing this batch.");
            var apiKey = OpenAIBatchAPI.GetApiKey();
            if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("OPENAI_API_KEY missing in .env");
            var status = await OpenAIBatchAPI.CheckBatchStatusAsync(apiKey, _batchId);
            if (status == null) { _status = "Error: Batch not found."; return; }
            if (status.Status != "completed" && status.Status != "expired" && status.Status != "cancelled") {
                _status = status.Errors != null && status.Errors.Count > 0
                    ? $"Batch {status.Status}: {string.Join("; ", status.Errors)}" : $"Batch status: {status.Status}";
                return;
            }
            if (string.IsNullOrEmpty(status.OutputFileId)) {
                _status = $"Batch {status.Status}: no successful output. Failed: {status.Failed}.";
                return;
            }
            _status = "Downloading results...";
            Repaint();
            string resultContent = await OpenAIBatchAPI.DownloadFileContentAsync(apiKey, status.OutputFileId);
            if (resultContent == null) throw new InvalidOperationException("Result download failed; see Console.");
            var document = new SteamContentCsv(CSVParser.LoadFromPath(sourcePath), _contentType);
            int imported = MergeResults(document, resultContent);
            if (imported == 0) { _status = $"Batch {status.Status}: no new valid translations. Failed: {status.Failed}. See Console for skipped responses."; return; }
            string outputPath = EditorUtility.SaveFilePanel("Save translated Steam CSV", Path.GetDirectoryName(sourcePath), Path.GetFileNameWithoutExtension(sourcePath) + "_translated", "csv");
            if (string.IsNullOrEmpty(outputPath)) { _status = "Export cancelled. Process the batch again to export its results."; return; }
            if (string.Equals(Path.GetFullPath(outputPath), sourcePath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a different output filename to preserve the source CSV.");
            File.WriteAllText(outputPath, CSVParser.WriteToString(document.Export(), CSVParser.Delimiter.Comma), new UTF8Encoding(true));
            AssetDatabase.Refresh();
            _status = $"Batch {status.Status}: exported {imported} translations, failed {status.Failed}. Saved to {outputPath}. See Console for skipped responses.";
        } catch (Exception e) { _status = $"Error: {e.Message}"; }
        finally { _isProcessing = false; Repaint(); }
    }

    private int MergeResults(SteamContentCsv document, string jsonContent) {
        int imported = 0;
        foreach (string line in jsonContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            try {
                var result = OpenAIJson.ReadBatchResult(line);
                string id = result.custom_id;
                if (!OpenAITranslationModels.TryReadText(result, out string translation, out string error)) {
                    Debug.LogWarning($"[Steam Translation] Skipped {id}: {error}");
                    continue;
                }
                string[] parts = (id ?? "").Split('|');
                if (parts.Length != 2 || !document.Entries.TryGetValue(parts[0], out var languages) || !languages.TryGetValue("english", out string source)) {
                    Debug.LogWarning($"[Steam Translation] Unexpected result ID: {id}.");
                    continue;
                }
                if (_hasDocumentSnapshot && _submittedBatchId == _batchId && !string.IsNullOrEmpty(_batchId)) {
                    var snapshot = _submittedSources.Find(s => s.Key == id);
                    string hash = SteamContentTranslation.SourceHash(document, source, Settings, _eventNotes);
                    if (snapshot == null || snapshot.Hash != hash) {
                        Debug.LogWarning($"[Steam Translation] Skipped stale or unexpected source: {id}.");
                        continue;
                    }
                }
                if (!SteamContentTranslation.PreservesNames(source, translation, Settings, out error) ||
                    !document.TryAddTranslation(parts[0], parts[1], translation, out error)) {
                    Debug.LogWarning($"[Steam Translation] Skipped {id}: {error}");
                    continue;
                }
                imported++;
            } catch (Exception e) { Debug.LogWarning($"[Steam Translation] Malformed result line: {e.Message}"); }
        }
        return imported;
    }
}
