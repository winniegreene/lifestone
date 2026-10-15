using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lifestone.Data.Ui;

/// <summary>
/// Reads Lifestone's own panel layouts (the tools panel, the automation inspector) from JSON into <see cref="UiLayout"/>s
/// that <see cref="UiLayouts.Register"/> adds next to the retail ones, so the same resolver, anchoring and runtime handle
/// both.
/// </summary>
/// <remarks>
/// <para>
/// <b>Retail ids only.</b> A layout names retail things by id: the classes and templates it builds on (an element gives
/// either a UI class <c>type</c>, or a <c>base</c> retail template it inherits from, which the resolver merges as
/// <c>LayoutDesc::InqFullDesc</c> does), properties by their master-property name, images and sounds by DID, strings by
/// table and token. The only text a file may hold is Lifestone's own (a string <c>literal</c>, retail's override 1), and
/// the reader refuses anything that looks like embedded data (a <c>data:</c> URI or a long base64 run). Layout DIDs lie
/// in <see cref="UiLayouts.LifestoneLayoutFirst"/>-<see cref="UiLayouts.LifestoneLayoutLast"/>.
/// </para>
/// <para>
/// <b>Format</b> (<c>lifestone-layout/1</c>). Ids and DIDs are numbers or <c>"0x..."</c> strings.
/// </para>
/// <code>
/// { "format": "lifestone-layout/1", "id": "0x7F000001", "width": 800, "height": 600, "elements": [ element, ... ] }
/// element: { "id", "type" | "base": { "layout", "element" }, "x", "y", "width", "height", "zLevel", "readOrder",
///            "defaultState", "edges": { "left", "top", "right", "bottom" }, "passToChildren", "properties": { name: value },
///            "media": [ media, ... ], "states": { "stateId": { "passToChildren", "properties", "media" } },
///            "children": [ element, ... ] }
/// media:   { "image": did, "drawMode": "Normal" | "AlphaBlend" | 0-3 } | { "alpha": did } | { "cursor": did, "hotspot": [x, y] }
///          | { "sound": did, "soundType": n } | { "state": id, "probability": p } | { "pause": [min, max] }
///          | { "fade": [startAlpha, endAlpha, seconds] }
/// value:   by the property's master type: Bool true/false; Integer and Float numbers; Enum, DataId, InstanceId,
///          Bitfield32/64 a number or "0x..."; Color "#AARRGGBB"; Vector [x, y, z]; StringInfo { "literal": text } or
///          { "table": did | "tableEnum": enum, "token": "ID_..." | "id": stringId }; Array [ { name: value }, ... ];
///          Struct { name: value, ... }
/// </code>
/// <para>
/// A position or size field sets its incorporation flag (<see cref="UiIncorporation"/>), so a partial desc overrides only
/// what it gives, as <c>StateDesc::Incorporate</c> merges it. Edges are <see cref="UiEdge"/> names or 0-4.
/// </para>
/// <para>
/// Media numbers are checked, not coerced: a hot spot is two whole numbers, none negative (absent, [0, 0]); a
/// probability is a number in [0, 1] (absent, 1); a pause is [min, max] seconds with 0 &lt;= min &lt;= max; a fade's
/// alphas are in [0, 1] and its seconds not negative. Every float (these, Float and Vector properties) must be finite as
/// a 4-byte float. The retail layouts' own media keep to these ranges (alphas 0 and 1, probabilities 1, pauses 0.1-5 s).
/// </para>
/// <para>
/// <b>Never throws for bad input</b> (the pattern of <c>Lifestone.Client.Cinematic.ShotList</c>): every problem is listed
/// with its JSON path and the result is null when there is any. A layout that reads still has to resolve: a base that
/// does not exist fails in the resolver with its usual message.
/// </para>
/// </remarks>
public sealed partial class LifestoneLayoutReader
{
    /// <summary>The format string a layout file starts with.</summary>
    public const string Format = "lifestone-layout/1";

    /// <summary>The longest literal string a layout may hold.</summary>
    public const int MaxLiteralLength = 256;

    private static readonly HashSet<string> TopKeys = ["format", "id", "width", "height", "elements"];

