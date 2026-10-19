using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public static class SteamContentTranslation {
    public static string SourceHash(SteamContentCsv document, string source, LocalizationMasterDatabase database, string eventNotes) {
        string[] parts = { source, database.GameTitle, database.GameGenre, database.GameTone, database.GlobalContext, database.ProtectedNames,
            document.ContentType.ToString(), document.EnglishEventContext(), document.ContentType == SteamContentType.Event ? eventNotes : "" };
        string input = string.Concat(parts.Select(p => (p ?? "").Length + ":" + (p ?? "")));
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(input))).Replace("-", "");
    }

    public static List<(string Key, string Language, string Source)> GetPendingTranslations(SteamContentCsv document, LocalizationMasterDatabase database) {
        var targets = (database.TargetLanguages ?? new List<string>()).Select(SteamContentCsv.NormalizeLanguage).Distinct().ToList();
        if (targets.Count == 0) throw new InvalidOperationException("Select at least one target language in the master database.");
        foreach (var code in targets) {
            if (code == "english" || !SteamLanguageList.All.Any(l => l.langCode == code))
                throw new InvalidOperationException($"Invalid target language '{code}'. Select non-English languages from the language picker.");
        }
        // Arabic is available for game content, but not as a Steam UI language.
        // Keep the shared game-language selection intact and filter only store copy.
        if (document.ContentType == SteamContentType.StorePage) targets.Remove("arabic");
        var pending = new List<(string Key, string Language, string Source)>();
        foreach (var entry in document.Entries) {
            if (document.ContentType == SteamContentType.StorePage && entry.Key.StartsWith("app[content][sysreqs]", StringComparison.OrdinalIgnoreCase)) continue;
            if (!entry.Value.TryGetValue("english", out string source) || string.IsNullOrWhiteSpace(source)) continue;
            foreach (var code in targets) {
                if (entry.Value.TryGetValue(code, out string existing) && !string.IsNullOrWhiteSpace(existing)) continue;
                pending.Add((entry.Key, code, source));
            }
        }
        return pending;
    }

    public static List<OpenAIJson.Request> CreateRequests(SteamContentCsv document, LocalizationMasterDatabase database, string eventNotes) {
        var pending = GetPendingTranslations(document, database);
        string names = string.Join("\n", database.GetProtectedNames().Select(n => "- " + n));
        string eventContext = document.EventPromptContext();
        var requests = new List<OpenAIJson.Request>();
        foreach (var item in pending) {
            string language = SteamLanguageList.GetDisplayName(item.Language);
            string system = $@"You are a professional video game localizer translating Steam {(document.ContentType == SteamContentType.Event ? "event and announcement" : "store page")} content into {language}.
Game title: {database.GameTitle}
Game genre: {database.GameGenre}
Project tone: {database.GameTone}
Global context: {database.GlobalContext}

Guidelines:
1. Adapt idioms and phrasing naturally for native {language} readers. Use standard gaming terminology.
2. Preserve every BBCode tag exactly, including its attributes and order. Preserve all URLs, image references, and placeholders such as {{STEAM_CLAN_IMAGE}} exactly.
3. Preserve these names exactly wherever they occur; translate other feature names naturally:
{names}
4. Keep proper names and official brands such as Steam Next Fest recognizable; do not force-translate them merely to eliminate English words.
5. Preserve factual meaning, dates, prices, and numbers. Do not invent missing information.
6. Return only the translation of the requested field, without explanation, extra quotes, or code fences.";
            string user = document.ContentType == SteamContentType.Event
                ? $"Event notes (translation guidance):\n{eventNotes}\n\nFull English event (reference only; keep terminology consistent):\n{eventContext}\n\nTranslate only the '{item.Key}' field into {language}:\n\n{item.Source}"
                : $"Translate the '{item.Key}' field into {language}:\n\n{item.Source}";
            requests.Add(OpenAITranslationModels.CreateRequest(item.Key + "|" + item.Language, database.OpenAIModel, database.Effort,
                system, user, maxCompletionTokens: database.MaxCompletionTokens));
        }
        return requests;
    }
}
