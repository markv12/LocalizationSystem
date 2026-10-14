using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public class BatchStatusResult {
    public string Status;
    public string OutputFileId;
    public string ErrorFileId;
    public int Total;
    public int Completed;
    public int Failed;
    public List<string> Errors = new List<string>();
}

public static class OpenAIBatchAPI {
    private const string FILES_URL = "https://api.openai.com/v1/files";
    private const string BATCH_URL = "https://api.openai.com/v1/batches";

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
