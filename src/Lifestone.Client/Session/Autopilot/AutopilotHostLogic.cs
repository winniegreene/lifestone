using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Net.Session;
using Lifestone.ServerTools;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// What a run carries across a hand-back: the loop restarts from the state it finds (a fresh scenario, whose station runs held the player's keys and walks), but the cycles it had counted, its tallies and the time it
/// ran are kept. The factory applies it with <see cref="Apply"/> (and may seed more: slice a2's checkpoint does).
/// </summary>
/// <param name="Cycles">Cycles completed before.</param>
/// <param name="Totals">The totals of the cycles completed.</param>
/// <param name="CycleTally">The cycle in progress (its kills so far, <c>interrupted=</c> and <c>resumed=</c>).</param>
/// <param name="ElapsedSeconds">The seconds the run has used over all its starts (<see cref="AutopilotScenario.SecondsUsed"/>: prior plus elapsed, M5 4 F3, 4-R5): the minutes rule counts them.</param>
/// <param name="ForcedTripDone">True when the forced trip (<c>--autopilot-trip-after-kills</c>, once a run) was taken before.</param>
/// <param name="TestDeathDone">True when the scripted death (<c>--autopilot-test-death</c>, once a run) was taken before.</param>
/// <param name="Seed">The abandoned scenario's <see cref="AutopilotScenario.Seed"/> (M5 4 F3, 4-R9): the cycle in progress, the cycles ended, the seconds used, a cut death's tally and a cut trip. The factory hands it to the fresh scenario.</param>
/// <param name="Task">M6 6 MB: the task the dial runs (<see cref="AutopilotHostLogic.Task"/>), or null for the options' own (<see cref="AutopilotOptions.Task"/>): a hand-back resumes the same task, and its due check prevents a second turn-in.</param>
/// <param name="HandOver">
/// M6 item 14: what the run that just ended proved (<see cref="AutopilotScenario.HandOver"/>), given by the move-on's restart alone (<c>stop=rung-moved:&lt;rung&gt;</c>): the factory passes it to the fresh scenario, which checks each part
/// against the world before it uses it. Null for every other start: a cold start, whose audit reads everything.
/// </param>
public sealed record AutopilotCarry(int Cycles, AutopilotTally Totals, AutopilotTally CycleTally, double ElapsedSeconds, bool ForcedTripDone = false, bool TestDeathDone = false, AutopilotSeed? Seed = null, AutopilotTask? Task = null, AutopilotHandOver? HandOver = null)
{
    /// <summary>The first run: nothing carried.</summary>
    public static AutopilotCarry None => new(0, new AutopilotTally(), new AutopilotTally(), 0);

    /// <summary>True when something was carried.</summary>
    public bool IsEmpty => Cycles == 0 && ElapsedSeconds <= 0 && CycleTally.Interrupted == 0 && CycleTally.Resumed == 0 && CycleTally.Kills == 0;

    /// <summary>Adds the carried totals and the cycle in progress to <paramref name="scenario"/> (before its first step).</summary>
    /// <param name="scenario">The fresh scenario.</param>
    public void Apply(AutopilotScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        scenario.Totals.Add(Totals);
        scenario.CycleTally.Add(CycleTally);
    }

    /// <summary>
    /// The options for the fresh scenario: the once-a-run forced trip and scripted death are off once taken. The minutes stay the run's <b>total</b> target when the carry has a <see cref="Seed"/> (the fresh scenario starts at the seed's
    /// <see cref="AutopilotSeed.SecondsUsed"/>, so a hand-back after a resume at 240 of 480 minutes still ends at 480 in all, and its checkpoint writes the real minutes used: M5 4 F3, 4-R5); a carry with no seed (a factory that
    /// cannot seed) gets only what is left of the minutes, as before. M6 6 MB: the task is the carry's when it names one, else the options' own.
    /// </summary>
    /// <param name="options">The run's options.</param>
    public AutopilotOptions Remaining(AutopilotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var minutes = options.Minutes > 0 && Seed is null ? Math.Max(1.0 / 60, options.Minutes - (ElapsedSeconds / 60)) : options.Minutes;
        return options with { Minutes = minutes, TripAfterKills = ForcedTripDone ? 0 : options.TripAfterKills, TestDeath = options.TestDeath && !TestDeathDone, Task = Task ?? options.Task };
    }
}

/// <summary>The host's answer to a move-on (<see cref="AutopilotHostLogic.MoveOn"/>): the next rung's run is ready, still being prepared (the host presses the dial itself when it is), or refused with the reason.</summary>
/// <param name="Ready">True when the next run can start now.</param>
/// <param name="Refusal">Why the next rung cannot run (a stop name and its reason), or null.</param>
public sealed record MoveOnAnswer(bool Ready, string? Refusal = null)
{
    /// <summary>The next run starts now.</summary>
    public static MoveOnAnswer Go { get; } = new(true);

    /// <summary>The parts are being read; the host presses the dial when they are.</summary>
    public static MoveOnAnswer Wait { get; } = new(false);

    /// <summary>The next rung cannot run.</summary>
    /// <param name="why">The stop name and its reason.</param>
    public static MoveOnAnswer Refused(string why) => new(false, why);
}

/// <summary>
/// The dial's logic (M5 item 4 h2, D20; spec DoD 9; the controls since M5 7 D145): owns the autopilot's scenario for a play session, engine-free. <b>/auto</b> (the chat's, through the host's press: <see cref="Toggle"/> from off, done,
/// stopped or halted) starts it from the state the loop finds, or hands the controls back: a fresh scenario reads the state it finds and goes on with what was carried (<see cref="AutopilotCarry"/>), <c>resumed=</c> counts. <b>/stop</b>
/// (<see cref="StopHere"/>) stops it where it stands: the run is dropped and the loop stops stepping, every queued automation action is cancelled and every key automation holds released, <c>interrupted=</c> counts. Both counts are in
/// the cycle and done lines (the scenario's tally fields). <b>The movement keys</b> (<see cref="MoveIntents.KeyLock"/>) are locked while it runs or stops and unlocked in every other mode, whatever ended the run (/stop, a stop rule, a
/// failure, the kill switch, a fault), with a chat line (<see cref="ChatNotice"/>) saying why: a stopped autopilot never leaves the person locked out. A person's movement key no longer takes the controls (D98's hand-off is retired:
/// while it runs the key does nothing). The <b>kill switch</b> halts it within one tick: the loop is not stepped (the Action API has already cancelled the automation actions it queued and released the keys it held), and nothing resumes
/// until the switch is off and /auto is typed. A clean stop at the next safe point (<see cref="Stop"/>, <see cref="AutopilotScenario.RequestStop"/>) is the stop file's, the window close's and the hosts'.
/// </summary>
/// <remarks>
/// <para>Why a fresh scenario on a hand-back: a station run that was in flight when the person took the keys (a fight's walk, a cast) is not resumable (a runner the person interrupted ends the leg, "the player's keys win"), so the loop
/// restarts at its known state, which is what the spec says ("resumes from the state the loop finds"). The kills of the fight that was running are not in the cycle (a fight adds its kills to the cycle when it ends).</para>
/// <para>Game thread only. A host ticks <see cref="Tick"/> once a frame after the session host, wires /auto to <see cref="Toggle"/> (through its press) and /stop to <see cref="StopHere"/>, shows <see cref="ChatNotice"/> in the chat, and
/// reads <see cref="Status"/> (the inspector's <see cref="IAutopilotStatusSource"/>).</para>
/// </remarks>
public sealed partial class AutopilotHostLogic : IAutopilotStatusSource, IDisposable
{
    /// <summary>The log category (the scenario's: <c>autopilot</c>).</summary>
    public const string LogCategory = AutopilotScenario.LogCategory;

    /// <summary>
    /// The host's start rule (M5: BSD Mage only; M6 item 0 a4, D7): the one character the job plays: <see cref="MageTemplate.CharacterName"/> in the den job, <see cref="AutopilotOptions.LadderCharacter"/> in the level up job, never the other
    /// (D25: the ladder's character runs alone, never beside BSD Mage's lane).
    /// </summary>
    /// <param name="options">The parsed options (their <see cref="AutopilotOptions.Job"/>).</param>
    public static string RequiredCharacter(AutopilotOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Job == AutopilotJob.LevelUp ? options.Climber : MageTemplate.CharacterName;
    }

    /// <summary>Why the dial does not start for <paramref name="character"/> under <paramref name="options"/> (the host's one refusal line), or null when the start rule allows it; a name not known yet is not refused (the press waits for it).</summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="character">The name of the character in the world, or null before it is known.</param>
    public static string? StartRefusal(AutopilotOptions options, string? character)
    {
        var required = RequiredCharacter(options);
        return string.IsNullOrEmpty(character) || string.Equals(character, required, StringComparison.Ordinal)
            ? null
            : $"the {AutopilotOptions.JobName(options.Job)} job plays {required} only; this character is {character}";
    }

    /// <summary>
    /// The journey plan the play host builds the job's parts on (M6 item 0 a6): <see cref="JourneyPlans.Climber"/> in the level up job (Winnie on the account test; its character is the session's login name and its checkpoint file is
    /// Winnie's own, <see cref="AutopilotHostOptions.CheckpointFileName"/>), <see cref="JourneyPlans.Mage"/> in the den job, unchanged.
    /// </summary>
    /// <param name="options">The parsed options (their <see cref="AutopilotOptions.Job"/>).</param>
    /// <param name="ground">The level up job's rung ground, or null: a dungeon rung makes the plan's ground the den (its rooms are read), as the mage plan's is.</param>
    public static JourneyPlan PlanFor(AutopilotOptions options, HuntingGround? ground = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Job != AutopilotJob.LevelUp)
        {
            return JourneyPlans.Mage;
        }

