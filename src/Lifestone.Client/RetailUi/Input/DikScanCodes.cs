namespace Lifestone.Client.RetailUi.Input;

/// <summary>
/// DirectInput keyboard scan codes (DIK_*, dinput.h): the "semantic" of a retail keyboard control
/// (<see cref="Lifestone.Data.Ui.RetailControl.Semantic"/>). Extended keys carry 0x80 (the arrows are 0xC8-0xD0, the
/// right Ctrl and Alt 0x9D and 0xB8), as <c>CInputManager_WIN32::GenerateKeyboardEvent</c> (11.4186 0x00689340, 11.6096
/// 0x0068A3D0) builds them from a window message's scan code and extended flag.
/// </summary>
public static class DikScanCodes
{
#pragma warning disable CS1591 // The DIK names are their own documentation.
    public const int Escape = 0x01;
    public const int D1 = 0x02;
    public const int D2 = 0x03;
    public const int D3 = 0x04;
    public const int D4 = 0x05;
    public const int D5 = 0x06;
    public const int D6 = 0x07;
    public const int D7 = 0x08;
    public const int D8 = 0x09;
    public const int D9 = 0x0A;
    public const int D0 = 0x0B;
    public const int Minus = 0x0C;
    public const int EqualsSign = 0x0D;
    public const int Back = 0x0E;
    public const int Tab = 0x0F;
    public const int Q = 0x10;
    public const int W = 0x11;
    public const int E = 0x12;
    public const int R = 0x13;
    public const int T = 0x14;
    public const int Y = 0x15;
    public const int U = 0x16;
    public const int I = 0x17;
    public const int O = 0x18;
    public const int P = 0x19;
    public const int LBracket = 0x1A;
    public const int RBracket = 0x1B;
    public const int Return = 0x1C;
    public const int LControl = 0x1D;
    public const int A = 0x1E;
    public const int S = 0x1F;
    public const int D = 0x20;
    public const int F = 0x21;
    public const int G = 0x22;
    public const int H = 0x23;
    public const int J = 0x24;
    public const int K = 0x25;
    public const int L = 0x26;
    public const int Semicolon = 0x27;
    public const int Apostrophe = 0x28;
    public const int Grave = 0x29;
    public const int LShift = 0x2A;
    public const int Backslash = 0x2B;
    public const int Z = 0x2C;
    public const int X = 0x2D;
    public const int C = 0x2E;
    public const int V = 0x2F;
    public const int B = 0x30;
    public const int N = 0x31;
    public const int M = 0x32;
    public const int Comma = 0x33;
    public const int Period = 0x34;
    public const int Slash = 0x35;
    public const int RShift = 0x36;
    public const int Multiply = 0x37;
    public const int LMenu = 0x38;
    public const int Space = 0x39;
    public const int Capital = 0x3A;
    public const int F1 = 0x3B;
    public const int F2 = 0x3C;
    public const int F3 = 0x3D;
    public const int F4 = 0x3E;
    public const int F5 = 0x3F;
    public const int F6 = 0x40;
    public const int F7 = 0x41;
    public const int F8 = 0x42;
    public const int F9 = 0x43;
    public const int F10 = 0x44;
    public const int NumLock = 0x45;
    public const int Scroll = 0x46;
    public const int NumPad7 = 0x47;
    public const int NumPad8 = 0x48;
    public const int NumPad9 = 0x49;
    public const int Subtract = 0x4A;
    public const int NumPad4 = 0x4B;
    public const int NumPad5 = 0x4C;
    public const int NumPad6 = 0x4D;
    public const int Add = 0x4E;
    public const int NumPad1 = 0x4F;
    public const int NumPad2 = 0x50;
    public const int NumPad3 = 0x51;
    public const int NumPad0 = 0x52;
    public const int Decimal = 0x53;
    public const int Oem102 = 0x56;
    public const int F11 = 0x57;
    public const int F12 = 0x58;
    public const int NumPadEnter = 0x9C;
    public const int RControl = 0x9D;
    public const int Divide = 0xB5;
    public const int SysRq = 0xB7;
    public const int RMenu = 0xB8;
    public const int Pause = 0xC5;
    public const int Home = 0xC7;
    public const int Up = 0xC8;
    public const int Prior = 0xC9;
    public const int Left = 0xCB;
    public const int Right = 0xCD;
    public const int End = 0xCF;
    public const int Down = 0xD0;
    public const int Next = 0xD1;
    public const int Insert = 0xD2;
    public const int Delete = 0xD3;
    public const int LWin = 0xDB;
    public const int RWin = 0xDC;
    public const int Apps = 0xDD;
#pragma warning restore CS1591

