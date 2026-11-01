using System.Globalization;
using Lifestone.Client.Avatars;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Dat;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// What a journey host offers when it can count the server's sounds (F750) that sounded: the Godot host (the play's hook-sound router); the headless host has none,
/// so the buff station sets <c>sounds_server</c> aside there as a named host-only judge (M4 P5b fix FB, P5b-R4; D27 judges at least 1).
/// </summary>
public interface IJourneyServerSounds
{
    /// <summary>The server sounds that sounded so far this run, or null when the counter does not exist.</summary>
    int? ServerSounds { get; }
}

/// <summary>
/// The physics fields of a station's span (M5 item 5 c1, report-only; the den-fight station's line): the jump watch's counts as the difference of <see cref="AvatarStats"/>' totals from the span's first sample to its last, and the maxima of the
/// per-frame figures sampled over the span (the avatars' transition time, the sliding placements' time, the transitions of one advance). Read from <see cref="IJourneyWalks.PhysicsCost"/>; a host with none (the headless one) gives
/// <c>n/a</c> for all five. Game thread only.
/// </summary>
public sealed class PhysicsSpan
{
    private AvatarPhysicsCost? _first;
    private AvatarPhysicsCost _last;
    private double _transitMsMax;
    private double _placeMsMax;
    private int _transitsMax;

    /// <summary>The fields printed for a span that never saw a cost.</summary>
    public const string NotAvailable = "creature_jumps=n/a creature_flips=n/a transit_ms_max=n/a place_ms_max=n/a transits_max=n/a";

    /// <summary>Takes one sample: the first non-null one starts the span, each one extends it; a null (no avatars yet, or a host without any) changes nothing.</summary>
    /// <param name="cost">The host's physics cost now.</param>
    public void Sample(AvatarPhysicsCost? cost)
    {
        if (cost is not { } now)
        {
            return;
        }

        _first ??= now;
        _last = now;
        _transitMsMax = Math.Max(_transitMsMax, now.TransitionMs);
        _placeMsMax = Math.Max(_placeMsMax, now.PlacementMs);
        _transitsMax = Math.Max(_transitsMax, now.Transitions);
    }

    /// <summary>The five fields: <c>creature_jumps= creature_flips= transit_ms_max= place_ms_max= transits_max=</c> (counts over the span, milliseconds with two decimals), or all <c>n/a</c> when nothing was sampled.</summary>
    public string Fields() => _first is not { } first
        ? NotAvailable
        : string.Create(
            CultureInfo.InvariantCulture,
            $"creature_jumps={Math.Max(0, _last.CreatureJumps - first.CreatureJumps)} creature_flips={Math.Max(0, _last.CreatureFlips - first.CreatureFlips)} transit_ms_max={_transitMsMax:0.00} place_ms_max={_placeMsMax:0.00} transits_max={_transitsMax}");
}

/// <summary>
/// The station catalog and the journey's keeper, shared by the two hosts (M4 P5b; hook H1: the signatures are fixed here so the
/// headless host (J4) and the Godot host (J5) can be built side by side; J4 fills the bodies). The catalog maps each of a plan's
/// station kinds (<see cref="StationKinds"/>) to its station: <see cref="ScenarioStation"/> over the embedded gear, buff, hunt,
/// vendor, den-fight and death scenarios, and the script stations of <c>HealStations</c>, <c>HoltburgStations</c>,
/// <c>ShopStations</c>, <c>TravelStations</c> and <c>BuildStations</c>.
/// </summary>
public static class JourneyStations
{
    /// <summary>The catalog for <paramref name="plan"/>; the scenario options (<c>--hunt-*</c>, <c>--fight-*</c>, <c>--death-*</c>, <c>--buff-*</c>, <c>--vitals-*</c>) come from <paramref name="arguments"/>, the host's command line.</summary>
    /// <param name="plan">The journey's plan.</param>
    /// <param name="arguments">The host's command-line arguments.</param>
    /// <param name="dats">The dats (the hunt's follower, the walker, the build's tables), or null in a test without them.</param>
    public static IStationFactory Create(JourneyPlan plan, IReadOnlyList<string> arguments, DatSet? dats) => Create(plan, arguments, dats, null);

    /// <summary>The journey's vitals keeper for <paramref name="plan"/>'s character (a fighter's casts through its CasterSwap; the in-fight potion on in a journey), from the <c>--vitals-*</c> and <c>--heal-*</c> options.</summary>
    /// <param name="plan">The journey's plan.</param>
    /// <param name="session">The session.</param>
    /// <param name="spells">The spell table.</param>
    /// <param name="arguments">The host's command-line arguments.</param>
    /// <param name="log">The log.</param>
    public static VitalsKeeper? CreateKeeper(JourneyPlan plan, IGameSessionView session, IReadOnlyDictionary<uint, SpellInfo> spells, IReadOnlyList<string> arguments, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(log);
        var parsed = CommandLineArgument.ParseAll(arguments);
        var options = VitalsOptions.FromArguments(parsed);
        Refuse(options.Errors, "--vitals-* / --heal-*");
        if (!parsed.Any(a => a.Name == InFightPotionOption))
        {
            options = options with { InFightPotion = true }; // the owner's call (m4-hunt-infight-heal): on in a journey unless the command line says otherwise
        }

        // The fighters cast their heals and transfers through their caster swap (P5a C1b); the mage (no fighter template) casts with its own wand: null.
        var swap = CasterSwap.ForCharacter(plan.Character, session.Actions, session.State, log);
        log.Info(JourneyVerdict.LogCategory, $"journey keeper: {plan.Character}, infight_potion={(options.InFightPotion ? "on" : "off")}, caster_swap={(swap is null ? "none" : "yes")}");
        return new VitalsKeeper(session, spells, options, log, swap);
    }

