using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 item 16, slice E5e1: the wear service's one wield routine (<see cref="Wardrobe.PutOn"/>) on the scripted server the town trip's gear tests use (<see cref="VendorRig"/>: ACE's own writers and the real decoder; a wield
/// answered with the piece's WieldObject or refused, a move of a worn piece to a pack answered or refused). What these prove: the check before sends nothing for a piece she does not hold or wears already; what blocks the
/// slot comes off first, in order, and one that stays on ends the routine with nothing more sent; the slot is the caller's or the one rule's (the free side of a ring); the server's refusal is <c>refused</c> and a
/// confirmed wield the caller's reading does not see is <c>not-worn</c>; the wait for the worn state is the caller's; the hooks fire in the routine's order; and the tick-driven half sends the same wield.
/// </summary>
public sealed class WardrobeWieldTests
{
    private const uint Clothing = 0x4, Jewellery = 0x8;
    private const uint Head = 0x1, Hands = 0x20;
    private const uint RingLeft = 0x00040000, RingRight = 0x00080000;

    /// <summary>The player with a few pieces, ACE's side scripted: a wield and a take-off answered or refused.</summary>
    private sealed class Closet
    {
        private int _seen;
        private byte _sequence = 90;
        private uint _wcid = 9000;

        public Closet()
        {
            View = new J3aView(Rig.R);
        }

        public VendorRig Rig { get; } = new();

        public J3aView View { get; }

        /// <summary>The pieces whose wield ACE refuses.</summary>
        public HashSet<uint> RefuseWield { get; } = [];

        /// <summary>The worn pieces whose take-off ACE refuses.</summary>
        public HashSet<uint> RefuseOff { get; } = [];

        public PlayerInventory Inventory => Rig.State.Self.Inventory;

        public uint Own(string name, uint itemType, uint valid)
        {
            var guid = Rig.NextGuid();
            Rig.Arrive(guid, name, ++_wcid, itemType, 10, validLocations: valid);
            Rig.Put(guid);
            Rig.Tick();
            return guid;
        }

        public uint OwnWorn(string name, uint itemType, uint valid, uint location)
        {
            var guid = Own(name, itemType, valid);
            Rig.R.Server((p, s) => AceM3Writers.WieldObject(p, s, guid, (int)location));
            Rig.Tick();
            Assert.Equal(InventoryPlaceKind.Equipped, Inventory.Find(guid).Kind);
            return guid;
        }

        public bool Worn(uint guid) => Inventory.Find(guid).Kind == InventoryPlaceKind.Equipped;

        public List<FakeSend> Wields => Rig.H.Net.Of(FakeSendKind.GetAndWieldItem);

        public List<FakeSend> Moves => Rig.H.Net.Of(FakeSendKind.PutItemInContainer);

        public WieldResult PutOn(WieldRequest request, WieldHost? host = null)
        {
            var result = new WieldResult();
            Drive(Wardrobe.PutOn(View, request, result, host));
            return result;
        }

        public void Drive(IEnumerable<ActionHandle?> steps)
        {
            using var script = steps.GetEnumerator();
            ActionHandle? wait = null;
            for (var i = 0; i < 400; i++)
            {
                if (wait is not { IsCompleted: false })
                {
                    if (!script.MoveNext())
                    {
                        return;
                    }

                    wait = script.Current;
                }

                Step();
            }

            Assert.Fail("the routine did not end in 400 frames");
        }

        public void Step()
        {
            Rig.Tick(0.25);
            Pump();
            Rig.Tick();
        }

        private void Pump()
        {
            var sends = Rig.H.Net.Sends;
            for (; _seen < sends.Count; _seen++)
            {
                var s = sends[_seen];
                switch (s.Kind)
                {
                    case FakeSendKind.GetAndWieldItem:
                        if (RefuseWield.Contains(s.Guid))
                        {
                            Rig.ItemRefusal(s.Guid);
                            break;
                        }

                        Rig.R.Raw(AceM3Writers.PublicUpdateInstanceId(++_sequence, s.Guid, 3, 0));
                        Rig.R.Raw(AceM3Writers.PublicUpdatePropertyInt(_sequence, s.Guid, 10, 0));
                        Rig.R.Server((p, q) => AceM3Writers.WieldObject(p, q, s.Guid, (int)s.Amount));
                        break;
                    case FakeSendKind.PutItemInContainer:
                        if (RefuseOff.Contains(s.Guid))
                        {
                            Rig.ItemRefusal(s.Guid);
                            break;
                        }

                        if (Worn(s.Guid))
                        {
                            Rig.R.Raw(AceM3Writers.PublicUpdateInstanceId(++_sequence, s.Guid, 3, 0));
                            Rig.R.Raw(AceM3Writers.PublicUpdatePropertyInt(_sequence, s.Guid, 10, 0));
                        }

                        Rig.Put(s.Guid, uint.Parse(s.Target!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        break;
                }
            }
        }
    }

    [Fact]
    public void A_held_piece_goes_on_at_the_callers_slot_and_the_result_reads_it_worn()
    {
        var c = new Closet();
        var cap = c.Own("Cap", Clothing, Head);

        var result = c.PutOn(new WieldRequest(cap) { Name = "Cap", Location = Head });

        Assert.Null(result.Refusal);
        Assert.True(result.Sent && result.Confirmed && result.Worn);
        Assert.Equal(Head, result.Location);
        Assert.Equal((cap, Head), (Assert.Single(c.Wields).Guid, (uint)Assert.Single(c.Wields).Amount));
        Assert.True(c.Worn(cap));
        Assert.StartsWith("Confirmed", result.Outcome(ScriptLeg.Describe), StringComparison.Ordinal); // the caller's own phrase for the handle
    }

    [Fact]
    public void A_piece_she_does_not_hold_or_wears_already_sends_nothing()
    {
        var c = new Closet();
        var worn = c.OwnWorn("Cap", Clothing, Head, Head);

        var missing = c.PutOn(new WieldRequest(0x80001234) { Name = "Ghost", Location = Head });
        var already = c.PutOn(new WieldRequest(worn) { Name = "Cap", Location = Head });
        var none = c.PutOn(new WieldRequest(0) { Location = Head });

        Assert.Equal((Wardrobe.NotHeld, Wardrobe.WornAlready, Wardrobe.NotHeld), (missing.Refusal, already.Refusal, none.Refusal));
        Assert.False(missing.Sent || already.Sent || none.Sent);
        Assert.Empty(c.Wields);
        Assert.Equal("not sent: 'Cap' 0x" + worn.ToString("X8", CultureInfo.InvariantCulture) + " is worn already: nothing sent", already.Outcome(ScriptLeg.Describe));
    }

    [Fact]
    public void What_blocks_the_slot_comes_off_first_in_order_and_then_the_piece_goes_on()
    {
        var c = new Closet();
        var old = c.OwnWorn("Old Cap", Clothing, Head, Head);
        var gloves = c.OwnWorn("Old Gloves", Clothing, Hands, Hands);
        var robe = c.Own("Robe", Clothing, Head | Hands);
        var seen = new List<string>();
        var host = new WieldHost(VendorCounterHost.ClockUntil(c.View))
        {
            TookOff = (b, off, isOff) => seen.Add($"off {b.Name} {off.Outcome!.Value.Kind} {isOff}"),
            Sending = location => seen.Add($"sending 0x{location:X}"),
            Answered = r => seen.Add("answered " + (r.Confirmed ? "Confirmed" : "not confirmed")),
        };

        var result = c.PutOn(new WieldRequest(robe) { Name = "Robe", Location = Head | Hands, TakeOff = [new WieldBlocker(old, "Old Cap") { Into = VendorRig.Player }, new WieldBlocker(gloves, "Old Gloves")] }, host);

        Assert.Null(result.Refusal);
        Assert.True(c.Worn(robe) && !c.Worn(old) && !c.Worn(gloves));
        Assert.Equal([old, gloves], result.TakenOff.Select(b => b.Guid));
        Assert.Equal([old, gloves], c.Moves.Select(m => m.Guid)); // a move to the pack named, and an unwield (the same wire action to the main pack), in the request's order
        Assert.Equal(["off Old Cap Confirmed True", "off Old Gloves Confirmed True", "sending 0x21", "answered Confirmed"], seen);
    }

    [Fact]
    public void A_piece_in_the_way_that_stays_on_ends_the_routine_and_the_wield_is_not_sent()
    {
        var c = new Closet();
        var old = c.OwnWorn("Old Cap", Clothing, Head, Head);
        var gloves = c.OwnWorn("Old Gloves", Clothing, Hands, Hands);
        var robe = c.Own("Robe", Clothing, Head | Hands);
        c.RefuseOff.Add(gloves);
        var offs = new List<(string Name, bool Off)>();
        var host = new WieldHost(VendorCounterHost.ClockUntil(c.View)) { TookOff = (b, _, isOff) => offs.Add((b.Name, isOff)) };

        var result = c.PutOn(new WieldRequest(robe) { Name = "Robe", Location = Head | Hands, TakeOff = [new WieldBlocker(old, "Old Cap"), new WieldBlocker(gloves, "Old Gloves")] }, host);

        Assert.Equal(Wardrobe.Blocked, result.Refusal);
        Assert.False(result.Sent);
        Assert.Empty(c.Wields);
        Assert.Equal([old], result.TakenOff.Select(b => b.Guid)); // what came off before it: the caller puts it back
        Assert.Equal([("Old Cap", true), ("Old Gloves", false)], offs);
        Assert.Contains("'Old Gloves'", result.Why, StringComparison.Ordinal);
        Assert.True(c.Worn(gloves) && !c.Worn(robe));
    }

    [Fact]
    public void The_slot_of_a_ring_is_chosen_by_the_one_rule_the_right_hand_first_then_the_free_side()
    {
        var c = new Closet();
        var first = c.Own("Ring", Jewellery, RingLeft | RingRight);
        var second = c.Own("Ring", Jewellery, RingLeft | RingRight);

        var one = c.PutOn(new WieldRequest(first) { Name = "Ring", Valid = RingLeft | RingRight });
        var two = c.PutOn(new WieldRequest(second) { Name = "Ring", Valid = RingLeft | RingRight });

        Assert.Equal((RingRight, RingLeft), (one.Location, two.Location)); // WieldSlotChoice.Choose: the preferred side, then the free one
        Assert.Equal([RingRight, RingLeft], c.Wields.Select(w => (uint)w.Amount));
        Assert.True(c.Worn(first) && c.Worn(second));
        Assert.Equal(first, c.Inventory.EquippedAt(RingRight));
        Assert.Equal(second, c.Inventory.EquippedAt(RingLeft));
    }

    [Fact]
    public void A_wield_the_server_refuses_is_refused_by_name_and_is_not_sent_again()
    {
        var c = new Closet();
        var cap = c.Own("Cap", Clothing, Head);
        c.RefuseWield.Add(cap);

        var result = c.PutOn(new WieldRequest(cap) { Name = "Cap", Location = Head });

        Assert.Equal(Wardrobe.Refused, result.Refusal);
        Assert.True(result.Sent);
        Assert.False(result.Confirmed || result.Worn);
        Assert.Single(c.Wields); // never retried here: a second try is the caller's named recovery
        Assert.False(c.Worn(cap));
        Assert.Contains("is not worn after the wield at 0x1", result.Why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_confirmed_wield_the_callers_reading_does_not_see_worn_is_not_worn_by_name_after_the_callers_wait()
    {
        var c = new Closet();
        var cap = c.Own("Cap", Clothing, Head);
        var asked = 0;
        var start = c.View.Clock.Now;

        var result = c.PutOn(new WieldRequest(cap) { Name = "Cap", Location = Head, WornSeconds = 2, Worn = () => { asked++; return false; } });

        Assert.Equal(Wardrobe.NotWorn, result.Refusal);
        Assert.True(result.Confirmed);
        Assert.False(result.Worn);
        Assert.True(asked > 2); // asked each frame of the wait, then once for the check after
        Assert.True(c.View.Clock.Now - start >= 2);
        Assert.Single(c.Wields);
    }

    [Fact]
    public void The_wait_for_the_worn_state_ends_as_soon_as_the_callers_reading_holds()
    {
        var c = new Closet();
        var cap = c.Own("Cap", Clothing, Head);
        var start = c.View.Clock.Now;

        var result = c.PutOn(new WieldRequest(cap) { Name = "Cap", Location = Head, WornSeconds = 30, Worn = () => c.Worn(cap) });

        Assert.Null(result.Refusal);
        Assert.True(result.Worn);
        Assert.True(c.View.Clock.Now - start < 5);
    }

    [Fact]
    public void A_take_off_alone_is_sent_waited_for_and_read()
    {
        var c = new Closet();
        var cap = c.OwnWorn("Cap", Clothing, Head, Head);
        var stuck = c.OwnWorn("Gloves", Clothing, Hands, Hands);
        c.RefuseOff.Add(stuck);
        var off = new TakeOffResult();
        var stays = new TakeOffResult();

        c.Drive(Wardrobe.TakeOff(c.View, new WieldBlocker(cap, "Cap") { OffSeconds = 2 }, off));
        c.Drive(Wardrobe.TakeOff(c.View, new WieldBlocker(stuck, "Gloves"), stays));

        Assert.True(off.Off && !c.Worn(cap));
        Assert.Equal(ActionOutcomeKind.Confirmed, off.Handle!.Outcome!.Value.Kind);
        Assert.False(stays.Off);
        Assert.True(c.Worn(stuck));
        Assert.NotEqual(ActionOutcomeKind.Confirmed, stays.Handle!.Outcome!.Value.Kind);
    }

    [Fact]
    public void The_tick_driven_half_checks_before_chooses_the_slot_and_sends_the_one_wield()
    {
        var c = new Closet();
        var ring = c.Own("Ring", Jewellery, RingLeft | RingRight);
        var worn = c.OwnWorn("Cap", Clothing, Head, Head);
        var request = new WieldRequest(ring) { Name = "Ring", Valid = RingLeft | RingRight };
        var result = new WieldResult();
        var refused = new WieldResult();

        Assert.Null(Wardrobe.Send(c.View, new WieldRequest(worn) { Name = "Cap", Location = Head }, refused));
        var handle = Wardrobe.Send(c.View, request, result);

        Assert.Equal(Wardrobe.WornAlready, refused.Refusal);
        Assert.NotNull(handle);
        Assert.Same(handle, result.Handle);
        Assert.Equal(RingRight, result.Location);
        for (var i = 0; i < 40 && !handle.IsCompleted; i++)
        {
            c.Step();
        }

        Wardrobe.ReadAfter(c.View, request, result);
        Assert.Null(result.Refusal);
        Assert.True(result.Worn && c.Worn(ring));
        Assert.Single(c.Wields);
        Assert.Throws<ArgumentException>(() => Wardrobe.Send(c.View, request with { TakeOff = [new WieldBlocker(worn, "Cap")] }, new WieldResult()));
    }
}
