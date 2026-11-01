using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.Physics.Geometry;
using Lifestone.Data.Physics.Objects;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// The travel stations of a journey (M4 P5b lane J3b, owner decisions D36 and D38; research W0 sections 2 and 3, live 2026-10-24): the bind and the lifestone tie at Holtburg, the mage's way into the den (a Primary Portal Recall, or the first
/// ever journey's tie and portal walk) and the way home (Lifestone Recall). Every station is a <c>public sealed</c> <see cref="IStation"/> with a plain constructor; its run is a <see cref="ScriptLeg"/> built from the journey's context.
/// <para><b>No silent place</b> (lanes.md contract 5): every walk goes through <see cref="JourneyContext.Travel"/>, never <c>Tools.Place</c>; the one place a station may ask for is the untied den-travel's <see cref="TravelPurpose.Ground"/> request to
/// the portal's front, which the journey's travel counts against its teleport budget.</para>
/// <para><b>The facts</b> (contract 4): the server never says whether the character is tied. The bind sets <see cref="JourneyFacts.Bound"/> from ACE's text and the tie sets <see cref="JourneyFacts.LifestoneTied"/> from "You have successfully linked with
/// the life stone."; the portal tie sets <see cref="JourneyFacts.PortalTieOf"/> (the ground's, M5 3 p1) from "...with the portal." or a Primary Portal Recall's arrival, and a 0x04A3 sets it untied; a recall adds to <see cref="JourneyFacts.Recalls"/>.</para>
/// </summary>
internal abstract class TravelLeg(string name, JourneyContext context) : ScriptLeg(name, text => context.Log.Info("journey", text))
{
    /// <summary>Seconds a cast may take to end.</summary>
    protected const double CastSeconds = 15;

    /// <summary>Seconds the server's text is waited for after a cast or a use.</summary>
    protected const double TextSeconds = 5;

    /// <summary>Seconds a lifestone or a portal is looked for.</summary>
    protected const double FindSeconds = 12;

    /// <summary>Seconds after a recall's cast for the teleport to start (ACE answers a recall after 2 s).</summary>
    protected const double TeleportStartSeconds = 20;

    /// <summary>Seconds, once the teleport started, to be present.</summary>
    protected const double PresentSeconds = 45;

    /// <summary>Seconds a combat mode change may take.</summary>
    protected const double ModeSeconds = 10;

    /// <summary>The fizzles one cast retries, logged and counted (a named cap, Lifestone's own, as <see cref="RecallHome.MaxFizzles"/>); the next fizzle fails the cast.</summary>
    protected const int MaxFizzles = 3;

    /// <summary>
    /// The journey's home lifestone and the known standing point off it (M5 3 p1, closes 2-R10 for these stations): the journey ground's <see cref="HuntingGround.Home"/> (<see cref="GroundHome.LifestoneCell"/> and <see cref="GroundHome.LifestonePoint"/>; the profile's measure, session.md section 41),
    /// no constant. The fighters' journeys (belt, forest) have no dungeon ground: their context holds the option's default ground, and every profile in the catalog carries the same home, so they read Holtburg's through it
    /// (<c>TODO(m6-fighter-home)</c>: a fighter ground of another town needs its own <see cref="JourneyContext.Dungeon"/>).
    /// </summary>
    protected (uint Cell, Vector3 At) HomeLifestone => (context.Dungeon.Home.LifestoneCell, context.Dungeon.Home.LifestonePoint);

    /// <summary>The script legs' cast rules (M5 item 1 lane K4, D13): <see cref="CastRules.Travel(int)"/> of <see cref="MaxFizzles"/> same-level retries, sent at once, as the helper always did.</summary>
    private static readonly CastRules FizzleRules = CastRules.Travel(MaxFizzles);

    private readonly uint[] _castIds = new uint[1];
    private readonly List<(ChatKind Kind, uint Type, string Text)> _chat = [];
    private readonly List<SelfTravel> _travel = [];
    private IDisposable? _chatSubscription;
    private IDisposable? _travelSubscription;
    private CastAttempt? _attempt;

    /// <summary>The journey's context.</summary>
    protected JourneyContext Context => context;

    /// <summary>The fizzles this leg's casts retried (report-only).</summary>
    protected int Fizzles { get; private set; }

    /// <summary>The leg's body.</summary>
    protected abstract IEnumerable<ActionHandle?> Body();

    /// <inheritdoc />
    protected sealed override IEnumerable<ActionHandle?> Script()
    {
        _chatSubscription = S.Events.Subscribe<ChatReceived>((in ChatReceived e) => _chat.Add((e.Line.Kind, e.Line.ChatType, e.Line.Text)));
        _travelSubscription = S.Events.Subscribe<SelfTravelChanged>((in SelfTravelChanged e) => _travel.Add(e.Travel));
        try
        {
            foreach (var h in Body())
            {
                yield return h;
            }

            Stage = "done";
        }
        finally
        {
            _chatSubscription.Dispose();
            _travelSubscription.Dispose();
        }
    }

    // ------------------------------------------------------------------------------------------------ the server's text

    /// <summary>The number of chat lines so far (a mark for <see cref="TextSince"/>).</summary>
    protected int ChatMark => _chat.Count;

