using System.Runtime.InteropServices;

namespace RalBgGenerator.Infrastructure;

/// <summary>
/// Ersatz fuer <c>Marshal.GetActiveObject</c>.
///
/// Wichtig: <c>Marshal.GetActiveObject</c> existiert nur im .NET Framework und wurde
/// in .NET Core / .NET 5+ ersatzlos entfernt. Unter .NET 8 muss der Zugriff auf die
/// Running Object Table daher direkt ueber die Win32-API erfolgen.
/// </summary>
internal static class NativeMethods
{
    /// <summary>MK_E_UNAVAILABLE - es laeuft keine registrierte Instanz.</summary>
    private const int MK_E_UNAVAILABLE = unchecked((int)0x800401E3);

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int CLSIDFromProgID(string lpszProgID, out Guid lpclsid);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int GetActiveObject(
        ref Guid rclsid,
        IntPtr pvReserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    /// <summary>
    /// Sucht eine bereits laufende COM-Instanz zur ProgID.
    /// </summary>
    /// <returns>
    /// Die laufende Instanz oder <c>null</c>, wenn keine registriert ist
    /// (bzw. die ProgID unbekannt ist).
    /// </returns>
    public static object? TryGetActiveObject(string progId)
    {
        var hr = CLSIDFromProgID(progId, out var clsid);
        if (hr < 0)
        {
            // ProgID nicht registriert => Solid Edge ist nicht installiert.
            return null;
        }

        hr = GetActiveObject(ref clsid, IntPtr.Zero, out var instance);
        if (hr == MK_E_UNAVAILABLE)
        {
            return null;
        }

        if (hr < 0)
        {
            return null;
        }

        return instance;
    }

    /// <summary>Prueft, ob die ProgID ueberhaupt registriert ist.</summary>
    public static bool IsProgIdRegistered(string progId) => CLSIDFromProgID(progId, out _) >= 0;
}
