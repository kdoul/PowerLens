# PowerLens

A coconutBattery-style battery and charger inspector for Windows 10 and 11, focused on one question:
**how many watts is my USB-C charger actually delivering?**

- **Charger card:** the reported charger rating when Windows exposes it. Otherwise, an estimate of the
  power coming from the charger now, the peak since it was plugged in, and the smallest standard
  USB-C/PD rating that fits (for example "consistent with a 45 W charger or larger").
- **Battery card:** charge %, charge/discharge power, health (full ÷ design capacity), cycle count,
  voltage, current, temperature, chemistry, manufacturer, serial number, and manufacture date.
- **Live chart:** battery power and estimated charger output for the last 10 minutes, with
  plugged-in periods shaded.
- **Adapter test:** a guided 1-minute measurement (unplug → plug back in) that gives the most
  accurate charger estimate the hardware allows.
- **Raw data:** every IOCTL result, the `root\WMI` battery classes, and any vendor WMI classes that
  look charger-related. You can copy or save it as a text report.
- **CLI** (`powerlens-cli`): text report, `--json`, or `--watch` for a live line every 2 s.

No admin rights needed.

## Where the numbers come from

| Data | Windows API |
| --- | --- |
| Battery details & live status | Battery class driver: `IOCTL_BATTERY_QUERY_INFORMATION` / `IOCTL_BATTERY_QUERY_STATUS` (same source as `powercfg /batteryreport`) |
| AC online, aggregate rate | `CallNtPowerInformation(SystemBatteryState)` |
| Cycle-count fallback | WMI `root\WMI` `BatteryCycleCount` (many ACPI firmwares report 0 through the IOCTL) |
| **Negotiated USB-C contract** (V × A) | `BatteryChargerStatus` / `BatteryChargingSource` IOCTL levels → `BATTERY_USB_CHARGER_STATUS` |
| **Vendor-reported adapter wattage** | Any numeric `*Watt*` property in Dell / HP / Lenovo / ASUS / Surface WMI namespaces |
| **Estimated charger output** | Derived: (system load + battery charge power) ÷ 90 % conversion efficiency |

### Why the wattage is often an estimate

Windows has **no public user-mode API for the USB-PD contract**. Your laptop negotiates the contract
in its embedded controller (UCSI), and Windows only uses it internally, for example for the
"slow charger" notification. PowerLens shows a hard number only in these cases:

1. The battery driver implements the charger-status IOCTL levels. This is common on
   Windows-on-ARM devices and devices that use the Windows charging stack, but rare on x86 ACPI
   laptops.
2. An OEM tool publishes it in WMI. For example, Dell Command | Monitor publishes it in
   `root\dcim\sysman`.

Otherwise PowerLens measures what the charger is delivering:

- **On battery**, the discharge rate is the system load. PowerLens remembers it as a baseline for
  30 minutes.
- **Plugged in**, the charger supplies system load + battery charge power + conversion losses.
- Laptops cap their own intake, and charging slows above about 80 %. So the measured figure is a
  **lower bound**: the charger is rated at least that high, never lower. For the best figure, run the
  **Adapter test** below 80 % battery with the laptop idle.
- If the battery drains while plugged in, the status shows **Charger too weak**. This is typical
  for phone chargers or non-PD sources.

## Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). It builds on Windows, macOS, or Linux (`EnableWindowsTargeting`).

```bash
dotnet test
```

```bash
dotnet run --project src/PowerLens.App
```

Single-file, self-contained executables (no .NET install needed on the target PC):

```bash
dotnet publish src/PowerLens.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish/app
```

```bash
dotnet publish src/PowerLens.Cli -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish/cli
```

For Snapdragon / ARM64 Windows laptops, use `-r win-arm64`.

## Layout

```
src/PowerLens.Core   battery IOCTL + WMI readers, struct decoding, charger estimator, adapter test (net10.0)
src/PowerLens.App    WinForms UI (net10.0-windows)
src/PowerLens.Cli    console report / --json / --watch (net10.0-windows)
tests/               xUnit tests for decoding and estimation logic (run on any OS)
```
