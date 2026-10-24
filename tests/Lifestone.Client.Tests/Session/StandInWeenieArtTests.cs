using System.Collections.Immutable;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;


namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 4.1 lane v4 (D27): the stand-in's items carry their weenie's own Setup and icons from <see cref="StandInWeenieArt"/> instead of the placeholder icon 0x06000001. The rows are the world database's (the lead's read-only export), so the
/// tests pin the five the plan names, the wire bits of an overlay, the table's reach (every weenie the stand-in serves has a row) and that every item description the stand-in sends carries a non-placeholder icon.
/// </summary>
public sealed class StandInWeenieArtTests
{
    private const uint Placeholder = 0x06000001;

    [Theory]
    [InlineData(628u, 0x020002FAu, 0x060032EFu)] // the Handy Healing Kit (healingkitcrude)
    [InlineData(377u, 0x020000ABu, 0x060032D6u)] // the Potion of Healing (healthpotion)
    [InlineData(273u, 0x02000B77u, 0x0600229Fu)] // the Pyreal (coinstack)
    [InlineData(8552u, 0x020009ADu, 0x06001EEDu)] // The Healer's Heart (orbhealer)
    [InlineData(307u, 0x02000129u, 0x06001599u)] // the stand-in's bow (the Shortbow, bowshort)
    public void The_rows_of_the_kit_the_potion_the_pyreal_the_Healers_Heart_and_the_bow_are_the_weenies_own(uint wcid, uint setup, uint icon)
    {
        Assert.True(StandInWeenieArt.TryGet(wcid, out var art));
        Assert.Equal(setup, art.Setup);
        Assert.Equal(icon, art.Icon);
        Assert.Equal(0u, art.Underlay);
        Assert.Equal(0u, art.Overlay);
        Assert.NotEqual(Placeholder, art.Icon);
    }

    [Fact]
    public void Every_row_has_a_setup_and_a_surface_icon_and_no_row_names_the_placeholder_icon()
    {
        foreach (var wcid in StandInWeenieArt.Wcids)
        {
            Assert.True(StandInWeenieArt.TryGet(wcid, out var art));
            Assert.Equal(0x02000000u, art.Setup & 0xFF000000u);
            Assert.Equal(0x06000000u, art.Icon & 0xFF000000u);
            Assert.NotEqual(Placeholder, art.Icon);
            Assert.True(art.Underlay == 0 || (art.Underlay & 0xFF000000u) == 0x06000000u);
            Assert.True(art.Overlay == 0 || (art.Overlay & 0xFF000000u) == 0x06000000u);
        }
    }

    [Fact]
    public void An_overlay_sets_the_first_headers_bit_and_the_icon_ids_land_in_the_description()
    {
        // The Foci of Enchantment (15268) is a row with an overlay.
        Assert.True(StandInWeenieArt.TryGet(15268, out var art));
        Assert.NotEqual(0u, art.Overlay);
        var weenie = StandInWeenieArt.Apply(Bare(15268), null);
        Assert.Equal(art.Icon, weenie.IconId);
        Assert.Equal(art.Overlay, weenie.IconOverlayId);
        Assert.True(weenie.Header.HasFlag(WeenieHeaderFlags.IconOverlay));
        Assert.Null(weenie.IconUnderlayId);
        Assert.Equal(WeenieHeaderFlags2.None, weenie.Header2);
    }

