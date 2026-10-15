using System.Numerics;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Data.Physics;
using Lifestone.Data.World;

namespace Lifestone.Client.RetailUi.Radar;

/// <summary>The shapes a radar blip has (<c>RadarBlipShape</c>; <c>gmRadarUI::DrawBlip</c>, 11.6096 0x004D9690).</summary>
public enum RadarBlipShape
{
    /// <summary>No blip (an object that is hidden from the UI).</summary>
    Undefined = 0,

    /// <summary>One pixel (<c>DrawPoint</c> 0x004D8890). <c>GetBlipShape</c> never returns it.</summary>
    Point = 1,

    /// <summary>The eight pixels around the point, the point itself empty (<c>DrawHollow</c> 0x004D9090): allegiance members.</summary>
    Hollow = 2,

    /// <summary>The point and its four corners (<c>DrawX</c> 0x004D8A90): both sides player killers (or both PK-Lite).</summary>
    X = 3,

    /// <summary>The point and its four edges (<c>DrawCross</c> 0x004D8A40): everything else.</summary>
    Cross = 4,

    /// <summary>A pixel with three under it (<c>DrawTriangle</c> 0x004D8AE0): a fellowship's leader.</summary>
    Triangle = 5,

    /// <summary>A pixel with three over it (<c>DrawInvertedTriangle</c> 0x004D8B90): a fellowship's member.</summary>
    InvertedTriangle = 6,

    /// <summary>The hollow shape with four more corners two pixels out (<c>DrawXBox</c> 0x004D90D0). <c>GetBlipShape</c> never returns it.</summary>
    XBox = 7,
}

/// <summary>
/// The radar's relations between players: a fellowship and an allegiance. M2 has neither (the session keeps no fellowship or allegiance), so <see cref="None"/> answers no to all;
/// the rules take it so a later milestone supplies the real ones without changing them.
/// </summary>
public interface IRadarRelations
{
    /// <summary>True when <paramref name="guid"/> is in the player's fellowship (<c>ClientFellowshipSystem::IsFellow</c>).</summary>
    bool IsFellow(uint guid);

    /// <summary>True when <paramref name="guid"/> leads the player's fellowship (<c>ClientFellowshipSystem::IsFellowshipLeader</c>).</summary>
    bool IsFellowshipLeader(uint guid);

    /// <summary>True when <paramref name="guid"/> is in the player's allegiance (<c>ACCWeenieObject::IsAllegianceMember</c>).</summary>
    bool IsAllegianceMember(uint guid);

    /// <summary>No fellowship and no allegiance (M2).</summary>
    public static IRadarRelations None { get; } = new NoRelations();

    private sealed class NoRelations : IRadarRelations
    {
        public bool IsFellow(uint guid) => false;

        public bool IsFellowshipLeader(uint guid) => false;

        public bool IsAllegianceMember(uint guid) => false;
    }
}

/// <summary>
/// Retail's radar rules over an object's description (<see cref="RadarObjectDesc"/>, the weenie description's radar fields) (docs/retail-ui.md section 21.4; the 11.4186 decompile read, addresses 11.6096): whether an object is on the radar
/// (<c>ACCWeenieObject::InqShowableOnRadar</c> 0x0058D080 and <c>gmRadarUI::AddObject</c> 0x004DA660), its shape (<c>GetBlipShape</c> 0x004D87C0), its colour (<c>GetBlipColor</c> 0x004D8350), the
/// range (<c>CPlayerSystem::GetRadarRadius</c> 0x0055E5A0) and the intensity and the pixel of a drawn blip (<c>DrawObjects</c> 0x004D9FE0). The object description bits are the PublicWeenieDesc
/// ones (ACE names them the same).
/// </summary>
public static class RadarRules
{
    /// <summary>The range outdoors, in metres (<c>CPlayerSystem::GetRadarRadius</c> 0x0055E5A0).</summary>
    public const double OutdoorRange = 75.0;

    /// <summary>The range indoors, in metres.</summary>
    public const double IndoorRange = 25.0;

    /// <summary>The metres either side of the player's height within which a blip is bright (<c>DrawObjects</c>: the vertical difference under 5).</summary>
    public const float BrightHeight = 5f;

    /// <summary>The intensity of a bright blip.</summary>
    public const float BrightIntensity = 1.0f;

    /// <summary>The intensity of a blip above or below the player (the constant 0.64999998 in <c>DrawObjects</c>).</summary>
    public const float DimIntensity = 0.65f;