    /// <summary>The melee journey's hunt kills when <c>--hunt-kills</c> is not given (design 3.4: "hunt (3 kills, ...)").</summary>
    public const int MeleeHuntKills = 3;

    /// <summary>The archer journey's hunt kills when <c>--hunt-kills</c> is not given (design 3.4: "missile hunt (2 kills, ...)").</summary>
    public const int ArcherHuntKills = 2;

    /// <summary>
    /// The archer journey's hunt seconds when <c>--hunt-seconds</c> is not given: the forest's 20-30 band is sparse with real spawns (D23), and a gate an hour after
    /// another found none within 35 m of five cells in the default 300 s (M4 4.2 live round 2, <c>m4-hunt-band-empty</c>), so the archer searches longer; M5 item 2 k2 sets 1,200 s for the forest's 33 spots (D42; g3's estimate: the chance of two usable spots is 0.98 within all 33, 0.87 within 20, and one loop of the 33 is about 3,100 m).
    /// The station's limit (<see cref="JourneyScenario.StationLimitFactor"/> times its <see cref="JourneyPlans.ArcherHuntExpectedSeconds"/> 400 s plus the margin, 1,500 s) holds it, and both hosts' whole-run limits add the difference (<see cref="JourneyLimits.WholeExtraSeconds"/>), so a failure ends by the hunt's own named reason.
    /// </summary>
    public const double ArcherHuntSeconds = 1200;

    /// <summary>
    /// M5 4 a1 (D57): the den-fight spec argument that makes the fight open-ended for the autopilot: no kill cap (<see cref="FightOptions.UnlimitedKills"/>, whatever the plan or <c>--fight-kills</c> say), no clean minutes stop and no failure
    /// limit (<see cref="OpenEndedFailSeconds"/>), so only the fight's external stop (<see cref="IStoppableFight.RequestStop"/>), its death, its kill switch or its walk ends it. The autopilot's watchdogs (slice a2) bound it instead.
    /// </summary>
    public const string OpenEndedArgument = "open-ended";

    /// <summary>The open-ended fight's failure limit: none (the autopilot's stop rules and watchdogs end it, never a fight timer).</summary>
    public const double OpenEndedFailSeconds = double.PositiveInfinity;

    /// <summary>M5 4 a1 (D57): the logistics spec's <see cref="JourneyPlans.TripArgument"/> value for the unforced trip: the station trips only when its counters say so (the other value is <see cref="JourneyPlans.TripForced"/>; no argument is the same as this one).</summary>
    public const string TripByCounters = "counters";

    /// <summary>
    /// M5 4 a1 (D57): the death spec argument that leaves out the death handler's own return to the fight (<see cref="DeathOptions.Fight"/> off for this station): the autopilot goes back to its own open-ended fight after the corpse trip and the
    /// item lines, so the handler's composed fight of <c>--death-fight-kills</c> kills would be a second fight.
    /// </summary>
    public const string DeathNoFightArgument = "no-fight";

    /// <summary>M5 7 F30: the den-fight spec argument of a fight that starts with the mage already on the ground: it fights from where it stands (<see cref="FightOptions.ResumeOnGround"/>), with no place stage and no walk to the arrival.</summary>
    public const string ResumeOnGroundArgument = "resume-on-ground";

    /// <summary>M5 7 F23: the buff spec's argument of the autopilot's own buff and upkeep stations: the re-buff driver of the station is never held by its <c>in a fight</c> guard (<see cref="Mage.BuffOptions.DriverUnattended"/>).</summary>
    public const string UnattendedArgument = "unattended";

    /// <summary>D126 (a): the buff spec's argument of the autopilot's start (and restart) buff station: its pass recasts every line due within one buff cycle (<see cref="Mage.BuffOptions.OneBuffCycleSeconds"/>) unless the command line names <c>--buff-start-margin-s</c>.</summary>
    public const string StartPassArgument = "start-pass";

