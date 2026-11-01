using System.Numerics;
using Lifestone.Client.Movement;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M5 7 P7p, the item-end gate's fighter vendors (journey-melee's vendor and vendor-facts stations, journey-archer's vendor, 2026-10-31 00:53 UTC): Renald the Elder never opened, because every walk to him ended 6.5 m from him centre to
/// centre, about 5.05 m cylinder to cylinder, outside ACE's 5 m use radius, and ACE's MoveTo from there ran into the barrels by his counter (<see cref="VendorStopConfirmDatTests"/> on the dats). The fix: one stand point inside his use radius
/// (<see cref="ShopLeg.RenaldStand"/>, the den profile's <c>stop_point</c>) that every walk goes to as a point goal, and (D141) a named recovery when an open is refused after ACE's MoveTo ended stuck: the stand, then one more open.
/// The hunt's half of the "same point" is <c>J2aHuntSeamTests.The_vendor_stage_alone_walks_from_the_lifestone_to_Renalds_stand_...</c>.
/// </summary>
public sealed class P7pRenaldStandTests
{
    private const uint Cell = 0x860201AD;
    private const uint Elder = 0x7A9B4021;
    private const uint RenaldMask = 0x480467A7;

    /// <summary>A leg that walks to Renald's stand and opens him the way the vendor-facts station does (<see cref="ShopLeg.GoToRenaldStand"/>, <see cref="ShopLeg.OpenRenald"/>), nothing else.</summary>
    private sealed class StandProbe(JourneyContext context) : ShopLeg("stand-probe", context)
    {
        public uint Found { get; private set; }

        public ActionHandle? Open { get; private set; }

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in GoToRenaldStand("the first walk")) { yield return h; }

            Found = NpcGuid;
            if (Found == 0)
            {
                yield break;
            }

            foreach (var h in OpenRenald(Found, "(b) OpenVendor Renald")) { yield return h; }

