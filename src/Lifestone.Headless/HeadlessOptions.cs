using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Headless;

/// <summary>What a headless run does (<c>--scenario</c>).</summary>
internal enum HeadlessScenario
{
    /// <summary>Log in, enter the world, check the server mod's status, log off and quit (ST15).</summary>
    Login,

    /// <summary>Laps around the Holtburg lifestone with zero server corrections (ST16).</summary>
    HoltburgLaps,

    /// <summary>From the Training Academy's spawn to its exit portal with zero server corrections (ST16).</summary>
    AcademyExit,

    /// <summary>Find or create the test account's level-126 combat mage and bring it to the template (M3 1.1, <c>MageBuildScenario</c>); idempotent.</summary>
    MageBuild,

    /// <summary>Find or create the test account's level-40 fighter "Holtburg Hunter", bring it to the template, the kit and Holtburg (M4 1.1 s2, <c>FighterBuildScenario</c>); idempotent.</summary>
    FighterBuild,

    /// <summary>Spawn BSD Mage's gear kit, wield its wand and wear its leather set (M3 1.4 s1, <c>GearScenario</c>); idempotent.</summary>
    Gear,

    /// <summary>Gear, then the buff cycle of the profile's 24 lines and a forced re-buff pass (M3 2.1 s1, <c>BuffScenario</c>); idempotent.</summary>
    Buff,

    /// <summary>Gear, the buff top-up, the Black Spawn Den and a Flame Bolt fight until <c>--fight-kills</c> kills (M3 3.1, <c>FightScenario</c>); idempotent apart from the experience the kills earn.</summary>
    Fight,

    /// <summary>Gear, the spell bars, a bind at Holtburg's lifestone, the Black Spawn Den, a suicide when buffed, the tuskers' death of the player, the respawn and the gear again (M3 3.2, <c>DeathScenario</c>); changes the character (vitae, dropped items, a corpse).</summary>
    DeathAndRegear,

    /// <summary>The fighter binds at Holtburg's lifestone, goes to the Low B belt, fights until <c>--hunt-kills</c> kills with the Mace or the Shortbow and comes back (M4 1.2 s4a, <c>HuntScenario</c>); changes the character (experience, health potions).</summary>
    Hunt,

    /// <summary>Log in as the account's fighter, run to <c>--to</c> with the wayfinder and the human's keys over the collision and tiles the headless streamer builds ahead (and with <c>--wayfind-back</c> back), print the wayfind lines (P5a S6, <c>WayfindScenario</c>); moves the character.</summary>
    Wayfind,

    /// <summary>One of the three journeys (<c>--journey=mage|melee|archer</c>, M4 P5b J4): the plan's stations from one login, on foot.</summary>
    Journey,

    /// <summary>The autopilot (M5 4 h1, D57): BSD Mage's outer loop over the journey's stations until <c>--autopilot-minutes</c>, <c>--autopilot-level</c> or a stop request (<c>AutopilotScenario</c>); changes the character.</summary>
    Autopilot,

    /// <summary>Create the climber Winnie on the account test (M6 item 0 a3a, D5-D7): the fresh-character re-create with the climber's template, no grants (<c>ClimberCreateScenario</c>); changes the account's character list.</summary>
    ClimberCreate,

    /// <summary>The villa track's live proof on Lifestone Throwaway (account test-tools only; M6 villa): grants by the mod's verbs, then buys apartment 4051 through the guardian window, pays its rent, recalls home and moves an item through the storage chest (<c>VillaProofScenario</c>); changes the character.</summary>
    VillaProof,
}

/// <summary>
/// The headless host's command line: its own group (<c>OptionCatalog.Headless</c>), the Session group
/// (<see cref="SessionOptions.FromArguments"/>) and the five Core options it shares with the
/// game. Arguments of no group, or of the game's scenes only, are problems (<see cref="OptionRegistry.Problems"/>), named
/// without their values.
/// </summary>
internal sealed record HeadlessOptions
{
    /// <summary>The account <c>--scenario=villa-proof</c> runs on (M6 villa): the test-tools account, whose Lifestone Throwaway may be levelled and granted freely.</summary>
    internal const string VillaProofAccount = "test-tools";

    /// <summary>The default number of Holtburg laps.</summary>
    internal const int DefaultLaps = 3;

    /// <summary>The most laps accepted.</summary>
    internal const int MaxLaps = 1000;

