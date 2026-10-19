using Lifestone.Data.Dat;
namespace Lifestone.Data.Tests.Appearance;

/// <summary>
/// M3 4.2 p6 follow-up (TODO(q7-clothing-palettes)): BSD Mage's kit asks the platemail gauntlets (ClothingBase 0x10000011) and the sollerets (0x10000054) in palette template 21 (Gold); the dat's tables define it,
/// so neither ACE's first-template fallback nor retail's failed palette step (<c>ClothingTable::BuildObjDesc</c> 11.6096 0x005A89B0) is reached. The other kit pieces' own templates are defined too.
/// </summary>
public sealed class ClothingPaletteTemplateTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsTheory]
    [InlineData(0x10000011u, 21u)]
    [InlineData(0x10000054u, 21u)]
    [InlineData(0x1000002Cu, 13u)]
    [InlineData(0x100001A1u, 21u)]
    [InlineData(0x100001A0u, 18u)]
    public void A_kit_piece_defines_the_palette_template_it_is_asked_for(uint clothingTable, uint template)
    {
        Assert.True(fixture.Dats.Portal.TryGet<DatReaderWriter.DBObjs.ClothingTable>(clothingTable, out var table));
        Assert.True(table.ClothingSubPalEffects.ContainsKey(template), $"0x{clothingTable:X8} has templates {string.Join(",", table.ClothingSubPalEffects.Keys)}");
    }
}
