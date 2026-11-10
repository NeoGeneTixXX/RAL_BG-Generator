using System.Collections.Concurrent;

namespace RalBgGenerator.Infrastructure;

/// <summary>
/// Fuehrt Arbeit auf genau einem dedizierten STA-Thread aus.
///
/// Hintergrund: Solid Edge ist ein Out-of-Process-COM-Server. Wird ein COM-Objekt in
/// einem STA erzeugt, muessen alle weiteren Aufrufe auf dieses Objekt aus demselben
/// Apartment kommen - sonst marshallt die Runtime jeden Aufruf ueber einen Proxy,
/// was zu Deadlocks und zu nicht freigegebenen Referenzen fuehrt.
/// <see cref="Task.Run(Action)"/> reicht dafuer nicht: Thread-Pool-Threads sind MTA.
/// </summary>
public sealed class StaThreadRunner : IDisposable
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private bool _disposed;

    public StaThreadRunner(string threadName)
    {
        _thread = new Thread(WorkLoop)
        {
            Name = threadName,
            IsBackground = true
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>True, wenn der aufrufende Thread der STA-Worker ist.</summary>
    public bool IsOnStaThread => Thread.CurrentThread == _thread;

    private void WorkLoop()
    {
        foreach (var workItem in _queue.GetConsumingEnumerable())
        {
            workItem();
        }
    }

    public Task<T> InvokeAsync<T>(Func<T> function)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // RunContinuationsAsynchronously verhindert, dass Fortsetzungen des Aufrufers
        // auf dem STA-Thread landen und dort die COM-Pumpe blockieren.
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        _queue.Add(() =>
        {
            try
            {
                completion.SetResult(function());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        return completion.Task;
    }

    public Task InvokeAsync(Action action) =>
        InvokeAsync<object?>(() =>
        {
            action();
            return null;
        });

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();

        // Der Thread bekommt Zeit, laufende COM-Aufrufe (z. B. Application.Quit)
        // zu Ende zu bringen. Er ist ein Background-Thread, blockiert den Prozess
        // also auch im Timeout-Fall nicht.
        _thread.Join(TimeSpan.FromSeconds(30));
        _queue.Dispose();
    }
}
