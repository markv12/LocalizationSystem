using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// JsonUtility owns serialization and deserialization. The small JSON view below
// checks token shapes that Unity otherwise coerces (for example, 123 into "123").
public static class OpenAIJson {
    public sealed class Request {
        public string CustomId { get; }
        public string Json { get; }
        internal Request(string customId, string json) { CustomId = customId; Json = json; }
        public override string ToString() => Json;
    }

    [Serializable] private class RequestHeader {
        public string custom_id;
        public string method = "POST";
        public string url = OpenAITranslationModels.Endpoint;
    }
    [Serializable] private class ReasoningField { public string reasoning_effort; }
    [Serializable] private class FormatField { public ResponseFormat response_format; }
    [Serializable] public class Message { public string role; public string content; public string refusal; }
    // Request messages must not contain the response-only refusal field.
    [Serializable] private class RequestMessage { public string role; public string content; }
    [Serializable] private class MessagesBody {
        public string model;
        public RequestMessage[] messages;
        public int max_completion_tokens;
    }

    internal static Request CreateRequest(string id, string model, string role, string system, string user,
        string reasoning, ResponseFormat format, int tokens) {
        string body = JsonUtility.ToJson(new MessagesBody {
            model = model, max_completion_tokens = tokens,
            messages = new[] {
                new RequestMessage { role = role, content = system },
                new RequestMessage { role = "user", content = user }
            }
        });
        if (NeedsRepair(system) || NeedsRepair(user)) {
            var edits = new List<StringEdit>();
            int index = 0;
            foreach (var message in new JsonValue(body).Field("messages").Elements()) {
                AddRepair(edits, message.Field("content"), index++ == 0 ? system : user);
            }
            body = ApplyEdits(body, edits);
        }
        if (reasoning != null) body = Merge(body, JsonUtility.ToJson(new ReasoningField { reasoning_effort = reasoning }));
        if (format != null) {
            string fields = JsonUtility.ToJson(new FormatField { response_format = format });
            var keys = format.json_schema.schema.properties.translations.items.properties.key.@enum;
            if (keys != null && Array.Exists(keys, NeedsRepair)) {
                var edits = new List<StringEdit>();
                int index = 0;
                foreach (var key in new JsonValue(fields).Field("response_format").Field("json_schema").Field("schema")
                    .Field("properties").Field("translations").Field("items").Field("properties").Field("key").Field("enum").Elements())
                    AddRepair(edits, key, keys[index++]);
                fields = ApplyEdits(fields, edits);
            }
            body = Merge(body, fields);
        }
        string header = JsonUtility.ToJson(new RequestHeader { custom_id = id });
        if (NeedsRepair(id)) header = ApplyEdits(header, new List<StringEdit> { new StringEdit(new JsonValue(header).Field("custom_id"), id) });
        return new Request(id, header.Substring(0, header.Length - 1) + ",\"body\":" + body + "}");
    }

    // Merge only complete objects emitted by JsonUtility, never user-provided JSON.
    private static string Merge(string json, string fields) => json.Substring(0, json.Length - 1) + "," + fields.Substring(1);

