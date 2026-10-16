using Lifestone.Data.Magic;

namespace Lifestone.Data.Tests.Magic;

/// <summary>
/// The component map (M3 1.4 s0): the spell components of the gear kit and the weenies ACE gives them, pinned by name and class id against the portal dat. The map is a table taken from a read-only SELECT of ACE's world database; the dat
/// decides whether it is right: each name equals the SpellComponentTable's name for the id, each class id equals the DualDidMapper 0x27000002 ACE reads (<c>Spell.GetComponentWCID</c>), and the destruction modifier equals the dat's.
/// </summary>
public sealed class ComponentWeeniesTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [Fact]
    public void The_map_holds_81_components_in_the_families_the_select_names()
    {
        Assert.Equal(81, ComponentWeenies.All.Count);
        Assert.Equal(81, ComponentWeenies.All.Select(c => c.ComponentId).Distinct().Count());
        Assert.Equal(81, ComponentWeenies.All.Select(c => c.Wcid).Distinct().Count());
        Assert.Equal(new[] { 9, 18, 12, 14, 15, 13 }, new[] { Count(1, 6, 110, 112, 193), Count(7, 24), Count(25, 36), Count(37, 48, 111, 187), Count(49, 62, 190), Count(63, 74, 188) });
        Assert.Equal(1000, ComponentWeenies.All.Single(c => c.ComponentId == 188).MaxStack);
        Assert.All(ComponentWeenies.All.Where(c => c.ComponentId != 188), c => Assert.Equal(100, c.MaxStack));
        Assert.True(ComponentWeenies.TryGetByWcid(691, out var lead) && lead is { ComponentId: 1, Name: "Lead Scarab" });
        Assert.False(ComponentWeenies.TryGetByComponent(192, out _)); // the Dark Scarab (37117, maximum stack 1) is not in the kit
        Assert.False(ComponentWeenies.TryGetByComponent(113, out _)); // the "Pea" family is separate
        Assert.True(ComponentWeenies.IsColouredTaper(63) && ComponentWeenies.IsColouredTaper(74));
        Assert.False(ComponentWeenies.IsColouredTaper(62) || ComponentWeenies.IsColouredTaper(75) || ComponentWeenies.IsColouredTaper(188));
    }

    private static int Count(uint from, uint to, params uint[] extra) =>
        ComponentWeenies.All.Count(c => (c.ComponentId >= from && c.ComponentId <= to) || extra.Contains(c.ComponentId));

    [Fact]
    public void The_dat_mapper_decodes_a_count_of_one_two_or_four_bytes()
    {
        static byte[] Record(uint count, byte[] compressed, params (uint, uint)[] pairs)
        {
            var bytes = new List<byte>(BitConverter.GetBytes(0x27000002u)) { 1 };
            bytes.AddRange(compressed);
            foreach (var (key, value) in pairs)
            {
                bytes.AddRange(BitConverter.GetBytes(key));
                bytes.AddRange(BitConverter.GetBytes(value));
            }

            return [.. bytes];
        }

        Assert.Equal(new Dictionary<uint, uint> { [1] = 691, [2] = 689 }, ComponentWeenies.DecodeClientEnumToId(Record(2, [2], (1, 691), (2, 689))));
        Assert.Equal(new Dictionary<uint, uint> { [7] = 774 }, ComponentWeenies.DecodeClientEnumToId(Record(1, [0x80, 0x01], (7, 774))));
        Assert.Equal(new Dictionary<uint, uint> { [7] = 774 }, ComponentWeenies.DecodeClientEnumToId(Record(1, [0xC0, 0x00, 0x01, 0x00], (7, 774))));
        Assert.Throws<InvalidDataException>(() => ComponentWeenies.DecodeClientEnumToId(Record(3, [3], (1, 691))));
    }

    [RequiresDatsFact]
    public void Each_pinned_name_class_id_and_destruction_modifier_equals_the_dats()
    {
        var table = SpellComponentTableData.Load(fixture.Dats);
        var mapper = ComponentWeenies.LoadClientEnumToId(fixture.Dats);
        Assert.Equal(164, mapper.Count); // the dat's own count: the 81 here, the Pea family, the Dark Scarab and the void school's four
        foreach (var c in ComponentWeenies.All)
        {
            Assert.True(table.Components.TryGetValue(c.ComponentId, out var dat), $"component {c.ComponentId}");
            Assert.Equal(dat.Name, c.Name);
            Assert.Equal(dat.Cdm, c.Cdm);
            Assert.True(mapper.TryGetValue(c.ComponentId, out var wcid), $"mapper has component {c.ComponentId}");
            Assert.Equal(wcid, c.Wcid);
        }

        // The coloured tapers are the dat's types 6 (Taper) with ids 63 to 74, and the Prismatic Taper is 188.
        Assert.All(Enumerable.Range(63, 12), id => Assert.Equal(6u, (uint)table.Components[(uint)id].Type));
        Assert.Equal("Prismatic Taper", table.Components[SpellFormulaRules.PrismaticTaperId].Name);
    }
}
