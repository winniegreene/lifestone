using System.Numerics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 8 L: the quest station's kill loop after the Worcer errand's death (live, 20:20 UTC): no mana never spins the frame and never walks; a dead attacker's name leaves the list; a fought attacker is one in reach; a shot the check blocks is not paid for; two casts that came to nothing drop the target; one that lands keeps it.
/// M6 16 Q8: these are the old loop's own rules (each cast's shot check, its wasted-cast drops, its wait for the mana), so they run on the fallback (<see cref="JourneyFacts.QuestOwnKillLoop"/>); the skirmish's answers are
/// <c>QuestSkirmishTests</c>'.
/// </summary>
public sealed class QuestKillShotTests
{
    private const uint First = 0x80001960;
    private const uint Second = 0x80001961;
    private const string Name = "Drudge Skulker";

    private sealed class Defence(JourneyContext context) : QuestStepLeg("shot-probe", context)
    {
        public string Shots => ShotFields;

        protected override IEnumerable<ActionHandle?> Script()
        {
            Watch();
            foreach (var h in Until(() => LiveAttacker() != 0, 5)) { yield return h; }

            var here = S.State.Self.Position!.Value;
            foreach (var h in KillOne(0, "the attackers", here.CellId, here.Origin, new Victim())) { yield return h; }
        }
    }

    private sealed class StepKill(JourneyContext context, uint cell, Vector3 near) : QuestStepLeg("shot-probe", context)
    {
        protected override IEnumerable<ActionHandle?> Script()
        {
            Watch();
            foreach (var h in KillOne(236, Name, cell, near, new Victim())) { yield return h; }
        }
    }

    private sealed class FixedCheck(params ShotVerdict[] answers) : IShotCheck
    {
        private int _next;

        public ShotVerdict Check(IGameSessionView session, uint spellId, uint target) => answers[Math.Min(_next++, answers.Length - 1)];
    }

    private static QuestRig Rig()
    {
        var rig = new QuestRig { OwnKillLoop = true }; // M6 16 Q8: the old loop's rules (see the class)
        rig.Armed([.. ArenaProfile.LineNamed(ArenaProfile.WarLineName)!.Spells.Select(b => (ushort)b.Id)]);
        return rig;
    }

    private static int Casts(QuestRig rig) => rig.Events.Count(e => e.StartsWith("cast:", StringComparison.Ordinal));

    private static int CastsAt(QuestRig rig, uint guid) => rig.Events.Count(e => e.StartsWith("cast:", StringComparison.Ordinal) && e.EndsWith($"@{guid:X8}", StringComparison.Ordinal));

    private static ScenarioStatus RunGuarded(QuestRig rig, Lifestone.Client.Session.Scenarios.Journey.IStationRun leg)
    {
        var running = Task.Run(() => rig.Run(leg).Status);
        Assert.True(running.Wait(TimeSpan.FromSeconds(60)), "the kill loop spun inside one frame");
        return running.Result;
    }

    [Fact]
    public void With_no_mana_and_an_attacker_in_range_the_loop_gives_the_frame_back_and_ends_by_name()
    {
        var rig = Rig();
        rig.After(0.5, () => rig.Attacker(First, Name, 3f));
        rig.SetMana(0);
        var leg = new Defence(rig.Context());

        var status = RunGuarded(rig, leg);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("kill: no mana", leg.Failure, StringComparison.Ordinal);
        Assert.Equal(0, Casts(rig));
        Assert.Single(rig.Lines("no cast and no walk"));
    }

    [Fact]
    public void With_no_mana_she_walks_after_nobody()
    {
        var rig = Rig();
        rig.Creature(First, Name, 30f);
        rig.SetMana(0);
        var here = rig.R.H.State.Self.Position!.Value;

        RunGuarded(rig, new StepKill(rig.Context(), here.CellId, here.Origin));

        Assert.DoesNotContain(rig.Travel.Requests, r => r.Destination.Name == Name);
        Assert.Empty(rig.Lines("reach=walk"));
    }

    [Fact]
    public void A_dead_attackers_name_leaves_the_list_so_another_of_that_name_is_not_taken_for_it()
    {
        var rig = Rig();
        rig.After(0.5, () =>
        {
            rig.Creature(Second, Name, 10f); // alive, in reach, and it never attacked
            rig.Attacker(First, Name, 3f);
        });
        rig.CastAt = (_, target) => rig.Kill(target);
        var leg = new Defence(rig.Context());

        Assert.Equal(ScenarioStatus.Succeeded, RunGuarded(rig, leg));

        Assert.Equal((1, 0), (CastsAt(rig, First), CastsAt(rig, Second)));
        Assert.Single(rig.Lines("defend: no living attacker left"));
        Assert.Single(rig.Lines($"kill: {Name} (0x{First:X8}) is dead"));
    }

