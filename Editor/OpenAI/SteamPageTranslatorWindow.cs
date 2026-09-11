using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public class SteamPageTranslatorWindow : EditorWindow {
    private string _csvPath = "";
    private string _batchId = "";
    private string _status = "Ready";
    private bool _isProcessing = false;
    private string _gameTitle = "";
    private string _gameContext = "";

    [MenuItem("Window/Localization/Steam Page Translator")]
    public static void ShowWindow() {
        GetWindow<SteamPageTranslatorWindow>("Steam Page Translator");
    }

    private void OnEnable() {
        var db = Resources.Load<LocalizationMasterDatabase>("LocalizationMasterDatabase");
        if (db != null) {
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
            if (requests.Count == 0) {
                _status = "No translations needed.";
                return;
            }

            string jsonlPath = Path.Combine(Path.GetTempPath(), "translation_batch.jsonl");
            File.WriteAllLines(jsonlPath, requests);

            string fileId = await OpenAIBatchAPI.UploadFileAsync(apiKey, jsonlPath);
            _batchId = await OpenAIBatchAPI.CreateBatchAsync(apiKey, fileId);
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
            var status = await OpenAIBatchAPI.CheckBatchStatusAsync(apiKey, _batchId);

            if (status == null) {
                _status = "Error: Batch not found.";
            } else if (status.Status == "completed") {
                _status = "Downloading results...";
                string resultContent = await OpenAIBatchAPI.DownloadFileContentAsync(apiKey, status.OutputFileId);
                var data = CSVParser.LoadFromPath(_csvPath);
                var entries = ParseEntries(data);
                MergeResults(data, entries, resultContent);
                _status = "Complete! Saved to Assets/SteamPageTranslationResult.csv";
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
        var headers = data[0];
        int keyIdx = headers.IndexOf("key");
        int langIdx = headers.IndexOf("language");
        int textIdx = headers.IndexOf("text");

        var entries = new Dictionary<string, Dictionary<string, string>>();
        for (int i = 1; i < data.Count; i++) {
            var row = data[i];
            string key = row[keyIdx];
            string lang = row[langIdx];
            string text = row[textIdx];

            if (!entries.ContainsKey(key)) entries[key] = new Dictionary<string, string>();
            if (!entries[key].ContainsKey(lang)) entries[key][lang] = text;
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
                    var req = new {
                        custom_id = $"{keyEntry.Key}|{lang.langCode}",
                        method = "POST",
                        url = "/v1/chat/completions",
                        body = new {
                            model = "gpt-5.5",
                            messages = new[] {
                                new {
                                    role = "system",
                                    content = $@"You are a professional video game localizer. Your goal is to localize Steam store content into {lang.displayName}.

Context: {_gameContext}

Guidelines:
1. ADAPTATION: Do not translate literally. If an English idiom or phrasing sounds stiff in {lang.displayName}, rewrite it to feel natural and native.
2. FORMATTING: You MUST preserve all BBCode tags exactly (e.g., [h2], [p], [b]). Do not translate the text inside the brackets.
3. TERMINOLOGY: Use standard gaming terminology for {lang.displayName}.
4. TRANSLATION OF NAMES: Translate names of game features into the target language so they feel integrated, UNLESS they are the specific game title '{_gameTitle}'.
5. NO ENGLISH: Ensure that NO English words remain in the translated text, except for the game title '{_gameTitle}'.
6. IDIOMS: Do not translate idioms literally. Use natural-sounding equivalents in {lang.displayName}."
                                },
                                new {
                                    role = "user",
                                    content = $"Localize the following text into {lang.displayName}:\n\n{englishText}"
                                }
                            },
                            reasoning_effort = "medium",
                        }
                    };
                    requests.Add(Newtonsoft.Json.JsonConvert.SerializeObject(req));
                }
            }
        }
        return requests;
    }

    private void MergeResults(List<List<string>> originalData, Dictionary<string, Dictionary<string, string>> entries, string jsonContent) {
        var lines = jsonContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines) {
            var obj = Newtonsoft.Json.Linq.JObject.Parse(line);
            string customId = obj["custom_id"]?.ToString();
            string translatedText = obj["response"]?["body"]?["choices"]?[0]?["message"]?["content"]?.ToString();

            if (!string.IsNullOrEmpty(customId) && !string.IsNullOrEmpty(translatedText)) {
                var parts = customId.Split('|');
                if (parts.Length == 2) {
                    string key = parts[0];
                    string lang = parts[1];
                    if (entries.ContainsKey(key)) {
                        entries[key][lang] = translatedText;
                    }
                }
            }
        }

        var newData = new List<List<string>> { originalData[0] };
        foreach (var keyEntry in entries) {
            foreach (var langEntry in keyEntry.Value) {
                newData.Add(new List<string> { originalData[1][0], keyEntry.Key, langEntry.Key, langEntry.Value });
            }
        }

        string csv = CSVParser.WriteToString(newData, CSVParser.Delimiter.Comma);
        var encoding = new UTF8Encoding(true);
        File.WriteAllText(Path.Combine(Application.dataPath, "SteamPageTranslationResult.csv"), csv, encoding);
        AssetDatabase.Refresh();
    }
}
