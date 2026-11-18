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
/// M5 7 D139 rows (d), (k), (l), (m): the home visit's gear phase on round 13's packs (2026-10-30 03:06 UTC): the held Wand, a better Fire Sceptre unwielded in the main pack and a kit pack full of empty mana stones. The stones beyond the
/// keep go to the sale first, so the kit pack has room; the pick runs before the sort, so the sceptre is wielded instead of sent to the review pack; the Wand goes to a pack with a free slot; and when ACE refuses the move of the held caster
/// (its fallback leaves the hands empty) the pick is wielded at once.
/// </summary>
public sealed partial class ResupplyStationTests
{
    private const uint HeldSlot = 0x01000000;

    /// <summary>The round-13 packs at the home: the components pack, the kit pack full of empty Greater Mana Stones, the review Sack, the Wand held and the Fire Sceptre in the main pack.</summary>
    private sealed class GearHome
    {
        private uint _seq = 700;
        private int _seen;

        public GearHome(int emptyStones = 24, bool refuseHeldMove = false, bool refuseSceptre = false)
        {
            Home = new Home(Den, 1000);
            RefuseHeldMove = refuseHeldMove;
            RefuseSceptre = refuseSceptre;
            var r = Home.Vendor.R;
            r.PrivateInt(25, 81); // level 81: the den's plan at 81 is Fire (element 0x10)
            Components = Pack("Pack");
            Kit = Pack("Pack");
            Sack = Pack("Sack");
            Comp = Item("Pyreal Scarab", 690, 0x1000, Components);
            for (var i = 0; i < emptyStones; i++)
            {
                Stones.Add(Item("Greater Mana Stone", 2436, 0x80000, Kit, 1000));
            }

            Wand = Item("Wand", 2472, 0x8000, VendorRig.Player);
            r.Server((p, s) => AceM3Writers.WieldObject(p, s, Wand, (int)HeldSlot));
            Sceptre = Item("Fire Sceptre", 29262, 0x8000, VendorRig.Player, 2312);
        }

        public Home Home { get; }

        public bool RefuseHeldMove { get; }

        /// <summary>M6 14 (m6-caster-spend): ACE refuses the sceptre's wield as under a wield requirement: 0x00A0 with SkillTooLow 0x0468 (Player_Inventory.cs:1655-1661 @95ef426d), the sceptre left in its pack.</summary>
        public bool RefuseSceptre { get; }

        public uint Components { get; }

        public uint Kit { get; }

        public uint Sack { get; }

        public uint Comp { get; }

        public uint Wand { get; }

        public uint Sceptre { get; }

        public List<uint> Stones { get; } = [];

        public PlayerInventory Inventory => Home.Vendor.State.Self.Inventory;

        private uint Pack(string name)
        {
            var guid = Home.Vendor.NextGuid();
            Home.Vendor.Arrive(guid, name, 136, 0x200, 65);
            Home.Vendor.R.Raw(AceM3Writers.PutObjInContainer(VendorRig.Player, ++_seq, guid, VendorRig.Player, 0, 1));
            return guid;
        }

        private uint Item(string name, uint wcid, uint itemType, uint container, uint value = 10)
        {
            var guid = Home.Vendor.NextGuid();
            Home.Vendor.Arrive(guid, name, wcid, itemType, value);
            Home.Vendor.Put(guid, container);
            return guid;
        }

        /// <summary>ACE's side of the gear phase: each Identify answered (an empty stone with its efficiency, the sceptre's fire bonus, the rest plain), each move and wield as ACE answers it.</summary>
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
                    case FakeSendKind.GetAndWieldItem:
                        if (RefuseSceptre && s.Guid == Sceptre)
                        {
                            r.Raw(AceM3Writers.InventoryServerSaysFailed(VendorRig.Player, ++_seq, s.Guid, SelfEquipJudge.SkillTooLow));
                            break;
                        }

