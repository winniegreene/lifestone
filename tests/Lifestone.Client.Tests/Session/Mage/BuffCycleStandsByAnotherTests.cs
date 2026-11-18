using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 S2b: a creature or life buff line stands when a stronger spell of its category stands on her that is none of her profile's (the buff bot's Other spell at level VII over her own Self line; ACE stacks by
/// category and power, <c>Managers/EnchantmentManager.cs</c> 134-204 @95ef426d). On the scripted ACE of <see cref="ScriptedCaster"/> with the bot's enchantment fed as ACE writes it (GameEvent 0x02C2): the line
/// reads the level of its own spell of that power and that entry's seconds; a pass skips it and casts the others; it comes due on that entry's clock; a weaker spell of another's under her own changes nothing;
/// a rung gate's "up" condition is met by it; an item line (the armour's ledger, the wand's auras) is untouched; a line whose category cannot be read is read as before and says so once.
/// </summary>
public sealed class BuffCycleStandsByAnotherTests
{
    private const string Armor = "Armor Self";

    private sealed class Rig
    {
        public Rig(BuffOptions? options = null, IReadOnlyDictionary<uint, SpellInfo>? table = null)
        {
            R = new WorldActionRig();
            Caster = new ScriptedCaster(R, 426, 275);
            Cycle = new BuffCycle(R.Api, R.H.State, table ?? Caster.Table, options ?? new BuffOptions(), R.H.Log);
        }

        public WorldActionRig R { get; }

        public ScriptedCaster Caster { get; }

        public BuffCycle Cycle { get; }

        public double Now => R.H.R.Clock.Now;

        public BuffStatus Run(double maxSeconds = 4000, double dt = 0.25)
        {
            var end = Now + maxSeconds;
            var status = BuffStatus.Running;
            while (status == BuffStatus.Running && Now < end)
            {
                R.Tick(dt);
                Caster.Pump();
                R.Tick();
                status = Cycle.Step(Now);
            }

            return status;
        }

        /// <summary>The buff lines whose casts landed after the first <paramref name="skip"/> answers.</summary>
        public List<string> Cast(int skip = 0) =>
            [.. Caster.Answered.Skip(skip).Where(a => !a.Fizzled).Select(a => ArenaProfile.Lines.First(l => l.Spells.Any(s => s.Id == a.Spell))).Where(l => l.IsBuff).Select(l => l.Name)];

        public BuffCycle.LineState Line(string name)
        {
            var lines = new List<BuffCycle.LineState>();
            Cycle.ReadLines(Now, lines);
            return lines.Single(l => l.Line.Name == name);
        }