    /// <summary><c>--help</c> was given.</summary>
    internal bool Help { get; init; }

    /// <summary>The scenario (<c>--scenario</c>, default login).</summary>
    internal HeadlessScenario Scenario { get; init; } = HeadlessScenario.Login;

    /// <summary>Holtburg laps (<c>--laps</c>).</summary>
    internal int Laps { get; init; } = DefaultLaps;

    /// <summary>The character to use, created first when missing (<c>--create-character</c>), or null.</summary>
    internal string? CreateCharacter { get; init; }

    /// <summary>Where to write the run summary JSON (<c>--summary</c>), or null.</summary>
    internal string? SummaryPath { get; init; }

    /// <summary>The dat folder (<c>--dat-dir</c>), or null for the default.</summary>
    internal string? DatDirectory { get; init; }

    /// <summary>The lowest severity logged (<c>--log-level</c>, default info).</summary>
    internal LogSeverity LogLevel { get; init; } = LogSeverity.Info;

    /// <summary>Seconds after which the run fails whatever happens (<c>--quit-after</c>), or null.</summary>
    internal double? QuitAfter { get; init; }

    /// <summary>Decoding worker threads (<c>--workers</c>), or null for the default.</summary>
    internal int? Workers { get; init; }

    /// <summary>The buff cycle's options (<c>--buff-*</c>), used by <c>--scenario=buff</c>.</summary>
    internal BuffOptions Buff { get; init; } = new();

    /// <summary>The fight loop's options (<c>--fight-*</c>), used by <c>--scenario=fight</c>.</summary>
    internal FightOptions Fight { get; init; } = new();

    /// <summary>The death loop's option (<c>--death-seconds</c>), used by <c>--scenario=death-and-regear</c>.</summary>
    internal DeathOptions Death { get; init; } = new();

    /// <summary>The hunt loop's options (<c>--hunt-*</c>), used by <c>--scenario=hunt</c>.</summary>
    internal HuntOptions Hunt { get; init; } = new();

    /// <summary>The wayfind scenario's options (<c>--to</c>, <c>--from</c>, <c>--wayfind-back</c>), used by <c>--scenario=wayfind</c>.</summary>
    internal WayfindOptions Wayfind { get; init; } = new();

    /// <summary>
    /// The fighter template of <c>--scenario=fighter-build</c>, <c>--scenario=hunt</c> and <c>--scenario=wayfind</c> (M4 P4, D31): <c>--fighter</c> when given, else the template of <c>--account</c> (test is the Hunter, test-melee the Brawler,
    /// test-archer the Archer); null for every other scenario. An explicit <c>--fighter</c> that does not match <c>--account</c> is an error.
    /// </summary>
    internal FighterTemplate? Fighter { get; init; }

    /// <summary>The journey's plan (<c>--journey</c>), or null when the scenario is not <c>journey</c>.</summary>
    internal JourneyPlan? Journey { get; init; }

    /// <summary>M5 7 D146: <c>--gear-grant-only=WCID:COUNT</c>, the gear scenario grants exactly this and nothing else (the owner's exception for one test run), or null.</summary>
    internal (uint Wcid, int Count)? GearGrantOnly { get; init; }

    /// <summary>The autopilot's options (<c>--autopilot-*</c>); the defaults (no stop rule) when the scenario is not <c>autopilot</c>.</summary>
    internal Lifestone.Client.Session.Autopilot.AutopilotOptions Autopilot { get; init; } = new();

    /// <summary><c>--fighter-recreate</c> (M4 P5a C5, D40): <c>--scenario=fighter-build</c> on the Brawler or the Archer deletes (pending deletion) and re-creates a character whose creation attributes differ from the template's.</summary>
    internal bool FighterRecreate { get; init; }

    /// <summary><c>--fresh-character</c> (M4 fresh B1, D44/D45): <c>--scenario=fighter-build</c> on the Brawler or the Archer, or <c>--scenario=mage-build</c>, deletes the live character from the character list, creates and builds the template's, and purges the old copies with the mod.</summary>
    internal bool FreshCharacter { get; init; }

    /// <summary><c>--mage-level</c> (M5 7 F3, D87): the level <c>--scenario=mage-build</c> builds the mage to; <see cref="MageTemplate.TargetLevel"/> (126) by default.</summary>
    internal int MageLevel { get; init; } = MageTemplate.TargetLevel;