    /// <summary>The number of travel changes so far (a mark for <see cref="TeleportSince"/>).</summary>
    protected int TravelMark => _travel.Count;

    /// <summary>True when a chat line since <paramref name="mark"/> contains <paramref name="needle"/> (case-insensitive).</summary>
    protected bool TextSince(int mark, string needle)
    {
        for (var i = mark; i < _chat.Count; i++)
        {
            if (_chat[i].Text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The first WeenieError code in the chat since <paramref name="mark"/>, or 0.</summary>
    protected uint RefusalSince(int mark)
    {
        for (var i = mark; i < _chat.Count; i++)
        {
            if (_chat[i].Kind == ChatKind.WeenieError)
            {
                return _chat[i].Type;
            }
        }

        return 0;
    }

    /// <summary>True when the player's travel changed since <paramref name="mark"/> (a teleport began).</summary>
    protected bool TeleportSince(int mark) => _travel.Count > mark;

    /// <summary>A refusal in one phrase (ACE's name for it when <see cref="RecallHome.NameOfRefusal"/> knows it).</summary>
    protected static string Named(uint code) => RecallHome.NameOfRefusal(code) ?? $"WeenieError 0x{code:X4}";

    // ------------------------------------------------------------------------------------------------ steps

    /// <summary>
    /// Casts <paramref name="spell"/> (untargeted when <paramref name="target"/> is 0) as automation through the cast policy (M5 D13, <see cref="CastRules.Travel(int)"/>): a fizzle is retried at once up to <see cref="MaxFizzles"/> times, logged and
    /// counted; <paramref name="result"/> holds the last handle and the chat and travel marks before the last try. A cast the kill switch (or an end before any send) kept from being sent fails the leg, named.
    /// </summary>
    protected IEnumerable<ActionHandle?> Cast(uint spell, uint target, CastResult result)
    {
        _attempt ??= new CastAttempt(S.Actions, S.State, context.Log, context.Spells);
        _castIds[0] = spell;
        var tries = 0;
        var fizzled = 0;
        result.Mark = ChatMark;
        result.TravelMark = TravelMark;
        var end = _attempt.Start(new CastRequest { Levels = _castIds, Rules = FizzleRules, Purpose = "journey cast", Target = target }, S.Clock.Now);
        for (;;)
        {
            var seen = _attempt.Report;
            if (seen.Tries != tries)
            {
                // A new try was sent in that step: the marks are those of the last try, as when each try was its own loop turn.
                tries = seen.Tries;
                result.Mark = ChatMark;
                result.TravelMark = TravelMark;
            }

            while (fizzled < seen.Fizzles)
            {
                fizzled++;
                Fizzles++;
                Log($"cast {spell}: fizzled ({Fizzles}); {(fizzled <= MaxFizzles ? "casting again" : "the cap was reached")}");
            }

            if (end != CastEnd.Running)
            {
                break;
            }

            yield return null;
            end = _attempt.Step(S.Clock.Now);
        }

        var report = _attempt.Report;
        result.Handle = _attempt.Handle;

        if (result.Handle is null)
        {
            throw new InvalidOperationException($"cast {spell} was not sent: {end.Name()} ({report.Why})");
        }
    }

    /// <summary>Waits until the combat mode is <paramref name="wanted"/>.</summary>
    protected IEnumerable<ActionHandle?> Mode(CombatMode wanted, int modeValue)
    {
        if (S.State.Self.Qualities.CombatMode == modeValue)
        {
            yield break;
        }

        var mode = S.Actions.SetCombatMode(wanted, ActionSource.Automation);
        foreach (var h in Until(() => mode.IsCompleted && S.State.Self.Qualities.CombatMode == modeValue, ModeSeconds))
        {
            yield return h;
        }

        Require(S.State.Self.Qualities.CombatMode == modeValue, $"the {wanted} mode was not reached within {ModeSeconds:0} s: {Describe(mode)}");
        foreach (var h in Pause(1))
        {
            yield return h;
        }
    }

    /// <summary>Goes to the magic mode (the mage wields its wand already).</summary>
    protected IEnumerable<ActionHandle?> MagicNow() => Mode(CombatMode.Magic, CombatModes.Magic);

    /// <summary>Goes to peace.</summary>
    protected IEnumerable<ActionHandle?> PeaceNow() => Mode(CombatMode.NonCombat, CombatModes.NonCombat);

    /// <summary>
    /// A travel request through the journey's seam (never <c>Tools.Place</c>): stepped every frame until it ends. A failure ends the leg Failed with the seam's reason. For a place the seam's arrival only says the action was confirmed, so the
    /// teleport is waited out as <see cref="ScriptLeg.PlaceAndSettle"/> does (the teleport seen, present, the post-teleport LoginComplete, 3 s).
    /// </summary>
    protected IEnumerable<ActionHandle?> Go(TravelRequest request, string what)
    {
        var total = S.ActionLog.Total;
        using var leg = context.Travel.GoTo(S, request, S.Clock.Now);
        Log($"travel {what}: {request.Purpose} to {request.Destination.Name}{(request.ObjectGuid == 0 ? string.Empty : $" (object {request.ObjectGuid:X8}, range {request.Range:0.#} m)")}");
        var status = TravelStatus.Running;
        while ((status = leg.Step(S, S.Clock.Now)) == TravelStatus.Running)
        {
            yield return null;
        }

        Require(status == TravelStatus.Arrived, $"travel {what} failed: {leg.Failure ?? "no reason given"}");
        Log($"travel {what}: arrived by {leg.Means}" + (leg.Fields.Length == 0 ? string.Empty : " " + leg.Fields));
        if (leg.Means == TravelMeans.Place)
        {
            foreach (var h in Until(() => S.State.Self.Travel is SelfTravel.TeleportArrived or SelfTravel.Arriving or SelfTravel.Materialising, 30))
            {
                yield return h;
            }

            foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present && LoginCompleteConfirmedSince(total), 30))
            {
                yield return h;
            }

            Require(S.State.Self.Travel == SelfTravel.Present, $"travel {what}: the teleport did not end present (travel {S.State.Self.Travel})");
            foreach (var h in Pause(3))
            {
                yield return h;
            }
        }
    }

    /// <summary>Waits for the player to be present in landblock <paramref name="landblock"/> (the high word of the cell id) after a teleport that began since <paramref name="travelMark"/>.</summary>
    protected IEnumerable<ActionHandle?> ArriveIn(uint landblock, int travelMark, string what)
    {
        foreach (var h in Until(() => TeleportSince(travelMark), TeleportStartSeconds))
        {
            yield return h;
        }

        Require(TeleportSince(travelMark), $"{what}: no teleport began within {TeleportStartSeconds:0} s of the cast");
        foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present, PresentSeconds))
        {
            yield return h;
        }

        Require(S.State.Self.Travel == SelfTravel.Present, $"{what}: the player was not present within {PresentSeconds:0} s of the teleport (travel {S.State.Self.Travel})");
        Require(S.State.Self.Position is { } p && (p.CellId >> 16) == landblock, $"{what}: the player arrived in cell {(S.State.Self.Position is { } q ? "0x" + q.CellId.ToString("X8", CultureInfo.InvariantCulture) : "none")}, not in landblock 0x{landblock:X4}");
        foreach (var h in Pause(3))
        {
            yield return h;
        }
    }

    /// <summary>The nearest lifestone (the description flag) within <paramref name="metres"/>, or 0.</summary>
    protected uint FindLifestone(float metres) => S.State.Self.Position is { } here && S.State.Objects.NearestWithFlags(WorldObjectTable.LifestoneFlag, in here, metres, out var guid) ? guid : 0;

    /// <summary>True once <see cref="StepOffLifestone"/> walked.</summary>
    protected bool SteppedOff { get; private set; }

    /// <summary>
    /// How far past the lifestone's collision (the stone's reach and the body's radius) the step-off's computed point lies, in metres: a point outside the home landblock is the stone's own reach
    /// plus this (<c>TODO(m5-lifestone-point-other-towns)</c>: the journey ground's home point is a profile value, another town's is computed).
    /// </summary>
    protected const float StepOffMarginMetres = 1.0f;

    /// <summary>
    /// The named fallback of a recall that arrives inside a lifestone (M4 4.2 FS, kept by M5 item 5 l1): walks off the lifestone; with <paramref name="collisionOnly"/> only when the client's body still stands within the stone's
    /// collision. Since the server-object physics host (h1, h2) the stone is solid in the body's world, and the arrival's sliding placement (<c>CPhysicsObj::SetPosition</c> with Placement and Slide,
    /// 11.6096 0x00516E10 and 0x00516DB0) puts the body 2.2 m out of it where ACE's own placement does, so the expected result is no walk (<c>stepped_off=no</c>). The test is the stone's own collision
    /// (its Setup's spheres and cylinders at its scale and position, against the body's sphere), never a distance constant. When the host cannot say where the body stands (the Godot journey host answers
    /// from the player's walker since M5 item 5 c1, <see cref="IJourneyWalks.BodyPosition"/>; null only while the body is not placed) or the dats cannot give the stone's shape, the server's position stands in for the body and the walk happens, logged.
    /// The walk goes to the journey ground's home lifestone point when the stone is in the home landblock, else to a point the stone's own reach puts outside it
    /// (<c>TODO(m5-lifestone-point-other-towns)</c>), and fails, named, only when the host has no such stone. <see cref="SteppedOff"/> says whether it walked.
    /// </summary>
    /// <param name="withinMetres">How near a lifestone must be to look at it.</param>
    /// <param name="where">How the player came to stand there, for the lines (e.g. "the recall arrived").</param>
    /// <param name="why">Why it steps off, for the log line.</param>
    /// <param name="collisionOnly">True (the recall home's): walk only when the body is still within the stone's collision. False (the hero's den-travel: the crystal must not fill the chase camera, a view matter, not a collision one): walk whenever a lifestone is within <paramref name="withinMetres"/>.</param>
    protected IEnumerable<ActionHandle?> StepOffLifestone(float withinMetres, string where, string why, bool collisionOnly = false)
    {
        var lifestone = FindLifestone(withinMetres);
        if (lifestone == 0)
        {
            yield break;
        }

        S.State.Objects.TryGet(lifestone, out var stone);
        var verdict = BodyInStone(stone, out var body, out var reach, out var detail);
        if (collisionOnly && verdict == false)
        {
            Log($"{where}: the body stands clear of lifestone {lifestone:X8} ({detail}): no step-off");
            yield break;
        }

        var cell = S.State.Self.Position?.CellId ?? 0;
        (uint Cell, Vector3 At)? target = null;
        if (cell >> 16 == context.Dungeon.Home.LifestoneCell >> 16)
        {
            target = HomeLifestone;
        }
        else if (stone?.Position is { } there && reach > 0f)
        {
            var away = body is { } b && Vector2.Distance(new Vector2(b.Frame.Origin.X, b.Frame.Origin.Y), new Vector2(there.Frame.Origin.X, there.Frame.Origin.Y)) > 0.3f
                ? Vector2.Normalize(new Vector2(b.Frame.Origin.X - there.Frame.Origin.X, b.Frame.Origin.Y - there.Frame.Origin.Y))
                : Vector2.UnitY;
            var at = there.Frame.Origin + (new Vector3(away.X, away.Y, 0f) * (reach + StepOffMarginMetres));
            if (CellIds.LandCellAt(LandblockCoord.FromId(there.CellId), at.X, at.Y) is { } landCell)
            {
                target = (landCell, at);
            }
        }

        Require(
            target is not null,
            $"{Name}: {where} in lifestone {lifestone:X8} in cell 0x{cell:X8}, outside {context.Dungeon.Home.Town}, and the host has no such lifestone object with an outdoor position and a collision shape to step off to (TODO(m5-lifestone-point-other-towns))");
        Log($"{where} in lifestone {lifestone:X8} (within {withinMetres} m; {detail}): stepping off it {why}");
        foreach (var h in Go(new TravelRequest(Destination("home-lifestone-point", target!.Value.Cell, target.Value.At), TravelPurpose.Bind), "off the lifestone"))
        {
            yield return h;
        }

        SteppedOff = true;
    }

    /// <summary>
    /// Whether the client's body stands within <paramref name="stone"/>'s collision: true or false from the stone's Setup (spheres and cylinder-spheres at its scale and position, the server-object
    /// physics host's own shape) against the body's sphere; null when a shape is unknown. When the host cannot say where the body stands the server's position stands in for it (true when that is
    /// inside, null when it is not), and <paramref name="detail"/> says so.
    /// </summary>
    private bool? BodyInStone(WorldObject? stone, out CellFrame? body, out float reach, out string detail)
    {
        reach = 0f;
        body = null;
        var dats = context.Dats;
        var stoneSetupId = stone?.Physics.SetupId ?? 0;
        if (stone?.Position is not { } stoneAt || dats is null || stoneSetupId == 0 || S.State.Self.Position is not { } serverAt)
        {
            detail = "the stone's shape is unknown, so the server's position stands in for the body";
            return null;
        }

        body = (context.Runners as IJourneyWalks)?.BodyPosition(context);
        var selfSetup = S.State.Objects.TryGet(S.State.Self.Guid, out var self) ? self.Physics.SetupId ?? 0 : 0;
        var geometry = new PhysicsGeometryLibrary(dats);
        var shape = PhysicsSetup.Load(dats, stoneSetupId, geometry);
        var bodyShape = selfSetup == 0 ? null : PhysicsSetup.Load(dats, selfSetup, geometry);
        if (shape is null || bodyShape is null || bodyShape.Spheres.Count == 0)
        {
            detail = "the stone's or the body's shape is not in the dats, so the server's position stands in for the body";
            return null;
        }

        var scale = stone.Physics.Scale is { } sc and > 0f ? sc : 1f;
        var stoneGlobal = stoneAt.ToGlobal();
        var bodyFrame = body ?? serverAt;
        var bodyCentre = bodyFrame.ToGlobal() + Vector3.Transform(bodyShape.Spheres[0].Center, bodyFrame.Frame.Orientation);
        var rb = bodyShape.Spheres[0].Radius;
        var inside = false;
        var nearest = double.MaxValue;
        foreach (var sphere in shape.Spheres)
        {
            var c = stoneGlobal + Vector3.Transform(sphere.Center * scale, stoneAt.Frame.Orientation);
            var d = c.DistanceTo(bodyCentre);
            nearest = Math.Min(nearest, d - ((sphere.Radius * scale) + rb));
            inside |= d < (sphere.Radius * scale) + rb;
            reach = Math.Max(reach, (new Vector2(sphere.Center.X, sphere.Center.Y) * scale).Length() + (sphere.Radius * scale) + rb);
        }

        foreach (var cyl in shape.CylSpheres)
        {
            var low = stoneGlobal + Vector3.Transform(cyl.LowPoint * scale, stoneAt.Frame.Orientation);
            var flat = Math.Sqrt(((low.X - bodyCentre.X) * (low.X - bodyCentre.X)) + ((low.Y - bodyCentre.Y) * (low.Y - bodyCentre.Y)));
            nearest = Math.Min(nearest, flat - ((cyl.Radius * scale) + rb));
            inside |= flat < (cyl.Radius * scale) + rb && bodyCentre.Z > low.Z - rb && bodyCentre.Z < low.Z + (cyl.Height * scale) + rb;
            reach = Math.Max(reach, (new Vector2(cyl.LowPoint.X, cyl.LowPoint.Y) * scale).Length() + (cyl.Radius * scale) + rb);
        }

        var known = body is not null;
        detail = string.Create(CultureInfo.InvariantCulture, $"{(known ? "the body" : "the server's position, for the body")} is {nearest:F2} m {(nearest < 0 ? "inside" : "outside")} the stone's collision");
        if (known)
        {
            return inside;
        }

        detail += " (the host cannot say where the body stands: it is not placed)";
        return inside ? true : null;
    }

    /// <summary>The cast's refusal, if any: the chat's WeenieError since the cast, else the handle's server error code.</summary>
    protected static uint Refusal(TravelLeg leg, CastResult cast)
    {
        var chat = leg.RefusalSince(cast.Mark);
        return chat != 0 ? chat : cast.Handle?.Outcome is { Kind: ActionOutcomeKind.ServerError, Code: var code } ? code : 0;
    }

    /// <summary>The last cast's handle and the marks before it.</summary>
    protected sealed class CastResult
    {
        /// <summary>The last attempt's handle.</summary>
        public ActionHandle? Handle { get; set; }

        /// <summary>The chat mark before the last attempt.</summary>
        public int Mark { get; set; }

        /// <summary>The travel mark before the last attempt.</summary>
        public int TravelMark { get; set; }
    }
}

/// <summary>
/// <c>bind</c> (M4 P5b J3b, W0 section 2): on foot to Holtburg's lifestone (<see cref="TravelPurpose.Bind"/>, the lifestone as an object goal in use range), <c>Use</c> it ("You have attuned your spirit to this Lifestone ...":
/// <see cref="JourneyFacts.Bound"/>; ACE's Sanctuary, <c>Lifestone.cs</c>:60-66 at 95ef426d), then Lifestone Tie 2644 at it ("You have successfully linked with the life stone.": <see cref="JourneyFacts.LifestoneTied"/>; ACE's LinkedLifestone,
/// <c>WorldObject_Magic.cs</c>:966-976, which Lifestone Recall reads). The fighters cast inside a <see cref="CasterSwap"/> (In, cast, Out); the mage goes to the magic mode with its wand and back to peace. Fields: <c>bound= tied= swap_s=</c>
/// and the report-only <c>tie_fizzles=</c>.
/// </summary>
public sealed class BindStation : IStation
{
    /// <summary>Lifestone Tie.</summary>
    public const uint LifestoneTieSpell = 2644;

    /// <summary>How near the lifestone the walk ends, in metres (use range; the W0 probe used it from 4.9 m).</summary>
    public const double UseRangeMetres = 4.0;

    /// <summary>The station's name (the verdict's <c>station=</c>).</summary>
    public const string StationName = "bind";

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(StationWhere.Holtburg, Peace: true);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context);
    }

    private sealed class Run(JourneyContext context) : TravelLeg(StationName, context)
    {
        private bool _bound;
        private bool _tied;
        private double _swapSeconds;

        public override string VerdictFields() =>
            string.Create(CultureInfo.InvariantCulture, $"bound={(_bound ? "yes" : "no")} tied={(_tied ? "yes" : "no")} swap_s={_swapSeconds:0.0} tie_fizzles={Fizzles}") + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

        protected override IEnumerable<ActionHandle?> Body()
        {
            Stage = "find";
            var lifestone = FindLifestone(150f);
            if (lifestone == 0)
            {
                // M4 P5b F2 (live round 2): the Archer started by Thelnoth's shop, too far to see the lifestone. Walk to its known point first (a point goal), then find it by the flag.
                Log("the lifestone is not in view: the fallback walk to its known point first (once)");
                Stage = "walk-to-point";
                foreach (var h in Go(new TravelRequest(Destination("home-lifestone-point", HomeLifestone.Cell, HomeLifestone.At), TravelPurpose.Bind), "to the lifestone's known point"))
                {
                    yield return h;
                }

                Stage = "find";
            }

            foreach (var h in Until(() => lifestone != 0 || (lifestone = FindLifestone(150f)) != 0, FindSeconds))
            {
                yield return h;
            }

            Require(lifestone != 0, $"bind: no lifestone (the BF_LIFESTONE description flag) within 150 m of the player in {(S.State.Self.Position is { } p ? "cell 0x" + p.CellId.ToString("X8", CultureInfo.InvariantCulture) : "no cell")} within {FindSeconds:0} s");
            var at = S.State.Objects.TryGet(lifestone, out var o) && o.Position is { } op ? (op.CellId, op.Origin) : HomeLifestone;
            Log($"found the lifestone {lifestone:X8}");

            Stage = "walk";
            foreach (var h in Go(new TravelRequest(Destination("home-lifestone", at.Item1, at.Item2), TravelPurpose.Bind, lifestone, UseRangeMetres), "to the lifestone"))
            {
                yield return h;
            }

            Stage = "use";
            foreach (var h in PeaceNow())
            {
                yield return h;
            }

            var mark = ChatMark;
            var use = S.Actions.Use(lifestone, ActionSource.Automation);
            foreach (var h in Until(() => use.IsCompleted, 20))
            {
                yield return h;
            }

            foreach (var h in Until(() => TextSince(mark, "attuned your spirit"), TextSeconds))
            {
                yield return h;
            }

            _bound = TextSince(mark, "attuned your spirit");
            Context.Facts.Bound = Context.Facts.Bound || _bound;
            Check(_bound, $"bind: the Use of the lifestone said nothing of attuning the spirit ({Describe(use)})");

            Stage = "tie";
            CasterSwap? swap = null;
            if (FighterTemplate.ForName(Context.Character) is { HasMagic: true })
            {
                swap = CasterSwap.ForCharacter(Context.Character, S.Actions, S.State, Context.Log);
                Require(swap is not null, "bind: no caster swap for " + Context.Character);
                foreach (var h in RunSwap(swap!, SwapDirection.In, "swap-in"))
                {
                    yield return h;
                }
            }
            else
            {
                foreach (var h in MagicNow())
                {
                    yield return h;
                }
            }

            var cast = new CastResult();
            foreach (var h in Cast(LifestoneTieSpell, lifestone, cast))
            {
                yield return h;
            }

            foreach (var h in Until(() => TextSince(cast.Mark, "linked with the life stone") || Refusal(this, cast) != 0, TextSeconds))
            {
                yield return h;
            }

            _tied = TextSince(cast.Mark, "linked with the life stone");
            if (_tied)
            {
                Context.Facts.LifestoneTied = true;
            }
            else
            {
                var refusal = Refusal(this, cast);
                Check(false, $"bind: Lifestone Tie did not link ({(refusal != 0 ? Named(refusal) : Describe(cast.Handle!))})");
            }

            if (swap is not null)
            {
                foreach (var h in RunSwap(swap, SwapDirection.Out, "swap-out"))
                {
                    yield return h;
                }
            }
            else
            {
                foreach (var h in PeaceNow())
                {
                    yield return h;
                }
            }
        }

        private IEnumerable<ActionHandle?> RunSwap(CasterSwap swap, SwapDirection direction, string step)
        {
            Require(swap.Begin(direction, Now), $"bind: the caster swap refused to begin ({step})");
            while (swap.Step(S.Clock.Now) == SwapStatus.Running)
            {
                yield return null;
            }

            _swapSeconds += swap.Seconds;
            Log($"{step}: {swap.Status} {swap.Fields}");
            Require(swap.Status == SwapStatus.Done, $"bind: the caster swap ended {swap.Status} at {step}: {swap.Failure} {swap.Detail}");
        }
    }
}

/// <summary>
/// <c>den-travel</c> (the mage; M4 P5b J3b, D36, W0 section 3): unless <see cref="JourneyFacts.PortalTieOf"/> says the ground is untied, casts the ground's Primary Portal Recall 48 and waits for the arrival in the journey's dungeon ground's arrival landblock (<see cref="JourneyContext.Dungeon"/>, the den's 0x0105; present about 10 s after the cast; the recall lands INSIDE the den,
/// the portal's destination, <c>WorldObject_Magic.cs</c>:1105-1175 at 95ef426d, so there is no portal walk): the ground tied, <see cref="JourneyFacts.Recalls"/> up by one. Refused 0x04A3 (YouMustLinkToPortalToRecall: not tied),
/// or when the facts already say it, the first-ever journey's path: <see cref="TravelPurpose.Ground"/> to the portal's front (the ground's <see cref="GroundPortal.FrontCell"/> and <see cref="GroundPortal.Front"/>, a journey's travel refuses it, <c>travel:ground:untied</c>, since D50 ties at build time and the first tie's walk is <c>TODO(m6-first-tie-walk)</c>), Primary Portal Tie at the portal (the ground's <see cref="GroundPortal.Wcid"/>,
/// "You have successfully linked with the portal."; every portal fact is the ground profile's, none is a constant here), then the portal on foot (an object goal, <see cref="TravelPurpose.Seek"/>, and <c>Use</c>) into the den. Every other refusal is named and fails the station. Fields:
/// <c>den=recall|portal portal_tied= recalls= den_s=</c>.
/// <para><b>The hero's step-off</b> (the M4 hero video; <see cref="JourneyPlans.StepOffArgument"/>, the hero mage's plan only): the bind leaves the mage in the lifestone's use range, and a recall cast
/// there is filmed through the crystal (the chase camera behind the mage looks into it). With the step-off, a mage within <see cref="StepOffMetres"/> of a lifestone first walks to Holtburg's
/// lifestone point (as the recall home's step-off does) and casts there; the fields add <c>stepped_off=yes|no</c>. The gate's den-travel has no step-off and its fields are unchanged.</para>
/// </summary>
public sealed class DenTravelStation : IStation
{
    /// <summary>A den-travel without the hero's step-off (the gate's).</summary>
    public DenTravelStation()
        : this(stepOffLifestone: false)
    {
    }

