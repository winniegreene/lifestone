using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 7 F4: the fight's self-defence (FightScenario.SelfDefence.cs) over the same scripted ACE: a creature the ground's profile avoids (the Virindi, wcid 23) that damages the player becomes the target and is killed; one that stops
/// attacking is dropped after the bound; one in a no-go cell is cast at from where the player stands when a line exists and held (and logged once) when none does; the profile's targets resume after. Nothing here needs the dats or a server.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint Virindi = 23;
    private const uint NoGoCell = 0x0105010D;
    private const uint FloorCell = 0x01050303;
    private const uint OtherFloorCell = 0x010502E1;
    private const uint VirindiGuid = 0x80002001;
    private const uint TuskerGuid = 0x80001001;

    private static void MeleeHit(Rig r, string name = "Virindi") =>
        r.R.Server((p, s) => AceM3Writers.DefenderNotification(p, s, name, 4, 0.1, 12, 4, false, 0));

    /// <summary>
    /// M6 fight targeting (m6-blow-unpinned): an attack a creature makes from range, as ACE writes it to its target: a war spell's damage line (<c>SpellProjectile.cs</c> 810-845). A melee notice names a name alone and is pinned only on a
    /// creature that can have struck her (a swing order, melee reach, or the only one of the name), so a test whose attackers stand off her uses this.
    /// </summary>
    private static void SpellAttack(Rig r, string name = "Virindi") => r.R.Raw(AceWireWriters.ServerMessage($"{name} scorches you for 12 points with Flame Bolt.", 7));

    private static void Evade(Rig r, string name = "Virindi") =>
        r.R.Server((p, s) => AceM3Writers.EvasionDefenderNotification(p, s, name));

    private static int LinesWith(Rig r, string text) => r.Log.Lines.Count(l => l.Contains(text, StringComparison.Ordinal));

    private static FightScenario ToTheFight(Rig r, FightScenario scenario)
    {
        for (var i = 0; i < 400 && scenario.Stage != "fight"; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal("fight", scenario.Stage);
        return scenario;
    }

    [Fact]
    public void An_avoided_creature_that_damages_the_player_is_the_target_before_the_tuskers_and_is_killed_and_counted()
    {
        var r = new Rig();
        var tusker = r.Add(TuskerGuid, TuskerSlave, FloorCell, 40); // 20 m, a profile target
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi"); // 10 m, avoided by the profile
        var tour = new RecordingTour();
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk { Tour = tour }));

        MeleeHit(r);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(virindi.Dead);
        Assert.False(tusker.Dead);
        Assert.All(r.CastTargets, t => Assert.Equal(VirindiGuid, t));
        Assert.Equal(1, scenario.Tally.Kills);
        Assert.Equal(1, scenario.Tally.TargetKills);
        Assert.Equal(1, scenario.Defended);
        Assert.Contains(" target_kills=1 defended=1 ", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal([(Virindi, FloorCell)], tour.Kills); // as any kill: the respawn schedule is told (it pairs it or counts it unpaired)
        Assert.Equal(1, LinesWith(r, "self-defence: Virindi 0x80002001 attacked (damage notice); fighting it before the targets"));
    }

    [Fact]
    public void An_evaded_attack_and_a_hostile_spell_line_make_the_attacker_the_target_too_logged_once()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        var master = r.Add(VirindiGuid, 237, FloorCell, 30, "Virindi Master", hitsToKill: 100);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        r.R.Raw(AceWireWriters.ServerMessage("Critical hit! Virindi Master scorches you for 31 points with Flame Bolt.", 7));
        Evade(r, "Virindi Master");
        MeleeHit(r, "Virindi Master");
        for (var i = 0; i < 12; i++)
        {
            r.Frame(scenario);
        }

        Assert.NotEmpty(r.CastTargets);
        Assert.All(r.CastTargets, t => Assert.Equal(master.Guid, t));
        Assert.Equal(1, LinesWith(r, "self-defence: Virindi Master 0x80002001 attacked (spell damage)"));
        Assert.Equal(0, LinesWith(r, "attacked (damage notice)") + LinesWith(r, "attacked (evasion notice)"));
    }

    [Fact]
    public void A_creature_that_stops_attacking_is_dropped_after_the_bound_and_the_profiles_targets_resume()
    {
        var r = new Rig(fight: new FightOptions { DefendForgetSeconds = 3 });
        var tusker = r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi", hitsToKill: 100); // never dies in this test
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        MeleeHit(r);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.False(virindi.Dead);
        Assert.True(tusker.Dead);
        Assert.Equal(VirindiGuid, r.CastTargets[0]);
        Assert.Equal(TuskerGuid, r.CastTargets[^1]);
        Assert.Equal(0, scenario.Defended);
        Assert.Equal(1, LinesWith(r, "self-defence: Virindi 0x80002001 has not attacked for 3 s; the targets resume"));
        Assert.Equal(0, scenario.AttackersNow);
    }

    [Fact]
    public void A_creature_that_keeps_attacking_stays_the_target_past_the_bound()
    {
        var r = new Rig(fight: new FightOptions { DefendForgetSeconds = 3 });
        r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi", hitsToKill: 100);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        for (var i = 0; i < 40; i++) // ten seconds, a hit every second
        {
            if (i % 4 == 0)
            {
                MeleeHit(r);
            }

            r.Frame(scenario);
        }

        Assert.NotEmpty(r.CastTargets);
        Assert.All(r.CastTargets, t => Assert.Equal(VirindiGuid, t));
    }

    [Fact]
    public void An_attacker_in_a_no_go_cell_with_a_line_is_cast_at_from_where_the_player_stands()
    {
        var r = new Rig();
        var tusker = r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        var virindi = r.Add(VirindiGuid, Virindi, NoGoCell, 30, "Virindi"); // the wing's cell: no-go in the profile
        var walk = new FakeWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(virindi.Dead);
        Assert.False(tusker.Dead);
        Assert.All(r.CastTargets, t => Assert.Equal(VirindiGuid, t));
        Assert.Equal(0, walk.Legs); // never walked into (or toward) the no-go cell
        Assert.Equal(1, scenario.Defended);
    }

    [Fact]
    public void An_attacker_in_a_no_go_cell_with_no_line_is_held_and_logged_once_and_the_targets_resume_after_the_bound()
    {
        var r = new Rig(fight: new FightOptions { DefendForgetSeconds = 3 });
        var tusker = r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        r.Add(VirindiGuid, Virindi, NoGoCell, 30, "Virindi", hitsToKill: 100);
        var walk = new FakeWalk { BlockedCell = NoGoCell };
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        var start = r.Now;
        double? firstCast = null;
        for (var i = 0; i < 80 && scenario.Stage == "fight"; i++)
        {
            r.Frame(scenario);
            if (firstCast is null && r.CastTargets.Count > 0)
            {
                firstCast = r.Now;
            }
        }

        Assert.Equal(0, walk.Legs); // held: nothing is walked into a no-go cell
        Assert.NotNull(firstCast);
        Assert.True(firstCast - start >= 2.9, $"the first cast came {firstCast - start} s after the attack");
        Assert.All(r.CastTargets, t => Assert.Equal(TuskerGuid, t)); // the Virindi was never cast at (no line), the tusker after the bound
        Assert.True(tusker.Dead);
        Assert.Equal(1, LinesWith(r, "self-defence: Virindi 0x80002001 stands in the no-go cell 0x0105010D with no line from here; holding"));
        Assert.Equal(0, scenario.Defended);
    }

    [Fact]
    public void An_attacker_with_no_clear_line_off_the_no_go_cells_walks_and_is_cast_at_once_in_line()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi");
        var walk = new FakeWalk { BlockedCell = FloorCell, TicksPerLeg = 4 };
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.Equal(1, walk.Legs);
        Assert.Equal(1, LinesWith(r, "self-defence: no attacker is in range with a clear line"));
        Assert.True(virindi.Dead);
        Assert.Equal(VirindiGuid, r.CastTargets[0]);
        Assert.Equal(1, scenario.Defended);
    }

    [Fact]
    public void A_walk_for_an_attacker_is_dropped_when_the_attacker_comes_into_line()
    {
        var r = new Rig();
        r.Add(TuskerGuid, TuskerSlave, OtherFloorCell, 45);
        var virindi = r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi");
        var walk = new FakeWalk { BlockedCell = FloorCell, TicksPerLeg = 1000 };
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        for (var i = 0; i < 8; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, walk.Legs);
        Assert.Empty(r.CastTargets);

        walk.BlockedCell = 0; // the attacker came round the corner
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(virindi.Dead);
        Assert.Equal(1, walk.Legs); // the leg was dropped, not walked to its end
        Assert.Equal(1, LinesWith(r, "self-defence: the walk is dropped; 0x80002001 is in range with a clear line"));
    }

    [Fact]
    public void With_the_bound_at_zero_self_defence_is_off_and_the_profiles_targets_are_cast_at()
    {
        var r = new Rig(fight: new FightOptions { DefendForgetSeconds = 0 });
        var tusker = r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        r.Add(VirindiGuid, Virindi, FloorCell, 30, "Virindi");
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        MeleeHit(r);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(tusker.Dead);
        Assert.All(r.CastTargets, t => Assert.Equal(TuskerGuid, t));
        Assert.Equal(0, scenario.Defended);
    }

    [Fact]
    public void An_attacker_no_living_creature_in_the_table_is_named_for_is_a_named_todo_logged_once_and_changes_nothing()
    {
        var r = new Rig();
        var tusker = r.Add(TuskerGuid, TuskerSlave, FloorCell, 40);
        var scenario = ToTheFight(r, r.Scenario(new FakeWalk()));

        MeleeHit(r, "Invisible Thing");
        MeleeHit(r, "Invisible Thing");
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario));

        Assert.True(tusker.Dead);
        Assert.Equal(1, LinesWith(r, "TODO(m5-defend-attacker-unseen)"));
    }

    [Theory]
    [InlineData("Virindi Mage scorches you for 31 points with Flame Bolt.", "Virindi Mage", "spell damage")]
    [InlineData("Critical hit! Overpower! Drudge Stalker sears you for 5 points with Acid Stream.", "Drudge Stalker", "spell damage")]
    [InlineData("Virindi casts Stamina to Mana Other and drains 40 points of your mana.", "Virindi", "spell drain")]
    [InlineData("You resist the spell cast by Virindi Master", "Virindi Master", "spell resisted")]
    [InlineData("Virindi Master casts Imperil Other on you", "Virindi Master", "spell cast on the player")]
    public void A_creatures_hostile_spell_line_names_its_source(string line, string source, string how)
    {
        Assert.True(FightScenario.HostileSpellSource(line, out var found, out var kind));
        Assert.Equal(source, found);
        Assert.Equal(how, kind);
    }

    [Theory]
    [InlineData("You scorch Tusker Slave for 40 points with Flame Bolt.")]
    [InlineData("Tusker Slave resists your spell")]
    [InlineData("With Drain Health you drain 5 points of health from Tusker Slave.")]
    [InlineData("You cast Flame Bolt on yourself")]
    [InlineData("Flame Bolt has expired.")]
    [InlineData("")]
    public void The_players_own_spell_lines_and_other_chatter_name_no_attacker(string line) =>
        Assert.False(FightScenario.HostileSpellSource(line, out _, out _));

    [Fact]
    public void The_defend_forget_bound_parses_zero_to_six_hundred_and_defaults_to_twenty()
    {
        Assert.Equal(20, new FightOptions().DefendForgetSeconds);
        var five = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-defend-forget-s=5"]));
        Assert.Empty(five.Errors);
        Assert.Equal(5, five.DefendForgetSeconds);
        Assert.Contains(" defend_forget_s=5", five.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-defend-forget-s=0"])).DefendForgetSeconds);
        foreach (var bad in new[] { "-1", "601", "soon" })
        {
            var parsed = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-defend-forget-s=" + bad]));
            Assert.Single(parsed.Errors);
            Assert.Equal(20, parsed.DefendForgetSeconds);
        }
    }
}