    /// <summary>
    /// The options of the den-fight station <paramref name="spec"/>: the command line's fight options <paramref name="baseline"/> without a leave on failure or at the end (the death follows in the den, lane J2b), then the spec's own arguments
    /// (<see cref="JourneyPlans.SpellArgument"/>, <see cref="JourneyPlans.KillsArgument"/>, <see cref="JourneyPlans.StoreysMinArgument"/>) unless the command line gave the option itself (<c>--fight-kills</c>, <c>--fight-storeys-min</c>).
    /// The mage's den fight asks for 3 kills and a tour of 2 storeys (M5 item 2 k2).
    /// </summary>
    /// <param name="spec">The station's spec.</param>
    /// <param name="baseline">The fight options of the command line.</param>
    /// <param name="arguments">The command line, to tell which options it gave.</param>
    public static FightOptions DenFightOptions(StationSpec spec, FightOptions baseline, IReadOnlyList<CommandLineArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(arguments);
        var fight = baseline with { LeaveOnFailure = false, LeaveAtEnd = false }; // the death follows in the den (lane J2b)
        if (spec.Arguments.TryGetValue(JourneyPlans.SpellArgument, out var spellText) && uint.TryParse(spellText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var spellId))
        {
            fight = fight with { SpellId = spellId }; // M4 P5b J7: the hero plan casts Cassius' Ring of Fire instead of the Flame Bolt line
        }

        if (spec.Arguments.TryGetValue(JourneyPlans.KillsArgument, out var killsText) && int.TryParse(killsText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var kills) && kills is >= 1 and <= FightOptions.MaxKills
            && !arguments.Any(a => a.Name == "--fight-kills"))
        {
            fight = fight with { Kills = kills }; // the plan's own kills unless the command line asked for others
        }

        if (spec.Arguments.TryGetValue(JourneyPlans.StoreysMinArgument, out var storeysText) && int.TryParse(storeysText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var storeys) && storeys is >= 1 and <= FightOptions.MaxStoreysMin
            && !arguments.Any(a => a.Name == "--fight-storeys-min"))
        {
            fight = fight with { StoreysMin = storeys }; // the plan's own tour depth unless the command line asked for another (the mage's den fight tours two storeys)
        }

        if (spec.Arguments.ContainsKey(OpenEndedArgument))
        {
            fight = fight with { Kills = FightOptions.UnlimitedKills, Minutes = 0, Seconds = OpenEndedFailSeconds }; // M5 4 a1: the autopilot's fight, ended only by its stop
        }

        if (spec.Arguments.ContainsKey(ResumeOnGroundArgument))
        {
            fight = fight with { ResumeOnGround = true }; // M5 7 F30: no teleport to the arrival and no walk to it
        }

        if (spec.Arguments.ContainsKey(UnattendedArgument))
        {
            fight = fight with { HoldStance = true }; // M5 7 F25 (D93): the autopilot's own fight keeps the magic mode at its end
        }

        return fight;
    }

    /// <summary>The keeper's in-fight potion option; absent from the command line, a journey turns the potion on.</summary>
    public const string InFightPotionOption = "--vitals-infight-potion";

    /// <summary>
    /// What the headless host cannot measure and the buff leg's verdict judges (M4 P5b J4): the server-sound counter of D27 (a Godot measure). Each problem
    /// left out of a station's ok is logged as not judged.
    /// </summary>
    public static IReadOnlyList<string> HostOnlyProblems { get; } = ["sounds_server=n/a"];

    /// <summary>The least mana share the mage's buff starts with (its two passes cast 24 lines).</summary>
    public const double MageBuffManaPercent = 60;

    /// <summary>The least mana share a fighter's buff starts with (12 lines).</summary>
    public const double FighterBuffManaPercent = 50;

    /// <summary>The gear fields of a fighter's buff leg: it has no gear step (M4 P5a C4), so the verdict line prints these.</summary>
    public const string FighterGearFields = "gear=n/a worn=n/a";

    /// <summary>The buff station's ok: the leg verdict (<see cref="BuffLegVerdict.Compose"/> over the scenario's result and no game measures) minus the named host-only judges (each logged as not judged).</summary>
    /// <param name="result">What the buff scenario left.</param>
    /// <param name="unjudged">The problem prefixes this host cannot judge.</param>
    /// <param name="station">The station (for the log).</param>
    /// <param name="log">The log.</param>
    /// <param name="serverSounds">The server sounds (F750) the host counted in the leg (D27 judges at least 1), or null when the host has no counter (then <c>sounds_server=n/a</c> is a named host-only judge in <paramref name="unjudged"/>).</param>
    public static bool BuffOk(BuffLegScenarioResult result, IReadOnlyList<string> unjudged, string station, ILogSink log, int? serverSounds = null) =>
        BuffJudge(result, unjudged, station, log, serverSounds, soundsReportOnly: false).Ok;

    /// <summary>The buff station's judges named (M4 P5b fix FC-b1): the verdict, the failing judges and the report-only ones.</summary>
    /// <param name="Ok">True when no judge failed.</param>
    /// <param name="Failed">The failing judges' problems (each a leg-verdict sentence), empty when ok.</param>
    /// <param name="Reported">The judges set aside as report-only in their first gate (<c>sounds_server=0</c>), failing or not.</param>
    public sealed record BuffJudgement(bool Ok, IReadOnlyList<string> Failed, IReadOnlyList<string> Reported);

