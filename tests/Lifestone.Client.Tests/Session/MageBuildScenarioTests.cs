using System.Text.RegularExpressions;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Net.Messages;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The mage build's decisions (M3 1.1 s3b, <see cref="MageBuildScenario"/>) over the whole headless run against <see cref="ScriptedAceSession"/> playing ACE
/// with <see cref="ScriptedMage"/>: an absent character is created and built from nothing, a short one gets only its missing steps in plan order, a ready
/// one is only verified (nothing sent but the spellbook check), a refused raise or an unknown spell fails the run by name, the spell ids go out in chunks of
/// at most 128, and no other character is ever entered. Nothing here names a target rank, a stat or a credit: it all comes from <see cref="MageTemplate"/>
/// and <see cref="ArenaProfile"/>, so the tests follow the template when its split changes. The scripted ACE has no dats: the XpTable is a synthetic one
/// (<see cref="ScriptedMage.SyntheticTable"/>).
/// </summary>
public sealed class MageBuildScenarioTests
{
    private static readonly Regex Verdict = new(
        @"^mage=(ready|built|failed) level=\d+ template=(ok|short)( template_short=.+?)? spells=\d+/\d+ built_steps=\d+( fresh=(recreated|created|none) purged=\d+ refill=(none|level-up:\d+->\d+|failed))?$",
        RegexOptions.CultureInvariant);

    /// <summary>M4 fresh F1: the verdict's refill field after a fresh build: one level-up from the template's level (the extra experience stays unspent).</summary>
    private static string Refilled => $"refill=level-up:{MageTemplate.TargetLevel}->{MageTemplate.TargetLevel + 1}";

    private static int Chunks => (ArenaProfile.SpellsToLearn.Count + MageBuildScenario.SpellChunk - 1) / MageBuildScenario.SpellChunk;

