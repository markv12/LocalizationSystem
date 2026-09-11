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
    private const int MaxCompletionTokens = 16000;
    public const int ChunkSize = 20;

    public static List<object> BuildRequests(LocalizationMasterDatabase database, List<string> targetLanguages, out List<LanguageTranslation> pending, IEnumerable<LocalizationEntry> entriesToProcess = null) {
        var requests = new List<object>();
        pending = new List<LanguageTranslation>();
        var targetEntries = entriesToProcess ?? database.AllEntries;

        foreach (var lang in targetLanguages) {
            var toTranslate = new List<LocalizationEntry>();
            foreach (var entry in targetEntries) {
                var t = entry.GetTranslation(lang);
                if (t == null || (t.Status != TranslationStatus.Untranslated && t.Status != TranslationStatus.Dirty)) continue;
                toTranslate.Add(entry);
                pending.Add(t);
            }

            for (int i = 0; i < toTranslate.Count; i += ChunkSize) {
                var chunk = toTranslate.GetRange(i, Math.Min(ChunkSize, toTranslate.Count - i));
                requests.Add(CreateBatchRequestObject(database, lang, chunk, i / ChunkSize));
            }
        }

        return requests;
    }

    public static void SendBatchRequest(LocalizationMasterDatabase database, List<string> targetLanguages, IEnumerable<LocalizationEntry> entriesToProcess = null) {
        string apiKey = OpenAIBatchAPI.GetApiKey();
        if (string.IsNullOrEmpty(apiKey)) {
            Debug.LogError("[Localization] API Key missing in .env file (OPENAI_API_KEY).");
            return;
        }

        var requests = BuildRequests(database, targetLanguages, out List<LanguageTranslation> pending, entriesToProcess);

        if (requests.Count == 0) {
            Debug.LogWarning("[Localization] No strings to translate.");
            return;
        }

        Debug.Log($"[Localization] Initiating background upload for {requests.Count} chunks...");
        string tempPath = Path.Combine(Application.temporaryCachePath, "batch_request.jsonl");

        Task.Run(async () => {
            try {
                File.WriteAllText(tempPath, string.Join("\n", requests.Select(r => JsonConvert.SerializeObject(r))));

                string fileId = await OpenAIBatchAPI.UploadFileAsync(apiKey, tempPath);
                if (string.IsNullOrEmpty(fileId)) return;

                string batchId = await OpenAIBatchAPI.CreateBatchAsync(apiKey, fileId);
                if (string.IsNullOrEmpty(batchId)) return;

                EditorMainThreadDispatcher.Enqueue(() => FinalizeBatchRequest(database, pending, batchId));
            } catch (Exception e) {
                Debug.LogError($"[Localization] Background task error: {e.Message}");
            }
        });
    }

    private static void FinalizeBatchRequest(LocalizationMasterDatabase database, List<LanguageTranslation> pending, string batchId) {
        foreach (var t in pending) {
            t.Status = TranslationStatus.OutForBatch;
            t.BatchID = batchId;
        }
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssets();
        Debug.Log($"<b>[Localization] Batch Submitted!</b> {pending.Count} strings. Batch ID: {batchId}");
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

                if (result.Status != "completed") return;

                if (!string.IsNullOrEmpty(result.OutputFileId)) {
                    string content = await OpenAIBatchAPI.DownloadFileContentAsync(apiKey, result.OutputFileId);
                    if (content != null)
                        EditorMainThreadDispatcher.Enqueue(() => ApplyResultsToDatabase(database, content));
                }

                if (!string.IsNullOrEmpty(result.ErrorFileId)) {
                    string errContent = await OpenAIBatchAPI.DownloadFileContentAsync(apiKey, result.ErrorFileId);
                    if (errContent != null) LogBatchErrors(errContent);
                }
            } catch (Exception ex) {
                Debug.LogError($"[Localization] Exception while checking batch {batchId}: {ex.Message}");
            }
        });
    }

    private static void LogBatchErrors(string content) {
        foreach (var line in content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            var result = JObject.Parse(line);
            string customId = result["custom_id"]?.ToString();
            var error = result["response"]?["body"]?["error"];
            Debug.LogError($"[Localization] Request failed ({customId}): {error?["message"]}");
        }
    }

    private static void ApplyResultsToDatabase(LocalizationMasterDatabase database, string content) {
        int successCount = 0;
        foreach (var line in content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            var result = JObject.Parse(line);
            string customId = result["custom_id"]?.ToString();
            string lang = customId?.Split('|')[0];

            string jsonResponse = result["response"]?["body"]?["choices"]?[0]?["message"]?["content"]?.ToString();
            if (string.IsNullOrEmpty(jsonResponse)) continue;

            try {
                if (JObject.Parse(jsonResponse)["translations"] is JArray translations) {
                    foreach (var t in translations) {
                        string key = t["key"]?.ToString();
                        string text = t["text"]?.ToString();
                        var entry = database.FindEntry(key);
                        if (entry == null) continue;
                        var translation = entry.GetTranslation(lang);
                        if (translation == null) continue;
                        translation.TranslatedText = text?.Trim();
                        translation.Status = TranslationStatus.Translated;
                        translation.SourceHash = LocalizationProcessor.GenerateHash(entry.EnglishText, entry.Context);
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

    public static object CreateBatchRequestObject(LocalizationMasterDatabase database, string lang, List<LocalizationEntry> entries, int chunkIndex) {
        return new {
            custom_id = $"{lang}|chunk_{chunkIndex}_{Guid.NewGuid().ToString().Substring(0, 8)}",
            method = "POST",
            url = "/v1/chat/completions",
            body = new {
                model = database.OpenAIModel,
                messages = new[] {
                    new { role = "system", content = ConstructSystemPrompt(database, lang) },
                    new { role = "user",   content = ConstructUserPrompt(entries) }
                },
                response_format = new {
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
                                            key  = new { type = "string" },
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
                },
                max_completion_tokens = MaxCompletionTokens,
                reasoning_effort = database.Effort.ToString().ToLowerInvariant(),
            }
        };
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
        sb.AppendLine("3. GAMER TERMINOLOGY: Use terminology that {language} players are accustomed to.");
        sb.AppendLine("4. STRICT JSON FORMAT: Return ONLY valid JSON. You must maintain the exact same JSON keys and structure as the input. Translate ONLY the string values. Do NOT wrap the output in markdown code blocks (e.g., no ```json). No commentary, no explanations.");
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
        var sb = new StringBuilder();
        sb.AppendLine("Translate the following items:");
        foreach (var entry in entries) {
            sb.AppendLine($"- Key: \"{entry.Key}\", Text: \"{entry.EnglishText}\"");
            if (!string.IsNullOrEmpty(entry.Context))
                sb.AppendLine($"  Context: {entry.Context}");
        }
        return sb.ToString();
    }
}