            Open = RenaldOpen;
        }
    }

    [Fact]
    public void The_vendor_facts_walk_to_Renald_is_a_point_goal_to_the_den_profiles_stand_the_hunts_walk_goes_to()
    {
        var rig = new J3aStationRig();
        Npc(rig, Elder, "Shopkeeper Renald the Elder", 718, 15.5f);
        var probe = new StandProbe(rig.Context("Holtburg Brawler"));

        rig.Run(probe, 20);

        var walk = rig.Travel.Requests[0];
        var profile = GroundCatalog.Get(FightOptions.DefaultGround).VendorOf(HuntScenario.RenaldWcid)!;
        Assert.Equal(TravelPurpose.Vendor, walk.Purpose);
        Assert.Equal(0u, walk.ObjectGuid); // a point goal: no object, so no range to stop at
        Assert.Equal(0.0, walk.Range);
        Assert.Equal(ShopLeg.RenaldStand.Cell, walk.Destination.Location.Position.CellId);
        Assert.Equal(ShopLeg.RenaldStand.Point, walk.Destination.Location.Position.Local);
        Assert.Equal(profile.StopCell, walk.Destination.Location.Position.CellId); // the one source: the profile's stop cell and stop point, read, not copied
        Assert.Equal(profile.StopPoint, walk.Destination.Location.Position.Local);
        Assert.Equal(ShopLeg.RenaldFrontRoomCell, ShopLeg.RenaldStand.Cell);
        Assert.Equal(new Vector3(133.9f, 15.7f, 94f), ShopLeg.RenaldStand.Point); // the vetted stand inside his use radius (VendorStopConfirmDatTests)
        Assert.Equal(Elder, probe.Found);
    }

    [Fact]
    public void A_profile_without_Renalds_stop_point_is_a_named_failure_never_a_guess()
    {
        var den = GroundCatalog.Get(FightOptions.DefaultGround);
        var without = den with { Vendors = [.. den.Vendors.Select(v => v.Wcid == HuntScenario.RenaldWcid ? v with { StopPoint = null } : v)] };
        var none = den with { Vendors = [.. den.Vendors.Where(v => v.Wcid != HuntScenario.RenaldWcid)] };

        Assert.Contains("TODO(renald-stand)", Assert.Throws<InvalidOperationException>(() => ShopLeg.RenaldStandOf(without)).Message, StringComparison.Ordinal);
        Assert.Contains("TODO(renald-stand)", Assert.Throws<InvalidOperationException>(() => ShopLeg.RenaldStandOf(none)).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ActionOutcomeKind.Refused, 0, 1, true)] // the gate's case: UseDone 0 but no 0062, after the stuck rule ended ACE's MoveTo
    [InlineData(ActionOutcomeKind.TimedOut, 2, 3, true)]
    [InlineData(ActionOutcomeKind.Refused, 1, 1, false)] // refused for another reason: no stuck MoveTo in between, no recovery
    [InlineData(ActionOutcomeKind.Confirmed, 0, 1, false)] // opened (ACE's walk ended stuck but within his radius): nothing to recover
    public void The_recovery_runs_only_for_an_open_refused_after_a_stuck_MoveTo(ActionOutcomeKind kind, int before, int after, bool recover)
    {
        Assert.Equal(recover, ShopLeg.RefusedAfterStuckMoveTo(new ActionOutcome(kind), before, after));
        Assert.False(ShopLeg.RefusedAfterStuckMoveTo(null, 0, 1)); // unsettled
    }

    [Fact]
    public void An_open_refused_after_ACEs_MoveTo_ended_stuck_walks_to_the_stand_and_opens_once_more()
    {
        var rig = new J3aStationRig();
        var body = new StuckMoveBody();
        rig.R.H.Api.Move.Bind(body, runByDefault: false);
        Npc(rig, Elder, "Shopkeeper Renald the Elder", 718, 15.5f);
        var probe = new StandProbe(rig.Context("Holtburg Brawler"));
        var answered = 0;

        // The server: the first open's walk-to-use ends stuck (the controller's stuck rule) and ACE's chain times out (UseDone 0, no 0062); the second open, from the stand, opens.
        var status = rig.Run(probe, 120, pump: () =>
        {
            var opens = Uses(rig);
            if (opens <= answered)
            {
                return;
            }

            answered = opens;
            if (opens == 1)
            {
                body.Stuck++;
                rig.R.UseDone();
            }
            else
            {
                rig.R.UseDone();
                Open(rig, Elder);
            }
        });

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(2, rig.Actions(ActionKind.OpenVendor)); // the refused open, then the recovery's: once, never a third
        Assert.Equal(2, rig.Travel.Requests.Count); // the first walk to the stand, then the recovery's walk to it
        Assert.All(rig.Travel.Requests, r => Assert.Equal(ShopLeg.RenaldStand.Point, r.Destination.Location.Position.Local));
        Assert.Equal(ActionOutcomeKind.Confirmed, probe.Open!.Outcome!.Value.Kind);
        Assert.Equal(Elder, rig.View.State.Vendor!.Vendor);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("recovery: walking to his stand and opening once more", StringComparison.Ordinal));
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("(b) OpenVendor Renald, the recovery's open: Confirmed", StringComparison.Ordinal));
    }

    [Fact]
    public void An_open_refused_with_no_stuck_MoveTo_is_not_retried()
    {
        var rig = new J3aStationRig();
        rig.R.H.Api.Move.Bind(new StuckMoveBody(), runByDefault: false);
        Npc(rig, Elder, "Shopkeeper Renald the Elder", 718, 15.5f);
        var probe = new StandProbe(rig.Context("Holtburg Brawler"));
        var answered = 0;

        rig.Run(probe, 120, pump: () =>
        {
            var opens = Uses(rig);
            if (opens > answered)
            {
                answered = opens;
                rig.R.UseDone(); // no 0062: refused, but no MoveTo of ACE's ended stuck
            }
        });

        Assert.Equal(1, rig.Actions(ActionKind.OpenVendor));
        Assert.Single(rig.Travel.Requests);
        Assert.NotEqual(ActionOutcomeKind.Confirmed, probe.Open!.Outcome!.Value.Kind);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("no recovery needed", StringComparison.Ordinal));
    }

    /// <summary>The Uses of Renald sent to the scripted server (an open is a Use; the action log counts it only once it settled).</summary>
    private static int Uses(J3aStationRig rig) => rig.R.H.Net.Of(FakeSendKind.Use).Count(s => s.Guid == Elder);

    private static void Npc(J3aStationRig rig, uint guid, string name, uint wcid, float x)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), ReducerHarness.Position(Cell, x, 20), state: PhysicsState.Gravity, name: name);
        rig.R.H.R.Reducer.OnCreateObject(rig.R.H.R.Context, basis with { Weenie = basis.Weenie with { WeenieClassId = wcid } }, isUpdate: false);
    }

    /// <summary>A 0062 through the real reducer, Renald the Elder's shape (as <c>FdVendorFixTests</c>).</summary>
    private static void Open(J3aStationRig rig, uint vendor) =>
        rig.R.H.R.Reducer.OnApproachVendor(rig.R.H.R.Context, new GameEventHeader(WorldActionRig.Player, 0, 0x0062), new VendorApproachMsg(vendor, RenaldMask, 0, 1_000_000, true, 0.9f, 1.35f, 0, 0, string.Empty, []));
}

/// <summary>A body bound to the Action API's movement whose server MoveTos ended stuck the test sets (the controller's stuck rule, <c>TODO(m5-moveto-stuck)</c>).</summary>
internal sealed class StuckMoveBody : IPlayerMovementIntents
{
    public int Stuck { get; set; }

    public CellFrame Position { get; } = new(0x860201AD, new AcFrame(new Vector3(10f, 20f, 0f), Quaternion.Identity));

    public bool OnGround => true;

    public int ServerMoveToStuckEnds => Stuck;

    public string? LastServerMoveToEnd => Stuck > 0 ? "stuck" : null;

    public void HandleCommand(uint command, bool start, float speed = 1f)
    {
    }

    public void SetHoldRun(bool on)
    {
    }

    public void SetHoldSidestep(bool on)
    {
    }

    public void ToggleAutoRun()
    {
    }

    public void CommenceJump(double now)
    {
    }

    public void DoJump(double now)
    {
    }
}
