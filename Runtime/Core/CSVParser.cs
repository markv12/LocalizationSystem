/*
 * CSV Parser for C#.
 *
 * These codes are licensed under CC0.
 * https://github.com/yutokun/CSV-Parser
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public static class CSVParser {
    public enum Delimiter {
        Auto,
        Comma,
        Tab
    }

    public static List<List<string>> LoadFromPath(string path, Delimiter delimiter = Delimiter.Auto, Encoding encoding = null) {
        encoding = encoding ?? Encoding.UTF8;

        if (delimiter == Delimiter.Auto) {
            delimiter = EstimateDelimiter(path);
        }

        var data = File.ReadAllText(path, encoding);
        return Parse(data, delimiter, 1);
    }

    public static async Task<List<List<string>>> LoadFromPathAsync(string path, Delimiter delimiter = Delimiter.Auto, Encoding encoding = null) {
        encoding = encoding ?? Encoding.UTF8;

        if (delimiter == Delimiter.Auto) {
            delimiter = EstimateDelimiter(path);
        }

        using (var reader = new StreamReader(path, encoding)) {
            var data = await reader.ReadToEndAsync();
            return Parse(data, delimiter, 1);
        }
    }

    static Delimiter EstimateDelimiter(string path) {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase)) {
            return Delimiter.Comma;
        }

        if (extension.Equals(".tsv", StringComparison.OrdinalIgnoreCase)) {
            return Delimiter.Tab;
        }

        throw new Exception($"Delimiter estimation failed. Unknown Extension: {extension}");
    }

    public static List<List<string>> LoadFromString(string data, Delimiter delimiter = Delimiter.Comma, int expectedColumns = 1) {
        if (delimiter == Delimiter.Auto) throw new InvalidEnumArgumentException("Delimiter estimation from string is not supported.");
        return Parse(data, delimiter, expectedColumns);
    }

    static List<List<string>> Parse(string data, Delimiter delimiter, int expectedColumns) {
        data = Regex.Replace(data, @"\r\n|\r", "\n");

        var rows = new List<List<string>>();
        var columns = new List<string>(expectedColumns);

        var start = 0;
        var end = 0;
        var insideQuote = false;
        var quoteCount = 0;
        var delimiterChar = delimiter == Delimiter.Comma ? ',' : '\t';

        while (end < data.Length) {
            var c = data[end];

            if (c == '"') {
                insideQuote = !insideQuote;
                quoteCount++;
            } else if (c == delimiterChar && !insideQuote) {
                var value = data.Substring(start, end - start);
                if (quoteCount > 0) {
                    value = Unquote(value);
                }

                columns.Add(value);
                start = end + 1;
                quoteCount = 0;
            } else if (c == '\n' && !insideQuote) {
                var value = data.Substring(start, end - start);
                if (quoteCount > 0) {
                    value = Unquote(value);
                }

                columns.Add(value);
                rows.Add(columns);
                columns = new List<string>(expectedColumns);
                start = end + 1;
                quoteCount = 0;
            }

            end++;
        }

        if (start < data.Length) {
            var value = data.Substring(start, end - start);
            if (quoteCount > 0) {
                value = Unquote(value);
            }

            columns.Add(value);
            rows.Add(columns);
        }

        return rows;
    }

    static string Unquote(string value) {
        if (value.StartsWith("\"") && value.EndsWith("\"")) {
            value = value.Substring(1, value.Length - 2);
        }

        return value.Replace("\"\"", "\"");
    }

    public static string WriteToString(List<List<string>> data, Delimiter delimiter = Delimiter.Comma) {
        var sb = new StringBuilder();
        var delimiterChar = delimiter == Delimiter.Comma ? ',' : '\t';

        for (var i = 0; i < data.Count; i++) {
            var row = data[i];
            for (var j = 0; j < row.Count; j++) {
                var value = row[j] ?? "";
                if (value.Contains("\"") || value.Contains(",") || value.Contains("\n") || value.Contains("\r") || value.Contains("\t")) {
                    value = "\"" + value.Replace("\"", "\"\"") + "\"";
                }

                sb.Append(value);
                if (j < row.Count - 1) {
                    sb.Append(delimiterChar);
                }
            }

            if (i < data.Count - 1) {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
