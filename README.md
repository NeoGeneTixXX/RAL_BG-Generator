# RAL_BG-Generator

Windows-Desktop-Anwendung, die per COM-Automatisierung mit **Solid Edge 2026** aus einzelnen
Teiledateien (`.psm` / `.par`) automatisiert Baugruppen (`.asm`) erzeugt — jeweils auf Basis
einer Vorlagebaugruppe, mit dem Teil am Ursprung fixiert und in einer frei wählbaren Farbe
(Face Style). Optional wird jeder Baugruppe zusätzlich eine **Pulverdatei** (`.par`) am
Ursprung fixiert beigelegt — sie trägt keine Geometrie, sondern Variablen für die Stückliste.

- **Technik:** C# / .NET 8, WPF, MVVM (`CommunityToolkit.Mvvm`)
- **Ausgabe:** `RAL_BG-Generator.exe` (x64)

---

## Voraussetzungen

| | |
|---|---|
| Betriebssystem | Windows x64 |
| Build | .NET SDK 8.0 oder neuer (getestet mit SDK 10.0.301) |
| Laufzeit | .NET 8 Desktop Runtime — oder Self-contained-Publish (siehe unten) |
| CAD | Solid Edge 2026, installiert und als COM-Server registriert |

Solid Edge wird **nur zur Laufzeit** benötigt, nicht zum Bauen.

## Bauen und Starten

```bash
dotnet build RAL_BG-Generator.sln -c Release
```

Ergebnis: `src\RAL_BG-Generator\bin\Release\net8.0-windows\RAL_BG-Generator.exe`

```bash
dotnet run --project src/RAL_BG-Generator/RAL_BG-Generator.csproj -c Release
```

### Weitergabe als einzelne EXE

`publish.ps1` erzeugt eine self-contained Single-File-EXE, die ohne installierte
.NET-Runtime läuft:

```bash
powershell -ExecutionPolicy Bypass -File publish.ps1
```

Ergebnis: `publish\RAL_BG-Generator.exe` (ca. 150 MB, keine Runtime-Installation nötig).

---

## Ablauf in der Anwendung

1. **Vorlage wählen** — Pfad zur `.asm`. Wird sofort validiert und in
   `%APPDATA%\RAL_BG-Generator\appsettings.json` gespeichert. Ungültig ⇒ Start bleibt gesperrt.
2. **Zeichnungsvorlage wählen** — Pfad zur `.dft`. Wird wie die Vorlagebaugruppe sofort
   validiert und gespeichert; ohne gültige Zeichnungsvorlage bleibt der Start gesperrt.
3. **Pulverdatei wählen** (optional) — eine `.par`, die in **jede** erzeugte Baugruppe
   zusätzlich am Ursprung fixiert eingefügt wird. Wird ebenfalls gespeichert und bei jedem
   Start wieder vorbelegt. Ohne Auswahl läuft der Batch ganz normal, nur ohne dieses Teil.
   Ein gesetzter, aber ungültiger Pfad sperrt den Start — sonst entstünden Baugruppen ohne
   den Stücklisteneintrag. Im Feld **Menge** daneben steht die Stücklistenmenge der
   Pulverdatei je Baugruppe (Standard 1, wird gespeichert). Erlaubt sind nur ganze Zahlen
   größer 0: Komma, Punkt, Vorzeichen, Buchstaben und Leerzeichen lassen sich weder tippen
   noch einfügen; ein leeres Feld oder 0 sperrt den Start. Ohne Pulverdatei ist das Feld
   ausgegraut.
4. **Farbe wählen** — die Face Styles werden aus der Vorlage gelesen. Das Suchfeld filtert die
   Liste (Vorlagen enthalten oft über 100 Styles).
5. **Teile wählen** — Mehrfachauswahl per Dialog oder Drag and Drop ins Fenster.
6. **Start** — pro Datei wird eine neue Baugruppe aus der Vorlage erzeugt, das Teil am Ursprung
   fixiert eingefügt, eingefärbt, die Pulverdatei dahinter gesetzt und das Ganze unter dem
   Namen der Quelldatei als `.asm` gespeichert. Danach entsteht aus der Zeichnungsvorlage
   eine Zeichnung der Baugruppe (`.dft`, gleicher Name, gleicher Ordner). Die Vorlage bringt
   Verknüpfung und Ansichten bereits mit — es wird **nichts hinzugefügt**: die vorhandene
   Verknüpfung auf `Rippenblech_Pulver.asm` wird auf die neue Baugruppe **umgestellt**, die
   Ansichten werden aktualisiert. Anschließend wird geprüft, ob die (einzige) ISO-Ansicht noch
   horizontal zentriert ist und dieselbe Breite hat wie in der Vorlage (Seitenbreite); weicht
   sie ab, werden Maßstab und Lage korrigiert. Unmittelbar vor dem Speichern werden alle
   Ansichten auf allen Blättern geprüft und veraltete aktualisiert. Erst dann wird die
   Zeichnung gespeichert und sofort geschlossen.