        var climber = JourneyPlans.ClimberFor(options.Climber);
        return ground is { Kind: GroundKind.Dungeon } ? climber with { Ground = StationWhere.Den } : climber;
    }

    /// <summary>The character's level (PropertyInt 25) in the read model, or 0 when the state has none.</summary>
    /// <param name="session">The session.</param>
    public static int LevelOf(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var level) ? level : 0;
    }

    /// <summary>
    /// M6 7 AW: the ground a fresh run is built on (the first press, a restart, a checkpoint's resume): <see cref="RungOwner"/>'s answer on what the state can tell before a run: the level, where she stands, the book. No pass has run, so the
    /// buffs are unknown, and unknown never opens a gate; what the state itself shows standing on her is read (M6 item 14, the remarks).
    /// </summary>
    /// <remarks>
    /// M6 item 14 (the start's rung; live, 2026-11-12 UTC, three relaunches of a level 29 climber who belongs on the Cave: each fresh process read every gate shut, built its first run on the outskirts, sold 72 items of
    /// the Cave's stock and bought 31, and moved on to the Cave at its buff stage, 337 s in). With <paramref name="options"/> (the level up job, the stop at the buff bot on) the start reads what the state holds
    /// before any run: her own enchantment registry, which arrives with the login. A gate line whose spell of the bot's stands in it with at least <see cref="StartStandsSeconds"/> left is read standing, in a learned
    /// line's place and as "up" (<see cref="GateStartRead"/>); one line names every gate line and what it stands by. The armour lines are on the worn pieces, whose spells the server sends only in an appraisal: no
    /// start can read them, so they are named and left to the run's way out (<c>LeavingHeld</c>), which reads the whole gate live before she leaves home. The pick is therefore the start's alone
    /// (<c>reason=start-pick:&lt;rung&gt;</c>, one more line): check before here, act (the run built on the rung's ground, at home), check after at the way out. A way out that reads the gate shut stops the run
    /// <c>rung-moved:&lt;lower rung&gt;</c> (ok) with no walk made, and the dial's move-on restarts once on that rung's ground: what the start cost before this, in the rare case and not at every relaunch.
    /// With no line of the bot's standing (they lapsed, she died) every gate reads shut as before, one line says so, and the run starts lower and moves on after its own stop at the bot. With the stop at the bot
    /// off, on the den's job, or at a level whose band has no gate, nothing is read and nothing is said: the start of before.
    /// </remarks>
    /// <param name="view">The session view.</param>
    /// <param name="why">The rung, or the stop's reason.</param>
    /// <param name="log">The log, or null: the choice's one line.</param>
    /// <param name="options">The run's options, or null: nothing standing is read (the book alone, buffs unknown).</param>
    public static HuntingGround? StartGround(IGameSessionView view, out string why, ILogSink? log = null, AutopilotOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        var read = options is { BuffBot: true, Job: AutopilotJob.LevelUp } && RungGateRule.Due(LadderLoader.WarMage, LevelOf(view)) ? StartRead(view, log, options.BuffBotProfile) : null; // a level with no gate in its band asks none: nothing to read
        return Asked(view, null, "start", out why, log, start: read, trial: options is { Job: AutopilotJob.LevelUp } ? options.Trial : null, killTask: options is { Job: AutopilotJob.LevelUp } ? options.KillTasks.FirstOrDefault(t => t.Steers) : null, holds: options?.ApproachHolds); // M6 item 14: a trial's rules are read at the start, its gate at the run's way out; M6 14 guard task: an armed kill task likewise
    }

    /// <summary>
    /// Lifestone's own: the seconds a spell of the bot's must have left for a start to read its line standing: half a minute, so the spell is on her when the read is used. It is not the field's line
    /// (<see cref="BuffBotRule.TripUnderSeconds"/>, 7 minutes): a relaunch is most often made at the bot's trip home, when every line is under that line, and the run the start builds stops at the bot before
    /// anything else at home and reads the whole gate live at its way out (<c>LeavingHeld</c>), so a line about to lapse is renewed before it is counted on.
    /// </summary>
    public const double StartStandsSeconds = 30;

    /// <summary>Why a start leaves an armour line's "up" to the run's way out (<see cref="GateStartRead.WayOutReads"/>).</summary>
    public const string ArmourLineUnread = "on the worn pieces: the server sends an item's spells only in an appraisal, which a run's pass makes";

    /// <summary>
    /// M6 item 14 (the start's rung): what stands on her now, read from the state before any run: for each line of the ladder's gates, the bot's spell of that line in her enchantment registry
    /// (<see cref="BuffBotRule.Read"/>, as the run's stop at the bot and its trip rule read it) with at least <see cref="StartStandsSeconds"/> left. One line names every gate line: what it stands by, why it does
    /// not stand, or that it is left to the way out. Null when the description has not arrived or no line stands: nothing is read, and every gate is asked as before.
    /// </summary>
    /// <param name="view">The session view.</param>
    /// <param name="log">The log, or null.</param>
    /// <param name="profile">The profile the run asks the bot for (<see cref="AutopilotOptions.BuffBotProfile"/>): its lines are the ones read. Every profile begins with the tuskers' lines, which the gates name.</param>
    private static GateStartRead? StartRead(IGameSessionView view, ILogSink? log, BuffBotProfile profile)
    {
        var qualities = view.State.Self.Qualities;
        if (!qualities.IsKnown)
        {
            log?.Warn(LogCategory, "ladder: start: her enchantments are not in the state yet (no player description has arrived): nothing standing is read, and the gates are asked on the book alone, buffs unknown");
            return null;
        }

        var now = view.Clock.Now;
        var reads = new List<BuffBotLineRead>();
        BuffBotRule.Read(profile, qualities, now, reads);
        var stands = new Dictionary<string, string>(StringComparer.Ordinal);
        var standing = new List<string>();
        var not = new List<string>();
        var left = new List<string>();
        foreach (var name in LadderLoader.WarMage.Gates.SelectMany(g => g.Lines).Select(l => l.Line).Distinct(StringComparer.Ordinal))
        {
            var bot = reads.FindIndex(r => string.Equals(r.Line.Line, name, StringComparison.Ordinal));
            if (bot < 0 && ArmourLine(name))
            {
                left.Add(name);
            }
            else if (bot < 0)
            {
                not.Add(name + $" (the bot's profile '{profile.Key}' casts no spell of this line)");
            }
            else if (reads[bot] is { Up: true, RemainingSeconds: >= StartStandsSeconds } up)
            {
                stands[name] = string.Create(CultureInfo.InvariantCulture, $"stands by the buff bot's spell {up.Line.SpellId} (level VII), {up.RemainingSeconds:0} s left"); // BuffBotWire: the bot casts each line's level VII spell (BuffReach.BotLevel)
                standing.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: spell {up.Line.SpellId}, {up.RemainingSeconds:0} s left"));
            }
            else
            {
                not.Add(string.Create(CultureInfo.InvariantCulture, $"{name} (spell {reads[bot].Line.SpellId}: {(reads[bot].Up ? $"{reads[bot].RemainingSeconds:0} s left, under {StartStandsSeconds:0}" : "not on her")})"));
            }
        }

        var leftText = left.Count == 0 ? "none" : $"{string.Join(", ", left)} ({ArmourLineUnread})";
        if (stands.Count == 0)
        {
            log?.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: start: no line of the gates stands on her by the buff bot's spell with at least {StartStandsSeconds:0} s left ({string.Join("; ", not)}): the gates are asked on the book alone, buffs unknown, as before; the run starts lower and moves on after its own stop at the bot"));
            return null;
        }

        log?.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: start: the gates are read with what stands on her now (her enchantments in the state, the buff bot's spells with at least {StartStandsSeconds:0} s left; no run has read anything yet): {string.Join("; ", standing)}; not standing: {(not.Count == 0 ? "none" : string.Join("; ", not))}; left to the way out's check: {leftText}"));
        return new GateStartRead(
            (line, level) => level <= BuffReach.BotLevel && stands.TryGetValue(line, out var text) ? text : null,
            line => ArmourLine(line) ? ArmourLineUnread : null);
    }

    /// <summary>True for a gate line cast on the worn pieces (the climber's profile says so): its spells are in no registry the server sends at a login.</summary>
    private static bool ArmourLine(string name) => RungGateRule.LineOf(BuffProfiles.Climber, name).Role == ProfileRole.ItemBuffArmor;

    /// <summary>
    /// M6 7 AW: the ground of a move-on restart: <see cref="RungOwner"/>'s answer with the rung the run's stop named, whose buffs that run read live. A stop that names no rung is asked as a start: buffs unknown, never the book alone
    /// (M6 7 lead, 2026-11-06, live: rung-moved:burrow restarted on the Lodge by her level band, past the Lodge's gate).
    /// </summary>
    /// <param name="view">The session view.</param>
    /// <param name="stop">The run's stop name, <c>rung-moved:&lt;rung&gt;</c>.</param>
    /// <param name="why">The rung, or the stop's reason.</param>
    /// <param name="log">The log, or null: the choice's one line.</param>
    /// <param name="handOver">
    /// M6 item 14 (live, 2026-11-12): the ended run's hand-over. The run opened the named rung's gate with lines that stand by the buff bot's spell (the gates' second form); the dial has no run to read a standing spell with, so on the
    /// book alone it read the gate shut and kept her on the ground the run had just left. The hand-over's read of those lines (<see cref="AutopilotHandOver.GateStands"/>) is used when it is this character's, this stop's and at most
    /// <see cref="MoveOnReadSeconds"/> old; else the book alone, as before.
    /// </param>
    /// <param name="trial">M6 item 14: the run's trial (<see cref="AutopilotOptions.Trial"/>), or null: while it steers, its ground is the next run's (the trial's rung change), and once it is over the ladder's own rung is.</param>
    /// <param name="killTasks">M6 14 guard task: the run's kill tasks (<see cref="AutopilotOptions.KillTasks"/>), or null: while one steers (its counter armed), its ground is the next run's.</param>
    /// <param name="holds">M6 14: the rungs held by their approach this process (<see cref="AutopilotOptions.ApproachHolds"/>), or null: none; a held rung is not taken ahead of its band.</param>
    public static HuntingGround? MoveOnGround(IGameSessionView view, string? stop, out string why, ILogSink? log = null, AutopilotHandOver? handOver = null, LadderTrial? trial = null, IReadOnlyList<LadderKillTask>? killTasks = null, ApproachHolds? holds = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        Func<string, int, string?>? stands = null;
        if (handOver is { GateStands.Count: > 0 })
        {
            var age = view.Clock.Now - handOver.EndedAt;
            var mine = handOver.CharacterGuid != 0 && handOver.CharacterGuid == view.State.Self.Guid && string.Equals(handOver.Stop, stop, StringComparison.Ordinal);
            if (mine && age >= 0 && age <= MoveOnReadSeconds)
            {
                stands = handOver.StandsAt;
                log?.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: move-on restart: the named rung's gate is read with the ended run's hand-over ({age:0.#} s old): {string.Join("; ", handOver.GateStands.Select(s => $"{s.Line} {s.Level}: {s.Read}"))}"));
            }
            else
            {
                log?.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: move-on restart: the hand-over's gate read is not used ({(mine ? $"{age:0.#} s old, over {MoveOnReadSeconds:0} s" : $"it is of {handOver.Stop}, character 0x{handOver.CharacterGuid:X8}")}): the named rung's gate is read on the book alone"));
            }
        }

        return Asked(view, RungOwner.NamedRung(LadderLoader.WarMage, stop), "move-on", out why, log, stands, trial: trial, killTask: killTasks?.FirstOrDefault(t => t.Steers), holds: holds);
    }

    /// <summary>Lifestone's own: the seconds a hand-over's read of the named rung's gate is used by the move-on (it is taken in the frame of the stop; a read older than this is no read of now).</summary>
    public const double MoveOnReadSeconds = 10;

    /// <summary>The stop name of a move-on that goes nowhere (<see cref="MoveOnGroundFor"/>, <see cref="MoveOnLoopSeconds"/>): the dial stops by it, loudly, and never restarts again.</summary>
    public const string MoveOnLoopStop = "failed:move-on-loop";

    /// <summary>Lifestone's own: a run a move-on restart started that stops with a move-on again inside this many seconds, with no cycle ended, is a loop (<see cref="MoveOnLoopStop"/>): nothing was played between the two.</summary>
    public const double MoveOnLoopSeconds = 1;

    /// <summary>
    /// M6 item 14 (live, 2026-11-12: 33 restarts in 15 s on the ground the run had just left): the ground of the move-on restart of the run that just ended, for the host's hook: <see cref="MoveOnGround"/> with that run's stop and its
    /// hand-over, then the check after: a move-on leaves the run's ground (<c>rung-moved</c> says "its ground is another"), so a choice that is that same ground would restart a run that stops the same way at once. Then null, and
    /// <paramref name="why"/> begins with <see cref="MoveOnLoopStop"/>: the host refuses the move-on with it, and the dial stops by that name.
    /// </summary>
    /// <param name="view">The session view.</param>
    /// <param name="why">The rung, or the reason no ground is given.</param>
    public HuntingGround? MoveOnGroundFor(IGameSessionView view, out string why)
    {
        var stop = _scenario?.StopName;
        var left = _handOver?.Ground;
        var ground = MoveOnGround(view, stop, out why, _log, _handOver, _options.Job == AutopilotJob.LevelUp ? _options.Trial : null, _options.Job == AutopilotJob.LevelUp ? _options.KillTasks : null, holds: _options.ApproachHolds);
        if (ground is null || left is null || !string.Equals(ground.Key, left, StringComparison.Ordinal))
        {
            return ground;
        }

        why = $"{MoveOnLoopStop}: the run on {left} stopped with {stop} to leave that ground, and the dial's own read keeps her on {left} ({why}): a run restarted there would stop the same way at once, so no restart is made";
        return null;
    }

    private static HuntingGround? Asked(IGameSessionView view, string? named, string path, out string why, ILogSink? log, Func<string, int, string?>? stands = null, GateStartRead? start = null, LadderTrial? trial = null, LadderKillTask? killTask = null, ApproachHolds? holds = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        var ask = new RungAsk(LevelOf(view), view.State.Self.Position?.CellId, view.State.Self.Qualities, AutopilotScenario.GateKnows(view), null, named) { StandsByAnother = stands, StartRead = start, Trial = trial, PlanWar = SpellPlan.PlanWarOf(view.XpTable), KillTask = killTask, AheadHeld = holds is null ? null : holds.IsHeld, VitaeHeld = holds is null ? null : holds.Vitae.HeldWhy }; // M6 sprint: the vitae burn's hold
        var choice = RungOwner.Decide(LadderLoader.WarMage, ask);
        trial?.Note(choice.Trial, ask.Level, path, log ?? NullLogSink.Instance); // M6 item 14: the trial's start or its refusal, by name
        if (start is not null && log is not null)
        {
            // M6 item 14: the start read lines standing, so each gate it asked is logged condition by condition, as a run's read is (AskRung): what shut a gate is named, and so is what was left unread
            foreach (var read in choice.Gates)
            {
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"ladder: gate {read.Gate.Rung}: level {ask.Level}: as the start reads it: {string.Join("; ", read.Conditions.Select(c => $"{c.Name}: {(c.Met ? "met" : "NOT MET")} ({c.Read})"))}; left to the way out's check: {(read.WayOutReads.Count == 0 ? "none" : string.Join(", ", read.WayOutReads))}"));
            }
        }

        log?.Info(LogCategory, choice.Line(path, ask));
        RungOwner.WarnLoud(choice, log, LogCategory); // M6 14 far below: a rung far below her level is never chosen silently (report-only)
        if (choice is { WayOutReads.Count: > 0, Ground: { } picked, Decision.Rung: { } rung })
        {
            log?.Info(LogCategory, $"ladder: start: rung {rung.Key} ({picked.Key}) is the start's pick by what stands on her now, not yet the whole gate: {string.Join(", ", choice.WayOutReads)} of its gate cannot be read before a run ({ArmourLineUnread}); check after: the run's way out reads the whole gate live before she leaves home, and shut there the run stops by the name of the rung the gates leave her (rung-moved, ok) with no walk made, and the dial restarts once on that rung's ground");
        }

        why = choice.Why;
        return choice.Ground;
    }

    /// <summary>The ground of a level by the band alone, for the band's own tests; no path chooses a rung by it (<see cref="RungOwner"/>). The den job's ground is the <c>--fight-ground</c> option's, never this.</summary>
    /// <param name="level">The character's level.</param>
    /// <param name="why">The rung, or the stop's reason.</param>
    internal static HuntingGround? LevelUpGround(int level, out string why) => RungOwner.BandGround(LadderLoader.WarMage, level, out why);

    /// <summary>
    /// True when a run that ended with <paramref name="stop"/> (<paramref name="ok"/>) is the level up job's move-on (<c>rung-moved:&lt;rung&gt;</c>, ok): the dial restarts once by itself on the next rung
    /// (<see cref="MoveOn"/>). The den job never moves on.
    /// </summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="stop">The run's stop name.</param>
    /// <param name="ok">Whether the stop is ok.</param>
    /// <param name="isStop">The stop-name rule (default: starts with <c>rung-moved:</c>).</param>
    public static bool IsMoveOn(AutopilotOptions options, string? stop, bool ok, Func<string?, bool>? isStop = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Job == AutopilotJob.LevelUp && ok && (isStop ?? IsMoveOnStop)(stop);
    }

    private static bool IsMoveOnStop(string? stop) => stop is not null && stop.StartsWith("rung-moved:", StringComparison.Ordinal);

    /// <summary>The start reason of a move-on restart that is unattended (<c>--autopilot-start</c>'s run stays unattended across rungs).</summary>
    public const string MoveOnReason = "rung-moved";

    /// <summary>
    /// The host's half of the move-on restart (M6 item 0 a6): called once when a run ends with <c>stop=rung-moved:&lt;rung&gt;</c>. It reads the new level and rung, builds the next rung's ground and parts, and answers: ready (the logic
    /// starts the next run at once), not ready and no refusal (the parts are being read, and the host presses the dial when they are), or a refusal naming why the next rung cannot run (the dial stops by that name, exit code 1 when unattended).
    /// </summary>
    public Func<IGameSessionView, MoveOnAnswer>? MoveOn { get; set; }

    /// <summary>The move-on restarts the logic made (a refused one is not counted).</summary>
    public int MoveOns { get; private set; }

    /// <summary>The refusal that ended the last move-on (the stop it named), or null.</summary>
    public string? MoveOnRefusal { get; private set; }

    /// <summary>The stop-name rule of a move-on (tests narrow it to reach the restart over a scripted run).</summary>
    internal Func<string?, bool> MoveOnStop { get; set; } = IsMoveOnStop;

    /// <summary>
    /// Sets the ground the stock counters read (the level up job knows its ground only once the character is in the world, so the host gives it at the first press and at each move-on); null shows none.
    /// </summary>
    /// <param name="ground">The ground.</param>
    public void UseGround(HuntingGround? ground) => _stockGround = ground is { Supplies.Stock.Count: > 0 } ? ground : null;

    /// <summary>Seconds between two <see cref="Changed"/> raises while running (the next trigger's counts move without an arrow).</summary>
    public const double StatusRefreshSeconds = 2;

    /// <summary>The stop request's token when the key or the button asked.</summary>
    public const string KeyReason = "key";

    /// <summary>M5 7 D145: the abandon token of a /stop (<see cref="StopHere"/>).</summary>
    public const string ChatStopReason = "chat-stop";


    /// <summary>M5 7 D145: the chat line of a /stop that stopped it.</summary>
    public const string StoppedHereLine = "Lifestone: the autopilot stopped where it stands; the controls are yours. Type /auto to hand them back.";

    /// <summary>M5 7 D145: the start of the log line of the check one frame after a stand-down (nothing held, nothing queued, the keys unlocked).</summary>
    public const string StandDownCheckLine = "autopilot host: the stand-down check";

    /// <summary>The stop request's token when the soak's stop file asked (<c>--autopilot-stop-file</c>, M5 7 F18): with <c>--autopilot-start</c> the run ends as an unattended end does (recall home, log off, quit).</summary>
    public const string StopFileReason = "stop-file";

    /// <summary>The stop request's token when the game window was closed during a run (M5 7 F18): the autopilot recalls home first, then the game quits (<see cref="WindowCloseGate"/>).</summary>
    public const string WindowCloseReason = "window-close";

    /// <summary>The log line when the person's own Reconnect moved the session's counter under a running dial (4-R8).</summary>
    public const string ReconnectedLine = "autopilot: the person reconnected: handing over";

    /// <summary>The log line when a hand-back waits for a held movement key (4-R16).</summary>
    public const string WaitingForKeyLine = "hand-back: waiting for the movement key to be released";

    /// <summary>
    /// M5 7 P7n (D145): the start reason of <c>/auto</c> (<see cref="Start"/>'s or <see cref="Toggle"/>'s <c>startedBy</c>): a person's resume, not an unattended run; the fresh scenario's audit is a resume
    /// (<see cref="StartupAudit.ResumeWhy"/>). A hand-back is a resume too.
    /// </summary>
    public const string ResumeReason = StartupAudit.ResumeWhy;

    /// <summary>The log line of an unattended start (<c>--autopilot-start</c>; M5 item 7 s1, <c>m5-autopilot-start</c>).</summary>
    public const string StartedByOptionLine = "autopilot: started by --autopilot-start";

    /// <summary>The log line when the dial closed the vitae page after a death (once a death; M5 item 7 s1, <c>m5-autopilot-vitae-panel</c>).</summary>
    public const string VitaeClosedLine = "autopilot: closed the vitae page after the death";

    /// <summary>
    /// How long after the death station ends the dial still looks for the vitae page to close, in seconds (Lifestone's own): retail opens it on the worse vitae (<c>RecvNotice_VitaeChanged</c>, 11.6096 0x004A7AA0), which can
    /// reach the client a moment after the station's last arrow.
    /// </summary>
    public const double VitaeGraceSeconds = 10;

    private readonly Func<AutopilotCarry, AutopilotScenario> _create;
    private readonly ILogSink _log;
    private readonly AutopilotOptions _options;
    private HuntingGround? _stockGround; // M6 16 E5d: the ground whose stock the next trigger reads with no run on: asked of the session's trip desk, never counters of the panel's own
    private readonly List<string> _lines = [];
    private AutopilotScenario? _scenario;
    private MoveIntents? _moves;
    private IGameSessionView? _session;
    private AutopilotMode _mode = AutopilotMode.Off;
    private string _decision = "not started";
    private int _reconnectsBase;
    private bool _handBackWaiting;
    private string? _handBackWhy;
    private AutopilotSeed? _abandonedSeed;
    private double _lastRaise = double.NegativeInfinity;
    private bool _forcedTripDone;
    private bool _testDeathDone;
    private string? _stopAsked;
    private int _linesTaken;
    private bool _unattended;
    private AutopilotUnattendedEnd? _unattendedEnd;
    private double _vitaeWatchUntil = double.NegativeInfinity;
    private bool _standDownCheck;
    private string? _quietUnlock;
    private AutopilotTask _task;
    private AutopilotHandOver? _handOver; // M6 item 14: the ended run's hand-over, taken at a move-on and given to the one start that follows it
    private bool _moveOnRun; // the run in hand was started by a move-on's restart (the loop guard reads it)
    private (string Ground, string Stop)? _lastMoveOn; // the ground and the stop of the last move-on (the loop guard: the same again after a restart is no progress)

    /// <summary>A test's change to the hand-over a move-on takes (a dial that cannot read what the run read); null: none.</summary>
    internal Func<AutopilotHandOver, AutopilotHandOver>? HandOverForTest { get; set; }

    /// <summary>A dial over scenarios made by <paramref name="create"/>.</summary>
    /// <param name="create">Makes a scenario for a start or a hand-back; it applies the carry (<see cref="AutopilotCarry.Apply"/>), and may give its options the carry's remaining minutes.</param>
    /// <param name="log">The log.</param>
    /// <param name="options">The run's options (the stop rules and the forced trip the next trigger reads).</param>
    /// <param name="ground">The ground, for the stock lines the next trigger reads; null shows none.</param>
    public AutopilotHostLogic(Func<AutopilotCarry, AutopilotScenario> create, ILogSink log, AutopilotOptions options, HuntingGround? ground = null)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _stockGround = ground is { Supplies.Stock.Count: > 0 } ? ground : null;
        _task = options.Task;
        if (_task.Kind != AutopilotTaskKind.Job)
        {
            _log.Info(LogCategory, $"autopilot task: {_task.Name} chosen by {TaskByOption}");
        }
    }

    /// <summary>M6 6 MB: who chose a task, for the <c>autopilot task: &lt;name&gt; chosen by &lt;who&gt;</c> line: the menu.</summary>
    public const string TaskByMenu = "menu";

    /// <summary>M6 6 MB: the chat's <c>/auto &lt;task&gt;</c>.</summary>
    public const string TaskByChat = "chat";

    /// <summary>M6 6 MB: the <c>--autopilot-task</c> option.</summary>
    public const string TaskByOption = "option";

    /// <summary>
    /// M6 6 MB: the task the dial runs inside the install job (the options' <see cref="AutopilotOptions.Task"/> until <see cref="UseTask"/> changes it). Every start and hand-back carries it (<see cref="AutopilotCarry.Task"/>), so a
    /// hand-back resumes the same task.
    /// </summary>
    public AutopilotTask Task => _task;

    /// <summary>
    /// M6 6 MB: chooses the task the next start or hand-back runs. Refused while the dial runs or stops (the run in hand is not changed under it: /stop first), and for a quest key in no quest file; allowed while off, done, interrupted or
    /// halted. Logged <c>autopilot task: &lt;name&gt; chosen by &lt;who&gt;</c>.
    /// </summary>
    /// <param name="task">The task.</param>
    /// <param name="by">Who chose it (<see cref="TaskByMenu"/>, <see cref="TaskByChat"/>, <see cref="TaskByOption"/>).</param>
    /// <returns>Null when chosen, else the refusal in plain words.</returns>
    public string? UseTask(AutopilotTask task, string by = TaskByMenu)
    {
        string? refusal = null;
        if (_mode is AutopilotMode.Running or AutopilotMode.Stopping)
        {
            refusal = $"the autopilot is {_mode.ToString().ToLowerInvariant()}: type /stop first, then choose";
        }
        else if (task.Kind == AutopilotTaskKind.Quest && !Quests.QuestCatalog.All.Any(q => q.Key == task.QuestKey))
        {
            refusal = $"no quest file names {task.QuestKey}";
        }
        else
        {
            refusal = QuestRefusal(task);
        }

        if (refusal is not null)
        {
            _log.Info(LogCategory, $"autopilot task: {task.Name} refused (by {by}): {refusal}; the task stays {_task.Name}");
            return refusal;
        }

        _task = task;
        _decision = $"task {task.Name} chosen by {by}";
        _log.Info(LogCategory, $"autopilot task: {task.Name} chosen by {by}");
        Raise();
        return null;
    }

    /// <summary>
    /// M6 6 FXM2 (6-M9): where the host gives the menu's input (the level, the timers, the ladder's quest keys) for a judgement of a quest task by <see cref="JobMenu"/>'s own rule; null (the default) judges nothing here and the
    /// scenario's checks stand. It returns null when the character's state is not read yet.
    /// </summary>
    public Func<JobMenuInput?>? MenuInput { get; set; }

    /// <summary>
    /// M6 6 FXM2 (6-M9): a quest task refused by the menu's rule (<c>ladder-quest</c>, <c>no-level</c>, <c>level-out-of-band</c>, <c>done-once</c>), one rule for the menu, the chat and the option; <c>not-due</c> is not refused here (the
    /// scenario stops <c>task-done:quest:&lt;key&gt;:not-due</c>, ok). Null when the task is not a quest, nothing is given to judge by, or the quest is allowed.
    /// </summary>
    private string? QuestRefusal(AutopilotTask task)
    {
        if (task.Kind != AutopilotTaskKind.Quest || MenuInput?.Invoke() is not { } input)
        {
            return null;
        }

        var quest = input.Quests.FirstOrDefault(q => q.Key == task.QuestKey);
        if (quest is null || JobMenu.Judge(input, quest).Refusal is not { } why || why.StartsWith("not-due", StringComparison.Ordinal))
        {
            return null;
        }

        return why;
    }

    /// <summary>
    /// M6 6 MB: the task as the inspector prints it (<see cref="AutopilotStatus.Job"/>): <c>level up (rung &lt;r&gt;)</c> (or <c>level up</c> before the rung is read) and <c>hunt here</c> for the job's own loop on a level up or den
    /// install, <c>do a quest: &lt;key&gt;</c>, <c>errand</c>.
    /// </summary>
    public string JobText => _task.Kind switch
    {
        AutopilotTaskKind.Quest => "do a quest: " + _task.QuestKey,
        AutopilotTaskKind.Errand => "errand",
        _ when _options.Job == AutopilotJob.LevelUp => _scenario?.Rung is { } rung ? $"level up (rung {rung.Key})" : "level up",
        _ => "hunt here",
    };

    /// <inheritdoc/>
    public event Action? Changed;

    /// <summary>
    /// M5 7 P7n (D145): the line for the chat window, in plain words: every scenario's <see cref="AutopilotScenario.ChatNotice"/> (the audit's summary at each start, its notices, the stop with its reason) and the halt by the kill switch.
    /// The Godot host wires it to the chat window (P7m).
    /// </summary>
    public event Action<string>? ChatNotice;

    /// <summary>
    /// M5 7 P7n (D145): raised once when the run stops by itself, with the reason (the scenario's <see cref="AutopilotScenario.StopName"/>: a stop rule, a stop request, a failure, a give-up), or when the kill switch halts it
    /// (<see cref="HaltReason"/>): the dial is no longer running, so the host unlocks the movement keys (a stopped bot never locks the person out). Not raised by an interrupt (the person took the keys).
    /// </summary>
    public event Action<string>? Stopped;

    /// <summary>M5 7 P7n: the reason <see cref="Stopped"/> carries when the kill switch halts the loop.</summary>
    public const string HaltReason = "halt:kill-switch";

    /// <summary>Where the dial is.</summary>
    public AutopilotMode Mode => _mode;

    /// <summary>True while the current run was started by <c>--autopilot-start</c> (a start by the key or the button clears it).</summary>
    public bool StartedByOption => _unattended;

    /// <summary>
    /// What the host does at the end of an unattended run (M5 item 7 s1): set once, when a run started by <c>--autopilot-start</c> reaches Done by one of its own stops (a stop rule, a failure) and not by a person's request. Null while the run goes on,
    /// after a request (the game stays open) and for runs the key started.
    /// </summary>
    public AutopilotUnattendedEnd? UnattendedEnd => _unattendedEnd;

    /// <summary>
    /// Closes the vitae page if it shows and says whether it did (M5 item 7 s1, <c>m5-autopilot-vitae-panel</c>): the play host wires the shell's page host (panel 15). Retail opens the page itself on a worse vitae (<c>RecvNotice_VitaeChanged</c>,
    /// 11.6096 0x004A7AA0, kept in <c>GameVitaePage</c>); the dial closes it as a person would once the death station has ended. Null (the default): nothing to close.
    /// </summary>
    public Func<bool>? CloseVitaePage { get; set; }

    /// <summary>How many times the dial closed the vitae page this run.</summary>
    public int VitaeCloses { get; private set; }

    /// <summary>
    /// The reconnects the session made that were not the autopilot's own (the person's Reconnect button: <c>GameSession.Reconnects</c> less the autopilot's tries; M5 4 F3, 4-R8). The host sets it; null (the default) turns the
    /// check off. When the number moves under a running dial the dial yields: an interrupt, logged <see cref="ReconnectedLine"/>, instead of starting a try of its own against the person's login.
    /// </summary>
    public Func<int>? ForeignReconnects { get; set; }

    /// <summary>
    /// True while a person holds a movement key (M5 4 F3, 4-R16; the host reads its input): a hand-back is then deferred until the key is up, logged once (<see cref="WaitingForKeyLine"/>). Null (the default): never held.
    /// </summary>
    public Func<bool>? HumanKeyHeld { get; set; }

    /// <summary>M5 7 D145: what asked for the last start or hand-back (<see cref="ResumeReason"/> for /auto, the option for <c>--autopilot-start</c>, else <see cref="KeyReason"/>), or null before one.</summary>
    public string? StartReason { get; private set; }

    /// <summary>M5 7 D145: true while the person's movement keys are locked (the autopilot has the controls).</summary>
    public bool KeysLocked => _moves?.KeyLock.Locked ?? false;

    /// <summary>M5 7 D145: the run's kills so far: the cycles ended, the cycle in progress and the fight in progress (the drill waits for the next kill on it).</summary>
    public int RunKills => _scenario is { } s ? s.Totals.Kills + s.CycleTally.Kills + FightKills() : 0;

    /// <summary>The cycle in which the scripted death was entered (0 until it was): D58's scripted interrupt counts from the first fight after it (4-R10).</summary>
    public int TestDeathCycle { get; private set; }

    /// <summary>True when the run scripts a death (<c>--autopilot-test-death</c>).</summary>
    public bool TestDeathScripted => _options.TestDeath;

    /// <summary>True while a hand-back waits for the person's movement key to be released.</summary>
    public bool HandBackWaiting => _handBackWaiting;

    /// <summary>The current scenario (null before the first start); it is replaced by a hand-back.</summary>
    public AutopilotScenario? Scenario => _scenario;

    /// <summary>How many times a person took the controls this run.</summary>
    public int Interrupted { get; private set; }

    /// <summary>How many times the controls were handed back this run.</summary>
    public int Resumed { get; private set; }

    /// <summary>How many times the kill switch halted the loop this run.</summary>
    public int Halts { get; private set; }

    /// <summary>The cycle, stop and done lines of this run, in order, across hand-backs (the lines of dropped scenarios and the current one's).</summary>
    public IReadOnlyList<string> Lines
    {
        get
        {
            if (_scenario is { } s)
            {
                for (var i = _linesTaken; i < s.Lines.Count; i++)
                {
                    _lines.Add(s.Lines[i]);
                }

                _linesTaken = s.Lines.Count;
            }

            return _lines;
        }
    }

    /// <inheritdoc/>
    public AutopilotStatus Status
    {
        get
        {
            var state = _scenario is { } s && _mode is not AutopilotMode.Off ? s.Stage : string.Empty;
            return new AutopilotStatus(_mode, state, _decision, _mode is AutopilotMode.Running or AutopilotMode.Stopping ? NextTrigger() : string.Empty, _scenario?.Cycle ?? 0, Interrupted, Resumed, JobText);
        }
    }

    /// <summary>
    /// The host's press (/auto through it, the unattended start): off or done starts a run, running asks it to stop, interrupted or halted hands the controls back. A press while it is stopping does nothing but a log line.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="startedBy">When a start is not a person's press: the option that asked for it (<c>--autopilot-start</c>); a stop or a hand-back ignores it.</param>
    /// <param name="reason">M5 7 D145: what asked (<see cref="ResumeReason"/> for /auto), kept as <see cref="StartReason"/> for a start or a hand-back; null: the option's name, else <see cref="KeyReason"/>.</param>
    public void Toggle(IGameSessionView session, string? startedBy = null, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        Attach(session);
        if (_mode is not (AutopilotMode.Running or AutopilotMode.Stopping))
        {
            StartReason = reason ?? startedBy ?? KeyReason;
            _log.Info(LogCategory, "autopilot host: a start or hand-back is asked (reason " + StartReason + ", mode " + _mode.ToString().ToLowerInvariant() + ")");
        }

        switch (_mode)
        {
            case AutopilotMode.Off or AutopilotMode.Done:
                Start(session, startedBy ?? (reason == ResumeReason ? ResumeReason : null)); // /auto from off or done: a resume (P7n's audit says why=resume)
                break;
            case AutopilotMode.Running:
                Stop(KeyReason);
                break;
            case AutopilotMode.Interrupted or AutopilotMode.Halted:
                HandBack(session);
                break;
            default:
                _log.Info(LogCategory, "autopilot host: a stop is already asked; the loop ends at its next safe point");
                break;
        }
    }

    /// <summary>Starts a run from the state the loop finds. Does nothing unless the dial is off or done.</summary>
    /// <param name="session">The session.</param>
    /// <param name="startedBy">The option that asked for the start (<c>--autopilot-start</c>): the run is then unattended (<see cref="UnattendedEnd"/>) and the start is logged; null for a person's press.</param>
    /// <returns>True when a run started.</returns>
    public bool Start(IGameSessionView session, string? startedBy = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        Attach(session);
        if (_mode is not (AutopilotMode.Off or AutopilotMode.Done))
        {
            return false;
        }

        if (QuestRefusal(_task) is { } refused)
        {
            // M6 6 FXM2 (6-M9): the option's task (--autopilot-task) is judged by the menu's rule at the start, as the chat's is at UseTask
            _decision = $"the task {_task.Name} is refused: {refused}";
            _log.Warn(LogCategory, $"autopilot task: {_task.Name} refused at the start: {refused}; nothing started");
            return false;
        }

        (Interrupted, Resumed, Halts, _forcedTripDone, _testDeathDone, _stopAsked, TestDeathCycle) = (0, 0, 0, false, false, null, 0);
        (_handBackWaiting, _handBackWhy, _abandonedSeed) = (false, null, null);
        _leaving = false;
        _lines.Clear();
        _linesTaken = 0;
        var resume = startedBy == ResumeReason; // M5 7 P7n (D145): /auto is a person's resume, never an unattended run
        (_unattended, _unattendedEnd, VitaeCloses, _vitaeWatchUntil) = (startedBy is not null && !resume, null, 0, double.NegativeInfinity);
        var handOver = _handOver; // M6 item 14: a move-on's restart is no cold start: the ended run's proofs go with it, once
        _handOver = null;
        _moveOnRun = handOver is not null;
        if (!_moveOnRun)
        {
            _lastMoveOn = null; // a start that is no move-on's restart begins a new count
        }
        if (handOver is not null)
        {
            _log.Info(LogCategory, "autopilot host: the start carries the hand-over of the run that just ended (" + handOver.Stop + " on " + handOver.Ground + "): the next run checks each part before it uses it");
        }

        Install(_create(AutopilotCarry.None with { Task = _task, HandOver = handOver }), resume ? ResumeReason : "start"); // M6 6 MB: the chosen task
        LogTaskStart("start");
        _mode = AutopilotMode.Running;
        _decision = startedBy is null ? "started: the loop reads the state it finds" : "started by " + startedBy + ": the loop reads the state it finds";
        _log.Info(LogCategory, "autopilot host: started (" + _options + ")" + (resume ? " by " + ResumeReason + ": the audit reads every state a fight needs" : string.Empty));
        if (startedBy is not null && !resume)
        {
            _log.Info(LogCategory, "autopilot: started by " + startedBy);
        }

        Raise();
        return true;
    }

    /// <summary>
    /// Asks the run to stop (a clean stop at its next safe point; <c>stop=request:&lt;why&gt;</c>). While interrupted the controls are handed back first and the stop asked of the fresh loop, so its lines are written; while halted by
    /// the switch the run ends at once (nothing can be sent), with a log line and no done line.
    /// </summary>
    /// <param name="why">Who asked (a token).</param>
    public void Stop(string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        switch (_mode)
        {
            case AutopilotMode.Running:
                _scenario!.RequestStop(why);
                _stopAsked = why;
                _mode = AutopilotMode.Stopping;
                _decision = "stop asked (" + why + "): the loop ends at its next safe point";
                Raise();
                break;
            case AutopilotMode.Interrupted when _session is { } session:
                HandBack(session, why);
                break;
            case AutopilotMode.Halted:
                _mode = AutopilotMode.Done;
                _decision = "stopped while halted by the kill switch: nothing was running";
                _log.Warn(LogCategory, "autopilot host: stopped while the kill switch halted it; no station was running, so no done line is written");
                if (_unattended && why == StopFileReason)
                {
                    // M5 7 G2: the owner's stop file ends an unattended run even when the switch halted it: nothing more can be sent, so the game logs off (tried) and quits, exit code 1 (no done line).
                    _unattendedEnd = new AutopilotUnattendedEnd(1, "autopilot: the stop file ended a run halted by the kill switch; the game logs off and quits with exit code 1");
                    _log.Info(LogCategory, _unattendedEnd.Decision);
                }

                Raise();
                break;
        }
    }

    /// <summary>
    /// Hands the controls back: a fresh scenario reads the state it finds and goes on with the carried counts, <c>resumed=</c> +1. Refused while the kill switch is on.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="stopWhy">When set, the fresh loop is asked to stop at once (a stop while interrupted).</param>
    /// <returns>True when the loop resumed.</returns>
    public bool HandBack(IGameSessionView session, string? stopWhy = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        Attach(session);
        if (_mode is not (AutopilotMode.Interrupted or AutopilotMode.Halted))
        {
            return false;
        }

        if (session.Actions.KillSwitch)
        {
            _decision = "the kill switch is on: turn it off, then hand back";
            _log.Warn(LogCategory, "autopilot host: a hand-back was asked but the kill switch is on; it stays " + _mode.ToString().ToLowerInvariant());
            Raise();
            return false;
        }

        if (HumanKeyHeld?.Invoke() == true)
        {
            // 4-R16: the fresh run's walk would start under the person's held key. Wait for the release (Tick looks each frame), say so once.
            _handBackWhy = stopWhy ?? _handBackWhy;
            if (!_handBackWaiting)
            {
                _handBackWaiting = true;
                _decision = "a hand-back is waiting for the movement key to be released";
                _log.Info(LogCategory, WaitingForKeyLine);
                Raise();
            }

            return false;
        }

        _handBackWaiting = false;
        stopWhy ??= _handBackWhy;
        _handBackWhy = null;
        var afterPerson = _mode == AutopilotMode.Interrupted;
        var old = _scenario!;
        if (afterPerson)
        {
            old.CycleTally.Resumed++;
        }

        Capture(old);
        var carry = new AutopilotCarry(old.Cycles, old.Totals, old.CycleTally, _abandonedSeed?.SecondsUsed ?? old.SecondsUsed, _forcedTripDone, _testDeathDone, _abandonedSeed ?? old.Seed, _task);
        _abandonedSeed = null;
        Install(_create(carry), ResumeReason); // M5 7 P7n (D145): a hand-back is a resume: the fresh scenario's audit reads every state a fight needs
        LogTaskStart("hand-back"); // M6 6 MB: the same task (its due check prevents a second turn-in)
        if (afterPerson)
        {
            Resumed++;
        }

        _mode = AutopilotMode.Running;
        _decision = (afterPerson ? "handed back (" + (StartReason ?? KeyReason) + ")" : "restarted after the kill switch") + ": the loop reads the state it finds (cycle " + _scenario!.Cycle + ")";
        _log.Info(LogCategory, $"autopilot host: {_decision}; interrupted={Interrupted} resumed={Resumed}");
        if ((stopWhy ?? _stopAsked) is { } asked)
        {
            Stop(asked); // a stop asked before the person took the controls still stands
        }

        Raise();
        return true;
    }

    /// <summary>
    /// M5 7 D145, <b>/stop</b>: while it runs or stops, it stops where it stands. No recall and no log off: the run in flight is dropped (its fight's kills folded in, 4-R9; no done line, the run is not over), every queued automation action is
    /// cancelled, every key and hold automation set is released, a swing in progress is called off, and the movement keys are unlocked; <c>interrupted=</c> counts and /auto hands the controls back. In any other mode it does nothing.
    /// Returns the chat line either way.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <param name="via">Who asked, for the log (<c>/stop</c>, the panel button, the drill).</param>
    public string StopHere(IGameSessionView session, string via = "/stop")
    {
        ArgumentNullException.ThrowIfNull(session);
        Attach(session);
        PersonStopped(); // in any mode: no order may start or resume the dial until a person's own press
        if (_mode is not (AutopilotMode.Running or AutopilotMode.Stopping) || _scenario is not { } scenario)
        {
            _log.Info(LogCategory, $"autopilot host: {via} while {_mode.ToString().ToLowerInvariant()}: nothing to stop; nothing else is done");
            return "Lifestone: the autopilot is not running (" + _mode.ToString().ToLowerInvariant() + "); nothing to stop.";
        }

        _quietUnlock = via; // the command's own answer says it; no second chat line
        Interrupt(scenario, ChatStopReason, via);
        _quietUnlock = null;
        _log.Info(LogCategory, "autopilot chat: " + StoppedHereLine);
        return StoppedHereLine;
    }

    /// <summary>The person takes the controls (/stop, or their own Reconnect): the run abandoned with its fight folded in (4-R9), no done line; then the stand-down (<see cref="StandDown"/>).</summary>
    private void Interrupt(AutopilotScenario scenario, string abandonToken, string who)
    {
        scenario.CycleTally.Interrupted++;
        Interrupted++;
        Drop(scenario, abandonToken); // the run in flight is dropped (its fight stops casting and walking); no done line: the run is not over
        var (cancelled, released, attack) = StandDown(abandonToken);
        _mode = AutopilotMode.Interrupted;
        _decision = $"stopped by {who}: the controls are the person's (/auto hands them back)";
        _log.Info(LogCategory, $"autopilot host: stopped by {who} where it stands (no recall, no log off); cancelled {cancelled} queued, released {released} held{(attack ? ", called off the swing" : string.Empty)}; interrupted={Interrupted} resumed={Resumed}");
        Raise();
    }

    /// <summary>
    /// M5 7 D145: the autopilot lets go of everything (a /stop, a halt, any stop it made): the queued automation actions cancelled, then every command, hold and autorun automation holds released (that order: no queued intent presses a key
    /// after the release), and a swing in progress called off (ACE repeats a swing until it is cancelled). The keys' lock follows the mode in <see cref="Raise"/>; the check one frame later reads that nothing is held.
    /// </summary>
    private (int Cancelled, int Released, bool Attack) StandDown(string why)
    {
        if (_session is not { } session)
        {
            return (0, 0, false);
        }

        var (cancelled, released) = session.Actions.Move.StandDownAutomation("the autopilot stood down (" + why + ")");
        var attack = false;
        if (session.Attack.Active && !session.Actions.KillSwitch)
        {
            _ = session.Actions.CancelAttack(ActionSource.Host);
            attack = true;
        }

        _standDownCheck = true;
        return (cancelled, released, attack);
    }

    /// <summary>
    /// M5 7 D145: the check one frame after a stand-down: nothing automation holds on the controller and the keys unlocked; anything left is released again and named. The automation actions queued now are reported, not cancelled: one frame
    /// later they can be another driver's (the re-buff driver goes on once the autopilot stops owning the buffs), which are not the autopilot's to withdraw.
    /// </summary>
    private void CheckStandDown(IGameSessionView session)
    {
        _standDownCheck = false;
        var move = session.Actions.Move;
        var (held, queued, locked) = (move.AutomationHeld, move.QueuedAutomation, move.KeyLock.Locked);
        if (held == 0 && !locked)
        {
            _log.Info(LogCategory, $"{StandDownCheckLine}: ok (held=0 keys=unlocked queued_now={queued} person_held={move.KeyLock.HeldByPerson}; mode {_mode.ToString().ToLowerInvariant()})");
            return;
        }

        var released = move.ReleaseAutomation();
        if (locked)
        {
            move.KeyLock.Unlock("the stand-down check found the keys locked");
        }

        _log.Warn(LogCategory, $"{StandDownCheckLine}: NOT clean (held={held} keys={(locked ? "locked" : "unlocked")} queued_now={queued}); released {released} again, keys unlocked");
    }

    /// <summary>Abandons <paramref name="scenario"/> (the fight's kills folded into the cycle, a cut death's or trip's state kept, the checkpoint written), keeps its lines and seed, and releases the run.</summary>
    private void Drop(AutopilotScenario scenario, string why)
    {
        scenario.Abandon(why);
        _abandonedSeed = scenario.Seed;
        Capture(scenario);
        scenario.Dispose();
    }

    /// <summary>One frame: halts at once when the kill switch is on, else steps the loop. Allocation-free while running unless a status is read.</summary>
    /// <param name="session">The session.</param>
    /// <param name="now">The host's clock, passed to the scenario.</param>
    public void Tick(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        Attach(session);
        if (_standDownCheck && _mode is not (AutopilotMode.Running or AutopilotMode.Stopping))
        {
            CheckStandDown(session);
        }

        if (_handBackWaiting && _mode is AutopilotMode.Interrupted or AutopilotMode.Halted)
        {
            HandBack(session); // 4-R16: the movement key is up now, or it waits another frame
            return;
        }

        if (_mode is not (AutopilotMode.Running or AutopilotMode.Stopping) || _scenario is not { } scenario)
        {
            return;
        }

        if (session.Actions.KillSwitch)
        {
            Halt(session, scenario);
            return;
        }

        if (ForeignReconnects is { } foreign && foreign() != _reconnectsBase)
        {
            // 4-R8: the person pressed the disconnected screen's Reconnect (the autopilot's own tries do not move this number). Two logins would tear each other down: the dial yields.
            _log.Warn(LogCategory, ReconnectedLine);
            Interrupt(scenario, "person-reconnect", "the person's reconnect");
            return;
        }

        var clock = session.Clock.Now;
        var status = scenario.Step(session, now);
        if (clock < _vitaeWatchUntil)
        {
            TryCloseVitae();
        }

        if (status == ScenarioStatus.Running && SeatIfReady(session, scenario))
        {
            return;
        }

        if (status != ScenarioStatus.Running)
        {
            Capture(scenario);
            var (cancelled, released, _) = StandDown("done"); // D145: a stop it made on its own lets go the same way as /stop
            _mode = AutopilotMode.Done;
            _decision = scenario.Ok ? $"finished: stop={scenario.StopName}" : $"failed: {scenario.Failure}";
            _log.Info(LogCategory, $"autopilot host: the run ended by itself ({_decision}); cancelled {cancelled} queued, released {released} held");
            if (IsMoveOn(_options, scenario.StopName, scenario.Ok, MoveOnStop))
            {
                if (_stopAsked is { } asked)
                {
                    // M6 item 14: a pending stop starts nothing new: the move-on's restart is not made under a stop that was asked (the scenario stops by the request itself when it sees one; this is the dial's own check)
                    _log.Warn(LogCategory, $"ladder: move-on restart: NOT made after {scenario.StopName}: a stop was asked ({asked}) and a pending stop starts no new run; the next start reads the rung again");
                }
                else if (MoveOnRestart(session, scenario))
                {
                    Raise();
                    return;
                }
            }

            if (!_leaving && AfterRun?.Invoke(scenario) == true)
            {
                _log.Info(LogCategory, $"autopilot host: the run's end (stop={scenario.StopName ?? "none"}) is claimed by the steer desk");
                Raise();
                return;
            }

            DecideUnattendedEnd(scenario);
            _log.Info(LogCategory, $"autopilot host: the run stopped by itself (stop={scenario.StopName ?? "none"} ok={(scenario.Ok ? "yes" : "no")}): Stopped is raised (the keys unlock)");
            Stopped?.Invoke(scenario.StopName ?? "stopped");
            Raise();
        }
        else if (clock - _lastRaise >= StatusRefreshSeconds)
        {
            Raise();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_moves is not null)
        {
            // D145: a disposed dial (a host fault, the scene's exit) never leaves the person locked out.
            _moves.KeyLock.Notice -= OnKeyNotice;
            if (_moves.KeyLock.Locked)
            {
                if (_session is not null)
                {
                    _ = StandDown("disposed");
                }

                _moves.KeyLock.Unlock("the autopilot was disposed (" + _mode.ToString().ToLowerInvariant() + ")");
            }

            _moves = null;
        }

        _scenario?.Dispose();
    }

    /// <summary>
    /// The level up job's move-on (M6 item 0 a6; check, act, check, M5 D141): the run ended with <c>stop=rung-moved:&lt;rung&gt;</c> (ok). Check: the stop is logged. Act: the host's <see cref="MoveOn"/> reads the new level and rung and
    /// prepares the next rung's ground and parts; when they are ready the dial starts the next run once, by itself (still unattended when it was), else the host presses it when its parts are read. Check: the next run's first cycle confirms
    /// the rung (the scenario stops <c>failed:rung-ground-mismatch</c> if its ground is not the rung's). A refusal, or a host with no hook, stops by name and is never a silent halt. Always true for a move-on (the stop is handled: no unattended
    /// end of the move-on itself, no <see cref="Stopped"/> unless refused). M6 item 14: "once" is checked, not assumed: the run a restart started that stops with a move-on again in its first second with no cycle, or with the same move-on
    /// on the same ground as the run before it, is refused by <see cref="MoveOnLoopStop"/> (the hook is not asked) and the dial stops by that name.
    /// </summary>
    private bool MoveOnRestart(IGameSessionView session, AutopilotScenario scenario)
    {
        var stop = scenario.StopName ?? "none";
        _log.Info(LogCategory, $"ladder: move-on restart: check before: the run ended with {stop} (ok) at cycle {scenario.Cycle}; the dial restarts once by itself on the next rung (check, act, check)");
        _handOver = scenario.HandOver; // M6 item 14: what the ended run proved goes to the next run (the bind, the buff pass, the kit audit, the named rung's gate), each checked there before it is used
        _handOver = HandOverForTest?.Invoke(_handOver) ?? _handOver;
        _log.Info(LogCategory, $"ladder: move-on restart: hand-over taken {_handOver.Line(session.Clock.Now)}");

        // M6 item 14 (live, 2026-11-12): a move-on restarts at most once. The run a restart started is in a loop when it stops with a move-on again with no cycle ended and no second played (nothing happened between the two), or
        // with the same move-on on the same ground as the run before it (the restart changed nothing, whatever trip it made first): stopped by name, never restarted.
        var instant = scenario.Cycles == 0 && scenario.SecondsUsed < MoveOnLoopSeconds;
        var same = _lastMoveOn is { } last && string.Equals(last.Ground, _handOver.Ground, StringComparison.Ordinal) && string.Equals(last.Stop, stop, StringComparison.Ordinal);
        var looped = _moveOnRun && (instant || same);
        _lastMoveOn = (_handOver.Ground, stop);
        var answer = looped
            ? MoveOnAnswer.Refused(string.Create(CultureInfo.InvariantCulture, $"{MoveOnLoopStop}: the run that move-on restart {MoveOns} started on {_handOver.Ground} stopped with {stop} again ({(same ? "the same move-on on the same ground as the run before it" : "another move-on")}, after {scenario.SecondsUsed:0.##} s and {scenario.Cycles} cycle(s)): the restart changed nothing, so no second restart is made"))
            : MoveOn is { } hook ? hook(session) : MoveOnAnswer.Refused("failed:rung-restart-refused:no-host-hook: the host gave no move-on hook");
        if (answer.Refusal is { } refusal)
        {
            var loop = refusal.StartsWith(MoveOnLoopStop, StringComparison.Ordinal);
            MoveOnRefusal = refusal;
            _handOver = null;
            _decision = $"failed: {refusal}";
            if (loop)
            {
                _log.Error(LogCategory, $"autopilot host: stop={refusal}; the dial stops here and starts nothing more (the cause is a rung read that differs between the run and the dial: the two `ladder: rung: asked by` lines above name it)");
            }

            _log.Warn(LogCategory, $"ladder: move-on restart: REFUSED after {stop}: {refusal}; the dial stops here by that name");
            if (_unattended)
            {
                var decision = $"autopilot: the run was started by --autopilot-start and the move-on after {stop} was refused ({refusal}); the game logs off and quits with exit code 1";
                _unattendedEnd = new AutopilotUnattendedEnd(1, decision);
                _log.Info(LogCategory, decision);
            }

            ChatNotice?.Invoke("Lifestone: the level up job could not move on to the next rung: " + refusal);
            Stopped?.Invoke(loop ? MoveOnLoopStop : "failed:rung-restart-refused");
            return true;
        }

        MoveOns++;
        ChatNotice?.Invoke($"Lifestone: the level up job moves on ({stop}); the next run starts on the next rung's ground.");
        if (answer.Ready)
        {
            _log.Info(LogCategory, $"ladder: move-on restart: act: the next rung's parts are ready; the dial starts the next run now (restart {MoveOns})");
            Start(session, _unattended ? MoveOnReason : ResumeReason);
        }
        else
        {
            _log.Info(LogCategory, $"ladder: move-on restart: act: the next rung's parts are being read; the host presses the dial when they are (restart {MoveOns})");
        }

        return true;
    }

    /// <summary>
    /// The unattended run's end (M5 item 7 s1): a run started by <c>--autopilot-start</c> that stopped by itself (a stop rule, a failure) logs the game off and quits, exit code 0 for ok=yes and 1 otherwise, after the done line (already
    /// written by the scenario); a person's own stop request keeps the game open. The decision is one log line.
    /// </summary>
    private void DecideUnattendedEnd(AutopilotScenario scenario)
    {
        if (!_unattended && !_leaving)
        {
            return;
        }

        var stop = scenario.StopName ?? "none";
        if (stop.StartsWith("request:", StringComparison.Ordinal) && stop != "request:" + StopFileReason)
        {
            _log.Info(LogCategory, $"autopilot: the run was started by --autopilot-start and ended on a stop request (stop={stop}); the game stays open");
            return;
        }

        var taskDone = AutopilotScenario.IsTaskDone(stop); // M6 6 MB: a task's end (task-done:*) is an ok end
        var exit = scenario.Ok || taskDone ? 0 : 1;
        var decision = $"autopilot: the run was started by --autopilot-start and ended by itself (stop={stop} ok={(scenario.Ok ? "yes" : "no")}{(taskDone ? ", the task is done" : string.Empty)}); the game logs off and quits with exit code {exit}";
        _unattendedEnd = new AutopilotUnattendedEnd(exit, decision);
        _log.Info(LogCategory, decision);
    }

    /// <summary>Closes the vitae page if it shows (once a death: logged), then stops looking.</summary>
    private void TryCloseVitae()
    {
        if (CloseVitaePage?.Invoke() == true)
        {
            VitaeCloses++;
            _vitaeWatchUntil = double.NegativeInfinity;
            _log.Info(LogCategory, VitaeClosedLine);
        }
    }

    private void Halt(IGameSessionView session, AutopilotScenario scenario)
    {
        // The Action API cancelled the queued automation actions and released the held keys when the switch went on; the loop is not stepped from this tick on.
        var released = session.Actions.Move.ReleaseAutomation();
        _standDownCheck = true; // D145: the check one frame later (nothing held, the keys unlocked)
        if (session.Attack.Active)
        {
            // ACE repeats a swing until it is cancelled, and the switch refuses the autopilot's own cancel: the host sends the stop itself (a stop is what the switch wants).
            _ = session.Actions.CancelAttack(ActionSource.Host);
        }

        Drop(scenario, "halt");
        Halts++;
        _mode = AutopilotMode.Halted;
        _decision = "the kill switch is on: the loop is paused and sends nothing (turn it off, then /auto restarts the loop)";
        _log.Warn(LogCategory, $"autopilot host: halted by the kill switch (released {released} held); /auto restarts the loop once the switch is off");
        Stopped?.Invoke(HaltReason); // M5 7 P7n (D145): raised for the host; the unlock's own chat line says it (P7m)
        Raise();
    }

    private void Install(AutopilotScenario scenario, string reason)
    {
        if (_scenario is { } old)
        {
            old.ChatNotice -= ForwardNotice;
        }

        scenario.StartReason = reason; // M5 7 P7n (D145): start, or resume (/auto, a hand-back): the audit's why
        scenario.ChatNotice += ForwardNotice;
        _scenario = scenario;
        _linesTaken = 0;
        ResetSteer();
        _reconnectsBase = ForeignReconnects?.Invoke() ?? 0;
        scenario.StateChanged += OnStateChanged;
    }

    /// <summary>M6 6 MB: the task a start or hand-back runs, one line (the job's own loop says nothing new).</summary>
    private void LogTaskStart(string how)
    {
        if (_task.Kind != AutopilotTaskKind.Job)
        {
            _log.Info(LogCategory, $"autopilot host: the {how} runs the task {_task.Name} ({JobText})");
        }
    }

    /// <summary>M5 7 P7n: a scenario's chat line, passed on to the host's chat window.</summary>
    private void ForwardNotice(string line) => ChatNotice?.Invoke(line);

    /// <summary>Keeps the lines of a scenario that is about to be replaced.</summary>
    private void Capture(AutopilotScenario scenario)
    {
        for (var i = _linesTaken; i < scenario.Lines.Count; i++)
        {
            _lines.Add(scenario.Lines[i]);
        }

        _linesTaken = scenario.Lines.Count;
    }

    private void OnStateChanged(AutopilotTransition t)
    {
        if (t.From == AutopilotState.Death && t.To != AutopilotState.Death)
        {
            // M5 item 7 s1 (m5-autopilot-vitae-panel): the death station ended (any exit), so close the page retail opened on the worse vitae, as a person would; keep looking for a short while in case the vitae message is late.
            _vitaeWatchUntil = (_session?.Clock.Now ?? 0) + VitaeGraceSeconds;
            TryCloseVitae();
        }

        if (t.To == AutopilotState.Trip && t.Why.Contains("forced trip", StringComparison.Ordinal))
        {
            _forcedTripDone = true;
        }
        else if (t.To == AutopilotState.Death && t.Why.Contains("scripted death", StringComparison.Ordinal))
        {
            _testDeathDone = true;
            TestDeathCycle = _scenario?.Cycle ?? 0;
        }

        _decision = $"{AutopilotScenario.StateName(t.From)} -> {AutopilotScenario.StateName(t.To)}: {t.Why}";
        Raise();
    }

    private void Attach(IGameSessionView session)
    {
        _session = session;
        var moves = session.Actions.Move;
        if (ReferenceEquals(moves, _moves))
        {
            return;
        }

        // D145: a person's movement key no longer takes the controls (D98's hand-off is retired); the session's key lock is the dial's to set. A new session's movement (a reconnect's) takes the lock's state over.
        var wasLocked = false;
        if (_moves is not null)
        {
            _moves.KeyLock.Notice -= OnKeyNotice;
            wasLocked = _moves.KeyLock.Locked;
            if (wasLocked)
            {
                _moves.KeyLock.Unlock("the session's movement was replaced");
            }
        }

        _moves = moves;
        moves.KeyLock.Notice += OnKeyNotice;
        if (wasLocked || _mode is AutopilotMode.Running or AutopilotMode.Stopping)
        {
            SyncKeys();
        }
    }

    /// <summary>The lock's rate-limited notice goes to the chat.</summary>
    private void OnKeyNotice(string line) => ChatNotice?.Invoke(line);

    private void Raise()
    {
        _lastRaise = _session?.Clock.Now ?? double.NegativeInfinity;
        SyncKeys();
        Changed?.Invoke();
    }

    /// <summary>
    /// M5 7 D145: the movement keys follow the mode: locked while it runs or stops, unlocked otherwise. An unlock the autopilot made on its own (a stop rule, a failure, the kill switch, a stop-file end) says why in the chat; /stop's own answer
    /// says it for a /stop. Allocation-free when nothing changes.
    /// </summary>
    private void SyncKeys()
    {
        if (_moves is not { } moves)
        {
            return;
        }

        var keys = moves.KeyLock;
        var want = _mode is AutopilotMode.Running or AutopilotMode.Stopping;
        if (want == keys.Locked)
        {
            return;
        }

        if (want)
        {
            keys.Lock("the autopilot " + (_mode == AutopilotMode.Running ? "runs" : "stops") + ", reason " + (StartReason ?? KeyReason));
            return;
        }

        keys.Unlock("the autopilot is " + _mode.ToString().ToLowerInvariant() + ": " + _decision);
        if (_quietUnlock is not null)
        {
            _quietUnlock = null;
            return;
        }

        var line = _mode == AutopilotMode.Halted
            ? "Lifestone: the kill switch halted the autopilot; the controls are yours. Turn the switch off, then type /auto."
            : "Lifestone: the autopilot stopped (" + _decision + "); the controls are yours. Type /auto to start it again.";
        _log.Info(LogCategory, "autopilot chat: " + line);
        ChatNotice?.Invoke(line);
    }

    /// <summary>The nearest things that will change the state: the stop rules, the forced trip, the stock line closest to its trip, the burden.</summary>
    private string NextTrigger()
    {
        var parts = new List<string>(3);
        var session = _session;
        if (_options.Minutes > 0)
        {
            var left = Math.Max(0, (_options.Minutes * 60) - (_scenario?.SecondsUsed ?? 0));
            parts.Add(string.Create(CultureInfo.InvariantCulture, $"stop at {_options.Minutes:0.#} min: {FormatLeft(left)} left"));
        }

        if (_options.Level > 0)
        {
            var level = session is not null && session.State.Self.Qualities.TryGetInt(PlayerQualities.LevelProperty, out var l) ? l.ToString(CultureInfo.InvariantCulture) : "?";
            parts.Add($"stop at level {_options.Level}: level {level}");
        }

        if (_options.TripAfterKills > 0 && !_forcedTripDone)
        {
            parts.Add($"trip at {_options.TripAfterKills} kills: {(_scenario?.CycleTally.Kills ?? 0) + FightKills()} so far");
        }

        if (session is not null && (_scenario?.TripReader ?? (_stockGround is { } stockGround ? Quartermaster.TripDesk.For(session.State).Reading(stockGround) : null)) is { Active: true } counters) // M6 16 E5d: the session's reading twin: the panel's read moves nothing the trigger reads; it knows the floor-only lines a job stated (M5 7 P7s, D148)
        {
            var verdict = counters.Check(session.State);
            if (verdict.Kind == LogisticsKind.Unread)
            {
                parts.Add("stock: not read yet");
            }
            else
            {
                var best = -1;
                var margin = int.MaxValue;
                for (var i = 0; i < counters.LineCount; i++)
                {
                    var line = counters.LineAt(i);
                    if (counters.IsSuspended(i))
                    {
                        continue; // M6 14 pack rules: no trip or stop while every focus is held
                    }

                    if (counters.IsFloorOnly(i))
                    {
                        if (counters.AtFloor(i))
                        {
                            parts.Add($"{line.Name} at its floor ({counters.HeldOf(i)} held): its spells are put away"); // M5 7 P7s (D148): no stop
                        }

                        continue;
                    }

                    var m = counters.HeldOf(i) - (counters.IsCarried(i) ? line.StopUnder : line.TripUnder);
                    if (m < margin)
                    {
                        (best, margin) = (i, m);
                    }
                }

                if (best >= 0)
                {
                    var line = counters.LineAt(best);
                    parts.Add(counters.IsCarried(best)
                        ? $"stop at {line.StopUnder} {line.Name}: {counters.HeldOf(best)} held"
                        : $"trip at {line.TripUnder} {line.Name}: {counters.HeldOf(best)} held");
                }
            }
        }

        return string.Join("; ", parts);
    }

    /// <summary>The kills of the fight in progress (they join the cycle when it ends).</summary>
    private int FightKills() => _scenario?.CurrentRun switch
    {
        Scenarios.Journey.IStationInner { Scenario: IStoppableFight inner } => inner.Kills,
        IStoppableFight fight => fight.Kills,
        _ => 0,
    };

    private static string FormatLeft(double seconds) =>
        seconds >= 90 ? string.Create(CultureInfo.InvariantCulture, $"{Math.Ceiling(seconds / 60):0} min") : string.Create(CultureInfo.InvariantCulture, $"{Math.Ceiling(seconds):0} s");
}

