using System.Collections.Frozen;
using Lifestone.Client.Session.Grounds;

namespace Lifestone.Client.Session.Vitals;

/// <summary>One creature's attack facts, as a ground profile records them (<see cref="CreatureFacts"/>).</summary>
/// <param name="Wcid">Its weenie class id.</param>
/// <param name="Name">Its name.</param>
/// <param name="Level">Its level (0: not given).</param>
/// <param name="MaxHealth">Its maximum health (0: not given).</param>
/// <param name="Hitter">Its hardest attack (<see cref="LadderHitter"/>: d_val, d_var and the attribute that scales it).</param>
/// <param name="DamageTypes">The DamageType bits of its attacks (0: not given; the line then reads the worst of the seven).</param>
/// <param name="Pack">The most of it one camp holds at once (1 for a dungeon's target: the ladder's surveyed pack is the place's).</param>
/// <param name="Where">Where the facts are recorded: <c>&lt;ground&gt;:targets</c> or <c>&lt;ground&gt;:approach</c>.</param>
public sealed record CreatureFact(uint Wcid, string Name, int Level, int MaxHealth, LadderHitter Hitter, uint DamageTypes, int Pack, string Where)
{
    /// <summary>M6 14 run past (D137): its Run skill, from an approach hostile's <c>pace</c>, or null.</summary>
    public int? RunSkill { get; init; }

    /// <summary>M6 14 run past: its motion table, or null.</summary>
    public uint? MotionTable { get; init; }

    /// <summary>M6 14 run past: its ObjScale, or null when no pace facts are recorded.</summary>
    public double? Scale { get; init; }

    /// <summary>M6 14 run past: its ranged attack, or null.</summary>
    public GroundRanged? Ranged { get; init; }
}

/// <summary>
/// M6 14 retreat attackers (live 2026-11-14 10:59:18 UTC: on the Holding's approach two Infected Assailers, acid, killed the climber while the run leg's line modelled the destination's Rampagers): the attack facts of every creature the
/// ground profiles record, by weenie class: each profile's targets that carry their attack's facts (<see cref="GroundTarget.DVal"/>, <see cref="GroundTarget.DVar"/>, the Strength of <see cref="GroundTarget.Attributes"/>) and each approach
/// hostile that does (<see cref="GroundApproachHostile.Hitter"/>), never only the destination's. A weenie recorded twice keeps the harder hit (the safe side). Pure after construction; a lookup allocates nothing.
/// </summary>
public sealed class CreatureFacts
{
    private static readonly Lazy<CreatureFacts> Catalog = new(() => Of(GroundCatalog.All));

    private readonly FrozenDictionary<uint, CreatureFact> _byWcid;

    private CreatureFacts(FrozenDictionary<uint, CreatureFact> byWcid) => _byWcid = byWcid;

    /// <summary>The facts of every profile of the catalog (<see cref="GroundCatalog.All"/>).</summary>
    public static CreatureFacts All => Catalog.Value;

    /// <summary>How many weenies carry facts.</summary>
    public int Count => _byWcid.Count;

    /// <summary>M6 16 E1a (the bestiary's facade): the weenie classes a ground profile records facts for (the table's are <see cref="Grounds.CreatureTable.All"/>).</summary>
    public IEnumerable<uint> Wcids => _byWcid.Keys;

    /// <summary>The facts of <paramref name="grounds"/>.</summary>
    /// <param name="grounds">The ground profiles.</param>
    public static CreatureFacts Of(IEnumerable<HuntingGround> grounds)
    {
        ArgumentNullException.ThrowIfNull(grounds);
        var found = new Dictionary<uint, CreatureFact>();
        foreach (var g in grounds)
        {
            foreach (var t in g.Targets)
            {
                if (t.DVal is { } dVal && t.DVar is { } dVar && t.Attributes is { } a)
                {
                    Keep(found, new CreatureFact(t.Wcid, t.Name, t.Level, t.MaxHealth, new LadderHitter(t.Name, t.Wcid, dVal, dVar, a.Strength), t.DamageTypes, 1, g.Key + ":targets"));
                }
            }

            foreach (var h in g.Approach?.Hostiles ?? [])
            {
                if (h.Hitter is { } hitter)
                {
                    Keep(found, new CreatureFact(h.Wcid, h.Name, h.Level ?? 0, h.MaxHealth ?? 0, hitter, h.DamageTypes, h.Pack, g.Key + ":approach") { RunSkill = h.RunSkill, MotionTable = h.MotionTable, Scale = h.RunSkill is null ? null : h.Scale, Ranged = h.Ranged });
                }
            }
        }

        return new CreatureFacts(found.ToFrozenDictionary());
    }

