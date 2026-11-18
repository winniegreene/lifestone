using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 1 g (<c>m6-account-formula-plan</c>, <c>m6-outskirts-account-formula</c>): a school the character trained later has no focus (docs/session.md section 64), so its casts burn the account's full formula, not the foci formula
/// (the level's scarab and Prismatic Tapers). The helper, the level floor and the component floor plan on it; with the focus held nothing changes. Also the recovery stagger (<c>m6-buff-recovery-batch</c>) and the long run's constants.
/// A made-up account name; the formula is the player-castable layout (scarab, accent taper, herb, accent taper, powder, potion, accent taper, talisman).
/// </summary>
public sealed class AccountFormulaTests
{
    private const string Account = "formula-test-account";
    private const uint PyrealScarabId = 6;
    private const uint HerbId = 7;
    private const uint PowderId = 25;
    private const uint PotionId = 37;
    private const uint TalismanId = 49;
    private const uint CreatureFocus = 15268;
    private const uint WarFocus = 15271;

    private static uint WcidOf(uint component) => ComponentWeenies.TryGetByComponent(component, out var w) ? w.Wcid : throw new InvalidOperationException("component " + component);

    /// <summary>A Creature Enchantment spell of the player-castable layout (version 1).</summary>
    private static SpellInfo Creature() =>
        Lifestone.Client.Tests.Session.WorldActionRig.Spell(1000, SpellType.Enchantment) with
        {
            School = MagicSchool.CreatureEnchantment,
            Formula = [PyrealScarabId, 63, HerbId, 63, PowderId, PotionId, 63, TalismanId],
            FormulaVersion = 1,
        };

    /// <summary>A pack that holds every component of the account's full formula (and none of the foci formula's Prismatic Taper).</summary>
    private static Dictionary<uint, int> PackOfAccount() =>
        AccountFormula.Counts(Creature(), false, Account).ToDictionary(c => WcidOf(c.Key), c => c.Value);

    [Fact]
    public void With_the_focus_held_the_foci_formula_and_without_it_the_accounts_full_formula()
    {
        var foci = AccountFormula.Counts(Creature(), true, Account);
        Assert.Equal(GearKit.FociFormulaCounts(Creature().Formula), foci);
        Assert.Equal(new Dictionary<uint, int> { [PyrealScarabId] = 1, [SpellFormulaRules.PrismaticTaperId] = 4 }, foci); // the Pyreal Scarab is a power 6 scarab: four Prismatic Tapers

        var full = AccountFormula.Counts(Creature(), false, Account);
        Assert.Equal(1, full[PyrealScarabId]);
        Assert.Equal(1, full[HerbId]);
        Assert.Equal(1, full[PowderId]);
        Assert.Equal(1, full[PotionId]);
        Assert.Equal(1, full[TalismanId]);
        Assert.DoesNotContain(SpellFormulaRules.PrismaticTaperId, full.Keys); // the Prismatic Taper is the foci formula's alone (ACE SpellFormula.cs:378)
        Assert.Equal(8, full.Values.Sum()); // the three accent slots are the account's coloured tapers (ids 63 to 74)
        Assert.All(full.Keys.Except([PyrealScarabId, HerbId, PowderId, PotionId, TalismanId]), id => Assert.InRange(id, SpellFormulaRules.LowestTaperId, SpellFormulaRules.LowestTaperId + 11));
    }

    [Fact]
    public void The_account_is_lower_cased_as_the_server_stores_it_and_a_missing_one_is_a_loud_stop()
    {
        Assert.Equal(AccountFormula.Counts(Creature(), false, Account), AccountFormula.Counts(Creature(), false, Account.ToUpperInvariant()));
        Assert.Throws<ArgumentException>(() => AccountFormula.Counts(Creature(), false, null));
        Assert.Throws<ArgumentException>(() => AccountFormula.Counts(Creature(), false, string.Empty));
        Assert.Equal(GearKit.FociFormulaCounts(Creature().Formula), AccountFormula.Counts(Creature(), true, null)); // the foci formula reads no account
    }

