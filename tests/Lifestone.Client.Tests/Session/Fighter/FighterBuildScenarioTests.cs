using System.Text.RegularExpressions;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.CharacterCreation;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The fighter build's decisions (M4 1.1 s2, <see cref="FighterBuildScenario"/>) over the whole headless run against <see cref="ScriptedAceSession"/> playing ACE with <see cref="ScriptedFighter"/>: an absent
/// "Holtburg Hunter" is created and built from nothing in order (xp, raises in plan order, the kit, the wields, the place), a ready one is only verified (nothing sent), a partial one gets only what is
/// missing, no other character is ever entered, and a refused grant fails by name. Nothing here names a rank or a stat: it all comes from <see cref="FighterTemplate"/> and <see cref="FighterKit"/>.
/// The scripted ACE has no dats: the XpTable is a synthetic one (<see cref="ScriptedMage.SyntheticTable"/>).
/// </summary>
public sealed class FighterBuildScenarioTests
{
    private static readonly Regex Verdict = new(
        @"^fighter=(ready|built|failed) level=\d+ template=(ok|short)( template_short=\S+)? kit=\S+ wielded=\d+ worn=\d+$",
        RegexOptions.CultureInvariant);

    private static int Grants => FighterKit.Items.Sum(i => i.Item.MaxStack is null ? i.Item.Stack : 1);

    private static int Wields => FighterKit.Worn.Count();

    private sealed class Run : IDisposable
    {
        public Run(Func<XpTableData, ScriptedAceSession, ScriptedFighter>? shape, Action<ScriptedAceSession>? server = null)
        {
            Table = ScriptedMage.SyntheticTable();
            Scenario = new FighterBuildScenario(Table, HeadlessCharacterTests.StandInClasses(), Log);
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
                        Server.Characters.Insert(0, new Lifestone.Tests.Shared.Net.AceCharacter(FighterGuid, FighterTemplate.CharacterName));
                        Fighter = shape(Table, Server);
                    }
                    else
                    {
                        Fighter = ScriptedFighter.Fresh(Table, Server, FighterTemplate.CharacterName);
                    }

