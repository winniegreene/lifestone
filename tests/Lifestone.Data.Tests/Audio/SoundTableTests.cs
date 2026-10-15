using System.Buffers.Binary;
using DatReaderWriter.Enums;
using Lifestone.Data.Audio;
using Lifestone.Data.Dat;
using DatSoundTable = DatReaderWriter.DBObjs.SoundTable;

namespace Lifestone.Data.Tests.Audio;

/// <summary>
/// Step PS9: every SoundTable (0x20) of the user's portal decodes exactly, with retail's layout (a node of entries and
/// children, recursively), and the UI sound table holds the portal whooshes (m2-log-in.md §4.2).
/// </summary>
public sealed class SoundTableTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>The UIASSET enum of the UI sound table (<c>UISoundTable</c>; the Client's <c>UiAssetIds.UiSoundTable</c>).</summary>
    private const uint UiSoundTableEnum = 0x10000003;

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_table_decodes_exactly_and_none_has_grandchildren()
    {
        var source = new DatAudioSource(fixture.Dats);
        var ids = source.AllSoundTableIds();
        Assert.Equal(190, ids.Count);

        var children = 0;
        var entries = 0;
        var multiVariant = new List<(uint Table, uint Type, int Count)>();
        foreach (var id in ids)
        {
            var table = source.GetSoundTable(id)!; // throws unless every byte is consumed
            Assert.Equal(id, table.Id);
            Assert.Equal(1, table.Depth); // children, and no grandchildren
            Assert.Equal(0, table.Padding);
            Assert.Equal(0, table.Root.DuplicateKeys);

            // The root is SoundTableData's constructor default, packed: key 0 and one default entry.
            Assert.Equal(0u, table.Root.Key);
            Assert.Equal([SoundEntry.Default], table.Root.Entries);

            foreach (var child in table.Root.Children)
            {
                Assert.Empty(child.Children);
                Assert.Same(child, table.Root.Find(child.Key));
                Assert.Same(child.Entries, table.Lookup(child.Key));
                children++;
                entries += child.Entries.Count;
                if (child.Entries.Count != 1)
                {
                    multiVariant.Add((id, child.Key, child.Entries.Count));
                }
            }
        }

        Assert.Equal(4184, children);
        Assert.Equal(4185, entries);

        // One sound in the whole portal has two variants (Swoosh2 of 0x200000A8); retail never picks its second.
        Assert.Equal([(0x200000A8u, (uint)Sound.Swoosh2, 2)], multiVariant);
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_table_matches_DatReaderWriter_entry_for_entry()
    {
        var dats = fixture.Dats;
        var source = new DatAudioSource(dats);
        foreach (var id in source.AllSoundTableIds())
        {
            var table = source.GetSoundTable(id)!;
            Assert.True(dats.TryGetPortal<DatSoundTable>(id, out var drw), $"DatReaderWriter cannot read 0x{id:X8}");

            // DatReaderWriter calls the root's key HashKey and its entries Hashes (keyed by wave id) ...
            Assert.Equal(table.Root.Key, (uint)drw.HashKey);
            Assert.Equal(table.Root.Entries.Count, drw.Hashes.Count);
            foreach (var entry in table.Root.Entries)
            {
                var theirs = drw.Hashes[entry.WaveId];
                Assert.Equal((entry.Priority, entry.Probability, entry.Volume), (theirs.Priority, theirs.Probability, theirs.Volume));
            }

            // ... and the children Sounds, whose Unknown is retail's grandchild count.
            Assert.Equal(table.Root.Children.Count, drw.Sounds.Count);
            foreach (var child in table.Root.Children)
            {
                var theirs = drw.Sounds[(Sound)child.Key];
                Assert.Equal(child.Children.Count, theirs.Unknown);
                Assert.Equal(
                    child.Entries,
                    theirs.Entries.Select(e => new SoundEntry(e.Id.DataId, e.Priority, e.Probability, e.Volume)));
            }
        }
    }

    [RequiresDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_wave_a_table_names_exists()
    {
        var source = new DatAudioSource(fixture.Dats);
        var named = new HashSet<uint>();
        foreach (var id in source.AllSoundTableIds())
        {
            foreach (var child in source.GetSoundTable(id)!.Root.Children)
            {
                foreach (var entry in child.Entries)
                {
                    Assert.NotEqual(0u, entry.WaveId);
                    Assert.True(source.GetWave(entry.WaveId) is not null, $"table 0x{id:X8} type 0x{child.Key:X2} names missing wave 0x{entry.WaveId:X8}");
                    named.Add(entry.WaveId);
                }
            }
        }

        Assert.Equal(701, named.Count);
    }

    [RequiresDatsFact]
    public void The_ui_sound_table_plays_the_portal_whooshes()
    {
        var dats = fixture.Dats;
        var tableId = dats.EnumMaps.Resolve(EnumGroup.UiAsset, UiSoundTableEnum);
        Assert.Equal(0x2000004Bu, tableId);

        var source = new DatAudioSource(dats);
        var table = source.GetSoundTable(tableId)!;

        Assert.Equal([new SoundEntry(0x0A000246, 0.9f, 1f, 1f)], table.Lookup((uint)Sound.UI_EnterPortal));
        Assert.Equal([new SoundEntry(0x0A000245, 0.9f, 1f, 1f)], table.Lookup((uint)Sound.UI_ExitPortal));
        Assert.Null(table.Lookup((uint)Sound.Swoosh2));
        Assert.Null(source.GetSoundTable(0x0A000316)); // a Wave id
        Assert.Null(source.GetSoundTable(0x2000FFFE)); // no such table
    }

    [Fact]
    public void Nested_nodes_decode_recursively_and_lookup_is_one_level()
    {
        var raw = Table(
            0x20000001,
            Node(0, [], Node(0x6A, [Entry(0x0A000001, 0.5f, 1f, 0.3f)]), Node(0x6B, [], Node(0x6A, [Entry(0x0A000009, 1f, 1f, 1f)]))));

        var table = SoundTableData.Decode(0x20000001, raw);

        Assert.Equal(2, table.Depth);
        Assert.Equal([new SoundEntry(0x0A000001, 0.5f, 1f, 0.3f)], table.Lookup(0x6A));
        Assert.Empty(table.Lookup(0x6B)!);
        Assert.Equal(0x0A000009u, table.Root.Find(0x6B)!.Find(0x6A)!.Entries[0].WaveId); // a grandchild, never looked up
        Assert.Null(table.Lookup(0x6C));
    }

    [Fact]
    public void A_duplicate_key_is_counted_and_the_first_child_wins()
    {
        var raw = Table(0x20000001, Node(0, [], Node(0x6A, [Entry(0x0A000001, 0f, 1f, 1f)]), Node(0x6A, [Entry(0x0A000002, 0f, 1f, 1f)])));

        var table = SoundTableData.Decode(0x20000001, raw);

        Assert.Equal(1, table.Root.DuplicateKeys);
        Assert.Equal(0x0A000001u, table.Lookup(0x6A)![0].WaveId);
        Assert.Equal(2, table.Root.Children.Count);
    }

    [Fact]
    public void Malformed_tables_throw()
    {
        var good = Table(0x20000001, Node(0, [Entry(0, 0f, 1f, 1f)], Node(0x6A, [Entry(0x0A000001, 0f, 1f, 1f)])));
        Assert.NotNull(SoundTableData.Decode(0x20000001, good));

        Assert.Throws<InvalidDataException>(() => SoundTableData.Decode(0x20000002, good)); // wrong id
        Assert.Throws<InvalidDataException>(() => SoundTableData.Decode(0x20000001, good.AsSpan(0, good.Length - 4))); // truncated
        Assert.Throws<InvalidDataException>(() => SoundTableData.Decode(0x20000001, [.. good, 0, 0, 0, 0])); // trailing data

        var huge = (byte[])good.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(huge.AsSpan(8), 0x10000000); // an entry count far past the end
        Assert.Throws<InvalidDataException>(() => SoundTableData.Decode(0x20000001, huge));

        // Nodes nested deeper than the decoder allows.
        var deep = Node(1, []);
        for (var i = 0; i <= SoundTableData.MaxDepth; i++)
        {
            deep = Node(1, [], deep);
        }

        Assert.Throws<InvalidDataException>(() => SoundTableData.Decode(0x20000001, Table(0x20000001, deep)));
    }

    private static byte[] Entry(uint wave, float priority, float probability, float volume)
    {
        var bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, wave);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4), priority);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(8), probability);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(12), volume);
        return bytes;
    }

    private static byte[] Node(uint key, byte[][] entries, params byte[][] children)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BitConverter.GetBytes(key));
        bytes.AddRange(BitConverter.GetBytes((uint)entries.Length));
        foreach (var entry in entries)
        {
            bytes.AddRange(entry);
        }

        bytes.AddRange(BitConverter.GetBytes((uint)children.Length));
        foreach (var child in children)
        {
            bytes.AddRange(child);
        }

        return [.. bytes];
    }

    private static byte[] Table(uint id, byte[] root) => [.. BitConverter.GetBytes(id), .. root];
}
