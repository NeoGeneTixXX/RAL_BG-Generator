using System.Collections.Specialized;
using System.Windows;
using System.Windows.Threading;
using RalBgGenerator.ViewModels;

namespace RalBgGenerator.Views;

public partial class MainWindow : Window
{
    /// <summary>Verhindert, dass sich waehrend eines Logschubs Scroll-Auftraege stapeln.</summary>
    private bool _isScrollPending;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel previous)
        {
            previous.Log.CollectionChanged -= OnLogChanged;
        }

        if (e.NewValue is MainViewModel current)
        {
            current.Log.CollectionChanged += OnLogChanged;
        }
    }

    private void OnLogChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _isScrollPending)
        {
            return;
        }

        // ScrollIntoView darf hier NICHT direkt aufgerufen werden: es erzwingt einen
        // Measure-Durchlauf, waehrend der ItemContainerGenerator die gerade gemeldete
        // Aenderung noch verarbeitet. Das Ergebnis ist ein harter Absturz mit
        // "ItemsControl ist nicht konsistent mit der zugehoerigen Elementquelle".
        // Deshalb erst scrollen, wenn das Layout die Aenderung verdaut hat.
        _isScrollPending = true;
        Dispatcher.BeginInvoke(ScrollLogToEnd, DispatcherPriority.Background);
    }

    /// <summary>Haelt die letzte Logzeile sichtbar.</summary>
    private void ScrollLogToEnd()
    {
        _isScrollPending = false;

        if (LogList.Items.Count == 0)
        {
            return;
        }

        LogList.ScrollIntoView(LogList.Items[^1]);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnFilesDropped(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            ViewModel?.AddPartFiles(paths);
        }

        e.Handled = true;
    }
}
