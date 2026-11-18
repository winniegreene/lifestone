using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Mage;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>M5 7 G2 (the stop, the fresh start, the alarm): the hostile watch hears a creature's spell as an attack (edge audit item 10).</summary>
public sealed class AutopilotG2Tests
{
    [Fact]
    public void Item10_the_hostile_watch_counts_a_creatures_spell_at_the_player_as_an_attack_and_its_damage_and_nothing_else()
    {
        var rig = new FightManaTests.Rig(mana: 400, stamina: 200, knowsDrain: true);
        rig.R.Tick();
        using var watch = new HostileWatch(rig.View, rig.Fight.GroundProfile, 0); // reach 0: only the notices count

        rig.R.Raw(AceWireWriters.ServerMessage("You scorch Tusker Slave for 87 points with Flame Bolt VII.", 7)); // the player's own cast
        rig.R.Raw(AceWireWriters.ServerMessage("Virindi Mage casts Frost Bolt and drains 5 points of your Mana.", 7)); // a drain: not damage
        Assert.False(watch.InReach(rig.Now, out _));
        Assert.Equal(0, watch.IncomingPerSecond(rig.Now));

        rig.R.Raw(AceWireWriters.ServerMessage("Critical hit! Virindi Mage scorches you for 31 points with Flame Bolt.", 7));
        Assert.True(watch.InReach(rig.Now, out var what));
        Assert.Contains("Virindi Mage attacked", what, StringComparison.Ordinal);
        Assert.Equal(31 / RecentHits.SeedAttackSeconds, watch.IncomingPerSecond(rig.Now), 3); // M6 7 Y: the pin moved from 3.1 (31 over the 10 s window) to one hit over the seed interval: the guard and the retreat read the recent hits
    }
}
