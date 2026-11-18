using DatReaderWriter.Enums;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 6 E (<c>m6-fight-back-war-spell</c>): the one bolt pick of the run leg's fight-back and the quest steps' kills (<see cref="WarBoltPick.Choose"/>): the highest power whose chance on the War Magic skill reaches the floor, else the lowest; ties to the
/// lower id; only the War Magic projectiles the book knows and the table holds; none is null.
/// </summary>
public sealed class WarBoltPickTests
{
    private static SpellInfo Bolt(uint id, uint power, MagicSchool school = MagicSchool.WarMagic, SpellType meta = SpellType.Projectile) =>
        WorldActionRig.Spell(id, meta) with { Name = "Bolt " + id, School = school, Power = power };

    private static Dictionary<uint, SpellInfo> Table(params SpellInfo[] spells) => spells.ToDictionary(s => s.Id);

    private static readonly Dictionary<uint, SpellInfo> Line = Table(Bolt(101, 1), Bolt(102, 50), Bolt(103, 100), Bolt(104, 150));

    [Fact]
    public void The_highest_power_whose_chance_reaches_the_floor_is_picked()
    {
        Assert.True(GearCasts.Chance(120, 100) >= 0.5); // the rule's inputs: level 3 passes on 120, level 4 does not
        Assert.True(GearCasts.Chance(120, 150) < 0.5);
        var pick = WarBoltPick.Choose(Line, [101, 102, 103, 104], 120, WarBoltPick.DefaultMinChance, out var why);
        Assert.Equal(103u, pick?.Id);
        Assert.StartsWith("power 100, chance ", why, StringComparison.Ordinal);
        Assert.EndsWith(" on War Magic 120", why, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_known_war_projectiles_in_the_table_count()
    {
        var table = Table(Bolt(101, 1), Bolt(103, 100), Bolt(201, 300, MagicSchool.LifeMagic), Bolt(202, 300, meta: SpellType.Enchantment));
        var pick = WarBoltPick.Choose(table, [101, 201, 202, 999], 400, WarBoltPick.DefaultMinChance, out _); // 103 is not known, 999 is in no table
        Assert.Equal(101u, pick?.Id);
    }

    [Fact]
    public void Equal_powers_go_to_the_lower_id_both_for_the_best_and_for_the_lowest()
    {
        var table = Table(Bolt(310, 100), Bolt(300, 100), Bolt(320, 100));
        Assert.Equal(300u, WarBoltPick.Choose(table, [320, 310, 300], 200, 0.5, out _)?.Id); // all reach the floor
        Assert.Equal(300u, WarBoltPick.Choose(table, [320, 310, 300], 10, 0.5, out var why)?.Id); // none does: the lowest
        Assert.StartsWith("the lowest power: no bolt reaches 0.50", why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_book_with_no_war_projectile_picks_nothing()
    {
        var table = Table(Bolt(201, 50, MagicSchool.LifeMagic));
        Assert.Null(WarBoltPick.Choose(table, [201], 300, 0.5, out var why));
        Assert.Equal("none", why);
        Assert.Null(WarBoltPick.Choose(Line, [], 300, 0.5, out _));
    }

    [Fact]
    public void A_zero_skill_reaches_no_chance_and_takes_the_lowest_power()
    {
        Assert.Equal(0.0, GearCasts.Chance(0, 1)); // ACE: a skill of 0 never passes the pre-check
        var pick = WarBoltPick.Choose(Line, [104, 103, 102, 101], 0, WarBoltPick.DefaultMinChance, out var why);
        Assert.Equal(101u, pick?.Id);
        Assert.Equal("the lowest power: no bolt reaches 0.50 on War Magic 0", why);
    }

    [Fact]
    public void An_unread_skill_takes_the_lowest_power_and_says_so()
    {
        var pick = WarBoltPick.Choose(Line, [104, 102], null, WarBoltPick.DefaultMinChance, out var why);
        Assert.Equal(102u, pick?.Id);
        Assert.Equal("the lowest power: the War Magic skill is not read", why);
    }

    // ------------------------------------------------------------------------------------------------ M6 6 R1F: the castable pick (the pack's components, ACE's refusals)

    /// <summary>A bolt of power <paramref name="power"/> whose base formula is the one scarab <paramref name="scarab"/> (component id 1 Lead to 5 Gold).</summary>
    private static SpellInfo Scarabbed(uint id, uint power, uint scarab) => Bolt(id, power) with { Formula = [scarab, 0, 0, 0, 0, 0, 0, 0] };

    /// <summary>A pack (by weenie class) that pays for each of <paramref name="paid"/> on the foci formula, and the War Magic focus.</summary>
    private static Dictionary<uint, int> PackFor(params SpellInfo[] paid)
    {
        var held = new Dictionary<uint, int> { [LevelFloor.FocusOf(MagicSchool.WarMagic)] = 1 };
        foreach (var info in paid)
        {
            foreach (var (component, n) in GearKit.FociFormulaCounts(info.Formula))
            {
                Assert.True(Lifestone.Data.Magic.ComponentWeenies.TryGetByComponent(component, out var weenie));
                held[weenie.Wcid] = Math.Max(held.GetValueOrDefault(weenie.Wcid), n);
            }
        }

        return held;
    }

    private static readonly Dictionary<uint, SpellInfo> Scarabs = Table(Scarabbed(81, 50, 1), Scarabbed(82, 100, 3), Scarabbed(83, 150, 4));

    /// <summary>M6 6 R1F (live round 1: Flame Bolt IV refused 0x0400 four times): the pack's check before drops a bolt it is short of, the rule picks among the rest, and the line names what was skipped.</summary>
    [Fact]
    public void A_bolt_the_pack_is_short_of_is_skipped_and_named_and_the_rule_picks_among_the_rest_R1F()
    {
        var pack = PackFor(Scarabs[81], Scarabs[82]); // Lead and Copper Scarabs, no Silver
        var pick = WarBoltPick.ChooseCastable(Scarabs, [81, 82, 83], 400, WarBoltPick.DefaultMinChance, pack, null, new HashSet<uint>(), out var why, out var lack);
        Assert.Equal(82u, pick?.Id); // 83 would be the pick on 400: its Silver Scarab is not held
        Assert.Equal(BoltLack.None, lack);
        Assert.Contains("skipped: Bolt 83 needs Silver Scarab 688, which the pack does not hold", why, StringComparison.Ordinal);
        Assert.DoesNotContain("not judged", why, StringComparison.Ordinal);
    }

    /// <summary>M6 6 R1F: a bolt ACE refused for a component on this leg is dropped (the next pick follows); every bolt refused or short is <see cref="BoltLack.NoComponents"/>, a book without a bolt stays <see cref="BoltLack.NoWarSpell"/>.</summary>
    [Fact]
    public void A_refused_bolt_is_dropped_and_none_left_is_no_components_not_no_war_spell_R1F()
    {
        var refused = new HashSet<uint> { 83 };
        Assert.Equal(82u, WarBoltPick.ChooseCastable(Scarabs, [81, 82, 83], 400, 0.5, null, null, refused, out var why, out _)?.Id);
        Assert.Contains("skipped: Bolt 83 was refused for a missing component on this leg", why, StringComparison.Ordinal);

        refused.UnionWith([81, 82]);
        Assert.Null(WarBoltPick.ChooseCastable(Scarabs, [81, 82, 83], 400, 0.5, null, null, refused, out why, out var lack));
        Assert.Equal(BoltLack.NoComponents, lack);
        Assert.StartsWith("no-components: Bolt 81 was refused", why, StringComparison.Ordinal);

        Assert.Null(WarBoltPick.ChooseCastable(Scarabs, [81, 82], 400, 0.5, PackFor(), null, new HashSet<uint>(), out why, out lack)); // the pack holds the focus and no scarab
        Assert.Equal(BoltLack.NoComponents, lack);
        Assert.Contains("Bolt 81 needs Lead Scarab 691, which the pack does not hold", why, StringComparison.Ordinal);

        Assert.Null(WarBoltPick.ChooseCastable(Table(Bolt(201, 50, MagicSchool.LifeMagic)), [201], 400, 0.5, null, null, refused, out why, out lack));
        Assert.Equal((BoltLack.NoWarSpell, "none"), (lack, why));
    }

    /// <summary>M6 6 R1F: a pack that cannot be judged (null: not read, not settled, or no focus and no account name) is not guessed at: the pick is <see cref="WarBoltPick.Choose"/>'s and the line says the components were not judged.</summary>
    [Fact]
    public void An_unjudged_pack_keeps_the_plain_pick_and_says_so_R1F()
    {
        var pick = WarBoltPick.ChooseCastable(Scarabs, [81, 82, 83], 400, 0.5, null, null, new HashSet<uint>(), out var why, out var lack);
        Assert.Equal(WarBoltPick.Choose(Scarabs, [81, 82, 83], 400, 0.5, out var plain)?.Id, pick?.Id);
        Assert.Equal(BoltLack.None, lack);
        Assert.StartsWith(plain, why, StringComparison.Ordinal);
        Assert.EndsWith("the pack's components not judged before the cast (the pack is not read or settled, or it holds no War Magic focus and no account name is given)", why, StringComparison.Ordinal);
    }
}
