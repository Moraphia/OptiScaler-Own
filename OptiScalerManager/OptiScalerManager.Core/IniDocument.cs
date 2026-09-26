using System.Text;

namespace OptiScalerManager.Core;

/// <summary>Small comment-preserving INI editor for manager-owned changes.</summary>
public sealed class IniDocument
{
    private readonly List<string> _lines;
    private readonly Encoding _encoding;
    private readonly string _newline;
    private readonly bool _trailingNewline;
    private IniDocument(List<string> lines, Encoding encoding, string newline, bool trailingNewline) { _lines = lines; _encoding = encoding; _newline = newline; _trailingNewline = trailingNewline; }

    public static IniDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding encoding;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) encoding = new UTF8Encoding(true, true);
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) encoding = new UnicodeEncoding(false, true, true);
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) encoding = new UnicodeEncoding(true, true, true);
        else encoding = new UTF8Encoding(false, true);
        var text = encoding.GetString(bytes).TrimStart('\uFEFF');
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\n') ? "\n" : text.Contains('\r') ? "\r" : Environment.NewLine;
        var trailing = text.EndsWith(newline, StringComparison.Ordinal);
        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None).ToList();
        if (trailing) lines.RemoveAt(lines.Count - 1);
        return new IniDocument(lines, encoding, newline, trailing);
    }

    public string? Get(string section, string key)
    {
        var current = "";
        foreach (var raw in _lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { current = line[1..^1].Trim(); continue; }
            if (!current.Equals(section, StringComparison.OrdinalIgnoreCase) || line.StartsWith(';') || line.StartsWith('#')) continue;
            var split = raw.IndexOf('=');
            if (split <= 0 || !raw[..split].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
            return raw[(split + 1)..].Trim();
        }
        return null;
    }

    public IReadOnlyList<IniEntry> GetEntries()
    {
        var result = new List<IniEntry>(); var section = "";
        foreach (var raw in _lines)
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1].Trim(); continue; }
            if (string.IsNullOrWhiteSpace(section) || line.StartsWith(';') || line.StartsWith('#')) continue;
            var split = raw.IndexOf('='); if (split <= 0) continue;
            result.Add(new IniEntry { Section = section, Key = raw[..split].Trim(), Value = raw[(split + 1)..].Trim() });
        }
        return result;
    }

    public void Set(string section, string key, string? value)
    {
        var sectionStart = -1;
        var sectionEnd = _lines.Count;
        for (var i = 0; i < _lines.Count; i++)
        {
            var line = _lines[i].Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                if (sectionStart >= 0) { sectionEnd = i; break; }
                if (line[1..^1].Trim().Equals(section, StringComparison.OrdinalIgnoreCase)) sectionStart = i;
            }
        }
        if (sectionStart < 0)
        {
            if (_lines.Count > 0 && !string.IsNullOrWhiteSpace(_lines[^1])) _lines.Add("");
            _lines.Add($"[{section}]");
            _lines.Add($"{key}={value ?? ""}");
            return;
        }
        for (var i = sectionStart + 1; i < sectionEnd; i++)
        {
            var split = _lines[i].IndexOf('=');
            if (split > 0 && _lines[i][..split].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                _lines[i] = $"{_lines[i][..split]}={value ?? ""}";
                return;
            }
        }
        _lines.Insert(sectionEnd, $"{key}={value ?? ""}");
    }

    public string Render() => string.Join(_newline, _lines) + (_trailingNewline ? _newline : "");
    public void SaveAtomic(string path)
    {
        var temp = path + ".manager.tmp";
        File.WriteAllText(temp, Render(), _encoding);
        File.Move(temp, path, true);
    }
}

public sealed class IniEntry : System.ComponentModel.INotifyPropertyChanged
{
    private string _value = "";
    private string? _category;
    private string? _description;
    private string? _documentation;
    public string Section { get; init; } = "";
    public string Key { get; init; } = "";
    public string Value { get => _value; set { if (_value == value) return; _value = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Value))); } }
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    public string Category => _category ??= ConfigMetadata.Category(Section);
    public string Description => _description ??= ConfigMetadata.Description(Section, Key);
    public string Documentation => _documentation ??= ConfigMetadata.Documentation(Section, Key);
    public IReadOnlyList<string> Options => ConfigMetadata.Options(Section, Key, Value);
}
