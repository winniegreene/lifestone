using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Tests.Session.Journey;

/// <summary>
/// M6 item 16, slice E5b: the quest station's buy step at the vendor counter (<c>VendorCounter.Buy</c>), over the quest's stand-in ACE (<see cref="QuestRig"/>) with the scripted shop beside it (<see cref="ScriptedShop"/>). The step is
/// the Atlan wand's shape (its file's step: one Magic Glyph 19410 from Silencia the Archmage 2498 at 7,750 pyreals). What these prove: a buy the coin covers runs as before, with the same lines; a buy the coin does not cover is paid
/// from a trade note of the main pack, changed at the counter (the slice's one behaviour change: it was the stop <c>buy:purse-short</c>); with no note, or at a shop whose mask takes none, the stop <c>buy:purse-short</c> stands by name.
/// </summary>
public sealed class QuestBuyStepTests
{
    private const uint Silencia = 0x790D0003;
    private const uint SilenciaWcid = 2498;
    private const string SilenciaName = "Silencia the Archmage";
    private const uint Glyph = 19410;
    private const uint GlyphRow = 0x80006001;
    private const int GlyphPrice = 7_750;
    private const uint MiscType = VendorRig.MiscType;
    private const uint Note10k = 2625;

    private static readonly Vector3 SilenciaAt = new(90, 12, 94);

    private sealed class Shop
    {
        public Shop(int coins, uint mask)
        {
            Rig = new QuestRig();
            Vendor = new VendorRig(Rig.R, coins);
            Shops = new ScriptedShop(Vendor);
            Vendor.PlaceAt(Silencia, SilenciaName, QuestRig.HomeCell, SilenciaAt, 3f, SilenciaWcid);
            Shops.Add(Silencia, mask).Rows.Add((-1, GlyphRow, "Magic Glyph", Glyph, MiscType, GlyphPrice)); // the scripted shop sells at 1.0, so the row's Value is its price
            Rig.OnOpen[Silencia] = () => { }; // the Use of the vendor is answered by the shop's side (its UseDone and its 0062), not by the quest rig's plain UseDone
        }

        public QuestRig Rig { get; }

        public VendorRig Vendor { get; }

        public ScriptedShop Shops { get; }

        public long Coin => Vendor.State.Self.Qualities.CoinValue;

        public uint Note()
        {
            var guid = Vendor.NextGuid();
            Vendor.Arrive(guid, "Trade Note (10,000)", Note10k, VendorStock.PromissoryNoteType, 10_000);
            Vendor.Put(guid);
            return guid;
        }

        public (QuestRun Run, ScenarioStatus Status) Buy()
        {
            var silencia = new QuestThing(SilenciaWcid, SilenciaName);
            var glyph = new QuestThing(Glyph, "Magic Glyph");
            var quest = QuestRig.Quest(
                "test-buy",
                new QuestDone(null, null, glyph),
                null,
                QuestRig.Step(0, QuestStepKind.Go, "to Silencia", place: new QuestPlace(QuestRig.HomeCell, new Vector3(89, 12, 94), "the roof"), target: silencia),
                QuestRig.Step(1, QuestStepKind.Buy, "a Magic Glyph from Silencia", target: silencia, item: glyph, count: 1));
            var (run, status) = Rig.Run(new QuestRun(Rig.Context(), quest, JourneyPlans.FromHome, null), pump: Shops.Pump);
            return ((QuestRun)run, status);
        }

        public string Dump => string.Join(" | ", Rig.Lines("buy"));
    }

