using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit.Abstractions;
using Server = Lifestone.Client.Tests.Session.Mage.XpSpendRuleTests.Server;
using View = Lifestone.Client.Tests.Session.Mage.XpSpendRuleTests.View;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 14, the owner's standing rule for the climber's experience as hard rules of the spend (serves D112): Max Health and Endurance about 5% of the experience earned each, no attribute over 10%, the rest to the magic skills of a named list. What these prove, on
/// <see cref="XpSpendRule"/> over the scripted ACE of <see cref="XpSpendRuleTests"/> (the synthetic table: an attribute rank 110, a vital's 73, a trained skill's 58, a specialised skill's 23): the named list (her split lists no stat off it, and a plan that does
/// list one raises none of them); the two accounts (the running share of all the experience earned, and the same percent of one allocation, with a stat's first rank always allowed and the account carried over a raise-cap end); a floor against a cap (the cap wins,
/// said once by name, and the pool does not wait) and the one exception (a load she cannot run under: the least Strength that cures it, over the cap, named); the buff bot's lines counted (its Strength line in the floor, its level VII lines in the push); the
/// summary line; and the leftover banked. <see cref="XpSpendReplayDatTests"/> replays the measured allocation of 2026-11-11 on the retail XpTable.
/// </summary>
public sealed class XpSpendAccountTests(ITestOutputHelper output)
{
    private static readonly XpTableData Table = ScriptedMage.SyntheticTable();
    private static readonly Ladder WarMage = LadderLoader.WarMage;

    private const MageStatKind A = MageStatKind.Attribute, V = MageStatKind.Vital, S = MageStatKind.Skill;

    /// <summary>The ladder's character on the scripted ACE: Strength, Endurance and Quickness created at 10, the others at 100; War Magic and Life Magic specialised; the skills of <paramref name="untrained"/> held untrained; every other skill of the D19 plan trained.</summary>
    private static Server Winnie(ActionHarness h, ulong total, long available, int level, int burden = 0, Func<StatTarget, int>? ranks = null, uint[]? untrained = null)
    {
        untrained ??= [MageTemplate.MeleeDefense];
        return new Server(
            h,
            total,
            available,
            start: t => (ranks?.Invoke(t) ?? 0, t.Kind == S && untrained.Contains(t.Id)),
            ints: new Dictionary<uint, int> { [PlayerQualities.LevelProperty] = level, [PlayerInventory.EncumbranceProperty] = burden },
            classOf: t => t.Kind == S && untrained.Contains(t.Id) ? 1u : t.Id is MageTemplate.WarMagic or MageTemplate.LifeMagic ? 3u : 2u,
            attributeStart: MageTemplate.CreationValue);
    }

    private static long Raised(Server server, MageStatKind kind, uint id) => server.Raised.Where(r => r.Kind == kind && r.Id == id).Sum(r => (long)r.Xp);

    private XpSpendRule Run(ActionHarness h, Server server, XpSpendRule rule)
    {
        var from = h.Log.Lines.Count;
        rule.Begin(new View(h));
        XpSpendRuleTests.Frames(h, server, rule.Tick);
        foreach (var line in h.Log.Lines.Skip(from).Where(l => l.Contains("xp: ", StringComparison.Ordinal) && !l.Contains("xp: raise", StringComparison.Ordinal)))
        {
            output.WriteLine(line); // the call as a watcher reads it
        }

        return rule;
    }

    // ------------------------------------------------------------------------------------------------ the named list

    [Fact]
    public void The_named_list_is_the_five_magic_skills_four_attributes_and_the_three_vitals()
    {
        Assert.Equal([MageTemplate.WarMagic, MageTemplate.LifeMagic, MageTemplate.ItemEnchantment, MageTemplate.ManaConversion, MageTemplate.CreatureEnchantment], MageTemplate.ClimberSpendSkills);
        foreach (var id in new[] { MageTemplate.Jump, MageTemplate.MagicDefense, MageTemplate.ArcaneLore, MageTemplate.Run, MageTemplate.Loyalty, MageTemplate.Salvaging, MageTemplate.MeleeDefense, MageTemplate.Alchemy })
        {
            Assert.False(MageTemplate.OnClimberList(S, id), MageTemplate.SkillName(id));
        }

        Assert.All(MageTemplate.ClimberSpendSkills, id => Assert.True(MageTemplate.OnClimberList(S, id)));
        Assert.Equal(
            [true, true, false, false, true, true],
            new[] { MageTemplate.Strength, MageTemplate.Endurance, MageTemplate.Quickness, MageTemplate.Coordination, MageTemplate.Focus, MageTemplate.Self }.Select(id => MageTemplate.OnClimberList(A, id)));
        Assert.All(new[] { MageTemplate.MaxHealth, MageTemplate.MaxStamina, MageTemplate.MaxMana }, id => Assert.True(MageTemplate.OnClimberList(V, id)));
        Assert.Same(MageShares.Climber, MageTemplate.SharesFor(22, climber: true));
        Assert.Same(MageShares.Dance, MageTemplate.SharesFor(22, climber: false)); // every other mage: the split of before
        Assert.Same(MageShares.D19, MageTemplate.SharesFor(126, climber: false));
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(50_000)]
    [InlineData(2_000_000)]
    public void Her_split_lists_no_stat_off_the_list_holds_the_health_stats_to_five_percent_and_leaves_Strength_to_its_floor(int total)
    {
        var plan = MageSpendingPlanner.Compute(Table, (ulong)total, [], MageShares.Climber, MageBuild.Traditional, strengthFloorRanks: 2, cap: WarMage.StatCap);
        Assert.All(plan.Targets, t => Assert.True(MageTemplate.OnClimberList(t.Kind, t.Id), t.Name));
        var strength = Assert.Single(plan.Targets, t => t.Kind == A && t.Id == MageTemplate.Strength); // the floor's target alone: no share
        Assert.True(strength.Floor);
        Assert.True(strength.Ranks <= 2);
        foreach (var t in plan.Targets)
        {
            var cost = t.XpList(Table)[t.Ranks];
            Assert.True(cost <= WarMage.StatCap!.Limit(t.Kind, t.Id, (ulong)total), $"{t.Name} costs {cost} of {total}");
        }

        Assert.True(plan.Spent <= plan.TotalXp);
    }

