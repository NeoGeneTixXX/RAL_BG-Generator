using System.Runtime.InteropServices;

namespace RalBgGenerator.Infrastructure;

/// <summary>
/// OLE-Message-Filter fuer den STA-Thread.
///
/// Solid Edge lehnt Automationsaufrufe ab, solange es beschaeftigt ist (Dokument laden,
/// Dialog offen, direkt nach dem Start). Ohne Message-Filter meldet COM das sofort als
/// RPC_E_CALL_REJECTED (0x80010001) an den Aufrufer - typisches Symptom:
/// "Could not get dispatch ID for Occurrences (error: 0x80010001)".
///
/// Mit registriertem Filter fragt COM stattdessen hier nach, ob der Aufruf wiederholt
/// werden soll. Die Wiederholung passiert damit unterhalb der Ausnahmeebene, also bevor
/// ueberhaupt ein Fehler entsteht. Das ist der eigentliche Fix; das Retry in
/// <see cref="ComRetry"/> bleibt nur als zweites Netz bestehen.
///
/// Muss auf demselben STA-Thread registriert werden, der die COM-Aufrufe macht.
/// </summary>
internal sealed class OleMessageFilter : IOleMessageFilter
{
    private const int ServerCallIsHandled = 0;
    private const int ServerCallRetryLater = 2;

    /// <summary>Rueckgabe &gt;= 100 bedeutet: nach so vielen Millisekunden erneut versuchen.</summary>
    private const int RetryDelayMilliseconds = 250;

    /// <summary>Rueckgabe -1 bedeutet: Aufruf abbrechen (fuehrt beim Aufrufer zur Ausnahme).</summary>
    private const int CancelCall = -1;

    private const int PendingMsgWaitDefProcess = 2;

    /// <summary>
    /// Wie lange insgesamt auf ein beschaeftigtes Solid Edge gewartet wird. Grosszuegig,
    /// weil das Erzeugen einer Baugruppe aus einer umfangreichen Vorlage dauern kann.
    /// </summary>
    private readonly int _timeoutMilliseconds;

    private IOleMessageFilter? _previousFilter;
    private bool _isRegistered;

    public OleMessageFilter(int timeoutMilliseconds = 120_000)
    {
        _timeoutMilliseconds = timeoutMilliseconds;
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(IOleMessageFilter? newFilter, out IOleMessageFilter? oldFilter);

    /// <summary>Registriert den Filter fuer den aktuellen (STA-)Thread.</summary>
    public bool Register()
    {
        if (_isRegistered)
        {
            return true;
        }

        var hr = CoRegisterMessageFilter(this, out _previousFilter);
        _isRegistered = hr >= 0;

        return _isRegistered;
    }

    /// <summary>Stellt den vorherigen Filter wieder her.</summary>
    public void Revoke()
    {
        if (!_isRegistered)
        {
            return;
        }

        CoRegisterMessageFilter(_previousFilter, out _);

        _previousFilter = null;
        _isRegistered = false;
    }

    /// <summary>Eingehende Aufrufe werden immer angenommen.</summary>
    public int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo) =>
        ServerCallIsHandled;

    /// <summary>
    /// Solid Edge hat abgelehnt. <paramref name="dwTickCount"/> ist die seit dem
    /// urspruenglichen Aufruf verstrichene Zeit in Millisekunden.
    /// </summary>
    public int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType)
    {
        if (dwTickCount >= _timeoutMilliseconds)
        {
            // Nach dem Zeitlimit aufgeben, statt endlos zu haengen.
            return CancelCall;
        }

        // SERVERCALL_RETRYLATER heisst "gleich nochmal". SERVERCALL_REJECTED wird hier
        // bewusst ebenfalls wiederholt: Solid Edge meldet waehrend des Ladens eines
        // Dokuments beides, und ein Abbruch waere in beiden Faellen verfrueht.
        return dwRejectType is ServerCallRetryLater or 1 ? RetryDelayMilliseconds : CancelCall;
    }

    /// <summary>Waehrend eines ausstehenden Aufrufs eintreffende Nachrichten normal verarbeiten.</summary>
    public int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType) => PendingMsgWaitDefProcess;
}

/// <summary>IOleMessageFilter (IID_IMessageFilter aus objidl.idl).</summary>
[ComImport]
[Guid("00000016-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IOleMessageFilter
{
    [PreserveSig]
    int HandleInComingCall(int dwCallType, IntPtr hTaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);

    [PreserveSig]
    int RetryRejectedCall(IntPtr hTaskCallee, int dwTickCount, int dwRejectType);

    [PreserveSig]
    int MessagePending(IntPtr hTaskCallee, int dwTickCount, int dwPendingType);
}