                        r.Raw(AceM3Writers.PublicUpdateInstanceId(++_wield, s.Guid, 3, 0));
                        r.Raw(AceM3Writers.PublicUpdatePropertyInt(_wield, s.Guid, 10, 0));
                        r.Server((p, q) => AceM3Writers.WieldObject(p, q, s.Guid, (int)s.Amount));
                        break;
                    case FakeSendKind.PutItemInContainer:
                        var target = uint.Parse(s.Target!, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                        var held = Inventory.Find(s.Guid).Kind == InventoryPlaceKind.Equipped;
                        if (held)
                        {
                            r.Raw(AceM3Writers.PublicUpdateInstanceId(++_wield, s.Guid, 3, 0)); // TryDequipObjectWithNetworking (Player_Inventory.cs 396-405)
                            r.Raw(AceM3Writers.PublicUpdatePropertyInt(_wield, s.Guid, 10, 0));
                        }

                        if ((held && RefuseHeldMove) || (target != VendorRig.Player && Inventory.ItemsIn(target).Count >= 24))
                        {
                            Refusals++;
                            Home.Vendor.Transient("Unable to put the item into container"); // 1320-1323, then the silent fallback (1335)
                            Home.Vendor.ItemRefusal(s.Guid);
                            break;
                        }

                        Home.Vendor.Put(s.Guid, target);
                        break;
                }
            }
        }

        public int Refusals { get; private set; }

        private byte _wield = 40;

        private void Appraise(uint guid)
        {
            var ints = new Dictionary<uint, int>();
            var floats = new Dictionary<uint, double>();
            if (Stones.Contains(guid))
            {
                floats[87] = 2.0; // ItemEfficiency; no ItemCurMana: an empty stone
            }
            else if (guid == Sceptre)
            {
                ints[45] = 0x10;
                floats[AppraisalIds.ElementalDamageMod] = 1.03;
                floats[144] = 0.102;
            }
            else if (guid == Wand)
            {
                floats[144] = 0.085;
            }

            var r = Home.Vendor.R;
            r.H.R.Reducer.OnIdentifyResponse(r.H.R.Context, new GameEventHeader(VendorRig.Player, ++_seq, 0x00C9), new IdentifyResponseMsg(
                guid, IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable, true, ints.ToImmutableDictionary(), ImmutableDictionary<uint, long>.Empty, ImmutableDictionary<uint, bool>.Empty, floats.ToImmutableDictionary(),
                ImmutableDictionary<uint, string>.Empty, ImmutableDictionary<uint, uint>.Empty, [], null, null, null, null, null, null, null, null));
        }

        public ScenarioStatus Run(ResupplyRun run) => Home.Stations.Run(run, 600, pump: Pump);
    }

    [Fact]
    public void M5_7_D139_d_k_m_the_empty_stones_beyond_the_keep_are_sold_the_kit_pack_gets_room_and_the_sceptre_is_wielded_before_the_sort()
    {
        var g = new GearHome();
        var run = g.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, g.Run(run));
        Assert.Equal(g.Sceptre, g.Inventory.EquippedAt(HeldSlot)); // (m): wielded, never sent to the review pack first
        Assert.DoesNotContain(g.Home.Lines, l => l.Contains("packs: move 'Fire Sceptre'", StringComparison.Ordinal));
        Assert.Equal(g.Kit, g.Inventory.Find(g.Wand).Container); // (k): the kit pack had room once the stones left
        Assert.Equal(0, g.Refusals);
        Assert.Equal(4, g.Stones.Count(s => g.Inventory.Contains(s))); // (d): the keep of 4 kept, 20 sold at Archmage Cindrue
        Assert.True(g.Home.Logged("gear stones (the visit's start): keep 0 charged and 4 empty of 24 (the keep is 4 stones, the charged first); 20 empty to sell"));
        Assert.True(g.Home.Logged("stones_kept=4 stones_shed=20 stones_sold=20"));
        Assert.True(g.Home.Logged("gear: the held caster 0x" + g.Wand.ToString("X8", CultureInfo.InvariantCulture) + " to the kit pack 0x" + g.Kit.ToString("X8", CultureInfo.InvariantCulture)));
        Assert.True(g.Home.Logged("packs free: main="));
        var order = g.Home.Lines.ToList();
        var pick = order.FindIndex(l => l.Contains("gear: caster pick:", StringComparison.Ordinal));
        Assert.InRange(pick, 0, order.FindIndex(l => l.Contains("packs: layout (the visit's start)", StringComparison.Ordinal)) - 1);
    }

    [Fact]
    public void M5_7_D139_l_a_refused_move_of_the_held_caster_leaves_the_hands_empty_and_the_pick_is_wielded_at_once()
    {
        // ACE (Player_Inventory.cs 1268-1340): unwield, the pack refuses, the silent fallback puts the Wand in the main pack. Round 13 logged "no swap" and went on with empty hands.
        var g = new GearHome(emptyStones: 0, refuseHeldMove: true);
        var run = g.Home.Start(new ResupplyOptions(true, () => [], Gear: true));

        Assert.Equal(ScenarioStatus.Succeeded, g.Run(run));

        Assert.Equal(1, g.Refusals);
        Assert.Equal(g.Sceptre, g.Inventory.EquippedAt(HeldSlot));
        Assert.Equal(InventoryPlaceKind.InSidePack, g.Inventory.Find(g.Wand).Kind); // ACE's fallback put it in the main pack (rotation gear: not offered to the sale there); the sort after the buys took it to the kit pack
        Assert.True(g.Home.Logged("inventory: the server refused the move of a wielded item after it had unwielded it"));
        Assert.True(g.Home.Logged("the hands hold nothing now (the server's updates)"));
        Assert.True(g.Home.Logged("gear: the hands hold no caster after the refused move (ACE's fallback put 0x" + g.Wand.ToString("X8", CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void M6_14_a_caster_swap_in_the_magic_mode_sets_peace_itself_and_the_sceptre_is_wielded_on_the_same_visit()
    {
        // Live 2026-11-14: the Fire Sceptre's swap waited two visits ("gear: no swap: the combat mode is 8, not peace") because she stood in the magic mode at home. Check, act, check: the station asks peace, reads the
        // server's answer, then swaps.
        var g = new GearHome(emptyStones: 0);
        g.Home.Vendor.AnswersCombatMode = true;
        g.Home.Vendor.R.CombatModeIs(CombatModes.Magic);
        Assert.Equal(ScenarioStatus.Succeeded, g.Run(g.Home.Start(new ResupplyOptions(true, () => [], Gear: true))));

        Assert.Equal(g.Sceptre, g.Inventory.EquippedAt(HeldSlot));
        Assert.Contains(g.Home.Lines, l => l.Contains("setting peace for the swap of 'Fire Sceptre'", StringComparison.Ordinal));
        Assert.Contains(g.Home.Lines, l => l.Contains("gear: peace for the swap: ", StringComparison.Ordinal));
        Assert.DoesNotContain(g.Home.Lines, l => l.Contains("waits for the next visit", StringComparison.Ordinal));
        Assert.True(g.Home.Vendor.R.H.Net.Of(FakeSendKind.ChangeCombatMode).Count >= 1);
    }

    [Fact]
    public void M6_14_caster_spend_a_refused_wield_is_a_named_failure_the_old_caster_goes_back_on_and_the_sceptre_is_held_back_for_the_run()
    {
        // The check after the swap: ACE refuses the sceptre (0x00A0, SkillTooLow 0x0468: a wield requirement the client read wrong), so the server's answer is a refusal and the hands do not hold it.
        var g = new GearHome(emptyStones: 0, refuseSceptre: true);
        var heldBack = Lifestone.Client.Session.Quartermaster.Wardrobe.For(g.Home.Vendor.State).HeldBack; // M6 16 E5e2: the session's one memory, where the journey carried its own
        var sceptre = g.Sceptre.ToString("X8", CultureInfo.InvariantCulture);
        Assert.Equal(ScenarioStatus.Succeeded, g.Run(g.Home.Start(new ResupplyOptions(true, () => [], Gear: true))));

        Assert.Equal(g.Wand, g.Inventory.EquippedAt(HeldSlot)); // the Wand back in the hands
        var failed = Assert.Single(g.Home.Lines, l => l.Contains("gear: FAILED", StringComparison.Ordinal));
        Assert.Contains($"gear: FAILED {ResupplyRun.CasterWieldFailed}: the wield of 'Fire Sceptre' 0x{sceptre} did not land: the server's answer ", failed, StringComparison.Ordinal);
        Assert.Contains("the server's answer ServerError (code 0x468)", failed, StringComparison.Ordinal); // ACE's 0x00A0 with SkillTooLow
        Assert.True(heldBack.IsHeldBack(g.Sceptre, out var why));
        Assert.StartsWith(ResupplyRun.CasterWieldFailed + ": ", why, StringComparison.Ordinal);
        Assert.Equal(2, g.Home.Vendor.R.H.Net.Of(FakeSendKind.GetAndWieldItem).Count); // the sceptre, then the Wand back

        // The next visit of the run: the pick passes it over by name and sends nothing for it.
        Assert.Equal(ScenarioStatus.Succeeded, g.Run(g.Home.Start(new ResupplyOptions(true, () => [], Gear: true))));
        Assert.Equal(g.Wand, g.Inventory.EquippedAt(HeldSlot));
        Assert.Equal(2, g.Home.Vendor.R.H.Net.Of(FakeSendKind.GetAndWieldItem).Count);
        Assert.True(g.Home.Logged($"1 held back for the run ('Fire Sceptre' 0x{sceptre}: {ResupplyRun.CasterWieldFailed}: "));
    }
}
