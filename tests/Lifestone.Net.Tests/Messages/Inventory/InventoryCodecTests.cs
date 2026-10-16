using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.Inventory;

/// <summary>
/// <see cref="InventoryCodecs"/>: the inventory, wield and public-quality messages (GM 0x0024, 0x0197, 0x02CE, 0x02DA; GE
/// 0x0022, 0x0023, 0x019A, 0x00A0) against bytes laid out from ACE's writers (<see cref="AceM3Writers"/>), each field
/// asserted; a truncated or over-long body is refused.
/// </summary>
public sealed class InventoryCodecTests
{
    private const uint Player = 0x50000001;

    [Fact]
    public void InventoryRemoveObject_reads_the_guid()
    {
        var message = AceM3Writers.InventoryRemoveObject(0x80000123);

        Assert.Equal(8, message.Length);
        Assert.True(InventoryCodecs.TryParseRemoveObject(message, out var result));
        Assert.Equal(new InventoryRemoveObjectMsg(0x80000123), result);
    }

    [Fact]
    public void SetStackSize_reads_sequence_guid_size_and_value()
    {
        var message = AceM3Writers.SetStackSize(9, 0x80000123, 12, 340);

        Assert.Equal(4 + 1 + 12, message.Length);
        Assert.True(InventoryCodecs.TryParseSetStackSize(message, out var result));
        Assert.Equal(new SetStackSizeMsg(9, 0x80000123, 12, 340), result);
    }

    [Fact]
    public void PublicUpdatePropertyInt_reads_sequence_guid_property_and_a_signed_value()
    {
        var message = AceM3Writers.PublicUpdatePropertyInt(3, 0x80000123, 0x0A, -2);

        Assert.Equal(4 + 1 + 12, message.Length);
        Assert.True(InventoryCodecs.TryParsePublicUpdatePropertyInt(message, out var result));
        Assert.Equal(new PublicUpdatePropertyIntMsg(3, 0x80000123, 0x0A, -2), result);
    }

    [Fact]
    public void PublicUpdateInstanceId_reads_sequence_guid_property_and_value()
    {
        var message = AceM3Writers.PublicUpdateInstanceId(4, 0x80000123, 0x03, Player);

        Assert.True(InventoryCodecs.TryParsePublicUpdateInstanceId(message, out var result));
        Assert.Equal(new PublicUpdateInstanceIdMsg(4, 0x80000123, 3, Player), result);
    }

    [Fact]
    public void PutObjInContainer_reads_item_container_placement_and_container_type()
    {
        var message = AceM3Writers.PutObjInContainer(Player, 5, 0x80000123, Player, 2, 0);

        Assert.Equal(16 + 16, message.Length);
        Assert.True(InventoryCodecs.TryParsePutObjInContainer(message, out var result));
        Assert.Equal(new InventoryPutObjInContainerEvent(0x80000123, Player, 2, 0), result);
    }

    [Fact]
    public void WieldObject_reads_item_and_equip_mask()
    {
        var message = AceM3Writers.WieldObject(Player, 6, 0x80000123, 0x01000000);

        Assert.True(InventoryCodecs.TryParseWieldObject(message, out var result));
        Assert.Equal(new WieldObjectEvent(0x80000123, 0x01000000), result);
    }

    [Fact]
    public void PutObjectIn3D_and_ServerSaysFailed_read_their_fields()
    {
        Assert.True(InventoryCodecs.TryParsePutObjectIn3D(AceM3Writers.PutObjectIn3D(Player, 7, 0x80000123), out var dropped));
        Assert.Equal(new InventoryPutObjectIn3DEvent(0x80000123), dropped);

        Assert.True(InventoryCodecs.TryParseServerSaysFailed(AceM3Writers.InventoryServerSaysFailed(Player, 8, 0x80000123, 0x0036), out var failed));
        Assert.Equal(new InventoryServerSaysFailedEvent(0x80000123, 0x0036), failed);
    }

    [Fact]
    public void A_truncated_or_over_long_body_is_refused()
    {
        var remove = AceM3Writers.InventoryRemoveObject(1);
        Assert.False(InventoryCodecs.TryParseRemoveObject(remove[..^1], out _));
        Assert.False(InventoryCodecs.TryParseRemoveObject([.. remove, 0], out _));

        var stack = AceM3Writers.SetStackSize(1, 1, 1, 1);
        Assert.False(InventoryCodecs.TryParseSetStackSize(stack[..^1], out _));
        Assert.False(InventoryCodecs.TryParseSetStackSize([.. stack, 0], out _));

        var update = AceM3Writers.PublicUpdatePropertyInt(1, 1, 1, 1);
        Assert.False(InventoryCodecs.TryParsePublicUpdatePropertyInt(update[..^1], out _));
        Assert.False(InventoryCodecs.TryParsePublicUpdateInstanceId(update, out _));

        var put = AceM3Writers.PutObjInContainer(Player, 1, 1, 2, 3, 4);
        Assert.False(InventoryCodecs.TryParsePutObjInContainer(put[..^1], out _));
        Assert.False(InventoryCodecs.TryParsePutObjInContainer([.. put, 0], out _));
        Assert.False(InventoryCodecs.TryParseWieldObject(put, out _));
        Assert.False(InventoryCodecs.TryParsePutObjectIn3D(AceM3Writers.PutObjectIn3D(Player, 1, 1)[..^1], out _));
        Assert.False(InventoryCodecs.TryParseServerSaysFailed(AceM3Writers.InventoryServerSaysFailed(Player, 1, 1, 1)[..^1], out _));
    }
}

/// <summary><see cref="AdminEnvironsCodec"/>: GM 0xEA60 carries the environ id and nothing else.</summary>
public sealed class AdminEnvironsCodecTests
{
    [Fact]
    public void AdminEnvirons_reads_the_environ_id_only()
    {
        Assert.True(AdminEnvironsCodec.TryParse(AceM3Writers.AdminEnvirons(0x05), out var fog));
        Assert.Equal(new AdminEnvironsMsg(5), fog);

        Assert.True(AdminEnvironsCodec.TryParse(AceM3Writers.AdminEnvirons(0x65), out var sound));
        Assert.Equal(0x65u, sound.Environ);
    }

    [Fact]
    public void AdminEnvirons_with_a_wrong_length_is_refused()
    {
        var message = AceM3Writers.AdminEnvirons(1);

        Assert.False(AdminEnvironsCodec.TryParse(message[..^1], out _));
        Assert.False(AdminEnvironsCodec.TryParse([.. message, 0], out _));
        Assert.False(AdminEnvironsCodec.TryParse(AceM3Writers.InventoryRemoveObject(1), out _));
    }
}