    /// <summary>A run of the scenario over a scripted ACE with the mage in the state <paramref name="shape"/> gives it (null: the account has no mage).</summary>
    private sealed class Run : IDisposable
    {
        public Run(Func<XpTableData, ScriptedAceSession, ScriptedMage>? shape, Action<ScriptedAceSession>? server = null, bool fresh = false, Action<ScriptedPurge>? purge = null, int levels = 275)
        {
            Table = ScriptedMage.SyntheticTable(levels);
            Scenario = new MageBuildScenario(Table, HeadlessCharacterTests.StandInClasses(), Log, freshCharacter: fresh);
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
                    server?.Invoke(Server);
                    if (shape is not null)
                    {
                        // The existing mage is the account's first character, as the account lists it.
                        Server.Characters.Insert(0, new Lifestone.Tests.Shared.Net.AceCharacter(MageGuid, MageTemplate.CharacterName));
                        Mage = shape(Table, Server);
                        Mage.Attach();
                    }
                    else
                    {
                        Mage = ScriptedMage.Fresh(Table, Server, MageTemplate.CharacterName);
                        Mage.Attach();
                    }

                    if (fresh)
                    {
                        Server.AnswerDeletes = true;
                        Purge = new ScriptedPurge(Server);
                        purge?.Invoke(Purge);
                    }

                    return Server;
                });
        }

        public const uint MageGuid = 0x50000020;

        /// <summary>The mod's purge as the scripted server plays it (M4 fresh B1); null unless the run is a fresh-character one.</summary>
        public ScriptedPurge Purge { get; private set; } = null!;

        public XpTableData Table { get; }

        public MageBuildScenario Scenario { get; }

        public ManualTimeProvider Time { get; } = new();

        public RecordingLogSink Log { get; } = new();

        public MemoryStream Recording { get; } = new();

        public SocketFactorySpy Sockets { get; } = new();

        public HeadlessTestWorld World { get; } = new();

        public HeadlessRunner Runner { get; }

        public ScriptedAceSession Server { get; private set; } = null!;

        public ScriptedMage Mage { get; private set; } = null!;

        public HeadlessRunResult Finish(double maxSeconds = 600)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone; t += 0.05)
            {
                Time.Advance(TimeSpan.FromMilliseconds(50));
                Runner.Step(Runner.Session.Clock.Now);
                if (Server.PlayerGuid is not null)
                {
                    Mage.Pump(); // the teleport after a place's reply (M4 fresh B1)
                }
            }

            Assert.True(Runner.IsDone, $"the run did not end within {maxSeconds} s (stage {Scenario.Stage})");
            return Runner.Result;
        }

        public void Dispose() => Runner.Dispose();
    }

    private static ScriptedMage Ready(XpTableData table, ScriptedAceSession server) => ScriptedMage.Ready(table, server, MageTemplate.CharacterName);

    [Fact]
    public void An_absent_mage_is_created_entered_alone_and_built_from_nothing_in_plan_order()
    {
        using var run = new Run(null);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.True(run.Scenario.Created);
        Assert.Contains((MessageOpcodes.CharacterCreate, 0u), run.Server.Sent);
        var created = run.Server.Characters.Single(c => c.Name == MageTemplate.CharacterName);
        Assert.Equal([created.Guid], run.Server.EnterRequests); // no other character is selected
        Assert.Equal(created.Guid, run.Scenario.EnteredGuid);

        // Exactly the experience to the table's level, once; then the trains, the raises in plan order, the spell chunks.
        Assert.Equal([(long)run.Table.XpForLevel(MageTemplate.TargetLevel)], run.Mage.XpAsked);
        var steps = run.Scenario.SentSteps;
        var expected = 1 + MageTemplate.TrainedLater.Count + MageTemplate.Targets.Count + Chunks;
        Assert.Equal(expected, steps.Count);
        Assert.StartsWith("xp ", steps[0], StringComparison.Ordinal);
        for (var i = 0; i < MageTemplate.TrainedLater.Count; i++)
        {
            Assert.StartsWith($"train skill {MageTemplate.TrainedLater[i]} for {MageTemplate.TrainCredits[MageTemplate.TrainedLater[i]]} credits", steps[1 + i], StringComparison.Ordinal);
        }

        var raises = steps.Skip(1 + MageTemplate.TrainedLater.Count).Take(MageTemplate.Targets.Count).ToArray();
        for (var i = 0; i < raises.Length; i++)
        {
            Assert.StartsWith("raise " + MageTemplate.Targets[i].Name + " ", raises[i], StringComparison.Ordinal);
        }

        Assert.All(steps.Skip(expected - Chunks), s => Assert.StartsWith("learn spells ", s, StringComparison.Ordinal));
        Assert.Equal(MageTemplate.TrainedLater.Count + MageTemplate.Targets.Count, run.Server.StatSends.Count);
        foreach (var skill in MageTemplate.TrainedLater)
        {
            Assert.Contains(run.Server.StatSends, s => s.Type == GameActionTypes.TrainSkill && s.Id == skill && (int)s.Amount == MageTemplate.TrainCredits[skill]);
        }

        var verdict = run.Scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.Equal(
            $"mage=built level={run.Mage.Level} template=ok spells={ArenaProfile.SpellsToLearn.Count}/{ArenaProfile.SpellsToLearn.Count} built_steps={expected}",
            verdict);
        Assert.True(run.Mage.Level >= MageTemplate.TargetLevel);
        Assert.DoesNotContain(MageTemplate.CharacterName, verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ready_mage_is_only_verified_nothing_is_raised_or_granted_and_every_spell_answers_known()
    {
        using var run = new Run(Ready);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.False(run.Scenario.Created);
        Assert.Equal([Run.MageGuid], run.Server.EnterRequests);
        Assert.DoesNotContain((MessageOpcodes.CharacterCreate, 0u), run.Server.Sent);
        Assert.Empty(run.Server.StatSends);
        Assert.Empty(run.Mage.XpAsked);
        Assert.Equal(Chunks, run.Mage.LearnSizes.Count);
        Assert.All(run.Scenario.SentSteps, s => Assert.StartsWith("learn spells ", s, StringComparison.Ordinal));
        Assert.Equal(
            $"mage=ready level={MageTemplate.TargetLevel} template=ok spells={ArenaProfile.SpellsToLearn.Count}/{ArenaProfile.SpellsToLearn.Count} built_steps=0",
            run.Scenario.VerdictFields());
    }

    [Fact]
    public void A_mage_past_the_target_level_from_the_loops_kills_is_ready_and_no_experience_is_sent()
    {
        using var run = new Run((table, server) =>
        {
            var mage = Ready(table, server);
            mage.Total += 5_000_000;
            mage.Level = MageTemplate.TargetLevel + 1;
            return mage;
        });
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Empty(run.Mage.XpAsked);
        Assert.Empty(run.Server.StatSends);
        Assert.StartsWith($"mage=ready level={MageTemplate.TargetLevel + 1} template=ok ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_mage_gets_only_its_missing_steps_in_order_and_ends_built()
    {
        var first = MageTemplate.TrainedLater[0];
        var raised = MageTemplate.Targets.Where(t => t.Kind == MageStatKind.Skill && t.Id == first).ToList(); // the skill's own target(s), whatever the template is
        using var run = new Run((table, server) =>
        {
            var mage = Ready(table, server);
            mage.Untrain(first);
            foreach (var id in ArenaProfile.SpellsToLearn.TakeLast(3))
            {
                mage.Known.Remove(id);
            }

            mage.Credits = MageTemplate.TrainCredits[first];
            mage.Available = 1_000_000_000;
            return mage;
        });
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        var steps = run.Scenario.SentSteps;
        Assert.Empty(run.Mage.XpAsked);
        Assert.StartsWith($"train skill {first} for {MageTemplate.TrainCredits[first]} credits", steps[0], StringComparison.Ordinal);
        for (var i = 0; i < raised.Count; i++)
        {
            Assert.StartsWith("raise " + raised[i].Name + " ", steps[1 + i], StringComparison.Ordinal);
        }

        Assert.All(steps.Skip(1 + raised.Count), s => Assert.StartsWith("learn spells ", s, StringComparison.Ordinal));
        Assert.Equal(1 + raised.Count, run.Server.StatSends.Count);
        Assert.Equal(GameActionTypes.TrainSkill, run.Server.StatSends[0].Type);
        Assert.All(run.Server.StatSends.Skip(1), s => Assert.Equal(GameActionTypes.RaiseSkill, s.Type));
        Assert.Equal(
            $"mage=built level={MageTemplate.TargetLevel} template=ok spells={ArenaProfile.SpellsToLearn.Count}/{ArenaProfile.SpellsToLearn.Count} built_steps={2 + raised.Count}",
            run.Scenario.VerdictFields()); // the train, its raises, and the one chunk that taught the three missing spells
    }

    [Fact]
    public void The_spell_ids_go_out_in_chunks_of_at_most_128_covering_every_id_once_in_order()
    {
        using var run = new Run((table, server) =>
        {
            var mage = Ready(table, server);
            mage.Known.Clear();
            return mage;
        });
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(Chunks, run.Mage.LearnSizes.Count);
        Assert.All(run.Mage.LearnSizes, n => Assert.InRange(n, 1, 128));
        Assert.Equal(128, run.Mage.LearnSizes[0]);
        Assert.Equal(ArenaProfile.SpellsToLearn.Count, run.Mage.LearnSizes.Sum());
        Assert.Equal(ArenaProfile.SpellsToLearn.Count, run.Mage.Known.Count);
        Assert.Equal(Chunks, run.Scenario.BuiltSteps);
    }

    [Fact]
    public void A_refused_raise_fails_the_run_by_action_and_stat_and_the_verdict_says_failed()
    {
        var target = MageTemplate.Targets[^1];
        using var run = new Run(null);
        run.Mage.Dropped.Add((target.Kind, target.Id));
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("building", result.Stage);
        Assert.Contains("raise " + target.Name, result.Failure, StringComparison.Ordinal);
        Assert.Contains("TimedOut", result.Failure, StringComparison.Ordinal);
        Assert.StartsWith("mage=failed ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
        Assert.Contains("template=short", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Empty(run.Mage.LearnSizes); // the build stops at the failed step; the spellbook is not touched
    }

    [Fact]
    public void An_unknown_spell_id_fails_the_run_naming_it()
    {
        var unknown = ArenaProfile.SpellsToLearn[^1];
        using var run = new Run(Ready, s => { });
        run.Mage.SpellTable.Remove(unknown);
        run.Mage.Known.Remove(unknown);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains("unknown-spell", result.Failure, StringComparison.Ordinal);
        Assert.Contains(unknown.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Failure, StringComparison.Ordinal);
        Assert.StartsWith("mage=failed ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_template_the_scenario_cannot_fix_ends_failed_and_names_what_is_short()
    {
        var specialised = MageTemplate.Targets.First(t => t.Kind == MageStatKind.Skill && t.Specialized);
        using var run = new Run((table, server) =>
        {
            var mage = Ready(table, server);
            mage.SetClass(specialised.Id, HeadlessCharacter.TrainedSkill);
            return mage;
        });
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains("short of the template", result.Failure, StringComparison.Ordinal);
        var verdict = run.Scenario.VerdictFields();
        Assert.StartsWith("mage=failed ", verdict, StringComparison.Ordinal);
        Assert.Contains("template=short template_short=" + specialised.Name.Replace(' ', '_') + "_class_", verdict, StringComparison.Ordinal);
        AssertKeyValueTokens(verdict); // 1.1-R10: no space or semicolon inside a field
        Assert.Empty(run.Server.StatSends);
    }

    [Fact]
    public void A_full_account_without_the_mage_fails_loudly_creates_nothing_and_enters_nobody()
    {
        using var run = new Run(
            null,
            server =>
            {
                server.Characters.Clear();
                for (var i = 0u; i < 11; i++)
                {
                    server.Characters.Add(new Lifestone.Tests.Shared.Net.AceCharacter(0x50000100 + i, "Filler " + i));
                }
            });
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("choosing", result.Stage);
        Assert.Contains("no free character slot", result.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain((MessageOpcodes.CharacterCreate, 0u), run.Server.Sent);
        Assert.Empty(run.Server.EnterRequests);
        Assert.StartsWith("mage=failed ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_mage_pending_deletion_is_not_entered_or_restored()
    {
        using var run = new Run(Ready);
        var index = run.Server.Characters.FindIndex(c => c.Name == MageTemplate.CharacterName);
        run.Server.Characters[index] = run.Server.Characters[index] with { DeleteField = 1 };
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains("pending deletion", result.Failure, StringComparison.Ordinal);
        Assert.Empty(run.Server.EnterRequests);
        Assert.DoesNotContain((MessageOpcodes.CharacterRestore, 0u), run.Server.Sent);
    }

    [Fact]
    public void The_mage_is_found_by_name_whatever_the_case_and_never_another_character()
    {
        using var run = new Run(Ready);
        var index = run.Server.Characters.FindIndex(c => c.Name == MageTemplate.CharacterName);
        run.Server.Characters[index] = run.Server.Characters[index] with { Name = MageTemplate.CharacterName.ToUpperInvariant() };
        run.Mage.Attach(); // the description is looked up by the name entered (case-insensitively)
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.Equal([Run.MageGuid], run.Server.EnterRequests);
    }

    [Fact]
    public void The_scenario_refuses_an_xp_table_that_ends_before_the_target_level()
    {
        var log = new RecordingLogSink();
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            w.Write(XpTableData.DataId);
            foreach (var c in new uint[] { 1, 1, 1, 1, 10 })
            {
                w.Write(c);
            }

            for (var i = 0; i < 2 + 2 + 2 + 2; i++)
            {
                w.Write(0u);
            }

            for (var i = 0; i < 11; i++)
            {
                w.Write(0UL);
            }

            for (var i = 0; i < 11; i++)
            {
                w.Write(0u);
            }
        }

        var shortTable = XpTableData.Decode(stream.ToArray());
        Assert.Throws<ArgumentException>(() => new MageBuildScenario(shortTable, HeadlessCharacterTests.StandInClasses(), log));
    }

    /// <summary>1.1-R10: every space-separated token of a verdict line is key=value, and none holds a semicolon.</summary>
    private static void AssertKeyValueTokens(string verdict)
    {
        Assert.All(verdict.Split(' '), token => Assert.Matches(@"^[a-z_]+=[^;=]+$", token));
    }

    [Fact]
    public void The_template_short_field_has_no_spaces_so_the_verdict_line_parses_as_key_value_and_the_verdict_is_logged_once()
    {
        using var ok = new Run(null);
        Assert.True(ok.Finish().Ok);
        AssertKeyValueTokens(ok.Scenario.VerdictFields());
        Assert.Single(ok.Log.Entries, e => e.Message.StartsWith("verdict ", StringComparison.Ordinal));

        var target = MageTemplate.Targets[^1];
        using var failed = new Run(null);
        failed.Mage.Dropped.Add((target.Kind, target.Id));
        Assert.False(failed.Finish().Ok);
        AssertKeyValueTokens(failed.Scenario.VerdictFields());
        Assert.Contains("template_short=", failed.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(failed.Log.Entries, e => e.Message.StartsWith("verdict ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_creation_trained_skill_starts_at_5_ranks_and_526_xp_as_aces_player_factory_gives_it_and_is_raised_from_its_spent_experience()
    {
        var skill = MageTemplate.CreationTrained[0];
        using var run = new Run(null);
        Assert.Equal((ScriptedMage.CreationBonusRanks, HeadlessCharacter.TrainedSkill, ScriptedMage.CreationBonusXp), run.Mage.SkillOf(skill));
        Assert.Equal((ushort)5, run.Mage.SkillOf(skill).Ranks);
        Assert.Equal(526u, run.Mage.SkillOf(skill).Experience);
        Assert.Equal((ushort)0, run.Mage.SkillOf(MageTemplate.LifeMagic).Ranks); // a skill trained later starts at nothing

        var result = run.Finish();
        Assert.True(result.Ok, result.Failure);
        var target = MageTemplate.TargetOf(MageStatKind.Skill, skill)!;
        Assert.Equal(target.XpList(run.Table)[target.Ranks], run.Mage.SkillOf(skill).Experience); // priced from the server's spent XP: 526 less than from zero
        var send = run.Server.StatSends.Single(s => s.Type == GameActionTypes.RaiseSkill && s.Id == skill);
        Assert.Equal(target.XpList(run.Table)[target.Ranks] - 526u, send.Amount);
    }

    [Fact]
    public void The_scripted_ace_prices_training_from_a_fixed_skill_table_copy_that_the_template_must_equal()
    {
        // 1.1-R1: a TrainCredits typo fails here, offline, not as a silently dropped train on the permanent build.
        Assert.All(MageTemplate.TrainedLater, id => Assert.Equal(ScriptedMage.AceTrainedCost[id], MageTemplate.TrainCredits[id]));

        using var run = new Run(null);
        run.Mage.Credits = 50;
        var life = MageTemplate.LifeMagic;
        var cost = ScriptedMage.AceTrainedCost[life];
        run.Server.StatHook!(GameActionTypes.TrainSkill, life, (uint)(cost + 1)); // one credit more than the table says: ACE drops it
        run.Server.StatHook!(GameActionTypes.TrainSkill, life, (uint)(cost - 1));
        Assert.Equal((50, HeadlessCharacter.UntrainedSkill), (run.Mage.Credits, run.Mage.SkillOf(life).Class));
        run.Server.StatHook!(GameActionTypes.TrainSkill, life, (uint)cost);
        Assert.Equal((50 - cost, HeadlessCharacter.TrainedSkill), (run.Mage.Credits, run.Mage.SkillOf(life).Class));
    }

    [Fact]
    public void The_scripted_ace_refuses_a_raise_past_the_end_of_the_experience_list()
    {
        using var run = new Run(null);
        run.Mage.Available = 4_000_000_000;
        var attributeEnd = run.Table.AttributeXpList[^1];
        run.Server.StatHook!(GameActionTypes.RaiseAttribute, MageTemplate.Focus, attributeEnd + 1);
        Assert.Equal((0u, 0u), run.Mage.AttributeOf(MageTemplate.Focus));
        Assert.Equal(4_000_000_000, run.Mage.Available);
        run.Server.StatHook!(GameActionTypes.RaiseAttribute, MageTemplate.Focus, attributeEnd); // exactly to the cap is fine
        Assert.Equal(((uint)run.Table.AttributeXpList.Count - 1, attributeEnd), run.Mage.AttributeOf(MageTemplate.Focus));

        var skill = MageTemplate.CreationTrained[0];
        var skillEnd = run.Table.TrainedSkillXpList[^1];
        run.Server.StatHook!(GameActionTypes.RaiseSkill, skill, skillEnd - 525); // 526 spent + this is one past the end
        Assert.Equal(526u, run.Mage.SkillOf(skill).Experience);
        run.Server.StatHook!(GameActionTypes.RaiseSkill, skill, skillEnd - 526);
        Assert.Equal(skillEnd, run.Mage.SkillOf(skill).Experience);
    }

    [Fact]
    public void A_level_that_disagrees_with_the_experience_fails_before_the_xp_grant_is_sent()
    {
        // 1.1-R7: the one irreversible step. Level 1 with the experience of a far higher level, and a high level with no experience.
        foreach (var (level, total) in new[] { (1, 50_000_000L), (MageTemplate.TargetLevel + 4, 0L) })
        {
            using var run = new Run((table, server) =>
            {
                var mage = ScriptedMage.Fresh(table, server, MageTemplate.CharacterName);
                (mage.Level, mage.Total) = (level, total);
                return mage;
            });
            var result = run.Finish();

            Assert.False(result.Ok);
            Assert.Contains("the read model's level and experience disagree", result.Failure, StringComparison.Ordinal);
            Assert.Contains($"level {level}", result.Failure, StringComparison.Ordinal);
            Assert.Contains(total.ToString("N0", System.Globalization.CultureInfo.InvariantCulture), result.Failure, StringComparison.Ordinal);
            Assert.Empty(run.Mage.XpAsked);
            Assert.Empty(run.Server.StatSends);
        }
    }

    [Fact]
    public void A_mod_that_does_not_report_xp_or_spell_learn_fails_at_the_status_stage_with_nothing_sent()
    {
        foreach (var missing in MageBuildScenario.RequiredVerbs)
        {
            using var run = new Run(null, server => server.StatusVerbs = [.. ToolsWire.Verbs.Where(v => v != missing)]);
            var result = run.Finish();

            Assert.False(result.Ok);
            Assert.Equal("status", result.Stage);
            Assert.Contains($"'{missing}'", result.Failure, StringComparison.Ordinal);
            Assert.Empty(run.Mage.XpAsked);
            Assert.Empty(run.Server.StatSends);
            Assert.Empty(run.Mage.LearnSizes);
        }
    }

    [Fact]
    public void The_precondition_wait_is_keyed_by_its_reason_each_reason_gets_its_own_allowance()
    {
        var timer = new ReasonTimer(MageBuildScenario.SettleSeconds);
        Assert.False(timer.Expired(0, "the level"));
        Assert.False(timer.Expired(4.9, "the level"));
        Assert.False(timer.Expired(4.95, "the credits")); // a new reason restarts the clock (it inherited 4.95 s before the fix)
        Assert.False(timer.Expired(9.9, "the credits"));
        Assert.True(timer.Expired(10.0, "the credits")); // 5.05 s on its own reason
        Assert.True(timer.Expired(10.1, "the credits"));
        timer.Reset();
        Assert.False(timer.Expired(11, "the credits")); // a sent step clears the wait
        Assert.True(timer.Expired(16.1, "the credits"));
    }

    // ------------------------------------------------------------------------------------------------ M4 fresh B1: --fresh-character (D44, D45)

    private static ScriptedMage Replaceable(XpTableData table, ScriptedAceSession server) => ScriptedMage.Replaceable(table, server, MageTemplate.CharacterName);

    [Fact]
    public void With_fresh_character_the_live_mage_is_deleted_from_the_list_never_entered_then_created_built_purged_placed_and_logged_off_in_that_order_M4_fresh_B1_D44_D45()
    {
        using var run = new Run(Replaceable, fresh: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.True(run.Scenario.Created);
        var verdict = run.Scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.StartsWith("mage=built ", verdict, StringComparison.Ordinal);
        Assert.Contains(" template=ok ", verdict, StringComparison.Ordinal);
        Assert.EndsWith(" fresh=recreated purged=1 " + Refilled, verdict, StringComparison.Ordinal); // fresh= and purged= stay on the one line the gate's judge reads, the refill last
        Assert.Contains($" level={MageTemplate.TargetLevel + 1} ", verdict, StringComparison.Ordinal); // one level above the template's: the refill's level-up (M4 fresh F1)

        // The old character is never entered: the delete comes from the character list, then the create, then the one entry (of the new character), then the mod's verbs, then the log off.
        var opcodes = run.Server.Sent.Select(o => o.Opcode).ToList();
        var delete = opcodes.IndexOf(MessageOpcodes.CharacterDelete);
        var create = opcodes.IndexOf(MessageOpcodes.CharacterCreate);
        var enter = opcodes.IndexOf(MessageOpcodes.CharacterEnterWorldRequest);
        var logOff = opcodes.IndexOf(MessageOpcodes.CharacterLogOff);
        Assert.True(delete >= 0 && delete < create && create < enter, string.Join(",", opcodes.Select(o => $"{o:X}")));
        Assert.Single(opcodes, o => o == MessageOpcodes.CharacterDelete);
        Assert.Single(opcodes, o => o == MessageOpcodes.CharacterCreate);
        Assert.Single(opcodes, o => o == MessageOpcodes.CharacterEnterWorldRequest);
        var created = run.Server.Characters.Single(c => c.Name == MageTemplate.CharacterName && c.DeleteField == 0);
        Assert.NotEqual(Run.MageGuid, created.Guid);
        Assert.Equal([created.Guid], run.Server.EnterRequests);
        Assert.DoesNotContain(run.Server.Characters, c => c.Guid == Run.MageGuid); // the old copy was listed pending (restorable) until the new copy's purge removed it
        Assert.Equal(1, run.Purge.Removed);

        // The mod's verbs after the entry: the build's, then the refill's experience grant (M4 fresh F1), then the purge asked until unknown-character (twice: one removal, then the refusal), then the place to Holtburg, and the log off last.
        var journal = run.Purge.Journal;
        var purges = journal.Select((j, i) => (j.Request, j.SentIndex, i)).Where(j => j.Request == nameof(CharPurgeRequest)).ToList();
        var place = journal.FindIndex(j => j.Request == nameof(PlacePoiRequest));
        Assert.Equal(2, purges.Count);
        Assert.Equal(purges[0].i + 1, purges[1].i);
        Assert.True(place == journal.Count - 1 && place == purges[1].i + 1, string.Join(",", journal.Select(j => j.Request)));
        Assert.Contains(journal.Take(purges[0].i), j => j.Request == nameof(XpRequest));
        Assert.Contains(journal.Take(purges[0].i), j => j.Request == nameof(SpellLearnRequest));
        Assert.All(journal.Take(purges[0].i), j => Assert.DoesNotContain(j.Request, new[] { nameof(CharPurgeRequest), nameof(PlacePoiRequest) }));
        var lastLearn = journal.FindLastIndex(j => j.Request == nameof(SpellLearnRequest));
        Assert.Equal(nameof(XpRequest), journal[purges[0].i - 1].Request); // the refill's grant is the last verb before the purge ...
        Assert.True(lastLearn < purges[0].i - 1, string.Join(",", journal.Select(j => j.Request))); // ... and comes after the build's last (the spellbook)
        Assert.Equal(2, journal.Count(j => j.Request == nameof(XpRequest))); // the build's grant and the refill's, never a third
        Assert.True(journal[0].SentIndex > enter);
        Assert.True(logOff > journal[place].SentIndex);
        Assert.All(run.Purge.Asked, n => Assert.Equal(MageTemplate.CharacterName, n)); // no other name is ever purged
        Assert.Equal(1, run.Mage.PlacedInHoltburg);
        Assert.Contains(run.Log.Lines, l => l.Contains("fresh=recreated", StringComparison.Ordinal));

        // The three other characters of the account are as they were.
        Assert.Contains(run.Server.Characters, c => c.Name == "Amy" && c.DeleteField == 0);
        Assert.Contains(run.Server.Characters, c => c.Name == "Bo" && c.DeleteField == 1);
        Assert.Contains(run.Server.Characters, c => c.Name == "Cy" && c.DeleteField == 0);
    }

    [Fact]
    public void With_fresh_character_the_built_mage_is_the_templates_from_nothing_not_the_old_copys_M4_fresh_B1_D44()
    {
        using var run = new Run(Replaceable, fresh: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        // The experience is granted again: the new copy had none. Then the refill's grant (M4 fresh F1): exactly the experience from the template's level to the next, nothing more.
        Assert.Equal([(long)run.Table.XpForLevel(MageTemplate.TargetLevel), (long)(run.Table.XpForLevel(MageTemplate.TargetLevel + 1) - run.Table.XpForLevel(MageTemplate.TargetLevel))], run.Mage.XpAsked);
        Assert.Equal(1 + MageTemplate.TrainedLater.Count + MageTemplate.Targets.Count + Chunks + 1 + 1, run.Scenario.SentSteps.Count); // the build's steps, the refill's grant and the place
        Assert.StartsWith("place to Holtburg", run.Scenario.SentSteps[^1], StringComparison.Ordinal);
        Assert.StartsWith("refill xp ", run.Scenario.SentSteps[^2], StringComparison.Ordinal);
        Assert.Equal(1 + MageTemplate.TrainedLater.Count + MageTemplate.Targets.Count + Chunks, run.Scenario.BuiltSteps); // the place and the refill are not built steps
    }

    [Fact]
    public void With_fresh_character_an_expired_old_row_is_purged_too_and_the_count_says_so_M4_fresh_B1_D45()
    {
        using var run = new Run(Replaceable, fresh: true, purge: p => p.ExpiredRows = 1);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.EndsWith(" fresh=recreated purged=2 " + Refilled, run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(3, run.Purge.Asked.Count); // two removals, then unknown-character
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_fresh_character_an_older_pending_copy_in_the_list_is_skipped_by_the_choosing_and_purged_with_the_deleted_one_M4_fresh_B1_D45()
    {
        using var run = new Run(Replaceable, server => server.Characters.Add(new Lifestone.Tests.Shared.Net.AceCharacter(0x50000077, MageTemplate.CharacterName, 1)), fresh: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.EndsWith(" fresh=recreated purged=2 " + Refilled, run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(0x50000077u, run.Server.EnterRequests);
        Assert.Single(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterDelete);
        Assert.DoesNotContain(run.Server.Characters, c => c.Name == MageTemplate.CharacterName && c.DeleteField != 0); // both old rows are gone
    }

    [Fact]
    public void With_fresh_character_and_no_live_copy_listed_the_mage_is_only_created_built_and_placed_and_the_verdict_says_created_M4_fresh_B1_D44()
    {
        using var run = new Run(null, fresh: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.True(run.Scenario.Created);
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterDelete);
        Assert.Single(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterCreate);
        Assert.EndsWith(" fresh=created purged=0 " + Refilled, run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(run.Purge.Asked); // asked once: unknown-character, which ends the loop with nothing purged
        Assert.Equal(1, run.Mage.PlacedInHoltburg);
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_fresh_character_a_pending_row_of_the_name_and_no_live_copy_is_created_over_and_the_new_copy_purges_it_M4_fresh_B1_D45()
    {
        using var run = new Run(null, server => server.Characters.Add(new Lifestone.Tests.Shared.Net.AceCharacter(0x50000077, MageTemplate.CharacterName, 1)), fresh: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterDelete);
        Assert.EndsWith(" fresh=created purged=1 " + Refilled, run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(0x50000077u, run.Server.EnterRequests);
    }

    [Fact]
    public void With_fresh_character_a_delete_the_server_does_not_answer_fails_before_anything_is_created_M4_fresh_B1_D44()
    {
        using var run = new Run(Replaceable, fresh: true);
        run.Server.AnswerDeletes = false; // the rig turns it on for a fresh run; this server never acknowledges the delete
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("recreate-delete", result.Stage);
        Assert.Contains("fresh-character: deleting the old character through the client's retail character delete", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterCreate);
        Assert.Empty(run.Server.EnterRequests);
        Assert.Empty(run.Purge.Asked);
        Assert.Contains(" fresh=none purged=0", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Theory]
    [InlineData(ToolErrors.Graduated, "the name is one of the graduated test characters", "D45 rule", "setup-server.ps1 -ModOnly")]
    [InlineData(ToolErrors.UnknownVerb, "the mod has no such verb", "no char purge verb", "setup-server.ps1 -ModOnly")]
    [InlineData(ToolErrors.OwnCharacter, "that is the character you are playing", "own-character", "that is the character you are playing")]
    public void With_fresh_character_a_purge_the_mod_refuses_fails_the_run_by_name_and_nothing_else_is_sent_M4_fresh_B1_D45(string code, string sentence, string named, string andAlso)
    {
        using var run = new Run(Replaceable, fresh: true, purge: p => p.Refuse(code, sentence));
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("purging", result.Stage);
        Assert.StartsWith("purge: ", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(named, run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(andAlso, run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Single(run.Purge.Asked); // no second ask after a refusal
        Assert.Equal(0, run.Mage.PlacedInHoltburg); // and the character is not placed
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterLogOff);
        Assert.StartsWith("mage=failed ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.EndsWith(" fresh=recreated purged=0 " + Refilled, run.Scenario.VerdictFields(), StringComparison.Ordinal); // the refill ran before the purge asked
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_fresh_character_a_recreated_mage_whose_old_copy_the_mod_did_not_purge_fails_the_run_M4_fresh_B1_D45()
    {
        using var run = new Run(Replaceable, fresh: true, purge: p => p.Gone = true);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains("purge: the old copy was not purged", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Single(run.Purge.Asked);
        Assert.EndsWith(" fresh=recreated purged=0 " + Refilled, run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0, run.Mage.PlacedInHoltburg);
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_fresh_character_a_mod_that_never_runs_out_of_old_copies_fails_at_the_cap_of_ten_purges_M4_fresh_B1_D45()
    {
        using var run = new Run(Replaceable, fresh: true, purge: p => p.Endless = true);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains($"purged {FreshPurge.MaxPurges} times", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(10, FreshPurge.MaxPurges);
        Assert.Equal(FreshPurge.MaxPurges, run.Purge.Asked.Count);
        Assert.EndsWith($" fresh=recreated purged={FreshPurge.MaxPurges} " + Refilled, run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(0, run.Mage.PlacedInHoltburg);
    }

    [Fact]
    public void With_fresh_character_a_build_that_fails_leaves_the_old_copy_pending_and_purges_nothing_M4_fresh_B1_D45()
    {
        var target = MageTemplate.Targets[^1];
        using var run = new Run(Replaceable, fresh: true);
        run.Mage.Dropped.Add((target.Kind, target.Id));
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains("raise " + target.Name, run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Empty(run.Purge.Asked); // the purge is only after a build that ended met
        Assert.Equal(1u, run.Server.Characters.Single(c => c.Guid == Run.MageGuid).DeleteField); // the old copy stays listed as pending deletion: restorable
        Assert.Contains(" fresh=recreated purged=0", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.EndsWith(" refill=none", run.Scenario.VerdictFields(), StringComparison.Ordinal); // and so is the refill (M4 fresh F1)
        Assert.Single(run.Mage.XpAsked); // only the build's grant: no refill grant after a build that ended short
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_fresh_character_a_mod_that_does_not_report_the_purge_verb_fails_at_the_status_stage_M4_fresh_B1()
    {
        using var run = new Run(null, server => server.StatusVerbs = [.. ToolsWire.Verbs.Where(v => v != FreshPurge.RequiredVerb)], fresh: true);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("status", result.Stage);
        Assert.Contains($"'{FreshPurge.RequiredVerb}'", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Empty(run.Mage.XpAsked);
    }

    // ------------------------------------------------------------------------------------------------ M4 fresh F1: the refill (the item's live gate round 1 finding)

    [Fact]
    public void With_fresh_character_the_vitals_are_refilled_by_one_level_up_after_the_build_and_before_the_purge_with_exactly_the_experience_to_the_next_level_M4_fresh_F1()
    {
        using var run = new Run(Replaceable, fresh: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var wanted = run.Table.XpForLevel(MageTemplate.TargetLevel);
        var next = run.Table.XpForLevel(MageTemplate.TargetLevel + 1);
        Assert.Equal([(long)wanted, (long)(next - wanted)], run.Mage.XpAsked); // the build's grant, then exactly the experience to the next level: nothing else, never twice
        var verdict = run.Scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.EndsWith(" fresh=recreated purged=1 " + Refilled, verdict, StringComparison.Ordinal);

        // The log tells it in order: the build ended met, why the vitals are below their maxima (with ACE's rule cited), the level-up that refilled them, then the purge.
        var lines = run.Log.Lines.ToList();
        int At(string text) => lines.FindIndex(l => l.Contains(text, StringComparison.Ordinal));
        var ended = At("the build ended met; refilling the vitals");
        var below = At("refill: the vitals are below their maxima (health ");
        var refilled = At("refill: the level-up refilled the vitals (level-up:");
        var full = At("the vitals are full (refill=level-up:");
        var purged = At("purged an old copy");
        Assert.True(ended >= 0 && ended < below && below < refilled && refilled < full && full < purged, string.Join(" | ", new[] { ended, below, refilled, full, purged }.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        Assert.Contains("Player_Xp.cs CheckForLevelup calls SetMaxVitals", lines[below], StringComparison.Ordinal);
        Assert.Contains("95ef426d", lines[below], StringComparison.Ordinal);
    }

    [Fact]
    public void With_fresh_character_and_the_vitals_already_full_the_refill_is_none_and_nothing_is_granted_M4_fresh_F1()
    {
        using var run = new Run(Replaceable, fresh: true);
        run.Mage.FullVitals = true;
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.Equal([(long)run.Table.XpForLevel(MageTemplate.TargetLevel)], run.Mage.XpAsked); // the build's grant alone
        var verdict = run.Scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.EndsWith(" fresh=recreated purged=1 refill=none", verdict, StringComparison.Ordinal);
        Assert.Contains($" level={MageTemplate.TargetLevel} ", verdict, StringComparison.Ordinal); // no level-up: the template's level
        Assert.Contains(run.Log.Lines, l => l.Contains("refill: every vital is at its maximum", StringComparison.Ordinal));
        Assert.Equal(1, run.Purge.Removed); // and the purge still ran after it
    }

    [Fact]
    public void With_fresh_character_a_refused_refill_grant_fails_the_run_by_name_and_purges_and_grants_nothing_more_M4_fresh_F1()
    {
        using var run = new Run(Replaceable, fresh: true);
        run.Mage.XpGrantsBeforeRefusal = 1; // the build's grant is answered, the refill's is refused
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("refilling", result.Stage);
        Assert.StartsWith("refill: the grant of ", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("was refused", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("the experience grant was refused", run.Scenario.Failure, StringComparison.Ordinal); // the mod's own sentence
        Assert.Equal(2, run.Mage.XpAsked.Count); // the refill asked once and is never asked again
        Assert.Empty(run.Purge.Asked);
        Assert.Equal(0, run.Mage.PlacedInHoltburg);
        Assert.Equal(1u, run.Server.Characters.Single(c => c.Guid == Run.MageGuid).DeleteField); // the old copy stays restorable
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterLogOff);
        Assert.StartsWith("mage=failed ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.EndsWith(" fresh=recreated purged=0 refill=failed", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_fresh_character_a_refill_the_server_does_not_play_fails_ten_seconds_after_the_answer_naming_the_level_and_the_vitals_M4_fresh_F1()
    {
        using var run = new Run(Replaceable, fresh: true);
        run.Mage.SkipRefill = true; // ACE's level-up that does not run SetMaxVitals: the grant levels the character up and the vitals stay short
        double? sent = null;
        double? ended = null;
        for (var t = 0.0; t < 600 && ended is null; t += 0.05)
        {
            run.Time.Advance(TimeSpan.FromMilliseconds(50));
            run.Runner.Step(run.Runner.Session.Clock.Now);
            if (run.Server.PlayerGuid is not null)
            {
                run.Mage.Pump();
            }

            if (sent is null && run.Mage.XpAsked.Count == 2)
            {
                sent = run.Runner.Session.Clock.Now;
            }

            if (run.Runner.IsDone)
            {
                ended = run.Runner.Session.Clock.Now;
            }
        }

        Assert.NotNull(ended);
        Assert.False(run.Runner.Result.Ok);
        Assert.Equal("refilling", run.Runner.Result.Stage);
        Assert.Equal(10, FreshRefill.WaitSeconds);
        Assert.InRange(ended!.Value - sent!.Value, FreshRefill.WaitSeconds, FreshRefill.WaitSeconds + 1); // the bound, not before it
        Assert.StartsWith("refill: the level-up and the refilled vitals were not seen within 10 s of the grant", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains($"level {MageTemplate.TargetLevel + 1}, was {MageTemplate.TargetLevel}; health ", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(2, run.Mage.XpAsked.Count); // never granted twice
        Assert.Empty(run.Purge.Asked);
        Assert.EndsWith(" fresh=recreated purged=0 refill=failed", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_fresh_character_an_xp_table_that_ends_at_the_templates_level_names_no_level_for_the_refill_and_fails_by_name_M4_fresh_F1()
    {
        using var run = new Run(Replaceable, fresh: true, levels: MageTemplate.TargetLevel);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("refilling", result.Stage);
        Assert.StartsWith($"refill: the XpTable cannot name a level above {MageTemplate.TargetLevel} (it ends at level {MageTemplate.TargetLevel})", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Single(run.Mage.XpAsked); // the refill sent nothing
        Assert.Empty(run.Purge.Asked);
        Assert.EndsWith(" refill=failed", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_fresh_character_the_build_sends_no_refill_grant_and_the_verdict_has_no_refill_field_M4_fresh_F1()
    {
        // The same build as the fresh one (the xp, then the raises that lift the maxima over the current values): only the fresh character refills.
        using var run = new Run(null);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.Equal([(long)run.Table.XpForLevel(MageTemplate.TargetLevel)], run.Mage.XpAsked);
        Assert.DoesNotContain("refill", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Scenario.SentSteps, s => s.StartsWith("refill", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Log.Lines, l => l.Contains("refill", StringComparison.Ordinal));
        Assert.Contains($" level={MageTemplate.TargetLevel} ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void Fresh_character_needs_the_session_ending_build_not_the_journeys_embedded_one_M4_fresh_B1()
    {
        var embedded = Assert.Throws<ArgumentException>(() => new MageBuildScenario(ScriptedMage.SyntheticTable(), HeadlessCharacterTests.StandInClasses(), new RecordingLogSink(), endSession: false, freshCharacter: true));
        Assert.Contains("character list", embedded.Message, StringComparison.Ordinal);
        Assert.NotNull(new MageBuildScenario(ScriptedMage.SyntheticTable(), HeadlessCharacterTests.StandInClasses(), new RecordingLogSink(), freshCharacter: true));
    }

    [Fact]
    public void Without_fresh_character_a_ready_mage_is_not_placed_purged_or_deleted_and_the_verdict_has_no_fresh_field_M4_fresh_B1()
    {
        using var run = new Run(Ready);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure);
        Assert.DoesNotContain("fresh", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterDelete);
        Assert.Equal(0, run.Mage.PlacedInHoltburg);
    }
}
