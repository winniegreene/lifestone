using System.Numerics;

namespace Lifestone.Client.Movement.Wayfinding;

/// <summary>One hostile creature on a run's creature layer (<see cref="NavCreatures"/>): where it stands and how near it notices the player.</summary>
/// <param name="Guid">The creature's object id.</param>
/// <param name="Centre">Where it stands, global metres (<see cref="NavPoint.Global"/>).</param>
/// <param name="Radius">The aggro disc's radius from the creature's centre to the player's, in metres (<see cref="NavCreatures.AggroRadius"/>).</param>
/// <param name="Z">Its height: its feet's z in its landblock's frame, the frame of a room route's points (M5 2 fix F2, 2-R6). A room route counts the creature only within <see cref="NavCreatures.StoreyBandMetres"/> of the route's height there
/// (<see cref="NavCreatures.SameStorey"/>); NaN, the default, is a creature whose height is not known, which counts at every height, and outdoors the height is never read.</param>
public readonly record struct NavCreature(uint Guid, Vector2 Centre, float Radius, float Z = float.NaN);

/// <summary>Where a run's creature layer comes from: the hostile creatures the client sees now (<see cref="HostileCreatureFeed"/>).</summary>
public interface INavCreatureFeed
{
    /// <summary>Clears <paramref name="into"/> and adds the hostile creatures the client sees now within <paramref name="maxMetres"/> of <paramref name="from"/> (global metres).</summary>
    /// <param name="from">The player, global metres.</param>
    /// <param name="maxMetres">How far to look.</param>
    /// <param name="into">The list to fill.</param>
    void Fill(Vector2 from, float maxMetres, List<NavCreature> into);
}

/// <summary>
/// A run's creature layer (nav-creature-avoid, docs/wayfinding.md "The creature layer"): the hostile creatures the client sees, each a disc the
/// planner costs and the runner keeps its shortcuts out of. Per run, refreshed as the creatures move, never written into the shared
/// <see cref="NavTile"/> (as the scars are not). The run's own goal creature (a hunt's seek) is left out by the caller (<see cref="Refresh"/>).
/// </summary>
/// <remarks>
/// <para><b>The disc.</b> ACE @95ef426d wakes an idle monster when the player comes within the monster's <c>VisualAwarenessRange</c>
/// (<c>Player.CheckMonsters</c>, Source/ACE.Server/WorldObjects/Player_Monster.cs:17-31, run as the player moves; <c>AlertMonster</c>,
/// Creature_Combat.cs:1121-1151), measured between the two cylinders (<c>PhysicsObj.get_distance_sq_to_object(obj, true)</c>,
/// Physics/PhysicsObj.cs:2519-2531) and scaled by the server property <c>mob_awareness_range</c> (1.0, PropertyManager.cs:658; the bundled
/// shard's value is 1). A creature without the property uses 18 m (<c>VisualAwarenessRange_Default</c>, Monster_Awareness.cs:377, "the most
/// common value from retail"). The client is not sent the property, so <see cref="CreatureAwareness"/> holds the hunt grounds' values. The
/// disc's radius is the awareness plus both bodies' radii (<see cref="AggroRadius"/>).</para>
/// <para><b>The cost.</b> A metre inside a disc costs <see cref="EdgeWeight"/> more at its edge, rising to <see cref="EdgeWeight"/> x (1 +
/// <see cref="CentreWeight"/>) at the creature, summed over overlapping discs: open ground round a disc is always cheaper than across it (a half
/// circle round is at most pi/2 of the chord), and a route that must cross (a creature in a gap) keeps as far from the creature as it can.
/// Soft only: a creature never makes a plan fail. Not retail: the weights are Lifestone's own.</para>
/// <para><b>Heights, indoors</b> (M5 2 fix F2, review 2-R6). The disc is flat, and a dungeon's storeys are stacked (the den's and the Tusker Temple's origins lie 6 m apart), so a disc projected onto every storey made a creature under a corridor
/// cost it. A room route's walk counts a creature only within <see cref="StoreyBandMetres"/> of the walk's height at that point (<see cref="SameStorey"/>, the overloads that take a height); a creature of unknown height counts at every height. Outdoors
/// nothing reads a height: the lattice's costs and the outdoor runs are unchanged. Approximate either way: ACE measures the awareness between the two cylinders in three dimensions (PhysicsObj.cs:2519 @95ef426d) and the client models no walls.</para>
/// <para>Not thread-safe: one per run, game thread.</para>
/// </remarks>
public sealed class NavCreatures
{
    /// <summary>The extra cost per metre at a disc's edge. Not retail: Lifestone's own (over pi/2 - 1, so a disc in open ground is always gone round).</summary>
    public const float EdgeWeight = 1f;

    /// <summary>How much more the extra cost is at the creature than at the edge (linear in the distance). Not retail: Lifestone's own.</summary>
    public const float CentreWeight = 4f;