    [Fact]
    public void A_pack_holds_a_focus_only_with_the_schools_item()
    {
        Assert.True(AccountFormula.HoldsFocus(new Dictionary<uint, int> { [CreatureFocus] = 1 }, MagicSchool.CreatureEnchantment));
        Assert.False(AccountFormula.HoldsFocus(new Dictionary<uint, int> { [WarFocus] = 1 }, MagicSchool.CreatureEnchantment)); // the War focus is not the Creature's
        Assert.False(AccountFormula.HoldsFocus(new Dictionary<uint, int> { [CreatureFocus] = 0 }, MagicSchool.CreatureEnchantment));
        Assert.False(AccountFormula.HoldsFocus(new Dictionary<uint, int> { [CreatureFocus] = 1 }, MagicSchool.None)); // no school item at all
        Assert.Equal(AccountFormula.Counts(Creature(), new Dictionary<uint, int> { [CreatureFocus] = 1 }, Account), AccountFormula.Counts(Creature(), true, Account));
        Assert.Equal(AccountFormula.Counts(Creature(), new Dictionary<uint, int> { [WarFocus] = 1 }, Account), AccountFormula.Counts(Creature(), false, Account));
    }

    [Fact]
    public void The_level_floor_plans_a_Creature_line_without_its_focus_on_the_accounts_scarab_herbs_powders_potions_and_talisman_M6_account_formula_plan()
    {
        // The pack of the foci formula's kit: the Pyreal Scarab and Prismatic Tapers, no Creature focus.
        var fociKit = new Dictionary<uint, int> { [WcidOf(PyrealScarabId)] = 100, [WcidOf(SpellFormulaRules.PrismaticTaperId)] = 1000 };
        Assert.False(LevelFloor.ShortOf(fociKit, Creature(), out _)); // today's rule (no account): the foci formula, paid
        Assert.True(LevelFloor.ShortOf(fociKit, Creature(), out var need, Account)); // the account's formula needs the herb, powder, potion and talisman too
        Assert.Contains("which the pack does not hold", need, StringComparison.Ordinal);

        // Each component of the account's formula is counted, one at a time.
        foreach (var component in new[] { PyrealScarabId, HerbId, PowderId, PotionId, TalismanId })
        {
            var pack = PackOfAccount();
            Assert.False(LevelFloor.ShortOf(pack, Creature(), out var whole, Account), whole);
            pack.Remove(WcidOf(component));
            Assert.True(LevelFloor.ShortOf(pack, Creature(), out var named, Account));
            Assert.Contains(WcidOf(component).ToString(CultureInfo.InvariantCulture), named, StringComparison.Ordinal);
        }

        // With the Creature focus in the pack the foci formula applies whatever the account is.
        var withFocus = new Dictionary<uint, int>(fociKit) { [CreatureFocus] = 1 };
        Assert.False(LevelFloor.ShortOf(withFocus, Creature(), out var none, Account), none);
    }

    [Fact]
    public void The_component_floor_reads_the_accounts_formula_for_a_school_without_its_focus_M6_outskirts_account_formula()
    {
        var ground = GroundCatalog.Get("black-spawn-den-aluvian");
        var table = LogisticsCountersTests.FloorTable();
        var defaults = ComponentFloor.FloorOnly(ground, table);
        Assert.Equal([7299u], defaults.Keys); // the Diamond Scarab, the ring's alone (the foci formula)

        // The same table on the account's formula with no focus held, and with every focus held: the scarab slot is the base formula's either way, so the floor-only line is the same.
        Assert.Equal(defaults.Keys, ComponentFloor.FloorOnly(ground, table, Account).Keys);
        Assert.Equal(defaults.Keys, ComponentFloor.FloorOnly(ground, table, Account, _ => true).Keys);

        // A spell whose Diamond Scarab (110) sits in a taper slot of the base formula (slot 1) is rewritten away by the account's formula: the foci formula still counts it, the account's does not.
        var odd = table.ToDictionary(p => p.Key, p => p.Value);
        odd[1785] = odd[1785] with { Formula = [PyrealScarabId, 110, HerbId, 63, PowderId, PotionId, 63, TalismanId], FormulaVersion = 1, School = MagicSchool.CreatureEnchantment };
        Assert.Contains(7299u, ComponentFloor.FloorOnly(ground, odd, Account, _ => true).Keys); // a held focus: the foci formula takes the scarabs of the base formula, the Diamond Scarab among them
        Assert.DoesNotContain(7299u, ComponentFloor.FloorOnly(ground, odd, Account, _ => false).Keys); // none held: slot 1 is rewritten to a coloured taper
    }

    [Fact]
    public void The_constants_carry_the_long_runs_measures()
    {
        Assert.Equal(2.0, FightScenario.BatchLineSeconds); // the batch line 1.94 s median, p90 2.13, max 2.40
        Assert.Equal(45, FightScenario.LeaveRefreshBoundSeconds); // the worst measured leave 17.7 s
        Assert.Equal(85, Lifestone.Client.Session.Autopilot.AutopilotScenario.TripSeedSeconds); // the sell trips 77 to 130 s, median 83 s
    }

