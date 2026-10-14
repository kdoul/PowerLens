namespace PowerLens.Core;

/// <summary>The few numbers the adapter estimator needs from one poll.</summary>
public readonly record struct PowerSample(
    DateTimeOffset Time,
    bool AcOnline,
    // Net battery power in mW across all batteries: positive = charging, negative = discharging.
    int? BatteryRateMw,
    double? ChargePercent)
{
    public double? BatteryRateWatts => BatteryRateMw / 1000.0;

    public static PowerSample From(DateTimeOffset time, SystemPowerState? system, IReadOnlyList<BatteryDevice> batteries)
    {
        bool ac = system?.AcOnline
            ?? batteries.Any(b => b.Status?.PowerState.HasFlag(BatteryPowerState.PowerOnLine) == true);

        // Relative-capacity batteries report rates in arbitrary units, so they can't be summed as mW.
        var absolute = batteries.Where(b => b.Status is not null && !b.IsRelative).ToList();
        int? rate = null;
        if (absolute.Count > 0 && absolute.All(b => b.Status!.RateMw is not null))
            rate = absolute.Sum(b => b.Status!.RateMw!.Value);
        else if (system is { BatteryPresent: true, RateMw: int systemRate })
            rate = systemRate;

        double? percent = null;
        var withCapacity = batteries
            .Where(b => b.Status?.Capacity is not null && b.Info is { FullChargedCapacity: > 0 and not BatteryStructs.UnknownCapacity })
            .ToList();
        if (withCapacity.Count > 0)
        {
            double remaining = withCapacity.Sum(b => (double)b.Status!.Capacity!.Value);
            double full = withCapacity.Sum(b => (double)b.Info!.FullChargedCapacity);
            percent = Math.Clamp(100.0 * remaining / full, 0, 100);
        }
        else if (system is { MaxCapacityMwh: > 0 })
        {
            percent = Math.Clamp(100.0 * system.RemainingCapacityMwh / system.MaxCapacityMwh, 0, 100);
        }

        return new PowerSample(time, ac, rate, percent);
    }
}
