using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 3.2 s4c: the vendor, live, through <see cref="IActionApi"/> only (the capture test that recorded ACE's raw order, <c>VendorCaptureLiveTests</c>, is removed: its findings are cache/m4/3-2/capture.md). WRITTEN AND COMPILED BY THE
/// SLICE, RUN BY THE LEAD: it needs the backend with the Lifestone server mod ready and Holtburg Brawler built (<c>--scenario=fighter-build --account=test-melee</c>); the character is neither created, levelled, reset nor purged here, and its pack is as it was
/// at the end (a kit bought is sold back; the two items the test grants itself are sold to Renald, so nothing is left over). The test does not depend on what the hunter carries (the hunt loop sells its spare loot, which is what failed the
/// first gate): after the log in it grants one Shirt (wcid 130, Clothing, inside Renald's mask) and one Leather Bracers (wcid 25637, Armor, outside Thelnoth's mask) with the mod's <c>item</c> verb (<see cref="PlayerTools.SpawnItem"/>: items
/// only, never a creature, D23) and waits for each item's F745 in the pack. One session, about 3 minutes (the log in, the two grants, two <c>place loc</c> into the shops with their settles, two vendor opens of about 2 s each, the
/// buy and the sells at about 1 s each, the log off).
/// <list type="bullet">
/// <item><b>(a) Thelnoth Cort the Healer</b> (<c>place loc</c> into cell 0xA9B40111 within his 3 m UseRadius, <see cref="IActionApi.OpenVendor"/> Confirmed on the 0062): one Handy Healing Kit 628 bought with <see cref="IActionApi.Buy"/> at the
/// price the 0062 predicts (<see cref="VendorStock.PriceToBuy(uint, uint)"/>: 14 for Value 10), the CoinValue delta equals the prediction and the kit's F745 arrived; then the new kit sold back (<see cref="VendorStock.PayoutFor(WorldObject)"/>: 9), the
/// payout equals the prediction and the hunter holds as many kits as before. Verdict lines <c>vendor buy=&lt;ok&gt; price=&lt;n&gt; predicted=&lt;n&gt; coins_delta=&lt;n&gt; kit_arrived=&lt;yes|no&gt;</c> and <c>vendor sellback=... payout= predicted= coins_delta= kits=&lt;after&gt;/&lt;before&gt;</c>.</item>
/// <item><b>(c) An item outside Thelnoth's mask</b> (the granted Leather Bracers, ItemType 0x2, not in his mask 0x44080): <see cref="IActionApi.Sell"/> is not a success (a 00A0 is a ServerError with the reason, code 0), the reason names the 02EB text
/// "... is unsellable." and the item is still in the pack. Verdict <c>vendor refused=&lt;ok&gt; kind= detail='...' item_kept=&lt;yes|no&gt;</c>.</item>
/// <item><b>(b) Shopkeeper Renald the Elder</b> (cell 0xA9B40141, 5 m): the granted Shirt sold; the CoinValue delta equals <see cref="VendorStock.PayoutFor(WorldObject)"/>. Verdict <c>vendor sold=&lt;ok&gt; item='...' payout=&lt;n&gt; predicted=&lt;n&gt;
/// coins_delta=&lt;n&gt; gone=&lt;yes|no&gt;</c>. Then the Bracers (armour is in his mask) are sold to him too, checked the same way but without a verdict line (the test has four).</item>
/// </list>
/// A phase that cannot start (no kit row, a grant that never arrived, a vendor that never came into the object table) is recorded as a problem and named; the test fails with all of them. Run it alone with
/// <c>dotnet test tests/Lifestone.Loopback.Tests --filter "FullyQualifiedName~VendorLiveTests" --logger "console;verbosity=detailed"</c>.
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-melee")]
[LoopbackCharacter("Holtburg Brawler")]
[LiveSharedSession]
public sealed class VendorLiveTests(ITestOutputHelper output)
{
    private const string Item = "3-2";

    [LoopbackFact]
    public void Holtburg_Brawler_buys_one_kit_at_the_predicted_price_sells_it_back_is_refused_an_unsellable_item_and_sells_one_looted_item()
    {
        using var dats = DatSet.Open();
        var leg = new VendorLeg(output.WriteLine, dats);
        var result = LiveRun.Run(output, leg, "vendor-brawler", 360, item: Item, character: FighterTemplate.Melee.Name);
        foreach (var line in leg.Verdicts)
        {
            output.WriteLine(line);
        }

        Assert.True(leg.Problems.Count == 0, "the checks that failed: " + string.Join("; ", leg.Problems) + " | " + string.Join(" | ", leg.Verdicts));
        Assert.True(result.Ok, result.Failure + " | " + string.Join(" | ", leg.Verdicts));
        Assert.Equal(4, leg.Verdicts.Count);
    }

    private sealed class VendorLeg(Action<string> log, DatSet dats) : HunterShopLeg("vendor-brawler", FighterTemplate.Melee.Name, log, dats)
    {
        private const uint PyrealWcid = 273;
        private const int PyrealGrant = 100;
        private const long PyrealFloor = 50;
        private const uint ShirtWcid = 130;
        private const uint BracersWcid = 25637;
        private readonly List<string> _verdicts = [];
        private uint _granted;
        private uint _shirt;
        private uint _bracers;

        public IReadOnlyList<string> Verdicts => _verdicts;

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in EnterHunter()) { yield return h; }

            // ---- M4 P4: Holtburg Brawler has no pyreals (the Hunter's came from its loot sales): the buy needs some, so the test grants a stack of Pyreals (wcid 273, the mod's item verb: an item, never a creature, D23) when the pack holds fewer than the floor ----
            if (Coins() < PyrealFloor)
            {
                Log($"the pack holds {Coins()} pyreals, below {PyrealFloor}: granting a stack of {PyrealGrant}");
                foreach (var h in Grant(PyrealWcid, "Pyreals", PyrealGrant)) { yield return h; }

                foreach (var h in Pause(1)) { yield return h; }

                Check(Coins() >= PyrealFloor, $"the Pyreal grant did not raise the CoinValue to {PyrealFloor} (it is {Coins()})");
            }

            // ---- the test's own sale items: an ITEM each (D23), never loot the hunt loop may already have sold ----
            foreach (var h in Grant(ShirtWcid, "Shirt")) { yield return h; }

            _shirt = _granted;
            foreach (var h in Grant(BracersWcid, "Leather Bracers")) { yield return h; }

            _bracers = _granted;

            // ---- (a) Thelnoth: open, buy one kit, sell it back ----
            foreach (var h in GoNear("Thelnoth", ThelnothCell, ThelnothAt, 1.5f, 3.0)) { yield return h; }

            var thelnoth = NpcGuid;
            if (thelnoth == 0)
            {
                Check(false, "(a): Thelnoth Cort the Healer never came into the object table; nothing was done");
                foreach (var h in LogOffAndQuit()) { yield return h; }

                yield break;
            }

            var open = S.Actions.OpenVendor(thelnoth, ActionSource.Host);
            yield return open;
            var opened = Confirmed(open, "(a) OpenVendor Thelnoth") && S.State.Vendor is { } && S.State.Vendor.Vendor == thelnoth;
            Check(opened, "(a): the vendor model holds Thelnoth's 0062 after the open");
            var stock = S.State.Vendor;
            var row = stock?.Entries.FirstOrDefault(e => e.WeenieClassId == KitWcid);
            if (stock is null || row is null)
            {
                Check(false, "(a): Thelnoth's 0062 holds no Handy Healing Kit 628; no buy");
            }
            else
            {
                foreach (var h in BuyAndSellBack(thelnoth, stock, row)) { yield return h; }

                // ---- (c) an item outside Thelnoth's mask is refused ----
                foreach (var h in Refusal(thelnoth, stock)) { yield return h; }
            }

            // ---- (b) Renald: one looted item sold ----
            foreach (var h in GoNear("Renald", RenaldCell, RenaldAt, 2.0f, 5.0)) { yield return h; }

            var renald = NpcGuid;
            if (renald == 0)
            {
                Check(false, "(b): Renald the Elder never came into the object table; no sale");
            }
            else
            {
                foreach (var h in SellOne(renald)) { yield return h; }

                foreach (var h in SellBracers(renald)) { yield return h; }
            }

            foreach (var h in LogOffAndQuit()) { yield return h; }
        }

        private IEnumerable<ActionHandle?> BuyAndSellBack(uint thelnoth, VendorStock stock, VendorStockEntry row)
        {
            var kitsBefore = InPack(KitWcid);
            var predicted = stock.PriceToBuy(row, 1);
            var coinsBefore = Coins();
            var buy = S.Actions.Buy(thelnoth, [(row.Guid, 1u)], ActionSource.Host);
            yield return buy;
            var bought = Confirmed(buy, "(a) Buy 1 kit") && buy.Result is { } r && r.Matched;
            foreach (var h in Until(() => InPack(KitWcid).Count > kitsBefore.Count, 5)) { yield return h; }

            var arrived = InPack(KitWcid).Count == kitsBefore.Count + 1;
            var delta = Coins() - coinsBefore;
            var ok = bought && arrived && delta == -(long)predicted && buy.Result!.Actual == predicted && predicted == 14;
            Check(ok, $"(a): the buy was Confirmed ({bought}), the kit arrived ({arrived}), the CoinValue delta {delta} is -{predicted} and the price is 14");
            _verdicts.Add($"vendor buy={Yes(ok)} price={buy.Result?.Actual.ToString(CultureInfo.InvariantCulture) ?? "n/a"} predicted={predicted} coins_delta={delta} kit_arrived={Yes(arrived)}");
            foreach (var h in Pause(1)) { yield return h; }

            var fresh = InPack(KitWcid).Except(kitsBefore).ToList();
            if (fresh.Count != 1 || !S.State.Objects.TryGet(fresh[0], out var newKit))
            {
                Check(false, "(a): no single new kit to sell back");
                _verdicts.Add("vendor sellback=no payout=n/a predicted=n/a coins_delta=n/a kits=n/a");
                yield break;
            }

            var payoutPredicted = S.State.Vendor?.PayoutFor(newKit) ?? 0;
            var coinsMid = Coins();
            var sell = S.Actions.Sell(thelnoth, [fresh[0]], ActionSource.Host);
            yield return sell;
            var sold = Confirmed(sell, "(a) Sell the new kit back") && sell.Result is { } sr && sr.Matched;
            foreach (var h in Pause(1)) { yield return h; }

            var payoutDelta = Coins() - coinsMid;
            var kitsAfter = InPack(KitWcid).Count;
            var okSell = sold && payoutDelta == payoutPredicted && payoutPredicted == 9 && kitsAfter == kitsBefore.Count;
            Check(okSell, $"(a): the sale back was Confirmed ({sold}), the payout {payoutDelta} is the predicted {payoutPredicted} (9) and the hunter holds {kitsAfter} kits (was {kitsBefore.Count})");
            _verdicts.Add($"vendor sellback={Yes(okSell)} payout={sell.Result?.Actual.ToString(CultureInfo.InvariantCulture) ?? "n/a"} predicted={payoutPredicted} coins_delta={payoutDelta} kits={kitsAfter}/{kitsBefore.Count}");
        }

        private IEnumerable<ActionHandle?> Refusal(uint thelnoth, VendorStock stock)
        {
            if (_bracers == 0 || !S.State.Self.Inventory.Contains(_bracers) || !S.State.Objects.TryGet(_bracers, out var bracersObject) || (bracersObject.Weenie.ItemType & 0x2) == 0 || (bracersObject.Weenie.ItemType & stock.Mask) != 0)
            {
                Check(false, "(c): the granted Leather Bracers are not in the pack as armour outside Thelnoth's mask; no refusal case");
                _verdicts.Add("vendor refused=no kind=n/a detail='no item' item_kept=n/a");
                yield break;
            }

            var coinsBefore = Coins();
            var sell = S.Actions.Sell(thelnoth, [_bracers], ActionSource.Host);
            yield return sell;
            foreach (var h in Pause(1)) { yield return h; }

            var detail = sell.Outcome?.Detail ?? string.Empty;
            var kept = S.State.Self.Inventory.Contains(_bracers);
            var ok = sell.Outcome is { Succeeded: false } && detail.Contains("unsellable", StringComparison.Ordinal) && kept && Coins() == coinsBefore;
            Check(ok, $"(c): offering the granted '{bracersObject.Weenie.Name}' (ItemType 0x{bracersObject.Weenie.ItemType:X}) was refused with the unsellable text (kind {sell.Outcome?.Kind}, '{detail}'), the item is kept ({kept}) and the coins are as they were");
            _verdicts.Add($"vendor refused={Yes(ok)} kind={sell.Outcome?.Kind} detail='{detail}' item_kept={Yes(kept)}");
        }

        /// <summary>Asks the mod's item verb for one <paramref name="wcid"/> (an item, never a creature), waits for its F745 in the pack and leaves its guid in <see cref="_granted"/> (0 when it never arrived).</summary>
        private IEnumerable<ActionHandle?> Grant(uint wcid, string what, int? stack = null)
        {
            _granted = 0;
            var spawn = S.Tools.SpawnItem(wcid, stack, ActionSource.Host);
            yield return spawn;
            if (!Check(spawn.Result is not null, $"the {what} grant (wcid {wcid}) failed: " + Describe(spawn)))
            {
                yield break;
            }

            var guid = spawn.Result!.Guid;
            foreach (var h in WaitHeld(guid, what)) { yield return h; }

            if (Check(S.State.Self.Inventory.Contains(guid), $"the granted {what} {guid:X8} is not in the pack"))
            {
                _granted = guid;
                var weenie = S.State.Objects.TryGet(guid, out var w) ? w.Weenie : null;
                Log($"granted: '{weenie?.Name ?? what}' {guid:X8} (wcid {wcid}, ItemType 0x{weenie?.ItemType ?? 0:X}, Value {weenie?.Value})");
            }

            foreach (var h in Pause(1)) { yield return h; } // the grant's own messages settle before the next action
        }

        /// <summary>Sells the granted Bracers to Renald (armour is in his mask) so the hunter ends with nothing extra; the payout is checked like (b), without a verdict line.</summary>
        private IEnumerable<ActionHandle?> SellBracers(uint renald)
        {
            var stock = S.State.Vendor;
            if (_bracers == 0 || stock is null || stock.Vendor != renald || !S.State.Objects.TryGet(_bracers, out var bracers) || !S.State.Self.Inventory.Contains(_bracers))
            {
                Check(false, "(b): the granted Bracers could not be sold back to Renald (not in the pack, or his stock is not open)");
                yield break;
            }

            var predicted = stock.PayoutFor(bracers);
            var coinsBefore = Coins();
            var sell = S.Actions.Sell(renald, [_bracers], ActionSource.Host);
            yield return sell;
            var confirmed = Confirmed(sell, "(b) Sell the granted Bracers") && sell.Result is { Matched: true };
            foreach (var h in Pause(1)) { yield return h; }

            var delta = Coins() - coinsBefore;
            var gone = !S.State.Self.Inventory.Contains(_bracers);
            Log($"left the pack: the granted '{bracers.Weenie.Name}' {_bracers:X8} to Renald (payout {delta})");
            Check(confirmed && gone && delta == predicted, $"(b): selling the granted '{bracers.Weenie.Name}' was Confirmed ({confirmed}), it left the pack ({gone}) and the CoinValue delta {delta} is the predicted {predicted}");
        }

        private IEnumerable<ActionHandle?> SellOne(uint renald)
        {
            var open = S.Actions.OpenVendor(renald, ActionSource.Host);
            yield return open;
            var stock = S.State.Vendor;
            if (!Confirmed(open, "(b) OpenVendor Renald") || stock is null || stock.Vendor != renald)
            {
                Check(false, "(b): Renald's 0062 did not arrive; no sale");
                _verdicts.Add("vendor sold=no item='n/a' payout=n/a predicted=n/a coins_delta=n/a gone=n/a");
                yield break;
            }

            if (_shirt == 0 || !S.State.Self.Inventory.Contains(_shirt) || !S.State.Objects.TryGet(_shirt, out var shirt) || !stock.Buys(shirt))
            {
                Check(false, "(b): the granted Shirt is not in the pack or Renald's mask does not take it; no sale");
                _verdicts.Add("vendor sold=no item='n/a' payout=n/a predicted=n/a coins_delta=n/a gone=n/a");
                yield break;
            }

            var predicted = stock.PayoutFor(shirt);
            var coinsBefore = Coins();
            var sell = S.Actions.Sell(renald, [_shirt], ActionSource.Host);
            yield return sell;
            var confirmed = Confirmed(sell, "(b) Sell the granted Shirt") && sell.Result is { } r && r.Matched;
            foreach (var h in Pause(1)) { yield return h; }

            var delta = Coins() - coinsBefore;
            var gone = !S.State.Self.Inventory.Contains(_shirt);
            var ok = confirmed && gone && delta == predicted;
            Log($"left the pack: the granted '{shirt.Weenie.Name}' {_shirt:X8} to Renald (payout {delta})");
            Check(ok, $"(b): selling the granted '{shirt.Weenie.Name}' (Value {shirt.Weenie.Value}) was Confirmed ({confirmed}), it left the pack ({gone}) and the CoinValue delta {delta} is the predicted {predicted}");
            _verdicts.Add($"vendor sold={Yes(ok)} item='{shirt.Weenie.Name}' payout={sell.Result?.Actual.ToString(CultureInfo.InvariantCulture) ?? "n/a"} predicted={predicted} coins_delta={delta} gone={Yes(gone)}");
        }
    }
}

