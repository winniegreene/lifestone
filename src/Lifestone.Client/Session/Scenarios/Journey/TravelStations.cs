using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Movement.Wayfinding;
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
internal abstract partial class TravelLeg(string name, JourneyContext context) : ScriptLeg(name, text => context.Log.Info("journey", text)), IStationWait, IStationCommitted
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
    protected internal const double ModeSeconds = 10;

    /// <summary>M6 14: how many times <see cref="Mode"/> asks for a mode another action undid after the server's answer (the first ask and two more).</summary>
    protected const int ModeAsks = 3;

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
    private IDisposable? _diedSubscription;
    private readonly List<PlayerDied> _died = [];
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
        _diedSubscription = S.Events.Subscribe<PlayerDied>((in PlayerDied e) => _died.Add(e)); // M6 4 F6: the death notice, read by the walking legs' check after (TravelLegSafety.cs)
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
            _diedSubscription.Dispose();
            DisposeSafety();
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

    /// <summary>M6 4 F6: the number of death notices so far (a mark for <see cref="DiedSince"/>).</summary>
    protected int DeathMark => _died.Count;

    /// <summary>M6 4 F6: the first death notice since <paramref name="mark"/> (<see cref="PlayerDied"/>: ACE's 0x01AC or the 0x019E broadcast), or null.</summary>
    protected PlayerDied? DiedSince(int mark) => mark < _died.Count ? _died[mark] : null;

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

    /// <summary>The seconds a travel cast waits for the mana its spell costs (M6 7 BC: the station's 20% need is under the cost of a 185-mana climber's 50-mana recall or tie).</summary>
    public const double SpellManaSeconds = 120;

    /// <summary>M6 7 BC: false when <see cref="RestForSpell"/> ended with the mana still under the spell's cost.</summary>
    protected bool SpellManaMet { get; private set; } = true;

    /// <inheritdoc />
    /// <remarks>M6 7 BD: the rest in place for a travel cast's mana (<see cref="RestForSpell"/>).</remarks>
    public string? Waiting { get; private set; }

    /// <summary>
    /// M6 7 BC (live, gate A climb 19: Lifestone Tie refused at 45 mana for a 50 cost, then the recall at 47): check before a travel cast, the mana held against the spell's cost (<see cref="GearCasts.ManaCeiling"/>, what ACE's
    /// 0x0401 reads); short, the leg rests in place up to <see cref="SpellManaSeconds"/> (the mana comes back by itself), then checks again. <see cref="SpellManaMet"/> says which. A spell the table does not hold, or a mana the state does not know, is no wait.
    /// </summary>
    /// <param name="spell">The spell's id.</param>
    /// <param name="seconds">The most seconds to wait.</param>
    protected IEnumerable<ActionHandle?> RestForSpell(uint spell, double seconds = SpellManaSeconds)
    {
        SpellManaMet = true;
        if (!Context.Spells.TryGetValue(spell, out var info))
        {
            yield break;
        }

        var need = GearCasts.ManaCeiling(info, S.State.Self.Inventory.Equipped.Count);
        if (!S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var held, out var max) || held >= need)
        {
            yield break;
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"mana {held} of {max} is under {info.Name}'s {need}: resting in place up to {seconds:0} s for the cast (check before)"));
        Waiting = $"the rest in place for {info.Name}'s mana";
        foreach (var h in Until(() => !S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var now, out _) || now >= need, seconds))
        {
            yield return h;
        }

        Waiting = null;
        SpellManaMet = !S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var after, out _) || after >= need;
        Log(string.Create(CultureInfo.InvariantCulture, $"mana for {info.Name}: {(S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var shown, out _) ? shown : 0)} against {need}: {(SpellManaMet ? "met" : "not met")} (check after)"));
    }

    /// <summary>Waits until the combat mode is <paramref name="wanted"/>.</summary>
    protected IEnumerable<ActionHandle?> Mode(CombatMode wanted, int modeValue)
    {
        if (S.State.Self.Qualities.CombatMode == modeValue)
        {
            yield break;
        }

        var mode = S.Actions.SetCombatMode(wanted, ActionSource.Automation);
        var asks = 1;
        for (; ;)
        {
            // check after: the server's answer, or the mode left again after it (M6 14, live 2026-11-14 19:19:15 and 19:22:19, ladder15: the walk's peace landed, the vitals guard set the magic mode for a heal while the
            // station was held, and the station waited out its seconds in the magic mode and gave the crossing up): a confirmed change that another action undid is asked again, at most ModeAsks times, each named
            foreach (var h in Until(() => mode.IsCompleted && (S.State.Self.Qualities.CombatMode == modeValue || asks < ModeAsks), ModeSeconds))
            {
                yield return h;
            }

            if (S.State.Self.Qualities.CombatMode == modeValue || !mode.IsCompleted || asks >= ModeAsks)
            {
                break;
            }

            asks++;
            Log(string.Create(CultureInfo.InvariantCulture, $"the {wanted} mode was left after the server's answer ({Describe(mode)}; the mode is {S.State.Self.Qualities.CombatMode} now): asked again ({asks} of {ModeAsks})"));
            mode = S.Actions.SetCombatMode(wanted, ActionSource.Automation);
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
    /// <param name="request">The travel request.</param>
    /// <param name="what">The walk in words (logged).</param>
    /// <param name="stopWhen">M6 4 F6: checked each step of the walk; true stops it (the travel leg is disposed, <see cref="WalkInterrupted"/> is set, nothing else is checked) so the caller can fight back an attacker and plan the walk again; null: never.</param>
    protected IEnumerable<ActionHandle?> Go(TravelRequest request, string what, Func<string?>? stopWhen = null)
    {
        var total = S.ActionLog.Total;
        WalkInterrupted = null;
        using var leg = context.Travel.GoTo(S, request, S.Clock.Now);
        Log($"travel {what}: {request.Purpose} to {request.Destination.Name}{(request.ObjectGuid == 0 ? string.Empty : $" (object {request.ObjectGuid:X8}, range {request.Range:0.#} m)")}");
        var status = TravelStatus.Running;
        while ((status = leg.Step(S, S.Clock.Now)) == TravelStatus.Running)
        {
            if (stopWhen?.Invoke() is { } stop)
            {
                WalkInterrupted = stop;
                Log($"travel {what}: the walk is stopped: {stop}");
                yield break;
            }

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

    /// <summary>M6 4 F6: why the last <see cref="Go"/> walk was stopped by its <c>stopWhen</c> (the walk ended neither arrived nor failed), or null.</summary>
    protected string? WalkInterrupted { get; private set; }

    /// <summary>The seconds the leg's on-foot walks to a point took (<see cref="WalkToPoint"/>; report-only, the verdict's <c>walk_s=</c>).</summary>
    protected double WalkedSeconds { get; private set; }

    /// <summary>
    /// A walk on foot to a point through the journey's travel seam (a point goal, M6 item 0 a6), check, act, check (M5 D141): before, the purpose (M5 D47) and where the player stands are logged and a player already within
    /// <see cref="JourneyTravel.ToleranceMetres"/> of the point in its landblock walks nowhere; the act is the seam's request (<paramref name="purpose"/>, never a place or a recall); after, the player must stand in the point's landblock within
    /// <see cref="JourneyTravel.ToleranceMetres"/> of it, else the leg fails, named (<paramref name="name"/>:arrival-off-point).
    /// </summary>
    /// <param name="name">The station's word for the walk (the failure's prefix).</param>
    /// <param name="label">The destination's label.</param>
    /// <param name="cell">The point's cell.</param>
    /// <param name="at">The point (landblock-local).</param>
    /// <param name="purpose">The travel purpose of the request (Cell for an arrival at a ground, Bind for the way to a lifestone).</param>
    /// <param name="why">The purpose in words (logged).</param>
    /// <param name="stopWhen">M6 4 F6: <see cref="Go"/>'s stop (an attacker to fight back); a stopped walk skips the check after and leaves <see cref="WalkInterrupted"/> set.</param>
    /// <param name="keepOff">M6 7 AI: for a walk to a portal, the other portals it keeps clear of (<see cref="RungPortal.KeepOffTheOthers"/>), or null.</param>
    /// <param name="way">M6 8 B1: the file's way to the point (<see cref="TravelRequest.Way"/>), or null: planned.</param>
    /// <param name="use">The hub walk: the object the walk ends by Using (<see cref="TravelRequest.Use"/>), or null.</param>
    protected IEnumerable<ActionHandle?> WalkToPoint(string name, string label, uint cell, Vector3 at, TravelPurpose purpose, string why, Func<string?>? stopWhen = null, PortalKeepOff? keepOff = null, IReadOnlyList<CellFrame>? way = null, NavUseTarget? use = null)
    {
        const float ArrivalSlackMetres = 0.5f; // the check after's slack over the walk's own tolerance: the body's stop, not a wider goal

        WalkInterrupted = null; // M6 8 G: a walk that has nowhere to go (below) was not stopped; the last walk's stop must not stand for it
        var (from, _) = WhereSheStands();
        var fromText = from is { } f ? string.Create(CultureInfo.InvariantCulture, $"cell 0x{f.CellId:X8} ({f.Frame.Origin.X:0.#}, {f.Frame.Origin.Y:0.#})") : "an unknown place";
        Log(string.Create(CultureInfo.InvariantCulture, $"{name}: walk check before: purpose: {why}; the player stands at {fromText}, the point is cell 0x{cell:X8} ({at.X:0.#}, {at.Y:0.#}, {at.Z:0.#})"));
        if (from is { } here && (here.CellId >> 16) == (cell >> 16) && Planar(here.Frame.Origin, at) <= JourneyTravel.ToleranceMetres)
        {
            Log($"{name}: the player already stands within {JourneyTravel.ToleranceMetres:0.#} m of the point; no walk");
            yield break;
        }

        var started = S.Clock.Now;
        foreach (var h in Go(new TravelRequest(Destination(label, cell, at), purpose, KeepOff: keepOff, Way: way, Use: use), why, stopWhen))
        {
            yield return h;
        }

        WalkedSeconds += S.Clock.Now - started;
        if (WalkInterrupted is { } stopped)
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"{name}: walk stopped before the point ({stopped}); walk_s={S.Clock.Now - started:0.0}"));
            yield break;
        }

        // M6 14 (live 2026-11-13 19:20:58 and 19:33:35 UTC: two walks ended Arrived at their points and failed this check 5.6 and 5.7 m off, reading the server's last position of her, a second of her buffed run behind): the check reads
        // where she stands, her body (the one world picture), and names the server's last report beside it.
        var (after, fromBody) = WhereSheStands();
        var server = S.State.Self.Position;
        var serverText = !fromBody ? "the server's last position (the host gives no body of its own)"
            : server is { } sv ? string.Create(CultureInfo.InvariantCulture, $"her body; the server's last report of her is cell 0x{sv.CellId:X8} ({sv.Frame.Origin.X:0.#}, {sv.Frame.Origin.Y:0.#}){(after is { } b && sv.CellId >> 16 == b.CellId >> 16 ? $", {Planar(sv.Frame.Origin, b.Frame.Origin):0.0} m from her body" : string.Empty)}")
            : "her body; the server has no position of her";

        // M6 8 (live 2026-11-11 19:42 UTC): the walk to the Facility Hub's portal in the Town Network ended 5.03 m from its point and failed this check by 3 cm; the route fell back to the run from the Refuge, which failed three
        // times of four and killed her once (20:12, a Jungle Reaver). The walk ends at the tolerance by the position it predicts; this check reads the position after the stop, so it allows the stop's slack.
        var ok = after is { } there && (there.CellId >> 16) == (cell >> 16) && Planar(there.Frame.Origin, at) <= JourneyTravel.ToleranceMetres + ArrivalSlackMetres;
        Require(ok, $"{name}: arrival-off-point: the walk to {label} ended at {(after is { } q ? string.Create(CultureInfo.InvariantCulture, $"cell 0x{q.CellId:X8} ({q.Frame.Origin.X:0.#}, {q.Frame.Origin.Y:0.#})") : "an unknown place")}, not within {JourneyTravel.ToleranceMetres:0.#} m of ({at.X:0.#}, {at.Y:0.#}) in cell 0x{cell:X8} (read from {serverText})");
        Log(string.Create(CultureInfo.InvariantCulture, $"{name}: walk check after: at the point, {Planar(after!.Value.Frame.Origin, at):0.0} m from it (read from {serverText}); walk_s={S.Clock.Now - started:0.0}"));
    }

    /// <summary>
    /// M6 14 (the one world picture, the owner's rule of 2026-11-12): where she stands for a walk's checks: her body, as the client draws it, when the host gives one (<see cref="IJourneyWalks.BodyPosition"/>, the Godot host's player and the
    /// headless host's walker) and she is present; else the server's last position (<see cref="SelfState.Position"/>), which ACE sends at most once a second while she runs, so at a run it lags her by up to a second of her speed.
    /// <c>Body</c> says which.
    /// </summary>
    protected (CellFrame? At, bool Body) WhereSheStands() =>
        S.State.Self.Travel == SelfTravel.Present && (context.Runners as IJourneyWalks)?.BodyPosition(context) is { } body ? (body, true) : (S.State.Self.Position, false);

    /// <summary>
    /// M6 14 (live 2026-11-13 19:20:59 UTC: the route fallback's Lifestone Recall was cast 1 s into the portal space a hop's walk had ended in, and refused four times, 0x001D): the check before a recall or a gem: a body in portal space or still
    /// arriving casts nothing; it waits for the arrival, at most <see cref="PresentSeconds"/>, one line before and one after (as the recall-home station does). A body still not present after it is the cast's own check's to name.
    /// </summary>
    /// <param name="what">The cast in words.</param>
    protected IEnumerable<ActionHandle?> ArrivalBeforeCast(string what)
    {
        if (S.State.Self.Travel == SelfTravel.Present)
        {
            yield break;
        }

        var from = S.Clock.Now;
        Log(string.Create(CultureInfo.InvariantCulture, $"{what}: check before: the character is in {S.State.Self.Travel} (portal space or arriving): waiting up to {PresentSeconds:0} s for the arrival before the cast"));
        foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present, PresentSeconds))
        {
            yield return h;
        }

        Log(string.Create(CultureInfo.InvariantCulture, $"{what}: check after the wait: travel {S.State.Self.Travel} after {S.Clock.Now - from:0.0} s, in cell {(S.State.Self.Position is { } p ? "0x" + p.CellId.ToString("X8", CultureInfo.InvariantCulture) : "none")}"));
    }

    private static double Planar(Vector3 a, Vector3 b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

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
    protected internal sealed class CastResult
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
/// and the report-only <c>tie_fizzles=</c>. M6 6 R1F: a book that holds no Lifestone Recall 1635 (the climber below 16) makes no tie (<c>tied=skipped</c>, the station ok on the bind alone); a tie that is cast and does not link stays the named failure.
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
        private bool _tieSkipped;
        private double _swapSeconds;

        public override string VerdictFields() =>
            string.Create(CultureInfo.InvariantCulture, $"bound={(_bound ? "yes" : "no")} tied={(_tied ? "yes" : _tieSkipped ? "skipped" : "no")} swap_s={_swapSeconds:0.0} tie_fizzles={Fizzles}") + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

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
            if (_bound)
            {
                // M6 6 FXD1 (6-D5): where the bind took, for the respawn's classification (the lifestone the Use was sent to: its object position, else the point the walk went to)
                Context.Facts.BoundAt = S.State.Objects.TryGet(lifestone, out var stone) && stone.Position is { } stoneAt ? stoneAt : new CellFrame(at.Item1, new Lifestone.Data.Animation.AcFrame(at.Item2, Quaternion.Identity));
                Log($"bound at the lifestone {lifestone:X8} in cell 0x{Context.Facts.BoundAt.Value.CellId:X8} (the respawn point, ACE's Sanctuary)");
            }

            Check(_bound, $"bind: the Use of the lifestone said nothing of attuning the spirit ({Describe(use)})");

            // M6 6 R1F (live round 1: a level-13 climber's tie fizzled four times and failed the station): Lifestone Tie serves Lifestone Recall alone (ACE's LinkedLifestone is read only by it, WorldObject_Magic.cs:966-976 @95ef426d),
            // so a book that is read and holds no Lifestone Recall 1635 makes no tie: check before, one line, the station ok on the bind alone (tied=skipped). A book that is not read yet casts the tie as before (the check cannot say).
            var book = S.State.Self.Spellbook;
            if ((book.IsKnown || book.Count > 0) && !book.Knows(RecallHome.LifestoneRecallSpell))
            {
                _tieSkipped = true;
                Log($"tie skipped: the spellbook ({book.Count} spells) holds no Lifestone Recall {RecallHome.LifestoneRecallSpell}, the one spell Lifestone Tie {LifestoneTieSpell} serves; the station is ok on the bind alone (tied=skipped)");
                yield break;
            }

            // M6 7 BC (live, gate A climb 19: 45 mana held, the tie costs 50, the station failed on a bind that had taken): the tie serves the recall alone, so it waits for its mana in place (check, rest, check) and a tie that
            // cannot be cast or does not link leaves the bind standing (bound=yes is what the audit needs; tied=no never fails the station; the recall home refused with 0x049E binds again, M5 7 F14).
            Stage = "tie-mana";
            foreach (var h in RestForSpell(LifestoneTieSpell))
            {
                yield return h;
            }

            if (!SpellManaMet)
            {
                Context.Log.Warn("journey", $"[live {StationName}] tie: the mana did not come back for Lifestone Tie {LifestoneTieSpell} within {SpellManaSeconds:0} s: the bind stands, tied=no (TODO(m6-bind-tie-mana): the next trip's learn step or the recall's 0x049E ties again)");
                yield break;
            }

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
                Context.Log.Warn("journey", $"[live {StationName}] tie: Lifestone Tie did not link ({(refusal != 0 ? Named(refusal) : Describe(cast.Handle!))}): the bind stands, tied=no (TODO(m6-bind-tie-mana))");
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
            if (Context.Dungeon.Kind == GroundKind.Outdoor)
            {
                // M6 item 0 a6: an outdoor ground (the belt, the forest) has no portal and no place: its arrival is a walk from Holtburg to its first cell's point (a climber has no Portal Recall at level 5, which is a level 3 Item spell).
                Stage = "outdoor-walk";
                foreach (var h in WalkToFirstCell())
                {
                    yield return h;
                }

                _how = "walk";
                _seconds = S.Clock.Now - started;
                yield break;
            }

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

                    Context.Facts.SetPrimaryPortalTie(groundKey); // M6 4 R2 (F1): the recall landed in the ground, so the one slot holds it and every other ground's tie is untied
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

        private IEnumerable<ActionHandle?> WalkToFirstCell()
        {
            var ground = Context.Dungeon;
            var first = WayHomeRule.FirstCell(ground);
            Require(first is not null, $"den-travel: the outdoor ground '{groundKey}' lists no cell to arrive at (TODO(m6-ground-fields))");
            Require(Context.Dats is not null, $"den-travel: the dats are not available, so the terrain height at the first cell of '{groundKey}' cannot be read");
            var cell = WayHomeRule.HuntCellOf(first!);
            var height = HuntGround.TerrainHeight(Context.Dats!, cell);
            Require(height is not null, $"den-travel: the first cell 0x{first!.Cell:X8} of '{groundKey}' has no terrain height");
            var at = HuntGround.StopPoint(cell, height!.Value);
            foreach (var h in WalkToPoint("den-travel", "ground-first-cell", first.Cell, at, TravelPurpose.Cell, $"to the first cell of {ground.Name} on foot from {ground.Home.Town}: an outdoor ground has no portal and no place, and the climber has no recall to it"))
            {
                yield return h;
            }
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
            Context.Facts.SetPrimaryPortalTie(groundKey); // M6 4 R2 (F1): ACE keeps one tie slot, so the link moves it here

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

    /// <summary>Seconds the recall waits for a body in portal space or arriving to be present (Lifestone's own: a portal space took 6.7 s live on 2026-11-12; the wait is four of those).</summary>
    public const double ArrivalWaitSeconds = 30;

    /// <summary>
    /// A player this close to a lifestone object stands in it (Lifestone's own: the Holtburg lifestone's placement moves a human body 2.22 m from its centre,
    /// <c>LifestonePlacementTests</c>; the recall's arrival is 0 m).
    /// </summary>
    public const float OnLifestoneMetres = 2.5f;

    /// <inheritdoc />
    public string Name => StationName;

    /// <summary>The least mana share the station starts at.</summary>
    public const double MinManaPercent = 20;

    /// <inheritdoc />
    public StationNeeds Needs { get; } = new(MinManaPercent: MinManaPercent);

    /// <inheritdoc />
    public IStationRun Start(JourneyContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Run(context);
    }

    private sealed class Run(JourneyContext context) : TravelLeg(StationName, context)
    {
        private RecallHome? _recall;
        private bool _walked;

        public override string VerdictFields() =>
            (_recall?.Fields ?? "recalls=0 recall_s=0.0") + " stepped_off=" + (SteppedOff ? "yes" : "no") + (_walked ? string.Create(CultureInfo.InvariantCulture, $" home=walk walk_s={WalkedSeconds:0.0}") : string.Empty) + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

        protected override IEnumerable<ActionHandle?> Body()
        {
            // M6 item 0 a6: a character whose spellbook is read and lacks Lifestone Recall (Winnie early in the climb) walks home from an outdoor ground the profile says is reachable on foot; any other case without the recall is a loud stop by name.
            var way = WayHomeRule.Choose(Context.Dungeon, S.State.Self.Spellbook);
            if (way.Kind != WayHomeKind.Recall)
            {
                Log($"way home: {way.Kind.ToString().ToLowerInvariant()}: {way.Why}");
                Require(way.Kind == WayHomeKind.Walk, $"recall-home: no way home: {way.Why}");
                Stage = "walk-home";
                var (homeCell, homeAt) = HomeLifestone;
                foreach (var h in WalkToPoint("recall-home", "home-lifestone-point", homeCell, homeAt, TravelPurpose.Bind, $"home to {Context.Dungeon.Home.Town}'s lifestone point on foot ({way.Why})"))
                {
                    yield return h;
                }

                _walked = true;
                yield break;
            }

            // M6 item 14 (m6-recall-waits-arrival; live 2026-11-13: a portal took her on a walk, the recovery's recall began 40 ms into the portal space, ended recall:present three times within 7 s and the run stopped, one second before she
            // arrived): check before: a body in portal space or arriving is waited for, bounded; then the recall's own check stands (still not present: recall:present, by name).
            if (S.State.Self.Travel != SelfTravel.Present)
            {
                Stage = "recall-arrival";
                var since = S.Clock.Now;
                Log(string.Create(CultureInfo.InvariantCulture, $"recall-home: check before: the character is in {S.State.Self.Travel} (portal space or arriving): waiting up to {ArrivalWaitSeconds:0} s for the arrival before the recall"));
                foreach (var h in Until(() => S.State.Self.Travel == SelfTravel.Present, ArrivalWaitSeconds))
                {
                    yield return h;
                }

                Log(string.Create(CultureInfo.InvariantCulture, $"recall-home: check after: travel {S.State.Self.Travel} after {S.Clock.Now - since:0.0} s{(S.State.Self.Travel == SelfTravel.Present ? string.Empty : ": still not present; the recall's own check names it")}"));
            }

            // M6 7 BC (live, gate A climb 19: the recall refused at 47 mana for a 50 cost, the station's need being 20% of 185): rest in place for the cast's mana first (check, rest, check); still short, the cast's own refusal names it.
            Stage = "recall-mana";
            foreach (var h in RestForSpell(RecallHome.LifestoneRecallSpell))
            {
                yield return h;
            }

            Stage = "recall";
            _recall = JourneyTravel.RecallFor(S, Context.Character, Context.Log); // M6 7 K: a character with the recall in its book, named or not
            Require(_recall is not null, $"recall-home: {Context.Character} has no way home (its spellbook holds no Lifestone Recall)");
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

                    if (_recall.Detail?.Contains("0x0400", StringComparison.Ordinal) == true || _recall.Detail?.Contains("0x400)", StringComparison.Ordinal) == true)
                    {
                        // M6 7 AV: named, so the autopilot takes the way home without the recall for the rest of the run (the trip that follows buys the components).
                        Context.Facts.LifestoneRecallUnavailable = true;
                        Log("recall-unavailable:components: ACE refused Lifestone Recall for a missing component (0x0400): the recall counts as not available for the rest of this run");
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
