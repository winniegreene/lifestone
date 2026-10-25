using System.Security.Cryptography;
using System.Text.Json;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Checkout;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;

namespace Lifestone.Headless;

/// <summary>Where the headless host looks for things: the environment and the search roots (tests pass their own).</summary>
/// <param name="ServerRuntime">The value of <see cref="ServerRuntimePaths.EnvironmentVariable"/>, or null.</param>
/// <param name="SearchRoots">Where to look for the checkout (the current directory, then the program's folder).</param>
/// <param name="Time">The clock (the system's in a real run).</param>
internal sealed record HeadlessEnvironment(string? ServerRuntime, IReadOnlyList<string> SearchRoots, TimeProvider Time)
{
    /// <summary>The running process's environment.</summary>
    internal static HeadlessEnvironment FromProcess() =>
        new(Environment.GetEnvironmentVariable(ServerRuntimePaths.EnvironmentVariable), CheckoutLocator.DefaultSearchRoots(), TimeProvider.System);
}

/// <summary>
/// The headless host's entry point (<c>dotnet Lifestone.Headless.dll --option=value ...</c>; step ST15, DoD 13): a game
/// session against the bundled ACE with no Godot. This file does the host's I/O, and only this file: it reads the command
/// line, opens the dats, finds the backend's runtime folder and reads pids.json and secrets.json as bytes, opens the
/// recording and the log file under <c>cache/</c>, then hands everything to <see cref="HeadlessRunner"/> (Lifestone.Client,
/// which does no I/O) on this thread with a <see cref="GameThreadSynchronizationContext"/>, and prints one summary line.
/// </summary>
/// <remarks>
/// <para><b>Exit codes:</b> 0 the scenario succeeded; 1 the command line was bad (nothing ran) or the scenario failed;
/// 2 the run could not start (no dats, no backend, a refused gate, a scenario not built yet).</para>
/// <para><b>Output:</b> log lines on standard error (from <c>--log-level</c>, default info) and in
/// <c>cache/headless/&lt;UTC&gt;-headless-&lt;pid&gt;.log</c> (everything from debug up when <c>--log-level=debug</c>, else info);
/// the recording (always, D48) in <c>cache/sessions/&lt;UTC&gt;-headless-&lt;pid&gt;.lsrec</c> or <c>--record-session=DIR</c>, deleted
/// again when the run could not start (no session ever wrote to it); the verdict line
/// (<see cref="HeadlessRunResult.SummaryLine"/>, m2-plan §4.14) on standard output once the backend was found and the
/// dats opened; with <c>--summary=PATH</c>, the whole result as JSON. Relative output paths are taken from the checkout's
/// root and must not lie under <c>src/</c> or <c>tests/</c>.</para>
/// <para><b>Secrets.</b> The account is <c>--account</c>, else <c>test</c> (the loopback tests' account, D10). secrets.json's
/// bytes are zeroed as soon as the session holds its credentials; no password reaches an argument, a log line, the
/// recording or the summary. A refusal's message (which may name account keys and usernames) goes to the log only; the
/// summary's failure is a fixed phrase (<see cref="HeadlessRunResult.StartFailure"/>).</para>
/// <para><c>--workers</c> is accepted (a Core option shared with the game) but has no effect here: the destination is
/// decoded on the game thread (<see cref="DataOnlyAttachments"/>); one WARNING says so.</para>
/// </remarks>
internal static class Program
{
    /// <summary>The exit code of a bad command line (nothing ran), and of a run whose scenario failed.</summary>
    internal const int BadArgumentsExitCode = 1;

    /// <summary>The exit code of a run whose scenario failed.</summary>
    internal const int FailedExitCode = 1;

    /// <summary>The exit code of a run that could not start.</summary>
    internal const int CouldNotStartExitCode = 2;

    /// <summary>The academy-exit run's limit when <c>--quit-after</c> is not given: the search for the portal, the walk, the portal space and the way home take minutes.</summary>
    internal const double AcademyExitQuitAfterSeconds = 600;