    [Fact]
    public void A_class_id_the_table_lacks_keeps_the_placeholder_icon_and_logs_the_TODO_once_with_the_wcid()
    {
        var log = new RecordingLogSink();
        const uint unknown = 4_000_123;
        var first = StandInWeenieArt.Apply(Bare(unknown), log);
        var second = StandInWeenieArt.Apply(Bare(unknown), log);
        Assert.Equal(Placeholder, first.IconId);
        Assert.Equal(Placeholder, second.IconId);
        var lines = log.Entries.Where(static e => e.Message.Contains("TODO(m4-standin-art-missing)", StringComparison.Ordinal)).ToList();
        var line = Assert.Single(lines);
        Assert.Contains("4000123", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_table_reaches_every_weenie_the_stand_in_serves()
    {
        var served = new HashSet<uint>();
        served.UnionWith(StandInWorld.KitWeenies.Keys);
        served.UnionWith(ComponentWeenies.All.Select(static c => c.Wcid));
        served.UnionWith(GearKit.FociWcids);
        served.UnionWith(StandInWorld.VendorCatalog.Keys);
        served.UnionWith(DecoyKit.All.Select(static d => d.Wcid));
        served.UnionWith([StandInChest.Wcid, StandInWorld.BowlWcid, StandInWorld.SmockWcid, RecordedDescription.PackWcid, StandInWorld.ThelnothWcid, StandInWorld.RenaldWcid, StandInItemKind.PyrealWcid]);
        served.UnionWith([StandInWorld.HandyKitWcid, StandInWorld.HealingPotionWcid, Lifestone.Client.Session.Fighter.FighterKit.MaceWcid, Lifestone.Client.Session.Fighter.FighterKit.ShortbowWcid, Lifestone.Client.Session.Fighter.FighterKit.ArrowWcid]);
        served.UnionWith([DeathDropModel.ManaScarabWcid, GearKit.PrismaticTaperWcid]);
        var missing = served.Where(static w => !StandInWeenieArt.TryGet(w, out _)).Order().ToList();
        Assert.True(missing.Count == 0, "no row for: " + string.Join(", ", missing));
    }

    [Fact]
    public void The_kit_weenies_carry_their_rows_setup_unless_a_worn_piece_names_its_own()
    {
        foreach (var (wcid, kit) in StandInWorld.KitWeenies)
        {
            Assert.True(StandInWeenieArt.TryGet(wcid, out var art), $"row {wcid}");
            // The six pieces and the wand keep the Setup the stand-in's dresser pinned (StandInKitLookTests); a component takes the table's.
            if (ComponentWeenies.All.Any(c => c.Wcid == wcid))
            {
                Assert.Equal(art.Setup, kit.SetupId);
            }

            Assert.NotEqual(StandInVisit.HumanSetup, kit.SetupId);
        }
    }

    [Fact]
    public void The_healing_kit_and_potions_of_the_login_burst_carry_their_icons_and_setups()
    {
        var burst = StandInWorld.HealItems(0x50000001);
        Assert.Equal(2, burst.Objects.Length);
        var kit = burst.Objects.Single(static o => o.Weenie.WeenieClassId == 628);
        var potion = burst.Objects.Single(static o => o.Weenie.WeenieClassId == 377);
        Assert.Equal(0x060032EFu, kit.Weenie.IconId);
        Assert.Equal(0x020002FAu, kit.Physics.SetupId);
        Assert.Equal(0x060032D6u, potion.Weenie.IconId);
        Assert.Equal(0x020000ABu, potion.Physics.SetupId);
    }

    [Fact]
    public void The_vendor_items_the_player_is_announced_with_carry_their_icons()
    {
        var burst = StandInWorld.WithVendorItems(new HealItemBurst(ImmutableArray<ContentDesc>.Empty, ImmutableArray<CreateObjectMsg>.Empty), 0x50000001);
        Assert.Equal(3, burst.Objects.Length);
        foreach (var o in burst.Objects)
        {
            Assert.NotEqual(Placeholder, o.Weenie.IconId);
            Assert.True(StandInWeenieArt.TryGet(o.Weenie.WeenieClassId, out var art));
            Assert.Equal(art.Icon, o.Weenie.IconId);
            Assert.Equal(art.Setup, o.Physics.SetupId);
        }
    }

    [Fact]
    public void The_placeholder_pack_and_its_pyreal_carry_their_icons()
    {
        var recorded = RecordedDescription.Of("x.lsrec", new Lifestone.Net.Messages.PlayerDescriptionMsg { Inventory = [] }, 0x50000077, []);
        var burst = recorded.SidePacks(0x50000001);
        Assert.True(burst.Added);
        var pack = Assert.Single(burst.Extra);
        var coin = Assert.Single(burst.Contents);
        Assert.Equal(0x06001BAFu, pack.Weenie.IconId);
        Assert.Equal(0x0600229Fu, coin.Weenie.IconId);
    }

    [Fact]
    public void Every_item_the_stand_in_creates_in_the_pack_carries_a_non_placeholder_icon_and_a_vendor_and_chest_object_too()
    {
        var items = StandInWorld.KitWeenies.ToDictionary(static kv => kv.Key, static kv => kv.Value.Kind);
        items[StandInItemKind.PyrealWcid] = new StandInItemKind("Pyreal", 25_000);
        var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, Items = items, HealItems = true, Vendors = true };
        using var rig = new CastRig(visit);
        rig.Begin();
        rig.Run(2.0);
        var made = new List<uint>();
        foreach (var wcid in items.Keys.Take(StandInWorld.PackCapacity - 8))
        {
            var spawn = rig.Session.Tools.SpawnItem(wcid, null, ActionSource.Host);
            rig.Run(0.5);
            Assert.True(spawn.IsCompleted, $"wcid {wcid} after {made.Count} items");
            Assert.NotNull(spawn.Result);
            made.Add(spawn.Result!.Guid);
        }

        rig.Run(1.0);
        Assert.NotEmpty(made);
        foreach (var guid in made)
        {
            Assert.True(rig.Session.State.Objects.TryGet(guid, out var item));
            Assert.NotEqual(Placeholder, item.Weenie.IconId);
            Assert.True(StandInWeenieArt.TryGet(item.Weenie.WeenieClassId, out var art));
            Assert.Equal(art.Icon, item.Weenie.IconId);
        }

        // The login burst's own objects: the kit, the potions, the coin and the two vendors' NPCs (their icon is the human's).
        var carried = rig.Session.State.Objects.All.Where(static o => o.Weenie.WeenieClassId is 628 or 377 or 273 or 36 or 715 or 718).ToList();
        Assert.NotEmpty(carried);
        Assert.All(carried, static o => Assert.NotEqual(Placeholder, o.Weenie.IconId));
        Assert.DoesNotContain(rig.Log.Entries, static e => e.Message.Contains("TODO(m4-standin-art-missing)", StringComparison.Ordinal));
    }

    [Fact]
    public void An_item_the_table_lacks_wears_the_placeholder_icon_and_setup_and_the_TODO_names_it()
    {
        const uint odd = 9_999_001;
        var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0, Items = new Dictionary<uint, StandInItemKind> { [odd] = new("Odd Thing", null) } };
        using var rig = new CastRig(visit);
        rig.Begin();
        rig.Run(2.0);
        var spawn = rig.Session.Tools.SpawnItem(odd, null, ActionSource.Host);
        rig.Run(2.0);
        Assert.True(spawn.IsCompleted);
        Assert.True(rig.Session.State.Objects.TryGet(spawn.Result!.Guid, out var item));
        Assert.Equal(Placeholder, item.Weenie.IconId);
        Assert.Equal(StandInWorld.PlaceholderItemSetup, item.Physics.SetupId);
        Assert.Contains(rig.Log.Entries, static e => e.Message.Contains("TODO(m4-standin-art-missing)", StringComparison.Ordinal) && e.Message.Contains("9999001", StringComparison.Ordinal));
    }

