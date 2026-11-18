using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 14 (<c>m6-vendor-portal-keep-off</c>; live 2026-11-13: the walk from Uziz's Network drop to its archmage ran into the Haven Residential Halls Portal, outdoors at (164.4, 53.9) in 0xA260, which no travel record names):
/// a walk to a shopkeeper keeps clear of every portal the client holds a place for (<see cref="JourneyRunLeg.LivePortals"/>).
/// </summary>
public sealed class LivePortalKeepOffTests
{
    private const uint PortalBit = 0x40000; // PublicWeenieDesc's Portal bit (WorldContentCensus)
    private const uint Uziz = 0xA2600000;

    private static readonly Vector3 HavenPortal = new(164.4f, 53.9f, 20f);
    private static readonly Vector3 NetworkDrop = new(182.9f, 87.9f, 20f);
    private static readonly Vector3 Archmage = new(183f, 40.1f, 17.2f);

    private static WorldObject Make(uint guid, string name, uint flags, uint cell, Vector3? at)
    {
        var weenie = new WeenieDesc { Header = WeenieHeaderFlags.None, Name = name, WeenieClassId = 21464, IconId = 0x06000001, ItemType = 0, ObjectDescriptionFlags = flags };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        var o = new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0);
        if (at is { } p)
        {
            o.Position = new CellFrame(cell, new AcFrame(p, Quaternion.Identity));
        }

        return o;
    }

    private sealed class View(ActionHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.R.Bus;

        public IActionApi Actions => h.Api;

        public PlayerTools Tools => null!;

        public SessionClock Clock => h.R.Clock;

        public ActionLog ActionLog => h.ActionLog;

        public LinkStats Link => default;
    }

    private static Vector2 Global(Vector3 at) => new NavPoint(Uziz, at).Global;

    [Fact]
    public void A_walk_to_a_shopkeeper_lists_every_placed_portal_the_client_holds_and_nothing_else()
    {
        var h = new ActionHarness(offline: true);
        h.State.Objects.Set(Make(0x7A260010, "Haven Residential Halls Portal", PortalBit, Uziz | 0x25, HavenPortal));
        h.State.Objects.Set(Make(0x7A260011, "Portal to Town Network", PortalBit, 0xA2600029, new Vector3(129.7f, 1.4f, 20.2f)));
        h.State.Objects.Set(Make(0x7A260012, "Effigy Foundry Portal", PortalBit, 0xA2600121, new Vector3(162.4f, 22f, 14.8f))); // in a room
        h.State.Objects.Set(Make(0x7A260013, "A portal not placed yet", PortalBit, Uziz, null));
        h.State.Objects.Set(Make(0x7A260006, "Archmage Sammam al-Ashiz", 0x10 | 0x200, 0xA2600112, Archmage)); // a vendor, no portal

        var keep = JourneyRunLeg.LivePortals(new View(h), Global(Archmage));

        Assert.NotNull(keep);
        Assert.Equal(["Effigy Foundry Portal", "Haven Residential Halls Portal", "Portal to Town Network"], keep.Portals.Select(p => p.Name).Order());
        var haven = keep.Portals.Single(p => p.Name == "Haven Residential Halls Portal");
        Assert.True(haven.Outdoor);
        Assert.False(keep.Portals.Single(p => p.Name == "Effigy Foundry Portal").Outdoor);
        Assert.Equal(Global(HavenPortal), haven.Global);

        // the straight line from a point beside the drop to a point past the portal meets its disc; one that passes ten metres east of it does not
        Assert.NotNull(keep.FirstMeeting(Global(new Vector3(180f, 84f, 20f)), 20f, Global(new Vector3(150f, 26f, 20f)), 20f));
        Assert.Null(keep.FirstMeeting(Global(NetworkDrop), 20f, Global(new Vector3(183f, 45f, 20f)), 20f));
    }

    [Fact]
    public void A_portal_beside_the_walks_own_end_is_left_out_and_no_portal_gives_no_list()
    {
        var h = new ActionHarness(offline: true);
        Assert.Null(JourneyRunLeg.LivePortals(new View(h), Global(Archmage)));

        h.State.Objects.Set(Make(0x7A260010, "Haven Residential Halls Portal", PortalBit, Uziz | 0x25, HavenPortal));
        var beside = Global(HavenPortal) + new Vector2(PortalKeepOff.ClearanceMetres - 0.1f, 0f);
        Assert.Null(JourneyRunLeg.LivePortals(new View(h), beside)); // the only portal stands within the clearance of the end: the end is not refused
        Assert.NotNull(JourneyRunLeg.LivePortals(new View(h), Global(HavenPortal) + new Vector2(PortalKeepOff.ClearanceMetres + 0.5f, 0f)));
    }
}