        /// <summary>
        /// Another's cast on her, as ACE's GameEvent 0x02C2 carries it: <paramref name="spell"/> in the category of <paramref name="line"/> (the scripted table's, or <paramref name="category"/>), with the stat mod of the
        /// line's own level VII (the Other spell of a line does what its Self spell does).
        /// </summary>
        public void Another(string line, ushort spell, uint power, double duration, ushort? category = null)
        {
            var own = ArenaProfile.LineNamed(line)!;
            var mod = BuffStatMods.BySpell.TryGetValue(own.Spells[6].Id, out var known) ? known : new BuffStatMod(spell, 0x9001, 1, 0.0);
            var of = category ?? (ushort)Caster.Table[own.Spells[0].Id].Category;
            R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment(spell, 1, of, 0, power, 0.0, duration, BuffBotWire.Guid, 0f, 0f, 0.0, mod.Type, mod.Key, (float)mod.Value, 0)));
            R.Tick();
        }

        public int StandsByLines(string line) => R.H.Log.Lines.Count(l => l.Contains("buff: " + line + " stands by spell", StringComparison.Ordinal));
    }

    /// <summary>The bot's spell over <paramref name="line"/>: the Other spell at level VII (<see cref="BuffBotWire"/>).</summary>
    private static ushort BotSpell(string line) => (ushort)BuffBotWire.Tusker.Lines.Single(l => l.Line == line).SpellId;

    [Fact]
    public void Anothers_stronger_spell_of_the_category_makes_the_line_stand_at_its_level_with_its_seconds()
    {
        var t = new Rig();
        Assert.Equal(0, t.Cycle.ActiveLevel(Armor, t.Now));
        Assert.False(t.Cycle.TryStandsByAnother(Armor, t.Now, out _));

        var at = t.Now;
        t.Another(Armor, BotSpell(Armor), 300, 3600);
        Assert.Equal((ushort)2052, BotSpell(Armor)); // Executor's Boon: none of her profile's ids
        Assert.DoesNotContain(ArenaProfile.Lines, l => l.Spells.Any(s => s.Id == 2052));

        // The bot's power 300 is the line's own level VII (Armor Self VII, power 300), and the time is the entry's.
        Assert.Equal(7, t.Cycle.ActiveLevel(Armor, t.Now));
        var line = t.Line(Armor);
        Assert.True(line.Active);
        Assert.Equal(3600, line.Remaining, 3);
        Assert.Equal(3600, t.Cycle.MinRemainingSeconds(t.Now), 3);
        Assert.Equal(7, t.Cycle.ActiveLevel(Armor, at + 3599));
        Assert.Equal(0, t.Cycle.ActiveLevel(Armor, at + 3601)); // past its end: nothing stands
        Assert.True(t.Cycle.TryStandsByAnother(Armor, t.Now, out var standing));
        Assert.Equal((2052u, 300u, 7), (standing.SpellId, standing.Power, standing.Level));
        Assert.Equal(3600, standing.Remaining, 3);

        // The other lines are untouched, and the line is logged once for this standing spell however often it is read.
        Assert.Equal(0, t.Cycle.ActiveLevel("Bludgeon Protection Self", t.Now));
        Assert.Equal(1, t.StandsByLines(Armor));
        Assert.Contains("buff: Armor Self stands by spell 2052 (power 300, another's cast): reads level VII, 3600 s left: her own cast is not needed", t.R.H.Log.Lines.Single(l => l.Contains("stands by spell", StringComparison.Ordinal)), StringComparison.Ordinal);

        // The bot casts it again: a new standing spell (a new end), one more line.
        t.R.Tick(100);
        t.Another(Armor, BotSpell(Armor), 300, 3600);
        Assert.Equal(7, t.Cycle.ActiveLevel(Armor, t.Now));
        Assert.Equal(3600, t.Line(Armor).Remaining, 3);
        Assert.Equal(2, t.StandsByLines(Armor));
    }

    [Theory]
    [InlineData(300u, 7)] // the line's own spell of the same power
    [InlineData(400u, 8)]
    [InlineData(150u, 4)]
    [InlineData(275u, 6)] // no spell of the line has that power: the highest level at or under it (VI, power 250)
    [InlineData(1u, 1)]
    [InlineData(999u, 8)]
    public void The_power_of_anothers_spell_reads_as_the_lines_own_level_of_that_power(uint power, int level)
    {
        var t = new Rig();
        t.Another(Armor, 60001, power, 600);

        Assert.Equal(level, t.Cycle.ActiveLevel(Armor, t.Now));
    }

    [Fact]
    public void A_spell_under_the_lines_first_level_is_not_read()
    {
        // Mana Renewal's level I has power 25: a spell of its category under that maps to no level of the line, so the line is read by her own spells only.
        var t = new Rig();
        Assert.Equal(25u, ArenaProfile.LineNamed("Mana Renewal Self")!.Spells[0].Power);
        t.Another("Mana Renewal Self", 60002, 10, 600);

        Assert.Equal(0, t.Cycle.ActiveLevel("Mana Renewal Self", t.Now));
        Assert.False(t.Cycle.TryStandsByAnother("Mana Renewal Self", t.Now, out _));
    }

    [Fact]
    public void The_pass_skips_the_line_that_stands_by_another_and_casts_the_others()
    {
        var t = new Rig();
        t.Another(Armor, BotSpell(Armor), 300, 3600);
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());

        var r = t.Cycle.Result!;
        var cast = t.Cast();
        Assert.DoesNotContain(Armor, cast); // her own Armor Self is never cast under the bot's: no mana spent on a surpassed cast
        Assert.Equal(t.Cycle.LineOrder.Where(l => l != Armor), cast.Distinct());
        Assert.Equal(24, r.Active);
        Assert.Empty(r.Misses);
        Assert.Contains("Armor:VII", r.Levels, StringComparison.Ordinal);
        Assert.DoesNotContain(t.R.H.State.Self.Qualities.ActiveEnchantments, e => ArenaProfile.LineNamed(Armor)!.Spells.Any(s => s.Id == e.SpellId));

        // A second cycle and a re-buff pass leave it alone too.
        var answered = t.Caster.Answered.Count;
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.True(t.Cycle.StartRebuff(t.Now, forced: false));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Equal(answered, t.Caster.Answered.Count);
    }

    [Fact]
    public void The_line_comes_due_on_anothers_clock_and_her_own_cast_under_it_is_sent_once()
    {
        // Another's spell of power 400 (over every level she casts here) with 1,000 s: her own lines run their 3,600 s, so only this line comes inside the margin of 300 s, on the other spell's clock.
        var t = new Rig(new BuffOptions { RebuffMarginSeconds = 300 });
        var at = t.Now;
        t.Another(Armor, 60001, 400, 1000);
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.DoesNotContain(Armor, t.Cast());
        Assert.Equal(8, t.Cycle.ActiveLevel(Armor, t.Now));
        Assert.Empty(t.Cycle.RebuffDue(at + 1000 - 301, 300));
        Assert.Equal([Armor], t.Cycle.RebuffDue(at + 1000 - 299, 300));

        // Inside the margin the pass casts her own line under it (ACE lands it surpassed and confirms it as any enchantment: the cast counts as landed).
        t.R.Tick(at + 1000 - 299 - t.Now);
        var answered = t.Caster.Answered.Count;
        Assert.True(t.Cycle.StartRebuff(t.Now, forced: false));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Equal([Armor], t.Cast(answered));
        Assert.Equal(1, t.Cycle.Result!.Cast);

        // The stronger spell is still the one in effect, so the line reads its level; her own entry underneath outlasts it and takes effect when it ends, so the line is covered and is not cast again under it.
        Assert.Equal(8, t.Cycle.ActiveLevel(Armor, t.Now));
        Assert.True(t.Cycle.TryStandsByAnother(Armor, t.Now, out var standing));
        Assert.InRange(standing.Remaining, 250, 299);
        Assert.InRange(t.Line(Armor).Remaining, 3500, 3600);
        Assert.Empty(t.Cycle.RebuffDue(t.Now, 300));
        answered = t.Caster.Answered.Count;
        Assert.True(t.Cycle.StartRebuff(t.Now, forced: false));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Equal(answered, t.Caster.Answered.Count);

        // The other spell ends: her own level VII stands, on its own clock.
        t.R.Tick(300);
        Assert.Equal(7, t.Cycle.ActiveLevel(Armor, t.Now));
        Assert.False(t.Cycle.TryStandsByAnother(Armor, t.Now, out _));
        Assert.InRange(t.Line(Armor).Remaining, 3200, 3300);
    }

    [Fact]
    public void Anothers_lower_spell_under_her_own_higher_one_changes_nothing()
    {
        var t = new Rig();
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Equal(7, t.Cycle.ActiveLevel(Armor, t.Now));
        var before = t.Line(Armor);
        var due = t.Cycle.RebuffDue(t.Now, 3000);

        t.Another(Armor, 60001, 150, 9000); // weaker and longer: it lands under hers
        Assert.Equal(7, t.Cycle.ActiveLevel(Armor, t.Now));
        Assert.Equal(before, t.Line(Armor));
        Assert.Equal(due, t.Cycle.RebuffDue(t.Now, 3000));
        Assert.False(t.Cycle.TryStandsByAnother(Armor, t.Now, out _));
        Assert.Equal(0, t.StandsByLines(Armor));

        // Hers ends: the other's is then the one in effect, at its own level and clock.
        Assert.Equal(4, t.Cycle.ActiveLevel(Armor, t.Now + 3700));
    }

    [Fact]
    public void A_rung_gates_up_condition_is_met_by_anothers_spell()
    {
        var gate = LadderLoader.WarMage.GateOf("lodge")!;
        Assert.True(gate.Lines.Single(l => l.Line == Armor).Highest);
        var t = new Rig();
        IReadOnlyList<GateCondition> Read() => RungGateRule.Check(gate, t.R.H.State.Self.Qualities, (_, _) => true, name => t.Cycle.ActiveLevel(name, t.Now), t.Cycle.ReachLevel);
        var want = Math.Max(gate.Lines.Single(l => l.Line == Armor).Level, t.Cycle.ReachLevel(Armor));
        Assert.InRange(want, 1, 7);

        Assert.Contains(Read(), c => c.Name == "Armor Self up" && !c.Met && c.Read == $"level 0 of {want}");
        t.Another(Armor, BotSpell(Armor), 300, 3600);
        Assert.Contains(Read(), c => c.Name == "Armor Self up" && c.Met && c.Read == $"level 7 of {want}");
        Assert.Contains(Read(), c => c.Name == "Impenetrability up" && !c.Met); // the item lines are her own to cast
    }

    [Fact]
    public void An_item_line_is_not_read_from_anothers_spell()
    {
        var t = new Rig();
        foreach (var name in new[] { "Impenetrability", "Bludgeon Bane", "Hermetic Link", "Defender" })
        {
            var line = ArenaProfile.LineNamed(name)!;
            Assert.True(line.Role is ProfileRole.ItemBuffArmor or ProfileRole.ItemBuffWand);
            t.Another(name, (ushort)(61000 + line.Spells[0].Id), 300, 3600);

            Assert.Equal(0, t.Cycle.ActiveLevel(name, t.Now));
            Assert.Contains(name, t.Cycle.RebuffDue(t.Now, 300));
            Assert.False(t.Cycle.TryStandsByAnother(name, t.Now, out _));
        }

        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("stands by spell", StringComparison.Ordinal));

        // The pass casts them as ever.
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Contains("Impenetrability", t.Cast());
        Assert.Contains("Hermetic Link", t.Cast());
        Assert.Equal(24, t.Cycle.Result!.Active);
    }

    [Fact]
    public void A_line_whose_category_cannot_be_read_is_read_as_before_and_says_so_once_until_her_own_entry_names_it()
    {
        // A cycle given a SpellTable with none of the line's spells: the category is not known, so another's spell is not read for the line, by name, once.
        var t = new Rig(table: new Dictionary<uint, SpellInfo>());
        t.Another(Armor, BotSpell(Armor), 300, 3600, category: 777);
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(0, t.Cycle.ActiveLevel(Armor, t.Now));
        }

        var said = Assert.Single(t.R.H.Log.Lines, l => l.Contains("Armor Self: its spell category cannot be read", StringComparison.Ordinal));
        Assert.Contains("TODO(m6-buff-line-category)", said, StringComparison.Ordinal);

        // Her own entry of the line carries the category (the registry's), and from then on the stronger spell of that category is read.
        var own = ArenaProfile.LineNamed(Armor)!.Spells[3];
        t.R.Server((p, s) => AceM3Writers.MagicUpdateEnchantment(p, s, new AceEnchantment((ushort)own.Id, 2, 777, 0, own.Power, 0.0, 1800, WorldActionRig.Player, 0f, 0f, 0.0, 0xA080, 0, 100f, 0)));
        t.R.Tick();
        Assert.Equal(7, t.Cycle.ActiveLevel(Armor, t.Now));
        Assert.Equal(3600, t.Line(Armor).Remaining, 3);
        Assert.Single(t.R.H.Log.Lines, l => l.Contains("its spell category cannot be read", StringComparison.Ordinal) && l.Contains(Armor, StringComparison.Ordinal));
    }

    [Fact]
    public void The_entry_in_effect_of_a_category_is_the_duels_winner_among_those_with_time_left()
    {
        var t = new Rig();
        var q = t.R.H.State.Self.Qualities;
        Assert.False(EffectRules.TryInEffectOf(q, 777, t.Now, out _));
        t.Another(Armor, 60001, 150, 9000, category: 777);
        t.Another(Armor, 60002, 300, 100, category: 777);
        t.Another("Focus Self", 60003, 400, 9000, category: 778);

        var all = new List<ActiveEnchantment>();
        EffectRules.InEffect(q, all);
        Assert.True(EffectRules.TryInEffectOf(q, 777, t.Now, out var top));
        Assert.Equal((ushort)60002, top.SpellId);
        Assert.Equal(all.Single(e => e.Enchantment.SpellCategory == 777), top); // the same answer as the effects pages' duel
        Assert.True(EffectRules.TryInEffectOf(q, 777, t.Now + 101, out var later));
        Assert.Equal((ushort)60001, later.SpellId); // the stronger one is past its end: the one under it stands
        Assert.False(EffectRules.TryInEffectOf(q, 777, t.Now + 9001, out _));
        Assert.False(EffectRules.TryInEffectOf(q, 0, t.Now, out _));

        // Equal power: the later start holds.
        t.R.Tick(10);
        t.Another(Armor, 60004, 300, 50, category: 777);
        Assert.True(EffectRules.TryInEffectOf(q, 777, t.Now, out var tie));
        Assert.Equal((ushort)60004, tie.SpellId);
        EffectRules.InEffect(q, all);
        Assert.Equal(all.Single(e => e.Enchantment.SpellCategory == 777), tie);
    }

    [Fact]
    [Trait("Tier", "Timing")] // a zero-byte allocation count, as the cycle's other allocation tests
    public void Reading_a_line_that_stands_by_another_allocates_nothing_after_its_one_log_line()
    {
        var t = new Rig();
        t.Another(Armor, BotSpell(Armor), 300, 3600);
        var now = t.Now;
        Assert.Equal(7, t.Cycle.ActiveLevel(Armor, now)); // the one log line
        t.Cycle.TryStandsByAnother(Armor, now, out _);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var sum = 0.0;
        for (var i = 0; i < 500; i++)
        {
            sum += t.Cycle.ActiveLevel(Armor, now + (i * 0.01));
            sum += t.Cycle.TryStandsByAnother(Armor, now + (i * 0.01), out var standing) ? standing.Level : 0;
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(sum > 0);
        Assert.Equal(1, t.StandsByLines(Armor));
    }
}
