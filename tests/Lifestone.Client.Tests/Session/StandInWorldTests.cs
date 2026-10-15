using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Ui;
using Lifestone.Data.World;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="StandInSessions.AnswerWorld"/> and its siblings (m2-plan G6 W11): what the play-mode checks use as a server for the online flows. A server-mod
/// place request, a Use of a portal object and a log off are answered through the reducer, a boot and a silent server end the session in failure, and the
/// log-off key (<see cref="EndSessionKey"/>) calls <c>Actions.LogOff</c>. A session that did not opt in refuses all of it, as every offline session does.
/// </summary>
public sealed class StandInWorldTests
{
    private const uint Holtburg = 0xA9B40019;
    private const uint Academy = 0x860201AD;
    private const uint Portal = 0x80000A03;

    private static readonly StandInVisit Visit = new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        Objects = [new StandInObject(Portal, "Portal", new Vector3(-3f, 0f, 0f), MotionTable: 0, PortalTo: new StandInTeleport(0, Academy, new Vector3(12.3199f, -28.482f, 0.005f)))],
        LogOffDelay = 1.0,
    };

    private static List<ChatLine> Lines(GameSession session)
    {
        var lines = new List<ChatLine>();
        for (var i = 0; i < session.State.Chat.Count; i++)
        {
            lines.Add(session.State.Chat[i]);
        }

        return lines;
    }

    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();

        public Rig(bool answerWorld = true)
        {
            Session = GameSession.Offline(
                h =>
                {
                    Handler = h;
                    StandInSessions.CharacterSelect(h);
                    StandInSessions.PlayerVisits(h, Visit);
                    if (answerWorld)
                    {
                        StandInSessions.AnswerWorld(h, Visit);
                    }
                },
                _time,
                new RecordingLogSink());
            Session.Events.Subscribe((in PlayerTeleportAnnounced e) => Teleports.Add(e.TeleportSequence));
            Session.Events.Subscribe((in ObjectMoved e) =>
            {
                if (e.IsSelf)
                {
                    Moves.Add(e.Position);
                }
            });
            Session.Events.Subscribe((in Booted e) => BootReason = e.Reason ?? "(none)");
        }

        public GameSession Session { get; }

        public IInboundMessageHandler Handler { get; private set; } = null!;

        public List<ushort> Teleports { get; } = [];

        public List<CellFrame> Moves { get; } = [];

        public string? BootReason { get; private set; }

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
            }
        }

        public void EnterWorld() => Run(2.0); // the visit enters at 0.2 s and unhides at 1.2 s

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void Plain_speech_is_heard_back_as_the_players_own_and_confirms_the_Talk()
    {
        using var rig = new Rig();
        rig.EnterWorld();

        var talk = rig.Session.Actions.Talk("hello there", ActionSource.Ui);
        rig.Run(1.0);

        Assert.True(talk.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Confirmed, talk.Outcome!.Value.Kind);
        var line = Assert.Single(Lines(rig.Session), l => l.Kind == ChatKind.Speech);
        Assert.Equal(("hello there", rig.Session.State.Self.Guid), (line.Text, line.SenderGuid));
    }

    [Fact]
    public void A_place_request_is_answered_with_a_reply_and_a_teleport()
    {
        using var rig = new Rig();
        rig.EnterWorld();
        var holtburg = TeleportCatalog.Default().Find("Holtburg")!;

        var place = rig.Session.Tools.Place(holtburg, ActionSource.Host);
        rig.Run(1.0);

        Assert.True(place.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Confirmed, place.Outcome!.Value.Kind);
        Assert.Equal(Holtburg, place.Result!.Destination.CellId);
        Assert.Single(rig.Teleports);
        Assert.Equal(Holtburg, rig.Moves[^1].CellId);
        Assert.Contains(Lines(rig.Session), l => l.Kind == ChatKind.ToolReply);
    }

    [Fact]
    public void A_place_by_location_goes_to_that_cell()
    {
        using var rig = new Rig();
        rig.EnterWorld();
        var inn = new TeleportDestination("Somewhere", [], new Lifestone.Client.World.WorldLocation("Somewhere", new Lifestone.Client.World.CellPosition(Academy, new Vector3(12.3199f, -28.482f, 0.005f)), true, 0.0), "test");

        var place = rig.Session.Tools.Place(inn, ActionSource.Host);
        rig.Run(1.0);

        Assert.Equal(ActionOutcomeKind.Confirmed, place.Outcome!.Value.Kind);
        Assert.Equal(Academy, rig.Moves[^1].CellId);
    }

    [Fact]
    public void A_purge_of_a_pending_deletion_character_is_confirmed_and_the_character_is_gone_from_the_list_and_after_a_log_off()
    {
        using var rig = new Rig();
        rig.EnterWorld();
        var before = rig.Session.State.Characters.Entries.Select(c => c.Name).ToArray();
        Assert.Equal(["Stand-in Alpha", "Stand-in Beta", "Stand-in Gamma"], before);

        var purge = rig.Session.Tools.PurgeCharacter("stand-in gamma", ActionSource.Host);
        rig.Run(1.0);

        Assert.True(purge.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Confirmed, purge.Outcome!.Value.Kind);
        Assert.Equal(new CharPurgeResult("Stand-in Gamma", 1, 1, 0), purge.Result);
        Assert.Equal(["Stand-in Alpha", "Stand-in Beta"], rig.Session.State.Characters.Entries.Select(c => c.Name).ToArray());
        Assert.Contains(Lines(rig.Session), l => l.Kind == ChatKind.ToolReply && l.Text.StartsWith("Lifestone: purged Stand-in Gamma", StringComparison.Ordinal));

        // A second purge of the same name finds nothing; the log off lists the others only.
        var again = rig.Session.Tools.PurgeCharacter("Stand-in Gamma", ActionSource.Host);
        rig.Run(1.0);
        Assert.Equal("unknown-character: no character named \"Stand-in Gamma\" is on your account", again.Outcome!.Value.Detail);

        rig.Session.Actions.LogOff(ActionSource.Host);
        rig.Run(3.0);
        Assert.Equal(SessionPhase.CharacterSelect, rig.Session.State.Phase);
        Assert.Equal(["Stand-in Alpha", "Stand-in Beta"], rig.Session.State.Characters.Entries.Select(c => c.Name).ToArray());
    }

    [Theory]
    [InlineData("Lifestone Smoke", "graduated: Lifestone Smoke is a graduated test character and stays as it is")]
    [InlineData("LIFESTONE LOOPBACK", "graduated: Lifestone Loopback is a graduated test character and stays as it is")]
    [InlineData("Stand-in Alpha", "own-character: you are playing Stand-in Alpha; log in as another character to purge it")]
    [InlineData("Nobody", "unknown-character: no character named \"Nobody\" is on your account")]
    [InlineData("Stand-in Beta", "not-pending-deletion: Stand-in Beta is not marked for deletion; delete it on the character screen first")]
    public void A_purge_the_mod_s_rules_refuse_is_refused_with_the_same_code_and_words(string name, string detail)
    {
        using var rig = new Rig();
        rig.EnterWorld();

        var purge = rig.Session.Tools.PurgeCharacter(name, ActionSource.Host);
        rig.Run(1.0);

        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0, detail), purge.Outcome);
        Assert.Null(purge.Result);
        Assert.Equal(["Stand-in Alpha", "Stand-in Beta", "Stand-in Gamma"], rig.Session.State.Characters.Entries.Select(c => c.Name).ToArray());
    }

    [Fact]
    public void A_use_of_a_portal_object_is_confirmed_and_teleports_the_player()
    {
        using var rig = new Rig();
        rig.EnterWorld();

        var use = rig.Session.Actions.Use(Portal, ActionSource.Host);
        rig.Run(1.0);

        Assert.Equal(ActionOutcomeKind.Confirmed, use.Outcome!.Value.Kind);
        Assert.Contains("teleport", use.Outcome!.Value.Detail, StringComparison.Ordinal);
        Assert.Single(rig.Teleports);
        Assert.Equal(Academy, rig.Moves[^1].CellId);
    }

    [Fact]
    public void A_use_of_another_object_is_confirmed_and_moves_nothing()
    {
        using var rig = new Rig();
        rig.EnterWorld();

        var use = rig.Session.Actions.Use(0x80000FFF, ActionSource.Host);
        rig.Run(1.0);

        Assert.Equal(ActionOutcomeKind.Confirmed, use.Outcome!.Value.Kind);
        Assert.Empty(rig.Teleports);
    }

    [Fact]
    public void A_log_off_through_the_key_returns_to_the_character_list()
    {
        using var rig = new Rig();
        rig.EnterWorld();
        var log = new RecordingLogSink();
        var key = new EndSessionKey(rig.Session, log);

        Assert.True(key.OnInputAction(EndSessionKey.InputMap, EndSessionKey.Action, new RetailActivation(true, 1f, RetailToggleType.Impulse, 1, 0x80000000)));
        Assert.Equal(1, key.Requests);
        Assert.NotNull(key.Last);
        rig.Run(3.0);

        Assert.True(key.Last!.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Confirmed, key.Last.Outcome!.Value.Kind);
        Assert.Equal(SessionPhase.CharacterSelect, rig.Session.State.Phase);
        Assert.Contains(Lines(rig.Session), l => l.Text.StartsWith("Logging off", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Entries, e => e.Message.Contains("TODO(g6-logoff-confirm)", StringComparison.Ordinal)); // retail-exact: the key never asks
        Assert.Contains(log.Entries, e => e.Message.Contains("without asking", StringComparison.Ordinal));
    }

    [Fact]
    public void The_key_does_nothing_out_of_the_world_and_ignores_other_actions_and_the_release()
    {
        using var rig = new Rig();
        var key = new EndSessionKey(rig.Session, new RecordingLogSink());
        var down = new RetailActivation(true, 1f, RetailToggleType.Impulse, 1, 0x80000000);

        Assert.False(key.OnInputAction(EndSessionKey.InputMap, EndSessionKey.Action, down)); // at the character list
        rig.EnterWorld();
        Assert.False(key.OnInputAction(EndSessionKey.InputMap, 0x27, down)); // Escape's own action
        Assert.False(key.OnInputAction(UiInputRouter.MovementMap, EndSessionKey.Action, down)); // another map
        Assert.True(key.OnInputAction(EndSessionKey.InputMap, EndSessionKey.Action, down with { Start = false })); // the release
        Assert.Equal(0, key.Requests);
    }

    [Fact]
    public void A_boot_ends_the_session_in_failure_with_the_reason()
    {
        using var rig = new Rig();
        rig.EnterWorld();

        StandInSessions.BootLater(rig.Handler, 0.2, "stand-in boot");
        rig.Run(1.0);

        Assert.Equal("stand-in boot", rig.BootReason);
        Assert.Equal(SessionPhase.Failed, rig.Session.State.Phase);
        Assert.Equal(SessionFailureKind.Booted, rig.Session.State.Failure!.Kind);
        Assert.True(rig.Session.State.ConnectionLost);
    }

    [Fact]
    public void A_silent_server_ends_the_session_and_a_reconnect_runs_the_visit_again()
    {
        using var rig = new Rig();
        rig.EnterWorld();

        StandInSessions.FailLater(rig.Handler, 0.2, new SessionFailure(SessionFailureKind.Silence, SessionPhase.InWorld, "went quiet"));
        rig.Run(1.0);
        Assert.Equal(SessionFailureKind.Silence, rig.Session.State.Failure!.Kind);

        rig.Session.Reconnect();
        rig.Run(0.1);
        Assert.Equal(1, rig.Session.Reconnects);
        Assert.False(rig.Session.State.ConnectionLost);
        rig.EnterWorld();
        Assert.Equal(SessionPhase.InWorld, rig.Session.State.Phase);
    }

    [Fact]
    public void A_session_that_did_not_opt_in_refuses_the_world_s_actions()
    {
        using var rig = new Rig(answerWorld: false);
        rig.EnterWorld();

        var use = rig.Session.Actions.Use(Portal, ActionSource.Host);
        var logOff = rig.Session.Actions.LogOff(ActionSource.Host);
        var place = rig.Session.Tools.Place(TeleportCatalog.Default().Find("Holtburg")!, ActionSource.Host);
        rig.Run(1.0);

        Assert.Equal(ActionOutcomeKind.Refused, use.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, logOff.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, place.Outcome!.Value.Kind);
        Assert.Empty(rig.Teleports);
    }
}
