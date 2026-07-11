using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WslManager.Core;

/// <summary>
/// Documento INI com round-trip: preserva comentários, ordem, espaçamento e
/// seções/chaves desconhecidas. Só as chaves realmente editadas mudam; o resto
/// do arquivo sobrevive intacto a um ciclo load → set → save.
/// </summary>
public sealed class WslConfigDocument
{
    // key = value  (captura recuo, chave, separador, valor e espaço final)
    private static readonly Regex KeyLine =
        new(@"^(?<indent>\s*)(?<key>[^=\s#;][^=]*?)(?<sep>\s*=\s*)(?<value>.*?)(?<trail>\s*)$", RegexOptions.Compiled);

    private sealed class Entry
    {
        public bool IsKey;
        public string Raw = "";          // linha original (comentário/branco/cabeçalho/desconhecida)
        public string Section = "";      // seção dona ("" = topo, antes de qualquer [seção])

        // partes de uma linha key=value
        public string Indent = "";
        public string Key = "";
        public string Sep = "=";
        public string Value = "";
        public string Trail = "";

        public bool IsBlank => !IsKey && Raw.Trim().Length == 0;

        public string Render() => IsKey ? $"{Indent}{Key}{Sep}{Value}{Trail}" : Raw;
    }

    private readonly List<Entry> _entries = [];
    private readonly string _newline;
    private readonly bool _trailingNewline;

    private WslConfigDocument(string newline, bool trailingNewline)
    {
        _newline = newline;
        _trailingNewline = trailingNewline;
    }

    public static WslConfigDocument Parse(string text)
    {
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var trailing = text.EndsWith('\n');

        var doc = new WslConfigDocument(newline, trailing);
        if (text.Length == 0) return doc;

        var pieces = text.Split('\n');
        // se o arquivo termina em \n, o último pedaço é "" e representa só o newline final
        var count = trailing ? pieces.Length - 1 : pieces.Length;

        var section = "";
        for (var i = 0; i < count; i++)
        {
            var line = pieces[i].TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith('[') && trimmed.TrimEnd().EndsWith(']'))
            {
                section = trimmed.TrimEnd()[1..^1].Trim();
                doc._entries.Add(new Entry { Raw = line, Section = section });
                continue;
            }

            if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                doc._entries.Add(new Entry { Raw = line, Section = section });
                continue;
            }

            var m = KeyLine.Match(line);
            if (m.Success && m.Groups["key"].Value.Trim().Length > 0)
            {
                doc._entries.Add(new Entry
                {
                    IsKey = true,
                    Section = section,
                    Indent = m.Groups["indent"].Value,
                    Key = m.Groups["key"].Value,
                    Sep = m.Groups["sep"].Value,
                    Value = m.Groups["value"].Value,
                    Trail = m.Groups["trail"].Value,
                });
            }
            else
            {
                doc._entries.Add(new Entry { Raw = line, Section = section }); // formato desconhecido → intacto
            }
        }

        return doc;
    }

    /// <summary>Valor atual da chave, ou null se ausente.</summary>
    public string? Get(string section, string key)
        => Find(section, key)?.Value;

    private Entry? Find(string section, string key)
        => _entries.FirstOrDefault(e => e.IsKey
            && e.Section.Equals(section, StringComparison.OrdinalIgnoreCase)
            && e.Key.Trim().Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Define/atualiza a chave, criando a seção se necessário.</summary>
    public void Set(string section, string key, string value)
    {
        var existing = Find(section, key);
        if (existing is not null)
        {
            existing.Value = value;
            return;
        }

        var sectionEntries = _entries
            .Select((e, idx) => (e, idx))
            .Where(t => t.e.Section.Equals(section, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var newKey = new Entry
        {
            IsKey = true,
            Section = section,
            Key = key,
            Sep = "=",
            Value = value,
        };

        if (sectionEntries.Count > 0)
        {
            // insere após a última linha não-branca da seção (antes de brancos finais)
            var insertAt = sectionEntries[^1].idx + 1;
            while (insertAt - 1 >= 0 && _entries[insertAt - 1].Section.Equals(section, StringComparison.OrdinalIgnoreCase)
                   && _entries[insertAt - 1].IsBlank)
                insertAt--;
            _entries.Insert(insertAt, newKey);
        }
        else
        {
            // seção nova no fim do documento
            if (_entries.Count > 0 && !_entries[^1].IsBlank)
                _entries.Add(new Entry { Raw = "", Section = _entries[^1].Section });
            _entries.Add(new Entry { Raw = $"[{section}]", Section = section });
            _entries.Add(newKey);
        }
    }

    /// <summary>Remove a chave (se existir). Deixa a seção mesmo que vazia.</summary>
    public void Remove(string section, string key)
    {
        var e = Find(section, key);
        if (e is not null) _entries.Remove(e);
    }

    /// <summary>Define quando <paramref name="value"/> tem conteúdo; remove quando null/vazio.</summary>
    public void SetOrRemove(string section, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) Remove(section, key);
        else Set(section, key, value);
    }

    public string ToText()
    {
        var body = string.Join(_newline, _entries.Select(e => e.Render()));
        return _trailingNewline ? body + _newline : body;
    }
}

/// <summary>
/// Lê/grava o %UserProfile%\.wslconfig usando o <see cref="WslConfigDocument"/>.
/// Antes de sobrescrever, cria backup .wslconfig.bak-&lt;timestamp&gt; (regra do CLAUDE.md).
/// </summary>
public sealed class WslConfigService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string ConfigPath { get; }

    public WslConfigService(string? path = null)
        => ConfigPath = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".wslconfig");

    public WslConfigDocument Load()
    {
        var text = File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : string.Empty;
        return WslConfigDocument.Parse(text);
    }

    /// <summary>Grava o documento, fazendo backup do arquivo atual se existir.</summary>
    public string? Save(WslConfigDocument doc)
    {
        string? backup = null;
        if (File.Exists(ConfigPath))
        {
            backup = $"{ConfigPath}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(ConfigPath, backup, overwrite: false);
        }

        File.WriteAllText(ConfigPath, doc.ToText(), Utf8NoBom);
        return backup;
    }
}
