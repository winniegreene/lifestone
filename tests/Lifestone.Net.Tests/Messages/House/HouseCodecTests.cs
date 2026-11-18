using System.Text;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.House;

/// <summary>
/// ACE's housing writers (M6 villa), written here from ACE's source independently of Lifestone.Net's codecs:
/// <c>GameEventHouseProfile</c> + <c>HouseProfileExtensions.Write</c>, <c>GameEventHouseData</c> + <c>HouseDataExtensions.Write</c> (the Position
/// as cell, Vector3, then W X Y Z), <c>HousePaymentExtensions.Write</c> (i32 Num, i32 Paid, u32 wcid, two String16L), <c>GameEventHouseStatus</c>,
/// <c>GameEventHouseUpdateRestrictions</c> (a ByteSequence byte, then the unaligned words of <c>RestrictionDBExtensions.Write</c>) and
/// <c>GameEventHouseAvailableHouses</c>.
/// </summary>
internal static class AceHouseWriters
{
    public const uint Player = 0x50000001;

    public static byte[] Profile(uint sequence, uint guardian, uint dwelling, uint owner, int minLevel, uint type, string? ownerName, (int Num, int Paid, uint Wcid, string Name, string Plural)[] buy, (int Num, int Paid, uint Wcid, string Name, string Plural)[] rent, uint flags = 1, bool free = false) =>
        Event(sequence, 0x021D, w =>
        {
            w.Write(guardian);
            w.Write(dwelling);
            w.Write(owner);
            w.Write(flags);
            w.Write(minLevel);
            w.Write(-1);
            w.Write(-1);
            w.Write(-1);
            w.Write(Convert.ToUInt32(free));
            w.Write(type);
            AceWireWriters.WriteString16L(w, ownerName);
            Payments(w, buy);
            Payments(w, rent);
        });

    public static byte[] Data(uint sequence, uint buyTime, uint rentTime, uint type, (int Num, int Paid, uint Wcid, string Name, string Plural)[] buy, (int Num, int Paid, uint Wcid, string Name, string Plural)[] rent, uint cell, float x, float y, float z) =>
        Event(sequence, 0x0225, w =>
        {
            w.Write(buyTime);
            w.Write(rentTime);
            w.Write(type);
            w.Write(0u);
            Payments(w, buy);
            Payments(w, rent);
            w.Write(cell);
            w.Write(x);
            w.Write(y);
            w.Write(z);
            w.Write(1f); // W
            w.Write(0f);
            w.Write(0f);
            w.Write(0f);
        });

    public static byte[] Status(uint sequence, uint notice) => Event(sequence, 0x0226, w => w.Write(notice));

    public static byte[] Restrictions(uint sequence, byte objectSequence, uint house, uint owner, params (uint Guid, uint Access)[] guests) =>
        Event(sequence, 0x0248, w =>
        {
            w.Write(objectSequence);
            w.Write(house);
            w.Write(0x10000002u);
            w.Write(0u);
            w.Write(0u);
            w.Write((ushort)guests.Length);
            w.Write((ushort)768);
            foreach (var (guid, access) in guests)
            {
                w.Write(guid);
                w.Write(access);
            }
        });

    public static byte[] Available(uint sequence, uint type, uint[] locations, int total) =>
        Event(sequence, 0x0271, w =>
        {
            w.Write(type);
            w.Write(locations.Length);
            foreach (var location in locations)
            {
                w.Write(location);
            }

            w.Write(total);
        });

    private static void Payments(BinaryWriter w, (int Num, int Paid, uint Wcid, string Name, string Plural)[] lines)
    {
        w.Write(lines.Length);
        foreach (var line in lines)
        {
            w.Write(line.Num);
            w.Write(line.Paid);
            w.Write(line.Wcid);
            AceWireWriters.WriteString16L(w, line.Name);
            AceWireWriters.WriteString16L(w, line.Plural);
        }
    }

    private static byte[] Event(uint sequence, uint type, Action<BinaryWriter> payload)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            payload(writer);
        }

        return AceWireWriters.GameEvent(Player, sequence, type, stream.ToArray());
    }
}

