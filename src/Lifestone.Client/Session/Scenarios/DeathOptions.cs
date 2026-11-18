using System.Globalization;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>Which stages of <see cref="DeathScenario"/> run (a host that has done one of them leaves it out; the tests pick the ones they script).</summary>
[Flags]
public enum DeathStages
{
    /// <summary>Only the logging off (the stage mask of a run with nothing to do).</summary>
    None = 0,

    /// <summary>The gear scenario's steps, then the mage's spell bars (<see cref="MageSpellBars"/>: tab I the buffs, tab II the fight).</summary>
    Gear = 1,

    /// <summary>D34: the teleport to Holtburg, the nearest lifestone and a Use on it (ACE moves the player to it and attunes it).</summary>
    Bind = 2,

    /// <summary>The teleport to the Black Spawn Den's arrival and the settle on its floor.</summary>
    Den = 4,

    /// <summary>
    /// D33, retired by D37 (M4 P5b, the owner's decision): a suicide in the den when any enchantment but the vitae is active. Not in <see cref="All"/> any more: the mage dies once, undressed and lying down among the tuskers
    /// (<see cref="DeathScenario"/>'s <c>undress</c> and lie-down), so no host runs it; the stage stays only for an explicit <c>--death-stages=unbuff</c> and the tests that name it.
    /// </summary>
    Unbuff = 8,

    /// <summary>The walk to the nearest tusker cell and the wait for the tuskers' death of the player, the respawn and the vitals.</summary>
    Death = 16,

    /// <summary>
    /// The corpse trip of the death handler (M5 3 d2, D51; <c>--death-recover-corpse</c>, <c>--death-recover-min-health</c>): the route to the ground, the runner to the corpse, the take-all and the items worn and wielded again. One order for every option set:
    /// the handler runs after the respawn's vitals; with this stage out of <c>--death-stages</c> (or the option off) it makes no trip.
    /// </summary>
    Corpse = 128,

    /// <summary>
    /// M4 3.1 d1 (D20), the death handler's first step since M5 3 d2: after the respawn and its vitals, the buff cycle: the death purged every enchantment, so this is the full cycle (<see cref="FightStages.Buffs"/> of the composed inner <see cref="FightScenario"/>;
    /// <c>--death-rebuff</c>). The order is the handler's, in <see cref="DeathScenario"/>'s class doc.
    /// </summary>
    Rebuff = 256,

    /// <summary>
    /// M4 3.1 d1 (D20): after the rebuff, the fight at the Black Spawn Den (<see cref="FightStages.Place"/> and <see cref="FightStages.Mode"/> and the kill loop of the composed inner <see cref="FightScenario"/>; <c>--death-fight</c>,
    /// <c>--death-fight-kills</c>, <c>--death-fight-seconds</c>): the handler's last step, after the corpse trip and the item lines. With it off the handler runs the same order and skips only this return to the fight.
    /// </summary>
    Fight = 512,

    /// <summary>Everything (the headless host's, and the game's bsd-death leg); since D37 (M4 P5b) without the <see cref="Unbuff"/> suicide: one death a run.</summary>
    All = Gear | Bind | Den | Death | Corpse | Rebuff | Fight,
}

/// <summary>
/// The death loop's options (M3 3.2 lane da; <c>--death-seconds</c> is registered in <c>OptionCatalog.Death.cs</c>, read by the headless host and the game's <c>bsd-death</c> leg): the seconds the wait for the tuskers'
/// blows may take. <see cref="StopAfterHits"/> is not on the command line. Immutable; <see cref="FromArguments"/> is the parser both hosts call.
/// </summary>
public sealed record DeathOptions
{
    /// <summary>M6 7 AY: the ladder a corpse run asks (<see cref="Autopilot.RungOwner.CorpseRun"/>). Null: the war mage's ladder for the ladder's character, none for any other (a test passes its own).</summary>
    internal Grounds.Ladder? CorpseLadder { get; init; }

    /// <summary>M6 7 AY: a test's buffs for that ask. Null: the live ones.</summary>
    internal Autopilot.RungBuffRead? CorpseBuffsForTest { get; init; }

    /// <summary>The default of <c>--death-seconds</c>.</summary>
    public const double DefaultSeconds = 600;

    /// <summary>The least <c>--death-seconds</c> accepted.</summary>
    public const double MinSeconds = 1;