    [Serializable] public class ResponseFormat {
        public string type = "json_schema";
        public JsonSchema json_schema = new JsonSchema();
    }
    [Serializable] public class JsonSchema {
        public string name = "translation_response";
        public bool strict = true;
        public TranslationSchema schema = new TranslationSchema();
    }
    [Serializable] public class TranslationSchema {
        public string type = "object";
        public TranslationProperties properties = new TranslationProperties();
        public string[] required = { "translations" };
        public bool additionalProperties;
    }
    [Serializable] public class TranslationProperties { public ArraySchema translations = new ArraySchema(); }
    [Serializable] public class ArraySchema {
        public string type = "array";
        public ItemSchema items = new ItemSchema();
    }
    [Serializable] public class ItemSchema {
        public string type = "object";
        public ItemProperties properties = new ItemProperties();
        public string[] required = { "key", "text" };
        public bool additionalProperties;
    }
    [Serializable] public class ItemProperties {
        public KeySchema key = new KeySchema();
        public StringSchema text = new StringSchema();
    }
    [Serializable] public class StringSchema { public string type = "string"; }
    [Serializable] public class KeySchema : StringSchema { public string[] @enum; }
    [Serializable] public class Prompt { public PromptItem[] items; }
    [Serializable] public class PromptItem { public string key; public string text; public string context; }
    [Serializable] public class CreateBatch {
        public string input_file_id;
        public string endpoint = OpenAITranslationModels.Endpoint;
        public string completion_window = "24h";
    }
    [Serializable] public class FileResult { public string id; }
    [Serializable] public class BatchStatus {
        public string status;
        public string output_file_id;
        public string error_file_id;
        public RequestCounts request_counts;
        public Errors errors;
    }
    [Serializable] public class RequestCounts { public int total; public int completed; public int failed; }
    [Serializable] public class Errors { public Error[] data; }
    [Serializable] public class Error { public string message; }
    [Serializable] public class BatchResult { public string custom_id; public Response response; public Error error; }
    [Serializable] public class Response { public int status_code; public ResponseBody body; }
    [Serializable] public class ResponseBody { public Choice[] choices; public Error error; }
    [Serializable] public class Choice { public string finish_reason; public Message message; }
    [Serializable] public class Translations { public Translation[] translations; }
    [Serializable] public class Translation { public string key; public string text; }

    // Unity writes null strings as "" and truncates strings at NUL. Repair only
    // those exceptional fields; normal serialization stays on the fast path.
    public static string SerializePrompt(Prompt prompt) {
        string json = JsonUtility.ToJson(prompt);
        if (!Array.Exists(prompt.items, item => NeedsRepair(item.key) || NeedsRepair(item.text) || NeedsRepair(item.context)))
            return CanonicalJson(json);
        var edits = new List<StringEdit>();
        int index = 0;
        foreach (var item in new JsonValue(json).Field("items").Elements()) {
            var data = prompt.items[index++];
            if (NeedsRepair(data.key)) AddRepair(edits, item.Field("key"), data.key);
            if (NeedsRepair(data.text)) AddRepair(edits, item.Field("text"), data.text);
            if (NeedsRepair(data.context)) AddRepair(edits, item.Field("context"), data.context);
        }
        return CanonicalJson(ApplyEdits(json, edits));
    }

    public static string SerializeSourceContext(string title, string context) {
        // Keep the exact historical escaping used by persisted Steam source hashes.
        return CanonicalJson("{\"title\":" + Quote(title) + ",\"context\":" + Quote(context) + "}");
    }

    private static string CanonicalJson(string json) {
        json = json.Replace("\u0085", "\\u0085").Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
        char[] normalized = null;
        for (int i = 0; i < json.Length - 1; i++) {
            if (json[i] != '\\') continue;
            if (json[i + 1] == 'u' && i + 5 < json.Length) {
                for (int j = i + 2; j < i + 6; j++) {
                    if (json[j] >= 'A' && json[j] <= 'F') {
                        if (normalized == null) normalized = json.ToCharArray();
                        normalized[j] = char.ToLowerInvariant(json[j]);
                    }
                }
                i += 5;
            } else i++; // Do not alter literal text such as "\\\\u001F".
        }
        return normalized == null ? json : new string(normalized);
    }

    [Serializable] private class StringValue { public string value; }
    [Serializable] private class IntValue { public int value; }
    public static string Quote(string value) {
        if (value == null) return "null";
        if (value.IndexOf('\0') < 0) {
            string json = JsonUtility.ToJson(new StringValue { value = value });
            return json.Substring(9, json.Length - 10);
        }
        // Let Unity escape each segment; only the NUL separator is supplied here.
        var result = new System.Text.StringBuilder("\"");
        bool first = true;
        foreach (string segment in value.Split('\0')) {
            if (!first) result.Append("\\u0000");
            first = false;
            string quoted = Quote(segment);
            result.Append(quoted, 1, quoted.Length - 2);
        }
        return result.Append('"').ToString();
    }

