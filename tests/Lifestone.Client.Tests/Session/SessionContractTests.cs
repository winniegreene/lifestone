using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The C-state contract (docs/session.md; m2-plan §4.7) as other tracks code against it: the event catalogue is complete
/// and every event is an allocation-free readonly struct, no contract type can carry a secret, and what cannot work
/// (the tools without a server, movement intents without a controller) fails visibly.
/// </summary>
public sealed class SessionContractTests
{
    /// <summary>
    /// The event catalogue of m2-plan §4.7, plus <see cref="CharacterSelected"/> (ST0) and <see cref="ObjectSoundPlayed"/> and
    /// <see cref="ObjectScriptPlayed"/> (ST8). Additions are allowed (update this list); removals and renames are not.
    /// </summary>
    private static readonly string[] Catalogue =
    [
        nameof(SessionPhaseChanged), nameof(ConnectionProgress), nameof(ContainerOpened), nameof(ContainerClosed), nameof(ContainerContentsChanged), nameof(CharacterListChanged), nameof(CharacterSelected),
        nameof(CharacterCreateResult), nameof(CharacterErrorReceived), nameof(Booted), nameof(PlayerCreated),
        nameof(ObjectCreated), nameof(ObjectUpdated), nameof(ObjectDeleted), nameof(ObjectMoved), nameof(ObjectMotionChanged),
        nameof(ObjectVectorChanged), nameof(ObjectStateChanged), nameof(ObjectParented), nameof(ObjectPickedUp),
        nameof(ObjectDescChanged), nameof(ObjectSoundPlayed), nameof(ObjectScriptPlayed),
        nameof(PlayerTeleportAnnounced), nameof(SelfTravelChanged), nameof(ServerTimeUpdated),
        nameof(ChatReceived), nameof(ToolReplyReceived), nameof(ToolsUnavailable), nameof(UseDone), nameof(HealthUpdated),
        nameof(QualitiesChanged), nameof(LogOffRequested), nameof(MessageDropped), nameof(UnknownMessage),
        nameof(EnchantmentChanged), nameof(SpellbookChanged), // M3 1.2 lane s1
        nameof(InventoryChanged), nameof(InventoryFailed),
        nameof(CombatEvent), nameof(CombatModeChanged), nameof(PlayerDied), nameof(PlayerKilledSomething), nameof(AutoTargetRequested), // M3 1.2 s3 (CombatEvents.cs), M3 4.2 p2
        nameof(AdminEnvironChanged), // M3 1.3 s2b (AmbientEnvirons.cs)
        nameof(AttackChanged), // M4 1.2 lane s1 (AttackSequence.cs)
        nameof(AttackRequested), // M4 1.3 lane s1 (AttackSequence.cs)
        nameof(ShortcutsChanged), // M4 3.2 lane h1 (ShortcutBar.cs)
        nameof(AppraisalReceived), // M4 3.2 lane s2 (Appraisal.cs)

        nameof(VendorOpened), nameof(VendorStockChanged), nameof(VendorClosed), nameof(CoinValueChanged), // M4 3.2 lane s1 (VendorEvents.cs)
    ];

    private static Type[] ClientTypes => typeof(IGameSessionView).Assembly.GetTypes();

    [Fact]
    public void The_event_catalogue_is_complete()
    {
        var events = ClientTypes.Where(t => typeof(IGameEvent).IsAssignableFrom(t) && t != typeof(IGameEvent)).Select(t => t.Name).Order(StringComparer.Ordinal);
        Assert.Equal(Catalogue.Order(StringComparer.Ordinal), events);
    }

    [Fact]
    public void Every_event_is_a_public_readonly_struct_in_the_contract_namespace()
    {
        foreach (var type in ClientTypes.Where(t => typeof(IGameEvent).IsAssignableFrom(t) && t != typeof(IGameEvent)))
        {
            Assert.True(type.IsValueType, $"{type.Name} must be a struct, so publishing allocates nothing");
            Assert.True(type.IsDefined(typeof(IsReadOnlyAttribute), inherit: false), $"{type.Name} must be readonly");
            Assert.True(type.IsPublic, $"{type.Name} must be public");
            Assert.Equal(typeof(IGameEvent).Namespace, type.Namespace);
        }
    }

