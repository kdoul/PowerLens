using System.Globalization;
using System.Text;

namespace PowerLens.Core;

public sealed record ReportRow(string Group, string Key, string Value);

/// <summary>Turns a snapshot into labelled rows (for the UI lists) and plain text (for CLI/export).</summary>
public static class ReportFormatter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string VerdictText(ChargerVerdict verdict) => verdict switch
    {
        ChargerVerdict.OnBattery => "On battery",
        ChargerVerdict.Charging => "Charging",
        ChargerVerdict.SlowCharging => "Charging slowly",
        ChargerVerdict.InsufficientPower => "Charger too weak",
        ChargerVerdict.PluggedInNotCharging => "Plugged in, not charging",
        ChargerVerdict.Full => "Fully charged",
        _ => "Unknown",
    };

    public static string Watts(double? w, string format = "0.0") =>
        w is double v ? v.ToString(format, Inv) + " W" : "—";

    public static string Duration(TimeSpan? t) =>
        t is TimeSpan d ? (d.TotalHours >= 1 ? $"{(int)d.TotalHours} h {d.Minutes:00} min" : $"{d.Minutes} min") : "—";

    public static IEnumerable<ReportRow> ChargerRows(PowerSnapshot s)
    {
        const string g = "Charger";
        var a = s.Adapter;
        yield return new(g, "Power source", a.AcOnline ? "Plugged in (AC online)" : "Battery");
        yield return new(g, "Status", VerdictText(a.Verdict));
        if (a.Reported is { } r)
        {
            yield return new(g, "Reported charger rating", Watts(r.Watts, "0.#"));
            yield return new(g, "Reported contract", r.Detail);
            yield return new(g, "Reported by", r.Source);
        }
        yield return new(g, "Battery power (+ in / − out)", SignedWatts(a.BatteryRateWatts));
        yield return new(g, "System load baseline", Watts(a.SystemDrawWatts));
        if (a.AcOnline)
        {
            yield return new(g, "Estimated charger output now", Watts(a.EstimatedInputWatts) + (a.InputIsLowerBound ? " (charge power only — lower bound)" : ""));
            yield return new(g, "Peak since plugged in", Watts(a.PeakInputWatts));
            yield return new(g, "Consistent with", a.LikelyChargerClass is { } c ? $"a {c.Label} charger or larger ({c.Description})" : "—");
            yield return new(g, "Confidence", a.Confidence.ToString());
        }
        yield return new(g, "Conversion efficiency assumed", (AdapterEstimator.ConversionEfficiency * 100).ToString("0", Inv) + " %");

        foreach (var b in s.Batteries)
        {
            string bg = $"Charging stack — battery {b.Index + 1}";
            if (b.ChargingSource is { } src)
                yield return new(bg, "Charging source", $"{src.Type} ({(src.Online ? "online" : "offline")})");
            if (b.ChargerStatus is { } cs)
            {
                yield return new(bg, "Charger type", cs.Type.ToString());
                yield return new(bg, "Voltage", $"{cs.VoltageMv / 1000.0:0.00} V");
                yield return new(bg, "Max current", $"{cs.MaxCurrentMa / 1000.0:0.00} A");
                yield return new(bg, "Contract", Watts(cs.Watts, "0.#"));
                yield return new(bg, "USB-PD", cs.IsPowerDelivery ? "Yes" : "No");
                yield return new(bg, "Port", cs.PortType == UsbChargerPort.TypeC ? "USB Type-C" : "Legacy USB");
            }
            if (b.ChargingSource is null && b.ChargerStatus is null)
                yield return new(bg, "Negotiated contract", "Not exposed by this battery driver (normal for ACPI laptops)");
        }

        foreach (var note in a.Notes)
            yield return new("Notes", "•", note);
    }

    public static IEnumerable<ReportRow> BatteryRows(PowerSnapshot s)
    {
        if (s.Batteries.Count == 0)
        {
            yield return new("Battery", "Status", "No battery found");
            yield break;
        }

        foreach (var b in s.Batteries)
        {
            string g = $"Battery {b.Index + 1}" + (b.DeviceName is { } n ? $" — {n}" : "");
            if (b.Error is { } err)
            {
                yield return new(g, "Error", err);
                continue;
            }

            string unit = b.Info?.CapacityUnit ?? "mWh";
            yield return new(g, "Charge", b.ChargePercent is double p ? $"{p:0.0} %" : "—");
            yield return new(g, "State", DescribeState(b.Status?.PowerState));
            yield return new(g, "Power (+ in / − out)", SignedWatts(b.IsRelative ? null : b.Status?.RateMw / 1000.0));
            yield return new(g, "Voltage", b.Status?.VoltageMv is uint mv ? $"{mv / 1000.0:0.000} V" : "—");
            if (b.Status?.RateMw is int rate && b.Status?.VoltageMv is uint v && v > 0 && !b.IsRelative)
                yield return new(g, "Current", $"{rate / (double)v:0.000} A");
            yield return new(g, "Remaining capacity", b.Status?.Capacity is uint cap ? $"{cap:N0} {unit}" : "—");
            yield return new(g, "Full charge capacity", b.Info is { } i1 ? $"{i1.FullChargedCapacity:N0} {unit}" : "—");
            yield return new(g, "Design capacity", b.Info is { } i2 ? $"{i2.DesignedCapacity:N0} {unit}" : "—");
            yield return new(g, "Health (full ÷ design)", b.HealthPercent is double h ? $"{h:0.0} %" : "—");
            yield return new(g, "Cycle count", b.CycleCount?.ToString("N0", Inv) ?? "—");
            yield return new(g, "Temperature", b.TemperatureC is double t ? $"{t:0.0} °C" : "Not reported");
            yield return new(g, "Estimated time remaining", Duration(b.EstimatedTime));
            yield return new(g, "Chemistry", b.Info?.Chemistry ?? "—");
            yield return new(g, "Technology", b.Info?.Technology ?? "—");
            yield return new(g, "Manufacturer", b.ManufactureName ?? "—");
            yield return new(g, "Serial number", b.SerialNumber ?? "—");
            yield return new(g, "Manufacture date", b.ManufactureDate?.ToString("yyyy-MM-dd", Inv) ?? "Not reported");
            if (b.Info is { } i3)
            {
                yield return new(g, "Warning / low alerts", $"{i3.DefaultAlert2:N0} / {i3.DefaultAlert1:N0} {unit}");
                yield return new(g, "Capabilities", i3.Capabilities.ToString());
            }
        }

        if (s.System is { } sys)
        {
            const string g = "System (all batteries)";
            yield return new(g, "AC online", sys.AcOnline ? "Yes" : "No");
            yield return new(g, "Remaining / max", $"{sys.RemainingCapacityMwh:N0} / {sys.MaxCapacityMwh:N0} mWh");
            yield return new(g, "Rate", SignedWatts(sys.RateMw / 1000.0));
            yield return new(g, "Estimated time", Duration(sys.EstimatedTime));
        }
    }

    public static IEnumerable<ReportRow> RawRows(PowerSnapshot s)
    {
        foreach (var b in s.Batteries)
        {
            string g = $"IOCTL — battery {b.Index + 1}";
            yield return new(g, "Device path", b.DevicePath);
            yield return new(g, "Tag", b.Tag.ToString(Inv));
            if (b.UniqueId is { } id) yield return new(g, "Unique ID", id);
            if (b.ChargerId is { } cid) yield return new(g, "Charger ID", cid.ToString());
            foreach (var (k, v) in b.Diagnostics)
                yield return new(g, k, v);
        }

        foreach (var w in s.WmiBatteries)
            foreach (var (k, v) in w.Properties.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                yield return new($@"WMI root\WMI — {w.InstanceName}", k, v);

        if (s.OemFindings.Count == 0)
            yield return new("OEM WMI", "Result", "No vendor charger/adapter classes found");
        foreach (var f in s.OemFindings)
            foreach (var (k, v) in f.Properties)
                yield return new($"OEM WMI — {f.Namespace}:{f.ClassName} ({f.Instance})", k, v);
    }

    public static string ToText(PowerSnapshot s)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PowerLens report — {s.Time:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Machine: {Environment.MachineName}   OS: {Environment.OSVersion}");
        sb.AppendLine();
        sb.AppendLine(Headline(s.Adapter));
        sb.AppendLine();
        AppendRows(sb, ChargerRows(s));
        AppendRows(sb, BatteryRows(s));
        AppendRows(sb, RawRows(s));
        return sb.ToString();
    }

    /// <summary>One-line summary of the charger, e.g. "Charger: 65 W (reported)".</summary>
    public static string Headline(AdapterAssessment a)
    {
        if (!a.AcOnline) return "Charger: not connected (running on battery)";
        if (a.Reported is { } r) return $"Charger: {Watts(r.Watts, "0.#")} reported — {r.Detail}";
        if (a.EstimatedInputWatts is double w)
        {
            string cls = a.LikelyChargerClass is { } c ? $"; consistent with a {c.Label}+ charger" : "";
            return $"Charger: delivering ~{w:0.0} W now{(a.InputIsLowerBound ? " (lower bound)" : "")}{cls} [{a.Confidence} confidence]";
        }
        return $"Charger: connected — {VerdictText(a.Verdict).ToLowerInvariant()}, wattage not measurable right now";
    }

    private static void AppendRows(StringBuilder sb, IEnumerable<ReportRow> rows)
    {
        foreach (var group in rows.GroupBy(r => r.Group))
        {
            sb.AppendLine($"[{group.Key}]");
            int width = Math.Min(34, group.Max(r => r.Key.Length));
            foreach (var row in group)
                sb.AppendLine($"  {row.Key.PadRight(width)}  {row.Value}");
            sb.AppendLine();
        }
    }

    private static string SignedWatts(double? w) =>
        w is double v ? (v > 0 ? "+" : "") + v.ToString("0.00", Inv) + " W" : "—";

    private static string DescribeState(BatteryPowerState? state)
    {
        if (state is not BatteryPowerState st) return "—";
        var parts = new List<string>();
        if (st.HasFlag(BatteryPowerState.PowerOnLine)) parts.Add("AC online");
        if (st.HasFlag(BatteryPowerState.Charging)) parts.Add("charging");
        if (st.HasFlag(BatteryPowerState.Discharging)) parts.Add("discharging");
        if (st.HasFlag(BatteryPowerState.Critical)) parts.Add("CRITICAL");
        return parts.Count == 0 ? "idle" : string.Join(", ", parts);
    }
}
