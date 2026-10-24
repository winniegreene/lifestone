using System.Reflection;
using DatReaderWriter.DBObjs;
using Lifestone.Data.Dat;
using Lifestone.Data.Ui;

namespace Lifestone.Data.Tests.Ui;

/// <summary>
/// The master property table and <see cref="UiPropertyIds"/> (m2-plan.md R4): every constant is pinned to the table's
/// name and type for its id, and every property the layouts hold is typed as the table says.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class MasterPropertyTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void The_table_describes_383_properties_and_names_one_more_id()
    {
        var table = MasterPropertyTable.Load(Dats);

        Assert.Equal(383, table.Properties.Count);
        Assert.Equal(table.Properties.Select(p => p.Id).Order(), table.Properties.Select(p => p.Id));
        Assert.Equal(384, table.Names.Count);
        var unnamed = Assert.Single(table.Names.Keys, id => !table.TryGet(id, out _));
        Assert.Equal(0u, unnamed); // the enum mapper's name for the "no property" id 0
        Assert.All(table.Properties, p => Assert.NotNull(p.Name));
        Assert.All(table.Properties, p => Assert.NotEqual(UiPropertyType.Invalid, p.Type));

        Assert.Equal(UiPropertyType.StringInfo, table.Get(UiPropertyIds.TextEntry).Type);
        Assert.True(table.TryFindByName(table.NameOf(UiPropertyIds.TextEntry)!, out var found));
        Assert.Equal(UiPropertyIds.TextEntry, found);
        Assert.False(table.TryFindByName("not a property", out _));
        Assert.False(table.TryGet(0xFFFFu, out _));
        var error = Assert.Throws<InvalidDataException>(() => table.Get(0xFFFFu));
        Assert.Equal("TODO(retailui-data): UI property 0xFFFF is not in the master property table 0x39000001.", error.Message);
    }

    [RequiresDatsFact]
    public void Every_UiPropertyIds_constant_is_pinned_to_its_dat_name_and_type()
    {
        var table = MasterPropertyTable.Load(Dats);
        var constants = typeof(UiPropertyIds).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .ToArray();

        Assert.Equal(192, constants.Length);
        Assert.Equal(constants.Length, constants.Select(f => (uint)f.GetRawConstantValue()!).Distinct().Count());
        foreach (var field in constants)
        {
            var id = (uint)field.GetRawConstantValue()!;
            Assert.True(table.TryGet(id, out var desc), $"{field.Name}: 0x{id:X} is not in the table");

            // The dat name without its UICore_/UI_ prefix and underscores, ignoring case, is the constant's name.
            var name = desc.Name!;
            var stem = name.StartsWith("UICore_", StringComparison.Ordinal) ? name["UICore_".Length..]
                : name.StartsWith("UI_", StringComparison.Ordinal) ? name["UI_".Length..]
                : null;
            Assert.True(stem is not null, $"{field.Name}: 0x{id:X} is not a UI property");
            Assert.True(string.Equals(stem!.Replace("_", "", StringComparison.Ordinal), field.Name, StringComparison.OrdinalIgnoreCase), $"{field.Name}: 0x{id:X} is {name}");

            var declared = field.GetCustomAttribute<PropertyTypeAttribute>();
            Assert.True(declared is not null, $"{field.Name} has no PropertyType attribute");
            Assert.True(declared!.Type == desc.Type, $"{field.Name}: declared {declared.Type}, the table says {desc.Type}");
        }

        // Every core widget property of the table has a constant.
        var coreIds = table.Properties.Where(p => p.Name!.StartsWith("UICore_", StringComparison.Ordinal)).Select(p => p.Id);
        Assert.Subset(constants.Select(f => (uint)f.GetRawConstantValue()!).ToHashSet(), coreIds.ToHashSet());
    }

    [RequiresDatsFact]
    public void Defaults_convert_and_match_the_retail_values()
    {
        var table = MasterPropertyTable.Load(Dats);

        Assert.Equal(0.2f, Assert.IsType<UiFloatProperty>(table.Get(UiPropertyIds.ButtonHotClickFirstInterval).Default).Value);
        Assert.Equal(0.05f, Assert.IsType<UiFloatProperty>(table.Get(UiPropertyIds.ButtonHotClickInterval).Default).Value);
        Assert.Equal(new UiEnumProperty(UiPropertyIds.ButtonClickAction, 1), table.Get(UiPropertyIds.ButtonClickAction).Default);
        Assert.Equal(new UiEnumProperty(UiPropertyIds.ListBoxItemNormalState, 1), table.Get(UiPropertyIds.ListBoxItemNormalState).Default);
        Assert.Equal(new UiEnumProperty(UiPropertyIds.ListBoxItemSelectedState, 6), table.Get(UiPropertyIds.ListBoxItemSelectedState).Default);
        Assert.Equal(new UiBoolProperty(UiPropertyIds.ScrollableKeepInBounds, true), table.Get(UiPropertyIds.ScrollableKeepInBounds).Default);
        Assert.Equal(new UiFloatProperty(UiPropertyIds.ScrollbarWidgetSize, 1f), table.Get(UiPropertyIds.ScrollbarWidgetSize).Default);
        Assert.Equal(new UiColorProperty(UiPropertyIds.ColorPickerCurrentSelection, 0xFF000000u), table.Get(UiPropertyIds.ColorPickerCurrentSelection).Default);
        Assert.Null(table.Get(UiPropertyIds.TextEntry).Default);
        Assert.Equal(
            12,
            table.Properties.Count(p => p.Name!.StartsWith("UICore_", StringComparison.Ordinal) && p.Default is not null));
    }

    [RequiresLocalDatsFact]
    [Trait("Tier", "Exhaustive")]
    public void Every_property_the_layouts_hold_is_typed_as_the_table_says()
    {
        var table = MasterPropertyTable.Load(Dats);
        var layouts = new UiLayouts(Dats);
        var all = layouts.LayoutIds.SelectMany(id => layouts.GetLayout(id).AllElements())
            .SelectMany(e => e.States.Values.Prepend(e.BaseState))
            .SelectMany(s => s.Properties.Values)
            .SelectMany(Flatten)
            .ToArray();

        Assert.All(all, p => Assert.Equal(table.Get(p.Id).Type, p.Type));

        // Array items carry their own ids: the text fonts and colours are lists of the single-font properties.
        Assert.All(all.OfType<UiArrayProperty>().Where(a => a.Id == UiPropertyIds.TextFonts).SelectMany(a => a.Items),
            item => Assert.Equal((UiPropertyIds.TextFont, UiPropertyType.DataId), (item.Id, item.Type)));
        Assert.All(all.OfType<UiArrayProperty>().Where(a => a.Id == UiPropertyIds.TextFontColors).SelectMany(a => a.Items),
            item => Assert.Equal((UiPropertyIds.TextFontColor, UiPropertyType.Color), (item.Id, item.Type)));
        Assert.Equal(171, all.Select(p => p.Id).Distinct().Count()); // 155 ids on states, 16 more only inside arrays and structs
    }

    [RequiresDatsFact]
    public void The_record_DatReaderWriter_unpacks_layouts_with_is_the_one_loaded()
    {
        var table = MasterPropertyTable.Load(Dats);
        var cached = Dats.Read(d => d.Portal.MasterProperty)!;

        Assert.Equal(cached.Properties.Keys.Order(), table.Properties.Select(p => p.Id));
        Assert.All(table.Properties, p => Assert.Equal((uint)cached.Properties[p.Id].Type, (uint)p.Type));
    }

    private static IEnumerable<UiProperty> Flatten(UiProperty property) => property switch
    {
        UiArrayProperty a => a.Items.SelectMany(Flatten).Prepend(a),
        UiStructProperty s => s.Members.Values.SelectMany(Flatten).Prepend(s),
        _ => [property],
    };
}
