using System.Numerics;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// <see cref="UiDescConverter"/> on synthetic DatReaderWriter records: every field and value type converts, and every
/// shape DatReaderWriter cannot read correctly fails loudly, naming where (m2-plan.md R1).
/// </summary>
public sealed class UiDescConverterTests
{
    private const uint LayoutId = 0x21000099u;

    [Fact]
    public void An_element_converts_field_for_field()
    {
        var child = Element(0x20, type: 0, flags: IncorporationFlags.X | IncorporationFlags.Y);
        child.BaseLayoutId = 0x2100003Fu;
        child.BaseElement = 0x10000371u;
        child.X = 15;
        child.Y = unchecked((uint)-3); // retail's m_y is signed
        child.LeftEdge = 1;
        child.TopEdge = 2;
        child.RightEdge = 3;
        child.BottomEdge = 4;

        var element = Element(0x10, type: 3, flags: IncorporationFlags.X | IncorporationFlags.Y | IncorporationFlags.Width | IncorporationFlags.Height | IncorporationFlags.ZLevel);
        (element.X, element.Y, element.Width, element.Height, element.ZLevel) = (1, 2, 300, 200, 7);
        element.ReadOrder = 5;
        element.DefaultState = (UIStateId)1;
        element.StateDesc.PassToChildren = true;
        element.StateDesc.Properties.Add(0x3B, new BoolBaseProperty { MasterPropertyId = 0x3B, Value = true });
        element.StateDesc.Media.Add(new MediaDescImage { Type = MediaType.Image, File = 0x06001234u, DrawMode = DrawModeType.Alphablend });
        element.States.Add((UIStateId)6, new StateDesc { StateId = 6, IncorporationFlags = IncorporationFlags.PassToChildren });
        element.Children.Add(0x20, child);

        var layout = UiDescConverter.Convert(Layout(element));

        Assert.Equal((LayoutId, 800, 600), (layout.Id, layout.Width, layout.Height));
        var e = Assert.Single(layout.Elements.Values);
        Assert.Same(e, layout.AccessElementDesc(0x10));
        Assert.Null(layout.AccessElementDesc(0x20)); // top level only, as LayoutDesc::AccessElementDesc
        Assert.Equal((0x10u, 3u, 0u, 0u, 1u, 5u, LayoutId), (e.ElementId, e.Type, e.BaseElement, e.BaseLayout, e.DefaultState, e.ReadOrder, e.SourceLayout));
        Assert.Equal(new UiBox(1, 2, 300, 201), e.Box);
        Assert.Equal(7u, e.BaseState.ZLevel);
        Assert.True(e.BaseState.PassToChildren);
        Assert.Equal((UiIncorporation)0x3E, e.BaseState.Incorporation);
        Assert.True(e.Properties.GetBool(0x3B));
        Assert.Equal(new UiImageMedia(0x06001234u, UiDrawMode.AlphaBlend), Assert.Single(e.Media));
        var state = Assert.Single(e.States).Value;
        Assert.Equal((6u, UiIncorporation.PassToChildren), (state.StateId, state.Incorporation));

        var c = Assert.Single(e.Children).Value;
        Assert.Equal((0x20u, 0u, 0x10000371u, 0x2100003Fu), (c.ElementId, c.Type, c.BaseElement, c.BaseLayout));
        Assert.Equal((15, -3, 0, 0), (c.BaseState.X, c.BaseState.Y, c.BaseState.Width, c.BaseState.Height));
        Assert.Equal(new UiEdges(UiEdge.OwnSide, UiEdge.OppositeSide, UiEdge.Centre, UiEdge.Proportional), c.Edges);
        Assert.Equal("1234", c.Edges.ToString());
        Assert.Equal([e, c], layout.AllElements());
    }