    /// <summary>
    /// The scan code retail looks bindings up with: <c>GenerateKeyboardEvent</c> treats the right Shift as the left one
    /// (54 becomes 42), so every Shift binding and the Shift meta key work from either side.
    /// </summary>
    public static int Canonical(int dik) => dik == RShift ? LShift : dik;
}

/// <summary>Which of two same-named keys a host key event came from (Godot's <c>KeyLocation</c>, as a plain int).</summary>
public enum UiKeyLocation
{
    /// <summary>0: the key has no left or right variant, or the host cannot tell.</summary>
    Unspecified = 0,

    /// <summary>1: the left one.</summary>
    Left = 1,

    /// <summary>2: the right one.</summary>
    Right = 2,
}

/// <summary>
/// Godot physical key codes (<c>InputEventKey.physical_keycode</c>, the US QWERTY position of the key) to DIK scan codes,
/// as plain ints so that Lifestone.Client never names Godot. A physical code is the key's position, not its character,
/// which is what a scan code is too, so every layout binds the same physical keys as retail did.
/// </summary>
/// <remarks>
/// Godot 4's <c>Key</c> values: printable keys are their US ASCII upper-case codes (0x20-0x7E); the rest are
/// <c>KEY_SPECIAL</c> (0x400000) plus a small index. Modifiers have one code for both sides; <see cref="UiKeyLocation"/>
/// picks the side (left when unspecified). Keys retail had no scan code for map to 0 (unbound).
/// </remarks>
public static class GodotKeyCodes
{
    private const int Special = 0x400000;

    private static readonly Dictionary<int, int> Printable = new()
    {
        [0x20] = DikScanCodes.Space,
        [0x27] = DikScanCodes.Apostrophe,
        [0x2C] = DikScanCodes.Comma,
        [0x2D] = DikScanCodes.Minus,
        [0x2E] = DikScanCodes.Period,
        [0x2F] = DikScanCodes.Slash,
        [0x30] = DikScanCodes.D0,
        [0x31] = DikScanCodes.D1,
        [0x32] = DikScanCodes.D2,
        [0x33] = DikScanCodes.D3,
        [0x34] = DikScanCodes.D4,
        [0x35] = DikScanCodes.D5,
        [0x36] = DikScanCodes.D6,
        [0x37] = DikScanCodes.D7,
        [0x38] = DikScanCodes.D8,
        [0x39] = DikScanCodes.D9,
        [0x3B] = DikScanCodes.Semicolon,
        [0x3D] = DikScanCodes.EqualsSign,
        [0x41] = DikScanCodes.A,
        [0x42] = DikScanCodes.B,
        [0x43] = DikScanCodes.C,
        [0x44] = DikScanCodes.D,
        [0x45] = DikScanCodes.E,
        [0x46] = DikScanCodes.F,
        [0x47] = DikScanCodes.G,
        [0x48] = DikScanCodes.H,
        [0x49] = DikScanCodes.I,
        [0x4A] = DikScanCodes.J,
        [0x4B] = DikScanCodes.K,
        [0x4C] = DikScanCodes.L,
        [0x4D] = DikScanCodes.M,
        [0x4E] = DikScanCodes.N,
        [0x4F] = DikScanCodes.O,
        [0x50] = DikScanCodes.P,
        [0x51] = DikScanCodes.Q,
        [0x52] = DikScanCodes.R,
        [0x53] = DikScanCodes.S,
        [0x54] = DikScanCodes.T,
        [0x55] = DikScanCodes.U,
        [0x56] = DikScanCodes.V,
        [0x57] = DikScanCodes.W,
        [0x58] = DikScanCodes.X,
        [0x59] = DikScanCodes.Y,
        [0x5A] = DikScanCodes.Z,
        [0x5B] = DikScanCodes.LBracket,
        [0x5C] = DikScanCodes.Backslash,
        [0x5D] = DikScanCodes.RBracket,
        [0x60] = DikScanCodes.Grave,
    };

