using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Messages;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 7 AU (the owner: acquire the foci of the other schools; the account-formula case was missed because a starter focus exists only for the schools trained at creation): the plan buys the Foci of Enchantment, then of Artifice, once the
/// Burrow gate's conditions are met, each school trained and the focus not held, at the price the real rule gives (SellPrice 50 x Value 500 = 25,000, Vendor.cs 577-585 @95ef426d); the cashing of a note and the keep of an unsellable potion are proved at the station.
/// </summary>
public sealed class FociPlanTests
{
    private const uint Enchantment = 15268;
    private const uint Artifice = 15269;

    private static HuntingGround Ground => GroundCatalog.Get("holtburg-outskirts");

    private static PlayerQualities Winnie(bool creature = true, bool item = true)
    {
        var q = new PlayerQualities { IsKnown = true };
        q.Skills[MageTemplate.CreatureEnchantment] = new SkillDesc(MageTemplate.CreatureEnchantment, 21, 1, creature ? 2u : 1u, 0, 0u, 0, 0);
        q.Skills[MageTemplate.ItemEnchantment] = new SkillDesc(MageTemplate.ItemEnchantment, 21, 1, item ? 2u : 1u, 0, 0u, 0, 0);
        return q;
    }

    private static FocusNeed? Next(PlayerQualities q, bool gateMet = true, HashSet<uint>? held = null, HashSet<uint>? refused = null) =>
        FociPlan.Next(Ground, LadderLoader.WarMage, ScrivenerCatalog.Default, q, (_, _) => gateMet, w => (held ?? []).Contains(w) ? 1 : 0, refused ?? [], out _);

    [Fact]
    public void The_real_price_is_twenty_five_thousand_each_at_the_two_home_scriveners()
    {
        Assert.Equal(25_000, Ground.PriceOf(Enchantment));
        Assert.Equal(25_000, Ground.PriceOf(Artifice));
        Assert.Equal(20205u, Ground.VendorSelling(Enchantment)!.Wcid); // the Apprentice Scrivener of Creature Magic
        Assert.Equal(20215u, Ground.VendorSelling(Artifice)!.Wcid); // the Apprentice Scrivener of Item and War Magic
        Assert.Equal([Enchantment, Artifice, 15270u, 15271u], ScrivenerCatalog.Default.Foci.Select(f => f.Wcid)); // M6 14 pack rules: Verdancy and Strife too, so a lost focus is bought back (docs/pack-rules.md)
    }

    [Fact]
    public void Enchantment_first_then_Artifice_then_nothing_once_both_are_held()
    {
        Assert.Equal(Enchantment, Next(Winnie())!.Focus.Wcid);
        Assert.Equal(Artifice, Next(Winnie(), held: [Enchantment])!.Focus.Wcid);
        Assert.Null(Next(Winnie(), held: [Enchantment, Artifice]));
    }

    [Fact]
    public void The_foci_wait_for_what_the_Burrow_gate_names_a_school_that_is_not_trained_and_a_refused_focus()
    {
        Assert.Null(Next(Winnie(), gateMet: false)); // the gate's scrolls and skills come first
        Assert.Equal(Artifice, Next(Winnie(creature: false))!.Focus.Wcid); // no Creature Enchantment: the Enchantment focus is no use
        Assert.Null(Next(Winnie(creature: false, item: false)));
        Assert.Equal(Artifice, Next(Winnie(), refused: [Enchantment])!.Focus.Wcid); // refused once: not tried again
    }

    [Fact]
    public void The_trigger_and_the_reason_name_the_focus_and_the_taper_stock_line_stays()
    {
        var need = Next(Winnie())!;
        var verdict = new LogisticsVerdict(LogisticsKind.Trip, LogisticsCause.Foci, null, 0, 0, 0, 0) { Focus = need };
        Assert.Equal("foci:Foci of Enchantment", verdict.Trigger);
        Assert.StartsWith("foci Foci of Enchantment: ", verdict.Reason, StringComparison.Ordinal);
        Assert.Contains(Ground.Supplies.Stock, l => l.Wcid == GearKit.PrismaticTaperWcid && l.TripUnder > 0 && l.StopUnder > 0); // the taper line and its trip stay: the foci formula burns tapers
        Assert.Equal(10_000, Ground.Supplies.Purse.MinNote); // M6 8 I (it was 250,000, M6 7 AU)
    }
}
