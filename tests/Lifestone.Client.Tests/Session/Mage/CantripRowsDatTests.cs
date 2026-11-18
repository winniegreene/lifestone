using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (<c>m6-armour</c>) against the retail dats: every cantrip row the wear pick reads by spell id (<see cref="CantripRows"/>, the world's spell table) is the SpellTable's (0x0E00000E) spell of that id and name, so a looted piece's
/// spell ids name the cantrips the score values.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class CantripRowsDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void Every_cantrip_row_is_the_SpellTables_spell_of_its_id()
    {
        var table = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var row in CantripRows.BySpell.Values.OrderBy(r => r.Id))
        {
            Assert.True(table.TryGetValue(row.Id, out var info), $"{row.Id} is not in the SpellTable");
            output.WriteLine($"{row.Id} {info!.Name}: {row.Mod.Value} on 0x{row.Mod.Type:X} key {row.Mod.Key}");
            Assert.Equal(row.Name, info.Name);
        }
    }
}
