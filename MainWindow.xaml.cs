using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace DxpLogViewer;

public partial class MainWindow : Window
{
    const string All = "(All)";
    readonly ObservableCollection<LogEntry> _entries = new();
    readonly HashSet<string> _loaded = new(StringComparer.OrdinalIgnoreCase);
    readonly ICollectionView _view;
    bool _suspend;

    List<(bool neg, string? field, string term, Regex? rx)> _terms = new();
    DateTime? _from, _to;
    bool _regexError;

    public MainWindow()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_entries);
        _view.SortDescriptions.Add(new SortDescription(nameof(LogEntry.Time), ListSortDirection.Ascending));
        _view.Filter = Filter;
        Grid.ItemsSource = _view;
        RefreshCombos();
        var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (args.Length > 0) _ = LoadPathsAsync(args);
    }

    // ---------- loading ----------
    static IEnumerable<string> Expand(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                foreach (var f in Directory.EnumerateFiles(p, "*.json", SearchOption.AllDirectories)) yield return f;
            else if (File.Exists(p)) yield return p;
        }
    }

    async Task LoadPathsAsync(IEnumerable<string> paths)
    {
        var files = Expand(paths).Where(f => !_loaded.Contains(f)).ToList();
        if (files.Count == 0) return;
        StatusText.Text = $"Loading {files.Count} file(s)...";
        var loaded = await Task.Run(() => files.Select(f => (f, list: LogEntry.LoadFile(f).ToList())).ToList());
        foreach (var (f, list) in loaded)
        {
            _loaded.Add(f);
            foreach (var e in list) _entries.Add(e);
        }
        RefreshCombos();
        _view.Refresh();
        UpdateStatus();
    }

    void OpenFiles_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Multiselect = true, Filter = "Log files (*.json;*.log;*.txt)|*.json;*.log;*.txt|All files|*.*" };
        if (dlg.ShowDialog() == true) _ = LoadPathsAsync(dlg.FileNames);
    }

    void OpenFolder_Click(object s, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select log folder (searched recursively)" };
        if (dlg.ShowDialog() == true) _ = LoadPathsAsync(new[] { dlg.FolderName });
    }

    void Window_Drop(object s, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] p) _ = LoadPathsAsync(p);
    }

    void Clear_Click(object s, RoutedEventArgs e)
    {
        _entries.Clear(); _loaded.Clear(); RefreshCombos(); UpdateStatus();
    }

    // ---------- filtering ----------
    void RefreshCombos()
    {
        _suspend = true;
        void Fill(ComboBox cb, Func<LogEntry, string> sel)
        {
            var cur = cb.SelectedItem as string;
            var items = new List<string> { All };
            items.AddRange(_entries.Select(sel).Where(x => x != "").Distinct().OrderBy(x => x));
            cb.ItemsSource = items;
            cb.SelectedItem = cur != null && items.Contains(cur) ? cur : All;
        }
        Fill(SevBox, x => x.Severity); Fill(SrcBox, x => x.Source); Fill(CatBox, x => x.Category);
        Fill(ConBox, x => x.Container); Fill(HostBox, x => x.Host);
        _suspend = false;
        BuildQuery();
    }

    static readonly Regex Tokenizer = new("(-?)(?:(\\w+):)?(?:\"([^\"]*)\"|(\\S+))", RegexOptions.Compiled);

    DateTime? ParseBound(string s)
    {
        s = s.Trim();
        if (s == "") return null;
        if (!s.Contains('-') && TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out var ts))
        {
            var day = _entries.Count > 0 ? _entries.Min(x => x.Time).Date : DateTime.UtcNow.Date;
            return day + ts;
        }
        return DateTime.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    }

    void BuildQuery()
    {
        _terms = new();
        _regexError = false;
        var q = SearchBox.Text ?? "";
        if (RegexBox.IsChecked == true)
        {
            if (q != "")
            {
                try { _terms.Add((false, null, q, new Regex(q, RegexOptions.IgnoreCase))); }
                catch (ArgumentException) { _regexError = true; }
            }
        }
        else
        {
            foreach (Match m in Tokenizer.Matches(q))
            {
                var term = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
                if (term == "") continue;
                _terms.Add((m.Groups[1].Value == "-", m.Groups[2].Success ? m.Groups[2].Value : null, term, null));
            }
        }
        _from = ParseBound(FromBox.Text);
        _to = ParseBound(ToBox.Text);
    }

    bool Filter(object o)
    {
        var e = (LogEntry)o;
        if (SevBox.SelectedItem is string s && s != All && e.Severity != s) return false;
        if (SrcBox.SelectedItem is string sr && sr != All && e.Source != sr) return false;
        if (CatBox.SelectedItem is string c && c != All && e.Category != c) return false;
        if (ConBox.SelectedItem is string co && co != All && e.Container != co) return false;
        if (HostBox.SelectedItem is string h && h != All && e.Host != h) return false;
        if (_from != null && e.Time < _from) return false;
        if (_to != null && e.Time > _to) return false;
        foreach (var (neg, field, term, rx) in _terms)
        {
            bool hit = field != null
                ? FieldValue(e, field) is { } v && Match(v, term, rx)
                : Match(e.SearchText, term, rx);
            if (hit == neg) return false;
        }
        return true;
    }

    static bool Match(string hay, string term, Regex? rx) =>
        rx != null ? rx.IsMatch(hay) : hay.Contains(term, StringComparison.OrdinalIgnoreCase);

    static string? FieldValue(LogEntry e, string f) => f.ToLowerInvariant() switch
    {
        "severity" or "sev" => e.Severity,
        "source" or "src" => e.Source,
        "message" or "msg" => e.Message,
        "time" => e.Time.ToString("yyyy-MM-dd HH:mm:ss.fff"),
        "file" => e.File,
        _ => e.Fields.TryGetValue(f, out var v) ? v : null
    };

    void FilterChanged(object s, EventArgs e)
    {
        if (_suspend || !IsInitialized || _view == null) return;
        BuildQuery();
        _view.Refresh();
        UpdateStatus();
        SearchBox.Background = _regexError ? System.Windows.Media.Brushes.MistyRose : System.Windows.Media.Brushes.White;
    }

    void Reset_Click(object s, RoutedEventArgs e)
    {
        _suspend = true;
        SearchBox.Text = ""; FromBox.Text = ""; ToBox.Text = ""; RegexBox.IsChecked = false;
        foreach (var cb in new[] { SevBox, SrcBox, CatBox, ConBox, HostBox }) cb.SelectedItem = All;
        _suspend = false;
        FilterChanged(this, EventArgs.Empty);
    }

    void UpdateStatus()
    {
        var shown = _view.Cast<object>().Count();
        var err = _entries.Count(x => x.Severity is "Error" or "Fatal");
        var warn = _entries.Count(x => x.Severity == "Warning");
        StatusText.Text = $"Showing {shown:N0} of {_entries.Count:N0} entries from {_loaded.Count} file(s)   |   {err:N0} errors, {warn:N0} warnings";
    }

    // ---------- details ----------
    void Grid_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is LogEntry le)
        {
            DetailBox.Text = $"{le.Time:yyyy-MM-dd HH:mm:ss.fff} UTC  [{le.Severity}]  {le.Source}\r\n\r\n{le.Message}";
            FieldsGrid.ItemsSource = le.Fields.ToList();
            RawBox.Text = le.RawJson;
        }
    }

    void Export_Click(object s, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "logs.csv" };
        if (dlg.ShowDialog() != true) return;
        static string Q(string v) => "\"" + v.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder("Time,Severity,Source,Message,Category,Container,Host,File\r\n");
        foreach (LogEntry x in _view)
            sb.AppendLine(string.Join(',', new[] { x.Time.ToString("o"), x.Severity, x.Source, x.Message, x.Category, x.Container, x.Host, x.File }.Select(Q)));
        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
    }
}
