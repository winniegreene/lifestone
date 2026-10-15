using System.Numerics;
using DatReaderWriter.DBObjs;
using GearCG = DatReaderWriter.Types.GearCG;
using SexCG = DatReaderWriter.Types.SexCG;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Motion;
using Lifestone.Data.World;

namespace Lifestone.Client.Avatars;

/// <summary>
/// A synthetic crowd (AV11) for the allocation and frame-time tests and for wave 2's <c>--avatar-stress=N</c>: CharGen
/// characters and server-object Setups scattered around a point, with no server. Lifestone's own tool, not retail
/// behaviour: the crowd's looks are random CharGen choices, and <see cref="AvatarStressDriver"/> sends them walking with
/// retail's MoveTo.
/// </summary>
public static class AvatarStress
{
    /// <summary>The guids the generated objects take, upwards (outside ACE's player and world ranges).</summary>
    public const uint FirstGuid = 0x7E000001;

    /// <summary>The creature item type a human template carries.</summary>
    private const bool Creature = true;

    /// <summary>
    /// <paramref name="count"/> random CharGen looks (seeded): a heritage and sex, then a random entry of every list and
    /// random shades, and each garment dyed with a random colour its table offers.
    /// </summary>
    public static IReadOnlyList<CharGenLook> RandomLooks(CharGen charGen, IAppearanceDats dats, int count, int seed)
    {
        ArgumentNullException.ThrowIfNull(charGen);
        ArgumentNullException.ThrowIfNull(dats);
        var random = new Random(seed);
        var heritages = charGen.HeritageGroups.OrderBy(kv => kv.Key).ToArray();
        var looks = new List<CharGenLook>(count);
        for (var i = 0; i < count; i++)
        {
            var (heritage, group) = heritages[random.Next(heritages.Length)];
            var sexes = group.Genders.OrderBy(kv => kv.Key).ToArray();
            var (sexId, sex) = sexes[random.Next(sexes.Length)];
            looks.Add(new CharGenLook(
                (int)heritage,
                sexId,
                Pick(random, sex.HairStyles.Count),
                Pick(random, sex.HairColors.Count),
                random.NextDouble(),
                Pick(random, sex.EyeStrips.Count),
                Pick(random, sex.EyeColors.Count),
                Pick(random, sex.NoseStrips.Count),
                Pick(random, sex.MouthStrips.Count),
                random.NextDouble(),
                Gear(random, sex, sex.Headgears, dats),
                Gear(random, sex, sex.Shirts, dats),
                Gear(random, sex, sex.Pants, dats),
                Gear(random, sex, sex.Footwear, dats)));
        }

        return looks;
    }

    /// <summary>A human template from a CharGen composition: a creature with the sex's motion table and script table.</summary>
    public static AvatarCreate Human(CharGenComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        return new AvatarCreate
        {
            Guid = 0,
            IsCreature = Creature,
            SetupId = composition.SetupId,
            MotionTableId = composition.MotionTableId,
            PhysicsScriptTableId = composition.PhysicsScriptTableId,
            ObjDesc = composition.ObjDesc,
            PhysicsState = AvatarStateBits.Gravity | AvatarStateBits.ReportCollisions,
        };
    }

    /// <summary>A template for a server object Setup (no motion table unless given).</summary>
    public static AvatarCreate Object(uint setupId, uint motionTableId = 0, bool isCreature = false) => new()
    {
        Guid = 0,
        IsCreature = isCreature,
        SetupId = setupId,
        MotionTableId = motionTableId,
        PhysicsState = AvatarStateBits.ReportCollisions,
    };

    /// <summary>
    /// <paramref name="count"/> creates cycling through <paramref name="templates"/>, with guids from
    /// <paramref name="firstGuid"/>, scattered (seeded) within <paramref name="radius"/> metres of
    /// <paramref name="center"/> at random headings; outdoors each takes the cell its origin falls in.
    /// </summary>
    public static IReadOnlyList<AvatarCreate> Generate(
        int count, int seed, in CellFrame center, float radius, IReadOnlyList<AvatarCreate> templates, uint firstGuid = FirstGuid)
    {
        ArgumentNullException.ThrowIfNull(templates);
        if (templates.Count == 0)
        {
            throw new ArgumentException("no templates", nameof(templates));
        }

        var random = new Random(seed);
        var creates = new List<AvatarCreate>(count);
        for (var i = 0; i < count; i++)
        {
            var position = RandomPoint(random, center, radius);
            var heading = (float)(random.NextDouble() * 360.0);
            position = position with { Frame = position.Frame with { Orientation = PositionFrames.WithHeading(Quaternion.Identity, heading) } };
            creates.Add(templates[i % templates.Count] with { Guid = firstGuid + (uint)i, Position = position });
        }

        return creates;
    }

