using System.Globalization;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// The autopilot's options (M5 item 4 a1, D57; registered in <c>OptionCatalog.Autopilot.cs</c>): the stop rules (<c>--autopilot-minutes</c> on the session clock, <c>--autopilot-level</c> on the character level the state holds), the forced trip
/// (<c>--autopilot-trip-after-kills</c>, D52's live half) and the gate's scripted death (<c>--autopilot-test-death</c>, M4 D37). A stop request (<see cref="AutopilotScenario.RequestStop"/>) is not an option: the key and the hosts send it.
/// No rule set means the run goes on until a stop request. Immutable; <see cref="FromArguments"/> is the parser the hosts call. The bounds are Lifestone's own.
/// </summary>
public sealed record AutopilotOptions
{
    /// <summary>
    /// The ladder's character (M6 D7, 2026-11-01): <c>Winnie</c> on the account <c>test</c>, re-created at each gate by the M4 D44/D45 pattern. A character name, not an account name. The host's start rule plays it in the
    /// <see cref="AutopilotJob.LevelUp"/> job only (<see cref="AutopilotHostLogic.RequiredCharacter"/>).
    /// </summary>
    public const string LadderCharacter = "Winnie";

    /// <summary>The job's command-line names (<c>--autopilot-job</c>), in <see cref="AutopilotJob"/> order.</summary>
    public static IReadOnlyList<string> JobNames { get; } = ["den", "level-up"];

    /// <summary>The most minutes <c>--autopilot-minutes</c> accepts: a week.</summary>
    public const double MaxMinutes = 7 * 24 * 60;

    /// <summary>The highest level <c>--autopilot-level</c> accepts (ACE's level cap, 275).</summary>
    public const int MaxLevel = 275;

    /// <summary>The most kills <c>--autopilot-trip-after-kills</c> accepts.</summary>
    public const int MaxTripAfterKills = 100_000;

    /// <summary>The most reconnect tries <c>--autopilot-reconnect-waits</c> accepts.</summary>
    public const int MaxReconnectTries = 10;

    /// <summary>The longest single reconnect wait <c>--autopilot-reconnect-waits</c> accepts, in seconds.</summary>
    public const double MaxReconnectWaitSeconds = 600;

    /// <summary>The most recoveries <c>--autopilot-watchdog-recoveries</c> accepts.</summary>
    public const int MaxWatchdogRecoveries = 20;

    /// <summary>The watchdog scale's range (<c>--autopilot-watchdog-scale</c>).</summary>
    public const double MinWatchdogScale = 0.01;

    /// <summary>The watchdog scale's range (<c>--autopilot-watchdog-scale</c>).</summary>
    public const double MaxWatchdogScale = 100;

    /// <summary>
    /// The seconds waited before each reconnect try (M5 item 4 a2; Lifestone's own, no retail rule): 5, 15, 30, 60, 120. ACE drops a client silent for 60 s (<c>NetworkSession.cs:336-338</c> @95ef426d), so the first tries may meet the old session
    /// still held and the sum (230 s) outlasts it by a wide margin. The last failure stops the run (<c>stop=reconnect-failed</c>).
    /// </summary>
    public static IReadOnlyList<double> DefaultReconnectWaits { get; } = [5, 15, 30, 60, 120];

    /// <summary>The session-clock minutes after which the autopilot stops (<c>stop=minutes</c>) at its next safe point; 0 (the default) is no such rule.</summary>
    public double Minutes { get; init; }

    /// <summary>The character level at which the autopilot stops (<c>stop=level</c>), read from the state's PropertyInt 25; 0 (the default) is no such rule.</summary>
    public int Level { get; init; }

    /// <summary>The kills, counted from the cycle's start, after which the autopilot forces one logistics trip (<c>trigger=forced</c>, D52); once a run (the gate wants one trip forced early). 0 (the default) is none.</summary>
    public int TripAfterKills { get; init; }

    /// <summary>True: the gate's scripted death, once, at the first fight after its first kill (the journey's death station: M4 D37's undress and lie down among the creatures).</summary>
    public bool TestDeath { get; init; }

    /// <summary>M6 item 14 (D125): true (the default): at home the autopilot stops at the buff bot before its own buff pass (<c>--autopilot-buff-bot</c>); the player turns it off.</summary>
    public bool BuffBot { get; init; } = true;

    /// <summary>
    /// M6 villa (the owner's ruling, 2026-11-15: packs first, and then ideally a villa): true (the default): the level up job's villa schedule runs at each town trip's boundary (<c>--autopilot-villa</c>; <see cref="Housing.VillaSchedule"/>): the writ run
    /// paced to the account's opening, the Crude Lockpick, the purchase, the rent and the storage trip. The cycle line's <c>villa=</c> says it and the run's progress.
    /// </summary>
    public bool Villa { get; init; } = true;