    /// <summary>The default time limit of the mage build (seconds): about forty steps on loopback, the experience grant and the learns among them.</summary>
    internal const double MageBuildQuitAfterSeconds = 600;

    /// <summary>The default time limit of the fighter build (seconds): the mage build's steps plus the kit's grants, the wields and the place to Holtburg.</summary>
    internal const double FighterBuildQuitAfterSeconds = 600;

    /// <summary>The default limit of <c>--scenario=gear</c>: the login, a whole kit's requests at the tools lane's 2 a second (about 30 s), the wields and the log off.</summary>
    internal const double GearQuitAfterSeconds = 300;

    /// <summary>The default limit of <c>--scenario=buff</c>: the gear, then a cycle (about 1,300 s of session time at the live regeneration rate, M3 2.1 s0b), the forced pass and the log off.</summary>
    internal const double BuffQuitAfterSeconds = 3000;

    /// <summary>The default limit of <c>--scenario=fight</c>: the gear, a buff top-up (a full cycle is about 1,300 s), the teleport, the fight (<c>--fight-seconds</c>, 300 s by default) and the log off.</summary>
    internal const double FightQuitAfterSeconds = 3600;

    /// <summary>
    /// What <c>--scenario=death-and-regear</c> needs besides <c>--death-seconds</c>, in seconds: the gear (about 120 s with the spell bars), the bind at Holtburg's lifestone (teleport, settle, the Use: 120 s), the den (60 s),
    /// a suicide (ACE's five lines 3 s apart, the death, the arrival and the vitals: 120 s), the respawn, the vitals and the regear (150 s), and the log off (120 s), with margin. M4 2.2 s1 adds the corpse trip after the respawn
    /// (a teleport to the corpse and its settle, the open, the loot at about 2.8 s an item, the teleport back: up to about 110 s); the margin of this overhead covers it, so the number stays.
    /// </summary>
    internal const double DeathQuitAfterOverheadSeconds = 900;

    /// <summary>
    /// What the stages the death loop added after the respawn need besides the fight's own <c>--death-fight-seconds</c>, in seconds (M4 3.1 d2, review R19; the game leg's budget, <c>DeathLegRunner.WholeCheckSecondsFor</c>): the decoy top-up 30, the rebuff 240, the
    /// teleport back to the den 30, the walk to the tuskers 60, the pre-trip heal 120, the corpse trip 90 and the return 60 = 630. Without it a headless run on the defaults whose rebuff waits out its mana and whose fight takes its seconds was stopped by the host in the trip.
    /// </summary>
    internal const double DeathQuitAfterNewStagesSeconds = 630;

    /// <summary>
    /// The default limit of <c>--scenario=death-and-regear</c>: <see cref="DeathQuitAfterOverheadSeconds"/> (the 900 s of M4 2.2) plus <see cref="DeathQuitAfterNewStagesSeconds"/> plus <c>--death-fight-seconds</c> plus <c>--death-seconds</c>, the formula of the loopback
    /// tests' <c>DeathLive.QuitAfterSecondsFor</c> (a ceiling: the real run takes about a third of it). 2,310 s with the defaults.
    /// </summary>
    /// <param name="deathSeconds">The wait among the tuskers (<c>--death-seconds</c>).</param>
    /// <param name="fightSeconds">The fight stage's limit (<c>--death-fight-seconds</c>).</param>
    internal static double DeathQuitAfterSeconds(double deathSeconds, double fightSeconds = DeathOptions.DefaultFightSeconds) =>
        DeathQuitAfterOverheadSeconds + DeathQuitAfterNewStagesSeconds + fightSeconds + deathSeconds;

    /// <summary>What <c>--scenario=hunt</c> needs besides <c>--hunt-seconds</c>, in seconds: the login (60), the bind (90), the potions and regeneration (up to 240), the weapon swap (30), the teleports and settles (60), the wind-down and the log off (120). The corpse sequence and its loot (M4 2.2 s0: about 2.8 s an item) run inside <c>--hunt-seconds</c>, which abandons them at its end.</summary>
    internal const double HuntQuitAfterOverheadSeconds = 600;

