using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 14: the home visit's refill of the worn items' mana (<c>ItemManaRefillStep.cs</c>) and the field refill of <see cref="WornManaWatch"/>, over the climber's worn set as the long run of 2026-11-11 read it (the Pathwarden Robe
/// at 97 of 1,000 with 48 minutes left, the Pathwarden Trinket at 4,658 of 6,000), with ACE's side of a mana stone scripted from <c>ManaStone.HandleActionUseOnTarget</c> (ManaStone.cs:59-233 @95ef426d): a charged stone on one item or
/// rationed over the worn ones, an empty stone draining and destroying its target (a worn one too), the stone emptied afterwards or destroyed.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint JewelryType = 0x8;
    private const uint StoneType = 0x80000;
    private const uint ChestSlot = 0x7F00;
    private const uint TrinketSlot = 0x04000000;

    /// <summary>The climber at the home with her worn set, and ACE's answers to an Identify, a stone's use, a take-off and a wield.</summary>
    private sealed class RefillHome
    {
        private readonly Dictionary<uint, Thing> _things = [];
        private int _seen;
        private uint _seq = 5000;
        private byte _wield = 120;
        private uint _wcid = 31000;

        public RefillHome(int robeMana = 97, int coins = 10_000)
        {
            Home = new Home("holtburg-outskirts", coins);
            var r = Home.Vendor.R;
            r.PrivateInt(25, 10);
            Training = Home.Own("Training Wand", 12748, 0x8000, 25);
            r.Server((p, s) => AceM3Writers.WieldObject(p, s, Training, (int)HeldSlot));
            Spare = Home.Own("Wand", 2472, 0x8000, 200); // two casters: the floor buys none
            Robe = Home.Own("Pathwarden Robe", 40439, ClothingType, 0);
            r.Server((p, s) => AceM3Writers.WieldObject(p, s, Robe, (int)ChestSlot));
            _things[Robe] = new Thing("Pathwarden Robe") { Mana = robeMana, Max = 1000, Rate = -1.0 / 30 };
            var gauntlets = Home.Own("Pathwarden Gauntlets", 33606, ClothingType, 0);
            r.Server((p, s) => AceM3Writers.WieldObject(p, s, gauntlets, 0x20));
            Trinket = Home.Own("Pathwarden Trinket", 41513, JewelryType, 0);
            r.Server((p, s) => AceM3Writers.WieldObject(p, s, Trinket, unchecked((int)TrinketSlot)));
            _things[Trinket] = new Thing("Pathwarden Trinket") { Mana = 4658, Max = 6000, Rate = -0.049 };
        }

        public Home Home { get; }

        public uint Training { get; }

        public uint Spare { get; }

        public uint Robe { get; }

        public uint Trinket { get; }

        public PlayerInventory Inventory => Home.Vendor.State.Self.Inventory;

        /// <summary>M6 16 E5e4: more PropertyInts of the Robe's appraisal (an activation limit) and the spells it casts.</summary>
        public Dictionary<uint, int> RobeInts { get; } = [];

        public uint[] RobeSpells { get; set; } = [];

        /// <summary>Every use the server saw: the stone, its target and whether the stone held mana then.</summary>
        public List<(uint Stone, uint Target, bool Charged)> Uses { get; } = [];

        /// <summary>The take-offs and wields the server saw.</summary>
        public List<string> Ops { get; } = [];

        /// <summary>Stones whose use on anything gives nothing (ACE's "You have no items equipped that need mana." and UseDone 0x36).</summary>
        public HashSet<uint> GivesNothing { get; } = [];

        /// <summary>Stones whose first Identify answers still say charged though the server holds them empty (a read that went stale): the count of such answers left.</summary>
        public Dictionary<uint, (int Answers, int Mana)> Stale { get; } = [];

        /// <summary>The wields ACE refuses before it answers one.</summary>
        public int RefuseWields { get; set; }

        public int? ManaOf(uint guid) => _things.TryGetValue(guid, out var t) ? t.Mana : null;

        /// <summary>A mana stone in the main pack: <paramref name="mana"/> null for an empty one.</summary>
        public uint Stone(string name, uint wcid, double efficiency, double destroyChance, int? mana = null, uint value = 1000, bool destroyedByUse = false)
        {
            var guid = Home.Own(name, wcid, StoneType, value);
            _things[guid] = new Thing(name) { Mana = mana, Efficiency = efficiency, Destroy = destroyChance, DestroyedByUse = destroyedByUse };
            return guid;
        }

        /// <summary>A Tiny Mana Charge (weenie 4612: 100 mana, destroyed by its use).</summary>
        public uint TinyCharge() => Stone("Tiny Mana Charge", 4612, 1, 1, 100, 350, destroyedByUse: true);

        /// <summary>A looted magic item in the main pack with mana.</summary>
        public uint Loot(string name, uint itemType, int mana, uint value)
        {
            var guid = Home.Own(name, ++_wcid, itemType, value);
            _things[guid] = new Thing(name) { Mana = mana, Max = mana, Rate = -0.01 };
            return guid;
        }

        public void GiveMana(uint guid, string name, int mana, int max) => _things[guid] = new Thing(name) { Mana = mana, Max = max, Rate = -0.01 };

        public void Pump()
        {
            Home.Shops.Pump();
            var r = Home.Vendor.R;
            var sends = r.H.Net.Sends;
            for (; _seen < sends.Count; _seen++)
            {
                var s = sends[_seen];
                switch (s.Kind)
                {
                    case FakeSendKind.Identify:
                        Appraise(s.Guid);
                        break;
                    case FakeSendKind.UseWithTarget:
                        Use(s.Guid, uint.Parse(s.Target!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        break;
                    case FakeSendKind.GetAndWieldItem when RefuseWields > 0:
                        RefuseWields--;
                        Ops.Add("refused:" + NameOf(s.Guid));
                        Home.Vendor.ItemRefusal(s.Guid);
                        break;
                    case FakeSendKind.GetAndWieldItem:
                        Ops.Add(string.Create(CultureInfo.InvariantCulture, $"on:{NameOf(s.Guid)}@0x{s.Amount:X}"));
                        r.Raw(AceM3Writers.PublicUpdateInstanceId(++_wield, s.Guid, 3, 0));
                        r.Raw(AceM3Writers.PublicUpdatePropertyInt(_wield, s.Guid, 10, 0));
                        r.Server((p, q) => AceM3Writers.WieldObject(p, q, s.Guid, unchecked((int)s.Amount)));
                        break;
                    case FakeSendKind.PutItemInContainer:
                        var target = uint.Parse(s.Target!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        if (Inventory.Find(s.Guid).Kind == InventoryPlaceKind.Equipped)
                        {
                            Ops.Add("off:" + NameOf(s.Guid));
                            r.Raw(AceM3Writers.PublicUpdateInstanceId(++_wield, s.Guid, 3, 0));
                            r.Raw(AceM3Writers.PublicUpdatePropertyInt(_wield, s.Guid, 10, 0));
                        }

                        Home.Vendor.Put(s.Guid, target);
                        break;
                }
            }
        }

        public ScenarioStatus Run(ResupplyRun run) => Home.Stations.Run(run, 900, pump: Pump);

        /// <summary>A trip with looting and the gear phase on and its own refill memory (the shared one is other tests' too).</summary>
        public ResupplyRun Start(WornManaMemory? memory = null)
        {
            var run = Home.Start(new ResupplyOptions(true, () => [], Gear: true));
            run.ManaMemory = memory ?? new WornManaMemory();
            return run;
        }

        private string NameOf(uint guid) => _things.TryGetValue(guid, out var t) ? t.Name : Home.Vendor.State.Objects.TryGet(guid, out var o) ? o.Name : "?";

        private void Appraise(uint guid)
        {
            var ints = new Dictionary<uint, int>();
            var floats = new Dictionary<uint, double>();
            if (_things.TryGetValue(guid, out var t))
            {
                var mana = t.Mana;
                if (Stale.TryGetValue(guid, out var stale) && stale.Answers > 0)
                {
                    Stale[guid] = (stale.Answers - 1, stale.Mana);
                    mana = stale.Mana;
                }

                if (mana is { } cur)
                {
                    ints[107] = cur;
                }

                if (t.Max > 0)
                {
                    ints[108] = t.Max;
                }

                if (t.Rate != 0)
                {
                    floats[5] = t.Rate;
                }

                if (t.Efficiency > 0)
                {
                    floats[87] = t.Efficiency;
                    floats[137] = t.Destroy;
                }
            }

            uint[] spells = guid == Robe ? RobeSpells : [];
            foreach (var (k, v) in guid == Robe ? RobeInts : [])
            {
                ints[k] = v;
            }

            var r = Home.Vendor.R;
            r.H.R.Reducer.OnIdentifyResponse(r.H.R.Context, new GameEventHeader(VendorRig.Player, ++_seq, 0x00C9), new IdentifyResponseMsg(
                guid, IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable | (spells.Length == 0 ? 0 : IdentifyResponseFlags.SpellBook), true, ints.ToImmutableDictionary(), ImmutableDictionary<uint, long>.Empty, ImmutableDictionary<uint, bool>.Empty, floats.ToImmutableDictionary(),
                ImmutableDictionary<uint, string>.Empty, ImmutableDictionary<uint, uint>.Empty, [.. spells], null, null, null, null, null, null, null, null));
        }

        /// <summary><c>ManaStone.HandleActionUseOnTarget</c> (ManaStone.cs:59-233).</summary>
        private void Use(uint stone, uint target)
        {
            var v = Home.Vendor;
            var s = _things[stone];
            Uses.Add((stone, target, s.Mana is > 0));
            if (s.Mana is not > 0)
            {
                // An empty stone: on the player nothing; on an item with mana the drain, and the item is destroyed, a worn one too (:84-113).
                if (target == VendorRig.Player || !_things.TryGetValue(target, out var drained) || drained.Mana is not > 0 || drained.Max <= 0)
                {
                    v.UseDone(0x36);
                    return;
                }

                s.Mana = (int)Math.Round(s.Efficiency * drained.Mana.Value);
                _things.Remove(target);
                v.Remove(target);
                v.H.ServerMessage(string.Create(CultureInfo.InvariantCulture, $"The Mana Stone drains {s.Mana:N0} points of mana from the {drained.Name}.\nThe {drained.Name} is destroyed."), 0);
                v.UseDone();
                return;
            }

            if (GivesNothing.Contains(stone))
            {
                v.H.ServerMessage("You have no items equipped that need mana.", 0);
                v.UseDone(0x36);
                return;
            }

            var left = s.Mana.Value;
            var given = 0;
            if (target == VendorRig.Player)
            {
                var worn = Inventory.Equipped.Select(e => e.Guid).Where(g => _things.TryGetValue(g, out var w) && w.Max > 0 && w.Mana < w.Max).ToList();
                while (left > 0)
                {
                    var now = worn.Where(g => _things[g].Mana < _things[g].Max).ToList();
                    if (now.Count == 0)
                    {
                        break;
                    }

                    var ration = Math.Max(left / now.Count, 1);
                    foreach (var g in now)
                    {
                        var add = Math.Min(ration, _things[g].Max - _things[g].Mana!.Value);
                        left -= add;
                        given += add;
                        _things[g].Mana += add;
                        if (left <= 0)
                        {
                            break;
                        }
                    }
                }

                v.H.ServerMessage(string.Create(CultureInfo.InvariantCulture, $"The Mana Stone gives {given:N0} points of mana to the following items: {string.Join(", ", worn.Select(NameOf))}.\nYou need 1 more mana to fully charge your items."), 0);
            }
            else
            {
                var item = _things[target];
                given = Math.Min(item.Max - (item.Mana ?? 0), left);
                item.Mana = (item.Mana ?? 0) + given;
                v.H.ServerMessage(string.Create(CultureInfo.InvariantCulture, $"The Mana Stone gives {given:N0} points of mana to the {item.Name}."), 0);
            }

            if (s.DestroyedByUse)
            {
                _things.Remove(stone);
                v.Remove(stone);
                v.H.ServerMessage("The Mana Stone is destroyed.", 0);
            }
            else
            {
                s.Mana = null; // emptied: ItemCurMana is removed (:182-186)
            }

            v.UseDone();
        }

        private sealed class Thing(string name)
        {
            public string Name { get; } = name;

            public int? Mana { get; set; }

            public int Max { get; init; }

            public double Rate { get; init; }

            public double Efficiency { get; init; }

            public double Destroy { get; init; }

            public bool DestroyedByUse { get; init; }
        }
    }

    [Fact]
    public void M6_14_the_due_Robe_is_refilled_at_home_from_a_capture_of_the_loot_whose_mana_costs_least()
    {
        // 2026-11-11 13:20 UTC: the Robe at 97/1000 with 48 minutes left, four empty stones held, a pack of magic loot, and the visit said "no charged stone is held". It ran out eleven minutes later.
        var f = new RefillHome(robeMana: 97);
        var stone = f.Stone("Mana Stone", 2435, 0.6, 0.1);
        var ring = f.Loot("Ring", JewelryType, 1084, 2343);
        var slippers = f.Loot("Slippers", ClothingType, 856, 7916);
        var run = f.Start();

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Equal([(stone, ring, false), (stone, f.Robe, true)], f.Uses); // the empty stone on the loot, then the charged stone on the due item: never an empty stone on a worn one
        Assert.Equal(747, f.ManaOf(f.Robe)); // 97 + round(0.6 x 1,084)
        Assert.Equal(4658, f.ManaOf(f.Trinket)); // 26 h left: not due, and the stone went on the Robe alone
        Assert.False(f.Inventory.Contains(ring));
        Assert.Equal(InventoryPlaceKind.Equipped, f.Inventory.Find(f.Robe).Kind);
        Assert.Equal((1, 1, 1, 0, 0), (run.ManaDue, run.StoneCaptures, run.StoneRefills, run.ManaChargesPlanned, run.ManaNoGain));
        Assert.True(f.Home.Logged("gear mana: refill: check before (read from the server now): 'Pathwarden Robe' 97/1000 (0.8 h left at 0.0333/s), 'Pathwarden Trinket' 4658/6000 (26.4 h left at 0.049/s); stones: 0 charged (0 mana), 1 empty; her next visit is expected in 46 min"));
        Assert.True(f.Home.Logged("gear mana: refill: weigh 'Mana Stone' 0x" + Hex(stone) + " (efficiency 0.6) on 'Slippers' 0x" + Hex(slippers) + ": 856 mana -> 514 in the stone for a payout of 7124 forgone and 100 for the stone's 10% destroy chance: 14.05 pyreals a point"));
        Assert.True(f.Home.Logged("gear mana: refill: capture: 'Mana Stone' 0x" + Hex(stone) + " (efficiency 0.6) on 'Ring' 0x" + Hex(ring) + ": 1084 mana -> 650 in the stone for a payout of 2108 forgone and 100 for the stone's 10% destroy chance: 3.40 pyreals a point, no dearer than a bought point (4.73) (due: 'Pathwarden Robe' 97/1000:"));
        Assert.True(f.Home.Logged("gear mana: refill: capture: check before: the stone 'Mana Stone' 0x" + Hex(stone) + " is empty (read now); 'Ring' 0x" + Hex(ring) + " is in the main pack with 1084 mana (read now), payout 2108; predicted 650 in the stone"));
        Assert.True(f.Home.Logged("'Ring' is gone and the stone holds 650 (predicted 650)"));
        Assert.True(f.Home.Logged("gear mana: refill: check before: the stone 'Mana Stone' 0x" + Hex(stone) + " holds 650 (read now), on 'Pathwarden Robe' 0x" + Hex(f.Robe) + "; predicted +650"));
        Assert.True(f.Home.Logged("'Pathwarden Robe' 97 -> 747 (+650); gained 650 (predicted 650); the stone is empty now, kept for a capture"));
        Assert.True(f.Home.Logged("gear mana: refill: nothing more: every worn item lasts past the next visit in 46 min plus 46 min; 0 charged stone(s) held")); // the Slippers' 14.05 a point is no reserve
        Assert.True(f.Home.Logged("gear mana: refill: done: mana_due=1 stone_refills=1 stone_captures=1 charges_planned=0 reworn=0 no_gain=0; the first to run out is 'Pathwarden Robe' 747/1000 (6.2 h left at 0.0333/s); still due 0"));
        Assert.True(f.Home.Logged("caster mana: Training Wand 0/0 inf h -> None")); // the held caster keeps its own rule
        Assert.Null(run.Failure);
    }

    [Fact]
    public void M6_14_with_no_stone_to_capture_with_Cindrues_charges_join_the_buy_list_and_a_kept_caster_is_never_drained()
    {
        var f = new RefillHome(robeMana: 97);
        var stone = f.Stone("Mana Stone", 2435, 0.6, 0.1);
        f.GiveMana(f.Spare, "Wand", 467, 467); // the spare caster holds mana: the trips of 2026-11-11 weighed it for a drain on every visit
        var run = f.Start();

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Empty(f.Uses); // nothing to drain but the spare, which is never drained
        Assert.True(f.Inventory.Contains(f.Spare));
        Assert.True(f.Home.Logged("gear mana: refill: never drained: 'Wand' 0x" + Hex(f.Spare) + " (467 mana): a caster she keeps"));
        Assert.Equal(3, run.ManaChargesPlanned); // two horizons of 92 min burn 368; she holds 97: 271, three Tiny charges
        Assert.Contains(run.BuyList, b => b.Line.Wcid == 4612 && b.Count == 3 && b.Vendor.Name == "Archmage Cindrue");
        Assert.True(f.Home.Logged("gear floor: ManaCharge: buy 3 x Tiny Mana Charge (weenie 4612) at Archmage Cindrue for 473 each: the worn items' mana: 3 x wcid 4612 (100 mana each for 473: 4.73 pyreals a point) at Archmage Cindrue: the due items need 271 to last two horizons of 92 min"));
        Assert.True(f.Home.Logged("gear mana: refill: buy: 3 x Tiny Mana Charge (300 mana for 1419) on this trip's buy list at Archmage Cindrue; they come after the sale, so the field refill uses them once she holds them, else the next visit"));
        Assert.True(f.Home.Logged("gear mana: refill: nothing more: 1 item(s) still due"));
        Assert.True(f.Home.Logged("gear floor: bought 3 x Tiny Mana Charge at Archmage Cindrue: check after: held 0 -> 3, paid 1419"));
        Assert.Equal(1, run.FloorBought);
        Assert.Equal(97, f.ManaOf(f.Robe)); // the charges came after the step: the field refill's, else the next visit's
        Assert.True(f.Inventory.Contains(stone));
        Assert.Null(run.Failure);
    }

    [Fact]
    public void M6_14_a_stone_whose_read_went_stale_is_never_used_on_a_worn_item_and_the_next_means_is_tried()
    {
        // The session's read says the stone holds 300, the server holds it empty: used on the Robe it would drain the Robe and destroy it (ManaStone.cs:84-113). The read taken just before the use says so.
        var f = new RefillHome(robeMana: 97);
        var stone = f.Stone("Mana Stone", 2435, 0.6, 0.1);
        f.Stale[stone] = (2, 300); // the gear phase's read and the step's first read answer "300"; the read before the use answers empty
        var run = f.Start();

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.DoesNotContain(f.Uses, u => u.Target == f.Robe || u.Target == f.Trinket);
        Assert.Equal(InventoryPlaceKind.Equipped, f.Inventory.Find(f.Robe).Kind);
        Assert.Equal(97, f.ManaOf(f.Robe));
        Assert.True(f.Home.Logged("gear mana: refill: check before FAILED: the stone 'Mana Stone' 0x" + Hex(stone) + " holds no mana by the read taken now: nothing sent (an empty stone used on a worn item would drain and destroy it, ManaStone.cs:84-113); the next means"));
        Assert.Equal((1, 0, 3), (run.ManaNoGain, run.StoneRefills, run.ManaChargesPlanned)); // the next means: no loot to drain, so Cindrue's charges
        Assert.Null(run.Failure);
    }

    [Fact]
    public void M6_14_a_refill_that_gained_nothing_is_named_and_its_stone_is_not_tried_again()
    {
        var f = new RefillHome(robeMana: 97);
        var dud = f.Stone("Mana Stone", 2435, 0.6, 0.1, mana: 300);
        f.GivesNothing.Add(dud);
        var good = f.Stone("Mana Stone", 2435, 0.6, 0.1, mana: 800);
        var run = f.Start();

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        // The largest stone no larger than the need (903) is the 800: it works, and the other stone is never needed.
        Assert.Equal([(good, f.Robe, true)], f.Uses);
        Assert.Equal(897, f.ManaOf(f.Robe));

        var g = new RefillHome(robeMana: 97);
        var only = g.Stone("Mana Stone", 2435, 0.6, 0.1, mana: 300);
        g.GivesNothing.Add(only);
        var again = g.Start();

        Assert.Equal(ScenarioStatus.Succeeded, g.Run(again));

        Assert.Equal([(only, g.Robe, true)], g.Uses); // sent once, never repeated
        Assert.True(g.Home.Logged("gear mana: refill: check after: NOTHING GAINED (named: mana-refill-no-gain): the stone 'Mana Stone' 0x" + Hex(only) + ":"));
        Assert.True(g.Home.Logged("no worn item's mana rose; this stone is not tried again this visit: the next means"));
        Assert.Equal((1, 0, 3), (again.ManaNoGain, again.StoneRefills, again.ManaChargesPlanned)); // the next means: the charges
        Assert.True(g.Home.Logged("gear mana: refill: done: mana_due=1 stone_refills=0 stone_captures=0 charges_planned=3 reworn=0 no_gain=1;"));
    }

    [Fact]
    public void M6_14_a_piece_that_ran_out_is_refilled_and_worn_again_and_a_marked_piece_is_worn_again_once_a_run()
    {
        // The Robe at 0: ACE removed its spells (Player_Tick.cs:681-699) and casts them again only at a wield (Player_Inventory.cs:313-315): mana alone leaves it silent, as the run of 2026-11-11 showed (400/1000 and burning none).
        var f = new RefillHome(robeMana: 0);
        var stone = f.Stone("Mana Stone", 2435, 0.6, 0.1, mana: 650);
        var run = f.Start();

        Assert.Equal(ScenarioStatus.Succeeded, f.Run(run));

        Assert.Equal([(stone, f.Robe, true)], f.Uses);
        Assert.Equal(650, f.ManaOf(f.Robe));
        Assert.Equal(["off:Pathwarden Robe", "on:Pathwarden Robe@0x7F00"], f.Ops);
        Assert.Equal(f.Robe, f.Inventory.EquippedAt(ChestSlot));
        Assert.Equal(1, run.ManaReworn);
        Assert.True(f.Home.Logged("gear mana: rewear: check before: 'Pathwarden Robe' 0x" + Hex(f.Robe) + " is worn at 0x7F00 with 650/1000; it was out of mana at this visit's first read, so ACE had removed its spells"));
        Assert.True(f.Home.Logged("on again at 0x7F00 (Confirmed"));
        Assert.True(f.Home.Logged("worn, its spells are cast again with its mana"));

        // A refused wield leaves what the piece covered bare: the recovery is the wield once more.
        var r = new RefillHome(robeMana: 0) { RefuseWields = 1 };
        r.Stone("Mana Stone", 2435, 0.6, 0.1, mana: 650);
        var recovered = r.Start();
        Assert.Equal(ScenarioStatus.Succeeded, r.Run(recovered));
        Assert.Equal(["off:Pathwarden Robe", "refused:Pathwarden Robe", "on:Pathwarden Robe@0x7F00"], r.Ops);
        Assert.Equal(r.Robe, r.Inventory.EquippedAt(ChestSlot));
        Assert.Equal(1, recovered.ManaReworn);
        Assert.True(r.Home.Logged("is not worn after the wield at 0x7F00"));
        Assert.True(r.Home.Logged("recovery: the wield once more"));

        // The watch's mark (mana held, none burned): worn again once; a second mark in the same run is named and left.
        var g = new RefillHome(robeMana: 400);
        var memory = new WornManaMemory();
        CasterSpellsDropped.Shared.Mark("Pathwarden Robe", [g.Robe]);
        try
        {
            var first = g.Start(memory);
            Assert.Equal(ScenarioStatus.Succeeded, g.Run(first));
            Assert.Equal(["off:Pathwarden Robe", "on:Pathwarden Robe@0x7F00"], g.Ops);
            Assert.Equal(1, first.ManaReworn);
            Assert.False(CasterSpellsDropped.Shared.IsDropped(g.Robe, "Pathwarden Robe"));
            Assert.True(g.Home.Logged("the watch saw it hold mana and burn none: its spells are off"));

            CasterSpellsDropped.Shared.Mark("Pathwarden Robe", [g.Robe]);
            var second = g.Start(memory);
            Assert.Equal(ScenarioStatus.Succeeded, g.Run(second));
            Assert.Equal(2, g.Ops.Count); // nothing more sent
            Assert.Equal(0, second.ManaReworn);
            Assert.True(g.Home.Logged("still burns none after it was worn again once this run: its spells do not come on when worn (named: spells-stay-off); left as it is"));
        }
        finally
        {
            CasterSpellsDropped.Shared.Clear(g.Robe, "Pathwarden Robe");
        }
    }

    /// <summary>
    /// M6 16 E5e4 (live 2026-11-18 00:50:47, ladder67): the watch marked six worn pieces (mana held, none burned) and the visit took each off and put it on, ACE answering each wield "Your Missile Defense is too low to
    /// use that item's magic." or "Your Melee Defense ...". A piece whose activation ACE would refuse again is left on: nothing is sent, the line says why, and the watch's mark is cleared.
    /// </summary>
    [Fact]
    public void M6_16_E5e4_a_marked_piece_whose_magic_ACE_would_refuse_again_is_not_taken_off_at_home()
    {
        var g = new RefillHome(robeMana: 400) { RobeSpells = [2581] }; // Minor Focus
        g.RobeInts[Lifestone.Client.Session.Mage.SelfEquipJudge.ItemSkillLevelLimitInt] = 144;
        g.RobeInts[Lifestone.Client.Session.Mage.SelfEquipJudge.AppraisalItemSkillInt] = 7; // Missile Defense, which this mage does not hold: unread, so unmet by name
        CasterSpellsDropped.Shared.Mark("Pathwarden Robe", [g.Robe]);
        try
        {
            var run = g.Start(new WornManaMemory());
            Assert.Equal(ScenarioStatus.Succeeded, g.Run(run));

            Assert.Empty(g.Ops); // nothing came off
            Assert.Equal(0, run.ManaReworn);
            Assert.Equal(g.Robe, g.Inventory.EquippedAt(ChestSlot));
            Assert.True(g.Home.Logged("gear mana: rewear: 'Pathwarden Robe' 0x" + Hex(g.Robe) + " left on: an off-and-on would light nothing: activation: Missile Defense unread < 144"));
            Assert.False(CasterSpellsDropped.Shared.IsDropped(g.Robe, "Pathwarden Robe"));
        }
        finally
        {
            CasterSpellsDropped.Shared.Clear(g.Robe, "Pathwarden Robe");
        }
    }

    [Fact]
    public void M6_14_in_the_field_a_held_charge_goes_on_the_item_that_would_be_empty_before_she_is_next_home()
    {
        var f = new RefillHome(robeMana: 20); // ten minutes left; no visit home is known, so the next is expected in 46
        var charge = f.TinyCharge(); // bought on the last trip, never appraised: a charge is known full by its weenie
        var watch = new WornManaWatch(f.Home.Vendor.H.Log, "autopilot") { Memory = new WornManaMemory(), Dropped = new CasterSpellsDropped() };

        StepWatch(f, watch, seconds: 80);

        Assert.Equal([(charge, f.Robe, true)], f.Uses);
        Assert.Equal(120, f.ManaOf(f.Robe));
        Assert.Equal(1, watch.FieldRefills);
        Assert.True(f.Home.Logged("gear mana: field refill: check before: 'Pathwarden Robe'"));
        Assert.True(f.Home.Logged("(the field's rule: empty before she is next home, in about 46 min); use 'Tiny Mana Charge' 0x" + Hex(charge) + " (100) on 'Pathwarden Robe' 0x" + Hex(f.Robe)));
        Assert.True(f.Home.Logged("gear mana: field refill: the stone 'Tiny Mana Charge' 0x" + Hex(charge) + ": Confirmed"));
        Assert.True(f.Home.Logged("gear mana: field refill: check after: 'Pathwarden Robe' 0x" + Hex(f.Robe)));
        Assert.True(f.Home.Logged(" -> 120 of 1000 (+"));
    }

    [Fact]
    public void M6_14_in_the_field_a_stone_goes_on_the_player_never_on_one_worn_item_and_nothing_is_used_while_no_item_is_due()
    {
        var f = new RefillHome(robeMana: 20);
        var stone = f.Stone("Mana Stone", 2435, 0.6, 0.1, mana: 300);
        var view = f.Home.Stations.View;
        view.Actions.Identify(stone, ActionSource.Host); // the home visit read it charged
        var watch = new WornManaWatch(f.Home.Vendor.H.Log, "autopilot") { Memory = new WornManaMemory(), Dropped = new CasterSpellsDropped() };

        StepWatch(f, watch, seconds: 80);

        Assert.Equal([(stone, VendorRig.Player, true)], f.Uses); // ACE's even ration: an empty stone on the Robe would destroy it, and the read may be stale in the field
        Assert.Equal((170, 4808), (f.ManaOf(f.Robe), f.ManaOf(f.Trinket)));
        Assert.True(f.Home.Logged("on the player (in the field a stone never goes on one worn item: an empty stone would destroy it)"));
        StepWatch(f, watch, seconds: 120);
        Assert.Single(f.Uses); // the emptied stone is not used again, and the Robe (85 min left) is no longer due before she is home

        var calm = new RefillHome(robeMana: 400); // 3.3 h left: past the next visit
        var held = calm.TinyCharge();
        var quiet = new WornManaWatch(calm.Home.Vendor.H.Log, "autopilot") { Memory = new WornManaMemory(), Dropped = new CasterSpellsDropped() };
        StepWatch(calm, quiet, seconds: 80);
        Assert.Empty(calm.Uses);
        Assert.True(calm.Inventory.Contains(held));
        Assert.Equal(0, quiet.FieldRefills);
    }

    [Fact]
    public void M6_14_the_alarm_says_what_the_refill_does_and_a_piece_that_holds_mana_and_burns_none_is_marked_for_the_home_visit()
    {
        var f = new RefillHome(robeMana: 20);
        var dropped = new CasterSpellsDropped();
        var watch = new WornManaWatch(f.Home.Vendor.H.Log, "autopilot") { Memory = new WornManaMemory(), Dropped = dropped };

        StepWatch(f, watch, seconds: 40); // no charged stone is held
        Assert.True(f.Home.Logged("gear mana: field refill: 'Pathwarden Robe' "));
        Assert.True(f.Home.Logged("under the next visit in 46 min plus 0 min (the field's rule: empty before she is next home), and no charged stone is held: nothing to use in the field; the next home visit refills it (named: field-refill-no-stone)"));
        Assert.True(watch.OnChat("Your Pathwarden Robe is low on Mana.", 100));
        Assert.True(f.Home.Logged("under 120 s left (Player_Tick.cs:662-679)); the refill: no charged stone is held, so the field has nothing to use: the next home visit (expected in about 46 min) refills it from a capture from loot or bought charges"));
        Assert.True(watch.OnChat("Your Pathwarden Robe is out of Mana.", 220));
        Assert.True(f.Home.Logged("its spells drop in 2 s (Player_Tick.cs:681-699)); the refill: the next home visit (expected in about 46 min) refills it from a charged stone, a capture from loot or bought charges, and wears it again, which alone brings its spells back (Player_Inventory.cs:313-315)"));
        Assert.DoesNotContain(f.Home.Lines, l => l.Contains("m5-stone-field-refill", StringComparison.Ordinal));

        // Two reads 300 s apart, the same mana, a rate that would have burned ten points: the spells are off.
        var log = new RecordingLogSink();
        var silent = new CasterSpellsDropped();
        var reads = new WornManaWatch(log, "autopilot") { Memory = new WornManaMemory(), Dropped = silent };
        var robe = new Appraisal(
            new IdentifyResponseMsg(
                f.Robe, IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable, true, new Dictionary<uint, int> { [107] = 400, [108] = 1000 }.ToImmutableDictionary(), ImmutableDictionary<uint, long>.Empty, ImmutableDictionary<uint, bool>.Empty,
                new Dictionary<uint, double> { [5] = -1.0 / 30 }.ToImmutableDictionary(), ImmutableDictionary<uint, string>.Empty, ImmutableDictionary<uint, uint>.Empty, [], null, null, null, null, null, null, null, null),
            1,
            0);
        reads.Read(f.Robe, "Pathwarden Robe", robe, 0);
        Assert.False(silent.IsDropped(f.Robe, "Pathwarden Robe"));
        reads.Read(f.Robe, "Pathwarden Robe", robe, 300);
        Assert.True(silent.IsDropped(f.Robe, "Pathwarden Robe"));
        Assert.Contains(log.Entries, e => e.Message == "gear mana: 'Pathwarden Robe' 0x" + Hex(f.Robe) + " holds 400/1000 and burned none in 300 s (its rate would have burned 10): its spells are off (ACE removes them at 0 and a refill does not bring them back, Player_Inventory.cs:313-315): the next home visit wears it again");
        reads.Read(f.Robe, "Pathwarden Robe", robe, 600);
        Assert.Single(log.Entries, e => e.Message.Contains("burned none", StringComparison.Ordinal)); // said once
    }

    private static string Hex(uint guid) => guid.ToString("X8", CultureInfo.InvariantCulture);

    /// <summary>Runs the watch as the autopilot does (a step a frame), with ACE's answers pumped, for <paramref name="seconds"/>.</summary>
    private static void StepWatch(RefillHome f, WornManaWatch watch, double seconds)
    {
        var view = f.Home.Stations.View;
        for (var t = 0.0; t < seconds; t += 0.25)
        {
            f.Home.Vendor.Tick(0.25);
            f.Pump();
            f.Home.Vendor.Tick();
            watch.Step(view, view.Clock.Now);
        }
    }
}
