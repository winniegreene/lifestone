using System.Numerics;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Mage;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 16 Q8 (the one fight-back, part 2): the quest steps' kill and their walks' defence cast through the skirmish (<c>QuestStepLeg.Skirmish.cs</c>), the old loop kept behind <c>--autopilot-quest-own-kill-loop</c>. Each changed
/// decision is shown on one state, before (the old loop, <see cref="QuestRig.OwnKillLoop"/>) where it differs, and after (the skirmish), over the stand-in ACE of the quest walk's tests (<see cref="QuestRig"/>).
/// </summary>
public sealed class QuestSkirmishTests
{
    private const uint Npc = 0x7F68200D;
    private const uint Reaver = 0x80001950;
    private const string ReaverName = "Jungle Reaver";
    private const uint Gout = 0x80007AA4;
    private const uint GoutWcid = 21164; // creature-resists.json: ResistFire 0, ResistCold 0.65 (the world database's table)

    private static QuestPlace Room => new(QuestRig.HomeCell, new Vector3(89, 12, 94), "her room");

    private static QuestThing Brighteyes => new(22642, "Brighteyes, the Tailor");

    private static QuestRig Rig(bool ownLoop = false)
    {
        var rig = new QuestRig { OwnKillLoop = ownLoop };
        rig.Npc(Npc, Brighteyes.Name, 22642, QuestRig.HomeCell, new Vector3(90, 12, 94));
        rig.Armed([.. ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id)]);
        rig.CastAt = (_, target) => rig.Kill(target);
        return rig;
    }

    private static QuestRun GoRun(QuestRig rig) =>
        new(rig.Context(), QuestRig.Quest("test-go", new QuestDone(null, "never said", null), null, QuestRig.Step(0, QuestStepKind.Go, "to Brighteyes", place: Room, target: Brighteyes), QuestRig.Step(1, QuestStepKind.Talk, "her intro", target: Brighteyes)), JourneyPlans.FromHome, null, "0-0");

    private static void FirstWalkAttacked(QuestRig rig, Action attacked)
    {
        var first = true;
        rig.Travel.RunningSteps = r =>
        {
            if (!first || r.Destination.Name != "her room")
            {
                return 0;
            }

            first = false;
            attacked();
            return int.MaxValue;
        };
    }

    private static string Trace(QuestRig rig) => string.Join(" | ", rig.Lines("kill").Concat(rig.Lines("fight-back")).Take(40));

    private static int CastsAt(QuestRig rig, uint guid) => rig.Events.Count(e => e.StartsWith("cast:", StringComparison.Ordinal) && e.EndsWith($"@{guid:X8}", StringComparison.Ordinal));

    /// <summary>The option: off by default (the skirmish), on is the old loop; registered; the autopilot hands it to the journey's facts.</summary>
    [Fact]
    public void The_old_loop_is_an_option_off_by_default()
    {
        Assert.True(OptionCatalog.Registry.IsKnown(AutopilotOptions.QuestOwnKillLoopOption));
        Assert.False(AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=level-up"])).QuestOwnKillLoop);
        var on = AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=level-up", AutopilotOptions.QuestOwnKillLoopOption]));
        Assert.Empty(on.Errors);
        Assert.True(on.QuestOwnKillLoop);
        Assert.False(AutopilotOptions.FromArguments(CommandLineArgument.ParseAll(["--autopilot-job=level-up", AutopilotOptions.QuestOwnKillLoopOption + "=off"])).QuestOwnKillLoop);
        Assert.False(new JourneyFacts().QuestOwnKillLoop);
    }

    /// <summary>
    /// The 90 s wait. State: a Jungle Reaver bites her walk, her mana and stamina at 0 (nothing to cast, nothing to dance with), no regeneration. Before: the old loop waits up to 90 s for the mana, then the walk fails
    /// <c>go:walk-fight:kill:no-mana</c>. After: the same failure, at once.
    /// </summary>
    [Fact]
    public void With_no_mana_and_no_dance_the_walk_fails_by_the_same_name_at_once_not_after_the_90_s_wait()
    {
        double FailedAfter(bool ownLoop)
        {
            var rig = Rig(ownLoop);
            rig.Caster.ManaRegenPerSecond = 0;
            rig.Caster.SetVitals(0, 0);
            FirstWalkAttacked(rig, () => rig.Attacker(Reaver, ReaverName, 3f));
            var start = rig.Now;
            var (_, status) = rig.Run(GoRun(rig), maxSeconds: 900);
            Assert.Equal(ScenarioStatus.Failed, status);
            Assert.Contains(" fail=go:walk-fight:kill:no-mana", Assert.Single(rig.Lines("quest: key=test-go")), StringComparison.Ordinal);
            Assert.Equal(0, CastsAt(rig, Reaver));
            return rig.Now - start;
        }

        Assert.True(FailedAfter(ownLoop: true) >= QuestStepLeg.RestSeconds); // before: the wait
        Assert.True(FailedAfter(ownLoop: false) < 10); // after: at once
    }

    /// <summary>
    /// The element and the dance (the live Gout of ladder64). State: a Gout (fire resistance 0) bites her walk with her mana at 20; she holds the fire and the cold lines. The stamina dance pays the bolt first, the skirmish casts the
    /// cold bolt (the line that hits its weenie hardest), it dies, and the same walk goes on; no fire bolt.
    /// </summary>
    [Fact]
    public void A_fire_immune_attacker_with_her_mana_low_gets_the_cold_bolt_through_the_skirmish_and_the_walk_goes_on()
    {
        var rig = Rig();
        var fire = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id).ToArray();
        var cold = WarSpellData.BoltLineIds["cold"].Take(fire.Length).Select(id => (ushort)id).ToArray();
        for (var i = 0; i < cold.Length; i++)
        {
            rig.Caster.Table[cold[i]] = rig.Caster.Table[fire[i]] with { Id = cold[i], Name = "Frost Bolt " + LevelFloor.Roman[i] };
        }

        rig.Armed([.. cold]);
        rig.CasterVitals = true;
        rig.Caster.ManaRegenPerSecond = 0;
        rig.Caster.StaminaRegenPerSecond = 0;
        rig.Caster.SetVitals(20, 275);
        FirstWalkAttacked(rig, () => rig.Attacker(Gout, "Gout", 3f, GoutWcid));
        rig.CastAt = (spell, target) =>
        {
            if (cold.Contains((ushort)spell))
            {
                rig.Kill(target);
            }
            else
            {
                rig.MagicLine("You singe Gout for 0 points with Ilservian's Flame.");
            }
        };

        var (_, status) = rig.Run(GoRun(rig));

        Assert.True(status == ScenarioStatus.Succeeded, Trace(rig));
        Assert.NotEmpty(rig.Lines("kill: the dance for Frost Bolt "));
        var atGout = rig.Events.Where(e => e.EndsWith("@80007AA4", StringComparison.Ordinal)).ToList();
        Assert.Contains(cold, id => Assert.Single(atGout) == $"cast:{id}@80007AA4");
        Assert.DoesNotContain(fire, id => rig.Events.Contains($"cast:{id}@80007AA4"));
        Assert.Single(rig.Lines("fight-back: 'Gout' 0x80007AA4 is dead"));
        Assert.Single(rig.Lines("walk fight-back: check after: the walk to to Brighteyes went on after 1 fight-back(s): it arrived"));
    }

    /// <summary>
    /// Bolts of 0 points (the quest steps' rule, kept by the skirmish for this host). State: only the fire line, a Gout singed for 0 points by each bolt, no Harm Other in the book. After <see cref="FightScenario.WallHitsBeforeBlock"/>
    /// bolts its bolts are given up, no life spell answers it, it is left, and the same walk goes on: never the kill's timeout.
    /// </summary>
    [Fact]
    public void An_attacker_her_bolts_do_not_hurt_is_left_and_the_walk_goes_on()
    {
        var rig = Rig();
        FirstWalkAttacked(rig, () => rig.Attacker(Gout, "Gout", 3f, GoutWcid));
        rig.CastAt = (_, _) => rig.MagicLine("You singe Gout for 0 points with Ilservian's Flame.");

        var (_, status) = rig.Run(GoRun(rig));

        Assert.True(status == ScenarioStatus.Succeeded, Trace(rig));
        Assert.True(FightScenario.WallHitsBeforeBlock == CastsAt(rig, Gout), Trace(rig));
        Assert.Single(rig.Lines($"fight-back: 'Gout' 0x80007AA4: {FightScenario.WallHitsBeforeBlock} bolts in a row came to nothing"));
        Assert.Single(rig.Lines("fight-back: 'Gout' 0x80007AA4: its bolts came to nothing"), l => l.Contains("left; the walk goes on", StringComparison.Ordinal));
        Assert.Empty(rig.Lines("way out is due"));
        Assert.Single(rig.Lines("walk fight-back: check after: the walk to to Brighteyes went on after 1 fight-back(s): it arrived"));
    }

    /// <summary>
    /// The reach. State: a Jungle Reaver 30 m off casts a bolt at her walk (past the old loop's 15 m cast range, inside her bolt's own range). Before: the old loop walks to it. After: the skirmish casts from where she stands, no walk.
    /// </summary>
    [Fact]
    public void An_attacker_inside_her_bolts_range_is_bolted_from_where_she_stands_with_no_chase()
    {
        QuestRig Attacked(bool ownLoop)
        {
            var rig = Rig(ownLoop);
            FirstWalkAttacked(rig, () =>
            {
                rig.Creature(Reaver, ReaverName, 30f);
                rig.MagicLine(ReaverName + " casts Flame Bolt I on you");
            });
            var (_, status) = rig.Run(GoRun(rig));
            Assert.True(status == ScenarioStatus.Succeeded, Trace(rig));
            return rig;
        }

        Assert.Contains(Attacked(ownLoop: true).Travel.Requests, r => r.Destination.Name == ReaverName); // before: the chase
        var after = Attacked(ownLoop: false);
        Assert.DoesNotContain(after.Travel.Requests, r => r.Destination.Name == ReaverName);
        Assert.Equal(1, CastsAt(after, Reaver));
        Assert.Single(after.Lines("fight-back: 'Jungle Reaver' 0x80001950 is dead"));
    }

    /// <summary>
    /// The bound. State: a Jungle Reaver that resists every bolt and bites on. The skirmish's own bound sets its way out due and the walk fails by the old name (<c>go:walk-fight:kill:timeout</c>), so the errand's walk rule keeps it
    /// callable; a resist is no bolt that came to nothing, so she does not run past it.
    /// </summary>
    [Fact]
    public void An_attacker_that_resists_every_bolt_ends_the_walk_at_the_skirmishs_bound_by_the_same_name()
    {
        var rig = Rig();
        FirstWalkAttacked(rig, () => rig.Attacker(Reaver, ReaverName, 3f));
        rig.CastAt = (_, _) =>
        {
            rig.Attacker(Reaver, ReaverName, 3f);
            rig.MagicLine(ReaverName + " resists your spell");
        };

        var (_, status) = rig.Run(GoRun(rig), maxSeconds: 400);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.NotEmpty(rig.Lines("kill: the skirmish's way out is due (why=timeout)"));
        Assert.Contains(rig.Lines("quest: key=test-go"), l => l.Contains(" fail=go:walk-fight:kill:timeout", StringComparison.Ordinal));
    }
}
