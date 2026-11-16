namespace Blackout.Core;

/// <summary>
/// One physical output as the platform sees it.
/// <para>
/// <see cref="Id"/> is the identity that survives a restart (on Windows it is the monitor
/// interface path). <see cref="DeviceName"/> is the volatile system name such as \\.\DISPLAY1
/// and is only used as a fallback when the stable id disappears.
/// </para>
/// </summary>
public sealed record MonitorInfo(
    string Id,
    string Name,
    string DeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    bool IsPrimary)
{
    public string DisplayName =>
        $"{Name} ({DeviceName.Replace(@"\\.\", string.Empty)}, {Width}x{Height}{(IsPrimary ? ", основной" : string.Empty)})";
}
