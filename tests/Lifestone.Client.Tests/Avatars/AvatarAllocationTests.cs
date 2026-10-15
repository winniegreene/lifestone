using System.Diagnostics;
using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Data.Animation;
using Lifestone.Data.Appearance;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Motion;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// The frame path allocates nothing (AV11, DoD 15): once warm, an <see cref="AvatarSet.Advance"/> of 60 walking humans
/// and 200 idle server objects, with its draw output, allocates 0 bytes. A crowd steered by MoveTo queues motions, which
/// M1's motion-table code still allocates for: that crowd is measured per queued motion.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class AvatarAllocationTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    [Fact]
    public void Advancing_60_walkers_and_200_idle_objects_allocates_nothing()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        var center = AvatarRig.At(new Vector3(96, 96, 0));
        var humans = AvatarStress.Generate(60, seed: 1, center, radius: 40f, [AvatarRig.HumanAt(0, default)]);
        var lamps = AvatarStress.Generate(200, seed: 2, center, radius: 60f, [AvatarStress.Object(AvatarRig.Lamp)], firstGuid: 0x7E100001);
        Populate(set, humans, lamps);
        Measure(set, output);
    }

    /// <summary>
    /// The same with the dats: CharGen-composed humans on their motion tables, and 200 idle humans (Setup 0x02000001
    /// without a motion table of its own, cycling its default table's default state).
    /// </summary>
    [RequiresDatsFact]
    public void Advancing_60_chargen_humans_and_200_objects_from_the_dats_allocates_nothing()
    {
        var dats = fixture.Dats;
        var sources = AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
        Assert.True(dats.TryGetPortal<DatReaderWriter.DBObjs.CharGen>(0x0E000002, out var charGen));
        var appearanceDats = new DatAppearanceDats(dats);
        var templates = AvatarStress.RandomLooks(charGen, appearanceDats, 60, seed: 3)
            .Where(l => l.Heritage is >= 1 and <= 3)
            .Take(12)
            .Select(l => AvatarStress.Human(CharGenAppearance.Compose(charGen, l, appearanceDats)))
            .ToList();
        Assert.NotEmpty(templates);
        var set = new AvatarSet(sources);
        var center = AvatarRig.At(new Vector3(96, 96, 0));
        var humans = AvatarStress.Generate(60, seed: 4, center, radius: 40f, templates);
        var objects = AvatarStress.Generate(200, seed: 5, center, radius: 60f, [AvatarStress.Object(0x02000001)], firstGuid: 0x7E100001);
        Populate(set, humans, objects);
        Measure(set, output);
    }

    /// <summary>
    /// The crowd <c>--avatar-stress</c> runs: 60 humans on their motion players, sent around by
    /// <see cref="AvatarStressDriver"/> with retail's MoveTo. The driver, the set and the bodies allocate nothing per
    /// tick or advance once warm; the MoveTos and their steering queue motions, and each queued motion allocates in
    /// M1's motion-table code (<see cref="MotionTableBytesPerQueuedMotion"/>), so the bytes are measured per queued
    /// motion. The frames that queue none are reported (a body's first link or action can still grow a list once).
    /// </summary>
    [Fact]
    public void A_moveto_crowd_allocates_only_for_the_motions_it_queues()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        var center = AvatarRig.At(new Vector3(96, 96, 0));
        var crowd = AvatarStress.Generate(60, seed: 5, center, radius: 50f, [AvatarRig.HumanAt(0, default)]);
        foreach (var create in crowd)
        {
            Assert.Equal(AvatarApply.Applied, set.Create(create, now: 0));
        }

        var players = set.All.Select(a => a.Body.Parts!).ToArray();
        var driver = new AvatarStressDriver(set, crowd.Select(c => c.Guid).ToArray(), center, 50f, seed: 6);
        long bytes = 0;
        long queued = 0;
        long quietBytes = 0;
        var quietFrames = 0;
        var judged = 0;
        for (var frame = 1; frame <= 1800; frame++)
        {
            var now = frame / 60.0;
            var before = MotionsQueued(players);
            var window = AllocationWindow.Open();
            driver.Tick(now);
            set.Advance(now, Viewer, long.MaxValue);
            var (b, gcInside) = window.Close();
            if (frame <= 600 || gcInside)
            {
                continue;
            }

            var motions = MotionsQueued(players) - before;
            bytes += b;
            queued += motions;
            if (motions == 0)
            {
                quietFrames++;
                quietBytes += b;
            }

            judged++;
        }

        output.WriteLine($"{judged} frames: {bytes} B for {queued} queued motions ({(double)bytes / Math.Max(1, queued):0.#} B each); {quietFrames} frames queued none ({quietBytes} B)");
        Assert.True(judged > 1100);
        Assert.True(queued > 100, $"only {queued} motions queued: the crowd is not being steered");
        Assert.True(
            bytes <= MotionTableBytesPerQueuedMotion * queued,
            $"{bytes} B for {queued} queued motions; the motion table's share is at most {MotionTableBytesPerQueuedMotion} B each");
    }

    /// <summary>
    /// A bound on what M1's motion-table code allocates per motion it queues, on average: <c>MotionTableManager</c>'s
    /// pending entry (a <c>PendingMotion</c> and its <c>LinkedListNode</c>, 72 B), the boxed enumerator of
    /// <c>MotionTableLogic.AddMotion</c>'s <c>foreach</c> for each motion data it appends (32 B; a substate change
    /// appends a link and a cycle), and <c>ReModify</c>'s copy of the state while modifiers are on. The crowd measures
    /// about 94 B. TODO(avatars): 0 once the lead makes them allocation-free (requested in the M2 avatars fix pass);
    /// this test and the motion interpreter's then pin 0 bytes.
    /// </summary>
    private const long MotionTableBytesPerQueuedMotion = 200;

    private static long MotionsQueued(MotionPlayer[] players)
    {
        long total = 0;
        for (var i = 0; i < players.Length; i++)
        {
            total += players[i].MotionsQueued;
        }

        return total;
    }

    private static void Populate(AvatarSet set, IReadOnlyList<AvatarCreate> humans, IReadOnlyList<AvatarCreate> objects)
    {
        foreach (var create in humans.Concat(objects))
        {
            Assert.Equal(AvatarApply.Applied, set.Create(create, now: 0));
        }

        var walk = AvatarRig.Moving(MotionCommands.WalkForward);
        foreach (var human in humans)
        {
            Assert.Equal(AvatarApply.Applied, set.SetMovement(human.Guid, isSelf: false, autonomous: false, walk, []));
        }
    }

    private static void Measure(AvatarSet set, ITestOutputHelper output)
    {
        var now = 0.0;
        for (var frame = 0; frame < 90; frame++)
        {
            now += 1.0 / 60.0;
            set.Advance(now, Viewer, long.MaxValue);
        }

        var judged = 0;
        var worst = 0L;
        var ticks = 0L;
        for (var frame = 0; frame < 240; frame++)
        {
            now += 1.0 / 60.0;
            var start = Stopwatch.GetTimestamp();
            var window = AllocationWindow.Open();
            set.Advance(now, Viewer, long.MaxValue);
            var (bytes, gcInside) = window.Close();
            ticks += Stopwatch.GetTimestamp() - start;
            if (gcInside)
            {
                continue;
            }

            worst = Math.Max(worst, bytes);
            judged++;
        }

        output.WriteLine($"{set.Count} avatars, {set.Frame.Parts.Length} parts: {ticks * 1000.0 / Stopwatch.Frequency / 240:0.000} ms per advance");
        Assert.True(judged > 200);
        Assert.Equal(0, worst);
        Assert.Equal(set.Count, set.Frame.Avatars.Length);
    }
}
