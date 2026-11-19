using System.Globalization;
using System.Numerics;
using Lifestone.Client.Movement.Wayfinding;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// The portal hop (M6 item 4 t2): one portal of the travel file, or a ground's entry or exit, used by play with check, act, check (M5 D141). The leg's helpers live on <see cref="TravelLeg"/> so every travel station may hop.
/// <list type="number">
/// <item><b>Check</b>: the body is present and stands in the portal's From place (its landblocks); the level passes the portal's gate (<see cref="RungPortal.Admits"/>), else the named refusal <c>gate:&lt;key&gt;:&lt;level&gt;&lt;&lt;min&gt;</c>
/// before any walk.</item>
/// <item><b>Act</b>: on foot to a point in front of the portal (<see cref="TravelLeg.WalkToPoint"/>, <see cref="TravelPurpose.Cell"/>: over the Town Network's and the Facility Hub's rooms indoors, clear of every other portal the travel data knows, M6 7 AI: <see cref="RungPortal.KeepOffTheOthers"/>; a walk that cannot keep clear fails <c>&lt;key&gt;:portal-keep-off</c>), the portal found by its guid, else by its weenie nearest its
/// origin, then <see cref="TravelPurpose.Seek"/> to it within <see cref="DenTravelStation.PortalRangeMetres"/>, peace, and <c>Use</c>. Use on a portal is the server's: ACE's <c>Portal.CheckUseRequirements</c> refuses a level below MinLevel
/// with WeenieError 0x04AB "You are not powerful enough to interact with that portal!" (@95ef426d WorldObjects/Portal.cs:166-170; the text WeenieErrorText 0x04AB) and above MaxLevel with 0x04AC (172-176); a Use within 3.5 s of the last
/// portal is 0x04A7 (136-155); <c>ActOnUse</c> teleports to the destination (269-290). A portal that takes the body on touch during the walk (ACE <c>OnCollideObject</c>, 103-106) is <c>taken=yes</c>: no Use is sent.</item>
/// <item><b>Check</b>: a teleport began and the body is present in the portal's destination landblock, else named: <c>&lt;key&gt;:no-teleport</c>, <c>&lt;key&gt;:not-present</c>, <c>&lt;key&gt;:wrong-landblock</c>.</item>
/// <item><b>One named recovery</b>: a Use with no teleport (or 0x04A7) walks up and uses the portal once more (<c>recovery=use-again</c>, as <c>AcademyStation.UseAndTeleport</c>); a second miss fails by name. A gate refusal read
/// from the server's text is not retried.</item>
/// </list>
/// </summary>
internal abstract partial class TravelLeg
{
    /// <summary>A body nearer than this to the portal's origin (planar, metres, its landblock) needs no walk to the front first (Lifestone's own).</summary>
    protected internal const double HopNearMetres = 8.0;

    /// <summary>The front point's distance from the portal's origin toward the approach, metres (the same 3 m as <see cref="Grounds.GroundPortal.Front"/>'s rule; the point is on the side the placement's rotation faces when the travel file gives it, else toward the approach).</summary>
    protected const float HopFrontMetres = 3.0f;

    /// <summary>The <c>front=</c> word when the travel file gave the portal's facing (the placement's rotation).</summary>
    internal const string FrontBasisFacing = "facing";

    /// <summary>The <c>front=</c> word when the file gave none and the front point is on the approach side (T2's rule).</summary>
    internal const string FrontBasisApproach = "approach";

    /// <summary>M6 7 U: the <c>front=</c> word of an indoor portal with no facing: the walk goes to the portal itself.</summary>
    internal const string FrontBasisPortal = "portal";

    /// <summary>A portal found by weenie must stand this near its origin, metres (Lifestone's own).</summary>
    protected const double HopFindMetres = 30.0;

    /// <summary>Seconds after a Use for a teleport or a refusal (ACE moves the body to the portal first; Lifestone's own).</summary>
    protected const double HopUseSeconds = 15.0;

    /// <summary>Seconds waited before the use-again after ACE's 0x04A7 (its 3.5 s, Portal.cs:120, plus a margin).</summary>
    protected const double HopTooRecentSeconds = 4.0;

    /// <summary>The short name of the failure a hop or a rung leg named (the verdict's <c>fail=</c>), or null.</summary>
    protected string? FailName { get; set; }

    /// <summary>The step a hop is in (<c>&lt;key&gt;:walk</c>, <c>&lt;key&gt;:use</c>): the <c>fail=</c> name when a helper's own check throws.</summary>
    protected string? HopStep { get; set; }

    /// <summary>M6 8 K: a portal's Use is sent: the server walks her to it and the teleport comes; the survival layer stands down until it does.</summary>
    public string? Committed => HopStep is { } step && step.EndsWith(":use", StringComparison.Ordinal) ? "the hop " + step : null;

    /// <summary>Fails the leg with a short name and its detail (the leg ends Failed, <c>FAILED: &lt;stage&gt;: &lt;name&gt;: &lt;detail&gt;</c>).</summary>
    /// <param name="name">The failure's name.</param>
    /// <param name="detail">What was seen.</param>
    protected void Fail(string name, string detail)
    {
        FailName ??= name;
        throw new InvalidOperationException(name + ": " + detail);
    }

    /// <summary>What a hop did (the leg's line reads it).</summary>
    protected sealed class HopOutcome
    {
        /// <summary>The portal object used.</summary>
        public uint Portal { get; set; }

        /// <summary>True when the portal took the body on touch during the walk (no Use was sent).</summary>
        public bool Taken { get; set; }

        /// <summary>The use-again recoveries made.</summary>
        public int Recoveries { get; set; }

        /// <summary>True when the walk to the facing side's front point failed and the approach side's was walked instead (the recovery <c>front-wall</c>, M6 4 w6a).</summary>
        public bool FrontWall { get; set; }

        /// <summary>The planar metres walked before the Use (global for outdoor cells, local within one landblock).</summary>
        public double WalkMetres { get; set; }
    }

    /// <summary>The level the character's own property says (PropertyInt 25).</summary>
    protected int CharacterLevel => S.State.Self.Qualities.Level;

    /// <summary>A portal's true cell: an outdoor row's 0xLLLL0000 resolved to its land cell at <paramref name="at"/>.</summary>
    /// <param name="cell">The stored cell.</param>
    /// <param name="at">The landblock-local point.</param>
    protected static uint CellAt(uint cell, Vector3 at) =>
        (cell & 0xFFFF) < 0x100 ? CellIds.LandCellAt(LandblockCoord.FromId(cell), Math.Clamp(at.X, 0.01f, 191.99f), Math.Clamp(at.Y, 0.01f, 191.99f)) ?? cell : cell;

    /// <summary>
    /// Planar metres between two positions: by global coordinates when both are outdoor (landblock x and y times 192 plus the local point), by local coordinates within one landblock, else <see cref="double.NaN"/> (two interiors cannot be
    /// compared).
    /// </summary>
    /// <param name="a">One position.</param>
    /// <param name="b">The other.</param>
    protected static double MetresBetween(CellFrame a, CellFrame b)
    {
        var ga = Global(a.CellId, a.Origin);
        var gb = Global(b.CellId, b.Origin);
        if (ga is { } p && gb is { } q)
        {
            return Vector2.Distance(p, q);
        }

        return a.CellId >> 16 == b.CellId >> 16 ? Vector2.Distance(new Vector2(a.Origin.X, a.Origin.Y), new Vector2(b.Origin.X, b.Origin.Y)) : double.NaN;
    }

    private static Vector2? Global(uint cell, Vector3 at) =>
        (cell & 0xFFFF) < 0x100 ? new Vector2(((cell >> 24) * 192f) + at.X, (((cell >> 16) & 0xFF) * 192f) + at.Y) : null;

