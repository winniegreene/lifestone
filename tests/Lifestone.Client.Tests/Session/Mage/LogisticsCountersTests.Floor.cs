using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 P7s (the owner's cheat-free default, D148; <c>m5-8-unsold-component-floor</c>): a carried component only the war options need (the den's Diamond Scarab 7299, which only Cassius' Ring of Fire burns) is floor-only
/// (<see cref="ComponentFloor.FloorOnly"/>): under its stop line the counters say no stop, the line is at its floor and the ring is not cast; over it again the ring returns. A carried line the bolt needs (the Pyreal Scarab) and a line a
/// vendor sells (the Prismatic Taper) stop or trip as before. The spell table is a stand-in: every profile spell on the Pyreal Scarab (component 6), the ring on the Diamond Scarab (component 110).
/// </summary>
public sealed partial class LogisticsCountersTests
{
    private const string Den = "black-spawn-den-aluvian";
    private const uint DiamondScarab = 7299;
    private const uint PyrealScarab = 690;
    private const uint PrismaticTaper = 20631;
    private const uint RingOfFire = 1785;

    /// <summary>Every spell of the profile's lines and extras on the Pyreal Scarab, the ring on the Diamond Scarab (<paramref name="ringAlsoCore"/>: a core spell needs the Diamond Scarab too).</summary>
    internal static Dictionary<uint, SpellInfo> FloorTable(bool ringAlsoCore = false)
    {
        var table = new Dictionary<uint, SpellInfo>();
        foreach (var id in ArenaProfile.Lines.Concat(ArenaProfile.Extras).SelectMany(l => l.Spells).Select(s => s.Id).Concat(ArenaProfile.FightExtras))
        {
            table[id] = WorldActionRig.Spell(id, SpellType.Projectile) with { Formula = [6, 0, 0, 0, 0, 0, 0, 0] };
        }

        table[RingOfFire] = table[RingOfFire] with { Name = "Cassius' Ring of Fire", Formula = [110, 0, 0, 0, 0, 0, 0, 0] };
        if (ringAlsoCore)
        {
            var bolt = ArenaProfile.Lines.First(l => l.Role == ProfileRole.WarBolt).Spells[0].Id;
            table[bolt] = table[bolt] with { Formula = [110, 0, 0, 0, 0, 0, 0, 0] };
        }

        return table;
    }

    private static int IndexOf(LogisticsCounters counters, uint wcid) => Enumerable.Range(0, counters.LineCount).First(i => counters.LineAt(i).Wcid == wcid);

    [Fact]
    public void The_dens_diamond_scarab_is_floor_only_and_names_the_ring_and_no_other_carried_line_is()
    {
        var ground = Ground(Den);
        var floor = ComponentFloor.FloorOnly(ground, FloorTable());

        Assert.Equal([DiamondScarab], floor.Keys);
        Assert.Equal(["Cassius' Ring of Fire"], floor[DiamondScarab]);
        Assert.Empty(ComponentFloor.FloorOnly(ground, FloorTable(ringAlsoCore: true))); // the bolt needs it too: today's stop
        Assert.Empty(ComponentFloor.FloorOnly(ground, null)); // no table: today's stop for every carried line
    }

    [Fact]
    public void A_floor_only_line_under_its_stop_line_is_no_stop_but_at_its_floor_and_over_it_again_it_is_not()
    {
        var ground = Ground(Den);
        var floorOnly = ComponentFloor.FloorOnly(ground, FloorTable()).Keys;
        var counters = new LogisticsCounters(ground, floorOnly);
        var diamond = IndexOf(counters, DiamondScarab);
        var stop = counters.LineAt(diamond).StopUnder;

        var low = new Pack(With(ground, DiamondScarab, stop - 1), burden: 100);
        var verdict = counters.Check(low.State);

        Assert.Equal(LogisticsKind.None, verdict.Kind); // the run goes on
        Assert.True(counters.IsFloorOnly(diamond));
        Assert.True(counters.AtFloor(diamond));
        Assert.Equal(1UL << diamond, counters.FloorMask);
        Assert.Equal(-1, counters.FirstCarriedShort());
        Assert.True(ComponentFloor.NeedsLineAtFloor(FloorTable()[RingOfFire], counters, out var line));
        Assert.Equal($"Diamond Scarab {stop - 1} held, under its stop line {stop}", line);
        Assert.False(ComponentFloor.NeedsLineAtFloor(FloorTable()[ArenaProfile.Lines.First(l => l.Role == ProfileRole.WarBolt).Spells[0].Id], counters, out _)); // the bolt goes on

        var back = new Pack(With(ground, DiamondScarab, stop), burden: 100);
        Assert.Equal(LogisticsKind.None, counters.Check(back.State).Kind);
        Assert.False(counters.AtFloor(diamond));
        Assert.Equal(0UL, counters.FloorMask);
        Assert.False(ComponentFloor.NeedsLineAtFloor(FloorTable()[RingOfFire], counters, out _)); // the ring returns
    }

    [Fact]
    public void Without_the_floor_only_lines_the_same_pack_stops_by_the_diamond_scarabs_name_as_before()
    {
        var ground = Ground(Den);
        var counters = new LogisticsCounters(ground);
        var stop = ground.Supplies.StockOf(DiamondScarab)!.StopUnder;

        var verdict = counters.Check(new Pack(With(ground, DiamondScarab, stop - 1), burden: 100).State);

        Assert.Equal(LogisticsKind.Stop, verdict.Kind);
        Assert.Equal("stop:Diamond Scarab", verdict.Trigger);
        Assert.Equal(0UL, counters.FloorMask);
    }

    [Fact]
    public void A_carried_line_the_bolt_needs_and_a_line_a_vendor_sells_still_stop_or_trip_with_the_floor_only_lines_set()
    {
        var ground = Ground(Den);
        var floorOnly = ComponentFloor.FloorOnly(ground, FloorTable()).Keys;
        var counters = new LogisticsCounters(ground, floorOnly);
        var pyreal = ground.Supplies.StockOf(PyrealScarab)!;
        var taper = ground.Supplies.StockOf(PrismaticTaper)!;

        var pyrealShort = counters.Check(new Pack(With(ground, PyrealScarab, pyreal.StopUnder - 1), burden: 100).State);
        Assert.Equal(LogisticsKind.Stop, pyrealShort.Kind);
        Assert.Equal("stop:Pyreal Scarab", pyrealShort.Trigger);
        Assert.True(pyrealShort.Carried);
        Assert.Equal(IndexOf(counters, PyrealScarab), counters.FirstCarriedShort());

        var taperShort = counters.Check(new Pack(With(ground, PrismaticTaper, taper.StopUnder - 1), burden: 100).State);
        Assert.Equal(LogisticsKind.Stop, taperShort.Kind);
        Assert.Equal("stop:Prismatic Taper", taperShort.Trigger);
        Assert.False(taperShort.Carried);

        var taperLow = counters.Check(new Pack(With(ground, PrismaticTaper, taper.TripUnder - 1), burden: 100).State);
        Assert.Equal(LogisticsKind.Trip, taperLow.Kind);
        Assert.Equal("stock:Prismatic Taper", taperLow.Trigger);
    }
}
