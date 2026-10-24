using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.World;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The seek rows (M4 P4 seek): when no creature of the band is within the acquire range, the hunt goes (<c>place loc</c>) to the nearest one the client already holds, to a dry point at the weapon's distance from it,
/// facing it. Scripted over <see cref="HuntScriptRig"/>: the only creature is placed once, on the first arrival at a belt cell, and nothing else is spawned.
/// </summary>
public sealed partial class HuntScenarioTests
{
    private const uint TumerokWarrior = 233; // level 30: listed in the belt's table, out of its 8-20 band
    private const uint Unlisted = 99999; // not in any table: never a target

    private static Func<RecordingLogSink, HuntScenario> SeekHunt(HuntWeapon weapon, Func<uint, float, float, float?>? seekHeight) =>
        log => new HuntScenario(new HuntOptions { Weapon = weapon, Kills = 1, Seconds = 300 }, log, _ => 40f, endSession: false) { SeekHeight = seekHeight };

    /// <summary>A rig whose first belt arrival holds the one creature the row places; later arrivals (the seek's too) add none.</summary>
    private static HuntScriptRig SeekRig(HuntWeapon weapon, Func<uint, float, float, float?>? seekHeight, double north, double east = 0, uint wcid = 7990)
    {
        var rig = HuntScriptRig.Create(SeekHunt(weapon, seekHeight));
        rig.OnBelt = (r, n) =>
        {
            if (n == 1)
            {
                r.PlaceCreature(FirstCreature, Belt, north, wcid: wcid, eastMetres: east);
            }
        };
        return rig;
    }

    private static float? Dry(uint cell, float x, float y) => 40f;