    /// <summary>The portal object: <paramref name="guid"/> when the client knows it, else the object of <paramref name="wcid"/> nearest <paramref name="origin"/> in <paramref name="cell"/>'s landblock within <see cref="HopFindMetres"/>; 0 when none.</summary>
    protected uint FindPortalObject(uint guid, uint wcid, uint cell, Vector3 origin)
    {
        if (guid != 0 && S.State.Objects.TryGet(guid, out var known) && known.Position is not null)
        {
            return guid;
        }

        var best = 0u;
        var bestMetres = HopFindMetres;
        foreach (var o in S.State.Objects.All)
        {
            if (o.Weenie.WeenieClassId != wcid || o.Position is not { } at || at.CellId >> 16 != cell >> 16)
            {
                continue;
            }

            var metres = Vector2.Distance(new Vector2(at.Origin.X, at.Origin.Y), new Vector2(origin.X, origin.Y));
            if (metres <= bestMetres)
            {
                best = o.Guid;
                bestMetres = metres;
            }
        }

        return best;
    }

    /// <summary>
    /// Waits for the teleport that began since <paramref name="travelMark"/> to end present in <paramref name="landblock"/>, failing by name (<c>&lt;prefix&gt;:no-teleport</c>, <c>&lt;prefix&gt;:not-present</c>,
    /// <c>&lt;prefix&gt;:wrong-landblock</c>), then 2 s. <see cref="ArriveIn"/>'s checks with the failures named for the verdict.
    /// </summary>
    protected IEnumerable<ActionHandle?> ArriveNamed(uint landblock, int travelMark, string prefix)
    {
        foreach (var h in Until(() => TeleportSince(travelMark), TeleportStartSeconds))
        {
            yield return h;
        }

        if (!TeleportSince(travelMark))
        {
            Fail(prefix + ":no-teleport", $"no teleport began within {TeleportStartSeconds:0} s");
        }

        foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present, PresentSeconds))
        {
            yield return h;
        }

        if (S.State.Self.Travel != SelfTravel.Present)
        {
            Fail(prefix + ":not-present", $"the player was not present within {PresentSeconds:0} s of the teleport (travel {S.State.Self.Travel})");
        }

        var cell = S.State.Self.Position?.CellId ?? 0;
        if (cell >> 16 != landblock)
        {
            Fail(prefix + ":wrong-landblock", string.Create(CultureInfo.InvariantCulture, $"the player arrived in cell 0x{cell:X8}, not in landblock 0x{landblock:X4}"));
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"{prefix}: check after: present in cell 0x{cell:X8} (landblock 0x{landblock:X4})"));
        foreach (var h in Pause(2))
        {
            yield return h;
        }
    }

    /// <summary>One portal used by play with check, act, check and one named recovery (see the file's summary). A body within <see cref="HopNearMetres"/> of the portal walks to no front first.</summary>
    /// <param name="portal">The portal.</param>
    /// <param name="outcome">What the hop did.</param>
    /// <param name="why">The hop's purpose (logged, M5 D47).</param>
    protected IEnumerable<ActionHandle?> HopThrough(RungPortal portal, HopOutcome outcome, string why)
    {
        ArgumentNullException.ThrowIfNull(portal);
        var key = portal.Key;
        HopStep = key + ":check";
        var start = S.State.Self.Position;
        var level = CharacterLevel;
        Log(string.Create(CultureInfo.InvariantCulture, $"hop {key}: check before: purpose: {why}; level {level}, gate {portal.MinLevel?.ToString(CultureInfo.InvariantCulture) ?? "-"}..{portal.MaxLevel?.ToString(CultureInfo.InvariantCulture) ?? "-"}; the player stands in {(start is { } s ? $"cell 0x{s.CellId:X8}" : "no cell")}, travel {S.State.Self.Travel}"));
        if (portal.Refusal(level) is { } refused)
        {
            Fail(refused, $"level {level} does not pass {portal.Name} (refused before any walk)");
        }

        if (start is not { } from || S.State.Self.Travel != SelfTravel.Present)
        {
            Fail(key + ":not-present", "the player is not present in the world");
            yield break;
        }

        var life = ReadLife(HopLegKind, "hop " + key); // M6 4 F6: check before: health and alive, the marks the death check after a move reads
        StartAttackWatch(); // M6 8 G: the hop's walks stop for an attacker (M5 D88); from the Use to the arrival nothing walks, so nothing stops

        if (portal.FromLandblocks.Count > 0 && !portal.FromLandblocks.Contains((ushort)(from.CellId >> 16)))
        {
            Fail(key + ":not-at-start", string.Create(CultureInfo.InvariantCulture, $"the player stands in landblock 0x{from.CellId >> 16:X4}, not in the portal's place ({string.Join(", ", portal.FromLandblocks.Select(l => "0x" + l.ToString("X4", CultureInfo.InvariantCulture)))})"));
        }

        foreach (var h in WalkInOf(portal, why))
        {
            yield return h;
        }

        if (portal.WalkIn is { Count: > 0 })
        {
            from = S.State.Self.Position ?? from; // M6 8 B1: the near walk and the front are read from the end of the way in
        }

        var portalCell = CellAt(portal.Cell, portal.Origin);
        var keepOff = portal.KeepOffTheOthers(); // M6 7 AI: from level 15 a neighbour in the hub's ring takes a body on touch
        var frontKeep = portal.KeepOffForFront(); // M6 14: and the walk to the front keeps off the portal itself, by its touch and her stop
        _walkedIn = false;
        var touchMark = TravelMark;
        var touched = false;
        var near = from.CellId >> 16 == portalCell >> 16 && Vector2.Distance(new Vector2(from.Origin.X, from.Origin.Y), new Vector2(portal.Origin.X, portal.Origin.Y)) <= HopNearMetres;
        if (!near)
        {
            HopStep = key + ":walk";
            var (frontCell, front, frontBasis, frontWhy) = HopFront(portal, portalCell, from, frontKeep, FrontStands);

            (frontCell, front) = FrontRoom(key, frontCell, front, out var inNoRoom);
            Log(string.Create(CultureInfo.InvariantCulture, $"hop {key}: front point 0x{frontCell:X8} ({front.X:0.0}, {front.Y:0.0}) front={frontBasis}{frontWhy}{(inNoRoom ? " in-no-room" : string.Empty)}"));
            string? facingFailure = null;
            string? walkFailure = null;
            var named = FailName; // M6 8 G: a name a guarded walk gave (a death, a retreat) ends the hop; no recovery of the walk answers it
            if (inNoRoom && frontBasis == FrontBasisFacing)
            {
                // M6 4 f3: the facing side's point stands in no room of the portal's landblock (in the wall behind a Town Network portal's alcove): the body cannot stand there, so the
                // recovery front-wall goes to the approach side at once, with no walk that must fail first.
                facingFailure = string.Create(CultureInfo.InvariantCulture, $"front-in-no-room: the facing side's point ({front.X:0.0}, {front.Y:0.0}) is in no room of landblock 0x{portalCell >> 16:X4}");
            }
            else
            {
                // M6 redoubt (live 2026-11-18 22:20:55 UTC: the first way home from the Holtburg Redoubt, `no path: portal-keep-off:Holtburg_Redoubt_exit: the goal stands 0.00 m from the walk's own portal, inside its touch and her
                // stop (2.84 m)`, twice, and the last resort /lifestone covered it): an indoor portal with no facing is walked to itself, so the portal takes the body on touch (M6 7 U, FirstFront). The own portal's clearance (M6 14,
                // KeepOffForFront) is for a front beside the portal; on a walk whose goal is the portal it refuses every plan. Such a walk keeps off the other portals only.
                var walkKeep = FrontWalkKeep(frontBasis, keepOff, frontKeep);
                var walk = FrontWalk(portal, frontCell, front, $"to the front of {portal.Name} ({why}; front={frontBasis})", life, walkKeep);
                foreach (var h in Catching(walk, f => walkFailure = f))
                {
                    yield return h;
                }

                if (walkFailure is not null && FailName != named)
                {
                    throw new InvalidOperationException(walkFailure);
                }

                foreach (var h in SettleAfterWalk(portal, touchMark, "the walk to the front"))
                {
                    yield return h;
                }

                if (walkFailure is not null && !TeleportSince(touchMark) && frontWhy.Length > 0 && OwnPortalRefused(portal, walkFailure))
                {
                    // M6 14: the approach side was taken because the straight line from the start lies past the portal, but the walk there would cross the portal (the rooms come in from its facing side): the facing side once.
                    var here = S.State.Self.Position ?? from;
                    var (facingCell, facingAt, _) = FrontOf(portal, portalCell, here);
                    (facingCell, facingAt) = FrontRoom(key, facingCell, facingAt, out _);
                    Log(string.Create(CultureInfo.InvariantCulture, $"hop {key}: front point 0x{facingCell:X8} ({facingAt.X:0.0}, {facingAt.Y:0.0}) front={FrontBasisFacing} recovery=front-own-portal: the walk to the approach side would pass through {portal.Name}: {walkFailure}"));
                    var ownFailure = walkFailure;
                    walkFailure = null;
                    frontBasis = FrontBasisFacing;
                    foreach (var h in Catching(FrontWalk(portal, facingCell, facingAt, $"to the front of {portal.Name} ({why}; front={FrontBasisFacing} recovery=front-own-portal)", life, frontKeep), f => walkFailure = f))
                    {
                        yield return h;
                    }

                    if (walkFailure is not null && FailName != named)
                    {
                        throw new InvalidOperationException(walkFailure);
                    }

                    foreach (var h in SettleAfterWalk(portal, touchMark, "the walk to the front"))
                    {
                        yield return h;
                    }

                    if (walkFailure is not null && !TeleportSince(touchMark))
                    {
                        FailIfKeptOff(key, walkFailure);
                        Fail(key + ":front-own-portal", $"the walks to both front points of {portal.Name} failed: approach: {ownFailure}; facing: {walkFailure}");
                    }
                }

                if (walkFailure is not null && !TeleportSince(touchMark))
                {
                    if (frontBasis != FrontBasisFacing)
                    {
                        FailIfKeptOff(key, walkFailure);
                        throw new InvalidOperationException(walkFailure);
                    }

                    facingFailure = walkFailure;
                }
            }

            if (facingFailure is not null && !TeleportSince(touchMark))
            {
                // M6 4 w6a (TODO(m6-front-wall), T3a's note): the point on the facing side may stand in a wall of a Network room. One recovery: T2's point on the approach side, from where the body stands now.
                var here = S.State.Self.Position ?? from;
                var (approachCell, approach, _) = FrontOf(portal with { Facing = null }, portalCell, here);
                (approachCell, approach) = FrontRoom(key, approachCell, approach, out _);
                Log(string.Create(CultureInfo.InvariantCulture, $"hop {key}: front point 0x{approachCell:X8} ({approach.X:0.0}, {approach.Y:0.0}) front={FrontBasisApproach} recovery=front-wall: the walk to the facing side failed: {facingFailure}"));
                outcome.FrontWall = true;
                string? approachFailure = null;
                foreach (var h in Catching(FrontWalk(portal, approachCell, approach, $"to the front of {portal.Name} ({why}; front={FrontBasisApproach} recovery=front-wall)", life, frontKeep), f => approachFailure = f))
                {
                    yield return h;
                }

                if (approachFailure is not null && FailName != named)
                {
                    throw new InvalidOperationException(approachFailure);
                }

                foreach (var h in SettleAfterWalk(portal, touchMark, "the walk to the front"))
                {
                    yield return h;
                }

                if (approachFailure is not null && !TeleportSince(touchMark))
                {
                    FailIfKeptOff(key, approachFailure);
                    Fail(key + ":front-wall", $"the walks to both front points of {portal.Name} failed (the recovery front-wall was spent): facing: {facingFailure}; approach: {approachFailure}");
                }
            }

            // M6 4 F6: a move during the walk has been checked for a death (the guarded walk: died:hop:respawned=<cell>); then F5's touch rule.
            if (TookOnTouch(touchMark, portal, outcome, from))
            {
                // M6 4 f5: the server moved the body during the walk (the walk may have failed travel:run:moved): checked at once, taken=touch, never a front-wall recovery after a teleport.
                foreach (var h in CheckTouch(touchMark, portal, from))
                {
                    yield return h;
                }

                touched = true;
            }
        }

        for (var attempt = 0; !touched && attempt < 2; attempt++)
        {
            if (attempt == 1)
            {
                outcome.Recoveries++;
                Log(_handOverStalled
                    ? $"hop {key}: recovery=own-steering: the game's walk to {portal.Name} stalled; her own steering walks up and uses it once more"
                    : $"hop {key}: recovery=use-again: no teleport {HopUseSeconds:0} s after the Use; walking up again and using it once more");
            }

            HopStep = key + ":find";
            var found = 0u;
            foreach (var h in Until(() => (found = FindPortalObject(portal.Guid, portal.Wcid, portalCell, portal.Origin)) != 0 || TeleportSince(touchMark), FindSeconds))
            {
                yield return h;
            }

            if (TeleportSince(touchMark))
            {
                foreach (var h in DiedAfterMove(HopLegKind, "hop " + key, life, from))
                {
                    yield return h;
                }
            }

            if (TookOnTouch(touchMark, portal, outcome, from))
            {
                foreach (var h in CheckTouch(touchMark, portal, from))
                {
                    yield return h;
                }

                break;
            }

            if (found == 0)
            {
                Fail(key + ":no-portal", string.Create(CultureInfo.InvariantCulture, $"no object of weenie {portal.Wcid} (guid 0x{portal.Guid:X8}) within {HopFindMetres:0} m of ({portal.Origin.X:0.#}, {portal.Origin.Y:0.#}) in landblock 0x{portalCell >> 16:X4} within {FindSeconds:0} s"));
            }

            outcome.Portal = found;
            HopStep = key + ":seek";
            var at = S.State.Objects.TryGet(found, out var o) && o.Position is { } op ? (op.CellId, op.Origin) : (portalCell, portal.Origin);
            var seekFailure = (string?)null;
            for (var ended = false; !ended;)
            {
                // M6 8 G: the last steps to the portal are a walk too: an attacker stops them, she fights back, and the seek runs again (bounded by the fight-backs' cap, which retreats).
                string? stopped = null;
                using (var seek = Context.Travel.GoTo(S, new TravelRequest(Destination(key, at.Item1, at.Item2), TravelPurpose.Seek, found, DenTravelStation.PortalRangeMetres, KeepOff: keepOff, Use: HandOverTarget(found, at.Item1, at.Item2)), S.Clock.Now))
                {
                    Log($"hop {key}: seek {portal.Name} 0x{found:X8} within {DenTravelStation.PortalRangeMetres:0.#} m");
                    var status = TravelStatus.Running;
                    while ((status = seek.Step(S, S.Clock.Now)) == TravelStatus.Running && !TeleportSince(touchMark) && (stopped = AttackerStopsSeek()) is null) // M6 14 run past: the seek is the door
                    {
                        yield return null;
                    }

                    seekFailure = status == TravelStatus.Failed ? seek.Failure ?? "no reason given" : null;
                }

                ended = stopped is null;
                if (stopped is null)
                {
                    continue;
                }

                Log($"hop {key}: the seek is stopped: {stopped}");
                foreach (var h in FightBackOnWalk(HopLegKind, portal.Name, stopped, life, retreating: false, portal.DestLandblock))
                {
                    yield return h;
                }

                if (_retreatWhy is not null)
                {
                    foreach (var h in Retreat(HopLegKind, portal.Name, from))
                    {
                        yield return h;
                    }
                }

                ended = _walkEnded; // a portal took the body in the fight: the checks below read it
                Log($"hop {key}: fight-back {FoughtBack} at the portal ended; {(ended ? "the walk is over" : "the seek runs again")} from {CellWord(S.State.Self.Position)}");
            }

            foreach (var h in SettleAfterWalk(portal, touchMark, "the seek"))
            {
                yield return h; // M6 14: a touch the server finds in her stop comes after the seek ended; the Use must not be sent into it
            }

            if (TeleportSince(touchMark))
            {
                foreach (var h in DiedAfterMove(HopLegKind, "hop " + key, life, from))
                {
                    yield return h;
                }
            }

            if (TookOnTouch(touchMark, portal, outcome, from))
            {
                foreach (var h in CheckTouch(touchMark, portal, from))
                {
                    yield return h;
                }

                break;
            }

            if (seekFailure is not null)
            {
                FailIfKeptOff(key, seekFailure);
                Fail(key + ":seek", $"the walk to {portal.Name} failed: {seekFailure}");
            }

            HopStep = key + ":use";
            foreach (var h in PeaceNow())
            {
                yield return h;
            }

            var before = S.State.Self.Position ?? from;
            outcome.WalkMetres = MetresBetween(from, before) is var m && !double.IsNaN(m) ? m : 0;
            var mark = ChatMark;
            var travelMark = TravelMark;
            var use = S.Actions.Use(found, ActionSource.Automation);
            Log(string.Create(CultureInfo.InvariantCulture, $"hop {key}: use {portal.Name} 0x{found:X8} from cell 0x{before.CellId:X8} (attempt {attempt + 1})"));
            // The hub walk: a Use from beyond the portal's reach is ACE's walk-to-use; her body's progress toward the portal is read, and a walk that brings her no nearer for HandOverStallSeconds is stopped by name.
            var portalGlobal = new NavPoint(at.Item1 & 0xFFFF0000u, at.Item2).Global;
            var nearest = MetresTo(before, portalGlobal);
            var nearestAt = S.Clock.Now;
            var walked = nearest > DenTravelStation.PortalRangeMetres;
            var stalled = false;
            foreach (var h in Until(
                () =>
                {
                    if (walked && S.State.Self.Position is { } now && MetresTo(now, portalGlobal) is var m && m < nearest - HandOverStallMetres)
                    {
                        (nearest, nearestAt) = (m, S.Clock.Now);
                    }

                    stalled = walked && !_handOverStalled && nearest > DenTravelStation.PortalRangeMetres && S.Clock.Now - nearestAt > HandOverStallSeconds;
                    return TeleportSince(travelMark) || RefusalSince(mark) != 0 || stalled;
                },
                HopUseSeconds))
            {
                yield return h;
            }

            if (walked)
            {
                Log(string.Create(CultureInfo.InvariantCulture, $"hop {key}: check after the game's walk: {(TeleportSince(travelMark) ? "taken" : stalled ? "stalled" : "not taken")}, {nearest:0.0} m from {portal.Name} at the nearest"));
            }

            if (stalled && !TeleportSince(travelMark))
            {
                _handOverStalled = true;
                Log(string.Create(CultureInfo.InvariantCulture, $"hop {key}: the game's walk to {portal.Name} brought her no nearer for {HandOverStallSeconds:0} s ({nearest:0.0} m away): stopping it; her own steering takes the rest (recovery=own-steering)"));
            }

            var refusal = RefusalSince(mark);
            if (refusal is 0x04AB or 0x04AC || TextSince(mark, "powerful enough to interact with that portal") || TextSince(mark, "too powerful to interact with that portal"))
            {
                Fail(
                    RungRoute.GateRefusal(key, level, portal.MinLevel, portal.MaxLevel) ?? $"gate:{key}:{level}:server",
                    $"the server refused the Use: {(refusal != 0 ? $"WeenieError 0x{refusal:X4}" : "its text")} (ACE Portal.CheckUseRequirements, Portal.cs:166-176)");
            }

            if (TeleportSince(travelMark))
            {
                foreach (var h in ArriveNamed(portal.DestLandblock, travelMark, "hop:" + key))
                {
                    yield return h;
                }

                foreach (var h in WalkOutOf(portal))
                {
                    yield return h;
                }

                HopStep = null;
                yield break;
            }

            Log($"hop {key}: no teleport after the Use (attempt {attempt + 1}; {(refusal != 0 ? Named(refusal) : Describe(use))}, travel {S.State.Self.Travel})");
            if (attempt == 1)
            {
                Fail("hop:" + key + ":no-teleport", $"two Uses of {portal.Name} and no teleport (the recovery use-again was spent)");
            }

            if (refusal == 0x04A7)
            {
                foreach (var h in Pause(HopTooRecentSeconds))
                {
                    yield return h;
                }
            }

            touchMark = TravelMark;
        }

        // Taken on touch: the arrival is checked as a Use's.
        foreach (var h in ArriveNamed(portal.DestLandblock, touchMark, "hop:" + key))
        {
            yield return h;
        }

        foreach (var h in WalkOutOf(portal))
        {
            yield return h;
        }

        HopStep = null;
    }

    /// <summary>M6 8 G (<c>m6-hop-walk-fight-back</c>): the walk to a front point of <paramref name="portal"/>: the guarded walk (<see cref="WalkGuarded"/>: an attacker is fought back, M5 D88), with the hop's own life check; a touch of the portal is the hop's (<see cref="TookOnTouch"/>).</summary>
    private IEnumerable<ActionHandle?> FrontWalk(RungPortal portal, uint cell, Vector3 at, string why, LifeMark life, PortalKeepOff? keepOff) =>
        WalkGuarded(HopLegKind, "rung-change", portal.Key + "-front", cell, at, TravelPurpose.Cell, why, portal.DestLandblock, keepOff, knownLife: life, moveWhat: "hop " + portal.Key, use: HandOverTarget(portal.Guid, CellAt(portal.Cell, portal.Origin), portal.Origin));

    /// <summary>
    /// The hub walk (the owner's go-ahead of 2026-11-18: hand a portal walk to the game's own walk once the way is clear): the portal as a walk's Use target (<see cref="TravelRequest.Use"/>), so the walk hands over to ACE's walk-to-use once the straight line to it is
    /// clear (<see cref="UseHandOver"/>); null once the hand-over stalled in this hop (<see cref="HandOverStallSeconds"/>: her own steering walks the rest, the one recovery).
    /// <para>M6 redoubt (live 2026-11-18 23:06:51 UTC, the way home from the Holtburg Redoubt: `handing over to the game's walk: the line to the target is clear ... target=0x00000000 distance_m=8.4`, the run ended Arrived 8.4 m from the exit portal and the
    /// walk's check after failed `arrival-off-point`, twice; the last resort /lifestone covered it): a portal the plan knows by weenie only (a ground's exit: <see cref="RungPortal.Guid"/> 0) is no Use target, since no Use can be sent to it; the walk goes
    /// to its end and the seek, which has found the object, hands over.</para>
    /// </summary>
    private NavUseTarget? HandOverTarget(uint guid, uint cell, Vector3 at) => _handOverStalled || guid == 0 ? null : new NavUseTarget(guid, new CellFrame(cell, new AcFrame(at, Quaternion.Identity)), (float)DenTravelStation.PortalRangeMetres);

    /// <summary>
    /// How long the server's walk-to-use may bring her no nearer the portal (by <see cref="HandOverStallMetres"/>) before the hop stops waiting and her own steering walks up and uses it once more (recovery=own-steering), in seconds:
    /// the client's MoveTo turns to face the target first (a half turn at the standing rate takes about 1.4 s). Not retail: Lifestone's own.
    /// </summary>
    internal const double HandOverStallSeconds = 3.0;

    /// <summary>How much nearer the portal the server's walk must bring her within <see cref="HandOverStallSeconds"/>, in metres. Not retail: Lifestone's own.</summary>
    internal const float HandOverStallMetres = 0.5f;

    private bool _handOverStalled;

    /// <summary>
    /// M6 8 B1: the edge's way in to the portal's room (<see cref="RungPortal.WalkIn"/>), check, act, check: the first point by the ordinary walk (the door's front), then the rest as the file's way (<see cref="AuthoredWaySource"/>, the confirmer's physics first), each <see cref="WalkToPoint"/>'s arrival check; a walk that
    /// fails leaves the hop <c>&lt;key&gt;:walk-in</c>. Nothing when the edge has none. M6 8 G: both are guarded walks.
    /// </summary>
    private IEnumerable<ActionHandle?> WalkInOf(RungPortal portal, string why)
    {
        if (portal.WalkIn is not { Count: > 0 } points)
        {
            yield break;
        }

        HopStep = portal.Key + ":walk-in";
        var first = points[0];
        Log(string.Create(CultureInfo.InvariantCulture, $"hop {portal.Key}: walk-in: {points.Count} points, the first by the walk: cell 0x{first.Cell:X8} ({first.Origin.X:0.#}, {first.Origin.Y:0.#}, {first.Origin.Z:0.#}): {first.Why}"));
        foreach (var h in WalkGuarded(HopLegKind, "hop:" + portal.Key, "walk-in-front", first.Cell, first.Origin, TravelPurpose.Cell, $"{first.Why} ({why})"))
        {
            yield return h;
        }

        var way = points.Skip(1).Select(p => new CellFrame(p.Cell, new AcFrame(p.Origin, Quaternion.Identity))).ToList();
        var last = points[^1];
        Log(string.Create(CultureInfo.InvariantCulture, $"hop {portal.Key}: walk-in: the way of {way.Count} points to cell 0x{last.Cell:X8}: {string.Join("; ", points.Skip(1).Select(p => p.Why))}"));
        foreach (var h in WalkGuarded(HopLegKind, "hop:" + portal.Key, "walk-in-way", last.Cell, last.Origin, TravelPurpose.Cell, $"the way to {portal.Name} ({why})", way: way))
        {
            yield return h;
        }
    }

    /// <summary>
    /// M6 8 B1: the edge's walk out of the drop's room (<see cref="RungPortal.WalkOut"/>), each point check, act, check (<see cref="WalkToPoint"/>: the landblock and the point after, else the leg's failure names the step, <c>&lt;key&gt;:walk-out</c>): the room graph has no way out of the Crater Pathway's top
    /// building, the arrival's, so the file names the ramp's foot and the street; one line a point, nothing when the edge has none.
    /// </summary>
    private IEnumerable<ActionHandle?> WalkOutOf(RungPortal portal)
    {
        if (portal.WalkOut is not { Count: > 0 } points)
        {
            yield break;
        }

        HopStep = portal.Key + ":walk-out";
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            Log(string.Create(CultureInfo.InvariantCulture, $"hop {portal.Key}: walk-out point {i + 1} of {points.Count}: cell 0x{p.Cell:X8} ({p.Origin.X:0.#}, {p.Origin.Y:0.#}, {p.Origin.Z:0.#}): {p.Why}"));
            foreach (var h in WalkGuarded(HopLegKind, "hop:" + portal.Key, "walk-out-" + (i + 1).ToString(CultureInfo.InvariantCulture), p.Cell, p.Origin, TravelPurpose.Cell, p.Why))
            {
                yield return h;
            }
        }
    }

    /// <summary>M6 7 AI: fails the hop <c>&lt;key&gt;:portal-keep-off</c> when <paramref name="failure"/> is the runner's own: a walk that could not keep clear of a neighbour, never a silent brush. The route's fallback reads it as any hop's named failure.</summary>
    private void FailIfKeptOff(string key, string failure)
    {
        if (failure.Contains(NavRunner.PortalKeepOffFailure, StringComparison.Ordinal))
        {
            Fail(key + ":" + NavRunner.PortalKeepOffFailure, failure);
        }
    }

    /// <summary>Steps <paramref name="steps"/> and hands a failure it throws (a <see cref="ScriptLeg"/> check's <see cref="InvalidOperationException"/>) to <paramref name="failed"/> instead of ending the leg: the hop's one recovery decides.</summary>
    protected static IEnumerable<ActionHandle?> Catching(IEnumerable<ActionHandle?> steps, Action<string> failed)
    {
        using var e = steps.GetEnumerator();
        while (true)
        {
            try
            {
                if (!e.MoveNext())
                {
                    yield break;
                }
            }
            catch (InvalidOperationException ex)
            {
                failed(ex.Message);
                yield break;
            }

            yield return e.Current;
        }
    }

    /// <summary>The check after a touch (M6 4 f5): waits until the body is present, then names a landblock other than the portal's destination <c>hop:&lt;key&gt;:moved-elsewhere:&lt;landblock&gt;</c> (a teleport that was not this portal's; ACE <c>Portal.OnCollideObject</c>, Portal.cs:103-106).</summary>
    private IEnumerable<ActionHandle?> CheckTouch(int touchMark, RungPortal portal, CellFrame from)
    {
        foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present, PresentSeconds))
        {
            yield return h;
        }

        if (S.State.Self.Travel == SelfTravel.Present && S.State.Self.Position is { } now && now.CellId >> 16 != portal.DestLandblock)
        {
            // M6 14 (m6-14-plaza): the portal that took her is named by the destination it moved her to (TakerOf); the recovery is the errand's own: a go's failure is a walk step, counted, she recalls home and the errand is called again
            // at the next way home within its tries (AutopilotScenario's ErrandWalkFailed / ErrandWalkTry).
            Fail(
                TakerOf(portal, now.CellId, from) is { } taker
                    ? string.Create(CultureInfo.InvariantCulture, $"hop:{portal.Key}:taken-by:{taker.Name.Replace(' ', '_')}:{now.CellId >> 16:X4}")
                    : string.Create(CultureInfo.InvariantCulture, $"hop:{portal.Key}:moved-elsewhere:{now.CellId >> 16:X4}"),
                TakerOf(portal, now.CellId, from) is { } by
                    ? string.Create(CultureInfo.InvariantCulture, $"{by.Name} (0x{by.Guid:X8}, {Vector2.Distance(by.Global, portal.Global()):0.0} m from {portal.Name}) took the player on touch during the walk to its destination, cell 0x{now.CellId:X8}, not into landblock 0x{portal.DestLandblock:X4}; the recovery: the errand's walk count, recall home, called again at the next way home")
                    : string.Create(CultureInfo.InvariantCulture, $"the server moved the player during the walk to cell 0x{now.CellId:X8}, not into landblock 0x{portal.DestLandblock:X4} (no placed portal sends a body to landblock 0x{now.CellId >> 16:X4})"));
        }
    }

    /// <summary>
    /// M6 14 (m6-14-plaza; live ladder62, 2026-11-17 16:29:41 UTC: Sand's Edge Portal took her to 0xA586003D on the walk to the Yinar Portal's front, and the hop named the Yinar Portal): the placed portal that sent the body to
    /// <paramref name="arrivalCell"/> when that is not <paramref name="portal"/>'s destination (<see cref="Lifestone.Client.Session.Travel.WorldPortals.TakenBy"/>, judged from the hop's own portal); null when it is the hop's own or no placed portal sends a body there.
    /// </summary>
    /// <param name="portal">The hop's portal.</param>
    /// <param name="arrivalCell">Where the server put the body.</param>
    /// <param name="from">Where the walk started, or null (the portal's own point then: a walk beside it).</param>
    internal static Lifestone.Client.Session.Travel.WorldPortal? TakerOf(RungPortal portal, uint arrivalCell, CellFrame? from = null) =>
        arrivalCell >> 16 == portal.DestLandblock ? null
        : Lifestone.Client.Session.Travel.WorldPortals.TakenBy(arrivalCell, from is { } f ? NavMap.FromCellFrame(f).Global : portal.Global(), portal.Global(), TakerReachMetres);

    /// <summary>How far off the walk's straight line a portal can have taken the body, in metres: the walks' keep-off reach (a landblock; a route wanders less than that off its line). Not retail: Lifestone's own.</summary>
    internal const float TakerReachMetres = 192f;

    /// <summary>True when the facing side's front point lies past the portal's plane from <paramref name="from"/> (the walker is on the other side, so a walk to the point crosses the portal; M6 4 f5).</summary>
    internal static bool FacingPastPortal(RungPortal portal, uint portalCell, CellFrame from)
    {
        if (portal.Facing is not { } facing)
        {
            return false;
        }

        var g0 = Global(portalCell, portal.Origin);
        var g1 = Global(from.CellId, from.Origin);
        Vector2? toward = g0 is { } a && g1 is { } b ? b - a : from.CellId >> 16 == portalCell >> 16 ? new Vector2(from.Origin.X - portal.Origin.X, from.Origin.Y - portal.Origin.Y) : null;
        return toward is { } t && Vector2.Dot(t, facing) < 0f;
    }

    private bool TookOnTouch(int touchMark, RungPortal portal, HopOutcome outcome, CellFrame from)
    {
        if (!TeleportSince(touchMark))
        {
            return false;
        }

        outcome.Taken = true;
        outcome.WalkMetres = S.State.Self.Position is { } now && MetresBetween(from, now) is var m && !double.IsNaN(m) ? m : outcome.WalkMetres;
        var taker = S.State.Self.Position is { } there && TakerOf(portal, there.CellId, from) is { } by ? by : null; // M6 14 (m6-14-plaza): the portal that took her, by where it sent her
        var name = taker is null ? portal.Name : string.Create(CultureInfo.InvariantCulture, $"{taker.Name} (not {portal.Name}: its destination 0x{taker.DestCell:X8} is where the server put her)");
        Log(_walkedIn
            ? $"hop {portal.Key}: walked-in: {name} took the player as the walk ended (taken=touch): no Use is sent; the arrival decides"
            : $"hop {portal.Key}: {name} took the player on touch during the walk (taken=touch): no Use is sent");
        return true;
    }

    /// <summary>M6 14: true when the last <see cref="SettleAfterWalk"/> saw the teleport begin after the walk had ended (the hop's <c>walked-in</c>).</summary>
    private bool _walkedIn;

    /// <summary>
    /// M6 14: the server's answer time after her last position report, seconds: the round trip and a slow frame on each side (the live answer came 14 ms after the stop; a slow frame is 141 ms). Not retail: Lifestone's own.
    /// </summary>
    internal const double ServerAnswerSeconds = 0.5;

    /// <summary>
    /// M6 14: the longest the check after a walk beside a hop's portal waits for the server's verdict on where she stopped, seconds: the client reports her position at the stop (the released keys' MoveToState) and then on the position cadence
    /// (<see cref="PhysicsCompat.PositionEventInterval"/>: retail 1.0 s, ACE's 1.875 s; the longer), which carries the coast after the stop, and the server answers each report (<see cref="ServerAnswerSeconds"/>).
    /// </summary>
    internal static double SettleSeconds => Math.Max(PhysicsCompat.Retail.PositionEventInterval, PhysicsCompat.Ace.PositionEventInterval) + ServerAnswerSeconds;

    /// <summary>
    /// M6 14 (portal overshoot, live 2026-11-13 19:20:58 UTC: the walk to the Tusker Barracks' front ended Arrived, and 14 ms later the server's teleport came: its sweep of her reported positions had crossed the portal), the check after a walk that ends beside
    /// a hop's portal (the front walk, the seek): the server judges where she stopped only when her reports reach it, so before the hop reads anything it waits, check before and after one line each, until a teleport began (the portal took her:
    /// <c>walked-in</c>; <see cref="TookOnTouch"/> and <see cref="CheckTouch"/> read the arrival: the hop's destination landblock is the hop taken, elsewhere <c>hop:&lt;key&gt;:moved-elsewhere</c>), or the server's last position agrees with
    /// her body (it has her stop: no touch), or <see cref="SettleSeconds"/>. A host that gives no body of its own (<see cref="IJourneyWalks"/>) has only the server's position, which agrees with itself: no wait.
    /// </summary>
    private IEnumerable<ActionHandle?> SettleAfterWalk(RungPortal portal, int touchMark, string what)
    {
        _walkedIn = false;
        if (TeleportSince(touchMark) || S.State.Self.Travel != SelfTravel.Present)
        {
            yield break; // the move is already seen: the touch rule reads it
        }

        var started = S.Clock.Now;
        if (ServerHasHerStop(out var gap))
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"hop {portal.Key}: check after {what}: the server's last position agrees with her body ({gap}): no wait"));
            yield break;
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"hop {portal.Key}: check after {what}: the server's last position is {gap} from her body; waiting up to {SettleSeconds:0.0} s for its verdict on her stop beside {portal.Name} (a teleport now is the portal taking her)"));
        foreach (var h in Until(() => TeleportSince(touchMark) || ServerHasHerStop(out _), SettleSeconds))
        {
            yield return h;
        }

        _walkedIn = TeleportSince(touchMark);
        var waited = S.Clock.Now - started;
        if (_walkedIn)
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"hop {portal.Key}: walked-in: a teleport began {waited:0.00} s after {what} ended: the server's sweep of her reports met {portal.Name}; the arrival decides (landblock 0x{portal.DestLandblock:X4} is the hop taken)"));
        }
        else if (ServerHasHerStop(out var agreed))
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"hop {portal.Key}: check after {what}: after {waited:0.00} s the server agrees with her body ({agreed}); no teleport"));
        }
        else
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"hop {portal.Key}: check after {what}: no teleport within {SettleSeconds:0.0} s; the server's last position is still {agreed} from her body (its position reports are sparse); she stands outside the portal"));
        }
    }

    /// <summary>M6 14: true when the server's last position stands within her stop (<see cref="NavRunner.StopMetres"/>) of her body in its landblock, or when the host gives no body; <paramref name="gap"/> says how far, in words.</summary>
    private bool ServerHasHerStop(out string gap)
    {
        var (body, fromBody) = WhereSheStands();
        if (!fromBody || body is not { } b || S.State.Self.Position is not { } server)
        {
            gap = "no body of the host's own";
            return true;
        }

        if (server.CellId >> 16 != b.CellId >> 16)
        {
            gap = string.Create(CultureInfo.InvariantCulture, $"landblock 0x{server.CellId >> 16:X4} against 0x{b.CellId >> 16:X4}");
            return false;
        }

        var metres = Vector2.Distance(new Vector2(server.Frame.Origin.X, server.Frame.Origin.Y), new Vector2(b.Frame.Origin.X, b.Frame.Origin.Y));
        gap = string.Create(CultureInfo.InvariantCulture, $"{metres:0.0} m");
        return metres <= NavRunner.StopMetres(NavGoal.DefaultStopTolerance);
    }

    /// <summary>The horizontal metres from a position to a global point.</summary>
    private static float MetresTo(in CellFrame at, Vector2 global) => Vector2.Distance(NavMap.FromCellFrame(at).Global, global);

    /// <summary>M6 14: true when <paramref name="failure"/> is the runner's refusal of a plan through the walk's own portal (<see cref="RungPortal.KeepOffForFront"/>), named by that portal.</summary>
    private static bool OwnPortalRefused(RungPortal portal, string failure) =>
        failure.Contains(NavRunner.PortalKeepOffFailure + ":" + portal.Name.Replace(' ', '_') + ":", StringComparison.Ordinal);

    /// <summary>M6 14 (m6-14-plaza): whether a body stands at the point (<see cref="HopFront"/>'s turn): outdoors always, indoors where a room of the landblock holds it unmoved (<see cref="FrontRoomIn"/>); true with no dats.</summary>
    private bool FrontStands(uint cell, Vector3 at) => LandDefs.IsOutdoorCell(cell) || FrontCells(cell) is not { } cells || FrontRoomIn(cells, cell, at) is { Moved: 0f };

    private LandblockCellSet? FrontCells(uint cell)
    {
        if (Context.Dats is not { } dats)
        {
            return null;
        }

        var landblock = cell >> 16;
        if (!_frontRooms.TryGetValue(landblock, out var cells))
        {
            _frontRooms[landblock] = cells = LandblockCellSet.Load(new CellStructureLibrary(dats), LandblockCoord.FromId(cell));
        }

        return cells;
    }

    /// <summary>
    /// The room that holds an indoor front point (M6 4 f3): <see cref="FrontOf"/> names the portal's own cell, but 3 m from a Town Network portal the point is out of its alcove: in the wall
    /// between the alcove and the corridor before it (the approach side), or behind the alcove (the facing side), and a walk planned to a point in a cell that does not hold it is refused by
    /// the physics at its end. The point is moved to the nearest point a room of the portal's landblock holds within <see cref="FrontSnapMetres"/> (<see cref="FrontRoomIn"/>), logged by name
    /// (<c>front-room</c>); <paramref name="inNoRoom"/> is true when there is none. An outdoor point, or no dats (an offline rig), is kept as it is.
    /// </summary>
    private (uint Cell, Vector3 At) FrontRoom(string key, uint cell, Vector3 at, out bool inNoRoom)
    {
        inNoRoom = false;
        if (LandDefs.IsOutdoorCell(cell) || FrontCells(cell) is not { } cells)
        {
            return (cell, at);
        }

        if (FrontRoomIn(cells, cell, at) is not { } room)
        {
            inNoRoom = true;
            return (cell, at);
        }

        if (room.Cell != cell || room.Moved > 0f)
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"hop {key}: front point ({at.X:0.0}, {at.Y:0.0}) moved {room.Moved:0.0} m to ({room.At.X:0.0}, {room.At.Y:0.0}) in room 0x{room.Cell:X8} (front-room; the portal's cell 0x{cell:X8})"));
        }

        return (room.Cell, room.At);
    }

    /// <summary>The farthest an indoor front point is moved to stand in a room (metres; Lifestone's, the hop's own 3 m front).</summary>
    internal const float FrontSnapMetres = 3f;

    /// <summary>
    /// <see cref="FrontRoom"/>'s rule, pure: the room of <paramref name="cells"/> that holds <paramref name="at"/> (a body's middle, 0.5 m over it; <paramref name="cell"/> first), else the nearest point on
    /// rings round it every 0.25 m out to <see cref="FrontSnapMetres"/> (16 bearings) that a room holds; null when none does.
    /// </summary>
    internal static (uint Cell, Vector3 At, float Moved)? FrontRoomIn(LandblockCellSet cells, uint cell, Vector3 at)
    {
        ArgumentNullException.ThrowIfNull(cells);
        var lift = new Vector3(0f, 0f, 0.5f);
        if (cells.FindCell(at + lift, cell) is { } room)
        {
            return (room.Id, at, 0f);
        }

        for (var r = 0.25f; r <= FrontSnapMetres + 1e-3f; r += 0.25f)
        {
            for (var k = 0; k < 16; k++)
            {
                var a = k * MathF.PI / 8f;
                var p = at + new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), 0f);
                if (cells.FindCell(p + lift, cell) is { } near)
                {
                    return (near.Id, p, r);
                }
            }
        }

        return null;
    }

    private readonly Dictionary<uint, LandblockCellSet> _frontRooms = [];

    /// <summary>
    /// M6 redoubt: the list the walk to a hop's first point keeps. A front beside the portal keeps off the portal itself too (<paramref name="own"/>, M6 14: <see cref="RungPortal.KeepOffForFront"/>); a walk whose goal is the portal
    /// (<see cref="FrontBasisPortal"/>, M6 7 U: an indoor portal with no facing is taken by touch) keeps off the other portals only (<paramref name="others"/>), because the own portal's clearance refuses every plan that ends on it.
    /// </summary>
    /// <param name="basis">The front's basis (<see cref="HopFront"/>).</param>
    /// <param name="others">The other portals (<see cref="RungPortal.KeepOffTheOthers"/>).</param>
    /// <param name="own">The others and the hop's own portal (<see cref="RungPortal.KeepOffForFront"/>).</param>
    internal static PortalKeepOff FrontWalkKeep(string basis, PortalKeepOff others, PortalKeepOff own) => basis == FrontBasisPortal ? others : own;
    /// <summary>
    /// M6 7 U (r1f-climb's way home, on the dats): the first point a hop walks to. A ground's exit portal stands indoors with no facing in the travel data, and the approach side's rule (<see cref="FrontOf"/>) takes the straight line toward the body,
    /// which indoors says nothing of the way in: the Burrow's exit stands in an alcove entered from the south, the line points north into its wall, the room search snaps to (120.1, -132.2) past the alcove's floor (it ends at -132.62) and the physics
    /// refuses the plan's end. So an indoor portal with no facing is walked to itself: ACE takes the body on touch (<c>taken=touch</c>, the leg's rule), and a body stopped short still stands in reach of its Use. A portal with a facing, and an outdoor
    /// portal, keep <see cref="FrontOf"/>.
    /// </summary>
    internal static (uint Cell, Vector3 At, string Basis) FirstFront(RungPortal portal, uint portalCell, CellFrame from) =>
        FirstFrontOf(portal, portalCell, from);

    /// <summary>
    /// The hop's front point and its basis, with the reason (<c>" why=..."</c>, empty for none): <see cref="FirstFront"/>; when the facing side's point lies past the portal's plane from the start, the approach side (M6 4 f5,
    /// <see cref="FacingPastPortal"/>). M6 14 (m6-14-plaza): when the chosen point stands within the clearance of another portal of <paramref name="keep"/> at its storey (a front no walk may reach: the Facility Hub's Eastham Portal stands
    /// 3.6 m from the Banderling Conquest Dungeon and Mosswart Maze portals, and its approach point from the drop lies 0.7 m from the first), the point is turned round the portal, <see cref="FrontTurnDegrees"/> at a time either way
    /// from the chosen side, to the first one that stands clear of every other portal and where a body can stand (<paramref name="stands"/>: a room holds it); when none does, the first stays and the walk ends by name
    /// (<see cref="NavRunner.PortalKeepOffFailure"/>).
    /// </summary>
    /// <param name="portal">The hop's portal.</param>
    /// <param name="portalCell">Its cell (the land cell under an outdoor portal).</param>
    /// <param name="from">Where the body stands.</param>
    /// <param name="keep">The walk's list (<see cref="RungPortal.KeepOffForFront"/>).</param>
    /// <param name="stands">Whether a body stands at a point of a cell (indoors: a room holds it unmoved), or null for every point.</param>
    internal static (uint Cell, Vector3 At, string Basis, string Why) HopFront(RungPortal portal, uint portalCell, CellFrame from, PortalKeepOff keep, Func<uint, Vector3, bool>? stands = null)
    {
        ArgumentNullException.ThrowIfNull(keep);
        var (cell, at, basis) = FirstFront(portal, portalCell, from);
        var why = string.Empty;
        if (basis == FrontBasisFacing && FacingPastPortal(portal, portalCell, from))
        {
            // M6 4 f5: the facing side's point lies past the portal's plane from the start, so a walk to it crosses the portal (and the portal takes the body on touch): the approach side first.
            (cell, at, basis) = FrontOf(portal with { Facing = null }, portalCell, from);
            why = " why=facing-past-portal";
        }

        if (basis == FrontBasisPortal || NeighbourAt(keep, cell, at) is not { } neighbour)
        {
            return (cell, at, basis, why);
        }

        var first = MathF.Atan2(at.Y - portal.Origin.Y, at.X - portal.Origin.X);
        for (var k = 1; k * FrontTurnDegrees <= 180f; k++)
        {
            foreach (var sign in new[] { 1f, -1f })
            {
                var turn = sign * k * FrontTurnDegrees;
                var a = first + (turn * MathF.PI / 180f);
                var p = portal.Origin + new Vector3(MathF.Cos(a) * HopFrontMetres, MathF.Sin(a) * HopFrontMetres, 0f);
                var c = portalCell;
                if ((portalCell & 0xFFFF) < 0x100)
                {
                    p = new Vector3(Math.Clamp(p.X, 0.5f, 191.5f), Math.Clamp(p.Y, 0.5f, 191.5f), p.Z);
                    c = CellAt(portalCell & 0xFFFF0000, p);
                }

                if (NeighbourAt(keep, c, p) is null && (stands?.Invoke(c, p) ?? true))
                {
                    return (c, p, basis, why + string.Create(CultureInfo.InvariantCulture, $" front-turned={turn:0}:{neighbour.Name.Replace(' ', '_')}"));
                }
            }
        }

        return (cell, at, basis, why + string.Create(CultureInfo.InvariantCulture, $" front-in-clearance-of:{neighbour.Name.Replace(' ', '_')}(no-clear-side)"));
    }

    /// <summary>M6 14 (m6-14-plaza): the step the front point is turned round its portal by, in degrees (<see cref="HopFront"/>). Not retail: Lifestone's own.</summary>
    internal const float FrontTurnDegrees = 15f;

    /// <summary>M6 14 (m6-14-plaza): the first portal of <paramref name="keep"/> other than the walk's own whose clearance holds the point at its storey, or null.</summary>
    private static KeptPortal? NeighbourAt(PortalKeepOff keep, uint cell, Vector3 at)
    {
        var g = new NavPoint(cell & 0xFFFF0000u, at).Global;
        foreach (var p in keep.Portals)
        {
            if (!p.Own && Vector2.Distance(p.Global, g) < keep.ClearanceOf(p) && MathF.Abs(at.Z - p.Z) <= PortalKeepOff.StoreyMetres)
            {
                return p;
            }
        }

        return null;
    }

    private static (uint Cell, Vector3 At, string Basis) FirstFrontOf(RungPortal portal, uint portalCell, CellFrame from) =>
        portal.Facing is null && (portalCell & 0xFFFF) >= 0x100 ? (portalCell, portal.Origin, FrontBasisPortal) : FrontOf(portal, portalCell, from);

    /// <summary>The point <see cref="HopFrontMetres"/> from the portal toward the body (global coordinates outdoors), in the portal's cell indoors or its land cell outdoors (<see cref="FrontRoom"/> then finds the room that holds it).</summary>
    internal static (uint Cell, Vector3 At, string Basis) FrontOf(RungPortal portal, uint portalCell, CellFrame from)
    {
        var dir = Vector2.UnitY;
        var basis = FrontBasisFacing;
        if (portal.Facing is { } facing)
        {
            // M6 4 T3a (closes TODO(m6-portal-front-facing)): the side the placement's rotation faces.
            dir = facing;
        }
        else
        {
            var g0 = Global(portalCell, portal.Origin);
            var g1 = Global(from.CellId, from.Origin);
            Vector2? toward = g0 is { } a && g1 is { } b ? b - a : from.CellId >> 16 == portalCell >> 16 ? new Vector2(from.Origin.X - portal.Origin.X, from.Origin.Y - portal.Origin.Y) : null;
            basis = FrontBasisApproach;
            if (toward is { } t && t.Length() > 0.3f)
            {
                dir = Vector2.Normalize(t);
            }
        }

        var at = portal.Origin + new Vector3(dir.X * HopFrontMetres, dir.Y * HopFrontMetres, 0);
        if ((portalCell & 0xFFFF) < 0x100)
        {
            at = new Vector3(Math.Clamp(at.X, 0.5f, 191.5f), Math.Clamp(at.Y, 0.5f, 191.5f), at.Z);
            return (CellAt(portalCell & 0xFFFF0000, at), at, basis);
        }

        return (portalCell, at, basis);
    }
}

