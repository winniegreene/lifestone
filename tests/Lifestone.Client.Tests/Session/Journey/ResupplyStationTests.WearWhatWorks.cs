using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 16, slice E5e5 (the owner's decision of 2026-11-18: an unusable great-cantrip piece is kept, never sold and never dropped, and she wears what works): the town trip itself, with ACE's side scripted. One
/// visit takes a worn keeper ACE can never light off for a piece whose minor cantrip lights and keeps it through the sale and the junk drop; the next visit's sort, gear phase, sale and equip pass leave it in her
/// packs and never send a wield for it.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint WorksMajorFocus = 2574, WorksMinorFocus = 2581;

    private static readonly Dictionary<uint, SpellInfo> WorksSpells = new()
    {
        [WorksMajorFocus] = WorldActionRig.Spell(WorksMajorFocus, SpellType.Enchantment) with { Name = "Major Focus" },
        [WorksMinorFocus] = WorldActionRig.Spell(WorksMinorFocus, SpellType.Enchantment) with { Name = "Minor Focus" },
    };

    /// <summary>
    /// The climber at the outskirts' home with her skills described: Arcane Lore 275 (trained), Quickness and Coordination 225 each, so her untrained Missile Defense reads 90 (the attributes are the test's). She wears
    /// a helm with the numbers of the live log's Tenassa Breastplate as slice E5e4's tests rebuilt it (ItemDifficulty 240, a limit of 220 on Missile Defense; on the head here, for this home's robe holds the
    /// chest), casting Major Focus, and holds a leather cap of a quarter its armour that casts a Minor Focus she can light.
    /// </summary>
    private static (FloorHome Floor, uint Dead, uint Working) WearWhatWorksWorld()
    {
        var f = new FloorHome(level: 9, description: new PlayerDescriptionMsg
        {
            Attributes =
            [
                new AttributeDesc(MageTemplate.Strength, 0, 100, 0), new AttributeDesc(MageTemplate.Endurance, 0, 100, 0), new AttributeDesc(MageTemplate.Quickness, 0, 225, 0),
                new AttributeDesc(MageTemplate.Coordination, 0, 225, 0), new AttributeDesc(MageTemplate.Focus, 0, 100, 0), new AttributeDesc(MageTemplate.Self, 0, 100, 0),
            ],
            Skills = [new SkillDesc(MageTemplate.WarMagic, 100, 1, 3u, 0, 0u, 0, 0), new SkillDesc(MageTemplate.ArcaneLore, 242, 1, 2u, 0, 0u, 0, 0)],
        });
        f.Home.Vendor.State.Self.Name = MageTemplate.ClimberName;
        f.OwnSpareWand();
        var dead = f.Home.OwnPiece("Tenassa Helm", 9_201, HeadWear, Head, value: 2_000);
        f.Home.Vendor.R.Server((p, s) => AceM3Writers.WieldObject(p, s, dead, (int)HeadWear));
        var working = f.Home.OwnPiece("Leather Cap", 9_202, HeadWear, Head, value: 300);
        f.Ints[dead] = new()
        {
            [28] = 400, [5] = 400, [AppraisalIds.ManaCurrent] = 600, [AppraisalIds.ManaMax] = 600, [AppraisalIds.ArcaneLore] = 240,
            [SelfEquipJudge.ItemSkillLevelLimitInt] = 220, [SelfEquipJudge.AppraisalItemSkillInt] = 7,
        };
        f.Spells[dead] = [WorksMajorFocus];
        f.Ints[working] = new() { [28] = 100, [5] = 400, [AppraisalIds.ManaCurrent] = 600, [AppraisalIds.ManaMax] = 600, [AppraisalIds.ArcaneLore] = 100 };
        f.Spells[working] = [WorksMinorFocus];
        foreach (var guid in new[] { dead, working })
        {
            var r = f.Home.Vendor.R;
            r.H.R.Reducer.OnIdentifyResponse(r.H.R.Context, new GameEventHeader(VendorRig.Player, ++_appraisalSeq, 0x00C9), new IdentifyResponseMsg(
                guid, IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.SpellBook, true, f.Ints[guid].ToImmutableDictionary(), ImmutableDictionary<uint, long>.Empty, ImmutableDictionary<uint, bool>.Empty,
                ImmutableDictionary<uint, double>.Empty, ImmutableDictionary<uint, string>.Empty, ImmutableDictionary<uint, uint>.Empty, [.. f.Spells[guid]], null, null, null, null, null, null, null, null));
        }

        return (f, dead, working);
    }

    [Fact]
    public void M6_16_E5e5_the_trip_takes_a_keeper_ACE_can_never_light_off_for_a_piece_that_works_keeps_it_through_the_sale_and_the_next_visit_does_not_put_it_back()
    {
        var (f, dead, working) = WearWhatWorksWorld();
        var inventory = f.Inventory;
        Assert.Equal(dead, inventory.EquippedAt(HeadWear));
        Assert.True(CantripRoom.IsUsefulGreatPiece(f.Home.Vendor.State, dead, WorksSpells)); // a keeper before the visit
        List<uint> Sold() => [.. f.Home.Vendor.H.Net.Of(FakeSendKind.Sell).SelectMany(s => s.Items ?? []).Select(i => i.item)];

        // The first visit. Before this slice the gear phase answered "the worn 'Tenassa Helm' casts a great cantrip useful to her and this piece casts none: never taken off for it", and she went back out
        // with a Major Focus that never lit and the Minor Focus in her pack.
        var first = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true), WorksSpells, character: MageTemplate.ClimberName);
        Assert.True(f.Run(first) == ScenarioStatus.Succeeded, first.Failure + "\n" + string.Join("\n", f.Home.Lines));

        var lines = string.Join("\n", f.Home.Lines);
        Assert.True(f.Home.Logged("cantrips first: it raises her core cantrips by "), lines);
        Assert.True(f.Home.Logged("casts a great cantrip useful to her that can never light for her (its magic asks Missile Defense 220 and hers is 90: she does not hold Missile Defense trained and her plan does not train it"), lines);
        Assert.True(f.Home.Logged("equip pass: replace: 'Tenassa Helm' 0x" + dead.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + " off to the main pack"), lines);
        Assert.Equal(working, inventory.EquippedAt(HeadWear)); // she wears what works
        Assert.Equal(1, first.EquipReplaced);
        Assert.True(inventory.Contains(dead) && inventory.Find(dead).Kind != InventoryPlaceKind.Equipped, lines); // kept: in her packs
        Assert.DoesNotContain(dead, Sold()); // never offered to the sale
        Assert.DoesNotContain(f.Home.Vendor.H.Net.Of(FakeSendKind.DropItem), s => s.Guid == dead); // never dropped
        Assert.True(CantripRoom.IsUsefulGreatPiece(f.Home.Vendor.State, dead, WorksSpells)); // a keeper still
        var wieldsOfDead = f.Home.Vendor.H.Net.Of(FakeSendKind.GetAndWieldItem).Count(s => s.Guid == dead);
        Assert.Equal(0, wieldsOfDead); // the equip pass after the sale judged it again in the same visit and did not put it back

        // The next visit: the sort, the gear phase's pick over every held piece, the sale and the equip pass.
        f.Home.Vendor.StandAt(f.Home.Ground.Home.LifestoneCell, f.Home.Ground.Home.LifestonePoint); // she comes home again (the first visit's walks ended away from the lifestone)
        var second = f.Home.Start(new ResupplyOptions(true, () => [], Gear: true), WorksSpells, character: MageTemplate.ClimberName);
        Assert.True(f.Run(second) == ScenarioStatus.Succeeded, second.Failure + "\n" + string.Join("\n", f.Home.Lines));

        lines = string.Join("\n", f.Home.Lines);
        Assert.True(f.Home.Logged("packs: move 'Tenassa Helm' 0x" + dead.ToString("X8", System.Globalization.CultureInfo.InvariantCulture) + " main -> review pack"), lines); // the sorter's keep: a keeper goes to the review pack
        Assert.Contains(f.Home.Lines, l => l.Contains("gear: no gain from 'Tenassa Helm'", StringComparison.Ordinal) && l.Contains("cantrips first: it would lower her core cantrips by ", StringComparison.Ordinal)); // the pick over the review pack's piece: judged on what it gives
        Assert.Equal(working, inventory.EquippedAt(HeadWear));
        Assert.Equal(0, second.EquipReplaced);
        Assert.Equal(0, f.Home.Vendor.H.Net.Of(FakeSendKind.GetAndWieldItem).Count(s => s.Guid == dead)); // no wield of it was ever sent: no thrash
        Assert.True(inventory.Contains(dead) && inventory.Find(dead).Kind != InventoryPlaceKind.Equipped, lines);
        Assert.DoesNotContain(dead, Sold());
        Assert.DoesNotContain(f.Home.Vendor.H.Net.Of(FakeSendKind.DropItem), s => s.Guid == dead);
    }
}