### Namensregel

Die Zieldatei heißt **exakt wie die Quelldatei**, nur mit der Endung `.asm` — ohne jeden Zusatz,
insbesondere ohne Farbnamen.

| Quelldatei | Zieldatei |
|---|---|
| `01_Wohnzimmer.psm` | `01_Wohnzimmer.asm` |
| `Halter-01.par` | `Halter-01.asm` |
| `Bauteil.mit.punkten.psm` | `Bauteil.mit.punkten.asm` |

Die Zeichnung heißt wie die Baugruppe, nur mit `.dft`. Die Überschreiben-Abfrage erscheint,
sobald Baugruppe **oder** Zeichnung bereits existiert, und gilt für beide.

> **Folge:** Der Dateiname enthält die Farbe nicht. Ein zweiter Lauf mit einer anderen Farbe
> zielt damit auf dieselbe Datei und überschreibt das vorherige Ergebnis — abgefangen durch die
> Überschreiben-Abfrage. Wer mehrere Farbvarianten nebeneinander braucht, wählt pro Lauf einen
> eigenen Zielordner.

---

## Architektur

```
src/RAL_BG-Generator/
├── Program.cs              expliziter [STAThread]-Einstiegspunkt, Composition Root
├── Models/                 Einstellungen, Logeintrag, Dateieintrag, Ergebnistypen
├── Infrastructure/
│   ├── StaThreadRunner.cs  dedizierter STA-Thread für alle COM-Aufrufe
│   ├── ComScope.cs         sammelt COM-Referenzen und gibt sie wieder frei
│   ├── OleMessageFilter.cs fängt "Solid Edge ist beschäftigt" auf COM-Ebene ab
│   ├── ComRetry.cs         zweites Netz: Wiederholung auf Ausnahmeebene
│   └── NativeMethods.cs    Ersatz für das in .NET 8 entfernte Marshal.GetActiveObject
├── Services/
│   ├── SolidEdgeService.cs gesamte CAD-Automatisierung (einziger COM-Berührungspunkt)
│   ├── SettingsService.cs  appsettings.json im AppData-Ordner
│   ├── DocumentValidator   prüft Vorlage und Pulverdatei, ohne Solid Edge zu starten
│   ├── OutputNameBuilder   Zieldateiname (Quellname + .asm)
│   └── DialogService.cs    Datei-/Ordnerdialoge, Überschreiben-Abfrage
├── ViewModels/             MainViewModel: Zustand, Kommandos, Batch-Ablauf
├── Views/                  MainWindow, OverwriteDialog
└── Assets/AppIcon.ico      App-Symbol (16–256 px), erzeugt von tools/IconGenerator

tools/IconGenerator/        zeichnet das Symbol als Vektorgrafik und schreibt die .ico
```

### App-Symbol

RAL-Farbfächer, davor ein isometrisches Bauteil in der Farbe der vordersten Karte — also
„Teil + RAL-Farbe = Baugruppe". Für 16–24 px gibt es eine vereinfachte Variante (drei Karten,
größerer Würfel), damit das Motiv auch in der Titelleiste lesbar bleibt.

Das Symbol wird nicht von Hand gepflegt, sondern erzeugt. Farben oder Formen ändert man in
`tools/IconGenerator/Program.cs` und schreibt die Datei neu:

```bash
dotnet run --project tools/IconGenerator -c Release -- src/RAL_BG-Generator/Assets
```

Zum Begutachten vorher mit `--preview` in einen beliebigen anderen Ordner schreiben — dann
entstehen zusätzlich `icon_*.png` und `preview.png` (alle Größen auf hellem und dunklem Grund,
kleine Größen pixelgenau vergrößert). Der Generator gehört nicht zur Solution und wird beim
normalen Build nicht mitgebaut.

