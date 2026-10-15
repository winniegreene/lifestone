using System.Buffers.Binary;
using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// An independent structural reader of a LayoutDesc's raw bytes, re-expressed from retail's serializers rather than from
/// DatReaderWriter, so the two can be compared (UiLayoutCensusTests): if DatReaderWriter misread a field, the element
/// ids, flags, state ids, property ids or media types after it would differ.
/// </summary>
/// <remarks>
/// <para>
/// Field order, by function (11.4186 decompile; no code copied): <c>LayoutDesc::Serialize</c> (0x0069A490: DBObj id,
/// display width and height, the element hash table), <c>ElementDesc::Serialize</c> (0x0069BBA0: its StateDesc, read
/// order, element id, type, base element, base layout, default state, X/Y/W/H/Z each only when its incorporation flag is
/// set, the four edges, the states table, the children table), <c>StateDesc::Serialize</c> (0x0069C5F0, the versioned
/// path: state id, pass-to-children byte, incorporation flags, properties, media), <c>PropertyCollection::Serialize</c>
/// and <c>BaseProperty::Serialize</c> (0x00681420, 0x0042A090: a property id, then the value its type's
/// <c>*PropertyValue::Serialize</c> writes), the media container (0x0069C470: a count, then per entry the type and the
/// <c>MD_Data_*::Serialize</c> payload, which starts with <c>MediaDesc::Serialize</c>'s own type field) and
/// <c>StringInfo::Serialize</c> (0x0042DEB0). Hash tables are a bucket-size byte, a compressed count and the entries.
/// </para>
/// <para>
/// Property value types come from the master property table (a portal record, read separately). The walker throws on
/// any shape the retail layouts do not hold (a literal StringInfo, strings or variables, an undecodable media or
/// property type), so a pass also proves those shapes absent.
/// </para>
/// </remarks>
internal sealed class RawLayoutWalker
{
    private readonly byte[] _bytes;
    private readonly IReadOnlyDictionary<uint, UiPropertyType> _types;
    private int _offset;

    private RawLayoutWalker(byte[] bytes, IReadOnlyDictionary<uint, UiPropertyType> types)
    {
        _bytes = bytes;
        _types = types;
    }

    /// <summary>Walks one layout's bytes; throws unless they are consumed exactly.</summary>
    public static RawLayout Walk(byte[] bytes, IReadOnlyDictionary<uint, UiPropertyType> propertyTypes)
    {
        var walker = new RawLayoutWalker(bytes, propertyTypes);
        var layout = walker.ReadLayout();
        if (walker._offset != bytes.Length)
        {
            throw new InvalidDataException($"layout 0x{layout.Id:X8}: the walker read {walker._offset} of {bytes.Length} bytes");
        }

        return layout;
    }

    private RawLayout ReadLayout()
    {
        var id = U32();
        var width = U32();
        var height = U32();
        var elements = Table(ReadElement);
        return new RawLayout(id, width, height, elements);
    }

    private RawElement ReadElement()
    {
        var state = ReadState();
        var readOrder = U32();
        var elementId = U32();
        var type = U32();
        var baseElement = U32();
        var baseLayout = U32();
        var defaultState = U32();
        var position = new uint?[5];
        for (var bit = 0; bit < 5; bit++)
        {
            // X 0x2, Y 0x4, Width 0x8, Height 0x10, ZLevel 0x20.
            position[bit] = (state.Flags & (2u << bit)) != 0 ? U32() : null;
        }

        var edges = new[] { U32(), U32(), U32(), U32() };
        var states = Table(ReadState);
        var children = Table(ReadElement);
        return new RawElement(elementId, type, baseElement, baseLayout, defaultState, readOrder, position, edges, state, states, children);
    }

    private RawState ReadState()
    {
        var stateId = U32();
        var pass = Byte();
        var flags = U32();
        var properties = Table(ReadProperty);
        var mediaCount = CompressedCount();
        var media = new List<uint>(mediaCount);
        for (var i = 0; i < mediaCount; i++)
        {
            media.Add(ReadMedia());
        }

        return new RawState(stateId, pass, flags, properties, media);
    }

