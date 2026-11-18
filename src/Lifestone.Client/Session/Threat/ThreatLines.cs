using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;

namespace Lifestone.Client.Session.Threat;

/// <summary>What one Magic-typed line says of an attack on her (<see cref="ThreatLines.TryRead"/>).</summary>
public enum ThreatLineKind : byte
{
    /// <summary>Health was taken: a spell's damage, or a drain or a loss of health of more than 0.</summary>
    Hit,

    /// <summary>An attack that took no health: a resisted spell, a drain of stamina or mana, a hostile spell cast on her, a health drain of 0.</summary>
    Attack,

    /// <summary>An enchantment another cast on her (ACE writes the line for a buff too): no attack by itself, evidence only for a kind already heard attacking.</summary>
    Enchant,
}

/// <summary>One Magic-typed line read as an attack on her.</summary>
/// <param name="Kind">What it was.</param>
/// <param name="Source">The caster's name, as the line names it.</param>
/// <param name="How">How it was heard, in the words the detectors log (<c>spell damage</c>, <c>spell drain</c>, <c>spell resisted</c>, <c>spell cast on the player</c>, <c>health drain</c>, <c>enchantment</c>).</param>
/// <param name="Amount">The health taken (0 for an attack that took none).</param>
public readonly record struct ThreatLine(ThreatLineKind Kind, string Source, string How, uint Amount);

/// <summary>
/// M6 16 E2a (docs/automation-engine.md section 2.2, m6-plan.md D139): the threat board's ONE reader of the damage lines. The server tells her of a creature's spell in Magic-typed chat lines that carry a name and no guid; three
/// detectors read them, each with its own handler (<c>HostileWatch.OnChat</c>, <c>AttackWatch.OnChat</c>, the fight's <c>OnDefenceChat</c>). This is no new parser: it calls the same three classifications they call, in their
/// order (<see cref="FightTally.TryHealthLoss"/>, <see cref="FightScenario.HostileSpellSource"/> with <see cref="FightTally.Classify"/>, <see cref="FightTally.TryCastOnYou"/>), and states the result once.
/// </summary>
/// <remarks>
/// Where the detectors read a line differently, the board takes <c>HostileWatch</c>'s reading, the one the outer loop's "attacked" decides by: health a spell took is a hit; a health drain or loss of 0 is an attack heard and no hit
/// (<c>AttackWatch</c> notes a hit of 0 there, which changes none of its answers: its attackers are the names heard). A line allocates what those classifications allocate, the source's name, and nothing else; a line that is none of
/// them (her own casts, a creature resisting her spell) allocates nothing.
/// </remarks>
public static class ThreatLines
{
    private const string HealthDrainTail = " points of your health.";

    /// <summary>Reads <paramref name="line"/> as an attack on her; false when it is none (not a Magic-typed system line, her own line, a line that names no attacker).</summary>
    /// <param name="line">The chat line.</param>
    /// <param name="read">What it says.</param>
    public static bool TryRead(in ChatLine line, out ThreatLine read)
    {
        read = default;
        if (line.ChatType != FightTally.MagicChatType || line.Kind != ChatKind.System || line.Text is not { } text)
        {
            return false;
        }

        if (FightTally.TryHealthLoss(text, out var lossSource, out var loss))
        {
            read = new ThreatLine(loss > 0 ? ThreatLineKind.Hit : ThreatLineKind.Attack, lossSource, "health drain", Points(loss));
            return lossSource.Length > 0;
        }

        if (FightScenario.HostileSpellSource(text, out var source, out var how))
        {
            var kind = FightTally.Classify(text, out var amount, out _);
            var hit = kind == MagicLineKind.DamageTaken || (kind == MagicLineKind.DrainTaken && amount > 0 && text.Contains(HealthDrainTail, StringComparison.Ordinal));
            read = new ThreatLine(hit ? ThreatLineKind.Hit : ThreatLineKind.Attack, source, how, hit ? Points(amount) : 0);
            return true;
        }

        if (FightTally.TryCastOnYou(text, out var caster) && caster.Length > 0)
        {
            read = new ThreatLine(ThreatLineKind.Enchant, caster, "enchantment", 0);
            return true;
        }

        return false;
    }

    private static uint Points(long amount) => (uint)Math.Clamp(amount, 0, uint.MaxValue);
}
