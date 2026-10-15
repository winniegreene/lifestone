using System.Collections.ObjectModel;

namespace Lifestone.Data.Ui;

/// <summary>
/// One state of a UI element (retail's <c>StateDesc</c>): what changes when the element enters the state. An element's
/// own fields are also a StateDesc (<see cref="UiElementDesc.BaseState"/>), and only there do the position fields come
/// from the dat.
/// </summary>
/// <remarks>
/// Immutable. <see cref="Incorporation"/> says which fields override a base desc's when the descs are merged
/// (<c>StateDesc::Incorporate</c>, <see cref="UiDescResolver"/>). The position and size are relative to the parent; for
/// a state of <see cref="UiElementDesc.States"/> they are 0 (the dat stores none) unless re-anchoring moved them.
/// </remarks>
public sealed record UiStateDesc
{
    /// <summary>The state's id (<c>m_stateID</c>; 0 for an element's base state).</summary>
    public uint StateId { get; init; }

    /// <summary>Whether entering the state also passes it to the element's children (<c>m_bPassToChildren</c>).</summary>
    public bool PassToChildren { get; init; }

    /// <summary>Which fields override a base desc's (<c>m_uiIncorporationFlags</c>).</summary>
    public UiIncorporation Incorporation { get; init; }

    /// <summary>The left column, relative to the parent (<c>m_x</c>).</summary>
    public int X { get; init; }

    /// <summary>The top row, relative to the parent (<c>m_y</c>).</summary>
    public int Y { get; init; }

    /// <summary>The width in layout pixels (<c>m_width</c>).</summary>
    public int Width { get; init; }

    /// <summary>The height in layout pixels (<c>m_height</c>).</summary>
    public int Height { get; init; }

    /// <summary>The z level (<c>m_zLevel</c>); how it orders drawing is the runtime's (R10).</summary>
    public uint ZLevel { get; init; }

    /// <summary>The state's properties by id, in dat order (<c>m_properties</c>).</summary>
    public IReadOnlyDictionary<uint, UiProperty> Properties { get; init; } = ReadOnlyDictionary<uint, UiProperty>.Empty;

    /// <summary>The state's media, in order (<c>m_media</c>).</summary>
    public IReadOnlyList<UiMedia> Media { get; init; } = [];

    /// <summary>The position and size as an inclusive box (<c>(x, y, x + w - 1, y + h - 1)</c>).</summary>
    public UiBox Box => UiBox.FromPositionAndSize(X, Y, Width, Height);

    /// <summary>This state with its position and size taken from <paramref name="box"/>.</summary>
    public UiStateDesc WithBox(UiBox box) => this with { X = box.X0, Y = box.Y0, Width = box.Width, Height = box.Height };
}

/// <summary>
/// One UI element as a layout describes it (retail's <c>ElementDesc</c>): its class, its own state, its other states and
/// its children. A desc of <see cref="Type"/> 0 is a partial desc that inherits from <see cref="BaseElement"/> of
/// <see cref="BaseLayout"/>; <see cref="UiDescResolver"/> (<c>LayoutDesc::InqFullDesc</c>) turns it into a full one.
/// </summary>
/// <remarks>Immutable; the collections keep the dat's order.</remarks>
public sealed record UiElementDesc
{
    /// <summary>The element's id (<c>m_elementID</c>), unique among its siblings.</summary>
    public uint ElementId { get; init; }

    /// <summary>The element's UI class (<c>m_type</c>); 0 for a desc that inherits its class from its base.</summary>
    public uint Type { get; init; }

    /// <summary>The top-level element of <see cref="BaseLayout"/> this desc inherits from (<c>m_baseElement</c>), or 0.</summary>
    public uint BaseElement { get; init; }

    /// <summary>The LayoutDesc (0x21xxxxxx) that holds <see cref="BaseElement"/> (<c>m_baseLayout</c>), or 0.</summary>
    public uint BaseLayout { get; init; }

    /// <summary>The state the element starts in (<c>m_defaultState</c>; 0 for none).</summary>
    public uint DefaultState { get; init; }

    /// <summary>The element's read order among its siblings (<c>m_uiReadOrder</c>).</summary>
    public uint ReadOrder { get; init; }

