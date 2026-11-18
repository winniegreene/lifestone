using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;
using Lifestone.Data.Animation;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Contracts;

/// <summary><c>WorldObjectTable.NearestCreature</c> by name and level band (M4 1.2 s1; the 4.2 review's R10): non-creatures, players and the dead are skipped.</summary>
public sealed class NearestCreatureByNameTests
{
    private const uint Den = 0x01050000;
    private static readonly CellFrame Centre = At(Den | 0x0375, 0, 0);

    [Fact]
    public void An_attackable_creature_is_what_can_come_at_her_a_town_npc_is_not()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, "Scrivener of Item Magic", 0x10, 0, 2)); // the nearest creature: an NPC, the attackable bit clear
        table.Set(Make(2, "Lifestone", 0x1000, WorldObjectTable.AttackableFlag, 3)); // not a creature
        table.Set(Make(3, "Holtburg Hunter", 0x10, 0x8 | WorldObjectTable.AttackableFlag, 4)); // a player

        Assert.False(table.NearestAttackableCreature(Centre, 50f, 0, out _));
        Assert.True(table.NearestCreature(Centre, 50f, 0, null, 0, int.MaxValue, null, out var npc)); // the plain query is unchanged
        Assert.Equal(1u, npc);

        table.Set(Make(4, "Drudge Skulker", 0x10, WorldObjectTable.AttackableFlag, 9));
        Assert.True(table.NearestAttackableCreature(Centre, 50f, 0, out var guid));
        Assert.Equal(4u, guid);
        Assert.False(table.NearestAttackableCreature(Centre, 5f, 0, out _));
    }

    [Fact]
    public void Non_creatures_players_and_the_dead_are_skipped()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, "Lifestone", 0x1000, 0x4000, 1)); // not a creature
        table.Set(Make(2, "Corpse of Drudge", 0x2000, 0x2000, 2)); // a corpse: not a creature
        table.Set(Make(3, "Holtburg Hunter", 0x10, 0x8, 3)); // a player
        var dead = Make(4, "Drudge", 0x10, 0x10, 4);
        dead.Health = 0f;
        table.Set(dead);
        var dying = Make(5, "Drudge", 0x10, 0x10, 5);
        dying.Motion = new ServerMotionState { MovementType = MovementType.Interpreted, Forward = WorldObjectTable.DeadMotionLowWord };
        table.Set(dying);
        table.Set(Make(6, "Drudge", 0x10, 0x10, 9));
        table.Set(Make(7, "Olthoi", 0x10, 0x10, 12));

        Assert.True(table.NearestCreature(Centre, 50f, 0, null, 0, int.MaxValue, null, out var guid));
        Assert.Equal(6u, guid);
        Assert.True(table.NearestCreature(Centre, 50f, 0, ["Olthoi", "Rat"], 0, int.MaxValue, null, out guid));
        Assert.Equal(7u, guid);
        Assert.False(table.NearestCreature(Centre, 50f, 0, ["Rat"], 0, int.MaxValue, null, out guid));
        Assert.Equal(0u, guid);
    }

    [Fact]
    public void The_radius_and_the_excluded_id_apply()
    {
        var table = new WorldObjectTable();
        table.Set(Make(6, "Drudge", 0x10, 0x10, 9));
        table.Set(Make(7, "Drudge", 0x10, 0x10, 12));

        Assert.False(table.NearestCreature(Centre, 8f, 0, ["Drudge"], 0, int.MaxValue, null, out _));
        Assert.True(table.NearestCreature(Centre, 50f, 6, ["Drudge"], 0, int.MaxValue, null, out var guid));
        Assert.Equal(7u, guid);
    }

    [Fact]
    public void A_level_band_keeps_the_creatures_in_it_and_skips_the_unknown()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, "Drudge", 0x10, 0x10, 3));
        table.Set(Make(2, "Drudge", 0x10, 0x10, 5));
        table.Set(Make(3, "Drudge", 0x10, 0x10, 7));
        table.Set(Make(4, "Drudge", 0x10, 0x10, 2));
        int? LevelOf(uint g) => g switch { 1 => 2, 2 => 6, 3 => 20, _ => null }; // 4 is unknown: TODO(m4-creature-level)

        Assert.True(table.NearestCreature(Centre, 50f, 0, ["Drudge"], 4, 12, LevelOf, out var guid));
        Assert.Equal(2u, guid);
        Assert.True(table.NearestCreature(Centre, 50f, 0, ["Drudge"], 10, 30, LevelOf, out guid));
        Assert.Equal(3u, guid);
        Assert.False(table.NearestCreature(Centre, 50f, 0, ["Drudge"], 21, 30, LevelOf, out _));
        Assert.True(table.NearestCreature(Centre, 50f, 0, ["Drudge"], 0, int.MaxValue, null, out guid)); // no band: the level is not asked
        Assert.Equal(4u, guid);
    }

    private static CellFrame At(uint cell, float x, float y) => new(cell, new AcFrame(new Vector3(x, y, 0f), Quaternion.Identity));

    private static WorldObject Make(uint guid, string name, uint itemType, uint flags, float x)
    {
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = name,
            WeenieClassId = 100,
            IconId = 0x06000001,
            ItemType = itemType,
            ObjectDescriptionFlags = flags,
        };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        return new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0) { Position = At(Den | 0x0303, x, 0) };
    }
}
