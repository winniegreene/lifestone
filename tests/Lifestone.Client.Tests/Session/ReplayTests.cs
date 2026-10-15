using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.Client.Session.Recording;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// Replays (step ST14; DoD 14 "replay tests over recorded sessions"): synthetic recordings made at run time by a live
/// session over <see cref="ScriptedAceSession"/> (bodies from the shared ACE writers, Lifestone-authored values only, so
/// they run everywhere and nothing is committed, D14) replay to the digest in their trailer, twice to the same event
/// stream, and drive a whole session's handles through <see cref="ReplayAcSession"/>. Real recordings in
/// <c>cache/sessions/</c> are checked when present.
/// </summary>
public sealed class ReplayTests(ITestOutputHelper output)
{
    [Fact]
    public void Synthetic_login_replays_to_the_recorded_digest()
    {
        var recording = Synthetic.LoginEnterTalkLogOff();
        var replayer = new SessionReplayer();
        var result = replayer.Replay(new MemoryStream(recording));

        Assert.Equal(RecordingEnd.Trailer, result.End);
        Assert.True(result.Trailer!.IsIntact);
        Assert.True(result.DigestMatches, "the replayed state differs from the live one");
        Assert.Equal(0, result.Malformed);
        Assert.Equal(0, result.HandlerFailures);
        Assert.True(result.InboundMessages > 15);
        Assert.True(result.OutboundMessages > 5);
        Assert.True(result.SessionEvents > 8);
        Assert.True(result.Notices >= 3);
        Assert.Equal(SessionPhase.Closed, replayer.State.Phase);
        Assert.Equal(3, replayer.State.Characters.Entries.Count);
        Assert.Equal("Logging off...", replayer.State.Chat[replayer.State.Chat.Count - 1].Text);
    }

    /// <summary>
    /// dod-1-2-12-13-6: a message parked for 60 s whose object is created by the first message of a later tick is replayed
    /// live (the live session expires at the end of its tick); the replay must apply it too, not drop it as expired before the
    /// creating frame. The live and the replayed digests then agree, and the object carries the parked state.
    /// </summary>
    [Fact]
    public void A_parked_message_that_comes_due_with_its_object_creation_replays_as_live()
    {
        const uint Late = 0x80000999;
        const uint Ethereal = (uint)PhysicsState.Ethereal;
        using var h = new LiveHarness();
        h.Present();
        h.Server.Inject(AceWireWriters.SetState(Late, Ethereal, 1, 2));
        h.Run();
        Assert.Equal(1, h.Session.Reducer.Parked.CountFor(Late));

        // Frames of 50 ms until just short of the expiry, then one tick that starts after it and carries the creation.
        h.Run(PendingObjectMessages.ExpirySeconds - 0.3);
        Assert.Equal(1, h.Session.Reducer.Parked.CountFor(Late));
        h.Time.Advance(TimeSpan.FromSeconds(0.4));
        h.Server.Inject(AceWireWriters.ObjectCreate(new AceCreateObject
        {
            Guid = Late,
            PhysicsState = (uint)(PhysicsState.Gravity | PhysicsState.ReportCollisions),
            Position = new AcePosition(ScriptedAceSession.AcademyCell, 60f, -20f, 0.005f),
            Setup = 0x02000001,
            Sequences = [1, 1, 1, 1, 1, 1, 1, 1, 1],
            Name = "Late arrival",
            WeenieClassId = 150,
            Icon = 0x06001036,
        }));
        h.Session.Tick(h.Session.Clock.Now);
        Assert.Equal(0, h.Session.Reducer.Counters.ParkingExpired);
        Assert.True(h.State.Objects.TryGet(Late, out var live));
        Assert.Equal(PhysicsState.Ethereal, live.State);
        h.Run();

        var digest = GameStateDigest.Compute(h.State);
        h.Dispose();
        var replayer = new SessionReplayer();
        var expired = 0;
        replayer.Events.Subscribe((in MessageDropped e) => expired += e.Reason == DropReason.ParkingExpired ? 1 : 0);
        var result = replayer.Replay(new MemoryStream(h.RecordingBytes()));
        Assert.Equal(0, expired);
        Assert.True(replayer.State.Objects.TryGet(Late, out var replayed));
        Assert.Equal(PhysicsState.Ethereal, replayed.State);
        Assert.True(result.DigestMatches, "the replayed state differs from the live one");
        Assert.Equal(digest, GameStateDigest.Compute(replayer.State));
    }

