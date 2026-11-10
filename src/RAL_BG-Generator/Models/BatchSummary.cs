namespace RalBgGenerator.Models;

/// <summary>Zusammenfassung eines Laufs.</summary>
public sealed class BatchSummary
{
    public int Succeeded { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }

    public bool WasCancelled { get; set; }

    public int Total => Succeeded + Skipped + Failed;
}
