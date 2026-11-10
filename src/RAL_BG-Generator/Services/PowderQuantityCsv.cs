using System.Globalization;
using System.IO;
using System.Text;

namespace RalBgGenerator.Services;

/// <summary>
/// Liest die Mengen-CSV fuer die Pulverdatei: Spalte 1 = Dateiname, Spalte 2 = Menge.
/// <list type="bullet">
/// <item>Trennzeichen: Semikolon, Tab oder Komma - erkannt an der ersten Zeile.</item>
/// <item>Kopfzeile erlaubt: erste Zeile, deren Menge keine Zahl ist.</item>
/// <item>Dateiname mit oder ohne Endung, ohne Ordner; Gross-/Kleinschreibung egal.</item>
/// <item>Menge: ganze Zahl groesser 0, sonst Fehler mit Zeilennummer.</item>
/// <item>Doppelter Dateiname: Fehler.</item>
/// </list>
/// </summary>
public sealed class PowderQuantityCsv
{
    private static readonly char[] CandidateDelimiters = [';', '\t', ','];

    private readonly Dictionary<string, int> _quantities;

    private PowderQuantityCsv(Dictionary<string, int> quantities, IReadOnlyList<string> errors)
    {
        _quantities = quantities;
        Errors = errors;
    }

    /// <summary>Formatfehler der Datei; leer = Datei ist in Ordnung.</summary>
    public IReadOnlyList<string> Errors { get; }

    public bool IsValid => Errors.Count == 0;

    public int Count => _quantities.Count;

    /// <summary>
    /// Menge zu einer Datei der Liste. Zuerst mit Endung gesucht ("Halter-01.psm"),
    /// dann ohne ("Halter-01").
    /// </summary>
    public int? Find(string partPath)
    {
        var fileName = Path.GetFileName(partPath);

        if (_quantities.TryGetValue(fileName, out var exact))
        {
            return exact;
        }

        return _quantities.TryGetValue(Path.GetFileNameWithoutExtension(fileName), out var withoutExtension)
            ? withoutExtension
            : null;
    }

    public static PowderQuantityCsv Load(string path)
    {
        string[] lines;

        try
        {
            lines = ReadLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed("CSV-Datei nicht lesbar: " + ex.Message);
        }

        return Parse(lines);
    }

    public static PowderQuantityCsv Parse(IReadOnlyList<string> lines)
    {
        var quantities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        char? delimiter = null;
        var isFirstDataLine = true;

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var lineNumber = index + 1;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            delimiter ??= DetectDelimiter(line);

            var columns = line.Split(delimiter.Value);
            var name = Clean(columns[0]);
            var quantityText = columns.Length > 1 ? Clean(columns[1]) : string.Empty;

            if (isFirstDataLine)
            {
                isFirstDataLine = false;

                // Kopfzeile: in Spalte 2 steht keine Zahl (z. B. "Datei;Menge").
                if (columns.Length > 1 && !LooksNumeric(quantityText))
                {
                    continue;
                }
            }

            if (columns.Length < 2)
            {
                errors.Add($"Zeile {lineNumber}: Menge fehlt.");
                continue;
            }

            // Nur der Dateiname zaehlt - ein versehentlich mitkopierter Ordner wird entfernt.
            name = Path.GetFileName(name);

            if (name.Length == 0)
            {
                errors.Add($"Zeile {lineNumber}: Dateiname fehlt.");
                continue;
            }

            if (!int.TryParse(quantityText, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) || quantity < 1)
            {
                errors.Add($"Zeile {lineNumber}: Menge '{quantityText}' fuer '{name}' ist keine ganze Zahl groesser 0.");
                continue;
            }

            if (!quantities.TryAdd(name, quantity))
            {
                errors.Add($"Zeile {lineNumber}: '{name}' steht mehrfach in der CSV.");
            }
        }

        if (errors.Count == 0 && quantities.Count == 0)
        {
            errors.Add("Die CSV-Datei enthaelt keine Mengen.");
        }

        return new PowderQuantityCsv(quantities, errors);
    }

    private static PowderQuantityCsv Failed(string error) =>
        new(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase), [error]);

    /// <summary>
    /// Excel speichert CSV auf deutschen Systemen meist als ANSI (Windows-1252), neuere
    /// Versionen auch als UTF-8. Gelesen wird UTF-8 streng; scheitert das, gilt Latin-1,
    /// das fuer Umlaute und ß mit Windows-1252 identisch ist und ohne Codepage-Provider
    /// auskommt - sonst wuerden Dateinamen mit Umlauten nicht gefunden.
    /// </summary>
    private static string[] ReadLines(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;

        try
        {
            text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.Latin1.GetString(bytes);
        }

        // BOM entfernen, falls vorhanden.
        text = text.TrimStart('﻿');

        return text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
    }

    private static char DetectDelimiter(string line)
    {
        foreach (var candidate in CandidateDelimiters)
        {
            if (line.Contains(candidate))
            {
                return candidate;
            }
        }

        return ';';
    }

    private static string Clean(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1].Replace("\"\"", "\"").Trim();
        }

        return trimmed;
    }

    /// <summary>Grobe Erkennung, ob ein Feld eine (ggf. ungueltige) Zahl sein soll.</summary>
    private static bool LooksNumeric(string value) =>
        value.Length > 0 && value.Any(char.IsDigit) && value.All(c => char.IsDigit(c) || c is ',' or '.' or '-' or '+' or ' ');
}