    private RawProperty ReadProperty()
    {
        var id = U32();
        if (!_types.TryGetValue(id, out var type))
        {
            throw new InvalidDataException($"property 0x{id:X} is not in the master property table");
        }

        var strings = new List<(uint StringId, uint TableId)>();
        var children = new List<RawProperty>();
        switch (type)
        {
            case UiPropertyType.Bool:
                Skip(1);
                break;
            case UiPropertyType.Integer:
            case UiPropertyType.Float:
            case UiPropertyType.Color: // RGBAColor::Serialize: four bytes
            case UiPropertyType.Enum:
            case UiPropertyType.DataId:
            case UiPropertyType.InstanceId:
            case UiPropertyType.Bitfield32:
                Skip(4);
                break;
            case UiPropertyType.Bitfield64:
                Skip(8);
                break;
            case UiPropertyType.Vector:
                Skip(12);
                break;
            case UiPropertyType.StringInfo:
                strings.Add(ReadStringInfo(id));
                break;
            case UiPropertyType.Array: // SmartArray<BaseProperty>::Serialize: a 32-bit count
                var count = U32();
                for (var i = 0; i < count; i++)
                {
                    children.Add(ReadProperty());
                }

                break;
            case UiPropertyType.Struct: // StructPropertyValue::Serialize: a hash table
                children.AddRange(Table(ReadProperty));
                break;
            default:
                throw new InvalidDataException($"property 0x{id:X} has type {type}, which the walker does not read");
        }

        return new RawProperty(id, type, strings, children);
    }

    private (uint StringId, uint TableId) ReadStringInfo(uint propertyId)
    {
        var overrideByte = Byte();
        if (overrideByte == 1)
        {
            throw new InvalidDataException($"property 0x{propertyId:X}: a literal StringInfo");
        }

        var stringId = U32();
        var tableId = U32();
        var hasStrings = Byte();
        if (hasStrings != 0)
        {
            throw new InvalidDataException($"property 0x{propertyId:X}: a StringInfo with strings");
        }

        Skip(1); // the variables table's bucket-size byte
        if (CompressedCount() != 0)
        {
            throw new InvalidDataException($"property 0x{propertyId:X}: a StringInfo with variables");
        }

        return (stringId, tableId);
    }

    private uint ReadMedia()
    {
        var type = U32();
        var ownType = U32(); // MediaDesc::Serialize
        if (ownType != type)
        {
            throw new InvalidDataException($"media type {type} has its own type field {ownType}");
        }

        switch (type)
        {
            case 2: // Alpha: file
                Skip(4);
                break;
            case 5: // Image: file, draw mode
            case 6: // Jump: item index, probability
            case 7: // Message: message id, probability
            case 8: // Pause: min, max
            case 9: // Sound: file, sound type
            case 10: // State: state id, probability
                Skip(8);
                break;
            case 4: // Cursor: file, hot spot x, y
            case 11: // Fade: start alpha, end alpha, duration
                Skip(12);
                break;
            case 3: // Animation: duration, draw mode, a 32-bit frame count, the frames
                Skip(8);
                Skip(checked((int)U32() * 4));
                break;
            case 1: // Movie: a byte string, the stretch byte
                Skip(CompressedCount());
                Skip(1);
                break;
            default:
                throw new InvalidDataException($"media type {type}, which MediaDesc::CreateMediaType does not create");
        }

        return type;
    }

    private List<T> Table<T>(Func<T> read)
    {
        Skip(1); // bucket-size index
        var count = CompressedCount();
        var items = new List<T>(count);
        for (var i = 0; i < count; i++)
        {
            Skip(4); // the key; the value repeats it, which UiLayoutCensusTests compares through the converter
            items.Add(read());
        }

        return items;
    }

    /// <summary><c>SB_As32Bit_Compressed</c>: one byte below 0x80, else two bytes with the top bit set.</summary>
    private int CompressedCount()
    {
        var b0 = Byte();
        if ((b0 & 0x80) == 0)
        {
            return b0;
        }

        if ((b0 & 0x40) != 0)
        {
            throw new InvalidDataException("a four-byte compressed count, which no layout table needs");
        }

        return ((b0 & 0x7F) << 8) | Byte();
    }

    private byte Byte() => _bytes[_offset++];

    private uint U32()
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(_bytes.AsSpan(_offset, 4));
        _offset += 4;
        return value;
    }

    private void Skip(int count)
    {
        if (count < 0 || _offset + count > _bytes.Length)
        {
            throw new InvalidDataException($"a field of {count} bytes runs past the end at {_offset}");
        }

        _offset += count;
    }
}

internal sealed record RawLayout(uint Id, uint Width, uint Height, List<RawElement> Elements);

internal sealed record RawElement(
    uint ElementId,
    uint Type,
    uint BaseElement,
    uint BaseLayout,
    uint DefaultState,
    uint ReadOrder,
    uint?[] Position,
    uint[] Edges,
    RawState State,
    List<RawState> States,
    List<RawElement> Children);

internal sealed record RawState(uint StateId, byte PassToChildren, uint Flags, List<RawProperty> Properties, List<uint> Media);

internal sealed record RawProperty(uint Id, UiPropertyType Type, List<(uint StringId, uint TableId)> Strings, List<RawProperty> Children);
