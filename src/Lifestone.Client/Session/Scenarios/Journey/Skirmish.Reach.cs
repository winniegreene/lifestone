using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.World;
using Lifestone.Data.Magic;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 the hovering Zefir (live ladder51, 2026-11-17 02:55:47 to 03:00:13 UTC, the Crater Pathway 0x01A7, the quest hop pathway-to-crater-lake): a Kilif Zefir 0x8000802D hovered 38.9 m from the level-79 climber, the run past named it
/// to fight (<c>ranged-line</c>), and the travel fight-back read a fixed "cast range 15 m" (the Academy station's distance to cast from in its rooms, <see cref="QuestStepLeg.CastRange"/>, never a spell's range): it waited 10 s, left it, took it back at its
/// next Harm Other, and so on for 180 s with no cast, until the timeout recalled her at health 207 of 219, while her own bolt reached 68.2 m. The fight-back's reach is now the spell's own:
/// <list type="bullet">
/// <item><b>The range</b> (<see cref="SpellReach"/>): ACE's <c>Player.VerifySpellRange</c> (Player_Magic.cs:481-506 @95ef426d): for a spell cast with no caster item, <c>min(BaseRangeConstant + (InitLevel + Ranks) * BaseRangeMod,
/// MaxRadarRange_Outdoors)</c> of the spell's school, measured <c>Distance2D</c> (planar); beyond it the cast is refused (UseDone MissileOutOfRange). The same rule as the hunting fight's <see cref="FightScenario.CastRange"/>; the distance
/// is planar where the client draws both bodies (<see cref="PlanarMetres"/>).</item>
/// <item><b>Beyond the bolt's range</b>: Life Magic when a Harm Other reaches it (<see cref="LifeStrikeAt"/>, no projectile); else she runs past it at once (<see cref="RunPastOutOfReach"/>): it is left with her reach, the walk
/// goes on with a logged reason, and it is fought again only when it attacks her from within that reach (the host's <see cref="ISkirmishHost.Leave"/>), never for attacking from beyond it.</item>
/// <item><b>The decision window</b> (<see cref="FightBackDecisionSeconds"/>): one target of a fight-back that has had no cast at it for that long (the dance under fire's seconds not counted) is run past the same way, so a fight-back
/// with no cast never runs to the 180 s timeout.</item>
/// </list>
/// No shot check on the road: the outdoor walk has no line test (<c>TODO(m6-outdoor-line)</c>), so a bolt with no clear shot is found by its result, the bolts that come to nothing (<c>TravelLegLifeStrike.cs</c>), and Life Magic takes it.
/// </summary>
internal sealed partial class Skirmish
{
    /// <summary>Seconds one target of a fight-back may go with no cast at it before she runs past it (Lifestone's own: one decision window, the old wait's length).</summary>
    internal const double FightBackDecisionSeconds = 10;

    /// <summary>Her range for <paramref name="spell"/>: ACE's (see the class), from the start level plus ranks of its school's skill (War Magic, else Life Magic).</summary>
    private float SpellReach(SpellInfo spell)
    {
        var skillId = spell.School == MagicSchool.LifeMagic ? MageTemplate.LifeMagic : MageTemplate.WarMagic;
        var initPlusRanks = S.State.Self.Qualities.TryGetSkill(skillId, out var skill) ? skill.Init + skill.Ranks : 0u;
        return FightScenario.CastRange(spell, initPlusRanks);
    }

    /// <summary>The farthest any Harm Other she knows reaches (not refused on this leg), 0 with none.</summary>
    private float LifeReach()
    {
        var best = 0f;
        foreach (var id in VitalsMeans.HarmOther)
        {
            if (S.State.Self.Spellbook.Knows(id) && !_boltsRefused.Contains(id) && Context.Spells.TryGetValue(id, out var info))
            {
                best = Math.Max(best, SpellReach(info));
            }
        }

        return best;
    }

    /// <summary>The planar distance from her to <paramref name="guid"/> where the client draws both (ACE measures a spell's range in two dimensions), or <paramref name="fallback"/> when either is not read.</summary>
    internal double PlanarMetres(uint guid, double fallback)
    {
        if (host.WhereSheStands() is not { } her || !S.State.Objects.TryGet(guid, out var o) || S.Live.At(o) is not { } it)
        {
            return fallback;
        }

        var a = her.ToGlobal();
        var b = it.ToGlobal();
        var (dx, dy) = (a.X - b.X, a.Y - b.Y);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// She runs past an attacker she cannot hurt from where she stands (see the class): it is left with <paramref name="reach"/> (fought again only when it attacks her from within it), one line with the reason, and the fight-back goes on
    /// to the next attacker or ends, so the walk goes on (M6 14 run past, D137).
    /// </summary>
    private void RunPastOutOfReach(uint guid, string name, double reach, string why)
    {
        host.Leave(guid, name, forRange: true, why, reach);
        Log(string.Create(CultureInfo.InvariantCulture, $"fight-back: '{name}' 0x{guid:X8}: {why}: she runs past it and the walk goes on; it is fought again only if it attacks her from within {reach:0.#} m (M6 14 run past, D137; {host.LastAttackWord()})"));
    }
}