Der Batch-Ablauf läuft asynchron auf dem UI-Thread, **jeder einzelne COM-Aufruf** wird auf den
STA-Thread ausgelagert (`await staRunner.InvokeAsync(...)`). Damit bleibt die UI bedienbar und
alle COM-Zugriffe erfolgen garantiert aus demselben Apartment. Meldungen, die aus dem
STA-Thread stammen, werden über den Dispatcher zurück auf den UI-Thread marshallt.

---

## Warum Late Binding (`dynamic`) statt Interop-Assemblies

Bewusste Entscheidung gegen einen Verweis auf die Solid-Edge-PIAs:

1. **Baubarkeit.** Die Interop-DLLs liegen ausschließlich in
   `C:\Program Files\Siemens\Solid Edge 2026\Program\`. Ein `HintPath` dorthin bricht
   `dotnet build` auf jedem Rechner ohne Solid Edge (Build-Server, Kollegenrechner).
2. **Versionsstabilität.** In Solid Edge 2026 heißen die Namespaces
   `SolidEdge.Assembly.Interop` / `SolidEdge.Framework.Interop` — früher
   `SolidEdgeAssembly` / `SolidEdgeFramework`. Früh gebundener Code müsste pro Version neu
   kompiliert werden.
3. **Verteilung.** Keine Interop-DLLs im Ausgabeordner, keine Bitness-/GAC-Fragen.

Preis: Tippfehler in API-Namen fallen erst zur Laufzeit auf. Deshalb wurden alle verwendeten
Signaturen vorab **gegen die Type Library von Solid Edge 2026 verifiziert** (Reflection über
`interop.SolidEdgeAssemblyLib.dll` / `interop.SolidEdgeFrameworkLib.dll`):

| Verwendet | Verifizierte Signatur |
|---|---|
| Neue Baugruppe aus Vorlage | `Documents.Add(Object ProgID, Object TemplateDoc)` |
| Teil einfügen | `Occurrences.AddWithMatrix(String OccurrenceFileName, ref Array Matrix)` |
| Fallback | `Occurrences.AddWithTransform(String, Double×6)` |
| Fixieren | `AssemblyDocument.Relations3d.AddGround(Occurrence)` |
| Ursprung ausblenden | `AssemblyDocument.CoordinateSystems` → `CoordinateSystem.Visible` (best effort) |
| Farbe | `Occurrence.FaceStyle` (Object, get/set), `Occurrence.Style` (String, get/set) |
| Style-Name | `FaceStyle.StyleName` — **es gibt kein `FaceStyle.Name`** |
| Menge der Pulverdatei | `Occurrence.HasNongraphicQuantity` → `Occurrence.NongraphicQuantity` (Double), sonst `Occurrence.Quantity` (Int32) |
| Speichern | `AssemblyDocument.SaveAs(String NewName, …)` |
| Schließen | `AssemblyDocument.Close(Object SaveChanges, …)` |
| Schließen abschließen | `Application.DoIdle()` (vgl. `Documents.CloseDocument(…, Object DoIdle)`) |
| Neue Zeichnung aus Vorlage | `Documents.Add("SolidEdge.DraftDocument", TemplateDoc)` |
| Verknüpfung finden | `DraftDocument.ModelLinks` → `Item`, `ModelLink.FileName` |
| Verknüpfung umstellen | `ModelLink.ChangeSource(String FullPath, [Object Member])` |
| Ansichten aktualisieren | `ModelLink.ForceUpdateViews()`, Fallback `DraftDocument.UpdateAll(Boolean bForceUpdate)` |
| Vor dem Speichern | je Ansicht `DrawingView.IsUpToDate` → `Update()`, notfalls `ForceUpdate()`, dann `Application.DoIdle()` |
| ISO-Ansicht finden | `DraftDocument.Sheets` → `Sheet.DrawingViews`, `DrawingView.DrawingViewType` (`igIsometricView = 2`), sonst `DrawingView.ViewOrientation(ref ×7)` |
| Blattgröße | `Sheet.SheetSetup.SheetWidth` / `SheetHeight` (Meter) |
| Prüfen/Korrigieren | `DrawingView.Range(ref ×4)`, `ScaleFactor`, `GetOrigin`/`SetOrigin` |

Die Zeichnungsvorlage muss die Verknüpfung auf `Rippenblech_Pulver.asm` enthalten (Konstante
`TemplateAssemblyFileName` in `SolidEdgeService.cs`); fehlt sie, schlägt die Zeichnung mit
klarer Meldung fehl. Wird nicht genau eine ISO-Ansicht gefunden, entfällt die Lageprüfung —
ein Hinweis steht im Protokoll. Meldet eine Ansicht auch nach `ForceUpdate()` noch „veraltet“,
wird die Zeichnung trotzdem gespeichert; die betroffenen Ansichten stehen im Protokoll.

`Marshal.GetActiveObject` existiert in .NET 8 **nicht mehr** (nur .NET Framework). Der Zugriff
auf eine laufende Instanz erfolgt daher über `ole32!CLSIDFromProgID` +
`oleaut32!GetActiveObject` (siehe `NativeMethods.cs`), mit Fallback auf
`Activator.CreateInstance(Type.GetTypeFromProgID(...))`.

---

## Verhalten, das bewusst so gewählt ist

- **Die Vorlage wird nie geöffnet.** Sie wird ausschließlich als Template an `Documents.Add`
  übergeben — auch beim Einlesen der Farbliste. Damit kann sie weder gesperrt noch verändert
  werden.
- **Quelldateien werden nie gespeichert.** Die erzeugte Baugruppe referenziert das Teil nur.
- **Jede Baugruppe wird sofort wieder geschlossen** — direkt nach dem Speichern, nicht erst am
  Ende des Laufs. Dazu gehört neben `Close(false)` ein `Application.DoIdle()`, nachdem alle
  COM-Referenzen auf das Dokument freigegeben sind (siehe Fehlersuche).
- **Fremde Solid-Edge-Instanz bleibt unangetastet.** War Solid Edge bereits offen, wird nur
  angedockt: kein `Quit` und keine Änderung an der Sichtbarkeit. Nur eine selbst gestartete
  Instanz wird sichtbar geschaltet — eine Umschaltmöglichkeit dafür gibt es bewusst nicht,
  weil sie in der Praxis keinen sichtbaren Unterschied gemacht hat.
- **Die Menge der Pulverdatei wird geprüft.** Nach dem Setzen wird sie zurückgelesen; weicht
  der Wert ab, schlägt die Datei fehl, statt mit falscher Menge zu speichern.
- **Nichtgrafische Pulverdatei → nichtgrafische Menge.** Die Pulverdatei hat keine Geometrie;
  Solid Edge meldet für sie `HasNongraphicQuantity = True` und lehnt `Occurrence.Quantity` mit
  `E_FAIL (0x80004005)` ab (in Solid Edge 2026 getestet). Dann wird
  `Occurrence.NongraphicQuantity` gesetzt — immer mit der ganzen Zahl aus dem Mengenfeld.
  Teile mit Geometrie bekommen weiterhin `Occurrence.Quantity`.
- **Die Pulverdatei bekommt keine Farbe.** Der Face Style gilt nur für das eigentliche Teil.
  Sie wird nach ihm eingefügt, steht also in der Stückliste hinter ihm, und die
  Koordinatensysteme der neuen Baugruppe werden ausgeblendet, damit an ihrer Stelle kein
  Ursprung sichtbar bleibt. Lässt die verbundene Solid-Edge-Version das Ausblenden nicht zu,
  steht ein einmaliger Hinweis im Protokoll — die Baugruppe wird trotzdem gespeichert.
- **Schlägt nur die Zeichnung fehl**, bleibt die gespeicherte Baugruppe erhalten; die Datei
  zählt in der Zusammenfassung trotzdem als fehlgeschlagen.
- **Fehlt die Pulverdatei beim Lauf**, schlägt die betroffene Datei fehl, statt eine Baugruppe
  ohne sie zu speichern.
- **Verbindung wird nach jedem Lauf getrennt.** `DisplayAlerts` wird zurückgesetzt und eine
  selbst gestartete Instanz beendet. Wer viele Läufe hintereinander macht, startet Solid Edge
  am besten einmal selbst — dann wird nur angedockt und nichts neu gestartet.
- **Farbzuweisung wird geprüft.** Nach dem Setzen wird zurückgelesen. Schlägt es fehl, bricht
  die Datei mit klarer Meldung ab, statt eine ungefärbte Baugruppe zu speichern.
- **Ein Fehler stoppt den Batch nicht.** Er wird rot protokolliert, die Verarbeitung läuft
  weiter; am Ende steht eine Zusammenfassung.
- **Abbrechen** wirkt nach der laufenden Datei (ein COM-Aufruf lässt sich nicht mittendrin
  abbrechen).
- **Überschreiben** wird nur gefragt, wenn die Zieldatei existiert. Eine vorhandene Datei wird
  erst gelöscht, wenn `SaveAs` sie nicht selbst ersetzen konnte — so geht sie bei einem anderen
  Fehler nicht verloren.

---

## Fehlersuche

**Die Anwendung schließt sich ohne Meldung.**
Sollte nicht mehr vorkommen: `App.xaml.cs` fängt Fehler des UI-Threads, nicht behandelte
Ausnahmen anderer Threads und unbeobachtete Task-Fehler ab, zeigt sie an und schreibt sie nach
`%APPDATA%\RAL_BG-Generator\error.log`.

Ein bekannter Fall war das Auto-Scrollen im Protokoll: `ScrollIntoView` direkt im
`CollectionChanged`-Handler erzwingt einen Measure-Durchlauf, **bevor** der
`ItemContainerGenerator` der ListBox dasselbe Event verarbeitet hat (der Handler des Fensters
wird zuerst registriert, weil der `DataContext` vor der Bindungsauswertung gesetzt wird). Der
Generator zählt dann weniger Elemente als die Sammlung tatsächlich enthält und WPF bricht mit
`InvalidOperationException: ItemsControl ist nicht konsistent mit der zugehörigen Elementquelle`
ab. Gelöst durch nachgelagertes Scrollen über den Dispatcher (`DispatcherPriority.Background`),
zusätzlich gebündelt, damit sich bei Protokollschüben keine Scroll-Aufträge stapeln.

**Solid-Edge-Prozess bleibt im Speicher.**
Häufigste Ursache sind nicht freigegebene COM-Referenzen. Dagegen: jeder Arbeitsschritt läuft in
einem `ComScope`, der alle Zwischenobjekte in umgekehrter Reihenfolge per
`Marshal.ReleaseComObject` freigibt; beim Trennen folgt `FinalReleaseComObject` plus
`GC.Collect()` / `WaitForPendingFinalizers()`. Bleibt trotzdem etwas hängen, hilft das
Protokoll: dort steht, ob die Instanz selbst gestartet oder nur angedockt wurde.

**Erzeugte Baugruppen bleiben bis zum Ende des Laufs in Solid Edge geöffnet.**
`Close(false)` allein reicht im Batch nicht: Solid Edge räumt geschlossene Dokumente erst im
Leerlauf endgültig ab, und während eines Laufs kommt es nie in den Leerlauf. Die Type Library
zeigt das Muster — `Documents.CloseDocument` hat dafür eigens einen Parameter `DoIdle`. Deshalb
wird nach jeder Baugruppe (und nach dem Einlesen der Farbliste) `Application.DoIdle()`
aufgerufen, und zwar erst nachdem der `ComScope` alle Referenzen auf das Dokument freigegeben
hat. Schlägt `DoIdle` fehl, steht einmalig ein Hinweis im Protokoll.

**„Could not get dispatch ID for … (error: 0x80010001)"**
`0x80010001` ist `RPC_E_CALL_REJECTED`: Solid Edge war beschäftigt (meist während des
Erzeugens der Baugruppe aus der Vorlage) und hat den Aufruf abgelehnt. Dagegen wirken zwei
Ebenen:

1. **`OleMessageFilter`** (`CoRegisterMessageFilter` auf dem STA-Thread) — COM fragt bei einer
   Ablehnung zurück und wiederholt den Aufruf alle 250 ms, bis zu 120 s. Das passiert
   *unterhalb* der Ausnahmeebene, es entsteht also gar kein Fehler.
2. **`ComRetry`** als zweites Netz. Wichtig: Der COM-Binder des DLR meldet eine Ablehnung
   **nicht** als `COMException`, sondern als `MissingMemberException` mit dem HRESULT im
   Meldungstext. Ein Retry, der nur `COMException` fängt, greift hier nicht — genau das war
   die Ursache der obigen Fehlermeldung.

Unterschieden wird dabei sauber: `0x80020006` (`DISP_E_UNKNOWNNAME`, also ein wirklich
fehlendes Member) gilt nicht als „beschäftigt" und wird nicht wiederholt.

**„Der Face Style … ließ sich nicht zuweisen."**
Vor ST9 war das per Automatisierung generell nicht möglich; ab ST9 ist der Fehler behoben. Bei
2026 ist die häufigere Ursache, dass das Bauteil selbst eine feste Flächenfarbe erzwingt.

**„Solid Edge ist nicht installiert oder nicht als COM-Server registriert."**
Die ProgID `SolidEdge.Application` fehlt in der Registry — Solid-Edge-Installation reparieren.

**Anwendung muss x64 sein.**
Solid Edge COM ist x64-only; das Projekt setzt `<PlatformTarget>x64</PlatformTarget>`.
