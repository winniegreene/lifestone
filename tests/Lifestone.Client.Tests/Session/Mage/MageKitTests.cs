using System.Globalization;
using System.Text.RegularExpressions;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Tests.Shared;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 item 3 m1 (D8, D38, D46, D48, D49): the mage's kit from a ground's supplies, over BOTH dungeon profiles (the Black Spawn Den and Tusker Temple). The kit is the profile's kit lines plus the four foci plus the stock lines
/// and nothing else; every component of every spell the mage casts, on the foci formula, is a stock line; the build's grants equal the profile's keep counts and purse; the kit and the purse stay under the supplies' burden
/// line; a carried stack under its stop line stops the fight by name; a robe the mage cannot wear is a named TODO. No wcid, count or price here: every value is read from the profile.
/// </summary>
public sealed class MageKitTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>BSD Mage's Arcane Lore (read from the character at the build), the number the Tusker Temple robe is judged against.</summary>
    private const int BsdMageArcaneLore = 144;

    public static TheoryData<string> Grounds => new() { "black-spawn-den-aluvian", "tusker-temple-fort-tethana" };

    private IReadOnlyDictionary<uint, SpellInfo> Spells => SpellTableData.Load(fixture.Dats).Spells;

    [Theory]
    [MemberData(nameof(Grounds))]
    public void A_profile_with_a_kit_and_stock_lines_has_a_vendor_kit_whose_weenies_the_build_knows(string key)
    {
        var ground = GroundCatalog.Get(key);
        Assert.NotEmpty(ground.Supplies.Kit);
        Assert.Contains(ground.Supplies.Kit, k => k.Slot == KitSlot.Worn);
        Assert.Contains(ground.Supplies.Kit, k => k.Slot == KitSlot.Wielded);
        foreach (var line in ground.Supplies.Kit)
        {
            var w = GearKit.SupplyWeenieOf(line.Wcid, ground, "kit");
            Assert.Equal(line.Name, w.Name); // the profile's name is the weenie's
        }

        foreach (var line in ground.Supplies.Stock)
        {
            Assert.Equal(line.Name, GearKit.SupplyWeenieOf(line.Wcid, ground, "stock").Name);
        }

        foreach (var note in ground.Supplies.Purse.Notes)
        {
            Assert.True(GearKit.SupplyWeenieOf(note.Wcid, ground, "purse note").MaxStack >= note.Count);
        }
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void The_kit_is_the_profiles_kit_lines_plus_the_four_foci_plus_the_stock_lines_and_nothing_else(string key)
    {
        var ground = GroundCatalog.Get(key);
        var kit = GearKit.Build(Spells, "kit-test-account", ground: ground);
        output.WriteLine(key + ": " + kit.Describe());

        var expected = ground.Supplies.Kit.Select(k => k.Wcid)
            .Concat(GearKit.MageFociWcids)
            .Concat(ground.Supplies.Stock.Select(s => s.Wcid))
            .Order()
            .ToList();
        Assert.Equal(expected, kit.Items.Select(i => i.Wcid).Order().ToList());
        Assert.Equal(4, kit.Items.Count(i => i.Kind == GearKind.Focus));
        Assert.Equal([15268u, 15269u, 15270u, 15271u], kit.Items.Where(i => i.Kind == GearKind.Focus).Select(i => i.Wcid).Order());

        // The wielded line is first and the wand; the worn line is the armour line (a robe); the kit's components are exactly the stock lines.
        Assert.Equal(ground.Supplies.Kit.Single(k => k.Slot == KitSlot.Wielded).Wcid, kit.WandItem.Wcid);
        Assert.Equal(GearKind.Wand, kit.Items[0].Kind);
        Assert.Equal(ground.Supplies.Kit.Where(k => k.Slot == KitSlot.Worn).Select(k => k.Wcid).Order(), kit.ArmourItems.Select(a => a.Wcid).Order()); // every worn line is an armour item (M5 7 F13: the den wears a hooded robe and gloves, two)
        Assert.Equal(ground.Supplies.Stock.Select(s => s.Wcid).Order(), kit.Components.Select(c => c.Wcid).Order());

        // The old loot kit is gone: no Healer's Heart, no classic armour, no coloured taper, none of the 38 customised ids.
        Assert.Null(kit.ItemOf(GearKit.Wand.Wcid));
        Assert.All(GearKit.Armour, a => Assert.Null(kit.ItemOf(a.Wcid)));
        Assert.True(kit.Components.Count() <= ground.Supplies.Stock.Count);
        Assert.True(kit.CustomisedComponentIds.Count <= 6, "the foci formula burns the scarabs and the Prismatic Taper only: " + string.Join(',', kit.CustomisedComponentIds));
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void Every_component_of_every_spell_the_mage_casts_is_a_stock_line_of_the_ground(string key)
    {
        var ground = GroundCatalog.Get(key);
        var burns = GearKit.CastBurns(ground, Spells, heroRing: false);
        Assert.NotEmpty(burns);
        var stock = ground.Supplies.Stock.ToDictionary(s => s.Wcid);
        foreach (var burn in burns)
        {
            Assert.True(stock.TryGetValue(burn.Wcid, out var line), $"{burn.Spell} ({burn.SpellId}) burns {burn.Name} ({burn.Wcid}), which is no stock line of {key}");
            Assert.True(line!.StopUnder >= burn.PerCast, $"{burn.Spell} needs {burn.PerCast} {burn.Name} a cast; the line stops under {line.StopUnder}");
        }

        // The spells of every kind are in the cast set: the bolt (the plan's levels), the buffs of all four schools, the mana lines, the travel spells, the keeper's Heal Self.
        var cast = burns.Select(b => b.SpellId).ToHashSet();
        foreach (var plan in ground.SpellPlan)
        {
            var bolt = ArenaProfile.LineNamed(plan.Family)!.Spells[plan.SpellLevel - 1];
            Assert.Contains(bolt.Id, cast);
        }

        Assert.All(GearKit.KitTravelSpells, id => Assert.Contains(id, cast));
        Assert.All(GearKit.KitFightSpells, id => Assert.DoesNotContain(id, cast)); // the ring is the hero mage's (heroRing), not the autopilot's
        Assert.All(GearKit.KitKeeperSpells, id => Assert.Contains(id, cast));
        foreach (var role in new[] { ProfileRole.CreatureBuff, ProfileRole.LifeBuff, ProfileRole.ItemBuffArmor, ProfileRole.ItemBuffWand, ProfileRole.ManaTransfer, ProfileRole.StaminaRestore })
        {
            Assert.Contains(ArenaProfile.LinesOf(role), line => line.Spells.Any(s => cast.Contains(s.Id)));
        }

        // The armour lines (Impenetrability, the banes) are cast on what the kit wears: the cycle enchants every worn armour or clothing piece, and the robe is clothing.
        Assert.Equal(ProfileRole.ItemBuffArmor, ArenaProfile.LineNamed("Impenetrability")!.Role);
        Assert.Contains(ground.Supplies.Kit.Where(k => k.Slot == KitSlot.Worn), k => GearKit.SupplyWeenieOf(k.Wcid, ground, "kit").ItemType == GearKit.ItemTypeClothing); // the robe (M5 7 F13: the gloves are armour)

        // Which scarabs: only the levels the plan casts (the den: VI and VII) and the Prismatic Taper.
        output.WriteLine(key + ": " + string.Join(", ", burns.Select(b => b.Name + " x" + b.PerCast).Distinct()));
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void The_hero_rings_diamond_scarab_is_a_named_profile_gap_until_the_profile_carries_it(string key)
    {
        var ground = GroundCatalog.Get(key);
        if (ground.Supplies.StockOf(7299) is not null)
        {
            // M5 7 F10 (the owner's components rule): the den carries the ring's Diamond Scarab now (keep 6), so its hero kit builds and the ring burns only stock lines.
            Assert.NotNull(GearKit.Build(Spells, "kit-test-account", heroRing: true, ground: ground).ItemOf(7299));
            Assert.Contains(GearKit.CastBurns(ground, Spells, heroRing: true), b => b.Wcid == 7299 && b.PerCast == 2);
            return;
        }

        var e = Assert.Throws<InvalidOperationException>(() => GearKit.Build(Spells, "kit-test-account", heroRing: true, ground: ground));
        Assert.Contains("ground-plan-gap", e.Message, StringComparison.Ordinal);
        Assert.Contains("Cassius' Ring of Fire", e.Message, StringComparison.Ordinal);
        Assert.Contains("Diamond Scarab", e.Message, StringComparison.Ordinal);

        // With the line (carried, like the Platinum Scarab) the hero's kit holds it and the ring's casts burn only stock lines.
        var diamond = new StockLine(7299, "Diamond Scarab", 10, 2, 2);
        var carried = ground with { Supplies = ground.Supplies with { Stock = [.. ground.Supplies.Stock, diamond] } };
        var hero = GearKit.Build(Spells, "kit-test-account", heroRing: true, ground: carried);
        Assert.NotNull(hero.ItemOf(7299));
        Assert.Contains(GearKit.CastBurns(carried, Spells, heroRing: true), b => b.Wcid == 7299 && b.PerCast == 2);
    }

    [RequiresDatsFact]
    public void A_component_that_is_not_a_stock_line_fails_the_build_by_name_a_profile_gap_not_a_code_fix()
    {
        var ground = GroundCatalog.Get("black-spawn-den-aluvian");
        var platinum = ground.Supplies.Stock.Single(s => s.Wcid == GearKit.PlatinumScarabWcid);
        var gapped = ground with { Supplies = ground.Supplies with { Stock = [.. ground.Supplies.Stock.Where(s => s != platinum)] } };

        var e = Assert.Throws<InvalidOperationException>(() => GearKit.Build(Spells, "kit-test-account", ground: gapped));
        Assert.Contains("ground-plan-gap", e.Message, StringComparison.Ordinal);
        Assert.Contains("Platinum Scarab", e.Message, StringComparison.Ordinal);
        Assert.Contains("not a stock line", e.Message, StringComparison.Ordinal);

        var small = ground with { Supplies = ground.Supplies with { Stock = [.. ground.Supplies.Stock.Select(s => s.Wcid == GearKit.PrismaticTaperWcid ? s with { StopUnder = 1 } : s)] } };
        var tooSmall = Assert.Throws<InvalidOperationException>(() => GearKit.Build(Spells, "kit-test-account", ground: small));
        Assert.Contains("Prismatic Taper", tooSmall.Message, StringComparison.Ordinal);

        var unknown = ground with { Supplies = ground.Supplies with { Kit = [.. ground.Supplies.Kit.Select(k => k with { Wcid = 99_999_999 })] } };
        Assert.Contains("TODO(m6-kit-weenie)", Assert.Throws<InvalidOperationException>(() => GearKit.Build(Spells, "kit-test-account", ground: unknown)).Message, StringComparison.Ordinal);

        var empty = ground with { Supplies = GroundSupplies.Empty };
        Assert.Contains("ground-plan-gap", Assert.Throws<InvalidOperationException>(() => GearKit.Build(Spells, "kit-test-account", ground: empty)).Message, StringComparison.Ordinal);
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void The_builds_grants_equal_the_keep_counts_and_the_purse_and_the_load_is_under_the_burden_line(string key)
    {
        var ground = GroundCatalog.Get(key);
        var plan = GearKit.KitPlan.For(ground, Spells, "kit-test-account");
        output.WriteLine(key + ": " + plan.LoadLine + "; " + plan.Kit.Describe() + "; purse slots " + plan.PurseSlots);

        // The stock lines are granted to their keep, one stack each.
        foreach (var line in ground.Supplies.Stock)
        {
            var item = plan.Kit.ItemOf(line.Wcid)!;
            Assert.Equal(line.Keep, item.Stack);
            Assert.All(GearKit.StackSizes(item.Stack, item.MaxStack), stack => Assert.True(item.MaxStack is null || stack <= item.MaxStack)); // M5 7 F3 (D87): a line past one stack is granted in several
        }

        // The purse: the coin as pyreals in stacks of at most the pyreal stack size, the notes as the profile counts them.
        var coin = plan.Purse.Where(p => p.Wcid == GearKit.PyrealWcid).ToList();
        var pyrealStack = GearKit.SupplyWeenies.Single(w => w.Wcid == GearKit.PyrealWcid).MaxStack!.Value;
        Assert.Equal(ground.Supplies.Purse.Coin, coin.Sum(c => c.Total));
        Assert.All(coin.SelectMany(c => c.Stacks), stack => Assert.InRange(stack, 1, pyrealStack));
        foreach (var note in ground.Supplies.Purse.Notes)
        {
            Assert.Equal(note.Count, plan.Purse.Single(p => p.Wcid == note.Wcid).Total);
        }

        Assert.Equal(ground.Supplies.Purse.Notes.Count + (ground.Supplies.Purse.Coin > 0 ? 1 : 0), plan.Purse.Count);

        // The load: the kit's burden and the purse's, logged as "load: <burden> of <capacity>", under the supplies' line.
        Assert.Matches(new Regex(@"^load: \d+ of 19650$", RegexOptions.CultureInvariant), plan.LoadLine);
        Assert.Equal(plan.Kit.Burden + ground.Supplies.Purse.Notes.Sum(n => n.Count), plan.Load);
        Assert.True(plan.Load <= (long)plan.Capacity * ground.Supplies.BurdenTripPercent / 100);
        Assert.True(plan.Kit.Slots + plan.PurseSlots <= GearKit.PackCapacity);
    }

    [RequiresDatsFact]
    public void A_kit_and_purse_over_the_burden_line_fail_the_build_by_name()
    {
        var ground = GroundCatalog.Get("black-spawn-den-aluvian");
        var heavy = ground with { Supplies = ground.Supplies with { BurdenTripPercent = 20 } };

        var e = Assert.Throws<InvalidOperationException>(() => GearKit.KitPlan.For(heavy, Spells, "kit-test-account"));
        Assert.Contains("ground-plan-gap", e.Message, StringComparison.Ordinal);
        Assert.Contains("burden line of 20%", e.Message, StringComparison.Ordinal);
        Assert.Contains("load: ", e.Message, StringComparison.Ordinal);
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void A_carried_stack_under_its_stop_line_stops_the_fight_by_name_and_a_sold_line_only_logs(string key)
    {
        var ground = GroundCatalog.Get(key);
        var kit = GearKit.Build(Spells, "kit-test-account", ground: ground);
        var full = HeldAtKeep(ground);
        var notes = new List<string>();

        Assert.Null(GearKit.FightStartStop(ground, kit, full, notes.Add)); // everything held at its keep: no stop, no note
        Assert.Empty(notes);

        foreach (var line in ground.Supplies.Stock.Where(s => ground.IsCarried(s.Wcid)))
        {
            var low = new Dictionary<uint, int>(full) { [line.Wcid] = line.StopUnder - 1 };
            var stop = GearKit.FightStartStop(ground, kit, low, notes.Add);
            Assert.NotNull(stop);
            Assert.Contains(line.Name, stop, StringComparison.Ordinal);
            Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"stack is {line.StopUnder - 1}, under its stop line {line.StopUnder}"), stop, StringComparison.Ordinal);
            Assert.Contains(ground.Home.Town + " sells none (D8)", stop, StringComparison.Ordinal);
            Assert.StartsWith(line.Wcid == GearKit.PlatinumScarabWcid ? "m5-platinum-stock: " : "m5-carried-stock: ", stop, StringComparison.Ordinal);
        }

        // A sold line under its trip line is logged and the fight goes on; one stack at the stop line itself is not a stop.
        foreach (var line in ground.Supplies.Stock.Where(s => !ground.IsCarried(s.Wcid) && s.TripUnder > 0))
        {
            var low = new Dictionary<uint, int>(full) { [line.Wcid] = line.TripUnder - 1 };
            Assert.Null(GearKit.FightStartStop(ground, kit, low, notes.Add));
            Assert.Contains(notes, n => n.Contains(line.Name, StringComparison.Ordinal) && n.Contains("trip line", StringComparison.Ordinal));
        }

        // The foci: one missing is the other named stop.
        var noFocus = new Dictionary<uint, int>(full);
        noFocus.Remove(GearKit.MageFociWcids[3]);
        Assert.StartsWith("m5-mage-foci: ", GearKit.FightStartStop(ground, kit, noFocus, notes.Add), StringComparison.Ordinal);

        // A hand-made kit (a test's) has no foci and nothing to count.
        Assert.Null(GearKit.FightStartStop(ground, GearKit.Of([]), new Dictionary<uint, int>(), notes.Add));
    }

    [RequiresDatsTheory]
    [MemberData(nameof(Grounds))]
    public void A_robe_or_wand_the_mage_cannot_use_is_a_named_todo_not_silently_accepted(string key)
    {
        var ground = GroundCatalog.Get(key);
        var plan = GearKit.KitPlan.For(ground, Spells, "kit-test-account");
        var shorts = plan.WieldShortfalls(BsdMageArcaneLore);
        var robe = WornWeenies(ground).MaxBy(w => w.ItemDifficulty)!; // the worn line that asks the most (M5 7 F13: the den wears a robe and gloves; the gloves ask none)
        output.WriteLine(key + ": " + robe.Name + " needs Arcane Lore " + robe.ItemDifficulty + "; " + string.Join(" | ", shorts));

        if (robe.ItemDifficulty > BsdMageArcaneLore)
        {
            // Tusker Temple's Dho War Master Robe needs 158 against BSD Mage's 144.
            var only = Assert.Single(shorts);
            Assert.Contains("TODO(m6-tethana-supplies)", only, StringComparison.Ordinal);
            Assert.Contains(robe.Name, only, StringComparison.Ordinal);
            Assert.Contains("158", only, StringComparison.Ordinal);
            Assert.Contains("144", only, StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(shorts); // the den's Faran Robe with Hood and Leather Gauntlets need none (M5 7 F13; the old War robe 20)
        }

        Assert.Empty(plan.WieldShortfalls(1_000));
    }

    [Fact]
    public void Only_the_den_robe_is_worn_by_BSD_Mage_and_the_tethana_robe_is_the_named_gap()
    {
        Assert.All(WornWeenies(GroundCatalog.Get("black-spawn-den-aluvian")), w => Assert.True(w.ItemDifficulty <= BsdMageArcaneLore, w.Name));
        var tethana = GroundCatalog.Get("tusker-temple-fort-tethana");
        Assert.Equal(158, WornWeenies(tethana).Single().ItemDifficulty);
    }

    [Fact]
    public void The_dens_kit_lines_load_with_two_packs_the_live_grant_takes_M5_7_G3()
    {
        var ground = GroundCatalog.Get("black-spawn-den-aluvian");
        var packs = ground.Supplies.Kit.Where(k => k.Slot == KitSlot.Pack).ToList();
        var line = Assert.Single(packs);
        Assert.Equal(136u, line.Wcid);
        Assert.Equal(2, line.Count); // M5 7 lead: the live build took two of seven (ACE: no room for the rest)
        Assert.Equal(7, GearKit.ContainerCapacity);
        Assert.All(ground.Supplies.Kit.Where(k => k.Slot != KitSlot.Pack), k => Assert.Equal(1, k.Count));
        Assert.NotNull(ground.Supplies.KitOf(136));
    }

    [RequiresDatsFact]
    public void The_build_grants_the_packs_one_by_one_outside_the_main_pack_slots_and_the_burden_stays_under_the_line_M5_7_G3()
    {
        var ground = GroundCatalog.Get("black-spawn-den-aluvian");
        var plan = GearKit.KitPlan.For(ground, Spells, "kit-test-account");
        var packs = plan.Kit.ItemOf(136)!;
        Assert.Equal(GearKind.Pack, packs.Kind);
        Assert.Equal(2, packs.Stack);
        Assert.True(packs.Counted);
        Assert.Equal(2 * 15, packs.Burden);
        Assert.Equal(2, GearKit.StackSizes(packs.Stack, packs.MaxStack).Count); // two requests of one (the live grant took two: ACE refused the rest with no room): a pack does not stack
        Assert.Equal(plan.Kit.Items.Where(i => i.Kind != GearKind.Pack).Sum(i => GearKit.StackSizes(i.Stack, i.MaxStack).Count), plan.Kit.Slots); // a container takes no main-pack item slot
        Assert.True(plan.Load <= (long)plan.Capacity * ground.Supplies.BurdenTripPercent / 100);
        output.WriteLine(plan.LoadLine + "; " + plan.Kit.Describe());
    }

    /// <summary>The weenies of the ground's worn kit lines (M5 7 F13: the den's are a hooded robe and gloves).</summary>
    private static List<GearKit.SupplyWeenie> WornWeenies(HuntingGround ground) =>
        [.. ground.Supplies.Kit.Where(k => k.Slot == KitSlot.Worn).Select(k => GearKit.SupplyWeenieOf(k.Wcid, ground, "kit"))];

    /// <summary>What a mage built to the ground's supplies holds at the start: the foci, every stock line at its keep.</summary>
    private static Dictionary<uint, int> HeldAtKeep(HuntingGround ground)
    {
        var held = GearKit.MageFociWcids.ToDictionary(w => w, _ => 1);
        foreach (var line in ground.Supplies.Stock)
        {
            held[line.Wcid] = line.Keep;
        }

        return held;
    }
}
