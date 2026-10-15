using System.Collections.Concurrent;

namespace Lifestone.Data.Ui;

/// <summary>The result of resolving a desc: the full desc, or why there is none.</summary>
/// <param name="Full">The full desc, or null on failure.</param>
/// <param name="Failure">Why the desc does not resolve (naming the element, its layout and the broken link), or null.</param>
public readonly record struct UiResolveResult(UiElementDesc? Full, string? Failure)
{
    /// <summary>True when <see cref="Full"/> is set.</summary>
    public bool Succeeded => Full is not null;
}

/// <summary>
/// Template inheritance: turns a partial element desc (<see cref="UiElementDesc.Type"/> 0) into a full one by merging it
/// onto its base element's full desc, as retail does before it creates an element.
/// </summary>
/// <remarks>
/// <para>
/// <b><c>LayoutDesc::InqFullDesc</c></b> (11.4186 0x0069A520, 11.6096 0x0069B400), re-expressed in
/// <see cref="InqFullDesc"/>: a desc whose type is not 0 is used exactly as authored. A desc of type 0 names a base
/// element, which must be a top-level element of its base layout (<c>LayoutDesc::AccessElementDesc</c>); the base is
/// resolved the same way (recursively), and the desc is merged onto the result with <see cref="Incorporate(UiElementDesc, UiElementDesc)"/>. Resolved
/// bases are cached per (layout, element), as retail's <c>LayoutDesc::s_DescCache</c>; only successes are cached, as there.
/// A missing base layout or element fails, and so does a base reached twice in one chain (retail adds each base to a
/// list with <c>SmartArray::add_unique</c>, 11.4186 0x00699B40, 11.6096 0x0069AA00, and fails when it is already there);
/// retail then creates nothing, and the failure here names the whole chain.
/// </para>
/// <para>
/// <b><c>ElementDesc::Incorporate</c></b> (11.4186 0x0069B5A0, 11.6096 0x0069C430) merges a derived desc onto a base:
/// </para>
/// <list type="number">
///   <item>the base state, with <see cref="Incorporate(UiStateDesc, UiStateDesc)"/>;</item>
///   <item>the element id, default state, edges and read order are the derived desc's; the class and the base links stay
///   the base's;</item>
///   <item>each derived state is merged onto the base's state of the same id, or added;</item>
///   <item>each derived child is merged (recursively, without resolving its own base) onto the base's child of the same
///   id, or added with its read order raised by the number of base children the derived desc does not mention;</item>
///   <item>if the merge changed the element's width or height, the states and children only the base has are re-anchored
///   from the old box to the new one (<see cref="UiAnchor"/>): the states by the element's (new) edges, the children by
///   their own.</item>
/// </list>
/// <para>
/// <b><c>StateDesc::Incorporate</c></b> (11.4186 0x0069CCA0, 11.6096 0x0069DB30): the incorporation flags are ORed; the
/// pass-to-children flag, X, Y, width, height and z level are copied only when the derived desc's flag for each is set;
/// the derived media follow the base's (<c>StateDesc::ConcatenateMedia</c>); the derived properties replace or join the
/// base's (<c>PropertyCollection::UpdateCollection</c>, 11.4186 0x006807E0, 11.6096 0x006817E0). The state id stays the
/// base's.
/// </para>
/// <para>
/// Everything is immutable, so a resolved desc never shares mutable state with the cache. Safe from any thread.
/// </para>
/// </remarks>
public sealed class UiDescResolver
{
    private readonly UiLayouts _layouts;
    private readonly ConcurrentDictionary<(uint Layout, uint Element), UiElementDesc> _bases = new();

    /// <summary>A resolver over <paramref name="layouts"/> (normally reached through <see cref="UiLayouts.InqFullDesc"/>).</summary>
    public UiDescResolver(UiLayouts layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        _layouts = layouts;
    }

    /// <summary><c>LayoutDesc::InqFullDesc</c>: the full desc of <paramref name="local"/>, or why there is none.</summary>
    /// <exception cref="InvalidDataException">A base layout exists but does not convert (<see cref="UiDescConverter"/>).</exception>
    public UiResolveResult InqFullDesc(UiElementDesc local)
    {
        ArgumentNullException.ThrowIfNull(local);
        if (local.Type != 0)
        {
            return new UiResolveResult(local, null);
        }

        var chain = new List<(uint Layout, uint Element)>();
        var full = ResolveBase(local, chain, out var failure);
        return full is null
            ? new UiResolveResult(null, $"{local.Describe()} does not resolve: {failure}")
            : new UiResolveResult(Incorporate(full, local), null);
    }

    /// <summary>The full desc of <paramref name="local"/>, or a loud failure naming the broken link.</summary>
    /// <exception cref="InvalidDataException">The desc does not resolve.</exception>
    public UiElementDesc RequireFullDesc(UiElementDesc local)
    {
        var result = InqFullDesc(local);
        return result.Full ?? throw new InvalidDataException($"TODO(retailui-data): {result.Failure}");
    }