    private static bool NeedsRepair(string value) => value == null || value.IndexOf('\0') >= 0;
    private readonly struct StringEdit {
        public readonly JsonValue Token;
        public readonly string Value;
        public StringEdit(JsonValue token, string value) { Token = token; Value = value; }
    }
    private static void AddRepair(List<StringEdit> edits, JsonValue token, string value) {
        if (NeedsRepair(value)) edits.Add(new StringEdit(token, value));
    }
    private static string ApplyEdits(string json, List<StringEdit> edits) {
        if (edits.Count == 0) return json;
        edits.Sort((a, b) => a.Token.Start.CompareTo(b.Token.Start));
        var result = new System.Text.StringBuilder(json.Length);
        int position = 0;
        foreach (var edit in edits) {
            result.Append(json, position, edit.Token.Start - position).Append(Quote(edit.Value));
            position = edit.Token.End;
        }
        return result.Append(json, position, json.Length - position).ToString();
    }

    public static T ReadObject<T>(string json) {
        if (!new JsonValue(json).IsObject) throw new ArgumentException("Expected a JSON object.");
        return Deserialize<T>(json);
    }

    private static T Deserialize<T>(string json) {
        string marker = null;
        if (json.IndexOf("\\u0000", StringComparison.Ordinal) >= 0) {
            // Choose a private-use character absent in both literal and escaped
            // form, so a decoded NUL survives Unity's native string boundary.
            int code = 0xe000;
            while (code <= 0xf8ff && (json.IndexOf((char)code) >= 0 ||
                json.IndexOf("\\u" + code.ToString("x4"), StringComparison.OrdinalIgnoreCase) >= 0)) code++;
            if (code <= 0xf8ff) marker = ((char)code).ToString();
            else {
                // All private-use characters are present. A non-overlapping
                // marker longer than the total E000 count cannot occur in input.
                int count = 0;
                for (int i = 0; i < json.Length; i++) {
                    if (json[i] == '\ue000') count++;
                    else if (i + 5 < json.Length && string.Compare(json, i, "\\ue000", 0, 6, StringComparison.OrdinalIgnoreCase) == 0) count++;
                }
                marker = new string('\ue000', count + 1) + "\ue001";
            }
            var masked = new System.Text.StringBuilder(json.Length);
            for (int i = 0; i < json.Length; i++) {
                if (json[i] == '\\' && i + 1 < json.Length) {
                    if (i + 5 < json.Length && string.CompareOrdinal(json, i, "\\u0000", 0, 6) == 0) {
                        masked.Append(marker); i += 5;
                    } else { masked.Append(json[i]).Append(json[++i]); }
                } else masked.Append(json[i]);
            }
            json = masked.ToString();
        }
        var value = JsonUtility.FromJson<T>(json);
        if (marker != null) RestoreNuls(value, marker);
        return value;
    }

