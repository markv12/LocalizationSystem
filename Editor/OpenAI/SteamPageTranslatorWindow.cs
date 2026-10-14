using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public class SteamPageTranslatorWindow : EditorWindow {
    [SerializeField] private string _csvPath = "";
    [SerializeField] private string _batchId = "";
    private string _status = "Ready";
    private bool _isProcessing = false;
    [SerializeField] private string _gameTitle = "";
    [SerializeField] private string _gameContext = "";
    [SerializeField] private string _model = "gpt-6.1-sol";
    [SerializeField] private ReasoningEffort _effort = ReasoningEffort.Medium;
    [SerializeField] private int _maxCompletionTokens = OpenAITranslationModels.MaxCompletionTokens;
    [SerializeField] private string _submittedBatchId;
    [SerializeField] private List<SubmittedSource> _submittedSources = new List<SubmittedSource>();

    [Serializable]
    private class SubmittedSource {
        public string Key;
        public string Hash;
    }

    private string GetSourceHash(string englishText) => LocalizationProcessor.GenerateHash(englishText,
        OpenAIJson.SerializeSourceContext(_gameTitle, _gameContext));

    [MenuItem("Window/Localization/Steam Page Translator")]
    public static void ShowWindow() {
        GetWindow<SteamPageTranslatorWindow>("Steam Page Translator");
    }

    private void OnEnable() {
        var db = LocalizationMasterDatabase.LoadDatabase();
        if (db != null && string.IsNullOrEmpty(_gameContext)) {
            _gameContext = db.GlobalContext;
        }
    }

    private void OnGUI() {
        GUILayout.Label("Steam Page CSV Translator", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        _csvPath = EditorGUILayout.TextField("CSV Path", _csvPath);
        if (GUILayout.Button("Browse", GUILayout.Width(60))) {
            _csvPath = EditorUtility.OpenFilePanel("Select Steam CSV", Application.dataPath, "csv");
        }
        EditorGUILayout.EndHorizontal();

        _gameTitle = EditorGUILayout.TextField("Game Title", _gameTitle);
        if (GUILayout.Button($"OpenAI Model: {_model}", EditorStyles.popup)) {
            new OpenAIModelDropdown(new UnityEditor.IMGUI.Controls.AdvancedDropdownState(), model => _model = model)
                .Show(GUILayoutUtility.GetLastRect());
        }
        _effort = (ReasoningEffort)EditorGUILayout.EnumPopup("Reasoning Effort", _effort);
        _maxCompletionTokens = EditorGUILayout.IntField("Output Token Budget", _maxCompletionTokens);
        EditorGUILayout.LabelField("Game Context:");
        _gameContext = EditorGUILayout.TextArea(_gameContext, GUILayout.Height(50));

        _batchId = EditorGUILayout.TextField("Batch ID", _batchId);

        EditorGUILayout.Space(6);

        GUI.enabled = !_isProcessing && !string.IsNullOrEmpty(_csvPath);
        if (GUILayout.Button("Submit Batch")) {
            SubmitBatch();
        }

        GUI.enabled = !_isProcessing && !string.IsNullOrEmpty(_csvPath) && !string.IsNullOrEmpty(_batchId);
        if (GUILayout.Button("Check/Process Batch")) {
            CheckAndProcessBatch();
        }
        GUI.enabled = true;

        EditorGUILayout.Space(6);
        EditorGUILayout.HelpBox(_status, MessageType.Info);
    }

    private async void SubmitBatch() {
        _isProcessing = true;
        _status = "Submitting batch...";
        try {
            var data = CSVParser.LoadFromPath(_csvPath);
            var entries = ParseEntries(data);
            var apiKey = OpenAIBatchAPI.GetApiKey();
            if (string.IsNullOrEmpty(apiKey)) {
                _status = "Error: OPENAI_API_KEY missing in .env";
                return;
            }

            var requests = GenerateRequests(entries);
            var sources = entries.Where(e => e.Value.ContainsKey("english")).Select(e => new SubmittedSource {
                Key = e.Key, Hash = GetSourceHash(e.Value["english"])
            }).ToList();
            if (requests.Count == 0) {
                _status = "No translations needed.";
                return;
            }

            if (requests.Count > 50000) throw new InvalidOperationException("A batch supports at most 50,000 requests.");
            string jsonlPath = Path.Combine(Path.GetTempPath(), $"translation_batch_{Guid.NewGuid():N}.jsonl");
            try {
                File.WriteAllLines(jsonlPath, requests, new UTF8Encoding(false));
                if (new FileInfo(jsonlPath).Length > 200L * 1000 * 1000)
                    throw new InvalidOperationException("Batch input exceeds 200 MB.");
                string fileId = await OpenAIBatchAPI.UploadFileAsync(apiKey, jsonlPath);
                if (string.IsNullOrEmpty(fileId)) throw new InvalidOperationException("Batch upload failed; see Console.");
                string batchId = await OpenAIBatchAPI.CreateBatchAsync(apiKey, fileId);
                if (string.IsNullOrEmpty(batchId)) throw new InvalidOperationException("Batch creation failed; see Console.");
                _batchId = batchId;
                _submittedBatchId = batchId;
                _submittedSources = sources;
            } finally {
                if (File.Exists(jsonlPath)) File.Delete(jsonlPath);
            }
            _status = $"Batch submitted! ID: {_batchId}";
        } catch (Exception e) {
            _status = $"Error: {e.Message}";
        } finally {
            _isProcessing = false;
        }
    }

    private async void CheckAndProcessBatch() {
        _isProcessing = true;
        _status = "Checking batch status...";
        try {
            var apiKey = OpenAIBatchAPI.GetApiKey();
            if (string.IsNullOrEmpty(apiKey)) throw new InvalidOperationException("OPENAI_API_KEY missing in .env");
            var status = await OpenAIBatchAPI.CheckBatchStatusAsync(apiKey, _batchId);

            if (status == null) {
                _status = "Error: Batch not found.";
            } else if (status.Status == "completed" || status.Status == "expired" || status.Status == "cancelled") {
                if (string.IsNullOrEmpty(status.OutputFileId)) {
                    _status = $"Batch {status.Status}: no successful output. Failed: {status.Failed}.";
                    return;
                }
                _status = "Downloading results...";
                string resultContent = await OpenAIBatchAPI.DownloadFileContentAsync(apiKey, status.OutputFileId);
                if (resultContent == null) throw new InvalidOperationException("Result download failed; see Console.");
                var data = CSVParser.LoadFromPath(_csvPath);
                var entries = ParseEntries(data);
                int imported = MergeResults(data, entries, resultContent);
                _status = $"Batch {status.Status}: imported {imported} translations, failed {status.Failed}. Saved to Assets/SteamPageTranslationResult.csv. See Console for skipped responses.";
            } else {
                if (status.Errors != null && status.Errors.Count > 0) {
                    _status = $"Batch {status.Status}: {string.Join("; ", status.Errors)}";
                } else {
                    _status = $"Batch status: {status.Status}";
                }
            }
        } catch (Exception e) {
            _status = $"Error: {e.Message}";
        } finally {
            _isProcessing = false;
        }
    }

    private Dictionary<string, Dictionary<string, string>> ParseEntries(List<List<string>> data) {
        if (data == null || data.Count == 0) throw new InvalidDataException("Steam CSV is empty.");
        var headers = data[0];
        int keyIdx = headers.IndexOf("key");
        int langIdx = headers.IndexOf("language");
        int textIdx = headers.IndexOf("text");
        if (keyIdx < 0 || langIdx < 0 || textIdx < 0) throw new InvalidDataException("Steam CSV needs key, language, and text columns.");

        var entries = new Dictionary<string, Dictionary<string, string>>();
        for (int i = 1; i < data.Count; i++) {
            var row = data[i];
            if (row.Count <= Math.Max(keyIdx, Math.Max(langIdx, textIdx)))
                throw new InvalidDataException($"Steam CSV row {i + 1} is missing columns.");
            string key = row[keyIdx];
            string lang = row[langIdx];
            string text = row[textIdx];
            if (string.IsNullOrWhiteSpace(key) || key.Contains("|") || key.Length + 1 + SteamLanguageList.All.Max(l => l.langCode.Length) > 64)
                throw new InvalidDataException($"Steam CSV row {i + 1} has an invalid or overly long key for a batch custom_id.");

            lang = lang.Trim().ToLowerInvariant();
            if (!SteamLanguageList.All.Any(l => l.langCode == lang))
                throw new InvalidDataException($"Steam CSV row {i + 1} has unknown language '{lang}'.");
            if (!entries.ContainsKey(key)) entries[key] = new Dictionary<string, string>();
            if (entries[key].ContainsKey(lang)) throw new InvalidDataException($"Duplicate Steam CSV key/language: {key}/{lang}.");
            entries[key][lang] = text;
        }
        return entries;
    }

    private List<string> GenerateRequests(Dictionary<string, Dictionary<string, string>> entries) {
        var requests = new List<string>();

        foreach (var keyEntry in entries) {
            string englishText = keyEntry.Value.ContainsKey("english") ? keyEntry.Value["english"] : "";
            if (string.IsNullOrEmpty(englishText)) continue;

            foreach (var lang in SteamLanguageList.All) {
                if (lang.langCode == "english") continue;
                if (!keyEntry.Value.ContainsKey(lang.langCode) || string.IsNullOrEmpty(keyEntry.Value[lang.langCode])) {
                    var systemPrompt = $@"You are a professional video game localizer. Your goal is to localize Steam store content into {lang.displayName}.

Context: {_gameContext}

Guidelines:
1. ADAPTATION: Do not translate literally. If an English idiom or phrasing sounds stiff in {lang.displayName}, rewrite it to feel natural and native.
2. FORMATTING: You MUST preserve all BBCode tags exactly (e.g., [h2], [p], [b]). Do not translate the text inside the brackets.
3. TERMINOLOGY: Use standard gaming terminology for {lang.displayName}.
4. TRANSLATION OF NAMES: Translate names of game features into the target language so they feel integrated, UNLESS they are the specific game title '{_gameTitle}'.
5. NO ENGLISH: Ensure that NO English words remain in the translated text, except for the game title '{_gameTitle}'.
6. IDIOMS: Do not translate idioms literally. Use natural-sounding equivalents in {lang.displayName}.";
                    var req = OpenAITranslationModels.CreateRequest($"{keyEntry.Key}|{lang.langCode}", _model, _effort,
                        systemPrompt, $"Localize the following text into {lang.displayName}:\n\n{englishText}",
                        maxCompletionTokens: _maxCompletionTokens);
                    requests.Add(req.Json);
                }
            }
        }
        return requests;
    }

    private int MergeResults(List<List<string>> originalData, Dictionary<string, Dictionary<string, string>> entries, string jsonContent) {
        int imported = 0;
        var lines = jsonContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines) {
            try {
                var obj = OpenAIJson.ReadBatchResult(line);
                string customId = obj.custom_id;
                if (!OpenAITranslationModels.TryReadText(obj, out string translatedText, out string error)) {
                    Debug.LogWarning($"[Steam Translation] Skipped {customId}: {error}");
                    continue;
                }

                if (!string.IsNullOrEmpty(customId) && !string.IsNullOrEmpty(translatedText)) {
                    var parts = customId.Split('|');
                    if (parts.Length == 2) {
                        string key = parts[0];
                        string lang = parts[1];
                        if (_submittedBatchId == _batchId && !string.IsNullOrEmpty(_batchId)) {
                            var source = _submittedSources.Find(s => s.Key == key);
                            if (source == null || !entries.ContainsKey(key) || !entries[key].ContainsKey("english") ||
                                source.Hash != GetSourceHash(entries[key]["english"])) {
                                Debug.LogWarning($"[Steam Translation] Skipped stale or unexpected source: {key}.");
                                continue;
                            }
                        }
                        if (entries.ContainsKey(key) && lang != "english" && SteamLanguageList.All.Any(l => l.langCode == lang) &&
                            entries[key].ContainsKey("english") && OpenAITranslationModels.PreservesPlaceholders(entries[key]["english"], translatedText) &&
                            (!entries[key].ContainsKey(lang) || string.IsNullOrEmpty(entries[key][lang]))) {
                            entries[key][lang] = translatedText;
                            imported++;
                        }
                    }
                }
            } catch (Exception e) {
                Debug.LogWarning($"[Steam Translation] Malformed result line: {e.Message}");
            }
        }

        var newData = new List<List<string>> { originalData[0] };
        foreach (var keyEntry in entries) {
            foreach (var langEntry in keyEntry.Value) {
                var existingRow = originalData.Skip(1).FirstOrDefault(row =>
                    row[originalData[0].IndexOf("key")] == keyEntry.Key && row[originalData[0].IndexOf("language")] == langEntry.Key);
                var templateRow = existingRow ?? originalData.Skip(1).FirstOrDefault(row => row[originalData[0].IndexOf("key")] == keyEntry.Key);
                var rowCopy = templateRow != null ? new List<string>(templateRow) : new List<string>(originalData[0].ConvertAll(_ => ""));
                rowCopy[originalData[0].IndexOf("key")] = keyEntry.Key;
                rowCopy[originalData[0].IndexOf("language")] = langEntry.Key;
                rowCopy[originalData[0].IndexOf("text")] = langEntry.Value;
                newData.Add(rowCopy);
            }
        }

        string csv = CSVParser.WriteToString(newData, CSVParser.Delimiter.Comma);
        var encoding = new UTF8Encoding(true);
        File.WriteAllText(Path.Combine(Application.dataPath, "SteamPageTranslationResult.csv"), csv, encoding);
        AssetDatabase.Refresh();
        return imported;
    }
}