    /// <summary>A den-travel; with <paramref name="stepOffLifestone"/> the mage steps off a lifestone before the recall's cast (the hero mage's). The dungeon is the journey's (<see cref="JourneyContext.Dungeon"/>, read when the station starts).</summary>
    /// <param name="stepOffLifestone">True to step off a lifestone within <see cref="StepOffMetres"/> first.</param>
    public DenTravelStation(bool stepOffLifestone)
    {
        StepOff = stepOffLifestone;
    }

    /// <summary>
    /// A lifestone nearer than this before the cast is stepped off (Lifestone's own: Holtburg's lifestone point is 4.9 m from the lifestone's centre, and the bind's use range leaves the mage
    /// about 2 m from it).
    /// </summary>
    public const float StepOffMetres = 4f;

    /// <summary>True when the station steps off a lifestone before the recall's cast.</summary>
    public bool StepOff { get; }

    /// <summary>Primary Portal Tie.</summary>
    public const uint PortalTieSpell = 47;

    /// <summary>Primary Portal Recall.</summary>
    public const uint PortalRecallSpell = 48;

    /// <summary>How near the portal the walk ends, in metres.</summary>
    public const double PortalRangeMetres = 2.0;

    /// <summary>The station's name.</summary>
    public const string StationName = "den-travel";

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(MinManaPercent: 20);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context, StepOff, context.Dungeon.ArrivalLandblock(), context.Dungeon.Portal, context.Dungeon.Key);
    }

    private sealed class Run(JourneyContext context, bool stepOff, uint arrivalLandblock, GroundPortal? portalRecord, string groundKey) : TravelLeg(StationName, context)
    {
        private string _how = "none";
        private int _recalls;
        private double _seconds;

        public override string VerdictFields() =>
            string.Create(CultureInfo.InvariantCulture, $"den={_how} portal_tied={Context.Facts.PortalTieOf(groundKey) switch { PortalTie.Tied => "yes", PortalTie.Untied => "no", _ => "unknown" }} recalls={_recalls} den_s={_seconds:0.0}")
            + (stepOff ? " stepped_off=" + (SteppedOff ? "yes" : "no") : string.Empty) + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

        protected override IEnumerable<ActionHandle?> Body()
        {
            var started = S.Clock.Now;
            var viaPortal = Context.Facts.PortalTieOf(groundKey) == PortalTie.Untied;
            if (viaPortal)
            {
                Log($"the {groundKey} portal is known untied (TODO(m6-first-tie-walk): an untied mage's way to the portal is the first tie's walk, not a place)");
            }
            else
            {
                if (stepOff)
                {
                    Stage = "step-off";
                    foreach (var h in StepOffLifestone(StepOffMetres, "the mage stands", "before Primary Portal Recall's cast (the hero's step-off: the crystal would fill the chase camera)"))
                    {
                        yield return h;
                    }
                }

                Stage = "recall";
                foreach (var h in MagicNow())
                {
                    yield return h;
                }

                var cast = new CastResult();
                foreach (var h in Cast(portalRecord?.RecallSpell ?? PortalRecallSpell, 0, cast))
                {
                    yield return h;
                }

                foreach (var h in Until(() => Refusal(this, cast) != 0 || TeleportSince(cast.TravelMark), TeleportStartSeconds))
                {
                    yield return h;
                }

                var refusal = Refusal(this, cast);
                if (refusal == 0x04A3)
                {
                    Context.Facts.SetPortalTie(groundKey, PortalTie.Untied);
                    viaPortal = true;
                    Log($"Primary Portal Recall refused: {Named(refusal)}; the {groundKey} portal is untied (TODO(m6-first-tie-walk): the walk to its portal is not a place)");
                }
                else
                {
                    Require(refusal == 0, "den-travel: Primary Portal Recall was refused: " + Named(refusal));
                    foreach (var h in ArriveIn(arrivalLandblock, cast.TravelMark, "den-travel recall"))
                    {
                        yield return h;
                    }

                    Context.Facts.SetPortalTie(groundKey, PortalTie.Tied);
                    Context.Facts.Recalls++;
                    _recalls++;
                    _how = "recall";
                }
            }

            if (viaPortal)
            {
                foreach (var h in ViaPortal())
                {
                    yield return h;
                }
            }

            _seconds = S.Clock.Now - started;
        }

        private IEnumerable<ActionHandle?> ViaPortal()
        {
            Require(portalRecord is not null, $"den-travel: the ground '{groundKey}' has no portal record, so there is no portal to tie to (TODO(m6-ground-fields))");
            var record = portalRecord!;
            Stage = "portal-front";
            foreach (var h in Go(new TravelRequest(Destination("den-portal-front", record.FrontCell, record.Front), TravelPurpose.Ground), "to the den portal's front"))
            {
                yield return h;
            }

            var portal = 0u;
            foreach (var h in Until(() => (portal = FindPortal(record.Wcid)) != 0, FindSeconds))
            {
                yield return h;
            }

            Require(portal != 0, $"den-travel: no object of weenie {record.Wcid} within {FindSeconds:0} s of the arrival");

            Stage = "tie";
            foreach (var h in MagicNow())
            {
                yield return h;
            }

            var tie = new CastResult();
            foreach (var h in Cast(record.TieSpell, portal, tie))
            {
                yield return h;
            }

            foreach (var h in Until(() => TextSince(tie.Mark, "linked with the portal") || Refusal(this, tie) != 0, TextSeconds))
            {
                yield return h;
            }

            var tied = TextSince(tie.Mark, "linked with the portal");
            Require(tied, $"den-travel: Primary Portal Tie did not link ({(Refusal(this, tie) is var r and not 0 ? Named(r) : Describe(tie.Handle!))})");
            Context.Facts.SetPortalTie(groundKey, PortalTie.Tied);

            Stage = "portal";
            foreach (var h in PeaceNow())
            {
                yield return h;
            }

            var at = S.State.Objects.TryGet(portal, out var o) && o.Position is { } op ? (op.CellId, op.Origin) : (record.Cell, record.Origin);
            foreach (var h in Go(new TravelRequest(Destination("den-portal", at.Item1, at.Item2), TravelPurpose.Seek, portal, PortalRangeMetres), "to the den portal"))
            {
                yield return h;
            }

            var travelMark = TravelMark;
            var use = S.Actions.Use(portal, ActionSource.Automation);
            foreach (var h in ArriveIn(arrivalLandblock, travelMark, "den-travel portal"))
            {
                yield return h;
            }

            Check(use.Outcome is { Kind: ActionOutcomeKind.Confirmed } or null, $"den-travel: Use of the portal: {Describe(use)}");
            _how = "portal";
        }

        private uint FindPortal(uint wcid)
        {
            foreach (var o in S.State.Objects.All)
            {
                if (o.Weenie.WeenieClassId == wcid)
                {
                    return o.Guid;
                }
            }

            return 0;
        }
    }
}

