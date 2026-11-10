using System.IO;
using RalBgGenerator.Models;

namespace RalBgGenerator.Services;

/// <summary>
/// Prueft die vom Anwender gewaehlten Solid-Edge-Dateien - ohne Solid Edge zu starten.
/// </summary>
public static class DocumentValidator
{
    /// <summary>Signatur eines OLE-Compound-Files; Solid-Edge-Dokumente sind so aufgebaut.</summary>
    private static readonly byte[] CompoundFileSignature =
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    /// <summary>Die Vorlagebaugruppe ist Pflicht: ohne sie bleibt der Start gesperrt.</summary>
    public static FileValidationResult ValidateTemplate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return FileValidationResult.Invalid("Keine Vorlagebaugruppe ausgewaehlt.");
        }

        if (!HasExtension(path, ".asm"))
        {
            return FileValidationResult.Invalid("Die Vorlage muss eine Solid-Edge-Baugruppe (.asm) sein.");
        }

        if (!File.Exists(path))
        {
            return FileValidationResult.Invalid($"Vorlage nicht gefunden: {path}");
        }

        return ReadHeader(path, "Vorlage")
               ?? FileValidationResult.Valid($"Vorlage in Ordnung: {Path.GetFileName(path)}");
    }

    /// <summary>
    /// Die Zeichnungsvorlage ist ebenfalls Pflicht: zu jeder Baugruppe entsteht eine Zeichnung.
    /// </summary>
    public static FileValidationResult ValidateDrawingTemplate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return FileValidationResult.Invalid("Keine Zeichnungsvorlage ausgewaehlt.");
        }

        if (!HasExtension(path, ".dft"))
        {
            return FileValidationResult.Invalid("Die Zeichnungsvorlage muss eine Solid-Edge-Zeichnung (.dft) sein.");
        }

        if (!File.Exists(path))
        {
            return FileValidationResult.Invalid($"Zeichnungsvorlage nicht gefunden: {path}");
        }

        return ReadHeader(path, "Zeichnungsvorlage")
               ?? FileValidationResult.Valid($"Zeichnungsvorlage in Ordnung: {Path.GetFileName(path)}");
    }

    /// <summary>
    /// Die Pulverdatei ist optional. Ohne Auswahl laeuft der Batch ganz normal durch,
    /// nur ohne das zusaetzliche Teil in den erzeugten Baugruppen.
    /// </summary>
    public static FileValidationResult ValidatePowderPart(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return FileValidationResult.Valid("Keine Pulverdatei ausgewaehlt (optional).");
        }

        if (!HasExtension(path, ".par"))
        {
            return FileValidationResult.Invalid("Die Pulverdatei muss ein Solid-Edge-Teil (.par) sein.");
        }

        if (!File.Exists(path))
        {
            return FileValidationResult.Invalid($"Pulverdatei nicht gefunden: {path}");
        }

        return ReadHeader(path, "Pulverdatei")
               ?? FileValidationResult.Valid($"Pulverdatei in Ordnung: {Path.GetFileName(path)}");
    }

    private static bool HasExtension(string path, string extension) =>
        string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Liest den Dateikopf. Liefert null, wenn nichts zu beanstanden ist, sonst das
    /// Ergebnis, mit dem die Pruefung endet.
    /// </summary>
    private static FileValidationResult? ReadHeader(string path, string label)
    {
        try
        {
            using var stream = File.OpenRead(path);

            if (stream.Length == 0)
            {
                return FileValidationResult.Invalid($"Die Datei '{label}' ist leer.");
            }

            Span<byte> header = stackalloc byte[CompoundFileSignature.Length];
            if (stream.Read(header) == header.Length && !header.SequenceEqual(CompoundFileSignature))
            {
                // Bewusst nur eine Warnung: das Dateiformat kann sich aendern,
                // die Datei laesst sich trotzdem verwenden.
                return FileValidationResult.Warning(
                    $"{label} gefunden, hat aber kein typisches Solid-Edge-Dateiformat. " +
                    "Verwendung auf eigenes Risiko.");
            }
        }
        catch (IOException)
        {
            return FileValidationResult.Invalid(
                $"{label} ist gesperrt oder nicht lesbar (in Solid Edge geoeffnet?).");
        }
        catch (UnauthorizedAccessException)
        {
            return FileValidationResult.Invalid($"Kein Lesezugriff auf die Datei '{label}'.");
        }

        return null;
    }
}