    private static readonly HashSet<string> ElementKeys =
    [
        "id", "type", "base", "x", "y", "width", "height", "zLevel", "readOrder", "defaultState", "edges", "passToChildren",
        "properties", "media", "states", "children",
    ];

    private static readonly HashSet<string> StateKeys = ["passToChildren", "properties", "media"];

    private readonly MasterPropertyTable _properties;
    private readonly Func<uint, uint?>? _resolveTableEnum;

    /// <summary>A reader that names properties through <paramref name="properties"/>.</summary>
    /// <param name="properties">The master property table (types every property by name).</param>
    /// <param name="resolveTableEnum">
    /// Resolves a STRINGTABLE enum to a table DID (<see cref="UiStringTables.TryResolveTableEnum"/>), for strings given by
    /// <c>tableEnum</c>; null when the caller has no string tables (such strings are then reported).
    /// </param>
    public LifestoneLayoutReader(MasterPropertyTable properties, Func<uint, uint?>? resolveTableEnum = null)
    {
        ArgumentNullException.ThrowIfNull(properties);
        _properties = properties;
        _resolveTableEnum = resolveTableEnum;
    }

    /// <summary>
    /// Reads a layout. Never throws for bad input: every problem is added to <paramref name="errors"/> with its JSON path,
    /// and the result is null when there is any.
    /// </summary>
    public UiLayout? Read(string json, List<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json ?? string.Empty, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            errors.Add($"not valid JSON: {ex.Message}");
            return null;
        }

        using (document)
        {
            var before = errors.Count;
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                errors.Add("$: a layout must be a JSON object");
                return null;
            }

            Unknown(root, TopKeys, "$", errors);
            if (String(root, "format", "$", errors) != Format)
            {
                errors.Add($"$.format: expected \"{Format}\"");
            }

            var id = Id(root, "id", "$", errors) ?? 0;
            if (id is < UiLayouts.LifestoneLayoutFirst or > UiLayouts.LifestoneLayoutLast)
            {
                errors.Add($"$.id: Lifestone layouts use 0x{UiLayouts.LifestoneLayoutFirst:X8}-0x{UiLayouts.LifestoneLayoutLast:X8}");
            }

