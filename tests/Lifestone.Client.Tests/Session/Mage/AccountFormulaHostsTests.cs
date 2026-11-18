using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Diagnostics;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Lifestone.Client.Tests.Session;
using Xunit;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>
/// M6 item 1 w (closes <c>m6-account-formula-callers</c>): the hosts that build a floor pass the account name the server hashes, so the climber (War and Life foci only, Creature trained from the credits) plans its Creature lines on
/// the account's full formula at every floor (h1 and h2 gave the floors the parameter; a null account keeps the old foci rule). One test per host family: the journey's stations and keeper, the autopilot, the buff scenario, the death
/// scenario and the fight scenario. The names are made up; the journey's <see cref="JourneyContext.Account"/> is the secrets KEY, never the login name, so a host's login name must win over it.
/// </summary>
public sealed class AccountFormulaHostsTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const string Login = "host-login-name";
    private const string Key = "host-account-key";

    private static readonly IReadOnlyDictionary<uint, SpellInfo> NoSpells = new Dictionary<uint, SpellInfo>();

    private static JourneyContext ContextOf(string character) => new() { Character = character, Account = Key, Log = new RecordingLogSink(), Travel = new PlaceTravel(), Spells = NoSpells };

    [Fact]
    public void The_journey_factory_names_the_login_the_server_hashes_and_the_autopilot_reads_it_over_the_account_key()
    {
        Assert.Equal(Login, JourneyStations.AccountNameFor(JourneyStations.Create(JourneyPlans.Mage, [], null, Login)));
        var noLogin = JourneyStations.Create(JourneyPlans.Mage, [], null, null);
        var warned = new RecordingLogSink();
        Assert.Null(JourneyStations.AccountNameFor(noLogin, warned)); // M6 1 fix (1-R9): a host with no login hashes no name (the key is not what the server hashes): the foci rule applies
        Assert.Null(JourneyStations.AccountNameFor(noLogin, warned));
        Assert.Single(warned.Entries, l => l.Message.Contains("TODO(j4-kit-account): the host gave no login name; the component floors use the foci rule", StringComparison.Ordinal)); // warned once
        Assert.DoesNotContain(warned.Entries, l => l.Message.Contains(JourneyPlans.Mage.Account, StringComparison.Ordinal)); // no name printed
        Assert.True(JourneyStations.IsHostFactory(noLogin));
        Assert.False(JourneyStations.IsHostFactory(new ScriptedStations()));

        // The autopilot over the factory of a host that knows the login: the buff cycle, the guard and the floor-only lines read the login, not the context's key (h1 passed the key).
        var autopilot = new AutopilotScenario(new AutopilotOptions(), JourneyStations.Create(JourneyPlans.Mage, [], null, Login), ContextOf(MageTemplate.ClimberName));
        Assert.Equal(Login, autopilot.FormulaAccountName);

        // A scripted factory (a test's) has none: the context's account stays.
        var scripted = new AutopilotScenario(new AutopilotOptions(), new ScriptedStations(), ContextOf(MageTemplate.ClimberName));
        Assert.Equal(Key, scripted.FormulaAccountName);
    }

    [Fact]
    public void The_build_station_makes_the_kit_for_the_hosts_login_not_the_key_and_names_the_gap_when_it_has_none_M6_1_fix_1_R9()
    {
        Assert.Equal(Login, new BuildVerifyStation(accountName: Login).KitAccountOf(ContextOf(MageTemplate.ClimberName))); // the login wins over the context's key (the mage build's kit and the fighters' components)

        var log = new RecordingLogSink();
        var context = new JourneyContext { Character = MageTemplate.ClimberName, Account = Key, Log = log, Travel = new PlaceTravel(), Spells = NoSpells };
        Assert.Equal(Key, new BuildVerifyStation().KitAccountOf(context)); // no login given: the key, with the tag logged (TODO(j4-kit-account) narrowed to this case)
        Assert.Single(log.Entries, e => e.Message.Contains("TODO(j4-kit-account): the host gave no login name; the build's kit is made for the account key", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Entries, e => e.Message.Contains(Key, StringComparison.Ordinal)); // the name is never printed
    }

    [Fact]
    public void The_journeys_keeper_carries_the_hosts_login_name_to_its_level_floors()
    {
        var rig = new WorldActionRig();
        var view = new RigView(rig);
        var log = new RecordingLogSink();
        Assert.Equal(Login, JourneyStations.CreateKeeper(JourneyPlans.Mage, view, NoSpells, [], log, Login)?.AccountName);
        Assert.Null(JourneyStations.CreateKeeper(JourneyPlans.Mage, view, NoSpells, [], log)?.AccountName); // a host that gives none keeps the foci rule
    }

    [RequiresDatsFact]
    public void The_buff_scenario_carries_the_account_to_its_cycle_and_its_gear_stage()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells; // the kit reads the profile's spells
        var options = new BuffOptions();
        var byAccount = new BuffScenario(spells, Login, options, new RecordingLogSink(), endSession: false);
        Assert.Equal(Login, byAccount.Gear.Account);
        Assert.Null(options.AccountName); // the caller's options are not changed

        // A host that builds the scenario over a kit it made (the Game's buff leg) carries the name in the options.
        var byKit = new BuffScenario(GearKit.Of([]), NoSpells, new BuffOptions { AccountName = Login }, new RecordingLogSink(), endSession: false);
        Assert.Equal(Login, byKit.Gear.Account);
        Assert.Null(new BuffScenario(GearKit.Of([]), NoSpells, new BuffOptions(), new RecordingLogSink(), endSession: false).Gear.Account); // no account: the foci rule, as before
    }

    [RequiresDatsFact]
    public void The_death_and_fight_scenarios_carry_the_account_to_their_gear_stage()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells;
        var death = new DeathScenario(spells, Login, new DeathOptions(), null, new RecordingLogSink(), endSession: false);
        Assert.Equal(Login, death.Gear?.Account);

        var fight = new FightScenario(spells, Login, new BuffOptions(), new FightOptions(), null, new RecordingLogSink(), endSession: false);
        Assert.Equal(Login, fight.Gear?.Account);
    }

    /// <summary>A scripted factory (a test's): it makes no station and names no login.</summary>
    private sealed class ScriptedStations : IStationFactory
    {
        public IStation Create(StationSpec spec) => throw new NotSupportedException("the test scripts no station");
    }

    /// <summary>The session view over the rig.</summary>
    private sealed class RigView(WorldActionRig rig) : IGameSessionView
    {
        public GameState State => rig.H.State;

        public IEventBus Events => rig.H.R.Bus;

        public IActionApi Actions => rig.Api;

        public PlayerTools Tools => rig.Api.Tools;

        public SessionClock Clock => rig.H.R.Clock;

        public ActionLog ActionLog => rig.H.ActionLog;

        public LinkStats Link => default;
    }
}
