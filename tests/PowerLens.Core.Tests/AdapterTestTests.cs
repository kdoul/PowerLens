using PowerLens.Core;

namespace PowerLens.Core.Tests;

public class AdapterTestTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
    private int _t;

    private void Feed(AdapterTest test, bool ac, int? rateMw, int seconds = 1, double pct = 40)
    {
        for (int i = 0; i < seconds; i++)
            test.Feed(new PowerSample(T0.AddSeconds(_t++), ac, rateMw, pct));
    }

    [Fact]
    public void FullRunProducesChargerEstimate()
    {
        var test = new AdapterTest();
        test.Start();

        Feed(test, ac: true, rateMw: 25_000, seconds: 3);
        Assert.Equal(AdapterTestStage.WaitingForUnplug, test.Stage);

        Feed(test, ac: false, rateMw: -12_000, seconds: 22);
        Assert.Equal(AdapterTestStage.WaitingForPlugIn, test.Stage);

        Feed(test, ac: true, rateMw: 33_000, seconds: 40);

        Assert.Equal(AdapterTestStage.Completed, test.Stage);
        var r = test.Result!;
        Assert.Equal(12.0, r.SystemDrawWatts, 3);
        Assert.Equal(33.0, r.BatteryRateWatts, 3);
        Assert.Equal(45.0 / AdapterEstimator.ConversionEfficiency, r.InputWatts, 3);
        Assert.Equal(60.0, r.LikelyChargerClass!.Watts);
        Assert.Equal(1.0, test.Progress);
    }

    [Fact]
    public void UnpluggingDuringChargeMeasurementFails()
    {
        var test = new AdapterTest();
        test.Start();
        Feed(test, ac: false, rateMw: -10_000, seconds: 22);
        Feed(test, ac: true, rateMw: 20_000, seconds: 12);
        Assert.Equal(AdapterTestStage.MeasuringCharge, test.Stage);

        Feed(test, ac: false, rateMw: -10_000);

        Assert.Equal(AdapterTestStage.Failed, test.Stage);
        Assert.Contains("unplugged", test.FailureReason);
    }

    [Fact]
    public void FailsWhenFirmwareReportsNoRate()
    {
        var test = new AdapterTest();
        test.Start();
        Feed(test, ac: false, rateMw: null, seconds: 25);
        Assert.Equal(AdapterTestStage.Failed, test.Stage);
    }
}
