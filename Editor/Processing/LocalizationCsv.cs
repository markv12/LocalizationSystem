using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// The id,english,context CSV format shared by drop files and imports.
/// </summary>
public static class LocalizationCsv {
    public static readonly string[] Columns = { "id", "english", "context" };

    public class RowSet {
        public readonly List<string> Order = new List<string>();
        public readonly Dictionary<string, (string english, string context)> Values =
            new Dictionary<string, (string, string)>();

        public bool Set(string key, string english, string context) {
            bool isNew = !Values.ContainsKey(key);
            if (isNew) Order.Add(key);
            Values[key] = (english, context);
            return isNew;
        }
    }

    public static RowSet Read(string path) {
        var rows = new RowSet();
        if (!File.Exists(path)) return rows;

        var grid = CSVParser.LoadFromString(File.ReadAllText(path));
        if (grid == null || grid.Count < 2) return rows;

        var columns = FindColumns(grid[0]);
        if (columns == null) return rows;
        var (keyIndex, englishIndex, contextIndex) = columns.Value;

        foreach (var row in grid.Skip(1)) {
            if (row.Count <= keyIndex || string.IsNullOrWhiteSpace(row[keyIndex])) continue;
            rows.Set(row[keyIndex],
                row.Count > englishIndex ? row[englishIndex] : "",
                contextIndex >= 0 && row.Count > contextIndex ? row[contextIndex] : "");
        }
        return rows;
    }

    public static void Write(string path, RowSet rows) {
        var grid = new List<List<string>> { Columns.ToList() };
        foreach (string key in rows.Order) {
            var value = rows.Values[key];
            grid.Add(new List<string> { key, value.english, value.context });
        }
        string directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, CSVParser.WriteToString(grid), new UTF8Encoding(true));
    }

    public static (int key, int english, int context)? FindColumns(List<string> headers) {
        int key = headers.FindIndex(h => h.Equals("id", StringComparison.OrdinalIgnoreCase));
        int english = headers.FindIndex(h => h.Equals("english", StringComparison.OrdinalIgnoreCase));
        int context = headers.FindIndex(h => h.Equals("context", StringComparison.OrdinalIgnoreCase));
        return key >= 0 && english >= 0 ? (key, english, context) : ((int, int, int)?)null;
    }
}
