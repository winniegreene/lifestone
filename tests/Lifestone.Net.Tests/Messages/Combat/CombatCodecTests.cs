using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Net.Tests.Messages.Combat;

/// <summary>
/// <see cref="CombatCodecs"/>: the combat and death notifications (GE 0x01AC, 0x01AD, 0x01B1-0x01B4; GM 0x019E) against
/// bytes laid out from ACE's writers (<see cref="AceM3Writers"/>), each field asserted, the String16L padding included.
/// </summary>
public sealed class CombatCodecTests
{
    private const uint Player = 0x50000001;

    [Fact]
    public void Victim_and_Killer_notifications_read_the_text()
    {
        Assert.True(CombatCodecs.TryParseVictim(AceM3Writers.VictimNotification(Player, 1, "You have been killed by a Drudge!"), out var victim));
        Assert.Equal(new VictimNotificationEvent("You have been killed by a Drudge!"), victim);

        Assert.True(CombatCodecs.TryParseKiller(AceM3Writers.KillerNotification(Player, 2, "You killed the Drudge!"), out var killer));
        Assert.Equal(new KillerNotificationEvent("You killed the Drudge!"), killer);
    }

    [Fact]
    public void Attacker_notification_reads_every_field()
    {
        // "Drudge" is 6 bytes: u16 length + 6 = 8, already aligned; "Rat" and "Mosswart" below exercise the padding.
        var message = AceM3Writers.AttackerNotification(Player, 3, "Drudge", 0x10, 0.25, 7, true, 0x0000000100000002UL);
        Assert.True(CombatCodecs.TryParseAttacker(message, out var hit));
        Assert.Equal(new AttackerNotificationEvent("Drudge", 0x10, 0.25, 7, true, 0x0000000100000002UL), hit);

        var padded = AceM3Writers.AttackerNotification(Player, 3, "Mosswart", 0x01, 1.0, 1, false, 0);
        Assert.True(CombatCodecs.TryParseAttacker(padded, out var second));
        Assert.Equal(new AttackerNotificationEvent("Mosswart", 1, 1.0, 1, false, 0), second);

        var oddName = AceM3Writers.AttackerNotification(Player, 3, "Rat", 0x01, 0.5, 2, false, 0);
        Assert.Equal(16 + 8 + 4 + 8 + 4 + 4 + 8, oddName.Length);
        Assert.True(CombatCodecs.TryParseAttacker(oddName, out var third));
        Assert.Equal("Rat", third.DefenderName);
    }

    [Fact]
    public void Defender_notification_reads_every_field_including_the_location()
    {
        var message = AceM3Writers.DefenderNotification(Player, 4, "Banderling", 0x02, 0.125, 3, 4, false, 0x20UL);

        Assert.True(CombatCodecs.TryParseDefender(message, out var hit));
        Assert.Equal(new DefenderNotificationEvent("Banderling", 0x02, 0.125, 3, 4, false, 0x20UL), hit);

        var crit = AceM3Writers.DefenderNotification(Player, 4, "Rat", 0x04, 0.5, 9, 1, true, 0);
        Assert.True(CombatCodecs.TryParseDefender(crit, out var second));
        Assert.True(second.CriticalHit);
        Assert.Equal("Rat", second.AttackerName);
    }

    [Fact]
    public void Evasion_notifications_read_the_name()
    {
        Assert.True(CombatCodecs.TryParseEvasionAttacker(AceM3Writers.EvasionAttackerNotification(Player, 5, "Drudge"), out var attacker));
        Assert.Equal(new EvasionAttackerNotificationEvent("Drudge"), attacker);

        Assert.True(CombatCodecs.TryParseEvasionDefender(AceM3Writers.EvasionDefenderNotification(Player, 6, "Banderling"), out var defender));
        Assert.Equal(new EvasionDefenderNotificationEvent("Banderling"), defender);
    }

    [Fact]
    public void PlayerKilled_reads_text_victim_and_killer()
    {
        var message = AceM3Writers.PlayerKilled("Tester has been killed by a Drudge.", 0x50000001, 0x80000456);

        Assert.True(CombatCodecs.TryParsePlayerKilled(message, out var killed));
        Assert.Equal(new PlayerKilledMsg("Tester has been killed by a Drudge.", 0x50000001, 0x80000456), killed);
    }
}
