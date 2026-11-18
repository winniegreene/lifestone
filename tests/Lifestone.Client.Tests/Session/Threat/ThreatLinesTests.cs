using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Threat;

namespace Lifestone.Client.Tests.Session.Threat;

/// <summary>
/// M6 16 E2a: the threat board's one reader of the damage lines (<see cref="ThreatLines"/>). It is no new parser: it calls the classifications the detectors call. Each form ACE writes is pinned here, and the same lines are
/// sent to the two detectors that read them (the outer loop's <c>HostileWatch</c>, the travel legs' <c>AttackWatch</c>) to show the board hears what they hear: a hit where the outer loop's watch notes one, an attack where the
/// travel legs' watch hears a name.
/// </summary>
public sealed class ThreatLinesTests
{
    /// <summary>The lines: the text, whether it is read at all, and then its kind, source, how and amount.</summary>
    public static TheoryData<string, bool, ThreatLineKind, string, string, uint> Lines() => new()
    {
        { "Tusker Guard scorches you for 15 points with Flame Bolt III.", true, ThreatLineKind.Hit, "Tusker Guard", "spell damage", 15 },
        { "Critical hit! Kilif Zefir grazes you for 31 points with Shock Wave II.", true, ThreatLineKind.Hit, "Kilif Zefir", "spell damage", 31 },
        { "Dark Wisp casts Harm Other II and drains 11 points of your health.", true, ThreatLineKind.Hit, "Dark Wisp", "spell drain", 11 },
        { "Dark Wisp casts Harm Other II and drains 0 points of your health.", true, ThreatLineKind.Attack, "Dark Wisp", "spell drain", 0 },
        { "Dark Wisp casts Drain Stamina Other II and drains 9 points of your stamina.", true, ThreatLineKind.Attack, "Dark Wisp", "spell drain", 0 },
        { "Dark Wisp casts Drain Mana Other II and drains 9 points of your mana.", true, ThreatLineKind.Attack, "Dark Wisp", "spell drain", 0 },
        { "You lose 7 points of health due to Dark Wisp casting Drain Health Other II on you", true, ThreatLineKind.Hit, "Dark Wisp", "health drain", 7 },
        { "You lose 0 points of health due to Dark Wisp casting Drain Health Other II on you", true, ThreatLineKind.Attack, "Dark Wisp", "health drain", 0 },
        { "You resist the spell cast by Kilif Zefir", true, ThreatLineKind.Attack, "Kilif Zefir", "spell resisted", 0 },
        { "Dark Wisp casts Fester Other II on you", true, ThreatLineKind.Attack, "Dark Wisp", "spell cast on the player", 0 },
        { "Dark Wisp cast Exhaustion Other II on you", true, ThreatLineKind.Enchant, "Dark Wisp", "enchantment", 0 },
        { "Sho cast Strength Self on you, refreshing Strength Self", true, ThreatLineKind.Enchant, "Sho", "enchantment", 0 },
        { "You cast Heal Self VI and restore 52 points of your health.", false, default, "", "", 0 },
        { "You scorch Tusker Guard for 40 points with Flame Bolt VI.", false, default, "", "", 0 },
        { "Tusker Guard resists your spell", false, default, "", "", 0 },
        { "With Harm Other VI you drain 22 points of health from Tusker Guard.", false, default, "", "", 0 },
        { "You lose 7 points of stamina due to Dark Wisp casting Drain Stamina Other II on you", false, default, "", "", 0 },
    };

    private static ChatLine Magic(string text) => new(0, ChatKind.System, FightTally.MagicChatType, null, 0, text);

    [Theory]
    [MemberData(nameof(Lines))]
    public void A_magic_line_is_read_once_as_a_hit_an_attack_an_enchantment_or_nothing(string text, bool read, ThreatLineKind kind, string source, string how, uint amount)
    {
        Assert.Equal(read, ThreatLines.TryRead(Magic(text), out var line));
        if (read)
        {
            Assert.Equal(new ThreatLine(kind, source, how, amount), line);
        }
    }

    [Fact]
    public void Only_a_magic_typed_system_line_is_read()
    {
        const string Text = "Tusker Guard scorches you for 15 points with Flame Bolt III.";
        Assert.False(ThreatLines.TryRead(new ChatLine(0, ChatKind.System, CombatChatTypes.CombatEnemy, null, 0, Text), out _)); // the melee notices come as combat events, not as lines
        Assert.False(ThreatLines.TryRead(new ChatLine(0, ChatKind.Speech, FightTally.MagicChatType, "Tusker Guard", 0, Text), out _)); // a player can say anything
    }

    [Theory]
    [MemberData(nameof(Lines))]
    public void The_board_hears_a_line_as_the_two_watches_that_read_it_do(string text, bool read, ThreatLineKind kind, string source, string how, uint amount)
    {
        _ = (how, amount);
        var t = new ThreatRig();
        var board = t.Board;
        using var outer = t.OuterWatch();
        using var leg = t.LegWatch();
        t.Wait(1);
        t.Magic(text);

        var hit = read && kind == ThreatLineKind.Hit;
        var attack = read && kind != ThreatLineKind.Enchant;
        Assert.Equal(hit, outer.LastAttackAt == t.Now); // the outer loop's watch notes a hit exactly where the board does
        Assert.Equal(hit, board.LastHitAt == t.Now);
        Assert.Equal(attack, leg.HeardWithin(1).Contains(source)); // the travel legs' watch hears a name exactly where the board hears an attack
        Assert.Equal(attack, board.LastAttackAt == t.Now);
        if (attack)
        {
            Assert.Equal((source, hit ? source : string.Empty), (board.LastAttackBy, board.LastHitBy));
            Assert.Equal(outer.LastAttacker, board.LastHitBy);
        }
        else
        {
            Assert.Equal(0, board.AttackerCount); // an enchantment or another line makes no attacker
        }
    }
}