    /// <summary>The most <c>--death-seconds</c> accepted.</summary>
    public const double MaxSeconds = 3600;

    /// <summary>
    /// The seconds, on the session clock from the start of the wait among the tuskers, after which the scenario fails loudly (<c>--death-seconds</c>); with <see cref="StopAfterHits"/> the same limit bounds the
    /// wait for the first blow.
    /// </summary>
    public double Seconds { get; init; } = DefaultSeconds;

    /// <summary>
    /// True: the scenario does not wait for the death (DoD 9's live test, not on the command line). On the first 0x01B2 melee notice with the health share under 100% it waits <see cref="DeathScenario.StopAfterHitsSeconds"/> s, then
    /// stays in non-combat and logs off. A mage that dies first (unbuffed it can in about 20 s) is reported, its respawn is not judged, and the run still logs off cleanly.
    /// </summary>
    public bool StopAfterHits { get; init; }

    /// <summary>
    /// The stages to run (<c>--death-stages=gear,bind,den,unbuff,death,corpse,rebuff,fight</c>, or <c>all</c> or <c>none</c>), or null for all of them. A run without the Death stage judges nothing about the death (only a failure fails it),
    /// the stages after the respawn (rebuff, corpse, fight) run only after a death. Read by the headless host; the game's bsd-death leg always runs all.
    /// </summary>
    public DeathStages? Stages { get; init; }

    /// <summary>The default of <c>--death-fight-kills</c>.</summary>
    public const int DefaultFightKills = 4;

    /// <summary>The default of <c>--death-fight-seconds</c>.</summary>
    public const double DefaultFightSeconds = 180;

    /// <summary>The default of <c>--death-recover-min-health</c>, in percent of the maximum health.</summary>
    public const double DefaultRecoverMinHealthPercent = 60;

    /// <summary>The default of <c>--death-recover-abort-health</c>, in percent of the maximum health (M4 2.2's 40).</summary>
    public const double DefaultAbortHealthPercent = 40;

    /// <summary>The default of <c>--death-recover-abort-seconds</c>, in seconds (M4 2.2's 90).</summary>
    public const double DefaultAbortSeconds = DeathScenario.TripSeconds;

    /// <summary>
    /// True (default, <c>--death-recover-corpse on</c>): the <see cref="DeathStages.Corpse"/> stage goes back for the mage's own corpse after the respawn. False (<c>off</c>) makes no trip (the corpse is not gone back for and the items stay on it; M5 D51:
    /// nothing is spawned in their place).
    /// </summary>
    public bool RecoverCorpse { get; init; } = true;

    /// <summary>The least health share, in percent, the mage must have for the trip to start (<c>--death-recover-min-health</c>, 0 to 100, default 60; the respawn leaves it at 75).</summary>
    public double RecoverMinHealthPercent { get; init; } = DefaultRecoverMinHealthPercent;

    /// <summary>
    /// The health line among the creatures (<c>--death-recover-abort-health</c>, 0 to 100, default 40; M5 3 d4): when the share falls under it on the trip the handler holds where it stands and heals up to the larger of this and
    /// <see cref="RecoverMinHealthPercent"/>, and turns back (gives the corpse up, <c>trip_abort=health-under-&lt;N&gt;</c>) only when the heals cannot bring it back. 0 never holds or turns back for health.
    /// </summary>
    public double AbortHealthPercent { get; init; } = DefaultAbortHealthPercent;

    /// <summary>The default of <c>--death-trip-aggro-m</c> (M5 7 F19, D92: Lifestone's own aggro reach of a creature near the corpse trip's path, 20 m).</summary>
    public const double DefaultTripAggroMetres = 20;

    /// <summary>
    /// M5 7 F19 (D92, the owner: the corpse trip never runs past tuskers): the reach, in metres, within which a living target creature of the ground near the path ahead is fought before the trip advances, and near the corpse before it is
    /// looted (<c>--death-trip-aggro-m</c>, 0 to 200, default 20; 0 is off).
    /// </summary>
    public double TripAggroMetres { get; init; } = DefaultTripAggroMetres;

    /// <summary>The trip's time, in seconds, counted from the arrival in the ground (<c>--death-recover-abort-seconds</c>, above 0, default 90; M5 3 d4): past it the trip is given up, <c>trip_abort=trip-over-&lt;N&gt;s</c>.</summary>
    public double AbortSeconds { get; init; } = DefaultAbortSeconds;