    /// <summary>The player's body radius in metres, the tile's body (<see cref="NavBuildOptions.BodyRadius"/>).</summary>
    public const float PlayerRadiusMetres = 0.48f;

    /// <summary>
    /// The creature's own radius in metres when it is not read: the feed takes the creature's Setup radius times its scale (<see cref="HostileCreatureFeed"/>,
    /// closes nav-creature-radius) and falls back to this for a creature whose Setup it cannot read (logged once). 1 m is about a drudge's and less than a
    /// tusker's. Not retail: Lifestone's own.
    /// </summary>
    public const float UnknownCreatureRadiusMetres = 1f;

    /// <summary>The sampling step of <see cref="ExposureAlong(Vector2, Vector2)"/> in metres (the lattice sight's 0.5 m). Not retail: Lifestone's own.</summary>
    public const float SampleMetres = 0.5f;

    /// <summary>A dungeon storey's height in metres: how far apart the stacked storeys of the den and of the Tusker Temple lie (their profiles' storey origins are multiples of it). Not retail: Lifestone's own.</summary>
    public const float StoreyHeightMetres = 6f;

    /// <summary>
    /// How far above or below a room route's height a creature still counts for the route, in metres: a band one storey tall (<see cref="StoreyHeightMetres"/>) centred on the route, so the storey above and the storey below, a whole storey
    /// away, are outside it and a creature on the route's own storey (stairs and ramps within it are a few metres) is inside. Not retail: Lifestone's own.
    /// </summary>
    public const float StoreyBandMetres = StoreyHeightMetres / 2f;

    private readonly List<NavCreature> _all = [];

    /// <summary>The creatures on the layer.</summary>
    public IReadOnlyList<NavCreature> All => _all;

    /// <summary>The number of creatures on the layer.</summary>
    public int Count => _all.Count;

    /// <summary>The disc radius for a creature of <paramref name="awarenessMetres"/> and body <paramref name="creatureRadiusMetres"/>: centre to centre, both bodies added (ACE's cylinder distance).</summary>
    /// <param name="awarenessMetres">The creature's visual awareness range (<see cref="CreatureAwareness.MetresOf"/>).</param>
    /// <param name="creatureRadiusMetres">The creature's own radius.</param>
    public static float AggroRadius(float awarenessMetres, float creatureRadiusMetres = UnknownCreatureRadiusMetres) =>
        awarenessMetres + creatureRadiusMetres + PlayerRadiusMetres;

    /// <summary>The extra cost of a metre at <paramref name="distance"/> from a creature whose disc is <paramref name="radius"/>; 0 outside the disc.</summary>
    /// <param name="distance">The distance from the creature's centre in metres.</param>
    /// <param name="radius">The disc's radius in metres.</param>
    public static float CostPerMetre(float distance, float radius) =>
        distance >= radius || radius <= 0f ? 0f : EdgeWeight * (1f + (CentreWeight * (1f - (distance / radius))));

    /// <summary>Replaces the layer with <paramref name="creatures"/> (tests and hosts that keep their own list).</summary>
    /// <param name="creatures">The creatures.</param>
    public void Set(IEnumerable<NavCreature> creatures)
    {
        ArgumentNullException.ThrowIfNull(creatures);
        _all.Clear();
        _all.AddRange(creatures);
    }

    /// <summary>Refills the layer from <paramref name="feed"/> round <paramref name="from"/>, leaving out <paramref name="ignore"/> (the run's goal creature; 0 for none).</summary>
    /// <param name="feed">The feed.</param>
    /// <param name="from">The player, global metres.</param>
    /// <param name="maxMetres">How far to look.</param>
    /// <param name="ignore">The creature left out, or 0.</param>
    public void Refresh(INavCreatureFeed feed, Vector2 from, float maxMetres, uint ignore)
    {
        ArgumentNullException.ThrowIfNull(feed);
        feed.Fill(from, maxMetres, _all);
        if (ignore == 0)
        {
            return;
        }

        for (int i = _all.Count - 1; i >= 0; i--)
        {
            if (_all[i].Guid == ignore)
            {
                _all.RemoveAt(i); // no predicate: the runner refreshes twice a second and allocates nothing
            }
        }
    }

    /// <summary>Removes every creature.</summary>
    public void Clear() => _all.Clear();

    /// <summary>The extra cost of a metre at <paramref name="global"/>: the sum over the discs that hold it.</summary>
    /// <param name="global">The point, global metres.</param>
    public float CostAt(Vector2 global) => CostAt(global, 0);

    /// <summary>The extra cost of a metre at <paramref name="global"/> leaving out <paramref name="ignore"/> (a run's goal creature; 0 for none).</summary>
    /// <param name="global">The point, global metres.</param>
    /// <param name="ignore">The creature left out, or 0.</param>
    public float CostAt(Vector2 global, uint ignore) => Cost(global, float.NaN, ignore);