    /// <summary>A seeded point within <paramref name="radius"/> of <paramref name="center"/> (outdoors, in its own cell).</summary>
    internal static CellFrame RandomPoint(Random random, in CellFrame center, float radius)
    {
        var angle = random.NextDouble() * Math.PI * 2.0;
        var distance = Math.Sqrt(random.NextDouble()) * radius;
        var origin = center.Origin + new Vector3((float)(Math.Cos(angle) * distance), (float)(Math.Sin(angle) * distance), 0f);
        var cell = center.CellId;
        if (LandDefs.IsOutdoorCell(cell))
        {
            LandDefs.AdjustToOutside(ref cell, ref origin);
        }

        return new CellFrame(cell, new AcFrame(origin, Quaternion.Identity));
    }

    private static int Pick(Random random, int count) => count > 0 ? random.Next(count) : -1;

    private static GearChoice Gear(Random random, SexCG sex, List<GearCG> list, IAppearanceDats dats)
    {
        if (list.Count == 0)
        {
            return GearChoice.None;
        }

        var index = random.Next(list.Count);
        var table = dats.GetClothingTable(list[index].ClothingTable.DataId);
        var colours = table is null
            ? []
            : sex.ClothingColors.Where(c => table.ClothingSubPalEffects.TryGetValue(c, out var e) && e.CloSubPalettes.Count > 0).ToArray();
        var template = colours.Length > 0 ? colours[random.Next(colours.Length)] : 0u;
        return new GearChoice(index, template, random.NextDouble());
    }
}

/// <summary>
/// Sends a stress crowd walking (AV11): every few seconds each walker gets a MoveToPosition (type 7, walk or run past
/// the walk/run threshold) to a random point within the radius, which retail's <see cref="MoveToManager"/> turns into
/// turns and walks. Seeded.
/// </summary>
/// <remarks>
/// Not allocation-free: the driver itself allocates nothing per tick, but every MoveTo it sends, and the steering that
/// follows in <see cref="AvatarSet.Advance"/>, queues motions in the walkers' motion tables, and each queued motion
/// allocates its pending entry in <see cref="MotionTableManager"/> (M1 code; its queue is not yet allocation-free).
/// The allocation tests measure the crowd's bytes per queued motion (<see cref="MotionPlayer.MotionsQueued"/>).
/// </remarks>
public sealed class AvatarStressDriver
{
    private readonly AvatarSet _set;
    private readonly uint[] _walkers;
    private readonly double[] _next;
    private readonly CellFrame _center;
    private readonly float _radius;
    private readonly Random _random;

    /// <param name="set">The avatar set holding the crowd.</param>
    /// <param name="walkers">The guids to walk.</param>
    /// <param name="center">The centre of the area.</param>
    /// <param name="radius">The area's radius in metres.</param>
    /// <param name="seed">The seed.</param>
    public AvatarStressDriver(AvatarSet set, IReadOnlyList<uint> walkers, in CellFrame center, float radius, int seed)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(walkers);
        _set = set;
        _walkers = [.. walkers];
        _next = new double[_walkers.Length];
        _center = center;
        _radius = radius;
        _random = new Random(seed);
    }

    /// <summary>The MoveTos sent so far.</summary>
    public long MovesSent { get; private set; }

    /// <summary>Sends each walker whose time has come a new MoveToPosition, and schedules its next one in 3-9 s.</summary>
    public void Tick(double now)
    {
        var stance = MotionCommandTable.ToWire(MotionCommands.NonCombat);
        for (var i = 0; i < _walkers.Length; i++)
        {
            if (now < _next[i])
            {
                continue;
            }

            _next[i] = now + 3.0 + (_random.NextDouble() * 6.0);
            var target = AvatarStress.RandomPoint(_random, _center, _radius);
            var parameters = new MovementParameters
            {
                Flags = MovementParameterFlags.CanWalk | MovementParameterFlags.CanRun | MovementParameterFlags.MoveTowards,
                DistanceToObject = 0.6f,
                WalkRunThreshold = 8f,
            };
            var movement = new WireMovement(
                WireMovementType.MoveToPosition,
                stance,
                StickToObject: false,
                StandingLongJump: false,
                State: default,
                MoveTo: new WireMoveTo(0, target, parameters, RunRate: 1f));
            if (_set.SetMovement(_walkers[i], isSelf: false, autonomous: false, movement, []) == AvatarApply.Applied)
            {
                MovesSent++;
            }
        }
    }
}
