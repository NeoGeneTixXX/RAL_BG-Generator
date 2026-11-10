namespace RalBgGenerator.Models;

/// <summary>Ergebnis der Dateipruefung (Vorlagebaugruppe, Pulverdatei).</summary>
/// <param name="IsValid">Nur bei true darf der Start-Button aktiv sein.</param>
/// <param name="Message">Klartextmeldung fuer die UI.</param>
/// <param name="IsWarning">True = nutzbar, aber auffaellig (z. B. untypischer Dateiinhalt).</param>
public sealed record FileValidationResult(bool IsValid, string Message, bool IsWarning = false)
{
    public static FileValidationResult Valid(string message) => new(true, message);

    public static FileValidationResult Warning(string message) => new(true, message, IsWarning: true);

    public static FileValidationResult Invalid(string message) => new(false, message);
}