    [Fact]
    public void A_creature_of_the_attackers_name_beyond_the_bite_reach_is_not_an_attacker()
    {
        var rig = Rig();
        rig.After(0.5, () => rig.Attacker(First, Name, 42f)); // the live "attacker" 42 m away

        RunGuarded(rig, new Defence(rig.Context()));

        Assert.Equal(0, Casts(rig));
        Assert.DoesNotContain(rig.Travel.Requests, r => r.Destination.Name == Name);
    }

    [Fact]
    public void Two_casts_with_no_bolt_drop_the_target_with_its_name_the_count_and_the_reason()
    {
        var rig = Rig();
        rig.After(0.5, () => rig.Attacker(First, Name, 6f));
        var leg = new Defence(rig.Context());

        Assert.Equal(ScenarioStatus.Succeeded, RunGuarded(rig, leg));

        Assert.Equal(2, CastsAt(rig, First));
        Assert.Single(rig.Lines($"wasted cast 2 of 2 on 0x{First:X8} ({Name})"));
        Assert.Single(rig.Lines($"shot drop: 0x{First:X8} ({Name}) is left alone for 120 s: 2 casts in a row wasted"));
        Assert.Contains("wasted_casts=2", leg.Shots, StringComparison.Ordinal);
    }

    [Fact]
    public void A_target_that_takes_damage_is_kept_and_its_count_starts_again()
    {
        var rig = Rig();
        rig.After(0.5, () => rig.Attacker(First, Name, 6f));
        var cast = 0;
        rig.CastAt = (_, target) =>
        {
            cast++;
            if (cast == 2)
            {
                rig.MagicLine($"You incinerate {Name} for 35 points with Flame Bolt I.");
            }
            else if (cast == 4)
            {
                rig.Kill(target);
            }
        };
        var leg = new Defence(rig.Context());

        Assert.Equal(ScenarioStatus.Succeeded, RunGuarded(rig, leg));

        Assert.Equal(4, CastsAt(rig, First)); // 1 wasted, 1 landed (the count starts again), 1 wasted, the kill
        Assert.Empty(rig.Lines("shot drop:"));
    }

    [Fact]
    public void A_blocked_shot_is_not_cast_and_an_attacker_behind_the_wall_is_dropped_by_name()
    {
        var rig = Rig();
        rig.R.Api.UseShots(new FixedCheck(new ShotVerdict(ShotAnswer.Blocked, "a ledge")));
        rig.After(0.5, () => rig.Attacker(First, Name, 6f));
        var leg = new Defence(rig.Context());

        Assert.Equal(ScenarioStatus.Succeeded, RunGuarded(rig, leg));

        Assert.Equal(0, Casts(rig));
        Assert.Single(rig.Lines($"shot drop: 0x{First:X8} ({Name}) is left alone for 120 s: the shot is blocked: a ledge"));
        Assert.Contains("shots_blocked=1", leg.Shots, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blocked_step_creature_gets_one_walk_for_a_line_before_it_is_dropped()
    {
        var rig = Rig();
        rig.R.Api.UseShots(new FixedCheck(new ShotVerdict(ShotAnswer.Blocked, "a corner")));
        rig.Creature(First, Name, 12f);
        var here = rig.R.H.State.Self.Position!.Value;

        RunGuarded(rig, new StepKill(rig.Context(), here.CellId, here.Origin));

        Assert.Equal(0, Casts(rig));
        Assert.Single(rig.Lines("from here: one walk to within"));
        Assert.Single(rig.Lines($"shot drop: 0x{First:X8}"));
    }

    [Fact]
    public void A_shot_the_check_called_clear_that_the_server_did_not_let_fly_is_a_counted_disagreement()
    {
        var rig = Rig();
        rig.R.Api.UseShots(new FixedCheck(new ShotVerdict(ShotAnswer.Clear, "open air")));
        rig.After(0.5, () => rig.Attacker(First, Name, 6f));
        var leg = new Defence(rig.Context());

        RunGuarded(rig, leg);

        Assert.Contains("shot_disagreements=2", leg.Shots, StringComparison.Ordinal);
        Assert.Equal(2, rig.Lines("shot check: DISAGREEMENT").Count);
    }
}
