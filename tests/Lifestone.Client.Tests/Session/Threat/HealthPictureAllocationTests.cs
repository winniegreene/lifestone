using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// M6 16 E2d: the health picture stands beside reads the fight makes at every decision and the emergency at every frame, so a read, a comparison that agrees, a comparison whose difference stands, and the feed for a
/// creature it holds allocate nothing (measured on the calling thread, <see cref="AllocationMeasurementCollection"/>; a window with a GC inside is not judged).
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class HealthPictureAllocationTests
{
    private const string Guard = "Tusker Guard";

    [Fact]
    public void A_read_and_a_comparison_allocate_nothing_while_the_pair_stands()
    {
        var t = new ThreatRig();
        var picture = t.Board.Health;
        var shadow = picture.Shadow;
        var max = Bestiary.Find(t.Listed)!.Vitals.MaxHealth!.Value;
        var guids = new uint[12];
        for (var i = 0; i < guids.Length; i++)
        {
            guids[i] = t.Creature(i % 2 == 0 ? Guard : "Tusker Slave", t.Listed, 4 + (i * 3));
        }

        t.Magic("You scorch Tusker Guard for 82 points with Flame Bolt VI."); // the nearest Guard is counted
        HealthPictureTests.Update(t, guids[1], 0.5f); // the nearest Slave is told by the server
        picture.Took(guids[2], 40);
        var lines = new List<string>();
        Action<string> line = lines.Add;
        var now = t.Now;
        shadow.Compare(HealthReader.FightEmergency, guids[0], true, max, max, now, line, 0, double.PositiveInfinity); // a difference that stands from here on: its line is written once, now
        Assert.Single(lines);

        var sum = 0.0;
        AllocationAssert.Nothing(() =>
        {
            for (var round = 0; round < 50; round++)
            {
                for (var i = 0; i < guids.Length; i++)
                {
                    var read = picture.Read(guids[i], now);
                    sum += read.Health;
                    shadow.Compare(HealthReader.FightPicker, guids[i], true, read.Health, read.Max, now, line, read.Dealt, read.Told ? read.BaseAge : double.PositiveInfinity); // agrees
                    shadow.Compare(HealthReader.FightWar, guids[i], false, 0, max, now, line); // a model told nothing: agrees with the ones at their maximum, differs from the hurt ones
                }

                shadow.Compare(HealthReader.FightEmergency, guids[0], true, max, max, now, line, 0, double.PositiveInfinity); // the standing difference
                sum += picture.Read(0x80001234, now).Health + picture.LastForgotten.Health;
            }
        });
        Assert.True(sum > 0);
        Assert.True(shadow.Agree(HealthReader.FightPicker) >= 12 * 100 && shadow.Differ(HealthReader.FightPicker) == 0); // the warm-up and at least one measured window
        Assert.True(shadow.Differ(HealthReader.FightWar) >= 100 && shadow.Differ(HealthReader.FightEmergency) >= 101);
        Assert.Single(lines, l => l.Contains("fight-emergency", StringComparison.Ordinal));
    }

    /// <summary>A session view over a reducer harness that records no events (the fight tests' harness boxes every event it publishes into a list, which is the harness's allocation, not the feed's).</summary>
    private sealed class BareView(ReducerHarness h) : IGameSessionView
    {
        public GameState State => h.State;

        public IEventBus Events => h.Bus;

        public IActionApi Actions => throw new NotSupportedException();

        public PlayerTools Tools => throw new NotSupportedException();

        public SessionClock Clock => h.Clock;

        public ActionLog ActionLog => throw new NotSupportedException();

        public Lifestone.Net.Diagnostics.LinkStats Link => default;
    }

    [Fact]
    public void The_feed_allocates_nothing_for_a_creature_it_holds_or_a_line_that_is_none_of_hers()
    {
        var h = new ReducerHarness(track: false);
        var player = h.EnterWorld();
        const uint Creature = 0x80007001;
        var basis = ReducerHarness.Create(Creature, ReducerHarness.Stamps(), ReducerHarness.Position(player.Position!.Value.CellId, 13), name: Guard);
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { ItemType = 0x10, WeenieClassId = 4242, ObjectDescriptionFlags = WorldObjectTable.AttackableFlag } }, isUpdate: false);
        var picture = HealthPicture.For(new BareView(h));
        var bus = h.Bus;
        var told = new HealthUpdated(Creature, 0.5f);
        var swing = new ObjectMotionChanged(Creature, false, false, new ServerMotionState { MovementType = MovementType.Interpreted, StickyGuid = ReducerHarness.PlayerGuid });
        var gone = new ObjectDeleted(0x80004444);
        var theirs = new ChatReceived(new ChatLine(0, ChatKind.System, FightTally.MagicChatType, null, 0, "Tusker Guard scorches you for 15 points with Flame Bolt III."));
        var speech = new ChatReceived(new ChatLine(0, ChatKind.Speech, 0, "Someone", 0x50000001, "You scorch Tusker Guard for 82 points with Flame Bolt VI."));
        bus.Publish(in told); // the creature's entry is made here, once
        Assert.Equal(1, picture.Count);

        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 50; i++)
            {
                bus.Publish(in told);
                bus.Publish(in swing);
                bus.Publish(in gone);
                bus.Publish(in theirs);
                bus.Publish(in speech);
            }
        });
        Assert.True(picture.Reports >= 101);
        Assert.Equal((1, 0L, 0L), (picture.Count, picture.Lines, picture.Unplaced));
    }
}