/// <summary>
/// <c>recall-home</c> (M4 P5b J3b, D38): Lifestone Recall to the lifestone the character is tied to, through <see cref="RecallHome"/> (P5a C1c: the fighters over their <see cref="CasterSwap"/> via <see cref="RecallHome.ForCharacter"/>, the mage alone).
/// A recall that arrives adds to <see cref="JourneyFacts.Recalls"/> and says the lifestone is tied (<see cref="JourneyFacts.LifestoneTied"/> true); a refusal 0x049E (YouMustLinkToLifestoneToRecall) says it is not (false) and fails the station, named.
/// Fields are RecallHome's own: <c>recalls= recall_s= recall_fizzles=</c> (and <c>recall_why=</c> on a failure), then <c>stepped_off=yes|no</c>.
/// <para><b>Off the lifestone</b> (M4 4.2 FS, live gate round 1; M5 item 5 l1): the recall arrives at the tied lifestone's own position (ACE WorldObject_Magic.cs 1087-1095
/// recalls to <c>LinkedLifestone</c>, the lifestone's location), inside its collision. Retail's client places itself out of it on arrival
/// (<c>SmartBox::TeleportPlayer</c>, a sliding <c>SetPositionSimple</c>, 11.6096 0x00516DB0), and since M5 item 5 h1 and h2 the lifestone is a physics object in the body's world
/// (<c>ServerObjectPhysicsHost</c>), so Lifestone's body slides out the same way: 2.2 m north of the stone's centre at Holtburg, where ACE's own placement puts a character that
/// logs in there (ACE Player_Networking.cs 224 before WorldObject.cs 209-220), so the next login's where-am-I agrees (<c>LifestonePlacementTests</c>). A stone created after the teleport
/// is handled by the headless body's re-placement (<c>TODO(m5-create-overlap)</c>). The station therefore ends with <c>stepped_off=no</c>. The walk to the lifestone's point remains as the named
/// fallback (<c>TravelLeg.StepOffLifestone</c>): it runs only when the client's body still stands within the stone's collision, measured from the stone's own shape, and a lifestone
/// in view but not that close is left alone.</para>
/// </summary>
public sealed class RecallHomeStation : IStation
{
    /// <summary>The station's name.</summary>
    public const string StationName = "recall-home";

