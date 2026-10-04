using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

// Text-only Chat Completions + Structured Outputs capabilities, verified 2026-10-04.
// Do not infer capabilities from a model prefix: new models can have different contracts.
public static class OpenAITranslationModels {
    public const string Endpoint = "/v1/chat/completions";
    public const string DefaultModel = "gpt-6-luna";
    public const int MaxCompletionTokens = 16000; // Includes reasoning tokens.

    private static readonly Dictionary<string, string[]> Models = new Dictionary<string, string[]> {
        { "gpt-6-astra", new[] { "low", "medium", "high", "xhigh", "max" } },
        { "gpt-6.1-sol", new[] { "low", "medium", "high", "xhigh", "max" } },
        { "gpt-6-sol", new[] { "none", "low", "medium", "high", "xhigh", "max" } },
        { "gpt-6-luna", new[] { "none", "low", "medium", "high", "xhigh", "max" } },
        { "gpt-5.6-luna", new[] { "none", "low", "medium", "high", "xhigh", "max" } },
        { "gpt-5.6-terra", new[] { "none", "low", "medium", "high", "xhigh", "max" } },
        { "gpt-5.6-sol", new[] { "none", "low", "medium", "high", "xhigh", "max" } },
        { "gpt-5.5", new[] { "none", "low", "medium", "high", "xhigh" } },
        { "gpt-5.2", new[] { "none", "low", "medium", "high", "xhigh" } },
        { "gpt-4o", Array.Empty<string>() },
        { "gpt-4o-mini", Array.Empty<string>() }
    };

    public static IEnumerable<string> ModelIds => Models.Keys;

    private static readonly Regex Placeholders = new Regex(@"\{(?:[0-9]+|[A-Za-z_][A-Za-z0-9_]*)(?:\s*,\s*-?[0-9]+)?(?::[^{}]*)?\}");

    public static bool PreservesPlaceholders(string source, string translation) {
        return Placeholders.Matches(source ?? "").Cast<Match>().Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal)
            .SequenceEqual(Placeholders.Matches(translation ?? "").Cast<Match>().Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal));
    }

    public static JObject CreateRequest(string customId, string model, ReasoningEffort effort,
        string systemPrompt, string userPrompt, JObject responseFormat = null, int maxCompletionTokens = MaxCompletionTokens) {
        model = model?.Trim();
        if (string.IsNullOrEmpty(model) || !Models.TryGetValue(model, out var supportedEfforts))
            throw new ArgumentException($"Unsupported translation model '{model}'. Select a model from the OpenAI model picker.");
        if (string.IsNullOrWhiteSpace(customId) || customId.Length > 64)
            throw new ArgumentException("Batch custom_id must contain 1–64 characters.");
        if (!Enum.IsDefined(typeof(ReasoningEffort), effort))
            throw new ArgumentException("Invalid reasoning effort.");
        int outputLimit = supportedEfforts.Length == 0 ? 16384 : 128000;
        if (maxCompletionTokens <= 0 || maxCompletionTokens > outputLimit)
            throw new ArgumentException($"Model '{model}' requires an output budget between 1 and {outputLimit} tokens (including reasoning).");
        string effortName = effort.ToString().ToLowerInvariant();
        if (supportedEfforts.Length > 0 && !supportedEfforts.Contains(effortName))
            throw new ArgumentException($"Model '{model}' does not support reasoning effort '{effortName}'. Supported: {string.Join(", ", supportedEfforts)}.");

        var body = new JObject {
            ["model"] = model,
            ["messages"] = new JArray {
                new JObject { ["role"] = supportedEfforts.Length > 0 ? "developer" : "system", ["content"] = systemPrompt },
                new JObject { ["role"] = "user", ["content"] = userPrompt }
            },
            ["max_completion_tokens"] = maxCompletionTokens
        };
        if (supportedEfforts.Length > 0) body["reasoning_effort"] = effortName;
        if (responseFormat != null) body["response_format"] = responseFormat;
        return new JObject { ["custom_id"] = customId, ["method"] = "POST", ["url"] = Endpoint, ["body"] = body };
    }

    // Only complete, non-refused text can be imported, even when partial text is valid JSON.
    public static bool TryReadText(JObject result, out string text, out string error) {
        text = null;
        error = null;
        if (result["error"] is JObject requestError) {
            error = requestError["message"]?.ToString() ?? requestError.ToString();
            return false;
        }
        var response = result["response"] as JObject;
        var body = response?["body"] as JObject;
        if (response?["status_code"]?.Value<int>() != 200 || body == null || body["error"] is JObject) {
            error = body?["error"]?["message"]?.ToString() ?? "Missing or unsuccessful batch response.";
            return false;
        }
        var choice = (body["choices"] as JArray)?.FirstOrDefault() as JObject;
        var message = choice?["message"] as JObject;
        string refusal = message?["refusal"]?.ToString();
        if (choice?["finish_reason"]?.ToString() != "stop" || !string.IsNullOrEmpty(refusal)) {
            error = !string.IsNullOrEmpty(refusal) ? refusal : $"Incomplete response ({choice?["finish_reason"]}). Increase the token budget or reduce chunk size for token exhaustion.";
            return false;
        }
        var content = message?["content"];
        if (content?.Type != JTokenType.String || string.IsNullOrWhiteSpace(content.Value<string>())) {
            error = "Response contains no translation text.";
            return false;
        }
        text = content.Value<string>();
        return true;
    }
}