    [Fact]
    public void Every_property_type_converts()
    {
        var state = new StateDesc();
        void Add(uint id, BaseProperty p)
        {
            p.MasterPropertyId = id;
            state.Properties.Add(id, p);
        }

        Add(1, new BoolBaseProperty { Value = true });
        Add(2, new IntegerBaseProperty { Value = -5 });
        Add(3, new FloatBaseProperty { Value = 0.25f });
        Add(4, new VectorBaseProperty { Value = new Vector3(1, 2, 3) });
        Add(5, new ColorBaseProperty { Value = new ColorARGB { Alpha = 0x11, Red = 0x22, Green = 0x33, Blue = 0x44 } });
        Add(6, new StringInfoBaseProperty { Value = new StringInfo { StringId = 0x0DCFFF73u, TableId = new QualifiedDataId<StringTable> { DataId = 0x23000001u }, English = 1 } });
        Add(7, new EnumBaseProperty { Value = 0x10000001u });
        Add(8, new DataIdBaseProperty { Value = 0x06000001u });
        Add(9, new InstanceIdBaseProperty { Value = 0x50000001u });
        Add(10, new Bitfield32BaseProperty { Value = 0xF0u });
        Add(11, new Bitfield64BaseProperty { Value = 0xF00000000ul });
        Add(12, new ArrayBaseProperty { Value = [new DataIdBaseProperty { MasterPropertyId = 0x18, Value = 0x40000000u }] });
        Add(13, new StructBaseProperty { Value = { [0x54] = new IntegerBaseProperty { MasterPropertyId = 0x54, Value = 3 } } });

        var element = Element(1, type: 3, flags: 0);
        element.StateDesc = state;
        var p = UiDescConverter.Convert(Layout(element)).Elements[1].Properties;

        Assert.Equal(Enumerable.Range(1, 13).Select(i => (uint)i), p.Keys);
        Assert.True(p.GetBool(1));
        Assert.Equal(-5, p.GetInteger(2));
        Assert.Equal(0.25f, p.GetFloat(3));
        Assert.Equal(new Vector3(1, 2, 3), Assert.IsType<UiVectorProperty>(p[4]).Value);
        Assert.Equal(0x11223344u, Assert.IsType<UiColorProperty>(p[5]).Argb);
        Assert.Equal(new UiStringRef(0x0DCFFF73u, 0x23000001u), Assert.IsType<UiStringInfoProperty>(p[6]).Value);
        Assert.Equal(0x10000001u, p.GetEnum(7));
        Assert.Equal(0x06000001u, p.GetDataId(8));
        Assert.Equal(0x50000001u, Assert.IsType<UiInstanceIdProperty>(p[9]).Value);
        Assert.Equal(0xF0u, Assert.IsType<UiBitfield32Property>(p[10]).Value);
        Assert.Equal(0xF00000000ul, Assert.IsType<UiBitfield64Property>(p[11]).Value);
        Assert.Equal(new UiDataIdProperty(0x18, 0x40000000u), Assert.Single(Assert.IsType<UiArrayProperty>(p[12]).Items));
        Assert.Equal(3, Assert.IsType<UiStructProperty>(p[13]).Members.GetInteger(0x54));
        Assert.Equal(
            [UiPropertyType.Bool, UiPropertyType.Integer, UiPropertyType.Float, UiPropertyType.Vector, UiPropertyType.Color,
             UiPropertyType.StringInfo, UiPropertyType.Enum, UiPropertyType.DataId, UiPropertyType.InstanceId,
             UiPropertyType.Bitfield32, UiPropertyType.Bitfield64, UiPropertyType.Array, UiPropertyType.Struct],
            p.Values.Select(v => v.Type));

        // A read with the wrong type is an error, not an absent value; an absent one gets the fallback.
        var error = Assert.Throws<InvalidDataException>(() => p.GetBool(2));
        Assert.Contains("0x2 is a Integer property", error.Message, StringComparison.Ordinal);
        Assert.Equal(9, p.GetInteger(99, fallback: 9));
    }

    [Fact]
    public void Every_media_type_converts()
    {
        var element = Element(1, type: 3, flags: 0);
        element.StateDesc.Media.AddRange(
        [
            new MediaDescMovie { Type = MediaType.Movie, FileName = "intro.bik", StretchToFullScreen = true },
            new MediaDescAlpha { Type = MediaType.Alpha, File = 0x06000002u },
            new MediaDescAnimation { Type = MediaType.Animation, Duration = 1.5f, DrawMode = DrawModeType.Normal, Frames = [0x06000003u, 0x06000004u] },
            new MediaDescCursor { Type = MediaType.Cursor, File = 0x06000005u, XHotspot = 3, YHotspot = 4 },
            new MediaDescJump { Type = MediaType.Jump, JumpItemIndex = 2, Probability = 0.5f },
            new MediaDescMessage { Type = MediaType.Message, Id = 0x10000001u, Probability = 1f },
            new MediaDescPause { Type = MediaType.Pause, MinDuration = 1f, MaxDuration = 2f },
            new MediaDescSound { Type = MediaType.Sound, File = 0x2000004Bu, Sound = (Sound)0x76 },
            new MediaDescState { Type = MediaType.State, StateId = (UIStateId)6, Probability = 1f },
            new MediaDescFade { Type = MediaType.Fade, StartAlpha = 0f, EndAlpha = 1f, Duration = 0.5f },
        ]);

        var media = UiDescConverter.Convert(Layout(element)).Elements[1].Media;

        Assert.Equal(new UiMovieMedia("intro.bik", true), media[0]);
        Assert.Equal(new UiAlphaMedia(0x06000002u), media[1]);
        var animation = Assert.IsType<UiAnimationMedia>(media[2]);
        Assert.Equal((1.5f, UiDrawMode.Normal), (animation.Duration, animation.DrawMode));
        Assert.Equal([0x06000003u, 0x06000004u], animation.Frames);
        Assert.Equal(new UiCursorMedia(0x06000005u, 3, 4), media[3]);
        Assert.Equal(new UiJumpMedia(2, 0.5f), media[4]);
        Assert.Equal(new UiMessageMedia(0x10000001u, 1f), media[5]);
        Assert.Equal(new UiPauseMedia(1f, 2f), media[6]);
        Assert.Equal(new UiSoundMedia(0x2000004Bu, 0x76), media[7]);
        Assert.Equal(new UiStateMedia(6, 1f), media[8]);
        Assert.Equal(new UiFadeMedia(0f, 1f, 0.5f), media[9]);
        Assert.Equal(Enumerable.Range(1, 11).Where(t => t != 5).Select(t => (UiMediaType)t), media.Select(m => m.Type));
    }

