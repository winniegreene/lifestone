using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using Lifestone.Data.Dat;
using Lifestone.Data.Terrain;
using Lifestone.Data.Textures;

namespace Lifestone.Data.Tests.Dat;

/// <summary>
/// The language dat (<c>client_local_English.dat</c>) read through <see cref="DatSet"/>'s standalone databases: its
/// records are unpacked with a reader bound to the portal, which holds the master property table (m2-log-in.md §5.1).
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class LocalDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [RequiresLocalDatsFact]
    public void Local_iteration_is_994()
    {
        Assert.True(Dats.HasLocal);
        Assert.Equal(994, Dats.Iterations.Local); // ACE DatManager.ITERATION_LANGUAGE; reported to the server at login
        Assert.Equal(994, Dats.Read(d => d.Local!.Iteration.CurrentIteration));
        Assert.Contains("-l994-", Dats.Iterations.CacheKey, StringComparison.Ordinal);
        Dats.RequireLocal("This test"); // present: no throw
    }

    [RequiresLocalDatsFact]
    public void A_layout_decodes_with_its_id_and_typed_properties()
    {
        Assert.True(Dats.TryGetLocal<LayoutDesc>(0x2100000Fu, out var smartBox)); // classic_smartbox
        Assert.Equal(0x2100000Fu, smartBox.Id);
        Assert.Equal((800u, 600u), (smartBox.Width, smartBox.Height));
        Assert.NotEmpty(smartBox.Elements);
        Assert.Contains(AllElements(smartBox.Elements.Values), e => AllStates(e).Any(s => s.Properties.Count > 0));

        Assert.False(Dats.TryGetLocal<LayoutDesc>(0x21FFFFFFu, out var missing));
        Assert.Null(missing);
        Assert.Null(Dats.TryGetLocalBytes(0x21FFFFFFu));
    }

    [RequiresLocalDatsFact]
    public void The_language_database_alone_cannot_type_layout_properties()
    {
        // Why TryGetLocal exists: DatReaderWriter's own LocalDatabase.TryGet binds the reader to the language dat, where
        // BaseProperty.UnpackGeneric finds no master property table and throws. If a DatReaderWriter update makes this
        // pass, the note on standalone databases in DatSet's remarks can be revisited.
        var error = Assert.ThrowsAny<Exception>(() => Dats.Read(d => d.Local!.TryGet<LayoutDesc>(0x2100000Fu, out _)));
        Assert.Contains("MasterProperties", error.Message, StringComparison.Ordinal); // BaseProperty.UnpackGeneric, not another failure
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_layout_decodes_exactly_through_the_dat_set()
    {
        var ids = Dats.GetAllLocalIds<LayoutDesc>();
        Assert.Equal(101, ids.Count); // m2-log-in.md §5.1
        Assert.All(ids, id => Assert.Equal(0x21000000u, id & 0xFF000000u));

        var elements = 0;
        var sizes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            Assert.True(Dats.TryGetLocal<LayoutDesc>(id, out var layout), $"0x{id:X8} does not decode");
            Assert.Equal(id, layout.Id);

            // The same bytes, unpacked by hand, are consumed exactly: nothing is left over or read past the end.
            var bytes = Dats.TryGetLocalBytes(id)!;
            var consumed = Dats.Read(d =>
            {
                var reader = new DatBinReader(bytes, d.Portal);
                new LayoutDesc().Unpack(reader);
                return reader.Offset;
            });
            Assert.True(consumed == bytes.Length, $"0x{id:X8}: unpacking consumed {consumed} of {bytes.Length} bytes");

            elements += AllElements(layout.Elements.Values).Count();
            var size = $"{layout.Width}x{layout.Height}";
            sizes[size] = sizes.GetValueOrDefault(size) + 1;
        }

        Assert.Equal(2162, elements); // every element, children included
        Assert.Equal(new SortedDictionary<string, int>(StringComparer.Ordinal) { ["800x500"] = 3, ["800x600"] = 98 }, sizes);
    }

    [RequiresLocalDatsFact]
    public void Every_string_table_decodes_and_the_missing_id_is_0x2300000F()
    {
        var ids = Dats.GetAllLocalIds<StringTable>();
        var range = Enumerable.Range(0, 16).Select(i => 0x23000001u + (uint)i).ToArray();

        // m2-log-in.md §5.1 says 15 tables in 0x23000001-0x23000010: that range holds 16 ids, and one is absent.
        Assert.Equal(15, ids.Count);
        Assert.Equal(ids.Order(), ids);
        Assert.Subset(range.ToHashSet(), ids.ToHashSet());
        Assert.Equal([0x2300000Fu], range.Except(ids));

        var strings = 0;
        foreach (var id in ids)
        {
            Assert.True(Dats.TryGetLocal<StringTable>(id, out var table), $"0x{id:X8} does not decode");
            Assert.Equal(id, table.Id);
            Assert.Equal(1u, table.Language); // English
            Assert.NotEmpty(table.Strings);
            strings += table.Strings.Count;
        }

        Assert.Equal(6899, strings);

        // The frame-rate display's "FPS: ... DEG: ..." (m2-log-in.md §5.1) is in the UI table, with two variables.
        Assert.True(Dats.TryGetLocal<StringTable>(0x23000001u, out var ui));
        var fps = ui.Strings[0x0DCFFF73u];
        Assert.StartsWith("FPS", fps.Strings[0].Value, StringComparison.Ordinal);
        Assert.Equal(2, fps.Variables.Count);
    }

    [RequiresDatsFact]
    public void Master_property_table_decodes()
    {
        Assert.Equal([0x39000001u], Dats.GetAllPortalIds<MasterProperty>());
        Assert.True(Dats.TryGetPortal<MasterProperty>(0x39000001u, out var master));
        Assert.Equal(383, master.Properties.Count);

        // The instance BaseProperty.UnpackGeneric uses when TryGetLocal unpacks with a portal-bound reader.
        var cached = Dats.Read(d => d.Portal.MasterProperty);
        Assert.NotNull(cached);
        Assert.Equal(master.Properties.Keys.Order(), cached.Properties.Keys.Order());
    }

    [RequiresLocalDatsFact]
    public void Local_and_texture_reads_in_parallel_are_consistent()
    {
        var layouts = Dats.GetAllLocalIds<LayoutDesc>();
        var cursors = Dats.EnumMaps.Entries(EnumGroup.UiCursor).Values.ToArray(); // RenderSurfaces in the portal
        Assert.Equal(41, cursors.Length);
        var loader = new TextureLoader(Dats);

        string Layout(uint id) =>
            Dats.TryGetLocal<LayoutDesc>(id, out var l) ? $"{l.Id:X8} {l.Width}x{l.Height} {AllElements(l.Elements.Values).Count()}" : "missing";

        string Cursor(uint id)
        {
            var texture = loader.DecodeRenderSurface(id, TextureDecodeMode.Rgba8);
            return $"{texture.Width}x{texture.Height} {texture.Data.Aggregate(17L, (h, b) => (h * 31) + b)}";
        }

        var work = Enumerable.Range(0, 4 * (layouts.Count + cursors.Length))
            .Select(i => i % 2 == 0 ? (Local: true, Id: layouts[i / 2 % layouts.Count]) : (Local: false, Id: cursors[i / 2 % cursors.Length]))
            .ToArray();
        var expected = work.Select(w => w.Local ? Layout(w.Id) : Cursor(w.Id)).ToArray();
        var actual = new string[work.Length];
        Parallel.For(0, work.Length, i => actual[i] = work[i].Local ? Layout(work[i].Id) : Cursor(work[i].Id));
        Assert.Equal(expected, actual);
    }

    [RequiresDatsFact] // needs only portal and cell: the machine without the language dat is the case it models
    public void Without_the_language_dat_only_local_reads_fail()
    {
        // The same real files, opened without the language dat: no dat is copied or moved.
        var dir = Dats.Directory;
        var highRes = Path.Combine(dir, DatSet.HighResFileName);
        using var dats = DatSet.Open(new DatSetPaths(
            dir,
            Path.Combine(dir, DatSet.PortalFileName),
            Path.Combine(dir, DatSet.CellFileName),
            Local: null,
            HighRes: File.Exists(highRes) ? highRes : null));

        Assert.False(dats.HasLocal);
        Assert.Null(dats.Local);
        Assert.Null(dats.Iterations.Local);
        Assert.Contains("-lx-", dats.Iterations.CacheKey, StringComparison.Ordinal);
        Assert.False(dats.TryGetLocal<LayoutDesc>(0x2100000Fu, out var layout));
        Assert.Null(layout);
        Assert.Null(dats.TryGetLocalBytes(0x2100000Fu));
        Assert.Empty(dats.GetAllLocalIds<StringTable>());

        var error = Assert.Throws<InvalidOperationException>(() => dats.RequireLocal("The retail interface"));
        Assert.Contains("The retail interface", error.Message, StringComparison.Ordinal);
        Assert.Contains(DatSet.LocalFileName, error.Message, StringComparison.Ordinal);
        Assert.Contains(dir, error.Message, StringComparison.Ordinal);
        Assert.Contains(DatSet.DirectoryEnvironmentVariable, error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => dats.RequireLocal(" "));

        // Everything else still reads: portal, cell, the enum maps and the master property table.
        Assert.Equal("Dereth", dats.Region.RegionName.Value);
        Assert.NotNull(dats.GetLandBlock(LandblockCoord.Holtburg));
        Assert.Equal(0x02000306u, dats.EnumMaps.Resolve(EnumGroup.UiAsset, 0x10000001u));
        Assert.Equal(0x21000004u, dats.EnumMaps.Resolve(EnumGroup.UiLayout, 0x10000005u)); // names a layout it cannot read
        Assert.True(dats.TryGetPortal<MasterProperty>(0x39000001u, out _));
    }

    [RequiresDatsFact]
    public void A_dat_of_another_type_is_invalid_data_naming_the_file()
    {
        // The real portal dat, opened read-only in the language dat's place (nothing copied): the header check that
        // DatReaderWriter reports as ArgumentException surfaces as the InvalidDataException callers treat as bad dats.
        var dir = Dats.Directory;
        var portal = Path.Combine(dir, DatSet.PortalFileName);
        var cell = Path.Combine(dir, DatSet.CellFileName);

        var error = Assert.Throws<InvalidDataException>(() => DatSet.Open(new DatSetPaths(dir, portal, cell, Local: portal, HighRes: null)));
        Assert.Contains($"'{portal}' is not a retail language dat", error.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentException>(error.InnerException);

        error = Assert.Throws<InvalidDataException>(() => DatSet.Open(new DatSetPaths(dir, portal, cell, Local: null, HighRes: cell)));
        Assert.Contains($"'{cell}' is not a retail high-res (portal-type) dat", error.Message, StringComparison.Ordinal);
    }

    private static IEnumerable<ElementDesc> AllElements(IEnumerable<ElementDesc> elements) =>
        elements.SelectMany(e => AllElements(e.Children.Values).Prepend(e));

    private static IEnumerable<StateDesc> AllStates(ElementDesc element) => element.States.Values.Prepend(element.StateDesc);
}