    /// <summary>
    /// <see cref="BuffOk"/> with the judges named. D27's <c>sounds_server&gt;=1</c> is new in the journey (the sound comes from the wand's re-seat, which the
    /// retired login-buff leg did inside the leg and the journey does in the gear station, before this window): with <paramref name="soundsReportOnly"/> it is
    /// report-only in its first gate (the rule), its count printed in <c>judges_reported=</c> and never failing the station (FC-b1).
    /// </summary>
    public static BuffJudgement BuffJudge(BuffLegScenarioResult result, IReadOnlyList<string> unjudged, string station, ILogSink log, int? serverSounds, bool soundsReportOnly)
    {
        ArgumentNullException.ThrowIfNull(unjudged);
        var problems = BuffLegVerdict.Compose(result, new BuffLegMeasures(0, 0, 0, null, serverSounds)).Problems;
        var host = serverSounds is null ? unjudged : [.. unjudged.Where(u => u != HostOnlyProblems[0])];
        var reported = new List<string>();
        var failed = new List<string>();
        foreach (var problem in problems)
        {
            if (host.Any(u => problem.StartsWith(u, StringComparison.Ordinal)))
            {
                log.Info(JourneyVerdict.LogCategory, $"station {station}: not judged in this host: {problem}");
            }
            else if (soundsReportOnly && problem.StartsWith("sounds_server=", StringComparison.Ordinal))
            {
                reported.Add(problem);
                log.Info(JourneyVerdict.LogCategory, $"station {station}: report-only in its first journey gate: {problem}");
            }
            else
            {
                failed.Add(problem);
                log.Warn(JourneyVerdict.LogCategory, $"station {station}: judge failed: {problem}");
            }
        }

        return new BuffJudgement(failed.Count == 0, failed, reported);
    }

    /// <summary>The token a station line carries for failing judges (<c>why=judge:...</c>): each problem's first word up to its first space, joined with <c>+</c>.</summary>
    public static string JudgeWhy(IReadOnlyList<string> failed) => "judge:" + string.Join('+', failed.Select(JudgeName));

    private static string JudgeName(string problem) => problem.StartsWith("the scenario", StringComparison.Ordinal) ? "scenario" : JourneyVerdict.Token(problem.Split(['=', ' ', ':'], 2)[0]);

    /// <summary>The sale the vendor station requires (M4 P5b F2): at least one item sold, or the named <c>nothing-to-sell</c> (the pack held nothing the shop takes: <c>sell=none</c>); anything else (the walk failed, the sale refused or never tried) fails.</summary>
    /// <param name="sold">The items sold.</param>
    /// <param name="sellOutcome">The hunt's sale outcome (<c>HuntScenario.SellOutcome</c>).</param>
    /// <param name="station">The station (for the log).</param>
    /// <param name="log">The log.</param>
    public static bool SaleHappened(int sold, string sellOutcome, string station, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(sellOutcome);
        ArgumentNullException.ThrowIfNull(log);
        if (sold >= 1)
        {
            return true;
        }

        if (sellOutcome == "none")
        {
            log.Info(JourneyVerdict.LogCategory, $"station {station}: nothing-to-sell: the pack holds no loot the shop takes (sell=none); the sale is not required");
            return true;
        }

        log.Warn(JourneyVerdict.LogCategory, $"station {station}: the sale did not happen (sold={sold} sell={sellOutcome}): the station fails");
        return false;
    }

    /// <summary>
    /// The hunt station's fields (M4 4.2 J1): the hunt's verdict fields, then what its verdict judges that those do not print (<see cref="HuntScenario.JudgedFields"/>), <c>mode_mismatch=&lt;n&gt;</c> (the station's watch,
    /// judged <c>= 0</c>), and the attack and loot windows' slowest frames from the host (<c>attack_frame_max_ms=&lt;x.x|n/a&gt; loot_frame_max_ms=&lt;x.x|n/a&gt;</c>; <c>n/a</c> on a host without a frame clock, report-only for the
    /// station: smoke judges them).
    /// </summary>
    /// <param name="hunt">The hunt that ran.</param>
    /// <param name="frames">The host's window frame times, or null.</param>
    public static string HuntFields(HuntScenario hunt, IJourneyWindowFrames? frames)
    {
        ArgumentNullException.ThrowIfNull(hunt);
        return hunt.VerdictFields() + " " + hunt.JudgedFields() + " " + CombatModeWatch.FieldName + "=" + (hunt.ModeWatch?.Violations ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " attack_frame_max_ms=" + HuntLegVerdict.FrameMs(frames?.AttackFrameMaxMs) + " loot_frame_max_ms=" + HuntLegVerdict.FrameMs(frames?.LootFrameMaxMs);
    }

    /// <summary>
    /// The vendor station's fields (M4 4.2 J1): the hunt's verdict and vendor fields, then the vendor window's slowest frame from the host (<c>vendor_frame_max_ms=&lt;x.x|n/a&gt;</c>, report-only for the station), then
    /// <c>door_opened=yes|no</c> (M5 item 5, d2 and F2: a door the walk to the shop itself Used turned Ethereal, <see cref="HuntScenario.DoorOpenedField"/>; the vendor's own Use and other doors never count; report-only, printed before any <c>why=</c>).
    /// </summary>
    /// <param name="hunt">The hunt's vendor stage that ran.</param>
    /// <param name="frames">The host's window frame times, or null.</param>
    public static string VendorFields(HuntScenario hunt, IJourneyWindowFrames? frames)
    {
        ArgumentNullException.ThrowIfNull(hunt);
        return hunt.VerdictFields() + " " + hunt.VendorFields() + " vendor_frame_max_ms=" + HuntLegVerdict.FrameMs(frames?.VendorFrameMaxMs) + " door_opened=" + hunt.DoorOpenedField;
    }

    /// <summary>The hunt's judges a vendor visit cannot meet (it attacks nothing): left out of the vendor station's ok, logged.</summary>
    public static IReadOnlyList<string> VendorUnjudged { get; } = ["kills=", "damage_dealt=", "swings=", "shots=", "hits=", "looted="];

    /// <summary>
    /// The catalog for <paramref name="plan"/> with the account's login name the mage's gear kit is built for (ACE customises the taper slots from it; the
    /// host reads it at run time, the kit hashes it, nothing stores or prints it). Null when the host has none: the kit is then built for the account KEY,
    /// logged loudly as <c>TODO(j4-kit-account)</c> (the taper wcids may not match the account's).
    /// </summary>
    /// <param name="plan">The journey's plan.</param>
    /// <param name="arguments">The host's command-line arguments.</param>
    /// <param name="dats">The dats, or null in a test without them.</param>
    /// <param name="kitAccount">The login name, or null.</param>
    public static IStationFactory Create(JourneyPlan plan, IReadOnlyList<string> arguments, DatSet? dats, string? kitAccount)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(arguments);
        return new Factory(plan, CommandLineArgument.ParseAll(arguments), dats, kitAccount);
    }

