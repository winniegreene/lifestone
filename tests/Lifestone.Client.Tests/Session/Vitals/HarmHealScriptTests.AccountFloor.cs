using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M6 item 1 h2 (<c>m6-account-formula-callers</c>, the heal side): the harm-heal script's level floor reads the account's full formula for a pack that holds no Life Magic focus (the keeper carries the account name and the script
/// falls back to it). A made-up account name; every harm level's formula is one scarab, so the account's formula and the foci formula name the same scarab and differ only in the focus rule, which is what the test isolates.
/// </summary>
public sealed partial class HarmHealScriptTests
{
    private const string FloorAccount = "floor-test-account";

    /// <summary>The rig of <see cref="A_mage_on_the_foci_formula_never_casts_a_harm_level_whose_scarab_the_pack_lacks"/>: levels I to VI burn the scarab of component 1 to 6, VII component 110; the pack holds VI's and VII's scarabs, the tapers, and the focus when asked.</summary>
    private C3HarmRig FloorRig(bool focus, string? account)
    {
        var rig = new C3HarmRig(Mage());
        var harm = VitalsMeans.HarmSelf;
        for (var i = 0; i < harm.Count; i++)
        {
            rig.Inner.Table[harm[i]] = rig.Inner.Table[harm[i]] with { Formula = [i < 6 ? (uint)(i + 1) : 110u] };
        }

        Assert.True(ComponentWeenies.TryGetByComponent(6, out var pyreal));
        Assert.True(ComponentWeenies.TryGetByComponent(110, out var seventh));
        if (focus)
        {
            Give(rig, 0x80000F40, GearKit.FociWcids[2], "Foci of Verdancy", 1);
        }

        Give(rig, 0x80000F41, pyreal.Wcid, pyreal.Name, 50);
        Give(rig, 0x80000F42, seventh.Wcid, seventh.Name, 50);
        Give(rig, 0x80000F43, 20631, "Prismatic Taper", 200);
        rig.Inner.Keeper.AccountName = account;
        return rig;
    }

    private static List<uint> HarmCasts(C3HarmRig rig) => rig.Inner.Casts.Select(c => c.Spell).Where(VitalsMeans.HarmSelf.Contains).ToList();

    [Fact]
    public void With_the_Life_focus_held_the_account_changes_nothing_the_foci_floor_skips_the_scarabless_levels()
    {
        var rig = FloorRig(focus: true, FloorAccount);
        var state = rig.RunToEnd();
        Assert.True(state == HarmHealState.Done, state + ": " + rig.Script.VerdictLine());
        var harms = HarmCasts(rig);
        Assert.NotEmpty(harms);
        Assert.All(harms, s => Assert.True(s is 1171 or 2071, "only VI and VII are payable: " + string.Join(' ', harms)));
    }

    [Fact]
    public void Without_the_Life_focus_the_floor_applies_only_with_the_account_the_foci_rule_had_none_M6_account_formula_callers()
    {
        // No focus and no account name: today's rule, no floor for the school (the pack is cast on the account's formula, which could not be named).
        var before = FloorRig(focus: false, account: null);
        Assert.Equal(HarmHealState.Done, before.RunToEnd());
        Assert.NotEmpty(HarmCasts(before)); // the script picks the highest payable-looking level; with no floor nothing is passed over or named
        Assert.DoesNotContain(before.R.H.Log.Entries, e => e.Message.Contains("which the pack does not hold; skipped (the level floor)", StringComparison.Ordinal));

        // The same pack with the account name: the floor reads the account's formula and passes over the same levels.
        var after = FloorRig(focus: false, FloorAccount);
        var state = after.RunToEnd();
        Assert.True(state == HarmHealState.Done, state + ": " + after.Script.VerdictLine());
        var harms = HarmCasts(after);
        Assert.NotEmpty(harms);
        Assert.All(harms, s => Assert.True(s is 1171 or 2071, "only VI and VII are payable: " + string.Join(' ', harms)));
        Assert.Contains(after.R.H.Log.Entries, e => e.Message.Contains("Iron Scarab 689, which the pack does not hold; skipped (the level floor)", StringComparison.Ordinal));
    }
}
