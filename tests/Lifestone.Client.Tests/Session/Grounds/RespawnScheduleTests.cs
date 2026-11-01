using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Grounds;
using static Lifestone.Client.Tests.Session.Grounds.TwoStoreyGround;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// The respawn schedule (M5 item 2 g6, D25, D46): a kill paired to its slot by wcid and cell, an unpaired kill named; a slot due at its death plus ITS generator's delay up to that generator's next tick, the phase learned
/// from the first create that brings a slot back and the due time one tick later while it is unknown; two generators with their own delays and ticks; a slot seen again stands; the queries allocate nothing. Over the
/// synthetic <see cref="TwoStoreyGround"/> and the den's and Tusker Temple's profiles.
/// </summary>
public sealed class RespawnScheduleTests
{
    [Fact]
    public void A_kill_pairs_with_a_standing_slot_of_its_wcid_in_its_cell_and_an_unpaired_kill_is_named()
    {
        var log = new RecordingLogSink();
        var s = new RespawnSchedule(Build(), log);

        Assert.Equal(1, s.Killed(Slave, T(2), 10)); // the first of the two slots in T(2)
        Assert.Equal(2, s.Killed(Slave, T(2), 11)); // then the second
        Assert.Equal(-1, s.Killed(Slave, T(2), 12)); // both dead: unpaired
        Assert.Equal(-1, s.Killed(Guard, T(1), 13)); // T(1) holds a Slave's slot, not a Guard's
        Assert.Equal(0, s.Killed(Slave, T(1), 14));

        Assert.Equal((3, 2), (s.Paired, s.Unpaired));
        Assert.False(s.IsStanding(0));
        Assert.Equal(2, log.Lines.Count(l => l.Contains("pairs with no standing slot", StringComparison.Ordinal)));
        Assert.Equal(6, s.HuntedCount); // the Ghost's slot is avoided
        Assert.Equal(3, s.StandingHunted); // 6 hunted less 3 dead
    }

    [Fact]
    public void A_kill_pairs_by_the_creatures_spawn_wherever_it_dies_and_the_slot_gets_its_due_time()
    {
        // 2-R1: three Slaves seen created in their slots' cells (T(2) holds two, T(1) one); the second of T(2)'s dies two rooms away, in a cell that holds no slot.
        var log = new RecordingLogSink();
        var s = new RespawnSchedule(Build(), log);
        Assert.Equal(-1, s.Created(Slave, T(2), 0, 100));
        Assert.Equal(-1, s.Created(Slave, T(2), 0, 101));
        Assert.Equal(-1, s.Created(Slave, T(1), 0, 102));
        Assert.Equal((1, 2, 0), (s.SlotOfGuid(100), s.SlotOfGuid(101), s.SlotOfGuid(102))); // in the profile's order, one creature a slot

        Assert.Equal(2, s.Killed(Slave, T(5), 50, 101)); // died in T(5): the death-cell rule would find no slot there
        Assert.False(s.IsStanding(2));
        Assert.Equal(50 + 600 + 60, s.DueAt(2)); // its due time, not "standing for good"
        Assert.Equal((1, 0, 1), (s.Paired, s.Unpaired, s.PairedBySpawn));
        Assert.Contains(log.Lines, l => l.Contains("paired by its spawn in 0x0A0A0102", StringComparison.Ordinal) && l.Contains("died away from the slot's cell", StringComparison.Ordinal));
        Assert.Equal(-1, s.SlotOfGuid(101)); // forgotten with its death

        // A creature that died in a neighbour cell holding another Slave's slot takes its own slot, not the neighbour's.
        Assert.Equal(0, s.Killed(Slave, T(2), 60, 102)); // 102 was T(1)'s, died in T(2), where slot 1 (creature 100's) stands
        Assert.True(s.IsStanding(1)); // 100's slot stands untouched
        Assert.Equal(1, s.Killed(Slave, T(2), 61, 100));
        Assert.Equal(3, s.StandingHunted); // the three Slaves' slots dead of the six hunted: the three Guards' stand
    }

