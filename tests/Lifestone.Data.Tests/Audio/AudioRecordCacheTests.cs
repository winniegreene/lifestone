using Lifestone.Data.Audio;

namespace Lifestone.Data.Tests.Audio;

/// <summary>
/// The decode cache behind <see cref="DatAudioSource"/>: a record, an absence and a malformed record are each read and
/// decoded once.
/// </summary>
public sealed class AudioRecordCacheTests
{
    [Fact]
    public void A_malformed_record_is_decoded_once_and_each_request_throws_its_message()
    {
        var cache = new AudioRecordCache<WaveData>();
        var loads = 0;

        // The id alone: the decoder finds no header size.
        WaveData? Load(uint id, byte[] raw)
        {
            loads++;
            return WaveData.Decode(id, raw);
        }

        var raw = BitConverter.GetBytes(0x0A000001u);
        var first = Assert.Throws<InvalidDataException>(() => cache.Get(0x0A000001, (id, r) => Load(id, r), raw));
        var second = Assert.Throws<InvalidDataException>(() => cache.Get(0x0A000001, (id, r) => Load(id, r), raw));

        Assert.Equal(1, loads);
        Assert.Equal(first.Message, second.Message);
        Assert.Same(first.InnerException, second.InnerException);
        Assert.IsType<InvalidDataException>(first.InnerException);
        Assert.Equal(first.InnerException!.Message, first.Message);
    }

    [Fact]
    public void A_record_and_an_absence_are_loaded_once()
    {
        var cache = new AudioRecordCache<string>();
        var loads = 0;

        string? Load(uint id)
        {
            loads++;
            return id == 1 ? "one" : null;
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal("one", cache.Get(1, (id, _) => Load(id), 0));
            Assert.Null(cache.Get(2, (id, _) => Load(id), 0));
        }

        Assert.Equal(2, loads);
    }

    [Fact]
    public void Only_invalid_data_is_cached_other_failures_propagate_each_time()
    {
        var cache = new AudioRecordCache<string>();
        var loads = 0;

        string? Load()
        {
            loads++;
            throw new IOException("the dat could not be read");
        }

        Assert.Throws<IOException>(() => cache.Get(1, (_, _) => Load(), 0));
        Assert.Throws<IOException>(() => cache.Get(1, (_, _) => Load(), 0));
        Assert.Equal(2, loads);
    }
}
