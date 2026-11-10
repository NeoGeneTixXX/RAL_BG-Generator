using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RalBgGenerator.Models;

/// <summary>Eine ausgewaehlte Quelldatei (.psm/.par) inklusive Vorschau des Zieldateinamens.</summary>
public sealed partial class PartFileItem : ObservableObject
{
    public PartFileItem(string fullPath)
    {
        FullPath = fullPath;
    }

    public string FullPath { get; }

    public string FileName => Path.GetFileName(FullPath);

    public string DirectoryName => Path.GetDirectoryName(FullPath) ?? string.Empty;

    /// <summary>Vorschau auf den erzeugten Dateinamen, abhaengig von der gewaehlten Farbe.</summary>
    [ObservableProperty]
    private string _targetPreview = string.Empty;
}
