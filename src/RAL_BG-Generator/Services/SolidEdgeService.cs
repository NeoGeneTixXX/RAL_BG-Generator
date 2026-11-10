using System.IO;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;
using RalBgGenerator.Infrastructure;

namespace RalBgGenerator.Services;

/// <summary>
/// Kapselt die gesamte Solid-Edge-Automatisierung.
///
/// Bindung: Late Binding ueber <c>dynamic</c> (IDispatch) statt Verweis auf die
/// Interop-Assemblies. Die Interop-DLLs liegen ausschliesslich im Installationsordner
/// von Solid Edge, sind versionsgebunden (in 2026 heissen die Namespaces
/// SolidEdge.Assembly.Interop / SolidEdge.Framework.Interop statt wie frueher
/// SolidEdgeAssembly / SolidEdgeFramework) und wuerden "dotnet build" auf jedem Rechner
/// ohne passende Installation brechen. Late Binding haelt das Projekt baubar und
/// versionsunabhaengig; der Preis sind Laufzeit- statt Compilerfehler, was durch
/// Pruefungen und klare Fehlermeldungen in dieser Klasse abgefangen wird.
///
/// Alle COM-Aufrufe laufen ueber den STA-Thread des <see cref="StaThreadRunner"/>.
/// </summary>
public sealed class SolidEdgeService : ISolidEdgeService
{
    private const string ApplicationProgId = "SolidEdge.Application";
    private const string AssemblyDocumentProgId = "SolidEdge.AssemblyDocument";
    private const string DraftDocumentProgId = "SolidEdge.DraftDocument";

    /// <summary>
    /// Baugruppe, auf die die Zeichnungsvorlage verknuepft ist. Diese Verknuepfung wird in
    /// jeder erzeugten Zeichnung auf die neue Baugruppe umgestellt.
    /// </summary>
    private const string TemplateAssemblyFileName = "Rippenblech_Pulver.asm";

    /// <summary>DrawingViewTypeConstants.igIsometricView = 2.</summary>
    private const int IsometricDrawingViewType = 2;

    /// <summary>
    /// ViewOrientationConstants 7..30 sind die raeumlichen Ausrichtungen (iso-, di- und
    /// trimetrisch); 1..6 sind die Hauptansichten.
    /// </summary>
    private const int FirstPictorialOrientation = 7;

    private const int LastPictorialOrientation = 30;

    /// <summary>
    /// Seitenrand der ISO-Ansicht in Metern - nur Fallback, falls sich ihre Breite in der
    /// Vorlage nicht messen laesst.
    /// </summary>
    private const double DrawingViewMargin = 0.01;

    /// <summary>Zulaessige Abweichung von Breite und Mitte der ISO-Ansicht: 0,5 mm.</summary>
    private const double ViewLayoutTolerance = 0.0005;

    /// <summary>Fallback, falls die Blattgroesse nicht lesbar ist: DIN A4 quer, in Metern.</summary>
    private const double FallbackSheetWidth = 0.297;

    private const double FallbackSheetHeight = 0.21;

    /// <summary>Ein Kaltstart von Solid Edge darf bis zu zwei Minuten dauern.</summary>
    private const int StartupRetryAttempts = 240;

    private readonly StaThreadRunner _staRunner;
    private readonly OleMessageFilter _messageFilter = new();

    private object? _application;
    private bool _startedByThisApplication;
    private bool _previousDisplayAlerts = true;
    private bool _displayAlertsChanged;

    /// <summary>
    /// Hinweise, die sich innerhalb einer Verbindung nicht mehr aendern, werden nur einmal
    /// protokolliert - sonst stuenden sie bei jeder Datei (und je Teil) erneut im Log.
    /// </summary>
    private bool _coordinateSystemHintLogged;

    private bool _addWithMatrixHintLogged;
    private bool _doIdleHintLogged;
    private bool _disposed;

    public SolidEdgeService(StaThreadRunner staRunner)
    {
        _staRunner = staRunner;
    }

    public bool IsConnected => _application is not null;

    public Action<string>? DiagnosticLog { get; set; }

    public Task<ConnectionInfo> ConnectAsync(bool allowStart) =>
        _staRunner.InvokeAsync(() => ConnectCore(allowStart));

    public Task<IReadOnlyList<string>> LoadFaceStylesAsync(string templatePath, bool allowStart) =>
        _staRunner.InvokeAsync(() =>
        {
            ConnectCore(allowStart);
            return LoadFaceStylesCore(templatePath);
        });

