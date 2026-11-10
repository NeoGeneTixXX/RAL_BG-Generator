<#
    Erzeugt eine self-contained Single-File-EXE, die ohne installierte .NET-Runtime laeuft.
    Ergebnis: publish\RAL_BG-Generator.exe
#>
[CmdletBinding()]
param(
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\RAL_BG-Generator\RAL_BG-Generator.csproj'

Write-Host "Veroeffentliche nach $OutputDirectory ..." -ForegroundColor Cyan

# WPF unterstuetzt kein Trimming - IncludeNativeLibrariesForSelfExtract haelt die EXE
# trotzdem als eine einzelne Datei zusammen.
dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $OutputDirectory

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish ist mit Exitcode $LASTEXITCODE fehlgeschlagen."
}

$exe = Join-Path $OutputDirectory 'RAL_BG-Generator.exe'

if (Test-Path $exe) {
    $sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "Fertig: $exe ($sizeMb MB)" -ForegroundColor Green
} else {
    throw "Die erwartete EXE wurde nicht erzeugt: $exe"
}
