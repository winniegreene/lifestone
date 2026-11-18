using System.Globalization;
using System.Runtime.CompilerServices;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Mage;

/// <summary>What she casts on a ground (<see cref="CastElement.OnGround"/>).</summary>
/// <param name="Element">The element that deals most of her damage there (a key of <see cref="WarSpellData.BoltLineIds"/>), or null when the fight's choice is not known.</param>
/// <param name="Share">That element's share of her damage on the ground, 0 to 1 (0 when unknown).</param>
/// <param name="Source">How it was read, or why it is not known, for a log line.</param>
public sealed record GroundCast(string? Element, double Share, string Source);

/// <summary>
/// M6 16 E5e3 (docs/automation-engine.md section 2.5; a live defect of 2026-11-18: she cast the fire line at Tusker Slaves holding a piercing sceptre, a fire wand in her pack): the one answer to "what element does she cast
/// here", one a session. Two deciders did not talk: the fight chose each target's line from the creature's resistances and her book (<see cref="BoltElement"/>), and the wear judge was told the element by a ladder rung's label
/// or a ground's plan row, which are both empty past a ground's own rows (level 82 on the Tusker Quarters, an alternate of the Holding's rung whose plan ends at 74): the pick then counted no caster's bonus on her bolts.
/// <para><b>One function a creature</b> (<see cref="BestOn"/>): the line that hits a creature hardest from her book, at a cast chance and under a cap. The fight asks it when a target is begun; the ground's answer asks it of
/// every creature of the ground.</para>
/// <para><b>The ground's answer</b> (<see cref="OnGround"/>): the element that deals most of her damage there. Her damage on a kind is what its kills take: its maximum health (the bestiary's; the profile's where the
/// bestiary holds none) times how many of the kind there are for one of the others (an outdoor ground's spawn mass; an indoor profile holds no count by kind, so each target kind counts once, named when the kinds disagree:
/// <c>TODO(m6-ground-kind-counts)</c>). Each kind's damage goes to the element <see cref="BestOn"/> gives for it; a kind every line does nothing to is left out. The largest share wins; equal shares go to the fight's first
/// line (the ground's planned line, else fire: <c>FightScenario.StartElement</c>).</para>
/// <para><b>The terms</b> are the fight's: the chance and the cap it stated at its start on that ground this session (<see cref="StateFight"/>); before a fight has stated them, the chance the job's fight casts at (the
/// caller's: <c>Wardrobe.KeepCastChance</c>) and no cap (every level the chance allows, as the ladder's live cap).</para>
/// <para>Not known (null) when the ground names no creature, her War Magic or her book is not read, or no line is castable: the caller then falls back to the rung's label and the plan's family and says so.</para>
/// </summary>
public sealed class CastElement
{
    private static readonly ConditionalWeakTable<GameState, CastElement> PerSession = new();
    private readonly GameState _state;
    private readonly Dictionary<string, (double Chance, int Cap)> _fights = new(StringComparer.Ordinal);

    private CastElement(GameState state) => _state = state;

    /// <summary>The answer of a session, made on first use.</summary>
    /// <param name="state">The session's read model.</param>
    public static CastElement For(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return PerSession.GetValue(state, static s => new CastElement(s));
    }

    /// <summary>The fight states the terms it casts by on its ground, at its start.</summary>
    /// <param name="groundKey">The fight's ground.</param>
    /// <param name="chance">Its cast chance.</param>
    /// <param name="cap">The highest level it casts, 1 to 7.</param>
    public void StateFight(string groundKey, double chance, int cap)
    {
        ArgumentNullException.ThrowIfNull(groundKey);
        _fights[groundKey] = (chance, Math.Clamp(cap, 1, FightScenario.Levels));
    }

    /// <summary>True when a fight on <paramref name="groundKey"/> stated its terms this session: it has a line in hand there.</summary>
    /// <param name="groundKey">The ground.</param>
    public bool FoughtOn(string groundKey) => _fights.ContainsKey(groundKey);

