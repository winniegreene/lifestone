using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Tests;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>The scripted ACE's mana economy (M3 2.1 fix B, review 2.1-R2): the skill formula it uses is the dat's, and its costs are ACE's Mana Conversion costs, not the ceiling.</summary>
public sealed class ScriptedCasterTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void The_scripted_mages_mana_conversion_formula_is_the_dats()
    {
        var skills = SkillTableData.Load(fixture.Dats);
        var e = skills.Skills.First(s => s.Id == MageTemplate.ManaConversion);
        output.WriteLine($"Mana Conversion: ({e.Formula.Attribute1} x {e.Formula.Attribute1Multiplier} + {e.Formula.Attribute2} x {e.Formula.Attribute2Multiplier} + {e.Formula.AdditiveBonus}) / {e.Formula.Divisor}");
        Assert.Equal((MageTemplate.Focus, MageTemplate.Self), ((uint)e.Formula.Attribute1, (uint)e.Formula.Attribute2));
        Assert.Equal((1, 1, 0), (e.Formula.Attribute1Multiplier, e.Formula.Attribute2Multiplier, e.Formula.AdditiveBonus));
        Assert.Equal(ScriptedCaster.ManaConversionDivisor, e.Formula.Divisor);
    }
}
