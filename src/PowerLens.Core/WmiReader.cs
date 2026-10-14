using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace PowerLens.Core;

/// <summary>
/// WMI sources: the standard root\WMI battery classes (cycle count fallback, raw view) and a
/// best-effort scan of vendor namespaces for classes that expose AC adapter / charger data.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class WmiReader
{
    private static readonly string[] BatteryClasses =
        ["BatteryStaticData", "BatteryFullChargedCapacity", "BatteryCycleCount", "BatteryStatus", "BatteryRuntime"];

    // Vendor namespaces where OEM tools publish power data, plus the generic one.
    private static readonly string[] OemNamespaces =
        [@"root\WMI", @"root\dcim\sysman", @"root\HP\InstrumentedBIOS", @"root\Lenovo", @"root\ASUS", @"root\Surface"];

    [GeneratedRegex(@"adapter|charger|power_?supply|acpower|ac_?line|type_?c|ucsi|usb_?pd|wattage", RegexOptions.IgnoreCase)]
    private static partial Regex InterestingClass();

    [GeneratedRegex(@"ndis|network|iscsi|scsi|storage|disk|display|video|monitor|bluetooth|wlan|wifi|audio|serial|parallel|keyboard|mouse|^__|^cim_|^msft_", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseClass();

    public static IReadOnlyList<WmiBatteryInfo> ReadBatteries()
    {
        var byInstance = new SortedDictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string cls in BatteryClasses)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", $"SELECT * FROM {cls}");
                foreach (ManagementBaseObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        string instance = mo["InstanceName"]?.ToString() ?? "(unnamed)";
                        if (!byInstance.TryGetValue(instance, out var props))
                            byInstance[instance] = props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                        foreach (PropertyData p in mo.Properties)
                        {
                            if (p.Name is "InstanceName" or "Active" or "Tag" or "Frequency_Object" or "Frequency_PerfTime" or "Timestamp_Object" or "Timestamp_PerfTime" or "Timestamp_Sys100NS")
                                continue;
                            props[p.Name] = Format(p.Value);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                // Class not implemented on this machine, or access denied. Skip it.
            }
        }
        return byInstance.Select(kv => new WmiBatteryInfo(kv.Key, kv.Value)).ToList();
    }

    /// <summary>
    /// Scans vendor WMI namespaces for classes that look like charger/adapter data
    /// (e.g. Dell Command | Monitor, HP, Lenovo, ASUS). Slow-ish; run off the UI thread.
    /// </summary>
    public static IReadOnlyList<OemPowerFinding> ScanOem(TimeSpan? perQueryTimeout = null)
    {
        var timeout = perQueryTimeout ?? TimeSpan.FromSeconds(5);
        var findings = new List<OemPowerFinding>();

        foreach (string ns in OemNamespaces)
        {
            List<string> classes;
            try
            {
                var scope = new ManagementScope(ns);
                scope.Connect();
                using var classSearcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM meta_class"),
                    new System.Management.EnumerationOptions { Timeout = timeout, ReturnImmediately = true, Rewindable = false });
                classes = [];
                foreach (ManagementBaseObject c in classSearcher.Get())
                {
                    using (c)
                    {
                        string name = c["__CLASS"]?.ToString() ?? "";
                        if (name.Length > 0 && InterestingClass().IsMatch(name) && !NoiseClass().IsMatch(name))
                            classes.Add(name);
                    }
                }
            }
            catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                continue; // Namespace not present on this machine.
            }

            foreach (string cls in classes.Take(40))
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(new ManagementScope(ns), new ObjectQuery($"SELECT * FROM {cls}"),
                        new System.Management.EnumerationOptions { Timeout = timeout, ReturnImmediately = true, Rewindable = false });
                    int count = 0;
                    foreach (ManagementBaseObject mo in searcher.Get())
                    {
                        using (mo)
                        {
                            if (count++ >= 8) break;
                            var props = mo.Properties.Cast<PropertyData>()
                                .Where(p => p.Value is not null)
                                .Take(60)
                                .Select(p => KeyValuePair.Create(p.Name, Format(p.Value)))
                                .ToList();
                            string instance = props.FirstOrDefault(p => p.Key is "InstanceName" or "InstanceID" or "Name" or "Caption").Value ?? $"#{count}";
                            findings.Add(new OemPowerFinding(ns, cls, instance, props));
                        }
                    }
                }
                catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
                {
                    // Abstract class, provider error or access denied — ignore.
                }
            }
        }
        return findings;
    }

    private static string Format(object? value) => value switch
    {
        null => "",
        string s => s.Trim(),
        Array a => string.Join(", ", a.Cast<object?>().Select(Format)),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
