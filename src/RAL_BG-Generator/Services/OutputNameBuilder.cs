using System.IO;

namespace RalBgGenerator.Services;

/// <summary>
/// Bildet den Zieldateinamen: der Name der Quelldatei, nur mit der Endung .asm.
/// Kein Zusatz, insbesondere kein Farbname.
///
/// Damit kann eine Zieldatei durch einen Lauf mit einer anderen Farbe ueberschrieben
/// werden - abgefangen wird das durch die Ueberschreiben-Abfrage vor dem Speichern.
/// </summary>
public static class OutputNameBuilder
{
    private const string AssemblyExtension = ".asm";
    private const string DrawingExtension = ".dft";

    /// <summary>"Halter-01.psm" wird zu "Halter-01.asm".</summary>
    public static string BuildFileName(string sourcePath) =>
        Path.GetFileNameWithoutExtension(sourcePath) + AssemblyExtension;

    /// <summary>Liefert den vollstaendigen Zielpfad.</summary>
    /// <param name="sourcePath">Quelldatei (.psm/.par).</param>
    /// <param name="outputFolder">Zielordner; null/leer = Ordner der Quelldatei.</param>
    public static string BuildTargetPath(string sourcePath, string? outputFolder)
    {
        var folder = string.IsNullOrWhiteSpace(outputFolder)
            ? Path.GetDirectoryName(sourcePath) ?? string.Empty
            : outputFolder;

        return Path.Combine(folder, BuildFileName(sourcePath));
    }

    /// <summary>Die Zeichnung liegt neben der Baugruppe: "Halter-01.asm" wird zu "Halter-01.dft".</summary>
    public static string BuildDrawingPath(string assemblyPath) =>
        Path.ChangeExtension(assemblyPath, DrawingExtension);
}
