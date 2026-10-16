using Lifestone.Data.Dat;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Dat;

/// <summary>
/// The portal's quality filter that <c>CEnchantmentRegistry::EnchantAttribute2nd</c> asks (<c>ACQualityFilter::QueryAttribute2nd</c>; enum 0x1000000C of enum group 3): it decodes exactly,
/// and it names the secondary attributes a client may enchant (M3 2.2 a1, closing <c>TODO(g4-quality-filter)</c>).
/// </summary>
public sealed class QualityFilterDataTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void The_enchantable_filter_decodes_and_lists_the_vitals()
    {
        var did = fixture.Dats.EnumMaps.Resolve(QualityFilterData.QualityFilterGroup, QualityFilterData.EnchantableEnum);
        var filter = QualityFilterData.LoadEnchantable(fixture.Dats);

        output.WriteLine($"quality filter 0x{did:X8}: attributes=[{string.Join(",", filter.AttributeIds)}] attribute2nds=[{string.Join(",", filter.Attribute2ndIds)}] skills={filter.SkillIds.Count}");
        Assert.Equal(did, filter.DataId);
        Assert.Equal(new uint[] { 1, 3, 5 }, filter.Attribute2ndIds); // maximum health, stamina and mana
        Assert.True(filter.QueryAttribute2nd(5));
        Assert.False(filter.QueryAttribute2nd(6)); // a vital's current value is not asked
    }
}
