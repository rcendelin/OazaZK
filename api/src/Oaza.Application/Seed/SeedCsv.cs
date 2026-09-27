using System.Globalization;
using System.Text;

namespace Oaza.Application.Seed;

/// <summary>One data row of a seed CSV: its line number in the file and the values by column name.</summary>
public sealed class SeedRow(string file, int line, IReadOnlyDictionary<string, string> values)
{
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");
    private static readonly string[] DayFormats = ["yyyy-MM-dd", "d.M.yyyy", "d. M. yyyy", "dd.MM.yyyy"];

    public string File { get; } = file;
    public int Line { get; } = line;

    /// <summary>Problems found while reading the values; the row is skipped when there are any.</summary>
    public List<string> Errors { get; } = [];

    /// <summary>Trimmed value, null when empty or the column is missing.</summary>
    public string? Text(string column) =>
        values.TryGetValue(column, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

    public string Required(string column)
    {
        var value = Text(column);
        if (value is null)
            Errors.Add($"chybí hodnota ve sloupci „{column}“");
        return value ?? string.Empty;
    }

    public DateOnly? Day(string column, bool required = true)
    {
        var text = required ? Required(column) : Text(column);
        if (string.IsNullOrEmpty(text))
            return null;
        if (DateOnly.TryParseExact(text, DayFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return day;
        Errors.Add($"„{text}“ ve sloupci „{column}“ není datum (RRRR-MM-DD nebo D.M.RRRR)");
        return null;
    }

    /// <summary>A number with a decimal comma or dot; spaces (thousands) are ignored.</summary>
    public decimal? Number(string column, bool required = true)
    {
        var text = required ? Required(column) : Text(column);
        if (string.IsNullOrEmpty(text))
            return null;
        var normalized = text.Replace(" ", string.Empty).Replace(" ", string.Empty).Replace(',', '.');
        if (decimal.TryParse(normalized, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            return value;
        Errors.Add($"„{text}“ ve sloupci „{column}“ není číslo");
        return null;
    }

    /// <summary>ano/ne, true/false, 1/0; empty = <paramref name="fallback"/>.</summary>
    public bool Flag(string column, bool fallback)
    {
        var text = Text(column)?.ToLower(Czech);
        switch (text)
        {
            case null:
                return fallback;
            case "ano" or "true" or "1" or "a" or "yes":
                return true;
            case "ne" or "false" or "0" or "n" or "no":
                return false;
            default:
                Errors.Add($"„{text}“ ve sloupci „{column}“ musí být ano, nebo ne");
                return fallback;
        }
    }

    /// <summary>An enum value by its name (case-insensitive) or by one of the Czech aliases.</summary>
    public TEnum? Choice<TEnum>(string column, IReadOnlyDictionary<string, TEnum>? aliases = null, bool required = true) where TEnum : struct, Enum
    {
        var text = required ? Required(column) : Text(column);
        if (string.IsNullOrEmpty(text))
            return null;
        if (aliases is not null && aliases.TryGetValue(text.ToLower(Czech), out var aliased))
            return aliased;
        if (Enum.TryParse<TEnum>(text, ignoreCase: true, out var value) && !int.TryParse(text, out _))
            return value;
        var allowed = string.Join(", ", Enum.GetNames<TEnum>().Concat(aliases?.Keys ?? []));
        Errors.Add($"„{text}“ ve sloupci „{column}“ není povolená hodnota ({allowed})");
        return null;
    }
}

/// <summary>
/// Reads the seed CSV templates (T13). Separator „;“ or „,“ (by the header line), values may be quoted, lines that are
/// empty or start with „#“ are skipped, column names are case-insensitive. The same shape as the app's CSV exports.
/// </summary>
public static class SeedCsv
{
    public static (IReadOnlyList<SeedRow> Rows, IReadOnlyList<string> Errors) Parse(string file, string text, IReadOnlyList<string> columns)
    {
        var lines = (text ?? string.Empty).TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var rows = new List<SeedRow>();
        var errors = new List<string>();
        string[]? header = null;
        char separator = ';';

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#'))
                continue;
            if (header is null)
            {
                separator = line.Count(c => c == ';') >= line.Count(c => c == ',') ? ';' : ',';
                header = Split(line, separator).Select(h => h.Trim().ToLowerInvariant()).ToArray();
                var missing = columns.Where(c => !header.Contains(c)).ToList();
                if (missing.Count > 0)
                    errors.Add($"{file}: chybí sloupce {string.Join(", ", missing)} (očekávané: {string.Join(";", columns)})");
                var unknown = header.Where(h => h.Length > 0 && !columns.Contains(h)).ToList();
                if (unknown.Count > 0)
                    errors.Add($"{file}: neznámé sloupce {string.Join(", ", unknown)}");
                if (missing.Count > 0 || unknown.Count > 0)
                    return (rows, errors);
                continue;
            }

            var cells = Split(line, separator);
            if (cells.Count > header.Length)
            {
                errors.Add($"{file}, řádek {i + 1}: víc hodnot ({cells.Count}) než sloupců ({header.Length})");
                continue;
            }
            var values = new Dictionary<string, string>();
            for (var c = 0; c < header.Length; c++)
                values[header[c]] = c < cells.Count ? cells[c] : string.Empty;
            rows.Add(new SeedRow(file, i + 1, values));
        }
        return (rows, errors);
    }

    private static List<string> Split(string line, char separator)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else if (ch == separator)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        cells.Add(current.ToString());
        return cells;
    }
}
