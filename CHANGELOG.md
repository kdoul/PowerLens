# Changelog

## 0.2.0 — 2026-10-14

- **Updates:** PowerLens checks GitHub for new releases about once a day and shows a bar when one is
  available. "Update now" downloads the package for your PC, verifies its SHA-256 checksum against the
  release, replaces the executables and restarts. Turn automatic checks off or check manually in
  Settings; the tray menu has "Check for updates" and "Update to …" too.

## 0.1.1 — 2026-10-14

- The power adapter tile now leads with the likely charger rating (e.g. "60 W") instead of the
  momentary draw, which can be just a few watts while the battery tops off.
- No more "Charging slowly" above 80 % battery, where laptops deliberately slow charging.
- Watts and other numbers follow the Windows number format everywhere (e.g. "5,3 W" in German).

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
