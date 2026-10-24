using Lifestone.Client.Session.Fighter;

namespace Lifestone.Client.Tests.Session.Fighter;

/// <summary>M4 2.1-R1: the one rule behind <c>worn=</c> in the fighter build check and the corpse test.</summary>
public sealed class FighterKitWornTests
{
    [Fact]
    public void Worn_counts_the_kits_leather_armour_lines_only_and_each_line_once()
    {
        var armour = FighterKit.Hunter.Worn.Where(i => i.Role == FighterRole.Armour).Select(i => i.Wcid).ToArray();
        Assert.Equal(5, armour.Length);
        Assert.Equal(5, FighterKit.Hunter.CountWornArmour(armour));
        Assert.Equal(5, FighterKit.Hunter.CountWornArmour([.. armour, .. armour])); // a second copy of a line is not a second piece
        Assert.Equal(5, FighterKit.Hunter.CountWornArmour([.. armour, FighterKit.MaceWcid, 999999, 0])); // the Mace and anything else worn is not armour of the kit
        Assert.Equal(0, FighterKit.Hunter.CountWornArmour([FighterKit.MaceWcid]));
        Assert.Equal(4, FighterKit.Hunter.CountWornArmour(armour.Skip(1)));
    }
}
