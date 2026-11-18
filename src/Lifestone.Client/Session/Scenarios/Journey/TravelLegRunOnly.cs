using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Animation;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Data.World.Scenery;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 item 14 (the den run, the owner's decision of 2026-11-12: she does not stop to fight and runs straight): the run-only road of a travel leg, on while <see cref="JourneyFacts.RunOnly"/> is set (the den-run station sets it for its road).
/// <list type="bullet">
/// <item><b>No answer, no hold, no retreat.</b> <see cref="AttackerStopsWalk"/> stops no walk: an attacker is named once (<c>run-only: ... not answered</c>) and she runs on. The fight-back, its cap and the retreat (M6 4 F6, M6 6 E) are
/// never entered, so no walk is planned again after a fight and none goes back to its start. The life checks stay: a body at health 0 walks nowhere, and a move by the server is read for a death (<c>died:&lt;leg&gt;:respawned=</c>).</item>
/// <item><b>A drink stands her still, so it is the last resort.</b> Live 2026-11-13 (the first den run, called off): 44 drinks in 137 s on the 252 m to the Desert March Portal, one every 1.6 s for 10 to 15 missing health,
/// and 34 m covered in one 54 s stretch. A drink is no act on the move: ACE's <c>Player.ApplyConsumable</c> (Player_Use.cs:268-309 @95ef426d) marks the body busy (275), forces it to the NonCombat Ready stance and through the
/// drink motion and Ready again (286-304), and frees it only then (306); the client's own heal lane sends no move into a busy body. So <see cref="RunDrink"/> drinks only under <see cref="RunDrinkUnderShare"/> of the
/// health (<see cref="RunDrinkDue"/>): the biggest drink that fits whole (<see cref="HealthDrinks.Find"/>), one at a time, each a named stand of about 1.6 s. Above that line nothing stops her feet.</item>
/// <item><b>What she does not do.</b> No cast: a cast needs her to stand through its wind-up (a jump, a change of combat mode or a death fails it: Player.cs:918-923, Player_Combat.cs:787-788, Player_Death.cs:172-173; damage does not), and a
/// recall teleports 2 s after its cast lands (WorldObject_Magic.cs:1136-1141 and 1162-1175): about six seconds standing among creatures that caught a running body. So nothing stops her feet on the road, a recall away included; the
/// road's ends (a portal's Use, the den's door) are where she is out of reach.</item>
/// <item><b>Straight.</b> <see cref="RunStraight"/> walks an outdoor leg along its straight line in pieces of <see cref="StraightPieceMetres"/>, each the file's way (<see cref="AuthoredWaySource"/>): the route confirmer's physics checks each
/// piece before a step, and a piece the physics refuses hands the rest of the leg to the planner, by name.</item>
/// </list>
/// </summary>
internal abstract partial class TravelLeg
{
    /// <summary>Seconds one drink on the move is waited for before the next may be sent (Lifestone's own: live, a drink ends 2.5 s after its send, 3.5 s behind a cast; ACE holds the body busy until the motion ends).</summary>
    public const double RunDrinkSeconds = 5;

    /// <summary>
    /// The share of the maximum health under which a drink is worth its stand on the run-only road (Lifestone's own, from the first den run's log: a stand of 1.6 s among the hub's casters cost 16 to 44 points, and under this share
    /// the missing health takes the 50-point tincture whole).
    /// </summary>
    public const double RunDrinkUnderShare = 0.4;

    /// <summary>True when the health is low enough for a drink on the run-only road: under <see cref="RunDrinkUnderShare"/> of the maximum.</summary>
    /// <param name="health">The health now.</param>
    /// <param name="max">The maximum health.</param>
    public static bool RunDrinkDue(uint health, uint max) => max > 0 && health > 0 && health < max * RunDrinkUnderShare;

    /// <summary>The length of one piece of a straight run, in metres (Lifestone's own: under one landblock's 192 m, so the collision the confirmer probes is attached round the body).</summary>
    public const float StraightPieceMetres = 96f;

    /// <summary>A piece's end keeps this far inside its landblock, in metres (Lifestone's own: over the walk's arrival tolerance, so the check after reads the body in the point's landblock).</summary>
    public const float StraightEdgeMetres = 8f;

    private const float Block = (float)LandDefs.BlockLength;

    private readonly HashSet<uint> _ranFrom = [];
    private readonly List<uint> _runDrinkScratch = [];
    private HealHandle? _runDrink;
    private double _runDrinkAt;
    private uint _runDrinkHealth;
    private HealthDrink _runDrinkKind;
    private bool _runNoDrinkSaid;

    /// <summary>The attackers the run-only road did not answer (report-only).</summary>
    protected int RanFrom => _ranFrom.Count;

    /// <summary>The drinks drunk on the move (report-only).</summary>
    protected int RunDrinks { get; private set; }

    /// <summary>The pieces of straight runs walked by the file's way (report-only).</summary>
    protected int StraightPieces { get; private set; }

    /// <summary>The straight runs a refused piece handed to the planner (report-only).</summary>
    protected int StraightRefused { get; private set; }

    /// <summary>One frame of a run-only walk: the attacker named once, then the drink. Never a stop.</summary>
    private void RunOnFrame(string why)
    {
        if (WalkStandsDown())
        {
            return;
        }

        if (Attacker(out var guid, out var name, out var metres) && _ranFrom.Add(guid))
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"run-only: '{name}' 0x{guid:X8} attacked the player and stands {metres:0.0} m away: not answered, no stop, no retreat: she runs on ({why}); health {VitalWord(MageTemplate.MaxHealth)}; attackers not answered so far: {_ranFrom.Count}"));
        }

        if (guid != 0)
        {
            S.State.Objects.WalkAwareness.Attacked(guid); // M6 16 E1f: the walk-awareness shadow reads the run-only road's nearest attacker (report-only)
        }

        RunDrink();
    }

    /// <summary>
    /// The drink on the move, check, act, check: before, the health and the pack's health drinks (<see cref="HealthDrinks.Find"/>: the biggest that fits whole in the missing health; none that would overflow); the act, one
    /// <c>Drink</c>; after (its end, or <see cref="RunDrinkSeconds"/>), what ACE's line said it restored. With none held, one line the first time the health is under half.
    /// </summary>
    private void RunDrink()
    {
        var q = S.State.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out var max) || health == 0 || max == 0)
        {
            return;
        }

        if (_runDrink is { } sent)
        {
            if (!sent.IsCompleted && S.Clock.Now - _runDrinkAt < RunDrinkSeconds)
            {
                return;
            }

            var restored = sent.Result is { Kind: HealResultKind.Drank, Amount: > 0 } result ? result.Amount : 0;
            Log(string.Create(CultureInfo.InvariantCulture, $"run-only: drink: check after: {(sent.Outcome?.Kind.ToString() ?? string.Create(CultureInfo.InvariantCulture, $"no answer in {RunDrinkSeconds:0} s"))}: the {_runDrinkKind.Name} restored {restored} health (ACE's line); health {_runDrinkHealth} then {health} of {max} with the hits taken meanwhile; drinks on the move: {RunDrinks}"));
            _runDrink = null;
        }

        if (!RunDrinkDue(health, max))
        {
            return; // above the line a drink's stand costs more than it gives: she runs on
        }

        var missing = max > health ? max - health : 0u;
        if (!HealthDrinks.Find(S.State, _runDrinkScratch, missing, out var potion, out var kind, out var held, out var heldAll))
        {
            if (heldAll == 0 && !_runNoDrinkSaid)
            {
                _runNoDrinkSaid = true;
                Log(string.Create(CultureInfo.InvariantCulture, $"run-only: drink: health {health} of {max} and no health drink is held: nothing is done for it on the road; she runs on"));
            }

            return;
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"run-only: drink: check before: {kind.Name} 0x{potion:X8} (+{kind.Boost}, {held} held of {heldAll} health drink(s)) at health {health} of {max}, under {RunDrinkUnderShare * 100:0}% ({missing} missing: the biggest that fits whole): she stands for it"));
        _runDrink = S.Actions.Drink(potion, ActionSource.Automation);
        (_runDrinkAt, _runDrinkHealth, _runDrinkKind) = (S.Clock.Now, health, kind);
        RunDrinks++;
    }

    /// <summary>
    /// M6 item 14: an outdoor leg run straight, check, act, check. Before: the body and the point are outdoors and the dats give the ground under the line, else the planned walk takes the leg, by name. Act: pieces of
    /// <see cref="StraightPieceMetres"/> along the straight line from where the body stands, each a guarded walk by the file's way of one point (the confirmer's physics first). After each: the walk's own arrival check; a piece that fails
    /// with no name of its own (the physics refused the line, or the body was carried off it) hands the rest to the planner once (<c>run-only: straight: refused</c>); a death or a portal's touch ends the leg as any guarded walk's.
    /// </summary>
    /// <param name="leg">The leg's word in a failure's name.</param>
    /// <param name="name">The station's word for the walk.</param>
    /// <param name="label">The destination's label.</param>
    /// <param name="cell">The point's cell.</param>
    /// <param name="at">The point.</param>
    /// <param name="why">The purpose in words.</param>
    /// <param name="takenInto">The landblock the portal at the leg's end drops the body in, or null (<see cref="WalkGuarded"/>).</param>
    /// <param name="keepOff">The portals the walk keeps clear of, or null.</param>
    protected IEnumerable<ActionHandle?> RunStraight(string leg, string name, string label, uint cell, Vector3 at, string why, uint? takenInto = null, PortalKeepOff? keepOff = null)
    {
        var goal = NavMap.FromCellFrame(new CellFrame(cell, new AcFrame(at, Quaternion.Identity))).Global;
        var straight = LandDefs.IsOutdoorCell(cell) && Context.Dats is not null;
        if (!straight)
        {
            Log($"run-only: straight: {label}: the point is indoors or the dats are not at hand: the planned walk takes the leg");
        }

        for (var piece = 1; straight; piece++)
        {
            if (S.State.Self.Position is not { } here || !LandDefs.IsOutdoorCell(here.CellId))
            {
                Log($"run-only: straight: {label}: the body stands indoors or nowhere: the planned walk takes the rest");
                break;
            }

            var from = NavMap.FromCellFrame(here).Global;
            var left = Vector2.Distance(from, goal);
            if (left <= StraightPieceMetres + StraightEdgeMetres)
            {
                break; // the last piece is the walk to the point itself, below
            }

            if (PieceEnd(from, goal, left) is not { } end)
            {
                Log(string.Create(CultureInfo.InvariantCulture, $"run-only: straight: {label}: piece {piece}: no ground is read under the line {left:0} m from the point: the planned walk takes the rest"));
                StraightRefused++;
                straight = false;
                break;
            }

            var named = FailName;
            string? failed = null;
            var mark = TravelMark;
            foreach (var h in Catching(WalkGuarded(leg, name, string.Create(CultureInfo.InvariantCulture, $"{label}-piece-{piece}"), end.CellId, end.Frame.Origin, TravelPurpose.Cell, string.Create(CultureInfo.InvariantCulture, $"{why}; straight, piece {piece}, {left:0} m left"), takenInto, keepOff, way: [end]), f => failed = f))
            {
                yield return h;
            }

            if (WalkTakenFrom is not null)
            {
                yield break; // the portal at the leg's end took the body on touch: the caller reads it
            }

            if (failed is not null)
            {
                if (FailName != named || TeleportSince(mark))
                {
                    throw new InvalidOperationException(failed); // a death, or a move by the server: the walk's own name stands
                }

                Log(string.Create(CultureInfo.InvariantCulture, $"run-only: straight: refused: {label}: piece {piece} failed ({failed}): the planned walk takes the rest of the leg from {CellWord(S.State.Self.Position)}"));
                StraightRefused++;
                straight = false;
                break;
            }

            StraightPieces++;
        }

        foreach (var h in WalkGuarded(leg, name, label, cell, at, TravelPurpose.Cell, why, takenInto, keepOff, way: straight ? [new CellFrame(cell, new AcFrame(at, Quaternion.Identity))] : null))
        {
            yield return h;
        }
    }

    /// <summary>
    /// M6 item 14 (the den run's rehearsal): the point on the dats' ground <paramref name="shortMetres"/> before <paramref name="goal"/> on the straight line from <paramref name="from"/> (further back by steps of
    /// <see cref="StraightEdgeMetres"/> while it lies within that of its landblock's edge); null, with the reason in <paramref name="why"/>, when an end is in a room, the dats are not at hand, the line is no longer than that, or no
    /// ground is read there. A goal whose cell's low word is 0 is outdoors: a landblock_instance row of an outdoor object stores cell 0 (the Desert March Portal's 0x5B9C0000; live 2026-11-13, the first rehearsal read it as
    /// indoors and ended den-run:rehearse:no-ground).
    /// </summary>
    protected CellFrame? PointShortOf(CellFrame from, CellFrame goal, float shortMetres, out string why) => PointShortOf(Context.Dats, from, goal, shortMetres, out why);

    /// <summary>The rule of the instance overload, on the given dats (the dat test calls this one).</summary>
    internal static CellFrame? PointShortOf(Lifestone.Data.Dat.DatSet? dats, CellFrame from, CellFrame goal, float shortMetres, out string why)
    {
        why = string.Empty;
        if (dats is null)
        {
            why = "the dats are not at hand";
            return null;
        }

        if ((from.CellId & 0xFFFF) >= 0x100 || (goal.CellId & 0xFFFF) >= 0x100)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"an end stands in a room (from 0x{from.CellId:X8}, the goal 0x{goal.CellId:X8})");
            return null;
        }

        var a = NavMap.FromCellFrame(from).Global;
        var b = NavMap.FromCellFrame(goal).Global;
        var length = Vector2.Distance(a, b);
        if (length <= shortMetres + StraightEdgeMetres)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"the line is {length:0.0} m, no longer than {shortMetres:0} m and the edge band");
            return null;
        }

        var direction = (b - a) / length;
        for (var back = shortMetres; back <= shortMetres + (StraightEdgeMetres * 5) && back < length; back += StraightEdgeMetres)
        {
            var g = a + (direction * (length - back));
            var (bx, by) = ((int)MathF.Floor(g.X / Block), (int)MathF.Floor(g.Y / Block));
            var local = new Vector2(g.X - (bx * Block), g.Y - (by * Block));
            if (bx is < 0 or > 254 || by is < 0 or > 254 || local.X < StraightEdgeMetres || local.Y < StraightEdgeMetres || local.X > Block - StraightEdgeMetres || local.Y > Block - StraightEdgeMetres)
            {
                continue;
            }

            var coord = new LandblockCoord((byte)bx, (byte)by);
            if (CellIds.LandCellAt(coord, local.X, local.Y) is not { } landCell || dats.GetLandblockTerrain(coord) is not { } terrain)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"no ground is read in landblock ({bx}, {by}) at ({local.X:0.#}, {local.Y:0.#})");
                return null;
            }

            var z = new TerrainSampler(terrain, dats.TerrainRegion.LandHeightTable).GetHeight(local.X, local.Y);
            return new CellFrame(landCell, new AcFrame(new Vector3(local.X, local.Y, z), Quaternion.Identity));
        }

        why = "every point tried lies in a landblock's edge band";
        return null;
    }

    /// <summary>The end of the next piece: <see cref="StraightPieceMetres"/> along the line (further by steps of <see cref="StraightEdgeMetres"/> while it lies within that of its landblock's edge), on the dats' ground; null when no ground is read there.</summary>
    private CellFrame? PieceEnd(Vector2 from, Vector2 goal, float left)
    {
        var direction = (goal - from) / left;
        for (var along = StraightPieceMetres; along <= StraightPieceMetres + (StraightEdgeMetres * 5) && along < left; along += StraightEdgeMetres)
        {
            var g = from + (direction * along);
            var (bx, by) = ((int)MathF.Floor(g.X / Block), (int)MathF.Floor(g.Y / Block));
            var local = new Vector2(g.X - (bx * Block), g.Y - (by * Block));
            if (bx is < 0 or > 254 || by is < 0 or > 254 || local.X < StraightEdgeMetres || local.Y < StraightEdgeMetres || local.X > Block - StraightEdgeMetres || local.Y > Block - StraightEdgeMetres)
            {
                continue;
            }

            var coord = new LandblockCoord((byte)bx, (byte)by);
            if (CellIds.LandCellAt(coord, local.X, local.Y) is not { } landCell || Context.Dats!.GetLandblockTerrain(coord) is not { } terrain)
            {
                return null;
            }

            var z = new TerrainSampler(terrain, Context.Dats.TerrainRegion.LandHeightTable).GetHeight(local.X, local.Y);
            return new CellFrame(landCell, new AcFrame(new Vector3(local.X, local.Y, z), Quaternion.Identity));
        }

        return null;
    }
}
