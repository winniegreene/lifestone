using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;

namespace Lifestone.Data.Ui;

/// <summary>
/// The retail layouts of <c>client_local_English.dat</c> as <see cref="UiLayout"/>s: read and converted once each on
/// first use, found by DID or by UILAYOUT enum, and shared by every thread.
/// </summary>
/// <remarks>
/// <para>
/// Retail finds a mode's root layout by enum: <c>UIMainFramework::CreateAndAddRootElement</c> (11.4186 0x006A0C00,
/// 11.6096 0x006A1A20) resolves the layout enum through the UILAYOUT group (5, map 0x2500000E) with
/// <c>DBObj::GetDIDByEnum</c> and gives up when it does not resolve; <see cref="ResolveLayoutEnum"/> and
/// <see cref="TryResolveLayoutEnum"/> do the same through <see cref="EnumIdMaps"/>. Within a layout,
/// <c>LayoutDesc::AccessElementDesc</c> (11.4186 0x00699990, 11.6096 0x0069A850) looks only at the top-level element
/// table (<see cref="AccessElementDesc"/>).
/// </para>
/// <para>
/// Lifestone's own panels are layouts too (<see cref="LifestoneLayoutReader"/>): <see cref="Register"/> adds them, in
/// DIDs 0x7F000000-0x7FFFFFFF, and from then on they are found and resolved exactly like retail ones.
/// </para>
/// <para>
/// A layout that fails to convert (<see cref="UiDescConverter"/>) throws, and so does every later request for it: the
/// failure is cached with the layout. Nothing here writes files; a diagnostic dump of a layout belongs in an opt-in test
/// that writes under <c>cache/</c>.
/// </para>
/// </remarks>
public sealed class UiLayouts
{
    /// <summary>The first DID of the range Lifestone's own layouts use (<see cref="Register"/>).</summary>
    public const uint LifestoneLayoutFirst = 0x7F000000u;

    /// <summary>The last DID of that range.</summary>
    public const uint LifestoneLayoutLast = 0x7FFFFFFFu;

    private readonly Func<uint, UiLayout?> _read;
    private readonly Func<uint, uint?> _resolveEnum;
    private readonly ConcurrentDictionary<uint, Lazy<UiLayout?>> _layouts = new();
    private readonly ConcurrentDictionary<uint, UiLayout> _registered = new();
    private readonly object _embeddedLock = new();
    private readonly DatSet? _dats;
    private bool _embeddedRead;
    private string? _embeddedFailure;

    /// <summary>The layouts of <paramref name="dats"/>' language dat, found by enum through <see cref="DatSet.EnumMaps"/>.</summary>
    /// <param name="dats">The opened dats (not owned).</param>
    /// <exception cref="InvalidOperationException"><c>client_local_English.dat</c> is absent (<see cref="DatSet.RequireLocal"/>).</exception>
    public UiLayouts(DatSet dats)
        : this(Read(dats), e => dats.EnumMaps.TryResolve(EnumGroup.UiLayout, e, out var did) ? did : null, dats.GetAllLocalIds<LayoutDesc>())
    {
        _dats = dats;
    }

