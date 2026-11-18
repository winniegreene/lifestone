using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Autopilot;

/// <summary>
/// The kit-factory seam of the level up job (M6 item 16 E0b, D139; docs/automation-engine.md section 5): what only a host can do when the run's ground changes. A kit is the parts a run on one ground is built from (the station
/// factory, the journey context with its runner host, the checkpoint store, the reconnect). The order of the steps and what a restart carries are the client's (<see cref="AutopilotRungKits"/>); the host builds the parts: the play
/// host over the game's nodes with the player's own body as the walker and its files (<c>AutopilotHost</c>), the headless host over its own body (<see cref="Hosting.HeadlessAutopilotKits"/>).
/// </summary>
public interface IAutopilotKitFactory
{
    /// <summary>True when the kit asked for last is built: <see cref="Create"/> can make a scenario now. False before the first <see cref="Prepare"/> and while a host still reads the kit's parts.</summary>
    bool Ready { get; }

    /// <summary>
    /// Releases the kit in hand and begins the kit of a run over <paramref name="ground"/> on <paramref name="plan"/>. A host that reads parts on workers starts nothing here (<see cref="PressWhenReady"/> does); what it already read
    /// and can share (the spell table) it keeps.
    /// </summary>
    /// <param name="ground">The run's ground (the rung's).</param>
    /// <param name="plan">The journey plan for it (<see cref="AutopilotHostLogic.PlanFor"/>).</param>
    void Prepare(HuntingGround ground, JourneyPlan plan);

    /// <summary>
    /// Called when a move-on finds the kit not <see cref="Ready"/>: the host reads the kit's parts and presses the dial itself when they are read, with <paramref name="startedBy"/> and <paramref name="reason"/> as the press's
    /// (<see cref="AutopilotHostLogic.Toggle"/>). A host whose kit is always ready after <see cref="Prepare"/> is never asked.
    /// </summary>
    /// <param name="startedBy">The start's option token for an unattended run, or null.</param>
    /// <param name="reason">The start's reason for a person's run, or null.</param>
    void PressWhenReady(string? startedBy, string? reason);

    /// <summary>The scenario of a start or a hand-back over the kit in hand (the dial's factory: <see cref="AutopilotHostLogic"/>'s <c>create</c>); it carries <paramref name="carry"/> as <see cref="AutopilotRungKits.Scenario"/> does.</summary>
    /// <param name="carry">What the run carries (<see cref="AutopilotCarry.None"/> for a start).</param>
    AutopilotScenario Create(AutopilotCarry carry);
}

/// <summary>
/// The level up job's kits by rung (M6 item 16 E0b, D139): the orchestration the play host's node held until this slice (its <c>PrepareRung</c>, <c>UseRung</c> and <c>MoveOnNext</c>, and the carry in <c>AutopilotKit.Create</c>),
/// moved here unchanged so every host runs one copy and the climb can be replayed with no window. Which ground a rung is stays <see cref="RungOwner"/>'s (D93), asked through the dial
/// (<see cref="AutopilotHostLogic.StartGround"/>, <see cref="AutopilotHostLogic.MoveOnGroundFor"/>); nothing here chooses one.
/// </summary>
/// <remarks>
/// <para>The first press (<see cref="PrepareRung"/>): the level in the state, the start's ground, the kit over it. The move-on (<see cref="MoveOnNext"/>, the dial's <see cref="AutopilotHostLogic.MoveOn"/> hook, called when a run
/// ends <c>stop=rung-moved:&lt;rung&gt;</c>): the next ground, the kit over it, and the answer: go (the kit is ready), wait (the host reads it and presses the dial), or refused by name. The log lines and their order are the play
/// host's own, kept word for word.</para>
/// <para>Game thread only.</para>
/// </remarks>
public sealed class AutopilotRungKits
{
    private readonly AutopilotHostLogic _logic;
    private readonly AutopilotOptions _options;
    private readonly IAutopilotKitFactory _kits;
    private readonly ILogSink _log;

