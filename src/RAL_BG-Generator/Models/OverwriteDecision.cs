namespace RalBgGenerator.Models;

/// <summary>Antwort auf die Abfrage bei bereits vorhandener Zieldatei.</summary>
public enum OverwriteDecision
{
    Overwrite,
    Skip,
    OverwriteAll,
    SkipAll,

    /// <summary>Dialog abgebrochen - Batch beenden.</summary>
    Cancel
}
