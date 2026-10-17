using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Contracts;

/// <summary><c>WorldObjectTable.NearestWithFlags</c> (M3 3.2 s0): the nearest object whose CreateObject description flags contain any of the given bits, in the distance frame of <c>NearestCreature</c>.</summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class WorldObjectTableFlagQueryTests
{
    private const uint Holtburg = 0xA9B40000;

    [Fact]
    public void The_constants_are_the_census_bits()
    {
        Assert.Equal(0x4000u, WorldObjectTable.LifestoneFlag);
        Assert.Equal(0x2000u, WorldObjectTable.CorpseFlag);
    }

    [Fact]
    public void The_nearest_object_with_the_bit_wins_and_other_bits_are_ignored()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, WorldObjectTable.LifestoneFlag, At(Holtburg | 0x0010, 10, 0)));
        table.Set(Make(2, WorldObjectTable.LifestoneFlag, At(Holtburg | 0x0010, 4, 0)));
        table.Set(Make(3, WorldObjectTable.CorpseFlag, At(Holtburg | 0x0010, 1, 0)));
        table.Set(Make(4, 0x10, At(Holtburg | 0x0010, 2, 0)));

        Assert.True(table.NearestWithFlags(WorldObjectTable.LifestoneFlag, At(Holtburg | 0x0010, 0, 0), 50f, out var guid));
        Assert.Equal(2u, guid);
        Assert.True(table.NearestWithFlags(WorldObjectTable.CorpseFlag, At(Holtburg | 0x0010, 0, 0), 50f, out guid));
        Assert.Equal(3u, guid);
    }

    [Fact]
    public void Any_of_the_given_bits_matches()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, WorldObjectTable.LifestoneFlag, At(Holtburg | 0x0010, 9, 0)));
        table.Set(Make(2, WorldObjectTable.CorpseFlag | 0x10, At(Holtburg | 0x0010, 3, 0)));

        Assert.True(table.NearestWithFlags(WorldObjectTable.LifestoneFlag | WorldObjectTable.CorpseFlag, At(Holtburg | 0x0010, 0, 0), 50f, out var guid));
        Assert.Equal(2u, guid);
    }

    [Fact]
    public void A_position_is_required_and_the_range_is_inclusive_and_zero_flags_match_nothing()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, WorldObjectTable.LifestoneFlag, null));
        table.Set(Make(2, WorldObjectTable.LifestoneFlag, At(Holtburg | 0x0010, 15, 0)));

        Assert.True(table.NearestWithFlags(WorldObjectTable.LifestoneFlag, At(Holtburg | 0x0010, 0, 0), 15f, out var guid));
        Assert.Equal(2u, guid);
        Assert.False(table.NearestWithFlags(WorldObjectTable.LifestoneFlag, At(Holtburg | 0x0010, 0, 0), 14.9f, out guid));
        Assert.Equal(0u, guid);
        Assert.False(table.NearestWithFlags(0, At(Holtburg | 0x0010, 0, 0), 50f, out guid));
        Assert.Equal(0u, guid);
    }

    [Fact]
    public void Objects_in_another_landblock_are_compared_in_global_metres()
    {
        var table = new WorldObjectTable();

        // The centre is at y 190 of one landblock, the lifestone at y 3 of the next one north: 5 m apart.
        table.Set(Make(1, WorldObjectTable.LifestoneFlag, At(0xA9B50010, 0, 3)));
        Assert.True(table.NearestWithFlags(WorldObjectTable.LifestoneFlag, At(Holtburg | 0x0010, 0, 190), 6f, out var guid));
        Assert.Equal(1u, guid);
        Assert.False(table.NearestWithFlags(WorldObjectTable.LifestoneFlag, At(Holtburg | 0x0010, 0, 190), 4f, out _));
    }

    [Fact]
    public void A_query_allocates_nothing()
    {
        var table = new WorldObjectTable();
        for (uint i = 1; i <= 50; i++)
        {
            table.Set(Make(i, i % 2 == 0 ? WorldObjectTable.LifestoneFlag : WorldObjectTable.CorpseFlag, At(Holtburg | 0x0010, i, 0)));
        }

        var centre = At(Holtburg | 0x0010, 0, 0);
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                table.NearestWithFlags(WorldObjectTable.LifestoneFlag, centre, 80f, out _);
            }
        });
    }

    private static CellFrame At(uint cell, float x, float y) => new(cell, new AcFrame(new Vector3(x, y, 0f), Quaternion.Identity));

    private static WorldObject Make(uint guid, uint descriptionFlags, CellFrame? position)
    {
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = "Thing",
            WeenieClassId = 100,
            IconId = 0x06000001,
            ItemType = 0,
            ObjectDescriptionFlags = descriptionFlags,
        };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        return new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0) { Position = position };
    }
}
