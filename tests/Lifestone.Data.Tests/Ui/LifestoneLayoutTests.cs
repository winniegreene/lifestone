using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// Lifestone's own layouts (m2-plan.md R9): the JSON reader, registration next to the retail layouts, resolution against
/// retail templates, per-field error reports, and the hygiene of layout files (ids, property names and Lifestone's own
/// text only).
/// </summary>
public sealed class LifestoneLayoutTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    /// <summary>A tools panel in the shape G7 will author: a frame, a title, a field, a list and two buttons on retail templates.</summary>
    private const string ToolsPanel = """
        {
          "format": "lifestone-layout/1",
          "id": "0x7F000001",
          "width": 800,
          "height": 600,
          "elements": [
            {
              "id": "0x7F000100", "type": 3, "x": 560, "y": 80, "width": 220, "height": 300, "zLevel": 10,
              "edges": { "left": "OppositeSide", "right": "OwnSide" },
              "properties": { "UICore_Element_activatable": true },
              "children": [
                { "id": "0x7F000101", "base": { "layout": "0x21000042", "element": "0x100002AF" },
                  "x": 0, "y": 0, "width": 220, "height": 300, "edges": { "right": "OwnSide", "bottom": "OwnSide" } },
                { "id": "0x7F000102", "base": { "layout": "0x21000040", "element": "0x100002CA" },
                  "x": 12, "y": 10, "width": 196, "height": 16,
                  "properties": { "UICore_Text_entry": { "literal": "Lifestone tools" } } },
                { "id": "0x7F000103", "base": { "layout": "0x21000040", "element": "0x100002CA" },
                  "x": 12, "y": 34, "width": 196, "height": 18,
                  "properties": { "UICore_Text_editable": true, "UICore_Text_max_chars": 64, "UICore_Text_one_line": true } },
                { "id": "0x7F000104", "base": { "layout": "0x21000040", "element": "0x100002C8" },
                  "x": 12, "y": 240, "width": 97, "height": 44,
                  "properties": { "UICore_Text_entry": { "literal": "Where am I" } } },
                { "id": "0x7F000105", "base": { "layout": "0x21000040", "element": "0x100002C8" },
                  "x": 111, "y": 240, "width": 97, "height": 44,
                  "properties": { "UICore_Text_entry": { "tableEnum": "0x10000002", "token": "ID_CharacterManagement_PleaseWait" } } },
                { "id": "0x7F000106", "type": 1, "x": 12, "y": 290, "width": 196, "height": 8,
                  "media": [ { "image": "0x06004D46", "drawMode": "AlphaBlend" } ],
                  "states": { "0x10000006": { "passToChildren": true, "properties": { "UICore_Element_hide": true } } } }
              ]
            }
          ]
        }
        """;

    private DatSet Dats => fixture.Dats;

    [RequiresLocalDatsFact]
    public void A_tools_panel_reads_registers_and_resolves_against_its_retail_templates()
    {
        var layouts = new UiLayouts(Dats);
        var tables = new UiStringTables(Dats);
        var reader = new LifestoneLayoutReader(MasterPropertyTable.Load(Dats), e => tables.TryResolveTableEnum(e, out var did) ? did : null);
        var errors = new List<string>();

        var layout = reader.Read(ToolsPanel, errors);
        Assert.Empty(errors);
        Assert.NotNull(layout);
        layouts.Register(layout);
        Assert.Same(layout, layouts.GetLayout(0x7F000001u));
        Assert.Equal([0x7F000001u], layouts.RegisteredLayoutIds);

        var root = layouts.RequireFullDesc(layouts.AccessElementDesc(0x7F000001u, 0x7F000100u)!);
        Assert.Equal((3u, new UiBox(560, 80, 779, 379)), (root.Type, root.Box));
        Assert.Equal(new UiEdges(UiEdge.OppositeSide, UiEdge.Keep, UiEdge.OwnSide, UiEdge.Keep), root.Edges);
        Assert.Equal(6, root.Children.Count);

        // Every child resolves; the templated ones take their class and properties from retail and their box from us.
        var full = root.Children.Values.Select(layouts.RequireFullDesc).ToDictionary(d => d.ElementId);
        var frame = full[0x7F000101u];
        Assert.Equal((3u, 220, 300), (frame.Type, frame.Box.Width, frame.Box.Height));
        Assert.Equal(8, frame.Children.Count); // the dialog frame's pieces, re-anchored to the new size
        var title = full[0x7F000102u];
        Assert.Equal(1u, title.Type);
        Assert.True(title.Properties.ContainsKey(UiPropertyIds.TextFonts)); // inherited
        Assert.True(title.Properties.TryGet<UiStringInfoProperty>(UiPropertyIds.TextEntry, out var entry));
        Assert.Equal("Lifestone tools", entry.Value.Literal);
        Assert.True(full[0x7F000103u].Properties.GetBool(UiPropertyIds.TextEditable));
        Assert.Equal(64, full[0x7F000103u].Properties.GetInteger(UiPropertyIds.TextMaxChars));
        Assert.True(full[0x7F000105u].Properties.TryGet<UiStringInfoProperty>(UiPropertyIds.TextEntry, out var tableEntry));
        Assert.Equal(new UiStringRef(StringHash.Tokenize("ID_CharacterManagement_PleaseWait"), 0x23000002u), tableEntry.Value);
        Assert.Equal("Please Wait", new UiStrings(tables).GetString(tableEntry.Value));
        var bar = full[0x7F000106u];
        Assert.Equal(new UiImageMedia(0x06004D46u, UiDrawMode.AlphaBlend), Assert.Single(bar.Media));
        Assert.True(bar.States[0x10000006u].PassToChildren);
        Assert.True(bar.States[0x10000006u].Properties.GetBool(UiPropertyIds.ElementHide));
        Assert.Equal(UiIncorporation.X | UiIncorporation.Y | UiIncorporation.Width | UiIncorporation.Height, bar.BaseState.Incorporation);
    }

    [RequiresDatsFact]
    public void Malformed_values_are_reported_per_field_and_nothing_is_returned()
    {
        var properties = MasterPropertyTable.Load(Dats);
        var reader = new LifestoneLayoutReader(properties);
        var errors = new List<string>();
        var hoverDelay = properties.NameOf(UiPropertyIds.ElementHoverDelay);

        // Media numbers are checked, never coerced: one bad value per rule (1e999 overflows a double, 1e39 a float).
        var layout = reader.Read($$"""
            {
              "format": "lifestone-layout/2", "id": "0x21000005", "width": 0, "extra": 1,
              "elements": [
                { "id": 0, "type": 3, "base": { "layout": "0x21000040", "element": "0x100002CA" } },
                { "id": "0x7F000002", "type": "three", "x": "left", "edges": { "left": "Sideways" },
                  "properties": { "UICore_No_such": 1, "UICore_Text_editable": 1, "UICore_Text_font_color": "red",
                                  "UICore_Text_entry": { "literal": "a", "token": "b" }, "{{hoverDelay}}": 1e39 },
                  "media": [ { "image": "0x06000001", "drawMode": "Blend" }, { "movie": "intro.bik" },
                             { "cursor": "0x06004D68", "hotspot": [-5, 3] }, { "cursor": "0x06004D68", "hotspot": [3.7, 0] },
                             { "cursor": "0x06004D68", "hotspot": [1e999, 0] },
                             { "state": 6, "probability": "0.5" }, { "state": 6, "probability": 1.5 },
                             { "state": 6, "probability": -0.1 }, { "state": 6, "probability": 1e999 },
                             { "pause": [2, 1] }, { "pause": [-1, 1] }, { "pause": [0, 1e999] },
                             { "fade": [1, 0, -0.5] }, { "fade": [2, 0, 0.5] }, { "fade": [1, -1, 0.5] }, { "fade": [1, 0, 1e39] } ],
                  "states": { "zero": {} } },
                { "id": "0x7F000003", "type": 3 },
                { "id": "0x7F000003", "type": 3 }
              ]
            }
            """, errors);

        Assert.Null(layout);
        string[] expected =
        [
            "$.extra: unknown property",
            "$.format: expected \"lifestone-layout/1\"",
            "$.id: Lifestone layouts use 0x7F000000-0x7FFFFFFF",
            "$.width: expected a whole number in [1, 16384]",
            "$.elements[0].id: required, and not 0",
            "$.elements[0]: give a UI class \"type\" or a \"base\" template, not both and not neither",
            "$.elements[1].type: expected a number or \"0x...\"",
            "$.elements[1].x: expected a whole number",
            "$.elements[1].edges.left: expected Keep, OwnSide",
            "$.elements[1].properties.UICore_No_such: 'UICore_No_such' is not a property",
            "$.elements[1].properties.UICore_Text_editable: 'UICore_Text_editable' is a Bool property; expected true or false",
            "$.elements[1].properties.UICore_Text_font_color: 'UICore_Text_font_color' is a Color property; expected \"#AARRGGBB\"",
            "$.elements[1].properties.UICore_Text_entry.literal: a literal is a string and stands alone",
            $"$.elements[1].properties.{hoverDelay}: '{hoverDelay}' is a Float property; expected a number that is finite as a float",
            "$.elements[1].media[0].drawMode: expected Undefined, Normal, Overlay, AlphaBlend or 0-3",
            "$.elements[1].media[1]: expected one of image, alpha, cursor, sound, state, pause or fade",
            "$.elements[1].media[2].hotspot: expected 2 whole numbers, none negative",
            "$.elements[1].media[3].hotspot: expected 2 whole numbers, none negative",
            "$.elements[1].media[4].hotspot: expected 2 whole numbers, none negative",
            "$.elements[1].media[5].probability: expected a number in [0, 1]",
            "$.elements[1].media[6].probability: expected a number in [0, 1]",
            "$.elements[1].media[7].probability: expected a number in [0, 1]",
            "$.elements[1].media[8].probability: expected a number in [0, 1]",
            "$.elements[1].media[9].pause: expected [min, max] in seconds with 0 <= min <= max",
            "$.elements[1].media[10].pause: expected [min, max] in seconds with 0 <= min <= max",
            "$.elements[1].media[11].pause: expected 2 finite numbers",
            "$.elements[1].media[12].fade: expected [startAlpha, endAlpha, seconds] with alphas in [0, 1] and seconds >= 0",
            "$.elements[1].media[13].fade: expected [startAlpha, endAlpha, seconds] with alphas in [0, 1] and seconds >= 0",
            "$.elements[1].media[14].fade: expected [startAlpha, endAlpha, seconds] with alphas in [0, 1] and seconds >= 0",
            "$.elements[1].media[15].fade: expected 3 finite numbers",
            "$.elements[1].states.zero: a state id must be a non-zero number",
            "$.elements[3].id: 0x7F000003 is used by an earlier sibling",
        ];
        foreach (var e in expected)
        {
            Assert.Contains(errors, line => line.StartsWith(e, StringComparison.Ordinal));
        }

        // Each media entry above is wrong in one way only, so it gets one line.
        Assert.All(Enumerable.Range(2, 14), i => Assert.Single(errors, line => line.StartsWith($"$.elements[1].media[{i}].", StringComparison.Ordinal)));

        errors.Clear();
        Assert.Null(reader.Read("not json", errors));
        Assert.StartsWith("not valid JSON", Assert.Single(errors), StringComparison.Ordinal);
        errors.Clear();
        Assert.Null(reader.Read("[]", errors));
        Assert.Equal("$: a layout must be a JSON object", Assert.Single(errors));
    }

    [RequiresDatsFact]
    public void Embedded_data_and_strings_by_enum_without_tables_are_refused()
    {
        var reader = new LifestoneLayoutReader(MasterPropertyTable.Load(Dats));
        var errors = new List<string>();
        var blob = new string('A', 60);

        Assert.Null(reader.Read($$"""
            { "format": "lifestone-layout/1", "id": "0x7F000002", "elements": [
              { "id": 1, "type": 1, "properties": { "UICore_Text_entry": { "literal": "data:image/png;base64,{{blob}}" } } },
              { "id": 2, "type": 1, "properties": { "UICore_Text_entry": { "literal": "{{blob}}" } } },
              { "id": 3, "type": 1, "properties": { "UICore_Text_entry": { "tableEnum": "0x10000001", "token": "ID_Wait_PleaseWait" } } } ] }
            """, errors));
        Assert.Contains("$.elements[0].properties.UICore_Text_entry.literal: a layout holds ids and Lifestone's own text, not embedded data", errors);
        Assert.Contains("$.elements[1].properties.UICore_Text_entry.literal: a layout holds ids and Lifestone's own text, not embedded data", errors);
        Assert.Contains(errors, e => e.StartsWith("$.elements[2].properties.UICore_Text_entry.tableEnum: 0x10000001 does not resolve to a StringTable (the reader was given no string tables)", StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void Arrays_structs_numbers_and_hex_ids_read_by_the_master_type()
    {
        var reader = new LifestoneLayoutReader(MasterPropertyTable.Load(Dats));
        var errors = new List<string>();

        var layout = reader.Read("""
            { "format": "lifestone-layout/1", "id": "0x7F000003", "width": 800, "height": 500, "elements": [
              { "id": "0x7F000010", "type": 1, "readOrder": 2, "defaultState": "0x10000006",
                "properties": {
                  "UICore_Text_fonts": [ { "UICore_Text_font": "0x40000000" }, { "UICore_Text_font": 1073741826 } ],
                  "UICore_Text_font_color": "#FFC0B060",
                  "UICore_Text_horizontal_justification": 2 },
                "media": [ { "cursor": "0x06004D68", "hotspot": [3, 4] }, { "pause": [0.5, 1] }, { "fade": [1, 0, 0.25] },
                           { "sound": "0x2000004B", "soundType": 7 }, { "alpha": "0x06001234" }, { "state": 6 },
                           { "state": 7, "probability": 0.5 } ] } ] }
            """, errors);

        Assert.Empty(errors);
        Assert.NotNull(layout);
        Assert.Equal((800, 500), (layout.Width, layout.Height));
        var text = layout.Elements[0x7F000010u];
        Assert.Equal((2u, 0x10000006u, 0u), (text.ReadOrder, text.DefaultState, (uint)text.BaseState.Incorporation));
        Assert.True(text.Properties.TryGet<UiArrayProperty>(UiPropertyIds.TextFonts, out var fonts));
        Assert.Equal([0x40000000u, 0x40000002u], fonts.Items.Cast<UiDataIdProperty>().Select(f => f.Value));
        Assert.All(fonts.Items, f => Assert.Equal(UiPropertyIds.TextFont, f.Id));
        Assert.True(text.Properties.TryGet<UiColorProperty>(UiPropertyIds.TextFontColor, out var color));
        Assert.Equal(0xFFC0B060u, color.Argb);
        Assert.Equal(2u, text.Properties.GetEnum(UiPropertyIds.TextHorizontalJustification));
        Assert.Equal(
            [UiMediaType.Cursor, UiMediaType.Pause, UiMediaType.Fade, UiMediaType.Sound, UiMediaType.Alpha, UiMediaType.State, UiMediaType.State],
            text.Media.Select(m => m.Type));
        Assert.Equal(new UiCursorMedia(0x06004D68u, 3, 4), text.Media[0]);
        Assert.Equal(new UiPauseMedia(0.5f, 1f), text.Media[1]);
        Assert.Equal(new UiFadeMedia(1f, 0f, 0.25f), text.Media[2]);
        Assert.Equal(new UiStateMedia(6, 1f), text.Media[5]); // no probability: always
        Assert.Equal(new UiStateMedia(7, 0.5f), text.Media[6]);
    }

    [Fact]
    public void Register_takes_only_the_Lifestone_range_and_each_layout_once()
    {
        var layouts = UiLayouts.FromLayouts([]);
        var mine = new UiLayout(0x7F000009u, 800, 600, new Dictionary<uint, UiElementDesc>());

        layouts.Register(mine);
        layouts.Register(mine); // the same layout again is a no-op
        Assert.Same(mine, layouts.GetLayout(0x7F000009u));
        Assert.Throws<InvalidOperationException>(() => layouts.Register(mine with { Width = 640 }));
        Assert.Throws<ArgumentException>(() => layouts.Register(new UiLayout(0x21000099u, 800, 600, new Dictionary<uint, UiElementDesc>())));
    }

    [RequiresLocalDatsFact]
    public void Layout_files_hold_only_ids_property_names_and_Lifestones_own_text()
    {
        // G7 adds the files (wave 2); every file there must read, and no literal may be retail text.
        var folder = Path.Combine(RepositoryFiles.Root, "src", "Lifestone.Data", "Ui", "Layouts");
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json") : [];
        var tables = new UiStringTables(Dats);
        var retailText = RetailText(tables);
        var reader = new LifestoneLayoutReader(MasterPropertyTable.Load(Dats), e => tables.TryResolveTableEnum(e, out var did) ? did : null);
        foreach (var file in files)
        {
            var errors = new List<string>();
            var layout = reader.Read(File.ReadAllText(file), errors);
            Assert.True(errors.Count == 0, $"{Path.GetFileName(file)}: {string.Join("; ", errors)}");
            var retail = Literals(layout!).Where(retailText.Contains).ToArray();
            Assert.True(retail.Length == 0, $"{Path.GetFileName(file)} holds retail string text: {string.Join(" | ", retail)}");
        }
    }

    [RequiresLocalDatsFact]
    public void The_embedded_layout_files_register_on_the_first_lookup_and_every_element_resolves()
    {
        var layouts = new UiLayouts(Dats);
        Assert.Contains(LifestoneLayoutFiles.ReadAll(), f => f.Name == "tools.json");
        Assert.Empty(layouts.RegisteredLayoutIds); // nothing registered before a lookup in the Lifestone range

        var tools = layouts.GetLayout(0x7F000010u); // G7 lane A's tools panel (src/Lifestone.Data/Ui/Layouts/tools.json)
        Assert.Contains(0x7F000010u, layouts.RegisteredLayoutIds);
        Assert.Same(tools, layouts.GetLayout(0x7F000010u));
        foreach (var element in tools.AllElements())
        {
            var resolved = layouts.InqFullDesc(element);
            Assert.True(resolved.Full is not null, resolved.Failure);
        }

        Assert.False(layouts.TryGetLayout(0x7F0FFFFFu, out _)); // a DID in the range that no file holds is absent, not an error
        Assert.NotNull(layouts.AccessElementDesc(0x7F000010u, 0x7F000100u));
    }

    [RequiresLocalDatsFact]
    public void The_layout_hygiene_walk_finds_literals_nested_in_arrays_and_structs()
    {
        // The reader takes a StringInfo anywhere a property goes, so the check above must look inside arrays and structs.
        var properties = MasterPropertyTable.Load(Dats);
        var tables = new UiStringTables(Dats);
        var reader = new LifestoneLayoutReader(properties);
        var menu = properties.NameOf(UiPropertyIds.DialogMenuItemArray);
        var item = properties.NameOf(UiPropertyIds.DialogMenuItemText);
        var page = properties.NameOf(UiPropertyIds.PanelPageData);
        var errors = new List<string>();

        var layout = reader.Read($$"""
            { "format": "lifestone-layout/1", "id": "0x7F000004", "elements": [
              { "id": 1, "type": 1, "properties": {
                  "{{menu}}": [ { "{{item}}": { "literal": "Lifestone menu item" } }, { "{{item}}": { "literal": "Please Wait" } } ],
                  "{{page}}": { "{{menu}}": [ { "{{item}}": { "literal": "Character Information" } } ] } } } ] }
            """, errors);

        Assert.Empty(errors);
        Assert.NotNull(layout);
        Assert.Empty(layout.Elements[1].BaseState.Properties.Values.OfType<UiStringInfoProperty>()); // none at the top
        Assert.Equal(["Lifestone menu item", "Please Wait", "Character Information"], Literals(layout));
        Assert.Equal(["Please Wait", "Character Information"], Literals(layout).Where(RetailText(tables).Contains));
    }

    /// <summary>Every retail string of at least four characters, as the string tables give it.</summary>
    private static HashSet<string> RetailText(UiStringTables tables) =>
        tables.TableIds.Select(tables.GetTable)
            .SelectMany(t => t.StringIds.Select(id => { t.TryGetEntry(id, out var e); return string.Concat(e!.Parts); }))
            .Where(s => s.Length >= 4)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Every literal string in a layout: on every state of every element, inside arrays and structs too.</summary>
    private static List<string> Literals(UiLayout layout)
    {
        var literals = new List<string>();
        foreach (var state in layout.AllElements().SelectMany(e => e.States.Values.Prepend(e.BaseState)))
        {
            Collect(state.Properties.Values);
        }

        return literals;

        void Collect(IEnumerable<UiProperty> properties)
        {
            foreach (var property in properties)
            {
                switch (property)
                {
                    case UiStringInfoProperty { Value.Literal: { } literal }:
                        literals.Add(literal);
                        break;
                    case UiArrayProperty array:
                        Collect(array.Items);
                        break;
                    case UiStructProperty structure:
                        Collect(structure.Members.Values);
                        break;
                }
            }
        }
    }

    private static string Panel(string did, string element) =>
        "{ \"format\": \"lifestone-layout/1\", \"id\": \"" + did + "\", \"width\": 800, \"height\": 600, \"elements\": [ { \"id\": \"" + element + "\", \"type\": 3, \"x\": 0, \"y\": 0, \"width\": 10, \"height\": 10 } ] }";

    [RequiresLocalDatsFact]
    public void Two_embedded_files_with_one_DID_fail_loudly_naming_both_files_on_every_lookup()
    {
        var logged = new List<string>();
        var layouts = new UiLayouts(Dats)
        {
            ErrorLog = logged.Add,
            EmbeddedFiles = () => [("a.json", Panel("0x7F0000A1", "0x7F0001A1")), ("b.json", Panel("0x7F0000A1", "0x7F0001A2")), ("c.json", Panel("0x7F0000A3", "0x7F0001A3"))],
        };

        var first = Assert.Throws<InvalidDataException>(() => layouts.TryGetLayout(0x7F0000A3u, out _));
        Assert.Contains("TODO(g7-layouts)", first.Message, StringComparison.Ordinal);
        Assert.Contains("b.json", first.Message, StringComparison.Ordinal);
        Assert.Contains("a.json", first.Message, StringComparison.Ordinal);
        Assert.Contains("0x7F0000A1", first.Message, StringComparison.Ordinal);

        // Not silently "absent" afterwards: every later lookup in the range fails the same way, and the error was logged once.
        var later = Assert.Throws<InvalidDataException>(() => layouts.TryGetLayout(0x7F0000FFu, out _));
        Assert.Equal(first.Message, later.Message);
        Assert.Equal([first.Message], logged);
    }

    [RequiresLocalDatsFact]
    public void An_embedded_file_that_throws_or_does_not_read_fails_every_lookup_naming_the_cause()
    {
        var throwing = new UiLayouts(Dats) { EmbeddedFiles = () => throw new InvalidDataException("TODO(g7-layouts): embedded layout x.json cannot be opened.") };
        var first = Assert.Throws<InvalidDataException>(() => throwing.TryGetLayout(0x7F000010u, out _));
        Assert.Contains("x.json cannot be opened", first.Message, StringComparison.Ordinal);
        Assert.Equal(first.Message, Assert.Throws<InvalidDataException>(() => throwing.TryGetLayout(0x7F000010u, out _)).Message);

        var broken = new UiLayouts(Dats) { EmbeddedFiles = () => [("bad.json", "{ not json")] };
        var read = Assert.Throws<InvalidDataException>(() => broken.TryGetLayout(0x7F000010u, out _));
        Assert.Contains("bad.json", read.Message, StringComparison.Ordinal);
        Assert.Equal(read.Message, Assert.Throws<InvalidDataException>(() => broken.TryGetLayout(0x7F000011u, out _)).Message);
    }

    [RequiresLocalDatsFact]
    public void Distinct_embedded_files_register_and_a_test_registered_layout_wins_over_an_embedded_one()
    {
        var layouts = new UiLayouts(Dats) { EmbeddedFiles = () => [("a.json", Panel("0x7F0000A1", "0x7F0001A1")), ("b.json", Panel("0x7F0000A2", "0x7F0001A2"))] };
        Assert.True(layouts.TryGetLayout(0x7F0000A2u, out var b));
        Assert.Equal(0x7F0000A2u, b.Id);
        Assert.Equal([0x7F0000A1u, 0x7F0000A2u], layouts.RegisteredLayoutIds);
    }

    [RequiresLocalDatsFact]
    public void Warming_the_embedded_layouts_registers_them_at_once_and_a_failure_is_logged_once_and_still_thrown_by_the_lookups()
    {
        var layouts = new UiLayouts(Dats) { EmbeddedFiles = () => [("a.json", Panel("0x7F0000A1", "0x7F0001A1"))] };
        Assert.True(layouts.WarmEmbeddedLayouts());
        Assert.Equal([0x7F0000A1u], layouts.RegisteredLayoutIds);

        var logged = new List<string>();
        var broken = new UiLayouts(Dats) { ErrorLog = logged.Add, EmbeddedFiles = () => [("bad.json", "{ not json")] };
        Assert.False(broken.WarmEmbeddedLayouts());
        Assert.Single(logged);
        Assert.Contains("bad.json", logged[0], StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => broken.TryGetLayout(0x7F000010u, out _));
        Assert.Single(logged);
    }
}
