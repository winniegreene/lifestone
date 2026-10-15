using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;

namespace Lifestone.Data.Ui;

/// <summary>
/// Turns DatReaderWriter's decoded LayoutDesc into the immutable Lifestone desc model (<see cref="UiLayout"/>), and
/// checks, loudly, the places where DatReaderWriter's reading of the bytes can silently go wrong.
/// </summary>
/// <remarks>
/// <para>
/// The layouts are read with <see cref="Dat.DatSet.TryGetLocal{T}"/>. DatReaderWriter's LayoutDesc, ElementDesc and
/// StateDesc follow retail's <c>LayoutDesc::Serialize</c>, <c>ElementDesc::Serialize</c> and <c>StateDesc::Serialize</c>
/// (11.6096 0x0069B370, 0x0069CA30, 0x0069D480) field for field. Two of its decoders do not, and either would leave the
/// rest of the layout misread without an error, so every value they produce is checked here:
/// </para>
/// <list type="bullet">
///   <item>
///     <c>StringInfo</c> (dats.xml says "this doesnt match dats"). Retail's <c>StringInfo::Serialize</c> (11.4186
///     0x0042DEB0, 11.6096 0x0042E0D0) is re-expressed in <see cref="UiStringRef"/>'s remarks. Only the shape with no
///     literal (override 0), no strings (has-strings 0) and no variables (count 0) reads correctly; anything else throws.
///   </item>
///   <item>
///     <c>MediaDesc.Unpack</c> returns null for media type 12 (<c>Stretch</c>), which retail's
///     <c>MediaDesc::CreateMediaType</c> does not create either; a null entry throws, naming the layout and element.
///   </item>
/// </list>
/// <para>
/// It also checks what retail would treat as corrupt data: a hash-table key that differs from the id inside its value,
/// a media entry whose two type fields differ, and an edge mode above 4 (retail's switch has no case, but no layout
/// holds one, so it would be an unknown). Every failure is an <see cref="InvalidDataException"/> that starts with
/// <c>TODO(retailui-data)</c> and names the layout, the element path and what was found. The retail layouts convert
/// without one (UiLayoutCensusTests).
/// </para>
/// </remarks>
public static class UiDescConverter
{
    /// <summary>The highest edge mode <c>StateDesc::UpdateSizeAndPosition</c> handles.</summary>
    private const uint MaxEdge = (uint)UiEdge.Proportional;

    /// <summary>Converts a decoded LayoutDesc.</summary>
    /// <exception cref="InvalidDataException">The layout holds a shape DatReaderWriter cannot read correctly (see remarks).</exception>
    public static UiLayout Convert(LayoutDesc layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var layoutId = layout.Id;
        var where = $"layout 0x{layoutId:X8}";
        var elements = UiOrdered.Dictionary(
            layout.Elements.Select(kv => (kv.Key, ConvertElement(kv.Key, kv.Value, layoutId, where))),
            key => Fail(where, $"two top-level elements have the id 0x{key:X8}"));
        return new UiLayout(layoutId, checked((int)layout.Width), checked((int)layout.Height), elements);
    }

