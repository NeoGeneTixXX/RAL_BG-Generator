using System.Windows;
using RalBgGenerator.Infrastructure;
using RalBgGenerator.Services;
using RalBgGenerator.ViewModels;
using RalBgGenerator.Views;

namespace RalBgGenerator;

/// <summary>
/// Expliziter Einstiegspunkt. Der UI-Thread muss STA sein, weil WPF das verlangt und
/// weil COM-Aufrufe an Solid Edge nur aus einem STA-Apartment zulaessig sind.
/// Die eigentliche COM-Arbeit laeuft trotzdem auf einem eigenen STA-Thread
/// (<see cref="StaThreadRunner"/>), damit die UI nie blockiert.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new App();
        app.InitializeComponent();

        using var staRunner = new StaThreadRunner("SolidEdge-COM-STA");
        using var solidEdge = new SolidEdgeService(staRunner);

        var settings = new SettingsService();
        var dialogs = new DialogService();
        var viewModel = new MainViewModel(settings, solidEdge, dialogs);

        var window = new MainWindow { DataContext = viewModel };
        window.Loaded += async (_, _) => await viewModel.InitializeAsync();

        app.Run(window);
    }
}