    /// <summary>
    /// A player this close to a lifestone object stands in it (Lifestone's own: the Holtburg lifestone's placement moves a human body 2.22 m from its centre,
    /// <c>LifestonePlacementTests</c>; the recall's arrival is 0 m).
    /// </summary>
    public const float OnLifestoneMetres = 2.5f;

    /// <inheritdoc />
    public string Name => StationName;

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(MinManaPercent: 20);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context);
    }

    private sealed class Run(JourneyContext context) : TravelLeg(StationName, context)
    {
        private RecallHome? _recall;

        public override string VerdictFields() =>
            (_recall?.Fields ?? "recalls=0 recall_s=0.0") + " stepped_off=" + (SteppedOff ? "yes" : "no") + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

        protected override IEnumerable<ActionHandle?> Body()
        {
            Stage = "recall";
            _recall = RecallHome.ForCharacter(S, Context.Character, Context.Log);
            Require(_recall is not null, $"recall-home: {Context.Character} has no way home (only the Brawler, the Archer and BSD Mage cast Lifestone Recall)");
            try
            {
                Require(_recall!.Begin(S.Clock.Now), "recall-home: a recall was already running");
                while (_recall.Step(S.Clock.Now) == RecallStatus.Running)
                {
                    yield return null;
                }

                if (_recall.Status == RecallStatus.Done)
                {
                    Context.Facts.Recalls += _recall.Recalls;
                    Context.Facts.LifestoneTied = true;
                }
                else
                {
                    if (_recall.Detail?.Contains("0x049E", StringComparison.Ordinal) == true)
                    {
                        Context.Facts.LifestoneTied = false;
                    }

                    Require(false, $"recall-home: {_recall.Failure}: {_recall.Detail}");
                }
            }
            finally
            {
                _recall!.Dispose();
            }

            Stage = "step-off";
            foreach (var h in StepOffLifestone(OnLifestoneMetres, "the recall arrived", "before the station ends (the fallback: the body is still within its collision)", collisionOnly: true))
            {
                yield return h;
            }
        }
    }
}
