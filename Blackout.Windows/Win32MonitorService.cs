using System.Runtime.InteropServices;
using Blackout.Core;
using Blackout.Platforms.Windows.Interop;

namespace Blackout.Platforms.Windows;

/// <summary>Reads the display topology with EnumDisplayMonitors.</summary>
public sealed class Win32MonitorService : IMonitorService
{
    public IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();

        NativeMethods.EnumDisplayMonitors(0, 0, (hMonitor, _, _, _) =>
        {
            var info = new NativeMethods.MONITORINFOEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.MONITORINFOEX>(),
            };

            if (!NativeMethods.GetMonitorInfoW(hMonitor, ref info))
            {
                return true;
            }

            var deviceName = info.szDevice;
            var (id, name) = QueryDevice(deviceName);

            monitors.Add(new MonitorInfo(
                Id: string.IsNullOrWhiteSpace(id) ? deviceName : id,
                Name: string.IsNullOrWhiteSpace(name) ? deviceName : name,
                DeviceName: deviceName,
                Left: info.rcMonitor.Left,
                Top: info.rcMonitor.Top,
                Width: info.rcMonitor.Right - info.rcMonitor.Left,
                Height: info.rcMonitor.Bottom - info.rcMonitor.Top,
                IsPrimary: (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));

            return true;
        }, 0);

        return monitors
            .OrderBy(m => m.Left)
            .ThenBy(m => m.Top)
            .ToList();
    }

    /// <summary>
    /// The stable id is the monitor interface path ("\\?\DISPLAY#..."), which survives a reboot
    /// but changes when the cable moves to another port. DeviceString is the friendly model name.
    /// </summary>
    private static (string Id, string Name) QueryDevice(string deviceName)
    {
        var device = new NativeMethods.DISPLAY_DEVICE
        {
            cb = (uint)Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>(),
        };

        if (!NativeMethods.EnumDisplayDevicesW(deviceName, 0, ref device, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME))
        {
            return (string.Empty, string.Empty);
        }

        return (device.DeviceID, device.DeviceString);
    }
}
