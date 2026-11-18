using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="CombatModeGuard"/> (M4 3.1 g): every wielded setup against every combat mode, the texts that name the item, a wielded item the object table lacks, and the allocation-free allowed path. The rule is ACE's
/// (<c>Player_Combat.cs</c> 812-835 and 872-882) and retail's <c>ClientCombatSystem::GetDefaultCombatMode</c> (11.6096 0x0056C050): melee is refused with a caster or a launcher wielded, magic without a caster, missile without a launcher.
/// </summary>
[Collection(AllocationMeasurementCollection.Name)]
public sealed class CombatModeGuardTests
{
    [Theory]
    [MemberData(nameof(WieldedSetups.Names), MemberType = typeof(WieldedSetups))]
    public void Every_setup_allows_exactly_the_modes_its_table_row_says_and_peace_always(string name)
    {
        var c = WieldedSetups.Find(name);
        var state = WieldedSetups.World(c.Pieces).State;

        Assert.Null(CombatModeGuard.Check(CombatMode.NonCombat, state)); // peace fits everything, an unknown weapon included
        Assert.Equal(c.Melee, CombatModeGuard.Check(CombatMode.Melee, state) is null);
        Assert.Equal(c.Magic, CombatModeGuard.Check(CombatMode.Magic, state) is null);
        Assert.Equal(c.Missile, CombatModeGuard.Check(CombatMode.Missile, state) is null);

        // The int form (the property-40 value) answers the same.
        Assert.Equal(c.Melee, CombatModeGuard.Check(CombatModes.Melee, state) is null);
        Assert.Equal(c.Magic, CombatModeGuard.Check(CombatModes.Magic, state) is null);
        Assert.Equal(c.Missile, CombatModeGuard.Check(CombatModes.Missile, state) is null);
    }