    /// <summary>The Session group's values.</summary>
    internal SessionOptions Session { get; init; } = new();

    /// <summary>Every problem found, one line each, values never repeated.</summary>
    internal IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>The command line as given (the journey's stations read their scenario options from it).</summary>
    internal IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Parses the headless command line.</summary>
    /// <param name="args">The process arguments.</param>
    internal static HeadlessOptions Parse(IEnumerable<string> args)
    {
        var raw = args.ToList();
        var arguments = CommandLineArgument.ParseAll(raw);
        var errors = new List<string>(OptionCatalog.Registry.Problems(arguments, OptionScenes.Headless));
        var session = SessionOptions.FromArguments(arguments);
        errors.AddRange(session.Errors);
        var buff = BuffOptions.FromArguments(arguments);
        errors.AddRange(buff.Errors);
        var fight = FightOptions.FromArguments(arguments);
        errors.AddRange(fight.Errors);
        var death = DeathOptions.FromArguments(arguments);
        errors.AddRange(death.Errors);
        var hunt = HuntOptions.FromArguments(arguments);
        errors.AddRange(hunt.Errors);
        var wayfind = WayfindOptions.FromArguments(arguments);
        errors.AddRange(wayfind.Errors);
        var autopilot = Lifestone.Client.Session.Autopilot.AutopilotOptions.FromArguments(arguments);
        errors.AddRange(autopilot.Errors);
        var o = new HeadlessOptions();
        FighterTemplate? requestedFighter = null;
        JourneyPlan? journey = null;
        foreach (var argument in arguments)
        {
            switch (argument.Name)
            {
                case "--help":
                    o = o with { Help = true };
                    break;
                case "--scenario":
                    o = o with
                    {
                        Scenario = OptionValues.Choice(
                            argument,
                            errors,
                            o.Scenario,
                            ("login", HeadlessScenario.Login),
                            ("holtburg-laps", HeadlessScenario.HoltburgLaps),
                            ("academy-exit", HeadlessScenario.AcademyExit),
                            ("mage-build", HeadlessScenario.MageBuild),
                            ("fighter-build", HeadlessScenario.FighterBuild),
                            ("gear", HeadlessScenario.Gear),
                            ("buff", HeadlessScenario.Buff),
                            ("fight", HeadlessScenario.Fight),
                            ("death-and-regear", HeadlessScenario.DeathAndRegear),
                            ("hunt", HeadlessScenario.Hunt),
                            ("wayfind", HeadlessScenario.Wayfind),
                            ("journey", HeadlessScenario.Journey),
                            ("autopilot", HeadlessScenario.Autopilot),
                            ("climber-create", HeadlessScenario.ClimberCreate),
                            ("villa-proof", HeadlessScenario.VillaProof)),
                    };
                    break;
                case "--journey":
                    journey = JourneyPlans.ForName(argument.Value);
                    if (journey is null)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not one of {string.Join(", ", JourneyPlans.All.Select(p => p.Name))}");
                    }

                    break;
                case "--fighter":
                    requestedFighter = FighterTemplate.ForKey(argument.Value);
                    if (requestedFighter is null)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not one of {string.Join(", ", FighterTemplate.All.Select(t => t.Key))}");
                    }

                    break;
                case "--gear-grant-only":
                    var pair = argument.Value.Split(':');
                    if (pair.Length == 2 && uint.TryParse(pair[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var grantWcid) && grantWcid > 0
                        && int.TryParse(pair[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var grantCount) && grantCount is >= 1 and <= 100)
                    {
                        o = o with { GearGrantOnly = (grantWcid, grantCount) };
                    }
                    else
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not WCID:COUNT with a count of 1 to 100");
                    }

                    break;
                case "--laps":
                    var known = errors.Count;
                    var laps = OptionValues.Int(argument, errors, o.Laps);
                    if (errors.Count == known && laps is >= 1 and <= MaxLaps)
                    {
                        o = o with { Laps = laps };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not 1 to {MaxLaps}");
                    }

                    break;
                case "--create-character":
                    var name = argument.Value.Trim();
                    if (name.Length is 0 or > SessionOptions.MaxCharacterNameLength || !name.All(c => c is >= ' ' and <= '~'))
                    {
                        errors.Add($"{argument.Name}: needs a character name (1 to {SessionOptions.MaxCharacterNameLength} printable ASCII characters); the value is not shown");
                    }
                    else
                    {
                        o = o with { CreateCharacter = name };
                    }

                    break;
                case "--summary":
                    o = o with { SummaryPath = Required(argument, errors, "a file path") ?? o.SummaryPath };
                    break;
                case "--dat-dir":
                    o = o with { DatDirectory = Required(argument, errors, "a folder") ?? o.DatDirectory };
                    break;
                case "--log-level":
                    o = o with
                    {
                        LogLevel = OptionValues.Choice(
                            argument,
                            errors,
                            o.LogLevel,
                            ("debug", LogSeverity.Debug),
                            ("info", LogSeverity.Info),
                            ("warning", LogSeverity.Warning),
                            ("error", LogSeverity.Error)),
                    };
                    break;
                case "--quit-after":
                    var knownQuit = errors.Count;
                    var seconds = OptionValues.Double(argument, errors, 0);
                    if (errors.Count == knownQuit && seconds > 0)
                    {
                        o = o with { QuitAfter = seconds };
                    }
                    else if (errors.Count == knownQuit)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a positive number of seconds");
                    }

                    break;
                case "--workers":
                    var knownWorkers = errors.Count;
                    var workers = OptionValues.Int(argument, errors, 0);
                    if (errors.Count == knownWorkers && workers >= 1)
                    {
                        o = o with { Workers = workers };
                    }
                    else if (errors.Count == knownWorkers)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not at least 1");
                    }

                    break;
            }
        }

        var fighterScenario = o.Scenario is HeadlessScenario.FighterBuild or HeadlessScenario.Hunt or HeadlessScenario.Wayfind;
        var buffScenario = o.Scenario == HeadlessScenario.Buff;
        if (o.Scenario is HeadlessScenario.MageBuild or HeadlessScenario.ClimberCreate or HeadlessScenario.FighterBuild or HeadlessScenario.Gear or HeadlessScenario.Buff or HeadlessScenario.Fight or HeadlessScenario.DeathAndRegear or HeadlessScenario.Hunt or HeadlessScenario.Wayfind && session.Account is { } account && !string.Equals(account, Program.DefaultAccount, StringComparison.OrdinalIgnoreCase) && !(o.Scenario == HeadlessScenario.ClimberCreate && string.Equals(account, Lifestone.Client.Session.Credentials.TestAccounts.Qwinnie, StringComparison.Ordinal)) && !(fighterScenario && FighterTemplate.ForAccount(account) is not null) && !(buffScenario && FighterTemplate.ForAccount(account) is { Kit.HasCaster: true }))
        {
            // M3 1.1-R5: the build creates and levels a permanent character, so it runs on the default test account only (automation uses accounts.test only).
            // M4 P4 (D31): the fighter-build and hunt scenarios also run on the account of a fighter template (test-melee, test-archer); every other scenario stays on test.
            // M4 P5a C4: so does the buff scenario, on the account of a fighter with a caster (the Brawler's and the Archer's; the Hunter has none and buffs nobody).
            errors.Add($"--account: --scenario={(o.Scenario switch { HeadlessScenario.Gear => "gear", HeadlessScenario.Buff => "buff", HeadlessScenario.Fight => "fight", HeadlessScenario.DeathAndRegear => "death-and-regear", HeadlessScenario.Hunt => "hunt", HeadlessScenario.Wayfind => "wayfind", HeadlessScenario.FighterBuild => "fighter-build", HeadlessScenario.ClimberCreate => "climber-create", _ => "mage-build" })} runs on {(fighterScenario ? "the account of a fighter template (" + string.Join(", ", FighterTemplate.All.Select(t => t.AccountKey)) + ")" : buffScenario ? "the default test account '" + Program.DefaultAccount + "' or the account of a fighter with a caster (" + string.Join(", ", FighterTemplate.All.Where(t => t.Kit.HasCaster).Select(t => t.AccountKey)) + ")" : "the default test account '" + Program.DefaultAccount + "'")} only; the value is not shown");
        }

        if (o.Scenario == HeadlessScenario.VillaProof && !string.Equals(session.Account, VillaProofAccount, StringComparison.OrdinalIgnoreCase))
        {
            // M6 villa: the proof grants and buys, so it runs on Lifestone Throwaway of the test-tools account only (CLAUDE.md: the one character that may be levelled freely); never on the climber's account.
            errors.Add("--account: --scenario=villa-proof runs on the account '" + VillaProofAccount + "' only; the value is not shown");
        }

        FighterTemplate? fighter = null;
        if (fighterScenario || buffScenario)
        {
            fighter = FighterTemplate.ForAccount(session.Account);
            if (requestedFighter is not null && fighter is not null && fighter != requestedFighter)
            {
                errors.Add($"--fighter: the {requestedFighter.Key} fighter lives on account '{requestedFighter.AccountKey}', which is not the account of this run (the value of --account is not shown)");
            }

            fighter = fighter is null ? null : requestedFighter ?? fighter;
            if (buffScenario && fighter is { Kit.HasCaster: false })
            {
                // The buff scenario is the mage's on the default account (the Hunter's account) and the fighter's only for a fighter with a caster.
                if (requestedFighter is not null)
                {
                    errors.Add($"--fighter: the {requestedFighter.Key} fighter has no caster and no buff profile; --scenario=buff takes {string.Join(", ", FighterTemplate.All.Where(t => t.Kit.HasCaster).Select(t => t.Key))}");
                }

                fighter = null;
            }

            if (o.Scenario == HeadlessScenario.Hunt && fighter is not null)
            {
                var resolved = hunt.ForTemplate(fighter);
                errors.AddRange(resolved.Errors.Skip(hunt.Errors.Count));
                hunt = resolved;
            }
        }
        else if (requestedFighter is not null)
        {
            errors.Add("--fighter: only --scenario=fighter-build, --scenario=hunt, --scenario=wayfind and --scenario=buff take a fighter template");
        }

        if (o.Scenario == HeadlessScenario.Wayfind)
        {
            if (wayfind.To is null && !wayfind.Errors.Any(e => e.StartsWith("--to:", StringComparison.Ordinal)))
            {
                errors.Add("--to: --scenario=wayfind needs a destination (--to=0xA5B00032 or --to=CELL@x,y,z)");
            }
        }
        else if (arguments.Any(a => a.Name is "--to" or "--from" or "--wayfind-back"))
        {
            errors.Add("--to, --from and --wayfind-back: only --scenario=wayfind takes them");
        }

        if (o.Scenario == HeadlessScenario.Journey)
        {
            // M4 P5b J4, the account rule: each journey runs on its own test account only (mage: test, melee: test-melee, archer: test-archer);
            // without --account the run uses the journey's account.
            if (journey is null && !arguments.Any(a => a.Name == "--journey"))
            {
                errors.Add($"--journey: --scenario=journey needs a journey ({string.Join(", ", JourneyPlans.All.Select(p => p.Name))})");
            }
            else if (journey is not null)
            {
                if (session.Account is { } journeyAccount && !string.Equals(journeyAccount, journey.Account, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"--account: --journey={journey.Name} runs on the test account '{journey.Account}' only; the value is not shown");
                }

                session = session with { Account = journey.Account };
            }
        }
        else if (o.Scenario == HeadlessScenario.Autopilot)
        {
            // M5 4 h1: the autopilot is the mage's (BSD Mage on the test account), the journey's account rule; --journey may only name the mage.
            // M6 16 E0b: with --autopilot-job=level-up the plan is the job's, as the play host's is (AutopilotHostLogic.PlanFor: the ladder's character on its test account), and the run is the climb (HeadlessClimbScenario).
            var plan = Lifestone.Client.Session.Autopilot.AutopilotHostLogic.PlanFor(autopilot);
            if (journey is not null && journey.Name != plan.Name)
            {
                errors.Add($"--journey: --scenario=autopilot runs the {plan.Name} plan only");
            }

            if (session.Account is { } autopilotAccount && !string.Equals(autopilotAccount, plan.Account, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"--account: --scenario=autopilot runs on the test account '{plan.Account}' only; the value is not shown");
            }

            journey = plan;
            session = session with { Account = plan.Account };
        }
        else if (arguments.Any(a => a.Name == "--journey"))
        {
            errors.Add("--journey: only --scenario=journey takes it");
            journey = null;
        }

        if (o.Scenario != HeadlessScenario.Autopilot && arguments.Any(a => a.Name is "--autopilot-minutes" or "--autopilot-level" or "--autopilot-trip-after-kills" or "--autopilot-test-death"))
        {
            errors.Add("--autopilot-minutes, --autopilot-level, --autopilot-trip-after-kills and --autopilot-test-death: only --scenario=autopilot takes them");
        }

        var recreate = false;
        foreach (var argument in arguments)
        {
            if (argument.Name == "--fighter-recreate")
            {
                recreate = OptionValues.Switch(argument, errors, false);
            }
        }

        if (recreate && !(o.Scenario == HeadlessScenario.FighterBuild && fighter is not null && fighter.Key != FighterTemplate.Hunter.Key))
        {
            errors.Add("--fighter-recreate: only --scenario=fighter-build on the Brawler (test-melee) or the Archer (test-archer) takes it; the Hunter, any other account and every other scenario refuse it");
            recreate = false;
        }

        var fresh = false;
        foreach (var argument in arguments)
        {
            if (argument.Name == "--fresh-character")
            {
                fresh = OptionValues.Switch(argument, errors, false);
            }
        }

        if (fresh && recreate)
        {
            errors.Add("--fresh-character: it excludes --fighter-recreate (the first deletes the live character whatever it is, the second only when its attributes differ)");
            fresh = false;
        }
        else if (fresh && !(o.Scenario is HeadlessScenario.MageBuild or HeadlessScenario.ClimberCreate || (o.Scenario == HeadlessScenario.FighterBuild && fighter is not null && fighter.Key != FighterTemplate.Hunter.Key)))
        {
            errors.Add("--fresh-character: only --scenario=fighter-build on the Brawler (test-melee) or the Archer (test-archer) and --scenario=mage-build on the default account takes it; the Hunter, any other account and every other scenario refuse it");
            fresh = false;
        }

        if (o.Scenario == HeadlessScenario.ClimberCreate)
        {
            // M6 D7: the standing job's Winnie is never re-created without the owner's word, so the re-create is asked for in words; the character is always Winnie (BSD Mage's runs are another scenario's).
            if (!fresh)
            {
                errors.Add("--scenario=climber-create: it deletes the live Winnie and creates a new one, so it needs --fresh-character");
            }

            if (session.Character is not null || o.CreateCharacter is not null)
            {
                errors.Add("--character and --create-character: --scenario=climber-create creates the climber only (the name is fixed); the option is refused");
            }
        }

        var mageLevel = MageTemplate.TargetLevel;
        foreach (var argument in arguments)
        {
            if (argument.Name == "--mage-level")
            {
                var asked = OptionValues.Int(argument, errors, MageTemplate.TargetLevel);
                if (o.Scenario == HeadlessScenario.Journey && journey is not null && !string.Equals(journey.Character, MageTemplate.CharacterName, StringComparison.Ordinal))
                {
                    errors.Add("--mage-level: --scenario=journey takes it with --journey=mage only (the level of the mage's build it verifies)");
                }
                else if (o.Scenario is not (HeadlessScenario.MageBuild or HeadlessScenario.Journey))
                {
                    errors.Add("--mage-level: only --scenario=mage-build and --scenario=journey (M5 7 F14) take it");
                }
                else if (asked < MageTemplate.MinLevel || asked > MageTemplate.TargetLevel)
                {
                    errors.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"--mage-level: '{argument.Value}' is not a level {MageTemplate.MinLevel} to {MageTemplate.TargetLevel} (below {MageTemplate.TargetLevel} the mage is the traditional build without Melee Defense; {MageTemplate.TargetLevel} is the gate's)"));
                }
                else
                {
                    mageLevel = asked;
                }
            }
        }

        return o with { Session = session, MageLevel = mageLevel, Buff = buff, Fight = fight, Death = death, Hunt = hunt, Wayfind = wayfind, Fighter = fighter, FighterRecreate = recreate, FreshCharacter = fresh, Journey = o.Scenario is HeadlessScenario.Journey or HeadlessScenario.Autopilot ? journey : null, Autopilot = autopilot, Arguments = raw, Errors = [.. errors.Distinct(StringComparer.Ordinal)] }; // one line per distinct problem: BuffOptions and FightOptions both parse --cast-mana-* (M5 1 fix B, G3)
    }

    private static string? Required(CommandLineArgument argument, List<string> errors, string what)
    {
        if (string.IsNullOrWhiteSpace(argument.Value))
        {
            errors.Add($"{argument.Name}: needs {what} (--name=VALUE)");
            return null;
        }

        return argument.Value;
    }
}
