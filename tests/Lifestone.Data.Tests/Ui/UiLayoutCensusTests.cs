using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;
using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// The census of the 101 retail layouts in the Lifestone desc model (m2-log-in.md §5.1, m2-plan.md R1): every layout
/// converts, the counts match, the decoder traps DatReaderWriter has (StringInfo, MediaType.Stretch) are absent, and an
/// independent walker of the raw bytes (<see cref="RawLayoutWalker"/>) agrees with DatReaderWriter's reading. The
/// numbers are what a reader of the test log needs to trust the decode; nothing dat-derived is written anywhere.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class UiLayoutCensusTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_layout_converts_with_the_m2_counts()
    {
        var layouts = ConvertAll();

        Assert.Equal(101, layouts.Count);
        Assert.Equal(372, layouts.Sum(l => l.Elements.Count));
        Assert.Equal(2162, layouts.Sum(l => l.AllElements().Count()));
        Assert.All(layouts, l => Assert.All(l.AllElements(), e => Assert.Equal(l.Id, e.SourceLayout)));

        // 98 layouts are laid out for 800x600; the three journal-style books for 800x500.
        Assert.All(layouts, l => Assert.Equal(800, l.Width));
        Assert.Equal(98, layouts.Count(l => l.Height == 600));
        Assert.Equal([0x21000066u, 0x21000067u, 0x21000069u], layouts.Where(l => l.Height == 500).Select(l => l.Id).Order());
        Assert.Equal(
            ["classic_contracts", "classic_journal", "classic_pagelist"],
            layouts.Where(l => l.Height == 500).Select(l => LayoutName(l.Id)).Order(StringComparer.Ordinal));
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Element_classes_edges_and_flags_census()
    {
        var elements = ConvertAll().SelectMany(l => l.AllElements()).ToArray();

        // 1,088 partial descs (type 0) inherit from a base; every one names both its base layout and its base element.
        var partial = elements.Where(e => e.Type == 0).ToArray();
        Assert.Equal(1088, partial.Length);
        Assert.All(partial, e => Assert.True(e.BaseLayout != 0 && e.BaseElement != 0, e.Describe()));
        Assert.All(elements.Where(e => e.Type != 0), e => Assert.Equal((0u, 0u), (e.BaseLayout, e.BaseElement)));
        Assert.Equal(695, elements.Count(e => e.Type == 3)); // Field: the container class most layouts are built from
        Assert.Equal(100, elements.Select(e => e.Type).Where(t => t != 0).Distinct().Count());

        // Every edge mode 0-4 is used on the left and right; top and bottom never use Proportional.
        string Census(Func<UiEdges, UiEdge> side) =>
            string.Join(" ", elements.GroupBy(e => side(e.Edges)).OrderBy(g => g.Key).Select(g => $"{(uint)g.Key}:{g.Count()}"));
        Assert.Equal("0:774 1:1106 2:232 3:42 4:8", Census(e => e.Left));
        Assert.Equal("0:779 1:1088 2:285 3:10", Census(e => e.Top));
        Assert.Equal("0:774 1:923 2:415 3:42 4:8", Census(e => e.Right));
        Assert.Equal("0:779 1:898 2:475 3:10", Census(e => e.Bottom));

        // The element's own incorporation flags; the states of the state tables never carry position flags.
        Assert.Equal(
            "0x0:62 0x18:10 0x1E:1656 0x1F:139 0x38:5 0x3E:256 0x3F:34",
            string.Join(" ", elements.GroupBy(e => (uint)e.BaseState.Incorporation).OrderBy(g => g.Key).Select(g => $"0x{g.Key:X}:{g.Count()}")));
        var states = elements.SelectMany(e => e.States.Values).ToArray();
        Assert.Equal(3304 - 2162, states.Length);
        Assert.All(states, s => Assert.Equal(UiIncorporation.None, s.Incorporation & ~UiIncorporation.PassToChildren));
        Assert.All(states, s => Assert.Equal((0, 0, 0, 0, 0u), (s.X, s.Y, s.Width, s.Height, s.ZLevel)));
        Assert.All(elements, e => Assert.Equal(0u, e.BaseState.StateId));
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Media_census_has_no_undecodable_entry()
    {
        var media = AllStates().SelectMany(s => s.Media).ToArray();

        // m2-log-in.md §5.1: 1,310 images and 101 cursors, a few of everything else, and no Stretch (it would have thrown).
        Assert.Equal(
            "Movie:2 Alpha:3 Animation:2 Cursor:101 Image:1310 Jump:2 Message:6 Pause:13 Sound:3 State:3 Fade:6",
            string.Join(" ", media.GroupBy(m => m.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}")));
        var images = media.OfType<UiImageMedia>().ToArray();
        Assert.Equal(842, images.Count(i => i.DrawMode == UiDrawMode.Normal));
        Assert.Equal(468, images.Count(i => i.DrawMode == UiDrawMode.AlphaBlend));
        Assert.Equal(624, images.Select(i => i.File).Distinct().Count()); // R7 resolves them (623 of 624)
        Assert.All(media.OfType<UiAnimationMedia>(), a => Assert.Equal((UiDrawMode.Normal, 15), (a.DrawMode, a.Frames.Count)));
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_string_reference_is_a_table_reference_that_resolves()
    {
        var strings = AllProperties().OfType<UiStringInfoProperty>().Select(p => p.Value).ToArray();

        // All 465 have override 0, no strings and no variables (anything else throws in the converter).
        Assert.Equal(465, strings.Length);
        Assert.All(strings, s => Assert.False(s.IsLiteral));
        Assert.Equal(
            "0x23000001:303 0x23000002:162",
            string.Join(" ", strings.GroupBy(s => s.TableId).OrderBy(g => g.Key).Select(g => $"0x{g.Key:X8}:{g.Count()}")));
        foreach (var table in strings.Select(s => s.TableId).Distinct())
        {
            Assert.True(Dats.TryGetLocal<StringTable>(table, out var record), $"StringTable 0x{table:X8}");
            Assert.All(strings.Where(s => s.TableId == table), s => Assert.True(record.Strings.ContainsKey(s.StringId), s.ToString()));
        }
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Property_census_by_type()
    {
        var topLevel = AllStates().SelectMany(s => s.Properties.Values).ToArray();

        Assert.Equal(
            "Bool:1191 Integer:647 Float:37 Color:9 StringInfo:465 Enum:1158 DataId:223 Array:424 Struct:7",
            string.Join(" ", topLevel.GroupBy(p => p.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}")));
        Assert.Equal(155, topLevel.Select(p => p.Id).Distinct().Count()); // m2-log-in.md §5.1: 155 kinds

        // With array items and struct members (fonts, colours, list templates, panel pages): no StringInfo is nested.
        Assert.Equal(
            "Bool:1221 Integer:661 Float:37 Color:311 StringInfo:465 Enum:1304 DataId:502 Array:424 Struct:108",
            string.Join(" ", AllProperties().GroupBy(p => p.Type).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}")));
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void The_raw_walker_agrees_with_DatReaderWriter_on_every_layout()
    {
        Assert.True(Dats.TryGetPortal<MasterProperty>(0x39000001u, out var master));
        var types = master.Properties.ToDictionary(kv => kv.Key, kv => (UiPropertyType)(uint)kv.Value.Type);

        foreach (var id in Dats.GetAllLocalIds<LayoutDesc>())
        {
            var raw = RawLayoutWalker.Walk(Dats.TryGetLocalBytes(id)!, types);
            var layout = Convert(id);
            Assert.Equal((id, 800u), (raw.Id, raw.Width));
            Assert.Equal((uint)layout.Height, raw.Height);
            AssertSameElements($"0x{id:X8}", raw.Elements, layout.Elements.Values.ToList());
        }
    }

    private static void AssertSameElements(string where, List<RawElement> raw, List<UiElementDesc> converted)
    {
        Assert.Equal(raw.Select(e => e.ElementId), converted.Select(e => e.ElementId));
        for (var i = 0; i < raw.Count; i++)
        {
            var (r, c) = (raw[i], converted[i]);
            var at = $"{where} > 0x{r.ElementId:X8}";
            Assert.Equal((r.Type, r.BaseElement, r.BaseLayout, r.DefaultState, r.ReadOrder), (c.Type, c.BaseElement, c.BaseLayout, c.DefaultState, c.ReadOrder));
            Assert.Equal(r.Edges, new[] { (uint)c.Edges.Left, (uint)c.Edges.Top, (uint)c.Edges.Right, (uint)c.Edges.Bottom });
            var position = new long[] { c.BaseState.X, c.BaseState.Y, c.BaseState.Width, c.BaseState.Height, c.BaseState.ZLevel };
            for (var p = 0; p < 5; p++)
            {
                Assert.True((r.Position[p] ?? 0u) == unchecked((uint)position[p]), $"{at}: position field {p}");
            }

            AssertSameState(at, r.State, c.BaseState);
            Assert.Equal(r.States.Select(s => s.StateId), c.States.Keys);
            for (var s = 0; s < r.States.Count; s++)
            {
                AssertSameState($"{at} state {s}", r.States[s], c.States.Values.ElementAt(s));
            }

            AssertSameElements(at, r.Children, c.Children.Values.ToList());
        }
    }

    private static void AssertSameState(string where, RawState raw, UiStateDesc converted)
    {
        Assert.True(raw.StateId == converted.StateId, where);
        Assert.Equal(raw.PassToChildren != 0, converted.PassToChildren);
        Assert.Equal(raw.Flags, (uint)converted.Incorporation);
        Assert.Equal(raw.Media, converted.Media.Select(m => (uint)m.Type));
        AssertSameProperties(where, raw.Properties, converted.Properties.Values.ToList());
    }

    private static void AssertSameProperties(string where, List<RawProperty> raw, IReadOnlyList<UiProperty> converted)
    {
        Assert.Equal(raw.Select(p => (p.Id, p.Type)), converted.Select(p => (p.Id, p.Type)));
        for (var i = 0; i < raw.Count; i++)
        {
            switch (converted[i])
            {
                case UiStringInfoProperty s:
                    Assert.Equal(raw[i].Strings, [(s.Value.StringId, s.Value.TableId)]);
                    break;
                case UiArrayProperty a:
                    AssertSameProperties($"{where} 0x{a.Id:X}", raw[i].Children, a.Items);
                    break;
                case UiStructProperty st:
                    AssertSameProperties($"{where} 0x{st.Id:X}", raw[i].Children, st.Members.Values.ToList());
                    break;
            }
        }
    }

    private IReadOnlyList<UiLayout> ConvertAll() => Dats.GetAllLocalIds<LayoutDesc>().Select(Convert).ToArray();

    private UiLayout Convert(uint id)
    {
        Assert.True(Dats.TryGetLocal<LayoutDesc>(id, out var layout), $"0x{id:X8}");
        return UiDescConverter.Convert(layout);
    }

    private IEnumerable<UiStateDesc> AllStates() =>
        ConvertAll().SelectMany(l => l.AllElements()).SelectMany(e => e.States.Values.Prepend(e.BaseState));

    /// <summary>Every property, nested array items and struct members included.</summary>
    private IEnumerable<UiProperty> AllProperties() => AllStates().SelectMany(s => s.Properties.Values).SelectMany(Flatten);

    private static IEnumerable<UiProperty> Flatten(UiProperty property) => property switch
    {
        UiArrayProperty a => a.Items.SelectMany(Flatten).Prepend(a),
        UiStructProperty s => s.Members.Values.SelectMany(Flatten).Prepend(s),
        _ => [property],
    };

    private string? LayoutName(uint layoutId) =>
        Dats.EnumMaps.Entries(EnumGroup.UiLayout).Where(kv => kv.Value == layoutId).Select(kv => Dats.EnumMaps.NameOf(EnumGroup.UiLayout, kv.Key)).SingleOrDefault();
}
