using Lifestone.Client.Options;
using Lifestone.Client.Play;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 onscreen start (owner ruling 2026-11-19: the sprint's whole run in one game window): the character-creation plan read from the climber's template, the creation scenario's on-screen path (through a fake of the screens over the
/// scripted server), the in-process gate before the level up job, the buff bot's journey facts every host sets, and the option and account rules of <c>--login-check=climber-start</c>. No live server.
/// </summary>
public sealed class ClimberOnScreenStartTests
{
    private const string Climber = "Qwinnie";
    private const uint OldGuid = 0x50000020;

    private static MageTemplate.ClimberCreationPlan Plan(bool buffBot = true) => MageTemplate.PlanClimberCreation(52, _ => 8, id => id == MageTemplate.WarMagic ? 28 : 20, buffBot);

    // ------------------------------------------------------------------------------------------------ the plan

    [Theory]
    [InlineData("Qwinnie", true)]
    [InlineData("Qwinnie", false)]
    [InlineData("Winnie", true)]
    public void The_chargen_plan_is_the_climbers_creation_intent_heritage_gender_attributes_skills_and_their_order(string climber, bool buffBot)
    {
        var classes = HeadlessCharacterTests.StandInClasses();
        var credits = Plan(buffBot);
        var plan = ClimberChargenPlan.From(climber, classes, credits);
        var intent = MageTemplate.ClimberCreationIntent(climber, classes, credits);

        Assert.Equal(climber, plan.Name);
        Assert.Equal(HeadlessCharacter.Heritage, plan.Heritage); // Aluvian
        Assert.Equal(HeadlessCharacter.Gender, plan.Gender); // male
        Assert.Equal(0, plan.StartArea); // Holtburg's Training Academy
        Assert.Equal([intent.Strength, intent.Endurance, intent.Quickness, intent.Coordination, intent.Focus, intent.Self], plan.Attributes.Select(a => (uint)a));
        foreach (var id in ClimberChargenPlan.AttributeIds)
        {
            Assert.Equal((int)MageTemplate.ClimberCreationValue(climber, id), plan.AttributeTarget(id)); // the policy's values (Qwinnie: Endurance 100, Coordination 10; Winnie: the other way)
        }

        Assert.Equal(intent.SkillAdvancementClasses, plan.Skills);
        Assert.Equal(HeadlessCharacter.UntrainedSkill, plan.Skills[(int)MageTemplate.MeleeDefense]);
        Assert.Equal(credits.Paid, plan.RaiseOrder); // the order the up arrows are pressed in is the plan's
        Assert.Equal(MageTemplate.WarMagic, plan.RaiseOrder[0].Skill);
        Assert.Equal(HeadlessCharacter.SpecializedSkill, plan.RaiseOrder[0].Class);
        Assert.Equal(MageTemplate.ClimberOrder(buffBot).Where(s => credits.ClassOf(s) is not null), plan.RaiseOrder.Skip(1).Select(p => p.Skill));
        Assert.Empty(plan.Mismatches(intent));
        Assert.Contains("skills in order: War Magic specialised", plan.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_plan_names_each_way_a_built_character_differs_from_the_template_and_ignores_what_the_screens_choose()
    {
        var classes = HeadlessCharacterTests.StandInClasses();
        var plan = ClimberChargenPlan.From(Climber, classes, Plan());
        var intent = MageTemplate.ClimberCreationIntent(Climber, classes, Plan());

        Assert.Empty(plan.Mismatches(intent with { TemplateOption = 4, ClassId = 9, Name = "x", Appearance = intent.Appearance with { HairStyle = 3 } })); // the screens' own
        var skills = intent.SkillAdvancementClasses.ToArray();
        skills[MageTemplate.MeleeDefense] = HeadlessCharacter.TrainedSkill;
        var wrong = plan.Mismatches(intent with { Gender = 2, StartArea = 1, Focus = 50, SkillAdvancementClasses = skills });
        Assert.Equal(4, wrong.Count);
        Assert.Contains("gender 2, the template 1", wrong);
        Assert.Contains("start area 1, the template 0", wrong);
        Assert.Contains("Focus 50, the template 100", wrong);
        Assert.Contains(wrong, w => w.StartsWith(MageTemplate.SkillName(MageTemplate.MeleeDefense) + " trained, the template untrained", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ the gate

    private const string GoodCreation = "verdict fresh=recreated purged=1 granted=0 template=ok level=1";
    private const string GoodDone = "journey done ok=true name=qwinnie stations=2/2 failed=none granted=0 places=0";
    private const string GoodClimber = "journey climber cheat_free=yes level=6 granted=0 place=0 recovery_place=0 stop=none";

    [Fact]
    public void The_gate_allows_the_level_up_job_only_on_a_clean_creation_and_a_cheat_free_journey()
    {
        var allowed = ClimberStartGate.Decide(true, GoodCreation, GoodDone, GoodClimber);
        Assert.True(allowed.Allowed, allowed.Why);
        Assert.Equal("climber start: gate level_up=allowed creation=[fresh=recreated purged=1 granted=0 template=ok level=1] journey=[" + GoodClimber + "]", allowed.Line(GoodCreation, GoodClimber));
    }

    [Theory]
    [InlineData(false, GoodCreation, GoodDone, GoodClimber, "the creation did not finish ok")]
    [InlineData(true, "verdict fresh=created purged=0 granted=2 template=ok level=1", GoodDone, GoodClimber, "the creation read granted=2, not 0")]
    [InlineData(true, "verdict fresh=created purged=0 template=ok level=1", GoodDone, GoodClimber, "the creation read granted=unread, not 0")]
    [InlineData(true, "verdict fresh=created purged=0 granted=0 template=short level=1", GoodDone, GoodClimber, "the creation read template=short")]
    [InlineData(true, null, GoodDone, GoodClimber, "no creation verdict line")]
    [InlineData(true, GoodCreation, "journey done ok=false name=qwinnie", GoodClimber, "the journey done line is not ok=true")]
    [InlineData(true, GoodCreation, null, GoodClimber, "no 'journey done' line")]
    [InlineData(true, GoodCreation, GoodDone, "journey climber cheat_free=no level=6 granted=1 place=0 recovery_place=0 stop=none", "the journey read granted=1, not 0; the journey read cheat_free=no, not yes")]
    [InlineData(true, GoodCreation, GoodDone, "journey climber cheat_free=yes level=6 granted=0 place=1 recovery_place=2 stop=none", "the journey read place=1, not 0; the journey read recovery_place=2, not 0")]
    [InlineData(true, GoodCreation, GoodDone, "journey climber cheat_free=yes level=6", "the journey read granted=unread, not 0; the journey read place=unread, not 0; the journey read recovery_place=unread, not 0")]
    [InlineData(true, GoodCreation, GoodDone, null, "no 'journey climber' line")]
    public void The_gate_refuses_by_name_and_a_count_no_line_gave_is_a_refusal(bool createdOk, string? creation, string? done, string? climber, string why)
    {
        var refused = ClimberStartGate.Decide(createdOk, creation, done, climber);

        Assert.False(refused.Allowed);
        Assert.Equal(why, refused.Why);
        Assert.Equal("climber start: gate level_up=refused why=" + why, refused.Line(creation, climber));
    }

    // ------------------------------------------------------------------------------------------------ the buff bot's journey facts

    [Fact]
    public void The_buff_bot_facts_are_set_from_the_autopilots_option_for_the_climbers_journeys_and_left_off_for_the_others()
    {
        foreach (var plan in JourneyPlans.Climbers)
        {
            var on = new JourneyFacts();
            JourneyBotFacts.Apply(on, plan, []);
            Assert.True(on.BuffBotOn!()); // on by default, as the autopilot's option
            Assert.Same(BuffBotWire.Default, on.BuffBotProfile);

            var off = new JourneyFacts();
            JourneyBotFacts.Apply(off, plan, ["--autopilot-buff-bot=off", "--autopilot-buff-bot-word=tusker"]);
            Assert.False(off.BuffBotOn!());
            Assert.Same(BuffBotWire.ProfileOf("tusker"), off.BuffBotProfile);
        }

        var mage = new JourneyFacts();
        JourneyBotFacts.Apply(mage, JourneyPlans.Mage, []);
        Assert.Null(mage.BuffBotOn); // no Pathwarden station: no stop, as before
    }

    [Fact]
    public void The_game_windows_journey_parts_and_the_headless_journey_set_the_buff_bot_facts_the_same_way()
    {
        var game = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "JourneyLegRunner.cs"));
        var headless = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Client", "Session", "Scenarios", "Journey", "HeadlessJourneyRunnerHost.cs"));

        Assert.Contains("JourneyBotFacts.Apply(context.Facts, plan, arguments);", game, StringComparison.Ordinal); // BuildParts: the journey check, the climber start and the autopilot build one way
        Assert.Contains("JourneyBotFacts.Apply(context.Facts, Plan, arguments);", headless, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ the option and the accounts

    [Fact]
    public void The_climber_start_is_a_login_check_of_its_own_on_a_climbers_account_and_its_wait_file_is_its_alone()
    {
        var qwinnie = PlayOptions.Parse(["--login-check=climber-start", "--account=qwinnie", "--climber-start-wait-file=cache/m6/climb/x/record-go"]);
        Assert.Empty(qwinnie.Errors);
        Assert.Equal(LoginCheckKind.ClimberStart, qwinnie.LoginCheck);
        Assert.Equal("cache/m6/climb/x/record-go", qwinnie.ClimberStartWaitFile);

        Assert.Empty(PlayOptions.Parse(["--login-check=climber-start"]).Errors); // no --account: the test account, Winnie's
        Assert.Contains(PlayOptions.Parse(["--login-check=climber-start", "--account=test-melee"]).Errors, e => e.Contains("climber's account", StringComparison.Ordinal));
        Assert.Contains(PlayOptions.Parse(["--climber-start-wait-file=cache/x"]).Errors, e => e.Contains("only --login-check=climber-start reads it", StringComparison.Ordinal));

        Assert.True(AutomatedAccountRule.TryLoginCheck(TestAccounts.Qwinnie, LoginCheckKind.ClimberStart, out var none), none);
        Assert.True(AutomatedAccountRule.TryLoginCheck(TestAccounts.Test, LoginCheckKind.ClimberStart, out none), none);
        Assert.False(AutomatedAccountRule.TryLoginCheck(TestAccounts.Tools, LoginCheckKind.ClimberStart, out var refusal));
        Assert.Contains("climber", refusal, StringComparison.Ordinal);
        Assert.False(AutomatedAccountRule.TryLoginCheck(TestAccounts.Melee, LoginCheckKind.ClimberStart, out _));
    }

    [Fact]
    public void The_options_the_one_window_run_passes_are_registered()
    {
        var catalog = string.Concat(Directory.GetFiles(RepositoryFiles.PathOf("src", "Lifestone.Client", "Options"), "OptionCatalog*.cs").Select(File.ReadAllText));
        foreach (var option in new[] { "--login-check", "--video-pace", "--climber-start-wait-file", "--video-timer" })
        {
            Assert.Contains($"new(\"{option}\"", catalog, StringComparison.Ordinal);
        }

        Assert.Contains("'--login-check=climber-start'", catalog, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ the creation on screen

    /// <summary>The screens' fake: the list's select and delete and the summary page's Finish act as the real clicks end (the delete and the create through the server, and the client entering with the new character at once).</summary>
    private sealed class FakeScreens(Func<IGameSessionView> session, MageTemplate.ClimberCreationPlan plan) : IClimberScreens
    {
        private string? _selected;

        public List<string> Calls { get; } = [];

        public ClimberChargenPlan? Opened { get; private set; }

        public string? RefuseWith { get; set; }

        public ScreenOp SelectCharacter(string name)
        {
            Calls.Add("select");
            _selected = name;
            return ScreenOp.Completed();
        }

        public ScreenOp DeleteSelected()
        {
            Calls.Add("delete");
            var s = session();
            var slot = s.State.Characters.Entries.First(c => string.Equals(c.Name, _selected, StringComparison.OrdinalIgnoreCase) && !c.PendingDeletion);
            var handle = s.Actions.DeleteCharacter(slot.Guid, ActionSource.Ui);
            return new ScreenFrameOp(op => Await(op, handle, () => s.State.Characters.Entries.Any(c => c.Guid == slot.Guid && c.PendingDeletion)));
        }

        public ScreenOp OpenCharGenAs(ClimberChargenPlan chargen)
        {
            Calls.Add("chargen");
            Opened = chargen;
            return ScreenOp.Completed();
        }

        public ScreenOp Finish(string name)
        {
            Calls.Add("finish");
            if (RefuseWith is { } text)
            {
                return ScreenOp.Completed(text);
            }

            var s = session();
            var create = s.Actions.CreateCharacter(MageTemplate.ClimberCreationIntent(name, HeadlessCharacterTests.StandInClasses(), plan), ActionSource.Ui); // what the summary page holds (ClimberChargenDriverTests)
            return new ScreenFrameOp(op => FinishSteps(op, s, create, name));
        }

        private static IEnumerable<object?> Await(ScreenFrameOp op, ActionHandle handle, Func<bool> listed)
        {
            yield return new ScreenFrameOp.Until(() => handle.IsCompleted, 30, "the delete's answer");
            if (handle.Outcome?.Kind != ActionOutcomeKind.Confirmed)
            {
                op.Abort("the delete: " + handle.Outcome?.Kind);
                yield break;
            }

            yield return new ScreenFrameOp.Until(listed, 30, "the list to show it pending");
            op.Succeed();
        }

        private static IEnumerable<object?> FinishSteps(ScreenFrameOp op, IGameSessionView s, ActionHandle create, string name)
        {
            yield return new ScreenFrameOp.Until(() => create.IsCompleted, 30, "the create's answer");
            if (create.Outcome?.Kind != ActionOutcomeKind.Confirmed)
            {
                op.Succeed("the create was refused: " + create.Outcome?.Kind);
                yield break;
            }

            yield return new ScreenFrameOp.Until(() => s.State.Characters.Entries.Any(c => c.Name == name && !c.PendingDeletion), 30, "the new character in the list");
            s.Actions.EnterWorld(s.State.Characters.Entries.First(c => c.Name == name && !c.PendingDeletion).Guid, ActionSource.Ui); // character creation logs the new character in at once
            op.Succeed();
        }
    }

    /// <summary>The game's own login, which the on-screen run waits for (the headless runner sends none itself).</summary>
    private sealed class HostLogin(IScenario inner) : IScenario
    {
        private ActionHandle? _login;

        public string Name => inner.Name;

        public string Stage => _login is { Outcome.Kind: ActionOutcomeKind.Confirmed } ? inner.Stage : "host-login";

        public string? Failure => inner.Failure ?? (_login is { Outcome: { } o } && o.Kind != ActionOutcomeKind.Confirmed ? "the host's login: " + o.Kind : null);

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            if (_login is null)
            {
                _login = session.Actions.Login(ActionSource.Host);
                return ScenarioStatus.Running;
            }

            if (_login.Outcome is not { } outcome)
            {
                return ScenarioStatus.Running;
            }

            return outcome.Kind == ActionOutcomeKind.Confirmed ? inner.Step(session, now) : ScenarioStatus.Failed;
        }
    }

    private sealed class Run : IDisposable
    {
        public Run(bool existing)
        {
            var table = ScriptedMage.SyntheticTable();
            Screens = new FakeScreens(() => Runner!.Session, Plan());
            Scenario = new ClimberCreateScenario(HeadlessCharacterTests.StandInClasses(), Plan(), Log, climber: Climber, screens: Screens);
            Runner = HeadlessRunner.StartWith(
                new HeadlessRunOptions { Session = new SessionOptions { Account = "test" }, QuitAfterSeconds = 600 },
                new HostLogin(Scenario),
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
                    return Server;
                });
        }

        public FakeScreens Screens { get; }

        public ClimberCreateScenario Scenario { get; }

        public ScriptedPurge Purge { get; private set; } = null!;

        public ScriptedAceSession Server { get; private set; } = null!;

        public ScriptedMage Mage { get; private set; } = null!;

        public ManualTimeProvider Time { get; } = new();

        public RecordingLogSink Log { get; } = new();

        public MemoryStream Recording { get; } = new();

        public SocketFactorySpy Sockets { get; } = new();

        public HeadlessTestWorld World { get; } = new();

        public HeadlessRunner? Runner { get; }

        public HeadlessRunResult Finish(double maxSeconds = 600)
        {
            for (var t = 0.0; t < maxSeconds && !Runner!.IsDone; t += 0.05)
            {
                Time.Advance(TimeSpan.FromMilliseconds(50));
                Runner.Step(Runner.Session.Clock.Now);
                if (Server.PlayerGuid is not null)
                {
                    Mage.Pump();
                }
            }

            Assert.True(Runner!.IsDone, $"the run did not end within {maxSeconds} s (stage {Scenario.Stage})");
            return Runner.Result;
        }

        public void Dispose() => Runner?.Dispose();
    }

    [Fact]
    public void On_screen_the_live_climber_is_deleted_through_the_screens_the_new_one_made_to_the_template_checked_purged_and_left_in_the_world()
    {
        using var run = new Run(existing: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("done", run.Scenario.Stage);
        Assert.True(run.Scenario.OnScreen);
        Assert.Equal(["select", "delete", "chargen", "finish"], run.Screens.Calls);
        Assert.Equal(ClimberChargenPlan.From(Climber, HeadlessCharacterTests.StandInClasses(), Plan()), run.Screens.Opened!, new PlanComparer());
        Assert.Equal("fresh=recreated purged=1 granted=0 template=ok level=1", run.Scenario.VerdictFields()); // the headless step's verdict, word for word
        Assert.True(run.Log.AnyLineContains("verdict fresh=recreated purged=1 granted=0 template=ok level=1"));
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterLogOff); // she stays in the world: the journey follows in the same session
        Assert.Equal([Climber], run.Purge.Asked.Distinct());
        Assert.NotEqual(OldGuid, Assert.Single(run.Server.Characters, c => c.Name == Climber).Guid);
    }

    [Fact]
    public void On_screen_a_first_creation_deletes_nothing_and_ends_in_the_world()
    {
        using var run = new Run(existing: false);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(["chargen", "finish"], run.Screens.Calls);
        Assert.Equal("fresh=created purged=0 granted=0 template=ok level=1", run.Scenario.VerdictFields());
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterLogOff);
    }

    [Fact]
    public void On_screen_a_name_the_server_refuses_fails_by_the_dialogs_words_and_the_old_copy_stays_pending()
    {
        using var run = new Run(existing: true);
        run.Screens.RefuseWith = "That name is already in use.";
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("screen-finish", result.Stage);
        Assert.Contains("the retail dialog said: That name is already in use.", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Empty(run.Purge.Asked); // nothing is purged before the new copy is made and checked (M4 D45)
        Assert.Contains(run.Server.Characters, c => c.Guid == OldGuid);
    }

    /// <summary>Compares two plans by value (their lists are compared element by element).</summary>
    private sealed class PlanComparer : IEqualityComparer<ClimberChargenPlan>
    {
        public bool Equals(ClimberChargenPlan? x, ClimberChargenPlan? y) =>
            x is not null && y is not null && x.Name == y.Name && x.Heritage == y.Heritage && x.Gender == y.Gender && x.StartArea == y.StartArea
            && x.Attributes.SequenceEqual(y.Attributes) && x.Skills.SequenceEqual(y.Skills) && x.RaiseOrder.SequenceEqual(y.RaiseOrder);

        public int GetHashCode(ClimberChargenPlan obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }
}