/// <summary>
/// One portal hop as its own leg (M6 item 4 t2): <see cref="TravelLeg.HopThrough"/> stepped by a station that must see the hop's failure without ending (the rung change's gem recovery). <see cref="FailureName"/> is the hop's named failure
/// (<c>gate:&lt;key&gt;:...</c>, <c>hop:&lt;key&gt;:no-teleport</c>, <c>&lt;key&gt;:walk</c>, ...), null when it arrived.
/// </summary>
internal sealed class PortalHopLeg(JourneyContext context, RungPortal portal, string why) : TravelLeg("portal-hop", context)
{
    /// <summary>What the hop did.</summary>
    public HopOutcomeView Outcome => new(_outcome.Portal, _outcome.Taken, _outcome.Recoveries, _outcome.WalkMetres);

    private readonly HopOutcome _outcome = new();

    /// <summary>The hop's named failure, or null.</summary>
    public string? FailureName => Failure is null ? null : FailName ?? HopStep ?? portal.Key + ":failed";

    /// <inheritdoc />
    public override string VerdictFields() =>
        string.Create(CultureInfo.InvariantCulture, $"portal={portal.Key} taken={(_outcome.Taken ? "yes" : "no")} recoveries={_outcome.Recoveries} walk_m={_outcome.WalkMetres:0}") + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

    /// <inheritdoc />
    protected override IEnumerable<ActionHandle?> Body() => HopThrough(portal, _outcome, why);
}

/// <summary>A hop's outcome as the station reads it.</summary>
/// <param name="Portal">The portal object used.</param>
/// <param name="Taken">True when the portal took the body on touch (no Use sent).</param>
/// <param name="Recoveries">The use-again recoveries made.</param>
/// <param name="WalkMetres">The planar metres walked before the Use.</param>
internal readonly record struct HopOutcomeView(uint Portal, bool Taken, int Recoveries, double WalkMetres);