/// <summary>The housing codecs (M6 villa) against ACE's writers, the dispatcher's routing, and the actions' bytes in ACE's reader order.</summary>
public sealed class HouseCodecTests
{
    private const uint Guardian = 0x7A9B4F10;
    private const string Envelope = "B1F70000";
    private static readonly InboundContext Context = new(3, MessageQueue.Ui, 99);

    private static readonly (int, int, uint, string, string)[] ApartmentBuy = [(100_000, 0, 273, "Pyreal", "Pyreals"), (1, 0, 11710, "Writ of Refuge", "Writs of Refuge")];

    private static readonly (int, int, uint, string, string)[] VillaRent = [(100_000, 50_000, 273, "Pyreal", "Pyreals"), (2, 0, 11710, "Writ of Refuge", "Writs of Refuge")];

    [Fact]
    public void An_unowned_apartment_profile_reads_every_field()
    {
        var message = AceHouseWriters.Profile(7, Guardian, 1234, 0, -1, 4, null, ApartmentBuy, ApartmentBuy);

        Assert.True(HouseCodecs.TryParseHouseProfile(message, out var profile, out var reason), reason);
        Assert.Equal(Guardian, profile.Guardian);
        Assert.Equal(1234u, profile.DwellingId);
        Assert.False(profile.IsOwned);
        Assert.Equal(HouseType.Apartment, profile.Kind);
        Assert.Equal(-1, profile.MinLevel);
        Assert.Equal(string.Empty, profile.OwnerName);
        Assert.Equal(HouseProfileMsg.ActiveFlag, profile.Flags);
        Assert.Equal(2, profile.Buy.Length);
        Assert.Equal(new HousePayment(100_000, 0, 273, "Pyreal", "Pyreals"), profile.Buy[0]);
        Assert.True(profile.Buy[0].IsMoney);
        Assert.Equal(new HousePayment(1, 0, 11710, "Writ of Refuge", "Writs of Refuge"), profile.Buy[1]);
        Assert.Equal(1, profile.Buy[1].Remaining);
    }

    [Fact]
    public void An_owned_villa_profile_carries_the_owner_and_the_paid_rent()
    {
        var message = AceHouseWriters.Profile(8, Guardian, 77, 0x50000ABC, 35, 2, "Lifestone Throwaway", [(2_000_000, 2_000_000, 273, "Pyreal", "Pyreals")], VillaRent);

        Assert.True(HouseCodecs.TryParseHouseProfile(message, out var profile, out var reason), reason);
        Assert.True(profile.IsOwned);
        Assert.Equal("Lifestone Throwaway", profile.OwnerName);
        Assert.Equal(HouseType.Villa, profile.Kind);
        Assert.Equal(35, profile.MinLevel);
        Assert.Equal(50_000, profile.Rent[0].Remaining);
        Assert.Equal(2, profile.Rent[1].Remaining);
    }

    [Fact]
    public void A_profile_with_a_byte_more_or_less_is_refused_with_its_reason()
    {
        var message = AceHouseWriters.Profile(7, Guardian, 1234, 0, -1, 4, null, ApartmentBuy, ApartmentBuy);

        Assert.False(HouseCodecs.TryParseHouseProfile([.. message, 0], out _, out var longer));
        Assert.Contains("unread bytes", longer, StringComparison.Ordinal);
        Assert.False(HouseCodecs.TryParseHouseProfile(message.AsSpan(0, message.Length - 2), out _, out var shorter));
        Assert.Contains("rent line", shorter, StringComparison.Ordinal);
    }

