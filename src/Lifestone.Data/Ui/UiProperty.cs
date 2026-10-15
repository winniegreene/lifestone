using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace Lifestone.Data.Ui;

/// <summary>
/// The value type of a UI property: retail's property type numbers, as <c>MasterProperty::AllocatePropertyValue</c>
/// (11.4186 0x00429120, 11.6096 0x004293C0) switches on them and as the master property table (0x39000001) records them
/// for every property id (<see cref="MasterPropertyTable"/>).
/// </summary>
/// <remarks>
/// The retail layouts use only Bool, Integer, Float, Color, StringInfo, Enum, DataId, Array and Struct
/// (UiLayoutCensusTests). The master table types a few others that DatReaderWriter can decode (Vector, InstanceId,
/// Bitfield32, Bitfield64), which <see cref="UiProperty"/> models too. The remaining types (LongInteger, String, Waveform,
/// Position, TimeStamp, StringToken, PropertyName, TriState) have no decoder; a layout that held one would fail to decode.
/// </remarks>
public enum UiPropertyType : uint
{
    /// <summary>0: no type.</summary>
    Invalid = 0,

    /// <summary>1: one byte, 0 or 1.</summary>
    Bool = 1,

    /// <summary>2: a 32-bit signed integer.</summary>
    Integer = 2,

    /// <summary>3: a 64-bit signed integer (no decoder).</summary>
    LongInteger = 3,

    /// <summary>4: a 32-bit float.</summary>
    Float = 4,

    /// <summary>5: three 32-bit floats.</summary>
    Vector = 5,

    /// <summary>6: a colour, stored as a 32-bit ARGB value.</summary>
    Color = 6,

    /// <summary>7: a byte string (no decoder).</summary>
    String = 7,

    /// <summary>8: a localised string reference (<see cref="UiStringRef"/>).</summary>
    StringInfo = 8,

    /// <summary>9: a 32-bit enum value (element ids, UI state ids, input actions, ...).</summary>
    Enum = 9,

    /// <summary>10: a 32-bit data id (DID).</summary>
    DataId = 10,

    /// <summary>11: a waveform (no decoder).</summary>
    Waveform = 11,

    /// <summary>12: a 32-bit object instance id.</summary>
    InstanceId = 12,

    /// <summary>13: a position (no decoder).</summary>
    Position = 13,

    /// <summary>14: a time stamp (no decoder).</summary>
    TimeStamp = 14,

    /// <summary>15: 32 bits of flags.</summary>
    Bitfield32 = 15,

    /// <summary>16: 64 bits of flags.</summary>
    Bitfield64 = 16,

    /// <summary>17: a list of properties.</summary>
    Array = 17,

    /// <summary>18: a set of properties keyed by property id.</summary>
    Struct = 18,

    /// <summary>19: a string token (no decoder).</summary>
    StringToken = 19,

    /// <summary>20: a property name (no decoder).</summary>
    PropertyName = 20,

    /// <summary>21: a tri-state value (no decoder).</summary>
    TriState = 21,
}

/// <summary>
/// One typed UI property of a StateDesc: its id (a key of the master property table, <see cref="UiPropertyIds"/>) and
/// its value. Retail's <c>BaseProperty</c>, with the value types the dats can hold as sealed records.
/// </summary>
/// <param name="Id">The property id (retail's property name enum).</param>
public abstract record UiProperty(uint Id)
{
    /// <summary>The value's type.</summary>
    public abstract UiPropertyType Type { get; }
}

/// <summary>A Bool property.</summary>
public sealed record UiBoolProperty(uint Id, bool Value) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.Bool;
}

/// <summary>An Integer property.</summary>
public sealed record UiIntegerProperty(uint Id, int Value) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.Integer;
}

/// <summary>A Float property.</summary>
public sealed record UiFloatProperty(uint Id, float Value) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.Float;
}

/// <summary>A Vector property (three floats, AC axes).</summary>
public sealed record UiVectorProperty(uint Id, Vector3 Value) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.Vector;
}

/// <summary>A Color property, as a 32-bit ARGB value (0xAARRGGBB; the dat stores the bytes B, G, R, A).</summary>
public sealed record UiColorProperty(uint Id, uint Argb) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.Color;
}