    [Fact]
    public void A_buy_the_coin_covers_runs_as_before_with_its_lines()
    {
        var shop = new Shop(10_000, MiscType | VendorStock.PromissoryNoteType);

        var (_, status) = shop.Buy();

        Assert.True(status == ScenarioStatus.Succeeded, shop.Dump);
        Assert.True(shop.Rig.Lines($"buy: check before: {SilenciaName} (0x{Silencia:X8}) open Confirmed: opened with 1 rows: Magic Glyph listed at 7750, 1 cost 7750 within the coin 10000; ").Count == 1, shop.Dump);
        Assert.Single(shop.Rig.Lines("buy: check after: 1 Magic Glyph (+1), paid 7750"));
        Assert.Contains(shop.Rig.Lines("quest_step: n=1 kind=buy"), l => l.Contains(" ok=yes ", StringComparison.Ordinal) && l.Contains("bought=1 paid=7750", StringComparison.Ordinal));
        Assert.Equal((1, 0, 2_250L), (shop.Shops.Buys, shop.Shops.Sells, shop.Coin));
        Assert.Empty(shop.Rig.Lines("purse:")); // no note is touched while the coin pays
    }

    [Fact]
    public void A_buy_the_coin_does_not_cover_is_paid_from_a_trade_note_changed_at_the_counter()
    {
        var shop = new Shop(100, MiscType | VendorStock.PromissoryNoteType);
        var note = shop.Note();

        var (_, status) = shop.Buy();

        Assert.True(status == ScenarioStatus.Succeeded, shop.Dump); // before E5b: the stop buy:purse-short ("the coin 100 does not cover 1 Magic Glyph at 7750 each (7750)")
        Assert.Single(shop.Rig.Lines("Magic Glyph listed at 7750, 1 cost 7750: the coin 100 is short by 7650 and the trade notes held (10000) cover it, so a note is changed at the counter"));
        Assert.Single(shop.Rig.Lines($"buy: purse: the coin was short: changed Trade Note (10,000) to coin at {SilenciaName}: paid 10000 pyreals (the coin 100 -> 10100)"));
        Assert.Single(shop.Rig.Lines($"buy: paid from the purse: 1 trade note(s) changed to coin at {SilenciaName} before the buy"));
        Assert.Single(shop.Rig.Lines("buy: check after: 1 Magic Glyph (+1), paid 7750"));
        Assert.Contains(shop.Rig.Lines("quest_step: n=1 kind=buy"), l => l.Contains(" ok=yes ", StringComparison.Ordinal) && l.Contains("bought=1 paid=7750", StringComparison.Ordinal));
        Assert.Equal((1, 1, 100 + 10_000L - GlyphPrice), (shop.Shops.Buys, shop.Shops.Sells, shop.Coin)); // the note at its face (Vendor.cs 591-599 @95ef426d), then the glyph
        Assert.False(shop.Vendor.State.Self.Inventory.Contains(note));
    }

    [Fact]
    public void A_short_purse_with_no_note_to_change_is_still_the_stop_buy_purse_short_by_name()
    {
        var bare = new Shop(100, MiscType | VendorStock.PromissoryNoteType);
        var (run, status) = bare.Buy();
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("buy:purse-short: the coin 100 does not cover 1 Magic Glyph at 7750 each (7750)", run.Failure, StringComparison.Ordinal);
        Assert.Contains(bare.Rig.Lines("quest_step: n=1 kind=buy"), l => l.Contains(" ok=no ", StringComparison.Ordinal) && l.Contains("fail=buy:purse-short", StringComparison.Ordinal));
        Assert.Equal((0, 0, 100L), (bare.Shops.Buys, bare.Shops.Sells, bare.Coin));

        var masked = new Shop(100, MiscType); // her mask takes no trade note: the note stays a note
        var note = masked.Note();
        var (refused, ended) = masked.Buy();
        Assert.Equal(ScenarioStatus.Failed, ended);
        Assert.Contains("buy:purse-short: the coin 100 does not cover 1 Magic Glyph at 7750 each (7750); the shop's mask takes no trade note (the 10000 held in notes are not changed here)", refused.Failure, StringComparison.Ordinal);
        Assert.Equal((0, 0), (masked.Shops.Buys, masked.Shops.Sells));
        Assert.True(masked.Vendor.State.Self.Inventory.Contains(note));
    }
}