                    Fighter.Attach();
                    return Server;
                });
        }

        public const uint FighterGuid = 0x50000030;

        public XpTableData Table { get; }

        public FighterBuildScenario Scenario { get; }

        public ManualTimeProvider Time { get; } = new();

        public RecordingLogSink Log { get; } = new();

        public MemoryStream Recording { get; } = new();

        public SocketFactorySpy Sockets { get; } = new();

        public HeadlessTestWorld World { get; } = new();

        public HeadlessRunner Runner { get; }

        public ScriptedAceSession Server { get; private set; } = null!;

        public ScriptedFighter Fighter { get; private set; } = null!;

        public uint FighterGuidOnServer => Server.Characters.Single(c => c.Name == FighterTemplate.CharacterName).Guid;

        public HeadlessRunResult Finish(double maxSeconds = 600)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone; t += 0.05)
            {
                Time.Advance(TimeSpan.FromMilliseconds(50));
                Runner.Step(Runner.Session.Clock.Now);
                if (Server.PlayerGuid is not null)
                {
                    Fighter.Pump();
                }
            }

            Assert.True(Runner.IsDone, $"the run did not end within {maxSeconds} s (stage {Scenario.Stage}, steps {string.Join(" | ", Scenario.SentSteps)})");
            return Runner.Result;
        }

        public void Dispose() => Runner.Dispose();
    }

    private static ScriptedFighter Ready(XpTableData table, ScriptedAceSession server) => ScriptedFighter.Ready(table, server, FighterTemplate.CharacterName);

    [Fact]
    public void An_absent_fighter_is_created_entered_alone_and_built_from_nothing_in_order()
    {
        using var run = new Run(null);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.True(run.Scenario.Created);
        Assert.Contains((MessageOpcodes.CharacterCreate, 0u), run.Server.Sent);
        Assert.Equal([run.FighterGuidOnServer], run.Server.EnterRequests); // no other character is selected
        Assert.Equal(run.FighterGuidOnServer, run.Scenario.EnteredGuid);

        Assert.Equal([(long)run.Table.XpForLevel(FighterTemplate.TargetLevel)], run.Fighter.XpAsked);
        var steps = run.Scenario.SentSteps;
        Assert.Equal(1 + FighterTemplate.Targets.Count + Grants + Wields + 1, steps.Count);
        Assert.StartsWith("xp ", steps[0], StringComparison.Ordinal);
        for (var i = 0; i < FighterTemplate.Targets.Count; i++)
        {
            Assert.StartsWith("raise " + FighterTemplate.Targets[i].Name + " ", steps[1 + i], StringComparison.Ordinal);
        }

        var afterRaises = 1 + FighterTemplate.Targets.Count;
        Assert.All(steps.Skip(afterRaises).Take(Grants), s => Assert.StartsWith("item ", s, StringComparison.Ordinal));
        Assert.StartsWith("wield Mace", steps[afterRaises + Grants], StringComparison.Ordinal); // the Mace first, then the five pieces
        Assert.All(steps.Skip(afterRaises + Grants + 1).Take(Wields - 1), s => Assert.StartsWith("wear the ", s, StringComparison.Ordinal));
        Assert.StartsWith("place to Holtburg", steps[^1], StringComparison.Ordinal);
        Assert.Equal(FighterTemplate.Targets.Count, run.Server.StatSends.Count);
        Assert.Equal(FighterKit.HealingKitCount, run.Fighter.Items.Count(i => i.Kind.Wcid == FighterKit.HealingKitWcid)); // two grants: kits do not stack
        Assert.Equal(FighterKit.ArrowCount, run.Fighter.Items.Where(i => i.Kind.Wcid == FighterKit.ArrowWcid).Sum(i => i.Stack));
        Assert.True(run.Fighter.InHoltburg);

        var verdict = run.Scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.Equal($"fighter=built level={run.Fighter.Level} template=ok kit=ok wielded={FighterKit.MaceWcid} worn=5", verdict);
        Assert.True(run.Fighter.Level >= FighterTemplate.TargetLevel);
        Assert.DoesNotContain(FighterTemplate.CharacterName, verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ready_fighter_is_only_verified_nothing_is_sent()
    {
        using var run = new Run(Ready);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.False(run.Scenario.Created);
        Assert.Empty(run.Scenario.SentSteps);
        Assert.Empty(run.Fighter.XpAsked);
        Assert.Empty(run.Fighter.ItemWcidsAsked);
        Assert.Empty(run.Server.StatSends);
        Assert.Empty(run.Server.WorldActionSends);
        Assert.Equal([run.FighterGuidOnServer], run.Server.EnterRequests);
        Assert.Equal($"fighter=ready level={FighterTemplate.TargetLevel} template=ok kit=ok wielded={FighterKit.MaceWcid} worn=5", run.Scenario.VerdictFields());
        Assert.Single(run.Log.Entries, e => e.Message.StartsWith("verdict ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_partial_fighter_gets_only_what_is_missing_a_skill_trained_with_the_skill_tables_credits_and_the_missing_lines_granted()
    {
        using var run = new Run((table, server) =>
        {
            var f = Ready(table, server);
            f.Untrain(FighterTemplate.Healing);
            f.Available = 100_000_000; // the experience the Healing raise will cost is still unspent
            f.DropItems(FighterKit.PotionWcid);
            f.DropItems(FighterKit.HealingKitWcid);
            f.InHoltburg = false;
            return f;
        });
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var steps = run.Scenario.SentSteps;
        Assert.Equal(
            [$"train skill Healing for {FighterBuildScenario.TrainCredits[FighterTemplate.Healing]} credits"],
            steps.Where(s => s.StartsWith("train ", StringComparison.Ordinal)).ToArray());
        Assert.Contains(run.Server.StatSends, s => s.Type == GameActionTypes.TrainSkill && s.Id == FighterTemplate.Healing && (int)s.Amount == ScriptedFighter.AceTrainedCost[FighterTemplate.Healing]);
        // The two healing kits are two grants (they do not stack), the 5 potions one.
        Assert.Equal(FighterKit.HealingKitCount, run.Fighter.ItemWcidsAsked.Count(w => w == FighterKit.HealingKitWcid));
        Assert.Equal(1, run.Fighter.ItemWcidsAsked.Count(w => w == FighterKit.PotionWcid));
        Assert.Equal(FighterKit.HealingKitCount + 1, run.Fighter.ItemWcidsAsked.Count);
        Assert.DoesNotContain(steps, s => s.StartsWith("xp ", StringComparison.Ordinal));
        Assert.DoesNotContain(steps, s => s.StartsWith("wield ", StringComparison.Ordinal) || s.StartsWith("wear ", StringComparison.Ordinal));
        Assert.StartsWith("place to Holtburg", steps[^1], StringComparison.Ordinal);
        Assert.StartsWith("fighter=built ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains("template=ok kit=ok", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void No_other_character_is_entered_a_full_account_creates_nothing_and_a_refused_grant_fails_by_name()
    {
        // A full account without the fighter: nothing is created, nobody is entered.
        using var full = new Run(null, server =>
        {
            server.Characters.Clear();
            for (uint i = 0; i < 11; i++)
            {
                server.Characters.Add(new Lifestone.Tests.Shared.Net.AceCharacter(0x50000100 + i, "Other " + i));
            }
        });
        Assert.False(full.Finish().Ok);
        Assert.Empty(full.Server.EnterRequests);
        Assert.DoesNotContain((MessageOpcodes.CharacterCreate, 0u), full.Server.Sent);
        Assert.Contains("no free character slot", full.Scenario.Failure, StringComparison.Ordinal);
        Assert.StartsWith("fighter=failed ", full.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Matches(Verdict, full.Scenario.VerdictFields());

        // The mod refuses the Mace's class id: the run fails naming the step, and the kit field names what is short.
        using var refused = new Run(null);
        refused.Fighter.UnknownWcids.Add(FighterKit.MaceWcid);
        Assert.False(refused.Finish().Ok);
        Assert.Contains("item Mace", refused.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("Mace:0/1", refused.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Matches(Verdict, refused.Scenario.VerdictFields());
        Assert.Equal([refused.FighterGuidOnServer], refused.Server.EnterRequests);
    }

    [Fact]
    public void A_hunter_left_holding_the_Shortbow_and_the_arrows_has_them_taken_off_in_that_order_before_the_Mace_is_wielded_3_1_fix2()
    {
        // The live round 4 state: a half-done weapon swap left the Mace in the pack and the Shortbow and the arrows wielded; ACE refused the Mace (WeenieError 0, CheckWeaponCollision), which the model reproduces.
        uint bow = 0, arrows = 0, mace = 0;
        using var run = new Run((table, server) =>
        {
            var f = Ready(table, server);
            mace = f.PutAway(FighterKit.MaceWcid);
            bow = f.Hold(FighterKit.ShortbowWcid, EquipMask.MissileWeapon);
            arrows = f.Hold(FighterKit.ArrowWcid, EquipMask.MissileAmmo);
            return f;
        });

        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var steps = run.Scenario.SentSteps;
        Assert.Equal(3, steps.Count);
        Assert.StartsWith("take off the wielded ammunition", steps[0], StringComparison.Ordinal);
        Assert.StartsWith("take off the wielded launcher 'Shortbow'", steps[1], StringComparison.Ordinal);
        Assert.StartsWith("wield Mace", steps[2], StringComparison.Ordinal);
        var sends = run.Server.WorldActionSends.Select(x => (x.Type, x.First)).ToArray();
        Assert.Equal([(GameActionTypes.PutItemInContainer, arrows), (GameActionTypes.PutItemInContainer, bow), (GameActionTypes.GetAndWieldItem, mace)], sends);
        Assert.Empty(run.Fighter.CollisionRefusals);
        Assert.Empty(run.Fighter.At(EquipMask.MissileWeapon | EquipMask.MissileAmmo));
        Assert.Equal([mace], run.Fighter.At(EquipMask.MeleeWeapon).Select(i => i.Guid));
        Assert.Equal($"fighter=built level={FighterTemplate.TargetLevel} template=ok kit=ok wielded={FighterKit.MaceWcid} worn=5", run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_a_second_Shortbow_and_a_second_Mace_in_the_pack_the_wielded_launcher_is_the_one_taken_off_and_one_Mace_ends_wielded_3_1_fix2()
    {
        // The duplicates the hunt's loot adds: the pack copies are listed before the wielded one, so a lookup that takes the first of a class finds a copy that is not wielded.
        uint wieldedBow = 0, copyBow = 0;
        using var run = new Run((table, server) =>
        {
            var f = Ready(table, server);
            f.PutAway(FighterKit.MaceWcid);
            copyBow = f.AddCopy(FighterKit.ShortbowWcid);
            f.AddCopy(FighterKit.MaceWcid);
            wieldedBow = f.Hold(FighterKit.ShortbowWcid, EquipMask.MissileWeapon); // the first Shortbow of the kit, listed before the copy
            return f;
        });

        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var sends = run.Server.WorldActionSends;
        Assert.Contains(sends, x => x.Type == GameActionTypes.PutItemInContainer && x.First == wieldedBow);
        Assert.DoesNotContain(sends, x => x.First == copyBow); // the copy in the pack is never touched
        Assert.Empty(run.Fighter.CollisionRefusals);
        Assert.Empty(run.Fighter.At(EquipMask.MissileWeapon | EquipMask.MissileAmmo));
        var maces = run.Fighter.At(EquipMask.MeleeWeapon);
        Assert.Single(maces);
        Assert.Equal(FighterKit.MaceWcid, maces[0].Kind.Wcid);
        Assert.Equal(2, run.Fighter.Items.Count(i => i.Kind.Wcid == FighterKit.MaceWcid));
        Assert.Equal($"fighter=built level={FighterTemplate.TargetLevel} template=ok kit=ok wielded={FighterKit.MaceWcid} worn=5", run.Scenario.VerdictFields());
    }

    [Fact]
    public void The_name_check_is_the_templates_and_the_required_verbs_are_the_builds()
    {
        Assert.True(FighterBuildScenario.NameCheck("Holtburg Hunter"));
        Assert.True(FighterBuildScenario.NameCheck("holtburg hunter"));
        Assert.False(FighterBuildScenario.NameCheck("BSD Mage"));
        Assert.False(FighterBuildScenario.NameCheck(null));
        Assert.Equal(["xp", "item"], FighterBuildScenario.RequiredVerbs);
        Assert.Equal(FighterTemplate.Targets.Where(t => t.Kind == MageStatKind.Skill && !t.Specialized).Select(t => t.Id).Order(), FighterBuildScenario.TrainCredits.Keys.Order());
    }
}