    [Fact]
    public void No_contract_type_can_carry_a_secret()
    {
        Type[] secretTypes = [typeof(AcCredentials), typeof(char[]), typeof(Memory<char>), typeof(ReadOnlyMemory<char>), typeof(SecureString)];
        var offences = new List<string>();
        foreach (var type in ClientTypes.Where(t => t.IsPublic && t.Namespace is "Lifestone.Client.Session" or "Lifestone.Client.Session.Contracts"))
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            offences.AddRange(type.GetFields(all).Where(f => secretTypes.Contains(f.FieldType)).Select(f => $"{type.Name}.{f.Name}"));
            offences.AddRange(type.GetProperties(all).Where(p => secretTypes.Contains(p.PropertyType)).Select(p => $"{type.Name}.{p.Name}"));
        }

        Assert.True(offences.Count == 0, "a contract type exposes a secret-capable member: " + string.Join(", ", offences));
    }

    [Fact]
    public void Position_verdicts_name_the_player_and_the_rest()
    {
        var frame = default(Lifestone.Data.World.CellFrame);
        Assert.True(new ObjectMoved(1, frame, PositionVerdict.ForceBlip, false, true, null, null).IsSelf);
        Assert.True(new ObjectMoved(1, frame, PositionVerdict.SelfTeleport, true, true, null, null).IsSelf);
        Assert.True(new ObjectMoved(1, frame, PositionVerdict.SelfEcho, false, true, null, null).IsSelf);
        Assert.False(new ObjectMoved(2, frame, PositionVerdict.Remote, false, false, null, null).IsSelf);
    }

    [Theory]
    [InlineData(ActionOutcomeKind.Confirmed, true)]
    [InlineData(ActionOutcomeKind.Local, true)]
    [InlineData(ActionOutcomeKind.Sent, true)]
    [InlineData(ActionOutcomeKind.ServerError, false)]
    [InlineData(ActionOutcomeKind.TimedOut, false)]
    [InlineData(ActionOutcomeKind.Refused, false)]
    [InlineData(ActionOutcomeKind.NotImplemented, false)]
    [InlineData(ActionOutcomeKind.Cancelled, false)]
    [InlineData(ActionOutcomeKind.DryRun, false)]
    public void Only_confirmed_local_and_sent_outcomes_succeed(ActionOutcomeKind kind, bool succeeded)
    {
        Assert.Equal(succeeded, new ActionOutcome(kind).Succeeded);
    }

    [Fact]
    public void Character_errors_name_known_codes()
    {
        Assert.Equal(Lifestone.Net.Messages.CharacterErrorCode.Logon, new CharacterErrorReceived(0x01).Known);
        Assert.Null(new CharacterErrorReceived(0xDEAD).Known);
    }

    [Fact]
    public void The_offline_session_checks_its_arguments_first()
    {
        Assert.Throws<ArgumentNullException>(() => GameSession.Offline(null!, TimeProvider.System, new RecordingLogSink()));
        Assert.Throws<ArgumentNullException>(() => GameSession.Offline(_ => { }, null!, new RecordingLogSink()));
        Assert.Throws<ArgumentNullException>(() => GameSession.Offline(_ => { }, TimeProvider.System, null!));
    }

    [Fact]
    public void Offline_tools_refuse_and_unbound_move_intents_fail_loudly()
    {
        var log = new RecordingLogSink();
        using var session = GameSession.Offline(h => Lifestone.Client.Session.StandIn.StandInSessions.CharacterSelect(h), new ManualTimeProvider(), log);
        Assert.Equal(ToolAvailability.NotInWorld, session.Tools.Availability);
        var where = session.Tools.WhereAmI(ActionSource.Test);
        Assert.Equal(ActionOutcomeKind.Refused, where.Outcome!.Value.Kind);
        Assert.Null(where.Result);

        var move = session.Actions.Move;
        Assert.False(move.IsBound);
        var jump = move.Jump(ActionSource.Automation);
        Assert.Equal(ActionOutcomeKind.NotImplemented, jump.Outcome!.Value.Kind);
        Assert.Contains("TODO(state)", jump.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains(log.Entries, e => e.Severity == Lifestone.Client.Diagnostics.LogSeverity.Error && e.Message.Contains("MoveIntent Jump", StringComparison.Ordinal));
    }

    [Fact]
    public void Session_options_default_to_the_bundled_ace_on_loopback()
    {
        var a = new SessionOptions();
        Assert.Equal("127.0.0.1:9000", a.Server.ToString());
        Assert.Equal(2.0, a.BudgetMs);
        Assert.False(a.Play);
        Assert.False(a.RetailNet);
        Assert.NotSame(a.Server, new SessionOptions().Server);
    }
}
