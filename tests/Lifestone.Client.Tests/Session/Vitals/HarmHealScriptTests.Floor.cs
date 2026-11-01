using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>The harm levels the pack can pay for (M5 3 gate round 3: the level floor over the foci formula, as the buff cycle's).</summary>
public sealed partial class HarmHealScriptTests
{
    [Fact]
    public void A_mage_on_the_foci_formula_never_casts_a_harm_level_whose_scarab_the_pack_lacks()
    {
        // Live round 3: Harm Self VII and VI landed, then Harm Self II was refused with 0x0400 (the ground's kit carries no Iron Scarab). Here every level's formula is one scarab (Lead to Pyreal for I to VI,
        // component 110 for VII), and the pack holds the life focus, the tapers and the scarabs of VI and VII only.
        var rig = new C3HarmRig(Mage());
        var harm = VitalsMeans.HarmSelf;
        for (var i = 0; i < harm.Count; i++)
        {
            rig.Inner.Table[harm[i]] = rig.Inner.Table[harm[i]] with { Formula = [i < 6 ? (uint)(i + 1) : 110u] };
        }

        Assert.True(ComponentWeenies.TryGetByComponent(6, out var pyreal));
        Assert.True(ComponentWeenies.TryGetByComponent(110, out var seventh));
        Give(rig, 0x80000F40, GearKit.FociWcids[2], "Foci of Verdancy", 1);
        Give(rig, 0x80000F41, pyreal.Wcid, pyreal.Name, 50);
        Give(rig, 0x80000F42, seventh.Wcid, seventh.Name, 50);
        Give(rig, 0x80000F43, 20631, "Prismatic Taper", 200); // the foci formula's tapers (GearKit.FociFormulaCounts)

        var state = rig.RunToEnd();
        Assert.True(state == HarmHealState.Done, state + ": " + rig.Script.VerdictLine() + " | casts " + string.Join(' ', rig.Inner.Casts.Select(c => c.Spell)));

        var harms = rig.Inner.Casts.Select(c => c.Spell).Where(harm.Contains).ToList();
        Assert.NotEmpty(harms);
        Assert.All(harms, s => Assert.True(s is 1171 or 2071, "only VI and VII are payable: " + string.Join(' ', harms)));
        Assert.Contains(rig.R.H.Log.Entries, e => e.Message.Contains("Iron Scarab 689, which the pack does not hold; skipped (the level floor)", StringComparison.Ordinal));
    }

    private static void Give(C3HarmRig rig, uint guid, uint wcid, string name, int count)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        rig.R.H.R.Reducer.OnCreateObject(
            rig.R.H.R.Context,
            basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Container, ContainerId = WorldActionRig.Player, WeenieClassId = wcid, ItemType = 0x1000, StackSize = (ushort)count } },
            isUpdate: false);
        rig.R.Server((player, sequence) => AceM3Writers.PutObjInContainer(player, sequence, guid, WorldActionRig.Player, 0, 0));
        rig.R.Tick();
    }
}