    [Fact]
    public void The_limit_is_five_percent_for_Max_Health_and_Endurance_ten_for_every_other_attribute_and_none_for_the_rest()
    {
        var cap = WarMage.StatCap!;
        Assert.Equal((5, 5, 10, 10, 10, 10, 10), (cap.LimitPercent(V, MageTemplate.MaxHealth), cap.LimitPercent(A, MageTemplate.Endurance), cap.LimitPercent(A, MageTemplate.Strength), cap.LimitPercent(A, MageTemplate.Focus), cap.LimitPercent(A, MageTemplate.Self), cap.LimitPercent(A, MageTemplate.Coordination), cap.LimitPercent(A, MageTemplate.Quickness)));
        Assert.Equal((100, 100, 100), (cap.LimitPercent(V, MageTemplate.MaxMana), cap.LimitPercent(V, MageTemplate.MaxStamina), cap.LimitPercent(S, MageTemplate.WarMagic)));
        Assert.Equal((5_000ul, 5_000ul, 10_000ul, ulong.MaxValue), (cap.Limit(V, MageTemplate.MaxHealth, 100_000), cap.Limit(A, MageTemplate.Endurance, 100_000), cap.Limit(A, MageTemplate.Focus, 100_000), cap.Limit(S, MageTemplate.WarMagic, 100_000)));
        Assert.Equal((277ul, 50ul), (cap.Limit(A, MageTemplate.Strength, 1_000), cap.Limit(A, MageTemplate.Endurance, 1_000))); // the grace is the 10% line's alone: the health stats' limit is the floor's aim, which never had one
    }

