using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 pack rules (<see cref="PackRuleBook"/>, docs/pack-rules.md): every class on the climber's live pack (a read-only SELECT of ace_shard, 2026-11-13 about 12:50 UTC: each kind of item it held, by weenie, ItemType and Value), the
/// table in the doc equal to the code's, and the four foci for sale at Holtburg's scriveners for the buy-back.
/// </summary>
public sealed class PackRuleBookTests
{
    private static readonly HuntingGround Abode = GroundCatalog.Get("tusker-abode");

    /// <summary>Each kind of item of the live pack: weenie, name, ItemType, Value, attuned, the rule the book must give.</summary>
    public static TheoryData<uint, string, uint, int, bool, string> LivePack => new()
    {
        { 3828, "Flaming Khanjar", 0x1, 2740, false, "gear-loot" },
        { 121, "Gloves", 0x4, 9669, false, "gear-loot" },
        { 297, "Ring", 0x8, 5832, false, "gear-loot" },
        { 273, "Pyreal", 0x40, 25000, false, "money-coin" },
        { 630, "Gifted Healing Kit", 0x80, 120, false, "healing-kits" },
        { 632, "Peerless Healing Kit", 0x80, 1000, false, "healing-kits" },
        { 2460, "Mana Draught", 0x80, 340, false, "drinks-in-use" },
        { 27319, "Health Tincture", 0x80, 6500, false, "drinks-in-use" },
        { 377, "Potion of Healing", 0x80, 5610, false, "drinks-in-use" },
        { 12711, "Oil of Rendering", 0x80, 0, false, "value-zero-drop" },
        { 15271, "Foci of Strife", 0x80, 500, false, "foci-held" },
        { 15268, "Foci of Enchantment", 0x80, 500, false, "foci-held" },
        { 49422, "Acid Spectre Essence (80)", 0x80, 5000, false, "essences" },
        { 150, "Flagon", 0x100, 363, false, "gear-loot" },
        { 136, "Pack", 0x200, 65, false, "containers-in-use" },
        { 2394, "Black Garnet", 0x800, 373, false, "trade-goods" },
        { 31000, "Blackmoor's Favor", 0x800, 0, true, "self-buff-gem" },
        { 49563, "Facility Hub Portal Gem", 0x800, 0, true, "travel-gem" },
        { 20458, "Scroll of Tempest", 0x2000, 2000, false, "scrolls" },
        { 515, "Superb Lockpick", 0x4000, 1200, false, "trade-goods" },
        { 2472, "Wand", 0x8000, 5658, false, "gear-loot" },
        { 2625, "Trade Note (10,000)", 0x40000, 440000, false, "money-notes" },
        { 2436, "Greater Mana Stone", 0x80000, 5000, false, "mana-stones" },
        { 20646, "Ust", 0x20000000, 10, false, "tinkering-tool" },
    };

    private static PackFacts Facts(uint wcid, string name, uint itemType, int value, bool attuned) =>
        new(wcid, name, itemType, value, Attuned: attuned, QuestItem: ResupplyRun.QuestItemWcids.Contains(wcid), Drink: ResupplyRun.DrinkInUse(wcid), BuyerTakes: Abode.Vendors.Any(v => v.Buys(itemType)));

    [Theory]
    [MemberData(nameof(LivePack))]
    public void Every_kind_of_item_in_the_live_pack_gets_its_rule(uint wcid, string name, uint itemType, int value, bool attuned, string rule)
    {
        var (cls, verdict) = PackRuleBook.Classify(Facts(wcid, name, itemType, value, attuned));

        Assert.Equal(rule, verdict.Rule);
        Assert.Equal(PackRuleBook.Of(rule).Class, cls);
    }