    public Task CreateAssemblyAsync(
        string templatePath,
        string partPath,
        string targetPath,
        string styleName,
        string? powderPartPath) =>
        _staRunner.InvokeAsync(
            () => CreateAssemblyCore(templatePath, partPath, targetPath, styleName, powderPartPath));

    public Task CreateDrawingAsync(string drawingTemplatePath, string assemblyPath, string targetPath) =>
        _staRunner.InvokeAsync(() => CreateDrawingCore(drawingTemplatePath, assemblyPath, targetPath));

    public Task DisconnectAsync() => _staRunner.InvokeAsync(DisconnectCore);

    // ----------------------------------------------------------------- Verbindung

    private ConnectionInfo ConnectCore(bool allowStart)
    {
        if (_application is not null)
        {
            return new ConnectionInfo(_startedByThisApplication, ReadVersion(_application));
        }

        if (!NativeMethods.IsProgIdRegistered(ApplicationProgId))
        {
            throw new SolidEdgeException(
                "Solid Edge ist auf diesem Rechner nicht installiert oder nicht als COM-Server registriert " +
                "(ProgID 'SolidEdge.Application' fehlt).");
        }

        // Muss vor dem ersten COM-Aufruf stehen und auf genau diesem STA-Thread passieren.
        if (!_messageFilter.Register())
        {
            DiagnosticLog?.Invoke(
                "Warnung: OLE-Message-Filter nicht registriert - Solid Edge kann Aufrufe ablehnen.");
        }

        var instance = NativeMethods.TryGetActiveObject(ApplicationProgId);
        var started = false;

        if (instance is null)
        {
            if (!allowStart)
            {
                throw new SolidEdgeNotRunningException();
            }

            var type = Type.GetTypeFromProgID(ApplicationProgId, throwOnError: false)
                       ?? throw new SolidEdgeException("Die ProgID 'SolidEdge.Application' konnte nicht aufgeloest werden.");

            DiagnosticLog?.Invoke("Solid Edge wird gestartet ...");

            instance = Activator.CreateInstance(type)
                       ?? throw new SolidEdgeException("Solid Edge konnte nicht gestartet werden.");

            started = true;
        }
        else
        {
            DiagnosticLog?.Invoke("An laufende Solid-Edge-Instanz angedockt (wird am Ende nicht beendet).");
        }

        _application = instance;
        _startedByThisApplication = started;

        dynamic application = _application;

        // Erster echter Aufruf. Direkt nach dem Start lehnt Solid Edge Automationsaufrufe
        // ab, bis die Anwendung bereit ist - deshalb mit Wiederholung.
        var version = ComRetry.Execute(
            () => (string)application.Version,
            attempts: started ? StartupRetryAttempts : 20);

        try
        {
            _previousDisplayAlerts = (bool)application.DisplayAlerts;
            application.DisplayAlerts = false;
            _displayAlertsChanged = true;
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            DiagnosticLog?.Invoke("Hinweis: DisplayAlerts konnte nicht gesetzt werden.");
        }

        if (started)
        {
            // Nur an der selbst gestarteten Instanz. Eine bereits laufende Instanz gehoert
            // dem Anwender - die wird nicht unter ihm umgeschaltet.
            try
            {
                application.Visible = true;
            }
            catch (Exception ex) when (ComErrors.IsComFailure(ex))
            {
                DiagnosticLog?.Invoke("Hinweis: Sichtbarkeit konnte nicht gesetzt werden.");
            }
        }

        return new ConnectionInfo(started, version);
    }

