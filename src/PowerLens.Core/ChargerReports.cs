using System.Globalization;
using System.Text.RegularExpressions;

namespace PowerLens.Core;

/// <summary>A charger rating reported directly by a driver or firmware (as opposed to estimated).</summary>
public sealed record ReportedCharger(double Watts, string Source, string Detail);

public static partial class ChargerReports
{
    [GeneratedRegex(@"watt", RegexOptions.IgnoreCase)]
    private static partial Regex WattProperty();

    /// <summary>
    /// Looks for a charger rating that Windows or the OEM publishes:
    /// 1. BATTERY_USB_CHARGER_STATUS from the battery driver (the negotiated USB contract), then
    /// 2. a numeric "*Watt*" property in an OEM WMI class (e.g. AdapterWattage).
    /// </summary>
    public static ReportedCharger? Find(IEnumerable<BatteryDevice> batteries, IEnumerable<OemPowerFinding> oem)
    {
        foreach (var battery in batteries)
        {
            if (battery.ChargerStatus is { Watts: double w } status && w > 0)
            {
                string detail = $"{status.VoltageMv / 1000.0:0.0#} V × {status.MaxCurrentMa / 1000.0:0.00} A"
                    + (status.IsPowerDelivery ? " · USB-PD contract" : "")
                    + (status.PortType == UsbChargerPort.TypeC ? " · Type-C port" : "");
                return new ReportedCharger(w, "Windows charging stack (BatteryChargerStatus)", detail);
            }
        }

        foreach (var finding in oem)
        {
            foreach (var (name, value) in finding.Properties)
            {
                if (!WattProperty().IsMatch(name)) continue;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double watts) && watts is >= 5 and <= 500)
                    return new ReportedCharger(watts, $"OEM WMI {finding.Namespace}:{finding.ClassName}", $"{name} = {value}");
            }
        }

        return null;
    }
}
