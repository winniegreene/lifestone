namespace Lifestone.Data.Audio;

/// <summary>
/// One variant of a sound (retail <c>SoundData</c>): a Wave and the three values retail stores with it.
/// </summary>
/// <param name="WaveId">The Wave (0x0Axxxxxx); 0 plays nothing.</param>
/// <param name="Priority">
/// The variant's priority. Retail never uses it at playback: <c>SoundManager::GetSound</c> copies it into a local, and the
/// voice pool compares the priority of the cached sound buffer instead, which keeps <c>SoundData</c>'s default 0.
/// </param>
/// <param name="Probability">The chance it plays, 0 to 1 (<c>SoundManager::PlayProbability</c>).</param>
/// <param name="Volume">The volume, linear (1 is full).</param>
public readonly record struct SoundEntry(uint WaveId, float Priority, float Probability, float Volume)
{
    /// <summary>
    /// <c>SoundData::SoundData</c>'s values (11.4186 0x00551CB0, 11.6096 0x005528D0): no wave, priority 0,
    /// probability 1, volume 1.
    /// </summary>
    public static SoundEntry Default => new(0, 0f, 1f, 1f);
}

/// <summary>
/// One node of a sound table (retail <c>SoundTableData</c>): a key, its variants and its child nodes. The root's
/// children are keyed by <c>SoundType</c> (DatReaderWriter's <c>Sound</c> enum).
/// </summary>
public sealed class SoundTableNode
{
    private readonly Dictionary<uint, SoundTableNode>? _byKey;

    internal SoundTableNode(uint key, SoundEntry[] entries, SoundTableNode[] children, int duplicateKeys)
    {
        Key = key;
        Entries = entries;
        Children = children;
        DuplicateKeys = duplicateKeys;
        if (children.Length > 0)
        {
            // Retail's IntrusiveHashTable<..., SoundTableData*>::add refuses a key it already holds, so the first child wins.
            _byKey = new Dictionary<uint, SoundTableNode>(children.Length);
            foreach (var child in children)
            {
                _byKey.TryAdd(child.Key, child);
            }
        }
    }

    /// <summary>The node's key (retail <c>m_hashKey</c>): a <c>SoundType</c> for the root's children.</summary>
    public uint Key { get; }

    /// <summary>The node's variants, in dat order (retail <c>data_[num_stdatas_]</c>).</summary>
    public IReadOnlyList<SoundEntry> Entries { get; }

    /// <summary>The child nodes, in dat order (retail <c>sound_hash_</c>).</summary>
    public IReadOnlyList<SoundTableNode> Children { get; }

    /// <summary>Children whose key an earlier child already has; retail's hash table refuses them.</summary>
    public int DuplicateKeys { get; }

    /// <summary>The first child with <paramref name="key"/> (retail <c>SoundTableData::Lookup</c>), or null. Allocation-free.</summary>
    public SoundTableNode? Find(uint key) => _byKey is not null && _byKey.TryGetValue(key, out var child) ? child : null;
}

