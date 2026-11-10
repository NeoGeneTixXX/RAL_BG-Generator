namespace RalBgGenerator.Models;

/// <summary>
/// Persistente Einstellungen. Wird als appsettings.json im AppData-Ordner der
/// Anwendung abgelegt und bei jeder Aenderung sofort geschrieben.
/// </summary>
public sealed class AppSettings
{
    /// <summary>Pfad zur Vorlagebaugruppe (.asm).</summary>
    public string? TemplatePath { get; set; }

    /// <summary>
    /// Pfad zur Zeichnungsvorlage (.dft). Aus ihr wird zu jeder erzeugten Baugruppe eine
    /// Zeichnung erzeugt; die Verknuepfung der Vorlage wird dabei auf die Baugruppe umgestellt.
    /// </summary>
    public string? DrawingTemplatePath { get; set; }

    /// <summary>
    /// Pfad zur Pulverdatei (.par), die jeder erzeugten Baugruppe zusaetzlich am
    /// Ursprung fixiert beigelegt wird. Leer = keine Pulverdatei einfuegen.
    /// </summary>
    public string? PowderPartPath { get; set; }

    /// <summary>Stuecklistenmenge der Pulverdatei je Baugruppe; ganze Zahl groesser 0.</summary>
    public int PowderQuantity { get; set; } = 1;

    /// <summary>Abweichender Zielordner; leer = Ordner der jeweiligen Quelldatei.</summary>
    public string? OutputFolder { get; set; }

    /// <summary>True, wenn <see cref="OutputFolder"/> statt des Quellordners gelten soll.</summary>
    public bool UseCustomOutputFolder { get; set; }

    /// <summary>Zuletzt gewaehlter Face Style, wird beim Start vorselektiert.</summary>
    public string? LastFaceStyle { get; set; }
}