/// <summary>
/// What the vendor and appraisal live legs share (M4 3.2 s4c): the entry as the fighter it is given (never created, reset or purged here), the walk into a shop (<c>place loc</c> into the NPC's cell beside him, the opposite side when the first does not land
/// inside the cell and the radius: <c>TODO(m4-vendor-approach)</c>), and the pack reads. Not a test itself.
/// </summary>
internal abstract class HunterShopLeg(string name, string characterName, Action<string> log, DatSet dats) : LiveLeg(name, [], log, dats)
{
    /// <summary>The fighter the leg runs as (M4 P4: Holtburg Brawler for the vendor, Holtburg Archer for the appraisal; was the Hunter's).</summary>
    internal string CharacterName => characterName;

    internal const uint KitWcid = 628;
    internal const uint MaceWcid = 331;
    internal const uint CoinProperty = 20;
    internal const uint ThelnothCell = 0xA9B40111;
    internal const uint RenaldCell = 0xA9B40141;
    internal static readonly Vector3 ThelnothAt = new(30.02f, 159.68f, 66.01f);
    internal static readonly Vector3 RenaldAt = new(139.23f, 18.33f, 94.01f);

    /// <summary>Wcids of what the sale never offers: the kits, potions, draughts, the fighter's Mace and Shortbow, arrows and pyreals.</summary>
    private static readonly HashSet<uint> KeepWcids = [628, 629, 377, 378, 379, 2457, 2460, 27326, 331, 307, 300, 273];

