using Lifestone.Data.Physics.World;
using Lifestone.Data.Terrain;

namespace Lifestone.WorldWalk;

/// <summary>
/// Finished landblocks shared by every thread's world: a <see cref="LandblockCollision"/> is immutable once built, so one copy serves them all. Holds the most recently used
/// <paramref name="capacity"/> landblocks, which bounds the memory of a walk across the world.
/// </summary>
internal sealed class CollisionStore(LandblockCollisionBuilder builder, int capacity)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<LandblockCoord, (Lazy<LandblockCollision> Block, long Used)> _blocks = [];
    private long _clock;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _blocks.Count;
            }
        }
    }

    public LandblockCollision Get(LandblockCoord coord, CancellationToken cancellation = default)
    {
        _ = cancellation; // a build is shared, so one reader's cancel must not end it
        Lazy<LandblockCollision> block;
        lock (_gate)
        {
            _clock++;
            if (_blocks.TryGetValue(coord, out var held))
            {
                block = held.Block;
                _blocks[coord] = (block, _clock);
            }
            else
            {
                block = new Lazy<LandblockCollision>(() => builder.Build(coord), LazyThreadSafetyMode.ExecutionAndPublication);
                _blocks[coord] = (block, _clock);
                if (_blocks.Count > capacity)
                {
                    var oldest = _blocks.MinBy(x => x.Value.Used).Key;
                    _blocks.Remove(oldest);
                }
            }
        }

        return block.Value;
    }
}