    /// <summary>
    /// M6 villa (the owner, 2026-11-16: a house needs no account age): false (the default): the account may buy a villa at once (<see cref="Housing.VillaFacts.ClimberAccountOpensVillas"/>), as the server mod sets ACE's
    /// house_15day_account false at every start. True (<c>--autopilot-villa-account-age=ace</c>, to match a server started with <c>LIFESTONE_ACE_HOUSE_ACCOUNT_AGE=ace</c>): retail's 15 days, the climber's account opening
    /// <see cref="Housing.VillaFacts.ClimberAccountOpensVillasRetail"/>.
    /// </summary>
    public bool VillaAccountAge { get; init; }

    /// <summary>The first instant the villa schedule and the purchase let the account buy (<see cref="VillaAccountAge"/>).</summary>
    public DateTimeOffset VillaOpensAt => VillaAccountAge ? Housing.VillaFacts.ClimberAccountOpensVillasRetail : Housing.VillaFacts.ClimberAccountOpensVillas;

    /// <summary>
    /// M6 14 (the owner, 2026-11-16: with the money she has, she buys every spell, eventually when affordable): true (the default): the level up job's town trips buy and read every spell a catalog scrivener sells that she can cast, beyond the
    /// spell plan and the job's needs, the damage lines first, then her buffs and protections, then the rest, from the purse above a reserve derived from her supplies, the buff bot's fee, the villa and the trip's planned needs
    /// (<c>--autopilot-every-spell</c>; <see cref="Mage.EverySpellPlan"/>). The cycle line's <c>every_spell=</c> says it and the last trip's numbers.
    /// </summary>
    public bool EverySpell { get; init; } = true;

    /// <summary>
    /// M6 14 hour load (the owner, 2026-11-16): the hour's rising load line and the hour's trip on a comfortable ground (<c>--autopilot-hour-load</c>, <c>--autopilot-hour-load-minutes</c>, <c>--autopilot-hour-load-top</c>; default on, 60 minutes,
    /// 150%; <see cref="Mage.CycleLoad"/>).
    /// </summary>
    public Mage.CycleLoadSettings HourLoad { get; init; } = new();

    /// <summary>
    /// M6 item 14 (the owner, 2026-11-12: she says <c>all</c>): the profile the character asks the buff bot for (<c>--autopilot-buff-bot-word</c>, any word the bot knows; the default <see cref="BuffBotWire.Default"/>, <c>all</c>;
    /// <c>tusker</c> is the profile of before). Every rule of the stop reads this profile: the stop is due and done by its lines, the trip home is called by the least seconds any of them has left, and the bot's cover is its lines.
    /// The word told is the profile's key.
    /// </summary>
    public BuffBotProfile BuffBotProfile { get; init; } = BuffBotWire.Default;

    /// <summary>
    /// M6 item 14 S3 (the owner's sprint, 2026-11-11): true (the default): the ladder runs its quest errands and its trophies as before (<c>--autopilot-ladder-quests</c>). False: the level up job runs no quest errand from home, turns in no trophy, wants no
    /// trophy of the fight and enters no rung for a trophy job; each quest skipped is logged once by name. The ladder's first rungs that are quests (the Training Academy and the Pathwarden chest, her first gear) stay in both settings.
    /// </summary>
    public bool LadderQuests { get; init; } = true;

    /// <summary>
    /// M6 14 guard task (the owner, 2026-11-13): the quests the level up job runs when <see cref="LadderQuests"/> is on (<c>--autopilot-ladder-quests</c> with a list of the quest catalog's keys), or null: <see cref="LadderQuestSelection.On"/>, the ladder
    /// as before. Read through <see cref="Quests"/>.
    /// </summary>
    public LadderQuestSelection? QuestSelection { get; init; }

    /// <summary>
    /// M6 14 (the owner's ruling, 2026-11-16: start the quest on request (a way to trigger the autopilot to start the quest at once)): the quest whose errand the level up job runs from home at the run's start (<c>--autopilot-start-quest</c>, a key of the quest catalog), or null:
    /// none. Its level gate and its timers are read at the start, by name; then the ladder goes on as usual (<c>AutopilotScenario.StartQuest.cs</c>).
    /// </summary>
    public string? StartQuest { get; init; }

    /// <summary>The option that names <see cref="StartQuest"/>.</summary>
    public const string StartQuestOption = "--autopilot-start-quest";

    /// <summary>The quests the level up job runs: <see cref="LadderQuestSelection.Off"/> when <see cref="LadderQuests"/> is off, else <see cref="QuestSelection"/> or <see cref="LadderQuestSelection.On"/>.</summary>
    public LadderQuestSelection Quests => !LadderQuests ? LadderQuestSelection.Off : QuestSelection ?? LadderQuestSelection.On;

