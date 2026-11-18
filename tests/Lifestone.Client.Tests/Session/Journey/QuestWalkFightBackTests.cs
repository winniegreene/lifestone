using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Mage;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 8 G (<c>m6-quest-go-fight-back</c>; M5 D88: she always fights back an attacker): the quest station's walks over a stand-in ACE (<see cref="QuestRig"/>): attacked in mid-walk she stops, fights (<see cref="QuestStepLeg.TravelFightingBack"/>, the station's own fight-back) and the same walk goes on; a walk that cannot go on
/// ends by name; what does not attack her, or stands out of reach, changes nothing. Item 7's night ended with its one death on a walk that had no answer: a Jungle Reaver on the road.
/// </summary>
public sealed class QuestWalkFightBackTests
{
    private const uint Npc = 0x7F68200D;
    private const uint Reaver = 0x80001950;
    private const string ReaverName = "Jungle Reaver";

    private static QuestPlace Room => new(QuestRig.HomeCell, new Vector3(89, 12, 94), "her room");

    private static QuestThing Brighteyes => new(22642, "Brighteyes, the Tailor");

    private static QuestRig Rig()
    {
        var rig = new QuestRig();
        rig.Npc(Npc, Brighteyes.Name, 22642, QuestRig.HomeCell, new Vector3(90, 12, 94));
        rig.Armed([.. ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id)]);
        rig.CastAt = (_, target) => rig.Kill(target);
        return rig;
    }

    /// <summary>One go to her room, the range that ends there: the walk is all the run does.</summary>
    private static QuestRun GoRun(QuestRig rig) =>
        new(rig.Context(), QuestRig.Quest("test-go", new QuestDone(null, "never said", null), null, QuestRig.Step(0, QuestStepKind.Go, "to Brighteyes", place: Room, target: Brighteyes), QuestRig.Step(1, QuestStepKind.Talk, "her intro", target: Brighteyes)), JourneyPlans.FromHome, null, "0-0");

    /// <summary>The first walk to her room runs until it is stopped, with <paramref name="attacked"/> done on it.</summary>
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

    private static int WalksToTheRoom(QuestRig rig) => rig.Travel.Requests.Count(r => r.Destination.Name == "her room");

    [Fact]
    public void Attacked_in_mid_walk_she_fights_and_the_same_walk_finishes()
    {
        var rig = Rig();
        FirstWalkAttacked(rig, () => rig.Attacker(Reaver, ReaverName, 3f));
        var (_, status) = rig.Run(GoRun(rig));

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", rig.R.H.Log.Lines.TakeLast(14)));
        Assert.Single(rig.Lines("the walk is stopped: 'Jungle Reaver' 0x80001950 attacked the player and stands 3.0 m away (within 40 m): fight back (M5 D88)"), l => l.Contains("travel to to Brighteyes", StringComparison.Ordinal));
        Assert.Contains("cast:", Assert.Single(rig.Events, e => e.EndsWith("@80001950", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Single(rig.Lines("walk fight-back 1: check before: the walk to to Brighteyes is stopped"));
        Assert.Single(rig.Lines("walk fight-back 1: no attacker left; the same walk to to Brighteyes goes on from cell 0x"));
        Assert.Single(rig.Lines("walk fight-back: check after: the walk to to Brighteyes went on after 1 fight-back(s): it arrived"));
        Assert.Equal(2, WalksToTheRoom(rig)); // the same walk, not a new errand
        Assert.Contains(rig.Lines("quest_step: n=0 kind=go"), l => l.Contains(" ok=yes ", StringComparison.Ordinal) && l.Contains(" fought_back=1", StringComparison.Ordinal));
        Assert.Equal(CombatModes.NonCombat, rig.R.H.State.Self.Qualities.CombatMode); // the walk goes on in peace
    }

    private sealed class FixedCheck(params Lifestone.Client.Session.Casting.ShotVerdict[] answers) : Lifestone.Client.Session.Casting.IShotCheck
    {
        private int _next;

        public Lifestone.Client.Session.Casting.ShotVerdict Check(Lifestone.Client.Session.Contracts.IGameSessionView session, uint spellId, uint target) => answers[Math.Min(_next++, answers.Length - 1)];
    }

    private static readonly Lifestone.Client.Session.Casting.ShotVerdict Blocked = new(Lifestone.Client.Session.Casting.ShotAnswer.Blocked, "a ledge");

    private static readonly Lifestone.Client.Session.Casting.ShotVerdict Clear = new(Lifestone.Client.Session.Casting.ShotAnswer.Clear, "open air");

    /// <summary>M6 8 (the long round): the walk's stop and the fight's pick must agree, or they go round until the cap ends the errand.</summary>
    [Fact]
    public void An_attacker_the_shot_rule_dropped_does_not_stop_the_walk_again_from_afar()
    {
        var rig = Rig();
        rig.R.Api.UseShots(new FixedCheck(Blocked));
        var walks = 0;
        rig.Travel.RunningSteps = r =>
        {
            if (r.Destination.Name != "her room")
            {
                return 0;
            }

            walks++;
            if (walks == 1)
            {
                rig.Attacker(Reaver, ReaverName, 6f);
                return int.MaxValue;
            }

            return walks == 2 ? 8 : 0;
        };

        var (_, status) = rig.Run(GoRun(rig));

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", rig.R.H.Log.Lines.TakeLast(14)));
        Assert.Single(rig.Lines("walk fight-back 1: check before"));
        Assert.Empty(rig.Lines("walk fight-back 2"));
        Assert.Empty(rig.Lines("fight-back-cap"));
    }

    /// <summary>M6 8 (the long round): the dropped drudge that came up and bit her at 1.6 m. M5 D88: she always fights back.</summary>
    [Fact]
    public void A_dropped_attacker_that_comes_close_is_back_and_is_fought()
    {
        var rig = Rig();
        rig.OwnKillLoop = true; // M6 16 Q8: the old loop's shot drop (the skirmish checks no shot on a defence)
        rig.R.Api.UseShots(new FixedCheck(Blocked, Clear));
        var walks = 0;
        rig.Travel.RunningSteps = r =>
        {
            if (r.Destination.Name != "her room")
            {
                return 0;
            }

            walks++;
            switch (walks)
            {
                case 1:
                    rig.Attacker(Reaver, ReaverName, 6f);
                    return int.MaxValue;
                case 2:
                    rig.Kill(Reaver); // it left her view and came back, as the live drudge did: the rig has no move for a creature, and the drop is kept by guid
                    rig.Attacker(Reaver, ReaverName, 1.5f);
                    return int.MaxValue;
                default:
                    return 0;
            }
        };

        var (_, status) = rig.Run(GoRun(rig));

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", rig.R.H.Log.Lines.TakeLast(14)));
        Assert.Single(rig.Lines("shot drop: 0x80001950 is back: it stands 1.5 m away, within 3 m"));
        Assert.Contains(rig.Events, e => e.StartsWith("cast:", StringComparison.Ordinal) && e.EndsWith("@80001950", StringComparison.Ordinal));
        Assert.Empty(rig.Lines("fight-back-cap"));
    }

    [Theory]
    [InlineData(6.0, true)]
    [InlineData(3.0, false)]
    [InlineData(1.6, false)]
    public void A_drop_holds_only_while_the_attacker_stands_off(double metres, bool holds)
    {
        var lines = new List<string>();
        var ledger = new Lifestone.Client.Session.Casting.ShotLedger(lines.Add);
        ledger.Drop(Reaver, ReaverName, 0, "a ledge");

        Assert.Equal(holds, ledger.IsDropped(Reaver, 1, metres));
        Assert.Equal(holds, ledger.IsDropped(Reaver, 2)); // back means the drop is over, not only for this look
    }

    [Fact]
    public void A_bolt_from_afar_stops_the_walk_as_a_bite_does()
    {
        var rig = Rig();
        FirstWalkAttacked(rig, () =>
        {
            rig.Creature(Reaver, ReaverName, 12f);
            rig.MagicLine(ReaverName + " casts Flame Bolt I on you");
        });
        var (_, status) = rig.Run(GoRun(rig));

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", rig.R.H.Log.Lines.TakeLast(14)));
        Assert.Single(rig.Lines("attacked by Jungle Reaver (spell cast on the player)"));
        Assert.Single(rig.Lines("walk fight-back 1: no attacker left"));
    }

    [Fact]
    public void An_attacker_beyond_the_cast_range_is_walked_to_without_the_fight_stopping_itself()
    {
        var rig = Rig();
        rig.OwnKillLoop = true; // M6 16 Q8: the old loop's chase (the skirmish casts from the spell's own reach: QuestSkirmishTests)
        FirstWalkAttacked(rig, () =>
        {
            rig.Creature(Reaver, ReaverName, 30f); // M6 8 L: a spell from afar: a bite's reach is the cast range now, so a blow never starts a walk
            rig.MagicLine(ReaverName + " casts Flame Bolt I on you");
        });
        var (_, status) = rig.Run(GoRun(rig));

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", rig.R.H.Log.Lines.TakeLast(14)));
        Assert.Contains(rig.Travel.Requests, r => r.Destination.Name == ReaverName); // the fight's own chase, not stopped by the attacker it chases
        Assert.Single(rig.Lines("walk fight-back 1: check before"));
        Assert.Empty(rig.Lines("walk fight-back 2"));
    }

    [Fact]
    public void A_creature_that_does_not_attack_or_stands_out_of_reach_changes_nothing()
    {
        var quiet = Rig();
        quiet.Travel.RunningSteps = r =>
        {
            quiet.Creature(Reaver, ReaverName, 3f);
            return 20;
        };
        Assert.Equal(ScenarioStatus.Succeeded, quiet.Run(GoRun(quiet)).Status);
        Assert.Empty(quiet.Lines("walk fight-back"));
        Assert.Equal(1, WalksToTheRoom(quiet));

        var far = Rig();
        far.Travel.RunningSteps = r =>
        {
            far.Attacker(Reaver, ReaverName, 60f);
            return 20;
        };
        Assert.Equal(ScenarioStatus.Succeeded, far.Run(GoRun(far)).Status);
        Assert.Empty(far.Lines("walk fight-back"));
        Assert.Equal(1, WalksToTheRoom(far));
    }

    [Fact]
    public void A_walk_she_cannot_resume_recovers_once_and_then_ends_by_name()
    {
        var rig = Rig();
        FirstWalkAttacked(rig, () => rig.Attacker(Reaver, ReaverName, 3f));
        var walks = 0;
        rig.Travel.Refuse = r => r.Destination.Name == "her room" && ++walks > 1 ? "no path: the fight carried her off the way" : null;
        var (_, status) = rig.Run(GoRun(rig));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Single(rig.Lines("walk fight-back 1: no attacker left"));
        Assert.Single(rig.Lines("recovery=step-away: the walk to to Brighteyes was refused (no path: the fight carried her off the way)"));
        Assert.Equal(3, WalksToTheRoom(rig)); // cut, resumed and refused, planned once more
        Assert.Contains(rig.Lines("quest: key=test-go"), l => l.Contains(" ok=no ", StringComparison.Ordinal));
    }

    /// <summary>Long round 4: the go to Worcer aimed at his own standing point; his body stopped her 2.1 m short, and the step failed after three plans. A refused walk that leaves her within the use reach of the step's person is in reach.</summary>
    [Fact]
    public void A_refused_walk_that_leaves_her_in_reach_of_the_steps_person_is_in_reach_and_one_out_of_reach_still_ends_by_name()
    {
        var rig = Rig(); // Brighteyes stands 1 m from the room's point, where the walk is refused
        rig.Put(QuestRig.HomeCell, new Vector3(86.5f, 12, 94)); // the body stopped 3.5 m short of her: beyond "in reach already" (2.5 m), within the use reach (5 m)
        rig.Travel.Refuse = r => r.Destination.Name == "her room" ? "stuck after 3 plans: a body on the point" : null;
        var (_, status) = rig.Run(GoRun(rig));

        Assert.True(status == ScenarioStatus.Succeeded, string.Join(" | ", rig.R.H.Log.Lines.TakeLast(10)));
        Assert.Single(rig.Lines("reach=use: the walk to to Brighteyes was refused (stuck after 3 plans: a body on the point), but its person stands "));
        Assert.Empty(rig.Lines("recovery=step-away"));
        Assert.Equal(1, WalksToTheRoom(rig));

        var far = new QuestRig();
        far.Npc(Npc, Brighteyes.Name, 22642, QuestRig.HomeCell, new Vector3(150, 12, 94)); // well out of reach
        far.Armed([.. ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id)]);
        far.Travel.Refuse = r => r.Destination.Name == "her room" ? "stuck after 3 plans: a body on the point" : null;
        Assert.Equal(ScenarioStatus.Failed, far.Run(GoRun(far)).Status);
        Assert.Empty(far.Lines("reach=use: the walk to"));
        Assert.Single(far.Lines("recovery=step-away: the walk to to Brighteyes was refused"));
    }

    [Fact]
    public void An_attacker_the_bolts_cannot_hurt_ends_the_walk_by_the_fights_own_name()
    {
        var rig = Rig();
        rig.OwnKillLoop = true; // M6 16 Q8: the old loop's bound and line (the skirmish's: QuestSkirmishTests)
        FirstWalkAttacked(rig, () => rig.Attacker(Reaver, ReaverName, 3f));
        rig.CastAt = (_, _) =>
        {
            rig.Attacker(Reaver, ReaverName, 3f); // it does not die, and it attacks on
            rig.MagicLine(ReaverName + " resists your spell"); // M6 8 L: a resist reached it, so the wasted-cast drop does not apply and the fight's own limit does
        };
        var (_, status) = rig.Run(GoRun(rig), maxSeconds: 400);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.NotEmpty(rig.Lines("kill: the attackers not killed within 180 s"));
        Assert.Contains(rig.Lines("quest: key=test-go"), l => l.Contains(" fail=go:walk-fight:kill:timeout", StringComparison.Ordinal)); // M6 14 the quest kill: a failed walk, so the errand's walk rule keeps it callable
        Assert.Equal(1, WalksToTheRoom(rig)); // every cast resisted: it is not shown unhurt, so she does not run past it
    }

    [Fact]
    public void A_fight_during_which_the_server_moved_her_ends_the_walk_by_name()
    {
        var rig = Rig();
        FirstWalkAttacked(rig, () => rig.Attacker(Reaver, ReaverName, 3f));
        rig.CastAt = (_, target) =>
        {
            rig.Kill(target);
            rig.Teleport(0xA9B40001, new Vector3(10, 10, 94));
        };
        var (_, status) = rig.Run(GoRun(rig));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(rig.Lines("quest: key=test-go"), l => l.Contains(" fail=walk:moved-in-the-fight", StringComparison.Ordinal));
        Assert.Equal(1, WalksToTheRoom(rig)); // never resumed from the wrong place
    }

    [Fact]
    public void A_walk_stopped_again_and_again_ends_by_name_at_the_cap()
    {
        var rig = Rig();
        var next = Reaver;
        rig.Travel.RunningSteps = r =>
        {
            if (r.Destination.Name != "her room")
            {
                return 0;
            }

            rig.Attacker(++next, ReaverName, 3f);
            return int.MaxValue;
        };
        var (_, status) = rig.Run(GoRun(rig));

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(QuestStepLeg.MaxWalkFightBacks + 1, WalksToTheRoom(rig));
        Assert.Contains(rig.Lines("quest: key=test-go"), l => l.Contains(" fail=walk:fight-back-cap", StringComparison.Ordinal));
    }

    private const uint Gout = 0x80007AA4;
    private const uint GoutWcid = 21164; // creature-resists.json: ResistFire 0, ResistCold 0.65 (the world database's table)

    /// <summary>The fire line and the cold line in the book and the table (the cold levels the fire levels' data under their own ids and names), a wand in the hand; the scripted caster answers her dance.</summary>
    private static (QuestRig Rig, ushort[] Fire, ushort[] Cold) GoutRig()
    {
        var rig = new QuestRig();
        rig.Npc(Npc, Brighteyes.Name, 22642, QuestRig.HomeCell, new Vector3(90, 12, 94));
        var fire = ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id).ToArray();
        var cold = WarSpellData.BoltLineIds["cold"].Take(fire.Length).Select(id => (ushort)id).ToArray();
        for (var i = 0; i < cold.Length; i++)
        {
            rig.Caster.Table[cold[i]] = rig.Caster.Table[fire[i]] with { Id = cold[i], Name = "Frost Bolt " + LevelFloor.Roman[i] };
        }

        rig.Armed([.. fire, .. cold]);
        rig.CasterVitals = true;
        rig.Caster.ManaRegenPerSecond = 0; // the dance, not the regeneration, pays the bolt
        rig.Caster.StaminaRegenPerSecond = 0;
        return (rig, fire, cold);
    }

    /// <summary>
    /// M6 14 the quest kill (live ladder64, 2026-11-17 18:50:10 to 18:53:48 UTC: a Gout attacked the Atlan wand's go to the Minor Smoldering Stone; the station cast Ilservian's Flame for 0 points 50 times at "health ?", sat at mana 25 to
    /// 34 of 412 with stamina 138 of 309 and no dance, and <c>kill: timeout</c> ended the errand): a Gout attacks the walk with her mana at 20; the bolt is the cold line (its fire resistance 0), the stamina dance pays it, the cold bolt
    /// kills it, and the same walk goes on. No fire bolt is cast at it.
    /// </summary>
    [Fact]
    public void A_fire_immune_attacker_with_her_mana_low_gets_her_best_element_the_dance_pays_it_and_the_kill_finishes()
    {
        var (rig, fire, cold) = GoutRig();
        rig.OwnKillLoop = true; // M6 16 Q8: the old loop's lines (the skirmish's: QuestSkirmishTests)
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

        var trace = string.Join(" | ", rig.Lines("kill:").Concat(rig.Lines("walk fight-back")).Take(30));
        Assert.True(status == ScenarioStatus.Succeeded, trace);
        Assert.True(rig.Lines("kill: target Gout (0x80007AA4, wcid 21164)").Any(l => l.Contains("health 125/125 (no report yet: full)", StringComparison.Ordinal)), trace); // its maximum from the creature facts (weenie 21164)
        Assert.Single(rig.Lines("kill: the war spell for Gout is Frost Bolt "));
        Assert.NotEmpty(rig.Lines("kill: mana 20/")); // the bolt she could not pay
        Assert.NotEmpty(rig.Lines("kill: the dance for Frost Bolt "));
        Assert.Contains(rig.Events, e => e.StartsWith("self:", StringComparison.Ordinal)); // the dance's casts, at herself
        var atGout = rig.Events.Where(e => e.EndsWith("@80007AA4", StringComparison.Ordinal)).ToList();
        Assert.Single(atGout);
        Assert.Contains(cold, id => atGout[0] == $"cast:{id}@80007AA4");
        Assert.DoesNotContain(fire, id => rig.Events.Contains($"cast:{id}@80007AA4"));
        Assert.Single(rig.Lines("kill: Gout (0x80007AA4) is dead"));
        Assert.Single(rig.Lines("walk fight-back: check after: the walk to to Brighteyes went on after 1 fight-back(s): it arrived"));
    }

    /// <summary>
    /// M6 14 the quest kill (rules 3 and 4): with only the fire line, a Gout's bolts come to nothing (0 points); after <see cref="FightScenario.WallHitsBeforeBlock"/> Life Magic would take it, but her book holds no Harm Other, so she runs
    /// past it and the same walk goes on, never the kill's timeout.
    /// </summary>
    [Fact]
    public void An_attacker_she_cannot_hurt_is_run_past_and_the_walk_goes_on()
    {
        var rig = Rig();
        rig.OwnKillLoop = true; // M6 16 Q8: the old loop's lines (the skirmish's: QuestSkirmishTests)
        FirstWalkAttacked(rig, () => rig.Attacker(Gout, "Gout", 3f, GoutWcid));
        rig.CastAt = (_, _) => rig.MagicLine("You singe Gout for 0 points with Ilservian's Flame.");

        var (_, status) = rig.Run(GoRun(rig));

        var trace = string.Join(" | ", rig.Lines("kill:").Concat(rig.Lines("walk fight-back")).Take(30));
        Assert.True(status == ScenarioStatus.Succeeded, trace);
        Assert.Single(rig.Lines($"kill: {FightScenario.WallHitsBeforeBlock} bolts in a row at Gout (0x80007AA4) came to nothing (0 points)"));
        Assert.Equal(FightScenario.WallHitsBeforeBlock, rig.Events.Count(e => e.EndsWith("@80007AA4", StringComparison.Ordinal)));
        Assert.Contains(rig.Lines("kill: Gout (0x80007AA4): its bolts came to nothing and no life spell answers it ("), l => l.Contains("she runs past it", StringComparison.Ordinal));
        Assert.Empty(rig.Lines("not killed within"));
        Assert.Single(rig.Lines("walk fight-back: check after: the walk to to Brighteyes went on after 1 fight-back(s): it arrived"));
    }

    /// <summary>M6 14 the quest kill (rule 5): a kill that fails inside a walk's fight-back fails the walk (<c>go:walk-fight:...</c>), so the errand's walk rule keeps it callable.</summary>
    [Fact]
    public void A_kill_that_fails_in_a_walks_fight_back_is_a_failed_walk()
    {
        var rig = Rig();
        rig.Caster.ManaRegenPerSecond = 0;
        rig.Caster.SetVitals(0, 0); // nothing to cast and no stamina to dance with
        FirstWalkAttacked(rig, () => rig.Attacker(Reaver, ReaverName, 3f));

        var (_, status) = rig.Run(GoRun(rig), maxSeconds: 900);

        Assert.Equal(ScenarioStatus.Failed, status);
        var fail = Assert.Single(rig.Lines("quest: key=test-go"));
        Assert.Contains(" fail=go:walk-fight:kill:no-mana", fail, StringComparison.Ordinal);
        Assert.True(AutopilotScenario.ErrandWalkFailed("go:walk-fight:kill:no-mana"));
    }
}
