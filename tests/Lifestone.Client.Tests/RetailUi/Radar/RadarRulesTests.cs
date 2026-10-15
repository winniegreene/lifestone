using System.Numerics;
using Lifestone.Client.RetailUi.Radar;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.RetailUi.Radar;

/// <summary>
/// The radar's rules (docs/retail-ui.md section 21.4; <c>gmRadarUI</c>, <c>CPlayerSystem::GetRadarRadius</c>): the range, where a blip lands, the shape and the colour an object
/// gets from its weenie description, the intensity, and the shapes' pixels. No dats.
/// </summary>
public sealed class RadarRulesTests
{
    private const uint Outdoor = 0xA9B40001;
    private const uint Indoor = 0x860201AD;

    private static CellFrame At(uint cell, float x, float y, float z, double headingDegrees = 0) =>
        new(cell, new AcFrame(new Vector3(x, y, z), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(-headingDegrees * Math.PI / 180.0))));

    private static RadarObjectDesc Weenie(uint flags = 0, uint itemType = 0x10, byte? blip = null, byte? behavior = 4) => new(flags, itemType, blip, behavior);

    private sealed class Relations(HashSet<uint> fellows, uint leader = 0, HashSet<uint>? allegiance = null) : IRadarRelations
    {
        public bool IsFellow(uint guid) => fellows.Contains(guid);

        public bool IsFellowshipLeader(uint guid) => guid == leader;

        public bool IsAllegianceMember(uint guid) => allegiance?.Contains(guid) ?? false;
    }

    // ------------------------------------------------------------------ range

    [Fact]
    public void The_range_is_75_metres_outdoors_and_25_indoors_and_the_update_interval_is_retails()
    {
        Assert.Equal(75.0, RadarRules.RangeFor(At(Outdoor, 0, 0, 0)));
        Assert.Equal(25.0, RadarRules.RangeFor(At(Indoor, 0, 0, 0)));
        Assert.Equal(0.025, GameRadar.UpdateInterval);
    }

    [Theory]
    [InlineData(75.0, 73.9, true)]
    [InlineData(75.0, 74.0, false)] // the test is against the range less one metre, squared, and "greater or equal" drops it
    [InlineData(75.0, 80.0, false)]
    [InlineData(25.0, 23.9, true)]
    [InlineData(25.0, 24.0, false)]
    [InlineData(25.0, 40.0, false)]
    public void An_object_beyond_the_range_less_a_metre_is_dropped(double range, double metresAhead, bool shown)
    {
        var shownAt = RadarRules.TryPlace(new Vector3(0f, (float)metresAhead, 0f), range, 50, 60, 60, out _, out _);
        Assert.Equal(shown, shownAt);
    }

    [Fact]
    public void The_same_distance_is_inside_the_outdoor_range_and_outside_the_indoor_one()
    {
        var thirty = new Vector3(0f, 30f, 0f);
        Assert.True(RadarRules.TryPlace(thirty, RadarRules.OutdoorRange, 50, 60, 60, out _, out _));
        Assert.False(RadarRules.TryPlace(thirty, RadarRules.IndoorRange, 50, 60, 60, out _, out _));
    }

    // ------------------------------------------------------------------ placement

    [Fact]
    public void A_blip_lands_at_the_centre_plus_the_offset_scaled_by_radius_over_range_with_forward_up()
    {
        // 10 m east and 20 m north of a player facing north, outdoors: 50 / 75 pixels a metre: x = (int)(66.67) = 66; y = (int)(60 - 13.33) = 46.
        var player = At(Outdoor, 100, 100, 50);
        var local = RadarRules.ToPlayerSpace(player, At(Outdoor, 110, 120, 50));
        Assert.True(RadarRules.TryPlace(local, 75, 50, 60, 60, out var x, out var y));
        Assert.Equal((66, 46), (x, y));

        // Indoors the scale is 50 / 25 = 2 pixels a metre: 3 m east and 5 m north is (66, 50).
        Assert.True(RadarRules.TryPlace(RadarRules.ToPlayerSpace(At(Indoor, 10, 10, 0), At(Indoor, 13, 15, 0)), 25, 50, 60, 60, out x, out y));
        Assert.Equal((66, 50), (x, y));

        // West of the player is left of the centre, south is below: 3.3 m west is 2.2 pixels left, truncated from 57.8; 6 m south is 4 pixels down.
        Assert.True(RadarRules.TryPlace(RadarRules.ToPlayerSpace(player, At(Outdoor, 96.7f, 94.0f, 50)), 75, 50, 60, 60, out x, out y));
        Assert.Equal((57, 64), (x, y));
    }

    [Fact]
    public void The_radar_is_player_up_so_an_object_ahead_of_a_player_facing_east_is_straight_above_the_centre()
    {
        // The player faces east (heading 90); an object 20 m east is 20 m ahead: x stays at the centre, y is (int)(60 - 13.33) = 46.
        var player = At(Outdoor, 100, 100, 50, headingDegrees: 90);
        Assert.Equal(90.0, player.HeadingDegrees, 3);
        var ahead = RadarRules.ToPlayerSpace(player, At(Outdoor, 120, 100, 50));
        Assert.True(RadarRules.TryPlace(ahead, 75, 50, 60, 60, out var x, out var y));
        Assert.Equal((60, 46), (x, y));

        // North of that player is on its left.
        var left = RadarRules.ToPlayerSpace(player, At(Outdoor, 100, 120, 50));
        Assert.True(RadarRules.TryPlace(left, 75, 50, 60, 60, out x, out y));
        Assert.Equal((46, 60), (x, y));
    }

    [Fact]
    public void Placement_crosses_a_landblock_seam()
    {
        // Player at the east edge of landblock 0xA9B4, an object just across the seam in 0xAAB4: 10 m east of the player.
        var player = At(0xA9B40001, 188, 50, 20);
        var across = At(0xAAB40001, 2, 50, 20); // 192 + 2 - 188 = 6 m east
        var local = RadarRules.ToPlayerSpace(player, across);
        Assert.Equal(6.0, local.X, 3);
        Assert.Equal(0.0, local.Y, 3);
    }

    // ------------------------------------------------------------------ intensity

    [Theory]
    [InlineData(0f, 1.0f)]
    [InlineData(4.99f, 1.0f)]
    [InlineData(-4.99f, 1.0f)]
    [InlineData(5.0f, 0.65f)]
    [InlineData(-5.0f, 0.65f)]
    [InlineData(40f, 0.65f)]
    public void A_blip_is_bright_within_5_metres_of_the_players_height_and_dim_beyond(float verticalOffset, float expected)
    {
        Assert.Equal(expected, RadarRules.IntensityOf(verticalOffset));
    }

    [Fact]
    public void The_intensity_scales_red_green_and_blue_but_not_alpha()
    {
        var dim = RadarRules.Scaled(RadarPalette.BrightGreen, RadarRules.DimIntensity);
        Assert.Equal(0f, dim.R);
        Assert.Equal(0.65f, dim.G);
        Assert.Equal(0f, dim.B);
        Assert.Equal(1f, dim.A);
        Assert.Equal(0xFF00A500u, dim.ToColor32()); // 0.65 * 255 = 165.75 truncates to 165
        Assert.Equal(0xFFBF63FFu, RadarRules.Scaled(RadarPalette.Purple, RadarRules.BrightIntensity).ToColor32());
    }

    // ------------------------------------------------------------------ shape and colour

    [Fact]
    public void A_blip_colour_byte_from_the_server_picks_the_palette_entry()
    {
        Assert.Equal(RadarPalette.Blue, RadarRules.ColorOf(1, Weenie(blip: 1), IRadarRelations.None)); // a life stone
        Assert.Equal(RadarPalette.Yellow, RadarRules.ColorOf(1, Weenie(blip: 8), IRadarRelations.None)); // an NPC
        Assert.Equal(RadarPalette.Gold, RadarRules.ColorOf(1, Weenie(blip: 2), IRadarRelations.None));
        Assert.Equal(RadarPalette.BrightGreen, RadarRules.ColorOf(1, Weenie(blip: 10), IRadarRelations.None));
        Assert.Equal(RadarPalette.Default, RadarRules.ColorOf(1, Weenie(blip: 11), IRadarRelations.None)); // not in the table
        Assert.Equal(RadarPalette.Blue, RadarRules.ColorOf(1, Weenie(flags: RadarRules.PortalBit, blip: 1), IRadarRelations.None)); // the byte wins over the portal bit
    }

    [Fact]
    public void Portals_vendors_creatures_and_players_get_their_colours()
    {
        var none = IRadarRelations.None;
        Assert.Equal(RadarPalette.Purple, RadarRules.ColorOf(1, Weenie(RadarRules.PortalBit), none));
        Assert.Equal(RadarPalette.Yellow, RadarRules.ColorOf(1, Weenie(RadarRules.VendorBit), none));
        Assert.Equal(RadarPalette.Gold, RadarRules.ColorOf(1, Weenie(RadarRules.AttackableBit), none)); // an attackable creature
        Assert.Equal(RadarPalette.White, RadarRules.ColorOf(1, Weenie(0), none)); // an NPC with no server colour
        Assert.Equal(RadarPalette.White, RadarRules.ColorOf(1, Weenie(RadarRules.AttackableBit, itemType: 0x1), none)); // attackable but not a creature
        Assert.Equal(RadarPalette.White, RadarRules.ColorOf(1, Weenie(RadarRules.PlayerBit), none));
        Assert.Equal(RadarPalette.White, RadarRules.ColorOf(1, Weenie(RadarRules.PlayerBit | RadarRules.AttackableBit), none)); // a player is never the creature gold from the attackable bit
        Assert.Equal(RadarPalette.Red, RadarRules.ColorOf(1, Weenie(RadarRules.PlayerBit | RadarRules.PlayerKillerBit), none));
        Assert.Equal(RadarPalette.Pink, RadarRules.ColorOf(1, Weenie(RadarRules.PlayerBit | RadarRules.PkLiteBit), none));
        Assert.Equal(RadarPalette.Gold, RadarRules.ColorOf(1, Weenie(RadarRules.PlayerBit | RadarRules.FreePkBit), none));
        Assert.Equal(RadarPalette.Cyan, RadarRules.ColorOf(1, Weenie(RadarRules.PlayerBit | RadarRules.AdminBit), none));
        Assert.Equal(RadarPalette.White, RadarRules.ColorOf(1, Weenie(RadarRules.PlayerBit | RadarRules.AdminBit | RadarRules.HiddenAdminBit), none)); // a hidden admin looks like anyone
        Assert.Equal(RadarPalette.White, RadarRules.ColorOf(1, Weenie(RadarRules.UiHiddenBit | RadarRules.PortalBit), none));
    }

    [Fact]
    public void A_fellowship_member_is_bright_green_and_an_inverted_triangle_and_the_leader_a_triangle()
    {
        var relations = new Relations([5, 6], leader: 5);
        var player = Weenie(RadarRules.PlayerBit | RadarRules.PlayerKillerBit);
        var self = Weenie(RadarRules.PlayerBit);
        Assert.Equal(RadarPalette.BrightGreen, RadarRules.ColorOf(5, player, relations));
        Assert.Equal(RadarPalette.BrightGreen, RadarRules.ColorOf(6, player, relations));
        Assert.Equal(RadarPalette.Red, RadarRules.ColorOf(7, player, relations)); // not in the fellowship
        Assert.Equal(RadarBlipShape.Triangle, RadarRules.ShapeOf(5, player, self, relations));
        Assert.Equal(RadarBlipShape.InvertedTriangle, RadarRules.ShapeOf(6, player, self, relations));
    }

    [Fact]
    public void The_shape_is_a_cross_unless_the_object_is_hidden_allied_or_a_player_killer_like_the_player()
    {
        var none = IRadarRelations.None;
        var self = Weenie(RadarRules.PlayerBit);
        Assert.Equal(RadarBlipShape.Cross, RadarRules.ShapeOf(1, Weenie(0), self, none)); // an NPC
        Assert.Equal(RadarBlipShape.Cross, RadarRules.ShapeOf(1, Weenie(RadarRules.AttackableBit), self, none)); // a creature
        Assert.Equal(RadarBlipShape.Cross, RadarRules.ShapeOf(1, Weenie(RadarRules.PortalBit), self, none)); // a portal
        Assert.Equal(RadarBlipShape.Cross, RadarRules.ShapeOf(1, Weenie(RadarRules.PlayerBit), self, none)); // another player
        Assert.Equal(RadarBlipShape.Undefined, RadarRules.ShapeOf(1, Weenie(RadarRules.UiHiddenBit), self, none));
        Assert.Equal(RadarBlipShape.Cross, RadarRules.ShapeOf(1, Weenie(0), null, none)); // the player has no description yet
        Assert.Equal(RadarBlipShape.Hollow, RadarRules.ShapeOf(9, Weenie(RadarRules.PlayerBit), self, new Relations([], allegiance: [9])));

        var pk = Weenie(RadarRules.PlayerBit | RadarRules.PlayerKillerBit);
        Assert.Equal(RadarBlipShape.X, RadarRules.ShapeOf(1, pk, pk, none)); // both player killers
        Assert.Equal(RadarBlipShape.Cross, RadarRules.ShapeOf(1, pk, self, none)); // only one
        var lite = Weenie(RadarRules.PlayerBit | RadarRules.PkLiteBit);
        Assert.Equal(RadarBlipShape.X, RadarRules.ShapeOf(1, lite, lite, none));
        Assert.Equal(RadarBlipShape.Cross, RadarRules.ShapeOf(1, lite, pk, none)); // a PK and a PK-Lite are not alike
    }

    [Fact]
    public void An_object_is_on_the_radar_only_with_a_behaviour_of_2_3_or_4()
    {
        Assert.False(RadarRules.IsShowable(Weenie(behavior: null)));
        Assert.False(RadarRules.IsShowable(Weenie(behavior: 0)));
        Assert.False(RadarRules.IsShowable(Weenie(behavior: 1)));
        Assert.True(RadarRules.IsShowable(Weenie(behavior: 2)));
        Assert.True(RadarRules.IsShowable(Weenie(behavior: 3)));
        Assert.True(RadarRules.IsShowable(Weenie(behavior: 4)));
        Assert.False(RadarRules.IsShowable(Weenie(behavior: 5)));
    }

    // ------------------------------------------------------------------ shapes

    [Fact]
    public void Each_shape_has_retails_pixels()
    {
        static HashSet<(int, int)> Of(RadarBlipShape shape) => [.. RadarShapes.OffsetsOf(shape).ToArray()];
        Assert.Equal([(0, 0)], Of(RadarBlipShape.Point));
        Assert.Equal([(0, 0), (0, -1), (0, 1), (-1, 0), (1, 0)], Of(RadarBlipShape.Cross));
        Assert.Equal([(0, 0), (1, 1), (-1, -1), (-1, 1), (1, -1)], Of(RadarBlipShape.X));
        Assert.Equal([(0, -1), (0, 1), (-1, 0), (1, 0), (1, 1), (-1, -1), (-1, 1), (1, -1)], Of(RadarBlipShape.Hollow)); // the centre stays empty
        Assert.Equal([(0, 0), (-1, 1), (0, 1), (1, 1)], Of(RadarBlipShape.Triangle));
        Assert.Equal([(0, 0), (-1, -1), (0, -1), (1, -1)], Of(RadarBlipShape.InvertedTriangle));
        var xbox = Of(RadarBlipShape.XBox);
        Assert.Equal(12, xbox.Count);
        Assert.Contains((-2, -2), xbox);
        Assert.Contains((2, 2), xbox);
        Assert.DoesNotContain((0, 0), xbox);
        Assert.Empty(RadarShapes.OffsetsOf(RadarBlipShape.Undefined).ToArray());
    }

    [Fact]
    public void The_selection_mark_is_a_square_outline_7_pixels_across_and_the_players_marker_a_plus_with_a_two_pixel_arm()
    {
        var selected = RadarShapes.SelectedOffsets.ToArray();
        Assert.Equal(20, selected.Distinct().Count());
        Assert.All(selected, p => Assert.Equal(3, Math.Max(Math.Abs(p.X), Math.Abs(p.Y))));
        Assert.Equal(-3, selected.Min(p => p.X));
        Assert.Equal(3, selected.Max(p => p.X));

        var marker = RadarShapes.PlayerOffsets.ToArray();
        Assert.Equal(9, marker.Distinct().Count());
        Assert.Contains((-2, 0), marker);
        Assert.Contains((0, 2), marker);
        Assert.DoesNotContain((1, 1), marker);
    }

    [Fact]
    public void The_palette_holds_the_values_read_from_the_exe()
    {
        Assert.Equal(new UiRgbaColor(0.25f, 0.66f, 1f, 1f), RadarPalette.Blue);
        Assert.Equal(new UiRgbaColor(1f, 0.67f, 0f, 1f), RadarPalette.Gold);
        Assert.Equal(new UiRgbaColor(1f, 1f, 0.5f, 1f), RadarPalette.Yellow);
        Assert.Equal(new UiRgbaColor(1f, 0.25f, 0.39f, 1f), RadarPalette.Red);
        Assert.Equal(new UiRgbaColor(0.75f, 0.39f, 1f, 1f), RadarPalette.Purple);
        Assert.Equal(new UiRgbaColor(1f, 0.66f, 0.75f, 1f), RadarPalette.Pink);
        Assert.Equal(new UiRgbaColor(0f, 0.5f, 0.25f, 1f), RadarPalette.Green);
        Assert.Equal(new UiRgbaColor(0f, 1f, 1f, 1f), RadarPalette.Cyan);
        Assert.Equal(new UiRgbaColor(0f, 1f, 0f, 1f), RadarPalette.BrightGreen);
    }
}