    [Fact]
    public void The_stand_in_chest_is_weenie_143s_chest_setup_and_icon_not_a_human()
    {
        var chest = StandInChest.Make(0x7A9B4100);
        Assert.Equal(0x0200007Cu, chest.Setup);
        Assert.NotEqual(StandInVisit.HumanSetup, chest.Setup);
        var message = StandInSessions.ObjectMessage(chest, default);
        Assert.Equal(0x06001022u, message.Weenie.IconId);
        Assert.Equal(0x0200007Cu, message.Physics.SetupId);
    }

    private static WeenieDesc Bare(uint wcid) =>
        new() { Header = WeenieHeaderFlags.None, Name = "t", WeenieClassId = wcid, IconId = Placeholder, ItemType = 0x80, ObjectDescriptionFlags = 0 };
}

/// <summary>The icons the dats' SpellComponentTable gives against the weenies' own (needs the dats).</summary>
public sealed class StandInWeenieArtComponentIconTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresLocalDatsFact]
    public void The_components_table_icon_is_the_weenies_icon_for_all_but_two_components_where_the_dat_differs()
    {
        var table = SpellComponentTableData.Load(fixture.Dats);
        var differing = new List<string>();
        foreach (var c in ComponentWeenies.All)
        {
            Assert.True(StandInWeenieArt.TryGet(c.Wcid, out var art), $"row {c.Wcid}");
            Assert.True(table.Components.TryGetValue(c.ComponentId, out var info), $"component {c.ComponentId}");
            if (info.IconId != art.Icon)
            {
                differing.Add($"{c.Name}: table 0x{info.IconId:X8} weenie 0x{art.Icon:X8}");
            }
        }

        // The dat's table gives Powdered Azurite the Powdered Agate's picture and the Banyan Talisman an older one; retail's components page draws the table's (gmSpellComponentUI::MakeIcon 0x0048A250), so those two rows differ from the weenie's icon.
        Assert.Equal(["Banyan Talisman", "Powdered Azurite"], differing.Select(static d => d[..d.IndexOf(':', StringComparison.Ordinal)]).Order().ToArray());
    }
}
