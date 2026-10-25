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
using Lifestone.ServerTools;
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
        @"^fighter=(ready|built|failed) level=\d+ template=(ok|short|mismatch:[A-Za-z]+ \d+/\d+)( template_short=\S+)? kit=\S+ wielded=\d+ worn=\d+( magic=(trained|short|not_entered)(:[a-z+]+)? spells=\d+/\d+)?( gear=(\S+ gear_magic=\S+ spells_active=\S+|not_entered))?( gear_mana_min=\S+ recharged=\S+)?( pack=\d+/\d+)?( recreated=yes old=pending-deletion)?( fresh=(recreated|created|none) purged=\d+ refill=(none|level-up:\d+->\d+|failed))?$",
        RegexOptions.CultureInvariant);

    /// <summary>M4 fresh F1: the verdict's refill field after a fresh build of <paramref name="template"/>: one level-up from the template's level (the extra experience stays unspent).</summary>
    private static string Refilled(FighterTemplate template) => $"refill=level-up:{template.Level}->{template.Level + 1}";

    private static int Grants => FighterKit.Hunter.Items.Sum(i => i.Item.MaxStack is null ? i.Item.Stack : 1);

    private static int Wields => FighterKit.Hunter.Worn.Count();

    private sealed class Run : IDisposable
    {
        private readonly string _templateName;

        public Run(Func<XpTableData, ScriptedAceSession, ScriptedFighter>? shape, Action<ScriptedAceSession>? server = null, FighterTemplate? template = null, bool recreate = false, bool rechargeGear = false, bool fresh = false, Action<ScriptedPurge>? purge = null, int levels = 275)
        {
            _templateName = (template ?? FighterTemplate.Hunter).Name;
            Table = ScriptedMage.SyntheticTable(levels);
            Scenario = template is null
                ? new FighterBuildScenario(Table, HeadlessCharacterTests.StandInClasses(), Log)
                : new FighterBuildScenario(Table, HeadlessCharacterTests.StandInClasses(), Log, template: template, recreate: recreate, rechargeGear: rechargeGear, freshCharacter: fresh);
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
                        Server.Characters.Insert(0, new Lifestone.Tests.Shared.Net.AceCharacter(FighterGuid, (template ?? FighterTemplate.Hunter).Name));
                        Fighter = shape(Table, Server);
                    }
                    else
                    {
                        Fighter = ScriptedFighter.Fresh(Table, Server, (template ?? FighterTemplate.Hunter).Name);
                    }

                    Fighter.Attach();
                    if (fresh)
                    {
                        Server.AnswerDeletes = true;
                        Purge = new ScriptedPurge(Server);
                        purge?.Invoke(Purge);
                    }

                    return Server;
                });
        }

        public const uint FighterGuid = 0x50000030;

        /// <summary>The mod's purge as the scripted server plays it (M4 fresh B1); null unless the run is a fresh-character one.</summary>
        public ScriptedPurge Purge { get; private set; } = null!;

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

        public uint FighterGuidOnServer => Server.Characters.Single(c => c.Name == _templateName).Guid;

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

    private static ScriptedFighter Ready(XpTableData table, ScriptedAceSession server) => ScriptedFighter.Ready(table, server, FighterTemplate.HunterName);

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
        Assert.Equal(1 + FighterTemplate.Hunter.Targets.Count + Grants + Wields + 1, steps.Count);
        Assert.StartsWith("xp ", steps[0], StringComparison.Ordinal);
        for (var i = 0; i < FighterTemplate.Hunter.Targets.Count; i++)
        {
            Assert.StartsWith("raise " + FighterTemplate.Hunter.Targets[i].Name + " ", steps[1 + i], StringComparison.Ordinal);
        }

        var afterRaises = 1 + FighterTemplate.Hunter.Targets.Count;
        Assert.All(steps.Skip(afterRaises).Take(Grants), s => Assert.StartsWith("item ", s, StringComparison.Ordinal));
        Assert.StartsWith("wield Mace", steps[afterRaises + Grants], StringComparison.Ordinal); // the Mace first, then the five pieces
        Assert.All(steps.Skip(afterRaises + Grants + 1).Take(Wields - 1), s => Assert.StartsWith("wear the ", s, StringComparison.Ordinal));
        Assert.StartsWith("place to Holtburg", steps[^1], StringComparison.Ordinal);
        Assert.Equal(FighterTemplate.Hunter.Targets.Count, run.Server.StatSends.Count);
        Assert.Equal(FighterKit.HealingKitCount, run.Fighter.Items.Count(i => i.Kind.Wcid == FighterKit.HealingKitWcid)); // two grants: kits do not stack
        Assert.Equal(FighterKit.ArrowCount, run.Fighter.Items.Where(i => i.Kind.Wcid == FighterKit.ArrowWcid).Sum(i => i.Stack));
        Assert.True(run.Fighter.InHoltburg);

        var verdict = run.Scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.Equal($"fighter=built level={run.Fighter.Level} template=ok kit=ok wielded={FighterKit.MaceWcid} worn=5", verdict);
        Assert.True(run.Fighter.Level >= FighterTemplate.TargetLevel);
        Assert.DoesNotContain(FighterTemplate.HunterName, verdict, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_templates_fighter_is_created_and_entered_by_its_own_name_not_the_hunters_P4()
    {
        // P4's first live build: the choosing stage compared the list with the Hunter's name, so "Holtburg Brawler" was created and never entered.
        using var run = new Run(null, server => server.Characters.Add(new Lifestone.Tests.Shared.Net.AceCharacter(0x50000040, FighterTemplate.HunterName)), FighterTemplate.Melee);
        run.Finish();

        Assert.True(run.Scenario.Created);
        var brawler = run.Server.Characters.Single(c => c.Name == FighterTemplate.Melee.Name).Guid;
        Assert.Equal(brawler, run.Scenario.EnteredGuid);
        Assert.Equal([brawler], run.Server.EnterRequests);
    }

    [Fact]
    public void An_Archer_built_before_with_the_Shortbow_and_the_Arrows_is_granted_the_Longbow_and_the_Greater_Frost_Arrows_and_keeps_the_old_items_P4_arch()
    {
        // The Archer's kit changed from the Shortbow 307 and the Arrows 300 to the Longbow 31092 and the Greater Frost Arrows 5307; the character built before holds the old ones.
        using var run = new Run((table, server) =>
        {
            var f = ScriptedFighter.Ready(table, server, FighterTemplate.Archer.Name);
            f.DropItems(FighterKit.Archer.LauncherWcid);
            f.DropItems(FighterKit.GreaterFrostArrowWcid);
            f.AddOther(FighterKit.Hunter.ItemOf(FighterKit.ShortbowWcid)!.Item);
            f.AddOther(FighterKit.Hunter.ItemOf(FighterKit.ArrowWcid)!.Item);
            return f;
        }, template: FighterTemplate.Archer);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.Equal([FighterKit.Archer.LauncherWcid, FighterKit.GreaterFrostArrowWcid], run.Fighter.ItemWcidsAsked);
        Assert.Equal(FighterKit.ArcherArrowCount, run.Fighter.Items.Where(i => i.Kind.Wcid == FighterKit.GreaterFrostArrowWcid).Sum(i => i.Stack));
        Assert.Single(run.Fighter.Items, i => i.Kind.Wcid == FighterKit.ShortbowWcid);
        Assert.Single(run.Fighter.Items, i => i.Kind.Wcid == FighterKit.ArrowWcid);
        Assert.Equal($"fighter=built level={FighterTemplate.Archer.Level} template=ok kit=ok wielded=0 worn=7 magic=trained:creature+item+life+mana spells={FighterProfile.Archer.SpellsToLearn.Count}/{FighterProfile.Archer.SpellsToLearn.Count}", Core(run.Scenario.VerdictFields()));
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_fighter_built_before_the_caster_is_granted_the_Healers_Heart_once_into_the_pack_and_never_wielded_and_a_built_one_is_granted_none_P5a_C1b(string key)
    {
        var template = key == "melee" ? FighterTemplate.Melee : FighterTemplate.Archer;
        using var without = new Run((table, server) =>
        {
            var f = ScriptedFighter.Ready(table, server, template.Name);
            f.DropItems(template.Kit.CasterWcid);
            return f;
        }, template: template);
        var result = without.Finish();

        Assert.True(result.Ok, result.Failure + " / " + without.Scenario.Failure);
        Assert.Equal([8552u], without.Fighter.ItemWcidsAsked);
        var heart = Assert.Single(without.Fighter.Items, i => i.Kind.Wcid == 8552);
        Assert.Equal(0u, heart.Location);
        Assert.Equal("pack", without.Scenario.Caster);
        Assert.Contains(without.Scenario.SentSteps, s => s.StartsWith("item The Healer's Heart (weenie 8552), held 0 of 1", StringComparison.Ordinal));
        Assert.Contains(" kit=ok ", without.Scenario.VerdictFields(), StringComparison.Ordinal);

        using var built = new Run((table, server) => ScriptedFighter.Ready(table, server, template.Name), template: template);
        Assert.True(built.Finish().Ok);
        Assert.Empty(built.Fighter.ItemWcidsAsked);
        Assert.Contains(" kit=ok ", built.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_half_done_caster_swap_that_left_the_caster_in_the_hand_and_the_Mace_in_the_pack_is_repaired_by_the_build_P5a_C1b()
    {
        using var run = new Run((table, server) =>
        {
            var f = ScriptedFighter.Ready(table, server, FighterTemplate.Melee.Name);
            f.PutAway(FighterKit.Melee.WeaponWcid);
            f.Hold(8552, EquipMask.Held);
            return f;
        }, template: FighterTemplate.Melee);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.Empty(run.Fighter.CollisionRefusals);
        Assert.Contains(run.Scenario.SentSteps, s => s.StartsWith("take off the wielded caster to the pack", StringComparison.Ordinal));
        Assert.Equal("pack", run.Scenario.Caster);
        Assert.Single(run.Fighter.At(EquipMask.MeleeWeapon), i => i.Kind.Wcid == FighterKit.Melee.WeaponWcid);
        Assert.Empty(run.Fighter.At(EquipMask.Held));
        Assert.Contains($"wielded={FighterKit.Melee.WeaponWcid}", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_fighter_whose_kit_misses_the_caster_reports_it_by_name_in_the_kit_field_P5a_C1b()
    {
        using var run = new Run(
            (table, server) =>
            {
                var f = ScriptedFighter.Ready(table, server, FighterTemplate.Melee.Name);
                f.DropItems(8552);
                return f;
            },
            template: FighterTemplate.Melee);
        run.Fighter.UnknownWcids.Add(8552); // the mod refuses the grant: the build fails by name, and the verdict's kit field names the missing line
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains("kit=The_Healer's_Heart:0/1", run.Scenario.VerdictFields(), StringComparison.Ordinal);
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
        Assert.Equal(["xp", "item", "spell learn"], FighterBuildScenario.RequiredVerbs);
        Assert.Equal(FighterTemplate.All.SelectMany(f => f.Targets).Where(t => t.Kind == MageStatKind.Skill && !t.Specialized).Select(t => t.Id).Distinct().Order().Where(id => !FighterTemplate.FreeTrainedSkills.Contains(id)).ToArray(), FighterBuildScenario.TrainCredits.Keys.Order()); // the heritage's free trained skills (Run, Jump, Arcane Lore, ...) cost no credits
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_level_40_fighter_built_before_is_trained_raised_and_taught_its_spells_on_a_rerun_and_only_adds_M4_P5a_C1c_D38(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var profile = FighterProfile.For(template)!;
        using var run = new Run((table, server) => ScriptedFighter.Legacy(table, server, template.Name), template: template);
        var before = run.Fighter.Total;
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.False(run.Scenario.Created);
        var steps = run.Scenario.SentSteps;

        // xp first: exactly the difference to the new level; then the three trains in plan order with the SkillTable's credits (Creature Enchantment 8, Life Magic 12, Item Enchantment 8); then every target's raise in plan order (the base plan's stats buy on, the new ones follow); then the spell chunks.
        Assert.Equal([(long)run.Table.XpForLevel(template.Level) - before], run.Fighter.XpAsked);
        Assert.StartsWith("xp ", steps[0], StringComparison.Ordinal);
        Assert.Equal(
            ["train skill Creature Enchantment for 8 credits", "train skill Item Enchantment for 8 credits", "train skill Life Magic for 12 credits", "train skill Mana Conversion for 6 credits"], // M4 P5a C5: the floor rule lists the schools in the order trained (creature, item, life)
            steps.Where(s => s.StartsWith("train ", StringComparison.Ordinal)).ToArray());
        var raises = steps.Where(s => s.StartsWith("raise ", StringComparison.Ordinal)).ToArray();
        Assert.Equal(template.Targets.Count, raises.Length);
        for (var i = 0; i < raises.Length; i++)
        {
            Assert.StartsWith($"raise {template.Targets[i].Name} ", raises[i], StringComparison.Ordinal);
        }

        Assert.True(steps.ToList().IndexOf(raises[0]) > steps.ToList().FindLastIndex(s => s.StartsWith("train ", StringComparison.Ordinal)));
        var chunks = (profile.SpellsToLearn.Count + FighterBuildScenario.SpellChunk - 1) / FighterBuildScenario.SpellChunk;
        Assert.Equal(chunks, steps.Count(s => s.StartsWith("learn spells ", StringComparison.Ordinal)));
        Assert.Equal(1 + 4 + template.Targets.Count + chunks, steps.Count);
        Assert.Empty(run.Fighter.ItemWcidsAsked);
        Assert.Empty(run.Server.WorldActionSends);

        // The character ends with the three schools trained (Item Enchantment, M4 P5a C1c, D38), the profile known (Lifestone Recall among it) and the new level; the verdict says so.
        Assert.Equal(HeadlessCharacter.TrainedSkill, run.Fighter.SkillOf(FighterTemplate.CreatureEnchantment).Class);
        Assert.Equal(HeadlessCharacter.TrainedSkill, run.Fighter.SkillOf(FighterTemplate.ItemEnchantment).Class);
        Assert.Equal(HeadlessCharacter.TrainedSkill, run.Fighter.SkillOf(FighterTemplate.LifeMagic).Class);
        Assert.Contains(1635u, run.Fighter.Known);
        Assert.True(profile.SpellsToLearn.All(run.Fighter.Known.Contains));
        Assert.Equal(template.Level, run.Fighter.Level);
        Assert.Equal($"fighter=built level={template.Level} template=ok kit=ok wielded={template.WieldedWeaponWcid} worn=7 magic=trained:creature+item+life+mana spells={profile.SpellsToLearn.Count}/{profile.SpellsToLearn.Count}", Core(run.Scenario.VerdictFields()));
        Assert.Equal(34, 30 + (int)run.Table.SkillCreditsThroughLevel(template.Level, FighterTemplate.TargetLevel) - run.Fighter.Credits); // 8 + 8 + 12 + 6 (Mana Conversion, M4 P5a C7; D28: was 28) credits spent
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_fighter_built_to_level_48_by_C1_is_topped_up_with_Item_Enchantment_Lifestone_Recall_and_the_spread_and_nothing_is_lowered_M4_P5a_C1c_D38(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        using var run = new Run((table, server) => ScriptedFighter.C1Level48(table, server, template.Name), template: template);
        var before = run.Fighter.Total;
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var steps = run.Scenario.SentSteps;

        // Only the third school is trained; the two C1 schools sit above this plan's ranks (CE 127, LM 126 or 127 against 125) and are not raised, never lowered; every other target is raised.
        Assert.Equal(["train skill Item Enchantment for 8 credits", "train skill Mana Conversion for 6 credits"], steps.Where(s => s.StartsWith("train ", StringComparison.Ordinal)).ToArray());
        var raises = steps.Where(s => s.StartsWith("raise ", StringComparison.Ordinal)).ToArray();
        Assert.True(template.Targets.Count - 4 == raises.Length, string.Join(" | ", steps)); // M4 P5a C5: also Focus (96 = this plan's 96) and Self (106 against 96 or 95) sit at or above the plan, so they are not raised either
        Assert.DoesNotContain(raises, r => r.StartsWith("raise Creature Enchantment ", StringComparison.Ordinal) || r.StartsWith("raise Life Magic ", StringComparison.Ordinal) || r.StartsWith("raise Focus ", StringComparison.Ordinal) || r.StartsWith("raise Self ", StringComparison.Ordinal));
        Assert.Equal(127, run.Fighter.SkillOf(FighterTemplate.CreatureEnchantment).Ranks);
        Assert.True(run.Fighter.SkillOf(FighterTemplate.LifeMagic).Ranks >= 126);

        // The experience: one grant, the difference to the new level (the stand-in table's ranks are cheap, so the over-bought schools cost no shortfall here: the next test makes one).
        Assert.Equal([(long)run.Table.XpForLevel(template.Level) - before], run.Fighter.XpAsked);
        Assert.True(run.Fighter.Available >= 0);

        // Lifestone Recall is taught (C1's profile did not hold it); the rest of the spellbook answers known.
        Assert.Contains(1635u, run.Fighter.Known);
        Assert.True(FighterProfile.For(template)!.SpellsToLearn.All(run.Fighter.Known.Contains));
        Assert.Empty(run.Fighter.ItemWcidsAsked);
        Assert.Equal(template.Level, run.Fighter.Level);
        Assert.Matches(@"^fighter=built level=55 template=ok kit=ok wielded=\d+ worn=7 magic=trained:creature\+item\+life\+mana spells=\d+/\d+( gear=\S+ gear_magic=\S+ spells_active=\S+)?( pack=\d+/\d+)?$", run.Scenario.VerdictFields());
    }

    [Fact]
    public void A_fighter_already_at_the_level_but_short_of_experience_for_the_remaining_raises_is_granted_just_the_shortfall_M4_P5a_C1c_D38()
    {
        var template = FighterTemplate.Melee;
        using var run = new Run((table, server) =>
        {
            var f = ScriptedFighter.C1Level48(table, server, template.Name);
            f.Total = (long)table.XpForLevel(template.Level); // the level is reached, nothing unspent: the raises have nothing to pay with
            f.Level = template.Level;
            f.Available = 0;
            f.Credits = 19; // the live fighters' unspent credits before Mana Conversion (M4 P5a C7: the level's grant is not scripted here): 8 + 6 are trained below
            return f;
        }, template: template);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var asked = Assert.Single(run.Fighter.XpAsked);
        Assert.InRange(asked, 1, 1_000_000); // the remaining raises' cost, not a level's worth
        Assert.True(run.Fighter.Available >= 0);
        Assert.Contains("xp ", run.Scenario.SentSteps[0], StringComparison.Ordinal);
        Assert.Matches(@"^fighter=built level=55 template=ok ", run.Scenario.VerdictFields());
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_second_run_of_the_fighter_build_changes_nothing_and_says_so_M4_P5a_D33(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var profile = FighterProfile.For(template)!;
        using var run = new Run((table, server) => ScriptedFighter.Ready(table, server, template.Name), template: template);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.All(run.Scenario.SentSteps, s => Assert.StartsWith("learn spells ", s, StringComparison.Ordinal)); // the spellbook is only asked about (every id answers known) and nothing is taught
        Assert.NotEmpty(run.Scenario.SentSteps);
        Assert.Equal(0, run.Scenario.BuiltSteps);
        Assert.Empty(run.Fighter.XpAsked);
        Assert.Empty(run.Server.StatSends);
        Assert.Empty(run.Server.WorldActionSends);
        Assert.Empty(run.Fighter.ItemWcidsAsked);
        Assert.Equal($"fighter=ready level={template.Level} template=ok kit=ok wielded={template.WieldedWeaponWcid} worn=7 magic=trained:creature+item+life+mana spells={profile.SpellsToLearn.Count}/{profile.SpellsToLearn.Count}", Core(run.Scenario.VerdictFields()));
        Assert.Contains(run.Log.Entries, e => e.Message.StartsWith("nothing to add", StringComparison.Ordinal));
        Assert.Equal(run.Fighter.Known.Count, profile.SpellsToLearn.Distinct().Count());
    }

    [Fact]
    public void The_Hunter_is_left_as_it_was_built_no_magic_no_spells_and_no_magic_field_in_its_verdict_M4_P5a_D33()
    {
        using var run = new Run(Ready);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.Empty(run.Scenario.SentSteps);
        Assert.Empty(run.Fighter.LearnSizes);
        Assert.Empty(run.Fighter.Known);
        Assert.DoesNotContain("magic=", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(FighterTemplate.TargetLevel, run.Fighter.Level);
    }

    [Fact]
    public void A_fighter_with_only_some_spells_missing_learns_just_those_chunks_M4_P5a_D33()
    {
        var profile = FighterProfile.Brawler;
        using var run = new Run((table, server) =>
        {
            var f = ScriptedFighter.Ready(table, server, FighterTemplate.Melee.Name);
            f.Known.Clear();
            f.Known.UnionWith(profile.SpellsToLearn.Take(10));
            return f;
        }, template: FighterTemplate.Melee);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.Equal(profile.SpellsToLearn.Distinct().Count(), run.Fighter.Known.Count);
        Assert.All(run.Scenario.SentSteps, s => Assert.StartsWith("learn spells ", s, StringComparison.Ordinal));
        Assert.Equal($"fighter=built level={FighterTemplate.Melee.Level} template=ok kit=ok wielded={FighterKit.Melee.WeaponWcid} worn=7 magic=trained:creature+item+life+mana spells={profile.SpellsToLearn.Count}/{profile.SpellsToLearn.Count}", Core(run.Scenario.VerdictFields()));
    }

    [Theory]
    [InlineData("melee", "Endurance 100/10")]
    [InlineData("archer", "Strength 100/10")]
    public void A_character_created_with_another_attribute_split_fails_with_template_mismatch_and_nothing_is_bought_or_deleted_M4_P5a_C5_D40(string key, string first)
    {
        var template = FighterTemplate.ForKey(key)!;
        using var run = new Run((table, server) => ScriptedFighter.OldSplit(table, server, template.Name), template: template);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains("innate (creation) attributes differ", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("--fighter-recreate", run.Scenario.Failure, StringComparison.Ordinal);
        var verdict = run.Scenario.VerdictFields();
        Assert.StartsWith("fighter=failed ", verdict, StringComparison.Ordinal);
        Assert.Contains($" template=mismatch:{first} ", verdict, StringComparison.Ordinal);
        Assert.Matches(Verdict, verdict);
        Assert.Empty(run.Scenario.SentSteps); // nothing is bought on top
        Assert.Empty(run.Fighter.XpAsked);
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterDelete); // and no character is deleted without the opt-in
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void With_fighter_recreate_a_mismatched_character_is_deleted_by_the_retail_delete_listed_pending_then_created_and_built_in_that_order_M4_P5a_C5_D40(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        using var run = new Run((table, server) => ScriptedFighter.OldSplit(table, server, template.Name), server => server.AnswerDeletes = true, template, recreate: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.True(run.Scenario.Created);
        var verdict = run.Scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.Contains(" template=ok ", verdict, StringComparison.Ordinal);
        Assert.EndsWith(" recreated=yes old=pending-deletion", verdict, StringComparison.Ordinal);
        Assert.Contains($"fighter=built level={template.Level} ", verdict, StringComparison.Ordinal);

        // The order: the first entry (the mismatch), log off, delete, create, enter again, and then the build's mod verbs; the old entry stays pending (restorable), the new one is live.
        var opcodes = run.Server.Sent.Select(o => o.Opcode).ToList();
        var enter = opcodes.IndexOf(MessageOpcodes.CharacterEnterWorldRequest);
        var logOff = opcodes.IndexOf(MessageOpcodes.CharacterLogOff);
        var delete = opcodes.IndexOf(MessageOpcodes.CharacterDelete);
        var create = opcodes.IndexOf(MessageOpcodes.CharacterCreate);
        var enterAgain = opcodes.IndexOf(MessageOpcodes.CharacterEnterWorldRequest, create);
        Assert.True(enter >= 0 && enter < logOff && logOff < delete && delete < create && create < enterAgain, string.Join(",", opcodes.Select(o => $"{o:X}")));
        Assert.Single(opcodes, o => o == MessageOpcodes.CharacterDelete);
        Assert.Single(opcodes, o => o == MessageOpcodes.CharacterCreate);
        var mine = run.Server.Characters.Where(c => c.Name == template.Name).ToList();
        Assert.Equal(2, mine.Count);
        Assert.Equal(1u, mine.Single(c => c.Guid == Run.FighterGuid).DeleteField);
        Assert.Equal(0u, mine.Single(c => c.Guid != Run.FighterGuid).DeleteField);
        Assert.Contains(run.Log.Lines, l => l.Contains("recreated=yes old=pending-deletion", StringComparison.Ordinal));
        Assert.Contains(run.Log.Lines, l => l.Contains("template=mismatch:", StringComparison.Ordinal));

        // The new character is the template's: its creation attributes, the template's level and plan.
        Assert.Equal(template.Level, run.Fighter.Level);
        Assert.Equal(330u, template.Creation.Total);
        Assert.Empty(run.Fighter.Refusals);
        Assert.DoesNotContain(run.Log.Lines, l => l.Contains("Purge", StringComparison.Ordinal)); // no mod purge action (the scenario's own warning says it never uses one, in lower case)
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void With_fighter_recreate_and_no_mismatch_nothing_extra_happens_M4_P5a_C5_D40(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        using var run = new Run((table, server) => ScriptedFighter.Ready(table, server, template.Name), server => server.AnswerDeletes = true, template, recreate: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterDelete || o.Opcode == MessageOpcodes.CharacterCreate);
        Assert.DoesNotContain("recreated", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.StartsWith("fighter=ready ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_hunter_is_never_re_created_M4_P5a_C5_D40()
    {
        var ex = Assert.Throws<ArgumentException>(() => new FighterBuildScenario(ScriptedMage.SyntheticTable(), HeadlessCharacterTests.StandInClasses(), new RecordingLogSink(), template: FighterTemplate.Hunter, recreate: true));
        Assert.Contains("Brawler and the Archer only", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_fighter_without_the_gear_is_granted_it_then_wears_and_wields_it_and_the_verdict_names_it_M4_P5a_C6(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var set = template.Kit.Gear!;
        using var run = new Run((table, server) =>
        {
            var f = ScriptedFighter.Ready(table, server, template.Name);
            foreach (var piece in set.Pieces)
            {
                f.DropItems(piece.Wcid);
            }

            f.TrainArcaneLore(200);
            return f;
        }, template: template);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        // Granted in the kit's order (the weapon, the armour, the jewelry), every grant before the first wear.
        Assert.Equal(set.Pieces.Select(p => p.Wcid), run.Fighter.ItemWcidsAsked.Where(w => set.PieceOf(w) is not null));
        var steps = run.Scenario.SentSteps.ToList();
        Assert.True(steps.FindLastIndex(s => s.StartsWith("item ", StringComparison.Ordinal)) < steps.FindIndex(s => s.StartsWith("wield ", StringComparison.Ordinal) || s.StartsWith("wear the ", StringComparison.Ordinal)));
        // Every piece but the Archer's bow is on the body inside its own slots; the two rings and the two bracelets are on different hands and wrists.
        foreach (var piece in set.Pieces)
        {
            var item = run.Fighter.Items.Single(i => i.Kind.Wcid == piece.Wcid);
            if (piece.Kind == GearPieceKind.Weapon && !piece.IsMelee)
            {
                Assert.Equal(0u, item.Location); // the bow is kept in the pack; the hunt's swap wields it
                continue;
            }

            Assert.NotEqual(0u, item.Location);
            Assert.Equal(0u, item.Location & ~piece.Locations);
        }

        foreach (var kind in new[] { GearPieceKind.Ring, GearPieceKind.Bracelet })
        {
            var locations = set.Pieces.Where(p => p.Kind == kind).Select(p => run.Fighter.Items.Single(i => i.Kind.Wcid == p.Wcid).Location).ToArray();
            Assert.Equal(2, locations.Distinct().Count());
        }

        var verdict = run.Scenario.VerdictFields();
        Assert.Contains($" gear={set.Pieces.Count}/{set.Pieces.Count} ", verdict, StringComparison.Ordinal);
        Assert.Matches(@" pack=\d+/102$", verdict);
        // The side packs are granted and the component stacks moved into them, so the main pack keeps its headroom.
        Assert.Equal(run.Scenario.Kit.SidePacks, run.Fighter.Items.Count(i => i.Kind.Wcid == FighterKit.SidePackWcid));
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_fighter_whose_Arcane_Lore_is_untrained_fails_the_build_by_name_before_any_gear_is_worn_M4_P5a_C6(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        var set = template.Kit.Gear!;
        using var run = new Run((table, server) =>
        {
            var f = ScriptedFighter.Ready(table, server, template.Name);
            foreach (var piece in set.Pieces)
            {
                f.DropItems(piece.Wcid);
            }

            f.Untrain(14); // Arcane Lore is untrained (the build trains it again, free, and raises it: C5)
            return f;
        }, template: template);
        var result = run.Finish();

        // M4 P5a merge (C5 + C6): Arcane Lore is a free trained skill the plan's floor raises (136 at level 40, 164 at level 55: above both fighters' pieces, 100 and 110), so the build can no longer reach the gear step with it too low;
        // an untrained one fails by name at the skill step, before the gear is granted. The wield step's own refusal (FighterGearCheck.Refused) is pinned by FighterGearTests on the check.
        Assert.False(result.Ok);
        Assert.Contains("training skill Arcane Lore", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(run.Scenario.SentSteps, s => s.StartsWith("wield ", StringComparison.Ordinal) || s.StartsWith("wear the ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_main_pack_without_the_headroom_fails_the_build_naming_the_count_M4_P5a_C6()
    {
        var template = FighterTemplate.Melee;
        using var run = new Run((table, server) =>
        {
            var f = ScriptedFighter.Ready(table, server, template.Name);
            for (var i = 0; i < 80; i++)
            {
                f.AddOther(FighterKit.Hunter.ItemOf(FighterKit.ArrowWcid)!.Item); // the old loot that filled the live Hunter's pack
            }

            return f;
        }, template: template);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains("pack-full: the main pack holds", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("/102", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_the_recharge_a_Brawler_whose_Shirt_is_under_half_uses_the_kits_charge_on_itself_after_wearing_the_gear_and_is_granted_a_new_one()
    {
        var template = FighterTemplate.Melee;
        using var run = new Run((table, server) =>
        {
            var f = ScriptedFighter.Ready(table, server, template.Name);
            f.Appraises = true;
            f.SetMana(2032, 200); // Branith's Shirt, 500 full: 40%
            return f;
        }, template: template, rechargeGear: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var verdict = run.Scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.StartsWith("fighter=built ", verdict, StringComparison.Ordinal); // a charge used is a change
        Assert.Contains(" spells_active=n/a gear_mana_min=100 recharged=1 pack=", verdict, StringComparison.Ordinal);
        var use = Assert.Single(run.Fighter.StoneUses);
        Assert.Equal(run.FighterGuidOnServer, use.Target); // on the player: every equipped item below its maximum
        Assert.Equal(500, run.Fighter.ManaOf(run.Fighter.Items.Single(i => i.Kind.Wcid == 2032).Guid));
        // The kit's charge is granted again after the use, and the character ends holding one (the next build-verify's).
        Assert.Equal([FighterKit.ManaChargeWcid], run.Fighter.ItemWcidsAsked);
        Assert.Equal(1, run.Fighter.HeldCount(FighterKit.ManaChargeWcid));
        var steps = run.Scenario.SentSteps.ToList();
        Assert.True(steps.FindIndex(s => s.StartsWith("use a mana charge", StringComparison.Ordinal)) < steps.FindIndex(s => s.StartsWith("item Titan Mana Charge", StringComparison.Ordinal)), string.Join(" | ", steps));
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void With_the_recharge_a_ready_fighter_with_full_gear_is_only_appraised_and_stays_ready(string key)
    {
        var template = FighterTemplate.ForKey(key)!;
        using var run = new Run((table, server) =>
        {
            var f = ScriptedFighter.Ready(table, server, template.Name);
            f.Appraises = true;
            return f;
        }, template: template, rechargeGear: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var verdict = run.Scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.StartsWith("fighter=ready ", verdict, StringComparison.Ordinal); // the appraisals are a check, not a build step
        Assert.Contains(" gear_mana_min=100 recharged=0 ", verdict, StringComparison.Ordinal);
        Assert.Empty(run.Fighter.StoneUses);
        Assert.Equal(0, run.Scenario.BuiltSteps);
        Assert.All(run.Scenario.SentSteps, s => Assert.StartsWith("learn spells ", s, StringComparison.Ordinal)); // the spellbook's check only, as without the recharge
        // Every worn piece (and the Archer's bow in the pack) and the charge appraised.
        Assert.Equal(template.Kit.Gear!.Pieces.Count + 1, run.Fighter.Identified.Distinct().Count());
    }

    [Fact]
    public void Without_the_recharge_the_headless_build_appraises_nothing_and_has_no_gear_mana_fields()
    {
        var template = FighterTemplate.Melee;
        using var run = new Run((table, server) =>
        {
            var f = ScriptedFighter.Ready(table, server, template.Name);
            f.Appraises = true;
            f.SetMana(2032, 200);
            return f;
        }, template: template);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.Empty(run.Fighter.Identified);
        Assert.Empty(run.Fighter.StoneUses);
        Assert.DoesNotContain("gear_mana_min", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    /// <summary>The verdict without the gear and pack fields (M4 P5a C6; they are judged by <c>FighterGearBuildTests</c>).</summary>
    private static string Core(string verdict) => System.Text.RegularExpressions.Regex.Replace(verdict, @" gear=.*$| pack=\S+$", string.Empty);

    // ------------------------------------------------------------------------------------------------ M4 fresh B1: --fresh-character (D44, D45)

    private static FighterTemplate Template(string key) => FighterTemplate.ForKey(key)!;

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void With_fresh_character_the_live_fighter_is_deleted_from_the_list_never_entered_then_created_built_purged_placed_and_logged_off_in_that_order_M4_fresh_B1_D44_D45(string key)
    {
        var template = Template(key);
        using var run = new Run((table, server) => ScriptedFighter.Replaceable(table, server, template.Name), template: template, fresh: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.True(run.Scenario.Created);
        var verdict = run.Scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.StartsWith($"fighter=built level={template.Level + 1} template=ok ", verdict, StringComparison.Ordinal); // one level above the template's: the refill's level-up (M4 fresh F1)
        Assert.EndsWith(" fresh=recreated purged=1 " + Refilled(template), verdict, StringComparison.Ordinal); // fresh= and purged= stay on the one line the gate's judge reads, the refill last
        Assert.DoesNotContain("recreated=yes", verdict, StringComparison.Ordinal); // C5's field is C5's

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
        var created = run.Server.Characters.Single(c => c.Name == template.Name && c.DeleteField == 0);
        Assert.NotEqual(Run.FighterGuid, created.Guid);
        Assert.Equal([created.Guid], run.Server.EnterRequests);
        Assert.DoesNotContain(run.Server.Characters, c => c.Guid == Run.FighterGuid); // the old copy was listed pending (restorable) until the new copy's purge removed it
        Assert.Equal(1, run.Purge.Removed);

        // The mod's verbs after the entry: the build's, then the refill's experience grant (M4 fresh F1), then the purge asked until unknown-character (twice: one removal, then the refusal), then the place, and the log off last.
        var journal = run.Purge.Journal;
        var purges = journal.Select((j, i) => (j.Request, j.SentIndex, i)).Where(j => j.Request == nameof(CharPurgeRequest)).ToList();
        var place = journal.FindIndex(j => j.Request == nameof(PlacePoiRequest));
        Assert.Equal(2, purges.Count);
        Assert.Equal(purges[0].i + 1, purges[1].i);
        Assert.True(place == journal.Count - 1 && place == purges[1].i + 1, string.Join(",", journal.Select(j => j.Request)));
        Assert.All(journal.Take(purges[0].i), j => Assert.DoesNotContain(j.Request, new[] { nameof(CharPurgeRequest), nameof(PlacePoiRequest) }));
        Assert.Contains(journal.Take(purges[0].i), j => j.Request == nameof(XpRequest));
        Assert.Contains(journal.Take(purges[0].i), j => j.Request == nameof(ItemRequest));
        var lastItem = journal.FindLastIndex(j => j.Request == nameof(ItemRequest));
        Assert.Equal(nameof(XpRequest), journal[purges[0].i - 1].Request); // the refill's grant is the last verb before the purge ...
        Assert.True(lastItem < purges[0].i - 1, string.Join(",", journal.Select(j => j.Request))); // ... and comes after the build's last (the kit)
        Assert.Equal(2, journal.Count(j => j.Request == nameof(XpRequest))); // the build's grant and the refill's, never a third
        Assert.True(journal[0].SentIndex > enter);
        Assert.True(logOff > journal[place].SentIndex);
        Assert.All(run.Purge.Asked, n => Assert.Equal(template.Name, n)); // no other name is ever purged
        Assert.Contains(run.Log.Lines, l => l.Contains("fresh=recreated", StringComparison.Ordinal));
        Assert.True(run.Fighter.InHoltburg);

        // The three other characters of the account are as they were.
        Assert.Contains(run.Server.Characters, c => c.Name == "Amy" && c.DeleteField == 0);
        Assert.Contains(run.Server.Characters, c => c.Name == "Bo" && c.DeleteField == 1);
        Assert.Contains(run.Server.Characters, c => c.Name == "Cy" && c.DeleteField == 0);
    }

    [Fact]
    public void With_fresh_character_an_expired_old_row_is_purged_too_and_the_count_says_so_M4_fresh_B1_D45()
    {
        var template = Template("melee");
        using var run = new Run((table, server) => ScriptedFighter.Replaceable(table, server, template.Name), template: template, fresh: true, purge: p => p.ExpiredRows = 1);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.EndsWith(" fresh=recreated purged=2 " + Refilled(template), run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(3, run.Purge.Asked.Count); // two removals, then unknown-character
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_fresh_character_an_older_pending_copy_in_the_list_is_skipped_by_the_choosing_and_purged_with_the_deleted_one_M4_fresh_B1_D45()
    {
        var template = Template("archer");
        using var run = new Run(
            (table, server) => ScriptedFighter.Replaceable(table, server, template.Name),
            server => server.Characters.Add(new Lifestone.Tests.Shared.Net.AceCharacter(0x50000077, template.Name, 1)),
            template,
            fresh: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.EndsWith(" fresh=recreated purged=2 " + Refilled(template), run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(0x50000077u, run.Server.EnterRequests);
        Assert.Single(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterDelete);
        Assert.DoesNotContain(run.Server.Characters, c => c.Name == template.Name && c.DeleteField != 0); // both old rows are gone
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void With_fresh_character_and_no_live_copy_listed_the_fighter_is_only_created_and_built_and_the_verdict_says_created_M4_fresh_B1_D44(string key)
    {
        var template = Template(key);
        using var run = new Run(null, template: template, fresh: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.True(run.Scenario.Created);
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterDelete);
        Assert.Single(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterCreate);
        Assert.EndsWith(" fresh=created purged=0 " + Refilled(template), run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Single(run.Purge.Asked); // asked once: unknown-character, which ends the loop with nothing purged (and creating needs no purge)
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_fresh_character_a_pending_row_of_the_name_and_no_live_copy_is_created_over_and_the_new_copy_purges_it_M4_fresh_B1_D45()
    {
        var template = Template("melee");
        using var run = new Run(null, server => server.Characters.Add(new Lifestone.Tests.Shared.Net.AceCharacter(0x50000077, template.Name, 1)), template, fresh: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterDelete);
        Assert.EndsWith(" fresh=created purged=1 " + Refilled(template), run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(0x50000077u, run.Server.EnterRequests);
    }

    [Fact]
    public void With_fresh_character_a_delete_the_server_does_not_answer_fails_before_anything_is_created_M4_fresh_B1_D44()
    {
        var template = Template("melee");
        using var run = new Run((table, server) => ScriptedFighter.Replaceable(table, server, template.Name), template: template, fresh: true);
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
        var template = Template("archer");
        using var run = new Run((table, server) => ScriptedFighter.Replaceable(table, server, template.Name), template: template, fresh: true, purge: p => p.Refuse(code, sentence));
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("purging", result.Stage);
        Assert.StartsWith("purge: ", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(named, run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(andAlso, run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Single(run.Purge.Asked); // no second ask after a refusal
        Assert.DoesNotContain(run.Purge.Journal, j => j.Request == nameof(PlacePoiRequest)); // and the character is not placed
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterLogOff);
        Assert.StartsWith("fighter=failed ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.EndsWith(" fresh=recreated purged=0 " + Refilled(template), run.Scenario.VerdictFields(), StringComparison.Ordinal); // the refill ran before the purge asked
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_fresh_character_a_recreated_fighter_whose_old_copy_the_mod_did_not_purge_fails_the_run_M4_fresh_B1_D45()
    {
        var template = Template("melee");
        using var run = new Run((table, server) => ScriptedFighter.Replaceable(table, server, template.Name), template: template, fresh: true, purge: p => p.Gone = true);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains("purge: the old copy was not purged", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Single(run.Purge.Asked);
        Assert.EndsWith(" fresh=recreated purged=0 " + Refilled(template), run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Purge.Journal, j => j.Request == nameof(PlacePoiRequest));
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_fresh_character_a_mod_that_never_runs_out_of_old_copies_fails_at_the_cap_of_ten_purges_M4_fresh_B1_D45()
    {
        var template = Template("archer");
        using var run = new Run((table, server) => ScriptedFighter.Replaceable(table, server, template.Name), template: template, fresh: true, purge: p => p.Endless = true);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains($"purged {FreshPurge.MaxPurges} times", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(10, FreshPurge.MaxPurges);
        Assert.Equal(FreshPurge.MaxPurges, run.Purge.Asked.Count);
        Assert.EndsWith($" fresh=recreated purged={FreshPurge.MaxPurges} " + Refilled(template), run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Purge.Journal, j => j.Request == nameof(PlacePoiRequest));
    }

    [Fact]
    public void With_fresh_character_a_build_that_fails_leaves_the_old_copy_pending_and_purges_nothing_M4_fresh_B1_D45()
    {
        var template = Template("melee");
        using var run = new Run((table, server) => ScriptedFighter.Replaceable(table, server, template.Name), template: template, fresh: true);
        run.Fighter.UnknownWcids.Add(template.WieldedWeaponWcid); // the mod refuses the weapon's class id: the build ends short
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Contains($"(weenie {template.WieldedWeaponWcid})", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Empty(run.Purge.Asked); // the purge is only after a build that ended met
        Assert.Equal(1u, run.Server.Characters.Single(c => c.Guid == Run.FighterGuid).DeleteField); // the old copy stays listed as pending deletion: restorable
        Assert.Contains(" fresh=recreated purged=0", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.EndsWith(" refill=none", run.Scenario.VerdictFields(), StringComparison.Ordinal); // and so is the refill (M4 fresh F1)
        Assert.Single(run.Fighter.XpAsked); // only the build's grant: no refill grant after a build that ended short
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_fresh_character_a_mod_that_does_not_report_the_purge_verb_fails_at_the_status_stage_M4_fresh_B1()
    {
        var template = Template("archer");
        using var run = new Run(null, server => server.StatusVerbs = [.. ToolsWire.Verbs.Where(v => v != FreshPurge.RequiredVerb)], template, fresh: true);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("status", result.Stage);
        Assert.Contains($"'{FreshPurge.RequiredVerb}'", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Empty(run.Fighter.XpAsked);
    }

    // ------------------------------------------------------------------------------------------------ M4 fresh F1: the refill (the item's live gate round 1 finding)

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void With_fresh_character_the_vitals_are_refilled_by_one_level_up_after_the_build_and_before_the_purge_with_exactly_the_experience_to_the_next_level_M4_fresh_F1(string key)
    {
        var template = Template(key);
        using var run = new Run((table, server) => ScriptedFighter.Replaceable(table, server, template.Name), template: template, fresh: true);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        var next = run.Table.XpForLevel(template.Level + 1);
        Assert.Equal(2, run.Fighter.XpAsked.Count); // the build's grant and the refill's, never a third
        Assert.Equal(run.Table.XpForLevel(template.Level), (ulong)run.Fighter.XpAsked[0]); // the new copy had no experience: the build's grant is the template's level
        Assert.Equal((long)next - run.Fighter.XpAsked[0], run.Fighter.XpAsked[1]); // exactly the experience to the next level: nothing else
        Assert.Equal((long)next, run.Fighter.Total);
        Assert.Equal(template.Level + 1, run.Fighter.Level);
        var verdict = run.Scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.EndsWith(" fresh=recreated purged=1 " + Refilled(template), verdict, StringComparison.Ordinal);

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
        Assert.StartsWith("refill xp ", run.Scenario.SentSteps[^2], StringComparison.Ordinal); // recorded just before the place
        Assert.StartsWith("place to Holtburg", run.Scenario.SentSteps[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void With_fresh_character_and_the_vitals_already_full_the_refill_is_none_and_nothing_is_granted_M4_fresh_F1()
    {
        var template = Template("melee");
        using var run = new Run((table, server) => ScriptedFighter.Replaceable(table, server, template.Name), template: template, fresh: true);
        run.Fighter.FullVitals = true;
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.Single(run.Fighter.XpAsked); // the build's grant alone
        var verdict = run.Scenario.VerdictFields();
        Assert.Matches(Verdict, verdict);
        Assert.EndsWith(" fresh=recreated purged=1 refill=none", verdict, StringComparison.Ordinal);
        Assert.StartsWith($"fighter=built level={template.Level} template=ok ", verdict, StringComparison.Ordinal); // no level-up: the template's level
        Assert.Contains(run.Log.Lines, l => l.Contains("refill: every vital is at its maximum", StringComparison.Ordinal));
        Assert.Equal(1, run.Purge.Removed); // and the purge still ran after it
    }

    [Fact]
    public void With_fresh_character_a_refused_refill_grant_fails_the_run_by_name_and_purges_and_grants_nothing_more_M4_fresh_F1()
    {
        var template = Template("archer");
        using var run = new Run((table, server) => ScriptedFighter.Replaceable(table, server, template.Name), template: template, fresh: true);
        run.Fighter.XpGrantsBeforeRefusal = 1; // the build's grant is answered, the refill's is refused
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("refilling", result.Stage);
        Assert.StartsWith("refill: the grant of ", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("was refused", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("the experience grant was refused", run.Scenario.Failure, StringComparison.Ordinal); // the mod's own sentence
        Assert.Equal(2, run.Fighter.XpAsked.Count); // the refill asked once and is never asked again
        Assert.Empty(run.Purge.Asked);
        Assert.DoesNotContain(run.Purge.Journal, j => j.Request == nameof(PlacePoiRequest));
        Assert.Equal(1u, run.Server.Characters.Single(c => c.Guid == Run.FighterGuid).DeleteField); // the old copy stays restorable
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterLogOff);
        Assert.StartsWith("fighter=failed ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.EndsWith(" fresh=recreated purged=0 refill=failed", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Matches(Verdict, run.Scenario.VerdictFields());
    }

    [Fact]
    public void With_fresh_character_a_refill_the_server_does_not_play_fails_ten_seconds_after_the_answer_naming_the_level_and_the_vitals_M4_fresh_F1()
    {
        var template = Template("melee");
        using var run = new Run((table, server) => ScriptedFighter.Replaceable(table, server, template.Name), template: template, fresh: true);
        run.Fighter.SkipRefill = true; // ACE's level-up that does not run SetMaxVitals: the grant levels the character up and the vitals stay short
        double? sent = null;
        double? ended = null;
        for (var t = 0.0; t < 600 && ended is null; t += 0.05)
        {
            run.Time.Advance(TimeSpan.FromMilliseconds(50));
            run.Runner.Step(run.Runner.Session.Clock.Now);
            if (run.Server.PlayerGuid is not null)
            {
                run.Fighter.Pump();
            }

            if (sent is null && run.Fighter.XpAsked.Count == 2)
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
        Assert.Contains($"level {template.Level + 1}, was {template.Level}; health ", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(2, run.Fighter.XpAsked.Count); // never granted twice
        Assert.Empty(run.Purge.Asked);
        Assert.EndsWith(" fresh=recreated purged=0 refill=failed", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void With_fresh_character_an_xp_table_that_ends_at_the_templates_level_names_no_level_for_the_refill_and_fails_by_name_M4_fresh_F1()
    {
        var template = Template("archer");
        using var run = new Run((table, server) => ScriptedFighter.Replaceable(table, server, template.Name), template: template, fresh: true, levels: template.Level);
        var result = run.Finish();

        Assert.False(result.Ok);
        Assert.Equal("refilling", result.Stage);
        Assert.StartsWith($"refill: the XpTable cannot name a level above {template.Level} (it ends at level {template.Level})", run.Scenario.Failure, StringComparison.Ordinal);
        Assert.Single(run.Fighter.XpAsked); // the refill sent nothing
        Assert.Empty(run.Purge.Asked);
        Assert.EndsWith(" refill=failed", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_fresh_character_the_build_sends_no_refill_grant_and_the_verdict_has_no_refill_field_M4_fresh_F1()
    {
        // The same build as the fresh one (the xp, then the raises that lift the maxima over the current values): only the fresh character refills.
        var template = Template("melee");
        using var run = new Run(null, template: template);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.Single(run.Fighter.XpAsked);
        Assert.DoesNotContain("refill", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Scenario.SentSteps, s => s.StartsWith("refill", StringComparison.Ordinal));
        Assert.DoesNotContain(run.Log.Lines, l => l.Contains("refill", StringComparison.Ordinal));
        Assert.StartsWith($"fighter=built level={template.Level} template=ok ", run.Scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void Fresh_character_is_for_the_brawler_and_the_archer_alone_and_not_with_recreate_or_an_embedded_build_M4_fresh_B1()
    {
        var table = ScriptedMage.SyntheticTable();
        var classes = HeadlessCharacterTests.StandInClasses();
        var hunter = Assert.Throws<ArgumentException>(() => new FighterBuildScenario(table, classes, new RecordingLogSink(), template: FighterTemplate.Hunter, freshCharacter: true));
        Assert.Contains("Brawler and the Archer only", hunter.Message, StringComparison.Ordinal);
        var both = Assert.Throws<ArgumentException>(() => new FighterBuildScenario(table, classes, new RecordingLogSink(), template: FighterTemplate.Melee, recreate: true, freshCharacter: true));
        Assert.Contains("exclude each other", both.Message, StringComparison.Ordinal);
        var embedded = Assert.Throws<ArgumentException>(() => new FighterBuildScenario(table, classes, new RecordingLogSink(), template: FighterTemplate.Melee, endSession: false, freshCharacter: true));
        Assert.Contains("character list", embedded.Message, StringComparison.Ordinal);
        Assert.NotNull(new FighterBuildScenario(table, classes, new RecordingLogSink(), template: FighterTemplate.Archer, freshCharacter: true));
    }

    [Fact]
    public void Without_fresh_character_a_build_logs_no_fresh_field_and_asks_the_mod_to_purge_nothing_M4_fresh_B1()
    {
        var template = Template("melee");
        using var run = new Run((table, server) => ScriptedFighter.Ready(table, server, template.Name), template: template);
        var result = run.Finish();

        Assert.True(result.Ok, result.Failure + " / " + run.Scenario.Failure);
        Assert.DoesNotContain("fresh", run.Scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(run.Server.Sent, o => o.Opcode == MessageOpcodes.CharacterDelete);
    }
}