    private static double Metres(GlobalPosition a, GlobalPosition b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    [Fact]
    public void A_band_creature_beyond_the_acquire_range_is_sought_and_attacked_P4_seek()
    {
        using var rig = SeekRig(HuntWeapon.Missile, Dry, north: 80);

        Assert.True(rig.RunUntil(() => rig.Logged("acquire: target")), rig.Scenario.Failure + rig.Tail());

        Assert.Equal(1, rig.Scenario.Tally.Seeks);
        Assert.Equal(1, rig.Scenario.CellsVisited); // the first cell only: the seek replaced the next-cell hop
        Assert.Contains("seeks=1", rig.Scenario.Tally.Fields(), StringComparison.Ordinal);
        Assert.Matches($@"seek: '{Belt}' level 8 at 80 m; place loc to [\d.]+,[\d.]+ in 0x[0-9A-F]{{8}} \(20 m from it\)", rig.Log.Lines.First(l => l.Contains("seek: '", StringComparison.Ordinal)));
        Assert.True(rig.LineAt("seek: '") < rig.LineAt("acquire: target"), rig.Tail());
        Assert.False(rig.Logged("the next cell"), rig.Tail());
    }

    [Fact]
    public void The_seek_names_its_terrain_only_limit_once_per_hunt_P4_rfixb_R16()
    {
        using var rig = SeekRig(HuntWeapon.Missile, Dry, north: 80);

        Assert.True(rig.RunUntil(() => rig.Logged("acquire: target")), rig.Scenario.Failure + rig.Tail());

        Assert.Equal(1, rig.Log.Lines.Count(l => l.Contains("TODO(m4-seek-terrain-only)", StringComparison.Ordinal)));
        Assert.True(rig.LineAt("TODO(m4-seek-terrain-only)") < rig.LineAt("seek: '"), rig.Tail()); // named where the point is chosen, before the first seek goes out
    }

    [Fact]
    public void The_seek_point_respects_the_weapons_distance_and_faces_the_creature_P4_seek()
    {
        foreach (var (weapon, metres) in new[] { (HuntWeapon.Missile, 20.0), (HuntWeapon.Melee, 3.0) })
        {
            using var rig = SeekRig(weapon, Dry, north: 60, east: 60);

            Assert.True(rig.RunUntil(() => rig.Logged("acquire: target")), weapon + rig.Scenario.Failure + rig.Tail());

            var objects = rig.Session.State.Objects;
            Assert.True(objects.TryGet(FirstCreature, out var creature));
            var player = rig.Session.State.Self.Position!.Value;
            var spot = player.ToGlobal();
            var goal = creature!.Position!.Value.ToGlobal();
            Assert.InRange(Metres(spot, goal), metres - 0.6, metres + 0.6);
            Assert.InRange(player.HeadingDegrees, 44, 46); // it stands south-west of the creature, looking north-east
            Assert.InRange(player.Origin.Z, 40.4, 40.6); // the terrain height as given, plus 0.5 m
        }
    }

    [Fact]
    public void An_out_of_band_or_unlisted_creature_is_never_sought_P4_seek()
    {
        foreach (var wcid in new[] { TumerokWarrior, Unlisted })
        {
            using var rig = SeekRig(HuntWeapon.Missile, Dry, north: 80, wcid: wcid);

            rig.Run(60);

            Assert.Equal(0, rig.Scenario.Tally.Seeks);
            Assert.False(rig.Logged("seek: '"), rig.Tail());
            Assert.True(rig.Logged("the next cell"), $"wcid {wcid}: the fixed cells follow" + rig.Tail());
        }
    }

    [Fact]
    public void A_creature_off_the_ground_beyond_the_seek_radius_is_never_sought_P4_seek()
    {
        using var rig = SeekRig(HuntWeapon.Missile, Dry, north: HuntScenario.SeekRadiusMetres + 60);

        rig.Run(60);

        Assert.Equal(0, rig.Scenario.Tally.Seeks);
        Assert.False(rig.Logged("seek: '"), rig.Tail());
        Assert.True(rig.Logged("the next cell"), rig.Tail());
    }

    [Fact]
    public void A_creature_already_in_range_is_attacked_where_the_hunter_stands_and_not_sought_P4_seek()
    {
        using var rig = SeekRig(HuntWeapon.Missile, Dry, north: 20);

        Assert.True(rig.RunUntil(() => rig.Logged("acquire: target")), rig.Scenario.Failure + rig.Tail());

        Assert.Equal(0, rig.Scenario.Tally.Seeks);
        Assert.False(rig.Logged("seek: '"), rig.Tail());
    }

    [Fact]
    public void Without_a_seek_height_or_without_creatures_the_hunt_falls_back_to_the_fixed_cells_P4_seek()
    {
        using var none = SeekRig(HuntWeapon.Missile, null, north: 80);
        none.Run(60);
        Assert.Equal(0, none.Scenario.Tally.Seeks);
        Assert.True(none.Logged("the next cell"), none.Tail());

        using var empty = HuntScriptRig.Create(SeekHunt(HuntWeapon.Missile, Dry)); // the client sees no creature at all
        empty.Run(60);
        Assert.Equal(0, empty.Scenario.Tally.Seeks);
        Assert.True(empty.Logged("the next cell"), empty.Tail());
        Assert.Contains("seeks=0", empty.Scenario.Tally.Fields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_wet_or_steep_side_is_passed_over_and_a_creature_with_no_dry_point_is_skipped_loudly_P4_seek()
    {
        // The first try (the side the hunter is on, south of the creature) is water: the seek goes round the creature to the next dry point, still at the weapon's distance.
        var calls = 0;
        float? FirstSideWet(uint cell, float x, float y) => ++calls == 1 ? null : 40f;
        using var around = SeekRig(HuntWeapon.Missile, FirstSideWet, north: 80);
        Assert.True(around.RunUntil(() => around.Logged("acquire: target")), around.Scenario.Failure + around.Tail());
        Assert.Equal(1, around.Scenario.Tally.Seeks);
        Assert.True(calls >= 2);
        Assert.InRange(Metres(around.Session.State.Self.Position!.Value.ToGlobal(), around.Session.State.Objects.TryGet(FirstCreature, out var c) ? c!.Position!.Value.ToGlobal() : default), 19.4, 20.6);

        using var nowhere = SeekRig(HuntWeapon.Missile, (_, _, _) => null, north: 80);
        nowhere.Run(60);
        Assert.Equal(0, nowhere.Scenario.Tally.Seeks);
        Assert.True(nowhere.Logged("no dry walkable point"), nowhere.Tail());
        Assert.True(nowhere.Logged("the next cell"), nowhere.Tail());
    }

    [Fact]
    public void A_creature_is_sought_once_and_the_seeks_are_capped_P4_seek()
    {
        // The creature is placed once and the seek lands 20 m from it, so it is in range: one seek. Were it to vanish, the seek must not run to it again: the same creature is in the tried list.
        using var rig = SeekRig(HuntWeapon.Missile, Dry, north: 80);

        rig.Run(120);

        Assert.Equal(1, rig.LineCount("seek: '"));
        Assert.True(HuntScenario.MaxSeeks >= 1);
    }
}
