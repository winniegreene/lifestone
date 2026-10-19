using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Interiors;
using Lifestone.Client.Tests.Interiors;
using Lifestone.Data.Terrain;
using Lifestone.Data.Animation;
using Lifestone.Data.Tests;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// M3 4.2 x2, item 3 (closes <c>TODO(m3-bolt-cell-tracking)</c>): a flying projectile changes cell as it crosses a cell boundary (retail <c>CPhysicsObj::UpdateObjectInternal</c> 0x005161B0 through
/// <c>CObjCell::find_cell_list</c>), on the Aluvian Black Spawn Den's real cells (landblock 0x0105). Skips without the dats.
/// </summary>
public sealed class ProjectileCellFollowerTests(InteriorsFixture fixture) : IClassFixture<InteriorsFixture>
{
    private static readonly LandblockCoord Den = new(0x01, 0x05);
    private const uint Arrival = 0x01050375;

    [RequiresDatsFact]
    public void A_step_into_the_neighbour_behind_a_portal_moves_the_cell_and_a_step_inside_does_not()
    {
        var (registry, a, b, pointA, pointB) = TwoCells();
        var follower = new ProjectileCellFollower(registry);

        Assert.Equal(a.Id, follower.Follow(a.Id, pointA, pointA + new Vector3(0.01f, 0, 0)));
        Assert.Equal(b.Id, follower.Follow(a.Id, pointA, pointB));
        Assert.Equal(b.Id, follower.Follow(b.Id, pointB, pointB));
        Assert.Equal(1, follower.Moves);
    }

    [RequiresDatsFact]
    public void A_bolt_flying_through_two_cells_of_the_den_is_in_each_in_turn()
    {
        var (registry, a, b, pointA, pointB) = TwoCells();
        var set = new AvatarSet(AvatarRig.Sources()) { CellFollower = new ProjectileCellFollower(registry) };
        var velocity = (pointB - pointA) / 0.5f; // a straight flight of half a second from one cell's point to the other's
        var create = AvatarRig.HumanAt(0x80000900, pointA) with
        {
            SetupId = AvatarRig.Lamp,
            MotionTableId = 0,
            IsCreature = false,
            PhysicsState = AvatarStateBits.Missile | AvatarStateBits.ReportCollisions,
            Position = new CellFrame(a.Id, new AcFrame(pointA, Quaternion.Identity)),
            Velocity = velocity,
        };
        Assert.Equal(AvatarApply.Applied, set.Create(create, now: 0));
        var body = set.All[0].Body;
        Assert.True(body.IsProjectile);
        Assert.Equal(a.Id, body.Position.CellId);

        var viewer = new AvatarViewer(AvatarRig.At(pointA, 0f, a.Id));
        var seen = new HashSet<uint>();
        for (var frame = 1; frame <= 40; frame++)
        {
            set.Advance(frame / 60.0, viewer, long.MaxValue);
            seen.Add(body.Position.CellId);
        }

        Assert.Contains(a.Id, seen);
        Assert.Contains(b.Id, seen);
        Assert.Equal(b.Id, body.Position.CellId);
    }

    [RequiresDatsFact]
    public void Without_a_follower_a_bolt_keeps_its_creation_cell()
    {
        var (_, a, _, pointA, pointB) = TwoCells();
        var set = new AvatarSet(AvatarRig.Sources());
        var create = AvatarRig.HumanAt(0x80000901, pointA) with
        {
            SetupId = AvatarRig.Lamp,
            MotionTableId = 0,
            IsCreature = false,
            PhysicsState = AvatarStateBits.Missile | AvatarStateBits.ReportCollisions,
            Position = new CellFrame(a.Id, new AcFrame(pointA, Quaternion.Identity)),
            Velocity = (pointB - pointA) / 0.5f,
        };
        Assert.Equal(AvatarApply.Applied, set.Create(create, now: 0));
        var viewer = new AvatarViewer(AvatarRig.At(pointA, 0f, a.Id));
        for (var frame = 1; frame <= 40; frame++)
        {
            set.Advance(frame / 60.0, viewer, long.MaxValue);
        }

        Assert.Equal(a.Id, set.All[0].Body.Position.CellId);
    }

    /// <summary>Two cells of the den joined by a portal, each with a point its own BSP holds (the centre of its struct's bounds, when that is inside it).</summary>
    private (InteriorRegistry Registry, EnvCellData A, EnvCellData B, Vector3 PointA, Vector3 PointB) TwoCells()
    {
        var registry = fixture.Registry(Den);
        var block = registry.Get(Den)!;
        var start = block.Cells.GetCell(Arrival)!;
        foreach (var cell in new[] { start }.Concat(block.Cells.Cells))
        {
            var pointA = Centre(block, cell);
            if (pointA is null)
            {
                continue;
            }

            foreach (var portal in cell.Portals.Where(p => !p.LeadsOutside))
            {
                if (block.Cells.GetCell(portal.OtherCellId) is { } other && Centre(block, other) is { } pointB && (pointB - pointA.Value).Length() > 0.5f)
                {
                    return (registry, cell, other, pointA.Value, pointB);
                }
            }
        }

        throw new InvalidOperationException("no two joined den cells with an inside centre");
    }

    private static Vector3? Centre(LandblockInteriors block, EnvCellData cell)
    {
        if (!block.TryGetCellBounds(cell.Id, out var box))
        {
            return null;
        }

        var centre = (box.Min + box.Max) * 0.5f;
        return block.Contains(cell, centre) ? centre : null;
    }
}
