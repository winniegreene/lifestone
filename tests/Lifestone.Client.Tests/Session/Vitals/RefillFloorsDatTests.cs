using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>
/// M5 7 F5, the owner's D88 values worked from the dat spell table the cast code reads: the mana floor (one Stamina to Mana press and one Revitalize Self press kept affordable, on top of the bolt's try floor), the stamina floor
/// (the least stamina at which a landed transfer gives back what it and the refill cost), and the expected presses per landing of a level-80 mage (Mana Conversion 258, Stamina to Mana Self VI, item 0's model).
/// </summary>
public sealed class RefillFloorsDatTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    /// <summary>A level-80 mage's Mana Conversion (the M5 research's figure beside 311 at level 126: m5-plan.md §8).</summary>
    private const uint Mc80 = 258;

    private const uint StaminaMax80 = 275;

    [RequiresDatsFact]
    public void D88s_floors_and_the_presses_a_landing_costs_a_level_80_mage()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var transfer = spells[VitalsMeans.StaminaToManaSelf[5]]; // Stamina to Mana Self VI (a level-80 mage casts VI, D26)
        var revitalize = spells[VitalsMeans.RevitalizeSelf[5]]; // Revitalize Self VI
        var bolt = spells[85]; // Flame Bolt VI
        var tb = GearCasts.ManaCeiling(transfer, 0);
        var rb = GearCasts.ManaCeiling(revitalize, 0);
        var boltCeiling = GearCasts.ManaCeiling(bolt, 0);
        var boltFloor = ManaOrder.FloorOf(boltCeiling, bolt.Power, boltCeiling, Mc80, ManaOrderOptions.Default);
        var reserve = ManaOrder.ManaFloorOf(tb, transfer.Power, rb, revitalize.Power, Mc80);
        var row = BuffStatMods.TransferOf(transfer.Id)!.Value;
        var staminaFloor = ManaOrder.StaminaFloorOf(in row, StaminaMax80, tb, rb);
        output.WriteLine($"Stamina to Mana Self VI: base {tb} power {transfer.Power}; Revitalize Self VI: base {rb} power {revitalize.Power}; Flame Bolt VI: base {boltCeiling} power {bolt.Power}");
        output.WriteLine($"Mana Conversion {Mc80}: Flame Bolt try floor {boltFloor}, reserve {reserve} (transfer press floor {ManaConversionModel.TryFloor(tb, transfer.Power, Mc80)} + revitalize press floor {ManaConversionModel.TryFloor(rb, revitalize.Power, Mc80)}); mana floor {boltFloor + reserve}");
        output.WriteLine($"stamina floor {staminaFloor} of {StaminaMax80}: a landing gives {ManaOrder.Give(in row, staminaFloor)} (needs {tb + rb})");
        foreach (var held in new uint[] { 1, 5, boltFloor + reserve, tb })
        {
            var chance = ManaConversionModel.LandingChance(tb, transfer.Power, Mc80, held);
            output.WriteLine($"at mana {held}: a press lands {chance:P1}, so {(chance > 0 ? 1 / chance : double.PositiveInfinity):0.0} presses a landing");
        }

        Assert.True(reserve >= 10); // each press floor is never under a fizzle's 5
        Assert.True(ManaOrder.Give(in row, staminaFloor) >= tb + rb);
        Assert.InRange(staminaFloor, StaminaMax80 / 2, ManaOrder.FullStamina(StaminaMax80, 90) - 1); // under the refill's 90%: the refill keeps the stamina over the floor, which is the backstop
        Assert.InRange(1 / ManaConversionModel.LandingChance(tb, transfer.Power, Mc80, boltFloor + reserve), 1.0, 2.0); // 1.5 presses a landing at the mana floor (15), 2.8 at 5 mana and 10.2 at 1
    }
}