    /// <summary>The object description bit of a player.</summary>
    public const uint PlayerBit = 0x8;

    /// <summary>The bit of an attackable object.</summary>
    public const uint AttackableBit = 0x10;

    /// <summary>The bit of a player killer.</summary>
    public const uint PlayerKillerBit = 0x20;

    /// <summary>The bit of an admin the radar does not reveal (hidden admin).</summary>
    public const uint HiddenAdminBit = 0x40;

    /// <summary>The bit of an object hidden from the UI (it has no blip at all).</summary>
    public const uint UiHiddenBit = 0x80;

    /// <summary>The bit of a vendor.</summary>
    public const uint VendorBit = 0x200;

    /// <summary>The bit of a portal.</summary>
    public const uint PortalBit = 0x40000;

    /// <summary>The bit of an admin.</summary>
    public const uint AdminBit = 0x100000;

    /// <summary>The bit of a free-PK player (<c>GetBlipColor</c> paints it with the creature colour).</summary>
    public const uint FreePkBit = 0x200000;

    /// <summary>The bit of a PK-Lite player.</summary>
    public const uint PkLiteBit = 0x2000000;

    /// <summary>The ItemType of a creature.</summary>
    public const uint CreatureItemType = 0x10;

    /// <summary>The range for a player outdoors or indoors (<c>CPlayerSystem::GetRadarRadius</c>; <c>SmartBox::is_player_outside</c> is the player's cell being a land cell).</summary>
    public static double RangeFor(in CellFrame player) => player.IsOutdoor ? OutdoorRange : IndoorRange;

    /// <summary>
    /// <c>ACCWeenieObject::InqShowableOnRadar</c> (0x0058D080): the object has a physics object and its radar behaviour (header field, <c>_radar_enum</c>) is 2 (show on movement),
    /// 3 (show when attacking) or 4 (show always). An object whose description sent no behaviour has 0 and is never shown. (Retail's "movement" and "attacking" conditions are not applied
    /// by this check; 11.4186's <c>gmRadarUI::AddObject</c> shows all three the same.)
    /// </summary>
    public static bool IsShowable(RadarObjectDesc weenie)
    {
        return weenie.Behavior is 2 or 3 or 4;
    }

    /// <summary>True when the object's description hides it from the UI (<c>AddObject</c> adds none of these).</summary>
    public static bool IsUiHidden(RadarObjectDesc weenie)
    {
        return (weenie.Flags & UiHiddenBit) != 0;
    }

    /// <summary>
    /// <c>gmRadarUI::GetBlipShape</c> (0x004D87C0): none for an object hidden from the UI; a fellowship member is an inverted triangle and the leader a triangle; an allegiance member is
    /// hollow; both sides player killers (or both PK-Lite) is an X; anything else is a cross. <paramref name="self"/> is the player's own description (null when the player has none,
    /// which gives the cross).
    /// </summary>
    public static RadarBlipShape ShapeOf(uint guid, RadarObjectDesc weenie, RadarObjectDesc? self, IRadarRelations relations)
    {
        ArgumentNullException.ThrowIfNull(relations);
        if (IsUiHidden(weenie))
        {
            return RadarBlipShape.Undefined;
        }

        if (relations.IsFellow(guid))
        {
            return relations.IsFellowshipLeader(guid) ? RadarBlipShape.Triangle : RadarBlipShape.InvertedTriangle;
        }

        if (self is null)
        {
            return RadarBlipShape.Cross;
        }

        if (relations.IsAllegianceMember(guid))
        {
            return RadarBlipShape.Hollow;
        }

        var theirs = weenie.Flags;
        var ours = self.Value.Flags;
        if (((theirs & PlayerKillerBit) != 0 && (ours & PlayerKillerBit) != 0) || ((theirs & PkLiteBit) != 0 && (ours & PkLiteBit) != 0))
        {
            return RadarBlipShape.X;
        }

        return RadarBlipShape.Cross;
    }