    /// <summary><c>ElementDesc::Incorporate</c>: <paramref name="local"/> merged onto <paramref name="full"/> (see remarks).</summary>
    public static UiElementDesc Incorporate(UiElementDesc full, UiElementDesc local)
    {
        ArgumentNullException.ThrowIfNull(full);
        ArgumentNullException.ThrowIfNull(local);
        var oldSize = full.Box;
        var baseState = Incorporate(full.BaseState, local.BaseState);
        var newSize = baseState.Box;

        var resized = oldSize.Width != newSize.Width || oldSize.Height != newSize.Height;
        var states = full.States;
        if (local.States.Count > 0 || (resized && states.Count > 0))
        {
            var merged = new OrderedDictionary<uint, UiStateDesc>(full.States);
            foreach (var (id, state) in local.States)
            {
                merged[id] = merged.TryGetValue(id, out var inherited) ? Incorporate(inherited, state) : state;
            }

            if (resized)
            {
                // Only the states the derived desc does not mention, by the element's (now the derived desc's) edges.
                foreach (var id in merged.Keys.Where(id => !local.States.ContainsKey(id)).ToArray())
                {
                    merged[id] = UiAnchor.UpdateSizeAndPosition(merged[id], oldSize, newSize, local.Edges);
                }
            }

            states = UiOrdered.Dictionary(merged.Select(kv => (kv.Key, kv.Value)), DuplicateKey);
        }

        var children = full.Children;
        if (local.Children.Count > 0 || (resized && children.Count > 0))
        {
            var inheritedChildren = (uint)full.Children.Keys.Count(id => !local.Children.ContainsKey(id));
            var merged = new OrderedDictionary<uint, UiElementDesc>(full.Children);
            foreach (var (id, child) in local.Children)
            {
                merged[id] = merged.TryGetValue(id, out var inherited)
                    ? Incorporate(inherited, child)
                    : child with { ReadOrder = child.ReadOrder + inheritedChildren };
            }

            if (resized)
            {
                // Only the children the derived desc does not mention, each by its own edges.
                foreach (var id in merged.Keys.Where(id => !local.Children.ContainsKey(id)).ToArray())
                {
                    merged[id] = UiAnchor.UpdateSizeAndPosition(merged[id], oldSize, newSize);
                }
            }

            children = UiOrdered.Dictionary(merged.Select(kv => (kv.Key, kv.Value)), DuplicateKey);
        }

        return full with
        {
            ElementId = local.ElementId,
            DefaultState = local.DefaultState,
            Edges = local.Edges,
            ReadOrder = local.ReadOrder,
            BaseState = baseState,
            States = states,
            Children = children,
            SourceLayout = local.SourceLayout,
        };
    }

    /// <summary><c>StateDesc::Incorporate</c>: <paramref name="local"/> merged onto <paramref name="full"/> (see remarks).</summary>
    public static UiStateDesc Incorporate(UiStateDesc full, UiStateDesc local)
    {
        ArgumentNullException.ThrowIfNull(full);
        ArgumentNullException.ThrowIfNull(local);
        var flags = local.Incorporation;
        bool Has(UiIncorporation flag) => (flags & flag) != 0;

        var properties = new OrderedDictionary<uint, UiProperty>(full.Properties);
        foreach (var (id, property) in local.Properties)
        {
            properties[id] = property;
        }

        return full with
        {
            Incorporation = full.Incorporation | flags,
            PassToChildren = Has(UiIncorporation.PassToChildren) ? local.PassToChildren : full.PassToChildren,
            X = Has(UiIncorporation.X) ? local.X : full.X,
            Y = Has(UiIncorporation.Y) ? local.Y : full.Y,
            Width = Has(UiIncorporation.Width) ? local.Width : full.Width,
            Height = Has(UiIncorporation.Height) ? local.Height : full.Height,
            ZLevel = Has(UiIncorporation.ZLevel) ? local.ZLevel : full.ZLevel,
            Media = local.Media.Count == 0 ? full.Media : full.Media.Count == 0 ? local.Media : UiOrdered.List(full.Media.Concat(local.Media)),
            Properties = local.Properties.Count == 0 ? full.Properties : UiOrdered.Dictionary(properties.Select(kv => (kv.Key, kv.Value)), DuplicateKey),
        };
    }

    /// <summary>
    /// The full desc of <paramref name="local"/>'s base (not yet merged with <paramref name="local"/>), from the cache or
    /// by resolving it; null with <paramref name="failure"/> set when the chain breaks.
    /// </summary>
    private UiElementDesc? ResolveBase(UiElementDesc local, List<(uint Layout, uint Element)> chain, out string? failure)
    {
        (uint Layout, uint Element) key = (local.BaseLayout, local.BaseElement);
        if (_bases.TryGetValue(key, out var cached))
        {
            failure = null;
            return cached;
        }

        var link = $"base 0x{local.BaseLayout:X8}/0x{local.BaseElement:X8}";
        if (!_layouts.TryGetLayout(local.BaseLayout, out var layout))
        {
            failure = $"{link}: layout 0x{local.BaseLayout:X8} is not in client_local_English.dat";
            return null;
        }

        var baseDesc = layout.AccessElementDesc(local.BaseElement);
        if (baseDesc is null)
        {
            failure = $"{link}: layout 0x{local.BaseLayout:X8} has no top-level element 0x{local.BaseElement:X8}";
            return null;
        }

        if (chain.Contains(key))
        {
            failure = $"{link}: inheritance cycle ({string.Join(" -> ", chain.Append(key).Select(k => $"0x{k.Layout:X8}/0x{k.Element:X8}"))})";
            return null;
        }

        chain.Add(key);
        UiElementDesc? full;
        if (baseDesc.Type != 0)
        {
            full = baseDesc;
        }
        else
        {
            var deeper = ResolveBase(baseDesc, chain, out failure);
            if (deeper is null)
            {
                failure = $"{link} -> {failure}";
                return null;
            }

            full = Incorporate(deeper, baseDesc);
        }

        failure = null;
        return _bases.GetOrAdd(key, full);
    }

    private static InvalidOperationException DuplicateKey(uint key) => new($"Duplicate key 0x{key:X} in an ordered dictionary.");
}
