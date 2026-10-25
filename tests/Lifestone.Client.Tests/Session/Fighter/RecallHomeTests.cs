using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>
/// The way home (M4 P5a C1c, <see cref="RecallHome"/>) on <see cref="C1cRecallRig"/>: the fighters swap the caster in, cast Lifestone Recall, wait for the teleport to the lifestone's landblock and swap back; the mage casts alone; every failure is
/// <c>recall:&lt;step&gt;</c> and the fighter ends with its weapon in hand.
/// </summary>
public sealed class RecallHomeTests
{
    private const string Heart = "The Healer's Heart";
    private const int Peace = 1;

    private static int At(C1cRecallRig t, string even) => t.Events.IndexOf(even);

    [Theory]
    [InlineData("melee", "Staff of All Aspects")]
    [InlineData("archer", "Serpent's Flight")]
    public void A_fighter_swaps_the_caster_in_casts_waits_for_the_teleport_and_swaps_the_weapon_back(string key, string weaponNamePrefix)
    {
        var template = FighterTemplate.ForKey(key)!;
        var t = new C1cRecallRig(template);
        Assert.Equal(RecallStatus.Done, t.RunRecall());

        // The order: the caster in the hand and the magic mode, then the cast, then the teleport and the arrival, then peace and the weapon back.
        var wieldCaster = At(t, "wield:" + Heart);
        var magic = At(t, "mode:8");
        var cast = At(t, "cast:1635");
        var teleport = At(t, "teleport");
        var present = At(t, "present");
        var peace = t.Events.IndexOf("mode:1");
        var unwieldCaster = At(t, "unwield:" + Heart);
        var wieldWeapon = t.Events.FindLastIndex(e => e.StartsWith("wield:", StringComparison.Ordinal) && e != "wield:" + Heart && e.Contains(weaponNamePrefix, StringComparison.Ordinal));
        Assert.True(wieldCaster >= 0 && wieldCaster < magic && magic < cast && cast < teleport && teleport < present && present < peace && peace < unwieldCaster && unwieldCaster < wieldWeapon, string.Join(" > ", t.Events));
        Assert.Equal(1, t.Casts);

        // The end state: the weapon in hand, the caster in the pack, peace; the fields count one recall.
        Assert.Equal(Peace, t.SwapRig!.CombatMode);
        Assert.Equal(0u, t.SwapRig.LocationOf(C1bSwapRig.WandGuid));
        Assert.NotEqual(0u, t.SwapRig.LocationOf(C1bSwapRig.WeaponGuid));
        Assert.Null(t.Recall.Failure);
        Assert.Equal(1, t.Recall.Recalls);
        Assert.Matches(@"^recalls=1 recall_s=\d+\.\d recall_fizzles=0$", t.Recall.Fields);
        Assert.True(t.Recall.Seconds is > 3 and < 60, t.Recall.Seconds.ToString());
        Assert.Equal(SelfTravel.Present, t.R.H.State.Self.Travel);
        Assert.Equal(0xA9B4u, t.R.H.State.Self.Position!.Value.CellId >> 16);
    }

    [Fact]
    public void The_mage_casts_alone_with_no_swap_and_no_mode_change()
    {
        var t = new C1cRecallRig(null);
        Assert.Null(t.Recall.Swap);
        Assert.Equal(RecallStatus.Done, t.RunRecall());

        Assert.Equal(["cast:1635", "teleport", "present"], t.Events);
        Assert.Empty(t.Caster.ModesSent); // already in the magic mode: nothing sent
        Assert.Equal(1, t.Recall.Recalls);
        Assert.Equal(CombatModes.Magic, t.R.H.State.Self.Qualities.CombatMode);
    }

    [Fact]
    public void Two_recalls_count_two_and_the_seconds_add_up()
    {
        var t = new C1cRecallRig(null);
        Assert.Equal(RecallStatus.Done, t.RunRecall());
        var first = t.Recall.Seconds;
        t.Events.Clear();
        Assert.Equal(RecallStatus.Done, t.RunRecall());
        Assert.Equal(2, t.Recall.Recalls);
        Assert.True(t.Recall.Seconds > first);
        Assert.Matches(@"^recalls=2 recall_s=\d+\.\d recall_fizzles=0$", t.Recall.Fields);
    }