    private static List<BuffCycle.LineState> Lines(int count, double left, Func<int, double>? skew = null) =>
        [.. ArenaProfile.Lines.Where(l => l.IsBuff).Take(count).Select((l, i) => new BuffCycle.LineState(l, true, left + (skew?.Invoke(i) ?? 0)))];

    private static int BuffLines => ArenaProfile.Lines.Count(l => l.IsBuff);

    [Fact]
    public void Lines_read_with_the_same_time_left_fall_due_spread_over_the_stagger_and_not_at_once_M6_buff_recovery_batch()
    {
        var count = Math.Min(25, BuffLines);
        var states = Lines(count, 600);
        Assert.True(count >= FightScenario.StaggerMinLines);
        var memory = new Dictionary<string, (double LastLeft, double Offset)>(StringComparer.Ordinal);
        var step = FightScenario.BatchLineSeconds;

        Assert.Equal(count, FightScenario.StaggerSameAge(states, memory, step));
        var left = states.Select(s => s.Remaining).ToList();
        Assert.Equal(count, left.Distinct().Count()); // not all at once
        Assert.Equal(600 - ((count - 1) * step), left.Min(), 6); // the first recast is the most urgent
        Assert.Equal(600, left.Max(), 6);
        var ordered = left.Order().ToList();
        for (var i = 1; i < count; i++)
        {
            Assert.Equal(step, ordered[i] - ordered[i - 1], 6); // one line's pass apart
        }

        // The recast order is kept: protections first, then the cycle's own order (the stable sort of the groups), the most urgent first.
        var byUrgency = states.OrderBy(s => s.Remaining).Select(s => s.Line.Name).ToList();
        var recastOrder = ArenaProfile.Lines.Where(l => l.IsBuff).Take(count).OrderBy(ArenaProfile.RefreshGroupOf).Select(l => l.Name).ToList();
        Assert.Equal(recastOrder, byUrgency);

        // The lines fall due one after another as the threshold rises: the k-th line is under 600 - (count - k) steps.
        for (var k = 1; k <= count; k++)
        {
            var threshold = 600 - ((count - k) * step) + 0.001;
            Assert.Equal(k, left.Count(r => r < threshold));
        }
    }

    [Fact]
    public void The_stagger_keeps_its_offsets_while_the_lines_count_down_drops_a_recast_line_and_leaves_an_ordinary_batch_alone()
    {
        var count = Math.Min(25, BuffLines);
        var memory = new Dictionary<string, (double LastLeft, double Offset)>(StringComparer.Ordinal);
        var first = Lines(count, 600);
        FightScenario.StaggerSameAge(first, memory, 2.0);
        var firstName = first.OrderBy(s => s.Remaining).First().Line.Name;

        // 10 s later every line reads 590 s: the same offsets apply, nothing new is staggered.
        var later = Lines(count, 590);
        Assert.Equal(0, FightScenario.StaggerSameAge(later, memory, 2.0));
        Assert.Equal(590 - ((count - 1) * 2.0), later.Min(s => s.Remaining), 6);

        // The first line is recast (it reads 3,600 s): it keeps its real time left, the others their offsets.
        var recast = Lines(count, 580).Select(s => s.Line.Name == firstName ? s with { Remaining = 3600 } : s).ToList();
        Assert.Equal(0, FightScenario.StaggerSameAge(recast, memory, 2.0));
        Assert.Equal(3600, recast.First(s => s.Line.Name == firstName).Remaining);
        Assert.DoesNotContain(firstName, memory.Keys);
        Assert.Equal(count - 1, memory.Count);

        // Lines cast one after another (the ordinary full pass, 2.5 s apart) are no group, nor are a handful of lines.
        var spread = Lines(count, 600, i => i * 2.5);
        Assert.Equal(0, FightScenario.StaggerSameAge(spread, new Dictionary<string, (double LastLeft, double Offset)>(StringComparer.Ordinal), 2.0));
        Assert.Equal(600, spread[0].Remaining);
        var few = Lines(FightScenario.StaggerMinLines - 1, 600);
        Assert.Equal(0, FightScenario.StaggerSameAge(few, new Dictionary<string, (double LastLeft, double Offset)>(StringComparer.Ordinal), 2.0));
    }
}