    /// <summary>The kits of the level up job <paramref name="options"/> describe, for the dial <paramref name="logic"/>, built by <paramref name="kits"/>.</summary>
    /// <param name="logic">The dial (its stock counters follow the ground; its move-on asks <see cref="MoveOnNext"/>).</param>
    /// <param name="options">The job's options.</param>
    /// <param name="kits">The host's half.</param>
    /// <param name="log">The log (the host's sink: the lines are the play host's own).</param>
    public AutopilotRungKits(AutopilotHostLogic logic, AutopilotOptions options, IAutopilotKitFactory kits, ILogSink log)
    {
        _logic = logic ?? throw new ArgumentNullException(nameof(logic));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _kits = kits ?? throw new ArgumentNullException(nameof(kits));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>The ground of the kit asked for last, or null before the first.</summary>
    public HuntingGround? Ground { get; private set; }

    /// <summary>The journey plan of the kit asked for last, or null before the first.</summary>
    public JourneyPlan? Plan { get; private set; }

    /// <summary>
    /// The level up job's first press, and each press that starts a fresh run (M6 0 a6): reads the character's level in the state, takes the ground of the ladder's rung for it (<see cref="AutopilotHostLogic.StartGround"/>, with the
    /// job's options: M6 item 14, the start reads the buff bot's spells standing on her in the state, so a fresh launch builds its first run on the rung she belongs on) and asks for the kit over it. A level whose rung has no run (a
    /// quest, a pending or an unfightable rung, the top, an unread level) refuses by the ladder's own name: the refusal is logged and returned, and the host decides what a refused press ends.
    /// </summary>
    /// <param name="session">The session.</param>
    /// <returns>Null when the press goes on; else the refusal (<c>level N has no run on the ladder: why</c>).</returns>
    public string? PrepareRung(IGameSessionView session)
    {
        var level = AutopilotHostLogic.LevelOf(session);
        var ground = AutopilotHostLogic.StartGround(session, out var why, _log, _options); // M6 item 14: with the options, the start reads the buff bot's spells standing on her in the state (the start's rung)
        if (ground is null)
        {
            var refusal = $"level {level} has no run on the ladder: {why}";
            _log.Warn(AutopilotHostLogic.LogCategory, "autopilot: " + refusal);
            return refusal;
        }

        UseRung(ground, level);
        return null;
    }

    /// <summary>
    /// Asks for the level up run's kit over <paramref name="ground"/> unless it already is built over it (the same ground): the plan (the climber's, a dungeon rung's with the den's rooms to read), the dial's stock counters, and the
    /// host's kit (<see cref="IAutopilotKitFactory.Prepare"/>). A host that reads the kit's parts on workers is not ready until they are read (the press waits, as the first press does).
    /// </summary>
    /// <param name="ground">The run's ground.</param>
    /// <param name="level">The character's level, for the line.</param>
    public void UseRung(HuntingGround ground, int level)
    {
        ArgumentNullException.ThrowIfNull(ground);
        if (_kits.Ready && Ground is { } current && string.Equals(current.Key, ground.Key, StringComparison.Ordinal))
        {
            return;
        }

        Ground = ground;
        Plan = AutopilotHostLogic.PlanFor(_options, ground);
        _kits.Prepare(ground, Plan);
        _logic.UseGround(ground);
        _log.Info(AutopilotHostLogic.LogCategory, $"autopilot: level up: level {level}, the run's ground is {ground.Key} ({ground.Name}); the parts are built over it");
    }

    /// <summary>
    /// The level up job's move-on (<see cref="AutopilotHostLogic.MoveOn"/>): the run ended with <c>stop=rung-moved:*</c>. Reads the new level and rung, asks for the next rung's kit, and answers ready (the kit stands), waiting (the
    /// host reads it and presses the dial when it is read, still unattended when the run was) or refused by name.
    /// </summary>
    /// <param name="session">The session.</param>
    public MoveOnAnswer MoveOnNext(IGameSessionView session)
    {
        var level = AutopilotHostLogic.LevelOf(session);
        var ground = _logic.MoveOnGroundFor(session, out var why); // M6 item 14: with the ended run's hand-over (its read of the named rung's gate), and never the ground the run just left
        if (ground is null)
        {
            return MoveOnAnswer.Refused(why.StartsWith(AutopilotHostLogic.MoveOnLoopStop, StringComparison.Ordinal) ? why : $"failed:rung-restart-refused: the next rung at level {level} has no run: {why}");
        }

        UseRung(ground, level);
        if (_kits.Ready)
        {
            return MoveOnAnswer.Go;
        }

        _kits.PressWhenReady(_logic.StartedByOption ? AutopilotHostLogic.MoveOnReason : null, _logic.StartedByOption ? null : AutopilotHostLogic.ResumeReason);
        return MoveOnAnswer.Wait;
    }

    /// <summary>
    /// The scenario a kit makes for <paramref name="carry"/> over its parts (the carry of a start, a hand-back and a move-on's restart, one copy for every host). The options are what is left of the run's
    /// (<see cref="AutopilotCarry.Remaining"/>). A hand-back continues where the abandoned scenario was (4-R9, 4-R5): its seed (the cycle and the cycles ended, the seconds used, a cut death's tally and a cut trip), its totals and
    /// its cycle in progress (<see cref="AutopilotCarry.Apply"/>), and a checkpoint store that reads as empty once (<see cref="ColdReadOnceStore"/>), so the file the interrupt wrote is not counted on top of what is carried. A
    /// move-on's restart carries what the ended run proved (<see cref="AutopilotCarry.HandOver"/>, M6 item 14) and reads the file, as a start does.
    /// </summary>
    /// <param name="carry">What the run carries (<see cref="AutopilotCarry.None"/> for a start).</param>
    /// <param name="options">The run's options.</param>
    /// <param name="factory">The kit's station factory.</param>
    /// <param name="context">The kit's journey context (its <see cref="JourneyContext.Dungeon"/> is the run's ground).</param>
    /// <param name="store">The kit's checkpoint store, or null: none is kept.</param>
    /// <param name="session">The host's reconnect, or null: a dropped session stops the run by name.</param>
    public static AutopilotScenario Scenario(AutopilotCarry carry, AutopilotOptions options, IStationFactory factory, JourneyContext context, IAutopilotCheckpointStore? store, IAutopilotSession? session)
    {
        ArgumentNullException.ThrowIfNull(carry);
        var checkpoints = carry.IsEmpty || store is null ? store : new ColdReadOnceStore(store);
        var scenario = new AutopilotScenario(carry.Remaining(options), factory, context, checkpoints, session, carry.Seed, carry.HandOver); // M6 item 14: a move-on's restart carries what the ended run proved
        carry.Apply(scenario);
        return scenario;
    }
}
