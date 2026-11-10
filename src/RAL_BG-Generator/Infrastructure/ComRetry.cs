using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

namespace RalBgGenerator.Infrastructure;

/// <summary>
/// Wiederholt COM-Aufrufe, die Solid Edge abgelehnt hat, weil es gerade beschaeftigt ist.
///
/// Zweites Netz hinter dem <see cref="OleMessageFilter"/>: Der Message-Filter faengt
/// Ablehnungen bereits unterhalb der Ausnahmeebene ab. Was trotzdem durchkommt - etwa
/// weil der Filter (noch) nicht registriert ist - landet hier.
/// </summary>
public static class ComRetry
{
    private const int RPC_E_CALL_REJECTED = unchecked((int)0x80010001);
    private const int RPC_E_SERVERCALL_RETRYLATER = unchecked((int)0x8001010A);
    private const int RPC_E_SERVERCALL_REJECTED = unchecked((int)0x80010002);

    /// <summary>
    /// Der COM-Binder des DLR meldet eine Ablehnung nicht als COMException, sondern als
    /// MissingMemberException mit dem HRESULT im Text:
    /// "Could not get dispatch ID for Occurrences (error: 0x80010001)".
    /// </summary>
    private static readonly string[] BusyHResultTexts = ["0x80010001", "0x8001010A", "0x80010002"];

    public static T Execute<T>(Func<T> operation, int attempts = 20, int delayMilliseconds = 500)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (Exception ex) when (IsBusy(ex) && attempt < attempts)
            {
                Thread.Sleep(delayMilliseconds);
            }
        }
    }

    public static void Execute(Action operation, int attempts = 20, int delayMilliseconds = 500) =>
        Execute<object?>(
            () =>
            {
                operation();
                return null;
            },
            attempts,
            delayMilliseconds);

    /// <summary>True, wenn die Ausnahme ein "Solid Edge ist beschaeftigt" ausdrueckt.</summary>
    public static bool IsBusy(Exception exception)
    {
        if (exception.HResult is RPC_E_CALL_REJECTED or RPC_E_SERVERCALL_RETRYLATER or RPC_E_SERVERCALL_REJECTED)
        {
            return true;
        }

        // Nur fuer die Ausnahmetypen pruefen, die der DLR-COM-Binder wirft - sonst wuerde
        // eine zufaellig passende Fehlermeldung faelschlich als "beschaeftigt" gelten.
        if (exception is not (MissingMemberException or MissingMethodException or RuntimeBinderException))
        {
            return false;
        }

        var message = exception.Message;

        return BusyHResultTexts.Any(code => message.Contains(code, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>Einheitliche Erkennung von COM-/Late-Binding-Fehlern fuer catch-Filter.</summary>
public static class ComErrors
{
    /// <summary>
    /// True fuer alle Ausnahmen, die aus einem fehlgeschlagenen COM-Aufruf oder aus dem
    /// Late Binding stammen koennen. Beim Late Binding ist das mehr als nur COMException:
    /// fehlende Member melden sich als MissingMemberException, Typkonflikte als
    /// InvalidCastException, unbekannte Aufrufe als RuntimeBinderException.
    /// </summary>
    public static bool IsComFailure(Exception exception) =>
        exception is COMException
            or RuntimeBinderException
            or MissingMemberException
            or MissingMethodException
            or InvalidCastException
            or InvalidComObjectException
            or TargetInvocationException;
}
