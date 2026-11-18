using System.Collections.Immutable;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Housing;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;
using DatReaderWriter.Enums;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 16 E5c, Q11: the town trip's sale list is the rule book's verdict (<c>SaleVerdict</c>). A home holds a mixed pack (an item for each row a whole trip can reach) and one trip runs over it; the items offered, the lines it logs,
/// the sale memory and what is left in the pack are the ones the table's rows give. (Until Q11 a second trip made by the old filters was held beside it and every other trip of the assembly was compared the same way; the live
/// trips and the tests agreed, and the old list went.)
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint MixedMajorFocus = 2574;
    private const uint MixedReview = 0x8E5C0101, MixedRotation = 0x8E5C0102;

    /// <summary>The mixed pack's items by a name the test asks for.</summary>
    private sealed class MixedPack
    {
        public required FloorHome Floor { get; init; }

        public Dictionary<string, uint> Guid { get; } = new(StringComparer.Ordinal);

        public Home Home => Floor.Home;
    }

    private static void AppraiseSpells(Home home, uint guid, params uint[] spells)
    {
        var r = home.Vendor.R;
        r.H.R.Reducer.OnIdentifyResponse(r.H.R.Context, new GameEventHeader(VendorRig.Player, ++_appraisalSeq, 0x00C9), new IdentifyResponseMsg(
            guid, IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.SpellBook, true, ImmutableDictionary<uint, int>.Empty, ImmutableDictionary<uint, long>.Empty, ImmutableDictionary<uint, bool>.Empty,
            ImmutableDictionary<uint, double>.Empty, ImmutableDictionary<uint, string>.Empty, ImmutableDictionary<uint, uint>.Empty, [.. spells], null, null, null, null, null, null, null, null));
    }

    /// <summary>
    /// The climber at the outskirts' home (the robe and the Training Wand worn, no focus held) with a pack that holds an item for each row a trip can reach there: the sale's keeps, the table's classes and the guards. The same
    /// calls make the same guids in every home.
    /// </summary>
    private static MixedPack Mixed()
    {
        var pack = new MixedPack { Floor = new FloorHome(coins: 3_000) };
        var home = pack.Home;
        var side = home.Vendor.State.Self.Inventory.SidePacks.First(s => s.ContainerType == 1).Guid;
        uint InSide(string name, uint wcid, uint type, uint value)
        {
            var guid = home.Vendor.NextGuid();
            home.Vendor.Arrive(guid, name, wcid, type, value);
            home.Vendor.Put(guid, side);
            return guid;
        }

        uint Fixed(uint guid, string name, uint wcid, uint type, uint value)
        {
            home.Vendor.Arrive(guid, name, wcid, type, value);
            home.Vendor.Put(guid);
            return guid;
        }

        var g = pack.Guid;
        g["side ruby"] = InSide("Ruby", 9_101, 0x800, 500); // side-pack-held
        g["side kit"] = InSide("Adept Healing Kit", 629, MiscType, 50); // healing-kits, from a side pack
        g["side essence"] = InSide("Frost Wisp Essence (50)", 9_102, MiscType, 4_000); // essences, from a side pack
        g["spare wand"] = pack.Floor.OwnSpareWand(); // gear-caster-kept: not appraised, never sold blind
        g["focus ring"] = home.Own("Ring of Focus", 9_103, 0x8, 700); // kept-aside-match
        AppraiseSpells(home, g["focus ring"], MixedMajorFocus);
        g["writ"] = home.Own("Writ of Refuge", VillaFacts.WritWcid, MiscType, 100); // quest-item
        g["review"] = Fixed(MixedReview, "Covenant Sollerets", 9_104, 0x2, 9_000); // gear-review
        g["noted"] = home.Own("Odd Find", 9_105, MiscType, 300); // kept-aside-noted
        g["stone"] = home.Own("Greater Mana Stone", 2436, 0x80000, 5_000); // mana-stones: not appraised, kept
        g["rotation"] = Fixed(MixedRotation, "Old Helm", 9_106, 0x2, 800); // gear-rotation
        g["boots"] = home.Own("Leather Boots", 115, ClothingType, 1_100); // gear-leather-set: she wears the robe the set replaces
        g["tincture"] = home.Own("Health Tincture", HealthDrinks.TinctureWcid, MiscType, 6_000, stack: 12, maxStack: 100); // drinks-in-use
        g["pea"] = home.Own("Silver Pea", 8331, FociComponents.ComponentType, 25_000); // a component: no focus is held, so components-focus-missing
        g["dropped"] = home.Own("Refused Twice", 9_107, MiscType, 40); // dropped-before
        g["portal gem"] = home.Own("Facility Hub Portal Gem", 9_108, 0x800, 10); // travel-gem
        g["favor"] = home.Own("Blackmoor's Favor", 31000, 0x800, 0); // self-buff-gem
        g["bound"] = home.Own("Bound Token", 9_109, MiscType, 0); // value-zero-attuned
        Appraise(home, g["bound"], ints: new() { [114] = 1 });
        g["sash"] = home.Own("Pathwarden Sash", 9_110, ClothingType, 0); // value-zero-gear
        g["oil"] = home.Own("Oil of Rendering", 12711, MiscType, 0); // value-zero-drop: the junk drop's
        g["kit"] = home.Own("Gifted Healing Kit", 630, MiscType, 120); // healing-kits
        g["essence"] = home.Own("Acid Spectre Essence (80)", 49422, MiscType, 5_000); // essences
        g["ust"] = home.Own("Ust", 20646, 0x20000000, 10); // tinkering-tool: the junk drop's
        g["salvage"] = home.Own("Salvaged Iron", 9_111, 0x40000000, 30); // tinkering-salvage
        g["scroll"] = home.Own("Scroll of Dual Wield Mastery Self VI", 9_112, 0x2000, 1_000); // scrolls
        g["shirt"] = home.Own("Shirt", 130, ClothingType, 100); // gear-loot
        g["khanjar"] = home.Own("Flaming Khanjar", 3828, 0x1, 2_740); // gear-loot
        g["garnet"] = home.Own("Black Garnet", 2394, 0x800, 373); // trade-goods
        g["strange"] = home.Own("Strange Thing", 9_113, 0x80000000, 50); // unknown
        g["attuned"] = home.Own("Attuned Cap", 9_114, ClothingType, 50); // attuned-kept
        Appraise(home, g["attuned"], ints: new() { [114] = 1 });
        g["retained"] = home.Own("Retained Glove", 9_115, ClothingType, 40); // retained-kept
        Appraise(home, g["retained"], bools: new() { [91] = true });
        home.Memory.KeptAside.Add(g["noted"]);
        home.Memory.Dropped.Add(g["dropped"]);
        return pack;
    }

    private static readonly Dictionary<uint, SpellInfo> MixedSpells = new() { [MixedMajorFocus] = WorldActionRig.Spell(MixedMajorFocus, SpellType.Enchantment) with { Name = "Major Focus" } };

    /// <summary>One trip over the mixed pack: what it offered, every line it logged, its fields, its memory and what it left held.</summary>
    private static (List<uint> Offered, List<string> Lines, string Fields, List<uint> KeptAside, List<uint> Held, MixedPack Pack, ResupplyRun Run) MixedTrip()
    {
        var pack = Mixed();
        var run = pack.Home.Start(new ResupplyOptions(true, () => [], Memory: pack.Home.Memory), MixedSpells, character: MageTemplate.ClimberName);
        Assert.Equal(ScenarioStatus.Succeeded, pack.Floor.Run(run));
        var inventory = pack.Home.Vendor.State.Self.Inventory;
        return (
            [.. pack.Home.Vendor.H.Net.Of(FakeSendKind.Sell).SelectMany(s => s.Items ?? []).Select(i => i.item)],
            [.. pack.Home.Lines.Where(l => !l.Contains("cantrip keep (", StringComparison.Ordinal))], // a piece's cantrip verdicts are said once a process, not once a trip: the second home's trip does not say them again
            run.VerdictFields(),
            [.. pack.Home.Memory.KeptAside.Order()],
            [.. pack.Guid.Values.Where(inventory.Contains).Order()],
            pack,
            run);
    }

    [Fact]
    public void M6_16_E5c_the_verdicts_sale_list_over_a_mixed_pack_is_the_tables_rows_item_for_item()
    {
        PackMemory.Shared.WearReview.Add(MixedReview);
        PackMemory.Shared.Rotation.Add(MixedRotation);
        try
        {
            var verdict = MixedTrip();

            // and the list is the one the table's rows give, by name
            var g = verdict.Pack.Guid;
            string[] sold = ["side kit", "side essence", "kit", "essence", "salvage", "scroll", "shirt", "khanjar", "garnet"];
            string[] dropped = ["oil", "ust"];
            Assert.Equal(sold.Select(n => g[n]).Order(), verdict.Offered.Order());
            Assert.Equal(g.Where(p => !sold.Contains(p.Key) && !dropped.Contains(p.Key)).Select(p => p.Value).Order(), verdict.Held);
            Assert.Equal(sold.Length, verdict.Run.Sold);
            Assert.Equal(2, verdict.Run.JunkDropped);
            Assert.Contains(verdict.Lines, l => l.Contains("sale: kept 6 (1 attuned, 1 retained, 4 value 0)", StringComparison.Ordinal));
            Assert.Contains(verdict.Lines, l => l.Contains("sale: 'Ring of Focus'", StringComparison.Ordinal) && l.Contains("is kept aside (", StringComparison.Ordinal));
            Assert.Contains(verdict.Lines, l => l.Contains("pack rule unknown (Unknown): keep 'Strange Thing'", StringComparison.Ordinal));
            Assert.Contains(g["spare wand"], verdict.KeptAside);
            Assert.Contains(g["focus ring"], verdict.KeptAside);

            // M6 16 E5c2: the junk drop asks the verdict too (both trips': it is no part of the sale list), and on this pack it drops the two items the table alone dropped; its shadow went with its own rule
            Assert.DoesNotContain("sale_shadow", verdict.Fields, StringComparison.Ordinal);
            Assert.DoesNotContain(verdict.Lines, l => l.Contains("sale-shadow:", StringComparison.Ordinal));
        }
        finally
        {
            PackMemory.Shared.WearReview.Remove(MixedReview);
            PackMemory.Shared.Rotation.Remove(MixedRotation);
        }
    }

    [Fact]
    public void M6_16_Q11_the_trips_list_still_offers_a_class_ACE_refuses_and_no_parity_token_is_left_in_its_fields()
    {
        var pack = new FloorHome(coins: 3_000);
        var home = pack.Home;
        home.Own("Bread", VendorStock.BreadWcid, 0x20, 5); // Food, inside the shopkeeper's mask: the table's row sells it, ACE refuses the class
        home.Own("Arrow", VendorStock.ArrowWcid, 0x100, 40);
        var run = home.Start(new ResupplyOptions(true, () => [], Memory: home.Memory), MixedSpells, character: MageTemplate.ClimberName);
        Assert.Equal(ScenarioStatus.Succeeded, pack.Run(run));

        // the trip's context offers them as it always did (SaleContext.OffersRefusedClasses stays): the refusal is ACE's, and a second trip's refusal drops them; their rows say sell, not unsellable-class
        Assert.Contains(home.Lines, l => l.Contains("sell 'Bread'", StringComparison.Ordinal));
        Assert.Contains(home.Lines, l => l.Contains("sell 'Arrow'", StringComparison.Ordinal));
        Assert.DoesNotContain(home.Lines, l => l.Contains("unsellable class", StringComparison.Ordinal));
        Assert.DoesNotContain("sale_parity", run.VerdictFields(), StringComparison.Ordinal);
        Assert.DoesNotContain(home.Lines, l => l.Contains("sale-parity", StringComparison.Ordinal));
    }

    private const uint MixedBolt = 9_801;
    private const uint MixedStones = 0x8E5C0200;

    /// <summary>A spell of her book whose base formula holds one scarab (component 1): with every focus held, its foci formula is what her book burns.</summary>
    private static readonly Dictionary<uint, SpellInfo> MixedDenSpells = new() { [MixedBolt] = WorldActionRig.Spell(MixedBolt, SpellType.Enchantment) with { Formula = [1, 0, 0, 0, 0, 0, 0, 0] } };

    /// <summary>
    /// One trip of the den's mage: every focus held and a read spellbook (so the components rule sells), the stock with one line that is neither a component nor a drink, a kit line in the pack, five
    /// appraised mana stones (the keep is four) and a component of no value.
    /// </summary>
    private static (List<uint> Offered, List<string> Lines, string Fields, List<uint> KeptAside, List<uint> Held, Dictionary<string, uint> Guid, ResupplyRun Run) MixedDenTrip()
    {
        var den = GroundCatalog.Get(Den);
        var ground = den with { Supplies = den.Supplies with { Stock = [.. den.Supplies.Stock, new StockLine(515, "Superb Lockpick", 5, 2, 0)] } };
        var home = new Home(Den, 5_000, description: new PlayerDescriptionMsg { Spells = ImmutableDictionary<uint, float>.Empty.Add(MixedBolt, 2f) }, ground: ground);
        var g = new Dictionary<string, uint>(StringComparer.Ordinal);
        foreach (var (name, wcid) in new[] { ("Foci of Enchantment", 15268u), ("Foci of Artifice", 15269u), ("Foci of Verdancy", 15270u), ("Foci of Strife", 15271u) })
        {
            g[name] = home.Own(name, wcid, MiscType, 500); // foci-held
        }

        g["pea"] = home.Own("Silver Pea", 8331, FociComponents.ComponentType, 25_000); // components-pea
        g["iron"] = home.Own("Iron Scarab", 689, FociComponents.ComponentType, 10); // components-scarab-unused
        g["pink"] = home.Own("Pink Taper", 1649, FociComponents.ComponentType, 10); // components-account-only
        g["free pea"] = home.Own("Copper Pea", 8326, FociComponents.ComponentType, 0); // value-zero-kept: the rule sells a pea, ACE refuses one without a value
        g["robe"] = home.Own("Faran Robe with Hood", 5851, ClothingType, 900); // supplies-kit
        g["tusk"] = home.Own("Tusker Tusk", 99_001, MiscType, 300); // trade-goods
        for (var i = 0u; i < 5; i++)
        {
            var stone = MixedStones + i;
            home.Vendor.Arrive(stone, "Lesser Mana Stone", 2434, 0x80000, 100);
            home.Vendor.Put(stone);
            Appraise(home, stone); // read, and empty: the keep is four, the fifth is shed
            g["stone " + i] = stone;
        }

        var run = home.Start(new ResupplyOptions(true, () => [], Memory: home.Memory), MixedDenSpells);
        Assert.Equal(ScenarioStatus.Succeeded, home.Run(run, 4_000));
        var inventory = home.Vendor.State.Self.Inventory;
        return (
            [.. home.Vendor.H.Net.Of(FakeSendKind.Sell).SelectMany(s => s.Items ?? []).Select(i => i.item)],
            [.. home.Lines],
            run.VerdictFields(),
            [.. home.Memory.KeptAside.Order()],
            [.. g.Values.Where(inventory.Contains).Order()],
            g,
            run);
    }

    [Fact]
    public void M6_16_E5c_with_every_focus_held_the_verdicts_list_sells_the_components_and_the_stone_beyond_the_keep()
    {
        try
        {
            var verdict = MixedDenTrip();

            var g = verdict.Guid;
            string[] sold = ["pea", "iron", "pink", "tusk", "stone 4"]; // the stones are equal and empty: the four with the lowest guids are the keep
            Assert.Equal(sold.Select(n => g[n]).Order(), verdict.Offered.Order());
            Assert.Equal(g.Where(p => !sold.Contains(p.Key)).Select(p => p.Value).Order(), verdict.Held);
            Assert.Contains(verdict.Lines, l => l.Contains("pack rule components-pea: sell 'Silver Pea'", StringComparison.Ordinal));
            Assert.Contains(verdict.Lines, l => l.Contains("is a mana stone beyond the kit's target of 4 (the charged first): sell loot (M6 14)", StringComparison.Ordinal));
            Assert.Contains(verdict.Lines, l => l.Contains("sale: kept 1 (1 value 0)", StringComparison.Ordinal));
            Assert.Contains("components_offered=4", verdict.Fields, StringComparison.Ordinal); // the components rule's sell verdicts, the pea without a value among them (the guard keeps it; the count is the rule's, as before)
        }
        finally
        {
            for (var i = 0u; i < 5; i++)
            {
                PackMemory.Shared.Shed.Remove(MixedStones + i);
            }
        }
    }
}
