using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// Category APIs use production code; text/font lookup is outside this isolated project.
public static partial class Localizer {
    public const string DEFAULT_LANGUAGE = "english";
    public const string LANGUAGE_LIST_KEY = "LanguageList";
    public static string Format(string key, params object[] args) => throw new NotSupportedException("Text lookup is not included in this harness.");
}

public static class SteamTranslationRegression {
    private static int _assertions;
    private static void Check(bool condition, string message) {
        _assertions++;
        if (!condition) throw new Exception(message);
    }
    private static void Reject(Action action, string message) {
        try { action(); } catch (InvalidDataException) { _assertions++; return; }
        throw new Exception(message);
    }
    private static SteamContentCsv Event(string csv) => new SteamContentCsv(CSVParser.LoadFromString(csv), SteamContentType.Event);
    private static LocalizationMasterDatabase Database() {
        var database = ScriptableObject.CreateInstance<LocalizationMasterDatabase>();
        database.GameTitle = "10 Second Mixtape";
        database.GameGenre = "Music creation";
        database.GameTone = "Friendly and playful";
        database.GlobalContext = "A cooperative music game.";
        database.ProtectedNames = "Different Strokes\nDifferent Folks";
        database.TargetLanguages = new List<string> { "german", "koreana" };
        return database;
    }