    /// <summary>True when a creature at height <paramref name="creatureZ"/> counts for a walk at <paramref name="z"/> indoors: within <see cref="StoreyBandMetres"/> of it, or of unknown height (NaN).</summary>
    /// <param name="creatureZ">The creature's height (<see cref="NavCreature.Z"/>).</param>
    /// <param name="z">The walk's height at the point, in the same frame.</param>
    public static bool SameStorey(float creatureZ, float z) => float.IsNaN(creatureZ) || MathF.Abs(creatureZ - z) <= StoreyBandMetres;

    /// <summary><see cref="CostAt(Vector2, uint)"/> for a walk indoors at height <paramref name="z"/>: only the creatures within <see cref="StoreyBandMetres"/> of it (<see cref="SameStorey"/>) cost anything.</summary>
    /// <param name="global">The point, global metres.</param>
    /// <param name="z">The walk's height at the point, in the creatures' frame.</param>
    /// <param name="ignore">The creature left out, or 0.</param>
    public float CostAt(Vector2 global, float z, uint ignore) => Cost(global, z, ignore);

    // NaN z: every creature counts (outdoors, and the flat overloads); otherwise only the creatures of the walk's storey.
    private float Cost(Vector2 global, float z, uint ignore)
    {
        float cost = 0f;
        foreach (var c in _all)
        {
            if ((ignore == 0 || c.Guid != ignore) && (float.IsNaN(z) || SameStorey(c.Z, z)))
            {
                cost += CostPerMetre(Vector2.Distance(global, c.Centre), c.Radius);
            }
        }

        return cost;
    }

    /// <summary>The extra cost of the straight walk from <paramref name="a"/> to <paramref name="b"/> (global metres), sampled every <see cref="SampleMetres"/>.</summary>
    /// <param name="a">The start.</param>
    /// <param name="b">The end.</param>
    public float ExposureAlong(Vector2 a, Vector2 b) => ExposureAlong(a, b, 0);

    /// <summary><see cref="ExposureAlong(Vector2, Vector2)"/> leaving out <paramref name="ignore"/> (a run's goal creature; 0 for none): the room graph's crossing cost.</summary>
    /// <param name="a">The start.</param>
    /// <param name="b">The end.</param>
    /// <param name="ignore">The creature left out, or 0.</param>
    public float ExposureAlong(Vector2 a, Vector2 b, uint ignore) => Exposure(a, float.NaN, b, float.NaN, ignore);

    /// <summary>
    /// <see cref="ExposureAlong(Vector2, Vector2, uint)"/> for a walk indoors from height <paramref name="za"/> at <paramref name="a"/> to <paramref name="zb"/> at <paramref name="b"/> (a ramp climbs between them): each sample counts only the creatures within
    /// <see cref="StoreyBandMetres"/> of the walk's height there (<see cref="SameStorey"/>), so a creature a storey above or below costs nothing. The room graph's crossing cost.
    /// </summary>
    /// <param name="a">The start, global metres.</param>
    /// <param name="za">The start's height.</param>
    /// <param name="b">The end, global metres.</param>
    /// <param name="zb">The end's height.</param>
    /// <param name="ignore">The creature left out, or 0.</param>
    public float ExposureAlong(Vector2 a, float za, Vector2 b, float zb, uint ignore) => Exposure(a, za, b, zb, ignore);

    private float Exposure(Vector2 a, float za, Vector2 b, float zb, uint ignore)
    {
        if (_all.Count == 0)
        {
            return 0f;
        }

        float length = Vector2.Distance(a, b);
        int n = Math.Max(1, (int)MathF.Ceiling(length / SampleMetres));
        float step = length / n;
        float sum = 0f;
        for (int s = 1; s <= n; s++)
        {
            float t = (float)s / n;
            sum += Cost(Vector2.Lerp(a, b, t), float.IsNaN(za) || float.IsNaN(zb) ? float.NaN : za + ((zb - za) * t), ignore) * step;
        }

        return sum;
    }

    /// <summary>The creature whose centre is nearest <paramref name="global"/> and its distance; false on an empty layer.</summary>
    /// <param name="global">The point, global metres.</param>
    /// <param name="nearest">The nearest creature.</param>
    /// <param name="metres">Its centre's distance.</param>
    public bool Nearest(Vector2 global, out NavCreature nearest, out float metres)
    {
        nearest = default;
        metres = float.MaxValue;
        foreach (var c in _all)
        {
            float d = Vector2.Distance(global, c.Centre);
            if (d < metres)
            {
                metres = d;
                nearest = c;
            }
        }

        return _all.Count > 0;
    }

    /// <summary>Copies the layer into <paramref name="buffer"/> (grown if needed, so a warm planner allocates nothing); returns the count.</summary>
    internal int CopyTo(ref NavCreature[] buffer)
    {
        if (buffer.Length < _all.Count)
        {
            Array.Resize(ref buffer, Math.Max(8, Math.Max(_all.Count, buffer.Length * 2)));
        }

        _all.CopyTo(buffer);
        return _all.Count;
    }
}
