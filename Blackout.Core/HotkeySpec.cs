using System.Text;
using System.Text.Json.Serialization;

namespace Blackout.Core;

/// <summary>
/// Modifier keys for a global hotkey. Values intentionally match the Win32 MOD_* flags.
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}

/// <summary>
/// Platform neutral key identifiers. Values match the Windows virtual-key codes, which keeps
/// the Windows implementation a direct cast and loses nothing on other platforms.
/// </summary>
public enum KeyCode
{
    None = 0,

    Backspace = 0x08,
    Tab = 0x09,
    Enter = 0x0D,
    Escape = 0x1B,
    Space = 0x20,
    PageUp = 0x21,
    PageDown = 0x22,
    End = 0x23,
    Home = 0x24,
    Left = 0x25,
    Up = 0x26,
    Right = 0x27,
    Down = 0x28,
    PrintScreen = 0x2C,
    Insert = 0x2D,
    Delete = 0x2E,

    D0 = 0x30,
    D1 = 0x31,
    D2 = 0x32,
    D3 = 0x33,
    D4 = 0x34,
    D5 = 0x35,
    D6 = 0x36,
    D7 = 0x37,
    D8 = 0x38,
    D9 = 0x39,

    A = 0x41,
    B = 0x42,
    C = 0x43,
    D = 0x44,
    E = 0x45,
    F = 0x46,
    G = 0x47,
    H = 0x48,
    I = 0x49,
    J = 0x4A,
    K = 0x4B,
    L = 0x4C,
    M = 0x4D,
    N = 0x4E,
    O = 0x4F,
    P = 0x50,
    Q = 0x51,
    R = 0x52,
    S = 0x53,
    T = 0x54,
    U = 0x55,
    V = 0x56,
    W = 0x57,
    X = 0x58,
    Y = 0x59,
    Z = 0x5A,

    NumPad0 = 0x60,
    NumPad1 = 0x61,
    NumPad2 = 0x62,
    NumPad3 = 0x63,
    NumPad4 = 0x64,
    NumPad5 = 0x65,
    NumPad6 = 0x66,
    NumPad7 = 0x67,
    NumPad8 = 0x68,
    NumPad9 = 0x69,
    Multiply = 0x6A,
    Add = 0x6B,
    Subtract = 0x6D,
    Decimal = 0x6E,
    Divide = 0x6F,

    F1 = 0x70,
    F2 = 0x71,
    F3 = 0x72,
    F4 = 0x73,
    F5 = 0x74,
    F6 = 0x75,
    F7 = 0x76,
    F8 = 0x77,
    F9 = 0x78,
    F10 = 0x79,
    F11 = 0x7A,
    F12 = 0x7B,
    F13 = 0x7C,
    F14 = 0x7D,
    F15 = 0x7E,
    F16 = 0x7F,
    F17 = 0x80,
    F18 = 0x81,
    F19 = 0x82,
    F20 = 0x83,
    F21 = 0x84,
    F22 = 0x85,
    F23 = 0x86,
    F24 = 0x87,

    NumLock = 0x90,
    ScrollLock = 0x91,
    Semicolon = 0xBA,
    Plus = 0xBB,
    Comma = 0xBC,
    Minus = 0xBD,
    Period = 0xBE,
    Slash = 0xBF,
    Tilde = 0xC0,
    OpenBracket = 0xDB,
    Backslash = 0xDC,
    CloseBracket = 0xDD,
    Quote = 0xDE,
}

/// <summary>A global hotkey: a set of modifiers plus one non-modifier key.</summary>
public sealed record HotkeySpec(HotkeyModifiers Modifiers, KeyCode Key)
{
    public static HotkeySpec Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, KeyCode.B);

    /// <summary>
    /// A valid hotkey needs a key, and either a modifier or a function key so that plain
    /// letters are never hijacked system wide.
    /// </summary>
    [JsonIgnore]
    public bool IsValid =>
        Key != KeyCode.None &&
        (Modifiers != HotkeyModifiers.None || (Key >= KeyCode.F1 && Key <= KeyCode.F24));

    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (Key == KeyCode.None)
            {
                return "не задана";
            }

            var builder = new StringBuilder();
            if (Modifiers.HasFlag(HotkeyModifiers.Control))
            {
                builder.Append("Ctrl+");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Alt))
            {
                builder.Append("Alt+");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Shift))
            {
                builder.Append("Shift+");
            }

            if (Modifiers.HasFlag(HotkeyModifiers.Win))
            {
                builder.Append("Win+");
            }

            builder.Append(KeyName(Key));
            return builder.ToString();
        }
    }

    private static string KeyName(KeyCode key) => key switch
    {
        >= KeyCode.A and <= KeyCode.Z => ((char)('A' + (int)key - (int)KeyCode.A)).ToString(),
        >= KeyCode.D0 and <= KeyCode.D9 => ((char)('0' + (int)key - (int)KeyCode.D0)).ToString(),
        >= KeyCode.NumPad0 and <= KeyCode.NumPad9 => $"Num{(int)key - (int)KeyCode.NumPad0}",
        >= KeyCode.F1 and <= KeyCode.F24 => $"F{(int)key - (int)KeyCode.F1 + 1}",
        KeyCode.Escape => "Esc",
        KeyCode.Space => "Space",
        KeyCode.Enter => "Enter",
        KeyCode.Backspace => "Backspace",
        KeyCode.PageUp => "PageUp",
        KeyCode.PageDown => "PageDown",
        KeyCode.PrintScreen => "PrintScreen",
        KeyCode.NumLock => "NumLock",
        KeyCode.ScrollLock => "ScrollLock",
        KeyCode.Semicolon => ";",
        KeyCode.Plus => "+",
        KeyCode.Comma => ",",
        KeyCode.Minus => "-",
        KeyCode.Period => ".",
        KeyCode.Slash => "/",
        KeyCode.Tilde => "~",
        KeyCode.OpenBracket => "[",
        KeyCode.Backslash => "\\",
        KeyCode.CloseBracket => "]",
        KeyCode.Quote => "'",
        _ => key.ToString(),
    };
}