    private const double ArrivalSeconds = 90;

    protected uint SelfGuid { get; private set; }

    protected uint NpcGuid { get; private set; }

    protected static string Yes(bool value) => value ? "yes" : "no";

    protected long Coins() => S.State.Self.Qualities.TryGetInt(CoinProperty, out var value) ? value : -1;

    /// <summary>Logs in, chooses the leg's fighter by name, enters the world and waits for the arrival with the qualities known.</summary>
    protected IEnumerable<ActionHandle?> EnterHunter()
    {
        if (AlreadyInWorld)
        {
            Stage = "running";
            Follow ??= new HuntFollow(Dats, new RecordingLogSink());
            Log("already in the world: the shared session's entry logged the character in");
            SelfGuid = S.State.Self.Guid;
            yield break;
        }

        Stage = "login";
        var login = S.Actions.Login(ActionSource.Host);
        yield return login;
        Require(login.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "login: " + Describe(login));
        Follow ??= new HuntFollow(Dats, new RecordingLogSink());

        Stage = "choosing";
        CharacterSlot? slot = S.State.Characters.Entries.OrderBy(c => c.PendingDeletion).ThenBy(c => c.Guid).Cast<CharacterSlot?>().FirstOrDefault(c => string.Equals(c!.Value.Name, CharacterName, StringComparison.OrdinalIgnoreCase));
        Require(slot is { PendingDeletion: false }, $"'{CharacterName}' is not on the account (or is pending deletion): build it first; this test never creates or purges a character");
        Stage = "entering";
        var enter = S.Actions.EnterWorld(slot!.Value.Guid, ActionSource.Host);
        yield return enter;
        Require(enter.Outcome is { Kind: ActionOutcomeKind.Confirmed }, $"entering the world as {CharacterName}: " + Describe(enter));
        Stage = "arriving";
        var since = S.Clock.Now;
        while (S.State.Self.Travel != SelfTravel.Present || !S.State.Self.Qualities.IsKnown)
        {
            Require(S.Clock.Now - since <= ArrivalSeconds, "arriving: the player was not unhidden with its qualities within " + ArrivalSeconds + " s");
            yield return null;
        }

        Stage = "running";
        SelfGuid = S.State.Self.Guid;
        Log("arrived as " + CharacterName);
    }