    private void DisconnectCore()
    {
        if (_application is null)
        {
            return;
        }

        dynamic application = _application;

        if (_displayAlertsChanged)
        {
            try
            {
                application.DisplayAlerts = _previousDisplayAlerts;
            }
            catch (Exception ex) when (ComErrors.IsComFailure(ex))
            {
                // Beim Beenden nicht mehr relevant.
            }
        }

        if (_startedByThisApplication)
        {
            try
            {
                ComRetry.Execute(() => { application.Quit(); }, attempts: 6);
            }
            catch (Exception ex) when (ComErrors.IsComFailure(ex))
            {
                DiagnosticLog?.Invoke("Warnung: Solid Edge liess sich nicht sauber beenden.");
            }
        }

        try
        {
            Marshal.FinalReleaseComObject(_application);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidComObjectException)
        {
            // Bereits freigegeben.
        }

        _application = null;
        _startedByThisApplication = false;
        _displayAlertsChanged = false;
        _coordinateSystemHintLogged = false;
        _addWithMatrixHintLogged = false;
        _doIdleHintLogged = false;

        _messageFilter.Revoke();

        // Raeumt RCWs weg, die beim Late Binding als Zwischenergebnisse entstanden sind.
        // Ohne das bleibt der Solid-Edge-Prozess gelegentlich im Speicher haengen.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static string ReadVersion(object application)
    {
        try
        {
            dynamic instance = application;
            return (string)instance.Version;
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            return "unbekannt";
        }
    }

    // ----------------------------------------------------------------- Face Styles

    private IReadOnlyList<string> LoadFaceStylesCore(string templatePath)
    {
        EnsureConnected();

        try
        {
            using var scope = new ComScope();
            dynamic application = _application!;
            dynamic documents = scope.Track(ComRetry.Execute(() => (object)application.Documents));

            // Die Vorlage wird bewusst NICHT geoeffnet, sondern als Template verwendet.
            // Damit ist ausgeschlossen, dass die Vorlagedatei gesperrt oder veraendert wird.
            var documentObject = ComRetry.Execute(() => (object)documents.Add(AssemblyDocumentProgId, templatePath));
            dynamic document = scope.Track(documentObject);

            try
            {
                dynamic faceStyles = scope.Track(ComRetry.Execute(() => (object)document.FaceStyles));
                var count = ComRetry.Execute(() => (int)faceStyles.Count);
                var names = new List<string>(count);

                // Solid-Edge-Collections sind 1-basiert.
                for (var index = 1; index <= count; index++)
                {
                    dynamic style = scope.Track(ComRetry.Execute(() => (object)faceStyles.Item(index)));

                    // Achtung: Die Eigenschaft heisst StyleName - ein "Name" existiert an
                    // FaceStyle nicht (gegen die Type Library von Solid Edge 2026 geprueft).
                    var name = ComRetry.Execute(() => (string?)style.StyleName);

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        names.Add(name);
                    }
                }

                return names
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
            }
            finally
            {
                CloseWithoutSaving(document);
            }
        }
        finally
        {
            // Nach dem ComScope, also erst wenn keine Referenz mehr auf das Dokument zeigt.
            CompletePendingCloses();
        }
    }

    // ----------------------------------------------------------------- Baugruppe erzeugen

    private void CreateAssemblyCore(
        string templatePath,
        string partPath,
        string targetPath,
        string styleName,
        string? powderPartPath)
    {
        EnsureConnected();

        if (!File.Exists(partPath))
        {
            throw new SolidEdgeException("Quelldatei nicht gefunden: " + partPath);
        }

        try
        {
            using var scope = new ComScope();
            dynamic application = _application!;
            dynamic documents = scope.Track(ComRetry.Execute(() => (object)application.Documents));

            var documentObject = ComRetry.Execute(() => (object)documents.Add(AssemblyDocumentProgId, templatePath));
            dynamic document = scope.Track(documentObject);

            try
            {
                dynamic occurrences = scope.Track(ComRetry.Execute(() => (object)document.Occurrences));
                dynamic occurrence = scope.Track(AddAtOrigin(occurrences, partPath));

                GroundOccurrence(scope, document, occurrence);
                ApplyFaceStyle(scope, document, occurrence, styleName);

                if (!string.IsNullOrWhiteSpace(powderPartPath))
                {
                    AddPowderPart(scope, document, occurrences, powderPartPath);
                }

                SaveAs(document, targetPath);
            }
            finally
            {
                // Die neue Baugruppe wird immer geschlossen - auch im Fehlerfall, damit der
                // naechste Durchlauf nicht auf einem Stapel offener Dokumente aufsetzt.
                CloseWithoutSaving(document);
            }
        }
        finally
        {
            // Nach dem ComScope, also erst wenn keine Referenz mehr auf das Dokument zeigt.
            CompletePendingCloses();
        }
    }

    /// <summary>
    /// Fuegt das Teil mit Einheitsmatrix ein: unveraendert im Ursprung, ohne Beziehungen.
    /// </summary>
    private object AddAtOrigin(dynamic occurrences, string partPath)
    {
        // 4x4-Einheitsmatrix, zeilenweise.
        var identity = new double[]
        {
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
            0, 0, 0, 1
        };

