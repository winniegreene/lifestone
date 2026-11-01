using System.Text.RegularExpressions;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// The buff cycle (M3 2.1 s0b, <see cref="BuffCycle"/>) on a fake clock against <see cref="ScriptedCaster"/>, a scripted ACE that answers casts as the real one does (the enchantment and its StatMod for a self buff so the
/// current skill rises; one "You cast ... on ..." chat line per worn piece and UseDone 0 for an armour cast at the caster; vital updates for a transfer and a boost; a fizzle on demand): the owner's order and the levels
/// chosen from the buffed skill, a fizzle retried one level down and then a miss, the armour line's expiry per piece, the mana loop, the re-buff schedule, a buffed mage's second cycle and the idle allocation.
/// </summary>
public sealed class BuffCycleTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static readonly Regex Fields = new(
        @"^buffs=\d+/24 levels=(\w+:(I|II|III|IV|V|VI|VII|VIII|-),){23}\w+:(I|II|III|IV|V|VI|VII|VIII|-) rebuffed=(yes|no) min_remaining_s=(\d+|n/a) fizzles=\d+ transfers=\d+/\d+ revitalizes=\d+ mana_min=(\d+|n/a) cycle_s=\d+ mode_changes=\d+ misses=\S+( forced=yes)? surpassed=\d+$",
        RegexOptions.CultureInvariant);

    private sealed class Rig
    {
        public Rig(uint mana = 426, uint stamina = 275, BuffOptions? options = null, int manaSeed = ScriptedCaster.DefaultManaSeed)
        {
            R = new WorldActionRig();
            Caster = new ScriptedCaster(R, mana, stamina, manaSeed);
            Options = options ?? new BuffOptions();
            Cycle = new BuffCycle(R.Api, R.H.State, Caster.Table, Options, R.H.Log);
        }

        public WorldActionRig R { get; }

        public ScriptedCaster Caster { get; }

        public BuffOptions Options { get; }

        public BuffCycle Cycle { get; }

        public double Now => R.H.R.Clock.Now;

        /// <summary>One frame: the clock advances, the Action API sends, the scripted ACE answers, the Action API completes, the cycle steps.</summary>
        public BuffStatus Frame(double dt)
        {
            R.Tick(dt);
            Caster.Pump();
            R.Tick();
            return Cycle.Step(Now);
        }

        public BuffStatus Run(double maxSeconds = 4000, double dt = 0.25)
        {
            var end = Now + maxSeconds;
            var status = BuffStatus.Running;
            while (status == BuffStatus.Running && Now < end)
            {
                status = Frame(dt);
            }

            return status;
        }

        /// <summary>The buff-line casts that landed after the first <paramref name="skip"/> answers, as (line name, level position).</summary>
        public List<(string Line, int Position)> BuffCasts(int skip = 0)
        {
            var casts = new List<(string, int)>();
            foreach (var spell in Caster.Answered.Skip(skip).Where(a => !a.Fizzled).Select(a => a.Spell))
            {
                var line = ArenaProfile.Lines.First(l => l.Spells.Any(s => s.Id == spell));
                if (line.IsBuff)
                {
                    casts.Add((line.Name, line.Spells.First(s => s.Id == spell).Position));
                }
            }

            return casts;
        }

        public Dictionary<string, string> Levels() =>
            Cycle.Result!.Levels.Split(',').Select(l => l.Split(':')).ToDictionary(p => p[0], p => p[1], StringComparer.Ordinal);
    }

    private static string ShortOf(string line) => BuffCycle.ShortNameOf(line);

    /// <summary>Today's ceiling order (<c>--cast-mana-tries 0</c>, M5 1 K5): the tests that pin the mana loop before the tries and the dance.</summary>
    private static readonly BuffOptions CeilingOrder = new() { Mana = Lifestone.Client.Session.Vitals.ManaOrderOptions.Ceiling };

    [Fact]
    public void The_cycle_casts_the_owners_order_and_chooses_each_level_from_the_buffed_skill()
    {
        var t = new Rig(options: CeilingOrder); // M5 1 K5: the worst-case economy below pins the ceiling order (--cast-mana-tries 0)
        t.Caster.CeilingCost = true; // the worst-case economy (every cast at the ceiling): this test pins the order and the levels, and that the mana loop carries a cycle that needs it
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());

        var r = t.Cycle.Result!;
        Assert.Matches(Fields, r.Fields);
        Assert.Equal(24, r.Active);
        Assert.Empty(r.Misses);
        Assert.Equal(0, r.Fizzles);

        // The owner's order: Focus and Willpower, Creature Enchantment Mastery, the other masteries, then the rest in profile order; then the upgrade pass casts Focus and Willpower again.
        var casts = t.BuffCasts();
        Assert.Equal(26, casts.Count);
        Assert.Equal(26, r.Cast);
        Assert.Equal(2, r.Upgraded);
        Assert.Equal(["Focus Self", "Willpower Self"], casts.Skip(24).Select(c => c.Line));
        casts = casts.Take(24).ToList();
        Assert.Equal(["Focus Self", "Willpower Self", "Creature Enchantment Mastery Self", "War Magic Mastery Self", "Item Enchantment Mastery Self", "Life Magic Mastery Self", "Mana Mastery Self"], casts.Take(7).Select(c => c.Line));
        Assert.Equal(t.Cycle.LineOrder, casts.Select(c => c.Line));
        Assert.Equal(ArenaProfile.Lines.Where(l => l.IsBuff && !l.ByPlay).Select(l => l.Name).Order(StringComparer.Ordinal), casts.Select(c => c.Line).Order(StringComparer.Ordinal)); // M5 7 P7j: Spirit Drinker waits for the spellbook
        Assert.Equal(["Strength Self", "Endurance Self", "Coordination Self", "Quickness Self"], casts.Skip(7).Take(4).Select(c => c.Line));

        // The levels come from the skill the casts before left (s0a's chain, from skill 316): Focus VI, Willpower VI, then VII (Creature Enchantment Mastery already reaches VII at 91% from 333).
        Assert.Equal([6, 6], casts.Take(2).Select(c => c.Position));
        Assert.Equal(Enumerable.Repeat(7, 22), casts.Skip(2).Select(c => c.Position));
        var levels = t.Levels();
        Assert.Equal("VII", levels[ShortOf("Focus Self")]); // the upgrade pass: from the buffed skill the rule gives VII
        Assert.Equal("VII", levels[ShortOf("Willpower Self")]);
        Assert.Equal("VII", levels[ShortOf("Impenetrability")]);
        Assert.All(levels.Values, l => Assert.Equal("VII", l));
        Assert.True(r.CycleSeconds > 0);
        Assert.Equal(0, r.ModeChanges); // the waits at this regeneration rate are short: no mode change
        Assert.Equal(24, levels.Count);
        Assert.Equal(24, levels.Keys.Distinct().Count()); // the short names are distinct

        // The skills end at s0a's 373 (Creature, Item, Life) and 402 (War) after the ordered pass, and 3 higher after the upgrade pass.
        var q = t.R.H.State.Self.Qualities;
        Assert.True(GearCasts.TryCurrentSkill(q, MageTemplate.CreatureEnchantment, out var creature, out _));
        Assert.True(GearCasts.TryCurrentSkill(q, MageTemplate.WarMagic, out var war, out _));
        Assert.Equal((376u, 405u), (creature, war)); // s0a's 373 and 402, plus the 3 the upgraded Focus and Willpower (+5 each at VII) add

        // The mana loop carried the cycle: more mana was spent than the 426 the mage had, by transfers (and Revitalize for the stamina), and the lowest mana seen is a number.
        Assert.True(r.TransfersFired >= 5);
        Assert.Equal(r.TransfersFired, r.TransfersTried);
        Assert.NotNull(r.ManaMin);
        Assert.DoesNotContain(CombatModes.NonCombat, t.Caster.ModesSent);
        Assert.False(r.Rebuffed);
        Assert.True(r.MinRemainingSeconds > 2000);
    }

    [Fact]
    public void At_the_live_economy_the_cycle_ends_with_every_buff_active_in_about_the_live_time_and_mana_casts()
    {
        // The economy of D24 on the real numbers (M3 2.1 fix B, review 2.1-R2): mana regenerates at 0.2 a second, a cast takes 1.85 s (the live run: 26 lines in 56 s with 4 mana casts, so about 1.9 s a cast), and each cast
        // costs what ACE charges (ScriptedCaster: Creature_Magic.cs CalculateManaUsage / GetManaCost with the mage's Mana Conversion roll), while the cycle still waits for the ceiling (GearCasts.ManaCeiling) before a cast.
        // Live (BuffLoopLiveTests, 2026-10-16): 56 s, transfers 3/3, revitalizes 1 (4 mana casts), mana_min 63.
        var t = new Rig();
        t.Caster.ManaRegenPerSecond = 0.2;
        t.Caster.CastSeconds = 1.85;
        var start = t.Now;
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(maxSeconds: 20000, dt: 0.25));
        var r = t.Cycle.Result!;
        var elapsed = t.Now - start;
        var manaCasts = t.Caster.Answered.Count(a => t.Caster.Table[a.Spell].MetaSpellType is DatReaderWriter.Enums.SpellType.Transfer or DatReaderWriter.Enums.SpellType.Boost);
        var spent = t.Caster.Charged.Sum(c => (long)c.Cost);
        var ceiling = t.Caster.Charged.Sum(c => (long)GearCasts.ManaCeiling(t.Caster.Table[c.Spell], ScriptedCaster.PieceNames.Length));
        output.WriteLine($"scripted cycle at the live economy: {elapsed:0} s of session time, {t.Caster.Answered.Count} casts ({manaCasts} mana casts), charged {spent} mana against a ceiling of {ceiling}, Mana Conversion skill {t.Caster.ManaConversionSkill}, {r.Fields}");
        output.WriteLine("live: 56 s, 26 buff casts, transfers 3/3, revitalizes 1 (4 mana casts), mana_min 63");
        Assert.Equal(24, r.Active);
        Assert.Empty(r.Misses);
        Assert.True(r.MinRemainingSeconds > 0);

        // Near the live numbers, and far from the 1,325 s the ceiling economy gave.
        Assert.InRange(elapsed, 40, 90);
        Assert.InRange(manaCasts, 0, 8);
        Assert.True(spent * 2 < ceiling, $"ACE's cost ({spent}) is far under the ceiling ({ceiling})");

        // The Mana Conversion roll is luck: over sixteen seeds the cycle's time and mana casts stay in the same band (the live run's 4 mana casts is inside it or just above).
        var seedTimes = new List<double>();
        var seedMana = new List<int>();
        for (var seed = 1; seed <= 16; seed++)
        {
            var u = new Rig(manaSeed: seed);
            u.Caster.ManaRegenPerSecond = 0.2;
            u.Caster.CastSeconds = 1.85;
            var begin = u.Now;
            Assert.True(u.Cycle.Start(u.Now));
            Assert.Equal(BuffStatus.Done, u.Run(maxSeconds: 20000, dt: 0.25));
            Assert.Equal(24, u.Cycle.Result!.Active);
            seedTimes.Add(u.Now - begin);
            seedMana.Add(u.Caster.Answered.Count(a => u.Caster.Table[a.Spell].MetaSpellType is DatReaderWriter.Enums.SpellType.Transfer or DatReaderWriter.Enums.SpellType.Boost));
        }

        output.WriteLine($"over 16 seeds: cycle {seedTimes.Min():0}-{seedTimes.Max():0} s (mean {seedTimes.Average():0}), mana casts {seedMana.Min()}-{seedMana.Max()} (mean {seedMana.Average():0.0})");
        Assert.All(seedTimes, x => Assert.InRange(x, 40, 120));
        Assert.All(seedMana, x => Assert.InRange(x, 0, 12));
    }

    [Fact]
    public void A_cast_costs_aces_mana_conversion_price_never_above_the_ceiling_and_the_same_seed_rolls_the_same()
    {
        // Review 2.1-R2: ScriptedCaster charges Creature_Magic.cs CalculateManaUsage / GetManaCost, not the ceiling. The mage's Mana Conversion is (Focus + Self) / 6 + 169 ranks = 258 unbuffed.
        var a = new Rig();
        Assert.Equal(258u, a.Caster.ManaConversionSkill);
        Assert.True(a.Cycle.Start(a.Now));
        Assert.Equal(BuffStatus.Done, a.Run());
        Assert.True(a.Caster.ManaConversionSkill > 258u); // Focus, Self and Mana Mastery raised it
        Assert.NotEmpty(a.Caster.Charged);
        foreach (var (spell, cost) in a.Caster.Charged)
        {
            var ceiling = GearCasts.ManaCeiling(a.Caster.Table[spell], ScriptedCaster.PieceNames.Length);
            Assert.InRange(cost, 1u, ceiling);
        }

        Assert.Contains(a.Caster.Charged, c => c.Cost < GearCasts.ManaCeiling(a.Caster.Table[c.Spell], ScriptedCaster.PieceNames.Length));

        var b = new Rig();
        Assert.True(b.Cycle.Start(b.Now));
        Assert.Equal(BuffStatus.Done, b.Run());
        Assert.Equal(a.Caster.Charged, b.Caster.Charged); // the roll is seeded

        var c = new Rig(manaSeed: 7);
        Assert.True(c.Cycle.Start(c.Now));
        Assert.Equal(BuffStatus.Done, c.Run());
        Assert.NotEqual(a.Caster.Charged, c.Caster.Charged);

        var worst = new Rig();
        worst.Caster.CeilingCost = true;
        Assert.True(worst.Cycle.Start(worst.Now));
        Assert.Equal(BuffStatus.Done, worst.Run());
        Assert.All(worst.Caster.Charged, x => Assert.Equal(GearCasts.ManaCeiling(worst.Caster.Table[x.Spell], ScriptedCaster.PieceNames.Length), x.Cost));
    }

    [Fact]
    public void At_the_ceiling_cost_and_the_live_regeneration_the_cycle_still_ends_with_every_buff_active_the_worst_case()
    {
        // The old economy, kept as the worst case: every cast charged GearCasts.ManaCeiling (no Mana Conversion), regeneration 0.2 a second. A long wait, but the cycle ends with every line active.
        var t = new Rig(options: CeilingOrder); // M5 1 K5: the ceiling order (--cast-mana-tries 0)
        t.Caster.ManaRegenPerSecond = 0.2;
        t.Caster.CeilingCost = true;
        var start = t.Now;
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(maxSeconds: 20000, dt: 0.5));
        var r = t.Cycle.Result!;
        var manaCasts = t.Caster.Answered.Count(a => t.Caster.Table[a.Spell].MetaSpellType is DatReaderWriter.Enums.SpellType.Transfer or DatReaderWriter.Enums.SpellType.Boost);
        output.WriteLine($"worst-case (ceiling-cost) cycle: {t.Now - start:0} s of session time, {t.Caster.Answered.Count} casts ({manaCasts} mana casts), {r.Fields}");
        Assert.Equal(24, r.Active);
        Assert.Empty(r.Misses);
        Assert.True(r.MinRemainingSeconds > 0);
        Assert.True(manaCasts >= 10);
    }

    [Fact]
    public void A_fizzle_is_retried_at_the_same_level_twice_then_one_level_down_once_then_a_named_miss()
    {
        // M5 1 K3 (D14, the owner's fizzle rule): was "retried once one level down and a second fizzle is a named miss". Now: VI fizzles three times (the first try and two more), V is tried once, and a fizzle there is the miss.
        var focus = ArenaProfile.LineNamed("Focus Self")!;
        var t = new Rig();
        t.Caster.FizzleNext(focus.Spells[5].Id, 1); // Focus Self VI fizzles once: VI is cast again, at the same level
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var r = t.Cycle.Result!;
        Assert.Equal(1, r.Fizzles);
        Assert.Empty(r.Misses);
        Assert.Equal("VII", t.Levels()[ShortOf("Focus Self")]); // VI landed on the second try, then the upgrade pass cast VII
        Assert.Equal([focus.Spells[5].Id, focus.Spells[5].Id], t.Caster.Answered.Take(2).Select(a => a.Spell));
        Assert.Equal(2, r.Upgraded); // Focus (VI to VII) and Willpower (VI to VII)
        Assert.Equal(24, r.Active);

        var down = new Rig();
        down.Caster.FizzleNext(focus.Spells[5].Id, 3); // VI fizzles on all three of its tries: V is cast once
        Assert.True(down.Cycle.Start(down.Now));
        Assert.Equal(BuffStatus.Done, down.Run());
        Assert.Equal(3, down.Cycle.Result!.Fizzles);
        Assert.Empty(down.Cycle.Result.Misses);
        Assert.Equal([focus.Spells[5].Id, focus.Spells[5].Id, focus.Spells[5].Id, focus.Spells[4].Id], down.Caster.Answered.Take(4).Select(a => a.Spell));
        Assert.Equal([true, true, true, false], down.Caster.Answered.Take(4).Select(a => a.Fizzled));

        var twice = new Rig();
        twice.Caster.FizzleNext(s => s.Id == focus.Spells[5].Id || s.Id == focus.Spells[4].Id, 4); // VI three times, then V: all fizzle; no fifth try
        Assert.True(twice.Cycle.Start(twice.Now));
        Assert.Equal(BuffStatus.Done, twice.Run());
        var missed = twice.Cycle.Result!;
        Assert.Equal(4, missed.Fizzles);
        Assert.Equal(["Focus:fizzled"], missed.Misses);
        Assert.Contains("fizzled 4 times (2 more tries at the same level, then one level down)", Assert.Single(missed.MissDetails), StringComparison.Ordinal);
        Assert.Equal(23, missed.Active);
        Assert.Equal("-", twice.Levels()[ShortOf("Focus Self")]);
        Assert.Equal(4, twice.Caster.Answered.Count(a => focus.Spells.Any(s => s.Id == a.Spell))); // never a fifth try
        Assert.Matches(Fields, missed.Fields);
        Assert.Contains("misses=Focus:fizzled", missed.Fields, StringComparison.Ordinal);
    }

    [Fact]
    public void An_armour_line_is_one_cast_at_the_caster_ended_by_its_UseDone_with_the_expiry_kept_per_piece()
    {
        var t = new Rig();
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var impen = ArenaProfile.LineNamed("Impenetrability")!.Spells[6];
        var bane = ArenaProfile.LineNamed("Bludgeon Bane")!.Spells[6];
        Assert.Equal(1, t.Caster.Answered.Count(a => a.Spell == impen.Id)); // one cast per line, not one per piece
        Assert.Equal(1, t.Caster.Answered.Count(a => a.Spell == bane.Id));
        // Every buff line goes out as GameAction 0x004A at the caster's own guid: the two armour lines by D23, the 24 SelfTargeted ones as retail's ClientMagicSystem::CastSpell sends a self spell
        // (m5-buff-effects-drawn: ACE plays the buff's effect on the player only for a cast that names a target).
        var sent = t.R.H.Net.Of(FakeSendKind.CastTargetedSpell);
        Assert.All(sent, s => Assert.Equal(WorldActionRig.Player, s.Guid));
        Assert.Equal(26, sent.Count(s => ArenaProfile.Lines.Any(l => l.IsBuff && l.Spells.Any(x => x.Id == (uint)s.Index))));
        Assert.DoesNotContain(t.R.H.Net.Of(FakeSendKind.CastUntargetedSpell), s => ArenaProfile.Lines.Any(l => l.IsBuff && l.Spells.Any(x => x.Id == s.Guid)));
        Assert.Equal("VII", t.Levels()[ShortOf("Impenetrability")]);

        // The expiry is the cast's UseDone time plus the SpellTable duration (3,600 s at VII): the line leaves the margin's list when it is more than the margin away, and joins it inside.
        var at = t.Caster.Answered.First(a => a.Spell == impen.Id).At;
        Assert.DoesNotContain("Impenetrability", t.Cycle.RebuffDue(at + 3600 - 301, 300));
        Assert.Contains("Impenetrability", t.Cycle.RebuffDue(at + 3600 - 299, 300));
        Assert.Contains("Impenetrability", t.Cycle.RebuffDue(at + 3600 + 1, 300)); // expired: nothing left

        // A worn piece with no chat line is a named miss, and the line is not active: ACE names every piece it enchanted.
        var silent = new Rig();
        silent.Caster.SilencePiece("Leather Boots");
        Assert.True(silent.Cycle.Start(silent.Now));
        Assert.Equal(BuffStatus.Done, silent.Run());
        var r = silent.Cycle.Result!;
        Assert.Equal(["Impen:no-piece:Leather_Boots", "BludB:no-piece:Leather_Boots"], r.Misses);
        Assert.Equal(22, r.Active);
        Assert.Equal("-", silent.Levels()[ShortOf("Impenetrability")]);
        Assert.Contains("1 of 5 worn pieces", r.MissDetails[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_piece_whose_armour_cast_was_surpassed_holds_a_stronger_version_and_the_line_counts_active()
    {
        // ACE: "You cast <spell> on <item>, but it is surpassed by <spell>" (WorldObject_Magic.cs 419): the stronger enchantment stays (retail), so the line is active for that piece. Closes TODO(m3-buff-armour-surpassed).
        var t = new Rig();
        t.Caster.SurpassPiece("Leather Boots");
        t.Caster.SurpassPiece("Leather Cap");
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var r = t.Cycle.Result!;
        Assert.Empty(r.Misses);
        Assert.Equal(24, r.Active);
        Assert.Equal(4, r.Surpassed); // two pieces, two armour lines
        Assert.EndsWith(" surpassed=4", r.Fields, StringComparison.Ordinal);
        Assert.Matches(Fields, r.Fields);
        Assert.Equal("VII", t.Levels()[ShortOf("Impenetrability")]);

        // A silent piece is still a miss next to a surpassed one.
        var mixed = new Rig();
        mixed.Caster.SurpassPiece("Leather Cap");
        mixed.Caster.SilencePiece("Leather Boots");
        Assert.True(mixed.Cycle.Start(mixed.Now));
        Assert.Equal(BuffStatus.Done, mixed.Run());
        Assert.Equal(["Impen:no-piece:Leather_Boots", "BludB:no-piece:Leather_Boots"], mixed.Cycle.Result!.Misses);
        Assert.Equal(2, mixed.Cycle.Result.Surpassed);
    }

    [Fact]
    public void A_transfer_that_fizzles_twice_is_tried_again_until_it_fires()
    {
        var t = new Rig(mana: 60, options: CeilingOrder); // M5 1 K5: the ceiling order's transfer loop (--cast-mana-tries 0)
        t.Caster.FizzleNext(s => s.MetaSpellType == DatReaderWriter.Enums.SpellType.Transfer, 2);
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var r = t.Cycle.Result!;
        Assert.Equal(2, r.Fizzles);
        Assert.True(r.TransfersTried >= r.TransfersFired + 2);
        Assert.True(r.TransfersFired >= 1);
        Assert.Equal(24, r.Active);
        // The first casts of the cycle are the transfer's: each try is a separate cast.
        var first = t.Caster.Answered.Take(3).ToArray();
        Assert.All(first, a => Assert.Equal(DatReaderWriter.Enums.SpellType.Transfer, t.Caster.Table[a.Spell].MetaSpellType));
        Assert.Equal([true, true, false], first.Select(a => a.Fizzled));
    }

    [Fact]
    public void The_transfer_tries_run_out_and_fail_the_cycle_loudly()
    {
        var t = new Rig(mana: 60, options: new BuffOptions { TransferTries = 3, Mana = Lifestone.Client.Session.Vitals.ManaOrderOptions.Ceiling });
        t.Caster.FizzleNext(s => s.MetaSpellType == DatReaderWriter.Enums.SpellType.Transfer, 100);
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Failed, t.Run());
        Assert.Equal(BuffCycle.StageFailed, t.Cycle.Stage);
        var r = t.Cycle.Result!;
        Assert.Contains("3 Stamina to Mana casts in a row fizzled", r.Failure, StringComparison.Ordinal);
        Assert.Equal("0/3", $"{r.TransfersFired}/{r.TransfersTried}");
        Assert.Equal(3, t.Caster.Answered.Count);
    }

    [Fact]
    public void Revitalize_is_cast_first_when_the_stamina_is_too_low_for_a_transfer_to_cover_the_shortfall()
    {
        var t = new Rig(mana: 60, stamina: 10, options: CeilingOrder); // M5 1 K5: the ceiling order (--cast-mana-tries 0)
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var first = t.Caster.Table[t.Caster.Answered[0].Spell];
        Assert.Equal(DatReaderWriter.Enums.SpellType.Boost, first.MetaSpellType); // the stamina is 10: a transfer would drain 5 and give little
        var r = t.Cycle.Result!;
        Assert.True(r.Revitalizes >= 1);
        Assert.True(r.TransfersFired >= 1);
        Assert.Equal(24, r.Active);
    }

    [Fact]
    public void The_regeneration_wait_is_named_and_bounded_and_fails_the_cycle_when_it_runs_out()
    {
        // Mana 5: below the cheapest transfer level (10), so neither mana line can be cast; with no regeneration the wait runs out.
        var t = new Rig(mana: 5, options: new BuffOptions { ManaWaitSeconds = 30, Mana = Lifestone.Client.Session.Vitals.ManaOrderOptions.Ceiling });
        t.Caster.ManaRegenPerSecond = 0;
        Assert.True(t.Cycle.Start(t.Now));
        var status = BuffStatus.Running;
        for (var i = 0; i < 20 && status == BuffStatus.Running; i++)
        {
            status = t.Frame(0.25);
        }

        Assert.Equal(BuffStatus.Running, status);
        Assert.Equal(BuffCycle.StageManaWait, t.Cycle.Stage);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("mana wait: 5 of", StringComparison.Ordinal) && l.Contains("at most 30 s", StringComparison.Ordinal));
        Assert.Equal(BuffStatus.Failed, t.Run(maxSeconds: 60, dt: 0.5));
        var r = t.Cycle.Result!;
        Assert.Contains("the mana wait ran out", r.Failure, StringComparison.Ordinal);
        Assert.Equal(0, r.Active);
        Assert.Empty(t.Caster.Answered);

        // With regeneration the same wait ends by itself: the cycle goes on once the mana reaches the cheapest level.
        var regen = new Rig(mana: 5, options: CeilingOrder);
        Assert.True(regen.Cycle.Start(regen.Now));
        Assert.Equal(BuffStatus.Done, regen.Run());
        Assert.Equal(24, regen.Cycle.Result!.Active);
        Assert.Contains(regen.R.H.Log.Lines, l => l.Contains("mana wait over", StringComparison.Ordinal));
    }

    [Fact]
    public void A_re_buff_pass_casts_only_the_lines_inside_the_margin_and_a_forced_pass_casts_only_the_line_closest_to_expiry()
    {
        // A margin of 10 s keeps the lines apart: the cycle's casts span a few tens of seconds, so a clock that leaves the earliest line 5 s puts only the earliest ones inside it.
        var t = new Rig(options: new BuffOptions { RebuffMarginSeconds = 10, StartReserve = false }); // P7c: the spacing below is the pass of before's (no reserve dances)
        t.Caster.CeilingCost = true; // the margin test's timing is tuned to the worst-case economy's pace
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var first = t.Cycle.MinRemainingSeconds(t.Now);
        t.R.Tick(first - 5);
        var due = t.Cycle.RebuffDue(t.Now, 10);
        Assert.NotEmpty(due);
        Assert.True(due.Count < 24);
        Assert.True(t.Cycle.MinRemainingSeconds(t.Now) is > 4 and < 6);
        var answered = t.Caster.Answered.Count;

        Assert.True(t.Cycle.StartRebuff(t.Now, forced: false));
        Assert.Equal(BuffStatus.Done, t.Run());
        var pass = t.Cycle.Result!;
        Assert.Equal("rebuff", pass.Pass);
        Assert.False(pass.Forced);
        Assert.Equal(due.Count, pass.Cast);
        Assert.True(pass.Rebuffed); // each was recast while still active
        var recast = t.BuffCasts(answered);
        Assert.Equal(t.Cycle.LineOrder.Where(due.Contains), recast.Select(c => c.Line)); // exactly the lines inside the margin, in the cycle's order
        Assert.All(recast, c => Assert.Equal(7, c.Position));
        Assert.Equal(24, pass.Active);

        // Nothing is inside the margin now: a pass casts nothing.
        Assert.Empty(t.Cycle.RebuffDue(t.Now, 4));
        answered = t.Caster.Answered.Count;
        Assert.True(t.Cycle.StartRebuff(t.Now, forced: false));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Equal(answered, t.Caster.Answered.Count);
        Assert.Equal(0, t.Cycle.Result!.Cast);

        // The forced pass (D25 as narrowed): only the line closest to expiry is recast, while it is still active, and the result says so.
        answered = t.Caster.Answered.Count;
        var least = t.Cycle.MinRemainingSeconds(t.Now);
        var closest = t.Cycle.RebuffDue(t.Now, least + 1);
        Assert.NotEmpty(closest);
        Assert.True(t.Cycle.StartRebuff(t.Now, forced: true));
        Assert.Equal(BuffStatus.Done, t.Run());
        var forced = t.Cycle.Result!;
        Assert.True(forced.Forced);
        Assert.Equal(1, forced.Cast);
        var one = Assert.Single(t.BuffCasts(answered));
        Assert.Contains(one.Line, closest);
        Assert.Equal(24, forced.Active);
        Assert.True(forced.Rebuffed);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("forced: " + one.Line + " has the least left", StringComparison.Ordinal));
        Assert.Contains(" forced=yes", forced.Fields, StringComparison.Ordinal);
        Assert.Matches(Fields, forced.Fields);
    }

    [Fact]
    public void A_forced_pass_on_a_reused_cycle_that_finds_no_active_line_prints_rebuffed_no()
    {
        // Review 2.1-R5: Rebuffed used to be cumulative over the cycle object's life, so a forced pass that recast nothing while active still printed rebuffed=yes after an earlier pass had. It now counts the pass.
        var t = new Rig();
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Equal(0, t.Cycle.Result!.RecastWhileActive);

        Assert.True(t.Cycle.StartRebuff(t.Now, forced: true)); // every line is active: the one closest to expiry is recast while active
        Assert.Equal(BuffStatus.Done, t.Run());
        var first = t.Cycle.Result!;
        Assert.Equal(1, first.RecastWhileActive);
        Assert.True(first.Rebuffed);
        Assert.Contains(" rebuffed=yes ", first.Fields, StringComparison.Ordinal);
        Assert.EndsWith(" forced=yes surpassed=0", first.Fields, StringComparison.Ordinal);

        // Every buff runs out (the longest lasts under two hours): the same cycle object's forced pass finds no active line, so every line is due and every cast lands on an expired line.
        t.R.Tick(8000);
        Assert.True(double.IsNaN(t.Cycle.MinRemainingSeconds(t.Now)));
        Assert.True(t.Cycle.StartRebuff(t.Now, forced: true));
        Assert.Equal(BuffStatus.Done, t.Run());
        var second = t.Cycle.Result!;
        output.WriteLine(second.Fields);
        Assert.True(second.Forced);
        Assert.True(second.Cast >= 24);
        Assert.Equal(0, second.RecastWhileActive);
        Assert.False(second.Rebuffed);
        Assert.Contains(" rebuffed=no ", second.Fields, StringComparison.Ordinal);
        Assert.EndsWith(" forced=yes surpassed=0", second.Fields, StringComparison.Ordinal);
        Assert.Matches(Fields, second.Fields);
    }

    [Fact]
    public void A_cycle_ends_with_the_upgrade_pass_and_a_second_cycle_on_a_buffed_mage_casts_nothing()
    {
        var t = new Rig();
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var first = t.Cycle.Result!;
        Assert.Equal(26, first.Cast); // 24 lines, then Focus and Willpower again at VII (TODO(m3-buff-upgrade-skip) closed)
        Assert.Equal(2, first.Upgraded);
        var answered = t.Caster.Answered.Count;

        // The second cycle: every line is active at the level the rule gives from the skill the first one built (373 and 402), with more than the margin left: nothing is cast, the armour lines from the cycle's own memory.
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Equal(answered, t.Caster.Answered.Count);
        Assert.Equal(0, t.Cycle.Result!.Cast);
        Assert.Equal(0, t.Cycle.Result.Upgraded);
        Assert.Equal(24, t.Cycle.Result.Active);
        Assert.All(t.Levels().Values, l => Assert.Equal("VII", l));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(BuffOptions.OneBuffCycleSeconds, 24)]
    public void A_start_pass_with_one_buff_cycle_as_its_margin_recasts_every_line_and_the_rebuff_margin_none(double? startMargin, int recast)
    {
        // D126 (a), section 9's m5-start-pass-margin: 1,000 s after a full cycle every line has 1,700 s or more left. A start's pass at the re-buff margin (300 s) casts nothing, so the lines run on in the batch they were cast in;
        // the autopilot's start pass (one buff cycle, 2,700 s) recasts them all, and no line left from before falls due on its own in the first fight.
        var t = new Rig(options: new BuffOptions { StartMarginSeconds = startMargin });
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        t.R.Tick(1000);
        var answered = t.Caster.Answered.Count;
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        output.WriteLine(t.Cycle.Result!.Fields);
        Assert.Equal(recast, t.BuffCasts(answered).Select(c => c.Line).Distinct().Count());
        Assert.Equal(24, t.Cycle.Result.Active);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains($"cycle starts: 24 lines, margin {startMargin ?? 300:0} s", StringComparison.Ordinal));
    }

    [Fact]
    public void The_start_margin_is_a_registered_option_and_the_autopilots_start_station_names_its_pass()
    {
        Assert.Null(new BuffOptions().StartMarginSeconds);
        var parsed = BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-start-margin-s=1800"]));
        Assert.Empty(parsed.Errors);
        Assert.Equal(1800, parsed.StartMarginSeconds);
        Assert.NotEmpty(BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-start-margin-s=-1"])).Errors);
        Assert.Contains(OptionCatalog.Buff().Options, x => x.Name == "--buff-start-margin-s");
        Assert.True(Lifestone.Client.Session.Autopilot.AutopilotScenario.BuffSpec.Arguments.ContainsKey(Lifestone.Client.Session.Scenarios.Journey.JourneyStations.StartPassArgument));
        Assert.False(Lifestone.Client.Session.Autopilot.AutopilotScenario.UpkeepSpec.Arguments.ContainsKey(Lifestone.Client.Session.Scenarios.Journey.JourneyStations.StartPassArgument)); // the upkeep keeps the re-buff margin
    }

    [Fact]
    public void An_upgrade_that_fizzles_is_a_named_miss_and_is_not_retried()
    {
        var focus = ArenaProfile.LineNamed("Focus Self")!;
        var t = new Rig();
        t.Caster.FizzleNext(focus.Spells[6].Id, 3); // Focus Self VII fizzles: the upgrade is cast once
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var r = t.Cycle.Result!;
        Assert.Equal(["Focus:upgrade-fizzled"], r.Misses);
        Assert.Equal(1, r.Fizzles);
        Assert.Equal(1, t.Caster.Answered.Count(a => a.Spell == focus.Spells[6].Id)); // no retry, at the same level or one down
        Assert.Equal(1, t.Caster.Answered.Count(a => a.Spell == focus.Spells[5].Id)); // the VI of the ordered pass, and nothing else of the line
        Assert.Equal("VI", t.Levels()[ShortOf("Focus Self")]); // the line stays at the level it held
        Assert.Equal(24, r.Active);
    }

    [Fact]
    public void A_long_regeneration_wait_is_spent_in_non_combat_and_the_cycle_returns_to_magic_before_the_next_cast()
    {
        // Mana 5 and half a point a second: the first wait (to the cheapest transfer's 10) lasts 10 s, so it goes past NonCombat's 5 s; a wait shorter than that stays in Magic.
        var t = new Rig(mana: 5, options: CeilingOrder); // M5 1 K5: the ceiling order's regeneration wait (--cast-mana-tries 0)
        t.Caster.ManaRegenPerSecond = 0.5;
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(maxSeconds: 20000));
        var r = t.Cycle.Result!;
        Assert.Equal(24, r.Active);
        Assert.Empty(r.Misses);
        Assert.True(r.ModeChanges >= 2);
        Assert.Equal(0, r.ModeChanges % 2);
        Assert.Contains(" mode_changes=" + r.ModeChanges + " ", r.Fields, StringComparison.Ordinal);

        // Every NonCombat (1) is followed by Magic (8) before any cast goes out, and the run ends in Magic.
        var modes = t.Caster.ModesSent;
        Assert.Equal(r.ModeChanges, modes.Count);
        Assert.Equal(Enumerable.Range(0, modes.Count).Select(i => i % 2 == 0 ? CombatModes.NonCombat : CombatModes.Magic), modes);
        var inPeace = false;
        foreach (var send in t.R.H.Net.Sends)
        {
            if (send.Kind == FakeSendKind.ChangeCombatMode)
            {
                inPeace = send.Amount == CombatModes.NonCombat;
            }
            else if (send.Kind is FakeSendKind.CastTargetedSpell or FakeSendKind.CastUntargetedSpell)
            {
                Assert.False(inPeace, "a cast went out in the non-combat mode");
            }
        }

        Assert.Equal(CombatModes.Magic, t.R.H.State.Self.Qualities.CombatMode);
    }

    [Fact]
    public void A_cycle_does_not_start_outside_the_magic_combat_mode_or_twice()
    {
        var t = new Rig();
        t.R.CombatModeIs(CombatModes.NonCombat);
        Assert.False(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Failed, t.Cycle.Status);
        Assert.Contains("not in the magic combat mode", t.Cycle.Result!.Failure, StringComparison.Ordinal);
        Assert.DoesNotContain(t.R.H.Net.Sends, s => s.Kind is FakeSendKind.CastTargetedSpell or FakeSendKind.CastUntargetedSpell);

        t.R.CombatModeIs(CombatModes.Magic);
        Assert.True(t.Cycle.Start(t.Now));
        Assert.False(t.Cycle.Start(t.Now)); // a pass is running
    }

    [Fact]
    public void The_cycle_allocates_nothing_while_a_cast_is_in_flight_and_while_the_mana_wait_runs()
    {
        // A cast in flight: the cycle sent it and reads the handle's outcome each step; nothing answers, so the steps only read.
        var t = new Rig();
        Assert.True(t.Cycle.Start(t.Now));
        for (var i = 0; i < 5 && t.Cycle.Stage != BuffCycle.StageCasting; i++)
        {
            Assert.Equal(BuffStatus.Running, t.Cycle.Step(t.Now));
        }

        Assert.Equal(BuffCycle.StageCasting, t.Cycle.Stage);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++)
        {
            t.Cycle.Step(t.Now);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(BuffCycle.StageCasting, t.Cycle.Stage);

        // The regeneration wait: mana below the cheapest level and no regeneration, steps across several re-evaluations (the clock advances, the wait's own look runs each second).
        var w = new Rig(mana: 5, options: CeilingOrder); // M5 1 K5: the ceiling order's regeneration wait (--cast-mana-tries 0)
        w.Caster.ManaRegenPerSecond = 0;
        Assert.True(w.Cycle.Start(w.Now));
        for (var i = 0; i < 12 && w.Cycle.Stage != BuffCycle.StageManaWait; i++)
        {
            Assert.Equal(BuffStatus.Running, w.Frame(0.25)); // the wait's first step sends NonCombat, the scripted ACE answers, the wait begins
        }

        Assert.Equal(BuffCycle.StageManaWait, w.Cycle.Stage);
        var now = w.Now;
        w.Cycle.Step(now + 1.5); // the first look after the wait began allocates nothing either, but warm it
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 300; i++)
        {
            w.Cycle.Step(now + 1.5 + (i * 0.01)); // 3 s of the session clock: a few looks, still short of the NonCombat threshold
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(BuffCycle.StageManaWait, w.Cycle.Stage);

        // Past the threshold the cycle asks for NonCombat; while that change is in flight (nothing answers) its steps only read the handle.
        w.Cycle.Step(now + BuffCycle.NonCombatAfterSeconds + 1);
        Assert.Equal(BuffCycle.StageMode, w.Cycle.Stage);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++)
        {
            w.Cycle.Step(now + BuffCycle.NonCombatAfterSeconds + 1);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(BuffCycle.StageMode, w.Cycle.Stage);

        // Answered, the wait goes on in NonCombat and allocates nothing across its looks.
        for (var i = 0; i < 8 && w.Cycle.Stage != BuffCycle.StageManaWait; i++)
        {
            w.Frame(0.25);
        }

        Assert.Equal(BuffCycle.StageManaWait, w.Cycle.Stage);
        now = w.Now;
        w.Cycle.Step(now + 1);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 2000; i++)
        {
            w.Cycle.Step(now + 1 + (i * 0.01)); // 20 s: about 20 looks
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(BuffCycle.StageManaWait, w.Cycle.Stage);
        Assert.Equal(BuffStatus.Running, w.Cycle.Status);
    }

    [Fact]
    public void The_re_buff_driver_waits_for_the_margin_then_recasts_the_lines_RebuffDue_lists()
    {
        // M4 4.1 lane a (DoD 14): the driver's schedule on the cycle's own expiry times. Margin 10 s, as the pass test above: only the earliest lines are inside it.
        var t = new RebuffRig();
        Assert.Equal(BuffStatus.Done, t.BuildBuffs());
        t.SkipTo(RebuffRig.Margin + 0.5);
        var answered = t.Caster.Answered.Count;

        t.Frame(0.1); // the first check: the soonest line has more than the margin left, so nothing is due and nothing starts
        Assert.True(t.Cycle.MinRemainingSeconds(t.Now) > RebuffRig.Margin);
        Assert.Empty(t.Cycle.RebuffDue(t.Now, RebuffRig.Margin));
        Assert.Equal(RebuffDriverState.Idle, t.Driver.State);
        Assert.Equal(answered, t.Caster.Answered.Count);

        // Checks run every 5 s: the pass starts at the first check after the margin is crossed.
        Assert.True(t.RunUntil(() => t.Driver.State == RebuffDriverState.Running, 30));
        var due = t.Cycle.RebuffDue(t.Now, RebuffRig.Margin);
        Assert.NotEmpty(due);
        Assert.True(due.Count < 24);
        Assert.True(t.RunUntil(() => t.Driver.State != RebuffDriverState.Running, 120));
        Assert.Equal(1, t.Driver.Passes);
        var pass = t.Cycle.Result!;
        Assert.Equal("rebuff", pass.Pass);
        Assert.False(pass.Forced);

        // The pass recasts every line that was due when it began, in the cycle's order; a line that drifted into the margin while the pass ran is recast too (the cycle decides each line when it comes to it).
        var recast = t.Caster.Answered.Skip(answered).Where(a => !a.Fizzled).Select(a => ArenaProfile.Lines.First(l => l.Spells.Any(s => s.Id == a.Spell))).Where(l => l.IsBuff).Select(l => l.Name).ToList();
        Assert.Equal(recast.Count, pass.Cast);
        Assert.All(due, line => Assert.Contains(line, recast));
        Assert.Equal(recast, t.Cycle.LineOrder.Where(recast.Contains));
        Assert.Equal(24, pass.Active);
    }

    [Fact]
    public void The_re_buff_driver_leaves_a_pass_somebody_else_runs_alone_and_finds_nothing_due_after_it()
    {
        var t = new RebuffRig();
        Assert.True(t.Cycle.Start(t.Now)); // another host starts the first cycle on the same object

        t.Advance(5);
        Assert.Equal(BuffStatus.Running, t.Cycle.Status);
        Assert.Equal(RebuffDriverState.Waiting, t.Driver.State);
        Assert.Equal(RebuffDriver.GuardCycle, t.Driver.WaitingFor);
        Assert.Equal(0, t.Driver.Passes);

        // The driver never steps a pass it did not start; the other host does, to the end.
        var status = BuffStatus.Running;
        for (var i = 0; i < 20000 && status == BuffStatus.Running; i++)
        {
            t.R.Tick(0.25);
            t.Caster.Pump();
            t.R.Tick();
            status = t.Cycle.Step(t.Now);
            t.Driver.Tick(t.Now);
        }

        Assert.Equal(BuffStatus.Done, status);
        t.Advance(12);
        Assert.Equal(0, t.Driver.Passes);
        Assert.Equal(RebuffDriverState.Idle, t.Driver.State);
        Assert.Equal(24, t.Cycle.Result!.Active);
    }

    [Fact]
    public void The_auto_buff_options_parse_as_a_switch_and_a_check_interval_of_at_least_a_second()
    {
        Assert.True(BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-auto=on"])).Auto);
        Assert.True(BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-auto"])).Auto);
        Assert.False(BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-auto=off"])).Auto);

        var set = BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-auto=on", "--buff-auto-check-s=1", "--buff-rebuff-margin-s=120"]));
        Assert.Empty(set.Errors);
        Assert.Equal((true, 1.0, 120.0), (set.Auto, set.AutoCheckSeconds, set.RebuffMarginSeconds));
        Assert.Contains("auto=on auto_check_s=1", set.ToString(), StringComparison.Ordinal);

        var bad = BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-auto=maybe", "--buff-auto-check-s=0.5", "--buff-auto-check-s=often", "--buff-auto-check-s=-3"]));
        Assert.Equal(4, bad.Errors.Count);
        Assert.False(bad.Auto);
        Assert.Equal(BuffOptions.DefaultAutoCheckSeconds, bad.AutoCheckSeconds);
        Assert.Contains(bad.Errors, e => e.StartsWith("--buff-auto:", StringComparison.Ordinal));
    }

    [Fact]
    public void The_options_default_parse_and_report_what_they_cannot_read()
    {
        var defaults = BuffOptions.FromArguments([]);
        Assert.Equal((0.9, 300.0, 8, 180.0), (defaults.CastChance, defaults.RebuffMarginSeconds, defaults.TransferTries, defaults.ManaWaitSeconds));
        Assert.Empty(defaults.Errors);
        Assert.False(defaults.Auto); // M4 4.1: the driver is off unless asked for
        Assert.Equal(5.0, defaults.AutoCheckSeconds);

        var set = BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-cast-chance=0.95", "--buff-rebuff-margin-s=120", "--buff-transfer-tries=3", "--buff-mana-wait-s=60"]));
        Assert.Equal((0.95, 120.0, 3, 60.0), (set.CastChance, set.RebuffMarginSeconds, set.TransferTries, set.ManaWaitSeconds));
        Assert.Empty(set.Errors);

        var bad = BuffOptions.FromArguments(CommandLineArgument.ParseAll(["--buff-cast-chance=0", "--buff-cast-chance=high", "--buff-rebuff-margin-s=-1", "--buff-transfer-tries=0", "--buff-mana-wait-s=0"]));
        Assert.Equal(5, bad.Errors.Count);
        Assert.Equal(defaults.CastChance, bad.CastChance);
        Assert.Equal(defaults.TransferTries, bad.TransferTries);

        foreach (var name in new[] { "--buff-cast-chance", "--buff-rebuff-margin-s", "--buff-transfer-tries", "--buff-mana-wait-s", "--buff-auto", "--buff-auto-check-s" })
        {
            Assert.Equal(OptionScenes.Main | OptionScenes.Headless, OptionCatalog.Registry.ScenesOf(name));
        }
    }

    // ------------------------------------------------------------------------------------------------ M5 1 K5: the mana order (D15, D27), the default

    [Fact]
    public void M5_7_a_pass_that_starts_under_its_first_buffs_cost_dances_first_then_casts_with_no_mana_try()
    {
        // Mana 12: under the first buff's cost (70) though over its try floor (5 at the scripted mage's Mana Conversion). Live, a fresh login's first cast at 15 mana was refused for mana. Now the pass dances first
        // (Stamina to Mana pressed until it lands), logs one line for it, and casts the first buff with the mana in hand: no blind try.
        foreach (var seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var t = new Rig(mana: 12, manaSeed: seed);
            Assert.True(t.Cycle.Start(t.Now));
            Assert.Equal(BuffStatus.Done, t.Run());
            var r = t.Cycle.Result!;
            Assert.Equal(24, r.Active);
            var answered = t.Caster.Answered;
            var presses = answered.TakeWhile(a => t.Caster.Table[a.Spell].MetaSpellType == DatReaderWriter.Enums.SpellType.Transfer).Count();
            Assert.True(presses >= 1, "the first thing sent is a Stamina to Mana press");
            var firstBuff = answered.First(x => t.Caster.Table[x.Spell].MetaSpellType == DatReaderWriter.Enums.SpellType.Enchantment); // the stamina's Revitalize (D88) may stand between the dance and the buff
            Assert.Equal(DatReaderWriter.Enums.SpellType.Enchantment, t.Caster.Table[firstBuff.Spell].MetaSpellType);
            Assert.Equal(firstBuff.Spell, t.Caster.Charged.First(c => t.Caster.Table[c.Spell].MetaSpellType == DatReaderWriter.Enums.SpellType.Enchantment).Spell);
            var danceLines = t.R.H.Log.Lines.Where(l => l.Contains("] pre-pass dance:", StringComparison.Ordinal)).ToList();
            var line = Assert.Single(danceLines);
            Assert.Contains("mana 12 -> ", line, StringComparison.Ordinal);
            Assert.Contains($", {presses} press(es) (", line, StringComparison.Ordinal);
            Assert.EndsWith(", landed, for " + t.Caster.Table[firstBuff.Spell].Name, line, StringComparison.Ordinal);
            var firstTry = t.R.H.Log.Lines.First(l => l.Contains("try 1 " + t.Caster.Table[firstBuff.Spell].Name, StringComparison.Ordinal));
            Assert.DoesNotContain("(a mana try)", firstTry, StringComparison.Ordinal);
            Assert.True(r.Dances >= 1);
        }
    }

    [Fact]
    public void M5_7_a_pass_that_starts_with_enough_mana_does_not_dance_first()
    {
        var t = new Rig();
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Equal(DatReaderWriter.Enums.SpellType.Enchantment, t.Caster.Table[t.Caster.Answered[0].Spell].MetaSpellType);
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("] pre-pass dance:", StringComparison.Ordinal));
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("Revitalize after the pre-pass dance", StringComparison.Ordinal)); // P7c: a later line's reserve dance may Revitalize after it, not the first
    }

    [Fact]
    public void M5_7_P7c_a_start_pass_keeps_the_next_presss_mana_so_no_Stamina_to_Mana_press_is_a_mana_try_and_the_pass_ends_with_it_paid()
    {
        // D139 row a (m5-start-pass-dance): round 13's start pass spent the mana to 42 and the first Stamina to Mana press after it, Meditative Trance at 42 of up to 70, was refused "not enough mana" (0x0401). Now a start's
        // pass keeps the reserve (the dearest payable press's base, 70) past every line: under a line's cost plus it, the pass dances while the press is sure, so no press is a mana try and none is refused, and the pass
        // ends with the reserve in hand. Before P7c a pass that started with 200 mana ran it down and danced from under the press's ceiling (a mana try, as the round's refused press).
        foreach (var seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var t = new Rig(mana: 200, manaSeed: seed);
            Assert.True(t.Cycle.Start(t.Now));
            Assert.Equal(BuffStatus.Done, t.Run());
            var r = t.Cycle.Result!;
            Assert.Equal(24, r.Active);
            var lines = t.R.H.Log.Lines.ToList();
            Assert.DoesNotContain(lines, l => l.Contains("mana dance Stamina to Mana: try ", StringComparison.Ordinal) && l.Contains("(a mana try)", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("] reserve: mana ", StringComparison.Ordinal) && l.Contains("kept for the next Stamina to Mana press): the dance first, while a press is sure", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("] reserve dance: mana ", StringComparison.Ordinal) && l.Contains(", landed, for ", StringComparison.Ordinal));
            var transfers = t.Caster.Answered.Where(a => t.Caster.Table[a.Spell].MetaSpellType == DatReaderWriter.Enums.SpellType.Transfer).ToList();
            Assert.NotEmpty(transfers);
            Assert.Equal(transfers.Count, r.DancePresses); // every press landed at once: none refused
            Assert.True(t.Caster.Mana >= 70, $"seed {seed}: the pass ended with {t.Caster.Mana} mana, under the 70 the next press may cost");
        }
    }

    [Fact]
    public void M5_7_P7c_a_re_buff_pass_keeps_no_reserve()
    {
        // The reserve is a start's pass's (the autopilot's buff station, a person's start): a re-buff pass (the fight's refresh, the upkeep) looks only before its first line, as before.
        var t = new Rig();
        Assert.True(t.Cycle.StartRebuff(t.Now, forced: false));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("] reserve", StringComparison.Ordinal));
    }

    [Fact]
    public void M5_7_the_pre_pass_dance_is_followed_by_Revitalize_when_the_stamina_is_under_the_share()
    {
        var t = new Rig(mana: 12);
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var lines = t.R.H.Log.Lines.ToList();
        var dance = lines.FindIndex(l => l.Contains("] pre-pass dance:", StringComparison.Ordinal));
        var revive = lines.FindIndex(l => l.Contains("Revitalize after the pre-pass dance", StringComparison.Ordinal));
        Assert.True(dance >= 0 && revive > dance, "the Revitalize follows the dance's line");
        Assert.True(t.Cycle.Result!.Revitalizes >= 1);
        Assert.True(t.Cycle.Result.Revitalizes <= BuffCycle.PassRevitalizeLimit);
    }

    [Fact]
    public void A_mana_try_inside_a_pass_still_lands_when_no_dance_can_be_pressed_with_every_try_counted_K5()
    {
        // Mana 12 and no stamina to transfer: the pass cannot dance, so (as before M5 7) the first line is tried at once from its try floor; a 0x0401 inside the tries is expected. Over a few seeds of the scripted rolls at
        // least one first line lands after a refusal; every run ends with every buff active.
        var refusedThenLanded = 0;
        foreach (var seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var t = new Rig(mana: 12, stamina: 0, manaSeed: seed);
            Assert.True(t.Cycle.Start(t.Now));
            Assert.Equal(BuffStatus.Done, t.Run());
            var r = t.Cycle.Result!;
            Assert.Equal(24, r.Active);
            Assert.True(r.ManaTries >= 1, r.CastFields);
            Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("] pre-pass dance:", StringComparison.Ordinal));
            var lead = t.Caster.Answered.TakeWhile(a => a.Spell == t.Caster.Answered[0].Spell && !a.Fizzled).Count();
            if (lead <= 1)
            {
                continue;
            }

            refusedThenLanded++;
            Assert.Contains(t.R.H.Log.Lines, l => l.Contains("(a mana try)", StringComparison.Ordinal));
            Assert.True(r.ManaRefusals >= 1, r.CastFields);
        }

        Assert.True(refusedThenLanded >= 1, "no seed refused a first try");
    }

    [Fact]
    public void Under_the_floor_the_dance_presses_Stamina_to_Mana_until_it_lands_and_every_press_is_counted_K5()
    {
        // Mana 2: under every try floor (5), so the pass dances first. Each press is a cast the scripted ACE answers (0x0401 with nothing charged, or the landing).
        var seen = 0;
        foreach (var seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
        {
            var t = new Rig(mana: 2, manaSeed: seed);
            Assert.True(t.Cycle.Start(t.Now));
            Assert.Equal(BuffStatus.Done, t.Run());
            var r = t.Cycle.Result!;
            Assert.Equal(24, r.Active);
            Assert.True(r.Dances >= 1);
            Assert.True(r.DancesLanded >= 1);
            Assert.True(r.DancePresses >= r.Dances);
            Assert.Contains($" mana_presses={r.DancePresses} dances={r.Dances} dances_landed={r.DancesLanded}", r.CastFields, StringComparison.Ordinal);

            // The first dance: its presses are the transfer answers before the first buff answer, the last of them the landing.
            var presses = t.Caster.Answered.TakeWhile(a => t.Caster.Table[a.Spell].MetaSpellType == DatReaderWriter.Enums.SpellType.Transfer).Count();
            Assert.InRange(presses, 1, Lifestone.Client.Session.Vitals.ManaOrderOptions.DefaultDancePresses);
            var line = t.R.H.Log.Lines.First(l => l.Contains("mana: the dance landed on press", StringComparison.Ordinal));
            Assert.Contains($"on press {presses} (", line, StringComparison.Ordinal);
            if (presses > 1)
            {
                seen++;
            }
        }

        Assert.True(seen >= 1, "no seed refused a press before the landing");
    }

    [Fact]
    public void A_landed_press_of_the_dance_is_proved_by_the_transfer_chat_line_and_counted_in_proved_and_mana_landed_M5_1_fix_B_G5()
    {
        // ACE's transfer on oneself says "You cast <spell> on yourself and lose N points of stamina and also gain M points of mana" (Magic chat, 0x07; WorldObject_Magic.cs:835 @95ef426d), which CastChatProof reads as Proved.
        // The scripted caster writes that line with the transfer's UseDone 0, so a landing ends Landed (not Unproven) and counts as a proved cast and as a landed Mana Conversion try.
        foreach (var seed in new[] { 1, 2, 3 })
        {
            var t = new Rig(mana: 2, manaSeed: seed);
            Assert.True(t.Cycle.Start(t.Now));
            Assert.Equal(BuffStatus.Done, t.Run());
            var r = t.Cycle.Result!;
            Assert.True(r.DancesLanded >= 1);
            Assert.True(r.Proved >= r.DancesLanded, $"proved {r.Proved} dances landed {r.DancesLanded}: {r.CastFields}");
            var sure = t.R.H.Log.Lines.Count(l => l.Contains("] reserve dance: ", StringComparison.Ordinal) && l.Contains(", landed, ", StringComparison.Ordinal));
            Assert.True(r.ManaLanded >= r.DancesLanded - sure, r.CastFields); // P7c: a start pass's reserve dance is pressed at or over its ceiling, so it is no Mana Conversion try
            Assert.Contains($" proved={r.Proved}", r.CastFields, StringComparison.Ordinal);
            Assert.Contains(t.R.H.Log.Lines, l => l.Contains("mana dance Stamina to Mana: landed", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_pass_whose_mana_is_spent_dances_instead_of_todays_regeneration_wait_K5()
    {
        // Mana 5 and no regeneration: today's ceiling order waits and the wait runs out (the pinned The_regeneration_wait_is_named_and_bounded_...); the mana order dances and the pass ends with every buff.
        var ceiling = new Rig(mana: 5, options: new BuffOptions { ManaWaitSeconds = 30, Mana = Lifestone.Client.Session.Vitals.ManaOrderOptions.Ceiling });
        ceiling.Caster.ManaRegenPerSecond = 0;
        Assert.True(ceiling.Cycle.Start(ceiling.Now));
        Assert.Equal(BuffStatus.Failed, ceiling.Run(maxSeconds: 120, dt: 0.5));

        var t = new Rig(mana: 5, options: new BuffOptions { ManaWaitSeconds = 30 });
        t.Caster.ManaRegenPerSecond = 0;
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var r = t.Cycle.Result!;
        Assert.Equal(24, r.Active);
        Assert.Empty(r.Misses);
        Assert.True(r.Dances >= 1);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("] pre-pass dance:", StringComparison.Ordinal)); // M5 7: the pass starts with 5 mana, so it dances first
        Assert.True(r.Revitalizes <= BuffCycle.PassRevitalizeLimit); // Revitalize is not cast for mana: only the pass's refill after the pre-pass dance (M5 7, D88)
        Assert.DoesNotContain(t.R.H.Log.Lines, l => l.Contains("the mana wait ran out", StringComparison.Ordinal));
        Assert.EndsWith(r.CastFields, t.R.H.Log.Lines.Last(l => l.Contains("pass done: ", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_mana_orders_wait_allocates_nothing_across_its_looks_K5()
    {
        // No mana at all and no regeneration: no press can land, so the order waits (its own look each second); the wait reads, sends nothing and allocates nothing.
        var w = new Rig(mana: 0);
        w.Caster.ManaRegenPerSecond = 0;
        Assert.True(w.Cycle.Start(w.Now));
        for (var i = 0; i < 12 && w.Cycle.Stage != BuffCycle.StageManaWait; i++)
        {
            Assert.Equal(BuffStatus.Running, w.Frame(0.25));
        }

        Assert.Equal(BuffCycle.StageManaWait, w.Cycle.Stage);
        var now = w.Now;
        w.Cycle.Step(now + 1.5);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 300; i++)
        {
            w.Cycle.Step(now + 1.5 + (i * 0.01));
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(BuffCycle.StageManaWait, w.Cycle.Stage);
        Assert.Empty(w.Caster.Answered);
    }

    [Fact]
    public void A_buff_line_refused_for_mana_at_its_ceiling_after_its_tries_fails_the_cycle_named_and_is_not_sent_again_1_R14()
    {
        // M5 3 m4 (m5-buffcycle-ceiling-refusal, review 1-R14): the read model shows 426 mana, which covers every ceiling, while the scripted server holds 0 and says nothing (no regeneration, no vital update), so every cast is refused 0x0401
        // though the mana shown is over the ceiling. The first attempt is sent from the try floor, its tries are spent, the mana step finds the ceiling covered and sends once more at the ceiling; that refusal is a named failure,
        // not a resend for as long as the two disagree. The bound is the cycle's own: two attempts, each at most the tries (the default 8), no more.
        var t = new Rig(mana: 426);
        t.Caster.ManaRegenPerSecond = 0;
        t.Caster.Mana = 0; // the server's mana, changed without a word to the session
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Failed, t.Run(maxSeconds: 600));
        Assert.Equal(BuffCycle.StageFailed, t.Cycle.Stage);
        var r = t.Cycle.Result!;
        Assert.Contains("refused for mana (0x0401) again at its ceiling of", r.Failure, StringComparison.Ordinal);
        Assert.Contains("the read model's mana is not the server's", r.Failure, StringComparison.Ordinal);
        Assert.InRange(t.Caster.Answered.Count, 2, 2 * Lifestone.Client.Session.Vitals.ManaOrderOptions.DefaultTries);
        Assert.All(t.Caster.Answered, a => Assert.False(a.Fizzled));
        Assert.Empty(t.Caster.Charged); // nothing was charged: every answer was a refusal
        Assert.Equal(1, BuffCycle.CeilingAttemptsPerLine);
    }

    /// <summary>The lines the soak's fight leans on (M5 7 F12, D90 point 3): the war mastery and the other masteries, the three regenerations, the protection and the bane that match the tuskers' bludgeoning (the Tusker Slave 1628 and Guard 1629 rows of the pinned world SQL v0.9.296 hit with damage type 4, bludgeoning, on every body part, and cast no spell), the armour lines and the wand's two.</summary>
    private static readonly string[] FightKeyLines =
    [
        "War Magic Mastery Self", "Creature Enchantment Mastery Self", "Item Enchantment Mastery Self", "Life Magic Mastery Self", "Mana Mastery Self", "Focus Self", "Willpower Self",
        "Regeneration Self", "Rejuvenation Self", "Mana Renewal Self", "Bludgeon Protection Self", "Bludgeon Bane", "Armor Self", "Impenetrability", "Hermetic Link", "Defender",
    ];

    [Fact]
    public void The_cycle_casts_every_line_the_soaks_fight_leans_on_at_a_level_that_lasts_the_hour()
    {
        var t = new Rig();
        t.Caster.ManaRegenPerSecond = 0.2;
        t.Caster.CastSeconds = 1.85;
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run(maxSeconds: 20000));
        var casts = t.BuffCasts();
        Assert.All(FightKeyLines, key => Assert.Contains(casts, c => c.Line == key));
        Assert.Equal(24, t.Cycle.Result!.Active);
        Assert.True(t.Cycle.MinRemainingSeconds(t.Now) > BuffOptions.DefaultRebuffMarginSeconds, "the first cycle leaves every line far over the margin");
    }

    [Fact]
    public void A_re_buff_pass_that_starts_late_by_the_upkeeps_latency_at_the_mana_floor_recasts_every_line_before_one_lapses_and_a_short_margin_would_not_F12()
    {
        // The upkeep (AutopilotScenario.UpkeepDue) asks the fight to stop when a line is under the margin, checked every UpkeepCheckSeconds (5 s); the fight ends at its next safe point (a kill), the buff station waits for 60% mana, then
        // the pass casts the lines under the margin. Model that latency as 125 s (5 s check + 120 s safe point, walk and mana) with the mana at the floor (D88: the fight runs mana down), at the live economy of the test above
        // (a cycle of about a minute). A line lapses when its time left reaches 0 before its recast, which RebuffDue(now, ~0) lists (a line with no active buff has none left).
        const double Latency = 125;
        foreach (var (margin, lapses) in new[] { (BuffOptions.DefaultRebuffMarginSeconds, false), (30.0, true) })
        {
            var t = new Rig(options: new BuffOptions { RebuffMarginSeconds = margin });
            t.Caster.ManaRegenPerSecond = 0.2;
            t.Caster.CastSeconds = 1.85;
            Assert.True(t.Cycle.Start(t.Now));
            Assert.Equal(BuffStatus.Done, t.Run(maxSeconds: 20000));
            var first = t.Cycle.MinRemainingSeconds(t.Now);
            t.R.Tick(first - margin + Latency); // the first line went under the margin Latency seconds ago
            t.Caster.Pump(); // applies the regeneration of the skipped time, so the floor below is not refilled
            t.Caster.SetVitals(15, 275);
            Assert.True(t.Cycle.RebuffDue(t.Now, margin).Count > 0, "the first line is under the margin when the pass starts");
            var lapsed = t.Cycle.RebuffDue(t.Now, 0.001).Count > 0;
            Assert.True(t.Cycle.StartRebuff(t.Now, forced: false));
            var end = t.Now + 20000;
            var status = BuffStatus.Running;
            while (status == BuffStatus.Running && t.Now < end)
            {
                status = t.Frame(0.25);
                lapsed |= t.Cycle.RebuffDue(t.Now, 0.001).Count > 0;
            }

            Assert.Equal(BuffStatus.Done, status);
            Assert.Equal(lapses, lapsed);
            output.WriteLine($"margin {margin:0} s, latency {Latency:0} s: lapsed={lapsed}, pass cast {t.Cycle.Result!.Cast} lines, {t.Cycle.Result.Fields}");
            if (!lapses)
            {
                Assert.Equal(24, t.Cycle.Result!.Active);
                Assert.True(t.Cycle.MinRemainingSeconds(t.Now) > 1000, "every line is a fresh cast, or far from its end");
            }
        }
    }
    [Fact]
    public void A_line_learned_by_play_is_skipped_while_the_spellbook_lacks_it_and_kept_up_once_it_holds_a_level_M5_7_P7j()
    {
        // M5 7 P7j (D143): Spirit Drinker is a profile line the build never teaches. The spellbook (the state) lacks it: the cycle wants 24 lines, casts none of 3253-3258, records no miss for it, and says why once.
        var t = new Rig(options: CeilingOrder);
        var spirit = ArenaProfile.LineNamed("Spirit Drinker")!;
        Assert.True(spirit.ByPlay);
        Assert.True(t.Cycle.Start(t.Now));
        Assert.Equal(BuffStatus.Done, t.Run());
        var r = t.Cycle.Result!;
        Assert.Equal(24, r.Wanted);
        Assert.Equal(24, t.Cycle.Wanted);
        Assert.DoesNotContain(spirit.Name, t.Cycle.LineOrder);
        Assert.DoesNotContain(t.Caster.Answered, a => spirit.Spells.Any(s => s.Id == a.Spell));
        Assert.DoesNotContain(r.Misses, m => m.StartsWith("Spirit:", StringComparison.Ordinal));
        Assert.DoesNotContain(spirit.Name, t.Cycle.RebuffDue(t.Now, double.PositiveInfinity)); // never due while unknown: the re-buff driver starts no pass for it
        var notWanted = "Spirit Drinker: no level is in the spellbook; a line learned by play (D143), so it is not wanted until a level is learned";
        Assert.Single(t.R.H.Log.Entries, e => e.Message == notWanted); // logged once, not each frame or pass

        // The scroll is read: ACE's MagicUpdateSpell adds IV to the spellbook. The line is wanted from now on, due at once (nothing active), and a re-buff pass casts IV, the only level the book holds.
        t.R.Raw(Lifestone.Tests.Shared.Net.AceM3Writers.MagicUpdateSpell(WorldActionRig.Player, 90, 3256));
        Assert.True(t.R.H.State.Self.Spellbook.Knows(3256));
        Assert.Contains(spirit.Name, t.Cycle.RebuffDue(t.Now, t.Options.RebuffMarginSeconds));
        Assert.Equal(25, t.Cycle.Wanted);
        var before = t.Caster.Answered.Count;
        Assert.True(t.Cycle.StartRebuff(t.Now, forced: false));
        Assert.Equal(BuffStatus.Done, t.Run());
        Assert.Contains(3256u, t.Caster.Landed.Skip(before));
        Assert.DoesNotContain(t.Caster.Answered.Skip(before), a => a.Spell is 3253 or 3254 or 3255 or 3257 or 3258); // only the level the spellbook holds
        Assert.Contains("Spirit:IV", t.Cycle.Result!.Levels, StringComparison.Ordinal);
        Assert.Equal(25, t.Cycle.Result.Wanted);
        Assert.Single(t.R.H.Log.Entries, e => e.Message == "Spirit Drinker: Aura of Spirit Drinker Self IV (3256) is in the spellbook now: the line is wanted and kept up as the other auras (D143)");

        // Kept up: near its end the line is due again (the margin), as Hermetic Link and Defender are.
        Assert.DoesNotContain(spirit.Name, t.Cycle.RebuffDue(t.Now, t.Options.RebuffMarginSeconds));
        Assert.Contains(spirit.Name, t.Cycle.RebuffDue(t.Now + 1800 - (t.Options.RebuffMarginSeconds / 2), t.Options.RebuffMarginSeconds));
    }
}
