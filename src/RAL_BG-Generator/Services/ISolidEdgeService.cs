namespace RalBgGenerator.Services;

/// <param name="StartedByThisApplication">
/// True, wenn diese Anwendung Solid Edge gestartet hat. Nur dann wird es am Ende beendet.
/// </param>
/// <param name="Version">Versionsstring der verbundenen Instanz.</param>
public sealed record ConnectionInfo(bool StartedByThisApplication, string Version);

public interface ISolidEdgeService : IDisposable
{
    bool IsConnected { get; }

    /// <summary>Optionale Diagnoseausgabe; wird vom ViewModel in das Log gespiegelt.</summary>
    Action<string>? DiagnosticLog { get; set; }

    /// <param name="allowStart">
    /// False = nur an eine bereits laufende Instanz andocken, sonst
    /// <see cref="SolidEdgeNotRunningException"/>.
    /// </param>
    Task<ConnectionInfo> ConnectAsync(bool allowStart);

    /// <summary>
    /// Liest die Face Styles der Vorlage. Die Vorlage wird dabei nur gelesen: es wird ein
    /// neues Dokument aus ihr erzeugt und ungespeichert wieder geschlossen.
    /// </summary>
    Task<IReadOnlyList<string>> LoadFaceStylesAsync(string templatePath, bool allowStart);

    /// <summary>
    /// Erzeugt aus der Vorlage eine neue Baugruppe, fuegt das Teil im Ursprung fixiert ein,
    /// weist den Face Style zu und speichert unter <paramref name="targetPath"/>.
    /// </summary>
    /// <param name="powderPartPath">
    /// Optionale Pulverdatei (.par), die zusaetzlich im Ursprung fixiert eingefuegt wird.
    /// Sie bekommt keinen Face Style; null/leer = keine Pulverdatei.
    /// </param>
    /// <param name="powderQuantity">
    /// Stuecklistenmenge der Pulverdatei (Occurrence.Quantity), ganze Zahl groesser 0.
    /// Ohne Pulverdatei ohne Wirkung.
    /// </param>
    Task CreateAssemblyAsync(
        string templatePath,
        string partPath,
        string targetPath,
        string styleName,
        string? powderPartPath,
        int powderQuantity);

    /// <summary>
    /// Erzeugt aus der Zeichnungsvorlage eine Zeichnung der (bereits gespeicherten) Baugruppe:
    /// die in der Vorlage vorhandene Verknuepfung wird auf die Baugruppe umgestellt, die
    /// Ansichten werden aktualisiert und die ISO-Ansicht auf Breite und Mitte geprueft. Die Zeichnung
    /// wird unter <paramref name="targetPath"/> gespeichert und sofort geschlossen.
    /// </summary>
    Task CreateDrawingAsync(string drawingTemplatePath, string assemblyPath, string targetPath);

    Task DisconnectAsync();
}