    /// <summary>The default limit of <c>--scenario=hunt</c>: <see cref="HuntQuitAfterOverheadSeconds"/> plus <c>--hunt-seconds</c>.</summary>
    /// <param name="huntSeconds">The hunt's time (<c>--hunt-seconds</c>).</param>
    internal static double HuntQuitAfterSeconds(double huntSeconds) => HuntQuitAfterOverheadSeconds + huntSeconds;

    /// <summary>What <c>--scenario=journey</c> needs besides the plan's expected seconds: the login, the known state (a recovery place and its settle) and the log off.</summary>
    internal const double JourneyLoginMarginSeconds = 300;

    /// <summary>The default limit of <c>--scenario=journey</c>: the plan's expected seconds (the sum of its stations') plus <see cref="JourneyLoginMarginSeconds"/>.</summary>
    /// <param name="plan">The journey's plan.</param>
    internal static double JourneyQuitAfterSeconds(JourneyPlan plan) => plan.ExpectedSeconds + JourneyLoginMarginSeconds;

    /// <summary>The account the headless host uses when <c>--account</c> is not given (D10).</summary>
    internal const string DefaultAccount = "test";

    /// <summary>The first lines of the headless <c>--help</c>.</summary>
    internal const string HelpPreamble =
        "Lifestone headless host options. Run: dotnet Lifestone.Headless.dll --option=value ...\n" +
        "Only the options a headless run reads are listed (the game's --help lists every Lifestone option).";

    /// <summary>Where the run's log file goes, from the checkout's root.</summary>
    internal const string LogDirectory = "cache/headless";

    private static int Main(string[] args) => Run(args, Console.Out, Console.Error, HeadlessEnvironment.FromProcess());

    /// <summary>Runs the host on <paramref name="args"/> with the given console streams and returns the exit code.</summary>
    internal static int Run(IEnumerable<string> args, TextWriter output, TextWriter error) =>
        Run(args, output, error, HeadlessEnvironment.FromProcess());

    /// <summary>Runs the host on <paramref name="args"/> in <paramref name="environment"/> and returns the exit code.</summary>
    internal static int Run(IEnumerable<string> args, TextWriter output, TextWriter error, HeadlessEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(environment);
        var options = HeadlessOptions.Parse(args);
        if (options.Help)
        {
            output.Write(OptionCatalog.Registry.FormatHelp(HelpPreamble, OptionScenes.Headless));
            return 0;
        }

        if (options.Errors.Count > 0)
        {
            foreach (var problem in options.Errors)
            {
                error.WriteLine("headless: " + problem);
            }

            return BadArgumentsExitCode;
        }

        var console = new TextLogSink(error, options.LogLevel, environment.Time);
        if (options.Workers is not null)
        {
            console.Warn("headless", "--workers has no effect in the headless host: it decodes the destination on the game thread (DataOnlyAttachments)");
        }

        // An automated run uses only the test account (D10): refused here, before the backend or the dats are touched. HeadlessRunner.StartWith
        // applies the same rule again, so no other caller can reach the owner's play account either.
        if (!AutomatedAccountRule.TryApply(options.Session, automated: true, out var automatedSession, out var accountRefusal))
        {
            console.Error("headless", "the run is refused: " + accountRefusal);
            return CouldNotStartExitCode;
        }

        options = options with { Session = automatedSession };

        var root = CheckoutLocator.EnclosingCheckouts(environment.SearchRoots) is { Count: > 0 } checkouts ? checkouts[0] : Directory.GetCurrentDirectory();
        if (!Prepare(environment, console, out var runtime, out var pids, out var aceCommit))
        {
            return CouldNotStartExitCode;
        }

        DatSet dats;
        try
        {
            dats = DatSet.Open(options.DatDirectory);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            console.Error("headless", $"the dats cannot be opened ({e.GetType().Name}: {e.Message}); give their folder with --dat-dir or {DatSet.DirectoryEnvironmentVariable}");
            return CouldNotStartExitCode;
        }

        using (dats)
        {
            return RunWithDats(options, environment, root, runtime, pids, aceCommit, dats, console, output);
        }
    }