    /// <summary>
    /// M6 14 guard task: the kill tasks the selection names (<see cref="LadderKillTask"/>: each selected quest with a kill step), one object each for the process (the options are parsed once and every run's options are copies, so every run
    /// shares what the server said of the counter). Empty by default: the ladder has never run a kill task unprompted.
    /// </summary>
    public IReadOnlyList<LadderKillTask> KillTasks { get; init; } = [];

    /// <summary>M6 item 14: the den run's order is a rehearsal (<see cref="DenRunRehearseOption"/>): the road as far as the Desert March Portal's approach at Danby's Outpost, then home; off by default.</summary>
    public bool DenRunRehearse { get; init; }

    /// <summary>M6 16 Q8: the quest steps' kill and their walks' defence run their own old loop (<see cref="QuestOwnKillLoopOption"/>), the fallback for the skirmish's first live gate on the errands; off by default (the skirmish).</summary>
    public bool QuestOwnKillLoop { get; init; }

    /// <summary>
    /// M6 item 14 (the owner, 2026-11-13): the trial of a later rung's ground (<see cref="TrialGroundOption"/> with <see cref="TrialMinutesOption"/>), or null (the default: off). The one object of the process: the options are
    /// parsed once and every run's options are copies, so the runs share it (<see cref="LadderTrial"/>).
    /// </summary>
    public LadderTrial? Trial { get; init; }

    /// <summary>
    /// M6 14 (the move-on loop of 2026-11-14): the rungs a way out held by their approach in this process (<see cref="Autopilot.ApproachHolds"/>). One object, as <see cref="Trial"/>: the options are parsed once and every run's
    /// options are copies, so the host's restarts and the runs share it; a new options object (a test) starts empty.
    /// </summary>
    public ApproachHolds ApproachHolds { get; init; } = new();

    /// <summary>
    /// M6 14 gate armour: the armour lines a way out sent her on a town trip for in this process (<see cref="Autopilot.GateArmourTrips"/>), so a restart on the rung under it does not take the same trip again. One object, as
    /// <see cref="ApproachHolds"/>.
    /// </summary>
    public GateArmourTrips GateArmourTrips { get; init; } = new();

    /// <summary>The factor on every watchdog bound (<see cref="AutopilotWatchdogs.BaseSeconds"/>; <c>--autopilot-watchdog-scale</c>, default 1).</summary>
    public double WatchdogScale { get; init; } = 1;

    /// <summary>The recoveries a cycle's watchdog trips may make before the next one stops the run (<c>--autopilot-watchdog-recoveries</c>, default <see cref="AutopilotWatchdogs.DefaultMaxRecoveries"/>).</summary>
    public int WatchdogRecoveries { get; init; } = AutopilotWatchdogs.DefaultMaxRecoveries;

    /// <summary>The seconds waited before each reconnect try, one entry a try (<c>--autopilot-reconnect-waits</c>, default <see cref="DefaultReconnectWaits"/>).</summary>
    public IReadOnlyList<double> ReconnectWaits { get; init; } = DefaultReconnectWaits;

    /// <summary>
    /// The run's tag (M5 4 F1, 4-R1): the checkpoint stores it, and a file written under another tag is another run's, set aside and named on the <c>checkpoint:</c> line. Empty (the default): none. Not a command-line option and no host sets it
    /// today: the gate's lane (<c>tools/smoke.ps1</c>'s autopilot step) has no per-round value at hand, and a round starts on a re-created character whose checkpoint is its own (M6 7 I2; TODO(m6-run-tag-host) if a lane ever needs one).
    /// </summary>
    public string RunTag { get; init; } = string.Empty;

    /// <summary>
    /// The parsed buff options the host passes (M5 4 F1, 4-R4's scenario half): the autopilot's upkeep reads the rebuff margin (<see cref="BuffOptions.RebuffMarginSeconds"/>) and the buff cycle's other settings from it, as the rebuff driver does,
    /// so <c>--buff-auto</c>'s margin and the upkeep's agree. Null (the default): the driver's defaults (<c>new BuffOptions()</c>).
    /// </summary>
    public BuffOptions? Buff { get; init; }

    /// <summary>
    /// M6 16 E5e2: the command line's <c>--buff-cast-chance</c> as the buff options read it (<see cref="BuffOptions.FromArguments"/>), or null when the command line did not give the option. Given, the fight casts at it
    /// whatever the job (the den-fight station keeps the command line's chance over the job's, <c>JourneyStations.DenFightBuff</c>), so the gear keeps read it too (<c>AutopilotScenario.FightCastChance</c>).
    /// </summary>
    public double? FightCastChanceGiven { get; init; }

