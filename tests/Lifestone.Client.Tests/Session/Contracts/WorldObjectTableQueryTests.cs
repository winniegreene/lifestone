using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Contracts;

/// <summary><c>WorldObjectTable.NearestCreature</c> and <see cref="WorldObjectTable.HealthOf"/> (M3 3.1 s0).</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class WorldObjectTableQueryTests
{
    private const uint Den = 0x01050000;
    private static readonly uint[] Slaves = [1628u, 1629u];

    [Fact]
    public void The_nearest_listed_creature_wins()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, 1628, 0x10, At(Den | 0x0303, 10, 0)));
        table.Set(Make(2, 1628, 0x10, At(Den | 0x0303, 5, 0)));
        table.Set(Make(3, 1629, 0x10, At(Den | 0x0303, 8, 0)));

        Assert.True(table.NearestCreature(Slaves, At(Den | 0x0375, 0, 0), 50f, null, out var guid));
        Assert.Equal(2u, guid);
    }

    [Fact]
    public void Wcid_creature_bit_position_and_range_are_all_required()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, 23, 0x10, At(Den | 0x0303, 1, 0)));
        table.Set(Make(2, 1628, 0x2, At(Den | 0x0303, 1, 0)));
        table.Set(Make(3, 1628, 0x10, null));
        table.Set(Make(4, 1628, 0x10, At(Den | 0x0303, 40, 0)));

        Assert.False(table.NearestCreature(Slaves, At(Den | 0x0375, 0, 0), 30f, null, out var guid));
        Assert.Equal(0u, guid);
        Assert.True(table.NearestCreature(Slaves, At(Den | 0x0375, 0, 0), 41f, null, out guid));
        Assert.Equal(4u, guid);
    }

    [Fact]
    public void A_creature_known_dead_is_skipped_and_an_unknown_health_is_not()
    {
        var table = new WorldObjectTable();
        var dead = Make(1, 1628, 0x10, At(Den | 0x0303, 1, 0));
        dead.Health = 0f;
        var hurt = Make(2, 1628, 0x10, At(Den | 0x0303, 2, 0));
        hurt.Health = 0.4f;
        table.Set(dead);
        table.Set(hurt);
        table.Set(Make(3, 1628, 0x10, At(Den | 0x0303, 3, 0)));

        Assert.True(table.NearestCreature(Slaves, At(Den | 0x0375, 0, 0), 50f, null, out var guid));
        Assert.Equal(2u, guid);
        table.Remove(2);
        Assert.True(table.NearestCreature(Slaves, At(Den | 0x0375, 0, 0), 50f, null, out guid));
        Assert.Equal(3u, guid);
    }

    [Fact]
    public void The_cell_filter_sees_the_full_cell_id()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, 1628, 0x10, At(Den | 0x02D6, 1, 0)));
        table.Set(Make(2, 1628, 0x10, At(Den | 0x0303, 30, 0)));
        var seen = new List<uint>();
        bool Filter(uint cell)
        {
            seen.Add(cell);
            return cell == (Den | 0x0303);
        }

        Assert.True(table.NearestCreature(Slaves, At(Den | 0x0375, 0, 0), 50f, Filter, out var guid));
        Assert.Equal(2u, guid);
        Assert.Contains(Den | 0x02D6, seen);
    }

    [Fact]
    public void Objects_in_another_landblock_are_compared_in_global_metres()
    {
        var table = new WorldObjectTable();


        // The centre is at y 190 of landblock 0x0105, the creature at y 3 of 0x0106 (the next landblock north): 5 m apart.
        table.Set(Make(1, 1628, 0x10, At(0x01060303, 0, 3)));
        Assert.True(table.NearestCreature(Slaves, At(Den | 0x0375, 0, 190), 6f, null, out var guid));
        Assert.Equal(1u, guid);
        Assert.False(table.NearestCreature(Slaves, At(Den | 0x0375, 0, 190), 4f, null, out _));
    }

    [Fact]
    public void Health_of_reports_only_what_the_server_said()
    {
        var table = new WorldObjectTable();
        var hurt = Make(1, 1628, 0x10, null);
        hurt.Health = 0.25f;
        table.Set(hurt);
        table.Set(Make(2, 1628, 0x10, null));

        Assert.True(table.HealthOf(1, out var fraction));
        Assert.Equal(0.25f, fraction);
        Assert.False(table.HealthOf(2, out _));
        Assert.False(table.HealthOf(99, out _));
    }

    [Fact]
    public void A_query_allocates_nothing()
    {
        var table = new WorldObjectTable();
        for (uint i = 1; i <= 50; i++)
        {
            table.Set(Make(i, 1628, 0x10, At(Den | 0x0303, i, 0)));
        }

        var centre = At(Den | 0x0375, 0, 0);
        Func<uint, bool> filter = static cell => (cell & 0xFFFF) == 0x0303;
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                table.NearestCreature(Slaves, centre, 80f, filter, out _);
                table.HealthOf(7, out _);
            }
        });
    }

    private static CellFrame At(uint cell, float x, float y) => new(cell, new AcFrame(new Vector3(x, y, 0f), Quaternion.Identity));

    private static WorldObject Make(uint guid, uint wcid, uint itemType, CellFrame? position)
    {
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = "Tusker Slave",
            WeenieClassId = wcid,
            IconId = 0x06000001,
            ItemType = itemType,
            ObjectDescriptionFlags = 0,
        };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        return new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0) { Position = position };
    }
}
