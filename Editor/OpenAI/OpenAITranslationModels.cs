using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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

    public static OpenAIJson.Request CreateRequest(string customId, string model, ReasoningEffort effort,
        string systemPrompt, string userPrompt, OpenAIJson.ResponseFormat responseFormat = null, int maxCompletionTokens = MaxCompletionTokens) {
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

        return OpenAIJson.CreateRequest(customId, model, supportedEfforts.Length > 0 ? "developer" : "system",
            systemPrompt, userPrompt, supportedEfforts.Length > 0 ? effortName : null, responseFormat, maxCompletionTokens);
    }

    // Only complete, non-refused text can be imported, even when partial text is valid JSON.
    public static bool TryReadText(OpenAIJson.BatchResult result, out string text, out string error) {
        text = null;
        error = null;
        if (result.error != null) {
            error = result.error.message ?? "Batch request failed.";
            return false;
        }
        var response = result.response;
        var body = response?.body;
        if (response?.status_code != 200 || body == null || body.error != null) {
            error = body?.error?.message ?? "Missing or unsuccessful batch response.";
            return false;
        }
        var choice = body.choices?.FirstOrDefault();
        var message = choice?.message;
        string refusal = message?.refusal;
        if (choice?.finish_reason != "stop" || !string.IsNullOrEmpty(refusal)) {
            error = !string.IsNullOrEmpty(refusal) ? refusal : $"Incomplete response ({choice?.finish_reason}). Increase the token budget or reduce chunk size for token exhaustion.";
            return false;
        }
        var content = message?.content;
        if (string.IsNullOrWhiteSpace(content)) {
            error = "Response contains no translation text.";
            return false;
        }
        text = content;
        return true;
    }
}
