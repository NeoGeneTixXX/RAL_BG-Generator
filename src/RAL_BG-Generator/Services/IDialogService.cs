using RalBgGenerator.Models;

namespace RalBgGenerator.Services;

public interface IDialogService
{
    /// <summary>OpenFileDialog fuer die Vorlagebaugruppe (*.asm).</summary>
    string? PickTemplate(string? currentPath);

    /// <summary>OpenFileDialog fuer die Zeichnungsvorlage (*.dft).</summary>
    string? PickDrawingTemplate(string? currentPath);

    /// <summary>OpenFileDialog fuer die Pulverdatei (*.par).</summary>
    string? PickPowderPart(string? currentPath);

    /// <summary>OpenFileDialog fuer die Mengen-CSV der Pulverdatei (*.csv).</summary>
    string? PickPowderQuantityCsv(string? currentPath);

    /// <summary>OpenFileDialog mit Mehrfachauswahl fuer Teiledateien (*.psm, *.par).</summary>
    IReadOnlyList<string> PickPartFiles(string? initialFolder);

    /// <summary>Ordnerauswahl fuer den Zielordner.</summary>
    string? PickFolder(string? initialFolder);

    /// <summary>Abfrage bei bereits vorhandener Zieldatei. Wird auf dem UI-Thread angezeigt.</summary>
    Task<OverwriteDecision> AskOverwriteAsync(string targetPath);

    void ShowError(string title, string message);
}