    /// <summary>Finds the backend and reads pids.json (and the ACE commit) before anything heavier happens.</summary>
    private static bool Prepare(
        HeadlessEnvironment environment,
        ILogSink console,
        out ServerRuntimePaths runtime,
        out byte[] pids,
        out string? aceCommit)
    {
        runtime = null!;
        pids = [];
        aceCommit = null;
        if (!ServerRuntimePaths.TryFind(environment.ServerRuntime, environment.SearchRoots, out var found, out var problem))
        {
            console.Error("headless", problem);
            return false;
        }

        runtime = found;
        try
        {
            pids = File.ReadAllBytes(runtime.PidsFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            console.Error("headless", $"the backend is not running: {runtime.PidsFile} cannot be read ({e.GetType().Name}); start it with server\\start-server.ps1 (or tools\\play.ps1)");
            return false;
        }

        if (!BackendReadiness.ModReady(pids, out var reason))
        {
            console.Error("headless", "the backend is not ready: " + reason);
            return false;
        }

        aceCommit = ReadAceCommit(runtime, console);
        return true;
    }

    private static int RunWithDats(
        HeadlessOptions options,
        HeadlessEnvironment environment,
        string root,
        ServerRuntimePaths runtime,
        byte[] pids,
        string? aceCommit,
        DatSet dats,
        TextLogSink console,
        TextWriter output)
    {
        var stamp = environment.Time.GetUtcNow();
        var recordingFolder = options.Session.RecordDirectory ?? SessionOptions.DefaultRecordDirectory;
        var recordingRelative = Path.Combine(recordingFolder, RecordingFormat.FileName(stamp, HeadlessRunOptions.RunnerToken, Environment.ProcessId));
        var logRelative = Path.Combine(LogDirectory, Path.ChangeExtension(RecordingFormat.FileName(stamp, HeadlessRunOptions.RunnerToken, Environment.ProcessId), ".log"));
        if (!OutputPaths.TryResolve(recordingRelative, root, out var recordingPath, out var problem)
            || !OutputPaths.TryResolve(logRelative, root, out var logPath, out problem))
        {
            console.Error("headless", problem ?? "the output path is refused");
            return CouldNotStartExitCode;
        }

        StreamWriter logFile;
        FileStream recording;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(recordingPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            logFile = new StreamWriter(new FileStream(logPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            console.Error("headless", $"the log file cannot be created ({e.GetType().Name}: {e.Message})");
            return CouldNotStartExitCode;
        }

        using (logFile)
        {
            try
            {
                recording = new FileStream(recordingPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                console.Error("headless", $"the recording cannot be created ({e.GetType().Name}: {e.Message})");
                return CouldNotStartExitCode;
            }

            var fileLog = new TextLogSink(logFile, options.LogLevel == LogSeverity.Debug ? LogSeverity.Debug : LogSeverity.Info, environment.Time, flushEachLine: true);
            var log = new TeeLogSink(console, fileLog);
            var started = false;
            try
            {
                using (recording)
                {
                    return RunSession(options, environment, root, runtime, pids, aceCommit, dats, log, recording, recordingPath, output, out started);
                }
            }
            finally
            {
                // Also when Start threw something unexpected: a run that never started leaves no recording behind.
                if (!started)
                {
                    DeleteUnstartedRecording(recordingPath, log);
                }
            }
        }
    }

    private static int RunSession(
        HeadlessOptions options,
        HeadlessEnvironment environment,
        string root,
        ServerRuntimePaths runtime,
        byte[] pids,
        string? aceCommit,
        DatSet dats,
        ILogSink log,
        FileStream recording,
        string recordingPath,
        TextWriter output,
        out bool started)
    {
        started = false;
        var runOptions = new HeadlessRunOptions
        {
            Session = options.Session with { Account = options.Session.Account ?? DefaultAccount },
            QuitAfterSeconds = options.QuitAfter ?? options.Scenario switch
            {
                HeadlessScenario.AcademyExit => AcademyExitQuitAfterSeconds,
                HeadlessScenario.MageBuild => MageBuildQuitAfterSeconds,
                HeadlessScenario.FighterBuild => FighterBuildQuitAfterSeconds,
                HeadlessScenario.Gear => GearQuitAfterSeconds,
                HeadlessScenario.Buff => BuffQuitAfterSeconds,
                HeadlessScenario.Fight => FightQuitAfterSeconds,
                HeadlessScenario.DeathAndRegear => DeathQuitAfterSeconds(options.Death.Seconds, options.Death.FightSeconds),
                HeadlessScenario.Hunt => HuntQuitAfterSeconds(options.Hunt.Seconds),
                HeadlessScenario.Wayfind => WayfindScenario.QuitAfterSeconds(options.Wayfind),
                HeadlessScenario.Journey when options.Journey is { } journeyPlan => JourneyQuitAfterSeconds(journeyPlan),
                _ => HeadlessRunOptions.DefaultQuitAfterSeconds,
            },
            LifestoneVersion = typeof(Program).Assembly.GetName().Version?.ToString(),
            AceCommit = aceCommit,
        };
        IScenario scenario = new LoginScenario(options.Session.Character);
        var context = new GameThreadSynchronizationContext();
        HeadlessRunResult result;
        using (context.Install())
        {
            HeadlessRunner runner;
            var secrets = Array.Empty<byte>();
            try
            {
                if (options.Scenario == HeadlessScenario.HoltburgLaps)
                {
                    // Builds the Holtburg collision and the ring before the session exists (a few seconds).
                    log.Info("headless", "building the Holtburg collision and the ring for the laps");
                    scenario = new HoltburgLapsScenario(
                        dats,
                        log,
                        options.Session.Character,
                        options.CreateCharacter,
                        options.CreateCharacter is null ? null : HeadlessCharacter.RetailSkillClasses(dats));
                }
                else if (options.Scenario == HeadlessScenario.AcademyExit)
                {
                    log.Info("headless", "building the Training Academy collision for the exit walk");
                    scenario = new AcademyExitScenario(
                        dats,
                        log,
                        options.Session.Character,
                        options.CreateCharacter,
                        options.CreateCharacter is null ? null : HeadlessCharacter.RetailSkillClasses(dats),
                        portalCell: PortalExit.ExitToHoltburgCell);
                }
                else if (options.Scenario == HeadlessScenario.MageBuild)
                {
                    if (options.Session.Character is not null || options.CreateCharacter is not null)
                    {
                        log.Warn("headless", "--character and --create-character are ignored by --scenario=mage-build (the mage is always the template's character)");
                    }

                    scenario = new MageBuildScenario(XpTableData.Load(dats), HeadlessCharacter.RetailSkillClasses(dats), log, freshCharacter: options.FreshCharacter);
                }
                else if (options.Scenario == HeadlessScenario.FighterBuild)
                {
                    if (options.Session.Character is not null || options.CreateCharacter is not null)
                    {
                        log.Warn("headless", "--character and --create-character are ignored by --scenario=fighter-build (the fighter is always the template's character)");
                    }

                    // The components' taper colours are the account's (ACE customises them from the stored account name, as for the gear kit): read here, hashed by FighterComponents and never logged or printed.
                    var withComponents = Lifestone.Client.Session.Fighter.FighterProfile.For(options.Fighter) is not null; // the Hunter has no spells and so no components
                    scenario = new FighterBuildScenario(XpTableData.Load(dats), HeadlessCharacter.RetailSkillClasses(dats), log, template: options.Fighter, spells: withComponents ? SpellTableData.Load(dats).Spells : null, account: withComponents ? LoginName(runtime, runOptions.Session.Account) : null, recreate: options.FighterRecreate, freshCharacter: options.FreshCharacter);
                }
                else if (options.Scenario == HeadlessScenario.Gear)
                {
                    if (options.Session.Character is not null || options.CreateCharacter is not null)
                    {
                        log.Warn("headless", "--character and --create-character are ignored by --scenario=gear (the mage is always the template's character)");
                    }

                    // The kit's taper colours are the account's (ACE customises them from the stored account name, the login name; it is read here, hashed by the kit and never logged or printed).
                    scenario = new GearScenario(SpellTableData.Load(dats).Spells, LoginName(runtime, runOptions.Session.Account), log);
                }
                else if (options.Scenario == HeadlessScenario.Buff)
                {
                    if (options.Session.Character is not null || options.CreateCharacter is not null)
                    {
                        log.Warn("headless", "--character and --create-character are ignored by --scenario=buff (the character is the account's template: " + (options.Fighter?.Name ?? Lifestone.Client.Session.Mage.MageTemplate.CharacterName) + ")");
                    }

                    // M4 P5a C4: a fighter account (test-melee, test-archer) buffs through the caster swap with no gear step; the default account is the mage's scenario, unchanged.
                    scenario = options.Fighter is { } buffFighter
                        ? new BuffScenario(buffFighter, SpellTableData.Load(dats).Spells, options.Buff, log)
                        : new BuffScenario(SpellTableData.Load(dats).Spells, LoginName(runtime, runOptions.Session.Account), options.Buff, log, bars: new MageSpellBars(log));
                }
                else if (options.Scenario == HeadlessScenario.Fight)
                {
                    if (options.Session.Character is not null || options.CreateCharacter is not null)
                    {
                        log.Warn("headless", "--character and --create-character are ignored by --scenario=fight (the mage is always the template's character)");
                    }

                    log.Info("headless", "building the Black Spawn Den's collision and floor graph for the fight walk");
                    scenario = new FightScenario(SpellTableData.Load(dats).Spells, LoginName(runtime, runOptions.Session.Account), options.Buff, options.Fight, DenWalk.Create(dats, log), log);
                }
                else if (options.Scenario == HeadlessScenario.DeathAndRegear)
                {
                    if (options.Session.Character is not null || options.CreateCharacter is not null)
                    {
                        log.Warn("headless", "--character and --create-character are ignored by --scenario=death-and-regear (the mage is always the template's character)");
                    }

                    log.Info("headless", "building the Black Spawn Den's collision and floor graph for the death wait's walk");
                    scenario = new DeathScenario(SpellTableData.Load(dats).Spells, LoginName(runtime, runOptions.Session.Account), options.Death, DenWalk.Create(dats, log), log, stages: options.Death.Stages ?? DeathStages.All, approach: HoltburgApproach.Create(dats, log), buff: options.Buff, fight: new FightOptions { Kills = options.Death.FightKills, Seconds = options.Death.FightSeconds }) { LevelXp = DeathScenario.LevelXpOf(XpTableData.Load(dats)) };
                }
                else if (options.Scenario == HeadlessScenario.Hunt)
                {
                    if (options.Session.Character is not null || options.CreateCharacter is not null)
                    {
                        log.Warn("headless", "--character and --create-character are ignored by --scenario=hunt (the fighter is always the template's character)");
                    }

                    // The cell centres' heights come from the retail dats (the terrain sampler the client's ground heights use); read once per cell and kept.
                    var heights = new Dictionary<uint, float?>();
                    scenario = new HuntScenario(
                        options.Hunt,
                        log,
                        cell =>
                        {
                            if (!heights.TryGetValue(cell.CellId, out var height))
                            {
                                heights[cell.CellId] = height = Lifestone.Client.Session.Fighter.HuntGround.TerrainHeight(dats, cell);
                            }

                            return height;
                        },
                        ground: options.Hunt.GroundRow,
                        follow: new Lifestone.Client.Session.Scenarios.HuntFollow(dats, log),
                        template: options.Fighter)
                    {
                        SeekHeight = (cellId, x, y) => Lifestone.Client.Session.Fighter.HuntGround.SeekHeight(dats, cellId, x, y), // M4 P4 seek
                    };
                }
                else if (options.Scenario == HeadlessScenario.Wayfind)
                {
                    if (options.Session.Character is not null || options.CreateCharacter is not null)
                    {
                        log.Warn("headless", "--character and --create-character are ignored by --scenario=wayfind (the character is the account's fighter template)");
                    }

                    scenario = new WayfindScenario(dats, log, options.Wayfind, options.Fighter?.Name);
                }
                else if (options.Scenario == HeadlessScenario.Journey && options.Journey is { } plan)
                {
                    if (options.Session.Character is not null || options.CreateCharacter is not null)
                    {
                        log.Warn("headless", "--character and --create-character are ignored by --scenario=journey (the character is the journey's: " + plan.Character + ")");
                    }

                    // The kit's taper colours are the account's (the login name, read here, hashed by the kit and never logged or printed), as for --scenario=gear.
                    scenario = new HeadlessJourneyScenario(plan, options.Arguments, dats, SpellTableData.Load(dats).Spells, log, LoginName(runtime, runOptions.Session.Account), new HeadlessJourneyRunnerHost(dats, log));
                }
                else if (options.CreateCharacter is { } create)
                {
                    scenario = new LoginScenario(options.Session.Character, create, HeadlessCharacter.RetailSkillClasses(dats));
                }

                secrets = File.ReadAllBytes(runtime.SecretsFile);
                runner = HeadlessRunner.Start(runOptions, scenario, secrets, pids, dats, environment.Time, log, recording);
            }
            catch (Exception e) when (e is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
            {
                // The message may name account keys, usernames and remedies (never a password; docs/session.md §10): the
                // log only. The summary gets a fixed phrase.
                log.Error("headless", $"the run could not start: {e.Message}");
                var refused = HeadlessRunResult.CouldNotStart(scenario, e);
                output.WriteLine(refused.SummaryLine(null));
                if (options.SummaryPath is { } refusedSummary)
                {
                    WriteSummary(refusedSummary, root, refused, null, log);
                }

                return CouldNotStartExitCode;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secrets);
            }

            started = true;

            using (runner)
            {
                using var cancel = new CancellationTokenSource();
                ConsoleCancelEventHandler onCancel = (_, e) =>
                {
                    e.Cancel = true;
                    cancel.Cancel();
                };
                Console.CancelKeyPress += onCancel;
                try
                {
                    result = runner.Run(context, cancel.Token);
                }
                finally
                {
                    Console.CancelKeyPress -= onCancel;
                }
            }
        }

        var shown = Shown(root, recordingPath);
        if (scenario is MageBuildScenario mage)
        {
            // Additive to the frozen headless line: the mage's own verdict fields come first (no name, no account).
            output.WriteLine("mage-build " + mage.VerdictFields());
        }

        if (scenario is FighterBuildScenario fighter)
        {
            output.WriteLine("fighter-build " + fighter.VerdictFields());
        }

        if (scenario is HuntScenario huntRun)
        {
            output.WriteLine("hunt " + huntRun.VerdictFields());
        }

        if (scenario is WayfindScenario wayfindRun)
        {
            foreach (var leg in wayfindRun.Lines)
            {
                output.WriteLine(leg);
            }

            if (wayfindRun.StreamerLine is { } streamerLine)
            {
                output.WriteLine(streamerLine);
            }

            output.WriteLine(wayfindRun.DoneLine(result.Ok));
        }

        if (scenario is HeadlessJourneyScenario journeyRun)
        {
            foreach (var line in journeyRun.Lines)
            {
                output.WriteLine(line);
            }

            journeyRun.Dispose();
        }

        if (scenario is GearScenario gear)
        {
            output.WriteLine("gear " + gear.VerdictFields());
        }

        if (scenario is BuffScenario buffRun)
        {
            output.WriteLine("buff " + buffRun.VerdictFields());
        }

        if (scenario is FightScenario fightRun)
        {
            output.WriteLine("fight " + fightRun.VerdictFields());
        }

        if (scenario is DeathScenario deathRun)
        {
            output.WriteLine("death " + deathRun.VerdictFields());
        }

        output.WriteLine(result.SummaryLine(shown));
        if (options.SummaryPath is { } summary)
        {
            WriteSummary(summary, root, result, shown, log);
        }

        return result.ExitCode;
    }

    /// <summary>The login name of the account <paramref name="key"/> picks in the secrets file (the password is parsed and zeroed at once; the name is for the gear kit's hash only).</summary>
    private static string LoginName(ServerRuntimePaths runtime, string? key)
    {
        var secrets = File.ReadAllBytes(runtime.SecretsFile);
        try
        {
            using var credentials = PlayAccounts.Parse(secrets, AccountChoice.FromOption(key, AccountChoice.Default));
            return credentials.Account;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }
    }

    /// <summary>The ACE commit server/versions.json pins (for the recording header), or null with a WARNING.</summary>
    private static string? ReadAceCommit(ServerRuntimePaths runtime, ILogSink log)
    {
        var versions = Path.Combine(Path.GetDirectoryName(runtime.Folder) ?? runtime.Folder, "versions.json");
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllBytes(versions));
            return json.RootElement.TryGetProperty("ace", out var ace) && ace.TryGetProperty("commit", out var commit) && commit.GetString() is { Length: >= 7 and <= 40 } text
                && text.All(char.IsAsciiHexDigitLower)
                ? text
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            log.Warn("headless", $"server/versions.json cannot be read ({e.GetType().Name}); the recording names no ACE commit");
            return null;
        }
    }

    /// <summary>
    /// Deletes the recording of a run that could not start: no session wrote to it, so it is empty (or, when the session
    /// was created and then the runner refused, a recording of nothing). A failure to delete is a WARNING naming the file.
    /// </summary>
    private static void DeleteUnstartedRecording(string path, ILogSink log)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log.Warn("headless", $"the unused recording {path} could not be deleted ({e.GetType().Name}); delete it by hand");
        }
    }

