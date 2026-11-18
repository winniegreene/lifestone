using DatReaderWriter.Enums;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>M6 7 AT: what a pass short of a component tells the counters: the component, the count a cast burns, and the want of two passes (<see cref="ComponentNeeds"/>).</summary>
public sealed class ComponentNeedsTests
{
    private const uint PyrealScarabId = 6;

    private static SpellInfo Creature() =>
        WorldActionRig.Spell(1000, SpellType.Enchantment) with
        {
            School = MagicSchool.CreatureEnchantment,
            Formula = [PyrealScarabId, 63, 63, 63],
            FormulaVersion = 1,
        };

    [Fact]
    public void The_floor_names_the_short_component_by_weenie_and_the_count_a_cast_burns()
    {
        var scarab = ComponentWeenies.TryGetByComponent(PyrealScarabId, out var weenie) ? weenie : throw new InvalidOperationException();
        var pack = new Dictionary<uint, int> { [scarab.Wcid] = 1 }; // the scarab, and no Prismatic Taper

        Assert.True(LevelFloor.ShortComponent(pack, Creature(), out var need, out var found, out var perCast));
        Assert.Equal(SpellFormulaRules.PrismaticTaperId, found.ComponentId);
        Assert.Equal(4, perCast); // a power 6 scarab: four Prismatic Tapers on the foci formula
        Assert.Contains("which the pack does not hold", need, StringComparison.Ordinal);
        Assert.True(LevelFloor.ShortOf(pack, Creature(), out var same)); // the older answer is unchanged
        Assert.Equal(need, same);

        var paid = new Dictionary<uint, int> { [scarab.Wcid] = 1, [found.Wcid] = 4 };
        Assert.False(LevelFloor.ShortComponent(paid, Creature(), out _, out _, out var none));
        Assert.Equal(0, none);
    }

    [Fact]
    public void The_want_is_two_passes_of_the_lines_that_burn_it_and_a_line_counts_once()
    {
        var needs = new ComponentNeeds();
        Assert.Equal(0, needs.WantOf(1643));
        var before = needs.Version;

        Assert.True(needs.Note(1643, "Blue Taper", "Focus Self", 1));
        Assert.Equal(2, needs.WantOf(1643));
        Assert.True(needs.Note(1643, "Blue Taper", "Willpower Self", 2));
        Assert.Equal(6, needs.WantOf(1643)); // (1 + 2) a pass, two passes
        Assert.False(needs.Note(1643, "Blue Taper", "Focus Self", 1)); // noted already: nothing changes
        Assert.False(needs.Note(1643, "Blue Taper", "Armor Self", 0)); // a cast that burns none is no need
        Assert.Equal(before + 2, needs.Version);
        Assert.Equal("Blue Taper", needs.NameOf(1643));
        Assert.Equal([1643u], needs.Wcids.ToArray());
    }
}
