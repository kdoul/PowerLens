using System.Runtime.Versioning;

namespace PowerLens.Core;

public readonly record struct HistoryPoint(DateTimeOffset Time, bool AcOnline, double? BatteryWatts, double? InputWatts);

/// <summary>Fixed-duration history for the live chart.</summary>
public sealed class PowerHistory(TimeSpan window)
{
    private readonly LinkedList<HistoryPoint> _points = new();
    private readonly Lock _gate = new();

    public TimeSpan Window { get; } = window;

    public void Add(HistoryPoint point)
    {
        lock (_gate)
        {
            _points.AddLast(point);
            while (_points.First is { } first && point.Time - first.Value.Time > Window)
                _points.RemoveFirst();
        }
    }

    public IReadOnlyList<HistoryPoint> Snapshot()
    {
        lock (_gate) return [.. _points];
    }
}

/// <summary>Polls all sources and keeps the estimator and history up to date.</summary>
[SupportedOSPlatform("windows")]
public sealed class PowerMonitor
{
    private IReadOnlyList<WmiBatteryInfo> _wmiBatteries = [];
    private IReadOnlyList<OemPowerFinding> _oemFindings = [];

    public AdapterEstimator Estimator { get; } = new();
    public PowerHistory History { get; } = new(TimeSpan.FromMinutes(10));

    /// <summary>WMI queries are slow (hundreds of ms to seconds); run once, off the UI thread.</summary>
    public void LoadSlowSources()
    {
        _wmiBatteries = WmiReader.ReadBatteries();
        _oemFindings = WmiReader.ScanOem();
    }

    public PowerSnapshot Capture()
    {
        var now = DateTimeOffset.Now;
        var system = SystemPowerReader.Read();
        var batteries = BatteryReader.ReadAll();

        // ACPI batteries often report CycleCount = 0 through the IOCTL but correctly through WMI.
        var wmiCycles = _wmiBatteries.Select(w => w.CycleCount).Where(c => c is not null).ToList();
        if (wmiCycles.Count > 0)
            batteries = batteries.Select((b, i) => b with { WmiCycleCount = wmiCycles[Math.Min(i, wmiCycles.Count - 1)] }).ToList();

        var sample = PowerSample.From(now, system, batteries);
        Estimator.Add(sample);
        var assessment = Estimator.Assess(ChargerReports.Find(batteries, _oemFindings));

        History.Add(new HistoryPoint(now, sample.AcOnline, sample.BatteryRateWatts, assessment.EstimatedInputWatts));
        return new PowerSnapshot(now, system, batteries, _wmiBatteries, _oemFindings, sample, assessment);
    }
}
