namespace Lifestone.Data.Tests.Cells;

/// <summary>
/// The cell the live <c>place loc</c> refusal test sends (<c>0x0001FFFD</c>, ModVerbsLiveTests) is one the retail cell dat does not hold, so the mod's
/// <c>invalid-cell</c> rule (AceFacts.CellExists, the cell dat's file index) refuses it. PlaceRulesTests pins the other half: the parser and
/// PlaceRules accept it.
/// </summary>
public sealed class PlaceLocRefusalCellTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint RefusedCell = 0x0001FFFDu;

    [RequiresDatsFact]
    public void The_live_refusal_tests_cell_is_not_in_the_cell_dat()
    {
        Assert.Null(fixture.Dats.TryGetCellBytes(RefusedCell));

        // The lookup works at all (Holtburg's landblock is there), so the null above is a real absence.
        Assert.NotNull(fixture.Dats.TryGetCellBytes(0xA9B4FFFFu));
    }
}
