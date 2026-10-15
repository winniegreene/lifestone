using Lifestone.Client.Avatars;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Dat;
using Lifestone.Data.Models;
using Lifestone.Data.Tests;
using Lifestone.Data.Textures;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// A <see cref="FactAttribute"/> over the real session recordings of <c>cache/sessions</c> (D14: regenerated locally, never committed) that also needs the retail
/// dats: skipped, with the reason, when either is missing.
/// </summary>
public sealed class RequiresRecordingAndDatsFactAttribute : FactAttribute
{
    public RequiresRecordingAndDatsFactAttribute()
    {
        if (RealRecordings.Find().Count == 0)
        {
            Skip = "no session recordings (*.lsrec) in cache/sessions of this checkout or the main checkout; run the game with --record-session or the headless host against the backend to make one (D14: never committed)";
        }
        else if (!DatSet.Exists())
        {
            Skip = $"Retail dats not found in '{DatSet.ResolveDirectory()}' (set {DatSet.DirectoryEnvironmentVariable}).";
        }
    }
}

/// <summary>
/// AV13 over real traffic: every recording in <c>cache/sessions</c> is replayed through the reducer and its events go through the session adapter (applied at once)
/// to an avatar set over the retail dats. When the player is present the set holds an avatar for every object the session holds and the adapter has failed on
/// none; stepping the set for a few seconds throws nothing. Skipped without recordings or dats.
/// </summary>
public sealed class SessionAvatarAdapterReplayTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresRecordingAndDatsFact]
    public void Real_recordings_replay_into_an_avatar_set_that_matches_the_sessions_objects()
    {
        var dats = fixture.Dats;
        var checkedRecordings = 0;
        foreach (var path in RealRecordings.Find())
        {
            var sources = AvatarSources.FromDats(dats, new TextureLoader(dats), new GfxObjMeshCache(dats));
            var avatars = new AvatarSet(sources);
            var replayer = new SessionReplayer();
            using var adapter = new SessionAvatarAdapter(replayer.Events, replayer.State, avatars, () => replayer.Clock.Now, immediate: true);
            var snapshots = new List<string>();
            var failures = new List<string>();
            var name = Path.GetFileName(path);
            using var watch = replayer.Events.Subscribe((in SelfTravelChanged e) =>
            {
                if (e.Travel != SelfTravel.Present)
                {
                    return;
                }

                var objects = replayer.State.Objects.Count;
                snapshots.Add($"present: session objects {objects}, avatars {avatars.Count}, census {WorldContentCensus.Count(replayer.State).Describe()}");
                if (avatars.Count != objects)
                {
                    failures.Add($"{name}: the session holds {objects} objects and the avatar set {avatars.Count}");
                }

                if (avatars.Player is null || avatars.Player.Guid != replayer.State.Self.Guid)
                {
                    failures.Add($"{name}: the avatar set has no player for guid 0x{replayer.State.Self.Guid:X8}");
                }

                foreach (var obj in replayer.State.Objects.All)
                {
                    if (!avatars.TryGet(obj.Guid, out _))
                    {
                        failures.Add($"{name}: no avatar for 0x{obj.Guid:X8} ({obj.Name})");
                    }
                }

                // The set steps and draws what it holds without a throw (a second of frames; no budget).
                var viewer = new AvatarViewer(replayer.State.Self.Position ?? default);
                for (var i = 0; i < 30; i++)
                {
                    avatars.Advance(replayer.Clock.Now + (i / 30.0), viewer, long.MaxValue);
                }
            });

            var result = replayer.Replay(new MemoryStream(File.ReadAllBytes(path)));
            output.WriteLine($"{name}: frames {result.Frames}, events applied {adapter.Stats.Applied}, ignored {adapter.Stats.Ignored}, unknown {adapter.Stats.Unknown}, failed {adapter.Stats.Failed}, avatars {avatars.Count}, slowest event {adapter.Stats.SlowestEventMilliseconds:0.0} ms");
            foreach (var snapshot in snapshots)
            {
                output.WriteLine("  " + snapshot);
            }

            Assert.True(adapter.Stats.Failed == 0, $"{name}: the adapter could not apply {adapter.Stats.Failed} event(s); {avatars.Stats.CreateFailures} create failure(s)");
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            checkedRecordings++;
        }

        Assert.True(checkedRecordings > 0);
    }
}
