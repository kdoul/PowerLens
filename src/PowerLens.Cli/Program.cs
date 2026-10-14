using System.Text.Json;
using System.Text.Json.Serialization;
using PowerLens.Core;

// powerlens-cli            one-shot report (samples for a few seconds first)
// powerlens-cli --json     same data as JSON
// powerlens-cli --watch    live line every 2 s until Ctrl+C

var monitor = new PowerMonitor();
bool json = args.Contains("--json");
bool watch = args.Contains("--watch");

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("Usage: powerlens-cli [--json | --watch]");
    return 0;
}

if (watch)
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    Console.WriteLine("time      source   battery     charge   charger-now  peak       likely");
    while (!cts.IsCancellationRequested)
    {
        var s = monitor.Capture();
        var a = s.Adapter;
        Console.WriteLine(
            $"{s.Time:HH:mm:ss}  {(a.AcOnline ? "AC     " : "battery")}  {Signed(s.Sample.BatteryRateWatts),9}  " +
            $"{(s.Sample.ChargePercent is double p ? $"{p,5:0.0}%" : "   — "),7}  " +
            $"{ReportFormatter.Watts(a.Reported?.Watts ?? a.EstimatedInputWatts),11}  {ReportFormatter.Watts(a.PeakInputWatts),9}  " +
            $"{a.LikelyChargerClass?.Label ?? "—"}");
        try { await Task.Delay(2000, cts.Token); } catch (TaskCanceledException) { }
    }
    return 0;
}

monitor.LoadSlowSources();
PowerSnapshot snapshot = monitor.Capture();
for (int i = 0; i < 3; i++) // A few samples so the smoothed rate settles.
{
    await Task.Delay(1000);
    snapshot = monitor.Capture();
}

if (json)
{
    var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    Console.WriteLine(JsonSerializer.Serialize(snapshot, options));
}
else
{
    Console.WriteLine(ReportFormatter.ToText(snapshot));
}
return 0;

static string Signed(double? w) => w is double v ? $"{v:+0.0;-0.0;0.0} W" : "—";
