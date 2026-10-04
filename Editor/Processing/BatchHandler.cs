using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class BatchHandler {
    public const int ChunkSize = 20;
    private static readonly HashSet<LocalizationMasterDatabase> Submitting = new HashSet<LocalizationMasterDatabase>();

    private sealed class Submission {
        public LanguageTranslation Translation;
        public string RequestId;
        public string SourceHash;
    }

    public static List<object> BuildRequests(LocalizationMasterDatabase database, List<string> targetLanguages, out List<LanguageTranslation> pending, IEnumerable<LocalizationEntry> entriesToProcess = null) {
        return BuildRequests(database, targetLanguages, out pending, out _, entriesToProcess);
    }

    private static List<object> BuildRequests(LocalizationMasterDatabase database, List<string> targetLanguages,
        out List<LanguageTranslation> pending, out List<Submission> submissions, IEnumerable<LocalizationEntry> entriesToProcess) {
        if (database == null) throw new ArgumentNullException(nameof(database));
        if (targetLanguages == null) throw new ArgumentNullException(nameof(targetLanguages));
        var requests = new List<object>();
        pending = new List<LanguageTranslation>();
        submissions = new List<Submission>();
        var targetEntries = (entriesToProcess ?? database.AllEntries).Distinct().ToList();
        if (targetEntries.Any(e => e == null || string.IsNullOrWhiteSpace(e.Key)) ||
            targetEntries.Select(e => e.Key).Distinct(StringComparer.Ordinal).Count() != targetEntries.Count)
            throw new ArgumentException("Translation entries must have nonempty, unique keys.");

        foreach (var lang in targetLanguages.Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (string.IsNullOrWhiteSpace(lang) || lang.Contains("|") || lang.Length > 32)
                throw new ArgumentException("Target language must contain 1–32 characters and cannot contain '|'.");
            var toTranslate = new List<LocalizationEntry>();
            foreach (var entry in targetEntries) {
                var t = entry.GetTranslation(lang);
                if (t == null || (t.Status != TranslationStatus.Untranslated && t.Status != TranslationStatus.Dirty)) continue;
                toTranslate.Add(entry);
                pending.Add(t);
            }

            for (int i = 0; i < toTranslate.Count; i += ChunkSize) {
                var chunk = toTranslate.GetRange(i, Math.Min(ChunkSize, toTranslate.Count - i));
                var request = (JObject)CreateBatchRequestObject(database, lang, chunk, i / ChunkSize);
                requests.Add(request);
                foreach (var entry in chunk) {
                    submissions.Add(new Submission {
                        Translation = entry.GetTranslation(lang),
                        RequestId = request["custom_id"].Value<string>(),
                        SourceHash = LocalizationProcessor.GenerateHash(entry.EnglishText, entry.Context)
                    });
                }
            }
        }
        if (requests.Count > 50000) throw new ArgumentException("OpenAI batches support at most 50,000 requests. Submit fewer entries or languages.");
        return requests;
    }

    public static void SendBatchRequest(LocalizationMasterDatabase database, List<string> targetLanguages, IEnumerable<LocalizationEntry> entriesToProcess = null) {
        string apiKey = OpenAIBatchAPI.GetApiKey();
        if (string.IsNullOrEmpty(apiKey)) {
            Debug.LogError("[Localization] API Key missing in .env file (OPENAI_API_KEY).");
            return;
        }

        if (Submitting.Contains(database)) {
            Debug.LogWarning("[Localization] A batch submission for this database is already in progress.");
            return;
        }
        List<object> requests;
        List<Submission> submissions;
        try {
            requests = BuildRequests(database, targetLanguages, out _, out submissions, entriesToProcess);
        } catch (Exception e) {
            Debug.LogError($"[Localization] Cannot submit batch: {e.Message}");
            return;
        }

        if (requests.Count == 0) {
            Debug.LogWarning("[Localization] No strings to translate.");
            return;
        }

        Debug.Log($"[Localization] Initiating background upload for {requests.Count} chunks...");
        string tempPath = Path.Combine(Application.temporaryCachePath, $"batch_request_{Guid.NewGuid():N}.jsonl");
        Submitting.Add(database);

        Task.Run(async () => {
            try {
                File.WriteAllText(tempPath, string.Join("\n", requests.Select(r => JsonConvert.SerializeObject(r))));
                if (new FileInfo(tempPath).Length > 200L * 1000 * 1000)
                    throw new InvalidOperationException("OpenAI batch input exceeds 200 MB. Submit fewer entries or languages.");

                string fileId = await OpenAIBatchAPI.UploadFileAsync(apiKey, tempPath);
                if (string.IsNullOrEmpty(fileId)) return;

                string batchId = await OpenAIBatchAPI.CreateBatchAsync(apiKey, fileId);
                if (string.IsNullOrEmpty(batchId)) return;

                EditorMainThreadDispatcher.Enqueue(() => FinalizeBatchRequest(database, submissions, batchId));
            } catch (Exception e) {
                Debug.LogError($"[Localization] Background task error: {e.Message}");
            } finally {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch (IOException e) { Debug.LogWarning($"[Localization] Temporary batch cleanup failed: {e.Message}"); }
                EditorMainThreadDispatcher.Enqueue(() => Submitting.Remove(database));
            }
        });
    }

    private static void FinalizeBatchRequest(LocalizationMasterDatabase database, List<Submission> submissions, string batchId) {
        if (database == null) return;
        foreach (var submission in submissions) {
            var t = submission.Translation;
            if (t.Status != TranslationStatus.Untranslated && t.Status != TranslationStatus.Dirty) continue;
            t.Status = TranslationStatus.OutForBatch;
            t.SetBatchTracking(batchId, submission.RequestId, submission.SourceHash);
        }
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();
        Debug.Log($"<b>[Localization] Batch Submitted!</b> {submissions.Count} strings. Batch ID: {batchId}");
    }

    public static void CheckBatchStatus(LocalizationMasterDatabase database, string batchId) {
        string apiKey = OpenAIBatchAPI.GetApiKey();
        if (string.IsNullOrEmpty(apiKey)) {
            Debug.LogError("[Localization] API Key missing in .env file.");
            return;
        }

        Debug.Log($"[Localization] Checking status for batch {batchId}...");

        Task.Run(async () => {
            try {
                var result = await OpenAIBatchAPI.CheckBatchStatusAsync(apiKey, batchId);
                if (result == null) {
                    Debug.LogError($"[Localization] Failed to check status for batch {batchId}.");
                    return;
                }

                Debug.Log($"[Localization] Batch {batchId}: {result.Status} — Total: {result.Total}, Done: {result.Completed}, Failed: {result.Failed}");

                if (result.Errors != null && result.Errors.Count > 0) {
                    foreach (var err in result.Errors) {
                        Debug.LogError($"[Localization] Batch {batchId} validation/processing error: {err}");
                    }
                }

                if (result.Status != "completed" && result.Status != "failed" &&
                    result.Status != "expired" && result.Status != "cancelled") return;

                string content = null;
                if (!string.IsNullOrEmpty(result.OutputFileId)) {
                    content = await OpenAIBatchAPI.DownloadFileContentAsync(apiKey, result.OutputFileId);
                    // Keep pending entries intact if the download failed; a later check can retry.
                    if (content == null) return;
                }

                if (!string.IsNullOrEmpty(result.ErrorFileId)) {
                    string errContent = await OpenAIBatchAPI.DownloadFileContentAsync(apiKey, result.ErrorFileId);
                    if (errContent != null) LogBatchErrors(errContent);
                }
                EditorMainThreadDispatcher.Enqueue(() => {
                    if (database == null) return;
                    if (content != null) ApplyResultsToDatabase(database, content, batchId);
                    ResetUnresolvedTranslations(database, batchId);
                });
            } catch (Exception ex) {
                Debug.LogError($"[Localization] Exception while checking batch {batchId}: {ex.Message}");
            }
        });
    }

    private static void LogBatchErrors(string content) {
        foreach (var line in content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            try {
                var result = JObject.Parse(line);
                OpenAITranslationModels.TryReadText(result, out _, out string error);
                Debug.LogError($"[Localization] Request failed ({result["custom_id"]}): {error}");
            } catch (Exception e) {
                Debug.LogError($"[Localization] Malformed batch error line: {e.Message}");
            }
        }
    }

    private static void ApplyResultsToDatabase(LocalizationMasterDatabase database, string content, string batchId) {
        int successCount = 0;
        foreach (var line in content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            try {
                var result = JObject.Parse(line);
                string customId = result["custom_id"]?.Value<string>();
                if (string.IsNullOrEmpty(customId) || customId.IndexOf('|') <= 0) continue;
                string lang = customId.Split('|')[0];
                if (!OpenAITranslationModels.TryReadText(result, out string jsonResponse, out string error)) {
                    Debug.LogError($"[Localization] Request {customId}: {error}");
                    continue;
                }
                if (JObject.Parse(jsonResponse)["translations"] is JArray translations) {
                    var seenKeys = new HashSet<string>(StringComparer.Ordinal);
                    if (translations.Any(t => !(t is JObject) || t["key"]?.Type != JTokenType.String ||
                        t["text"]?.Type != JTokenType.String || !seenKeys.Add(t["key"].Value<string>())))
                        throw new InvalidDataException("Translation response has invalid or duplicate keys/text.");
                    foreach (var t in translations) {
                        string key = t["key"].Value<string>();
                        string text = t["text"].Value<string>();
                        var entry = database.FindEntry(key);
                        if (entry == null) continue;
                        var translation = entry.GetTranslation(lang);
                        if (translation == null) continue;
                        string sourceHash = LocalizationProcessor.GenerateHash(entry.EnglishText, entry.Context);
                        if (!translation.MatchesBatchRequest(batchId, customId, sourceHash)) continue;
                        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(entry.EnglishText)) continue;
                        if (!OpenAITranslationModels.PreservesPlaceholders(entry.EnglishText, text)) {
                            Debug.LogWarning($"[Localization] Skipped {key}: translation changed formatting placeholders.");
                            continue;
                        }
                        translation.TranslatedText = text;
                        translation.Status = TranslationStatus.Translated;
                        translation.SourceHash = sourceHash;
                        translation.ClearBatchTracking();
                        successCount++;
                    }
                }
            } catch (Exception e) {
                Debug.LogError($"[Localization] Failed to parse response: {e.Message}");
            }
        }

        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Localization] Results applied. {successCount} strings updated.");
    }

    private static void ResetUnresolvedTranslations(LocalizationMasterDatabase database, string batchId) {
        int count = LocalizationProcessor.ResetPendingTranslations(database, batchId: batchId);
        if (count > 0) Debug.LogWarning($"[Localization] {count} unresolved translations returned to Untranslated/Dirty for retry.");
    }

    public static object CreateBatchRequestObject(LocalizationMasterDatabase database, string lang, List<LocalizationEntry> entries, int chunkIndex) {
        if (entries == null || entries.Count == 0) throw new ArgumentException("A translation chunk cannot be empty.");
        var responseFormat = JObject.FromObject(new {
            type = "json_schema",
            json_schema = new {
                name = "translation_response",
                strict = true,
                schema = new {
                    type = "object",
                    properties = new {
                        translations = new {
                            type = "array",
                            items = new {
                                type = "object",
                                properties = new {
                                    key  = new { type = "string", @enum = entries.Select(e => e.Key).ToArray() },
                                    text = new { type = "string" }
                                },
                                required = new[] { "key", "text" },
                                additionalProperties = false
                            }
                        }
                    },
                    required = new[] { "translations" },
                    additionalProperties = false
                }
            }
        });
        return OpenAITranslationModels.CreateRequest($"{lang}|chunk_{chunkIndex}_{Guid.NewGuid().ToString("N").Substring(0, 16)}",
            database.OpenAIModel, database.Effort, ConstructSystemPrompt(database, lang), ConstructUserPrompt(entries), responseFormat, database.MaxCompletionTokens);
    }

    public static string ConstructSystemPrompt(LocalizationMasterDatabase database, string language) {
        var sb = new StringBuilder();
        sb.AppendLine($"You are an expert video game localizer specializing in the {language} market.");
        sb.AppendLine($"Game Genre: {database.GameGenre}");
        sb.AppendLine($"Project Tone: {database.GameTone}");
        sb.AppendLine($"Global Game Context: {database.GlobalContext}");
        sb.AppendLine();
        sb.AppendLine("CRITICAL RULES:");
        sb.AppendLine("1. PRESERVE PLACEHOLDERS: This project uses C# string formatting. You MUST preserve all placeholders like {0}, {1}, {n} exactly as they appear. Do not translate or modify anything inside the curly braces.");
        sb.AppendLine("2. USE CONTEXT: Each string may include a Context field describing exactly where and how it appears in the game UI. You MUST read and apply this context when choosing a translation. Context takes priority over a literal or default translation of the English word.");
        sb.AppendLine($"3. GAMER TERMINOLOGY: Use terminology that {language} players are accustomed to.");
        sb.AppendLine("4. STRICT JSON FORMAT: Return a JSON object with a translations array containing exactly one {key, text} object per input item. Copy each key exactly and translate only the text. Context is input guidance, not output. No markdown or commentary.");
        sb.AppendLine();
        if (language.Equals("arabic", StringComparison.OrdinalIgnoreCase)) {
            sb.AppendLine("ARABIC-SPECIFIC RULES:");
            sb.AppendLine("- TARGET DIALECT: Translate into Modern Standard Arabic (MSA).");
            sb.AppendLine("- RTL DISPLAY: The engine handles RTL rendering automatically. Write text in its natural logical order. Do NOT insert Unicode RTL/LTR control characters.");
            sb.AppendLine("- PLACEHOLDERS IN ARABIC: Keep all placeholders in Latin characters exactly as given (e.g. {0}, {1}). Never substitute Arabic-Indic numerals inside the brackets (e.g. never {٠}). Move the placeholder to the grammatically correct position in the sentence.");
            sb.AppendLine("- NEUTRAL UI PHRASING: Arabic grammar requires different forms for gender and for singular/dual/plural. Since placeholder values are unknown at runtime, avoid literal translations that commit to a specific form. Instead use colon-separated noun+value UI phrasing. Example — Input: \"You collected {0} apples.\" Good: \"التفاح المجموع: {0}\".");
        }

        sb.AppendLine($"Translate the following text into {language} with a focus on sounding natural to {language} players.");
        return sb.ToString();
    }

    public static string ConstructUserPrompt(List<LocalizationEntry> entries) {
        return JsonConvert.SerializeObject(new { items = entries.Select(entry => new {
            key = entry.Key, text = entry.EnglishText, context = entry.Context
        }) });
    }
}
