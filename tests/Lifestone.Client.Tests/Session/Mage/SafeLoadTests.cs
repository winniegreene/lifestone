using System.Collections.Immutable;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 safe load (the owner's rulings of 2026-11-20): up to 150% burden when she is safe, safe being inside a known dungeon on the level-up job (<see cref="ComfortRule"/>, <see cref="CycleLoad.LineAt"/>), held only by what her body cannot
/// do, the jump of a ground with jumps (<see cref="JumpLoadCap"/>); no trip home to sell before 40 minutes (<see cref="TripStint.SaleSeconds"/>, <see cref="LogisticsVerdict.IsSale"/>); the great-cantrip pieces that need a defence
/// skill to wield are not kept (<see cref="KeepAsideRule.DefenceSkillNeeded"/>). The live numbers: run 6 (2026-11-19 21:38 to 2026-11-20 17:48 UTC) and her inventory (a read-only SELECT, 2026-11-20).
/// </summary>
public sealed class SafeLoadTests
{
    private const int Capacity = 18_300;

    /// <summary>The Abode's and the Holding's pit (the profiles' <c>need_m</c>).</summary>
    private const float PitNeed = 10.75f;

    // ------------------------------------------------------------------ the jump's cap

    [Fact]
    public void Her_flight_clears_the_pit_up_to_106_percent_and_falls_to_6_metres_at_150()
    {
        // Run 100 (tools\fight-sim.ps1 her --her Qwinnie, 2026-11-20) and Jump 178 (the profiles' reading of 2026-11-13, the bot's set)
        Assert.Equal(11.43f, JumpReach.FlightMetres(100, 178, 1f), 2);
        Assert.Equal(6.15f, JumpReach.FlightMetres(100, 178, 1.5f), 2);
        Assert.Equal(106, JumpLoadCap.Percent(100, 178, PitNeed));
        Assert.Equal(107, JumpLoadCap.Percent(103, 178, PitNeed)); // the profiles' Run 103: 11.54 m
        Assert.Equal(143, JumpLoadCap.Percent(100, 178, 6.82f)); // the Holding's widest plate jump alone
        Assert.Null(JumpLoadCap.Percent(23, 98, PitNeed)); // bare (Run 23, Jump 98): 5.88 m, short even at 100%
        Assert.Equal(CycleLoadSettings.MaxTopPercent, JumpLoadCap.Percent(100, 178, 1f)); // a jump any load clears caps nothing short of the settings' most
    }

    // ------------------------------------------------------------------ safe

    private static ComfortInput Read(int jumps = 0, int? cap = null) => new("tusker-holding", GroundJumps: jumps, WayHome: "recall-home", Trial: false, Approach: false, Retreats: 0, SurvivalLeaves: 0, Deaths: 0, RacesLost: 0,
        Health: new CycleHealth(true, Lowest: 128, Max: 229, FloorAtLowest: 32, Floor: 31, Breached: false, BreachHealth: 0, BreachFloor: 0), IncomingPeak: 11, IncomingMean: 1.5, HealPerSecond: 7.39, BasePercent: 100, TopPercent: 150, Safe: true, JumpCapPercent: cap);

    [Fact]
    public void Safe_the_line_rises_whatever_her_deaths_retreats_races_and_health_and_only_the_body_holds_it()
    {
        Assert.Equal(ComfortKind.Comfortable, ComfortRule.Decide(Read()).Kind);
        Assert.Equal("safe: a known dungeon on the level-up job (the owner, 2026-11-20): the line rises to 150%, home by Lifestone Recall", ComfortRule.Decide(Read()).Why);
        Assert.Equal(ComfortKind.Comfortable, ComfortRule.Decide(Read() with { Deaths = 1, Retreats = 2, SurvivalLeaves = 1, RacesLost = 3 }).Kind);
        Assert.Equal(ComfortKind.Comfortable, ComfortRule.Decide(Read() with { Health = new CycleHealth(true, 20, 229, 31, 31, true, 20, 31) }).Kind);
        Assert.Equal(ComfortKind.Uncomfortable, ComfortRule.Decide(Read() with { Safe = false, Deaths = 1 }).Kind); // not safe: the old rule

        // the jumps: unread without her skills, held to the cap with them, the hold where the cap is the base
        Assert.Equal(ComfortKind.Unread, ComfortRule.Decide(Read(jumps: 1)).Kind);
        var held = ComfortRule.Decide(Read(jumps: 1, cap: 106));
        Assert.Equal(ComfortKind.Comfortable, held.Kind);
        Assert.Contains("held to 106% by her flight over the ground's longest jump", held.Why, StringComparison.Ordinal);
        Assert.Equal(ComfortKind.Uncomfortable, ComfortRule.Decide(Read(jumps: 1, cap: 100)).Kind);
        Assert.Equal(ComfortKind.Uncomfortable, ComfortRule.Decide(Read(jumps: 1, cap: 106) with { Safe = false }).Kind); // not safe: the pit-hold as before

        // the way home and the approach as before
        Assert.Equal(ComfortKind.Unread, ComfortRule.Decide(Read() with { WayHome = null }).Kind);
        Assert.Contains("TODO(m6-hour-load-route-jumps)", ComfortRule.Decide(Read() with { WayHome = "rung-change" }).Why, StringComparison.Ordinal);
        Assert.Equal(ComfortKind.Unread, ComfortRule.Decide(Read() with { Approach = true }).Kind);
        Assert.Equal(ComfortKind.Uncomfortable, ComfortRule.Decide(Read() with { TopPercent = 100 }).Kind);
    }

    [Fact]
    public void On_a_safe_ground_with_a_jump_the_line_rises_from_the_start_to_the_jumps_cap_and_never_past_it()
    {
        var load = new CycleLoad();
        load.Begin("tusker-holding", 0, Capacity * 107 / 100, Capacity, new CycleLoadSettings(), 150); // run 6's 02:23 start: 107% of the capacity
        load.NoteJumpCap(106);
        Assert.Equal(ComfortKind.Comfortable, load.Apply(ComfortRule.Decide(Read(jumps: 1, cap: 106))));

        Assert.Equal(106, load.LineFor(100, hasJumps: true, 0)); // the start held to the cap
        Assert.Equal(106, load.LineFor(100, hasJumps: true, 3600));
        Assert.Equal(150, load.LineFor(100, hasJumps: false, 3600)); // the same cycle on a ground without jumps: the top

        load.NoteJumpCap(null); // her skills unread at a later read: the base line, never a guess
        Assert.Equal(100, load.LineFor(100, hasJumps: true, 3600));
    }

    // ------------------------------------------------------------------ the sale's floor

    [Fact]
    public void A_trip_to_sell_waits_40_minutes_and_the_other_trips_only_the_stint()
    {
        var stint = new TripStint();
        Assert.Equal((900.0, 2400.0), (stint.Seconds, stint.SaleSeconds));
        stint.TripEnded(1000);
        Assert.True(stint.Holds(1000 + 899) && stint.HoldsSale(1000 + 899));
        Assert.False(stint.Holds(1000 + 900));
        Assert.True(stint.HoldsSale(1000 + 2399));
        Assert.False(stint.HoldsSale(1000 + 2400));
        Assert.Equal(1400, stint.SaleLeft(2000), 6);

        Assert.Equal(900, new TripStint(900, 0).SaleSeconds); // no floor: the stint alone
        Assert.Equal(0, new TripStint(0, 0).SaleSeconds);

        var full = new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.FullPack, null, 1, 0, 113, 100);
        Assert.True(full.IsSale);
        Assert.True((full with { Cause = LogisticsCause.Burden }).IsSale);
        Assert.True((full with { Cause = LogisticsCause.Slots }).IsSale);
        Assert.True((full with { Cause = LogisticsCause.Sale }).IsSale);
        Assert.True((full with { Cause = LogisticsCause.Cadence }).IsSale);
        Assert.False((full with { Cause = LogisticsCause.StockLow }).IsSale); // run 6's Vitriol and Turquoise Taper trips
        Assert.False((full with { Cause = LogisticsCause.Learn }).IsSale);
        Assert.False((full with { Cause = LogisticsCause.Foci }).IsSale);
        Assert.False((full with { Kind = LogisticsKind.Stop, Cause = LogisticsCause.StockStop }).IsSale);
    }

    // ------------------------------------------------------------------ the defence skills

    private const uint MajorQuickness = 2575, MajorLife = 2520; // ACE's spell table

    private static Appraisal Helm(uint guid, int? wieldSkill, int difficulty, params uint[] spells)
    {
        var ints = ImmutableDictionary<uint, int>.Empty;
        if (wieldSkill is { } skill)
        {
            ints = ints.Add(AppraisalIds.WieldType, 2).Add(AppraisalIds.WieldSkill, skill).Add(AppraisalIds.WieldDifficulty, difficulty); // RawSkill: the base
        }

        return new Appraisal(
            new IdentifyResponseMsg(
                guid,
                IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.SpellBook,
                true,
                ints,
                ImmutableDictionary<uint, long>.Empty,
                ImmutableDictionary<uint, bool>.Empty,
                ImmutableDictionary<uint, double>.Empty,
                ImmutableDictionary<uint, string>.Empty,
                ImmutableDictionary<uint, uint>.Empty,
                [.. spells],
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null),
            1,
            0);
    }

    private static string? Name(uint id) => id switch
    {
        MajorQuickness => "Major Quickness",
        MajorLife => "Major Life Magic Aptitude",
        _ => null,
    };

    [Theory]
    [InlineData(15, "Magic Defense 245")] // her Covenant Helm (Major Quickness)
    [InlineData(7, "Missile Defense 245")] // her Covenant Bracers (Major Life Magic Aptitude)
    [InlineData(6, "Melee Defense 245")]
    public void A_great_cantrip_piece_that_needs_a_defence_skill_is_not_kept(int skill, string needs)
    {
        const uint Armor = 0x2;
        var rule = new KeepAsideRule(KeepAsideRule.ClimberDefault);
        var helm = Helm(0x80001001, skill, 245, MajorQuickness, MajorLife);
        Assert.Equal(needs, KeepAsideRule.DefenceSkillNeeded(helm));
        Assert.Empty(rule.GreatCantrips(Armor, helm, Name));
        Assert.Null(rule.Match(Armor, helm, Name));

        var plain = Helm(0x80001002, wieldSkill: null, 0, MajorQuickness);
        Assert.Null(KeepAsideRule.DefenceSkillNeeded(plain));
        Assert.Equal(["Major Quickness"], rule.GreatCantrips(Armor, plain, Name));

        var war = Helm(0x80001003, 34, 290, MajorQuickness); // a War Magic need (her Piercing Sceptre's): not a defence, kept
        Assert.Null(KeepAsideRule.DefenceSkillNeeded(war));
        Assert.NotEmpty(rule.GreatCantrips(Armor, war, Name));
    }
}