    /// <summary>
    /// Converts one property value. <paramref name="id"/> is the property's id: the hash key for a StateDesc's or a
    /// Struct's property, the master table's key for a default value (whose own id field the dat leaves 0).
    /// </summary>
    /// <exception cref="InvalidDataException">A type with no Lifestone model, or a StringInfo DatReaderWriter misread.</exception>
    internal static UiProperty ConvertProperty(BaseProperty property, uint id, string where) => property switch
    {
        BoolBaseProperty p => new UiBoolProperty(id, p.Value),
        IntegerBaseProperty p => new UiIntegerProperty(id, p.Value),
        FloatBaseProperty p => new UiFloatProperty(id, p.Value),
        VectorBaseProperty p => new UiVectorProperty(id, p.Value),
        ColorBaseProperty p => new UiColorProperty(id, ((uint)p.Value.Alpha << 24) | ((uint)p.Value.Red << 16) | ((uint)p.Value.Green << 8) | p.Value.Blue),
        StringInfoBaseProperty p => new UiStringInfoProperty(id, ConvertStringInfo(p.Value, $"{where} property 0x{id:X}")),
        EnumBaseProperty p => new UiEnumProperty(id, p.Value),
        DataIdBaseProperty p => new UiDataIdProperty(id, p.Value),
        InstanceIdBaseProperty p => new UiInstanceIdProperty(id, p.Value),
        Bitfield32BaseProperty p => new UiBitfield32Property(id, p.Value),
        Bitfield64BaseProperty p => new UiBitfield64Property(id, p.Value),
        ArrayBaseProperty p => new UiArrayProperty(
            id,
            UiOrdered.List(p.Value.Select((item, i) => ConvertProperty(item, item.MasterPropertyId, $"{where} property 0x{id:X}[{i}]")))),
        StructBaseProperty p => new UiStructProperty(id, ConvertProperties(p.Value, $"{where} property 0x{id:X}")),
        null => throw Fail(where, $"property 0x{id:X} has no value"),
        _ => throw Fail(where, $"property 0x{id:X} has type {property.PropertyType}, which has no Lifestone model"),
    };

    /// <summary>
    /// Reinterprets DatReaderWriter's StringInfo in retail's field order (<see cref="UiStringRef"/>'s remarks): its
    /// <c>Token</c> is the override byte, <c>Override</c> the has-strings byte, <c>English</c> the variables table's
    /// bucket-size byte and <c>Comment</c> the first byte of the variable count.
    /// </summary>
    internal static UiStringRef ConvertStringInfo(StringInfo info, string where)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (info.Token != 0)
        {
            // Override 1 is a literal: a UTF-16 string follows instead of the two ids, so everything after is misread.
            // Override 2 (auto-generated) reads like 0, but its meaning when resolving is unknown.
            throw Fail(where, $"a StringInfo has override {info.Token} (only 0, a table reference, is supported)");
        }

        if ((byte)info.Override != 0)
        {
            throw Fail(where, $"a StringInfo has its has-strings byte set ({(byte)info.Override}): three strings follow that DatReaderWriter does not read");
        }

        if (info.Comment != 0)
        {
            throw Fail(where, $"a StringInfo has variables (count byte {info.Comment}), which DatReaderWriter does not read");
        }

