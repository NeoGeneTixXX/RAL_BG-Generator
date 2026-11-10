using System.Runtime.InteropServices;

namespace RalBgGenerator.Infrastructure;

/// <summary>
/// Sammelt COM-Referenzen und gibt sie am Ende eines Arbeitsschritts in umgekehrter
/// Reihenfolge wieder frei.
///
/// Beim Late Binding erzeugt jeder Property-Zugriff (<c>doc.Occurrences</c>) eine
/// eigene RCW-Referenz. Wird die nicht freigegeben, haelt der Prozess den
/// Solid-Edge-Server am Leben - typisches Symptom: "Solid Edge schliesst nicht mehr"
/// bzw. ein unsichtbarer Edge.exe-Prozess im Task-Manager.
///
/// Wichtig: Nach <see cref="Dispose"/> duerfen die getrackten Objekte nicht mehr
/// verwendet werden.
/// </summary>
public sealed class ComScope : IDisposable
{
    private readonly List<object> _tracked = new();

    /// <summary>
    /// Registriert das Objekt zur Freigabe und gibt es unveraendert zurueck,
    /// damit Aufrufe verkettbar bleiben. Nicht-COM-Werte werden ignoriert.
    /// </summary>
    public T Track<T>(T value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _tracked.Add(value);
        }

        return value;
    }

    public void Dispose()
    {
        // Rueckwaerts freigeben: Kinder vor Eltern.
        for (var i = _tracked.Count - 1; i >= 0; i--)
        {
            try
            {
                Marshal.ReleaseComObject(_tracked[i]);
            }
            catch (ArgumentException)
            {
                // Kein COM-Objekt (mehr) - nichts zu tun.
            }
            catch (InvalidComObjectException)
            {
                // Bereits getrennt.
            }
        }

        _tracked.Clear();
    }
}
