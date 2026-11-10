using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RalBgGenerator.Models;
using RalBgGenerator.Services;

namespace RalBgGenerator.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private static readonly string[] SupportedPartExtensions = [".psm", ".par"];

    private readonly ISettingsService _settingsService;
    private readonly ISolidEdgeService _solidEdgeService;
    private readonly IDialogService _dialogService;
    private readonly Dispatcher _dispatcher;

    /// <summary>Ungefilterte Face-Style-Liste der aktuellen Vorlage.</summary>
    private readonly List<string> _allFaceStyles = [];

    private AppSettings _settings = new();
    private CancellationTokenSource? _cancellation;
    private bool _isInitializing;

    public MainViewModel(
        ISettingsService settingsService,
        ISolidEdgeService solidEdgeService,
        IDialogService dialogService)
    {
        _settingsService = settingsService;
        _solidEdgeService = solidEdgeService;
        _dialogService = dialogService;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        PartFiles.CollectionChanged += OnPartFilesChanged;
        _solidEdgeService.DiagnosticLog = message => AppendLog(LogLevel.Info, message);
    }

    // ----------------------------------------------------------------- Zustand

    [ObservableProperty]
    private string? _templatePath;

    [ObservableProperty]
    private string _templateStatus = "Keine Vorlage ausgewaehlt.";

    [ObservableProperty]
    private bool _isTemplateValid;

    [ObservableProperty]
    private bool _isTemplateWarning;

    [ObservableProperty]
    private string? _drawingTemplatePath;

    [ObservableProperty]
    private string _drawingTemplateStatus = "Keine Zeichnungsvorlage ausgewaehlt.";

    [ObservableProperty]
    private bool _isDrawingTemplateValid;

    [ObservableProperty]
    private bool _isDrawingTemplateWarning;

    [ObservableProperty]
    private string? _powderPartPath;

    [ObservableProperty]
    private string _powderStatus = "Keine Pulverdatei ausgewaehlt (optional).";

    [ObservableProperty]
    private bool _isPowderValid = true;

    [ObservableProperty]
    private bool _isPowderWarning;

    /// <summary>Menge der Pulverdatei als Text, damit auch ungueltige Eingaben angezeigt werden.</summary>
    [ObservableProperty]
    private string _powderQuantityText = "1";

    [ObservableProperty]
    private bool _isPowderQuantityValid = true;

    [ObservableProperty]
    private string _powderQuantityStatus = string.Empty;

    [ObservableProperty]
    private string _faceStyleFilter = string.Empty;

    [ObservableProperty]
    private string? _selectedFaceStyle;

    [ObservableProperty]
    private bool _useCustomOutputFolder;

    [ObservableProperty]
    private string? _outputFolder;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMaximum = 1;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private PartFileItem? _selectedPartFile;

    public ObservableCollection<string> FaceStyles { get; } = [];

    public ObservableCollection<PartFileItem> PartFiles { get; } = [];

    public ObservableCollection<LogEntry> Log { get; } = [];

    /// <summary>Zielordner oder null, wenn neben der jeweiligen Quelldatei gespeichert wird.</summary>
    private string? EffectiveOutputFolder =>
        UseCustomOutputFolder && !string.IsNullOrWhiteSpace(OutputFolder) ? OutputFolder : null;

    /// <summary>Pulverdatei oder null, wenn keine eingefuegt werden soll.</summary>
    private string? EffectivePowderPartPath =>
        string.IsNullOrWhiteSpace(PowderPartPath) ? null : PowderPartPath;

    /// <summary>True, wenn eine Pulverdatei gewaehlt ist - nur dann ist die Menge relevant.</summary>
    public bool HasPowderPart => EffectivePowderPartPath is not null;

    /// <summary>Mengenfeld bedienbar: Pulverdatei gewaehlt und kein Lauf/Ladevorgang aktiv.</summary>
    public bool CanEditPowderQuantity => HasPowderPart && CanInteract();

    // ----------------------------------------------------------------- Start

    public async Task InitializeAsync()
    {
        _isInitializing = true;

        try
        {
            _settings = _settingsService.Load();

            TemplatePath = _settings.TemplatePath;
            DrawingTemplatePath = _settings.DrawingTemplatePath;
            PowderPartPath = _settings.PowderPartPath;
            PowderQuantityText = (_settings.PowderQuantity > 0 ? _settings.PowderQuantity : 1)
                .ToString(CultureInfo.InvariantCulture);
            OutputFolder = _settings.OutputFolder;
            UseCustomOutputFolder = _settings.UseCustomOutputFolder;
        }
        finally
        {
            _isInitializing = false;
        }

        AppendLog(LogLevel.Info, "Einstellungen: " + _settingsService.SettingsFilePath);
        ValidateTemplate();
        ValidateDrawingTemplate();
        ValidatePowderPart();
        ValidatePowderQuantity();

        if (!IsTemplateValid)
        {
            return;
        }

        // Beim Start wird Solid Edge NICHT hochgefahren. Laeuft bereits eine Instanz,
        // wird die Farbliste sofort geladen; sonst genuegt ein Klick auf "Farben laden".
        await LoadFaceStylesAsync(allowStart: false);
    }

    // ----------------------------------------------------------------- Vorlage

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private async Task BrowseTemplateAsync()
    {
        var selected = _dialogService.PickTemplate(TemplatePath);

        if (selected is null)
        {
            return;
        }

        TemplatePath = selected;

        if (IsTemplateValid)
        {
            // Neue Vorlage = neue Farbpalette. Hier darf Solid Edge gestartet werden,
            // weil der Anwender die Vorlage gerade bewusst gewechselt hat.
            await LoadFaceStylesAsync(allowStart: true);
        }
    }

    partial void OnTemplatePathChanged(string? value)
    {
        ValidateTemplate();
        PersistSettings();
    }

    private void ValidateTemplate()
    {
        var result = DocumentValidator.ValidateTemplate(TemplatePath);

        IsTemplateValid = result.IsValid;
        IsTemplateWarning = result.IsWarning;
        TemplateStatus = result.Message;

        if (!result.IsValid)
        {
            _allFaceStyles.Clear();
            FaceStyles.Clear();
            SelectedFaceStyle = null;

            if (!string.IsNullOrWhiteSpace(TemplatePath))
            {
                AppendLog(LogLevel.Error, result.Message);
            }
        }
        else if (result.IsWarning)
        {
            AppendLog(LogLevel.Warning, result.Message);
        }

        StartCommand.NotifyCanExecuteChanged();
    }

    // ----------------------------------------------------------------- Zeichnungsvorlage

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void BrowseDrawingTemplate()
    {
        var selected = _dialogService.PickDrawingTemplate(DrawingTemplatePath);

        if (selected is not null)
        {
            DrawingTemplatePath = selected;
        }
    }

    partial void OnDrawingTemplatePathChanged(string? value)
    {
        ValidateDrawingTemplate();
        PersistSettings();
    }

    private void ValidateDrawingTemplate()
    {
        var result = DocumentValidator.ValidateDrawingTemplate(DrawingTemplatePath);

        IsDrawingTemplateValid = result.IsValid;
        IsDrawingTemplateWarning = result.IsWarning;
        DrawingTemplateStatus = result.Message;

        // Wie bei der Pulverdatei: beim Start nur den zweiten Durchlauf protokollieren.
        if (!_isInitializing)
        {
            if (!result.IsValid && !string.IsNullOrWhiteSpace(DrawingTemplatePath))
            {
                AppendLog(LogLevel.Error, result.Message);
            }
            else if (result.IsWarning)
            {
                AppendLog(LogLevel.Warning, result.Message);
            }
        }

        StartCommand.NotifyCanExecuteChanged();
    }

    // ----------------------------------------------------------------- Pulverdatei

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void BrowsePowderPart()
    {
        var selected = _dialogService.PickPowderPart(PowderPartPath);

        if (selected is not null)
        {
            PowderPartPath = selected;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void ClearPowderPart() => PowderPartPath = null;

    partial void OnPowderPartPathChanged(string? value)
    {
        OnPropertyChanged(nameof(HasPowderPart));
        OnPropertyChanged(nameof(CanEditPowderQuantity));
        ValidatePowderPart();
        ValidatePowderQuantity();
        PersistSettings();
    }

    partial void OnPowderQuantityTextChanged(string value)
    {
        ValidatePowderQuantity();

        if (TryParsePowderQuantity(value, out var quantity))
        {
            _settings.PowderQuantity = quantity;
            PersistSettings();
        }
    }

    /// <summary>
    /// Nur Ziffern, keine Vorzeichen, keine Trennzeichen, keine Leerzeichen - und groesser 0.
    /// Das Textfeld laesst andere Zeichen gar nicht erst zu; die Pruefung hier ist das Netz
    /// fuer alles, was trotzdem hineingelangt (z. B. ein leeres Feld).
    /// </summary>
    public static bool TryParsePowderQuantity(string? text, out int quantity) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out quantity) && quantity > 0;

    private void ValidatePowderQuantity()
    {
        // Ohne Pulverdatei hat die Menge keine Wirkung und sperrt den Start nicht.
        IsPowderQuantityValid = !HasPowderPart || TryParsePowderQuantity(PowderQuantityText, out _);
        PowderQuantityStatus = IsPowderQuantityValid
            ? string.Empty
            : "Menge muss eine ganze Zahl groesser 0 sein.";

        StartCommand.NotifyCanExecuteChanged();
    }

    private void ValidatePowderPart()
    {
        var result = DocumentValidator.ValidatePowderPart(PowderPartPath);

        IsPowderValid = result.IsValid;
        IsPowderWarning = result.IsWarning;
        PowderStatus = result.Message;

        // Beim Start laeuft die Pruefung zweimal (Property-Setter und InitializeAsync).
        // Protokolliert wird nur der zweite Durchlauf, sonst stuende alles doppelt im Log.
        if (!_isInitializing)
        {
            if (!result.IsValid)
            {
                AppendLog(LogLevel.Error, result.Message);
            }
            else if (result.IsWarning)
            {
                AppendLog(LogLevel.Warning, result.Message);
            }
        }

        StartCommand.NotifyCanExecuteChanged();
    }

    // ----------------------------------------------------------------- Farben

    [RelayCommand(CanExecute = nameof(CanReloadFaceStyles))]
    private Task ReloadFaceStylesAsync() => LoadFaceStylesAsync(allowStart: true);

    private bool CanReloadFaceStyles() => IsTemplateValid && CanInteract();

    private async Task LoadFaceStylesAsync(bool allowStart)
    {
        if (string.IsNullOrWhiteSpace(TemplatePath))
        {
            return;
        }

        IsBusy = true;

        try
        {
            var previousSelection = SelectedFaceStyle ?? _settings.LastFaceStyle;

            var styles = await _solidEdgeService.LoadFaceStylesAsync(TemplatePath, allowStart);

            _allFaceStyles.Clear();
            _allFaceStyles.AddRange(styles);
            ApplyFaceStyleFilter();

            // Vorherige Auswahl beibehalten, sofern sie in der neuen Vorlage existiert.
            SelectedFaceStyle = FaceStyles.FirstOrDefault(
                                    style => string.Equals(style, previousSelection, StringComparison.OrdinalIgnoreCase))
                                ?? FaceStyles.FirstOrDefault();

            AppendLog(LogLevel.Info, $"{_allFaceStyles.Count} Face Style(s) aus der Vorlage gelesen.");
        }
        catch (SolidEdgeNotRunningException)
        {
            AppendLog(
                LogLevel.Info,
                "Solid Edge laeuft nicht - Farbliste ueber 'Farben laden' abrufen (startet Solid Edge).");
        }
        catch (Exception ex)
        {
            AppendLog(LogLevel.Error, "Farbliste konnte nicht geladen werden: " + Describe(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFaceStyleFilter()
    {
        var filter = FaceStyleFilter?.Trim();

        var matches = string.IsNullOrEmpty(filter)
            ? _allFaceStyles
            : _allFaceStyles.Where(style => style.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

        FaceStyles.Clear();

        foreach (var style in matches)
        {
            FaceStyles.Add(style);
        }
    }

    partial void OnFaceStyleFilterChanged(string value)
    {
        var previousSelection = SelectedFaceStyle;
        ApplyFaceStyleFilter();

        // Auswahl nur halten, wenn sie zum Filter passt.
        if (previousSelection is not null && FaceStyles.Contains(previousSelection))
        {
            SelectedFaceStyle = previousSelection;
        }
    }

    partial void OnSelectedFaceStyleChanged(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _settings.LastFaceStyle = value;
            PersistSettings();
        }

        StartCommand.NotifyCanExecuteChanged();
    }

    // ----------------------------------------------------------------- Dateien

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void AddFiles()
    {
        var initialFolder = PartFiles.LastOrDefault()?.DirectoryName
                            ?? Path.GetDirectoryName(TemplatePath ?? string.Empty);

        AddPartFiles(_dialogService.PickPartFiles(initialFolder));
    }

    /// <summary>Wird auch vom Drag-and-Drop-Handler der View verwendet.</summary>
    public void AddPartFiles(IEnumerable<string> paths)
    {
        var added = 0;
        var ignored = 0;

        foreach (var path in paths)
        {
            if (!SupportedPartExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                ignored++;
                continue;
            }

            if (PartFiles.Any(item => string.Equals(item.FullPath, path, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            PartFiles.Add(new PartFileItem(path));
            added++;
        }

        if (added > 0)
        {
            AppendLog(LogLevel.Info, $"{added} Datei(en) hinzugefuegt.");
        }

        if (ignored > 0)
        {
            AppendLog(LogLevel.Warning, $"{ignored} Datei(en) ignoriert - nur .psm und .par werden unterstuetzt.");
        }

        UpdateTargetPreviews();
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void RemoveFile(PartFileItem? item)
    {
        if (item is not null)
        {
            PartFiles.Remove(item);
        }
    }

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void ClearFiles() => PartFiles.Clear();

    private void OnPartFilesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateTargetPreviews();
        StartCommand.NotifyCanExecuteChanged();
        ClearFilesCommand.NotifyCanExecuteChanged();
    }

    private void UpdateTargetPreviews()
    {
        foreach (var item in PartFiles)
        {
            item.TargetPreview = OutputNameBuilder.BuildFileName(item.FullPath);
        }
    }

    // ----------------------------------------------------------------- Zielordner

    [RelayCommand(CanExecute = nameof(CanInteract))]
    private void BrowseOutputFolder()
    {
        var selected = _dialogService.PickFolder(OutputFolder);

        if (selected is null)
        {
            return;
        }

        OutputFolder = selected;
        UseCustomOutputFolder = true;
    }

    partial void OnOutputFolderChanged(string? value) => PersistSettings();

    partial void OnUseCustomOutputFolderChanged(bool value) => PersistSettings();

    // ----------------------------------------------------------------- Verarbeitung

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var templatePath = TemplatePath!;
        var drawingTemplatePath = DrawingTemplatePath!;
        var styleName = SelectedFaceStyle!;
        var powderPartPath = EffectivePowderPartPath;
        TryParsePowderQuantity(PowderQuantityText, out var powderQuantity);
        var files = PartFiles.ToList();

        _cancellation = new CancellationTokenSource();
        var token = _cancellation.Token;

        IsRunning = true;
        ProgressValue = 0;
        ProgressMaximum = files.Count;
        ProgressText = $"0 von {files.Count}";

        var summary = new BatchSummary();
        OverwriteDecision? blanketDecision = null;

        try
        {
            AppendLog(LogLevel.Info, $"Start: {files.Count} Datei(en), Farbe '{styleName}'.");

            if (powderPartPath is not null)
            {
                AppendLog(
                    LogLevel.Info,
                    "Pulverdatei je Baugruppe: " + Path.GetFileName(powderPartPath) + ", Menge " + powderQuantity);
            }

            var connection = await _solidEdgeService.ConnectAsync(allowStart: true);
            AppendLog(LogLevel.Info, $"Verbunden mit Solid Edge {connection.Version}.");

            for (var index = 0; index < files.Count; index++)
            {
                if (token.IsCancellationRequested)
                {
                    summary.WasCancelled = true;
                    break;
                }

                var file = files[index];
                ProgressText = $"{index + 1} von {files.Count}";

                var targetPath = OutputNameBuilder.BuildTargetPath(file.FullPath, EffectiveOutputFolder);
                var drawingPath = OutputNameBuilder.BuildDrawingPath(targetPath);

                // Baugruppe und Zeichnung gehoeren zusammen - eine Abfrage gilt fuer beide.
                if (File.Exists(targetPath) || File.Exists(drawingPath))
                {
                    var existingPath = File.Exists(targetPath) ? targetPath : drawingPath;
                    var decision = blanketDecision ?? await _dialogService.AskOverwriteAsync(existingPath);

                    if (decision is OverwriteDecision.OverwriteAll or OverwriteDecision.SkipAll)
                    {
                        blanketDecision = decision;
                    }

                    if (decision is OverwriteDecision.Cancel)
                    {
                        summary.WasCancelled = true;
                        break;
                    }

                    if (decision is OverwriteDecision.Skip or OverwriteDecision.SkipAll)
                    {
                        summary.Skipped++;
                        ProgressValue = index + 1;
                        AppendLog(LogLevel.Skipped, $"Uebersprungen: {Path.GetFileName(existingPath)} existiert bereits.");
                        continue;
                    }
                }

                try
                {
                    await _solidEdgeService.CreateAssemblyAsync(
                        templatePath,
                        file.FullPath,
                        targetPath,
                        styleName,
                        powderPartPath,
                        powderQuantity);

                    AppendLog(LogLevel.Success, $"Erstellt: {Path.GetFileName(targetPath)}");
                }
                catch (Exception ex)
                {
                    // Ein Fehler bei einer Datei darf den Batch nicht beenden.
                    summary.Failed++;
                    AppendLog(LogLevel.Error, $"Fehler bei {file.FileName}: {Describe(ex)}");
                    ProgressValue = index + 1;
                    continue;
                }

                try
                {
                    await _solidEdgeService.CreateDrawingAsync(drawingTemplatePath, targetPath, drawingPath);

                    summary.Succeeded++;
                    AppendLog(LogLevel.Success, $"Erstellt: {Path.GetFileName(drawingPath)}");
                }
                catch (Exception ex)
                {
                    // Die Baugruppe ist gespeichert, nur die Zeichnung fehlt - zaehlt als Fehler.
                    summary.Failed++;
                    AppendLog(
                        LogLevel.Error,
                        $"Zeichnung zu {Path.GetFileName(targetPath)} fehlgeschlagen: {Describe(ex)}");
                }
                finally
                {
                    ProgressValue = index + 1;
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog(LogLevel.Error, "Verarbeitung abgebrochen: " + Describe(ex));
            _dialogService.ShowError("Solid Edge", Describe(ex));
        }
        finally
        {
            await FinishRunAsync(summary, files.Count);
        }
    }

    private async Task FinishRunAsync(BatchSummary summary, int totalFiles)
    {
        try
        {
            // DisplayAlerts wird zurueckgesetzt und eine selbst gestartete Instanz beendet.
            await _solidEdgeService.DisconnectAsync();
        }
        catch (Exception ex)
        {
            AppendLog(LogLevel.Warning, "Beim Trennen von Solid Edge: " + Describe(ex));
        }

        _cancellation?.Dispose();
        _cancellation = null;

        IsRunning = false;
        ProgressText = $"{summary.Total} von {totalFiles}";

        var level = summary.Failed > 0 ? LogLevel.Error :
            summary.WasCancelled ? LogLevel.Warning : LogLevel.Success;

        var cancelledNote = summary.WasCancelled ? " (abgebrochen)" : string.Empty;

        AppendLog(
            level,
            $"Fertig{cancelledNote}: {summary.Succeeded} erfolgreich, {summary.Skipped} uebersprungen, " +
            $"{summary.Failed} fehlgeschlagen.");
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel()
    {
        _cancellation?.Cancel();
        AppendLog(LogLevel.Warning, "Abbruch angefordert - der Lauf endet nach der aktuellen Datei.");
    }

    private bool CanStart() =>
        !IsRunning
        && !IsBusy
        && IsTemplateValid
        && IsDrawingTemplateValid
        && IsPowderValid
        && IsPowderQuantityValid
        && !string.IsNullOrWhiteSpace(SelectedFaceStyle)
        && PartFiles.Count > 0;

    /// <summary>Bedienelemente sind gesperrt, solange ein Lauf oder ein Ladevorgang aktiv ist.</summary>
    private bool CanInteract() => !IsRunning && !IsBusy;

    partial void OnIsRunningChanged(bool value) => NotifyCommandStates();

    partial void OnIsBusyChanged(bool value) => NotifyCommandStates();

    private void NotifyCommandStates()
    {
        OnPropertyChanged(nameof(CanEditPowderQuantity));
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        BrowseTemplateCommand.NotifyCanExecuteChanged();
        BrowseDrawingTemplateCommand.NotifyCanExecuteChanged();
        BrowsePowderPartCommand.NotifyCanExecuteChanged();
        ClearPowderPartCommand.NotifyCanExecuteChanged();
        ReloadFaceStylesCommand.NotifyCanExecuteChanged();
        AddFilesCommand.NotifyCanExecuteChanged();
        RemoveFileCommand.NotifyCanExecuteChanged();
        ClearFilesCommand.NotifyCanExecuteChanged();
        BrowseOutputFolderCommand.NotifyCanExecuteChanged();
    }

    // ----------------------------------------------------------------- Hilfen

    private void PersistSettings()
    {
        if (_isInitializing)
        {
            return;
        }

        _settings.TemplatePath = TemplatePath;
        _settings.DrawingTemplatePath = DrawingTemplatePath;
        _settings.PowderPartPath = PowderPartPath;
        _settings.OutputFolder = OutputFolder;
        _settings.UseCustomOutputFolder = UseCustomOutputFolder;

        _settingsService.Save(_settings);
    }

    private void AppendLog(LogLevel level, string message)
    {
        // Diagnosemeldungen des Dienstes kommen vom STA-Thread und muessen ueber den
        // Dispatcher auf den UI-Thread marshallen.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.InvokeAsync(() => AppendLog(level, message));
            return;
        }

        Log.Add(new LogEntry(level, message));
    }

    private static string Describe(Exception exception) =>
        exception is AggregateException aggregate && aggregate.InnerException is not null
            ? aggregate.InnerException.Message
            : exception.Message;
}