    /// <summary>
    /// M6 item 0 a4: the job (<c>--autopilot-job</c>, default <see cref="AutopilotJob.Den"/>: M5's den job, unchanged). <see cref="AutopilotJob.LevelUp"/> is the <c>level up</c> job's first form: the ground is the ladder's rung for the
    /// character's level, re-read at each cycle's start (<see cref="AutopilotScenario.DecideRung"/>), and every cycle and the done line add a <c>[ladder]</c> line (report-only, D27).
    /// </summary>
    public AutopilotJob Job { get; init; } = AutopilotJob.Den;

    /// <summary>M6 6 MA: the task inside the install job (<c>--autopilot-task job|quest:&lt;key&gt;|errand</c>, default <see cref="AutopilotTask.Job"/>: the job's own loop).</summary>
    public AutopilotTask Task { get; init; } = AutopilotTask.Job;

    /// <summary><c>--autopilot-climber=NAME</c>: which climber the level up job plays (default <see cref="LadderCharacter"/>).</summary>
    public string Climber { get; init; } = LadderCharacter;

    /// <summary>The parse errors (empty when every option parsed).</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>
    /// M5 7 F15 (7-F2): <c>--fight-drain-under</c> as the fight reads it (default <see cref="Scenarios.FightOptions.DefaultDrainUnderPercent"/>; 0 is the drain rule off). The autopilot reads it only to keep its own health stop under the
    /// fight's drain line (<see cref="HealthFloorPercent"/>); the fight itself parses and validates the option.
    /// </summary>
    public int DrainUnderPercent { get; init; } = Scenarios.FightOptions.DefaultDrainUnderPercent;

    /// <summary>M5 7 F23: <c>--fight-emergency-floor</c> as the fight reads it: the vitals guard's emergency line in the states outside the fight is the fight's own (the fight parses and validates the option).</summary>
    public int EmergencyFloorPercent { get; init; } = Scenarios.FightOptions.DefaultEmergencyFloorPercent;

    /// <summary>M5 7 F23: <c>--fight-emergency-safe</c> as the fight reads it (the guard's safe line).</summary>
    public int EmergencySafePercent { get; init; } = Scenarios.FightOptions.DefaultEmergencySafePercent;

    /// <summary>M5 7 F23: <c>--fight-emergency-margin</c> as the fight reads it.</summary>
    public int EmergencyMarginPercent { get; init; } = Scenarios.FightOptions.DefaultEmergencyMarginPercent;

    /// <summary>
    /// M6 2 f (2-R1): <c>--fight-loot</c> as the fight reads it (<see cref="Scenarios.FightOptions.Loot"/>; the parser sets it from the fight's options). The gear floor is bought only on a loot trip (the resupply's <c>Gear</c> is the fight's loot), so with it off
    /// the audit names a short floor (<c>floor short, loot off: no trip</c>) and forces no trip that cannot buy. The default of a hand-built options object is true (the audit assumes the floor is buyable unless told loot is off); <see cref="FromArguments"/> always sets it.
    /// </summary>
    public bool LootOn { get; init; } = true;

    /// <summary>M5 7 F23: <c>--fight-heal-damage-weight</c> as the fight reads it (the guard's heal pick is the fight's).</summary>
    public double HealDamageWeight { get; init; } = Vitals.HealPick.DefaultDamageWeight;

    /// <summary>M5 7 F23: <c>--death-trip-aggro-m</c> as the death handler reads it: the aggro reach of the hostile watch (a living target within it holds the buff upkeep and the in-fight refresh; F19's registered distance).</summary>
    public double AggroReachMetres { get; init; } = Scenarios.DeathOptions.DefaultTripAggroMetres;

    /// <summary>
    /// M5 7 F15 (7-F2): the health share, in percent, under which the upkeep stops the fight for the keeper's heal. With the drain rule off (<see cref="DrainUnderPercent"/> 0) it is
    /// <see cref="AutopilotScenario.HealthFloorPercent"/> (50). With it on, D88's drain starts at <see cref="DrainUnderPercent"/> and runs until the safe line, so a stop at the same line would cut every drain short: the stop is
    /// then derived from the option, 60% of <c>--fight-drain-under</c> (30 at its default 50) and at most <see cref="AutopilotScenario.HealthFloorWithDrainPercent"/> (30), Lifestone's own numbers; the drains get the band between.
    /// </summary>
    public double HealthFloorPercent => DrainUnderPercent <= 0
        ? AutopilotScenario.HealthFloorPercent
        : Math.Min(AutopilotScenario.HealthFloorWithDrainPercent, DrainUnderPercent * 0.6);

    /// <summary>True when any stop rule is set (a run without one goes on until a stop request).</summary>
    public bool HasStopRule => Minutes > 0 || Level > 0;