    public static async void Run() {
        string report = Path.Combine(Path.GetDirectoryName(Application.dataPath), "result.txt");
        try {
            DatabaseLifecycle(); RoundTrips(); StoreTranslationScope(); Validation(); RequestsAndSnapshots(); SharedRequests(); MergeResults(); BatchErrorLogging(); BatchSubmission(); BatchDownloads(); SettingsPersistence();
            string[] args = Environment.GetCommandLineArgs();
            int fileArg = Array.IndexOf(args, "-steamEventCsv");
            if (fileArg >= 0) RealExport(args[fileArg + 1]);
            int storeArg = Array.IndexOf(args, "-steamStoreCsv");
            if (storeArg >= 0) RealStoreExport(args[storeArg + 1]);
            await CategoryBaking();
            File.WriteAllText(report, $"PASS: {_assertions} assertions using Unity {Application.unityVersion}.\n");
            EditorApplication.Exit(0);
        } catch (Exception e) {
            File.WriteAllText(report, "FAIL: " + e + "\n");
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    private static void DatabaseLifecycle() {
        LocalizationMasterDatabase.ClearCachedDatabase();
        var window = ScriptableObject.CreateInstance<SteamContentTranslatorWindow>();
        Check(Get(window, "_database") == null, "Missing database leaves translator unbound.");
        Check(Directory.GetFiles(Application.dataPath, "*.asset", SearchOption.AllDirectories).Length == 0, "Window does not create settings assets.");
        UnityEngine.Object.DestroyImmediate(window);
        var database = ScriptableObject.CreateInstance<LocalizationMasterDatabase>();
        Check(database.OpenAIModel == OpenAITranslationModels.DefaultModel && database.Effort == ReasoningEffort.Low, "Shared model defaults initialized.");
        Check(database.TargetLanguages.Count == 0, "Shared languages require explicit selection.");
        AssetDatabase.CreateAsset(database, "Assets/LocalizationMasterDatabase.asset");
        AssetDatabase.SaveAssetIfDirty(database);
        window = ScriptableObject.CreateInstance<SteamContentTranslatorWindow>();
        Check((LocalizationMasterDatabase)Get(window, "_database") == database, "Window discovers existing master database.");
        Check(Directory.GetFiles(Application.dataPath, "*.asset", SearchOption.AllDirectories).Length == 1, "Existing database requires no additional asset.");
        UnityEngine.Object.DestroyImmediate(window);
    }

    private static void RoundTrips() {
        var trailing = CSVParser.LoadFromString("field,language,value\ntitle,english,");
        Check(trailing.Count == 2 && trailing[1].Count == 3 && trailing[1][2] == "", "Final empty CSV cell retained.");
        string source = "[p]Hello, \"friends\"!\n10 Second Mixtape[/p][img src=\"{STEAM_CLAN_IMAGE}/123/image.gif\"][/img]";
        var rows = new List<List<string>> {
            new List<string> { "metadata", "value", "language", "field" },
            new List<string> { "body-note", source, "english", "body" },
            new List<string> { "reviewed", "Schon vorhanden", "german", "title" },
            new List<string> { "title-note", "Release!", "english", "title" },
            new List<string> { "keep", "", "korean", "body" },
            new List<string> { "", "", "", "" },
            new List<string> { "optional", "", "english", "subtitle" }
        };
        var document = new SteamContentCsv(CSVParser.LoadFromString(CSVParser.WriteToString(rows)), SteamContentType.Event);
        Check(document.Entries["body"]["english"] == source, "Multiline quoting round-trip.");
        Check(document.Entries["body"].ContainsKey("koreana"), "Korean alias normalization.");
        Check(document.TryAddTranslation("body", "koreana", source.Replace("Hello", "안녕하세요"), out _), "Accept intact markup.");
        Check(document.TryAddTranslation("title", "french", "Sortie !", out _), "Append missing target row.");
        Check(!document.TryAddTranslation("title", "german", "Changed", out _), "Preserve existing translation.");
        var exported = document.Export();
        Check(exported[0].SequenceEqual(rows[0]), "Header order preserved.");
        Check(exported[4][2] == "korean" && exported[4][0] == "keep", "Original language spelling and metadata.");
        Check(exported[2].SequenceEqual(rows[2]) && exported[5].SequenceEqual(rows[5]), "Existing and blank rows preserved.");
        Check(exported.Last()[0] == "reviewed" && exported.Last()[2] == "french", "New rows retain field metadata.");
        var roundTrip = new SteamContentCsv(CSVParser.LoadFromString(CSVParser.WriteToString(exported)), SteamContentType.Event);
        Check(roundTrip.Entries["title"]["french"] == "Sortie !", "Export reparses.");
        var missing = Event("field,language,value\ntitle,english,Hello");
        Check(missing.TryAddTranslation("title", "koreana", "안녕", out _), "Add missing Korean.");
        Check(missing.Export().Last()[1] == "korean", "Event output writes event Korean code.");
        var store = new SteamContentCsv(CSVParser.LoadFromString("key,language,text,notes\nabout,english,[b]Hello[/b],retain\nabout,german,,retain2"), SteamContentType.StorePage);
        Check(store.TryAddTranslation("about", "german", "[b]Hallo[/b]", out _), "Store page supported.");
        Check(store.Export()[2][3] == "retain2", "Store metadata preserved.");
    }

    private static void StoreTranslationScope() {
        const string about = "app[content][about]";
        const string summary = "app[content][short_description]";
        const string os = "app[content][sysreqs][windows][min][osversion]";
        string csv = "key,language,text\n" + about + ",english,[p]Hello[/p]\n" + about + ",german,[p]Hallo[/p]\n"
            + about + ",malay,\n" + summary + ",english,Make songs.\n" + summary + ",german,Mache Musik.\n"
            + summary + ",malay,\n" + os + ",english,Windows 10\n" + os + ",german,\n" + os + ",malay,";
        var document = new SteamContentCsv(CSVParser.LoadFromString(csv), SteamContentType.StorePage);
        var database = Database();
        database.TargetLanguages = new List<string> { "german", "malay", "arabic" };
        var requests = SteamContentTranslation.CreateRequests(document, database, "");
        Check(requests.Select(r => r.CustomId).OrderBy(id => id).SequenceEqual(new[] {
            about + "|malay", summary + "|malay"
        }.OrderBy(id => id)), "Store translates only missing Malay descriptions and skips Arabic and system requirements.");
        var withArabic = new SteamContentCsv(CSVParser.LoadFromString(csv + "\n" + about + ",arabic,[p]مرحبا[/p]\n" + summary + ",arabic,"), SteamContentType.StorePage);
        Check(SteamContentTranslation.CreateRequests(withArabic, database, "").All(r => !r.CustomId.EndsWith("|arabic"))
            && withArabic.Entries[about]["arabic"] == "[p]مرحبا[/p]", "Existing Arabic rows are preserved, but missing Arabic store fields are skipped.");
        var window = ScriptableObject.CreateInstance<SteamContentTranslatorWindow>();
        Set(window, "_database", database);
        Set(window, "_contentType", SteamContentType.StorePage);
        string fixture = Path.Combine(Path.GetDirectoryName(Application.dataPath), "store-preview.csv");
        File.WriteAllText(fixture, csv);
        Set(window, "_csvPath", fixture);
        var preview = typeof(SteamContentTranslatorWindow).GetMethod("RefreshPreview", BindingFlags.Instance | BindingFlags.NonPublic);
        preview.Invoke(window, null);
        Check((int)Get(window, "_previewRequestCount") == 2 && ((string)Get(window, "_previewSummary")).Contains("Bahasa Melayu: 2")
            && !((string)Get(window, "_previewSummary")).Contains("العربية"),
            "Preview reports the actual request count by language.");
        database.TargetLanguages = new List<string> { "german" };
        preview.Invoke(window, null);
        Check((int)Get(window, "_previewRequestCount") == 0, "Preview updates when selected languages change.");
        database.TargetLanguages = new List<string> { "german", "malay", "arabic" };
        var merge = typeof(SteamContentTranslatorWindow).GetMethod("MergeResults", BindingFlags.Instance | BindingFlags.NonPublic);
        string oldBatch = Result(os + "|malay", "Windows 10") + "\n" + Result(about + "|arabic", "[p]مرحبا[/p]") + "\n"
            + Result(about + "|german", "[p]Changed[/p]") + "\n" + Result(about + "|malay", "[p]Helo[/p]") + "\n"
            + Result(summary + "|malay", "Cipta lagu.") + "\n" + Result(summary + "|arabic", "اصنع الأغاني.");
        Check((int)merge.Invoke(window, new object[] { document, oldBatch }) == 2, "Existing batch imports only the Malay descriptions.");
        Check(document.Entries[os]["malay"] == "" && document.Entries[os]["german"] == "", "Old batch cannot fill system requirements.");
        Check(!document.Entries[about].ContainsKey("arabic") && document.Entries[about]["german"] == "[p]Hallo[/p]", "Arabic store results are ignored and existing translations remain untouched.");
        Check(document.Export().All(row => row[1] != "arabic"), "Export does not add Arabic store rows.");
        File.WriteAllText(fixture, CSVParser.WriteToString(document.Export()));
        File.SetLastWriteTimeUtc(fixture, DateTime.UtcNow.AddSeconds(2));
        preview.Invoke(window, null);
        Check((int)Get(window, "_previewRequestCount") == 0, "Preview refreshes after the source file changes.");
        Check(database.TargetLanguages.Contains("arabic"), "Store filtering does not modify the shared game-language selection.");
        var eventDocument = Event("field,language,value\ntitle,english,Hello\ntitle,arabic,");
        Check(SteamContentTranslation.CreateRequests(eventDocument, database, "").Any(r => r.CustomId.EndsWith("|arabic")), "Arabic event handling remains available.");
        var arabicEntry = new LocalizationEntry { Key = "arabic.greeting", EnglishText = "Hello" };
        arabicEntry.Translations.Add(new LanguageTranslation { LanguageCode = "arabic", Status = TranslationStatus.Untranslated });
        database.Entries.Add(arabicEntry);
        Check(BatchHandler.BuildRequests(database, database.TargetLanguages, out _).Any(r => r.CustomId.StartsWith("arabic|")), "Arabic in-game translation remains available.");
        UnityEngine.Object.DestroyImmediate(window);
        UnityEngine.Object.DestroyImmediate(database);
    }

    private static void Validation() {
        Reject(() => Event("field,english,german\ntitle,Hello,"), "Reject other event layout.");
        Reject(() => Event("field,language,value\ntitle,english,Hi\ntitle,korean,\ntitle,koreana,"), "Reject alias duplicates.");
        Reject(() => Event("field,language,value\ntitle,unknown,Hi"), "Reject unknown language.");
        Reject(() => Event("field,language,value\ntitle,english"), "Reject incomplete rows.");
        Reject(() => Event("field,language,value\ntitle|bad,english,Hi"), "Reject custom-ID delimiter.");
        Reject(() => Event("field,language,value\n" + new string('x', 60) + ",english,Hi"), "Reject long IDs.");
        Reject(() => Event("field,language,value\ntitle,english,"), "Reject no source.");
        Check(!SteamContentCsv.PreservesFormatting("[b]Hi[/b]", "[i]Hallo[/i]", out _), "Reject changed tags.");
        Check(!SteamContentCsv.PreservesFormatting("[b][i]Hi[/i][/b]", "[i][b]Hallo[/b][/i]", out _), "Reject changed tag order.");
        Check(!SteamContentCsv.PreservesFormatting("{STEAM_CLAN_IMAGE}/1/image.jpg", "{STEAM_CLAN_IMAGE}/2/image.jpg", out _), "Reject image changes.");
        Check(!SteamContentCsv.PreservesFormatting("Go https://example.com/a", "Gehe https://example.com/b", out _), "Reject URL changes.");
        Check(!SteamContentCsv.PreservesFormatting("Hello {player}", "Hallo {spieler}", out _), "Reject changed placeholders.");
        Check(SteamContentCsv.PreservesFormatting("Visit https://example.com/a {player}", "Besuche https://example.com/a {player}", out _), "Accept preserved URL and placeholder.");
        var database = Database();
        Check(!database.PreservesProtectedNames("Different Strokes", "Andere Striche", out _), "Reject changed name.");
        Check(database.PreservesProtectedNames("Play 10 Second Mixtape", "Spiele 10 Second Mixtape", out _), "Preserve title automatically.");
        UnityEngine.Object.DestroyImmediate(database);
    }

    private static void RequestsAndSnapshots() {
        var document = Event("field,language,value\ntitle,english,Release!\nbody,english,[p]Hello[/p]\nsubtitle,english,\nsummary,english,\ntitle,german,Fertig");
        var database = Database();
        var requests = SteamContentTranslation.CreateRequests(document, database, "Keep the playful tone.");
        Check(requests.Count == 3, "Skip empty fields and existing translations.");
        Check(requests.Any(r => r.CustomId == "title|koreana"), "Canonical request language.");
        Check(!requests.Any(r => r.CustomId == "title|german"), "Existing title skipped.");
        var json = OpenAIJson.ReadObject<RequestView>(requests.First().Json);
        Check(json.body.messages[0].content.Contains("Different Strokes"), "Names in prompt.");
        Check(json.body.messages[0].content.Contains(database.GlobalContext), "Event prompt uses shared Global Context.");
        Check(json.body.messages[1].content.Contains("[p]Hello[/p]") && json.body.messages[1].content.Contains("Release!"), "Full event accompanies field.");
        Check(json.body.messages[1].content.Contains("Keep the playful tone."), "Notes in prompt.");
        string hash = SteamContentTranslation.SourceHash(document, "Release!", database, "notes");
        Check(hash != SteamContentTranslation.SourceHash(document, "Release!", database, "changed"), "Notes affect snapshot.");
        database.GlobalContext = "Updated shared game guidance.";
        Check(hash != SteamContentTranslation.SourceHash(document, "Release!", database, "notes"), "Global Context affects snapshot.");
        hash = SteamContentTranslation.SourceHash(document, "Release!", database, "notes");
        document.Entries["body"]["english"] = "Updated event body";
        Check(hash != SteamContentTranslation.SourceHash(document, "Release!", database, "notes"), "Other fields affect snapshot.");
        string current = SteamContentTranslation.SourceHash(document, "Release!", database, "notes");
        database.ProtectedNames += "\nAnother Name";
        Check(current != SteamContentTranslation.SourceHash(document, "Release!", database, "notes"), "Names affect snapshot.");
        current = SteamContentTranslation.SourceHash(document, "Release!", database, "notes");
        database.GameGenre = "Updated genre";
        Check(current != SteamContentTranslation.SourceHash(document, "Release!", database, "notes"), "Genre affects snapshot.");
        current = SteamContentTranslation.SourceHash(document, "Release!", database, "notes");
        database.GameTone = "Updated tone";
        Check(current != SteamContentTranslation.SourceHash(document, "Release!", database, "notes"), "Tone affects snapshot.");
        var store = new SteamContentCsv(CSVParser.LoadFromString("key,language,text\na,english,Hello\na,german,"), SteamContentType.StorePage);
        Check(SteamContentTranslation.SourceHash(store, "Hello", database, "a") == SteamContentTranslation.SourceHash(store, "Hello", database, "b"), "Store ignores notes.");
        var storeRequest = SteamContentTranslation.CreateRequests(store, database, "").First();
        Check(OpenAIJson.ReadObject<RequestView>(storeRequest.Json).body.messages[0].content.Contains(database.GlobalContext), "Store prompt uses shared Global Context.");
        database.TargetLanguages.Clear();
        try { SteamContentTranslation.CreateRequests(document, database, ""); throw new Exception("Empty targets accepted."); }
        catch (InvalidOperationException) { _assertions++; }
        UnityEngine.Object.DestroyImmediate(database);
    }

    private static void SharedRequests() {
        var database = Database();
        database.OpenAIModel = "gpt-6.1-terra";
        database.Effort = ReasoningEffort.High;
        database.MaxCompletionTokens = 12345;
        database.TargetLanguages = new List<string> { "german", "japanese" };
        string source = "Play 10 Second Mixtape by Different Folks.";
        var entry = new LocalizationEntry { Key = "shared.greeting", EnglishText = source };
        foreach (string language in database.TargetLanguages)
            entry.Translations.Add(new LanguageTranslation { LanguageCode = language, Status = TranslationStatus.Untranslated });
        database.Entries.Add(entry);
        var document = Event("field,language,value\ntitle,english,Release!\nbody,english,Hello");
        var steam = SteamContentTranslation.CreateRequests(document, database, "");
        var regular = BatchHandler.BuildRequests(database, database.TargetLanguages, out var pending);
        Check(steam.Count == 4 && regular.Count == 2 && pending.Count == 2, "Both workflows use shared target languages.");
        foreach (var request in new[] { steam.First(), regular.First() }) {
            var body = OpenAIJson.ReadObject<RequestView>(request.Json).body;
            Check(body.model == database.OpenAIModel && body.reasoning_effort == "high" && body.max_completion_tokens == 12345,
                "Both workflows use the shared model, effort, and output budget.");
            string prompt = body.messages[0].content;
            Check(new[] { database.GameTitle, database.GameGenre, database.GameTone, database.GlobalContext, "Different Folks" }.All(prompt.Contains),
                "Both prompts use the shared game guidance and protected names.");
        }

        var german = entry.GetTranslation("german");
        german.Status = TranslationStatus.OutForBatch;
        german.SetBatchTracking("shared-batch", regular.First().CustomId, LocalizationProcessor.GenerateHash(entry.EnglishText, entry.Context));
        var apply = typeof(BatchHandler).GetMethod("ApplyResultsToDatabase", BindingFlags.Static | BindingFlags.NonPublic);
        string badText = JsonUtility.ToJson(new OpenAIJson.Translations { translations = new[] {
            new OpenAIJson.Translation { key = entry.Key, text = "Spiele 10 Second Mixtape von Andere Leute." }
        } });
        apply.Invoke(null, new object[] { database, Result(regular.First().CustomId, badText), "shared-batch" });
        Check(german.Status == TranslationStatus.OutForBatch, "Regular translations reject changed shared protected names.");
        string goodText = JsonUtility.ToJson(new OpenAIJson.Translations { translations = new[] {
            new OpenAIJson.Translation { key = entry.Key, text = "Spiele 10 Second Mixtape von Different Folks." }
        } });
        apply.Invoke(null, new object[] { database, Result(regular.First().CustomId, goodText), "shared-batch" });
        Check(german.Status == TranslationStatus.Translated && german.TranslatedText.Contains("Different Folks"),
            "Regular translations accept preserved shared protected names.");

        database.TargetLanguages = new List<string> { "japanese" };
        Check(SteamContentTranslation.CreateRequests(document, database, "").All(r => r.CustomId.EndsWith("|japanese")),
            "Steam immediately follows shared language edits.");
        var remaining = BatchHandler.BuildRequests(database, database.TargetLanguages, out _);
        Check(remaining.Count == 1 && remaining[0].CustomId.StartsWith("japanese|"), "Regular translation follows the same language edits.");
        UnityEngine.Object.DestroyImmediate(database);
    }

    [Serializable] private class RequestView { public BodyView body; }
    [Serializable] private class BodyView {
        public string model;
        public string reasoning_effort;
        public int max_completion_tokens;
        public OpenAIJson.Message[] messages;
    }
    private static string Result(string id, string text, string finish = "stop") {
        // Match the API's omitted error fields; JsonUtility serializes null DTOs as empty objects.
        return "{\"custom_id\":\"" + id + "\",\"response\":{\"status_code\":200,\"body\":{\"choices\":[{\"finish_reason\":\""
            + finish + "\",\"message\":" + JsonUtility.ToJson(new OpenAIJson.Message { content = text }) + "}]}}}";
    }
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    private static object Get(object target, string field) => target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

    private static void MergeResults() {
        var window = ScriptableObject.CreateInstance<SteamContentTranslatorWindow>();
        var database = Database();
        Set(window, "_database", database);
        Set(window, "_batchId", "test-batch");
        Set(window, "_submittedBatchId", "test-batch");
        Set(window, "_hasDocumentSnapshot", true);
        var document = Event("field,language,value\ntitle,english,Hello\nbody,english,[p]World[/p]");
        var snapshots = (IList)Get(window, "_submittedSources");
        var snapshotType = snapshots.GetType().GetGenericArguments()[0];
        var snapshot = Activator.CreateInstance(snapshotType, true);
        snapshotType.GetField("Key").SetValue(snapshot, "title|german");
        snapshotType.GetField("Hash").SetValue(snapshot, SteamContentTranslation.SourceHash(document, "Hello", database, ""));
        snapshots.Add(snapshot);
        var merge = typeof(SteamContentTranslatorWindow).GetMethod("MergeResults", BindingFlags.Instance | BindingFlags.NonPublic);
        string results = Result("title|german", "Partial", "length") + "\n" + Result("title|koreana", "안녕") + "\n"
            + Result("title|german", "Hallo") + "\n" + Result("title|german", "Overwrite");
        Check((int)merge.Invoke(window, new object[] { document, results }) == 1, "Reject truncated, unexpected, and duplicate results.");
        Check(document.Entries["title"]["german"] == "Hallo", "First valid translation retained.");
        var changed = Event("field,language,value\ntitle,english,Hello\nbody,english,Changed");
        Check((int)merge.Invoke(window, new object[] { changed, Result("title|german", "Hallo") }) == 0, "Reject changed event context.");
        var sameSource = Event("field,language,value\ntitle,english,Hello\nbody,english,[p]World[/p]");
        database.GlobalContext = "Changed after submission.";
        Check((int)merge.Invoke(window, new object[] { sameSource, Result("title|german", "Hallo") }) == 0, "Reject changed Global Context.");
        UnityEngine.Object.DestroyImmediate(window);
        UnityEngine.Object.DestroyImmediate(database);
    }

    private static void BatchErrorLogging() {
        var errors = new List<string>();
        Application.LogCallback onLog = (message, stackTrace, type) => {
            if (type == LogType.Error) errors.Add(message);
        };
        string serverError = ",\"response\":{\"status_code\":500,\"body\":{\"error\":{\"message\":\"The server had an error while processing your request.\"}}},\"error\":null}";
        string content = "{\"custom_id\":\"title|russian\"" + serverError + "\r\nnot json\r\n"
            + "{\"custom_id\":\"title|vietnamese\"" + serverError + "\n"
            + "{\"custom_id\":\"body|german\",\"response\":null,\"error\":{\"message\":\"Batch request expired.\"}}\n"
            + Result("title|german", "Hallo");
        Application.logMessageReceived += onLog;
        try {
            OpenAIBatchAPI.LogBatchErrors(content, "[Steam Translation] Batch test-batch:");
        } finally {
            Application.logMessageReceived -= onLog;
        }
        Check(errors.Count == 4, "Log each failed request and malformed line as an error; ignore successful responses.");
        Check(errors.Any(e => e.Contains("title|russian, HTTP 500") && e.Contains("The server had an error")), "Log Russian HTTP failure with request ID and server message.");
        Check(errors.Any(e => e.Contains("title|vietnamese, HTTP 500") && e.Contains("The server had an error")), "Continue logging Vietnamese failure after malformed error line.");
        Check(errors.Any(e => e.Contains("body|german") && e.Contains("Batch request expired.")), "Log top-level batch errors without HTTP responses.");
        Check(errors.All(e => e.StartsWith("[Steam Translation] Batch test-batch:", StringComparison.Ordinal)), "Identify the batch in every logged error.");
    }

    private static void BatchSubmission() {
        var request = new OpenAIJson.Request("title|german", "{\"custom_id\":\"title|german\",\"text\":\"Grüße 世界\"}");
        var requests = new[] { request, new OpenAIJson.Request("body|german", "{\"custom_id\":\"body|german\"}") };
        string path = Path.Combine(Path.GetTempPath(), "localization_regression_" + Guid.NewGuid().ToString("N") + ".jsonl");
        int uploads = 0, creates = 0;
        Func<string, Task<string>> upload = file => {
            uploads++;
            Check(file == path && File.Exists(file), "Submission uploads the generated input file.");
            byte[] bytes = File.ReadAllBytes(file);
            Check(!(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "Batch JSONL has no UTF-8 BOM.");
            Check(File.ReadAllLines(file).SequenceEqual(requests.Select(r => r.Json)), "Submission preserves Unicode and each request's exact JSON.");
            return Task.FromResult("file-test");
        };
        Func<string, Task<string>> create = fileId => {
            creates++;
            Check(fileId == "file-test", "Create the batch from the uploaded file ID.");
            return Task.FromResult("batch-test");
        };
        try {
            string batchId = OpenAIBatchAPI.SubmitRequestsAsync(requests, path, "[Test]", upload, create).GetAwaiter().GetResult();
            Check(batchId == "batch-test" && uploads == 1 && creates == 1, "Upload and create exactly one batch.");
            Check(!File.Exists(path), "Remove temporary input after successful submission.");

            foreach (bool failUpload in new[] { true, false }) {
                int createCalls = 0;
                string failure = null;
                try {
                    OpenAIBatchAPI.SubmitRequestsAsync(requests, path, "[Test]",
                        file => Task.FromResult(failUpload ? null : "file-test"),
                        fileId => { createCalls++; return Task.FromResult<string>(null); }).GetAwaiter().GetResult();
                } catch (InvalidOperationException e) { failure = e.Message; }
                Check(failure == (failUpload ? "Batch upload failed; see Console." : "Batch creation failed; see Console."), "Preserve the Steam submission failure message.");
                Check(createCalls == (failUpload ? 0 : 1), "Never create a batch after upload failure.");
                Check(!File.Exists(path), "Remove temporary input after submission failure.");
            }

            bool threw = false;
            try {
                OpenAIBatchAPI.SubmitRequestsAsync(requests, path, "[Test]",
                    file => throw new IOException("Simulated connection failure."), create).GetAwaiter().GetResult();
            } catch (IOException) { threw = true; }
            Check(threw && !File.Exists(path), "Clean up input and propagate unexpected submission errors.");

            foreach (int count in new[] { 0, OpenAIBatchAPI.MaxRequests + 1 }) {
                bool rejected = false;
                try {
                    OpenAIBatchAPI.SubmitRequestsAsync(Enumerable.Repeat(request, count).ToArray(), path, "[Test]", upload, create).GetAwaiter().GetResult();
                } catch (ArgumentException) { rejected = true; }
                Check(rejected && !File.Exists(path) && uploads == 1 && creates == 1, "Reject invalid request counts before writing or contacting the API.");
            }
        } finally {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void BatchDownloads() {
        var calls = new List<string>();
        var errors = new List<string>();
        Application.LogCallback onLog = (message, stackTrace, type) => { if (type == LogType.Error) errors.Add(message); };
        Application.logMessageReceived += onLog;
        try {
            var status = new BatchStatusResult { Status = "completed", ErrorFileId = "errors", OutputFileId = "output" };
            Func<string, Task<string>> download = fileId => {
                calls.Add(fileId);
                return Task.FromResult(fileId == "errors"
                    ? "{\"custom_id\":\"title|russian\",\"error\":{\"message\":\"Request failed.\"}}"
                    : Result("title|german", "Hallo"));
            };
            var result = OpenAIBatchAPI.DownloadBatchResultsAsync(status, "[Test] Batch test:", download,
                () => calls.Add("downloading")).GetAwaiter().GetResult();
            Check(calls.SequenceEqual(new[] { "errors", "downloading", "output" }), "Log failed requests before downloading successful output; preserve download progress timing.");
            Check(result.CanProcess && result.Content == Result("title|german", "Hallo"), "Return successful results from a partially failed batch unchanged.");
            Check(errors.Count == 1 && errors[0].Contains("title|russian"), "Report failed requests alongside successful results.");

            errors.Clear();
            result = OpenAIBatchAPI.DownloadBatchResultsAsync(status, "[Test] Batch test:",
                fileId => fileId == "errors" ? download(fileId) : Task.FromResult<string>(null)).GetAwaiter().GetResult();
            Check(!result.CanProcess && result.Content == null, "A failed output download cannot resolve or reset pending translations.");
            Check(errors.Count == 1 && errors[0].Contains("title|russian"), "Report request errors even if output download fails.");

            result = OpenAIBatchAPI.DownloadBatchResultsAsync(status, "[Test]",
                fileId => Task.FromResult(fileId == "errors" ? null : "")).GetAwaiter().GetResult();
            Check(result.CanProcess && result.Content == "", "Distinguish an empty output from a failed download; error-file failure does not discard successful output.");

            foreach (string terminal in new[] { "completed", "failed", "expired", "cancelled" }) {
                calls.Clear();
                result = OpenAIBatchAPI.DownloadBatchResultsAsync(new BatchStatusResult { Status = terminal }, "[Test]", download).GetAwaiter().GetResult();
                Check(result.CanProcess && result.Content == null && calls.Count == 0, "Terminal batch without output still allows unresolved translations to be reset: " + terminal);
            }
            foreach (string pending in new[] { "validating", "in_progress", "finalizing", "cancelling" }) {
                calls.Clear();
                errors.Clear();
                result = OpenAIBatchAPI.DownloadBatchResultsAsync(new BatchStatusResult {
                    Status = pending, ErrorFileId = "errors", OutputFileId = "output", Errors = new List<string> { "Validation diagnostic." }
                }, "[Test]", download, () => calls.Add("downloading")).GetAwaiter().GetResult();
                Check(!result.CanProcess && calls.Count == 0, "Do not download or process unfinished batches: " + pending);
                Check(errors.SequenceEqual(new[] { "[Test] Validation diagnostic." }), "Log batch-level errors even before the batch finishes.");
            }
        } finally {
            Application.logMessageReceived -= onLog;
        }
    }

    private static void SettingsPersistence() {
        var database = LocalizationMasterDatabase.LoadDatabase();
        database.GlobalContext = "Existing game context";
        database.Entries.Add(new LocalizationEntry { Key = "retained", EnglishText = "Existing string" });
        var serialized = new SerializedObject(database);
        serialized.Update();
        serialized.FindProperty("GlobalContext").stringValue = "Saved shared context";
        serialized.FindProperty("GameTitle").stringValue = "Saved Game";
        serialized.FindProperty("ProtectedNames").stringValue = "Saved Name";
        serialized.FindProperty("OpenAIModel").stringValue = "gpt-6-sol";
        serialized.FindProperty("Effort").intValue = (int)ReasoningEffort.High;
        serialized.FindProperty("MaxCompletionTokens").intValue = 8192;
        var languages = serialized.FindProperty("TargetLanguages");
        languages.arraySize = 1;
        languages.GetArrayElementAtIndex(0).stringValue = "japanese";
        serialized.ApplyModifiedProperties();
        AssetDatabase.SaveAssetIfDirty(database);
        string path = AssetDatabase.GetAssetPath(database);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        LocalizationMasterDatabase.ClearCachedDatabase();
        var reopened = ScriptableObject.CreateInstance<SteamContentTranslatorWindow>();
        var reloaded = (LocalizationMasterDatabase)Get(reopened, "_database");
        Check(reloaded.GameTitle == "Saved Game" && reloaded.GlobalContext == "Saved shared context", "Restore shared title and context in new window.");
        Check(reloaded.ProtectedNames == "Saved Name" && reloaded.TargetLanguages.SequenceEqual(new[] { "japanese" }), "Restore shared names and target languages.");
        Check(reloaded.OpenAIModel == "gpt-6-sol" && reloaded.Effort == ReasoningEffort.High && reloaded.MaxCompletionTokens == 8192, "Restore shared model controls.");
        Check(File.ReadAllText(Path.GetFullPath(path)).Contains("Saved Game"), "Shared settings saved in database on disk.");
        Check(reloaded.FindEntry("retained")?.EnglishText == "Existing string", "Localization entries preserved.");
        var decoded = ScriptableObject.CreateInstance<LocalizationMasterDatabase>();
        EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(reloaded), decoded);
        Check(decoded.GameTitle == "Saved Game" && decoded.TargetLanguages.Single() == "japanese", "Database serialization includes shared settings.");
        Check(Directory.GetFiles(Application.dataPath, "*.asset", SearchOption.AllDirectories).Length == 1, "No separate Steam settings asset created.");
        UnityEngine.Object.DestroyImmediate(decoded);
        UnityEngine.Object.DestroyImmediate(reopened);
    }

    private static Task NextEditorDelay() {
        var completion = new TaskCompletionSource<bool>();
        EditorApplication.delayCall += () => completion.SetResult(true);
        return completion.Task;
    }

    private static async Task CategoryBaking() {
        var database = LocalizationMasterDatabase.LoadDatabase();
        database.TargetLanguages = new List<string> { "german" };
        database.Entries.Clear();
        var first = new LocalizationEntry { Key = "reaction.first", EnglishText = "First" };
        first.Translations.Add(new LanguageTranslation {
            LanguageCode = "german", TranslatedText = "Erste", Status = TranslationStatus.Translated
        });
        var second = new LocalizationEntry { Key = "reaction.second", EnglishText = "Second" };
        var whitespace = new LocalizationEntry { Key = "reaction.space", EnglishText = " " };
        var empty = new LocalizationEntry { Key = "reaction.empty", EnglishText = "" };
        var category = new LocalizationCategory("Reactions") {
            Entries = new List<LocalizationEntry> { first, second, whitespace, empty }
        };
        database.AdditionalCategories = new List<LocalizationCategory> { category, new LocalizationCategory("Empty") };
        EditorUtility.SetDirty(database);
        LocalizationProcessor.BakeAssets(database);

        const string cataloguePath = "Assets/Resources/LocalizationCategories.json";
        const string englishPath = "Assets/Resources/Languages/english.asset";
        var keys = Localizer.GetCategoryKeys("REACTIONS");
        Check(keys.SequenceEqual(new[] { first.Key, second.Key, whitespace.Key }), "Catalogue and English baking share entry eligibility, including whitespace text.");
        Check(Localizer.GetCategoryKeys("Empty").Count == 0 && Localizer.GetCategoryKeys("missing").Count == 0
            && Localizer.GetCategoryKeys(null).Count == 0, "Empty, unknown, and null categories are empty.");
        Check(!Localizer.TryGetRandomKey("missing", out var missing) && missing == null, "Unknown category has no random key.");
        Check(!new LocalizedPhraseBag("Empty").TryNext(out _), "Empty phrase bag has no key.");
        Check(((IList<string>)keys).IsReadOnly, "Category key collections are read-only.");
        var english = AssetDatabase.LoadAssetAtPath<LanguageData>(englishPath);
        Check(keys.All(k => english.ToDictionary().ContainsKey(k)), "Every category key resolves in baked English.");
        Check(AssetDatabase.LoadAssetAtPath<LanguageData>("Assets/Resources/Languages/german.asset").ToDictionary()[first.Key] == "Erste", "Full bake preserves approved language text.");

        var bag = new LocalizedPhraseBag("Reactions");
        string previous = null;
        for (int cycle = 0; cycle < 10; cycle++) {
            var seen = new HashSet<string>();
            for (int i = 0; i < keys.Count; i++) {
                Check(bag.TryNext(out var key) && keys.Contains(key) && seen.Add(key), "Each phrase occurs once per cycle.");
                Check(key != previous, "Phrase bag avoids adjacent repeats, including cycle boundaries.");
                previous = key;
            }
        }

        // A repeat bake must not rewrite otherwise unchanged runtime outputs.
        var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(englishPath, stamp);
        File.SetLastWriteTimeUtc(cataloguePath, stamp);
        LocalizationProcessor.BakeAssets(database);
        Check(File.GetLastWriteTimeUtc(englishPath) == stamp && File.GetLastWriteTimeUtc(cataloguePath) == stamp, "Unchanged output files are not rewritten.");

        string originalCatalogue = File.ReadAllText(cataloguePath);
        category.Entries.Add(new LocalizationEntry { Key = first.Key, EnglishText = "Duplicate" });
        bool rejected = false;
        try { LocalizationProcessor.BakeAssets(database); } catch (UnityEditor.Build.BuildFailedException) { rejected = true; }
        category.Entries.RemoveAt(category.Entries.Count - 1);
        Check(rejected && File.ReadAllText(cataloguePath) == originalCatalogue, "Invalid keys fail before publishing a catalogue.");

        first.Key = "reaction.renamed";
        first.EnglishText = "Updated first";
        EditorUtility.SetDirty(database);
        // The pre-build path must update text as well as membership without a manual bake.
        new LocalizationCategoryBaker().OnPreprocessBuild(null);
        Check(Localizer.GetCategoryKeys("Reactions").Contains(first.Key), "Pre-build bake invalidates the category cache.");
        english = AssetDatabase.LoadAssetAtPath<LanguageData>(englishPath);
        Check(english.ToDictionary()[first.Key] == "Updated first" && !english.ToDictionary().ContainsKey("reaction.first"), "Pre-build bake synchronizes renamed keys and English text.");

        // Let any earlier import-triggered bake finish, then exercise a non-default filename.
        await NextEditorDelay();
        string oldPath = AssetDatabase.GetAssetPath(database);
        string renamedPath = Path.GetDirectoryName(oldPath).Replace('\\', '/') + "/GameStrings.asset";
        Check(string.IsNullOrEmpty(AssetDatabase.MoveAsset(oldPath, renamedPath)), "Database can be renamed.");
        await NextEditorDelay();
        database = LocalizationMasterDatabase.LoadDatabase();
        Check(AssetDatabase.GetAssetPath(database) == renamedPath, "Database discovery supports renamed assets.");
        category = database.AdditionalCategories[0];
        category.Entries.Add(new LocalizationEntry { Key = "reaction.imported", EnglishText = "Imported" });
        EditorUtility.SetDirty(database);
        AssetDatabase.SaveAssetIfDirty(database);
        AssetDatabase.ImportAsset(renamedPath, ImportAssetOptions.ForceUpdate);
        await NextEditorDelay();
        Check(Localizer.GetCategoryKeys("Reactions").Contains("reaction.imported"), "Importing a renamed database automatically refreshes categories.");
        english = AssetDatabase.LoadAssetAtPath<LanguageData>(englishPath);
        Check(english.ToDictionary()["reaction.imported"] == "Imported", "Automatic import bake includes matching English text.");
    }

    private static void RealExport(string path) {
        var rows = CSVParser.LoadFromPath(path);
        var document = new SteamContentCsv(rows, SteamContentType.Event);
        Check(document.Entries.Count == 4, "Real event has four fields.");
        Check(CSVParser.WriteToString(document.Export()) == CSVParser.WriteToString(rows), "Real export round-trips unchanged.");
        var database = Database();
        database.TargetLanguages = SteamLanguageList.All.Where(l => l.langCode != "english").Select(l => l.langCode).ToList();
        var requests = SteamContentTranslation.CreateRequests(document, database, "");
        Check(requests.Count == 2 * database.TargetLanguages.Count, "Real event generates two populated fields per target language.");
        Check(requests.All(r => r.CustomId.StartsWith("body|") || r.CustomId.StartsWith("title|")), "Empty subtitle/summary stay empty.");
        string source = document.Entries["body"]["english"];
        Check(document.TryAddTranslation("body", "german", source.Replace("Hey everyone!", "Hallo zusammen!"), out _), "Real BBCode and images preserved.");
        string exported = CSVParser.WriteToString(document.Export());
        Check(Event(exported).Entries["body"]["german"].Contains("Hallo zusammen!"), "Real translated body round-trips.");
        UnityEngine.Object.DestroyImmediate(database);
    }

    private static void RealStoreExport(string path) {
        var rows = CSVParser.LoadFromPath(path);
        var document = new SteamContentCsv(rows, SteamContentType.StorePage);
        var database = Database();
        database.TargetLanguages = SteamLanguageList.All.Where(l => l.langCode != "english").Select(l => l.langCode).ToList();
        int originalCount = document.Entries.Values.Where(l => l.TryGetValue("english", out var source) && !string.IsNullOrWhiteSpace(source))
            .Sum(l => database.TargetLanguages.Count(code => !l.TryGetValue(code, out var text) || string.IsNullOrWhiteSpace(text)));
        Check(originalCount == 94, "Reproduce the reported 94 requests with the previous all-fields/all-languages behavior.");
        var requests = SteamContentTranslation.CreateRequests(document, database, "");
        Check(requests.Count == 2 && requests.All(r => r.CustomId.EndsWith("|malay")),
            "Actual store export produces only two Malay requests, even with Arabic selected.");
        Check(requests.All(r => r.CustomId.StartsWith("app[content][about]|") || r.CustomId.StartsWith("app[content][short_description]|")),
            "Actual store requests cover only about and short description.");
        Check(database.TargetLanguages.Contains("arabic"), "Arabic is still selected for in-game translations.");
        Check(CSVParser.WriteToString(document.Export()) == CSVParser.WriteToString(rows), "Store source rows and existing translations remain unchanged.");
        UnityEngine.Object.DestroyImmediate(database);
    }
}