    [Fact]
    public void Replaying_twice_gives_identical_event_hashes()
    {
        var recording = Synthetic.LoginEnterTalkLogOff();
        var first = EventStreamHash(recording, out var firstCount);
        var second = EventStreamHash(recording, out var secondCount);
        Assert.True(firstCount > 30, $"only {firstCount} events were replayed");
        Assert.Equal(firstCount, secondCount);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Client_messages_reach_the_outbound_check_and_change_nothing()
    {
        var recording = Synthetic.LoginEnterTalkLogOff();
        var seen = new List<(uint Opcode, uint Action, bool Omitted)>();
        var replayer = new SessionReplayer { OutboundCheck = m => seen.Add((m.Opcode, GameActionBodies.TypeOf(m.Body.Span), m.BodyOmitted)) };
        replayer.Replay(new MemoryStream(recording));
        Assert.Contains((MessageOpcodes.CharacterEnterWorldRequest, 0u, false), seen);
        Assert.Contains((MessageOpcodes.GameAction, GameActionTypes.LoginComplete, false), seen);
        Assert.Contains((MessageOpcodes.GameAction, GameActionTypes.Talk, false), seen);
        Assert.Contains((MessageOpcodes.CharacterLogOff, 0u, false), seen);
    }

    [Fact]
    public void A_recording_cut_short_replays_to_its_last_whole_frame()
    {
        var recording = Synthetic.LoginEnterTalkLogOff();
        var cut = recording.AsSpan(0, recording.Length * 2 / 3).ToArray();
        var result = new SessionReplayer().Replay(new MemoryStream(cut));
        Assert.Equal(RecordingEnd.Truncated, result.End);
        Assert.Null(result.DigestMatches);
        Assert.True(result.Frames > 10);
    }

    [Fact]
    public void A_replayer_replays_one_recording()
    {
        var recording = Synthetic.LoginEnterTalkLogOff();
        var replayer = new SessionReplayer();
        replayer.Replay(new MemoryStream(recording));
        Assert.Throws<InvalidOperationException>(() => replayer.Replay(new MemoryStream(recording)));
    }

    [Fact]
    public void ReplayAcSession_completes_EnterWorld_and_LoginComplete_handles()
    {
        var recording = Synthetic.LoginEnterTalkLogOff(out var liveDigest);
        var frames = ReplayFrame.Load(new MemoryStream(recording));
        var time = new ManualTimeProvider();
        var log = new RecordingLogSink();
        using var session = ReplayAcSession.CreateSession(frames, time, log, out var replay);
        void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.05)
            {
                time.Advance(TimeSpan.FromMilliseconds(50));
                session.Tick(session.Clock.Now);
            }
        }

        // Nothing arrives until the client logs in.
        Run(1);
        Assert.Equal(SessionPhase.Idle, session.State.Phase);
        Assert.Equal(0, replay.Position);

        var login = session.Actions.Login();
        Run(1);
        Assert.Equal(ActionOutcomeKind.Confirmed, login.Outcome?.Kind);
        var enter = session.Actions.EnterWorld(ScriptedAceSession.Amy, ActionSource.Host);
        Run(1);
        Assert.Equal(ActionOutcomeKind.Confirmed, enter.Outcome?.Kind);
        Assert.Equal(SelfTravel.Arriving, session.State.Self.Travel);

        var complete = session.Actions.LoginComplete();
        Run(1);
        Assert.Equal(ActionOutcomeKind.Confirmed, complete.Outcome?.Kind);
        Assert.Equal(SelfTravel.Present, session.State.Self.Travel);

        var talk = session.Actions.Talk(Synthetic.Said, ActionSource.Ui);
        Run(1);
        Assert.Equal(ActionOutcomeKind.Confirmed, talk.Outcome?.Kind);
        var logOff = session.Actions.LogOff(ActionSource.Ui);
        Run(2);
        Assert.Equal(ActionOutcomeKind.Confirmed, logOff.Outcome?.Kind);
        var quit = session.Actions.Quit(ActionSource.Ui);
        Run(1);
        Assert.Equal(ActionOutcomeKind.Sent, quit.Outcome?.Kind);

