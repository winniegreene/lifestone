using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M5 4 G1, the keeper's and the harm-heal script's Stamina to Mana Self through the level floor (<see cref="LevelFloor"/>): the live gate's dance fell to level V (spell 1680, a Gold Scarab no stock line carries) at mana 1 of 415 and ACE
/// refused it 0x0400. The buff cycle's share is in <c>StaminaToManaFloorCycleTests</c> (the ground kits, both dungeons); here every level's formula is one scarab (Lead to Pyreal for I to VI, component 110 for VII) and the pack
/// holds the life focus, the tapers and the scarabs of VI and VII only.
/// </summary>
public sealed class StaminaToManaFloorTests
{
    private static readonly uint[] Levels = [.. VitalsMeans.StaminaToManaSelf];

    /// <summary>VI (1681) and VII (2345): the levels the pack pays for.</summary>
    private static readonly uint[] Payable = [1681, 2345];

    private static void OneScarabEach(Dictionary<uint, SpellInfo> table)
    {
        for (var i = 0; i < Levels.Length; i++)
        {
            table[Levels[i]] = table[Levels[i]] with { Formula = [i < 6 ? (uint)(i + 1) : 110u] };
        }
    }

    private static (uint Wcid, int Stack)[] PackOfSixAndSeven()
    {
        Assert.True(ComponentWeenies.TryGetByComponent(6, out var pyreal));
        Assert.True(ComponentWeenies.TryGetByComponent(110, out var seventh));
        return [(GearKit.FociWcids[2], 1), (pyreal.Wcid, 50), (seventh.Wcid, 50), (20631, 200)];
    }

    private static C2Character Mage(bool pack) => new() { Spells = Levels, Mana = 25, LifeMagicRanks = 400, Pack = pack ? PackOfSixAndSeven() : [] };