    /// <summary>The object whose name contains <paramref name="part"/> nearest the player within <paramref name="metres"/>, or 0.</summary>
    protected uint NearestContaining(string part, double metres)
    {
        if (S.State.Self.Position is not { } here)
        {
            return 0;
        }

        uint best = 0;
        var bestDistance = double.MaxValue;
        foreach (var o in S.State.Objects.All)
        {
            if (o.Guid == SelfGuid || o.Position is not { } p || p.CellId >> 16 != here.CellId >> 16 || !o.Name.Contains(part, StringComparison.Ordinal))
            {
                continue;
            }

            var d = Vector3.Distance(p.Origin, here.Origin);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = o.Guid;
            }
        }

        return bestDistance <= metres ? best : 0;
    }

    /// <summary>Places the hunter in <paramref name="cell"/> beside the NPC (<paramref name="offset"/> m on one side, then the other) and sets <see cref="NpcGuid"/> to the NPC when he is in the cell and the radius.</summary>
    protected IEnumerable<ActionHandle?> GoNear(string who, uint cell, Vector3 npc, float offset, double radius)
    {
        NpcGuid = 0;
        foreach (var side in new[] { offset, -offset })
        {
            var at = new Vector3(npc.X + side, npc.Y, npc.Z);
            foreach (var h in PlaceAndSettle(Destination($"{who} {side:+0.0;-0.0} m", cell, at), $"{who} {side:+0.0;-0.0} m")) { yield return h; }

            foreach (var h in Until(() => NearestContaining(who, 20) != 0, 10)) { yield return h; }

            NpcGuid = NearestContaining(who, 20);
            var here = S.State.Self.Position;
            var inCell = here is { } p && p.CellId == cell;
            var distance = double.NaN;
            if (NpcGuid != 0 && here is { } h0 && S.State.Objects.TryGet(NpcGuid, out var o) && o.Position is { } q)
            {
                distance = Vector3.Distance(q.Origin, h0.Origin);
            }

            Log($"{who}: placed {side:+0.0;-0.0} m; in the wanted cell {inCell}; the NPC {(NpcGuid == 0 ? "unknown" : "0x" + NpcGuid.ToString("X8", CultureInfo.InvariantCulture))} at {distance:0.0} m");
            if (NpcGuid != 0 && inCell && distance <= radius)
            {
                yield break;
            }
        }
    }

    /// <summary>The main pack's objects (ContainerType 0: no side pack, no focus).</summary>
    protected IEnumerable<(uint Guid, WorldObject Object)> PackItems()
    {
        foreach (var entry in S.State.Self.Inventory.Pack)
        {
            if (entry.ContainerType == 0 && S.State.Objects.TryGet(entry.Guid, out var o))
            {
                yield return (entry.Guid, o);
            }
        }
    }

    /// <summary>The guids of the pack items of weenie class <paramref name="wcid"/>, in the pack's order.</summary>
    protected List<uint> InPack(uint wcid) => PackItems().Where(p => p.Object.Weenie.WeenieClassId == wcid).Select(p => p.Guid).ToList();

    /// <summary>
    /// The cheapest pack item <paramref name="accept"/> takes that is not worn, not a kit, a potion, a draught, a tincture, the fighter's weapons, arrows or pyreals, has a Value of at least 1 and sits in the main pack (a stack of one is preferred
    /// to a stack of many when its Value is within 100).
    /// </summary>
    protected (uint Guid, WorldObject Object)? Cheapest(Func<WorldObject, bool> accept)
    {
        string[] keepNames = ["Kit", "Potion", "Draught", "Tincture", "Mace", "Bow", "Arrow", "Pyreal"];
        (uint Guid, WorldObject Object)? best = null;
        foreach (var (guid, o) in PackItems())
        {
            var w = o.Weenie;
            if (KeepWcids.Contains(w.WeenieClassId) || w.WielderId is { } wielder && wielder != 0 || keepNames.Any(n => w.Name.Contains(n, StringComparison.OrdinalIgnoreCase))
                || S.State.Self.Inventory.Find(guid).Kind != InventoryPlaceKind.MainPack || (w.Value ?? 0) < 1 || (w.ItemType & 0x200) != 0 || !accept(o))
            {
                continue;
            }

            var loneBetter = best is { } b && (b.Object.Weenie.StackSize ?? 1) > 1 && (w.StackSize ?? 1) <= 1 && w.Value <= b.Object.Weenie.Value + 100;
            if (best is null || w.Value < best.Value.Object.Weenie.Value || loneBetter)
            {
                best = (guid, o);
            }
        }

        return best;
    }
}