        Assert.Empty(replay.Divergences);
        Assert.True(replay.IsComplete, $"the replay stopped at frame {replay.Position} of {frames.Count}");
        Assert.Equal(liveDigest, GameStateDigest.Compute(session.State));
    }

    [Fact]
    public void ReplayAcSession_counts_a_send_the_recording_did_not_make()
    {
        var recording = Synthetic.LoginEnterTalkLogOff();
        var frames = ReplayFrame.Load(new MemoryStream(recording));
        var time = new ManualTimeProvider();
        var log = new RecordingLogSink();
        using var session = ReplayAcSession.CreateSession(frames, time, log, out var replay);
        void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.05)
            {
                time.Advance(TimeSpan.FromMilliseconds(50));
                session.Tick(session.Clock.Now);
            }
        }

        session.Actions.Login();
        Run(1);
        session.Actions.EnterWorld(ScriptedAceSession.Amy, ActionSource.Host);
        Run(1);
        session.Actions.LoginComplete();
        Run(1);
        Assert.Equal(SelfTravel.Present, session.State.Self.Travel);

        // The recording's next client step is the chat line; a Use is not it.
        session.Actions.Use(ScriptedAceSession.FirstObject, ActionSource.Ui);
        Run(0.5);
        var talk = session.Actions.Talk(Synthetic.Said, ActionSource.Ui);
        Run(1);

        var divergence = Assert.Single(replay.Divergences);
        Assert.Contains("action 0x0036", divergence, StringComparison.Ordinal);
        Assert.Contains("action 0x0015", divergence, StringComparison.Ordinal);
        Assert.Contains(log.Entries, e => e.Message.StartsWith("replay divergence", StringComparison.Ordinal));
        Assert.Equal(ActionOutcomeKind.Confirmed, talk.Outcome?.Kind);
    }

    [RequiresRecordingFact]
    public void Real_recordings_replay_to_their_trailer_digest()
    {
        foreach (var incomplete in RealRecordings.Incomplete())
        {
            output.WriteLine($"left out (ends before its header, not a recording): {Path.GetFileName(incomplete)}");
        }

        foreach (var path in RealRecordings.Find())
        {
            var bytes = File.ReadAllBytes(path);
            var first = new SessionReplayer().Replay(new MemoryStream(bytes));
            var name = Path.GetFileName(path);
            Assert.True(first.Malformed == 0, $"{name}: net could not parse {first.Malformed} server message(s): {string.Join(", ", first.MalformedOpcodes.Select(o => "0x" + o.ToString("X4", CultureInfo.InvariantCulture)))}");
            Assert.True(first.HandlerFailures == 0, $"{name}: {first.HandlerFailures} handler failure(s)");
            if (first.End == RecordingEnd.Trailer)
            {
                Assert.True(first.Trailer!.IsIntact, $"{name}: the trailer does not match the frames");
                Assert.True(first.DigestMatches is null or true, $"{name}: the replayed state differs from the recorded digest");
            }

            Assert.Equal(EventStreamHash(bytes, out _), EventStreamHash(bytes, out _));
        }
    }

    /// <summary>The SHA-256 of every event's text, in publish order, over a replay of <paramref name="recording"/>.</summary>
    private static string EventStreamHash(byte[] recording, out int count)
    {
        var replayer = new SessionReplayer();
        var texts = new List<string>();
        var track = typeof(ReplayTests).GetMethod(nameof(Track), BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var type in typeof(IGameEvent).Assembly.GetTypes().Where(t => t.IsValueType && typeof(IGameEvent).IsAssignableFrom(t)))
        {
            track.MakeGenericMethod(type).Invoke(null, [replayer.Events, texts]);
        }

        replayer.Replay(new MemoryStream(recording));
        count = texts.Count;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', texts))));
    }

    private static void Track<T>(IEventBus bus, List<string> texts)
        where T : struct, IGameEvent =>
        bus.Subscribe((in T e) => texts.Add(typeof(T).Name + " " + string.Format(CultureInfo.InvariantCulture, "{0}", e)));

    /// <summary>Synthetic recordings, made by a live session over the scripted server.</summary>
    internal static class Synthetic
    {
        /// <summary>What the synthetic session says.</summary>
        public const string Said = "Hello from a synthetic recording";

        /// <summary>A login, entering the world, LoginComplete, the status probe, a chat line, a logoff and a quit.</summary>
        public static byte[] LoginEnterTalkLogOff() => LoginEnterTalkLogOff(out _);

        /// <summary><see cref="LoginEnterTalkLogOff()"/>, with the live session's final digest.</summary>
        public static byte[] LoginEnterTalkLogOff(out byte[] digest)
        {
            var h = new LiveHarness();
            h.Present();
            var talk = h.Actions.Talk(Said, ActionSource.Ui);
            Assert.True(h.RunUntil(() => talk.IsCompleted));
            var logOff = h.Actions.LogOff(ActionSource.Ui);
            Assert.True(h.RunUntil(() => logOff.IsCompleted));
            var quit = h.Actions.Quit(ActionSource.Ui);
            h.Run(0.5);
            Assert.Equal(ActionOutcomeKind.Sent, quit.Outcome?.Kind);
            digest = GameStateDigest.Compute(h.State);
            h.Dispose();
            return h.RecordingBytes();
        }
    }
}