    /// <summary>The summary as JSON at <paramref name="path"/> (checked with <see cref="OutputPaths"/>).</summary>
    private static void WriteSummary(string path, string root, HeadlessRunResult result, string? recording, ILogSink log)
    {
        if (!OutputPaths.TryResolve(path, root, out var full, out var problem))
        {
            log.Error("headless", "--summary: " + problem);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        using var stream = new FileStream(full, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteString("scenario", result.Scenario);
        json.WriteBoolean("ok", result.Ok);
        json.WriteString("stage", result.Stage);
        if (result.Failure is { } failure)
        {
            json.WriteString("failure", failure);
        }

        json.WriteStartArray("phases");
        foreach (var phase in result.Phases)
        {
            json.WriteStringValue(phase.ToString());
        }

        json.WriteEndArray();
        json.WriteNumber("loginCompletes", result.LoginCompletes);
        json.WriteNumber("errors", result.Errors);
        json.WriteNumber("warnings", result.Warnings);
        json.WriteNumber("seconds", Math.Round(result.Seconds, 3));
        json.WriteNumber("recordedFrames", result.RecordedFrames);
        json.WriteNumber("corrections", result.Corrections);
        if (result.WhereMaxMetres is { } where)
        {
            json.WriteNumber("whereMaxMetres", Math.Round(where, 4));
        }
        else
        {
            json.WriteNull("whereMaxMetres");
        }

        json.WriteNumber("unmatched", result.Unmatched);
        if (recording is null)
        {
            json.WriteNull("recording");
        }
        else
        {
            json.WriteString("recording", recording);
        }

        json.WriteNumber("exitCode", result.ExitCode);
        json.WriteEndObject();
    }

    /// <summary>A path as the summary shows it: relative to the checkout's root, with forward slashes.</summary>
    private static string Shown(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>Writes every entry to two sinks.</summary>
    private sealed class TeeLogSink(ILogSink first, ILogSink second) : ILogSink
    {
        public bool IsEnabled(LogSeverity severity) => first.IsEnabled(severity) || second.IsEnabled(severity);

        public void Write(LogSeverity severity, string category, string message, ReadOnlySpan<LogField> fields)
        {
            if (first.IsEnabled(severity))
            {
                first.Write(severity, category, message, fields);
            }

            if (second.IsEnabled(severity))
            {
                second.Write(severity, category, message, fields);
            }
        }
    }
}
