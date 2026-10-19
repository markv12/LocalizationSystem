using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Only the runtime resource constants are needed in this isolated editor project.
public static class Localizer {
    public const string DEFAULT_LANGUAGE = "english";
    public const string LANGUAGE_LIST_KEY = "LanguageList";
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

    public static void Run() {
        string report = Path.Combine(Path.GetDirectoryName(Application.dataPath), "result.txt");
        try {
            DatabaseLifecycle(); RoundTrips(); Validation(); RequestsAndSnapshots(); SharedRequests(); MergeResults(); SettingsPersistence();
            string[] args = Environment.GetCommandLineArgs();
            int fileArg = Array.IndexOf(args, "-steamEventCsv");
            if (fileArg >= 0) RealExport(args[fileArg + 1]);
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
        var store = new SteamContentCsv(CSVParser.LoadFromString("key,language,text\na,english,Hello"), SteamContentType.StorePage);
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
        database.OpenAIModel = "gpt-6-sol";
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
}
