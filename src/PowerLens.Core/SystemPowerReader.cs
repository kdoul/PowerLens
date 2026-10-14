using System.Runtime.Versioning;
using static PowerLens.Core.Native.NativeMethods;

namespace PowerLens.Core;

[SupportedOSPlatform("windows")]
public static class SystemPowerReader
{
    /// <summary>Aggregate battery/AC state from CallNtPowerInformation(SystemBatteryState).</summary>
    public static SystemPowerState? Read()
    {
        byte[] buffer = new byte[BatteryStructs.SystemBatteryStateSize];
        uint status = CallNtPowerInformation(SystemBatteryState, IntPtr.Zero, 0, buffer, buffer.Length);
        return status == 0 ? BatteryStructs.ParseSystemBatteryState(buffer) : null;
    }
}
