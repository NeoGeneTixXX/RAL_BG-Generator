using System.IO;
using System.Windows;
using Microsoft.Win32;
using RalBgGenerator.Models;
using RalBgGenerator.Views;

namespace RalBgGenerator.Services;

/// <summary>
/// Kapselt alle Windows-Dialoge. Alle Methoden marshallen selbst auf den UI-Thread,
/// damit sie auch aus der Batch-Verarbeitung heraus aufgerufen werden koennen.
/// </summary>
public sealed class DialogService : IDialogService
{
    public string? PickTemplate(string? currentPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Vorlagebaugruppe auswaehlen",
            Filter = "Solid Edge Baugruppe (*.asm)|*.asm",
            CheckFileExists = true,
            Multiselect = false
        };

        ApplyInitialLocation(dialog, currentPath);

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickDrawingTemplate(string? currentPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Zeichnungsvorlage auswaehlen",
            Filter = "Solid Edge Zeichnung (*.dft)|*.dft",
            CheckFileExists = true,
            Multiselect = false
        };

        ApplyInitialLocation(dialog, currentPath);

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickPowderPart(string? currentPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Pulverdatei auswaehlen",
            Filter = "Solid Edge Teil (*.par)|*.par",
            CheckFileExists = true,
            Multiselect = false
        };

        ApplyInitialLocation(dialog, currentPath);

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickPowderQuantityCsv(string? currentPath)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Mengen-CSV fuer die Pulverdatei auswaehlen",
            Filter = "CSV-Datei (*.csv)|*.csv|Textdatei (*.txt)|*.txt|Alle Dateien (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        ApplyInitialLocation(dialog, currentPath);

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string> PickPartFiles(string? initialFolder)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Teiledateien auswaehlen",
            Filter = "Solid Edge Teile (*.psm;*.par)|*.psm;*.par|Blechteile (*.psm)|*.psm|Teile (*.par)|*.par",
            CheckFileExists = true,
            Multiselect = true
        };

        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public string? PickFolder(string? initialFolder)
    {
        // OpenFolderDialog gibt es seit .NET 8 direkt in WPF - kein Verweis auf
        // WindowsForms noetig, nur um an einen FolderBrowserDialog zu kommen.
        var dialog = new OpenFolderDialog
        {
            Title = "Zielordner auswaehlen",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialFolder) && Directory.Exists(initialFolder))
        {
            dialog.InitialDirectory = initialFolder;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public Task<OverwriteDecision> AskOverwriteAsync(string targetPath)
    {
        return InvokeOnUiThreadAsync(() =>
        {
            var dialog = new OverwriteDialog(targetPath)
            {
                Owner = GetActiveWindow()
            };

            dialog.ShowDialog();
            return dialog.Decision;
        });
    }

    public void ShowError(string title, string message)
    {
        InvokeOnUiThreadAsync<object?>(() =>
        {
            MessageBox.Show(GetActiveWindow(), message, title, MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        });
    }

    private static void ApplyInitialLocation(FileDialog dialog, string? currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            return;
        }

        var folder = Path.GetDirectoryName(currentPath);

        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            dialog.InitialDirectory = folder;
        }

        if (File.Exists(currentPath))
        {
            dialog.FileName = Path.GetFileName(currentPath);
        }
    }

    private static Window? GetActiveWindow()
    {
        var application = Application.Current;

        if (application is null)
        {
            return null;
        }

        return application.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
               ?? application.MainWindow;
    }

    private static Task<T> InvokeOnUiThreadAsync<T>(Func<T> function)
    {
        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            return Task.FromResult(function());
        }

        return dispatcher.InvokeAsync(function).Task;
    }
}
