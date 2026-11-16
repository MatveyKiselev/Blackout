using System.Text.Json.Serialization;

namespace Blackout.Core;

/// <summary>Everything the user can change. Serialized as JSON next to the user's app data.</summary>
public sealed class AppSettings
{
    /// <summary>Mirror of the real autostart registration, which stays the source of truth.</summary>
    public bool Autostart { get; set; }

    public HotkeySpec? ToggleHotkey { get; set; } = HotkeySpec.Default;

    public bool IdleBlackoutEnabled { get; set; }

    public int IdleTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// Stable monitor ids to cover. An empty list means "every monitor", which is also the
    /// sane default after adding a new display.
    /// </summary>
    public List<string> EnabledMonitorIds { get; set; } = new();

    [JsonIgnore]
    public HotkeySpec EffectiveHotkey => ToggleHotkey ?? HotkeySpec.Default;
}
