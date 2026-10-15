using System.Numerics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Radar;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Radar;

/// <summary>
/// The in-game radar over the real shell (docs/retail-ui.md section 21.4; <c>gmRadarUI</c>): its window and class, the blips for the session's objects (placement, colour, shape, intensity), the range indoors,
/// the compass tokens turning with the heading, the selection mark, the update interval, and the pixels on the radar's surface.
/// </summary>
public sealed class GameRadarTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint PlayerGuid = 0x50000001;
    private const uint PortalGuid = 0x80000A03;
    private const uint GuardGuid = 0x80000A01;
    private const uint StatueGuid = 0x80000A02;
    private const uint Outdoor = 0xA9B40001;
    private const uint Indoor = 0x860201AD;

    private static readonly InboundContext Context = new(0, MessageQueue.SmartBox, 0);

    private static CreateObjectMsg Make(uint guid, uint cell, Vector3 origin, Quaternion rotation, uint flags, byte behavior = 4, byte blip = 0)
    {
        var physics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.Position,
            State = PhysicsState.Gravity | PhysicsState.EdgeSlide,
            Position = new AcPosition(cell, origin, rotation),
            SetupId = StandInVisit.HumanSetup,
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        };
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.RadarBehavior | (blip != 0 ? WeenieHeaderFlags.RadarBlipColor : WeenieHeaderFlags.None),
            Name = $"Object {guid:X}",
            WeenieClassId = 2,
            IconId = 0x06000001,
            ItemType = 0x10,
            ObjectDescriptionFlags = flags,
            RadarBehavior = behavior,
            RadarBlipColor = blip != 0 ? blip : null,
        };
        return new CreateObjectMsg(guid, Lifestone.Net.Types.ModelData.Empty, physics, weenie);
    }

    private static Quaternion Facing(double headingDegrees) => Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)(-headingDegrees * Math.PI / 180.0));

    private (LoginRig Rig, GamePlayScreen Screen, IInboundMessageHandler Handler) EnterWorld(uint cell, double heading = 0)
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        handler!.OnPlayerCreate(Context, new PlayerCreateMsg(PlayerGuid));
        handler.OnCreateObject(Context, Make(PlayerGuid, cell, new Vector3(100f, 100f, 50f), Facing(heading), RadarRules.PlayerBit), false);
        return (rig, screen, handler);
    }

    private void Place(IInboundMessageHandler handler, uint cell, uint guid, float dx, float dy, float dz, uint flags, byte blip = 0, byte behavior = 4) =>
        handler.OnCreateObject(Context, Make(guid, cell, new Vector3(100f + dx, 100f + dy, 50f + dz), Quaternion.Identity, flags, behavior, blip), false);

    // ------------------------------------------------------------------ the window

    [RequiresLocalDatsFact]
    public void The_radar_window_is_the_radar_class_with_the_layouts_radius_and_centre_and_is_off_the_placeholder_note()
    {
        var (rig, screen, _) = EnterWorld(Outdoor);
        using var _ = rig;
        Assert.IsType<UiRadarElement>(screen.Radar);
        Assert.Equal(UiClassIds.Radar, screen.Radar!.ClassId);
        var radar = Assert.IsType<GameRadar>(screen.RadarUi);
        Assert.Equal((50, 60, 60), (radar.Radius, radar.CentreX, radar.CentreY));
        Assert.DoesNotContain("radar", screen.Placeholders);
        Assert.DoesNotContain("radar", screen.PlaceholderNote ?? string.Empty, StringComparison.Ordinal);
        Assert.False(screen.Radar.GetChildRecursive(GameRadar.DragButtonId)!.IsShown); // PostInit hides the drag button
    }

    // ------------------------------------------------------------------ blips

    [RequiresLocalDatsFact]
    public void Blips_are_placed_coloured_and_shaped_from_the_sessions_objects_and_the_radar_paints_them()
    {
        var (rig, screen, handler) = EnterWorld(Outdoor);
        using var _ = rig;
        var images = new DatUiImageSource(new TextureLoader(fixture.Dats), rig.Log);
        var compositor = new UiCompositor(images, rig.Log);
        compositor.Attach(rig.Manager);
        Settle(compositor);

        Place(handler, Outdoor, PortalGuid, -3.3f, 0f, 0f, RadarRules.PortalBit); // purple cross, 2.2 px left: x = (int)57.8 = 57
        Place(handler, Outdoor, GuardGuid, 15.3f, 30.3f, 0f, RadarRules.AttackableBit); // gold creature: (70, 39)
        Place(handler, Outdoor, StatueGuid, 0f, 6.3f, 9f, 0, blip: 8); // the server's yellow NPC, 9 m up so dim: (60, 55)
        Place(handler, Outdoor, 0x80000A04, 5f, 5f, 0f, RadarRules.UiHiddenBit | RadarRules.PortalBit); // hidden: no blip
        Place(handler, Outdoor, 0x80000A05, 5f, -5f, 0f, RadarRules.PortalBit, behavior: 1); // never shown
        Place(handler, Outdoor, 0x80000A06, 80f, 0f, 0f, RadarRules.PortalBit); // outside 75 m
        rig.Run(0.2);
        Settle(compositor);

        var radar = screen.RadarUi!;
        Assert.True(radar.Updates > 0);
        Assert.Equal(75.0, radar.Range);
        var blips = radar.Blips.ToDictionary(b => b.Guid);
        Assert.Equal(3, blips.Count);
        Assert.Equal(new RadarBlip(PortalGuid, 57, 60, RadarBlipShape.Cross, 0xFFBF63FF, false), blips[PortalGuid]);
        Assert.Equal(new RadarBlip(GuardGuid, 70, 39, RadarBlipShape.Cross, 0xFFFFAA00, false), blips[GuardGuid]); // gold (1, 0.67, 0): 0.67 * 255 = 170.85 truncates to 170 = 0xAA
        Assert.Equal(new RadarBlip(StatueGuid, 60, 55, RadarBlipShape.Cross, 0xFFA5A552, false), blips[StatueGuid]); // yellow (1, 1, 0.5) x 0.65

        // The pixels on the radar's own surface: the cross's centre and its left edge (its right edge is under the player marker), the player's bright green marker.
        var surface = SurfaceOf(rig, compositor, screen.Radar!);
        Assert.Equal(0xFFBF63FFu, Pixel(surface, 57, 60));
        Assert.Equal(0xFFBF63FFu, Pixel(surface, 56, 60));
        Assert.Equal(0xFFBF63FFu, Pixel(surface, 57, 59));
        Assert.Equal(0xFFFFAA00u, Pixel(surface, 70, 39));
        Assert.Equal(0xFFA5A552u, Pixel(surface, 60, 55));
        Assert.Equal(0xFF00FF00u, Pixel(surface, 60, 60));
        Assert.Equal(0xFF00FF00u, Pixel(surface, 62, 60));
        Assert.Equal(0xFF00FF00u, Pixel(surface, 60, 58));
    }

    [RequiresLocalDatsFact]
    public void Indoors_the_range_is_25_metres_and_the_scale_is_two_pixels_a_metre()
    {
        var (rig, screen, handler) = EnterWorld(Indoor);
        using var _ = rig;
        Place(handler, Indoor, PortalGuid, 3f, 5f, 0f, RadarRules.PortalBit); // (66, 50)
        Place(handler, Indoor, GuardGuid, 0f, 30f, 0f, RadarRules.AttackableBit); // beyond 25 m
        Place(handler, Indoor, StatueGuid, 0f, 24f, 0f, 0, blip: 8); // exactly the range less one: dropped
        rig.Run(0.2);

        var radar = screen.RadarUi!;
        Assert.Equal(25.0, radar.Range);
        var blip = Assert.Single(radar.Blips);
        Assert.Equal((PortalGuid, 66, 50), (blip.Guid, blip.X, blip.Y));
    }

    [RequiresLocalDatsFact]
    public void A_player_facing_east_has_east_at_the_top_and_the_compass_tokens_turn_with_the_heading()
    {
        var (rig, screen, handler) = EnterWorld(Outdoor, heading: 90);
        using var _ = rig;
        Place(handler, Outdoor, PortalGuid, 20f, 0f, 0f, RadarRules.PortalBit); // straight ahead
        rig.Run(0.2);

        var radar = screen.RadarUi!;
        Assert.Equal(90f, radar.PlayerHeading, 2);
        var blip = Assert.Single(radar.Blips);
        Assert.InRange(blip.X, 59, 60); // dead ahead: the centre column, give or take the rotation's rounding
        Assert.Equal(46, blip.Y); // 20 m ahead: 13.33 pixels up

        // North is now on the left of the ring (angle 90 + 180 degrees), east at the top, south on the right, west at the bottom.
        UiElement Token(uint id) => screen.Radar!.GetChildRecursive(id)!;
        var (north, east, south, west) = (Token(0x10000040), Token(0x10000041), Token(0x10000042), Token(0x10000043));
        Assert.True(north.Box.X0 + (north.Box.Width / 2) < 10, $"north token at {north.Box}");
        Assert.True(east.Box.Y0 + (east.Box.Height / 2) < 10, $"east token at {east.Box}");
        Assert.True(south.Box.X0 + (south.Box.Width / 2) > 110, $"south token at {south.Box}");
        Assert.True(west.Box.Y0 + (west.Box.Height / 2) > 110, $"west token at {west.Box}");
    }

    [RequiresLocalDatsFact]
    public void The_selected_object_gets_the_selection_mark_in_its_blip_colour()
    {
        var (rig, screen, handler) = EnterWorld(Outdoor);
        using var _ = rig;
        var images = new DatUiImageSource(new TextureLoader(fixture.Dats), rig.Log);
        var compositor = new UiCompositor(images, rig.Log);
        compositor.Attach(rig.Manager);
        Settle(compositor);
        Place(handler, Outdoor, PortalGuid, 0f, 15.3f, 0f, RadarRules.PortalBit); // (60, 49)
        Place(handler, Outdoor, GuardGuid, 15f, 15f, 0f, RadarRules.AttackableBit);
        var selected = 0u;
        screen.RadarUi!.SelectedProvider = () => selected;
        rig.Run(0.2);
        Assert.All(screen.RadarUi.Blips, b => Assert.False(b.Selected));

        selected = PortalGuid;
        rig.Run(0.2);
        Settle(compositor);
        Assert.Equal([PortalGuid], screen.RadarUi.Blips.Where(b => b.Selected).Select(b => b.Guid).ToArray());
        var surface = SurfaceOf(rig, compositor, screen.Radar!);
        Assert.Equal(0xFFBF63FFu, Pixel(surface, 63, 49)); // the right bar: x + 3
        Assert.Equal(0xFFBF63FFu, Pixel(surface, 60, 52)); // the lower bar: y + 3
        Assert.Equal(0xFFBF63FFu, Pixel(surface, 58, 46)); // the upper bar: y - 3, from x - 2
        Assert.NotEqual(0xFFBF63FFu, Pixel(surface, 64, 49)); // nothing further out

        selected = 0;
        rig.Run(0.2);
        Settle(compositor);
        Assert.NotEqual(0xFFBF63FFu, Pixel(SurfaceOf(rig, compositor, screen.Radar!), 63, 49)); // the mark is repainted away
    }

    [RequiresLocalDatsFact]
    public void The_radar_updates_every_25_milliseconds_of_the_ui_clock_not_every_frame()
    {
        var (rig, screen, _) = EnterWorld(Outdoor);
        using var _ = rig;
        var radar = screen.RadarUi!;
        rig.Run(0.1);
        var before = radar.Updates;
        var start = rig.Manager.Now;
        rig.Run(1.0); // 60 frames of 16.7 ms: an update every second frame (33 ms: the first frame at or after 25 ms)
        var made = radar.Updates - before;
        Assert.InRange(made, 28, 32);
        Assert.True(radar.NextUpdateTime >= rig.Manager.Now && radar.NextUpdateTime <= rig.Manager.Now + GameRadar.UpdateInterval + 1e-9, $"next={radar.NextUpdateTime} now={rig.Manager.Now} start={start}");
    }

    private static void Settle(UiCompositor compositor)
    {
        for (var i = 0; i < 2000 && (compositor.HasPendingWork || i == 0); i++)
        {
            if (!compositor.Composite(long.MaxValue))
            {
                Thread.Sleep(2);
            }
        }

        Assert.False(compositor.HasPendingWork);
    }

    private static UiSurface SurfaceOf(LoginRig rig, UiCompositor compositor, UiElement radar)
    {
        var surfaces = new List<IUiRegionView>();
        rig.Manager.CollectSurfaces(surfaces);
        var owner = surfaces.Single(s => ReferenceEquals(s, radar));
        return compositor.GetSurface(owner)!;
    }

    private static uint Pixel(UiSurface surface, int x, int y)
    {
        var i = ((y * surface.Width) + x) * 4;
        var p = surface.Pixels;
        return ((uint)p[i + 3] << 24) | ((uint)p[i] << 16) | ((uint)p[i + 1] << 8) | p[i + 2];
    }
}
