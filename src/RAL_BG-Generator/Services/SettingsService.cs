using System.IO;
using System.Text.Json;
using RalBgGenerator.Models;

namespace RalBgGenerator.Services;

public interface ISettingsService
{
    string SettingsFilePath { get; }

    AppSettings Load();

    void Save(AppSettings settings);
}

/// <summary>
/// Liest und schreibt appsettings.json im AppData-Ordner der Anwendung
/// (%APPDATA%\RAL_BG-Generator\appsettings.json).
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public SettingsService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RAL_BG-Generator");

        Directory.CreateDirectory(folder);
        SettingsFilePath = Path.Combine(folder, "appsettings.json");
    }

    public string SettingsFilePath { get; }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Beschaedigte oder gesperrte Datei darf den Start nicht verhindern.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(settings, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Speichern der Einstellungen ist nicht kritisch genug, um den Ablauf zu stoppen.
        }
    }
}