    [Fact]
    public void M6_6_I_describe_is_exhaustive_every_refusal_has_its_text_and_none_or_an_unknown_value_fails_loudly()
    {
        var wielded = default(WieldedWeapons);
        foreach (var problem in Enum.GetValues<CombatModeProblem>().Where(p => p != CombatModeProblem.None))
        {
            var text = CombatModeGuard.Describe(problem, CombatModes.Melee, in wielded);
            Assert.False(string.IsNullOrWhiteSpace(text), problem + " has no text");
            Assert.DoesNotContain("TODO(", text, StringComparison.Ordinal);
        }

        var none = Assert.Throws<ArgumentException>(() => CombatModeGuard.Describe(CombatModeProblem.None, CombatModes.Melee, in wielded));
        Assert.Contains("TODO(combat-mode-guard-problem)", none.Message, StringComparison.Ordinal);
        var unknown = Assert.Throws<ArgumentOutOfRangeException>(() => CombatModeGuard.Describe((CombatModeProblem)99, CombatModes.Melee, in wielded));
        Assert.Contains("TODO(combat-mode-guard-problem)", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_melee_mode_with_a_caster_or_a_launcher_is_refused_with_a_text_that_names_the_item()
    {
        var wand = CombatModeGuard.Check(CombatMode.Melee, WieldedSetups.World(WieldedSetups.Wand).State);
        Assert.NotNull(wand);
        Assert.Contains("Wand of Flame", wand, StringComparison.Ordinal);
        Assert.Contains("melee", wand, StringComparison.Ordinal);
        Assert.Contains("caster", wand, StringComparison.Ordinal);

        var orb = CombatModeGuard.Check(CombatMode.Melee, WieldedSetups.World(WieldedSetups.Orb).State);
        Assert.Contains("Crystal Orb", orb, StringComparison.Ordinal);

        var bow = CombatModeGuard.Check(CombatMode.Melee, WieldedSetups.World(WieldedSetups.Bow, WieldedSetups.Arrows).State);
        Assert.NotNull(bow);
        Assert.Contains("Shortbow", bow, StringComparison.Ordinal);
        Assert.Contains("launcher", bow, StringComparison.Ordinal);
        Assert.DoesNotContain("Arrow", bow, StringComparison.Ordinal);
    }

    [Fact]
    public void A_magic_or_missile_mode_without_the_weapon_names_what_the_player_wields_instead()
    {
        var magicMace = CombatModeGuard.Check(CombatMode.Magic, WieldedSetups.World(WieldedSetups.Mace).State);
        Assert.NotNull(magicMace);
        Assert.Contains("Mace", magicMace, StringComparison.Ordinal);
        Assert.Contains("magic", magicMace, StringComparison.Ordinal);

        var magicNothing = CombatModeGuard.Check(CombatMode.Magic, WieldedSetups.World().State);
        Assert.Contains("nothing", magicNothing, StringComparison.Ordinal);

        var missileMace = CombatModeGuard.Check(CombatMode.Missile, WieldedSetups.World(WieldedSetups.Sword).State);
        Assert.Contains("Sword", missileMace, StringComparison.Ordinal);
        Assert.Contains("missile", missileMace, StringComparison.Ordinal);

        var missileWand = CombatModeGuard.Check(CombatMode.Missile, WieldedSetups.World(WieldedSetups.Wand).State);
        Assert.Contains("Wand of Flame", missileWand, StringComparison.Ordinal);

        var arrowsAlone = CombatModeGuard.Check(CombatMode.Missile, WieldedSetups.World(WieldedSetups.Arrows).State);
        Assert.Contains("nothing", arrowsAlone, StringComparison.Ordinal); // arrows are ammunition, not a weapon
    }

    [Fact]
    public void A_wielded_weapon_the_object_table_lacks_cannot_be_checked_and_is_refused_for_every_mode_but_peace()
    {
        var state = WieldedSetups.World(WieldedSetups.UnknownMace).State;
        foreach (var mode in new[] { CombatMode.Melee, CombatMode.Magic, CombatMode.Missile })
        {
            var text = CombatModeGuard.Check(mode, state);
            Assert.NotNull(text);
            Assert.StartsWith("cannot check", text, StringComparison.Ordinal);
            Assert.Contains("0x80000777", text, StringComparison.Ordinal);
        }

        Assert.Null(CombatModeGuard.Check(CombatMode.NonCombat, state));

        // The object arrives: the kind is known and the answer is the table's.
        var harness = WieldedSetups.World(WieldedSetups.UnknownMace);
        WieldedSetups.Create(harness, WieldedSetups.UnknownMace with { Present = true });
        Assert.Null(CombatModeGuard.Check(CombatMode.Melee, harness.State));
        Assert.Contains("Mace", CombatModeGuard.Check(CombatMode.Magic, harness.State), StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_that_is_not_a_mode_is_refused_and_a_held_item_or_a_magic_wieldable_item_counts_as_a_caster()
    {
        var state = WieldedSetups.World(WieldedSetups.Wand).State;
        Assert.Contains("not one of", CombatModeGuard.Check(3, state), StringComparison.Ordinal);
        Assert.Contains("not one of", CombatModeGuard.Check(0, state), StringComparison.Ordinal);

        // ACE finds the caster in the held slot whatever its ItemType (GetEquippedWand), so a melee mode with any held item is refused; and the ItemType bit 0x200000 makes a caster of an item in another slot.
        var lockpick = new Piece(0x80000B01, "Lockpick", 0x80, null, EquipMask.Held);
        Assert.Contains("Lockpick", CombatModeGuard.Check(CombatMode.Melee, WieldedSetups.World(lockpick).State), StringComparison.Ordinal);
        Assert.Null(CombatModeGuard.Check(CombatMode.Magic, WieldedSetups.World(lockpick).State));
        var staff = new Piece(0x80000B02, "Quarterstaff", CombatModeGuard.ItemTypeMagicWieldable, null, EquipMask.MeleeWeapon);
        Assert.Contains("Quarterstaff", CombatModeGuard.Check(CombatMode.Melee, WieldedSetups.World(staff).State), StringComparison.Ordinal);
    }

    [Fact]
    public void The_allowed_path_allocates_nothing()
    {
        var melee = WieldedSetups.World(WieldedSetups.Mace, WieldedSetups.Shield).State;
        var magic = WieldedSetups.World(WieldedSetups.Wand).State;
        AllocationAssert.Nothing(() =>
        {
            for (var i = 0; i < 1000; i++)
            {
                Assert.Null(CombatModeGuard.Check(CombatMode.Melee, melee));
                Assert.Null(CombatModeGuard.Check(CombatModes.Magic, magic));
                Assert.Null(CombatModeGuard.Check(CombatMode.NonCombat, magic));
            }
        });
    }
}
