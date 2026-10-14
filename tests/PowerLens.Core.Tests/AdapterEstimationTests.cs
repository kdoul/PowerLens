using PowerLens.Core;

namespace PowerLens.Core.Tests;

public class AdapterEstimationTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static PowerSample At(int seconds, bool ac, int? rateMw, double pct = 50) =>
        new(T0.AddSeconds(seconds), ac, rateMw, pct);

    [Theory]
    [InlineData(41.0, 45.0)]
    [InlineData(65.5, 65.0)]
    [InlineData(66.9, 70.0)]
    [InlineData(12.0, 15.0)]
    [InlineData(230.0, 240.0)]
    public void SnapsToSmallestStandardCharger(double measured, double expected) =>
        Assert.Equal(expected, UsbPd.SmallestAtLeast(measured)!.Watts);

    [Fact]
    public void CombinesBaselineAndChargeRate()
    {
        var est = new AdapterEstimator();
        for (int i = 0; i < 10; i += 2) est.Add(At(i, ac: false, rateMw: -10_000));
        for (int i = 20; i < 40; i += 2) est.Add(At(i, ac: true, rateMw: 30_000));

        var a = est.Assess();

        Assert.Equal(ChargerVerdict.Charging, a.Verdict);
        Assert.Equal(10.0, a.SystemDrawWatts!.Value, 3);
        Assert.Equal(40.0 / AdapterEstimator.ConversionEfficiency, a.EstimatedInputWatts!.Value, 3);
        Assert.False(a.InputIsLowerBound);
        Assert.Equal(45.0, a.LikelyChargerClass!.Watts);
        Assert.Equal(EstimateConfidence.Medium, a.Confidence);
    }

    [Fact]
    public void WithoutBaselineEstimateIsLowerBound()
    {
        var est = new AdapterEstimator();
        est.Add(At(0, ac: true, rateMw: 18_000));

        var a = est.Assess();

        Assert.True(a.InputIsLowerBound);
        Assert.Equal(20.0, a.EstimatedInputWatts!.Value, 3);
        Assert.Equal(EstimateConfidence.Low, a.Confidence);
    }

    [Fact]
    public void DrainingWhilePluggedInMeansChargerTooWeak()
    {
        var est = new AdapterEstimator();
        est.Add(At(0, ac: false, rateMw: -25_000));
        est.Add(At(30, ac: true, rateMw: -8_000));

        var a = est.Assess();

        Assert.Equal(ChargerVerdict.InsufficientPower, a.Verdict);
        Assert.Equal(17.0 / AdapterEstimator.ConversionEfficiency, a.EstimatedInputWatts!.Value, 3);
    }

    [Fact]
    public void FullBatteryIsReportedAsFull()
    {
        var est = new AdapterEstimator();
        est.Add(At(0, ac: true, rateMw: 0, pct: 100));
        Assert.Equal(ChargerVerdict.Full, est.Assess().Verdict);
    }

    [Fact]
    public void ReportedChargerGivesHighConfidence()
    {
        var est = new AdapterEstimator();
        est.Add(At(0, ac: true, rateMw: 20_000));
        var a = est.Assess(new ReportedCharger(65, "test", "20 V × 3.25 A"));
        Assert.Equal(EstimateConfidence.High, a.Confidence);
    }

    [Fact]
    public void PeakResetsWhenChargerReconnected()
    {
        var est = new AdapterEstimator();
        est.Add(At(0, ac: true, rateMw: 60_000));
        est.Add(At(20, ac: false, rateMw: -10_000));
        est.Add(At(40, ac: true, rateMw: 5_000));

        Assert.Equal(15.0 / AdapterEstimator.ConversionEfficiency, est.Assess().PeakInputWatts!.Value, 3);
    }

    [Fact]
    public void FindsOemWattageProperty()
    {
        var finding = new OemPowerFinding(@"root\dcim\sysman", "DCIM_Adapter", "1",
            [KeyValuePair.Create("Name", "AC Adapter"), KeyValuePair.Create("RatedWattage", "90")]);

        var reported = ChargerReports.Find([], [finding]);

        Assert.NotNull(reported);
        Assert.Equal(90, reported.Watts);
    }

    [Fact]
    public void SampleSumsMultipleBatteries()
    {
        var info = new BatteryStaticInfo(BatteryCapabilities.SystemBattery, "Rechargeable", "LION", 50_000, 40_000, 0, 0, 0, 0);
        BatteryDevice Battery(int i, uint cap, int rate) => new()
        {
            Index = i,
            DevicePath = $"b{i}",
            Info = info,
            Status = new BatteryStatusInfo(BatteryPowerState.PowerOnLine | BatteryPowerState.Charging, cap, 12_000, rate),
        };

        var sample = PowerSample.From(T0, null, [Battery(0, 10_000, 15_000), Battery(1, 30_000, 5_000)]);

        Assert.True(sample.AcOnline);
        Assert.Equal(20_000, sample.BatteryRateMw);
        Assert.Equal(50.0, sample.ChargePercent!.Value, 3);
    }
}
