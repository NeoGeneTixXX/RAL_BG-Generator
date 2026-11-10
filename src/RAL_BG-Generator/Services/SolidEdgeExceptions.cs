namespace RalBgGenerator.Services;

/// <summary>Fehler aus der Solid-Edge-Automatisierung mit benutzerlesbarer Meldung.</summary>
public class SolidEdgeException : Exception
{
    public SolidEdgeException(string message)
        : base(message)
    {
    }

    public SolidEdgeException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Es laeuft keine Solid-Edge-Instanz und der Aufrufer hat das Starten nicht erlaubt.
/// </summary>
public sealed class SolidEdgeNotRunningException : SolidEdgeException
{
    public SolidEdgeNotRunningException()
        : base("Solid Edge laeuft derzeit nicht.")
    {
    }
}