    [Theory]
    [InlineData(1, 0, 0, "override 1")] // a literal: the bytes after it are misread
    [InlineData(2, 0, 0, "override 2")] // auto-generated: reads like 0, meaning unknown
    [InlineData(0, 1, 0, "has-strings byte set")]
    [InlineData(0, 0, 2, "has variables")]
    public void A_string_info_DatReaderWriter_misreads_throws_naming_where(byte token, byte hasStrings, byte variableCount, string expected)
    {
        var element = Element(0x31, type: 0x19, flags: 0);
        element.StateDesc.Properties.Add(0x17, new StringInfoBaseProperty
        {
            MasterPropertyId = 0x17,
            Value = new StringInfo { Token = token, Override = (StringInfoOverrideFlag)hasStrings, English = 1, Comment = variableCount },
        });

        var error = Assert.Throws<InvalidDataException>(() => UiDescConverter.Convert(Layout(element)));

        Assert.StartsWith("TODO(retailui-data): layout 0x21000099 > element 0x00000031 base state property 0x17", error.Message, StringComparison.Ordinal);
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_media_entry_DatReaderWriter_cannot_decode_throws_naming_where()
    {
        // MediaDesc.Unpack returns null for MediaType.Stretch (12); retail's MediaDesc::CreateMediaType makes nothing.
        var child = Element(0x20, type: 5, flags: 0);
        child.States.Add((UIStateId)1, new StateDesc { StateId = 1, Media = [null!] });
        var element = Element(0x10, type: 3, flags: 0);
        element.Children.Add(0x20, child);

        var error = Assert.Throws<InvalidDataException>(() => UiDescConverter.Convert(Layout(element)));

        Assert.StartsWith(
            "TODO(retailui-data): layout 0x21000099 > element 0x00000010 > element 0x00000020 state 0x1 media 0:",
            error.Message,
            StringComparison.Ordinal);
        Assert.Contains("Stretch", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Corrupt_or_unknown_values_throw()
    {
        var edge = Element(1, type: 3, flags: 0);
        edge.BottomEdge = 5;
        Assert.Contains("bottom edge mode is 5", Assert.Throws<InvalidDataException>(() => UiDescConverter.Convert(Layout(edge))).Message, StringComparison.Ordinal);

        var key = Element(1, type: 3, flags: 0);
        var layout = new LayoutDesc { Id = LayoutId, Width = 800, Height = 600 };
        layout.Elements.Add(2, key);
        Assert.Contains("stored under the key 0x00000002", Assert.Throws<InvalidDataException>(() => UiDescConverter.Convert(layout)).Message, StringComparison.Ordinal);

        var state = Element(1, type: 3, flags: 0);
        state.States.Add((UIStateId)6, new StateDesc { StateId = 7 });
        Assert.Contains("state 0x6: its state id is 0x7", Assert.Throws<InvalidDataException>(() => UiDescConverter.Convert(Layout(state))).Message, StringComparison.Ordinal);

        var property = Element(1, type: 3, flags: 0);
        property.StateDesc.Properties.Add(0x3B, new BoolBaseProperty { MasterPropertyId = 0x3A });
        Assert.Contains("property 0x3B holds a value for property 0x3A", Assert.Throws<InvalidDataException>(() => UiDescConverter.Convert(Layout(property))).Message, StringComparison.Ordinal);

        var media = Element(1, type: 3, flags: 0);
        media.StateDesc.Media.Add(new MediaDescImage { Type = MediaType.Cursor });
        Assert.Contains("Image media entry's own type field says Cursor", Assert.Throws<InvalidDataException>(() => UiDescConverter.Convert(Layout(media))).Message, StringComparison.Ordinal);

        var drawMode = Element(1, type: 3, flags: 0);
        drawMode.StateDesc.Media.Add(new MediaDescImage { Type = MediaType.Image, DrawMode = (DrawModeType)9 });
        Assert.Contains("draw mode 9 is unknown", Assert.Throws<InvalidDataException>(() => UiDescConverter.Convert(Layout(drawMode))).Message, StringComparison.Ordinal);
    }

    private static ElementDesc Element(uint id, uint type, IncorporationFlags flags) => new()
    {
        ElementId = id,
        Type = type,
        StateDesc = new StateDesc { IncorporationFlags = flags },
    };

    private static LayoutDesc Layout(ElementDesc element)
    {
        var layout = new LayoutDesc { Id = LayoutId, Width = 800, Height = 600 };
        layout.Elements.Add(element.ElementId, element);
        return layout;
    }
}