        try
        {
            return ComRetry.Execute(() => (object)occurrences.AddWithMatrix(partPath, identity));
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex) || ex is ArgumentException)
        {
            // AddWithMatrix erwartet ein SAFEARRAY als byref-Parameter; ueber IDispatch
            // laesst sich das nicht in jeder Konstellation marshallen. AddWithTransform
            // mit lauter Nullen ist geometrisch identisch zur Einheitsmatrix.
            if (!_addWithMatrixHintLogged)
            {
                _addWithMatrixHintLogged = true;
                DiagnosticLog?.Invoke("Hinweis: AddWithMatrix nicht nutzbar, verwende AddWithTransform(0,...,0).");
            }

            return ComRetry.Execute(() => (object)occurrences.AddWithTransform(partPath, 0d, 0d, 0d, 0d, 0d, 0d));
        }
    }

    /// <summary>
    /// Legt die Pulverdatei zusaetzlich in die Baugruppe: ebenfalls im Ursprung fixiert,
    /// aber ohne Face Style. Sie wird nach dem eigentlichen Teil eingefuegt, damit sie in
    /// der Stueckliste hinter ihm steht.
    /// </summary>
    private void AddPowderPart(ComScope scope, dynamic document, dynamic occurrences, string powderPartPath)
    {
        if (!File.Exists(powderPartPath))
        {
            throw new SolidEdgeException("Pulverdatei nicht gefunden: " + powderPartPath);
        }

        dynamic occurrence = scope.Track(AddAtOrigin(occurrences, powderPartPath));

        GroundOccurrence(scope, document, occurrence);
        HideCoordinateSystems(scope, document);
    }

    /// <summary>
    /// Blendet die Koordinatensysteme der neuen Baugruppe aus. Die Pulverdatei traegt keine
    /// Geometrie, sondern nur Variablen - ohne diesen Schritt bliebe an ihrer Stelle der
    /// Koordinatenursprung sichtbar.
    ///
    /// Best effort: laesst die verbundene Version das nicht zu, wird nur protokolliert.
    /// Die Baugruppe selbst ist dadurch nicht falsch.
    /// </summary>
    private void HideCoordinateSystems(ComScope scope, dynamic document)
    {
        try
        {
            dynamic coordinateSystems = scope.Track(ComRetry.Execute(() => (object)document.CoordinateSystems));
            var count = ComRetry.Execute(() => (int)coordinateSystems.Count);

            // Solid-Edge-Collections sind 1-basiert.
            for (var index = 1; index <= count; index++)
            {
                dynamic coordinateSystem = scope.Track(ComRetry.Execute(() => (object)coordinateSystems.Item(index)));
                ComRetry.Execute(() => { coordinateSystem.Visible = false; });
            }
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            if (!_coordinateSystemHintLogged)
            {
                _coordinateSystemHintLogged = true;
                DiagnosticLog?.Invoke(
                    "Hinweis: Die Koordinatensysteme der Baugruppe liessen sich nicht ausblenden.");
            }
        }
    }

    /// <summary>Fixiert das Occurrence (Grounding) - erzeugt keine Baugruppenbeziehungen.</summary>
    private static void GroundOccurrence(ComScope scope, dynamic document, dynamic occurrence)
    {
        try
        {
            // Gegen die Type Library geprueft: AddGround liegt an Relations3d,
            // nicht am Occurrence selbst.
            dynamic relations = scope.Track(ComRetry.Execute(() => (object)document.Relations3d));
            scope.Track(ComRetry.Execute(() => (object)relations.AddGround(occurrence)));
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            throw new SolidEdgeException(
                "Das Teil konnte nicht am Ursprung fixiert werden (Relations3d.AddGround ist fehlgeschlagen).",
                ex);
        }
    }

    /// <summary>
    /// Weist den Face Style zu und prueft das Ergebnis. Schlaegt die Zuweisung fehl,
    /// wird abgebrochen statt stillschweigend eine ungefaerbte Baugruppe zu speichern.
    /// </summary>
    private static void ApplyFaceStyle(ComScope scope, dynamic document, dynamic occurrence, string styleName)
    {
        dynamic faceStyles = scope.Track(ComRetry.Execute(() => (object)document.FaceStyles));
        var count = ComRetry.Execute(() => (int)faceStyles.Count);

        object? matchedStyle = null;

        for (var index = 1; index <= count; index++)
        {
            dynamic style = scope.Track(ComRetry.Execute(() => (object)faceStyles.Item(index)));
            var name = ComRetry.Execute(() => (string?)style.StyleName);

            if (string.Equals(name, styleName, StringComparison.OrdinalIgnoreCase))
            {
                matchedStyle = (object)style;
                break;
            }
        }

        if (matchedStyle is null)
        {
            throw new SolidEdgeException(
                "Der Face Style '" + styleName + "' ist in der neuen Baugruppe nicht vorhanden. " +
                "Wurde die Vorlage zwischenzeitlich geaendert? Dann die Farbliste neu laden.");
        }

        Exception? firstFailure = null;

        // Bevorzugter Weg laut API: Occurrence.FaceStyle (Read-Write).
        try
        {
            ComRetry.Execute(() => { occurrence.FaceStyle = matchedStyle; });
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            firstFailure = ex;
        }

        if (!StyleMatches(occurrence, styleName))
        {
            // Fallback ueber den Stilnamen (Occurrence.Style ist ebenfalls Read-Write).
            try
            {
                ComRetry.Execute(() => { occurrence.Style = styleName; });
            }
            catch (Exception ex) when (ComErrors.IsComFailure(ex))
            {
                firstFailure ??= ex;
            }
        }

        if (!StyleMatches(occurrence, styleName))
        {
            throw new SolidEdgeException(
                "Der Face Style '" + styleName + "' liess sich dem eingefuegten Teil nicht zuweisen. " +
                "In Versionen vor ST9 war das per Automatisierung generell nicht moeglich; " +
                "pruefen Sie ausserdem, ob das Bauteil selbst eine feste Flaechenfarbe erzwingt.",
                firstFailure);
        }
    }

    private static bool StyleMatches(dynamic occurrence, string styleName)
    {
        try
        {
            var current = ComRetry.Execute(() => (string?)occurrence.Style);
            return string.Equals(current, styleName, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            return false;
        }
    }

    // ----------------------------------------------------------------- Zeichnung erzeugen

    /// <summary>
    /// Zeichnung zur gespeicherten Baugruppe. Die Vorlage enthaelt Verknuepfung und Ansichten
    /// bereits: die Verknuepfung auf die Ursprungsbaugruppe wird auf die neue Baugruppe
    /// umgestellt, die Ansichten werden aktualisiert und die ISO-Ansicht wird auf Breite und
    /// horizontale Mitte geprueft. Solid Edge rechnet in Metern.
    /// </summary>
    private void CreateDrawingCore(string drawingTemplatePath, string assemblyPath, string targetPath)
    {
        EnsureConnected();

        if (!File.Exists(assemblyPath))
        {
            throw new SolidEdgeException("Baugruppe fuer die Zeichnung nicht gefunden: " + assemblyPath);
        }

        try
        {
            using var scope = new ComScope();
            dynamic application = _application!;
            dynamic documents = scope.Track(ComRetry.Execute(() => (object)application.Documents));

            // Wie bei der Baugruppe: die Vorlage wird nur als Template verwendet, nie geoeffnet.
            var documentObject = ComRetry.Execute(() => (object)documents.Add(DraftDocumentProgId, drawingTemplatePath));
            dynamic document = scope.Track(documentObject);

            try
            {
                // Die Vorlage bringt Verknuepfung und Ansichten bereits mit. Es wird nichts
                // hinzugefuegt: die vorhandene Verknuepfung wird auf die neue Baugruppe umgestellt.
                object modelLink = FindTemplateModelLink(scope, (object)document);
                var isoView = FindIsoView(scope, (object)document);

                // Soll-Breite der ISO-Ansicht aus der Vorlage merken, solange sie noch die
                // Ursprungsbaugruppe zeigt.
                ViewExtent? templateExtent = null;
                var sheetWidth = 0.0;

                if (isoView is { } found)
                {
                    templateExtent = TryReadExtent(found.View);
                    (sheetWidth, _) = ReadSheetSize(scope, found.Sheet);
                }

                ChangeModelLinkSource(modelLink, assemblyPath);
                UpdateDrawingViews((object)document, modelLink);

                // Ansichtsgrafik aufbauen lassen, damit Range verlaessliche Werte liefert.
                ComRetry.Execute(() => { application.DoIdle(); });

                if (isoView is { } iso)
                {
                    EnsureIsoViewLayout(iso.View, sheetWidth, templateExtent);
                }

                SaveAs(document, targetPath);
            }
            finally
            {
                CloseWithoutSaving(document);
            }
        }
        finally
        {
            CompletePendingCloses();
        }
    }

    private (double Width, double Height) ReadSheetSize(ComScope scope, object sheetObject)
    {
        dynamic sheet = sheetObject;

        try
        {
            dynamic setup = scope.Track(ComRetry.Execute(() => (object)sheet.SheetSetup));
            var width = ComRetry.Execute(() => (double)setup.SheetWidth);
            var height = ComRetry.Execute(() => (double)setup.SheetHeight);

            if (width > 0 && height > 0)
            {
                return (width, height);
            }
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            // Unten mit Fallback weiter.
        }

        DiagnosticLog?.Invoke("Hinweis: Blattgroesse der Zeichnungsvorlage nicht lesbar - DIN A4 quer angenommen.");
        return (FallbackSheetWidth, FallbackSheetHeight);
    }

    /// <summary>Umriss einer Zeichnungsansicht auf dem Blatt, in Metern.</summary>
    private readonly record struct ViewExtent(double Width, double CenterX, double CenterY);

    /// <summary>
    /// Sucht in der aus der Vorlage erzeugten Zeichnung die Verknuepfung auf die
    /// Ursprungsbaugruppe. Verglichen wird nur der Dateiname - der Ordner der Vorlage ist egal.
    /// </summary>
    private static object FindTemplateModelLink(ComScope scope, object documentObject)
    {
        dynamic document = documentObject;
        dynamic modelLinks = scope.Track(ComRetry.Execute(() => (object)document.ModelLinks));
        var count = ComRetry.Execute(() => (int)modelLinks.Count);

        for (var i = 1; i <= count; i++)
        {
            var index = i;
            dynamic link = scope.Track(ComRetry.Execute(() => (object)modelLinks.Item(index)));

            string? fileName;

            try
            {
                fileName = ComRetry.Execute(() => (string?)link.FileName);
            }
            catch (Exception ex) when (ComErrors.IsComFailure(ex))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(fileName) &&
                Path.GetFileName(fileName).StartsWith(TemplateAssemblyFileName, StringComparison.OrdinalIgnoreCase))
            {
                return link;
            }
        }

        throw new SolidEdgeException(
            "Die Zeichnungsvorlage enthaelt keine Verknuepfung auf '" + TemplateAssemblyFileName + "'. " +
            "Die Zeichnung kann nur aus der dafuer vorgesehenen Vorlage erzeugt werden.");
    }

    private static void ChangeModelLinkSource(object modelLinkObject, string assemblyPath)
    {
        dynamic modelLink = modelLinkObject;

        try
        {
            ComRetry.Execute(() => { modelLink.ChangeSource(assemblyPath); });
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            throw new SolidEdgeException(
                "Die Verknuepfung der Zeichnung liess sich nicht auf '" + Path.GetFileName(assemblyPath) +
                "' umstellen.",
                ex);
        }
    }

    /// <summary>Aktualisiert alle Ansichten der umgestellten Verknuepfung; Fallback: ganze Zeichnung.</summary>
    private void UpdateDrawingViews(object documentObject, object modelLinkObject)
    {
        dynamic document = documentObject;
        dynamic modelLink = modelLinkObject;

        try
        {
            ComRetry.Execute(() => { modelLink.UpdateViews(); });
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            DiagnosticLog?.Invoke("Hinweis: ModelLink.UpdateViews fehlgeschlagen - gesamte Zeichnung wird aktualisiert.");

            try
            {
                ComRetry.Execute(() => { document.UpdateAll(true); });
            }
            catch (Exception updateError) when (ComErrors.IsComFailure(updateError))
            {
                throw new SolidEdgeException(
                    "Die Zeichnungsansichten konnten nach dem Umstellen der Verknuepfung nicht aktualisiert werden.",
                    updateError);
            }
        }
    }

    /// <summary>
    /// Liefert die einzige ISO-Ansicht der Zeichnung samt ihrem Blatt. Gibt es keine als ISO
    /// erkennbare, aber insgesamt genau eine Ansicht, gilt diese. Sonst entfaellt die Pruefung.
    /// </summary>
    private (object View, object Sheet)? FindIsoView(ComScope scope, object documentObject)
    {
        dynamic document = documentObject;
        dynamic sheets = scope.Track(ComRetry.Execute(() => (object)document.Sheets));
        var sheetCount = ComRetry.Execute(() => (int)sheets.Count);

        var allViews = new List<(object View, object Sheet)>();
        var isoViews = new List<(object View, object Sheet)>();

        for (var s = 1; s <= sheetCount; s++)
        {
            var sheetIndex = s;
            object sheetObject = scope.Track(ComRetry.Execute(() => (object)sheets.Item(sheetIndex)));
            dynamic sheet = sheetObject;
            dynamic views = scope.Track(ComRetry.Execute(() => (object)sheet.DrawingViews));
            var viewCount = ComRetry.Execute(() => (int)views.Count);

            for (var v = 1; v <= viewCount; v++)
            {
                var viewIndex = v;
                object viewObject = scope.Track(ComRetry.Execute(() => (object)views.Item(viewIndex)));

                allViews.Add((viewObject, sheetObject));

                if (IsIsometric(viewObject))
                {
                    isoViews.Add((viewObject, sheetObject));
                }
            }
        }

        if (isoViews.Count == 1)
        {
            return isoViews[0];
        }

        if (isoViews.Count == 0 && allViews.Count == 1)
        {
            return allViews[0];
        }

        DiagnosticLog?.Invoke(
            "Hinweis: In der Zeichnung wurde nicht genau eine ISO-Ansicht gefunden (" + isoViews.Count +
            " von " + allViews.Count + " Ansichten) - Lage und Massstab werden nicht geprueft.");
        return null;
    }

    private static bool IsIsometric(object viewObject)
    {
        dynamic view = viewObject;

        try
        {
            if (ComRetry.Execute(() => (int)view.DrawingViewType) == IsometricDrawingViewType)
            {
                return true;
            }
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            // Unten ueber die Ausrichtung weiter.
        }

        try
        {
            double x = 0, y = 0, z = 0, upX = 0, upY = 0, upZ = 0;
            var orientation = 0;
            ComRetry.Execute(() => { view.ViewOrientation(ref x, ref y, ref z, ref upX, ref upY, ref upZ, ref orientation); });
            return orientation >= FirstPictorialOrientation && orientation <= LastPictorialOrientation;
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex) || ex is ArgumentException)
        {
            return false;
        }
    }

    private static ViewExtent? TryReadExtent(object viewObject)
    {
        try
        {
            var (minX, minY, maxX, maxY) = ReadRange(viewObject);
            var width = maxX - minX;

            return width > 0 ? new ViewExtent(width, (minX + maxX) / 2, (minY + maxY) / 2) : null;
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// Prueft nach dem Aktualisieren, ob die ISO-Ansicht noch horizontal zentriert ist und die
    /// Seitenbreite ausfuellt - Soll-Breite ist die Breite, die die Ansicht in der Vorlage hatte.
    /// Weicht sie ab (anderes Teil, anderer Umriss), werden Massstab und Lage korrigiert; die
    /// vertikale Mitte bleibt dabei dort, wo sie in der Vorlage lag.
    /// </summary>
    private void EnsureIsoViewLayout(object viewObject, double sheetWidth, ViewExtent? templateExtent)
    {
        dynamic view = viewObject;

        var targetWidth = templateExtent?.Width ?? sheetWidth - 2 * DrawingViewMargin;
        var targetCenterX = sheetWidth / 2;

        var current = TryReadExtent(viewObject)
            ?? throw new SolidEdgeException("Die Groesse der ISO-Ansicht konnte nach dem Aktualisieren nicht ermittelt werden.");

        var widthOk = Math.Abs(current.Width - targetWidth) <= ViewLayoutTolerance;
        var centerOk = Math.Abs(current.CenterX - targetCenterX) <= ViewLayoutTolerance;

        if (widthOk && centerOk)
        {
            DiagnosticLog?.Invoke("ISO-Ansicht geprueft: horizontal zentriert und auf Seitenbreite skaliert.");
            return;
        }

        if (!widthOk)
        {
            var currentScale = ComRetry.Execute(() => (double)view.ScaleFactor);

            if (currentScale <= 0)
            {
                currentScale = 1.0;
            }

            var newScale = currentScale * targetWidth / current.Width;
            ComRetry.Execute(() => { view.ScaleFactor = newScale; });
            UpdateView(view);

            current = TryReadExtent(viewObject)
                ?? throw new SolidEdgeException("Die Groesse der ISO-Ansicht konnte nach dem Skalieren nicht ermittelt werden.");
        }

        // Verschoben wird ueber die Differenz zwischen Umriss-Mitte und Zielmitte,
        // angewendet auf den Ansichtsursprung.
        double originX = 0, originY = 0;
        ComRetry.Execute(() => { view.GetOrigin(ref originX, ref originY); });

        var newX = originX + (targetCenterX - current.CenterX);
        var newY = originY + ((templateExtent?.CenterY ?? current.CenterY) - current.CenterY);
        ComRetry.Execute(() => { view.SetOrigin(newX, newY); });

        DiagnosticLog?.Invoke(
            "ISO-Ansicht korrigiert: " +
            (widthOk ? "Massstab unveraendert" : "auf Seitenbreite skaliert") + ", horizontal zentriert.");
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) ReadRange(object viewObject)
    {
        dynamic view = viewObject;
        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        ComRetry.Execute(() => { view.Range(ref minX, ref minY, ref maxX, ref maxY); });
        return (minX, minY, maxX, maxY);
    }

    private static void UpdateView(dynamic view)
    {
        try
        {
            ComRetry.Execute(() => { view.Update(); });
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            // Ansicht war bereits aktuell - unkritisch.
        }
    }

    private void SaveAs(dynamic document, string targetPath)
    {
        var targetFolder = Path.GetDirectoryName(targetPath);

        if (!string.IsNullOrEmpty(targetFolder))
        {
            Directory.CreateDirectory(targetFolder);
        }

        try
        {
            ComRetry.Execute(() => { document.SaveAs(targetPath); });
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex) && File.Exists(targetPath))
        {
            // Die vorhandene Datei wird erst entfernt, wenn SaveAs sie nicht selbst
            // ueberschreiben konnte - so geht sie bei einem anderen Fehler nicht verloren.
            DiagnosticLog?.Invoke("Zieldatei wird vor dem Speichern ersetzt: " + Path.GetFileName(targetPath));

            try
            {
                File.Delete(targetPath);
            }
            catch (Exception deleteError) when (deleteError is IOException or UnauthorizedAccessException)
            {
                throw new SolidEdgeException(
                    "Die vorhandene Zieldatei '" + Path.GetFileName(targetPath) + "' konnte nicht ersetzt werden " +
                    "(in Solid Edge geoeffnet oder schreibgeschuetzt?).",
                    deleteError);
            }

            ComRetry.Execute(() => { document.SaveAs(targetPath); });
        }
    }

    private void CloseWithoutSaving(dynamic document)
    {
        try
        {
            // false = Aenderungen verwerfen. Nach erfolgreichem SaveAs ist die Datei
            // bereits geschrieben; Quelldateien werden dabei nie gespeichert.
            ComRetry.Execute(() => { document.Close(false); }, attempts: 6);
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            DiagnosticLog?.Invoke("Warnung: Ein erzeugtes Dokument liess sich nicht schliessen.");
        }
    }

    /// <summary>
    /// Solid Edge fuehrt Close() nicht sofort vollstaendig aus, sondern raeumt geschlossene
    /// Dokumente erst im Leerlauf ab. Im Batch wird Solid Edge aber nie leerlaufend - ohne
    /// diesen Aufruf blieben alle erzeugten Baugruppen bis zum Ende des Laufs geoeffnet.
    /// Die Type Library bestaetigt das Muster: Documents.CloseDocument hat dafuer eigens
    /// einen Parameter DoIdle; hier wird Application.DoIdle() direkt aufgerufen.
    /// </summary>
    private void CompletePendingCloses()
    {
        if (_application is null)
        {
            return;
        }

        dynamic application = _application;

        try
        {
            ComRetry.Execute(() => { application.DoIdle(); }, attempts: 6);
        }
        catch (Exception ex) when (ComErrors.IsComFailure(ex))
        {
            if (!_doIdleHintLogged)
            {
                _doIdleHintLogged = true;
                DiagnosticLog?.Invoke(
                    "Hinweis: Application.DoIdle ist fehlgeschlagen - geschlossene Baugruppen bleiben " +
                    "moeglicherweise bis zum Ende des Laufs in Solid Edge geoeffnet.");
            }
        }
    }

    private void EnsureConnected()
    {
        if (_application is null)
        {
            throw new SolidEdgeException("Es besteht keine Verbindung zu Solid Edge.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            // Aufraeumen muss auf demselben STA-Thread laufen wie alle anderen COM-Aufrufe.
            _staRunner.InvokeAsync(DisconnectCore).Wait(TimeSpan.FromSeconds(60));
        }
        catch (Exception ex) when (ex is AggregateException or ObjectDisposedException or InvalidOperationException)
        {
            // Beim Herunterfahren nicht mehr behandelbar.
        }
    }
}
