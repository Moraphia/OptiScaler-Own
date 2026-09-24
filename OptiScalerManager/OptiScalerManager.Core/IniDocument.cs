using System.Text;

namespace OptiScalerManager.Core;

/// <summary>Small comment-preserving INI editor for manager-owned changes.</summary>
public sealed class IniDocument
{
    private readonly List<string> _lines;
    private readonly Encoding _encoding;
    private IniDocument(List<string> lines, Encoding encoding) { _lines = lines; _encoding = encoding; }

    public static IniDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? new UTF8Encoding(true) : new UTF8Encoding(false);
        return new IniDocument(File.ReadAllLines(path, encoding).ToList(), encoding);
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

    public string Render() => string.Join("\r\n", _lines) + "\r\n";
    public void SaveAtomic(string path)
    {
        var temp = path + ".manager.tmp";
        File.WriteAllText(temp, Render(), _encoding);
        File.Move(temp, path, true);
    }
}

public sealed class IniEntry
{
    public string Section { get; init; } = "";
    public string Key { get; init; } = "";
    public string Value { get; set; } = "";
}
