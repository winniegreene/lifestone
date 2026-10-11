using System.Numerics;
using DatMotionTable = DatReaderWriter.DBObjs.MotionTable;

namespace Lifestone.Data.Animation;

/// <summary>
/// One animation reference of a <see cref="MotionData"/> (retail <c>AnimData</c>): play frames
/// <see cref="LowFrame"/>..<see cref="HighFrame"/> (−1 = last) of Animation <see cref="AnimId"/> at
/// <see cref="Framerate"/> frames per second; a negative rate plays in reverse.
/// </summary>
public readonly record struct AnimDataRef(uint AnimId, int LowFrame, int HighFrame, float Framerate)
{
    /// <summary>Retail <c>operator*(float, AnimData)</c>: scales the framerate by a speed modifier.</summary>
    public AnimDataRef WithSpeed(float speed) => this with { Framerate = Framerate * speed };
}

/// <summary>
/// Retail <c>MotionData</c>: a list of animations plus the object velocity/omega while the motion runs.
/// <see cref="Bitfield"/> bit 0 clears modifiers when entered, bit 1 restricts the motion to the style's default
/// substate (<c>CMotionTable::is_allowed</c>).
/// </summary>
public sealed class MotionData(byte bitfield, byte flags, IReadOnlyList<AnimDataRef> anims, Vector3 velocity, Vector3 omega)
{
    public byte Bitfield { get; } = bitfield;

    /// <summary>1 = has velocity, 2 = has omega (only affects packing; absent vectors are zero).</summary>
    public byte Flags { get; } = flags;

    public IReadOnlyList<AnimDataRef> Anims { get; } = anims;

    public Vector3 Velocity { get; } = velocity;

    public Vector3 Omega { get; } = omega;
}

/// <summary>
/// An engine-agnostic MotionTable (0x09): <c>DefaultStyle</c>, <c>StyleDefaults[style] = substate</c>,
/// <c>Cycles[(style &lt;&lt; 16) | (motion &amp; 0xFFFFFF)]</c>, <c>Modifiers[...]</c> and
/// <c>Links[(style &lt;&lt; 16) | (from &amp; 0xFFFFFF)][to]</c> (docs/research/rendering.md §7).
/// Decoded through DatReaderWriter (its MotionTable layout matches ACE <c>ACE.DatLoader/FileTypes/MotionTable.cs</c>);
/// command ids are kept as raw end-of-retail (11.6096) numbers, see <see cref="MotionCommands"/>.
/// </summary>
public sealed class MotionTableData
{
    private MotionTableData(
        uint id,
        uint defaultStyle,
        IReadOnlyDictionary<uint, uint> styleDefaults,
        IReadOnlyDictionary<uint, MotionData> cycles,
        IReadOnlyDictionary<uint, MotionData> modifiers,
        IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, MotionData>> links)
    {
        Id = id;
        DefaultStyle = defaultStyle;
        StyleDefaults = styleDefaults;
        Cycles = cycles;
        Modifiers = modifiers;
        Links = links;
    }

    public uint Id { get; }

    public uint DefaultStyle { get; }

    public IReadOnlyDictionary<uint, uint> StyleDefaults { get; }

    public IReadOnlyDictionary<uint, MotionData> Cycles { get; }

    public IReadOnlyDictionary<uint, MotionData> Modifiers { get; }

    public IReadOnlyDictionary<uint, IReadOnlyDictionary<uint, MotionData>> Links { get; }

    /// <summary>Hash key used by <see cref="Cycles"/>, <see cref="Modifiers"/> and the outer <see cref="Links"/> table.</summary>
    public static uint Key(uint style, uint motion) => (style << 16) | (motion & 0xFFFFFF);

    /// <summary>Converts a DatReaderWriter MotionTable. The record must be read through <c>DatSet</c>.</summary>
    public static MotionTableData FromDat(DatMotionTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var styleDefaults = table.StyleDefaults.ToDictionary(kv => (uint)kv.Key, kv => (uint)kv.Value);
        var cycles = table.Cycles.ToDictionary(kv => (uint)kv.Key, kv => Convert(kv.Value));
        var modifiers = table.Modifiers.ToDictionary(kv => (uint)kv.Key, kv => Convert(kv.Value));
        var links = table.Links.ToDictionary(
            kv => (uint)kv.Key,
            kv => (IReadOnlyDictionary<uint, MotionData>)kv.Value.MotionData.ToDictionary(m => (uint)m.Key, m => Convert(m.Value)));

        return new MotionTableData(table.Id, (uint)table.DefaultStyle, styleDefaults, cycles, modifiers, links);
    }

    private static MotionData Convert(DatReaderWriter.Types.MotionData data) =>
        new(
            data.Bitfield,
            (byte)data.Flags,
            data.Anims.Select(a => new AnimDataRef(a.AnimId.DataId, a.LowFrame, a.HighFrame, a.Framerate)).ToArray(),
            data.Velocity,
            data.Omega);
}
