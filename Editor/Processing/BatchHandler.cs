using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

    public static List<OpenAIJson.Request> BuildRequests(LocalizationMasterDatabase database, List<string> targetLanguages, out List<LanguageTranslation> pending, IEnumerable<LocalizationEntry> entriesToProcess = null) {
        return BuildRequests(database, targetLanguages, out pending, out _, entriesToProcess);
    }

    private static List<OpenAIJson.Request> BuildRequests(LocalizationMasterDatabase database, List<string> targetLanguages,
        out List<LanguageTranslation> pending, out List<Submission> submissions, IEnumerable<LocalizationEntry> entriesToProcess) {
        if (database == null) throw new ArgumentNullException(nameof(database));
        if (targetLanguages == null) throw new ArgumentNullException(nameof(targetLanguages));
        var requests = new List<OpenAIJson.Request>();
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
                var request = CreateBatchRequestObject(database, lang, chunk, i / ChunkSize);
                requests.Add(request);
                foreach (var entry in chunk) {
                    submissions.Add(new Submission {
                        Translation = entry.GetTranslation(lang),
                        RequestId = request.CustomId,
                        SourceHash = LocalizationProcessor.GenerateHash(entry.EnglishText, entry.Context)
                    });
                }
            }
        }
        if (requests.Count > OpenAIBatchAPI.MaxRequests) throw new ArgumentException("OpenAI batches support at most 50,000 requests. Submit fewer entries or languages.");
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
        List<OpenAIJson.Request> requests;
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
                string batchId = await OpenAIBatchAPI.SubmitRequestsAsync(apiKey, requests, tempPath, "[Localization]");

                EditorMainThreadDispatcher.Enqueue(() => FinalizeBatchRequest(database, submissions, batchId));
            } catch (Exception e) {
                Debug.LogError($"[Localization] Background task error: {e.Message}");
            } finally {
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

                var results = await OpenAIBatchAPI.DownloadBatchResultsAsync(apiKey, result, $"[Localization] Batch {batchId}:");
                if (!results.CanProcess) return;

                EditorMainThreadDispatcher.Enqueue(() => {
                    if (database == null) return;
                    if (results.Content != null) ApplyResultsToDatabase(database, results.Content, batchId);
                    ResetUnresolvedTranslations(database, batchId);
                });
            } catch (Exception ex) {
                Debug.LogError($"[Localization] Exception while checking batch {batchId}: {ex.Message}");
            }
        });
    }

    private static void ApplyResultsToDatabase(LocalizationMasterDatabase database, string content, string batchId) {
        int successCount = 0;
        foreach (var line in content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            try {
                var result = OpenAIJson.ReadBatchResult(line);
                string customId = result.custom_id;
                if (string.IsNullOrEmpty(customId) || customId.IndexOf('|') <= 0) continue;
                string lang = customId.Split('|')[0];
                if (!OpenAITranslationModels.TryReadText(result, out string jsonResponse, out string error)) {
                    Debug.LogError($"[Localization] Request {customId}: {error}");
                    continue;
                }
                var translations = OpenAIJson.ReadTranslations(jsonResponse);
                if (translations != null) {
                    foreach (var t in translations) {
                        string key = t.key;
                        string text = t.text;
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
                        if (!database.PreservesProtectedNames(entry.EnglishText, text, out string nameError)) {
                            Debug.LogWarning($"[Localization] Skipped {key}: {nameError}");
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

    public static OpenAIJson.Request CreateBatchRequestObject(LocalizationMasterDatabase database, string lang, List<LocalizationEntry> entries, int chunkIndex) {
        if (entries == null || entries.Count == 0) throw new ArgumentException("A translation chunk cannot be empty.");
        var responseFormat = new OpenAIJson.ResponseFormat();
        responseFormat.json_schema.schema.properties.translations.items.properties.key.@enum = entries.Select(e => e.Key).ToArray();
        return OpenAITranslationModels.CreateRequest($"{lang}|chunk_{chunkIndex}_{Guid.NewGuid().ToString("N").Substring(0, 16)}",
            database.OpenAIModel, database.Effort, ConstructSystemPrompt(database, lang), ConstructUserPrompt(entries), responseFormat, database.MaxCompletionTokens);
    }

    public static string ConstructSystemPrompt(LocalizationMasterDatabase database, string language) {
        var sb = new StringBuilder();
        sb.AppendLine($"You are an expert video game localizer specializing in the {language} market.");
        sb.AppendLine($"Game Title: {database.GameTitle}");
        sb.AppendLine($"Game Genre: {database.GameGenre}");
        sb.AppendLine($"Project Tone: {database.GameTone}");
        sb.AppendLine($"Global Game Context: {database.GlobalContext}");
        sb.AppendLine("Preserve the following names exactly wherever they occur. Other feature names may be translated:");
        foreach (string name in database.GetProtectedNames()) sb.AppendLine("- " + name);
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
        return OpenAIJson.SerializePrompt(new OpenAIJson.Prompt { items = entries.Select(entry => new OpenAIJson.PromptItem {
            key = entry.Key, text = entry.EnglishText, context = entry.Context
        }).ToArray() });
    }
}
