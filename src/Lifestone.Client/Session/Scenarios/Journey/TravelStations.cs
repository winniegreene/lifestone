using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Net.Session;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// The travel stations of a journey (M4 P5b lane J3b, owner decisions D36 and D38; research W0 sections 2 and 3, live 2026-10-24): the bind and the lifestone tie at Holtburg, the mage's way into the den (a Primary Portal Recall, or the first
/// ever journey's tie and portal walk) and the way home (Lifestone Recall). Every station is a <c>public sealed</c> <see cref="IStation"/> with a plain constructor; its run is a <see cref="ScriptLeg"/> built from the journey's context.
/// <para><b>No silent place</b> (lanes.md contract 5): every walk goes through <see cref="JourneyContext.Travel"/>, never <c>Tools.Place</c>; the one place a station may ask for is the untied den-travel's <see cref="TravelPurpose.Ground"/> request to
/// the portal's front, which the journey's travel counts against its teleport budget.</para>
/// <para><b>The facts</b> (contract 4): the server never says whether the character is tied. The bind sets <see cref="JourneyFacts.Bound"/> from ACE's text and the tie sets <see cref="JourneyFacts.LifestoneTied"/> from "You have successfully linked with
/// the life stone."; the portal tie sets <see cref="JourneyFacts.PortalTied"/> from "...with the portal." or a Primary Portal Recall's arrival, and a 0x04A3 clears it; a recall adds to <see cref="JourneyFacts.Recalls"/>.</para>
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

    /// <summary>Holtburg's lifestone, as the W0 probe stood in front of it (landblock 0xA9B4, cell 0x0019).</summary>
    protected static readonly (uint Cell, Vector3 At) HoltburgLifestone = (0xA9B40019, new Vector3(84f, 7.7f, 94f));

    private readonly List<(ChatKind Kind, uint Type, string Text)> _chat = [];
    private readonly List<SelfTravel> _travel = [];
    private IDisposable? _chatSubscription;
    private IDisposable? _travelSubscription;

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

    /// <summary>Casts <paramref name="spell"/> (untargeted when <paramref name="target"/> is 0), retrying a fizzle up to <see cref="MaxFizzles"/> times (logged, counted); <paramref name="result"/> holds the last handle and the chat mark before it.</summary>
    protected IEnumerable<ActionHandle?> Cast(uint spell, uint target, CastResult result)
    {
        for (var attempt = 0; attempt <= MaxFizzles; attempt++)
        {
            result.Mark = ChatMark;
            result.TravelMark = TravelMark;
            var handle = target == 0 ? S.Actions.Cast(spell, ActionSource.Host) : S.Actions.Cast(spell, target, ActionSource.Host);
            result.Handle = handle;
            foreach (var h in Until(() => handle.IsCompleted, CastSeconds))
            {
                yield return h;
            }

            if (handle.Outcome is { Kind: ActionOutcomeKind.ServerError, Code: GearCasts.FizzleCode } || RefusalSince(result.Mark) == GearCasts.FizzleCode)
            {
                Fizzles++;
                Log($"cast {spell}: fizzled ({Fizzles}); {(attempt < MaxFizzles ? "casting again" : "the cap was reached")}");
                continue;
            }

            yield break;
        }
    }

    /// <summary>Waits until the combat mode is <paramref name="wanted"/>.</summary>
    protected IEnumerable<ActionHandle?> Mode(CombatMode wanted, int modeValue)
    {
        if (S.State.Self.Qualities.CombatMode == modeValue)
        {
            yield break;
        }

        var mode = S.Actions.SetCombatMode(wanted, ActionSource.Host);
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
                foreach (var h in Go(new TravelRequest(Destination("holtburg-lifestone-point", HoltburgLifestone.Item1, HoltburgLifestone.Item2), TravelPurpose.Bind), "to the lifestone's known point"))
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
            var at = S.State.Objects.TryGet(lifestone, out var o) && o.Position is { } op ? (op.CellId, op.Origin) : HoltburgLifestone;
            Log($"found the lifestone {lifestone:X8}");

            Stage = "walk";
            foreach (var h in Go(new TravelRequest(Destination("holtburg-lifestone", at.Item1, at.Item2), TravelPurpose.Bind, lifestone, UseRangeMetres), "to the lifestone"))
            {
                yield return h;
            }

            Stage = "use";
            foreach (var h in PeaceNow())
            {
                yield return h;
            }

            var mark = ChatMark;
            var use = S.Actions.Use(lifestone, ActionSource.Host);
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
/// <c>den-travel</c> (the mage; M4 P5b J3b, D36, W0 section 3): unless <see cref="JourneyFacts.PortalTied"/> is false, casts Primary Portal Recall 48 and waits for the arrival in the den's landblock 0x0105 (present about 10 s after the cast; the recall lands INSIDE the den,
/// the portal's destination, <c>WorldObject_Magic.cs</c>:1105-1175 at 95ef426d, so there is no portal walk): <see cref="JourneyFacts.PortalTied"/> true, <see cref="JourneyFacts.Recalls"/> up by one. Refused 0x04A3 (YouMustLinkToPortalToRecall: not tied),
/// or when the facts already say it, the first-ever journey's path: <see cref="TravelPurpose.Ground"/> to the portal's front (cell 0x1B90002C, 122.40 84.43 30.01, the journey's one budgeted place), Primary Portal Tie 47 at the portal (weenie 7242,
/// "You have successfully linked with the portal."), then the portal on foot (an object goal, <see cref="TravelPurpose.Seek"/>, and <c>Use</c>) into the den. Every other refusal is named and fails the station. Fields:
/// <c>den=recall|portal portal_tied= recalls= den_s=</c>.
/// </summary>
public sealed class DenTravelStation : IStation
{
    /// <summary>Primary Portal Tie.</summary>
    public const uint PortalTieSpell = 47;

    /// <summary>Primary Portal Recall.</summary>
    public const uint PortalRecallSpell = 48;

    /// <summary>The weenie class of the Black Spawn Den's portal.</summary>
    public const uint DenPortalWeenie = 7242;

    /// <summary>The den's landblock (the portal's destination).</summary>
    public const uint DenLandblock = 0x0105;

    /// <summary>The cell in front of the portal (3 m south of it, facing it).</summary>
    public const uint PortalFrontCell = 0x1B90002C;

    /// <summary>The place in front of the portal (W0, live).</summary>
    public static Vector3 PortalFront { get; } = new(122.40f, 84.43f, 30.01f);

    /// <summary>The portal's origin, if the client has not seen it (W0).</summary>
    public static Vector3 PortalOrigin { get; } = new(122.404f, 87.43f, 29.937f);

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
        return new Run(context);
    }

    private sealed class Run(JourneyContext context) : TravelLeg(StationName, context)
    {
        private string _how = "none";
        private int _recalls;
        private double _seconds;

        public override string VerdictFields() =>
            string.Create(CultureInfo.InvariantCulture, $"den={_how} portal_tied={Context.Facts.PortalTied switch { true => "yes", false => "no", _ => "unknown" }} recalls={_recalls} den_s={_seconds:0.0}") + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

        protected override IEnumerable<ActionHandle?> Body()
        {
            var started = S.Clock.Now;
            var viaPortal = Context.Facts.PortalTied == false;
            if (!viaPortal)
            {
                Stage = "recall";
                foreach (var h in MagicNow())
                {
                    yield return h;
                }

                var cast = new CastResult();
                foreach (var h in Cast(PortalRecallSpell, 0, cast))
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
                    Context.Facts.PortalTied = false;
                    viaPortal = true;
                    Log($"Primary Portal Recall refused: {Named(refusal)}; tying to the portal");
                }
                else
                {
                    Require(refusal == 0, "den-travel: Primary Portal Recall was refused: " + Named(refusal));
                    foreach (var h in ArriveIn(DenLandblock, cast.TravelMark, "den-travel recall"))
                    {
                        yield return h;
                    }

                    Context.Facts.PortalTied = true;
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
            Stage = "portal-front";
            foreach (var h in Go(new TravelRequest(Destination("den-portal-front", PortalFrontCell, PortalFront), TravelPurpose.Ground), "to the den portal's front"))
            {
                yield return h;
            }

            var portal = 0u;
            foreach (var h in Until(() => (portal = FindPortal()) != 0, FindSeconds))
            {
                yield return h;
            }

            Require(portal != 0, $"den-travel: no object of weenie {DenPortalWeenie} within {FindSeconds:0} s of the arrival");

            Stage = "tie";
            foreach (var h in MagicNow())
            {
                yield return h;
            }

            var tie = new CastResult();
            foreach (var h in Cast(PortalTieSpell, portal, tie))
            {
                yield return h;
            }

            foreach (var h in Until(() => TextSince(tie.Mark, "linked with the portal") || Refusal(this, tie) != 0, TextSeconds))
            {
                yield return h;
            }

            var tied = TextSince(tie.Mark, "linked with the portal");
            Require(tied, $"den-travel: Primary Portal Tie did not link ({(Refusal(this, tie) is var r and not 0 ? Named(r) : Describe(tie.Handle!))})");
            Context.Facts.PortalTied = true;

            Stage = "portal";
            foreach (var h in PeaceNow())
            {
                yield return h;
            }

            var at = S.State.Objects.TryGet(portal, out var o) && o.Position is { } op ? (op.CellId, op.Origin) : (PortalFrontCell, PortalOrigin);
            foreach (var h in Go(new TravelRequest(Destination("den-portal", at.Item1, at.Item2), TravelPurpose.Seek, portal, PortalRangeMetres), "to the den portal"))
            {
                yield return h;
            }

            var travelMark = TravelMark;
            var use = S.Actions.Use(portal, ActionSource.Host);
            foreach (var h in ArriveIn(DenLandblock, travelMark, "den-travel portal"))
            {
                yield return h;
            }

            Check(use.Outcome is { Kind: ActionOutcomeKind.Confirmed } or null, $"den-travel: Use of the portal: {Describe(use)}");
            _how = "portal";
        }

        private uint FindPortal()
        {
            foreach (var o in S.State.Objects.All)
            {
                if (o.Weenie.WeenieClassId == DenPortalWeenie)
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
/// <para><b>Off the lifestone</b> (M4 4.2 FS, live gate round 1): the recall arrives at the tied lifestone's own position (ACE WorldObject_Magic.cs 1087-1095
/// recalls to <c>LinkedLifestone</c>, the lifestone's location), inside its cylinder. Retail's client would place itself out of it on arrival
/// (<c>SmartBox::TeleportPlayer</c>, a sliding <c>SetPositionSimple</c>, 11.6096 0x00516DB0) and report where it stands; Lifestone's player does not collide
/// with server objects (no host keeps a physics object for them: <c>TODO(d25-server-object-host)</c>, docs/physics.md "Other objects"), so it stays inside,
/// and a character that logs off there is created inside on its next login, where ACE's own placement then moves it 2.2 m without telling the client
/// (ACE Player_Networking.cs 224 before WorldObject.cs 209-220): the next login's where-am-I disagrees (the Soak tier's BSD Mage, 2.22 m). So when the arrival
/// stands within <see cref="OnLifestoneMetres"/> of a lifestone in Holtburg, the station walks to the lifestone's known point (as the bind's fallback does) before
/// it ends; anywhere else it fails, named, rather than leave the character inside. A lifestone in view but not that close is left alone.</para>
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
        private bool _steppedOff;

        public override string VerdictFields() =>
            (_recall?.Fields ?? "recalls=0 recall_s=0.0") + " stepped_off=" + (_steppedOff ? "yes" : "no") + (Failure is null ? string.Empty : " why=" + Failure.Replace(' ', '_'));

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
            var lifestone = FindLifestone(OnLifestoneMetres);
            if (lifestone == 0)
            {
                yield break;
            }

            var cell = S.State.Self.Position?.CellId ?? 0;
            Require(
                cell >> 16 == JourneyGrounds.HoltburgLandblock,
                $"recall-home: the recall arrived in lifestone {lifestone:X8} in cell 0x{cell:X8}, outside Holtburg, and only Holtburg's lifestone has a known point to step off to (TODO(d25-server-object-host))");
            Log($"the recall arrived in lifestone {lifestone:X8} (within {OnLifestoneMetres} m): stepping off it before the station ends (TODO(d25-server-object-host): the client does not collide with it)");
            foreach (var h in Go(new TravelRequest(Destination("holtburg-lifestone-point", HoltburgLifestone.Item1, HoltburgLifestone.Item2), TravelPurpose.Bind), "off the lifestone"))
            {
                yield return h;
            }

            _steppedOff = true;
        }
    }
}
