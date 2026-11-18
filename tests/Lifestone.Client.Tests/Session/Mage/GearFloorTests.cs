using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 2 b: the gear floor (<see cref="GearFloor"/>) on the real setting: the outskirts profile's vendors (Archmage Cindrue's Wand 2472 at 270 and Mana Charges, Ecutha the Tailor's Faran Robe with Hood 5851 at 68: the pinned world SQL's
/// create list rows 21071, 21051-21054 and 12693), the ladder's floor level (the start rung's <c>move_on.level</c>) and Winnie at level 10 with the Training Wand 12748 wielded and the Pathwarden Robe 40439 worn. Every number is the
/// profile's or the ladder's; the casters are counted from the state.
/// </summary>
public sealed class GearFloorTests
{
    private const string Outskirts = "holtburg-outskirts";
    private const string Den = "black-spawn-den-aluvian";
    private const uint HeldSlot = 0x01000000;
    private const uint CasterType = 0x8000;

    private static HuntingGround Ground => GroundCatalog.Get(Outskirts);

    private static int Floor => GearFloor.FloorLevel(LadderLoader.WarMage);

    /// <summary>Winnie at level 10 (the evening climb's end, M6 0): one caster, the robe worn, the 10,000 pyreals of her creation.</summary>
    private static GearFloorState Winnie(int level = 10, int casters = 1, bool chest = true, long purse = 10_000) => new(level, casters, chest, chest ? "Pathwarden Robe" : null, purse);

    [Fact]
    public void The_floor_level_is_the_ladders_start_rung_move_on_level_not_a_constant()
    {
        var ladder = LadderLoader.WarMage;
        Assert.Equal(ladder.RungFor(1).MoveOn.Level, Floor);
        Assert.Equal("academy-and-chest", ladder.RungFor(1).Key);
        Assert.Equal(5, Floor); // the Academy and the Pathwarden chest run levels 1 to 4 with the Training Wand
    }