/// <summary>A StringInfo property: a localised string reference.</summary>
public sealed record UiStringInfoProperty(uint Id, UiStringRef Value) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.StringInfo;
}

/// <summary>An Enum property (element ids, UI state ids, input actions, justifications, ...).</summary>
public sealed record UiEnumProperty(uint Id, uint Value) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.Enum;
}

/// <summary>A DataId property (a DID: a layout, an image, a font, ...).</summary>
public sealed record UiDataIdProperty(uint Id, uint Value) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.DataId;
}

/// <summary>An InstanceId property (an object instance id).</summary>
public sealed record UiInstanceIdProperty(uint Id, uint Value) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.InstanceId;
}

/// <summary>A Bitfield32 property.</summary>
public sealed record UiBitfield32Property(uint Id, uint Value) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.Bitfield32;
}

/// <summary>A Bitfield64 property.</summary>
public sealed record UiBitfield64Property(uint Id, ulong Value) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.Bitfield64;
}

/// <summary>
/// An Array property: a list of properties in dat order, each with its own id (for example the font array
/// <see cref="UiPropertyIds.TextFonts"/> holds <see cref="UiPropertyIds.TextFont"/> entries).
/// </summary>
public sealed record UiArrayProperty(uint Id, IReadOnlyList<UiProperty> Items) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.Array;
}

/// <summary>A Struct property: properties keyed by property id, in dat order.</summary>
public sealed record UiStructProperty(uint Id, IReadOnlyDictionary<uint, UiProperty> Members) : UiProperty(Id)
{
    /// <inheritdoc />
    public override UiPropertyType Type => UiPropertyType.Struct;
}

/// <summary>Typed reads of a property collection (a StateDesc's properties, a Struct's members).</summary>
/// <remarks>
/// A property that is present with another type than the caller expects is a data or code error, not an absent value,
/// so the typed reads throw for it (loud, naming the id and both types) instead of returning false.
/// </remarks>
public static class UiPropertyLookup
{
    /// <summary>Reads property <paramref name="id"/> as <typeparamref name="T"/>; false when it is absent.</summary>
    /// <exception cref="InvalidDataException">The property is present with another type.</exception>
    public static bool TryGet<T>(this IReadOnlyDictionary<uint, UiProperty> properties, uint id, [NotNullWhen(true)] out T? property)
        where T : UiProperty
    {
        ArgumentNullException.ThrowIfNull(properties);
        if (!properties.TryGetValue(id, out var found))
        {
            property = null;
            return false;
        }

        property = found as T ?? throw new InvalidDataException(
            $"UI property 0x{id:X} is a {found.Type} property, not the {typeof(T).Name} the caller expects.");
        return true;
    }

    /// <summary>Property <paramref name="id"/> as a bool, or <paramref name="fallback"/> when absent.</summary>
    public static bool GetBool(this IReadOnlyDictionary<uint, UiProperty> properties, uint id, bool fallback = false) =>
        properties.TryGet<UiBoolProperty>(id, out var p) ? p.Value : fallback;

    /// <summary>Property <paramref name="id"/> as an integer, or <paramref name="fallback"/> when absent.</summary>
    public static int GetInteger(this IReadOnlyDictionary<uint, UiProperty> properties, uint id, int fallback = 0) =>
        properties.TryGet<UiIntegerProperty>(id, out var p) ? p.Value : fallback;

    /// <summary>Property <paramref name="id"/> as a float, or <paramref name="fallback"/> when absent.</summary>
    public static float GetFloat(this IReadOnlyDictionary<uint, UiProperty> properties, uint id, float fallback = 0f) =>
        properties.TryGet<UiFloatProperty>(id, out var p) ? p.Value : fallback;

    /// <summary>Property <paramref name="id"/> as an enum value, or <paramref name="fallback"/> when absent.</summary>
    public static uint GetEnum(this IReadOnlyDictionary<uint, UiProperty> properties, uint id, uint fallback = 0) =>
        properties.TryGet<UiEnumProperty>(id, out var p) ? p.Value : fallback;

    /// <summary>Property <paramref name="id"/> as a DID, or <paramref name="fallback"/> when absent.</summary>
    public static uint GetDataId(this IReadOnlyDictionary<uint, UiProperty> properties, uint id, uint fallback = 0) =>
        properties.TryGet<UiDataIdProperty>(id, out var p) ? p.Value : fallback;
}
