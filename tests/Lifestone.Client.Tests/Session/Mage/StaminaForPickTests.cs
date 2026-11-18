using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 14 (live 2026-11-14: out of stamina at 16 of 272 and not draining): with Assess Creature untrained, most appraisals of a level 80 Tusker Slave fail their roll against its Deception and tell no stamina, and every
/// unread Slave was passed over though the ones read held 600. What this proves: a creature's own reading wins; an unread creature of a kind read this fight stands at the kind's stamina less what her drains took from it,
/// never under 0; a creature of a kind never read is still passed over (null).
/// </summary>
public sealed class StaminaForPickTests
{
    [Theory]
    [InlineData(570u, 600u, 120u, 570u)] // its own reading wins over its kind's
    [InlineData(null, 600u, 0u, 600u)] // unread, its kind read at 600 (the live Slaves): assumed full
    [InlineData(null, 600u, 118u, 482u)] // unread, two of her drains took 118 from it: 600 less 118
    [InlineData(null, 600u, 700u, 0u)] // drained past the kind's reading: 0, never under
    [InlineData(null, null, 0u, null)] // no creature of its kind read yet: passed over
    public void An_unread_creature_stands_at_its_kinds_read_stamina_less_her_drains_and_its_own_reading_wins(uint? read, uint? kindRead, uint drained, uint? expected)
    {
        Assert.Equal(expected, FightScenario.StaminaForPick(read, kindRead, drained));
    }
}
