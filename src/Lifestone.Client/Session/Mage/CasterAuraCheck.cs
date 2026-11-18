using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>What <see cref="CasterAuraCheck.AfterSwap"/> found of one of her caster auras.</summary>
public enum CasterAuraVerdict
{
    /// <summary>The spellbook holds no level of the line: nothing to cast.</summary>
    Unlearned,

    /// <summary>Up on her with more than the margin left: it carries over to the new caster (ACE reads it as the wielder's).</summary>
    Carried,

    /// <summary>Down, or under the margin: the pass before the fight casts it on her again.</summary>
    Owed,
}

/// <summary>One aura line's verdict after a swap.</summary>
/// <param name="Line">The profile line (<see cref="CasterAuras.LineNames"/>).</param>
/// <param name="Level">The highest level the book holds (0: none).</param>
/// <param name="Verdict">The verdict.</param>
/// <param name="SecondsLeft">The seconds left on the highest level up (0: none up).</param>
public readonly record struct CasterAuraLine(string Line, int Level, CasterAuraVerdict Verdict, double SecondsLeft)
{
    /// <summary>The line as the log prints it.</summary>
    public override string ToString() => Verdict switch
    {
        CasterAuraVerdict.Unlearned => Line + ": not learned",
        CasterAuraVerdict.Carried => string.Create(CultureInfo.InvariantCulture, $"{Line} {ScrivenerLearnPlan.Numeral(Level)}: carried, {SecondsLeft:0} s left"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{Line} {ScrivenerLearnPlan.Numeral(Level)}: owed ({(SecondsLeft > 0 ? $"{SecondsLeft:0} s left, under the margin" : "down")})"),
    };
}

/// <summary>
/// M6 14 (<c>m6-caster</c>; the brief: the caster's own buffs are cast again after a swap): the check after a caster swap, check, act, check. Her three caster auras (<see cref="CasterAuras"/>) are "Aura of ... Self" spells: they land in
/// her own enchantments (WorldObject_Magic.cs:2180-2184 @95ef426d), not the caster's, and ACE reads them as the wielder's whenever it reads the wielded caster (WorldObject_Weapon.cs:125-126, 254-255, 390), so an aura up when the
/// caster changes holds on the new one at once and a cast of it then would only renew its time. A line that is down (never cast, run out, or the old caster's own item spell of it that left with it) or under the re-buff margin is
/// owed: the swap asks the pass before the next fight (<see cref="CasterAuraMemory"/>, <see cref="LearnedPassRule"/>), which casts it on her at the level her live skill lands at the buff bar. Pure over the read model.
/// </summary>
public static class CasterAuraCheck
{
    /// <summary>Each caster aura line, in <see cref="CasterAuras.LineNames"/>' order, as it stands after a swap.</summary>
    /// <param name="state">The read model (after the swap's confirmation).</param>
    /// <param name="now">The session clock.</param>
    /// <param name="margin">The re-buff margin in seconds.</param>
    public static IReadOnlyList<CasterAuraLine> AfterSwap(GameState state, double now, double margin)
    {
        ArgumentNullException.ThrowIfNull(state);
        var held = CasterAuras.Of(state.Self.Spellbook);
        var active = state.Self.Qualities.ActiveEnchantments;
        var lines = new List<CasterAuraLine>(CasterAuras.LineNames.Count);
        foreach (var line in CasterAuras.LineNames)
        {
            var level = held.LevelOf(line);
            if (level == 0)
            {
                lines.Add(new CasterAuraLine(line, 0, CasterAuraVerdict.Unlearned, 0));
                continue;
            }

            var left = 0.0;
            if (!active.IsDefaultOrEmpty && ArenaProfile.LineNamed(line) is { } profile)
            {
                foreach (var e in active)
                {
                    if (profile.Spells.Any(s => s.Id == e.SpellId))
                    {
                        left = Math.Max(left, e.RemainingSeconds(now));
                    }
                }
            }

            lines.Add(new CasterAuraLine(line, level, left > margin ? CasterAuraVerdict.Carried : CasterAuraVerdict.Owed, left));
        }

        return lines;
    }

    /// <summary>The check's line for the log: every aura's verdict, and what follows.</summary>
    /// <param name="lines">The verdicts.</param>
    public static string Describe(IReadOnlyList<CasterAuraLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var owed = lines.Count(l => l.Verdict == CasterAuraVerdict.Owed);
        return string.Join("; ", lines) + (owed == 0
            ? ": nothing to cast (an aura is hers, not the caster's: ACE reads it as the wielder's on the new caster, WorldObject_Weapon.cs:125-126, 254-255, 390 @95ef426d)"
            : string.Create(CultureInfo.InvariantCulture, $": {owed} owed: the pass before the next fight casts them on her"));
    }
}

/// <summary>M6 14 (<c>m6-caster</c>): the run's one memory of a caster swap that owes her aura lines a pass (set by the station's swap, read by the known state's pass rule, cleared when a pass ends). Game thread.</summary>
public sealed class CasterAuraMemory
{
    /// <summary>The run's one memory.</summary>
    public static CasterAuraMemory Shared { get; } = new();

    /// <summary>Why a pass is owed (the swap and the lines), or null.</summary>
    public string? Owed { get; private set; }

    /// <summary>Notes a swap's owed lines.</summary>
    /// <param name="why">The swap and the lines.</param>
    public void Owe(string why) => Owed = why ?? throw new ArgumentNullException(nameof(why));

    /// <summary>A pass ended: nothing is owed.</summary>
    public void Clear() => Owed = null;
}