/// <summary>What the play host does at the end of an unattended run (<c>--autopilot-start</c>): log off, quit, and exit with <paramref name="ExitCode"/>.</summary>
/// <param name="ExitCode">0 for ok=yes, 1 otherwise.</param>
/// <param name="Decision">The one log line that says why.</param>
public sealed record AutopilotUnattendedEnd(int ExitCode, string Decision);

/// <summary>
/// When D58's scripted interrupt starts counting (M5 4 F3, 4-R10; DoD 9 asks for "mid-fight"): not at the dial's start but at the first Fight state, and with the scripted death on (<c>--autopilot-test-death</c>) at the first Fight state
/// of the cycle <b>after</b> the death's, so the key never lands in the buff, the trip or the death station. Engine-free so the rule is tested; the game's scripted hand-off asks it each frame until it is armed.
/// </summary>
/// <param name="deathScripted">True when the run scripts a death (<see cref="AutopilotOptions.TestDeath"/>).</param>
public sealed class AutopilotInterruptArm(bool deathScripted)
{
    /// <summary>True once the first qualifying Fight state was seen (it stays true).</summary>
    public bool Armed { get; private set; }

    /// <summary>Looks at the dial; returns <see cref="Armed"/>.</summary>
    /// <param name="logic">The dial.</param>
    public bool Update(AutopilotHostLogic logic)
    {
        ArgumentNullException.ThrowIfNull(logic);
        if (Armed)
        {
            return true;
        }

        if (logic.Mode is AutopilotMode.Running or AutopilotMode.Stopping && logic.Scenario is { State: AutopilotState.Fight } scenario
            && (!deathScripted || (logic.TestDeathCycle > 0 && scenario.Cycle > logic.TestDeathCycle)))
        {
            Armed = true;
        }

        return Armed;
    }
}