    /// <summary>
    /// <c>gmRadarUI::GetBlipColor</c> (0x004D8350): a hidden object is the default (white); a server blip colour 1 to 10 picks from the palette (any other non-zero value is the default); else a
    /// portal is purple, a vendor yellow, an attackable creature that is not a player gold, and any other non-player the default; a player is the default, cyan for an admin (unless a hidden
    /// admin), red for a player killer, pink for a PK-Lite, gold for a free-PK; and a fellowship's leader and members are bright green over all that. The creature test reads the ItemType
    /// (retail calls a virtual of <c>ACCWeenieObject</c>, slot 11, whose body is not read: <c>TODO(g4-radar-creature-slot)</c>).
    /// </summary>
    public static UiRgbaColor ColorOf(uint guid, RadarObjectDesc weenie, IRadarRelations relations)
    {
        ArgumentNullException.ThrowIfNull(relations);
        var flags = weenie.Flags;
        if ((flags & UiHiddenBit) != 0)
        {
            return RadarPalette.Default;
        }

        if (weenie.BlipColor is { } byteColor && byteColor != 0)
        {
            return RadarPalette.ByServerColor(byteColor);
        }

        if ((flags & PortalBit) != 0)
        {
            return RadarPalette.Portal;
        }

        if ((flags & VendorBit) != 0)
        {
            return RadarPalette.Vendor;
        }

        var isPlayer = (flags & PlayerBit) != 0;
        if ((flags & AttackableBit) != 0 && (weenie.ItemType & CreatureItemType) != 0 && !isPlayer)
        {
            return RadarPalette.Creature;
        }

        if (!isPlayer)
        {
            return RadarPalette.Default;
        }

        var color = RadarPalette.Default;
        if ((flags & AdminBit) == 0 || (flags & HiddenAdminBit) != 0)
        {
            if ((flags & PlayerKillerBit) != 0)
            {
                color = RadarPalette.PlayerKiller;
            }
            else if ((flags & PkLiteBit) != 0)
            {
                color = RadarPalette.PkLite;
            }
            else if ((flags & FreePkBit) != 0)
            {
                color = RadarPalette.Creature;
            }
        }
        else
        {
            color = RadarPalette.Admin;
        }

        if (relations.IsFellowshipLeader(guid))
        {
            return RadarPalette.FellowshipLeader;
        }

        return relations.IsFellow(guid) ? RadarPalette.Fellowship : color;
    }

    /// <summary>The intensity of a blip whose object is <paramref name="verticalOffset"/> metres above or below the player (<c>DrawObjects</c>): 1.0 under 5, else 0.65 (a float).</summary>
    public static float IntensityOf(float verticalOffset) => Math.Abs(verticalOffset) < BrightHeight ? BrightIntensity : DimIntensity;

    /// <summary>The colour a blip is drawn in (<c>DrawBlip</c> 0x004D9690): red, green and blue times the intensity as floats, alpha unchanged.</summary>
    public static UiRgbaColor Scaled(UiRgbaColor color, float intensity) => new(color.R * intensity, color.G * intensity, color.B * intensity, color.A);

    /// <summary>
    /// <c>SmartBox::convert_to_player_space</c> (0x00452DE0): the object's position in the player's frame (x right, y forward, z up), across landblocks
    /// (<c>Position::localtolocal</c>).
    /// </summary>
    public static Vector3 ToPlayerSpace(in CellFrame player, in CellFrame obj) => player.LocalToLocal(obj, Vector3.Zero);

    /// <summary>
    /// The pixel of a blip in the radar's own coordinates (<c>DrawObjects</c>): <c>false</c> when the object is farther than the range less one metre (a squared test) or lands off the radar's
    /// pixel square; otherwise x = (int)(x * radius / range + centre x) and y = (int)(centre y - y * radius / range).
    /// </summary>
    /// <param name="playerSpace">The object in the player's space.</param>
    /// <param name="range">The range in metres.</param>
    /// <param name="radius">The radar's radius in pixels (property 0x1000002D).</param>
    /// <param name="centreX">The centre's x (property 0x1000002F in the centre struct).</param>
    /// <param name="centreY">The centre's y.</param>
    /// <param name="x">The blip's x.</param>
    /// <param name="y">The blip's y.</param>
    public static bool TryPlace(Vector3 playerSpace, double range, int radius, double centreX, double centreY, out int x, out int y)
    {
        x = 0;
        y = 0;
        var limit = range - 1.0;
        var squared = limit * limit;
        double px = playerSpace.X;
        double py = playerSpace.Y;
        if ((py * py) + (px * px) >= squared)
        {
            return false;
        }

        var scale = radius / range;
        var fx = (px * scale) + centreX;
        var fy = centreY - (scale * py);
        x = (int)fx;
        y = (int)fy;
        if (x < centreX - radius || x > radius + centreX || y < centreY - radius || y > radius + centreY)
        {
            return false;
        }

        return true;
    }
}