    /// <summary>
    /// The line that hits the creature of weenie class <paramref name="wcid"/> hardest from her book now (<see cref="BoltElement.Best"/> on her current War Magic), or null: no War Magic read, or no castable level of any line.
    /// A level is castable when her book holds it (a session with no book read holds every one: the arena's run) and <paramref name="dropped"/> does not name it. Each line's hit counts the caster in her hands
    /// (<see cref="CasterWar.Held"/>, M6 16 Q4): a plain caster, or none, is x1 on every line.
    /// </summary>
    /// <param name="state">The read model.</param>
    /// <param name="wcid">The creature's weenie class.</param>
    /// <param name="spell">The SpellTable's spell of an id, or null.</param>
    /// <param name="chance">The cast chance.</param>
    /// <param name="cap">The highest level cast, 1 to 7.</param>
    /// <param name="dropped">True for a spell the asking fight has dropped, or null for none.</param>
    /// <param name="current">The element in hand, which wins a tie, or null.</param>
    /// <param name="caster">The caster in her hands as read by the caller (<see cref="CasterWar.Held"/>), or null to read it now.</param>
    public static BoltPick? BestOn(GameState state, uint wcid, Func<uint, SpellInfo?> spell, double chance, int cap, Func<uint, bool>? dropped = null, string? current = null, CasterWar? caster = null) =>
        LinesOn(state, wcid, spell, chance, cap, dropped, current, caster) is { Count: > 0 } lines ? lines[0] : null;

    /// <summary>Every line on <paramref name="wcid"/>, the hardest first (<see cref="BestOn"/>'s first), or empty: no War Magic read, or no castable level of any line.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="wcid">The creature's weenie class.</param>
    /// <param name="spell">The SpellTable's spell of an id, or null.</param>
    /// <param name="chance">The cast chance.</param>
    /// <param name="cap">The highest level cast, 1 to 7.</param>
    /// <param name="dropped">True for a spell the asking fight has dropped, or null for none.</param>
    /// <param name="current">The element in hand, which wins a tie, or null.</param>
    /// <param name="caster">The caster in her hands as read by the caller, or null to read it now.</param>
    public static IReadOnlyList<BoltPick> LinesOn(GameState state, uint wcid, Func<uint, SpellInfo?> spell, double chance, int cap, Func<uint, bool>? dropped = null, string? current = null, CasterWar? caster = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(spell);
        if (!GearCasts.TryCurrentSkill(state.Self.Qualities, MageTemplate.WarMagic, out var skill, out _))
        {
            return [];
        }

        var book = state.Self.Spellbook;
        return BoltElement.Lines(wcid, spell, id => (book is not { IsKnown: true } || book.Knows(id)) && !(dropped?.Invoke(id) ?? false), skill, chance, cap, current, caster ?? CasterWar.Held(state));
    }

    /// <summary>The element that deals most of her damage on <paramref name="ground"/> (see the class), with how it was read.</summary>
    /// <param name="ground">The ground.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="jobChance">The chance the job's fight casts at, read while no fight has stated its own on this ground.</param>
    public GroundCast OnGround(HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells, double jobChance)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(spells);
        var kinds = Kinds(ground);
        if (kinds.Count == 0)
        {
            return new GroundCast(null, 0, $"{ground.Key} names no creature she fights (no target, no outdoor table)");
        }

        if (!GearCasts.TryCurrentSkill(_state.Self.Qualities, MageTemplate.WarMagic, out _, out var noWar))
        {
            return new GroundCast(null, 0, "War Magic is not read: " + noWar);
        }

        if (_state.Self.Spellbook is { IsKnown: false })
        {
            return new GroundCast(null, 0, "the spellbook has not arrived (no PlayerDescription)");
        }