    private static readonly Dictionary<int, int> Specials = new()
    {
        [Special | 0x01] = DikScanCodes.Escape,
        [Special | 0x02] = DikScanCodes.Tab,
        [Special | 0x04] = DikScanCodes.Back,
        [Special | 0x05] = DikScanCodes.Return,
        [Special | 0x06] = DikScanCodes.NumPadEnter,
        [Special | 0x07] = DikScanCodes.Insert,
        [Special | 0x08] = DikScanCodes.Delete,
        [Special | 0x09] = DikScanCodes.Pause,
        [Special | 0x0A] = DikScanCodes.SysRq,
        [Special | 0x0B] = DikScanCodes.SysRq,
        [Special | 0x0D] = DikScanCodes.Home,
        [Special | 0x0E] = DikScanCodes.End,
        [Special | 0x0F] = DikScanCodes.Left,
        [Special | 0x10] = DikScanCodes.Up,
        [Special | 0x11] = DikScanCodes.Right,
        [Special | 0x12] = DikScanCodes.Down,
        [Special | 0x13] = DikScanCodes.Prior,
        [Special | 0x14] = DikScanCodes.Next,
        [Special | 0x19] = DikScanCodes.Capital,
        [Special | 0x1A] = DikScanCodes.NumLock,
        [Special | 0x1B] = DikScanCodes.Scroll,
        [Special | 0x1C] = DikScanCodes.F1,
        [Special | 0x1D] = DikScanCodes.F2,
        [Special | 0x1E] = DikScanCodes.F3,
        [Special | 0x1F] = DikScanCodes.F4,
        [Special | 0x20] = DikScanCodes.F5,
        [Special | 0x21] = DikScanCodes.F6,
        [Special | 0x22] = DikScanCodes.F7,
        [Special | 0x23] = DikScanCodes.F8,
        [Special | 0x24] = DikScanCodes.F9,
        [Special | 0x25] = DikScanCodes.F10,
        [Special | 0x26] = DikScanCodes.F11,
        [Special | 0x27] = DikScanCodes.F12,
        [Special | 0x42] = DikScanCodes.Apps,
        [Special | 0x81] = DikScanCodes.Multiply,
        [Special | 0x82] = DikScanCodes.Divide,
        [Special | 0x83] = DikScanCodes.Subtract,
        [Special | 0x84] = DikScanCodes.Decimal,
        [Special | 0x85] = DikScanCodes.Add,
        [Special | 0x86] = DikScanCodes.NumPad0,
        [Special | 0x87] = DikScanCodes.NumPad1,
        [Special | 0x88] = DikScanCodes.NumPad2,
        [Special | 0x89] = DikScanCodes.NumPad3,
        [Special | 0x8A] = DikScanCodes.NumPad4,
        [Special | 0x8B] = DikScanCodes.NumPad5,
        [Special | 0x8C] = DikScanCodes.NumPad6,
        [Special | 0x8D] = DikScanCodes.NumPad7,
        [Special | 0x8E] = DikScanCodes.NumPad8,
        [Special | 0x8F] = DikScanCodes.NumPad9,
    };

    /// <summary>Godot's <c>KEY_SHIFT</c>.</summary>
    public const int Shift = Special | 0x15;

    /// <summary>Godot's <c>KEY_CTRL</c>.</summary>
    public const int Ctrl = Special | 0x16;

    /// <summary>Godot's <c>KEY_META</c> (the Windows key).</summary>
    public const int Meta = Special | 0x17;

    /// <summary>Godot's <c>KEY_ALT</c>.</summary>
    public const int Alt = Special | 0x18;

    /// <summary>
    /// The DIK scan code of Godot physical key <paramref name="physicalKeycode"/> at <paramref name="location"/>, or 0 when
    /// retail has none (the key is then unbound and the router does not take it).
    /// </summary>
    public static int ToDik(int physicalKeycode, UiKeyLocation location = UiKeyLocation.Unspecified)
    {
        var right = location == UiKeyLocation.Right;
        switch (physicalKeycode)
        {
            case Shift:
                return right ? DikScanCodes.RShift : DikScanCodes.LShift;
            case Ctrl:
                return right ? DikScanCodes.RControl : DikScanCodes.LControl;
            case Alt:
                return right ? DikScanCodes.RMenu : DikScanCodes.LMenu;
            case Meta:
                return right ? DikScanCodes.RWin : DikScanCodes.LWin;
        }

        if (Printable.TryGetValue(physicalKeycode, out var dik) || Specials.TryGetValue(physicalKeycode, out dik))
        {
            return dik;
        }

        // Godot reports a letter's code in lower case in some paths; the physical code is upper case, so fold it.
        return physicalKeycode is >= 'a' and <= 'z' && Printable.TryGetValue(physicalKeycode - 0x20, out dik) ? dik : 0;
    }

    /// <summary>Every Godot physical key code the table maps (for the host's binding check).</summary>
    public static IEnumerable<int> MappedKeycodes => Printable.Keys.Concat(Specials.Keys).Concat([Shift, Ctrl, Alt, Meta]);
}
