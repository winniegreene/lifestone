using Lifestone.Client.RetailUi.Rendering;

namespace Lifestone.Client.RetailUi.Radar;

/// <summary>
/// The radar's colours (<c>RGBAColor_Radar*</c>). The ten palette entries are the static floats at 11.6096 0x0081C69C onward, one 16-byte RGBA each (read from the user's
/// <c>acclient.exe</c> with D55, docs/research/retail-addresses-m2.md section 1.6, matching the 11.4186 decompile): blue (0.25, 0.66, 1), gold (1, 0.67, 0), yellow (1, 1, 0.5), white,
/// red (1, 0.25, 0.39), purple (0.75, 0.39, 1), pink (1, 0.66, 0.75), green (0, 0.5, 0.25), cyan (0, 1, 1), bright green (0, 1, 0). The named roles are copies assigned at start-up
/// (the decompile's research: default white, admin cyan, creature gold, portal purple, vendor yellow, player killer red, PK-Lite pink, the fellowship bright green); their addresses
/// were not read, the roles are the research's.
/// </summary>
public static class RadarPalette
{
    /// <summary>Blue (0.25, 0.66, 1): server blip colour 1; the life stone's.</summary>
    public static UiRgbaColor Blue { get; } = new(0.25f, 0.66f, 1f, 1f);

    /// <summary>Gold (1, 0.67, 0): server blip colour 2; creatures and free-PK players.</summary>
    public static UiRgbaColor Gold { get; } = new(1f, 0.67f, 0f, 1f);

    /// <summary>White: server blip colour 3; the default.</summary>
    public static UiRgbaColor White { get; } = new(1f, 1f, 1f, 1f);

    /// <summary>Purple (0.75, 0.39, 1): server blip colour 4; portals.</summary>
    public static UiRgbaColor Purple { get; } = new(0.75f, 0.39f, 1f, 1f);

    /// <summary>Red (1, 0.25, 0.39): server blip colour 5; player killers.</summary>
    public static UiRgbaColor Red { get; } = new(1f, 0.25f, 0.39f, 1f);

    /// <summary>Pink (1, 0.66, 0.75): server blip colour 6; PK-Lite players.</summary>
    public static UiRgbaColor Pink { get; } = new(1f, 0.66f, 0.75f, 1f);

    /// <summary>Green (0, 0.5, 0.25): server blip colour 7.</summary>
    public static UiRgbaColor Green { get; } = new(0f, 0.5f, 0.25f, 1f);

    /// <summary>Yellow (1, 1, 0.5): server blip colour 8; vendors and non-player characters.</summary>
    public static UiRgbaColor Yellow { get; } = new(1f, 1f, 0.5f, 1f);

    /// <summary>Cyan (0, 1, 1): server blip colour 9; admins.</summary>
    public static UiRgbaColor Cyan { get; } = new(0f, 1f, 1f, 1f);

    /// <summary>Bright green (0, 1, 0): server blip colour 10; the fellowship, and the player's own marker.</summary>
    public static UiRgbaColor BrightGreen { get; } = new(0f, 1f, 0f, 1f);

    /// <summary>The default (<c>RGBAColor_RadarDefault</c>): white.</summary>
    public static UiRgbaColor Default => White;

    /// <summary>An admin (<c>RGBAColor_RadarAdmin</c>): cyan.</summary>
    public static UiRgbaColor Admin => Cyan;

    /// <summary>A creature you can attack (<c>RGBAColor_RadarCreature</c>): gold.</summary>
    public static UiRgbaColor Creature => Gold;

    /// <summary>A portal (<c>RGBAColor_RadarPortal</c>): purple.</summary>
    public static UiRgbaColor Portal => Purple;

    /// <summary>A vendor (<c>RGBAColor_RadarVendor</c>): yellow.</summary>
    public static UiRgbaColor Vendor => Yellow;

    /// <summary>A player killer (<c>RGBAColor_RadarPlayerKiller</c>): red.</summary>
    public static UiRgbaColor PlayerKiller => Red;

    /// <summary>A PK-Lite player (<c>RGBAColor_RadarPKLite</c>): pink.</summary>
    public static UiRgbaColor PkLite => Pink;

    /// <summary>A fellowship's member (<c>RGBAColor_RadarFellowship</c>): bright green.</summary>
    public static UiRgbaColor Fellowship => BrightGreen;

    /// <summary>A fellowship's leader (<c>RGBAColor_RadarFellowshipLeader</c>): bright green.</summary>
    public static UiRgbaColor FellowshipLeader => BrightGreen;

    /// <summary>The colour of server blip colour <paramref name="value"/> (<c>GetBlipColor</c>'s switch): 1 to 10 in the order above; any other value is the default.</summary>
    public static UiRgbaColor ByServerColor(byte value) => value switch
    {
        1 => Blue,
        2 => Gold,
        3 => White,
        4 => Purple,
        5 => Red,
        6 => Pink,
        7 => Green,
        8 => Yellow,
        9 => Cyan,
        10 => BrightGreen,
        _ => Default,
    };
}