            var width = Int(root, "width", "$", errors, 800, 1, 16384);
            var height = Int(root, "height", "$", errors, 600, 1, 16384);
            var elements = Elements(root, "elements", "$", id, errors, required: true);
            return errors.Count == before ? new UiLayout(id, width, height, elements) : null;
        }
    }

    private IReadOnlyDictionary<uint, UiElementDesc> Elements(JsonElement parent, string name, string path, uint layoutId, List<string> errors, bool required)
    {
        if (!parent.TryGetProperty(name, out var array))
        {
            if (required)
            {
                errors.Add($"{path}.{name}: expected a non-empty array of elements");
            }

            return UiOrdered.Dictionary<UiElementDesc>([], _ => new InvalidOperationException());
        }

        if (array.ValueKind != JsonValueKind.Array || (required && array.GetArrayLength() == 0))
        {
            errors.Add($"{path}.{name}: expected {(required ? "a non-empty " : "an ")}array of elements");
            return UiOrdered.Dictionary<UiElementDesc>([], _ => new InvalidOperationException());
        }

        var list = new List<(uint, UiElementDesc)>();
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            if (Element(item, $"{path}.{name}[{index}]", layoutId, errors) is { } element)
            {
                if (list.Any(e => e.Item1 == element.ElementId))
                {
                    errors.Add($"{path}.{name}[{index}].id: 0x{element.ElementId:X8} is used by an earlier sibling");
                }
                else
                {
                    list.Add((element.ElementId, element));
                }
            }

            index++;
        }

        return UiOrdered.Dictionary(list, _ => new InvalidOperationException());
    }

    private UiElementDesc? Element(JsonElement e, string path, uint layoutId, List<string> errors)
    {
        if (e.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}: expected an object");
            return null;
        }

        var before = errors.Count;
        Unknown(e, ElementKeys, path, errors);
        var id = Id(e, "id", path, errors);
        if (id is null or 0)
        {
            errors.Add($"{path}.id: required, and not 0");
        }

        var type = Id(e, "type", path, errors) ?? 0;
        uint baseLayout = 0;
        uint baseElement = 0;
        if (e.TryGetProperty("base", out var baseRef))
        {
            if (baseRef.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{path}.base: expected {{ \"layout\": did, \"element\": id }}");
            }
            else
            {
                Unknown(baseRef, ["layout", "element"], $"{path}.base", errors);
                baseLayout = Id(baseRef, "layout", $"{path}.base", errors) ?? 0;
                baseElement = Id(baseRef, "element", $"{path}.base", errors) ?? 0;
                if (baseLayout == 0 || baseElement == 0)
                {
                    errors.Add($"{path}.base: needs a layout and an element");
                }
            }
        }

        if ((type == 0) == (baseLayout == 0))
        {
            errors.Add($"{path}: give a UI class \"type\" or a \"base\" template, not both and not neither");
        }

        var incorporation = UiIncorporation.None;
        int Position(string name, UiIncorporation flag, int min)
        {
            if (!e.TryGetProperty(name, out _))
            {
                return 0;
            }

            incorporation |= flag;
            return Int(e, name, path, errors, 0, min, 16384);
        }

        var x = Position("x", UiIncorporation.X, -16384);
        var y = Position("y", UiIncorporation.Y, -16384);
        var width = Position("width", UiIncorporation.Width, 0);
        var height = Position("height", UiIncorporation.Height, 0);
        var zLevel = (uint)Position("zLevel", UiIncorporation.ZLevel, 0);
        var passToChildren = PassToChildren(e, path, errors, ref incorporation);
        var baseState = new UiStateDesc
        {
            StateId = 0,
            PassToChildren = passToChildren,
            Incorporation = incorporation,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            ZLevel = zLevel,
            Properties = Properties(e, "properties", path, errors),
            Media = Media(e, path, errors),
        };

        var states = new List<(uint, UiStateDesc)>();
        if (e.TryGetProperty("states", out var stateObject))
        {
            if (stateObject.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{path}.states: expected an object of states by id");
            }
            else
            {
                foreach (var state in stateObject.EnumerateObject())
                {
                    var statePath = $"{path}.states.{state.Name}";
                    if (!TryParseId(state.Name, out var stateId) || stateId == 0)
                    {
                        errors.Add($"{statePath}: a state id must be a non-zero number or \"0x...\"");
                        continue;
                    }

                    if (state.Value.ValueKind != JsonValueKind.Object)
                    {
                        errors.Add($"{statePath}: expected an object");
                        continue;
                    }

                    Unknown(state.Value, StateKeys, statePath, errors);
                    var stateIncorporation = UiIncorporation.None;
                    var statePass = PassToChildren(state.Value, statePath, errors, ref stateIncorporation);
                    if (states.Any(s => s.Item1 == stateId))
                    {
                        errors.Add($"{statePath}: the state 0x{stateId:X} is given twice");
                        continue;
                    }

                    states.Add((stateId, new UiStateDesc
                    {
                        StateId = stateId,
                        PassToChildren = statePass,
                        Incorporation = stateIncorporation,
                        Properties = Properties(state.Value, "properties", statePath, errors),
                        Media = Media(state.Value, statePath, errors),
                    }));
                }
            }
        }

        var element = new UiElementDesc
        {
            ElementId = id ?? 0,
            Type = type,
            BaseElement = baseElement,
            BaseLayout = baseLayout,
            DefaultState = Id(e, "defaultState", path, errors) ?? 0,
            ReadOrder = (uint)Int(e, "readOrder", path, errors, 0, 0, int.MaxValue),
            Edges = Edges(e, path, errors),
            BaseState = baseState,
            States = UiOrdered.Dictionary(states, _ => new InvalidOperationException()),
            Children = Elements(e, "children", path, layoutId, errors, required: false),
            SourceLayout = layoutId,
        };
        return errors.Count == before ? element : null;
    }

    private static bool PassToChildren(JsonElement e, string path, List<string> errors, ref UiIncorporation incorporation)
    {
        if (!e.TryGetProperty("passToChildren", out var value))
        {
            return false;
        }

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            errors.Add($"{path}.passToChildren: expected true or false");
            return false;
        }

        incorporation |= UiIncorporation.PassToChildren;
        return value.GetBoolean();
    }

    private static UiEdges Edges(JsonElement e, string path, List<string> errors)
    {
        if (!e.TryGetProperty("edges", out var edges))
        {
            return UiEdges.None;
        }

        if (edges.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}.edges: expected {{ \"left\", \"top\", \"right\", \"bottom\" }}");
            return UiEdges.None;
        }

        Unknown(edges, ["left", "top", "right", "bottom"], $"{path}.edges", errors);
        UiEdge Side(string name)
        {
            if (!edges.TryGetProperty(name, out var v))
            {
                return UiEdge.Keep;
            }

            if (v.ValueKind == JsonValueKind.String && Enum.TryParse<UiEdge>(v.GetString(), ignoreCase: false, out var named) && Enum.IsDefined(named))
            {
                return named;
            }

            if (v.ValueKind == JsonValueKind.Number && v.TryGetUInt32(out var n) && n <= (uint)UiEdge.Proportional)
            {
                return (UiEdge)n;
            }

            errors.Add($"{path}.edges.{name}: expected Keep, OwnSide, OppositeSide, Centre, Proportional or 0-4");
            return UiEdge.Keep;
        }

        return new UiEdges(Side("left"), Side("top"), Side("right"), Side("bottom"));
    }

    private IReadOnlyDictionary<uint, UiProperty> Properties(JsonElement e, string name, string path, List<string> errors)
    {
        var list = new List<(uint, UiProperty)>();
        if (e.TryGetProperty(name, out var properties))
        {
            if (properties.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{path}.{name}: expected an object of properties by master-property name");
            }
            else
            {
                foreach (var p in properties.EnumerateObject())
                {
                    if (Property(p.Name, p.Value, $"{path}.{name}.{p.Name}", errors) is { } property)
                    {
                        list.Add((property.Id, property));
                    }
                }
            }
        }

        return UiOrdered.Dictionary(list, _ => new InvalidOperationException());
    }

    private UiProperty? Property(string name, JsonElement value, string path, List<string> errors)
    {
        if (!_properties.TryFindByName(name, out var id) || !_properties.TryGet(id, out var desc))
        {
            errors.Add($"{path}: '{name}' is not a property of the master property table 0x{MasterPropertyTable.DataId:X8}");
            return null;
        }

        switch (desc.Type)
        {
            case UiPropertyType.Bool when value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                return new UiBoolProperty(id, value.GetBoolean());
            case UiPropertyType.Integer when value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var integer):
                return new UiIntegerProperty(id, integer);
            case UiPropertyType.Float when Finite(value, out var real):
                return new UiFloatProperty(id, real);
            case UiPropertyType.Enum when TryUInt(value, out var e):
                return new UiEnumProperty(id, e);
            case UiPropertyType.DataId when TryUInt(value, out var did):
                return new UiDataIdProperty(id, did);
            case UiPropertyType.InstanceId when TryUInt(value, out var instance):
                return new UiInstanceIdProperty(id, instance);
            case UiPropertyType.Bitfield32 when TryUInt(value, out var bits):
                return new UiBitfield32Property(id, bits);
            case UiPropertyType.Bitfield64 when TryULong(value, out var bits64):
                return new UiBitfield64Property(id, bits64);
            case UiPropertyType.Color when value.ValueKind == JsonValueKind.String && TryColor(value.GetString()!, out var argb):
                return new UiColorProperty(id, argb);
            case UiPropertyType.Vector when value.ValueKind == JsonValueKind.Array && value.GetArrayLength() == 3
                && value.EnumerateArray().All(v => Finite(v, out _)):
                var c = value.EnumerateArray().Select(v => Finite(v, out var f) ? f : 0f).ToArray();
                return new UiVectorProperty(id, new Vector3(c[0], c[1], c[2]));
            case UiPropertyType.StringInfo:
                return StringInfo(value, path, errors) is { } reference ? new UiStringInfoProperty(id, reference) : null;
            case UiPropertyType.Array when value.ValueKind == JsonValueKind.Array:
                var items = new List<UiProperty>();
                var index = 0;
                foreach (var item in value.EnumerateArray())
                {
                    var itemPath = $"{path}[{index++}]";
                    var members = item.ValueKind == JsonValueKind.Object ? item.EnumerateObject().ToArray() : [];
                    if (members.Length != 1)
                    {
                        errors.Add($"{itemPath}: an array item is {{ \"propertyName\": value }}");
                        continue;
                    }

                    if (Property(members[0].Name, members[0].Value, $"{itemPath}.{members[0].Name}", errors) is { } p)
                    {
                        items.Add(p);
                    }
                }

                return new UiArrayProperty(id, UiOrdered.List(items));
            case UiPropertyType.Struct when value.ValueKind == JsonValueKind.Object:
                var memberList = new List<(uint, UiProperty)>();
                foreach (var member in value.EnumerateObject())
                {
                    if (Property(member.Name, member.Value, $"{path}.{member.Name}", errors) is { } p)
                    {
                        memberList.Add((p.Id, p));
                    }
                }

                return new UiStructProperty(id, UiOrdered.Dictionary(memberList, _ => new InvalidOperationException()));
            default:
                errors.Add($"{path}: '{name}' is a {desc.Type} property; {Expected(desc.Type)}");
                return null;
        }
    }

    private UiStringRef? StringInfo(JsonElement value, string path, List<string> errors)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{path}: a string is {{ \"literal\": text }} or {{ \"table\" | \"tableEnum\", \"token\" | \"id\" }}");
            return null;
        }

        Unknown(value, ["literal", "table", "tableEnum", "token", "id"], path, errors);
        if (value.TryGetProperty("literal", out var literal))
        {
            if (value.EnumerateObject().Count() != 1 || literal.ValueKind != JsonValueKind.String)
            {
                errors.Add($"{path}.literal: a literal is a string and stands alone");
                return null;
            }

            var text = literal.GetString()!;
            return CheckLiteral(text, $"{path}.literal", errors) ? UiStringRef.FromLiteral(text) : null;
        }

        var table = Id(value, "table", path, errors);
        var tableEnum = Id(value, "tableEnum", path, errors);
        if ((table is null) == (tableEnum is null))
        {
            errors.Add($"{path}: give a \"table\" DID or a \"tableEnum\", not both and not neither");
            return null;
        }

        if (tableEnum is { } e)
        {
            table = _resolveTableEnum is null ? null : _resolveTableEnum(e);
            if (table is null)
            {
                errors.Add($"{path}.tableEnum: 0x{e:X8} does not resolve to a StringTable{(_resolveTableEnum is null ? " (the reader was given no string tables)" : string.Empty)}");
                return null;
            }
        }

        var token = String(value, "token", path, errors);
        var stringId = Id(value, "id", path, errors);
        if ((token is null) == (stringId is null))
        {
            errors.Add($"{path}: give a \"token\" or an \"id\", not both and not neither");
            return null;
        }

        if (token is not null && (token.Length > 128 || token.Any(ch => ch > '\u007F')))
        {
            errors.Add($"{path}.token: a token is an ASCII name such as ID_...");
            return null;
        }

        return new UiStringRef(stringId ?? StringHash.Tokenize(token), table!.Value);
    }

    private static IReadOnlyList<UiMedia> Media(JsonElement e, string path, List<string> errors)
    {
        if (!e.TryGetProperty("media", out var media))
        {
            return [];
        }

        if (media.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{path}.media: expected an array of media entries");
            return [];
        }

        var list = new List<UiMedia>();
        var index = 0;
        foreach (var m in media.EnumerateArray())
        {
            var mediaPath = $"{path}.media[{index++}]";
            if (m.ValueKind != JsonValueKind.Object)
            {
                errors.Add($"{mediaPath}: expected an object");
                continue;
            }

            if (MediaEntry(m, mediaPath, errors) is { } entry)
            {
                list.Add(entry);
            }
        }

        return UiOrdered.List(list);
    }

    private static UiMedia? MediaEntry(JsonElement m, string path, List<string> errors)
    {
        if (m.TryGetProperty("image", out _))
        {
            Unknown(m, ["image", "drawMode"], path, errors);
            var file = Id(m, "image", path, errors) ?? 0;
            return new UiImageMedia(file, DrawMode(m, path, errors));
        }

        if (m.TryGetProperty("alpha", out _))
        {
            Unknown(m, ["alpha"], path, errors);
            return new UiAlphaMedia(Id(m, "alpha", path, errors) ?? 0);
        }

        if (m.TryGetProperty("cursor", out _))
        {
            Unknown(m, ["cursor", "hotspot"], path, errors);
            var hotspot = WholeNumbers(m, "hotspot", path, errors, 2) ?? [0, 0];
            return new UiCursorMedia(Id(m, "cursor", path, errors) ?? 0, hotspot[0], hotspot[1]);
        }

        if (m.TryGetProperty("sound", out _))
        {
            Unknown(m, ["sound", "soundType"], path, errors);
            return new UiSoundMedia(Id(m, "sound", path, errors) ?? 0, Id(m, "soundType", path, errors) ?? 0);
        }

        if (m.TryGetProperty("state", out _))
        {
            Unknown(m, ["state", "probability"], path, errors);
            var probability = 1f;
            if (m.TryGetProperty("probability", out var p))
            {
                if (Finite(p, out var chance) && chance is >= 0 and <= 1)
                {
                    probability = chance;
                }
                else
                {
                    errors.Add($"{path}.probability: expected a number in [0, 1]");
                }
            }

            return new UiStateMedia(Id(m, "state", path, errors) ?? 0, probability);
        }

        if (m.TryGetProperty("pause", out _))
        {
            Unknown(m, ["pause"], path, errors);
            var pause = FiniteNumbers(m, "pause", path, errors, 2);
            if (pause is not null && (pause[0] < 0 || pause[1] < pause[0]))
            {
                errors.Add($"{path}.pause: expected [min, max] in seconds with 0 <= min <= max");
            }

            pause ??= [0, 0];
            return new UiPauseMedia(pause[0], pause[1]);
        }

        if (m.TryGetProperty("fade", out _))
        {
            Unknown(m, ["fade"], path, errors);
            var fade = FiniteNumbers(m, "fade", path, errors, 3);
            if (fade is not null && (fade[0] is < 0 or > 1 || fade[1] is < 0 or > 1 || fade[2] < 0))
            {
                errors.Add($"{path}.fade: expected [startAlpha, endAlpha, seconds] with alphas in [0, 1] and seconds >= 0");
            }

            fade ??= [1, 1, 0];
            return new UiFadeMedia(fade[0], fade[1], fade[2]);
        }

        errors.Add($"{path}: expected one of image, alpha, cursor, sound, state, pause or fade");
        return null;
    }

    private static UiDrawMode DrawMode(JsonElement m, string path, List<string> errors)
    {
        if (!m.TryGetProperty("drawMode", out var drawMode))
        {
            return UiDrawMode.Normal;
        }

        if (drawMode.ValueKind == JsonValueKind.String && Enum.TryParse<UiDrawMode>(drawMode.GetString(), ignoreCase: false, out var named) && Enum.IsDefined(named))
        {
            return named;
        }

        if (drawMode.ValueKind == JsonValueKind.Number && drawMode.TryGetUInt32(out var n) && n <= (uint)UiDrawMode.AlphaBlend)
        {
            return (UiDrawMode)n;
        }

        errors.Add($"{path}.drawMode: expected Undefined, Normal, Overlay, AlphaBlend or 0-3");
        return UiDrawMode.Normal;
    }

    private static bool CheckLiteral(string text, string path, List<string> errors)
    {
        if (text.Length > MaxLiteralLength)
        {
            errors.Add($"{path}: a literal is at most {MaxLiteralLength} characters");
            return false;
        }

        if (text.Contains("data:", StringComparison.OrdinalIgnoreCase) || Base64Run().IsMatch(text))
        {
            errors.Add($"{path}: a layout holds ids and Lifestone's own text, not embedded data");
            return false;
        }

        return true;
    }

    [GeneratedRegex("[A-Za-z0-9+/]{48,}={0,2}")]
    private static partial Regex Base64Run();

    private static string Expected(UiPropertyType type) => type switch
    {
        UiPropertyType.Bool => "expected true or false",
        UiPropertyType.Integer => "expected a 32-bit integer",
        UiPropertyType.Float => "expected a number that is finite as a float",
        UiPropertyType.Enum or UiPropertyType.DataId or UiPropertyType.InstanceId or UiPropertyType.Bitfield32 => "expected a number or \"0x...\"",
        UiPropertyType.Bitfield64 => "expected a number or \"0x...\" (64-bit)",
        UiPropertyType.Color => "expected \"#AARRGGBB\"",
        UiPropertyType.Vector => "expected [x, y, z], each finite as a float",
        UiPropertyType.Array => "expected an array of { name: value } items",
        UiPropertyType.Struct => "expected an object of { name: value } members",
        _ => "this reader does not write that type",
    };

    private static void Unknown(JsonElement e, IReadOnlyCollection<string> known, string path, List<string> errors)
    {
        foreach (var p in e.EnumerateObject())
        {
            if (!known.Contains(p.Name))
            {
                errors.Add($"{path}.{p.Name}: unknown property (known: {string.Join(", ", known)})");
            }
        }
    }

    private static string? String(JsonElement e, string name, string path, List<string> errors)
    {
        if (!e.TryGetProperty(name, out var v))
        {
            return null;
        }

        if (v.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{path}.{name}: expected a string");
            return null;
        }

        return v.GetString();
    }

    private static int Int(JsonElement e, string name, string path, List<string> errors, int fallback, int min, int max)
    {
        if (!e.TryGetProperty(name, out var v))
        {
            return fallback;
        }

        if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || n < min || n > max)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture, $"{path}.{name}: expected a whole number in [{min}, {max}]"));
            return fallback;
        }

        return n;
    }

    private static uint? Id(JsonElement e, string name, string path, List<string> errors)
    {
        if (!e.TryGetProperty(name, out var v))
        {
            return null;
        }

        if (TryUInt(v, out var id))
        {
            return id;
        }

        errors.Add($"{path}.{name}: expected a number or \"0x...\"");
        return null;
    }

    /// <summary><paramref name="count"/> numbers finite as floats; null when absent, or (reported) otherwise.</summary>
    private static float[]? FiniteNumbers(JsonElement e, string name, string path, List<string> errors, int count)
    {
        if (!e.TryGetProperty(name, out var v))
        {
            return null;
        }

        var values = new float[count];
        if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == count)
        {
            var i = 0;
            foreach (var x in v.EnumerateArray())
            {
                if (!Finite(x, out values[i++]))
                {
                    return Fail();
                }
            }

            return values;
        }

        return Fail();

        float[]? Fail()
        {
            errors.Add($"{path}.{name}: expected {count} finite numbers");
            return null;
        }
    }

    /// <summary><paramref name="count"/> whole numbers in [0, 2^32 - 1]; null when absent, or (reported) otherwise.</summary>
    private static uint[]? WholeNumbers(JsonElement e, string name, string path, List<string> errors, int count)
    {
        if (!e.TryGetProperty(name, out var v))
        {
            return null;
        }

        var values = new uint[count];
        if (v.ValueKind == JsonValueKind.Array && v.GetArrayLength() == count)
        {
            var i = 0;
            foreach (var x in v.EnumerateArray())
            {
                if (x.ValueKind != JsonValueKind.Number || !x.TryGetUInt32(out values[i++]))
                {
                    return Fail();
                }
            }

            return values;
        }

        return Fail();

        uint[]? Fail()
        {
            errors.Add($"{path}.{name}: expected {count} whole numbers, none negative");
            return null;
        }
    }

    /// <summary>A JSON number that is finite as a float (a 4-byte <c>float</c> is what retail stores).</summary>
    private static bool Finite(JsonElement v, out float value)
    {
        value = 0;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var real))
        {
            return false;
        }

        value = (float)real;
        return float.IsFinite(value);
    }

    private static bool TryUInt(JsonElement v, out uint value)
    {
        value = 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetUInt32(out value),
            JsonValueKind.String => TryParseId(v.GetString()!, out value),
            _ => false,
        };
    }

    private static bool TryULong(JsonElement v, out ulong value)
    {
        value = 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.TryGetUInt64(out value),
            JsonValueKind.String => v.GetString() is { } s && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && ulong.TryParse(s.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static bool TryParseId(string text, out uint value) =>
        text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value)
            : uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    private static bool TryColor(string text, out uint argb)
    {
        argb = 0;
        var digits = text.StartsWith('#') ? text[1..] : text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : null;
        return digits is { Length: 8 } && uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out argb);
    }
}