    [Fact]
    public void Winnie_at_level_10_with_the_Training_Wand_only_buys_one_Wand_at_Cindrue_for_270()
    {
        var plan = GearFloor.Plan(Ground, Winnie(), Floor);

        var line = Assert.Single(plan.Lines);
        Assert.Equal((GearFloorKind.Caster, 2472u, "Wand", 1, 270, "Archmage Cindrue"), (line.Kind, line.Wcid, line.Name, line.Count, line.Price, line.Vendor.Name));
        Assert.Null(plan.Stop);
        Assert.Equal(2, plan.CastersAfter);
        Assert.Equal("gear floor: Caster: buy 1 x Wand (weenie 2472) at Archmage Cindrue for 270 each: 1 caster(s) held, 2 wanted: the cheapest caster a vendor of Holtburg sells", line.ToString());
        Assert.Contains("casters=1", plan.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_casters_and_a_worn_robe_buy_nothing()
    {
        var plan = GearFloor.Plan(Ground, Winnie(casters: 2), Floor);

        Assert.Empty(plan.Lines);
        Assert.Null(plan.Stop);
        Assert.Empty(plan.Notes);
        Assert.EndsWith("nothing to buy", plan.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void Below_the_floor_level_nothing_is_bought_even_with_no_caster_and_no_robe()
    {
        var plan = GearFloor.Plan(Ground, Winnie(level: Floor - 1, casters: 0, chest: false), Floor);

        Assert.Empty(plan.Lines);
        Assert.Null(plan.Stop);
        Assert.Contains("is under the floor level 5", plan.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void At_the_floor_level_itself_the_floor_holds()
    {
        Assert.Single(GearFloor.Plan(Ground, Winnie(level: Floor), Floor).Lines);
    }

    [Fact]
    public void A_purse_short_of_the_Wand_is_the_loud_no_caster_stop_with_the_reason()
    {
        var plan = GearFloor.Plan(Ground, Winnie(purse: 269), Floor);

        Assert.Empty(plan.Lines);
        Assert.True(plan.Stopped);
        Assert.StartsWith(GearFloor.NoCaster + ":", plan.Stop, StringComparison.Ordinal);
        Assert.Contains("the purse 269 is short of the Wand's 270 at Archmage Cindrue", plan.Stop, StringComparison.Ordinal);
        Assert.Equal(1, plan.CastersAfter); // the trip goes on with the one caster held; the audit's stop is the autopilot's
    }

    [Fact]
    public void No_caster_and_a_full_purse_buy_one_line_per_missing_caster()
    {
        var plan = GearFloor.Plan(Ground, Winnie(casters: 0), Floor);

        Assert.Equal(2, plan.Lines.Count);
        Assert.All(plan.Lines, l => Assert.Equal((GearFloorKind.Caster, 2472u, 1, 270), (l.Kind, l.Wcid, l.Count, l.Price)));
        Assert.Equal(2, plan.CastersAfter);
        Assert.Null(plan.Stop);
    }

    [Fact]
    public void No_caster_and_a_purse_for_one_buy_one_and_still_stop_by_name_for_the_spare()
    {
        var plan = GearFloor.Plan(Ground, Winnie(casters: 0, purse: 300), Floor);

        Assert.Single(plan.Lines);
        Assert.Equal(1, plan.CastersAfter);
        Assert.Contains("1 caster(s) after the trip, under 2: the purse 30 is short", plan.Stop, StringComparison.Ordinal);
    }

    [Fact]
    public void Zero_casters_and_no_vendor_that_sells_one_is_the_no_caster_stop_naming_the_town()
    {
        var bare = Ground with { Vendors = [.. Ground.Vendors.Select(v => v with { Sells = [.. v.Sells.Where(w => w.Wcid != 2472)] })] };

        var plan = GearFloor.Plan(bare, Winnie(casters: 0), Floor);

        Assert.Empty(plan.Lines);
        Assert.Equal(0, plan.CastersAfter);
        Assert.Equal("no-caster: 0 caster(s) held, under 2, and no vendor of Holtburg sells a caster (the profile's vendors list none)", plan.Stop);
    }

    [Fact]
    public void A_worn_Pathwarden_robe_makes_no_robe_line_and_a_bare_chest_buys_the_hooded_robe_at_Ecutha()
    {
        Assert.DoesNotContain(GearFloor.Plan(Ground, Winnie(casters: 2), Floor).Lines, l => l.Kind == GearFloorKind.Robe);

        var bare = GearFloor.Plan(Ground, Winnie(casters: 2, chest: false), Floor);

        var robe = Assert.Single(bare.Lines);
        Assert.Equal((GearFloorKind.Robe, 5851u, "Faran Robe with Hood", 68, "Ecutha the Tailor"), (robe.Kind, robe.Wcid, robe.Name, robe.Price, robe.Vendor.Name));
        Assert.Contains("chest bare", bare.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void The_caster_is_paid_before_the_robe_and_a_robe_the_purse_cannot_pay_is_a_named_note_not_a_stop()
    {
        var plan = GearFloor.Plan(Ground, Winnie(chest: false, purse: 300), Floor);

        Assert.Equal(GearFloorKind.Caster, Assert.Single(plan.Lines).Kind);
        Assert.Null(plan.Stop);
        Assert.Contains(plan.Notes, n => n.Contains("robe: no chest-covering piece is worn, but the purse 30 is short of the Faran Robe with Hood's 68 at Ecutha the Tailor", StringComparison.Ordinal));
    }

    [Fact]
    public void The_casters_mana_want_is_a_charge_line_at_Cindrue_at_her_price()
    {
        var want = new CasterManaDecision(CasterManaAct.WantStone, 0x80001234, "Atlan Wand", 0, 500, 0, 0, 0, 4615, 2304, 1, null, "out of mana, no charged spare and no charged stone is held");

        var plan = GearFloor.Plan(Ground, Winnie(casters: 2), Floor, want);

        var line = Assert.Single(plan.Lines);
        Assert.Equal((GearFloorKind.ManaCharge, 4615u, "High Mana Charge", 1, 3375, "Archmage Cindrue"), (line.Kind, line.Wcid, line.Name, line.Count, line.Price, line.Vendor.Name));
    }

    [Fact]
    public void A_want_with_no_charge_vendor_is_a_named_TODO_note_and_a_want_the_purse_cannot_pay_is_named()
    {
        var none = new CasterManaDecision(CasterManaAct.WantStone, 0x80001234, "Atlan Wand", 0, 500, 0, 0, 0, 0, 0, 0, "TODO(m6-no-stone-vendor)", "no charge");
        var plan = GearFloor.Plan(Ground, Winnie(casters: 2), Floor, none);
        Assert.Empty(plan.Lines);
        Assert.Contains(plan.Notes, n => n.Contains("TODO(m6-no-stone-vendor)", StringComparison.Ordinal));

        var want = new CasterManaDecision(CasterManaAct.WantStone, 0x80001234, "Atlan Wand", 0, 500, 0, 0, 0, 4615, 2304, 2, null, "out");
        var poor = GearFloor.Plan(Ground, Winnie(casters: 2, purse: 3_000), Floor, want);
        Assert.Empty(poor.Lines);
        Assert.Contains(poor.Notes, n => n.Contains("the purse 3000 is short of one High Mana Charge at 3375", StringComparison.Ordinal));
    }

    [Fact]
    public void A_mana_decision_that_is_not_a_want_adds_no_line()
    {
        var swap = new CasterManaDecision(CasterManaAct.SwapToSpare, 1, "Atlan Wand", 0, 500, 0, 0, 2, 0, 0, 0, null, "spare");
        Assert.Empty(GearFloor.Plan(Ground, Winnie(casters: 2), Floor, swap).Lines);
    }

    [Theory]
    [InlineData(2472u, true)] // Wand: a caster
    [InlineData(5851u, true)] // Faran Robe with Hood: covers the chest
    [InlineData(6071u, true)] // Faran War Apprentice Robe
    [InlineData(4612u, true)] // Tiny Mana Charge
    [InlineData(4615u, true)] // High Mana Charge
    [InlineData(56u, false)] // Leather Gauntlets: the hands, not the chest (M6 7 AF: IsGauntletWare names it apart; the audit keeps the kit gloves named)
    [InlineData(20631u, false)] // Prismatic Taper
    [InlineData(2621u, false)] // a trade note
    public void A_floor_ware_is_a_caster_a_chest_robe_or_a_mana_charge(uint wcid, bool floor)
    {
        Assert.Equal(floor, GearFloor.IsFloorWare(wcid));
    }

    [Fact]
    public void The_chest_piece_is_read_from_the_location_or_the_coverage()
    {
        var robe = new WornPiece(1, "Pathwarden Robe", 0x4, 0, 0x17F00, null); // ACE ClothingPriority 98048
        var armour = new WornPiece(2, "Breastplate", 0x2, GearFloor.ChestArmor, 0, null);
        var wand = new WornPiece(3, "Training Wand", CasterType, HeldSlot, 0, null);
        var boots = new WornPiece(4, "Sollerets", 0x2, 0x100, 0x10000, null);

        Assert.Same(robe, GearFloor.ChestPiece([wand, robe]));
        Assert.Same(armour, GearFloor.ChestPiece([armour, boots]));
        Assert.Null(GearFloor.ChestPiece([wand, boots]));
    }

    [Fact]
    public void The_casters_held_are_read_from_the_state_wielded_main_pack_and_side_pack()
    {
        var rig = new VendorRig(10_000);
        Assert.Equal(0, GearFloor.CastersHeld(rig.State));

        var training = Own(rig, "Training Wand", 12748, CasterType, VendorRig.Player);
        rig.R.Server((p, s) => AceM3Writers.WieldObject(p, s, training, (int)HeldSlot));
        Assert.Equal(1, GearFloor.CastersHeld(rig.State));

        Own(rig, "Wand", 2472, CasterType, VendorRig.Player);
        var pack = rig.NextGuid();
        rig.Arrive(pack, "Pack", 136, 0x200, 65);
        rig.R.Raw(AceM3Writers.PutObjInContainer(VendorRig.Player, 900, pack, VendorRig.Player, 0, 1));
        Own(rig, "Wand", 2472, CasterType, pack);
        Own(rig, "Lead Scarab", 691, 0x1000, VendorRig.Player);

        Assert.Equal(3, GearFloor.CastersHeld(rig.State));
    }

    [Fact]
    public void The_state_of_the_floor_reads_the_level_casters_and_chest_from_the_session()
    {
        var rig = new VendorRig(10_000);
        rig.R.PrivateInt(25, 10);
        var training = Own(rig, "Training Wand", 12748, CasterType, VendorRig.Player);
        rig.R.Server((p, s) => AceM3Writers.WieldObject(p, s, training, (int)HeldSlot));
        var robe = Own(rig, "Pathwarden Robe", 40439, 0x4, VendorRig.Player);
        rig.R.Server((p, s) => AceM3Writers.WieldObject(p, s, robe, 0x7F00));

        var state = GearFloor.StateOf(rig.State, 10_000);

        Assert.Equal(new GearFloorState(10, 1, true, "Pathwarden Robe", 10_000, HandsCovered: false, Bare: 0xC000), state); // M6 7 AF: the robe is worn, no gauntlets; M6 item 14: the rig's robe carries no ClothingPriority, so its location 0x7F00 is read: the head and the hands are bare
        var plan = GearFloor.Plan(Ground, state, Floor);
        Assert.Equal(2472u, Assert.Single(plan.Lines, l => l.Kind == GearFloorKind.Caster).Wcid);
        Assert.Equal(56u, Assert.Single(plan.Lines, l => l.Kind == GearFloorKind.Gauntlets).Wcid); // M6 7 AH: the outskirts' Renald sells the pair now
    }

    [Fact]
    public void The_den_mage_at_level_81_with_two_casters_and_the_kit_robe_buys_nothing_from_the_floor()
    {
        var den = GroundCatalog.Get(Den);

        var plan = GearFloor.Plan(den, new GearFloorState(81, 2, true, "Faran Robe with Hood", 400_000), Floor);

        Assert.Empty(plan.Lines);
        Assert.Equal(2472u, Assert.Single(GearFloor.Plan(den, new GearFloorState(81, 1, true, "Faran Robe with Hood", 400_000), Floor).Lines).Wcid); // the den's Cindrue sells the same Wand
    }

    [Fact]
    public void The_bare_kit_death_keeps_its_rearm_with_a_floor_planned_and_a_bought_spare_is_the_packs_rearm()
    {
        // DeathHandler.BeginRearm: rearm=none while a caster is wielded; =pack when GearInventory.PackCaster finds one; =corpse when the profile's kit and the built kit are empty. The floor never enters supplies.kit.
        var ground = Ground;
        var plan = GearFloor.Plan(ground, Winnie(), Floor);
        Assert.NotEmpty(plan.Lines);
        Assert.Empty(ground.Supplies.Kit);
        Assert.True(GearKit.HasNoKit(ground));
        Assert.Same(GearKit.Bare, GearKit.BuildFor(new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>(), "floor-test-account", ground));
        Assert.DoesNotContain(ground.Supplies.Kit, k => k.Slot == KitSlot.Wielded); // no wand line: corpse, not a kit trip

        var rig = new VendorRig(10_000);
        Assert.Equal(0u, GearInventory.PackCaster(rig.State, [])); // no spare: rearm=corpse (none while the Training Wand is wielded)
        var spare = Own(rig, "Wand", 2472, CasterType, VendorRig.Player); // the floor's Wand, bought and kept as the spare
        Assert.Equal(spare, GearInventory.PackCaster(rig.State, [])); // rearm=pack
    }

    [Fact]
    public void The_outskirts_profile_lists_the_floor_wares_at_the_world_sql_prices()
    {
        var g = Ground;
        Assert.Equal(270, g.PriceOf(2472)); // Value 200 x 1.35, row 21071
        Assert.Equal([473, 878, 2025, 3375], new uint[] { 4612, 4613, 4614, 4615 }.Select(w => g.PriceOf(w)!.Value)); // Values 350, 650, 1,500, 2,500 x 1.35, rows 21051-21054
        Assert.Equal(287_500, g.PriceOf(20630)); // the 250,000 note at 1.15, row 21069
        var ecutha = g.VendorSelling(5851)!;
        Assert.Equal(("Ecutha the Tailor", 719u, 0x7A9B4025u, 68), (ecutha.Name, ecutha.Wcid, ecutha.Guid, g.PriceOf(5851)!.Value));
        Assert.Equal(GroundCatalog.Get(Den).Vendors.Single(v => v.Wcid == 719) with { Sells = ecutha.Sells }, ecutha); // the den profile's row verbatim
        Assert.Equal([2625u], g.Supplies.Purse.Notes.Select(n => n.Wcid)); // M6 2 d: the purse names the one note the trips buy toward (min_note 10,000); the notes Cindrue lists are the trips' offers
        Assert.Equal((10.0, 125, 60), (g.Loot!.MinValuePerBurden, g.Loot.LoadLinePercent, g.Loot.RadiusMetres)); // M6 7 AR: her rule, the audit's rows 15, 28, 32
    }

    [Fact]
    public void The_out_of_mana_line_marks_the_casters_spells_dropped_until_its_next_wield()
    {
        var dropped = new CasterSpellsDropped();
        var watch = new WornManaWatch(new Lifestone.Client.Tests.RecordingLogSink(), "autopilot") { Dropped = dropped };

        Assert.True(watch.OnChat("Your Wand is low on Mana.", 10));
        Assert.False(dropped.IsDropped(0x80001000, "Wand")); // low is not out: the spells are still on
        Assert.True(watch.OnChat("Your Wand is out of Mana.", 20)); // ACE HandleManaDepleted, Player_Tick.cs:681-699
        Assert.True(dropped.IsDropped(0x80001000, "Wand")); // by name: the watch never read this caster
        Assert.False(dropped.IsDropped(0x80001001, "Training Wand"));

        dropped.Clear(0x80001000, "Wand"); // the resupply wielded it again (Player_Inventory.cs:313-315)
        Assert.False(dropped.IsDropped(0x80001000, "Wand"));
    }

    private static uint _seq = 9000;

    private static void Appraise(VendorRig rig, uint guid, Dictionary<uint, int> ints) =>
        rig.R.H.R.Reducer.OnIdentifyResponse(rig.R.H.R.Context, new GameEventHeader(VendorRig.Player, ++_seq, 0x00C9), new IdentifyResponseMsg(
            guid, IdentifyResponseFlags.IntStatsTable, true, ints.ToImmutableDictionary(), ImmutableDictionary<uint, long>.Empty, ImmutableDictionary<uint, bool>.Empty, ImmutableDictionary<uint, double>.Empty,
            ImmutableDictionary<uint, string>.Empty, ImmutableDictionary<uint, uint>.Empty, [], null, null, null, null, null, null, null, null));

    [Fact]
    public void M6_2_f_a_caster_the_character_cannot_wield_is_not_a_held_caster_an_unread_one_counts_and_is_named()
    {
        var rig = new VendorRig(10_000);
        rig.R.PrivateInt(25, 10);
        var training = Own(rig, "Training Wand", 12748, CasterType, VendorRig.Player);
        rig.R.Server((p, s) => AceM3Writers.WieldObject(p, s, training, (int)HeldSlot));
        var hard = Own(rig, "Heavy Wand", 9001, CasterType, VendorRig.Player);
        Appraise(rig, hard, new() { [AppraisalIds.ArcaneLore] = 9999 }); // activation: Arcane Lore far above the character's (SelfEquipJudge.Unmet)
        Assert.NotNull(SelfEquipJudge.Unmet(rig.State.Appraisals.TryGet(hard, out var a) ? a : throw new InvalidOperationException("no appraisal"), new QualitiesSkills(rig.State.Self.Qualities)));

        var unread = new List<string>();
        Assert.Equal(1, GearFloor.CastersHeld(rig.State, unread)); // 2-R3: the Training Wand only
        Assert.Empty(unread);
        Assert.False(GearFloor.CountsAsCaster(rig.State, rig.State.Objects.TryGet(hard, out var heavy) ? heavy : throw new InvalidOperationException(), out _));
        Assert.Equal(2472u, Assert.Single(GearFloor.Plan(Ground, GearFloor.StateOf(rig.State, 10_000), Floor).Lines, l => l.Kind == GearFloorKind.Caster).Wcid); // the floor still buys the Wand (the bare chest also buys a robe)

        var unknown = Own(rig, "Fresh Wand", 2472, CasterType, VendorRig.Player); // not appraised yet: counted while unread, and named
        Assert.Equal(2, GearFloor.CastersHeld(rig.State, unread));
        Assert.Equal(["Fresh Wand"], unread);
        Assert.True(unknown != 0);
    }

    [Fact]
    public void M6_2_f_the_floor_buys_no_robe_that_cannot_go_on_and_names_the_worn_piece_in_the_way()
    {
        var sollerets = new WornPiece(7, "Sollerets", 0x2, 0x100, 0x10000, null); // the feet: the hooded Faran Robe's 0x17F00 holds them
        var plan = GearFloor.Plan(Ground, Winnie(chest: false), Floor, null, [sollerets]);

        Assert.DoesNotContain(plan.Lines, l => l.Kind == GearFloorKind.Robe);
        var note = Assert.Single(plan.Notes);
        Assert.Contains("the Faran Robe with Hood (coverage 0x17F00) cannot go on: the worn 'Sollerets' 0x00000007 covers 0x10000 of it: not bought", note, StringComparison.Ordinal);
        Assert.Equal(5851u, Assert.Single(GearFloor.Plan(Ground, Winnie(chest: false), Floor, null, []).Lines, l => l.Kind == GearFloorKind.Robe).Wcid); // nothing in the way: bought as before
    }

    private const uint RobeCover = 0x17F00; // Pathwarden Robe 40439 ClothingPriority (world SQL PropertyInt 4): head, feet, chest, abdomen, arms, legs
    private const uint GauntletCover = 0x8000; // Pathwarden Gauntlets 33606 and the Leather Gauntlets 56
    private const uint SolleretCover = 0x10000; // Pathwarden Sollerets 33605

    [Fact]
    public void M6_7_af_the_robe_leaves_only_the_hands_bare_and_the_gauntlets_close_them_but_the_sollerets_cannot_go_on()
    {
        Assert.Equal(GearCoverage.Hands, GearCoverage.Gaps([RobeCover]));

        Assert.True(GearCoverage.ClosesGap([RobeCover], GauntletCover, 120, out var why));
        Assert.Contains("closes the bare parts 0x8000", why, StringComparison.Ordinal);
        Assert.False(GearCoverage.ClosesGap([RobeCover], SolleretCover, 120, out var feet)); // the robe covers the feet: ACE refuses the wield
        Assert.Contains("overlaps", feet, StringComparison.Ordinal);
        Assert.False(GearCoverage.ClosesGap([RobeCover, GauntletCover], GauntletCover, 120, out _)); // the hands are closed already
        Assert.False(GearCoverage.ClosesGap([RobeCover], GauntletCover, null, out var unread)); // an unread value is not guessed
        Assert.Contains("not read", unread, StringComparison.Ordinal);
        Assert.False(GearCoverage.ClosesGap([RobeCover], GauntletCover, 0, out _));
        Assert.False(GearCoverage.ClosesGap([RobeCover], 0, 120, out _)); // a trinket covers nothing
        Assert.Equal(0u, GearCoverage.Gaps([RobeCover, GauntletCover]));
    }

    [Fact]
    public void M6_7_af_a_worn_set_with_no_gauntlets_has_bare_hands_by_coverage_or_by_location()
    {
        var robe = new WornPiece(1, "Pathwarden Robe", 0x2, 0x7F01, RobeCover, null);
        var gloves = new WornPiece(2, "Leather Gauntlets", 0x2, 0x20, GauntletCover, null);
        var unreadGloves = new WornPiece(3, "Gloves", 0x2, 0x20, 0, null); // no ClothingPriority read: the location says HandWear
        Assert.False(GearFloor.HandsCoveredBy([robe]));
        Assert.True(GearFloor.HandsCoveredBy([robe, gloves]));
        Assert.True(GearFloor.HandsCoveredBy([robe, unreadGloves]));
    }

    [Fact]
    public void M6_7_af_the_floor_buys_the_cheapest_vendor_gauntlets_when_the_hands_are_bare_and_nothing_when_they_are_covered()
    {
        var den = GroundCatalog.Get(Den); // Shopkeeper Renald the Elder sells the Leather Gauntlets 56 (burden 270, armour level 130) at 1,485
        var bare = GearFloor.Plan(den, new GearFloorState(10, 2, true, "Pathwarden Robe", 10_000, HandsCovered: false), Floor);
        var line = Assert.Single(bare.Lines);
        Assert.Equal((GearFloorKind.Gauntlets, 56u, 1485, "Shopkeeper Renald the Elder"), (line.Kind, line.Wcid, line.Price, line.Vendor.Name));
        Assert.Null(bare.Stop);
        Assert.Empty(GearFloor.Plan(den, new GearFloorState(10, 2, true, "Pathwarden Robe", 10_000), Floor).Lines);

        var poor = GearFloor.Plan(den, new GearFloorState(10, 2, true, "Pathwarden Robe", 1_484, HandsCovered: false), Floor);
        Assert.Empty(poor.Lines);
        Assert.Contains("short of the Leather Gauntlets's 1485", Assert.Single(poor.Notes), StringComparison.Ordinal);

        var outskirts = GearFloor.Plan(Ground, new GearFloorState(10, 2, true, "Pathwarden Robe", 10_000, HandsCovered: false), Floor); // M6 7 AH: Renald's list on the outskirts holds the pair now (closes TODO(m6-no-gauntlet-vendor) for every ladder ground)
        Assert.Equal((GearFloorKind.Gauntlets, 56u, 1485), (Assert.Single(outskirts.Lines).Kind, outskirts.Lines[0].Wcid, outskirts.Lines[0].Price));
        var withoutPair = Ground with { Vendors = [.. Ground.Vendors.Select(v => v with { Sells = [.. v.Sells.Where(w => !GearFloor.IsGauntletWare(w.Wcid))] })] };
        var noVendor = GearFloor.Plan(withoutPair, new GearFloorState(10, 2, true, "Pathwarden Robe", 10_000, HandsCovered: false), Floor);
        Assert.Empty(noVendor.Lines);
        Assert.Contains("TODO(m6-no-gauntlet-vendor)", Assert.Single(noVendor.Notes), StringComparison.Ordinal);
        Assert.True(GearFloor.IsGauntletWare(56));
        Assert.False(GearFloor.IsGauntletWare(5851));
    }

    private static uint Own(VendorRig rig, string name, uint wcid, uint itemType, uint container)
    {
        var guid = rig.NextGuid();
        rig.Arrive(guid, name, wcid, itemType, 10);
        rig.Put(guid, container);
        return guid;
    }
}
