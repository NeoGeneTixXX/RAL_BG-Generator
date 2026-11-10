using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RalBgGenerator;

public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RAL_BG-Generator",
        "error.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ohne diese Handler beendet sich die Anwendung bei einem Fehler wortlos -
        // der Anwender sieht nur ein verschwundenes Fenster und hat nichts in der Hand.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>Fehler auf dem UI-Thread. Die Anwendung laeuft danach weiter.</summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report("Unerwarteter Fehler in der Oberflaeche", e.Exception, isFatal: false);
        e.Handled = true;
    }

    /// <summary>
    /// Fehler auf einem Hintergrund-Thread (z. B. dem STA-Thread). Hier laesst sich das
    /// Beenden nicht mehr verhindern, wohl aber protokollieren und anzeigen.
    /// </summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Report("Schwerwiegender Fehler", e.ExceptionObject as Exception, isFatal: true);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Report("Fehler in einer Hintergrundaufgabe", e.Exception, isFatal: false);
        e.SetObserved();
    }

    private static void Report(string title, Exception? exception, bool isFatal)
    {
        var details = exception?.ToString() ?? "Unbekannter Fehler.";

        WriteCrashLog(title, details);

        var message = exception?.Message ?? "Unbekannter Fehler.";

        if (isFatal)
        {
            message += "\n\nDie Anwendung wird beendet.";
        }

        message += $"\n\nEinzelheiten wurden protokolliert:\n{CrashLogPath}";

        try
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception)
        {
            // Wenn nicht einmal mehr ein Meldungsfenster geht, bleibt nur die Logdatei.
        }
    }

    private static void WriteCrashLog(string title, string details)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);

            File.AppendAllText(
                CrashLogPath,
                $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {title} ==={Environment.NewLine}{details}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Protokollieren darf nie selbst zum Problem werden.
        }
    }
}