    /// <summary>
    /// True (default, <c>--death-rebuff on</c>): after the respawn the loop runs the buff cycle at Holtburg (<see cref="DeathStages.Rebuff"/>; the death purged every enchantment). False (<c>off</c>) leaves the mage unbuffed for the next stage.
    /// </summary>
    public bool Rebuff { get; init; } = true;

    /// <summary>
    /// True (default, <c>--death-fight on</c>): after the respawn, the rebuff, the corpse trip and the item lines the handler returns to the fight (<see cref="FightKills"/> kills; <see cref="DeathStages.Fight"/>). False (<c>off</c>) keeps the handler and skips only this return
    /// (M5 3 d2: one order for every option set).
    /// </summary>
    public bool Fight { get; init; } = true;

    /// <summary>The kills that end the death loop's fight (<c>--death-fight-kills</c>, 1 to <see cref="FightOptions.MaxKills"/>, default <see cref="DefaultFightKills"/>).</summary>
    public int FightKills { get; init; } = DefaultFightKills;

    /// <summary>The seconds after which the death loop's fight fails loudly (<c>--death-fight-seconds</c>, above 0, default <see cref="DefaultFightSeconds"/>).</summary>
    public double FightSeconds { get; init; } = DefaultFightSeconds;

    /// <summary>
    /// M4 P4 g3fix: the most seconds the fight stage waits, at Holtburg and before the teleport to the den, for the mana to regenerate to <see cref="FightManaFloorPercent"/> of the maximum (0, the default: no wait, the game's leg is unchanged).
    /// The rebuff's 27 casts leave the mage at about 23 mana, and the mage regenerates about 0.43 a second, so a 180 s fight started there waits for mana for most of its time (g3: 3 of 4 kills, phase=Mana). Not a retry: nothing has been cast.
    /// </summary>
    public double FightManaWaitSeconds { get; init; }

    /// <summary>The share of the maximum mana, in percent, that ends the wait of <see cref="FightManaWaitSeconds"/> (default 70).</summary>
    public double FightManaFloorPercent { get; init; } = DefaultFightManaFloorPercent;

    /// <summary>The default of <see cref="FightManaFloorPercent"/>.</summary>
    public const double DefaultFightManaFloorPercent = 70;

    /// <summary>
    /// M4 P5b (3.4): the travel seam the death loop's places go through (the bind <see cref="TravelPurpose.Bind"/>, the den <see cref="TravelPurpose.Ground"/>, the composed fight's way back to the den after the respawn
    /// <see cref="TravelPurpose.Ground"/>, the corpse trip <see cref="TravelPurpose.Corpse"/>, the way home and the den exit <see cref="TravelPurpose.Home"/>), or null for today's <c>Tools.Place</c> (a fresh
    /// <see cref="PlaceTravel"/>), so every existing run is unchanged. A journey injects its own. Not on the command line.
    /// </summary>
    public IJourneyTravel? Travel { get; init; }

    /// <summary>
    /// M5 3 d1 (D51): the journey's facts, or null. The death handler reads the handler's ground's tie from <see cref="JourneyFacts.PortalTieOf"/> (tied: Primary Portal Recall; untied: the runner; unknown: the recall, whose 0x04A3 answer switches to
    /// the runner) and writes back what the recall taught (the tie, <see cref="JourneyFacts.Recalls"/>). Not on the command line.
    /// </summary>
    public JourneyFacts? Facts { get; init; }

    /// <summary>
    /// M5 3 F5 (D53): the journey's context, or null. The death handler's rearm (a mage whose wielded caster went to the corpse, with none in the pack, buys the ground's kit wand at the home vendor) runs the resupply's kit trip with it
    /// (<see cref="ResupplyRun"/>: its log, travel seam and ground). Null (the standalone death leg): the rearm cannot shop and fails by name (<c>TODO(m5-death-rearm-standalone)</c>). Not on the command line.
    /// </summary>
    public JourneyContext? Journey { get; init; }

    /// <summary>
    /// M6 6 C1 (the item's contract, map-death section 9): the worn set of before the death the handler wears again and reads for what left the inventory, owned by the caller (the autopilot keeps one per run). Null (the default) is
    /// <see cref="Mage.WornMemory.Shared"/>, today's process-wide memory, so every caller still builds; it goes once every caller passes its own (the lead's call at the merge). Not on the command line.
    /// </summary>
    public Mage.WornMemory? Worn { get; init; }

