using System.Numerics;
using System.Text.Json.Nodes;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 3 a2: the readers of the ground profile's new fields: the target's attributes and swing into the picker's snapshot, the union of the targets' damage types for the armour score, and the kill-first creature the pick takes first.
/// The embedded Temple 40+ profile is the one that carries the fields (the den's targets carry none, by design: its debuff picks are unchanged).
/// </summary>
public sealed class ProfileReadersTests
{
    private const string TempleKey = "tusker-temple-fort-tethana";
    private const uint Den = 0x01050000;

    private static HuntingGround Temple(Action<JsonObject>? edit = null)
    {
        var profile = (JsonObject)JsonNode.Parse(GroundCatalog.ResourceText(TempleKey + ".json")!)!;
        edit?.Invoke(profile);
        return GroundCatalog.Parse(TempleKey + ".json", profile.ToJsonString());
    }

    [Fact]
    public void The_snapshot_takes_the_profile_s_attributes_and_swing_and_zeroes_them_for_a_creature_without()
    {
        var slave = GroundCatalog.Get(TempleKey).Targets.Single(t => t.Wcid == 1628);
        var s = new FightSnapshot { TargetStrength = 1, TargetSwingSeconds = 9, TargetAttackSkill = 287 };
        FightPicker.FillTargetStats(ref s, slave);
        Assert.Equal((210.0, 300.0, 180.0, 200.0, 70.0, 60.0), (s.TargetStrength, s.TargetEndurance, s.TargetQuickness, s.TargetCoordination, s.TargetFocus, s.TargetSelf));
        Assert.Equal(0, s.TargetSwingSeconds); // TODO(f11-swing-seconds): no profile gives one yet, so Slowness stays unscored
        Assert.Equal(287, s.TargetAttackSkill); // not a profile field: left as it was

        FightPicker.FillTargetStats(ref s, slave with { SwingSeconds = 1.2 });
        Assert.Equal(1.2, s.TargetSwingSeconds);

        FightPicker.FillTargetStats(ref s, GroundCatalog.Get("black-spawn-den-aluvian").Targets.Single(t => t.Wcid == 1628)); // the den's rows give none
        Assert.Equal((0.0, 0.0, 0.0), (s.TargetStrength, s.TargetEndurance, s.TargetSwingSeconds));
        FightPicker.FillTargetStats(ref s, null);
        Assert.Equal(0.0, s.TargetFocus);
    }

    [Fact]
    public void The_armour_score_reads_the_union_of_the_targets_damage_types_and_the_loud_stub_stays_for_a_ground_with_none()
    {
        Assert.Equal<uint>([0x4], SelfEquipJudge.DamageTypesOf(GroundCatalog.Get(TempleKey)) ?? []); // the tuskers' 0x0004; the statue's 0x0048 is an avoided creature and not counted
        Assert.Null(SelfEquipJudge.DamageTypesOf(GroundCatalog.Get("black-spawn-den-aluvian"))); // TODO(m6-ground-damage-types) narrowed to this case

        var mixed = Temple(p => ((JsonObject)p["targets"]!.AsArray()[2]!)["damage_types"] = "0x0003");
        Assert.Equal<uint>([0x1, 0x2, 0x4], SelfEquipJudge.DamageTypesOf(mixed) ?? []);
    }

    [Fact]
    public void A_kill_first_creature_in_reach_is_picked_before_a_nearer_plain_one_and_the_nearest_decides_among_equals()
    {
        var plain = Temple();
        var first = Temple(p => ((JsonObject)p["targets"]!.AsArray()[1]!)["role"] = "kill-first"); // the Tusker Guard, 1629
        Assert.True(first.IsKillFirst(1629));
        Assert.False(first.IsKillFirst(1628));
        uint[] wcids = [1628, 1629];

        var table = new WorldObjectTable();
        table.Set(Make(1, 1628, At(Den | 0x0303, 3, 0)));
        table.Set(Make(2, 1629, At(Den | 0x0303, 12, 0)));
        table.Set(Make(3, 1629, At(Den | 0x0303, 9, 0)));
        var here = At(Den | 0x0375, 0, 0);

        Assert.True(new KillFirstPick(plain).Nearest(table, wcids, in here, 50f, null, null, out var guid));
        Assert.Equal(1u, guid); // no kill-first target: today's nearest
        var pick = new KillFirstPick(first);
        Assert.True(pick.Nearest(table, wcids, in here, 50f, null, null, out guid));
        Assert.Equal(3u, guid); // the farther kill-first one, the nearer of the two
        Assert.True(pick.Nearest(table, wcids, in here, 50f, null, [3u], out guid));
        Assert.Equal(2u, guid); // an excluded one is skipped as before
        Assert.True(pick.Nearest(table, wcids, in here, 10f, null, [3u], out guid));
        Assert.Equal(1u, guid); // none in reach: the plain one
        Assert.True(pick.Nearest(table, [1628u], in here, 50f, null, null, out guid));
        Assert.Equal(1u, guid); // a band without the kill-first class does not take it
    }

    [Fact]
    public void The_kill_first_pick_allocates_nothing_once_its_list_is_built()
    {
        var first = Temple(p => ((JsonObject)p["targets"]!.AsArray()[1]!)["role"] = "kill-first");
        IReadOnlyList<uint> wcids = new uint[] { 1628, 1629 };
        var table = new WorldObjectTable();
        table.Set(Make(1, 1628, At(Den | 0x0303, 3, 0)));
        table.Set(Make(2, 1629, At(Den | 0x0303, 9, 0)));
        var here = At(Den | 0x0375, 0, 0);
        var pick = new KillFirstPick(first);
        pick.Nearest(table, wcids, in here, 50f, null, null, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            pick.Nearest(table, wcids, in here, 50f, null, null, out _);
        }

        Assert.True(GC.GetAllocatedBytesForCurrentThread() - before < 1024);
    }

    private static CellFrame At(uint cell, float x, float y) => new(cell, new AcFrame(new Vector3(x, y, 0f), Quaternion.Identity));

    private static WorldObject Make(uint guid, uint wcid, CellFrame position)
    {
        var weenie = new WeenieDesc { Header = WeenieHeaderFlags.None, Name = "Tusker", WeenieClassId = wcid, IconId = 0x06000001, ItemType = 0x10, ObjectDescriptionFlags = 0 };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        return new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0) { Position = position };
    }
}