    [Fact]
    public void The_live_pack_sells_its_kits_and_essences_and_drops_its_junk_and_keeps_the_rest_it_names()
    {
        var actions = LivePack.ToDictionary(row => (string)row[1], row => PackRuleBook.Classify(Facts((uint)row[0], (string)row[1], (uint)row[2], (int)row[3], (bool)row[4])).Verdict.Action);

        Assert.Equal(PackAction.Sell, actions["Gifted Healing Kit"]);
        Assert.Equal(PackAction.Sell, actions["Acid Spectre Essence (80)"]);
        Assert.Equal(PackAction.Drop, actions["Oil of Rendering"]);
        Assert.Equal(PackAction.Drop, actions["Ust"]);
        Assert.Equal(PackAction.Keep, actions["Blackmoor's Favor"]); // ACE refuses both its sale and its drop
        Assert.Equal(PackAction.Keep, actions["Facility Hub Portal Gem"]);
        Assert.Equal(PackAction.Keep, actions["Foci of Strife"]);
    }

    [Fact]
    public void No_Holtburg_buyer_of_the_abode_takes_a_tinkering_tool_and_Renald_takes_a_healing_kit()
    {
        Assert.DoesNotContain(Abode.Vendors, v => v.Buys(0x20000000));
        Assert.Contains(Abode.Vendors, v => v.Name == "Shopkeeper Renald the Elder" && v.Buys(0x80));
        Assert.Contains(Abode.Vendors, v => v.Name == "Archmage Cindrue" && v.Buys(FociComponents.ComponentType));
    }

    [Fact]
    public void An_item_no_rule_names_and_no_buyer_takes_is_kept_and_named()
    {
        var (cls, verdict) = PackRuleBook.Classify(new PackFacts(99_999, "Strange Thing", 0x80000000, 50, BuyerTakes: false));

        Assert.Equal(PackClass.Unknown, cls);
        Assert.Equal(PackAction.Keep, verdict.Action);
        Assert.Contains("TODO(m6-pack-rule-unknown)", verdict.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_spare_empty_pack_is_sold_and_a_component_takes_the_components_rule()
    {
        Assert.Equal("containers-spare", PackRuleBook.Classify(new PackFacts(136, "Pack", 0x200, 65, ContainerInUse: false)).Verdict.Rule);
        var pea = FociComponents.Judge(8331, FociComponents.ComponentType, true, new Dictionary<uint, int> { [691] = 1 });
        var (cls, verdict) = PackRuleBook.Classify(new PackFacts(8331, "Silver Pea", FociComponents.ComponentType, 25000, Component: pea));
        Assert.Equal(PackClass.Components, cls);
        Assert.Equal(FociComponents.RulePea, verdict.Rule);
    }

    [Fact]
    public void Every_rule_of_the_code_is_a_row_of_the_doc_table()
    {
        var doc = File.ReadAllText(Path.Combine(RepoRoot(), "docs", "pack-rules.md"));
        string[] components = [FociComponents.RuleBurned, FociComponents.RuleStocked, FociComponents.RuleAccountOnly, FociComponents.RuleScarabUnused, FociComponents.RulePea, FociComponents.RuleFociMissing, FociComponents.RuleBookUnread, FociComponents.RuleUnknown];

        foreach (var name in PackRuleBook.Rules.Select(r => r.Name).Concat(components))
        {
            Assert.True(doc.Contains("| `" + name + "` |", StringComparison.Ordinal), $"docs/pack-rules.md has no table row for '{name}'");
        }

        Assert.Equal(PackRuleBook.Rules.Count, PackRuleBook.Rules.Select(r => r.Name).Distinct().Count());
    }

    [Fact]
    public void All_four_foci_are_sold_at_Holtburg_s_scriveners_for_25000_so_a_lost_one_is_bought_back()
    {
        var catalog = ScrivenerCatalog.Default;

        Assert.Equal(GearKit.MageFociWcids.Order(), catalog.Foci.Select(f => f.Wcid).Order());
        var holtburg = catalog.VendorsIn("Holtburg");
        foreach (var focus in catalog.Foci)
        {
            var seller = holtburg.First(v => v.Sells.Any(w => w.Wcid == focus.Wcid));
            Assert.Equal(0xA9B3u, seller.Cell >> 16);
            Assert.Equal(25_000, seller.Sells.Single(w => w.Wcid == focus.Wcid).Price);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lifestone.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("no repository root");
    }
}
