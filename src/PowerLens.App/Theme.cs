using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PowerLens.App;

/// <summary>Light/dark brushes, as DynamicResources so a theme switch restyles the live window.</summary>
internal static class Theme
{
    public static bool SystemUsesDarkMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    /// <param name="acrylic">When true, surfaces are translucent so the acrylic backdrop shows through.</param>
    public static void Apply(ResourceDictionary resources, bool dark, bool acrylic)
    {
        var palette = dark
            ? new Dictionary<string, string>
            {
                ["WindowBg"] = acrylic ? "#00000000" : "#FF1C1C1E",
                ["CardBg"] = acrylic ? "#59202024" : "#FF2A2A2E",
                ["CardBorder"] = "#1FFFFFFF",
                ["TextPrimary"] = "#FFF4F4F5",
                ["TextMuted"] = "#FFA1A1AA",
                ["RowHover"] = "#14FFFFFF",
                ["TabSelectedBg"] = "#2EFFFFFF",
                ["ButtonBg"] = "#1FFFFFFF",
                ["ButtonHover"] = "#33FFFFFF",
                ["TrackBg"] = "#26FFFFFF",
                ["Accent"] = "#FF2DD4BF",
                ["ScrollThumb"] = "#66FFFFFF",
                ["ChartBattery"] = "#FF4ADE80",
                ["ChartInput"] = "#FF60A5FA",
                ["ChartAcShade"] = "#1F60A5FA",
                ["ChartGrid"] = "#1AFFFFFF",
                ["ChartZero"] = "#4DFFFFFF",
            }
            : new Dictionary<string, string>
            {
                ["WindowBg"] = acrylic ? "#00000000" : "#FFF3F4F6",
                ["CardBg"] = acrylic ? "#A6FFFFFF" : "#FFFFFFFF",
                ["CardBorder"] = "#14000000",
                ["TextPrimary"] = "#FF18181B",
                ["TextMuted"] = "#FF5B5F6A",
                ["RowHover"] = "#0F000000",
                ["TabSelectedBg"] = "#E6FFFFFF",
                ["ButtonBg"] = "#B3FFFFFF",
                ["ButtonHover"] = "#FFFFFFFF",
                ["TrackBg"] = "#1A000000",
                ["Accent"] = "#FF0D9488",
                ["ScrollThumb"] = "#59000000",
                ["ChartBattery"] = "#FF16A34A",
                ["ChartInput"] = "#FF2563EB",
                ["ChartAcShade"] = "#1A2563EB",
                ["ChartGrid"] = "#14000000",
                ["ChartZero"] = "#40000000",
            };

        foreach (var (key, hex) in palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            resources[key] = brush;
        }
    }

    public static Brush VerdictBrush(Core.ChargerVerdict verdict)
    {
        var color = verdict switch
        {
            Core.ChargerVerdict.Charging or Core.ChargerVerdict.Full => Color.FromRgb(22, 163, 74),
            Core.ChargerVerdict.SlowCharging or Core.ChargerVerdict.PluggedInNotCharging => Color.FromRgb(217, 119, 6),
            Core.ChargerVerdict.InsufficientPower => Color.FromRgb(220, 38, 38),
            Core.ChargerVerdict.OnBattery => Color.FromRgb(82, 82, 91),
            _ => Color.FromRgb(113, 113, 122),
        };
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