    // Only documents containing an encoded NUL need this walk. DTOs are acyclic.
    private static void RestoreNuls(object value, string marker) {
        if (value == null) return;
        if (value is System.Collections.IList list) {
            for (int i = 0; i < list.Count; i++) {
                if (list[i] is string text) list[i] = text.Replace(marker, "\0");
                else RestoreNuls(list[i], marker);
            }
        } else {
            foreach (var field in value.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)) {
                if (field.FieldType == typeof(string)) {
                    if (field.GetValue(value) is string text) field.SetValue(value, text.Replace(marker, "\0"));
                } else if (!field.FieldType.IsValueType) RestoreNuls(field.GetValue(value), marker);
            }
        }
    }

    public static BatchResult ReadBatchResult(string json) {
        var result = ReadObject<BatchResult>(json);
        var root = new JsonValue(json);
        result.custom_id = LastString(root, "custom_id", result.custom_id);
        result.error = ReadError(root.Field("error"), true);
        var response = root.Field("response", out bool duplicate);
        if (!response.IsObject) { result.response = null; return result; }
        if (duplicate) result.response = Deserialize<Response>(response.Json);
        var status = response.Field("status_code", out duplicate);
        if (duplicate) result.response.status_code = Deserialize<IntValue>("{\"value\":" + status.Json + "}").value;
        var body = response.Field("body", out duplicate);
        if (!body.IsObject) { result.response.body = null; return result; }
        if (duplicate) result.response.body = Deserialize<ResponseBody>(body.Json);
        result.response.body.error = ReadError(body.Field("error"));
        var choices = body.Field("choices", out duplicate);
        if (!choices.IsArray) { result.response.body.choices = null; return result; }
        if (duplicate) result.response.body.choices = Deserialize<ResponseBody>("{\"choices\":" + choices.Json + "}").choices;
        foreach (var choice in choices.Elements()) {
            var parsed = result.response.body.choices[0];
            if (!choice.IsObject) { result.response.body.choices[0] = null; break; }
            var finish = choice.Field("finish_reason", out duplicate);
            if (duplicate) parsed.finish_reason = ReadString(finish);
            var message = choice.Field("message", out duplicate);
            if (!message.IsObject) { parsed.message = null; break; }
            if (duplicate) parsed.message = Deserialize<Message>(message.Json);
            var content = message.Field("content", out duplicate);
            if (!content.IsString) parsed.message.content = null;
            else if (duplicate) parsed.message.content = ReadString(content);
            var refusal = message.Field("refusal", out duplicate);
            if (refusal.IsObject || refusal.IsArray) parsed.message.refusal = refusal.Json;
            else if (duplicate) parsed.message.refusal = ReadString(refusal);
            break; // Only the first choice is imported, matching the API contract.
        }
        return result;
    }

    private static Error ReadError(JsonValue value, bool fallbackToJson = false) {
        if (!value.IsObject) return null;
        return new Error { message = ReadString(value.Field("message")) ?? (fallbackToJson ? value.Json : null) };
    }

    private static string ReadString(JsonValue value) {
        if (value.IsMissing) return null;
        if (value.Json == "null") return "";
        if (value.IsObject || value.IsArray) return value.Json;
        return Deserialize<StringValue>("{\"value\":" + value.Json + "}").value;
    }

    private static string LastString(JsonValue parent, string name, string parsed) {
        var field = parent.Field(name, out bool duplicate);
        return duplicate ? ReadString(field) : parsed;
    }

    private static int LastInt(JsonValue parent, string name, int parsed) {
        var field = parent.Field(name, out bool duplicate);
        return duplicate ? Deserialize<IntValue>("{\"value\":" + field.Json + "}").value : parsed;
    }

    public static string ReadFileId(string json) {
        var result = ReadObject<FileResult>(json);
        return LastString(new JsonValue(json), "id", result.id);
    }

    public static BatchStatus ReadBatchStatus(string json) {
        var result = ReadObject<BatchStatus>(json);
        var root = new JsonValue(json);
        result.status = LastString(root, "status", result.status);
        result.output_file_id = LastString(root, "output_file_id", result.output_file_id);
        result.error_file_id = LastString(root, "error_file_id", result.error_file_id);
        var counts = root.Field("request_counts", out bool duplicate);
        if (!counts.IsObject) result.request_counts = null;
        else {
            if (duplicate) result.request_counts = Deserialize<RequestCounts>(counts.Json);
            result.request_counts.total = LastInt(counts, "total", result.request_counts.total);
            result.request_counts.completed = LastInt(counts, "completed", result.request_counts.completed);
            result.request_counts.failed = LastInt(counts, "failed", result.request_counts.failed);
        }
        var errors = root.Field("errors");
        var data = errors.Field("data");
        if (!data.IsArray) result.errors = null;
        else {
            // Parsing the selected data field also handles duplicate errors/data containers.
            result.errors = Deserialize<Errors>("{\"data\":" + data.Json + "}");
            int index = 0;
            foreach (var item in data.Elements()) result.errors.data[index++] = ReadError(item);
        }
        return result;
    }

    public static Translation[] ReadTranslations(string json) {
        var result = ReadObject<Translations>(json);
        var array = new JsonValue(json).Field("translations", out bool duplicate);
        if (!array.IsArray) return null;
        if (duplicate) result = Deserialize<Translations>("{\"translations\":" + array.Json + "}");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (var item in array.Elements()) {
            var key = item.Field("key", out bool duplicateKey);
            var text = item.Field("text", out bool duplicateText);
            if (!item.IsObject || !key.IsString || !text.IsString)
                throw new InvalidDataException("Translation response has invalid or duplicate keys/text.");
            var parsed = result.translations[index++];
            if (duplicateKey) parsed.key = ReadString(key);
            if (duplicateText) parsed.text = ReadString(text);
            if (!seen.Add(parsed.key)) throw new InvalidDataException("Translation response has invalid or duplicate keys/text.");
        }
        return result.translations;
    }

    // A view into already-parsed JSON: no object tree, scalar conversions, or JSON writer.
    // It exists only to distinguish missing/null/object/array/string before trusting DTOs.
    private readonly struct JsonValue {
        private readonly string source;
        private readonly int start;
        private readonly int end;
        public JsonValue(string source) {
            this.source = source ?? "";
            start = SkipSpace(this.source, 0);
            end = this.source.Length;
        }
        private JsonValue(string source, int start, int end) { this.source = source; this.start = start; this.end = end; }
        private char Kind => source != null && start < end ? source[start] : '\0';
        public bool IsObject => Kind == '{';
        public bool IsArray => Kind == '[';
        public bool IsString => Kind == '"';
        public bool IsMissing => Kind == '\0';
        public int Start => start;
        public int End => end;
        public string Json => source.Substring(start, end - start);
        public JsonValue Field(string name) => Field(name, out _);
        public JsonValue Field(string name, out bool duplicate) {
            duplicate = false;
            if (!IsObject) return default;
            JsonValue found = default;
            int position = SkipSpace(source, start + 1);
            while (position < end && source[position] != '}') {
                int nameStart = position;
                int nameEnd = StringEnd(source, position);
                bool matches = source.IndexOf('\\', nameStart, nameEnd - nameStart) >= 0
                    ? Deserialize<FieldName>("{\"value\":" + source.Substring(nameStart, nameEnd - nameStart) + "}").value == name
                    : nameEnd - nameStart == name.Length + 2 && string.CompareOrdinal(source, nameStart + 1, name, 0, name.Length) == 0;
                position = SkipSpace(source, nameEnd);
                if (position >= end || source[position] != ':') throw new ArgumentException("Invalid JSON member.");
                position = SkipSpace(source, position + 1);
                int valueEnd = ValueEnd(source, position);
                if (matches) {
                    duplicate |= !found.IsMissing;
                    found = new JsonValue(source, position, valueEnd);
                }
                position = SkipSpace(source, valueEnd);
                if (position < end && source[position] == ',') position = SkipSpace(source, position + 1);
                else break;
            }
            return found;
        }
        public IEnumerable<JsonValue> Elements() {
            if (!IsArray) yield break;
            int position = SkipSpace(source, start + 1);
            while (position < end && source[position] != ']') {
                int valueEnd = ValueEnd(source, position);
                yield return new JsonValue(source, position, valueEnd);
                position = SkipSpace(source, valueEnd);
                if (position < end && source[position] == ',') position = SkipSpace(source, position + 1);
                else break;
            }
        }
        private static int SkipSpace(string text, int position) {
            while (position < text.Length && char.IsWhiteSpace(text[position])) position++;
            return position;
        }
        private static int StringEnd(string text, int position) {
            if (position >= text.Length || text[position] != '"') throw new ArgumentException("Invalid JSON string.");
            for (position++; position < text.Length; position++) {
                if (text[position] == '\\') position++;
                else if (text[position] == '"') return position + 1;
            }
            throw new ArgumentException("Unterminated JSON string.");
        }
        private static int ValueEnd(string text, int position) {
            if (position >= text.Length) throw new ArgumentException("Missing JSON value.");
            if (text[position] == '"') return StringEnd(text, position);
            if (text[position] == '{' || text[position] == '[') {
                int depth = 0;
                for (; position < text.Length; position++) {
                    char c = text[position];
                    if (c == '"') position = StringEnd(text, position) - 1;
                    else if (c == '{' || c == '[') depth++;
                    else if ((c == '}' || c == ']') && --depth == 0) return position + 1;
                }
                throw new ArgumentException("Unterminated JSON value.");
            }
            while (position < text.Length && text[position] != ',' && text[position] != '}' && text[position] != ']' && !char.IsWhiteSpace(text[position])) position++;
            return position;
        }
    }
    [Serializable] private class FieldName { public string value; }
}
