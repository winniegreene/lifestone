using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 2 f (the review's 2-R2, 2-R3, 2-R4, 2-R15): the resupply's equip pass, return pass and floor-caster keep on the outskirts home of a climber (<see cref="FloorHome"/>), with ACE's side scripted: a wield answered or refused,
/// a Give taken (the piece leaves, the reward arrives) or not.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint HeadWear = 0x1, HandWear = 0x20, Head = 0x4000, Hands = 0x8000;

    /// <summary>The climber's outskirts with two casters (the floor buys none), a worn cap of armour level 100 (appraised) and a held cap of <paramref name="heldLevel"/>.</summary>
    private static (FloorHome Home, uint Worn, uint Held) CapsWorld(int heldLevel, bool refuseHeld = false)
    {
        var f = new FloorHome(level: 9); // M6 14 (D135): under the Burrow's 10, so no gate's armour lines are read (the wear pick reads the last gate at or below her level) and the numbers below are the bare outskirts'
        f.OwnSpareWand();
        var worn = f.Home.OwnPiece("Old Cap", 9_101, HeadWear, Head);
        f.Home.Vendor.R.Server((p, s) => AceM3Writers.WieldObject(p, s, worn, (int)HeadWear));
        var held = f.Home.OwnPiece("Better Cap", 9_102, HeadWear, Head);
        if (refuseHeld)
        {
            f.RefuseOnly.Add(held); // ACE refuses this one wield; the old cap's wield (the recovery) is answered
        }

        Appraise(f.Home, worn, ints: new() { [28] = 100 });
        Appraise(f.Home, held, ints: new() { [28] = heldLevel });
        return (f, worn, held);
    }

    [Fact]
    public void M6_2_f_a_looted_piece_the_judge_would_replace_with_is_worn_before_the_sale_and_not_sold()
    {
        // M6 14 (m6-armour): the wear pick weighs armour by the rung's hits: a cap of 150 over 100 against the outskirts' Mite Scion (hit 7) is 0.05% of her hour, under the swap margin 0.1, so it stays; one of 500 is 0.16%
        var (f, worn, held) = CapsWorld(heldLevel: 500);
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));
        Assert.True(f.Home.Logged("wear pick: beats what it displaces by more than the swap margin 0.1"), string.Join("\n", f.Home.Lines));
        Assert.True(f.Home.Logged("her numbers before:"), string.Join("\n", f.Home.Lines)); // check after: her skills and vitals read again

        Assert.Equal(InventoryPlaceKind.Equipped, f.Inventory.Find(held).Kind); // 2-R2: worn in the gear phase, with the old cap taken off first
        var sold = f.Home.Vendor.H.Net.Of(FakeSendKind.Sell).SelectMany(s => s.Items ?? []).Select(i => i.item).ToList();
        Assert.DoesNotContain(held, sold);
        Assert.Equal(1, run.EquipReplaced);
        Assert.True(f.Home.Logged("equip pass: replace: 'Better Cap'"), string.Join("\n", f.Home.Lines));
        Assert.NotEqual(InventoryPlaceKind.Equipped, f.Inventory.Find(worn).Kind);
    }

    [Fact]
    public void M6_2_f_a_replace_whose_wield_is_refused_puts_the_old_piece_back_on_and_names_the_refusal()
    {
        var (f, worn, held) = CapsWorld(heldLevel: 500, refuseHeld: true); // M6 14 (m6-armour): 500 beats the swap margin on the outskirts' hits (above)
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        f.Run(run);

        Assert.NotEqual(InventoryPlaceKind.Equipped, f.Inventory.Find(held).Kind);
        Assert.Equal(InventoryPlaceKind.Equipped, f.Inventory.Find(worn).Kind); // the recovery: the old cap is back on
        Assert.True(f.Home.Logged("equip pass: replace: 'Better Cap'") && f.Home.Logged("not worn (refused)"), string.Join("\n", f.Home.Lines));
        Assert.True(f.Home.Logged("equip pass: recovery: 'Old Cap'"), string.Join("\n", f.Home.Lines));
        Assert.Equal(0, run.EquipReplaced);
        Assert.True(System.Text.RegularExpressions.Regex.IsMatch(run.EquipLine ?? string.Empty, @"equip pass: worn=\d+ replaced=0 casters=\d+ refused=[1-9]"), string.Join(" | ", f.Home.Lines.Where(l => l.Contains("equip", StringComparison.Ordinal))));
        Assert.True(f.WieldsRefused >= 1);
    }

    [Fact]
    public void M6_2_f_a_trip_with_nothing_to_buy_or_sell_still_runs_the_equip_pass()
    {
        var f = new FloorHome(coins: 1_000, notes: false); // M6 item 14: a purse that does not pay for the leather set, so that is held back and there is nothing to buy
        f.OwnSpareWand(); // two casters, the robe worn, the stock full: nothing to buy
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.True(f.Home.Logged("resupply: nothing to buy or sell: no walk"), string.Join("\n", f.Home.Lines));
        Assert.NotNull(run.EquipLine); // 2-R2: the pass ran before the no-walk return
        Assert.StartsWith("equip pass: worn=0 replaced=0 casters=2 refused=0", run.EquipLine, StringComparison.Ordinal);
    }

    [Fact]
    public void M6_2_f_a_requirement_unmet_caster_is_not_a_floor_caster_the_floor_buys_the_Wand_and_the_sale_does_not_keep_it()
    {
        var f = new FloorHome();
        var heavy = f.OwnSpareWand();
        Appraise(f.Home, heavy, ints: new() { [AppraisalIds.ArcaneLore] = 9_999 }); // Arcane Lore far above the climber's: it cannot be used as the spare
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Equal(1, run.FloorBought); // 2-R3: casters held = the Training Wand only, so the Wand at Cindrue is bought
        Assert.False(f.Home.Logged("floor caster"), string.Join("\n", f.Home.Lines.Where(l => l.StartsWith("sale", StringComparison.Ordinal))));
    }

    private const uint Thorolf = 33596, ThorolfGuid = 0x7A9B4F01, Gauntlets = 33606, BagOfSteel = 33621;

    /// <summary>The climber with Better Gauntlets worn (armour level 300, no Give row) and the attuned Pathwarden Gauntlets held (level 100): outgrown, so the return pass offers them to Thorolf, who stands in reach or not.</summary>
    /// <param name="thorolfPresent">Whether Thorolf stands in reach.</param>
    /// <param name="betterDifficulty">M6 16 E5e6: the worn Better Gauntlets' ItemDifficulty (PropertyInt 109), or 0 for none: one far above the climber's Arcane Lore leaves their spells off for her.</param>
    private static (FloorHome Home, uint Piece) ReturnWorld(bool thorolfPresent, int betterDifficulty = 0)
    {
        var f = new FloorHome(coins: 1_000, notes: false); // M6 item 14: a purse that does not pay for the leather set: no walk to the blacksmith, she stays by Thorolf
        f.OwnSpareWand();
        var better = f.Home.OwnPiece("Better Gauntlets", 9_201, HandWear, Hands);
        f.Home.Vendor.R.Server((p, s) => AceM3Writers.WieldObject(p, s, better, (int)HandWear));
        Appraise(f.Home, better, ints: betterDifficulty > 0 ? new() { [28] = 300, [AppraisalIds.ArcaneLore] = betterDifficulty } : new() { [28] = 300 });
        var piece = f.Home.OwnPiece("Pathwarden Gauntlets", Gauntlets, HandWear, Hands, value: 0);
        Appraise(f.Home, piece, ints: new() { [28] = 100, [114] = 1 });
        if (thorolfPresent)
        {
            f.Home.PlaceNpc(ThorolfGuid, "Pathwarden Thorolf", Thorolf, 2f);
        }

        return (f, piece);
    }

    /// <summary>ACE's Give: the piece leaves the player and the reward (a bag of Steel) arrives, or nothing happens when <paramref name="take"/> is false.</summary>
    private static ScenarioStatus RunGive(FloorHome f, ResupplyRun run, bool take)
    {
        var seen = 0;
        return f.Home.Stations.Run(run, 900, pump: () =>
        {
            f.Pump();
            var sends = f.Home.Vendor.H.Net.Sends;
            for (; seen < sends.Count; seen++)
            {
                if (sends[seen].Kind == FakeSendKind.GiveObject && take)
                {
                    f.Home.Vendor.Remove(sends[seen].Guid);
                    f.Home.Own("Bag of Steel", BagOfSteel, 0x80, 50);
                }
            }
        });
    }

    private static string ReturnLines(FloorHome f) => string.Join("\n", f.Home.Lines.Where(l => l.StartsWith("return", StringComparison.Ordinal)));

    [Fact]
    public void M6_2_f_the_return_pass_gives_an_outgrown_attuned_piece_to_Thorolf_with_a_check_after_and_counts_it()
    {
        var (f, piece) = ReturnWorld(thorolfPresent: true);
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, RunGive(f, run, take: true));

        Assert.Single(f.Home.Vendor.H.Net.Of(FakeSendKind.GiveObject));
        Assert.False(f.Inventory.Contains(piece)); // the piece left
        Assert.Equal(1, run.Returned);
        Assert.Empty(run.ReturnKept);
        Assert.True(f.Home.Logged("return pass: given=1 kept=0"), ReturnLines(f));
    }

    [Fact]
    public void M6_2_f_a_give_that_is_not_taken_keeps_the_piece_names_it_and_is_not_retried()
    {
        var (f, piece) = ReturnWorld(thorolfPresent: true);
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, RunGive(f, run, take: false));

        Assert.Single(f.Home.Vendor.H.Net.Of(FakeSendKind.GiveObject)); // no retry
        Assert.True(f.Inventory.Contains(piece));
        Assert.Equal(0, run.Returned);
        Assert.Single(run.ReturnKept);
        Assert.True(f.Home.Logged("return pass: given=0 kept=1"), ReturnLines(f));
    }

    [Fact]
    public void M6_2_f_with_Thorolf_out_of_the_object_table_the_piece_is_kept_and_no_Give_is_sent()
    {
        var (f, piece) = ReturnWorld(thorolfPresent: false);
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        f.Home.Stations.Run(run, 900, pump: f.Pump);

        Assert.Empty(f.Home.Vendor.H.Net.Of(FakeSendKind.GiveObject));
        Assert.True(f.Inventory.Contains(piece));
        Assert.Equal(0, run.Returned);
        Assert.Single(run.ReturnKept);
        Assert.True(f.Home.Logged("return pass: given=0 kept=1"), ReturnLines(f));
    }

    [Fact]
    public void M6_16_E5e6_an_attuned_piece_the_worn_piece_beats_only_with_its_magic_off_is_kept_named_once_and_no_Give_is_sent()
    {
        var (f, piece) = ReturnWorld(thorolfPresent: true, betterDifficulty: 9_999); // Arcane Lore far above the climber's: the worn gauntlets' spells are off for her, and they score their armour (300 against 100)
        var run = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, RunGive(f, run, take: true)); // Thorolf stands in reach and would take it

        var lines = string.Join("\n", f.Home.Lines.Where(l => l.Contains("return", StringComparison.Ordinal)));
        Assert.Empty(f.Home.Vendor.H.Net.Of(FakeSendKind.GiveObject));
        Assert.True(f.Inventory.Contains(piece));
        Assert.Equal(0, run.Returned);
        Assert.Single(f.Home.Lines, l => l.Contains("guard: gear-return-unlit", StringComparison.Ordinal)); // once a piece a visit
        Assert.True(f.Home.Lines.Any(l => l.EndsWith("] gear: return: 'Pathwarden Gauntlets' kept: the worn 'Better Gauntlets' beats it only on armour, its magic is off for her (guard: gear-return-unlit)", StringComparison.Ordinal)), lines);
        Assert.StartsWith("return: kept Pathwarden Gauntlets (the worn 'Better Gauntlets' ", Assert.Single(run.ReturnKept), StringComparison.Ordinal);
        Assert.False(f.Home.Logged("goes back to"), lines);
        Assert.True(f.Home.Logged("return pass: given=0 kept=1"), lines);
    }
}