    [Theory]
    [InlineData("melee")]
    [InlineData("archer")]
    public void A_character_bound_to_no_lifestone_fails_recall_cast_naming_ACEs_refusal_and_the_fighter_gets_its_weapon_back(string key)
    {
        var t = new C1cRecallRig(FighterTemplate.ForKey(key)!) { Bound = false };
        Assert.Equal(RecallStatus.Failed, t.RunRecall());

        Assert.Equal("recall:cast", t.Recall.Failure);
        Assert.Contains("YouMustLinkToLifestoneToRecall 0x049E", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Contains("WorldObject_Magic.cs:1087-1093", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Equal(0, t.Recall.Recalls);
        Assert.Matches(@"^recalls=0 recall_s=\d+\.\d recall_fizzles=0 recall_why=recall:cast$", t.Recall.Fields);
        Assert.DoesNotContain("teleport", t.Events);
        AssertWeaponBack(t);
    }

    [Fact]
    public void A_fizzle_is_counted_and_the_cast_retried_inside_the_cap_and_the_recall_arrives_P5a_R4()
    {
        var t = new C1cRecallRig(FighterTemplate.Melee) { FizzleFirst = 2 };
        Assert.Equal(RecallStatus.Done, t.RunRecall());

        Assert.Equal(3, t.Casts); // two fizzles, then the cast that landed: counted, not silent, not a failed recall
        Assert.Equal(2, t.Recall.Fizzles);
        Assert.Equal(1, t.Recall.Recalls);
        Assert.Null(t.Recall.Failure);
        Assert.Matches(@"^recalls=1 recall_s=\d+\.\d recall_fizzles=2$", t.Recall.Fields);
        Assert.Contains(t.R.H.Log.Lines, l => l.Contains("fizzled (fizzle 2 of at most", StringComparison.Ordinal));
        AssertWeaponBack(t);
    }

    [Fact]
    public void A_cast_that_fizzles_past_the_cap_is_recall_cast_with_the_code_named_and_the_weapon_comes_back()
    {
        var t = new C1cRecallRig(FighterTemplate.Melee) { Fizzle = true };
        Assert.Equal(RecallStatus.Failed, t.RunRecall());

        Assert.Equal("recall:cast", t.Recall.Failure);
        Assert.Contains("YourSpellFizzled 0x0402", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Contains($"over the cap of {RecallHome.MaxFizzles}", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Equal(RecallHome.MaxFizzles + 1, t.Casts); // the cap is the bound: five retried, the sixth fizzle fails it
        Assert.Equal(RecallHome.MaxFizzles + 1, t.Recall.Fizzles);
        AssertWeaponBack(t);
    }

    [Fact]
    public void A_recall_that_never_teleports_fails_recall_teleport_after_the_bound_and_restores_the_weapon()
    {
        var t = new C1cRecallRig(FighterTemplate.Archer) { NoTeleport = true };
        Assert.Equal(RecallStatus.Failed, t.RunRecall());

        Assert.Equal("recall:teleport", t.Recall.Failure);
        Assert.Contains("no teleport began within 20 s", t.Recall.Detail, StringComparison.Ordinal);
        Assert.InRange(t.Recall.Seconds, RecallHome.TeleportSeconds, RecallHome.MaxSeconds);
        AssertWeaponBack(t);
    }

    [Fact]
    public void A_teleport_that_leaves_the_player_in_portal_space_fails_recall_arrival()
    {
        var t = new C1cRecallRig(null) { StayInPortal = true };
        Assert.Equal(RecallStatus.Failed, t.RunRecall());

        Assert.Equal("recall:arrival", t.Recall.Failure);
        Assert.Contains("not present within 30 s", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Contains("TeleportArrived", t.Recall.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Arriving_in_another_landblock_is_recall_arrival_at_once_naming_both()
    {
        var t = new C1cRecallRig(FighterTemplate.Melee) { DestinationCell = 0x860201AD };
        Assert.Equal(RecallStatus.Failed, t.RunRecall());

        Assert.Equal("recall:arrival", t.Recall.Failure);
        Assert.Contains("0x8602", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Contains("0xA9B4", t.Recall.Detail, StringComparison.Ordinal);
        AssertWeaponBack(t);
    }

    [Fact]
    public void A_refused_caster_swap_in_is_recall_swap_in_with_nothing_cast_and_the_weapon_still_in_hand()
    {
        var t = new C1cRecallRig(FighterTemplate.Melee);
        t.SwapRig!.RefuseCaster = true;
        Assert.Equal(RecallStatus.Failed, t.RunRecall());

        Assert.Equal("recall:swap-in", t.Recall.Failure);
        Assert.Contains("swap:wield-caster", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Equal(0, t.Casts);
        AssertWeaponBack(t);
    }

    [Fact]
    public void A_player_in_portal_space_cannot_begin_a_recall_and_nothing_is_sent()
    {
        var t = new C1cRecallRig(null);
        t.R.H.R.Reducer.OnPlayerTeleport(t.R.H.R.Context, new PlayerTeleportMsg(5));
        var sent = t.R.H.Net.Sends.Count;

        Assert.True(t.Recall.Begin(t.Now));
        Assert.Equal(RecallStatus.Failed, t.Recall.Status);
        Assert.Equal("recall:present", t.Recall.Failure);
        Assert.Equal(sent, t.R.H.Net.Sends.Count);
        Assert.Equal(0, t.Casts);
    }

    [Fact]
    public void A_spellbook_without_Lifestone_Recall_is_recall_spell_and_nothing_is_cast()
    {
        var t = new C1cRecallRig(null, knowsSpell: false);
        Assert.True(t.R.H.State.Self.Spellbook.IsKnown); // the description arrived; it lists no Lifestone Recall
        Assert.Equal(RecallStatus.Failed, t.RunRecall());

        Assert.Equal("recall:spell", t.Recall.Failure);
        Assert.Contains("1635", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Equal(0, t.Casts);
    }

    [Fact]
    public void A_second_Begin_while_one_runs_is_refused_and_ForCharacter_knows_who_recalls_how()
    {
        var t = new C1cRecallRig(null);
        Assert.True(t.Recall.Begin(t.Now));
        Assert.False(t.Recall.Begin(t.Now));

        var log = t.R.H.Log;
        Assert.NotNull(RecallHome.ForCharacter(t.View, "Holtburg Brawler", log)!.Swap);
        Assert.NotNull(RecallHome.ForCharacter(t.View, "holtburg archer", log)!.Swap);
        Assert.Null(RecallHome.ForCharacter(t.View, "BSD Mage", log)!.Swap);
        Assert.Null(RecallHome.ForCharacter(t.View, "Holtburg Hunter", log));
        Assert.Null(RecallHome.ForCharacter(t.View, null, log));
    }

    [Fact]
    public void Primary_Portal_Recall_48_reuses_the_steps_and_arrives_in_the_den_landblock_M4_P5b_J1()
    {
        var t = new C1cRecallRig(null, spell: RecallHome.PrimaryPortalRecallSpell, homeLandblock: RecallHome.DenLandblock) { DestinationCell = 0x01050375 };
        Assert.Equal(48u, t.Recall.SpellId);
        Assert.Equal(0x0105u, t.Recall.HomeLandblock);
        Assert.Equal(RecallStatus.Done, t.RunRecall());

        Assert.Equal(["cast:48", "teleport", "present"], t.Events);
        Assert.Equal(1, t.Recall.Recalls);
        Assert.Equal(0u, t.Recall.Refusal);
        Assert.Equal(0x0105u, t.R.H.State.Self.Position!.Value.CellId >> 16);
        Assert.Contains(t.R.H.Log.Entries, e => e.Message.Contains("casting Primary Portal Recall (48)", StringComparison.Ordinal));

        // ForCharacter passes the spell and the landblock on (the journey's den travel).
        var viaName = RecallHome.ForCharacter(t.View, "BSD Mage", t.R.H.Log, RecallHome.DenLandblock, RecallHome.PrimaryPortalRecallSpell)!;
        Assert.Equal((48u, 0x0105u), (viaName.SpellId, viaName.HomeLandblock));
        Assert.Equal((1635u, 0xA9B4u), (RecallHome.ForCharacter(t.View, "BSD Mage", t.R.H.Log)!.SpellId, RecallHome.ForCharacter(t.View, "BSD Mage", t.R.H.Log)!.HomeLandblock));
    }

    [Fact]
    public void Primary_Portal_Recall_with_no_portal_tied_is_recall_cast_naming_0x04A3_and_the_refusal_is_readable_M4_P5b_J1()
    {
        var t = new C1cRecallRig(null, spell: RecallHome.PrimaryPortalRecallSpell, homeLandblock: RecallHome.DenLandblock) { Bound = false, UnboundRefusal = 0x04A3 };
        Assert.Equal(RecallStatus.Failed, t.RunRecall());

        Assert.Equal("recall:cast", t.Recall.Failure);
        Assert.Contains("YouMustLinkToPortalToRecall 0x04A3", t.Recall.Detail, StringComparison.Ordinal);
        Assert.Equal(0x04A3u, t.Recall.Refusal);
        Assert.Equal(0, t.Recall.Recalls);
        Assert.DoesNotContain("teleport", t.Events);
        Assert.Equal("Primary Portal Recall", RecallHome.SpellName(48));
        Assert.Equal("spell 7", RecallHome.SpellName(7));
    }

    [Theory]
    [InlineData(0x049Eu, "YouMustLinkToLifestoneToRecall")]
    [InlineData(0x049Fu, "YouFailToRecallToLifestone")]
    [InlineData(0x04A3u, "YouMustLinkToPortalToRecall")]
    [InlineData(0x04ADu, "YouCannotRecallPortal")]
    [InlineData(0x04CCu, "YouHaveBeenInPKBattleTooRecently")]
    [InlineData(0x0401u, "YouDontHaveEnoughManaToCast")]
    public void ACEs_recall_refusals_are_named_with_their_codes(uint code, string name)
    {
        var text = RecallHome.NameOfRefusal(code);
        Assert.NotNull(text);
        Assert.StartsWith(name + " 0x", text, StringComparison.Ordinal);
        Assert.Null(RecallHome.NameOfRefusal(0x1234));
    }

    private static void AssertWeaponBack(C1cRecallRig t)
    {
        // A failed recall leaves a fighter as it found it: the weapon in the hand, the caster in the pack, peace.
        Assert.Equal(Peace, t.SwapRig!.CombatMode);
        Assert.Equal(0u, t.SwapRig.LocationOf(C1bSwapRig.WandGuid));
        Assert.NotEqual(0u, t.SwapRig.LocationOf(C1bSwapRig.WeaponGuid));
    }
}
