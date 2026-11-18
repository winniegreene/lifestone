using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// M6 item 3 p3: the three alternate tusker grounds (the Cavern 0x5C43 of the Cave rung, the Habitat 0x5B47 of the Abode rung, the Barracks 0x5A48 of the Quarters rung) against item 0's W0 SELECT rows
/// (cache/m6/0/sel-spine.tsv for the slots, sel-portals.tsv for the gates, sel-creatures.tsv for the levels) and the pinned world SQL for the health and Magic Defense ACE computes (the creature's init skill
/// plus (Focus + Self) / 7, which is why they are above the W0 sheets' bare 97, 187 and 225). A profile that drifts from the data fails here (D28).
/// </summary>
public sealed class AlternateGroundsTests
{
    // key, entry min, generator delay (s), slots per wcid, targets: wcid -> (level, health, magic defence)
    private static readonly (string Key, int Min, double Delay, Dictionary<uint, int> Slots, (uint Wcid, int Level, int Health, int MagicDefence)[] Targets)[] Cases =
    [
        ("tusker-cavern", 15, 300, new() { [235] = 59, [22591] = 4 }, [(235, 30, 130, 103), (22591, 30, 130, 103)]),
        ("tusker-habitat", 25, 300, new() { [22519] = 53, [22595] = 4 }, [(22519, 60, 250, 203), (22595, 60, 250, 203)]),
        ("tusker-barracks", 40, 300, new() { [1629] = 49, [22593] = 5 }, [(1629, 80, 400, 244), (22593, 80, 400, 244)]),
    ];

    [Fact]
    public void The_three_alternate_grounds_hold_the_W0_counts_gates_and_target_stats()
    {
        foreach (var (key, min, delay, slots, targets) in Cases)
        {
            var ground = GroundCatalog.Get(key);
            Assert.Equal(slots.Values.Sum(), ground.Respawn.Slots.Count);
            var one = Assert.Single(ground.Respawn.Generators);
            Assert.Equal(delay, one.DelaySeconds);
            foreach (var (wcid, count) in slots)
            {
                Assert.True(ground.Respawn.Slots.Count(s => s.Wcid == wcid) == count, $"{key}: slots of wcid {wcid}");
            }

            Assert.NotNull(ground.Entry);
            Assert.Equal(min, ground.Entry.MinLevel);
            Assert.Null(ground.Entry.MaxLevel);
            Assert.False(ground.Entry.Tieable); // PortalBitmask 0x31 carries NoRecall
            Assert.False(ground.AdmitsLevel(min - 1)); // a gate with a MinLevel refuses the level below it
            Assert.True(ground.AdmitsLevel(min));
            Assert.True(ground.AdmitsLevel(100));
            Assert.Equal(targets.Length, ground.Targets.Count);
            foreach (var (wcid, level, health, magicDefence) in targets)
            {
                var t = Assert.Single(ground.Targets, x => x.Wcid == wcid);
                Assert.Equal((level, health, magicDefence), (t.Level, t.MaxHealth, t.MagicDefence));
                Assert.Equal(GroundRole.Target, t.Role);
                Assert.False(ground.IsKillFirst(wcid)); // none of these weenies has a spell book: nothing casts at the player
            }
        }
    }

    [Fact]
    public void The_Barracks_keeps_off_its_ten_Nullify_All_Magic_traps_and_the_other_two_grounds_have_none()
    {
        // weenie 7561 'trap-dispellall' (spell 1877, Nullify All Magic Other), ten landblock_instance rows in 0x5A48 over six cells; the boxes are the profile's avoid_regions
        var barracks = GroundCatalog.Get("tusker-barracks");
        Assert.Equal(6, barracks.AvoidRegions.Count);
        Assert.All(barracks.AvoidRegions, r => Assert.Equal((ushort)0x5A48, r.Landblock));
        Assert.Empty(GroundCatalog.Get("tusker-cavern").AvoidRegions);
        Assert.Empty(GroundCatalog.Get("tusker-habitat").AvoidRegions);
    }

    [Fact]
    public void The_alternates_are_live_in_the_ladder_beside_their_rungs()
    {
        var ladder = LadderLoader.WarMage;
        foreach (var (rung, alternate) in new[] { ("cave", "tusker-cavern"), ("abode", "tusker-habitat"), ("quarters", "tusker-barracks") })
        {
            var entry = Assert.Single(ladder.Rungs.Single(r => r.Key == rung).Alternates, a => a.GroundKey == alternate);
            Assert.False(entry.Pending);
        }
    }
}
