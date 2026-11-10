using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using RalBgGenerator.Models;

namespace RalBgGenerator.Converters;

/// <summary>Faerbt Logzeilen nach Ergebnis: Erfolg, Uebersprungen, Fehler.</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush InfoBrush = CreateFrozen(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly SolidColorBrush SuccessBrush = CreateFrozen(Color.FromRgb(0x10, 0x7C, 0x10));
    private static readonly SolidColorBrush SkippedBrush = CreateFrozen(Color.FromRgb(0x80, 0x6A, 0x00));
    private static readonly SolidColorBrush WarningBrush = CreateFrozen(Color.FromRgb(0xC0, 0x5A, 0x00));
    private static readonly SolidColorBrush ErrorBrush = CreateFrozen(Color.FromRgb(0xC4, 0x20, 0x20));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            LogLevel.Success => SuccessBrush,
            LogLevel.Skipped => SkippedBrush,
            LogLevel.Warning => WarningBrush,
            LogLevel.Error => ErrorBrush,
            _ => InfoBrush
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush CreateFrozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
