using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Tests.Shared.Net;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14, <c>m6-life-through</c> (the owner's rule of 2026-11-12): an attacker her bolt cannot reach is fought with Life Magic. Over the scripted ACE on the Tusker Cave's own ground (a Tusker Crimsonback: 110 health,
/// Magic Defense 94) with a mage of Life Magic 267 who knows Drain Health Other and Harm Other I to IV: the torch case of 2026-11-12 13:00 UTC end to end (bolts wasted, then a drain and Harm Other until the kill, with
/// no walk); the rule's place among the others (the bolt with a line, Heal Self under the drain line, the way out's mana, the mana order); the fallback with no Harm Other; and the bounds that stop a cast that never lands.
/// Nothing here needs the dats or a server, and nothing here was seen live.
/// </summary>
public sealed partial class FightScenarioTests
{
    private const uint Crimsonback = 1627;
    private const uint CrimsonGuid = 0x80003001;
    private const uint CaveHere = 0x5B4601FC;
    private const uint CaveThere = 0x5B4601FD;
    private const string CrimsonName = "Tusker Crimsonback";
    private const uint HarmFour = 1174;
    private const uint DrainOne = 1237;
    private const string CrimsonScorch = "Tusker Crimsonback scorches you for 12 points with Flame Bolt.";
    private const string LifeAt = "m6-life-through: Tusker Crimsonback 0x80003001, ";

    private static readonly uint[] HarmOneToFour = [7, 1172, 1173, HarmFour];
    private static readonly uint[] DrainOneToFour = [DrainOne, 1238, 1239, 1240];
    private static readonly Lazy<uint> LifeRanks267 = new(() =>
    {
        var probe = new Rig(life: true, lifeRanks: 0);
        Assert.True(GearCasts.TryCurrentSkill(probe.R.H.State.Self.Qualities, MageTemplate.LifeMagic, out var bare, out _));
        return 267 - bare;
    });

    /// <summary>The rig in the Tusker Cave (its targets' health and Magic Defense), the mage at Life Magic 267 knowing the bolts and <paramref name="lifeKnown"/>; the emergency is off unless a test's options turn it on.</summary>
    private static Rig CaveRig(IEnumerable<uint> lifeKnown, FightOptions? fight = null, uint mana = 400)
    {
        var cave = GroundCatalog.Get("tusker-cave");
        var bolts = ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarBolt).SelectMany(l => l.Spells).Select(s => s.Id);
        var options = (fight ?? new FightOptions { EmergencyFloorPercent = 0 }) with { Ground = "tusker-cave", GroundForTest = cave with { PullRest = null } };
        var r = new Rig(mana: mana, known: [.. bolts, .. lifeKnown], fight: options, life: true, lifeRanks: LifeRanks267.Value);
        r.R.H.R.Reducer.OnUpdatePosition(r.R.H.R.Context, ReducerHarness.Move(ActionHarness.Amy, 1, 3, 2, 1, cell: CaveHere, x: PlayerX));
        r.R.H.Unhide(stateStamp: 5);
        Assert.True(GearCasts.TryCurrentSkill(r.R.H.State.Self.Qualities, MageTemplate.LifeMagic, out var life, out _));
        Assert.Equal(267u, life);
        return r;
    }

    /// <summary>A fight that does not end at its first kill, so the cast that kills is read to its end.</summary>
    private static FightOptions Unending(bool lifeThrough = true) => new() { EmergencyFloorPercent = 0, Kills = FightOptions.UnlimitedKills, LifeThrough = lifeThrough, DefendForgetSeconds = 60 };

    private static uint HealthMaxOf(Rig r)
    {
        Assert.True(r.R.H.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out _, out var max));
        return max;
    }

    private static Creature AddCrimsonback(Rig r, float metres = 2.5f, int health = 110) => r.Add(CrimsonGuid, Crimsonback, CaveThere, PlayerY + metres, CrimsonName, hitsToKill: 100, health: health);

    private static FakeWalk TorchWalk() => new() { BlockedCell = CaveThere, KeepBlockedCell = true, TicksPerLeg = 3 };

    /// <summary>A spell line names the attacker: it is an attacker with no struck proof, so the line test alone decides.</summary>
    private static void SpellHit(Rig r) => r.R.Raw(AceWireWriters.ServerMessage(CrimsonScorch, 7));

    private static int LifeCasts(Rig r) => r.CastSpells.Count(id => DrainPicker.RowOf(id) is not null || HarmOtherRows.RowOf(id) is not null);

    [Fact]
    public void M6_14_the_torch_case_bolts_wasted_then_a_drain_and_Harm_Other_until_the_kill_with_no_walk()
    {
        // Live 2026-11-12 13:00 to 13:09 UTC, the Cave: a Crimsonback 2.5 m off behind a torch; its blows reached her, every bolt met the torch, and the fight had no answer but to walk.
        var r = CaveRig([.. DrainOneToFour, .. HarmOneToFour]);
        var tusker = AddCrimsonback(r);
        r.Script(Shot.NoProjectile, Shot.NoProjectile); // the two bolts the struck proof sends: wasted, and the shot rule drops it
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));
        r.SetHealth(HealthMaxOf(r) - 60);

        MeleeHit(r, CrimsonName);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario, 120));

        Assert.True(tusker.Dead);
        var bolt = scenario.SpellId;
        Assert.Equal([bolt, bolt, DrainOne, HarmFour, HarmFour, HarmFour, HarmFour], r.CastSpells); // the drain while it takes more than Harm Other would (28 of 110), then Harm Other IV: 24, 24, 24 and the last 10
        Assert.All(r.CastTargets, t => Assert.Equal(CrimsonGuid, t));
        Assert.Equal(0, walk.Legs);
        Assert.Equal(0, scenario.Walks);
        Assert.Equal(0, scenario.Sidesteps);
        Assert.Equal(0, LinesWith(r, "self-defence: no attacker is in range with a clear line"));
        Assert.Equal(1, LinesWith(r, "shot drop: 0x80003001 (Tusker Crimsonback) is left alone for 120 s: 2 casts in a row wasted"));
        Assert.Equal(1, LinesWith(r, "self-defence: the struck proof of Tusker Crimsonback 0x80003001 stands down: the shot rule dropped it"));
        Assert.Equal(1, LinesWith(r, LifeAt + "2.5 m off, cannot be reached by the bolt (the shot rule dropped it: its bolts came to nothing): Drain Health Other I (1237) at it through the obstacle: its modelled health 110 of 110 (queried); the drain is expected to take 27.8 and gives her 56 if it lands, Harm Other 24.3: the drain takes at least what Harm Other would, and heals her; mana "));
        Assert.Equal(1, LinesWith(r, LifeAt + "2.5 m off, cannot be reached by the bolt (the shot rule dropped it: its bolts came to nothing): Harm Other IV (1174) at it through the obstacle: its modelled health 82 of 110 (pushed); Harm Other is expected to take 24.3 (16 to 33 a landing, 99% to land), the drain 0: no drain is worth a cast; mana "));
        Assert.Equal(1, LinesWith(r, "Harm Other IV (1174) at it through the obstacle: its modelled health 58 of 110"));
        Assert.Equal(1, LinesWith(r, "Harm Other IV (1174) at it through the obstacle: its modelled health 34 of 110"));
        Assert.Equal(1, LinesWith(r, "Harm Other IV (1174) at it through the obstacle: its modelled health 10 of 110 (pushed); Harm Other is expected to take 9.9 "));
        Assert.Equal(1, LinesWith(r, "m6-life-through: Harm Other IV landed on Tusker Crimsonback 0x80003001: it took 10 by ACE's line; it is dead, and its kill ended the fight: no walk was made for it"));

        // The kill by Harm Other is a kill like a bolt's: the notice, the target's own, a defended one; what the lines said it took is damage dealt.
        Assert.Equal((1, 1, 1), (scenario.Tally.Kills, scenario.Tally.TargetKills, scenario.Defended));
        Assert.Equal((82, 82, 4), (scenario.Tally.DamageDealt, scenario.Tally.HarmDealt, scenario.Tally.HarmLinesDealt));
        Assert.Equal(1, scenario.Drains);
        Assert.Contains(" life_casts=5 life_drains=1 life_harms=4 life_kills=1 life_misses=0 life_asides=0", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(" wasted_casts=2 ", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void M6_14_an_attacker_with_no_line_and_no_proof_is_harmed_where_she_stands_and_each_line_lowers_its_modelled_health(bool pushed)
    {
        var r = CaveRig(HarmOneToFour, Unending());
        r.PushHealth = pushed; // false: a creature whose health the server does not push: the chat line alone says what the cast took
        r.CastSeconds = 1.0; // the answer to the health asked at the first cast is older than the 0.3 s in which a push and a damage line are one blow (FightScenario.PushSameEventSeconds)
        var tusker = AddCrimsonback(r);
        r.HarmAmounts.Enqueue(33);
        r.HarmAmounts.Enqueue(16);
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));

        SpellHit(r);
        for (var i = 0; i < 400 && !tusker.Dead; i++)
        {
            r.Frame(scenario);
        }

        Assert.True(tusker.Dead);
        Assert.Equal([HarmFour, HarmFour, HarmFour, HarmFour, HarmFour], r.CastSpells); // no bolt at all: 33, 16, 24, 24 and the last 13
        Assert.Equal((0, 0, 0), (walk.Legs, scenario.Sidesteps, scenario.StruckProofs));
        Assert.Equal(1, LinesWith(r, "target 0x80003001 (Tusker Crimsonback), was 0x00000000: m6-life-through: it attacked the player and no bolt reaches it from here"));
        Assert.Equal(1, LinesWith(r, LifeAt + "2.5 m off, cannot be reached by the bolt (no clear line from here): Harm Other IV (1174) at it through the obstacle: its modelled health 110 of 110"));
        Assert.Equal(1, LinesWith(r, "m6-life-through: Harm Other IV landed on Tusker Crimsonback 0x80003001: it took 33 by ACE's line; its modelled health is 77 of 110"));
        Assert.Equal(1, LinesWith(r, "m6-life-through: Harm Other IV landed on Tusker Crimsonback 0x80003001: it took 16 by ACE's line; its modelled health is 61 of 110"));
        Assert.Equal(1, LinesWith(r, "its modelled health 37 of 110"));
        Assert.Equal(1, LinesWith(r, "its modelled health 13 of 110"));
        Assert.Equal(1, LinesWith(r, "m6-life-through: Harm Other IV landed on Tusker Crimsonback 0x80003001: it took 13 by ACE's line; it is dead or gone"));
        Assert.Equal(1, LinesWith(r, "m6-life-through: Tusker Crimsonback 0x80003001 is dead or gone after 5 life cast(s) at it: no walk was made for it"));
        Assert.Equal(1, LinesWith(r, "kill 1: You have killed Tusker Crimsonback!"));
        Assert.Equal((1, 1, 1, 110), (scenario.Tally.Kills, scenario.Tally.TargetKills, scenario.Defended, scenario.Tally.DamageDealt));
        Assert.Equal((5, 0, 5, 1, 0, 0), (scenario.LifeThroughCasts, scenario.LifeThroughDrains, scenario.LifeThroughHarms, scenario.LifeThroughKills, scenario.LifeThroughMisses, scenario.LifeThroughAsides));
    }

    [Fact]
    public void M6_14_an_attacker_with_a_clear_line_is_the_bolts_as_before_and_a_line_that_clears_hands_it_back_to_the_bolt()
    {
        // A clear line: the bolt, which kills faster; no life cast.
        var open = CaveRig(HarmOneToFour);
        var first = open.Add(CrimsonGuid, Crimsonback, CaveThere, PlayerY + 5, CrimsonName, hitsToKill: 2, health: 110);
        var openScenario = ToTheFight(open, open.Scenario(new FakeWalk()));
        MeleeHit(open, CrimsonName);
        Assert.Equal(ScenarioStatus.Succeeded, open.Run(openScenario, 120));
        Assert.True(first.Dead);
        Assert.Equal(0, LifeCasts(open));
        Assert.Equal(0, openScenario.LifeThroughCasts);
        Assert.DoesNotContain("life_casts=", openScenario.VerdictFields(), StringComparison.Ordinal);

        // No line: Harm Other; the creature steps round the torch: the bolt takes over.
        var r = CaveRig(HarmOneToFour);
        var tusker = r.Add(CrimsonGuid, Crimsonback, CaveThere, PlayerY + 5, CrimsonName, hitsToKill: 2, health: 110);
        var walk = new FakeWalk { BlockedCell = CaveThere, KeepBlockedCell = true, TicksPerLeg = 3 };
        var scenario = ToTheFight(r, r.Scenario(walk));
        SpellHit(r);
        for (var i = 0; i < 200 && r.CastSpells.Count < 2; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal([HarmFour, HarmFour], r.CastSpells);
        walk.BlockedCell = 0;
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario, 120));

        var bolt = scenario.SpellId;
        Assert.True(tusker.Dead);
        Assert.Equal([HarmFour, HarmFour, bolt, bolt], r.CastSpells.Take(4));
        Assert.Equal(0, walk.Legs);
        Assert.Equal(2, scenario.LifeThroughCasts);
    }

    [Theory]
    [InlineData(5f)] // past the close-attacker rule's 3 m: without the rule the dropped attacker is left alone and the tour walks on
    [InlineData(2.5f)] // within it: without the rule the drop is taken back at once for two more bolts
    public void M6_14_an_attacker_the_shot_rule_dropped_is_harmed_while_the_drop_stands_whatever_the_line_test_says(float metres)
    {
        var r = CaveRig(HarmOneToFour);
        var tusker = AddCrimsonback(r, metres);
        r.Script(Shot.NoProjectile, Shot.NoProjectile);
        var walk = new FakeWalk(); // every line is clear by the test: the bolts are wasted all the same (a ledge, a torch the sweep does not see)
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r, CrimsonName);
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(scenario, 120));

        var bolt = scenario.SpellId;
        Assert.True(tusker.Dead);
        Assert.Equal([bolt, bolt, HarmFour, HarmFour, HarmFour, HarmFour, HarmFour], r.CastSpells);
        Assert.Equal(0, walk.Legs);
        Assert.Equal(0, LinesWith(r, "is back: it stands")); // the close-attacker rule did not take the drop back
        Assert.Equal(5, LinesWith(r, "cannot be reached by the bolt (the shot rule dropped it: its bolts came to nothing): Harm Other IV (1174)"));
        Assert.Contains(" life_casts=5 life_drains=0 life_harms=5 life_kills=1 life_misses=0 life_asides=0", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_with_no_Harm_Other_the_drain_goes_while_it_pays_then_the_walk_stands_as_before_named_once()
    {
        var r = CaveRig(DrainOneToFour, Unending());
        AddCrimsonback(r);
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));
        var max = HealthMaxOf(r);
        r.SetHealth(max - 60);

        for (var i = 0; i < 120; i++)
        {
            if (i % 8 == 0)
            {
                SpellHit(r);
            }

            r.Frame(scenario);
        }

        Assert.Equal([DrainOne], r.CastSpells); // one drain gave her 56 of the 60 she was missing; the next would give 4: not worth a cast, and a drain cannot finish it
        Assert.Equal(1, LinesWith(r, LifeAt + "2.5 m off, cannot be reached by the bolt (no clear line from here): Drain Health Other I (1237) at it through the obstacle: its modelled health 110 of 110"));
        Assert.Equal(1, LinesWith(r, "; the drain is expected to take 27.8 and gives her 56 if it lands, Harm Other 0: no Harm Other level is to be had: the drain while it pays; mana "));
        Assert.Equal(1, LinesWith(r, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"m6-life-through: no life cast at Tusker Crimsonback 0x80003001, 2.5 m off, which no bolt reaches from here (no clear line): Harm Other: no level is known; Drain Health Other: no level gives enough to be worth a cast; her health {max - 4} of {max}, mana ")));
        Assert.Equal(1, LinesWith(r, "its modelled health 82 of 110: the walk and the sidestep stand as before"));
        Assert.True(walk.Legs >= 1, "the walk stands as before");
        Assert.True(LinesWith(r, "self-defence: the walk toward Tusker Crimsonback 0x80003001 falls back to the tour's next leg") >= 1);
        Assert.Equal((1, 1, 0, 0), (scenario.LifeThroughCasts, scenario.LifeThroughDrains, scenario.LifeThroughHarms, scenario.LifeThroughAsides));
    }

    [Fact]
    public void M6_14_unhurt_and_with_no_Harm_Other_she_walks_at_once_as_before_and_says_why_once()
    {
        var r = CaveRig(DrainOneToFour, Unending());
        AddCrimsonback(r);
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 80; i++)
        {
            if (i % 8 == 0)
            {
                SpellHit(r);
            }

            r.Frame(scenario);
        }

        Assert.Empty(r.CastSpells);
        Assert.Equal(1, LinesWith(r, "which no bolt reaches from here (no clear line): Harm Other: no level is known; Drain Health Other: her health is full, and a drain takes only what she is missing; her health "));
        Assert.True(walk.Legs >= 1);
        Assert.Equal(0, scenario.LifeThroughCasts);
    }

    [Fact]
    public void M6_14_with_no_life_spell_at_all_the_walk_stands_and_one_line_a_fight_says_so()
    {
        var r = CaveRig([], Unending());
        AddCrimsonback(r);
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 80; i++)
        {
            if (i % 8 == 0)
            {
                SpellHit(r);
            }

            r.Frame(scenario);
        }

        Assert.Empty(r.CastSpells);
        Assert.Equal(1, LinesWith(r, "m6-life-through: no life cast at 0x80003001, which no bolt reaches from here: Harm Other: no level is known, and no Drain Health Other level is known and paid for; the walk and the sidestep stand as before (said once a fight)"));
        Assert.True(walk.Legs >= 1);
    }

    [Theory]
    [InlineData(nameof(Shot.Resist), 3, "resisted")] // "<name> resists your spell"
    [InlineData(nameof(Shot.OutOfRange), 3, "out-of-range")] // UseDone 0x0550: the record of where it stands was wrong
    [InlineData(nameof(Shot.NoProjectile), 3, "unproven")] // UseDone 0 and no line: nothing proves it landed
    [InlineData(nameof(Shot.Fizzle), 9, "fizzled")] // each cast is three tries while they fizzle (CastRules.Utility)
    public void M6_14_life_casts_that_do_not_land_stop_at_three_in_a_row_and_the_attacker_is_left_to_the_walk(string answer, int sent, string end)
    {
        var shot = Enum.Parse<Shot>(answer);
        var r = CaveRig(HarmOneToFour, Unending());
        AddCrimsonback(r);
        r.Script([.. Enumerable.Repeat(shot, 60)]);
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 320; i++) // 80 s: under the 120 s it is set aside for
        {
            if (i % 8 == 0)
            {
                SpellHit(r); // it goes on attacking
            }

            r.Frame(scenario);
        }

        Assert.Equal(sent, r.CastSpells.Count);
        Assert.All(r.CastSpells, id => Assert.Equal(HarmFour, id));
        Assert.Equal(3, LinesWith(r, $"m6-life-through: Harm Other IV at Tusker Crimsonback 0x80003001 did not land ({end}"));
        Assert.Equal(1, LinesWith(r, "miss 1 of 3 in a row"));
        Assert.Equal(1, LinesWith(r, "miss 3 of 3 in a row"));
        Assert.Equal(1, LinesWith(r, $"m6-life-through: Tusker Crimsonback 0x80003001 is left to the walk and the sidestep for 120 s: 3 life casts in a row at it did not land, the last: {end} (m6-life-through-aside)"));
        Assert.Equal((3, 0, 0, 3, 1), (scenario.LifeThroughCasts, scenario.LifeThroughHarms, scenario.LifeThroughKills, scenario.LifeThroughMisses, scenario.LifeThroughAsides));
        Assert.True(walk.Legs >= 1, "the walk and the sidestep stand as before while it is aside");
    }

    [Fact]
    public void M6_14_the_loop_of_13_00_UTC_cannot_come_back_a_striking_attacker_no_spell_lands_on_gets_five_casts_not_48()
    {
        // Live 2026-11-12 13:00 to 13:09 UTC: the struck proof and the close-attacker rule undid each other's stop and she cast at one unreachable tusker 48 times. Here it strikes her all the way through, 2.5 m off.
        var r = CaveRig(HarmOneToFour, Unending());
        var tusker = AddCrimsonback(r);
        r.Script([Shot.NoProjectile, Shot.NoProjectile, .. Enumerable.Repeat(Shot.Resist, 60)]);
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 400; i++) // 100 s: both stops (the shot rule's drop, the life rule's aside) stand for 120 s
        {
            if (i % 8 == 0)
            {
                MeleeHit(r, CrimsonName);
            }

            r.Frame(scenario);
        }

        var bolt = scenario.SpellId;
        Assert.False(tusker.Dead);
        Assert.Equal([bolt, bolt, HarmFour, HarmFour, HarmFour], r.CastSpells); // two wasted bolts, three resisted life casts, and nothing more
        Assert.Equal(1, scenario.LifeThroughAsides);
        Assert.Equal(1, scenario.StruckProofsVoid);
        Assert.Equal(0, LinesWith(r, "is back")); // neither stop was taken back
        Assert.Contains(" life_casts=3 life_drains=0 life_harms=0 life_kills=0 life_misses=3 life_asides=1", scenario.VerdictFields(), StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_an_attacker_set_aside_is_back_after_its_120_s_and_a_landing_starts_the_count_again()
    {
        var r = CaveRig(HarmOneToFour, Unending());
        var tusker = AddCrimsonback(r);
        r.Script(Shot.Resist, Shot.Resist, Shot.Normal, Shot.Resist, Shot.Resist, Shot.Resist); // two misses, a landing (the count starts again), three misses: aside
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 2000 && !tusker.Dead; i++)
        {
            if (i % 8 == 0)
            {
                SpellHit(r);
            }

            r.Frame(scenario);
        }

        Assert.True(tusker.Dead);
        Assert.Equal(1, scenario.LifeThroughAsides);
        Assert.Equal(2, LinesWith(r, "miss 2 of 3 in a row")); // before the landing and after it
        Assert.Equal(1, LinesWith(r, "miss 3 of 3 in a row"));
        Assert.Equal(1, LinesWith(r, "m6-life-through: Tusker Crimsonback 0x80003001 is back in the rule's reach: its 120 s aside are up"));
        Assert.Equal((5, 5), (scenario.LifeThroughMisses, scenario.LifeThroughHarms)); // 24 before the aside, then 24, 24, 24 and 14 after it
    }

    [Fact]
    public void M6_14_under_the_drain_line_Heal_Self_comes_first_then_the_life_cast()
    {
        var r = CaveRig([.. HarmOneToFour, .. VitalsMeans.HealSelf.Take(4)], Unending());
        var tusker = AddCrimsonback(r);
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));
        var max = HealthMaxOf(r);
        r.SetHealth(max * 40 / 100); // under --fight-drain-under (50)

        for (var i = 0; i < 600 && !tusker.Dead; i++)
        {
            if (i % 8 == 0)
            {
                SpellHit(r);
            }

            r.Frame(scenario);
        }

        Assert.True(tusker.Dead);
        var firstHarm = r.CastSpells.IndexOf(HarmFour);
        Assert.True(firstHarm >= 1, "Heal Self first");
        Assert.All(r.CastSpells.Take(firstHarm), id => Assert.Contains(id, VitalsMeans.HealSelf));
        Assert.All(r.CastSpells.Skip(firstHarm), id => Assert.Equal(HarmFour, id));
        Assert.True(r.Health * 100 >= max * 75, "the health is at the safe line (--fight-drain-until) when the life casts begin");
        Assert.True(LinesWith(r, "health: heal: Heal Self IV with no target in reach at health ") >= 1);
        Assert.Equal(0, walk.Legs);
    }

    [Fact]
    public void M6_14_the_way_out_keeps_its_mana_a_life_cast_under_the_line_is_refused_and_the_one_that_kills_the_last_attacker_goes()
    {
        // The line: Lifestone Recall 50 and one Drain Health Other I 10. Mana 70 pays the recall; Harm Other IV's 40 would leave 30.
        var r = CaveRig(HarmOneToFour, Unending(), mana: 70);
        r.ManaRegenPerSecond = 0;
        var tusker = AddCrimsonback(r);
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));
        scenario.ArmWayOut(ManaWayOut.LineOf(50, 10));

        for (var i = 0; i < 40; i++)
        {
            if (i % 8 == 0)
            {
                SpellHit(r);
            }

            r.Frame(scenario);
        }

        Assert.Empty(r.CastSpells);
        Assert.True(scenario.WayOutRefused);
        Assert.Equal(0, walk.Legs); // she does not walk off either: the autopilot's next frame takes her home
        Assert.Equal(1, LinesWith(r, "way out: the life cast is refused: mana 70 of "));
        Assert.Equal(1, LinesWith(r, ", 40 for it would leave it under the line=60 (Lifestone Recall 50 + Drain Health Other I 10), with an attacker on her, no press that pays and no Mana Potion in use; she leaves"));

        // The same mana, the attacker at 10 health: a landed Harm Other IV takes at least 16, the last attacker dies, and a dead attacker ends what the mana was held back from.
        var last = CaveRig(HarmOneToFour, Unending(), mana: 70);
        last.ManaRegenPerSecond = 0;
        var dying = AddCrimsonback(last);
        dying.Health = 10;
        var lastScenario = ToTheFight(last, last.Scenario(TorchWalk()));
        lastScenario.ArmWayOut(ManaWayOut.LineOf(50, 10));
        SpellHit(last);
        for (var i = 0; i < 40 && !dying.Dead; i++)
        {
            last.Frame(lastScenario);
        }

        Assert.True(dying.Dead);
        Assert.Equal([HarmFour], last.CastSpells);
        Assert.False(lastScenario.WayOutRefused);
        Assert.Equal(1, LinesWith(last, "way out: the life cast goes: mana 70 of "));
        Assert.True(tusker.Health == 110);
    }

    [Fact]
    public void M6_14_a_life_cast_the_mana_does_not_pay_waits_for_the_mana_order_and_then_goes_with_no_walk()
    {
        var r = CaveRig(HarmOneToFour, Unending(), mana: 4);
        var tusker = AddCrimsonback(r);
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 600 && !tusker.Dead; i++)
        {
            if (i % 8 == 0)
            {
                SpellHit(r);
            }

            r.Frame(scenario);
        }

        Assert.True(tusker.Dead);
        Assert.All(r.CastSpells, id => Assert.Equal(HarmFour, id));
        Assert.Equal(0, walk.Legs);
        Assert.True(scenario.ManaWaitSeconds > 0, "the mana order waited for the cast's mana");
    }

    [Fact]
    public void M6_14_a_life_cast_refused_for_mana_is_no_miss_and_the_next_waits_for_its_whole_cost()
    {
        // Mana 12 is over the try floor a Mana Conversion roll might pay Harm Other IV from (the bolt's kind of floor) and under its whole cost of 40: ACE refuses the try (0x0401, nothing charged).
        var r = CaveRig(HarmOneToFour, Unending(), mana: 12);
        r.ManaRegenPerSecond = 0;
        r.ManaPerCast = 0;
        var tusker = AddCrimsonback(r);
        r.RefusedBy[HarmFour] = 0x0401;
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 60; i++)
        {
            if (i % 8 == 0)
            {
                SpellHit(r);
            }

            r.Frame(scenario);
        }

        Assert.False(tusker.Dead);
        Assert.Equal([HarmFour], r.CastSpells); // one try, refused; no second try at a mana that cannot pay the whole cost
        Assert.Equal(1, LinesWith(r, "m6-life-through: Harm Other IV at Tusker Crimsonback 0x80003001 was refused for mana ("));
        Assert.Equal(1, LinesWith(r, "; not a miss: the next life cast waits for its whole cost"));
        Assert.Equal((1, 0, 0), (scenario.LifeThroughCasts, scenario.LifeThroughMisses, scenario.LifeThroughAsides));
        Assert.Equal(1, LinesWith(r, "which no bolt reaches from here (no clear line): Harm Other IV needs mana 40; no Drain Health Other level is known and paid for; her health "));
        Assert.True(walk.Legs >= 1, "with the mana over a bolt's and under the life cast's the walk stands as before");

        // The mana comes: the whole cost is held, the server cannot refuse it, and Harm Other goes.
        r.RefusedBy.Clear();
        r.ManaRegenPerSecond = 20;
        for (var i = 0; i < 400 && !tusker.Dead; i++)
        {
            if (i % 8 == 0)
            {
                SpellHit(r);
            }

            r.Frame(scenario);
        }

        Assert.True(tusker.Dead);
        Assert.Equal(6, r.CastSpells.Count); // the refused try and the five that killed it
        Assert.Equal((6, 5, 0, 0), (scenario.LifeThroughCasts, scenario.LifeThroughHarms, scenario.LifeThroughMisses, scenario.LifeThroughAsides));
    }

    [Fact]
    public void M6_14_the_rule_has_a_switch_and_off_the_walk_stands_as_before()
    {
        Assert.True(new FightOptions().LifeThrough);
        var off = FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-life-through=off"]));
        Assert.Empty(off.Errors);
        Assert.False(off.LifeThrough);
        Assert.Contains(" life_through=off", off.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("life_through", new FightOptions().ToString(), StringComparison.Ordinal);
        Assert.Single(FightOptions.FromArguments(CommandLineArgument.ParseAll(["--fight-life-through=maybe"])).Errors);
        Assert.Contains(OptionCatalog.Fight().Options, x => x.Name == "--fight-life-through");

        var r = CaveRig(HarmOneToFour, Unending(lifeThrough: false));
        AddCrimsonback(r);
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));
        for (var i = 0; i < 80; i++)
        {
            if (i % 8 == 0)
            {
                SpellHit(r);
            }

            r.Frame(scenario);
        }

        Assert.Empty(r.CastSpells);
        Assert.True(walk.Legs >= 1);
        Assert.Equal(0, LinesWith(r, "m6-life-through"));
    }

    [Fact]
    public void M6_14_an_attacker_past_the_life_spells_range_is_walked_to_as_before_and_the_line_says_so()
    {
        var r = CaveRig(HarmOneToFour, Unending());
        AddCrimsonback(r, Rig.LifeRange + 2); // within the bolt's 30 m, past the life spells' 20
        var walk = TorchWalk();
        var scenario = ToTheFight(r, r.Scenario(walk));

        for (var i = 0; i < 80; i++)
        {
            if (i % 8 == 0)
            {
                SpellHit(r);
            }

            r.Frame(scenario);
        }

        Assert.Empty(r.CastSpells);
        Assert.True(walk.Legs >= 1);
        Assert.Equal(1, LinesWith(r, "m6-life-through: no life cast at Tusker Crimsonback 0x80003001, 22 m off, which no bolt reaches from here (no clear line): Harm Other IV reaches 20 m; no Drain Health Other level is known and paid for; her health "));
    }

    [Fact]
    public void M6_14_an_attacker_in_a_no_go_cell_with_no_line_is_harmed_where_she_stands_in_place_of_the_step_and_the_hold()
    {
        // The den's Virindi in the no-go wing (M5 D148): nothing is walked into a no-go cell, and now nothing needs to be. M6 16 E1b: the den's list gives a Virindi Servant no health; the bestiary gives its 125 (the world
        // database's creature table), where the rule named it unknown and took 10,000. Harm Other goes either way (no drain is known here).
        var (r, scenario, virindi, walk) = HarmedInTheNoGoWing(Virindi, "Virindi", health: 125);

        Assert.True(virindi.Dead);
        Assert.Equal([HarmFour, HarmFour, HarmFour, HarmFour, HarmFour, HarmFour], r.CastSpells); // 24 five times and the last 5
        Assert.Equal(0, walk.Legs);
        Assert.Equal(0, LinesWith(r, "with no line from here; holding"));
        Assert.Equal(0, LinesWith(r, "a step to a spot"));
        Assert.Equal(6, LinesWith(r, "m6-life-through: Virindi 0x80002001, 10 m off, cannot be reached by the bolt (no clear line from here): Harm Other IV (1174) at it through the obstacle: its modelled health "));
        Assert.Equal(1, LinesWith(r, "Harm Other IV (1174) at it through the obstacle: its modelled health 125 of 125 (assumed full)"));
        Assert.Equal(1, LinesWith(r, "Harm Other IV (1174) at it through the obstacle: its modelled health 101 of 125 (pushed)"));
        Assert.Equal(0, LinesWith(r, "its maximum is not known"));
        Assert.Equal(1, LinesWith(r, "target facts: Virindi Servant (class 23): maximum health 125 (the world database's creature table), Magic Defense 171, from the bestiary"));
        Assert.Equal((1, 1), (scenario.Defended, scenario.LifeThroughKills));
    }

    [Fact]
    public void M6_16_E1b_an_attacker_of_a_class_the_bestiary_does_not_hold_is_harmed_on_the_large_target_as_before_and_named()
    {
        // The old answer stands only for a class the bestiary does not hold at all: its maximum is not known, the life casts take the large target, and the class is named once.
        var (r, scenario, thing, walk) = HarmedInTheNoGoWing(NoWeenie, "Unheard Thing", health: 60);

        Assert.True(thing.Dead);
        Assert.Equal([HarmFour, HarmFour, HarmFour], r.CastSpells); // 24, 24 and the last 12
        Assert.Equal(0, walk.Legs);
        Assert.Equal(3, LinesWith(r, "m6-life-through: Unheard Thing 0x80002001, 10 m off, cannot be reached by the bolt (no clear line from here): Harm Other IV (1174) at it through the obstacle: its modelled health 10000 (its maximum is not known, TODO(m5-drain-target-stats))"));
        Assert.Equal(1, LinesWith(r, "TODO(engine-bestiary-unknown-kind): Unheard Thing (class 999999999) is no kind of the bestiary"));
        Assert.Equal((1, 1), (scenario.Defended, scenario.LifeThroughKills));
    }

    /// <summary>A creature of <paramref name="wcid"/> in the den's no-go wing, 10 m off with no line, hits her; the fight runs until it is dead. She knows the bolts and Harm Other I to IV, and no drain.</summary>
    private static (Rig Rig, FightScenario Scenario, Creature Attacker, FakeWalk Walk) HarmedInTheNoGoWing(uint wcid, string name, int health)
    {
        var bolts = ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarBolt).SelectMany(l => l.Spells).Select(s => s.Id);
        var r = new Rig(known: [.. bolts, .. HarmOneToFour], fight: Unending(), life: true);
        var attacker = r.Add(VirindiGuid, wcid, NoGoCell, 30, name, hitsToKill: 100, health: health); // 10 m off
        var walk = new FakeWalk { BlockedCell = NoGoCell, KeepBlockedCell = true };
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r, name);
        for (var i = 0; i < 200 && !attacker.Dead; i++)
        {
            r.Frame(scenario);
        }

        return (r, scenario, attacker, walk);
    }

    [Fact]
    public void M6_14_asking_the_rule_every_frame_of_a_hold_allocates_nothing()
    {
        // An attacker in a no-go cell past the life spells' range is held as before; the hold asks the rule each frame, and an answer that does not change costs nothing.
        var bolts = ArenaProfile.Lines.Where(l => l.Role == ProfileRole.WarBolt).SelectMany(l => l.Spells).Select(s => s.Id);
        var r = new Rig(known: [.. bolts, .. DrainOneToFour, .. HarmOneToFour], fight: Unending(), life: true);
        r.Add(VirindiGuid, Virindi, NoGoCell, PlayerY + Rig.LifeRange + 5, "Virindi", hitsToKill: 100, health: 60);
        var walk = new FakeWalk { BlockedCell = NoGoCell, KeepBlockedCell = true };
        var scenario = ToTheFight(r, r.Scenario(walk));

        MeleeHit(r);
        for (var i = 0; i < 8; i++)
        {
            r.Frame(scenario);
        }

        Assert.Equal(1, LinesWith(r, "self-defence: Virindi 0x80002001 stands in the no-go cell 0x0105010D with no line from here; holding"));
        Assert.Equal(1, LinesWith(r, "m6-life-through: no life cast at Virindi 0x80002001, 25 m off, which no bolt reaches from here (no clear line): Harm Other IV reaches 20 m; Drain Health Other: "));
        Assert.Equal(0, Allocated(r, scenario, frames: 100));
        Assert.Empty(r.CastSpells);
        Assert.Equal(1, LinesWith(r, "m6-life-through: no life cast at Virindi"));
    }
}
