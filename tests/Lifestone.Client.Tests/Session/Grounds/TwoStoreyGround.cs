using System.Numerics;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Tests.Session.Grounds;

/// <summary>
/// A synthetic dungeon for the respawn schedule and the tour (M5 item 2 g6, D46): landblock 0x0A0A, a top storey (cells 0x0100-0x0105, the arrival 0x0100) and a low storey (0x0200-0x0205) joined by a 20 m ramp at
/// 0x0100/0x0200; two generators of different delays and ticks (<see cref="Top"/>: 600 s and 60 s over the top's slots; <see cref="Low"/>: 300 s and 30 s over the low's); wcid 10 and 11 targets, 12 avoided. Each cell
/// lies at x = 10 m times its index on its storey (<see cref="Metres"/>), so the nearest cell is known by hand. The rest of the profile is the den's (unused here).
/// </summary>
internal static class TwoStoreyGround
{
    public const uint Top = 0x7A0A0001;
    public const uint Low = 0x7A0A0002;
    public const uint Slave = 10;
    public const uint Guard = 11;
    public const uint Ghost = 12;
    public const uint Arrival = 0x0A0A0100;

    /// <summary>The cell of index <paramref name="i"/> on the top storey.</summary>
    public static uint T(int i) => 0x0A0A0100u + (uint)i;

    /// <summary>The cell of index <paramref name="i"/> on the low storey.</summary>
    public static uint L(int i) => 0x0A0A0200u + (uint)i;

    /// <summary>The two-storey ground; <paramref name="oneStorey"/>: the top storey and its generator alone.</summary>
    public static HuntingGround Build(bool oneStorey = false)
    {
        var den = GroundCatalog.Get("black-spawn-den-aluvian");
        var top = new GroundStorey("top", 0, 0, CellSet.Of(Enumerable.Range(0, 6).Select(T)));
        var low = new GroundStorey("low", -6, -6, CellSet.Of(Enumerable.Range(0, 6).Select(L)));
        GeneratorModel[] generators = oneStorey ? [new(Top, T(0), 600, 60)] : [new(Top, T(0), 600, 60), new(Low, L(0), 300, 30)];
        var slots = new List<RespawnSlot> { new(Top, T(1), Slave), new(Top, T(2), Slave), new(Top, T(2), Slave), new(Top, T(3), Ghost) };
        if (!oneStorey)
        {
            slots.AddRange([new(Low, L(1), Guard), new(Low, L(3), Guard), new(Low, L(5), Guard)]);
        }

        return new HuntingGround(
            oneStorey ? "one-storey-test" : "two-storey-test",
            "the test ground",
            GroundKind.Dungeon,
            den.Sources,
            [0x0A0A],
            new GroundArrival(Arrival, Vector3.Zero, 1, 0),
            den.Walk,
            oneStorey ? [top] : [top, low],
            CellSet.Empty,
            [new(Slave, "Slave", 60, GroundRole.Target), new(Guard, "Guard", 70, GroundRole.Target), new(Ghost, "Ghost", 90, GroundRole.Avoid)],
            den.LevelBand,
            null,
            den.Home,
            [],
            den.SpellPlan,
            [],
            [],
            new RespawnModel(generators, slots),
            oneStorey ? [T(1), T(2)] : [T(1), T(2), L(1), L(3), L(5)],
            null);
    }

    /// <summary>The walk between two cells: along a storey the x difference; between storeys each end's walk to the ramp at index 0 plus the 20 m ramp.</summary>
    public static double Metres(uint a, uint b)
    {
        var (sa, xa) = ((a >> 8) & 0xFF, 10.0 * (a & 0xFF));
        var (sb, xb) = ((b >> 8) & 0xFF, 10.0 * (b & 0xFF));
        return sa == sb ? Math.Abs(xa - xb) : xa + 20 + xb;
    }
}