    [Fact]
    public void M5_4_G1_the_keepers_dance_and_transfer_never_press_a_level_the_pack_lacks_the_scarab_for_and_rest_for_the_mana_instead()
    {
        // Without a focus in the pack there is no floor: at mana 25 the dance presses a low level (the live pick, the level the mana can land).
        var open = new C2VitalsRig(Mage(pack: false));
        OneScarabEach(open.Table);
        open.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 150));
        open.RunToEnd();
        var openCasts = open.Casts.Select(c => c.Spell).Where(Levels.Contains).ToList();
        Assert.Contains(openCasts, id => !Payable.Contains(id)); // the cause: the level the low mana can buy is a low one, which the ground's kit does not carry

        // With the life focus in the pack the foci formula applies: only VI and VII are pressed, and a look at the mana order has nothing to press below them, so the keeper rests (regeneration) until one is affordable.
        var rig = new C2VitalsRig(Mage(pack: true));
        OneScarabEach(rig.Table);
        rig.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 150));
        var state = rig.RunToEnd();

        Assert.Equal(VitalsKeeperState.Ready, state);
        var casts = rig.Casts.Select(c => c.Spell).Where(Levels.Contains).ToList();
        Assert.NotEmpty(casts);
        Assert.All(casts, id => Assert.Contains(id, Payable));
        Assert.Contains(rig.R.H.Log.Entries, e => e.Severity == Lifestone.Client.Diagnostics.LogSeverity.Warning && e.Message.Contains("skipped (the level floor)", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.R.H.Log.Entries, e => e.Severity >= Lifestone.Client.Diagnostics.LogSeverity.Error);
    }

    [Theory]
    [InlineData(VitalsLine.HealSelf)]
    [InlineData(VitalsLine.RevitalizeSelf)]
    public void M5_4_G1_the_keepers_Heal_Self_and_Revitalize_Self_levels_go_through_the_level_floor_too(VitalsLine line)
    {
        // One scarab a level (Lead to Pyreal for I to VI, component 110 for VII). The pack holds the life focus, the tapers and the Silver Scarab (component 4, level IV) only: the rule's level (VII by chance) is not payable, IV is.
        var ids = VitalsMeans.IdsOf(line);
        var payableId = ids[3];
        Assert.True(ComponentWeenies.TryGetByComponent(4, out var silver));
        (uint, int)[] pack = [(GearKit.FociWcids[2], 1), (silver.Wcid, 50), (20631, 200)];
        C2Character Hurt(bool packed) => new() { HealingClass = 1, Caster = true, LifeMagicRanks = 400, Spells = [.. ids], Health = line == VitalsLine.HealSelf ? 60u : 200u, Stamina = line == VitalsLine.RevitalizeSelf ? 40u : 200u, Mana = 200, Pack = packed ? pack : [] };

        List<uint> Casts(bool packed, out C2VitalsRig rig)
        {
            rig = new C2VitalsRig(Hurt(packed));
            for (var i = 0; i < ids.Count; i++)
            {
                rig.Table[ids[i]] = rig.Table[ids[i]] with { Formula = [i < 6 ? (uint)(i + 1) : 110u] };
            }

            rig.Keeper.NeedsFor(new VitalsNeeds());
            rig.RunToEnd();
            return [.. rig.Casts.Select(c => c.Spell).Where(ids.Contains)];
        }

        var open = Casts(packed: false, out _);
        Assert.Contains(open, id => id != payableId); // no focus in the pack, no floor: the rule's level is cast (the one ACE refuses 0x0400 on the foci formula)

        var floored = Casts(packed: true, out var rig);
        Assert.NotEmpty(floored);
        Assert.All(floored, id => Assert.Equal(payableId, id));
        Assert.Contains(rig.R.H.Log.Entries, e => e.Severity == Lifestone.Client.Diagnostics.LogSeverity.Warning && e.Message.StartsWith("vitals: ", StringComparison.Ordinal) && e.Message.EndsWith("skipped (the level floor)", StringComparison.Ordinal));
        Assert.Equal(1, rig.R.H.Log.Entries.Count(e => e.Message.Contains(" VII needs ", StringComparison.Ordinal) && e.Message.EndsWith("skipped (the level floor)", StringComparison.Ordinal))); // named once
    }

    [Fact]
    public void M5_4_G1_the_harm_heal_scripts_mana_top_up_never_casts_a_Stamina_to_Mana_level_the_pack_lacks_the_scarab_for()
    {
        List<uint> Transfers(bool pack, out C3HarmRig rig)
        {
            rig = new C3HarmRig(new C2Character { HealingClass = 1, LifeMagicRanks = 600, Spells = [.. VitalsMeans.HarmSelf, .. VitalsMeans.HealSelf, .. Levels], Mana = 25, Pack = pack ? PackOfSixAndSeven() : [] });
            OneScarabEach(rig.Inner.Table);
            var harm = VitalsMeans.HarmSelf;
            for (var i = 0; i < harm.Count; i++)
            {
                rig.Inner.Table[harm[i]] = rig.Inner.Table[harm[i]] with { Formula = [i < 6 ? (uint)(i + 1) : 110u] };
            }

            rig.RunToEnd();
            return [.. rig.Inner.Casts.Select(c => c.Spell).Where(Levels.Contains)];
        }

        // No focus in the pack, no floor: the mana of 25 buys one of the low levels (the pick that ACE refuses 0x0400 on the foci formula).
        Assert.Contains(Transfers(pack: false, out _), id => !Payable.Contains(id));

        // The life focus in the pack, the scarabs of VI and VII only: nothing below them is cast; with the mana under VI's cost there is no payable level, named once, and the script's harm cap decides.
        var casts = Transfers(pack: true, out var rig);
        Assert.All(casts, id => Assert.Contains(id, Payable));
        Assert.Contains(rig.R.H.Log.Entries, e => e.Message.Contains("harm-heal: Stamina to Mana Self V needs Gold Scarab 687, which the pack does not hold; skipped (the level floor)", StringComparison.Ordinal));
        Assert.Contains(rig.R.H.Log.Entries, e => e.Message.Contains("no Stamina to Mana Self can be cast (Stamina to Mana Self: no level the pack can pay for (the level floor))", StringComparison.Ordinal));
    }
}