    /// <summary>True when no problem of a leg verdict is left once those starting with one of <paramref name="unjudged"/> are set aside (each one logged).</summary>
    /// <param name="problems">The verdict's problems.</param>
    /// <param name="unjudged">The prefixes left out.</param>
    /// <param name="station">The station (for the log).</param>
    /// <param name="log">The log.</param>
    public static bool OkWithout(IReadOnlyList<string> problems, IReadOnlyList<string> unjudged, string station, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(problems);
        ArgumentNullException.ThrowIfNull(unjudged);
        ArgumentNullException.ThrowIfNull(log);
        var ok = true;
        foreach (var problem in problems)
        {
            if (unjudged.Any(u => problem.StartsWith(u, StringComparison.Ordinal)))
            {
                log.Info(JourneyVerdict.LogCategory, $"station {station}: not judged in this host: {problem}");
            }
            else
            {
                ok = false;
            }
        }

        return ok;
    }

    private static void Refuse(IReadOnlyList<string> errors, string what)
    {
        if (errors.Count > 0)
        {
            throw new ArgumentException($"journey: the {what} options do not parse: {string.Join("; ", errors)}");
        }
    }

    private static string Token(string fields, string name)
    {
        foreach (var part in fields.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith(name + "=", StringComparison.Ordinal))
            {
                return part[(name.Length + 1)..];
            }
        }