    [Fact]
    public void A_plan_that_lists_Jump_and_Magic_Defense_raises_neither_for_the_ladders_character_and_names_each_once()
    {
        // The ladder binds her but the plan is the D19 split's (followLevel off): it lists Coordination, Quickness, Melee Defense and the six skills the heritage trains. The spend raises none of them, whatever the plan says.
        var h = new ActionHarness();
        h.Present();
        var server = Winnie(h, total: 60_000, available: 60_000, level: 10, untrained: []);
        var rule = Run(h, server, new XpSpendRule(Table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, healthLadder: WarMage));

        Assert.Null(rule.Failure);
        Assert.Contains(rule.Plan!.Targets, t => t.Kind == S && t.Id == MageTemplate.Jump && t.Ranks > 0);
        Assert.NotEmpty(server.Raised);
        Assert.All(server.Raised, r => Assert.True(MageTemplate.OnClimberList(r.Kind, r.Id), $"{r.Kind} {r.Id}"));
        Assert.Equal(9, rule.OffListed); // Coordination, Quickness, Melee Defense, Arcane Lore, Magic Defense, Jump, Run, Loyalty, Salvaging
        foreach (var name in new[] { "Jump", "Magic Defense", "Arcane Lore", "Run", "Coordination", "Quickness" })
        {
            Assert.Equal(1, h.Log.Lines.Count(l => l.Contains($"xp: not on the list: {name}: ", StringComparison.Ordinal)));
        }

        Assert.True(rule.Banked > 58, "what the listed stats left is banked, though it covers a rank of Jump (58)");
        Assert.Equal(rule.Banked, h.State.Self.Qualities.AvailableExperience);
        Assert.Contains($"banked {rule.Banked}: ", rule.SharesNote, StringComparison.Ordinal);

        // The caps are the spend's own too: the D19 plan gives Focus and Self 30% together, and the account stops Focus at the 10% line (54 ranks: 5,940 of 60,000), by name.
        Assert.True(rule.Plan.Targets.Single(t => t.Kind == A && t.Id == MageTemplate.Focus).Ranks > 54);
        Assert.Equal(5_940, Raised(server, A, MageTemplate.Focus)); // on the synthetic table's flat prices the share's cheapest next rank is always Focus's
        Assert.Contains("Focus", rule.Held);
        Assert.Contains(h.Log.Lines, l => l.Contains("xp: held: Focus 54 -> 55: rank 55 would bring its cost to 6,050, over its 10% of the 60,000 earned (6,000): the cap wins", StringComparison.Ordinal));

        // A mage the ladder does not bind spends by the plan as before: the same plan raises Jump.
        var h2 = new ActionHarness();
        h2.Present();
        var other = Winnie(h2, total: 60_000, available: 60_000, level: 10, untrained: []);
        var plain = Run(h2, other, new XpSpendRule(Table, h2.R.Log, maxRaises: 100_000, maxSeconds: 100_000));
        Assert.True(Raised(other, S, MageTemplate.Jump) > 0);
        Assert.Equal(0, plain.OffListed);
        Assert.Contains("caps=none", plain.SharesNote, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------ the two accounts

    /// <summary>10,000 earned, 4,013 of it unassigned: War Magic and Life Magic hold 100 ranks each (2,300 each) and Max Mana 19 (1,387). Level 10: the outskirts' health floor wants 72.</summary>
    private static Server Behind(ActionHarness h) => Winnie(h, total: 10_000, available: 4_013, level: 10, ranks: t => (t.Kind, t.Id) switch { (S, MageTemplate.WarMagic) or (S, MageTemplate.LifeMagic) => 100, (V, MageTemplate.MaxMana) => 19, _ => 0 });

    [Fact]
    public void One_allocation_gives_a_capped_stat_its_percent_of_the_allocation_and_no_more_though_the_running_share_has_room()
    {
        var h = new ActionHarness();
        h.Present();
        var server = Behind(h);
        var rule = Run(h, server, new XpSpendRule(Table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, healthLadder: WarMage));

        // The running shares have room for more (5% of 10,000 is 500: six Max Health ranks, three of Endurance on its odd values; 10% is 1,000: nine ranks of Focus), and the plan asks for it. The allocation's account gives each its percent of 4,013: 200 for a health stat, 401 for Focus and Self.
        Assert.Null(rule.Failure);
        Assert.Equal(6, rule.Plan!.Targets.First(t => t.Floor && t.Kind == V && t.Id == MageTemplate.MaxHealth).Ranks);
        Assert.Equal(3, rule.Plan.Targets.First(t => t.Floor && t.Kind == A && t.Id == MageTemplate.Endurance).Ranks);
        Assert.True(rule.Plan.Targets.Where(t => t.Kind == A && t.Id == MageTemplate.Focus).Max(t => t.Ranks) > 3);
        Assert.Equal((146, 110, 330, 330), (Raised(server, V, MageTemplate.MaxHealth), Raised(server, A, MageTemplate.Endurance), Raised(server, A, MageTemplate.Focus), Raised(server, A, MageTemplate.Self))); // 2 ranks, 1 (its first rank is always allowed: 110 of 200), 3 and 3
        Assert.Equal(["Max Health", "Endurance", "Focus", "Self"], rule.Held);
        Assert.Contains(h.Log.Lines, l => l.Contains("xp: held: Max Health 2 -> 3 (a floor's rank): it has taken 146 of this allocation's 4,013, and rank 3 (73) would pass its 5% of the allocation (200): the cap wins; it waits for the next allocation and the stats after it are tried", StringComparison.Ordinal));
        Assert.Contains(h.Log.Lines, l => l.Contains("xp: held: Focus 3 -> 4: it has taken 330 of this allocation's 4,013, and rank 4 (110) would pass its 10% of the allocation (401)", StringComparison.Ordinal));
        Assert.True(Raised(server, S, MageTemplate.WarMagic) > 0, "a floor the account holds does not make the pool wait: the shares after it are bought");
        Assert.Equal(["cap: health: the floor wants base health 142 on the rung burrow and the cap allows 13 (129 short): Max Health and Endurance are each held to 5% of the experience earned (500 of 10,000); the cap wins (the owner's rule, D112): what the floor cannot buy goes to her magic"], rule.CapConflicts);
        Assert.Contains("| Endurance 110 2.7% run 1.1%/5% |", rule.SharesNote, StringComparison.Ordinal);
        Assert.Contains("| Max Health 146 3.6% run 1.5%/5% |", rule.SharesNote, StringComparison.Ordinal);
        Assert.Contains("| Focus 330 8.2% run 3.3%/10% |", rule.SharesNote, StringComparison.Ordinal);
    }

    [Fact]
    public void A_call_that_follows_a_raise_cap_end_goes_on_with_the_same_allocation_and_its_account()
    {
        var whole = new ActionHarness();
        whole.Present();
        var wholeServer = Behind(whole);
        Run(whole, wholeServer, new XpSpendRule(Table, whole.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, healthLadder: WarMage));

        var h = new ActionHarness();
        h.Present();
        var server = Behind(h);
        var rule = new XpSpendRule(Table, h.R.Log, maxRaises: 4, maxSeconds: 100_000, followLevel: true, healthLadder: WarMage);
        var calls = 0;
        do
        {
            Run(h, server, rule);
            calls++;
            Assert.Contains("shares: allocation=4013 ", rule.SharesNote, StringComparison.Ordinal); // the allocation is what the first call found, in every call's line
        }
        while (rule.End == XpSpendEnd.RaiseCap && calls < 200);

        Assert.True(calls > 3, "the cap of four raises a call split the allocation over several calls");
        Assert.Equal(wholeServer.Raised.Select(r => (r.Kind, r.Id, r.Xp)), server.Raised.Select(r => (r.Kind, r.Id, r.Xp))); // raise for raise the one call's: no call took a stat's share a second time
        Assert.Equal(146, Raised(server, V, MageTemplate.MaxHealth));
        Assert.Contains($"spent={server.Raised.Sum(r => (long)r.Xp)} ", rule.SharesNote, StringComparison.Ordinal); // the last line's sum is the allocation's, not the call's
    }

    [Fact]
    public void A_stat_already_over_its_running_share_is_not_raised_and_the_line_says_over()
    {
        // She was built before the cap: Endurance holds 11 ranks (1,210, 12.1% of 10,000). No allocation raises it until the experience earned has grown past it.
        var h = new ActionHarness();
        h.Present();
        var server = Winnie(h, total: 10_000, available: 4_000, level: 10, ranks: t => (t.Kind, t.Id) switch { (A, MageTemplate.Endurance) => 11, (S, MageTemplate.WarMagic) => 208, _ => 0 });
        var rule = Run(h, server, new XpSpendRule(Table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, healthLadder: WarMage));
        Assert.Equal(0, Raised(server, A, MageTemplate.Endurance));
        Assert.Equal(
            "shares: allocation=4000 spent=2724 banked=1276 earned=10000 caps=health<=5%,attribute<=10% | Strength 0 0.0% run 0.0%/10% | Endurance 0 0.0% run 12.1%/5% over | Coordination 0 0.0% run 0.0%/10% | Quickness 0 0.0% run 0.0%/10% | Focus 330 8.3% run 3.3%/10% | "
            + "Self 330 8.3% run 3.3%/10% | Max Health 146 3.7% run 1.5%/5% | Max Stamina 365 9.1% run 3.7% | Max Mana 365 9.1% run 3.7% | War Magic 414 10.4% run 52.0% | Life Magic 368 9.2% run 3.7% | Item Enchantment 0 0.0% run 0.0% | Mana Conversion 406 10.2% run 4.1% | "
            + "Creature Enchantment 0 0.0% run 0.0% | other skills 0 0.0% run 0.0% | groups: attributes 660 16.5% run 18.7%; listed skills 1188 29.7% run 59.7% | banked 1276: every rank the caps allow is bought: held to the next allocation: Max Health, Focus, Self",
            rule.SharesNote); // the line's whole form, pinned once
        Assert.Contains(h.Log.Lines, l => l.EndsWith("xp: " + rule.SharesNote, StringComparison.Ordinal));
        Assert.Equal((XpSpendEnd.PlanReached, 1_276L), (rule.End, rule.Banked)); // banked, not spent on a rank off the plan: the pool covers 22 ranks of Jump
        Assert.Equal(rule.Banked, h.State.Self.Qualities.AvailableExperience);

        // The next allocation is what the first banked: each held stat takes its percent of it, or its one rank (a Max Health rank is 73 of 1,276's 5%, 63; a Focus rank 110 of its 10%, 127).
        var before = server.Raised.Count;
        Run(h, server, rule);
        Assert.StartsWith("shares: allocation=1276 ", rule.SharesNote, StringComparison.Ordinal);
        var second = server.Raised.Skip(before).ToList();
        long Second(MageStatKind kind, uint id) => second.Where(r => r.Kind == kind && r.Id == id).Sum(r => (long)r.Xp);
        Assert.Equal((73, 110, 110, 0), (Second(V, MageTemplate.MaxHealth), Second(A, MageTemplate.Focus), Second(A, MageTemplate.Self), Second(A, MageTemplate.Endurance)));
        Assert.Equal(1, h.Log.Lines.Count(l => l.Contains("xp: cap: health:", StringComparison.Ordinal))); // a floor against its cap is said once by a rule, however many calls it makes
    }

    // ------------------------------------------------------------------------------------------------ a floor against a cap

    [Fact]
    public void A_Strength_floor_that_wants_more_than_the_cap_allows_is_cut_to_the_cap_and_said_once_by_name()
    {
        // Level 4 (the Academy: no health floor). 2,700 on Strength 10 is 180%: slower, and she still runs. The floor wants 18 (8 ranks, 880); 10% of the 6,000 earned is 600: 5 ranks.
        var h = new ActionHarness();
        h.Present();
        var server = Winnie(h, total: 6_000, available: 6_000, level: 4, burden: 2_700);
        var rule = Run(h, server, new XpSpendRule(Table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, burdenTripPercent: 150, healthLadder: WarMage));

        Assert.Null(rule.Failure);
        Assert.Equal(5u, server.RanksOf(A, MageTemplate.Strength));
        Assert.Equal(
            ["cap: Strength: the floor wants base Strength 18 (8 ranks over the start, 880 in all) and the cap allows 5 ranks: no attribute over 10% of the experience earned (600 of 6,000); the cap wins (the owner's rule, D112): she carries 2700 on capacity 1500 (180%), slower above 100%, until a trip lightens her"],
            rule.CapConflicts);
        Assert.True(Raised(server, S, MageTemplate.WarMagic) > 0, "the shares are bought: the floor's uncovered ranks do not hold the pool");
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("xp: cure:", StringComparison.Ordinal));
        Run(h, server, rule);
        Assert.Equal(1, h.Log.Lines.Count(l => l.Contains("xp: cap: Strength:", StringComparison.Ordinal))); // once, however many calls
        Assert.Equal(5u, server.RanksOf(A, MageTemplate.Strength));
    }

    [Fact]
    public void A_floor_the_allocations_account_holds_does_not_make_the_pool_wait()
    {
        // 20,000 earned, 2,000 unassigned: the floor's 8 ranks (880) are inside the 10% line (2,000), but 10% of this allocation is 200: one rank now, the next at the next allocation.
        var h = new ActionHarness();
        h.Present();
        var server = Winnie(h, total: 20_000, available: 2_000, level: 4, burden: 2_700, ranks: t => (t.Kind, t.Id) switch { (S, MageTemplate.WarMagic) or (S, MageTemplate.LifeMagic) => 226, (S, MageTemplate.Jump) => 100, _ => 0 });
        var rule = Run(h, server, new XpSpendRule(Table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, burdenTripPercent: 150, healthLadder: WarMage));
        Assert.Equal(8, rule.Plan!.Targets.Single(t => t.Floor).Ranks);
        Assert.Equal(1u, server.RanksOf(A, MageTemplate.Strength));
        Assert.Empty(rule.CapConflicts); // the running cap allows the whole floor: nothing to say but the hold
        Assert.Contains(h.Log.Lines, l => l.Contains("xp: held: Strength 1 -> 2 (a floor's rank): it has taken 110 of this allocation's 2,000, and rank 2 (110) would pass its 10% of the allocation (200)", StringComparison.Ordinal));
        Assert.True(Raised(server, S, MageTemplate.CreatureEnchantment) > 0);
        Assert.Equal(0, Raised(server, S, MageTemplate.Jump));
    }

    [Fact]
    public void A_load_she_cannot_run_under_buys_the_least_Strength_that_cures_it_over_the_cap_and_names_it()
    {
        Assert.Equal(200, StrengthFloor.RunLinePercent);
        Assert.Equal((false, true, true), (StrengthFloor.CannotRun(2_999, 1_500), StrengthFloor.CannotRun(3_000, 1_500), StrengthFloor.CannotRun(8_684, 1_500))); // the load modifier is 0 from a load of 2
        Assert.Equal(0.0, Lifestone.Data.Motion.EncumbranceSystem.LoadMod(2f));
        Assert.Equal((11, 29, 1), (StrengthFloor.CureWant(3_000, 0), StrengthFloor.CureWant(8_684, 0), StrengthFloor.CureWant(0, 0))); // 11 x 150 = 1,650: 3,000 is 182%; 29 x 150 = 4,350: 8,684 is 199.6%

        // The planner: the cure's ranks stand whatever the cap says, the rest of the floor is cut to it.
        var capped = MageSpendingPlanner.Compute(Table, 6_000, [], MageShares.Climber, MageBuild.Traditional, strengthFloorRanks: 48, cap: WarMage.StatCap);
        var cured = MageSpendingPlanner.Compute(Table, 6_000, [], MageShares.Climber, MageBuild.Traditional, strengthFloorRanks: 48, cap: WarMage.StatCap, strengthCureRanks: 19);
        Assert.Equal((5, 19), (capped.Targets.Single(t => t.Floor).Ranks, cured.Targets.Single(t => t.Floor).Ranks));
        Assert.Equal(19, MageSpendingPlanner.Compute(Table, 6_000, [], MageShares.Climber, MageBuild.Traditional, strengthFloorRanks: 19, cap: WarMage.StatCap, strengthCureRanks: 60).Targets.Single(t => t.Floor).Ranks); // never past the floor's own want

        // The armour set on base Strength 10 with no buff bot: 8,684 on 1,500 is 579%.
        var h = new ActionHarness();
        h.Present();
        var server = Winnie(h, total: 6_000, available: 6_000, level: 4, burden: 8_684);
        var rule = Run(h, server, new XpSpendRule(Table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, burdenTripPercent: 150, healthLadder: WarMage, standingBot: () => false));
        Assert.Null(rule.Failure);
        Assert.Equal(19u, server.RanksOf(A, MageTemplate.Strength)); // base 29: 4,350 carries 8,684 at 199.6%
        Assert.All(server.Raised.Take(19), r => Assert.Equal((A, MageTemplate.Strength), (r.Kind, r.Id)));
        Assert.Contains(h.Log.Lines, l => l.Contains("xp: cure: Strength: burden 8684 is 579% of capacity 1500, at or over the run line 200%: she cannot run; base Strength 10 -> 29 (19 ranks over the start) is bought whatever the caps say, the least that brings her under the line; the rest of the floor (to 58) stays under the cap", StringComparison.Ordinal));
        Assert.Equal(5, h.Log.Lines.Count(l => l.Contains("xp: raise Strength", StringComparison.Ordinal) && l.Contains("(the Strength floor, before the shares)", StringComparison.Ordinal))); // the five ranks the cap allows
        Assert.Equal(14, h.Log.Lines.Count(l => l.Contains("xp: raise Strength", StringComparison.Ordinal) && l.Contains("(the Strength floor's run-line cure, over the cap: she cannot run under this load)", StringComparison.Ordinal)));
        Assert.Contains("the floor wants base Strength 58 (48 ranks over the start, 5,280 in all) and the cap allows 19 ranks", Assert.Single(rule.CapConflicts), StringComparison.Ordinal);
        Assert.Contains("| Strength 2090 34.8% run 34.8%/10% over |", rule.SharesNote, StringComparison.Ordinal);
        Assert.True(Raised(server, S, MageTemplate.WarMagic) > 0);

        // Cured: the next call finds 199.6%, names no cure and buys no Strength (the cap's five ranks are long passed).
        var cures = h.Log.Lines.Count(l => l.Contains("xp: cure:", StringComparison.Ordinal));
        Run(h, server, rule);
        Assert.Equal(cures, h.Log.Lines.Count(l => l.Contains("xp: cure:", StringComparison.Ordinal)));
        Assert.Equal(19u, server.RanksOf(A, MageTemplate.Strength));
    }

    // ------------------------------------------------------------------------------------------------ the buff bot's lines

    [Fact]
    public void The_buff_bots_lines_are_counted_its_Strength_in_the_floor_and_its_level_VII_lines_in_the_push()
    {
        // Its Strength line: the same 8,684 is 116% of the capacity Strength 50 gives. She runs: no cure, and the floor's want is 18, cut to the cap's five ranks.
        var h = new ActionHarness();
        h.Present();
        var server = Winnie(h, total: 6_000, available: 6_000, level: 4, burden: 8_684);
        var rule = Run(h, server, new XpSpendRule(Table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, burdenTripPercent: 150, healthLadder: WarMage, standingBot: () => true));
        Assert.Contains("standing +40 (the worn pieces' own lasting Strength line +0, the buff bot's line +40), 116% now", rule.StrengthNote, StringComparison.Ordinal);
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("xp: cure:", StringComparison.Ordinal));
        Assert.Equal(5u, server.RanksOf(A, MageTemplate.Strength));
        Assert.Contains("the floor wants base Strength 18 (8 ranks over the start, 880 in all) and the cap allows 5 ranks", Assert.Single(rule.CapConflicts), StringComparison.Ordinal);

        // Its level VII lines in the bootstrap's place: Item Enchantment is pushed (to 6 ranks: Impenetrability III under the bar) and is the first skill raised; Creature Enchantment, untrained, is never raised.
        var bot = new ActionHarness();
        bot.Present();
        var botServer = Winnie(bot, total: 6_000, available: 6_000, level: 4, untrained: [MageTemplate.MeleeDefense, MageTemplate.CreatureEnchantment, MageTemplate.ManaConversion]);
        BuffBotCover.For(bot.State).Set(true, "the test's bot");
        Run(bot, botServer, new XpSpendRule(Table, bot.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, healthLadder: WarMage));
        Assert.Contains(bot.Log.Lines, l => l.Contains("xp: push: Item Enchantment base 56 brings level III under the bar 0.75 with the buff bot's level VII lines up (ranks 6)", StringComparison.Ordinal));
        Assert.Equal(Enumerable.Repeat((S, MageTemplate.ItemEnchantment), 6), botServer.Raised.Take(6).Select(r => (r.Kind, r.Id)));
        Assert.Equal(0, Raised(botServer, S, MageTemplate.CreatureEnchantment));
        Assert.Equal(0, Raised(botServer, S, MageTemplate.ManaConversion));

        // Without the cover the push asks nothing of a book with no bootstrap, and War Magic is the first skill raised.
        var own = new ActionHarness();
        own.Present();
        var ownServer = Winnie(own, total: 6_000, available: 6_000, level: 4, untrained: [MageTemplate.MeleeDefense, MageTemplate.CreatureEnchantment, MageTemplate.ManaConversion]);
        Run(own, ownServer, new XpSpendRule(Table, own.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, healthLadder: WarMage));
        Assert.DoesNotContain(own.Log.Lines, l => l.Contains("xp: push:", StringComparison.Ordinal));
        Assert.Equal((S, MageTemplate.WarMagic), (ownServer.Raised[0].Kind, ownServer.Raised[0].Id));
    }