    /// <summary>Parses the <c>--autopilot-*</c> options; other arguments are ignored.</summary>
    /// <param name="args">The user arguments.</param>
    public static AutopilotOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var all = args as IReadOnlyList<CommandLineArgument> ?? [.. args];
        var fight = Scenarios.FightOptions.FromArguments(all); // M5 7 F23: the guard's line is the fight's; the fight reports a bad value
        var reach = Scenarios.DeathOptions.FromArguments(all).TripAggroMetres;
        var o = new AutopilotOptions
        {
            EmergencyFloorPercent = fight.EmergencyFloorPercent,
            EmergencySafePercent = fight.EmergencySafePercent,
            EmergencyMarginPercent = fight.EmergencyMarginPercent,
            LootOn = fight.Loot,
            HealDamageWeight = fight.HealDamageWeight,
            AggroReachMetres = reach,
            FightCastChanceGiven = all.Any(a => a.Name == "--buff-cast-chance") ? BuffOptions.FromArguments(all).CastChance : null, // M6 16 E5e2: the buff options' own read of it (a bad value is theirs to report)
        };
        var errors = new List<string>();
        var denRun = false;
        var rehearse = false;
        string? trialGround = null;
        List<string>? holdGrounds = null;
        double? trialMinutesGiven = null;
        foreach (var argument in all)
        {
            var known = errors.Count;
            switch (argument.Name)
            {
                case "--autopilot-minutes":
                    var minutes = OptionValues.Double(argument, errors, o.Minutes);
                    if (errors.Count == known && minutes is >= 0 and <= MaxMinutes)
                    {
                        o = o with { Minutes = minutes };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 (off) to {MaxMinutes:0} minutes"));
                    }

                    break;
                case "--autopilot-level":
                    var level = OptionValues.Int(argument, errors, o.Level);
                    if (errors.Count == known && level is >= 0 and <= MaxLevel)
                    {
                        o = o with { Level = level };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 (off) to {MaxLevel}"));
                    }

                    break;
                case "--autopilot-trip-after-kills":
                    var kills = OptionValues.Int(argument, errors, o.TripAfterKills);
                    if (errors.Count == known && kills is >= 0 and <= MaxTripAfterKills)
                    {
                        o = o with { TripAfterKills = kills };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 (off) to {MaxTripAfterKills}"));
                    }

                    break;
                case "--autopilot-watchdog-scale":
                    var scale = OptionValues.Double(argument, errors, o.WatchdogScale);
                    if (errors.Count == known && scale is >= MinWatchdogScale and <= MaxWatchdogScale)
                    {
                        o = o with { WatchdogScale = scale };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not {MinWatchdogScale} to {MaxWatchdogScale}"));
                    }

                    break;
                case "--autopilot-watchdog-recoveries":
                    var recoveries = OptionValues.Int(argument, errors, o.WatchdogRecoveries);
                    if (errors.Count == known && recoveries is >= 0 and <= MaxWatchdogRecoveries)
                    {
                        o = o with { WatchdogRecoveries = recoveries };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 to {MaxWatchdogRecoveries}"));
                    }

                    break;
                case "--autopilot-reconnect-waits":
                    if (ParseWaits(argument.Value) is { } waits)
                    {
                        o = o with { ReconnectWaits = waits };
                    }
                    else
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 1 to {MaxReconnectTries} comma-separated waits of 0 to {MaxReconnectWaitSeconds:0} seconds"));
                    }

                    break;
                case "--fight-drain-under":
                    var drainUnder = OptionValues.Int(argument, new List<string>(), o.DrainUnderPercent); // the fight reports a bad value; here a bad one keeps the default
                    if (drainUnder is >= 0 and <= 100)
                    {
                        o = o with { DrainUnderPercent = drainUnder };
                    }

                    break;
                case "--autopilot-job":
                    var job = Array.IndexOf([.. JobNames], (argument.Value ?? string.Empty).Trim());
                    if (job >= 0)
                    {
                        o = o with { Job = (AutopilotJob)job };
                    }
                    else
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not one of {string.Join(", ", JobNames)}");
                    }

