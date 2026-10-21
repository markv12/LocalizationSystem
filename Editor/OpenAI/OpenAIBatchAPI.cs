using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public class BatchStatusResult {
    public string Status;
    public string OutputFileId;
    public string ErrorFileId;
    public int Total;
    public int Completed;
    public int Failed;
    public List<string> Errors = new List<string>();

    public bool IsTerminal => Status == "completed" || Status == "failed" || Status == "expired" || Status == "cancelled";
}

public static class OpenAIBatchAPI {
    internal const int MaxRequests = 50000;
    internal const long MaxInputBytes = 200L * 1000 * 1000;
    private const string FILES_URL = "https://api.openai.com/v1/files";
    private const string BATCH_URL = "https://api.openai.com/v1/batches";

    internal sealed class BatchResults {
        public BatchStatusResult Status { get; }
        public string Content { get; }

        // No output is valid for an all-failed batch. A failed download must remain retryable.
        public bool CanProcess => Status.IsTerminal && (string.IsNullOrEmpty(Status.OutputFileId) || Content != null);

        internal BatchResults(BatchStatusResult status, string content) { Status = status; Content = content; }
    }

    internal static Task<string> SubmitRequestsAsync(string apiKey, IReadOnlyCollection<OpenAIJson.Request> requests,
        string tempPath, string logPrefix) {
        return SubmitRequestsAsync(requests, tempPath, logPrefix,
            path => UploadFileAsync(apiKey, path), fileId => CreateBatchAsync(apiKey, fileId));
    }

    internal static async Task<string> SubmitRequestsAsync(IReadOnlyCollection<OpenAIJson.Request> requests,
        string tempPath, string logPrefix, Func<string, Task<string>> upload, Func<string, Task<string>> createBatch) {
        if (requests.Count == 0 || requests.Count > MaxRequests)
            throw new ArgumentException("OpenAI batches require 1–50,000 requests.");
        try {
            File.WriteAllLines(tempPath, requests.Select(r => r.Json), new UTF8Encoding(false));
            if (new FileInfo(tempPath).Length > MaxInputBytes)
                throw new InvalidOperationException("Batch input exceeds 200 MB.");
            string fileId = await upload(tempPath);
            if (string.IsNullOrEmpty(fileId)) throw new InvalidOperationException("Batch upload failed; see Console.");
            string batchId = await createBatch(fileId);
            if (string.IsNullOrEmpty(batchId)) throw new InvalidOperationException("Batch creation failed; see Console.");
            return batchId;
        } finally {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (IOException e) { Debug.LogWarning($"{logPrefix} Temporary batch cleanup failed: {e.Message}"); }
        }
    }

    internal static Task<BatchResults> DownloadBatchResultsAsync(string apiKey, BatchStatusResult status,
        string logPrefix, Action onDownloadingOutput = null) {
        return DownloadBatchResultsAsync(status, logPrefix,
            fileId => DownloadFileContentAsync(apiKey, fileId), onDownloadingOutput);
    }

    internal static async Task<BatchResults> DownloadBatchResultsAsync(BatchStatusResult status, string logPrefix,
        Func<string, Task<string>> download, Action onDownloadingOutput = null) {
        if (status.Errors != null) {
            foreach (string error in status.Errors) Debug.LogError($"{logPrefix} {error}");
        }
        string content = null;
        if (status.IsTerminal) {
            // Report request failures even when downloading successful results fails.
            if (!string.IsNullOrEmpty(status.ErrorFileId)) {
                string errors = await download(status.ErrorFileId);
                if (errors != null) LogBatchErrors(errors, logPrefix);
            }
            if (!string.IsNullOrEmpty(status.OutputFileId)) {
                onDownloadingOutput?.Invoke();
                content = await download(status.OutputFileId);
            }
        }
        return new BatchResults(status, content);
    }