        var stated = _fights.TryGetValue(ground.Key, out var fight);
        var (chance, cap) = stated ? fight : (jobChance, FightScenario.Levels);
        var terms = string.Create(CultureInfo.InvariantCulture, $"chance {chance:0.##}, levels up to {ScrivenerLearnPlan.Numeral(cap)}: {(stated ? "the terms the fight stated there this session" : "the job's fight's chance, no fight has stated its cap there yet")}");
        var first = BoltElement.ElementOf(ground.PlanFor(_state.Self.Qualities.Level)?.Family ?? string.Empty) ?? "fire";
        SpellInfo? Spell(uint id) => spells.TryGetValue(id, out var info) ? info : null;
        var held = CasterWar.Held(_state); // M6 16 Q4: the fight's line counts the caster in her hands, so the ground's answer does too
        var shares = new Dictionary<string, double>(StringComparer.Ordinal);
        var notes = new List<string>();
        var (total, uncounted, immune) = (0.0, false, 0);
        foreach (var kind in kinds)
        {
            if (BestOn(_state, kind.Wcid, Spell, chance, cap, null, first, held) is not { } pick)
            {
                return new GroundCast(null, 0, $"her book holds no bolt line castable at {terms}");
            }

            if (pick.Expected <= 0)
            {
                immune++; // every line she can cast does nothing to it: none of her damage is dealt there (the fight names it, TODO(m6-element-immune))
                continue;
            }

            var health = Bestiary.Find(kind.Wcid)?.Vitals.MaxHealth is { } h and > 0 ? h : kind.MaxHealth;
            uncounted |= health <= 0;
            var weight = (health > 0 ? health : 1) * kind.Mass;
            shares[pick.Element] = shares.GetValueOrDefault(pick.Element) + weight;
            total += weight;
            if (notes.Count < 4)
            {
                notes.Add(string.Create(CultureInfo.InvariantCulture, $"{kind.Name} ({kind.Wcid}): {pick.SpellName} x{pick.Resist:0.###}, health {health}{(kind.Counted ? string.Create(CultureInfo.InvariantCulture, $", spawn mass {kind.Mass:0.####}") : string.Empty)}"));
            }
        }

        if (total <= 0)
        {
            return new GroundCast(null, 0, $"every line she can cast does nothing to the {immune} kind(s) of {ground.Key} ({terms})");
        }

        var best = shares.OrderByDescending(s => Math.Round(s.Value, 6)).ThenBy(s => s.Key == first ? 0 : s.Key == "fire" ? 1 : 2).ThenBy(s => s.Key, StringComparer.Ordinal).First();
        var share = best.Value / total;
        var mixed = shares.Count > 1
            ? "; the other lines: " + string.Join(", ", shares.Where(s => s.Key != best.Key).OrderByDescending(s => s.Value).Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Key} {s.Value / total * 100:0.#}%")))
                + (kinds.Any(k => !k.Counted) ? "; the profile holds no count by kind, so each target kind counts once: TODO(m6-ground-kind-counts)" : string.Empty)
            : string.Empty;
        var source = string.Create(CultureInfo.InvariantCulture, $"the fight's own choice on {ground.Key}: {best.Key} deals {share * 100:0.#}% of her damage there, by each kind's hardest line from her book and its maximum health ({string.Join("; ", notes)}{(kinds.Count > notes.Count + immune ? $"; and {kinds.Count - notes.Count - immune} more" : string.Empty)}{(immune > 0 ? $"; {immune} kind(s) no line harms left out" : string.Empty)}{(uncounted ? "; a kind with no maximum health read counts as 1: TODO(m6-creature-health)" : string.Empty)}{mixed}; {terms}{(held.Plain && (held.Read || held.Guid == 0) ? string.Empty : "; the caster in her hands: " + held.Note)})");
        return new GroundCast(best.Key, share, source);
    }

    /// <summary>The creatures she fights on a ground: its targets, and an outdoor ground's table by its spawn mass.</summary>
    private static List<(uint Wcid, string Name, int MaxHealth, double Mass, bool Counted)> Kinds(HuntingGround ground)
    {
        var kinds = new List<(uint, string, int, double, bool)>();
        foreach (var t in ground.Targets)
        {
            if (t.Role == GroundRole.Target)
            {
                kinds.Add((t.Wcid, t.Name, t.MaxHealth, 1.0, false));
            }
        }

        if (ground.Outdoor is { } outdoor)
        {
            foreach (var c in outdoor.Creatures)
            {
                if (c.SpawnMass > 0 && !kinds.Exists(k => k.Item1 == c.Wcid))
                {
                    kinds.Add((c.Wcid, c.Name, c.MaxHealth, c.SpawnMass, true));
                }
            }
        }

        return kinds;
    }
}