    /// <summary>Layouts from any source (tests use synthetic ones).</summary>
    /// <param name="read">Returns the layout with the given DID, or null; called at most once per DID.</param>
    /// <param name="resolveEnum">Resolves a UILAYOUT enum to a layout DID, or null.</param>
    /// <param name="layoutIds">Every layout DID the source holds.</param>
    internal UiLayouts(Func<uint, UiLayout?> read, Func<uint, uint?> resolveEnum, IReadOnlyList<uint> layoutIds)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(resolveEnum);
        ArgumentNullException.ThrowIfNull(layoutIds);
        _read = read;
        _resolveEnum = resolveEnum;
        LayoutIds = layoutIds.Order().ToArray();
        Resolver = new UiDescResolver(this);
    }

    /// <summary>
    /// Where a failure to load Lifestone's embedded layouts (a file that does not read, two files with one DID, an unreadable resource) is reported once, as an error line naming the file;
    /// the failure is also thrown by every lookup in the Lifestone range (the host sets this to its log).
    /// </summary>
    public Action<string>? ErrorLog { get; init; }

    /// <summary>The embedded layout files (<see cref="LifestoneLayoutFiles.ReadAll"/>; tests give their own).</summary>
    internal Func<IReadOnlyList<(string Name, string Json)>> EmbeddedFiles { get; init; } = LifestoneLayoutFiles.ReadAll;

    /// <summary>Every LayoutDesc DID, ascending (101 in the retail language dat).</summary>
    public IReadOnlyList<uint> LayoutIds { get; }

    /// <summary>Template inheritance over these layouts, with its cache of resolved bases.</summary>
    public UiDescResolver Resolver { get; }

    /// <summary>Layouts built in memory, with UILAYOUT enums mapped by <paramref name="enums"/> (tests).</summary>
    internal static UiLayouts FromLayouts(IEnumerable<UiLayout> layouts, IReadOnlyDictionary<uint, uint>? enums = null)
    {
        var byId = layouts.ToDictionary(l => l.Id);
        return new UiLayouts(
            id => byId.GetValueOrDefault(id),
            e => enums is not null && enums.TryGetValue(e, out var did) ? did : null,
            byId.Keys.ToArray());
    }

    /// <summary>The layout <paramref name="layoutId"/>: a registered Lifestone layout, or one of the language dat; false when neither holds it.</summary>
    /// <exception cref="InvalidDataException">The layout exists but does not convert (<see cref="UiDescConverter"/>).</exception>
    public bool TryGetLayout(uint layoutId, [NotNullWhen(true)] out UiLayout? layout)
    {
        if (_registered.TryGetValue(layoutId, out layout))
        {
            return true;
        }

        if (layoutId is >= LifestoneLayoutFirst and <= LifestoneLayoutLast)
        {
            EnsureEmbeddedLayouts();
            return _registered.TryGetValue(layoutId, out layout);
        }

        layout = _layouts.GetOrAdd(
            layoutId,
            static (id, read) => new Lazy<UiLayout?>(() => read(id), LazyThreadSafetyMode.ExecutionAndPublication),
            _read).Value;
        return layout is not null;
    }

    /// <summary>
    /// Reads and registers Lifestone's embedded layout files (<see cref="LifestoneLayoutFiles"/>) once, on the first lookup of a DID in the Lifestone range, so a host needs no
    /// registration call. A layout already registered under the same DID (a test's own) is kept. A file that does not read fails every lookup in the range, loudly.
    /// </summary>
    private void EnsureEmbeddedLayouts()
    {
        if (_dats is null)
        {
            return; // layouts from any other source (tests) have only what they register
        }

        lock (_embeddedLock)
        {
            if (!_embeddedRead)
            {
                _embeddedRead = true;
                var errors = new List<string>();
                try
                {
                    var tables = new UiStringTables(_dats);
                    var reader = new LifestoneLayoutReader(MasterPropertyTable.Load(_dats), e => tables.TryResolveTableEnum(e, out var did) ? did : null);
                    var fileOf = new Dictionary<uint, string>();
                    foreach (var (name, json) in EmbeddedFiles())
                    {
                        var fileErrors = new List<string>();
                        if (reader.Read(json, fileErrors) is not { } layout)
                        {
                            errors.AddRange(fileErrors.Select(e => $"{name}: {e}"));
                        }
                        else if (fileOf.TryGetValue(layout.Id, out var first))
                        {
                            errors.Add($"{name}: layout DID 0x{layout.Id:X8} is already used by {first}; each embedded layout needs its own DID");
                        }
                        else
                        {
                            fileOf[layout.Id] = name;
                            _registered.TryAdd(layout.Id, layout); // a layout a test registered under the same DID wins
                        }
                    }
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    errors.Add($"the files could not be read: {e.GetType().Name}: {e.Message}");
                }

                _embeddedFailure = errors.Count == 0 ? null : "TODO(g7-layouts): Lifestone's embedded layouts do not read: " + string.Join("; ", errors);
                if (_embeddedFailure is not null)
                {
                    ErrorLog?.Invoke(_embeddedFailure);
                }
            }
        }

        if (_embeddedFailure is not null)
        {
            throw new InvalidDataException(_embeddedFailure);
        }
    }

    /// <summary>
    /// Reads and registers Lifestone's embedded layout files now (about 20 ms the first time), for a host to call from a worker at start-up so that the first lookup in the Lifestone range, which
    /// would otherwise do it on the frame that happens to ask (the first F7), finds them ready (docs/session.md section 27, the 50 ms rule). A failure is not thrown here: it is reported through
    /// <see cref="ErrorLog"/> once, and every lookup in the range still throws it (S2's loud error).
    /// </summary>
    /// <returns>True when the files read and registered; false when they did not (the error was logged) or this instance has no dats.</returns>
    public bool WarmEmbeddedLayouts()
    {
        if (_dats is null)
        {
            return false;
        }

        try
        {
            EnsureEmbeddedLayouts();
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>The layout <paramref name="layoutId"/>, or a loud failure.</summary>
    /// <exception cref="InvalidDataException">The language dat does not hold it, or it does not convert.</exception>
    public UiLayout GetLayout(uint layoutId) =>
        TryGetLayout(layoutId, out var layout)
            ? layout
            : throw new InvalidDataException($"TODO(retailui-data): LayoutDesc 0x{layoutId:X8} is not in {DatSet.LocalFileName}.");

    /// <summary>Every registered Lifestone layout DID, ascending (the embedded files register on the first lookup in the Lifestone range, <see cref="TryGetLayout"/>).</summary>
    public IReadOnlyList<uint> RegisteredLayoutIds => _registered.Keys.Order().ToArray();

    /// <summary>
    /// Adds one of Lifestone's own layouts (<see cref="LifestoneLayoutReader"/>), so that it is found by DID and its
    /// elements resolve against the retail templates they name exactly like a retail layout's. Its DID must lie in
    /// <see cref="LifestoneLayoutFirst"/>-<see cref="LifestoneLayoutLast"/>, where no retail layout is; a layout is
    /// registered once (the resolver caches what it resolved from it).
    /// </summary>
    /// <exception cref="ArgumentException">The DID is outside the Lifestone range.</exception>
    /// <exception cref="InvalidOperationException">A different layout with the same DID is already registered.</exception>
    public void Register(UiLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.Id is < LifestoneLayoutFirst or > LifestoneLayoutLast)
        {
            throw new ArgumentException(
                $"Lifestone layouts use DIDs 0x{LifestoneLayoutFirst:X8}-0x{LifestoneLayoutLast:X8}; 0x{layout.Id:X8} is outside that range.", nameof(layout));
        }

        var stored = _registered.GetOrAdd(layout.Id, layout);
        if (!ReferenceEquals(stored, layout))
        {
            throw new InvalidOperationException($"A Lifestone layout 0x{layout.Id:X8} is already registered.");
        }
    }

    /// <summary>
    /// Resolves a UILAYOUT enum to its LayoutDesc DID, as <c>UIMainFramework::CreateAndAddRootElement</c> does; false
    /// (retail's INVALID_DID, where it creates nothing) when it does not resolve.
    /// </summary>
    public bool TryResolveLayoutEnum(uint layoutEnum, out uint layoutId)
    {
        var did = _resolveEnum(layoutEnum);
        layoutId = did ?? 0;
        return did is not null;
    }

    /// <summary>Resolves a UILAYOUT enum to its LayoutDesc DID, or a loud failure.</summary>
    /// <exception cref="InvalidDataException">The enum does not resolve.</exception>
    public uint ResolveLayoutEnum(uint layoutEnum) =>
        TryResolveLayoutEnum(layoutEnum, out var layoutId)
            ? layoutId
            : throw new InvalidDataException($"TODO(retailui-data): UILAYOUT enum 0x{layoutEnum:X8} does not resolve to a layout.");

    /// <summary>
    /// The top-level element <paramref name="elementId"/> of layout <paramref name="layoutId"/>
    /// (<c>LayoutDesc::AccessElementDesc</c>), or null when the layout or the element is absent.
    /// </summary>
    public UiElementDesc? AccessElementDesc(uint layoutId, uint elementId) =>
        TryGetLayout(layoutId, out var layout) ? layout.AccessElementDesc(elementId) : null;

    /// <summary>
    /// The desc a mode's root is created from: the top-level element <see cref="UiRootRef.ElementId"/> of the layout
    /// <see cref="UiRootRef.LayoutEnum"/> resolves to. The desc may be partial (<see cref="InqFullDesc"/> completes it).
    /// </summary>
    /// <exception cref="InvalidDataException">The enum, the layout or the element is absent.</exception>
    public UiElementDesc GetRootDesc(UiRootRef root)
    {
        var layout = GetLayout(ResolveLayoutEnum(root.LayoutEnum));
        return layout.AccessElementDesc(root.ElementId) ?? throw new InvalidDataException(
            $"TODO(retailui-data): layout 0x{layout.Id:X8} (UILAYOUT enum 0x{root.LayoutEnum:X8}) has no top-level element 0x{root.ElementId:X8}.");
    }

    /// <summary>
    /// <c>LayoutDesc::InqFullDesc</c>: the full desc of <paramref name="local"/> (itself when its type is not 0, else
    /// merged onto its resolved base), or why there is none (<see cref="UiDescResolver"/>).
    /// </summary>
    public UiResolveResult InqFullDesc(UiElementDesc local) => Resolver.InqFullDesc(local);

    /// <summary>The full desc of <paramref name="local"/>, or a loud failure naming the broken link.</summary>
    /// <exception cref="InvalidDataException">The desc does not resolve.</exception>
    public UiElementDesc RequireFullDesc(UiElementDesc local) => Resolver.RequireFullDesc(local);

    private static Func<uint, UiLayout?> Read(DatSet dats)
    {
        ArgumentNullException.ThrowIfNull(dats);
        dats.RequireLocal("The retail interface");
        return id => dats.TryGetLocal<LayoutDesc>(id, out var layout) ? UiDescConverter.Convert(layout) : null;
    }
}