    [Fact]
    public void House_data_reads_the_times_the_lists_and_the_position()
    {
        var message = AceHouseWriters.Data(9, 1_700_000_000, 1_700_000_000, 4, ApartmentBuy, VillaRent, 0x9DAF0102, 12.5f, 20.25f, 94f);

        Assert.True(HouseCodecs.TryParseHouseData(message, out var data, out var reason), reason);
        Assert.Equal(1_700_000_000u, data.BuyTime);
        Assert.Equal(HouseType.Apartment, data.Kind);
        Assert.False(data.MaintenanceFree);
        Assert.Equal(2, data.Rent.Length);
        Assert.Equal(0x9DAF0102u, data.Position.CellId);
        Assert.Equal(new System.Numerics.Vector3(12.5f, 20.25f, 94f), data.Position.Origin);
        Assert.Equal(1f, data.Position.Rotation.W);
        Assert.False(HouseCodecs.TryParseHouseData(message.AsSpan(0, message.Length - 4), out _, out var cut));
        Assert.Contains("position", cut, StringComparison.Ordinal);
    }

    [Fact]
    public void House_status_is_one_word_and_ACE_default_is_BadParam()
    {
        var message = AceHouseWriters.Status(4, HouseStatusEvent.NoHouse);

        Assert.Equal(Convert.FromHexString("B0F70000" + "01000050" + "04000000" + "26020000" + "02000000"), message);
        Assert.True(HouseCodecs.TryParseHouseStatus(message, out var status));
        Assert.Equal(HouseStatusEvent.NoHouse, status.Notice);
        Assert.False(HouseCodecs.TryParseHouseStatus([.. message, 0, 0, 0, 0], out _));
    }

    [Fact]
    public void Restrictions_read_the_unaligned_words_after_the_sequence_byte()
    {
        var message = AceHouseWriters.Restrictions(5, 3, 0x79AF0100, 0x50000ABC, (0x50000ABC, 1), (0x50000DEF, 0));

        Assert.True(HouseCodecs.TryParseHouseRestrictions(message, out var restrictions, out var reason), reason);
        Assert.Equal(3, restrictions.Sequence);
        Assert.Equal(0x79AF0100u, restrictions.Object);
        Assert.Equal(0x10000002u, restrictions.Version);
        Assert.Equal((ushort)768, restrictions.Buckets);
        Assert.Equal([(0x50000ABCu, 1u), (0x50000DEFu, 0u)], restrictions.Entries.ToArray());
        Assert.False(HouseCodecs.TryParseHouseRestrictions(message.AsSpan(0, message.Length - 1), out _, out _));
    }

    [Fact]
    public void Available_houses_list_their_cells_and_the_total()
    {
        var message = AceHouseWriters.Available(6, 2, [0x9DAF0102, 0x9DB00102], 40);

        Assert.True(HouseCodecs.TryParseAvailableHouses(message, out var available, out var reason), reason);
        Assert.Equal(2u, available.Type);
        Assert.Equal([0x9DAF0102u, 0x9DB00102u], available.Locations.ToArray());
        Assert.Equal(40, available.Total);
    }

    [Fact]
    public void The_one_word_events_refuse_a_wrong_length()
    {
        var rentTime = AceWireWriters.GameEvent(AceHouseWriters.Player, 1, 0x0227, Convert.FromHexString("00000000"));
        var transaction = AceWireWriters.GameEvent(AceHouseWriters.Player, 1, 0x0259, Convert.FromHexString("02000000"));

        Assert.True(HouseCodecs.TryParseUpdateRentTime(rentTime, out var time));
        Assert.Equal(0u, time.RentTime);
        Assert.True(HouseCodecs.TryParseHouseTransaction(transaction, out var notice));
        Assert.Equal(2u, notice.Notice);
        Assert.False(HouseCodecs.TryParseHouseTransaction(transaction.AsSpan(0, transaction.Length - 1), out _));
        Assert.True(HouseCodecs.TryParseUpdateRentPayment(AceWireWriters.GameEvent(AceHouseWriters.Player, 1, 0x0228, Convert.FromHexString("00000000")), out var payment, out _));
        Assert.Empty(payment.Rent);
    }

