using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DxpLogViewer;

public class LogEntry
{
    public DateTime Time { get; init; }
    public string Severity { get; init; } = "";
    public string Source { get; init; } = "";
    public string Message { get; init; } = "";
    public string Category { get; init; } = "";
    public string Container { get; init; } = "";
    public string Host { get; init; } = "";
    public string File { get; init; } = "";
    public string RawJson { get; init; } = "";
    public string SearchText { get; init; } = "";
    public Dictionary<string, string> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    static readonly Regex Prefix = new(@"^\[(\d\d:\d\d:\d\d) (\w{3})\]\s*", RegexOptions.Compiled);
    static readonly Regex SourceRx = new(@"^((?:[A-Za-z_]\w*\.)+[A-Za-z_][\w`+]*):\s+", RegexOptions.Compiled);

    static string LongSeverity(string s) => s switch
    {
        "VRB" => "Verbose", "DBG" => "Debug", "INF" => "Info", "WRN" => "Warning",
        "ERR" => "Error", "FTL" => "Fatal", _ => s
    };

    public static LogEntry? Parse(string line, string file)
    {
        line = line.Trim();
        if (line.Length == 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            var f = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in root.EnumerateObject())
                f[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString();

            string G(string k) => f.TryGetValue(k, out var v) ? v : "";
            var msg = G("resultDescription");
            var sev = "";
            var src = "";
            var m = Prefix.Match(msg);
            if (m.Success) { sev = LongSeverity(m.Groups[2].Value); msg = msg[m.Length..]; }
            var sm = SourceRx.Match(msg);
            if (sm.Success) { src = sm.Groups[1].Value; msg = msg[sm.Length..]; }
            if (sev == "") sev = G("level");

            DateTime.TryParse(G("time"), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t);
            return new LogEntry
            {
                Time = t, Severity = sev, Source = src, Message = msg.TrimEnd(),
                Category = G("category"), Container = G("containerId"), Host = G("Host"),
                File = file, RawJson = line, Fields = f,
                SearchText = G("time") + "\n" + sev + "\n" + src + "\n" + msg + "\n" + string.Join('\n', f.Values)
            };
        }
        catch (JsonException) { return null; }
    }

    public static IEnumerable<LogEntry> LoadFile(string path)
    {
        var name = ShortName(path);
        foreach (var line in System.IO.File.ReadLines(path))
        {
            var e = Parse(line, name);
            if (e != null) yield return e;
        }
    }

    // .../y=2026/m=10/d=06/h=09/m=00/PT1H.json -> "2026-10-06 09h"
    static string ShortName(string path)
    {
        var parts = path.Split('\\', '/').ToList();
        string P(string k) => parts.FirstOrDefault(x => x.StartsWith(k + "=")) is { } s ? s[(k.Length + 1)..] : "";
        var y = P("y"); var d = P("d"); var h = P("h");
        var mo = P("m"); // first m= is month
        return y != "" && d != "" ? $"{y}-{mo}-{d} {h}h" : Path.GetFileName(path);
    }
}
