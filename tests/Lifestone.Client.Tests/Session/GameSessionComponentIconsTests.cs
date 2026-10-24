using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.Session;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <see cref="GameSession"/> as the components page's icon source (M4 4.1 fix, review R9; <see cref="ISpellComponentIcons"/>): the SpellComponentTable is loaded where the stat tables are
/// (<see cref="GameSession.UseStatTablesFrom"/>) and <see cref="GameSession.ComponentIcon"/> answers from the table's <c>IconId</c>, as <c>gmSpellComponentUI::MakeIcon</c> (11.6096 0x0048A250) draws it.
/// </summary>
public sealed class GameSessionComponentIconsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private static GameSession NewSession() => GameSession.Offline(static _ => { }, TimeProvider.System, NullLogSink.Instance);

    [Fact]
    public void A_session_without_the_dats_is_an_icon_source_that_gives_none()
    {
        using var session = NewSession();
        var icons = Assert.IsAssignableFrom<ISpellComponentIcons>(session);
        Assert.Equal(0u, icons.ComponentIcon(1));
        Assert.Equal(0u, icons.ComponentIcon(0));
    }

    [RequiresLocalDatsFact]
    public void With_the_dats_every_component_id_answers_the_tables_icon_and_an_unknown_id_answers_none()
    {
        using var session = NewSession();
        session.UseStatTablesFrom(fixture.Dats);
        var table = SpellComponentTableData.Load(fixture.Dats);
        Assert.NotEmpty(table.Components);
        foreach (var (id, info) in table.Components)
        {
            Assert.Equal(info.IconId, session.ComponentIcon(id));
        }

        Assert.Contains(table.Components.Values, static c => c.IconId != 0);
        Assert.Equal(0u, session.ComponentIcon(0xFFFF_FFF0));
    }
}