    [Fact]
    public void The_dispatcher_routes_each_house_event_to_its_callback()
    {
        var handler = new Recorder();

        Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, AceHouseWriters.Profile(1, Guardian, 1, 0, -1, 4, null, ApartmentBuy, ApartmentBuy), handler).Outcome);
        Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, AceHouseWriters.Data(2, 1, 1, 4, ApartmentBuy, ApartmentBuy, 0x9DAF0102, 0, 0, 0), handler).Outcome);
        Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, AceHouseWriters.Status(3, 2), handler).Outcome);
        Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, AceHouseWriters.Restrictions(4, 1, 0x79AF0100, 0), handler).Outcome);
        Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, AceHouseWriters.Available(5, 4, [], 0), handler).Outcome);

        Assert.Equal(["profile:7A9B4F10", "data:4", "status:2", "restrictions:79AF0100", "available:4"], handler.Calls);
        var bad = InboundDispatcher.Dispatch(Context, AceHouseWriters.Status(6, 2).AsSpan(0, 18), handler);
        Assert.Equal(DispatchOutcome.Malformed, bad.Outcome);
        Assert.StartsWith("F7B0 GameEvent 0x0226 HouseStatus: ", handler.Malformed[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Buy_and_rent_are_the_guardian_then_a_counted_item_list()
    {
        Assert.Equal(
            Convert.FromHexString(Envelope + "09000000" + "1C020000" + "104F9B7A" + "02000000" + "01000080" + "02000080"),
            Build(w => HouseActionCodecs.WriteBuyHouse(w, 9, Guardian, [0x80000001, 0x80000002])));
        Assert.Equal(
            Convert.FromHexString(Envelope + "0A000000" + "21020000" + "104F9B7A" + "01000000" + "03000080"),
            Build(w => HouseActionCodecs.WriteRentHouse(w, 10, Guardian, [0x80000003])));
    }

    [Fact]
    public void The_payload_free_actions_and_query_lord_have_their_types()
    {
        Assert.Equal(Convert.FromHexString(Envelope + "01000000" + "1E020000"), Build(w => HouseActionCodecs.WriteHouseQuery(w, 1)));
        Assert.Equal(Convert.FromHexString(Envelope + "02000000" + "1F020000"), Build(w => HouseActionCodecs.WriteAbandonHouse(w, 2)));
        Assert.Equal(Convert.FromHexString(Envelope + "03000000" + "62020000"), Build(w => HouseActionCodecs.WriteTeleToHouse(w, 3)));
        Assert.Equal(Convert.FromHexString(Envelope + "04000000" + "58020000" + "104F9B7A"), Build(w => HouseActionCodecs.WriteQueryLord(w, 4, Guardian)));
    }

    [Fact]
    public void An_empty_payment_list_is_refused_before_anything_is_written()
    {
        var writer = new PacketWriter();

        Assert.Throws<ArgumentException>(() => HouseActionCodecs.WriteBuyHouse(writer, 1, Guardian, []));
        Assert.Throws<ArgumentException>(() => HouseActionCodecs.WriteRentHouse(writer, 1, Guardian, []));
        Assert.Empty(writer.ToArray());
    }

    private static byte[] Build(Action<PacketWriter> write)
    {
        var writer = new PacketWriter();
        write(writer);
        return writer.ToArray();
    }

    private sealed class Recorder : IInboundMessageHandler
    {
        public List<string> Calls { get; } = [];

        public List<string> Malformed { get; } = [];

        public void OnHouseProfile(in InboundContext context, in GameEventHeader header, HouseProfileMsg message) => Calls.Add($"profile:{message.Guardian:X8}");

        public void OnHouseData(in InboundContext context, in GameEventHeader header, HouseDataMsg message) => Calls.Add($"data:{message.Type}");

        public void OnHouseStatus(in InboundContext context, in GameEventHeader header, in HouseStatusEvent message) => Calls.Add($"status:{message.Notice}");

        public void OnHouseRestrictions(in InboundContext context, in GameEventHeader header, HouseRestrictionsMsg message) => Calls.Add($"restrictions:{message.Object:X8}");

        public void OnAvailableHouses(in InboundContext context, in GameEventHeader header, AvailableHousesMsg message) => Calls.Add($"available:{message.Type}");

        public void OnMalformed(in InboundContext context, uint opcode, int length, string reason) => Malformed.Add(reason);
    }
}
