using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public enum SteamContentType { StorePage, Event }

// Both Steam formats share one internal field/language/text representation.
// Original rows remain intact so exports preserve metadata, spelling, and order.
public sealed class SteamContentCsv {
    private readonly List<List<string>> _rows;
    private readonly int _keyColumn, _languageColumn, _textColumn;
    private readonly Dictionary<string, int> _rowIndices = new Dictionary<string, int>();
    public SteamContentType ContentType { get; }
    public Dictionary<string, Dictionary<string, string>> Entries { get; } = new Dictionary<string, Dictionary<string, string>>();

    public static string NormalizeLanguage(string language) {
        string code = (language ?? "").Trim().ToLowerInvariant();
        return code == "korean" ? "koreana" : code;
    }

    public SteamContentCsv(List<List<string>> rows, SteamContentType type) {
        if (rows == null || rows.Count == 0) throw new InvalidDataException("Steam CSV is empty.");
        ContentType = type;
        _rows = rows.Select(row => new List<string>(row)).ToList();
        var headers = _rows[0];
        string keyHeader = type == SteamContentType.Event ? "field" : "key";
        string textHeader = type == SteamContentType.Event ? "value" : "text";
        _keyColumn = headers.IndexOf(keyHeader);
        _languageColumn = headers.IndexOf("language");
        _textColumn = headers.IndexOf(textHeader);
        if (_keyColumn < 0 || _languageColumn < 0 || _textColumn < 0)
            throw new InvalidDataException($"{type} CSV needs {keyHeader}, language, and {textHeader} columns. For events, choose Steam's field/language/value CSV export.");
        if (headers.Distinct(StringComparer.Ordinal).Count() != headers.Count)
            throw new InvalidDataException("Steam CSV has duplicate column headers.");

        int required = Math.Max(_keyColumn, Math.Max(_languageColumn, _textColumn));
        for (int i = 1; i < _rows.Count; i++) {
            var row = _rows[i];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            if (row.Count <= required) throw new InvalidDataException($"Steam CSV row {i + 1} is missing columns.");
            string key = row[_keyColumn];
            string language = NormalizeLanguage(row[_languageColumn]);
            if (string.IsNullOrWhiteSpace(key) || key.Contains("|") || key.Length + 1 + SteamLanguageList.All.Max(l => l.langCode.Length) > 64)
                throw new InvalidDataException($"Steam CSV row {i + 1} has an invalid or overly long field/key for a batch custom_id.");
            if (!SteamLanguageList.All.Any(l => l.langCode == language))
                throw new InvalidDataException($"Steam CSV row {i + 1} has unknown language '{row[_languageColumn]}'.");
            if (!Entries.ContainsKey(key)) Entries[key] = new Dictionary<string, string>();
            if (Entries[key].ContainsKey(language)) throw new InvalidDataException($"Duplicate Steam CSV field/language: {key}/{language}.");
            Entries[key][language] = row[_textColumn];
            _rowIndices.Add(key + "|" + language, i);
        }
        if (!Entries.Any(e => e.Value.TryGetValue("english", out var text) && !string.IsNullOrWhiteSpace(text)))
            throw new InvalidDataException("Steam CSV has no nonempty English source text.");
    }

    public string EnglishEventContext() {
        if (ContentType != SteamContentType.Event) return "";
        // Length-prefix each field to keep source snapshots unambiguous.
        return string.Concat(Entries.Where(e => e.Value.ContainsKey("english")).OrderBy(e => e.Key, StringComparer.Ordinal)
            .Select(e => e.Key.Length + ":" + e.Key + e.Value["english"].Length + ":" + e.Value["english"]));
    }

    public string EventPromptContext() {
        if (ContentType != SteamContentType.Event) return "";
        return string.Join("\n\n", Entries.Where(e => e.Value.TryGetValue("english", out var text) && !string.IsNullOrWhiteSpace(text))
            .Select(e => e.Key + ":\n" + e.Value["english"]));
    }

    public bool TryAddTranslation(string key, string language, string text, out string error) {
        language = NormalizeLanguage(language);
        if (!Entries.TryGetValue(key, out var languages) || !languages.TryGetValue("english", out var source) || string.IsNullOrWhiteSpace(source)) {
            error = "Missing English source.";
            return false;
        }
        if (language == "english" || !SteamLanguageList.All.Any(l => l.langCode == language)) {
            error = "Unexpected target language.";
            return false;
        }
        if (languages.TryGetValue(language, out var existing) && !string.IsNullOrWhiteSpace(existing)) {
            error = "Existing translation preserved.";
            return false;
        }
        if (!PreservesFormatting(source, text, out error)) return false;
        languages[language] = text;
        return true;
    }

    public List<List<string>> Export() {
        var output = _rows.Select(row => new List<string>(row)).ToList();
        foreach (var entry in Entries) {
            foreach (var language in entry.Value) {
                if (_rowIndices.TryGetValue(entry.Key + "|" + language.Key, out int rowIndex)) {
                    output[rowIndex][_textColumn] = language.Value;
                } else {
                    int templateIndex = _rowIndices.First(p => p.Key.StartsWith(entry.Key + "|", StringComparison.Ordinal)).Value;
                    var row = new List<string>(_rows[templateIndex]);
                    row[_keyColumn] = entry.Key;
                    row[_languageColumn] = ContentType == SteamContentType.Event && language.Key == "koreana" ? "korean" : language.Key;
                    row[_textColumn] = language.Value;
                    output.Add(row);
                }
            }
        }
        return output;
    }

    private static readonly Regex Tags = new Regex(@"\[[^\]\r\n]+\]");
    private static readonly Regex Urls = new Regex(@"(?:https?://|\{STEAM_CLAN_IMAGE\}/)[^\s\[\]<>""']+", RegexOptions.IgnoreCase);

    public static bool PreservesFormatting(string source, string translation, out string error) {
        error = null;
        if (string.IsNullOrWhiteSpace(translation)) error = "Empty translation.";
        else if (!OpenAITranslationModels.PreservesPlaceholders(source, translation)) error = "Changed placeholders.";
        else if (!Tags.Matches(source).Cast<Match>().Select(m => m.Value)
            .SequenceEqual(Tags.Matches(translation).Cast<Match>().Select(m => m.Value))) error = "Changed BBCode tags or tag order.";
        else if (!Urls.Matches(source).Cast<Match>().Select(m => m.Value).OrderBy(s => s, StringComparer.Ordinal)
            .SequenceEqual(Urls.Matches(translation).Cast<Match>().Select(m => m.Value).OrderBy(s => s, StringComparer.Ordinal))) error = "Changed URLs or image references.";
        return error == null;
    }
}