    [Fact]
    public void A_guid_first_seen_elsewhere_falls_back_to_the_cell_it_died_in_named_and_never_takes_a_slot_another_creature_holds()
    {
        var log = new RecordingLogSink();
        var s = new RespawnSchedule(Build(), log);
        s.Created(Slave, T(2), 0, 100); // slot 1's creature
        s.Created(Slave, T(2), 0, 101); // slot 2's

        Assert.Equal(-1, s.Killed(Slave, T(2), 10, 999)); // 999 was never seen created in a slot's cell, and both slots of T(2) have a creature of their own
        Assert.Equal(0, s.Killed(Slave, T(1), 11, 998)); // T(1)'s slot has none remembered: the death-cell fallback
        Assert.Equal((1, 1, 1), (s.Paired, s.Unpaired, s.PairedByCell));
        Assert.Contains(log.Lines, l => l.Contains("paired by the cell it died in", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("pairs with no standing slot", StringComparison.Ordinal));
        Assert.Equal(1, s.SlotOfGuid(100));

        // With no guid at all the death-cell rule alone holds, and it too leaves a slot another creature holds alone.
        Assert.Equal(-1, s.Killed(Slave, T(2), 12));
        Assert.Equal(2, s.Unpaired);
    }

    [Fact]
    public void A_create_that_brings_a_dead_slot_back_remembers_its_new_creature_and_a_creature_seen_again_changes_nothing()
    {
        var s = new RespawnSchedule(Build(), NullLogSink.Instance);
        s.Created(Slave, T(1), 0, 100);
        Assert.Equal(0, s.Killed(Slave, T(1), 100, 100));
        Assert.Equal(0, s.Created(Slave, T(1), 715, 200)); // the respawn: slot 0 stands again, and 200 is its creature
        Assert.Equal(0, s.SlotOfGuid(200));
        Assert.Equal(-1, s.Created(Slave, T(1), 720, 200)); // seen again: nothing
        Assert.Equal(0, s.Killed(Slave, T(4), 800, 200)); // died away from the cell: the slot is dead again
        Assert.Equal(1435, s.DueAt(0), 6); // ready at 1400; the phase learned at 715 (55 s of 60) puts the next tick at 1435
    }

    [Fact]
    public void A_seed_remembers_each_creature_standing_in_a_slots_cell_so_its_kill_pairs_by_its_spawn_and_leaves_the_rest_to_the_named_fallback()
    {
        // F4: creatures already standing when the schedule is made were created before it existed. T(2) holds two Slave slots, T(1) one, L(1) a Guard's. 100, 101 and 103 stand in T(2) (the third has no slot left), 102 in T(1);
        // 104 (a Slave) stands in T(5), where no slot is; 105 (a Guard) stands in T(1), which holds a Slave's slot; 106 (a Ghost) stands in T(3), whose slot is not hunted but is a slot all the same (the seed follows the create's rule).
        var log = new RecordingLogSink();
        var s = new RespawnSchedule(Build(), log);
        (uint Wcid, uint Cell, uint Guid)[] known = [(Slave, T(2), 100), (Slave, T(2), 101), (Slave, T(1), 102), (Slave, T(2), 103), (Slave, T(5), 104), (Guard, T(1), 105), (Ghost, T(3), 106)];

        Assert.Equal(4, s.Seed(known, 0));

        Assert.Equal((1, 2, 0, -1, -1, -1, 3), (s.SlotOfGuid(100), s.SlotOfGuid(101), s.SlotOfGuid(102), s.SlotOfGuid(103), s.SlotOfGuid(104), s.SlotOfGuid(105), s.SlotOfGuid(106)));
        Assert.Equal(4, s.Seeded);
        Assert.Contains(log.Lines, l => l.Contains("seeded from the creatures already known: 7 of a slot's wcid, 4 standing in a slot's cell", StringComparison.Ordinal));
        Assert.Equal(6, s.StandingHunted); // a seed kills nothing

        // A seeded creature's kill pairs by its spawn wherever it dies, and its slot gets its due time.
        Assert.Equal(2, s.Killed(Slave, T(5), 50, 101));
        Assert.Equal(50 + 600 + 60, s.DueAt(2));
        Assert.Equal((1, 1, 0), (s.Paired, s.PairedBySpawn, s.PairedByCell));
        Assert.Contains(log.Lines, l => l.Contains("paired by its spawn in 0x0A0A0102", StringComparison.Ordinal));

        // Creatures the seed did not remember keep the fallback, named, and it never takes a slot another creature holds.
        Assert.Equal(-1, s.Killed(Slave, T(2), 60, 103)); // T(2)'s slots: one held by 100, one dead: unpaired
        Assert.Equal(4, s.Killed(Guard, L(1), 61, 105)); // 105 stood in T(1), which has no Guard slot; it dies in L(1), whose Guard slot has no creature of its own
        Assert.Equal((2, 1, 1, 1), (s.Paired, s.PairedBySpawn, s.PairedByCell, s.Unpaired));
        Assert.Contains(log.Lines, l => l.Contains("paired by the cell it died in: its guid was not seen created in a slot's cell", StringComparison.Ordinal));
        Assert.Equal(0, s.Seed(known, 70)); // the same creatures offered again claim nothing: the held ones stay, the dead slot is not yet due
    }

    [Fact]
    public void The_due_time_is_one_tick_late_while_the_phase_is_unknown_and_on_the_generators_tick_once_seen()
    {
        var s = new RespawnSchedule(Build(), NullLogSink.Instance);

        Assert.Equal(1, s.Killed(Slave, T(2), 100));
        Assert.False(s.PhaseKnown(1));
        Assert.Equal(100 + 600 + 60, s.DueAt(1)); // phase unknown: death + delay + one tick

        // The generator's creates come at its ticks: one at 715 brings the slot back (inside 700..760) and teaches the phase, 715 mod 60 = 55.
        Assert.Equal(1, s.Created(Slave, T(2), 715));
        Assert.True(s.IsStanding(1));
        Assert.Equal(55, s.PhaseOf(Top), 6);

        Assert.Equal(1, s.Killed(Slave, T(2), 1000));
        Assert.Equal(1615, s.DueAt(1), 6); // ready at 1600; the ticks fall at 55 + 60 k: 1555, 1615
        Assert.Equal(0, s.Killed(Slave, T(1), 1555));
        Assert.Equal(2155, s.DueAt(0), 6); // ready at 2155, exactly on a tick
    }

    [Fact]
    public void Two_generators_keep_their_own_delays_ticks_and_phases()
    {
        var s = new RespawnSchedule(Build(), NullLogSink.Instance);

        Assert.Equal(0, s.Killed(Slave, T(1), 50));
        Assert.Equal(4, s.Killed(Guard, L(1), 50));
        Assert.Equal(50 + 600 + 60, s.DueAt(0)); // Top: 600 s and 60 s
        Assert.Equal(50 + 300 + 30, s.DueAt(4)); // Low: 300 s and 30 s

        Assert.Equal(4, s.Created(Guard, L(1), 362)); // Low's phase 362 mod 30 = 2
        Assert.Equal(2, s.PhaseOf(Low), 6);
        Assert.True(double.IsNaN(s.PhaseOf(Top))); // a create of the other generator teaches Top nothing
        Assert.Equal(50 + 600 + 60, s.DueAt(0));

        Assert.Equal(5, s.Killed(Guard, L(3), 400));
        Assert.Equal(722, s.DueAt(5), 6); // ready at 700; Low's ticks at 2 + 30 k: 692, 722
        Assert.Equal(0, s.EarliestDueOn(-1)); // 0 is due at 710, 5 at 722
        Assert.Equal(0, s.EarliestDueOn(0));
        Assert.Equal(5, s.EarliestDueOn(1));
    }

    [Fact]
    public void A_creature_seen_again_makes_its_slot_standing_but_not_before_its_delay_and_a_late_sighting_teaches_no_phase()
    {
        var s = new RespawnSchedule(Build(), NullLogSink.Instance);
        Assert.Equal(0, s.Killed(Slave, T(1), 100));

        Assert.Equal(-1, s.Created(Slave, T(1), 300)); // a Slave seen in T(1) before 700 is not this slot's (another creature came into sight)
        Assert.False(s.IsStanding(0));
        Assert.Equal(-1, s.Created(Slave, T(2), 800)); // another cell
        Assert.Equal(-1, s.Created(Guard, T(1), 800)); // another wcid

        Assert.Equal(0, s.Created(Slave, T(1), 900)); // past 700 + 60: the creature came into sight after its tick
        Assert.True(s.IsStanding(0));
        Assert.True(double.IsNaN(s.PhaseOf(Top))); // outside the window: no phase from it
        Assert.True(double.IsNaN(s.DueAt(0)));
        Assert.Equal(-1, s.Created(Slave, T(1), 901)); // a standing slot: a sighting changes nothing
    }

    [Fact]
    public void A_slot_long_past_its_due_with_no_create_seen_is_taken_as_standing_named()
    {
        var log = new RecordingLogSink();
        var s = new RespawnSchedule(Build(), log);
        s.Killed(Slave, T(1), 100); // due 760 (phase unknown)

        Assert.Equal(0, s.Expire(760 + RespawnSchedule.UnseenGraceSeconds));
        Assert.False(s.IsStanding(0));
        Assert.Equal(1, s.Expire(760 + RespawnSchedule.UnseenGraceSeconds + 0.5));
        Assert.True(s.IsStanding(0));
        Assert.Equal(1, s.Expired);
        Assert.Contains(log.Lines, l => l.Contains("TODO(ground-respawn-unseen)", StringComparison.Ordinal) && l.Contains("slot 0", StringComparison.Ordinal));
    }

    [Fact]
    public void The_queries_allocate_nothing()
    {
        var s = new RespawnSchedule(Build(), NullLogSink.Instance);
        s.Killed(Slave, T(1), 10);
        s.Killed(Guard, L(3), 20);
        var sink = 0.0;
        for (var i = 0; i < 2; i++)
        {
            sink += Query(s);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            sink += Query(s);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(sink > 0);

        static double Query(RespawnSchedule s)
        {
            var sum = 0.0;
            for (var i = 0; i < s.Count; i++)
            {
                sum += s.IsStanding(i) ? 1 : s.DueAt(i);
                sum += s.IsHunted(i) ? s.StoreyOf(i) : 0;
            }

            return sum + s.StandingOn(0) + s.StandingOn(1) + s.StandingHunted + s.EarliestDueOn(-1) + s.EarliestDueOn(1) + (s.PhaseKnown(0) ? 1 : 2);
        }
    }

    [Theory]
    [InlineData("black-spawn-den-aluvian", 78, 900, 60)]
    [InlineData("tusker-temple-fort-tethana", -1, 300, 60)]
    public void Any_dungeon_profiles_slots_are_scheduled_with_their_generators_delay(string key, int hunted, double delay, double tick)
    {
        var ground = GroundCatalog.Get(key);
        var s = new RespawnSchedule(ground, NullLogSink.Instance);
        Assert.Equal(ground.Respawn.Slots.Count, s.Count);
        if (hunted >= 0)
        {
            Assert.Equal(hunted, s.HuntedCount); // the den: 19 Slaves and 59 Guards in walkable cells (g2's count)
        }

        Assert.True(s.HuntedCount > 0);
        var first = Enumerable.Range(0, s.Count).First(s.IsHunted);
        var slot = s.Slot(first);
        Assert.Equal(first, s.Killed(slot.Wcid, slot.Cell, 1000));
        Assert.Equal(1000 + delay + tick, s.DueAt(first), 6);
        Assert.Equal(s.HuntedCount - 1, s.StandingHunted);
    }

    [Fact]
    public void A_slot_in_view_with_no_creature_of_its_own_after_the_settle_is_seen_empty_and_due_at_the_latest_at_the_moment_seen_plus_its_delay_and_tick()
    {
        // F5 (D47): TODO(ground-slot-dead-at-start) narrowed: a slot whose creature died before the schedule began is learned from what the player sees.
        var log = new RecordingLogSink();
        var s = new RespawnSchedule(Build(), log);
        var empty = new Lifestone.Client.Session.Contracts.WorldObjectTable();
        HashSet<uint> view = [T(1), T(2)];

        Assert.Equal(0, s.Look(view, empty, 10)); // in view from 10
        Assert.Equal(0, s.Look(view, empty, 10 + RespawnSchedule.SeenEmptySettleSeconds - 0.1)); // not before the settle
        Assert.Equal(0, s.Look(new HashSet<uint> { T(0) }, empty, 11)); // out of view: the settle starts again
        Assert.Equal(0, s.Look(view, empty, 12));
        Assert.Equal(3, s.Look(view, empty, 12 + RespawnSchedule.SeenEmptySettleSeconds)); // T(1)'s and T(2)'s three Slave slots
        Assert.Equal(3, s.SeenEmpty);
        Assert.True(s.IsSeenEmpty(0) && !s.IsStanding(0));
        Assert.Equal(0, s.Look(view, empty, 30)); // learned once
        Assert.Equal(3, log.Lines.Count(l => l.Contains("is seen empty at", StringComparison.Ordinal)));
        Assert.Equal(14 + 600 + 60, s.DueAt(0), 6); // the phase unknown: the moment seen, the delay and one tick (the latest it can be)
        Assert.Equal(0, s.Look(view, empty, 31)); // nothing more
        Assert.False(s.IsSeenEmpty(3)); // the Ghost's slot is not hunted: never looked at

        // A create of its wcid in its cell before the moment seen plus the delay brings it back (its real death was earlier) and teaches no phase.
        Assert.Equal(0, s.Created(Slave, T(1), 40, 0x80000E01));
        Assert.True(s.IsStanding(0));
        Assert.False(s.IsSeenEmpty(0));
        Assert.True(double.IsNaN(s.PhaseOf(Top)));

        // Unseen, the seen-empty slot is taken as standing after its latest due time and the grace, as a dead one is (TODO(ground-respawn-unseen)).
        Assert.Equal(2, s.Expire(14 + 600 + 60 + RespawnSchedule.UnseenGraceSeconds + 1));
        Assert.False(s.IsSeenEmpty(1));
    }

    [Fact]
    public void A_creature_of_its_own_keeps_a_slot_in_view_standing_and_with_the_phase_known_the_seen_empty_due_time_is_rounded_to_the_tick()
    {
        var s = new RespawnSchedule(Build(), NullLogSink.Instance);
        var slave = GroundTourPurposeTests.Creature(0x80000E02, Slave, T(2));
        var dead = GroundTourPurposeTests.Creature(0x80000E03, Slave, T(2));
        dead.Health = 0f;
        var table = GroundTourPurposeTests.Table(slave, dead);
        HashSet<uint> view = [T(1), T(2)];

        s.Look(view, table, 0);
        Assert.Equal(2, s.Look(view, table, 5)); // T(1)'s slot and one of T(2)'s: the living Slave in T(2) is claimed for the other (the seed's rule), the dead one counts for none
        Assert.Equal(1, s.SlotOfGuid(0x80000E02));
        Assert.True(s.IsStanding(1));
        Assert.True(s.IsSeenEmpty(0) && s.IsSeenEmpty(2));
        Assert.Equal(-1, s.SlotOfGuid(0x80000E03));

        // The Slave walks off (it chases the player) but lives: its slot is still its own, whatever cell it stands in.
        slave.Position = new Lifestone.Data.World.CellFrame(L(1), slave.Position!.Value.Frame);
        Assert.Equal(0, s.Look(view, table, 30));
        Assert.True(s.IsStanding(1));

        // With the Top generator's phase learned (a create inside a killed slot's window), a seen-empty slot's latest due time is the next tick after the moment seen plus the delay.
        var other = new RespawnSchedule(Build(), NullLogSink.Instance);
        other.Killed(Slave, T(1), 0);
        other.Created(Slave, T(1), 630); // phase 30 of 60
        Assert.Equal(30, other.PhaseOf(Top), 6);
        other.Look(new HashSet<uint> { T(2) }, new Lifestone.Client.Session.Contracts.WorldObjectTable(), 98);
        other.Look(new HashSet<uint> { T(2) }, new Lifestone.Client.Session.Contracts.WorldObjectTable(), 100);
        Assert.True(other.IsSeenEmpty(1));
        Assert.Equal(750, other.DueAt(1), 6); // 100 + 600 = 700, up to the tick at 30 + 12 x 60 = 750 (at most one tick past it)
    }
}
