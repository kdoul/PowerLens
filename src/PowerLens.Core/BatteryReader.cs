using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using PowerLens.Core.Native;
using static PowerLens.Core.Native.NativeMethods;

namespace PowerLens.Core;

/// <summary>
/// Reads every battery through the battery class driver IOCTLs — the same interface
/// powercfg and the Windows battery flyout use. No admin rights required.
/// </summary>
[SupportedOSPlatform("windows")]
public static class BatteryReader
{
    public static IReadOnlyList<BatteryDevice> ReadAll()
    {
        var paths = EnumerateDevicePaths();
        var result = new List<BatteryDevice>(paths.Count);
        for (int i = 0; i < paths.Count; i++)
        {
            var device = Read(paths[i], i);
            if (device is not null) result.Add(device);
        }
        return result;
    }

    public static IReadOnlyList<string> EnumerateDevicePaths()
    {
        var guid = GuidDevClassBattery;
        IntPtr set = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == InvalidHandleValue) return [];

        var paths = new List<string>();
        try
        {
            for (int index = 0; index < 100; index++)
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data)) break;

                SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out int required, IntPtr.Zero);
                if (required <= 0) continue;

                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize is the size of the fixed part: 8 on 64-bit, 6 on 32-bit.
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, required, out _, IntPtr.Zero))
                    {
                        string? path = Marshal.PtrToStringUni(detail + 4);
                        if (!string.IsNullOrEmpty(path)) paths.Add(path);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return paths;
    }

    private static BatteryDevice? Read(string path, int index)
    {
        var diag = new Dictionary<string, string>();
        using SafeFileHandle handle = CreateFileW(
            path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
            IntPtr.Zero, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int err = Marshal.GetLastWin32Error();
            return new BatteryDevice { Index = index, DevicePath = path, Error = $"Cannot open battery device: {DescribeError(err)}" };
        }

        uint tag = QueryTag(handle);
        if (tag == 0) return null; // Empty battery slot.

        var info = Query(handle, tag, BatteryQueryInformationLevel.BatteryInformation, 64, diag) is { Length: >= BatteryStructs.BatteryInformationSize } bi
            ? BatteryStructs.ParseBatteryInformation(bi)
            : null;

        var chargerStatusRaw = Query(handle, tag, BatteryQueryInformationLevel.BatteryChargerStatus, 256, diag, hexDump: true);
        var chargerIdRaw = Query(handle, tag, BatteryQueryInformationLevel.BatteryChargerId, 64, diag, hexDump: true);

        return new BatteryDevice
        {
            Index = index,
            DevicePath = path,
            Tag = tag,
            Info = info,
            Status = QueryStatus(handle, tag, diag),
            TemperatureC = Query(handle, tag, BatteryQueryInformationLevel.BatteryTemperature, 4, diag) is { } t ? BatteryStructs.ParseTemperatureC(t) : null,
            DeviceName = QueryString(handle, tag, BatteryQueryInformationLevel.BatteryDeviceName, diag),
            ManufactureName = QueryString(handle, tag, BatteryQueryInformationLevel.BatteryManufactureName, diag),
            SerialNumber = QueryString(handle, tag, BatteryQueryInformationLevel.BatterySerialNumber, diag),
            UniqueId = QueryString(handle, tag, BatteryQueryInformationLevel.BatteryUniqueID, diag),
            ManufactureDate = Query(handle, tag, BatteryQueryInformationLevel.BatteryManufactureDate, 4, diag) is { } d ? BatteryStructs.ParseManufactureDate(d) : null,
            EstimatedTime = Query(handle, tag, BatteryQueryInformationLevel.BatteryEstimatedTime, 4, diag) is { } e ? BatteryStructs.ParseEstimatedTime(e) : null,
            ChargingSource = Query(handle, tag, BatteryQueryInformationLevel.BatteryChargingSource, 16, diag, hexDump: true) is { } cs ? BatteryStructs.ParseChargingSource(cs) : null,
            ChargerId = chargerIdRaw is { Length: >= 16 } ? new Guid(chargerIdRaw.AsSpan(0, 16)) : null,
            ChargerStatus = chargerStatusRaw is { } raw ? BatteryStructs.ParseUsbChargerStatus(raw, IntPtr.Size) : null,
            Diagnostics = diag,
        };
    }

    private static uint QueryTag(SafeFileHandle handle)
    {
        byte[] wait = new byte[4]; // Timeout 0: return immediately.
        byte[] tag = new byte[4];
        return DeviceIoControl(handle, IOCTL_BATTERY_QUERY_TAG, wait, wait.Length, tag, tag.Length, out int returned, IntPtr.Zero) && returned == 4
            ? BinaryPrimitives.ReadUInt32LittleEndian(tag)
            : 0;
    }

    private static BatteryStatusInfo? QueryStatus(SafeFileHandle handle, uint tag, Dictionary<string, string> diag)
    {
        // BATTERY_WAIT_STATUS { BatteryTag; Timeout; PowerState; LowCapacity; HighCapacity; } — zeros mean "now".
        byte[] input = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(input, tag);
        byte[] output = new byte[BatteryStructs.BatteryStatusSize];
        if (!DeviceIoControl(handle, IOCTL_BATTERY_QUERY_STATUS, input, input.Length, output, output.Length, out int returned, IntPtr.Zero)
            || returned < BatteryStructs.BatteryStatusSize)
        {
            diag["IOCTL QueryStatus"] = $"failed ({DescribeError(Marshal.GetLastWin32Error())})";
            return null;
        }
        diag["IOCTL QueryStatus"] = "ok";
        return BatteryStructs.ParseBatteryStatus(output);
    }

    private static string? QueryString(SafeFileHandle handle, uint tag, BatteryQueryInformationLevel level, Dictionary<string, string> diag) =>
        Query(handle, tag, level, 512, diag) is { } bytes ? BatteryStructs.ParseString(bytes) : null;

    private static byte[]? Query(
        SafeFileHandle handle, uint tag, BatteryQueryInformationLevel level, int outputSize,
        Dictionary<string, string> diag, bool hexDump = false)
    {
        // BATTERY_QUERY_INFORMATION { BatteryTag; InformationLevel; AtRate; }
        byte[] input = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(input, tag);
        BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(4), (int)level);
        byte[] output = new byte[outputSize];

        string key = $"IOCTL {level}";
        if (!DeviceIoControl(handle, IOCTL_BATTERY_QUERY_INFORMATION, input, input.Length, output, output.Length, out int returned, IntPtr.Zero))
        {
            diag[key] = $"unavailable ({DescribeError(Marshal.GetLastWin32Error())})";
            return null;
        }

        diag[key] = hexDump
            ? $"{returned} bytes: {Convert.ToHexString(output, 0, Math.Min(returned, 64))}"
            : $"ok ({returned} bytes)";
        return output[..returned];
    }
}