/// <summary>
/// A decoded SoundTable (0x20) record: which waves a sound type plays. Re-expresses retail <c>CSoundTable::UnPack</c>
/// (11.4186 0x00551CD0, 11.6096 0x005528F0) and <c>SoundTableData::UnPack</c> (11.4186 0x00552370, 11.6096
/// 0x00552F90), and the lookup <c>CSoundTable::Lookup</c> (11.4186 0x00552100, 11.6096 0x00552D20) →
/// <c>SoundTableData::Lookup</c> (11.4186 0x005520A0; 11.6096 unmapped, TODO(portal): 11.6096, see
/// docs/research/retail-addresses-m2.md §3: not in the pinned table; it is the target of the one-instruction forwarder
/// <c>CSoundTable::Lookup</c> at 11.6096 0x00552D20).
/// </summary>
/// <remarks>
/// <para><b>Layout.</b> After the record's id comes one node, recursively: a u32 key, a u32 entry count, that many
/// entries (u32 wave, f32 priority, f32 probability, f32 volume), a u32 child count, then the children, each a node. The
/// record ends 4-byte aligned.</para>
/// <para><b>Lookup</b> is one level deep: the root's child whose key is the sound type. The root's own entries and the
/// children's children are decoded but never looked up by retail; the end-of-retail dats have no grandchildren (the
/// census pins it). DatReaderWriter's <c>SoundTable</c> reads the same bytes as an entry dictionary and a dictionary of
/// <c>SoundData</c> whose <c>Unknown</c> is retail's grandchild count, so it agrees only while that count is 0.</para>
/// <para>Immutable once decoded; safe to share between threads.</para>
/// </remarks>
public sealed class SoundTableData
{
    /// <summary>The deepest nesting the decoder accepts (retail has no limit; the dats nest one level).</summary>
    public const int MaxDepth = 8;

    private SoundTableData(uint id, SoundTableNode root, int depth, int padding)
    {
        Id = id;
        Root = root;
        Depth = depth;
        Padding = padding;
    }

    /// <summary>The table's id (0x20xxxxxx).</summary>
    public uint Id { get; }

    /// <summary>The root node (retail <c>CSoundTable::sound_data_</c>).</summary>
    public SoundTableNode Root { get; }

    /// <summary>The nesting below the root: 0 for a root without children, 1 when the children have none.</summary>
    public int Depth { get; }

    /// <summary>Zero bytes after the root that only align the record (0 to 3).</summary>
    public int Padding { get; }

    /// <summary>
    /// The variants of <paramref name="soundType"/> (retail <c>CSoundTable::Lookup</c>), or null when the table has no
    /// such type. Allocation-free.
    /// </summary>
    public IReadOnlyList<SoundEntry>? Lookup(uint soundType) => Root.Find(soundType)?.Entries;

    /// <summary>
    /// Decodes one raw SoundTable record (the bytes of file <paramref name="id"/>, starting with the id). Every byte must
    /// be consumed; up to 3 trailing zero bytes are taken as alignment (<see cref="Padding"/>).
    /// </summary>
    /// <exception cref="InvalidDataException">The record is malformed.</exception>
    public static SoundTableData Decode(uint id, ReadOnlySpan<byte> raw)
    {
        var reader = new AudioRecordReader(raw, "SoundTable", id);
        reader.ReadId();
        var depth = 0;
        var root = ReadNode(ref reader, 0, ref depth);
        var padding = reader.RequireEnd();
        return new SoundTableData(id, root, depth, padding);
    }

    private static SoundTableNode ReadNode(ref AudioRecordReader reader, int level, ref int depth)
    {
        if (level > MaxDepth)
        {
            throw new InvalidDataException($"SoundTable 0x{reader.RecordId:X8}: nodes nest deeper than {MaxDepth} levels at offset {reader.Position}.");
        }

        depth = Math.Max(depth, level);
        var key = reader.ReadUInt32();
        var entryCount = reader.ReadUInt32();
        reader.Require(checked((int)Math.Min(entryCount * 16L, int.MaxValue)));
        var entries = new SoundEntry[entryCount];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i] = new SoundEntry(reader.ReadUInt32(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        var childCount = reader.ReadUInt32();

        // Each child is at least 12 bytes (key, entry count, child count).
        reader.Require(checked((int)Math.Min(childCount * 12L, int.MaxValue)));
        var children = new SoundTableNode[childCount];
        var seen = childCount > 1 ? new HashSet<uint>() : null;
        var duplicates = 0;
        for (var i = 0; i < children.Length; i++)
        {
            children[i] = ReadNode(ref reader, level + 1, ref depth);
            if (seen is not null && !seen.Add(children[i].Key))
            {
                duplicates++;
            }
        }

        return new SoundTableNode(key, entries, children, duplicates);
    }
}