    /// <summary>
    /// The facts of weenie class <paramref name="wcid"/>: a ground profile's (they carry the pack, the pace and the throw), else, M6 14 the losing race (the owner's ruling, 2026-11-15: no creature is unknown, since the server
    /// database has them all), the world database's creature table's (<see cref="TableFact"/>); null only for a weenie the pinned world SQL does not hold.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    public CreatureFact? Find(uint wcid) => _byWcid.TryGetValue(wcid, out var fact) ? fact : TableFact(wcid);

    /// <summary>The facts a ground profile records for weenie class <paramref name="wcid"/>, or null (the table is not read).</summary>
    /// <param name="wcid">The weenie class id.</param>
    public CreatureFact? Recorded(uint wcid) => _byWcid.TryGetValue(wcid, out var fact) ? fact : null;

    /// <summary>The named TODO of a creature the world database records no physical attack for: what it does to her is measured only (its spells, if it casts).</summary>
    public const string NoAttackTodo = "TODO(m6-creature-spell-attack)";

    private static readonly Lazy<FrozenDictionary<uint, CreatureFact>> FromTable = new(() => Grounds.CreatureTable.All.Select(OfRow).ToFrozenDictionary(f => f.Wcid));

    /// <summary>
    /// M6 14 the losing race: the facts of <paramref name="wcid"/> from the world database's creature table (<see cref="Grounds.CreatureTable"/>): its hardest physical attack as ACE deals it (<see cref="Grounds.CreatureRow.Attack"/>), its
    /// level, maximum health and damage types, a pack of 1 (the measured hits cover a larger one live); a creature with no physical attack recorded gets a hit of 0 and the line says so (<see cref="NoAttackTodo"/>). Null when the table
    /// does not hold it. Built once for every row; a lookup allocates nothing.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    public static CreatureFact? TableFact(uint wcid) => FromTable.Value.TryGetValue(wcid, out var fact) ? fact : null;

    private static CreatureFact OfRow(Grounds.CreatureRow row)
    {
        var attack = row.Attack;
        var where = attack is null
            ? $"the world database's creature table: no melee or missile attack recorded, its hits measured only, {NoAttackTodo}"
            : $"the world database's creature table, {attack.Kind}";
        return new CreatureFact(row.Wcid, row.Name, row.Level, row.MaxHealth, attack?.Hitter ?? new LadderHitter(row.Name, row.Wcid, 0, 0, 0), row.DamageTypes, 1, where);
    }

    /// <summary>
    /// M6 14 walk drains (the owner, 2026-11-15: the target's health and kind are known data, and the server's database holds every creature): the maximum health of weenie class <paramref name="wcid"/>, the profiles' facts first
    /// (a target's or an approach hostile's <c>max_health</c>), then the world database's table (<see cref="Grounds.CreatureTable"/>); <paramref name="source"/> names which. Null only when neither holds it: a weenie that is no
    /// creature of the world database's table; the caller names it. M6 16 E1e: a creature weenie with no MaxHealth row or no Endurance has the health ACE gives it (<see cref="Grounds.CreatureTable.MaxHealthOf"/>), where it read
    /// null before.
    /// </summary>
    /// <param name="wcid">The weenie class id.</param>
    /// <param name="source">Where the number comes from, in words.</param>
    public int? MaxHealthOf(uint wcid, out string source)
    {
        if (_byWcid.TryGetValue(wcid, out var fact) && fact.MaxHealth > 0)
        {
            source = fact.Where + " max_health";
            return fact.MaxHealth;
        }

        if (Grounds.CreatureTable.Find(wcid) is { } row)
        {
            // M6 16 E1e: every row has a maximum health, ACE's; where the weenie does not state it whole, the source says which of ACE's rules stands in it
            source = row.MaxHealthNote is { } note ? "the world database's creature table; " + note : "the world database's creature table";
            return row.MaxHealth;
        }

        source = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"TODO(m6-creature-health): weenie {wcid} is no creature of the world database's table");
        return null;
    }

    private static void Keep(Dictionary<uint, CreatureFact> found, CreatureFact fact)
    {
        if (!found.TryGetValue(fact.Wcid, out var had))
        {
            found[fact.Wcid] = fact;
        }
        else if (FightLines.MaxHit(fact.Hitter) > FightLines.MaxHit(had.Hitter))
        {
            found[fact.Wcid] = fact with { RunSkill = fact.RunSkill ?? had.RunSkill, MotionTable = fact.MotionTable ?? had.MotionTable, Scale = fact.Scale ?? had.Scale, Ranged = fact.Ranged ?? had.Ranged };
        }
        else if ((had.RunSkill is null && fact.RunSkill is not null) || (had.Ranged is null && fact.Ranged is not null))
        {
            // M6 14 run past: the pace and the throw join the kept facts
            found[fact.Wcid] = had with { RunSkill = had.RunSkill ?? fact.RunSkill, MotionTable = had.MotionTable ?? fact.MotionTable, Scale = had.Scale ?? fact.Scale, Ranged = had.Ranged ?? fact.Ranged };
        }
    }
}