    public static string GetEnvVar(string key) {
        string envPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".env"));
        if (!File.Exists(envPath)) {
            envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        }
        if (File.Exists(envPath)) {
            foreach (var line in File.ReadAllLines(envPath)) {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("#") || !trimmed.Contains("=")) continue;
                var parts = trimmed.Split(new[] { '=' }, 2);
                if (parts[0].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                    return parts[1].Trim();
            }
        }
        return null;
    }

    public static string GetApiKey() {
        string key = GetEnvVar("OPENAI_API_KEY");
        if (!string.IsNullOrEmpty(key) && key.StartsWith("sk-") && !key.StartsWith("sk-proj-") && string.IsNullOrEmpty(GetProjectId())) {
            Debug.LogWarning("[OpenAI] Using a legacy User API key (sk-...) without an explicit OPENAI_PROJECT_ID. " +
                             "If you encounter 'Cannot find file, or organization does not have access to it', " +
                             "create a Project API key (sk-proj-...) in the OpenAI Dashboard or specify OPENAI_PROJECT_ID in .env.");
        }
        return key;
    }

    public static string GetOrganizationId() => GetEnvVar("OPENAI_ORG_ID") ?? GetEnvVar("OPENAI_ORGANIZATION");
    public static string GetProjectId() => GetEnvVar("OPENAI_PROJECT_ID") ?? GetEnvVar("OPENAI_PROJECT");

    private static HttpClient CreateClient(string apiKey) {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        string orgId = GetOrganizationId();
        if (!string.IsNullOrEmpty(orgId)) {
            client.DefaultRequestHeaders.Add("OpenAI-Organization", orgId);
        }

        string projectId = GetProjectId();
        if (!string.IsNullOrEmpty(projectId)) {
            client.DefaultRequestHeaders.Add("OpenAI-Project", projectId);
        }

        return client;
    }

    public static async Task<string> UploadFileAsync(string apiKey, string filePath) {
        using (var client = CreateClient(apiKey)) {
            using (var form = new MultipartFormDataContent()) {
                var fileContent = new ByteArrayContent(File.ReadAllBytes(filePath));
                fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse("application/jsonl");
                form.Add(fileContent, "file", Path.GetFileName(filePath));
                form.Add(new StringContent("batch"), "purpose");

                var response = await client.PostAsync(FILES_URL, form);
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                    return OpenAIJson.ReadFileId(body);
                Debug.LogError($"[OpenAI] Upload failed: {response.StatusCode}\n{body}");
                return null;
            }
        }
    }

    public static async Task<string> CreateBatchAsync(string apiKey, string fileId) {
        using (var client = CreateClient(apiKey)) {
            var payload = JsonUtility.ToJson(new OpenAIJson.CreateBatch {
                input_file_id = fileId,
                endpoint = OpenAITranslationModels.Endpoint,
                completion_window = "24h"
            });
            var response = await client.PostAsync(BATCH_URL,
                new StringContent(payload, Encoding.UTF8, "application/json"));
            var body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                return OpenAIJson.ReadFileId(body);
            Debug.LogError($"[OpenAI] Batch creation failed: {response.StatusCode}\n{body}");
            return null;
        }
    }

    public static async Task<BatchStatusResult> CheckBatchStatusAsync(string apiKey, string batchId) {
        using (var client = CreateClient(apiKey)) {
            var response = await client.GetAsync($"{BATCH_URL}/{batchId}");
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) {
                Debug.LogError($"[OpenAI] Status check failed: {response.StatusCode}\n{body}");
                return null;
            }
            return ParseBatchStatus(body);
        }
    }

    private static BatchStatusResult ParseBatchStatus(string body) {
        var json = OpenAIJson.ReadBatchStatus(body);
        var counts = json.request_counts;
        var statusResult = new BatchStatusResult {
            Status       = json.status,
            OutputFileId = json.output_file_id,
            ErrorFileId  = json.error_file_id,
            Total        = counts?.total ?? 0,
            Completed    = counts?.completed ?? 0,
            Failed       = counts?.failed ?? 0,
        };
        if (json.errors?.data != null) {
            foreach (var err in json.errors.data) {
                string msg = err?.message;
                if (!string.IsNullOrEmpty(msg))
                    statusResult.Errors.Add(msg);
            }
        }
        return statusResult;
    }

    internal static void LogBatchErrors(string content, string logPrefix) {
        foreach (var line in content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            try {
                var result = OpenAIJson.ReadBatchResult(line);
                if (OpenAITranslationModels.TryReadText(result, out _, out string error)) continue;
                string httpStatus = result.response != null ? $", HTTP {result.response.status_code}" : "";
                Debug.LogError($"{logPrefix} Request failed ({result.custom_id}{httpStatus}): {error}");
            } catch (Exception e) {
                Debug.LogError($"{logPrefix} Malformed batch error line: {e.Message}");
            }
        }
    }

    public static async Task<string> DownloadFileContentAsync(string apiKey, string fileId) {
        using (var client = CreateClient(apiKey)) {
            var response = await client.GetAsync($"{FILES_URL}/{fileId}/content");
            var body = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
                return body;
            Debug.LogError($"[OpenAI] File download failed: {response.StatusCode}\n{body}");
            return null;
        }
    }
}