    /// <summary>How the element follows its parent's size changes (<c>m_leftEdge</c> .. <c>m_bottomEdge</c>).</summary>
    public UiEdges Edges { get; init; }

    /// <summary>
    /// The element's own StateDesc (retail's <c>ElementDesc</c> derives from <c>StateDesc</c>): its position, size, z
    /// level, properties and media.
    /// </summary>
    public UiStateDesc BaseState { get; init; } = new();

    /// <summary>The element's other states by state id, in dat order (<c>m_states</c>).</summary>
    public IReadOnlyDictionary<uint, UiStateDesc> States { get; init; } = ReadOnlyDictionary<uint, UiStateDesc>.Empty;

    /// <summary>The element's children by element id, in dat order (<c>m_children</c>).</summary>
    public IReadOnlyDictionary<uint, UiElementDesc> Children { get; init; } = ReadOnlyDictionary<uint, UiElementDesc>.Empty;

    /// <summary>
    /// The LayoutDesc this desc was read from (a Lifestone addition, for messages; a resolved desc keeps the derived
    /// desc's layout).
    /// </summary>
    public uint SourceLayout { get; init; }

    /// <summary>The element's position and size, from <see cref="BaseState"/>.</summary>
    public UiBox Box => BaseState.Box;

    /// <summary>The element's own properties, from <see cref="BaseState"/>.</summary>
    public IReadOnlyDictionary<uint, UiProperty> Properties => BaseState.Properties;

    /// <summary>The element's own media, from <see cref="BaseState"/>.</summary>
    public IReadOnlyList<UiMedia> Media => BaseState.Media;

    /// <summary>For logs: <c>element 0xEEEEEEEE of layout 0xLLLLLLLL</c>.</summary>
    public string Describe() => $"element 0x{ElementId:X8} of layout 0x{SourceLayout:X8}";
}

/// <summary>
/// One LayoutDesc (0x21xxxxxx in <c>client_local_English.dat</c>): its design size and its top-level elements.
/// </summary>
/// <param name="Id">The layout's DID.</param>
/// <param name="Width">The width the layout was designed for (<c>m_displayWidth</c>; 800 for every retail layout).</param>
/// <param name="Height">The height it was designed for (<c>m_displayHeight</c>; 600, or 500 for three).</param>
/// <param name="Elements">The top-level elements by element id, in dat order (<c>m_elements</c>).</param>
public sealed record UiLayout(uint Id, int Width, int Height, IReadOnlyDictionary<uint, UiElementDesc> Elements)
{
    /// <summary>The layout's top-level element <paramref name="elementId"/>, or null (<c>LayoutDesc::AccessElementDesc</c>).</summary>
    public UiElementDesc? AccessElementDesc(uint elementId) =>
        Elements.TryGetValue(elementId, out var element) ? element : null;

    /// <summary>Every element of the layout, top-level ones first, each followed by its descendants (depth first).</summary>
    public IEnumerable<UiElementDesc> AllElements() => Elements.Values.SelectMany(Descendants);

    private static IEnumerable<UiElementDesc> Descendants(UiElementDesc element) =>
        element.Children.Values.SelectMany(Descendants).Prepend(element);
}

/// <summary>Builds the ordered, read-only collections of the desc model.</summary>
internal static class UiOrdered
{
    /// <summary>A read-only dictionary that enumerates in insertion order.</summary>
    /// <param name="entries">The entries, in order.</param>
    /// <param name="duplicate">The exception to throw when two entries share a key.</param>
    public static IReadOnlyDictionary<uint, T> Dictionary<T>(IEnumerable<(uint Key, T Value)> entries, Func<uint, Exception> duplicate)
    {
        var ordered = new OrderedDictionary<uint, T>();
        foreach (var (key, value) in entries)
        {
            if (!ordered.TryAdd(key, value))
            {
                throw duplicate(key);
            }
        }

        return ordered.Count == 0 ? ReadOnlyDictionary<uint, T>.Empty : new ReadOnlyDictionary<uint, T>(ordered);
    }

    /// <summary>A read-only copy of <paramref name="items"/>.</summary>
    public static IReadOnlyList<T> List<T>(IEnumerable<T> items)
    {
        var array = items.ToArray();
        return array.Length == 0 ? [] : Array.AsReadOnly(array);
    }
}