        return new UiStringRef(info.StringId, info.TableId.DataId);
    }

    private static UiElementDesc ConvertElement(uint key, ElementDesc element, uint layoutId, string parentWhere)
    {
        var where = $"{parentWhere} > element 0x{element.ElementId:X8}";
        if (key != element.ElementId)
        {
            throw Fail(where, $"it is stored under the key 0x{key:X8}");
        }

        var edges = new UiEdges(Edge(element.LeftEdge, "left", where), Edge(element.TopEdge, "top", where),
            Edge(element.RightEdge, "right", where), Edge(element.BottomEdge, "bottom", where));
        var baseState = ConvertState(element.StateDesc, where + " base state") with
        {
            // ElementDesc::Serialize stores these only when the incorporation flag is set; DatReaderWriter leaves the
            // others 0, as retail's StateDesc constructor does.
            X = unchecked((int)element.X),
            Y = unchecked((int)element.Y),
            Width = unchecked((int)element.Width),
            Height = unchecked((int)element.Height),
            ZLevel = element.ZLevel,
        };
        var states = UiOrdered.Dictionary(
            element.States.Select(kv => ((uint)kv.Key, ConvertKeyedState((uint)kv.Key, kv.Value, where))),
            k => Fail(where, $"two states have the id 0x{k:X}"));
        var children = UiOrdered.Dictionary(
            element.Children.Select(kv => (kv.Key, ConvertElement(kv.Key, kv.Value, layoutId, where))),
            k => Fail(where, $"two children have the id 0x{k:X8}"));

        return new UiElementDesc
        {
            ElementId = element.ElementId,
            Type = element.Type,
            BaseElement = element.BaseElement,
            BaseLayout = element.BaseLayoutId,
            DefaultState = (uint)element.DefaultState,
            ReadOrder = element.ReadOrder,
            Edges = edges,
            BaseState = baseState,
            States = states,
            Children = children,
            SourceLayout = layoutId,
        };
    }

    private static UiStateDesc ConvertKeyedState(uint key, StateDesc state, string elementWhere)
    {
        var where = $"{elementWhere} state 0x{key:X}";
        return key == state.StateId ? ConvertState(state, where) : throw Fail(where, $"its state id is 0x{state.StateId:X}");
    }

    private static UiStateDesc ConvertState(StateDesc state, string where) => new()
    {
        StateId = state.StateId,
        PassToChildren = state.PassToChildren,
        Incorporation = (UiIncorporation)(uint)state.IncorporationFlags,
        Properties = ConvertProperties(state.Properties, where),
        Media = UiOrdered.List(state.Media.Select((m, i) => ConvertMedia(m, $"{where} media {i}"))),
    };

    private static IReadOnlyDictionary<uint, UiProperty> ConvertProperties(Dictionary<uint, BaseProperty> properties, string where) =>
        UiOrdered.Dictionary(
            properties.Select(kv => (kv.Key, kv.Value is { } value && value.MasterPropertyId != kv.Key
                ? throw Fail(where, $"property 0x{kv.Key:X} holds a value for property 0x{value.MasterPropertyId:X}")
                : ConvertProperty(kv.Value!, kv.Key, where))),
            k => Fail(where, $"two properties have the id 0x{k:X}"));

    private static UiMedia ConvertMedia(MediaDesc? media, string where)
    {
        if (media is null)
        {
            throw Fail(where, "a media entry of a type DatReaderWriter cannot decode (MediaType.Stretch, 12, has no decoder; " +
                "retail's MediaDesc::CreateMediaType creates nothing for it), so the rest of the layout is misread");
        }

        if (media.Type != media.MediaType)
        {
            throw Fail(where, $"a {media.MediaType} media entry's own type field says {media.Type}");
        }

        return media switch
        {
            MediaDescImage m => new UiImageMedia(m.File, DrawMode(m.DrawMode, where)),
            MediaDescAlpha m => new UiAlphaMedia(m.File),
            MediaDescCursor m => new UiCursorMedia(m.File, m.XHotspot, m.YHotspot),
            MediaDescSound m => new UiSoundMedia(m.File, (uint)m.Sound),
            MediaDescState m => new UiStateMedia((uint)m.StateId, m.Probability),
            MediaDescJump m => new UiJumpMedia(m.JumpItemIndex, m.Probability),
            MediaDescMessage m => new UiMessageMedia(m.Id, m.Probability),
            MediaDescPause m => new UiPauseMedia(m.MinDuration, m.MaxDuration),
            MediaDescFade m => new UiFadeMedia(m.StartAlpha, m.EndAlpha, m.Duration),
            MediaDescAnimation m => new UiAnimationMedia(m.Duration, DrawMode(m.DrawMode, where), UiOrdered.List(m.Frames)),
            MediaDescMovie m => new UiMovieMedia(m.FileName.Value, m.StretchToFullScreen),
            _ => throw Fail(where, $"a {media.MediaType} media entry has no Lifestone model"),
        };
    }

    private static UiDrawMode DrawMode(DrawModeType mode, string where) =>
        (uint)mode <= (uint)UiDrawMode.AlphaBlend ? (UiDrawMode)(uint)mode : throw Fail(where, $"draw mode {(uint)mode} is unknown");

    private static UiEdge Edge(uint value, string side, string where) =>
        value <= MaxEdge ? (UiEdge)value : throw Fail(where, $"its {side} edge mode is {value}; StateDesc::UpdateSizeAndPosition handles 0-4");

    private static InvalidDataException Fail(string where, string what) =>
        new($"TODO(retailui-data): {where}: {what}.");
}