    /// <summary>
    /// M4 P5b (3.4): the stage the run begins at (an embedded run: the journey's death station begins at <see cref="DeathStages.Death"/>, after the den fight), or null for the first enabled one. One of
    /// <see cref="DeathStages.Gear"/>, <see cref="DeathStages.Bind"/>, <see cref="DeathStages.Den"/>, <see cref="DeathStages.Death"/>/>; the stages before it are skipped (the login too:
    /// a run that starts past the gear waits only for the player's description). Not on the command line.
    /// </summary>
    public DeathStages? StartAt { get; init; }

    /// <summary>
    /// M4 P5b (3.4): true (the default, every existing run) leaves the den on a failure (<see cref="GroundExit"/>) before the run ends; false (a journey) ends Failed where the player stands, in the world (no exit; no log off when
    /// <c>endSession</c> is false). The composed fight inherits it, and with it false the fight also stays in the den at its end (the corpse trip follows on foot).
    /// </summary>
    public bool LeaveOnFailure { get; init; } = true;

    /// <summary>Every problem found while parsing, one line each.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Parses <c>--death-seconds</c>, <c>--death-stages</c>, <c>--death-recover-corpse</c>, <c>--death-recover-min-health</c>, <c>--death-decoys</c>, <c>--death-rebuff</c>, <c>--death-fight</c>, <c>--death-fight-kills</c> and <c>--death-fight-seconds</c> from arguments already split; an option that does not parse is reported in <see cref="Errors"/> and keeps its default.</summary>
    /// <param name="args">The user arguments.</param>
    public static DeathOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new DeathOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            if (argument.Name == "--death-stages")
            {
                var parsed = ParseStages(argument.Value, out var problem);
                if (problem is null)
                {
                    o = o with { Stages = parsed };
                }
                else
                {
                    errors.Add($"{argument.Name}: {problem}");
                }

                continue;
            }

            if (argument.Name == "--death-recover-corpse")
            {
                o = o with { RecoverCorpse = OptionValues.Switch(argument, errors, o.RecoverCorpse) };
                continue;
            }

            if (argument.Name == "--death-decoys")
            {
                // M5 3 d3 (D51): the decoys are retired; the option is refused by name, never ignored in silence.
                errors.Add($"{argument.Name}: the decoys are retired: M5 D51 (a death drops the mage's own gear and the corpse trip brings it back)");
                continue;
            }

            if (argument.Name == "--death-rebuff")
            {
                o = o with { Rebuff = OptionValues.Switch(argument, errors, o.Rebuff) };
                continue;
            }

            if (argument.Name == "--death-fight")
            {
                o = o with { Fight = OptionValues.Switch(argument, errors, o.Fight) };
                continue;
            }

            if (argument.Name == "--death-fight-kills")
            {
                var before = errors.Count;
                var kills = OptionValues.Int(argument, errors, o.FightKills);
                if (errors.Count == before && kills is >= 1 and <= FightOptions.MaxKills)
                {
                    o = o with { FightKills = kills };
                }
                else if (errors.Count == before)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not 1 to {FightOptions.MaxKills}");
                }

