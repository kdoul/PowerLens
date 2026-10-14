# Changelog

## 0.1.0 — 2026-10-14

First public release.

- Battery details from the Windows battery driver: charge, health, cycle count, voltage, current,
  temperature, chemistry, manufacturer, serial number and manufacture date.
- USB-C charger wattage: the negotiated contract when the driver or OEM tools expose it, otherwise an
  estimate from system load plus charge power, snapped to standard USB-PD charger ratings.
- Guided adapter test for the most accurate charger measurement.
- Live 10-minute power chart.
- Windows 11 acrylic window that follows the light/dark theme.
- Optional tray icon with live watts, quick settings, close-to-tray and Start with Windows.
- Single instance: a second launch brings the running window to the front.
- `powerlens-cli` for text, JSON and live console output.
