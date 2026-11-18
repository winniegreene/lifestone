using Lifestone.Client.Session;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>How a climber's creation ends when the installed mod does not list her name: a run that deleted nothing reads <c>own-character</c> as nothing to purge, and a re-create still fails by name.</summary>
public sealed class ClimberCreatePurgeTests
{
    private const string Climber = "Qwinnie";
    private const uint OldGuid = 0x50000020;
    private const string OwnCharacterSentence = "you are playing Qwinnie; log in as another character to purge it";

    private static MageTemplate.ClimberCreationPlan Plan => MageTemplate.PlanClimberCreation(52, _ => 8, id => id == MageTemplate.WarMagic ? 28 : 20);

    private sealed class Run : IDisposable
    {
        public Run(bool existing, Action<ScriptedPurge>? purge = null)
        {
            var table = ScriptedMage.SyntheticTable();
            Scenario = new ClimberCreateScenario(HeadlessCharacterTests.StandInClasses(), Plan, Log, climber: Climber);
            Runner = HeadlessRunner.StartWith(
                new HeadlessRunOptions { Session = new SessionOptions { Account = "test" }, QuitAfterSeconds = 600 },
                Scenario,
                SessionTestData.SecretsV2(),
                SessionTestData.PidsReady(),
                () => SessionTestData.EndOfRetail,
                () => HeadlessTestWorld.Assets,
                _ => World,
                Time,
                Log,
                Recording,
                Sockets.Open,
                (_, sink, tap) =>
                {
                    Server = new ScriptedAceSession(sink, tap, Time);
                    if (existing)
                    {
                        Server.Characters.Insert(0, new Lifestone.Tests.Shared.Net.AceCharacter(OldGuid, Climber));
                    }

                    Mage = ScriptedMage.Fresh(table, Server, Climber, MageBuild.Traditional);
                    Mage.Attach();
                    Server.AnswerDeletes = true;
                    Purge = new ScriptedPurge(Server);
                    purge?.Invoke(Purge);
                    return Server;
                });
        }

        public ClimberCreateScenario Scenario { get; }

        public ScriptedPurge Purge { get; private set; } = null!;

        public ScriptedAceSession Server { get; private set; } = null!;

        public ScriptedMage Mage { get; private set; } = null!;

        public ManualTimeProvider Time { get; } = new();

        public RecordingLogSink Log { get; } = new();

        public MemoryStream Recording { get; } = new();

        public SocketFactorySpy Sockets { get; } = new();

        public HeadlessTestWorld World { get; } = new();

        public HeadlessRunner Runner { get; }

        public HeadlessRunResult Finish(double maxSeconds = 600)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone; t += 0.05)
            {
                Time.Advance(TimeSpan.FromMilliseconds(50));
                Runner.Step(Runner.Session.Clock.Now);
                if (Server.PlayerGuid is not null)
                {
                    Mage.Pump();
                }
            }

            Assert.True(Runner.IsDone, $"the run did not end within {maxSeconds} s (stage {Scenario.Stage})");
            return Runner.Result;
        }

        public void Dispose() => Runner.Dispose();
    }

    [Fact]
    public void A_first_creation_on_a_mod_that_does_not_list_her_name_logs_off_with_nothing_purged()
    {
        using var run = new Run(existing: false, purge: p => p.Refuse(ToolErrors.OwnCharacter, OwnCharacterSentence));
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("fresh=created purged=0 granted=0 template=ok level=1", run.Scenario.VerdictFields());
        Assert.Equal([Climber], run.Purge.Asked);
        Assert.Contains(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterLogOff);
        Assert.True(run.Log.AnyLineContains("answered own-character"));
    }

    [Fact]
    public void A_first_creation_on_a_mod_that_lists_her_name_logs_off_with_no_warning()
    {
        using var run = new Run(existing: false);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("fresh=created purged=0 granted=0 template=ok level=1", run.Scenario.VerdictFields());
        Assert.False(run.Log.AnyLineContains("answered own-character"));
    }

    [Fact]
    public void A_re_create_on_a_mod_that_does_not_list_her_name_still_fails_by_name()
    {
        using var run = new Run(existing: true, purge: p => p.Refuse(ToolErrors.OwnCharacter, OwnCharacterSentence));
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("purging", result.Stage);
        Assert.Contains("own-character", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.StartsWith("fresh=recreated purged=0 ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterLogOff);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 2)] // an older copy left over from before, invisible in the list, goes too
    public void A_re_create_on_a_mod_that_lists_her_deletes_the_old_copy_makes_the_new_one_and_purges_the_old_in_one_go(int expired, int purged)
    {
        using var run = new Run(existing: true, purge: p => p.ExpiredRows = expired);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Equal($"fresh=recreated purged={purged} granted=0 template=ok level=1", run.Scenario.VerdictFields());
        var left = Assert.Single(run.Server.Characters, c => c.Name == Climber);
        Assert.NotEqual(OldGuid, left.Guid);
        Assert.Equal([Climber], run.Purge.Asked.Distinct());
        Assert.Contains(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterLogOff);
    }

    [Theory]
    [InlineData(ToolErrors.Graduated, "the name is one of the graduated test characters")]
    [InlineData(ToolErrors.UnknownVerb, "the mod has no such verb")]
    public void Any_other_refusal_still_fails_a_first_creation(string code, string sentence)
    {
        using var run = new Run(existing: false, purge: p => p.Refuse(code, sentence));
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("purging", result.Stage);
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterLogOff);
    }
}
