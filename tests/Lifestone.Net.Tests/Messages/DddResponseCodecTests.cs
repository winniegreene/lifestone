using Lifestone.Net.Messages;
using Lifestone.Net.Packets;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.Support;

namespace Lifestone.Net.Tests.Messages;

/// <summary>
/// F7E6 DDD_InterrogationResponse (m2-plan §3.3 N3): the exact bytes for the end-of-retail dats, the high-res list only
/// when the server allows it and the client has the dat, the run-length form of retail's
/// <c>CMostlyConsecutiveIntSet::Serialize</c>, and ACE's reader (<c>ReadCAllIterationList</c>) getting every count back.
/// </summary>
public sealed class DddResponseCodecTests
{
    private static readonly DddIterations EndOfRetail = new(2072, 982, 994, 497);

    [Fact]
    public void End_of_retail_iterations_without_high_res_have_these_exact_bytes()
    {
        var bytes = Write(EndOfRetail, serverAllowsHighRes: false);

        uint[] expected =
        [
            0xF7E6, 1, 3,
            0, 1, 2072, unchecked((uint)-2072), 1,
            1, 2, 982, unchecked((uint)-982), 1,
            1, 3, 994, unchecked((uint)-994), 1,
            0, 0,
        ];
        Assert.Equal(Words(expected), bytes);
    }

    [Fact]
    public void Product_id_one_never_reports_high_res_even_when_the_client_has_it()
    {
        var response = AceClientReaders.ReadDddInterrogationResponse(Write(EndOfRetail, serverAllowsHighRes: false));
        Assert.Equal(3, response.Lists.Count);
        Assert.DoesNotContain(response.Lists, l => l.DatType == DddCodec.HighResDatType);
    }

    [Fact]
    public void Product_id_five_reports_high_res_497_as_HiFi_one()
    {
        var bytes = Write(EndOfRetail, serverAllowsHighRes: true);
        var response = AceClientReaders.ReadDddInterrogationResponse(bytes);

        Assert.Equal(4, response.Lists.Count);
        var highRes = response.Lists[3];
        Assert.Equal((0x69466948, 1, 497), (highRes.DatType, highRes.DatId, highRes.Iterations));
        Assert.Equal("HiFi"u8.ToArray(), BitConverter.GetBytes(highRes.DatType));
        Assert.Equal(new[] { -497, 1 }, highRes.Ints);
    }

    [Fact]
    public void A_server_that_allows_high_res_gets_three_lists_from_a_client_without_the_dat()
    {
        var response = AceClientReaders.ReadDddInterrogationResponse(Write(EndOfRetail with { HighRes = null }, serverAllowsHighRes: true));
        Assert.Equal(3, response.Lists.Count);
    }

    [Fact]
    public void Ace_reads_every_count_back_and_leaves_only_the_two_trailing_words()
    {
        var response = AceClientReaders.ReadDddInterrogationResponse(Write(EndOfRetail, serverAllowsHighRes: true));

        Assert.Equal(DddCodec.ClientLanguageEnglish, response.ClientLanguage);
        Assert.Equal(
            new[] { (0, 1, 2072), (1, 2, 982), (1, 3, 994), (DddCodec.HighResDatType, 1, 497) },
            response.Lists.Select(l => (l.DatType, l.DatId, l.Iterations)));
        Assert.Equal(8, response.Unread);
    }

    [Theory]
    [InlineData(0, new int[0])]
    [InlineData(1, new[] { 1 })]
    [InlineData(2, new[] { 1, 2 })]
    [InlineData(3, new[] { -3, 1 })]
    [InlineData(100_000, new[] { -100_000, 1 })]
    public void Iterations_one_to_n_are_written_as_retail_packs_a_consecutive_set(int count, int[] ints)
    {
        var response = AceClientReaders.ReadDddInterrogationResponse(Write(new DddIterations(5, count, 7, null), serverAllowsHighRes: false));
        var cell = response.Lists[1];
        Assert.Equal(count, cell.Iterations);
        Assert.Equal(ints, cell.Ints);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 0)]
    [InlineData(1, -1, 1)]
    [InlineData(100_001, 1, 1)]
    public void Unusable_iterations_throw(int portal, int cell, int language)
    {
        var writer = new PacketWriter();
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() =>
            DddCodec.WriteInterrogationResponse(writer, new DddIterations(portal, cell, language, null), serverAllowsHighRes: false));
    }

    private static byte[] Write(DddIterations iterations, bool serverAllowsHighRes)
    {
        var writer = new PacketWriter();
        DddCodec.WriteInterrogationResponse(writer, iterations, serverAllowsHighRes);
        return writer.ToArray();
    }

    private static byte[] Words(uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var i = 0; i < words.Length; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), words[i]);
        }

        return bytes;
    }
}
