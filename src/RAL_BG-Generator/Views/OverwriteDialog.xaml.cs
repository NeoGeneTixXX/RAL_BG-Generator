using System.Windows;
using RalBgGenerator.Models;

namespace RalBgGenerator.Views;

public partial class OverwriteDialog : Window
{
    public OverwriteDialog(string targetPath)
    {
        InitializeComponent();
        PathText.Text = targetPath;
    }

    /// <summary>Standard ist Abbrechen - auch wenn der Anwender das Fenster schliesst.</summary>
    public OverwriteDecision Decision { get; private set; } = OverwriteDecision.Cancel;

    private void OnOverwrite(object sender, RoutedEventArgs e) => Close(OverwriteDecision.Overwrite);

    private void OnSkip(object sender, RoutedEventArgs e) => Close(OverwriteDecision.Skip);

    private void OnOverwriteAll(object sender, RoutedEventArgs e) => Close(OverwriteDecision.OverwriteAll);

    private void OnSkipAll(object sender, RoutedEventArgs e) => Close(OverwriteDecision.SkipAll);

    private void OnCancel(object sender, RoutedEventArgs e) => Close(OverwriteDecision.Cancel);

    private void Close(OverwriteDecision decision)
    {
        Decision = decision;
        DialogResult = decision != OverwriteDecision.Cancel;
    }
}
