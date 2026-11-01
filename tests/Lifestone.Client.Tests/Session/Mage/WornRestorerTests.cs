using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M5 7 S2d (closes <c>TODO(m5-self-equip-death-rewear)</c>): the live case of 2026-10-29. After the gate's scripted death, BSD Mage logged in wearing the robe, the gauntlets, the Wand, the compass and the ring, but with the Shirt 0x800048F5 and the
/// Wide Breeches 0x800048F8 in the main pack and two Fire Sceptres in the review Sack. The restore (the start-up audit's, the death's) must end fully dressed with the best fire caster: from the worn set when one is remembered, else by
/// the free coverage.
/// </summary>
public sealed class WornRestorerTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Robe = 0x8000490A, Gauntlets = 0x8000490D, Wand = 0x80004907, Compass = 0x80003A64, Ring = 0x80002A12;
    private const uint Shirt = 0x800048F5, Breeches = 0x800048F8, Sack = 0x800048F9, Components = 0x8000490B, Kit = 0x8000490C;
    private const uint Fire1 = 0x80003EB7, Fire2 = 0x8000178B;

    private sealed class Mage : IGearSkills
    {
        public int Level => 80;

        public int AdvancementClass(uint skill) => 3;

        public int? Current(uint skill) => 330;

        public int? Base(uint skill) => 311;
    }

    private uint _seq = 900;

    private void Obj(WorldActionRig r, uint guid, string name, uint wcid, uint itemType, uint valid = 0, uint priority = 0, uint container = 0)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name, itemType: itemType);
        var header = WeenieHeaderFlags.ValidLocations | WeenieHeaderFlags.Priority | (container == 0 ? WeenieHeaderFlags.None : WeenieHeaderFlags.Container);
        r.H.R.Reducer.OnCreateObject(r.H.R.Context, basis with { Weenie = basis.Weenie with { Header = header, WeenieClassId = wcid, ValidLocations = valid, Priority = priority, ContainerId = container == 0 ? null : container } }, isUpdate: false);
    }

    private void Fire(WorldActionRig r, uint guid, double manaConversion)
    {
        r.H.R.Reducer.OnIdentifyResponse(r.H.R.Context, new GameEventHeader(Player, ++_seq, 0x00C9), new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable,
            true,
            new Dictionary<uint, int> { [45] = 0x10, [158] = 2, [159] = 34, [160] = 290 }.ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            new Dictionary<uint, double> { [AppraisalIds.ElementalDamageMod] = 1.03, [144] = manaConversion }.ToImmutableDictionary(),
            ImmutableDictionary<uint, string>.Empty,
            ImmutableDictionary<uint, uint>.Empty,
            [],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null));
    }

    private void Plain(WorldActionRig r, uint guid) => r.H.R.Reducer.OnIdentifyResponse(r.H.R.Context, new GameEventHeader(Player, ++_seq, 0x00C9), new IdentifyResponseMsg(
        guid, IdentifyResponseFlags.IntStatsTable, true, ImmutableDictionary<uint, int>.Empty, ImmutableDictionary<uint, long>.Empty, ImmutableDictionary<uint, bool>.Empty, ImmutableDictionary<uint, double>.Empty,
        ImmutableDictionary<uint, string>.Empty, ImmutableDictionary<uint, uint>.Empty, [], null, null, null, null, null, null, null, null));

    [Fact]
    public void A_login_with_no_appraisal_of_the_review_packs_sceptres_appraises_them_and_wields_the_best_fire_one()
    {
        // M5 7 S2e: live at 16:21:53 UTC the restore logged "none of the 0 usable caster(s) held": the session held appraisals of the worn pieces only.
        var memory = new WornMemory();
        var r = Login(appraised: false);
        var restorer = new WornRestorer(GroundCatalog.Get("black-spawn-den-aluvian"), 126, new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>(), r.H.Log, "autopilot", "the start-up audit", memory);

        Run(r, restorer);

        Assert.Contains(r.H.Net.Of(FakeSendKind.Identify), s => s.Guid == Fire1);
        Assert.Contains(r.H.Net.Of(FakeSendKind.Identify), s => s.Guid == Fire2);
        Assert.Equal(Fire1, r.H.State.Self.Inventory.EquippedAt(SelfEquipJudge.Held));
        Assert.Contains(r.H.Log.Entries, e => e.Message.StartsWith("gear: restore: appraising ", StringComparison.Ordinal));
        Assert.Contains(r.H.Log.Entries, e => e.Message.StartsWith("gear: restore: caster pick: 'Fire Sceptre' 0x80003EB7, the best of 2 usable caster(s) held", StringComparison.Ordinal));
    }

    /// <summary>The login of the live case: what the database held.</summary>
    private WorldActionRig Login(bool appraised = true)
    {
        var r = new WorldActionRig();
        r.Describe(
            [new ContentDesc(Shirt, 0), new ContentDesc(Breeches, 0), new ContentDesc(Components, 1), new ContentDesc(Kit, 1), new ContentDesc(Sack, 1)],
            [new EquippedDesc(Robe, 0x7F01, 0x17F00), new EquippedDesc(Gauntlets, 0x20, 0x8000), new EquippedDesc(Wand, SelfEquipJudge.Held, 0), new EquippedDesc(Compass, 0x04000000, 0), new EquippedDesc(Ring, 0x00080000, 0)]);
        Obj(r, Robe, "Faran Robe with Hood", 5851, 0x4, 0x7F01, 0x17F00);
        Obj(r, Gauntlets, "Leather Gauntlets", 56, 0x2, 0x20, 0x8000);
        Obj(r, Wand, "Wand", 2472, SelfEquipJudge.CasterType, SelfEquipJudge.Held);
        Obj(r, Compass, "Compass", 9_001, 0x8, 0x04000000);
        Obj(r, Ring, "Ring", 9_002, 0x8, 0x000C0000);
        Obj(r, Shirt, "Shirt", 130, 0x4, 0x1E, 0x68, Player); // the pinned world SQL: ValidLocations 30, ClothingPriority 104
        Obj(r, Breeches, "Wide Breeches", 2604, 0x4, 0x44, 0x13, Player); // ValidLocations 68, ClothingPriority 19
        Obj(r, Components, "Pack", 136, 0x200, 0, 0, Player);
        Obj(r, Kit, "Pack", 136, 0x200, 0, 0, Player);
        Obj(r, Sack, "Sack", 166, 0x200, 0, 0, Player);
        Obj(r, Fire1, "Fire Sceptre", 9_003, SelfEquipJudge.CasterType, SelfEquipJudge.Held, 0, Sack);
        Obj(r, Fire2, "Fire Sceptre", 9_004, SelfEquipJudge.CasterType, SelfEquipJudge.Held, 0, Sack);
        r.Raw(AceM3Writers.PutObjInContainer(Player, ++_seq, Fire1, Sack, 0, 0));
        r.Raw(AceM3Writers.PutObjInContainer(Player, ++_seq, Fire2, Sack, 1, 0));
        if (appraised)
        {
            Fire(r, Fire1, 0.04);
            Fire(r, Fire2, 0.03);
        }

        return r;
    }

    /// <summary>Steps the restorer and plays ACE's side: each wield answered with its 0x0023, each move with its 0x0022.</summary>
    private void Run(WorldActionRig r, WornRestorer restorer)
    {
        var view = new J3aView(r);
        var answered = 0;
        var identified = 0;
        for (var i = 0; i < 100; i++)
        {
            var running = restorer.Step(view, new Mage());
            r.Tick(0.1);
            var asks = r.H.Net.Of(FakeSendKind.Identify);
            for (; identified < asks.Count; identified++)
            {
                var guid = asks[identified].Guid;
                if (guid is Fire1 or Fire2)
                {
                    Fire(r, guid, guid == Fire1 ? 0.04 : 0.03); // ACE's 0x00C9 for the asked sceptre
                }
                else
                {
                    Plain(r, guid);
                }
            }

            var sends = r.H.Net.Sends.Where(s => s.Kind is FakeSendKind.GetAndWieldItem or FakeSendKind.PutItemInContainer).ToList();
            for (; answered < sends.Count; answered++)
            {
                var s = sends[answered];
                r.Raw(s.Kind == FakeSendKind.GetAndWieldItem
                    ? AceM3Writers.WieldObject(Player, ++_seq, s.Guid, (int)s.Amount)
                    : AceM3Writers.PutObjInContainer(Player, ++_seq, s.Guid, uint.Parse(s.Target!, NumberStyles.HexNumber, CultureInfo.InvariantCulture), 0, 0));
            }

            r.Tick(0.1);
            if (!running)
            {
                return;
            }
        }

        Assert.Fail("the restore did not end");
    }

    [Fact]
    public void The_login_with_the_shirt_and_breeches_in_the_pack_ends_fully_dressed_with_the_best_fire_sceptre()
    {
        var memory = new WornMemory(); // a new process whose checkpoint was set aside: no memory, the free coverage decides
        var r = Login();
        var restorer = new WornRestorer(GroundCatalog.Get("black-spawn-den-aluvian"), 80, new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>(), r.H.Log, "autopilot", "the start-up audit", memory);

        Run(r, restorer);

        var inventory = r.H.State.Self.Inventory;
        Assert.True(inventory.TryGetEquipped(Shirt, out _));
        Assert.True(inventory.TryGetEquipped(Breeches, out _));
        Assert.Equal(Fire1, inventory.EquippedAt(SelfEquipJudge.Held)); // fire x1.03 with the better mana conversion of the two
        Assert.Equal(InventoryPlaceKind.InSidePack, inventory.Find(Wand).Kind); // the Wand to the kit pack
        Assert.Equal(3, restorer.Restored);
        var lines = r.H.Log.Entries.Select(e => e.Message).ToList();
        Assert.Contains(lines, l => l.StartsWith("gear: restore: wear 'Shirt' 0x800048F5: its coverage 0x68 is free", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("gear: restore: wear 'Wide Breeches' 0x800048F8: its coverage 0x13 is free", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.StartsWith("gear: restore: caster pick: 'Fire Sceptre' 0x80003EB7", StringComparison.Ordinal));
        Assert.Contains("gear: restore (the start-up audit): worn_restored=3 refused=0", lines);
        Assert.True(memory.Set.ContainsKey(Shirt)); // the set is snapshotted at the end
    }

    [Fact]
    public void After_a_death_the_remembered_set_goes_back_on_at_its_locations_first()
    {
        var memory = new WornMemory();
        memory.Seed([new WornEntry(Breeches, 0x44), new WornEntry(Shirt, 0x1E), new WornEntry(Robe, 0x7F01)]);
        var r = Login();
        var restorer = new WornRestorer(GroundCatalog.Get("black-spawn-den-aluvian"), 80, new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>(), r.H.Log, "deathhandler", "the death's take-back", memory);

        Run(r, restorer);

        var wields = r.H.Net.Of(FakeSendKind.GetAndWieldItem).Select(s => (s.Guid, s.Amount)).ToList();
        Assert.Equal((Breeches, 0x44u), wields[0]); // the remembered order and locations, before any free-coverage pick
        Assert.Equal((Shirt, 0x1Eu), wields[1]);
        Assert.Equal((Fire1, SelfEquipJudge.Held), wields[2]);
        Assert.Contains(r.H.Log.Entries, e => e.Message == "gear: restore: wear 'Wide Breeches' 0x800048F8 at 0x00000044 (the worn set before the death's take-back): Confirmed: wielded at 0x00000044");
    }

    [Fact]
    public void M5_7_D139_k_with_the_kit_pack_full_the_held_caster_goes_to_the_main_pack_never_into_the_full_pack()
    {
        // Round 13 (03:06:37 UTC): the Wand sent to a kit pack with no free slot ("Unable to put Wand into container"), ACE's fallback left the hands empty.
        var r = Login();
        for (uint i = 0; i < 24; i++)
        {
            var stone = 0x80005000 + i;
            Obj(r, stone, "Greater Mana Stone", 2436, 0x80000, 0, 0, Kit);
            r.Raw(AceM3Writers.PutObjInContainer(Player, ++_seq, stone, Kit, i, 0));
        }

        var restorer = new WornRestorer(GroundCatalog.Get("black-spawn-den-aluvian"), 80, new Dictionary<uint, Lifestone.Data.Magic.SpellInfo>(), r.H.Log, "autopilot", "the start-up audit", new WornMemory());

        Run(r, restorer);

        var move = Assert.Single(r.H.Net.Of(FakeSendKind.PutItemInContainer), s => s.Guid == Wand);
        Assert.Equal(Player, uint.Parse(move.Target!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        Assert.Equal(InventoryPlaceKind.MainPack, r.H.State.Self.Inventory.Find(Wand).Kind);
        Assert.Equal(Fire1, r.H.State.Self.Inventory.EquippedAt(SelfEquipJudge.Held));
        Assert.Contains(r.H.Log.Entries, e => e.Message.Contains("the held caster 0x80004907 to the main pack (", StringComparison.Ordinal) && e.Message.Contains("the kit pack 0x8000490C is full (24 of 24)", StringComparison.Ordinal));
    }

    [Fact]
    public void M5_7_D139_k_the_swap_pack_is_the_kit_pack_with_room_else_the_main_pack_else_the_review_pack_else_none()
    {
        const uint Me = 0x50000001, Comp = 0x80001001, KitPack = 0x80001002, Review = 0x80001003;
        var rules = new PackRules(GroundCatalog.Get("black-spawn-den-aluvian"));
        PackLayout Layout(int main, int kit, int review) => PackLayout.Assign(Me, 102, main, [new(Comp, "Pack", 24, 0), new(KitPack, "Pack", 24, kit), new(Review, "Sack", 24, review)], [], rules, _ => false, _ => false);

        Assert.Equal(KitPack, HeldCaster.SwapPack(Layout(10, 23, 0), out var why));
        Assert.Equal("the kit pack 0x80001002 (1 free of 24)", why);
        Assert.Equal(Me, HeldCaster.SwapPack(Layout(10, 24, 0), out why));
        Assert.Equal("the main pack (92 free of 102; the kit pack 0x80001002 is full (24 of 24))", why);
        Assert.Equal(Review, HeldCaster.SwapPack(Layout(102, 24, 3), out why));
        Assert.Equal(0u, HeldCaster.SwapPack(Layout(102, 24, 24), out why));
        Assert.StartsWith("no pack has a free slot", why, StringComparison.Ordinal);
    }
}
