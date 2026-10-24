using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// The fighters' Mana and Stamina Potions on the scripted rig (M4 P5a C7, <see cref="C2VitalsRig"/>): the keeper drinks a Mana Potion before a Stamina to Mana cast or a rest, a Stamina Potion when the stamina is low, waits for the vital's update, and in
/// the fighters' order (<see cref="VitalsOptions.FighterOrder"/>) casts a transfer only at a high stamina with no potion left and within <see cref="VitalsKeeper.FighterTransferCap"/>. The numbers are round 5's Archer: health 189, stamina 174 of 238,
/// mana 135 of 291, Harm Self V's need 250.
/// </summary>
public sealed class VitalsKeeperPotionTests
{
    private const uint StaminaToManaIV = 1679;
    private const uint RevitalizeIV = 1180;
    private static readonly uint[] Spells = [StaminaToManaIV, RevitalizeIV];
    private static readonly VitalsOptions FighterOptions = VitalsOptions.Default.ForFighter();

    private static C2Character Archer(uint manaPotions = 0, uint staminaPotions = 0, uint stamina = 174, uint mana = 135) => new()
    {
        MaxHealth = 189, Health = 189, MaxStamina = 238, Stamina = stamina, MaxMana = 291, Mana = mana, Spells = Spells, ManaPotions = manaPotions, StaminaPotions = staminaPotions,
    };

    [Fact]
    public void A_mana_potion_is_drunk_until_the_next_cast_is_covered_before_any_transfer_or_rest_M4_P5a_C7()
    {
        var rig = new C2VitalsRig(Archer(manaPotions: 100), FighterOptions);

        rig.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 250));
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.Equal(["mana-potion", "mana-potion", "mana-potion", "mana-potion", "mana-potion"], rig.Used); // 135 + 5 x 25 = 260 >= 250: no cast, no mode change, no rest
        Assert.Equal((5, 0, 0, 0), (rig.Keeper.ManaPotions, rig.Keeper.Transfers, rig.Keeper.Rests, rig.Keeper.Revitalizes));
        Assert.Equal(95u, rig.ManaPotions);
        Assert.Contains("mana_potions=5 stamina_potions=0", rig.Keeper.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("Mana Potion 0x", StringComparison.Ordinal) && l.Contains("mana 135 of 291, 250 needed", StringComparison.Ordinal));
    }

    [Fact]
    public void A_stamina_potion_is_drunk_when_stamina_is_low_and_Revitalize_Self_is_not_cast_while_one_is_held_M4_P5a_C7()
    {
        var rig = new C2VitalsRig(Archer(staminaPotions: 50, stamina: 40), FighterOptions);

        rig.Keeper.NeedsFor(new VitalsNeeds());
        Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

        Assert.All(rig.Used, u => Assert.Equal("stamina-potion", u)); // 40 of 238 is under 30%: potions on to the 80% share (190.4), 7 drinks of 25
        Assert.Equal(7, rig.Keeper.StaminaPotions);
        Assert.Equal(0, rig.Keeper.Revitalizes);
        Assert.InRange(rig.Stamina, 215u, 220u);
    }

    [Fact]
    public void With_no_potion_left_a_fighter_with_a_high_stamina_casts_at_most_the_cap_of_transfers_and_never_Revitalize_Self_for_mana_M4_P5a_C7()
    {
        var rig = new C2VitalsRig(Archer(stamina: 238, mana: 40), FighterOptions);

        rig.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 250));
        rig.RunToEnd(maxSeconds: 60);

        Assert.InRange(rig.Keeper.Transfers, 1, VitalsKeeper.FighterTransferCap);
        Assert.Equal(0, rig.Keeper.Revitalizes);
        Assert.DoesNotContain("cast:" + RevitalizeIV, rig.Used);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("fighter mana order: no Mana Potion to drink", StringComparison.Ordinal));
    }

    [Fact]
    public void With_no_potion_and_a_stamina_under_the_high_share_a_fighter_rests_for_mana_and_casts_no_transfer_M4_P5a_C7()
    {
        var rig = new C2VitalsRig(Archer(stamina: 174, mana: 135), FighterOptions);

        rig.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 250));
        rig.RunToEnd(maxSeconds: 4);

        Assert.Equal(0, rig.Keeper.Transfers); // 174 of 238 is 73%, under the 80% a transfer needs
        Assert.Equal(1, rig.Keeper.Rests);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("not allowed", StringComparison.Ordinal) && l.Contains("about 575 s for 115 mana", StringComparison.Ordinal)); // 1 mana a 5 s tick
    }

    [Fact]
    public void A_mage_holding_no_potion_keeps_its_order_the_transfer_before_the_rest_M4_P5a_C7()
    {
        var rig = new C2VitalsRig(Archer(stamina: 174, mana: 135), VitalsOptions.Default);

        rig.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 250));
        rig.RunToEnd(maxSeconds: 60);

        Assert.True(rig.Keeper.Transfers >= 1);
    }

    [Fact]
    public void A_potion_whose_vital_update_never_comes_is_named_and_set_aside_after_the_bounded_wait_M4_P5a_C7()
    {
        var rig = new C2VitalsRig(Archer(manaPotions: 10, stamina: 174, mana: 135), FighterOptions);
        rig.PotionVitalLost = true;
        rig.ManaRegen = 0; // no regeneration tick can look like the vital's update

        rig.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 250));
        rig.RunToEnd(maxSeconds: 30);

        Assert.Equal(1, rig.Used.Count(u => u == "mana-potion")); // one drink, then the wait of PotionVitalWaitSeconds, then the potions are set aside (never a second drink on a stale mana); the order goes on to its fallback
        Assert.Equal(1, rig.Keeper.ManaPotions);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("did not change within 3 s of a confirmed Mana Potion", StringComparison.Ordinal));
    }
}