                    break;
                case "--autopilot-climber":
                    if (Mage.MageTemplate.IsClimber(argument.Value))
                    {
                        o = o with { Climber = Mage.MageTemplate.ClimberNames.First(n => string.Equals(n, argument.Value, StringComparison.OrdinalIgnoreCase)) };
                    }
                    else
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a climber ({string.Join(", ", Mage.MageTemplate.ClimberNames)})");
                    }

                    break;
                case "--autopilot-task":
                    if (AutopilotTask.TryParse(argument.Value ?? string.Empty, out var taskValue, out var taskError))
                    {
                        if (taskValue.Kind == AutopilotTaskKind.Quest && !QuestCatalog.All.Any(q => q.Key == taskValue.QuestKey))
                        {
                            errors.Add($"{argument.Name}: no quest has the key '{taskValue.QuestKey}'");
                        }
                        else
                        {
                            o = o with { Task = taskValue };
                        }
                    }
                    else
                    {
                        errors.Add($"{argument.Name}: {taskError}");
                    }

                    break;
                case "--autopilot-hour-load":
                    o = o with { HourLoad = o.HourLoad with { On = OptionValues.Switch(argument, errors, o.HourLoad.On) } };
                    break;
                case "--autopilot-hour-load-minutes":
                    var hourMinutes = OptionValues.Double(argument, errors, o.HourLoad.Minutes);
                    if (errors.Count == known && hourMinutes is > 0 and <= Mage.CycleLoadSettings.MaxMinutes)
                    {
                        o = o with { HourLoad = o.HourLoad with { Minutes = hourMinutes } };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not more than 0 and at most {Mage.CycleLoadSettings.MaxMinutes:0} minutes"));
                    }

                    break;
                case "--autopilot-hour-load-top":
                    var top = OptionValues.Int(argument, errors, o.HourLoad.TopPercent);
                    if (errors.Count == known && top is >= Mage.CycleLoadSettings.MinTopPercent and <= Mage.CycleLoadSettings.MaxTopPercent)
                    {
                        o = o with { HourLoad = o.HourLoad with { TopPercent = top } };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not {Mage.CycleLoadSettings.MinTopPercent} to {Mage.CycleLoadSettings.MaxTopPercent} (percent of the capacity)"));
                    }

                    break;
                case "--autopilot-buff-bot":
                    o = o with { BuffBot = OptionValues.Switch(argument, errors, o.BuffBot) };
                    break;
                case "--autopilot-villa":
                    o = o with { Villa = OptionValues.Switch(argument, errors, o.Villa) };
                    break;
                case "--autopilot-villa-account-age":
                    switch (argument.Value)
                    {
                        case "off" or "none":
                            o = o with { VillaAccountAge = false };
                            break;
                        case "ace":
                            o = o with { VillaAccountAge = true };
                            break;
                        default:
                            errors.Add($"{argument.Name}: '{argument.Value}' is not off or ace");
                            break;
                    }

                    break;
                case "--autopilot-every-spell":
                    o = o with { EverySpell = OptionValues.Switch(argument, errors, o.EverySpell) };
                    break;
                case "--autopilot-buff-bot-word":
                    if (BuffBotWire.ProfileOf(argument.Value) is { } botProfile)
                    {
                        o = o with { BuffBotProfile = botProfile };
                    }
                    else
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a word the buff bot knows ({string.Join(", ", BuffBotWire.Profiles.SelectMany(p => p.Words))})");
                    }

                    break;
                case StartQuestOption:
                    // M6 14: one key of the quest catalog; the level gate is read at the run's start
                    if (argument.Value is { Length: > 0 } startKey && QuestCatalog.All.Any(q => q.Key == startKey))
                    {
                        o = o with { StartQuest = startKey };
                    }
                    else
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is no quest of the catalog ({string.Join(", ", QuestCatalog.All.Select(q => q.Key))})");
                    }

                    break;
                case "--autopilot-ladder-quests":
                    if (LadderQuestSelection.Parse(argument.Value ?? string.Empty, QuestCatalog.All, out var selectionError) is { } selection)
                    {
                        // M6 14 guard task: on, off, or a selection of the quest catalog's keys; a kill task among them is one object for the process
                        o = o with
                        {
                            LadderQuests = !selection.IsOff,
                            QuestSelection = selection.IsOn || selection.IsOff ? null : selection,
                            KillTasks = selection.Keys is { } keys ? [.. QuestCatalog.All.Where(q => keys.Contains(q.Key, StringComparer.Ordinal) && q.Steps.Any(s => s.Kind == QuestStepKind.Kill)).Select(q => new LadderKillTask(q))] : [],
                        };
                    }
                    else
                    {
                        errors.Add($"{argument.Name}: {selectionError}");
                    }

                    break;
                case DenRunOption:
                    denRun = OptionValues.Switch(argument, errors, denRun);
                    break;
                case DenRunRehearseOption:
                    rehearse = OptionValues.Switch(argument, errors, rehearse);
                    break;
                case QuestOwnKillLoopOption:
                    o = o with { QuestOwnKillLoop = OptionValues.Switch(argument, errors, o.QuestOwnKillLoop) };
                    break;
                case "--autopilot-test-death":
                    o = o with { TestDeath = OptionValues.Switch(argument, errors, o.TestDeath) };
                    break;
                case TrialGroundOption:
                    if (Scenarios.FightOptions.GroundError(argument.Value) is { } groundError)
                    {
                        errors.Add($"{argument.Name}: {groundError}");
                    }
                    else
                    {
                        trialGround = argument.Value!.Trim();
                    }

                    break;
                case HoldGroundsOption:
                    // M6 14 (live 2026-11-14 19:43, ladder16: the climber died in the Tusker Armory on her first visit, and the ladder at 65 sends her back to it): grounds held for the session by name at launch
                    holdGrounds ??= [];
                    foreach (var key in (argument.Value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (Scenarios.FightOptions.GroundError(key) is { } holdError)
                        {
                            errors.Add($"{argument.Name}: {holdError}");
                        }
                        else
                        {
                            holdGrounds.Add(key);
                        }
                    }

                    if (holdGrounds.Count == 0 && errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: names no ground (a comma-separated list of ground keys, as tusker-armory)");
                    }

                    break;
                case TrialMinutesOption:
                    var trialMinutes = OptionValues.Double(argument, errors, LadderTrial.DefaultMinutes);
                    if (errors.Count == known && trialMinutes is > 0 and <= LadderTrial.MaxMinutes)
                    {
                        trialMinutesGiven = trialMinutes;
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not more than 0 and at most {LadderTrial.MaxMinutes:0} minutes"));
                    }

                    break;
            }
        }

        if (trialGround is not null || trialMinutesGiven is not null)
        {
            // M6 item 14 (the trial switch): the level up job's own loop only; the minutes are read only with a ground
            if (trialGround is null)
            {
                errors.Add($"{TrialMinutesOption}: is read only with {TrialGroundOption}");
            }
            else if (o.Job != AutopilotJob.LevelUp)
            {
                errors.Add($"{TrialGroundOption}: the trial is the level up job's (--autopilot-job {JobName(AutopilotJob.LevelUp)}); the job is {JobName(o.Job)}");
            }
            else if (o.Task.Kind != AutopilotTaskKind.Job || denRun || rehearse)
            {
                errors.Add($"{TrialGroundOption}: the trial is the level up job's own loop (--autopilot-task job); the task is {(denRun || rehearse ? AutopilotTask.DenRun.Name : o.Task.Name)}");
            }
            else
            {
                o = o with { Trial = new LadderTrial(trialGround, trialMinutesGiven ?? LadderTrial.DefaultMinutes) };
            }
        }

        if (rehearse)
        {
            // M6 item 14: a rehearsal is the den run's order with its road cut short: it names the order by itself.
            denRun = true;
            o = o with { DenRunRehearse = true };
        }

        if (denRun)
        {
            // M6 item 14: the order is the den-run task; another task named beside it is a contradiction, never a silent choice.
            if (o.Task.Kind is AutopilotTaskKind.Quest or AutopilotTaskKind.Errand)
            {
                errors.Add($"{DenRunOption}: the den run is a task of its own and --autopilot-task names another ({o.Task.Name}); give one of them");
            }
            else
            {
                o = o with { Task = AutopilotTask.DenRun };
            }
        }

        if (o.Task.Kind == AutopilotTaskKind.DenRun && o.Job != AutopilotJob.LevelUp)
        {
            errors.Add($"{DenRunOption}: the den run is the level up job's order (--autopilot-job {JobName(AutopilotJob.LevelUp)}); the job is {JobName(o.Job)}");
        }

        if (o.KillTasks.Count > 0 && (o.Job != AutopilotJob.LevelUp || o.Task.Kind != AutopilotTaskKind.Job))
        {
            // M6 14 guard task: a kill task is the level up job's own loop's (its hunt takes the place of her rung's ground)
            errors.Add($"--autopilot-ladder-quests: the kill task {string.Join(", ", o.KillTasks.Select(t => t.Key))} is the level up job's own loop's (--autopilot-job {JobName(AutopilotJob.LevelUp)}, --autopilot-task job); the job is {JobName(o.Job)}, the task {o.Task.Name}");
        }

        if (o.StartQuest is { } start && (o.Job != AutopilotJob.LevelUp || o.Task.Kind != AutopilotTaskKind.Job))
        {
            // M6 14: the start quest is the level up job's own loop's (the ladder goes on after it)
            errors.Add($"{StartQuestOption}: the start quest {start} is the level up job's own loop's (--autopilot-job {JobName(AutopilotJob.LevelUp)}, --autopilot-task job); the job is {JobName(o.Job)}, the task {o.Task.Name}");
        }

        foreach (var key in holdGrounds ?? [])
        {
            o.ApproachHolds.NoteAbandoned(key, $"held for the session by {HoldGroundsOption} (named at launch)"); // every way out reads it as a crossing given up, by name; no price releases it
        }

        return o with { Errors = errors };
    }

    /// <summary>M6 item 14 (the owner, 2026-11-12): the den run's order, off by default (<c>--autopilot-den-run</c>): the task <see cref="AutopilotTask.DenRun"/> inside the level up job.</summary>
    public const string DenRunOption = "--autopilot-den-run";

    /// <summary>M6 item 14: the den run's rehearsal, off by default (<c>--autopilot-den-run-rehearse</c>): the order with its road cut short of the Desert March Portal (<see cref="DenRunRehearse"/>).</summary>
    public const string DenRunRehearseOption = "--autopilot-den-run-rehearse";

    /// <summary>M6 16 Q8: the quest steps' own old kill loop, off by default (<c>--autopilot-quest-own-kill-loop</c>; <see cref="QuestOwnKillLoop"/>).</summary>
    public const string QuestOwnKillLoopOption = "--autopilot-quest-own-kill-loop";

    /// <summary>M6 item 14 (the owner, 2026-11-13): the trial's ground, off by default (<c>--autopilot-trial-ground</c>): a later rung's ground the level up job hunts for a while in place of its own rung (<see cref="Trial"/>).</summary>
    public const string TrialGroundOption = "--autopilot-trial-ground";

    /// <summary>
    /// M6 14 (live 2026-11-14 19:43: a death on the Tusker Armory's first visit, and the ladder at level 65 sends her back): grounds held for the session, named at launch (<c>--autopilot-hold-grounds=key[,key]</c>, default none). Each
    /// is noted in <see cref="ApproachHolds"/> as a crossing given up, so every way out holds it by name and the ladder hunts the rung before it or the rung's other ground.
    /// </summary>
    public const string HoldGroundsOption = "--autopilot-hold-grounds";

    /// <summary>M6 item 14: the trial's minutes of fight time from her first kill there (<c>--autopilot-trial-minutes</c>, default <see cref="LadderTrial.DefaultMinutes"/>).</summary>
    public const string TrialMinutesOption = "--autopilot-trial-minutes";

    private static double[]? ParseWaits(string? text)
    {
        var parts = (text ?? string.Empty).Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > MaxReconnectTries)
        {
            return null;
        }

        var waits = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var w) || !double.IsFinite(w) || w < 0 || w > MaxReconnectWaitSeconds)
            {
                return null;
            }

            waits[i] = w;
        }

        return waits;
    }

    /// <summary>The options as the run logs them, in invariant culture.</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"minutes={(Minutes > 0 ? Minutes.ToString("0.#", CultureInfo.InvariantCulture) : "off")} level={(Level > 0 ? Level.ToString(CultureInfo.InvariantCulture) : "off")} trip_after_kills={(TripAfterKills > 0 ? TripAfterKills.ToString(CultureInfo.InvariantCulture) : "off")} test_death={(TestDeath ? "on" : "off")}{(Job == AutopilotJob.Den ? string.Empty : " job=" + JobName(Job))}{(BuffBot ? string.Empty : " buff_bot=off")}{(Villa ? string.Empty : " villa=off")}{(VillaAccountAge ? " villa_account_age=ace" : string.Empty)}{(EverySpell ? string.Empty : " every_spell=off")}{(ReferenceEquals(BuffBotProfile, BuffBotWire.Default) ? string.Empty : " buff_bot_word=" + BuffBotProfile.Key)}{(!LadderQuests ? " ladder_quests=off" : QuestSelection is { } chosen ? " ladder_quests=" + chosen.Text : string.Empty)}{(Trial is { } trial ? " " + trial : string.Empty)}{(HourLoad == new Mage.CycleLoadSettings() ? string.Empty : " hour_load=" + (HourLoad.On ? string.Create(CultureInfo.InvariantCulture, $"{HourLoad.Minutes:0.#}m/{HourLoad.TopPercent}%") : "off"))}");

    /// <summary>The command-line name of <paramref name="job"/> (<see cref="JobNames"/>).</summary>
    /// <param name="job">A job.</param>
    public static string JobName(AutopilotJob job) => JobNames[(int)job];

    /// <summary>The watchdogs' and the reconnect's settings as the run logs them (after <see cref="ToString"/>), in invariant culture.</summary>
    public string Resilience => string.Create(
        CultureInfo.InvariantCulture,
        $"watchdog_scale={WatchdogScale:0.##} watchdog_recoveries={WatchdogRecoveries} reconnect_waits={string.Join(',', ReconnectWaits.Select(w => w.ToString("0.#", CultureInfo.InvariantCulture)))}");
}

/// <summary>The autopilot's jobs (<c>--autopilot-job</c>; M6 item 0 a4).</summary>
public enum AutopilotJob
{
    /// <summary>M5's den job: the ground is the journey's (<c>--fight-ground</c>), the character BSD Mage.</summary>
    Den,

    /// <summary>The <c>level up</c> job (M6 DoD 2-5, its first form): the ground is the ladder's rung for the character's level, the character the ladder's (<see cref="AutopilotOptions.LadderCharacter"/>).</summary>
    LevelUp,
}