                continue;
            }

            if (argument.Name == "--death-fight-seconds")
            {
                var before = errors.Count;
                var fightSeconds = OptionValues.Double(argument, errors, o.FightSeconds);
                if (errors.Count == before && fightSeconds > 0)
                {
                    o = o with { FightSeconds = fightSeconds };
                }
                else if (errors.Count == before)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not a positive number of seconds");
                }

                continue;
            }

            if (argument.Name == "--death-recover-min-health")
            {
                var before = errors.Count;
                var share = OptionValues.Double(argument, errors, o.RecoverMinHealthPercent);
                if (errors.Count == before && share is >= 0 and <= 100)
                {
                    o = o with { RecoverMinHealthPercent = share };
                }
                else if (errors.Count == before)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not 0 to 100 percent");
                }

                continue;
            }

            if (argument.Name == "--death-recover-abort-health")
            {
                var before = errors.Count;
                var line = OptionValues.Double(argument, errors, o.AbortHealthPercent);
                if (errors.Count == before && line is >= 0 and <= 100)
                {
                    o = o with { AbortHealthPercent = line };
                }
                else if (errors.Count == before)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not 0 to 100 percent");
                }

                continue;
            }

            if (argument.Name == "--death-trip-aggro-m")
            {
                var before = errors.Count;
                var reach = OptionValues.Double(argument, errors, o.TripAggroMetres);
                if (errors.Count == before && reach is >= 0 and <= 200)
                {
                    o = o with { TripAggroMetres = reach };
                }
                else if (errors.Count == before)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not 0 to 200 metres");
                }

                continue;
            }

            if (argument.Name == "--death-recover-abort-seconds")
            {
                var before = errors.Count;
                var limit = OptionValues.Double(argument, errors, o.AbortSeconds);
                if (errors.Count == before && limit > 0)
                {
                    o = o with { AbortSeconds = limit };
                }
                else if (errors.Count == before)
                {
                    errors.Add($"{argument.Name}: '{argument.Value}' is not a positive number of seconds");
                }

                continue;
            }

            if (argument.Name != "--death-seconds")
            {
                continue;
            }

            var known = errors.Count;
            var seconds = OptionValues.Double(argument, errors, o.Seconds);
            if (errors.Count == known && seconds is >= MinSeconds and <= MaxSeconds)
            {
                o = o with { Seconds = seconds };
            }
            else if (errors.Count == known)
            {
                errors.Add($"{argument.Name}: '{argument.Value}' is not 1 to {MaxSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds");
            }
        }

        return o with { Errors = errors };
    }

    /// <summary>Parses a stage list: names joined by commas or plus signs, case-insensitive; <c>all</c> and <c>none</c> stand alone.</summary>
    /// <param name="text">The option's value.</param>
    /// <param name="problem">Why it does not parse, or null.</param>
    public static DeathStages ParseStages(string? text, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            problem = "a list of stages is expected (gear, bind, den, unbuff, death, corpse, rebuff, fight, all, none)";
            return DeathStages.All;
        }

        var stages = DeathStages.None;
        foreach (var part in text.Split([',', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "gear": stages |= DeathStages.Gear; break;
                case "bind": stages |= DeathStages.Bind; break;
                case "den": stages |= DeathStages.Den; break;
                case "unbuff": stages |= DeathStages.Unbuff; break;
                case "death": stages |= DeathStages.Death; break;
                case "corpse": stages |= DeathStages.Corpse; break;
                case "regear":
                    problem = "the stage 'regear' is removed: the death handler wears and wields what the corpse gives back, nothing is spawned (M5 D51)";
                    return DeathStages.All;
                case "rebuff": stages |= DeathStages.Rebuff; break;
                case "fight": stages |= DeathStages.Fight; break;
                case "vitae":
                    problem = "the stage 'vitae' is removed: the vitae clears by earned experience, M5 D51";
                    return DeathStages.All;
                case "all": stages |= DeathStages.All; break;
                case "none": break;
                default:
                    problem = $"'{part}' is not a stage (gear, bind, den, unbuff, death, corpse, rebuff, fight, all, none)";
                    return DeathStages.All;
            }
        }

        return stages;
    }

    /// <summary>The options as the run logs them, in invariant culture.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"seconds={Seconds}{(StopAfterHits ? " stop_after_hits=yes" : string.Empty)}{(RecoverCorpse ? string.Empty : " recover_corpse=off")}{(Rebuff ? string.Empty : " rebuff=off")}{(Fight ? string.Empty : " fight=off")}{(FightKills == DefaultFightKills ? string.Empty : " fight_kills=" + FightKills)}{(FightSeconds == DefaultFightSeconds ? string.Empty : " fight_seconds=" + FightSeconds)}{(RecoverMinHealthPercent == DefaultRecoverMinHealthPercent ? string.Empty : " recover_min_health=" + RecoverMinHealthPercent)}{(AbortHealthPercent == DefaultAbortHealthPercent ? string.Empty : " abort_health=" + AbortHealthPercent)}{(AbortSeconds == DefaultAbortSeconds ? string.Empty : " abort_seconds=" + AbortSeconds)}{(Stages is { } stages ? " stages=" + stages.ToString().Replace(", ", "+", StringComparison.Ordinal).ToLowerInvariant() : string.Empty)}{(StartAt is { } at ? " start_at=" + at.ToString().ToLowerInvariant() : string.Empty)}{(LeaveOnFailure ? string.Empty : " leave_on_failure=no")}{(Travel is null or PlaceTravel ? string.Empty : " travel=journey")}");
}