    [Fact]
    public void Creature_Enchantment_and_Mana_Conversion_take_experience_from_the_spend_that_finds_them_trained()
    {
        (long Creature, long Conversion) Spend(uint[] untrained)
        {
            // 60,000: on the synthetic table's flat prices the magic skills' share fills War Magic and Life Magic to their rank caps first, then the trained skills.
            var h = new ActionHarness();
            h.Present();
            var server = Winnie(h, total: 60_000, available: 60_000, level: 4, untrained: untrained);
            Run(h, server, new XpSpendRule(Table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, healthLadder: WarMage));
            return (Raised(server, S, MageTemplate.CreatureEnchantment), Raised(server, S, MageTemplate.ManaConversion));
        }

        Assert.Equal((0, 0), Spend([MageTemplate.MeleeDefense, MageTemplate.CreatureEnchantment, MageTemplate.ManaConversion]));
        var (creature, conversion) = Spend([MageTemplate.MeleeDefense]);
        Assert.True(creature > 0 && conversion > 0, $"Creature Enchantment {creature}, Mana Conversion {conversion}");
    }
}

/// <summary>
/// M6 item 14: the allocation of 2026-11-11 16:19 UTC, replayed on the retail XpTable. The character as the run's log read her just before it (level 22; 171,369 unassigned of 1,835,455 earned; burden 9,417 on base Strength 62; no buff bot): the old rules spent
/// 164,873 of it with Focus, Self and Endurance 19,919 each (12.08%), Strength 16,565 (10.05%), Max Health 13,999 (8.49%), Jump 12,580 in 18 ranks and Magic Defense 10,814 in 14 (14.19% together), and the five skills War Magic, Life Magic, Creature Enchantment,
/// Item Enchantment and Arcane Lore 43,662 (26.48%). The new split is pinned here.
/// </summary>
public sealed class XpSpendReplayDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private const MageStatKind A = MageStatKind.Attribute, V = MageStatKind.Vital, S = MageStatKind.Skill;

    /// <summary>The unassigned experience the allocation found.</summary>
    private const long Pool = 171_369;

    /// <summary>The ranks before the allocation, from the run's raise lines (each stat's last raise before 16:19:11 UTC; Loyalty and Salvaging at the creation's 5).</summary>
    private static int RanksBefore(StatTarget t) => (t.Kind, t.Id) switch
    {
        (A, MageTemplate.Strength) => 52,
        (A, MageTemplate.Endurance) => 55,
        (A, MageTemplate.Coordination) => 20,
        (A, MageTemplate.Quickness) => 20,
        (A, MageTemplate.Focus) => 55,
        (A, MageTemplate.Self) => 55,
        (V, MageTemplate.MaxHealth) => 56,
        (V, MageTemplate.MaxStamina) => 45,
        (V, MageTemplate.MaxMana) => 64,
        (S, MageTemplate.WarMagic) => 83,
        (S, MageTemplate.LifeMagic) => 83,
        (S, MageTemplate.CreatureEnchantment) => 70,
        (S, MageTemplate.ItemEnchantment) => 70,
        (S, MageTemplate.ArcaneLore) => 30,
        (S, MageTemplate.Run) => 30,
        (S, MageTemplate.MagicDefense) => 17,
        (S, MageTemplate.Jump) => 13,
        (S, MageTemplate.Loyalty) => 5,
        (S, MageTemplate.Salvaging) => 5,
        _ => 0,
    };

    private static readonly uint[] Untrained = [MageTemplate.ManaConversion, MageTemplate.MeleeDefense];

    private (ActionHarness H, Server Server, XpSpendRule Rule) Replay(XpTableData table, Func<bool>? standingBot, bool cover = false)
    {
        var h = new ActionHarness();
        h.Present();
        if (cover)
        {
            BuffBotCover.For(h.State).Set(true, "the replay's buff bot");
        }

        var probe = MageSpendingPlanner.Compute(table, 1);
        ulong spent = 0;
        foreach (var t in probe.Targets)
        {
            var cls = t.Kind != S ? 0 : t.Id is MageTemplate.WarMagic or MageTemplate.LifeMagic ? 3 : 2;
            var list = t.Kind == A ? table.AttributeXpList : t.Kind == V ? table.VitalXpList : cls == 3 ? table.SpecializedSkillXpList : table.TrainedSkillXpList;
            spent += Untrained.Contains(t.Id) && t.Kind == S ? 0 : list[RanksBefore(t)];
        }

        var total = spent + (ulong)Pool;
        Assert.Equal(1_835_455ul, total); // between level 22 (1,593,459) and level 23 (1,911,400), as the log's "level 22"
        var server = new Server(
            h,
            total,
            Pool,
            start: t => (t.Kind == S && Untrained.Contains(t.Id) ? 0 : RanksBefore(t), t.Kind == S && Untrained.Contains(t.Id)),
            ints: new Dictionary<uint, int> { [PlayerQualities.LevelProperty] = 22, [PlayerInventory.EncumbranceProperty] = 9_417 },
            table: table,
            classOf: t => t.Kind == S && Untrained.Contains(t.Id) ? 1u : t.Id is MageTemplate.WarMagic or MageTemplate.LifeMagic ? 3u : 2u,
            attributeStart: MageTemplate.CreationValue);
        var rule = new XpSpendRule(table, h.R.Log, maxRaises: 100_000, maxSeconds: 100_000, followLevel: true, burdenTripPercent: 150, healthLadder: LadderLoader.WarMage, standingBot: standingBot);
        rule.Begin(new View(h));
        XpSpendRuleTests.Frames(h, server, rule.Tick);
        return (h, server, rule);
    }

    /// <summary>The allocation as the old rules spent it (the run's 51 raise lines, summed by stat): 164,873.</summary>
    private static readonly (MageStatKind Kind, uint Id, string Name, int Ranks, long Xp)[] Measured =
    [
        (A, MageTemplate.Strength, "Strength", 2, 16_565),
        (A, MageTemplate.Endurance, "Endurance", 2, 19_919),
        (A, MageTemplate.Coordination, "Coordination", 1, 1_444),
        (A, MageTemplate.Quickness, "Quickness", 1, 1_444),
        (A, MageTemplate.Focus, "Focus", 2, 19_919),
        (A, MageTemplate.Self, "Self", 2, 19_919),
        (V, MageTemplate.MaxHealth, "Max Health", 2, 13_999),
        (V, MageTemplate.MaxStamina, "Max Stamina", 1, 3_541),
        (V, MageTemplate.MaxMana, "Max Mana", 0, 0),
        (S, MageTemplate.WarMagic, "War Magic", 1, 10_525),
        (S, MageTemplate.LifeMagic, "Life Magic", 1, 10_683),
        (S, MageTemplate.CreatureEnchantment, "Creature Enchantment", 1, 10_738),
        (S, MageTemplate.ItemEnchantment, "Item Enchantment", 1, 10_649),
        (S, MageTemplate.ArcaneLore, "Arcane Lore", 1, 1_067),
        (S, MageTemplate.Run, "Run", 1, 1_067),
        (S, MageTemplate.MagicDefense, "Magic Defense", 14, 10_814),
        (S, MageTemplate.Jump, "Jump", 18, 12_580),
    ];

    private void Table(Server server, string title)
    {
        var c = CultureInfo.InvariantCulture;
        var (was, spent) = (Measured.Sum(m => m.Xp), server.Raised.Sum(r => (long)r.Xp));
        output.WriteLine(string.Create(c, $"{title} percent of the experience each spent ({was:N0} before, {spent:N0} after)"));
        foreach (var m in Measured)
        {
            var now = server.Raised.Where(r => r.Kind == m.Kind && r.Id == m.Id).ToList();
            var xp = now.Sum(r => (long)r.Xp);
            output.WriteLine(string.Create(c, $"  {m.Name,-21} before {m.Ranks,2} ranks {m.Xp,7:N0} {100.0 * m.Xp / was,5:0.00}%   after {now.Count,2} ranks {xp,7:N0} {100.0 * xp / spent,5:0.00}%"));
        }
    }

    private static long Raised(Server server, MageStatKind kind, uint id) => server.Raised.Where(r => r.Kind == kind && r.Id == id).Sum(r => (long)r.Xp);

    [RequiresDatsFact]
    public void The_allocation_of_2026_11_11_replayed_goes_to_her_magic_and_nothing_to_Jump_or_Magic_Defense()
    {
        var table = XpTableData.Load(fixture.Dats);
        Assert.Equal(164_873, Measured.Sum(m => m.Xp)); // the run's "spent_xp=164873"
        Assert.Equal(48.04, Math.Round(100.0 * Measured.Where(m => m.Kind == A).Sum(m => m.Xp) / 164_873, 2)); // the attributes took 48% of it
        var (h, server, rule) = Replay(table, standingBot: null);
        Table(server, "the allocation of 2026-11-11 16:19 UTC (171,369 unassigned at level 22), as measured and replayed (no buff bot):");
        output.WriteLine(rule.SharesNote);

        Assert.Null(rule.Failure);
        // Nothing off the list, and nothing to a stat already over its share of the 1,835,455 earned (Endurance 8.6% and Max Health 6.0% against 5%); Focus and Self hold more than the plan asks at this total (8.6% each).
        Assert.All(server.Raised, r => Assert.True(MageTemplate.OnClimberList(r.Kind, r.Id), $"{r.Kind} {r.Id}"));
        Assert.Equal((0, 0, 0, 0, 0, 0), (Raised(server, S, MageTemplate.Jump), Raised(server, S, MageTemplate.MagicDefense), Raised(server, S, MageTemplate.ArcaneLore), Raised(server, S, MageTemplate.Run), Raised(server, A, MageTemplate.Coordination), Raised(server, A, MageTemplate.Quickness)));
        Assert.Equal((0, 0, 0, 0), (Raised(server, A, MageTemplate.Endurance), Raised(server, V, MageTemplate.MaxHealth), Raised(server, A, MageTemplate.Focus), Raised(server, A, MageTemplate.Self)));
        // The Strength floor's one rank (9,417 on 9,300 is 101%): 4.69% of the allocation, 7.6% of the experience earned with it.
        Assert.Equal(8_032, Raised(server, A, MageTemplate.Strength));
        // The magic skills: 156,085 of the 171,369 (91.1%), against the 42,595 (24.9%) the old rules gave the same four.
        Assert.Equal((48_403, 48_403, 48_391, 10_888), (Raised(server, S, MageTemplate.WarMagic), Raised(server, S, MageTemplate.LifeMagic), Raised(server, S, MageTemplate.CreatureEnchantment), Raised(server, S, MageTemplate.ItemEnchantment)));
        Assert.Equal(3_541, Raised(server, V, MageTemplate.MaxStamina));
        Assert.Equal((167_658L, 3_711L), (rule.SpentXp, rule.Banked));
        Assert.Contains("| Endurance 0 0.0% run 8.6%/5% over |", rule.SharesNote, StringComparison.Ordinal);
        Assert.Contains("| Max Health 0 0.0% run 6.0%/5% over |", rule.SharesNote, StringComparison.Ordinal);
        Assert.Contains("| Strength 8032 4.7% run 7.6%/10% |", rule.SharesNote, StringComparison.Ordinal);
        Assert.Contains("| other skills (Arcane Lore, Magic Defense, Jump, Run, Loyalty, Salvaging) 0 0.0% run 2.0% |", rule.SharesNote, StringComparison.Ordinal);
        Assert.Contains("groups: attributes 8032 4.7% run 34.8%; listed skills 156085 91.1% run 43.9%", rule.SharesNote, StringComparison.Ordinal);
        Assert.EndsWith("banked 3711: no rule's next rank fits: the cheapest is Max Stamina 46 -> 47 for 3,743", rule.SharesNote, StringComparison.Ordinal);
        Assert.Contains("cap: health: the floor wants base health 390 on the rung cave and the cap allows 89 (301 short)", Assert.Single(rule.CapConflicts), StringComparison.Ordinal);
        Assert.Empty(rule.Held);
        Assert.Equal(0, rule.OffListed); // her split lists none: the list's check found nothing to stop
    }

    [RequiresDatsFact]
    public void On_the_retail_table_her_plan_at_every_level_lists_only_the_named_stats_and_holds_every_cap()
    {
        var table = XpTableData.Load(fixture.Dats);
        var ladder = LadderLoader.WarMage;
        var cap = ladder.StatCap!;
        var c = CultureInfo.InvariantCulture;
        for (var level = 2; level <= ladder.TopLevel; level++)
        {
            var total = table.XpForLevel(level);
            var plan = MageSpendingPlanner.Compute(table, total, [], MageTemplate.SharesFor(level, climber: true), MageBuild.Traditional, strengthFloorRanks: 13, ClimberHealthRule.ProjectedBuy(table, ladder, level), cap: cap); // 13 ranks: the armoured climber's floor with the bot's line (2026-11-12, level 9)
            ulong magic = 0;
            foreach (var g in plan.Targets.GroupBy(t => (t.Kind, t.Id)))
            {
                var t = g.MaxBy(x => x.Ranks)!;
                var cost = t.XpList(table)[t.Ranks];
                Assert.True(MageTemplate.OnClimberList(t.Kind, t.Id), string.Create(c, $"level {level}: {t.Name} is off the list"));
                Assert.True(cost <= cap.Limit(t.Kind, t.Id, total), string.Create(c, $"level {level}: {t.Name} costs {cost:N0} of {total:N0}, over its {cap.LimitPercent(t.Kind, t.Id)}%"));
                magic += t.Kind == S && t.Id != MageTemplate.ManaConversion ? cost : 0;
            }

            if (level is 5 or 10 or 22 or 40 or 80 or 126)
            {
                output.WriteLine(string.Create(c, $"level {level}: the four magic skills {100.0 * magic / total:0.0}% of {total:N0}; unspent {plan.Remainder:N0}"));
            }

            Assert.True(level < 5 || magic * 100 >= total * 45, string.Create(c, $"level {level}: the four magic skills hold {100.0 * magic / total:0.0}% of the experience earned")); // from level 5 at least 45% of all she has earned
        }
    }

    [RequiresDatsFact]
    public void With_the_buff_bots_lines_the_same_allocation_buys_no_Strength_at_all()
    {
        var table = XpTableData.Load(fixture.Dats);
        var (h, server, rule) = Replay(table, standingBot: () => true, cover: true);
        Table(server, "the same allocation with the buff bot's lines counted (its Strength line in the floor, its level VII lines in the push):");
        foreach (var line in h.Log.Lines.Where(l => l.Contains("xp: ", StringComparison.Ordinal) && !l.Contains("xp: raise", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        Assert.Null(rule.Failure);
        Assert.Contains("want=23 (base Strength 62, standing +40 (the worn pieces' own lasting Strength line +0, the buff bot's line +40), 62% now", rule.StrengthNote, StringComparison.Ordinal);
        Assert.EndsWith(": carried, no floor rank to buy", rule.StrengthNote, StringComparison.Ordinal);
        Assert.Equal(0, Raised(server, A, MageTemplate.Strength));
        Assert.All(server.Raised, r => Assert.True(r.Kind == S ? MageTemplate.ClimberSpendSkills.Contains(r.Id) : r.Kind == V, $"{r.Kind} {r.Id}")); // no attribute at all: the magic skills and the vitals her split names
        // 160,739 (93.8% of the allocation) to the magic skills, in the plan's order: War Magic and Life Magic to their targets, then Creature Enchantment as far as the pool goes; Item Enchantment, last of the four, waits for the next allocation.
        Assert.Equal((62_858, 62_858, 35_023, 0), (Raised(server, S, MageTemplate.WarMagic), Raised(server, S, MageTemplate.LifeMagic), Raised(server, S, MageTemplate.CreatureEnchantment), Raised(server, S, MageTemplate.ItemEnchantment)));
        Assert.Equal((7_284, 168_023L, 3_346L), (Raised(server, V, MageTemplate.MaxStamina), rule.SpentXp, rule.Banked));
        Assert.Contains("groups: attributes 0 0.0% run 34.4%; listed skills 160739 93.8% run 44.2%", rule.SharesNote, StringComparison.Ordinal);
    }
}