        return "?";
    }

    /// <summary>
    /// M5 7 F14 (closes <c>TODO(m5-7-journey-mage-level)</c>): the level of the mage's build the journey's build-verify station judges: <c>--mage-level</c> when given, else <see cref="MageTemplate.TargetLevel"/> (the gate's 126, unchanged).
    /// A value that is no integer or lies outside <see cref="MageTemplate.MinLevel"/> to <see cref="MageTemplate.TargetLevel"/> is refused by name (the headless host refuses it before the run).
    /// </summary>
    /// <param name="arguments">The host's parsed command-line arguments.</param>
    public static int MageLevelOf(IReadOnlyList<CommandLineArgument> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var level = MageTemplate.TargetLevel;
        var errors = new List<string>();
        foreach (var argument in arguments)
        {
            if (argument.Name != "--mage-level")
            {
                continue;
            }

            var asked = OptionValues.Int(argument, errors, MageTemplate.TargetLevel);
            if (errors.Count == 0 && (asked < MageTemplate.MinLevel || asked > MageTemplate.TargetLevel))
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"--mage-level: '{argument.Value}' is not a level {MageTemplate.MinLevel} to {MageTemplate.TargetLevel}"));
            }

            if (errors.Count == 0)
            {
                level = asked;
            }
        }

        Refuse(errors, "--mage-level");
        return level;
    }

    /// <summary>The catalog: one station per kind, built fresh for its spec; the scenarios run embedded (no login, no place of their own, no leave).</summary>
    private sealed class Factory : IStationFactory
    {
        private readonly JourneyPlan _plan;
        private readonly IReadOnlyList<CommandLineArgument> _arguments;
        private readonly DatSet? _dats;
        private readonly string? _kitAccount;
        private readonly BuffOptions _buff;
        private readonly FightOptions _fight;
        private readonly DeathOptions _death;
        private readonly HuntOptions _hunt;
        private readonly FighterTemplate? _fighter;
        private readonly int _mageLevel;

        private bool _denWalkLogged;
        private FightScenario? _denFight;

        /// <summary>The guids the den fight's loot step kept (M5 3 c2a, closes <c>TODO(m5-resupply-kept-list)</c>): the list the factory's logistics and resupply stations hand the resupply. The den-fight station of this journey sets it; null when no fight ran.</summary>
        private IReadOnlyCollection<uint> KeptLoot() => _denFight?.LootKeptGuids ?? [];

        /// <summary>The death station's den walk: the host's (<see cref="IJourneyWalks.FightWalk"/>: the headless body's or, since M4 P5b J5b, the Godot player's), else none (a scripted host), logged once.</summary>
        private IFightWalk? DeathGroundWalk(JourneyContext c)
        {
            if ((c.Runners as IJourneyWalks)?.FightWalk(c, _fight.GroundProfile) is { } walk)
            {
                return walk;
            }

            if (!_denWalkLogged)
            {
                _denWalkLogged = true;
                c.Log.Warn(JourneyVerdict.LogCategory, "this host offers no den walk (a scripted host: both journey hosts offer one since M4 P5b J5b); the death station's composed fight after the respawn has no walker (TODO(m4-death-walkless-wait))");
            }

            return null;
        }

        public Factory(JourneyPlan plan, IReadOnlyList<CommandLineArgument> arguments, DatSet? dats, string? kitAccount)
        {
            (_plan, _arguments, _dats, _kitAccount) = (plan, arguments, dats, kitAccount);
            _buff = BuffOptions.FromArguments(arguments);
            Refuse(_buff.Errors, "--buff-*");
            _fight = FightOptions.FromArguments(arguments);
            Refuse(_fight.Errors, "--fight-*");
            _death = DeathOptions.FromArguments(arguments);
            Refuse(_death.Errors, "--death-*");
            _mageLevel = MageLevelOf(arguments);
            _fighter = FighterTemplate.ForAccount(plan.Account) is { } t && t.Name == plan.Character ? t : null;
            var hunt = HuntOptions.FromArguments(arguments);
            Refuse(hunt.Errors, "--hunt-*");
            if (_fighter is not null)
            {
                var resolved = hunt.ForTemplate(_fighter);
                Refuse([.. resolved.Errors.Skip(hunt.Errors.Count)], "--hunt-* (for the template)");
                hunt = resolved;
            }

            _hunt = hunt;
        }

        /// <inheritdoc />
        public double StationLimitSeconds(StationSpec spec) => JourneyLimits.StationSeconds(spec, _fight);

        public IStation Create(StationSpec spec)
        {
            ArgumentNullException.ThrowIfNull(spec);
            return spec.Kind switch
            {
                StationKinds.BuildVerify => new BuildVerifyStation(mageLevel: _mageLevel),
                StationKinds.Gear => Gear(spec),
                StationKinds.Buff => Buff(spec),
                StationKinds.CastFacts => new CastFactsStation(KitAccount(null)),
                StationKinds.KitRefused => new KitRefusedStation(),
                StationKinds.HarmHeal => new HarmHealStation(),
                StationKinds.Bind => new BindStation(),
                StationKinds.DenTravel => new DenTravelStation(spec.Arguments.ContainsKey(JourneyPlans.StepOffArgument)),
                StationKinds.DenFight => DenFight(spec),
                StationKinds.Death => Death(spec),
                StationKinds.Hunt => Hunt(spec, vendor: false),
                StationKinds.Vendor => Hunt(spec, vendor: true),
                StationKinds.RecallHome => new RecallHomeStation(),
                StationKinds.Chests => new ChestsStation(),
                StationKinds.AppraiseItems => new AppraiseItemsStation(),
                StationKinds.AppraiseVendor => new AppraiseVendorStation(),
                StationKinds.VendorFacts => new VendorFactsStation(),
                StationKinds.Shortcuts => new ShortcutsStation(),
                StationKinds.Hotbar => new HotbarStation(),
                StationKinds.Corpses => new CorpsesStation(spec.Arguments.ContainsKey(JourneyPlans.NoItemWaitArgument)),
                StationKinds.Resupply => new ResupplyStation(new ResupplyOptions(Loot: _fight.Loot, Kept: KeptLoot, Memory: SaleMemory.Shared, Gear: _fight.Loot)),
                StationKinds.Logistics => new LogisticsStation(forced: TripForced(spec), new ResupplyOptions(Loot: _fight.Loot, Kept: KeptLoot, Memory: SaleMemory.Shared, Gear: _fight.Loot)),
                _ => throw new NotSupportedException($"TODO(j4-station-kind): the journey catalog has no station of kind '{spec.Kind}' (station '{spec.Name}')"),
            };
        }

        /// <summary>M5 4 a1: the logistics spec's trip: forced by <see cref="JourneyPlans.TripForced"/>, by the counters with no argument or <see cref="TripByCounters"/>; any other value is refused by name.</summary>
        private static bool TripForced(StationSpec spec)
        {
            if (!spec.Arguments.TryGetValue(JourneyPlans.TripArgument, out var trip) || trip == TripByCounters)
            {
                return false;
            }

            return trip == JourneyPlans.TripForced
                ? true
                : throw new ArgumentException($"journey: the logistics station '{spec.Name}' has trip={trip}: the trip is {JourneyPlans.TripForced} or {TripByCounters}");
        }

        private string KitAccount(ILogSink? log)
        {
            if (_kitAccount is { } name)
            {
                return name;
            }

            log?.Warn(JourneyVerdict.LogCategory, "TODO(j4-kit-account): the host gave no login name; the gear kit is built for the account key (its taper slots may not match the account's)");
            return _plan.Account;
        }

        private ScenarioStation Gear(StationSpec spec) => new(
            spec.Name,
            spec.NeedsOverride ?? new StationNeeds(StationWhere.Holtburg),
            c => new GearScenario(GearKit.Build(c.Spells, KitAccount(c.Log), spec.Arguments.ContainsKey(JourneyPlans.RingArgument), _fight.GroundProfile), c.Log, endSession: false, spells: c.Spells) { NoGrant = !spec.Arguments.ContainsKey(JourneyPlans.RingArgument) }, // M5 3 c2a (DoD 6): play grants nothing; the hero ring's plan keeps its grant (TODO(m5-hero-ring-diamond)); M5 3 F2: the kit of the journey's --fight-ground, as the buff and the den fight have it
            s => ((GearScenario)s).VerdictFields(),
            (s, status) => status == ScenarioStatus.Succeeded && Token(((GearScenario)s).VerdictFields(), "gear") == "wielded");

        private ScenarioStation Buff(StationSpec spec)
        {
            var fighter = _fighter;
            var options = _buff;
            if (fighter is null && !_arguments.Any(a => a.Name == "--buff-auto"))
            {
                options = options with { Auto = true }; // the mage's journey: the buff and the driver's pass (design 3.4: rebuffed=yes buffs=24/24)
            }

            if (spec.Arguments.ContainsKey(UnattendedArgument))
            {
                options = options with { DriverUnattended = true }; // M5 7 F23: the autopilot's own station; the driver's fight guard holds only a person's driver
            }

            if (spec.Arguments.ContainsKey(StartPassArgument) && options.StartMarginSeconds is null)
            {
                options = options with { StartMarginSeconds = Mage.BuffOptions.OneBuffCycleSeconds }; // D126 (a): the start's pass refreshes every line due within one buff cycle
            }

            // The fighters' buff has no gear step (gear=n/a, P5a C4): the mage's gear token is not judged for them.
            IReadOnlyList<string> unjudged = fighter is null ? HostOnlyProblems : [.. HostOnlyProblems, "gear=n/a"];
            ILogSink log = NullLogSink.Instance;
            IJourneyServerSounds? sounds = null;
            var soundsBefore = 0;
            string? judgeWhy = null;
            var judgesReported = string.Empty;
            return new ScenarioStation(
                spec.Name,
                spec.NeedsOverride ?? new StationNeeds(StationWhere.Holtburg, MinManaPercent: fighter is null ? MageBuffManaPercent : FighterBuffManaPercent),
                c =>
                {
                    log = c.Log;
                    sounds = c.Runners as IJourneyServerSounds; // D27's judge: the Godot host counts the F750 sounds as the retired login-buff leg did (P5b-R4)
                    soundsBefore = sounds?.ServerSounds ?? 0;
                    return fighter is not null
                        ? new BuffScenario(fighter, c.Spells, options, c.Log, endSession: false)
                        : new BuffScenario(c.Spells, KitAccount(c.Log), options, c.Log, endSession: false, bars: new MageSpellBars(c.Log), noGrant: true, ground: _fight.GroundProfile); // M5 3 F2 (closes 3-R3, DoD 6): the buff's gear stage verifies the journey ground's kit and sends no grant
                },
                s => ((BuffScenario)s).VerdictFields() + judgesReported,
                (s, status) =>
                {
                    var b = (BuffScenario)s;
                    // The fighters' leg has no gear step: its gear fields are the n/a ones the verdict line prints, not the unused gear scenario's "gear=failed".
                    var result = new BuffLegScenarioResult(status == ScenarioStatus.Succeeded, b.Failure, fighter is null ? b.Gear.VerdictFields() : FighterGearFields, b.CycleResult, b.ForcedResult, b.DriverOn, b.DriverResult, b.DriverRebuffSeconds, b.UnattendedEnd);
                    var counted = sounds?.ServerSounds is { } now ? Math.Max(0, now - soundsBefore) : (int?)null;
                    var judged = BuffJudge(result, unjudged, spec.Name, log, counted, soundsReportOnly: true);
                    judgesReported = judged.Reported.Count == 0 ? string.Empty : " judges_reported=" + string.Join('+', judged.Reported.Select(JudgeName)) + (counted is { } n ? $" sounds_server_counted={n}" : string.Empty);
                    judgeWhy = judged.Ok ? null : JudgeWhy(judged.Failed);
                    return judged.Ok;
                },
                () => judgeWhy);
        }

        private ScenarioStation DenFight(StationSpec spec)
        {
            var fight = DenFightOptions(spec, _fight, _arguments);

            var buff = _buff;
            var dats = _dats;
            var physics = new PhysicsSpan(); // M5 5 c1: the span of this station's fight, read from the host's avatars (report-only)
            return new ScenarioStation(
                spec.Name,
                spec.NeedsOverride ?? new StationNeeds(StationWhere.Den, MinHealthPercent: JourneyPlans.FightHealthPercent, MinManaPercent: 50, Buffed: true),
                c => _denFight = new FightScenario(
                    c.Spells,
                    KitAccount(c.Log),
                    buff,
                    fight with { Travel = c.Travel },
                    (c.Runners as IJourneyWalks)?.FightWalk(c, fight.GroundProfile) ?? (dats is null ? null : GroundWalk.Create(dats, fight.GroundProfile, c.Log)),
                    c.Log,
                    endSession: false,
                    stages: FightStages.Place | FightStages.Mode),
                s => ((FightScenario)s).VerdictFields() + " " + physics.Fields(),
                (s, status) =>
                {
                    var f = (FightScenario)s;
                    var t = f.Tally;
                    var measures = new FightLegMeasures(t.Kills, t.DamageDealt, t.Casts, t.Misses, t.XpGained, t.DamageTaken, t.HitsTaken, t.Evades, null, null, null, null, double.IsNaN(f.FightSeconds) ? null : f.FightSeconds, BlockedPicks: t.BlockedPicks, WallHits: t.WallHits);
                    return status == ScenarioStatus.Succeeded && FightLegVerdict.Compose(f.Failure, measures).Ok;
                },
                sample: c => physics.Sample((c.Runners as IJourneyWalks)?.PhysicsCost));
        }

        private ScenarioStation Death(StationSpec spec)
        {
            var death = _death with { StartAt = DeathStages.Death, LeaveOnFailure = false }; // D37: one death, no suicide first (lane J2b)
            if (spec.Arguments.ContainsKey(DeathNoFightArgument))
            {
                death = death with { Fight = false }; // M5 4 a1: the autopilot returns to its own fight
            }

            var buff = _buff;
            var dats = _dats;
            return new ScenarioStation(
                spec.Name,
                spec.NeedsOverride ?? new StationNeeds(StationWhere.Den),
                c => new DeathScenario(
                    c.Spells,
                    KitAccount(c.Log),
                    death with { Travel = c.Travel, Facts = c.Facts, Journey = c },
                    DeathGroundWalk(c),
                    c.Log,
                    endSession: false,
                    stages: death.Stages ?? DeathStages.All,
                    buff: buff,
                    fight: _fight with { Kills = death.FightKills, Seconds = death.FightSeconds, HoldStance = spec.Arguments.ContainsKey(UnattendedArgument) }), // M5 7 F25: the autopilot's corpse-trip fights keep the magic mode
                s => ((DeathScenario)s).VerdictFields(),
                (s, status) => status == ScenarioStatus.Succeeded && ((DeathScenario)s).Outcome().Ok);
        }

        private ScenarioStation Hunt(StationSpec spec, bool vendor)
        {
            var template = _fighter ?? throw new NotSupportedException($"TODO(j4-hunt-character): the {spec.Kind} station needs a fighter template; '{_plan.Character}' on '{_plan.Account}' has none");
            var options = vendor
                ? _hunt with { Stages = HuntStages.Vendor }
                : _hunt with
                {
                    Stages = HuntStages.Ground,
                    Kills = _arguments.Any(a => a.Name == "--hunt-kills") ? _hunt.Kills : template.Name == FighterTemplate.ArcherName ? ArcherHuntKills : MeleeHuntKills,
                    Seconds = _arguments.Any(a => a.Name == "--hunt-seconds") || template.Name != FighterTemplate.ArcherName ? _hunt.Seconds : ArcherHuntSeconds,
                    Sell = false,
                    BuyKits = 0,
                };
            var dats = _dats;
            ILogSink log = NullLogSink.Instance;
            IJourneyWindowFrames? frames = null;
            var needs = spec.NeedsOverride ?? (vendor
                ? new StationNeeds(StationWhere.Holtburg, Peace: true)
                : new StationNeeds(_plan.Ground, MinHealthPercent: JourneyPlans.FightHealthPercent, MinStaminaPercent: JourneyPlans.HuntStaminaPercent));
            return new ScenarioStation(
                spec.Name,
                needs,
                c =>
                {
                    log = c.Log;
                    frames = c.WindowFrames;
                    return new HuntScenario(
                        options with { Travel = c.Travel, Keeper = c.Keeper },
                        c.Log,
                        cell => dats is null ? null : HuntGround.TerrainHeight(dats, cell),
                        ground: options.GroundRow,
                        endSession: false,
                        follow: (c.Runners as IJourneyWalks)?.HuntFollow(c),
                        template: template)
                    {
                        SeekHeight = dats is null ? null : (cellId, x, y) => HuntGround.SeekHeight(dats, cellId, x, y),
                        ModeWatch = vendor ? null : new CombatModeWatch(), // M4 4.2 J1: mode_mismatch, judged = 0 by the hunt's verdict (D23)
                    };
                },
                s => vendor ? VendorFields((HuntScenario)s, frames) : HuntFields((HuntScenario)s, frames),
                (s, status) =>
                {
                    var h = (HuntScenario)s;
                    var outcome = HuntLegVerdict.Compose(h.Failure, HuntLegVerdict.FromScenario(h, options.Weapon, options.Loot.Enabled && options.OpenCorpse, options.Ground));
                    if (!vendor)
                    {
                        return status == ScenarioStatus.Succeeded && outcome.Ok;
                    }

                    var judged = OkWithout(outcome.Problems, VendorUnjudged, spec.Name, log);
                    return status == ScenarioStatus.Succeeded && judged && SaleHappened(h.Sold, h.SellOutcome, spec.Name, log);
                });
        }
    }
}
