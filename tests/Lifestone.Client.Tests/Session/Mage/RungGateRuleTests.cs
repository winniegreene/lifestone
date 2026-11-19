using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AK (the owner, 2026-11-06): she does not go to the Lodge until the gate holds. Proved: the gate is the ladder's data; a missing skill, scroll or standing line keeps the rung decision on the rung before it at any level of the Lodge's band; Armor Self and the protection are judged at the highest level she can reach; each condition carries its name and what was read; past the Lodge's band the gate decides nothing.
/// </summary>
public sealed class RungGateRuleTests
{
    private static Ladder L => LadderLoader.WarMage;

    private static RungGate Gate => L.GateOf("lodge")!;

    private static PlayerQualities Winnie(bool itemTrained)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, itemTrained ? 2u : 1u, 0, 0u, 0, 0);
        return q;
    }

    private static IReadOnlyList<GateCondition> All(bool itemTrained = true, int up = 4, int reach = 4) =>
        RungGateRule.Check(Gate, Winnie(itemTrained), (_, _) => true, _ => up, _ => reach);

    [Fact]
    public void The_gate_is_the_ladders_data_and_names_the_Lodge_and_the_robes_two_lines_at_III()
    {
        Assert.Equal("lodge", Gate.Rung);
        Assert.Equal([MageTemplate.ItemEnchantment], Gate.Skills);
        Assert.Equal(3, Gate.Lines.Single(l => l.Line == "Impenetrability").Level);
        Assert.Equal(3, Gate.Lines.Single(l => l.Line == "Bludgeon Bane").Level);
        Assert.True(Gate.Lines.Single(l => l.Line == "Armor Self").Highest);
        Assert.Contains("owner's decision", L.Sources["rung_gate"], StringComparison.Ordinal);
        Assert.All(Gate.Lines, l => Assert.NotNull(BuffProfiles.Climber.LineNamed(l.Line)));
    }

    private static uint SpellOf(string line, int level) => BuffProfiles.Climber.LineNamed(line)!.Spells[level - 1].Id;

    [Fact]
    public void A_book_with_II_and_III_only_has_learned_the_line_at_I()
    {
        // Stretch 4: "Focus Self I learned: NOT MET (not in the book)" with Focus Self II and III learned: the plan skips level I
        var book = new HashSet<uint> { SpellOf("Focus Self", 2), SpellOf("Focus Self", 3) };
        var knows = RungGateRule.BookKnows(BuffProfiles.Climber, book.Contains);

        Assert.True(knows("Focus Self", 1));
        Assert.True(knows("Focus Self", 3));
        Assert.False(knows("Focus Self", 4)); // a higher level is not learned by a lower one
        Assert.False(knows("Willpower Self", 1));
        Assert.Contains(RungGateRule.Standing(Gate, Winnie(true), knows), c => c is { Name: "Focus Self II learned", Met: true, Read: "in the book" }); // M6 14 (D135): the Lodge asks the Burrow's Focus Self II
    }

    [Fact]
    public void A_higher_level_of_every_gate_line_meets_it_and_the_robes_lines_hold_at_III()
    {
        var knows = RungGateRule.BookKnows(BuffProfiles.Climber, BuffProfiles.Climber.Lines.SelectMany(l => l.Spells.Where(s => s.Position == 4)).Select(s => s.Id).ToHashSet().Contains);

        Assert.All(RungGateRule.Standing(Gate, Winnie(true), knows), c => Assert.True(c.Met, c.Name));
    }

    [Theory]
    [InlineData("Bludgeon Protection Self")] // the gate's and the profile's name
    [InlineData("Bludgeoning Protection Self")] // the spell's and the first scrolls'
    public void The_protection_line_reads_by_its_real_names_so_the_check_is_not_false_for_ever(string name)
    {
        Assert.StartsWith("Bludgeoning Protection Self", RungGateRule.LineOf(BuffProfiles.Climber, name).Spells[0].Name, StringComparison.Ordinal);
        var knows = RungGateRule.BookKnows(BuffProfiles.Climber, new HashSet<uint> { 1021 }.Contains); // Bludgeoning Protection Self IV, the first scroll she buys

        Assert.True(knows(name, 1));
    }

    [Fact]
    public void A_gate_line_that_names_no_line_of_the_profile_fails_loudly_never_a_gate_shut_for_ever()
    {
        var thrown = Assert.Throws<InvalidOperationException>(() => RungGateRule.BookKnows(BuffProfiles.Climber, static _ => true)("Bludgeon Self", 1));

        Assert.Contains("TODO(rung-gate-line)", thrown.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(9, false, 9)] // the outskirts: no gate
    [InlineData(15, true, 15)] // every condition met (M6 14, D135: the Lodge from 15)
    [InlineData(15, false, 14)] // held on the rung before it, the Burrow (M6 7 AR)
    [InlineData(19, false, 14)] // the move-on level alone never sends her
    [InlineData(21, false, 20)] // M6 7 AX: the Cave has its gate: one step down, to the Lodge; Walk chains the rest (CaveGateTests)
    public void The_rung_decision_reads_the_gate(int level, bool itemTrained, int expected) =>
        Assert.Equal(expected, RungGateRule.HeldLevel(L, level, All(itemTrained)));

    private static IReadOnlyList<GateCondition> Conditions(RungGate gate, bool open, HashSet<string> log)
    {
        log.Add(gate.Rung);
        return [new GateCondition("the test", open, "the test")];
    }

    [Theory]
    [InlineData(10, false, false, 9, "burrow")] // the Burrow's gate shut: on the outskirts (M6 14, D135: the Burrow from 10)
    [InlineData(10, true, false, 10, "burrow")] // open: the Burrow
    [InlineData(15, false, false, 9, "burrow,lodge")] // both shut: the chain drops her to the outskirts, past the Burrow's level or not
    [InlineData(19, true, false, 14, "burrow,lodge")] // the Burrow open, the Lodge shut: she stays on the Burrow past its band
    [InlineData(15, true, true, 15, "lodge")] // the Lodge's gate alone moves her on
    public void Gates_chain_each_holds_her_on_the_rung_before_it(int level, bool burrow, bool lodge, int expected, string asked)
    {
        var read = new HashSet<string>();
        var held = RungGateRule.Walk(L, level, gate => Conditions(gate, gate.Rung == "burrow" ? burrow : lodge, read));

        Assert.Equal(expected, held);
        Assert.Equal(asked.Split(',').Order().ToArray(), read.Order().ToArray());
    }

    [Fact]
    public void She_stands_on_the_burrow_so_its_gate_is_not_read_and_she_never_falls_back_to_the_outskirts()
    {
        var held = RungGateRule.Walk(L, 18, gate => gate.Rung == "burrow" ? null : [new GateCondition("shut", false, "shut")]);

        Assert.Equal(14, held); // M6 14 (D135): the Burrow's band is 10-14
        Assert.Equal("burrow", L.RungFor(held).Key);
    }

    private static uint CellIn(string groundKey) => (uint)GroundCatalog.Get(groundKey).Landblocks[0] << 16 | 0x0001;

    [Fact]
    public void A_run_starts_with_every_line_up_counted_as_not_met_by_name_M6_7_AV()
    {
        var all = RungGateRule.AtStart(Gate, Winnie(true), (_, _) => true);

        Assert.All(all.Where(c => !c.Name.EndsWith(" up", StringComparison.Ordinal)), c => Assert.True(c.Met, c.Name)); // the book and the skills read as they do
        var up = all.Where(c => c.Name.EndsWith(" up", StringComparison.Ordinal)).ToList();
        Assert.Equal(Gate.Lines.Select(l => l.Line + " up").ToArray(), up.Select(c => c.Name).ToArray());
        Assert.All(up, c => { Assert.False(c.Met); Assert.Equal(RungGateRule.UnreadAtStart, c.Read); });
    }

    [Theory]
    [InlineData(15, "holtburg", false, 9)] // live 2026-11-06 (at 17, then): every line learned, no buff up: not the Lodge, not the Burrow (its gate reads the same)
    [InlineData(15, "holtburg", true, 15)] // with the buffs read standing the gates open; M6 7 AW: never on the book alone
    [InlineData(15, "tusker-lodge", false, 15)] // standing on the Lodge: its gate is not read, she is not stranded
    [InlineData(15, "tusker-retreat", false, 14)] // standing in the 10-14 rung's ground (M6 retreat: the Tusker Retreat): the Lodge's gate holds her there, the rung's own is not read
    [InlineData(20, "tusker-retreat", false, 14)]
    public void A_restart_never_picks_a_rung_behind_a_gate_on_the_book_alone_M6_7_AV(int level, string standing, bool buffsUp, int expected)
    {
        uint? cell = standing == "holtburg" ? 0xA9B40001 : CellIn(standing);
        var ask = new Lifestone.Client.Session.Autopilot.RungAsk(level, cell, Winnie(true), (_, _) => true, buffsUp ? new Lifestone.Client.Session.Autopilot.RungBuffRead(_ => 4, _ => 4) : null);

        Assert.Equal(expected, Lifestone.Client.Session.Autopilot.RungOwner.Decide(L, ask).HeldLevel);
    }

    [Fact]
    public void The_burrows_gate_is_the_two_armor_lines_at_IV_their_bootstrap_and_the_item_lines_at_III_up_and_data()
    {
        var gate = L.GateOf("burrow")!;

        Assert.Single(gate.Skills);
        Assert.Equal([("Focus Self", 2), ("Willpower Self", 2), ("Life Magic Mastery Self", 3), ("Armor Self", 4), ("Bludgeon Protection Self", 4), ("Impenetrability", 3), ("Bludgeon Bane", 3)], gate.Lines.Select(l => (l.Line, l.Level)).ToArray());
        Assert.All(gate.Lines, l => Assert.False(l.Highest));
        Assert.All(gate.Lines, l => Assert.NotNull(RungGateRule.LineOf(BuffProfiles.Climber, l.Line)));
        var conditions = RungGateRule.Check(gate, Winnie(true), (_, _) => true, name => name == "Armor Self" ? 3 : 4, _ => 4);
        Assert.Equal(["Armor Self up"], conditions.Where(c => !c.Met).Select(c => c.Name).ToArray()); // Armor III under the bar holds her outside
    }

    [Fact]
    public void A_gate_on_a_rung_that_does_not_exist_is_refused_by_the_loader()
    {
        Assert.Null(L.GateOf("no-such-rung"));
        Assert.Equal(["burrow", "lodge", "cave", "abode", "quarters", "holding"], L.Gates.Select(g => g.Rung).ToArray()); // M6 14: the Abode's gate, of the Cave's shape (AbodeGateTests); D135: the Quarters' and the Holding's
    }

    /// <summary>
    /// M6 14 (D135): a gate that reads open is the only one the chain reads (<see cref="RungGateRule.Walk"/>), so a later gate must hold every line an earlier one holds, at its level or more, and its skills; else a climber skips the
    /// earlier gate's demands by its band (the Lodge from 15 asked Armor Self at I or the highest she could reach while the Burrow's asked IV). Every rung from the Burrow up has one, in climbing order, and every line is a level of the
    /// climber's profile.
    /// </summary>
    [Fact]
    public void A_later_gate_holds_every_line_and_skill_of_an_earlier_one_at_its_level_or_more_and_no_band_past_the_outskirts_is_ungated()
    {
        var gates = L.Gates;
        for (var i = 1; i < gates.Count; i++)
        {
            foreach (var earlier in gates.Take(i))
            {
                Assert.Subset(gates[i].Skills.ToHashSet(), earlier.Skills.ToHashSet());
                foreach (var line in earlier.Lines)
                {
                    var later = gates[i].Lines.SingleOrDefault(l => l.Line == line.Line);
                    Assert.True(later is not null && later.Level >= line.Level && (later.Highest || !line.Highest), $"{gates[i].Rung} asks {line.Line} at {later?.Level.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing"}; {earlier.Rung} asks {line.Level}");
                }
            }
        }

        var order = L.Rungs.Select(r => r.Key).ToList();
        Assert.Equal(L.Gates.Select(g => order.IndexOf(g.Rung)).Order().ToArray(), L.Gates.Select(g => order.IndexOf(g.Rung)).ToArray()); // climbing order
        Assert.All(L.Rungs.SkipWhile(r => r.Key != "burrow"), r => Assert.NotNull(L.GateOf(r.Key)));
        Assert.All(L.Gates.SelectMany(g => g.Lines), l => Assert.True(RungGateRule.LineOf(BuffProfiles.Climber, l.Line).Spells.Count >= l.Level, l.Line));
        Assert.Equal([("Armor Self", 4, true), ("Bludgeon Protection Self", 4, true)], Gate.Lines.Where(l => l.Line is "Armor Self" or "Bludgeon Protection Self").Select(l => (l.Line, l.Level, l.Highest)));
        Assert.DoesNotContain("TODO(m6-gate-past-the-cave)", L.Sources["rung_gate"], StringComparison.Ordinal); // closed: the Quarters and the Holding have their gates
        Assert.Contains("D135", L.Sources["rung_gate"], StringComparison.Ordinal);
    }

    [Fact]
    public void Each_condition_is_named_and_a_line_under_the_highest_she_can_reach_holds_her()
    {
        var unmet = All(up: 3, reach: 4).Where(c => !c.Met).Select(c => c.Name).ToList();
        Assert.Equal(["Armor Self up", "Bludgeon Protection Self up"], unmet); // III stands, IV is in reach; the robe's lines ask III
        Assert.Equal("level 3 of 4", All(up: 3, reach: 4).First(c => c.Name == "Armor Self up").Read);
        Assert.Contains(All(itemTrained: false), c => c is { Name: "Item Enchantment trained", Met: false, Read: "untrained" });
        Assert.Contains(RungGateRule.Check(Gate, Winnie(true), (line, _) => line != "Bludgeon Bane", _ => 4, _ => 4), c => c is { Name: "Bludgeon Bane III learned", Met: false });
    }

    [Fact]
    public void A_line_that_stands_by_anothers_stronger_spell_needs_no_scroll_of_her_own_M6_14()
    {
        // The Cave's gate asks for Armor Self V and Bludgeon Protection Self V learned; with the buff bot's level VII standing over the line, the condition is met and says what stands, never "in the book".
        var cave = L.GateOf("cave")!;
        Func<string, int, bool> book = (line, level) => line is not ("Armor Self" or "Bludgeon Protection Self") || level <= 4; // her book holds level IV of the two
        Func<string, int, string?> stands = (line, level) => line == "Armor Self" && level <= 7 ? "stands by another's spell 2052 at level VII" : null;

        var alone = RungGateRule.Standing(cave, Winnie(true), book);
        Assert.Contains(alone, c => c is { Name: "Armor Self V learned", Met: false, Read: "not in the book" });
        Assert.Contains(alone, c => c is { Name: "Bludgeon Protection Self V learned", Met: false });

        var withBot = RungGateRule.Standing(cave, Winnie(true), book, stands);
        Assert.Contains(withBot, c => c is { Name: "Armor Self V learned", Met: true, Read: "stands by another's spell 2052 at level VII" });
        Assert.Contains(withBot, c => c is { Name: "Bludgeon Protection Self V learned", Met: false, Read: "not in the book" }); // nothing stands over that line
        Assert.Contains(withBot, c => c is { Name: "Focus Self II learned", Read: "in the book" }); // a line the book holds is never asked of the bot (M6 14, D135: the Cave asks the Burrow's Focus Self II)

        Assert.Contains(RungGateRule.AtStart(cave, Winnie(true), book, stands), c => c is { Name: "Armor Self V learned", Met: true });
        Assert.Contains(RungGateRule.Check(cave, Winnie(true), book, _ => 7, _ => 4, stands), c => c is { Name: "Armor Self V learned", Met: true });
    }

    /// <summary>Bludgeon Bane VI as the live armour line: 4 base mana and 20 a worn piece (Impenetrability VI read "up to 204" on ten pieces, 2026-11-13).</summary>
    private static (ProfileLine Line, Dictionary<uint, Lifestone.Data.Magic.SpellInfo> Spells, Spellbook Book) BaneVi()
    {
        var line = BuffProfiles.Climber.LineNamed("Bludgeon Bane")!;
        var vi = line.Spells[5];
        var info = WorldActionRig.Spell(vi.Id, DatReaderWriter.Enums.SpellType.Enchantment) with { Name = vi.Name, School = DatReaderWriter.Enums.MagicSchool.ItemEnchantment, Power = vi.Power, BaseMana = 4, ManaMod = 20, Category = 160 };
        var book = new Spellbook { IsKnown = true };
        book.Learn(vi.Id);
        return (line, new Dictionary<uint, Lifestone.Data.Magic.SpellInfo> { [vi.Id] = info }, book);
    }

    [Fact]
    public void M6_14_gate_lapse_a_line_in_her_book_on_her_bar_with_the_mana_to_hand_is_hers_to_recast_and_the_read_says_so()
    {
        var (line, spells, book) = BaneVi();
        Assert.True(book.AddFavorite(line.Spells[5].Id, 0, 4)); // tab V, the item enchantments (the player's rules put it there at its first cast)

        Assert.Null(RungGateRule.RecastHerself(line, reach: 6, want: 3, book, mana: 370, worn: 10, spells, out var can));
        Assert.Contains(line.Spells[5].Name + ": in her book and on her spell bar, her mana 370 covers up to 204, her skill and pack reach level VI of the gate's III", can, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_14_gate_lapse_each_rule_that_keeps_her_from_recasting_a_gate_line_herself_is_named_in_order()
    {
        var (line, spells, book) = BaneVi();
        var name = line.Spells[5].Name;

        Assert.StartsWith("no level of it is in reach now", RungGateRule.RecastHerself(line, 0, 3, book, 370, 10, spells, out _), StringComparison.Ordinal);
        Assert.Equal("the level she can cast now, II, is under the gate's III", RungGateRule.RecastHerself(line, 2, 3, book, 370, 10, spells, out _));
        Assert.Equal(line.Spells[4].Name + " is not in her book", RungGateRule.RecastHerself(line, 5, 3, book, 370, 10, spells, out _));
        Assert.Equal(name + " is on none of her spell bars (the autopilot casts only from the spell bar)", RungGateRule.RecastHerself(line, 6, 3, book, 370, 10, spells, out _)); // in the book, on no bar

        book.AddFavorite(line.Spells[5].Id, 0, 4);
        Assert.Equal("her mana 200 is under the most " + name + " can cost, 204", RungGateRule.RecastHerself(line, 6, 3, book, 200, 10, spells, out var can));
        Assert.Empty(can);
        Assert.Null(RungGateRule.RecastHerself(line, 6, 3, book, 204, 10, spells, out _)); // the ceiling exactly: payable
    }
}
