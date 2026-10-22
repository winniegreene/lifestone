using System.Collections.Immutable;
using System.Globalization;
using Lifestone.Client.Session;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 2.1 s3: the stand-in's login burst announces the side packs as ACE does (cache/m4/2-1/capture.md C1): one 0196 per side pack right after the description and before every CreateObject; then the main pack's items (the packs among
/// them), then the contents of the side packs with the pack as their container, then the equipped objects. A description with no pack gets a labelled one with one item.
/// </summary>
public sealed class StandInSidePackTests
{
    private const uint Recorded = 0x50000077;
    private const uint Player = StandInSessions.FirstCharacterGuid;
    private const uint Sack = 0x80000201;
    private const uint Loose = 0x80000202;
    private const uint Worn = 0x80000203;

    private sealed class Spy : IAcSessionSink
    {
        public List<string> Log { get; } = [];

        public PlayerDescriptionMsg? Description { get; private set; }

        public void OnSessionEvent(in SessionEvent sessionEvent)
        {
        }

        public void OnPlayerCreate(in InboundContext context, in PlayerCreateMsg message) => Log.Add("F746");

        public void OnPlayerDescription(in InboundContext context, in GameEventHeader header, PlayerDescriptionMsg message)
        {
            Description = message;
            Log.Add("0013");
        }

        public void OnViewContents(in InboundContext context, in GameEventHeader header, in ViewContentsEvent message) =>
            Log.Add("0196 " + message.Container.ToString("X8", CultureInfo.InvariantCulture) + " " + message.Entries.Length.ToString(CultureInfo.InvariantCulture));

        public void OnCreateObject(in InboundContext context, CreateObjectMsg message, bool isUpdate) =>
            Log.Add("F745 " + message.Guid.ToString("X8", CultureInfo.InvariantCulture) + (message.Weenie.ContainerId is { } c ? " in " + c.ToString("X8", CultureInfo.InvariantCulture) : string.Empty));
    }

    private static CreateObjectMsg Item(uint guid, uint? container, uint? wielder, uint itemType = 1) => new(
        guid,
        ModelData.Empty,
        new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.Gravity, Timestamps = default },
        new WeenieDesc { Header = WeenieHeaderFlags.None, WeenieClassId = 3, IconId = 0x06000001, ItemType = itemType, ObjectDescriptionFlags = 0, Name = "Item " + guid.ToString("X", CultureInfo.InvariantCulture), ContainerId = container, WielderId = wielder });

    private static Spy Login(bool withSack)
    {
        ContentDesc[] inventory = withSack ? [new ContentDesc(Loose, 0), new ContentDesc(Sack, 1)] : [new ContentDesc(Loose, 0)];
        var items = ImmutableArray.CreateBuilder<CreateObjectMsg>();
        items.Add(Item(Loose, Recorded, null));
        if (withSack)
        {
            items.Add(Item(Sack, Recorded, null, 0x200));
        }

        items.Add(Item(Worn, null, Recorded));
        var recorded = RecordedDescription.Of(
            "20260101T000000Z-headless.lsrec",
            new PlayerDescriptionMsg { Inventory = [.. inventory], Equipped = [new EquippedDesc(Worn, 0x100000, 1)] },
            Recorded,
            items.ToImmutable());
        var spy = new Spy();
        StandInSessions.Enter(spy, Player, "Stand In", new StandInVisit { Recorded = recorded });
        return spy;
    }

    private static string Hex(uint guid) => guid.ToString("X8", CultureInfo.InvariantCulture);

    [Fact]
    public void A_recorded_side_pack_gets_its_0196_right_after_the_description_before_every_CreateObject_and_the_equipped_objects_come_last()
    {
        var spy = Login(withSack: true);

        Assert.Equal(["F746", "0013", "0196 " + Hex(Sack) + " 0"], spy.Log[..3]);
        var created = spy.Log.Where(l => l.StartsWith("F745", StringComparison.Ordinal)).ToList();
        Assert.Equal("F745 " + Hex(Player), created[0]); // the player's own object follows the 0196s
        Assert.Equal(["F745 " + Hex(Loose) + " in " + Hex(Player), "F745 " + Hex(Sack) + " in " + Hex(Player)], created.Skip(1).Take(2));
        Assert.StartsWith("F745 " + Hex(Worn), created[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_description_with_no_side_pack_gets_a_labelled_Pack_with_one_item_the_0196_names_and_the_item_is_created_with_the_pack_as_its_container()
    {
        var spy = Login(withSack: false);

        var packGuid = RecordedDescription.PlaceholderPackGuid;
        var itemGuid = RecordedDescription.PlaceholderPackItemGuid;
        Assert.Equal("0196 " + Hex(packGuid) + " 1", spy.Log[2]);
        Assert.Contains(new ContentDesc(packGuid, 1), spy.Description!.Inventory);
        var created = spy.Log.Where(l => l.StartsWith("F745", StringComparison.Ordinal)).ToList();
        var pack = created.IndexOf("F745 " + Hex(packGuid) + " in " + Hex(Player));
        var item = created.IndexOf("F745 " + Hex(itemGuid) + " in " + Hex(packGuid));
        Assert.True(pack >= 0 && item > pack, string.Join(" | ", created));
        Assert.True(created.IndexOf("F745 " + Hex(Loose) + " in " + Hex(Player)) < pack); // the main pack's items first
        Assert.True(item < created.Count - 1); // the equipped object after the side pack's contents
        Assert.StartsWith("F745 " + Hex(Worn), created[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_added_pack_is_labelled_a_placeholder_in_its_name()
    {
        var recorded = RecordedDescription.Of("x.lsrec", new PlayerDescriptionMsg { Inventory = [] }, Recorded, []);

        var burst = recorded.SidePacks(Player);

        Assert.True(burst.Added);
        Assert.Contains("placeholder", Assert.Single(burst.Extra).Weenie.Name, StringComparison.Ordinal);
    }
}
