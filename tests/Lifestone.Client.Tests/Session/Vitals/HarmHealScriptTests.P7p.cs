using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M5 7 P7p, the item-end gate's journey-archer harm-heal (2026-10-31 00:54:57 UTC): the Potion of Healing was Confirmed ("restores 25 points", Drank 25), but a Gold Phyntos Wasp shocked the Archer for 39 in the same window, so the health
/// went 98 -&gt; 84 and the script, which judged only "the health rose", failed <c>why=potion-no-rise</c>. A heal is now judged on what it restored (D141), and the window's hits are logged.
/// </summary>
public sealed partial class HarmHealScriptTests
{
    [Fact]
    public void A_potion_Confirmed_with_a_restore_of_25_while_a_hit_takes_39_has_landed_and_the_window_logs_its_net_M5_7_P7p()
    {
        var rig = new C3HarmRig(Fighter(), FighterTemplate.Archer);
        rig.Inner.PotionWindowHit = 39;

        var state = rig.RunToEnd();

        var lines = rig.R.H.Log.Lines;
        Assert.True(state == HarmHealState.Done, "why=" + rig.Script.Why + " | " + string.Join(" | ", lines.Where(l => l.Contains("harm-heal", StringComparison.Ordinal))));
        Assert.DoesNotContain("potion-no-rise", rig.Script.VerdictLine(), StringComparison.Ordinal);
        Assert.Equal(1, rig.Script.OwnPotions);
        Assert.Equal(25, rig.Script.PotionPoints);
        var window = Assert.Single(lines, l => l.Contains("harm-heal: the potion landed: ", StringComparison.Ordinal));
        Assert.Contains("net -14: potion +25, hits -39 (1 hit(s))", window, StringComparison.Ordinal); // the gate's 98 -> 84
        Assert.Contains("the health did not rise", window, StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("harm-heal: a hit in the heal window: 39 point(s)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_potion_that_restores_nothing_and_raises_nothing_still_fails_potion_no_rise_M5_7_P7p()
    {
        var rig = new C3HarmRig(Fighter(), FighterTemplate.Melee);
        rig.Inner.PotionRestores = false;

        Assert.Equal(HarmHealState.Failed, rig.RunToEnd());

        Assert.Equal("potion-no-rise", rig.Script.Why);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("harm-heal: the potion did not land: ", StringComparison.Ordinal) && l.Contains("potion +0, hits -0", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_hit_the_potions_window_is_its_restore_M5_7_P7p()
    {
        var rig = new C3HarmRig(Fighter(), FighterTemplate.Melee);

        Assert.Equal(HarmHealState.Done, rig.RunToEnd());

        var window = Assert.Single(rig.R.H.Log.Lines, l => l.Contains("harm-heal: the potion landed: ", StringComparison.Ordinal));
        Assert.Contains("net +25: potion +25, hits -0 (0 hit(s))", window, StringComparison.Ordinal);
        Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("harm-heal: the kit landed: ", StringComparison.Ordinal)); // the kit is judged the same way
    }

    [Theory]
    [InlineData(true, 0, 0, true)] // the health rose
    [InlineData(false, 25, 0, true)] // Confirmed with restored 25 (the gate's potion), whatever the hits did
    [InlineData(false, 0, 52, true)] // the "restore N points" line of a Heal Self
    [InlineData(false, 0, 0, false)] // nothing restored, nothing rose: potion-no-rise, kit-no-rise, heal-no-rise
    public void A_heal_has_landed_when_the_health_rose_or_it_restored_points_M5_7_P7p(bool rose, int result, int line, bool landed) =>
        Assert.Equal(landed, HarmHealScript.HealLanded(rose, result, line));

    [Theory]
    [InlineData("The Potion of Healing restores 25 points of your Health.", true, 25)]
    [InlineData("You cast Heal Self VI and restore 52 points of your health.", true, 52)]
    [InlineData("The Potion of Healing restores 0 points of your Health.", true, 0)]
    [InlineData("The Mana Potion restores 25 points of your Mana.", false, 0)]
    [InlineData("You cast Harm Self IV and drain 21 points of your health.", false, 0)]
    [InlineData("Gold Phyntos Wasp shocks you for 39 points with Lightning Bolt III.", false, 0)]
    public void The_restore_line_is_read_for_the_health_only_M5_7_P7p(string text, bool read, int points)
    {
        Assert.Equal(read, HarmHealScript.TryRestoredHealth(text, out var n));
        Assert.Equal(points, n);
    }
}
