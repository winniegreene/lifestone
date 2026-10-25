using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Interiors;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.CharacterCreation;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using Lifestone.Data.World.Data;
using Lifestone.Net.Session;
using Lifestone.Net.Types;
using Lifestone.Client.Session.Scenarios.Journey;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M3 3.2's death-and-regear scenario (<c>--scenario=death-and-regear</c>, plan section 4.8 lane da; BSD Mage and nobody else): the character is geared and gets its spell bars, is bound at Holtburg's lifestone (D34), goes to the
/// Black Spawn Den, takes its armour off and lies down (D37, M4 P5b, <c>DeathScenario.LieDown.cs</c>: the D33 unbuff suicide is in no path since; <see cref="DeathStages.Unbuff"/> runs only when named), its places going through the travel seam (<see cref="DeathOptions.Travel"/>, <see cref="DeathOptions.StartAt"/>, <see cref="DeathOptions.LeaveOnFailure"/>), waits in non-combat among the Tusker Slaves until they kill it, respawns, is re-geared and logs off. By the Action API and
/// <c>PlayerTools</c> only; what it saw is <see cref="DeathTally"/>'s and the verdict is <see cref="DeathLegVerdict"/>'s.
/// </summary>
/// <remarks>
/// <para><b>Stages</b> (<see cref="Stage"/>, each logged once on entry as <c>[death] stage &lt;name&gt;</c>; <see cref="DeathStages"/> masks the groups): <c>start</c>; <c>gear</c> (<see cref="GearScenario"/>'s steps) and <c>bars</c>
/// (<see cref="MageSpellBars"/>: tab I the buffs, tab II the fight, exactly; only what <see cref="Spellbook.SpellBars"/> differs in is sent, <c>bars=set:&lt;n&gt;</c> or <c>bars=ok</c>); <c>bind-place</c>, <c>bind-teleporting</c>, <c>bind-settle</c>, <c>bind-use</c>, <c>bind-line</c> (the Holtburg point of interest through <c>PlayerTools.Place</c>, the nearest
/// <see cref="WorldObjectTable.LifestoneFlag"/> object within <see cref="LifestoneSearchMetres"/>, <c>IActionApi.Use</c>; ACE's <c>Lifestone.cs</c> 55-75: the Sanctuary motion, then in one action the weenie's UseMessage as a
/// Magic chat line and the stamina halved by a 0x02E9; done when that line, that halving and the UseDone all followed the Use, the line logged as the world database wrote it; the known Magic lines that are not the attune, an
/// enchantment's expiry among them, are ignored and counted); <c>den-place</c>, <c>den-teleporting</c>, <c>den-settle</c> (the floor rule of D12: a settle off the floor fails); <c>unbuff</c>,
/// <c>unbuff-dying</c>, <c>unbuff-arrival</c>, <c>unbuff-vitals</c> (the suicide, its death, the death's teleport, the arrival and ACE's 3 s of vitals; then the den stages again); <c>death-mode</c>, <c>death-wait</c> (non-combat among
/// the tuskers; <see cref="IFightWalk"/> walks to the nearest tusker cell first and on to the next tour cell when no blow came in <see cref="IdleLegSeconds"/> s; the walk is ticked from <c>den-teleporting</c> to <c>death-wait</c>
/// and nowhere else, and a leg still running when the death or the StopAfterHits stop comes is abandoned); <c>respawn</c>, <c>respawn-vitals</c>; M4 2.2 s1: <c>corpse-teleporting</c>, <c>corpse-settle</c>, <c>corpse-seek</c>, <c>corpse-opening</c>, <c>corpse-looting</c>, <c>corpse-closing</c>, <c>corpse-return</c>, <c>corpse-return-settle</c> (the trip back for the mage's own corpse, <c>DeathScenario.Corpse.cs</c>; skipped, named, by <c>--death-recover-corpse off</c> or a failed precondition); <c>regear</c>; M4 3.1 d1: <c>unbuff-decoys</c> (after <c>unbuff-vitals</c>: the decoys the suicide dropped are spawned again, <c>DeathScenario.TopUp.cs</c>), <c>rebuff</c>, <c>rebuff-peace</c>, <c>fight</c> and <c>pretrip-heal</c> (Heal Self VII and regeneration before the trip when the health share is the one failing precondition, <c>DeathScenario.Heal.cs</c>) (the composed inner
/// <see cref="FightScenario"/>; the order, the freeze of the judged measures and the decoy fields are in <c>DeathScenario.Order.cs</c>: after <c>respawn-vitals</c> the order is <c>regear</c>, <c>rebuff</c>, <c>fight</c>, <c>corpse-*</c> with the fight on, M4 2.2's <c>corpse-*</c>, <c>regear</c> with it off); <c>logging-off</c>, <c>quitting</c>, <c>done</c>. With
/// <c>endSession: false</c> (the game's leg, the tests) it ends at <c>done</c> in the world.</para>
/// <para><b>What counts.</b> <c>died</c> is the 0x01AC victim notices: a suicide sends none (<c>Player_Death.cs</c> 299-345), so the suicide's death is <c>suicides=1</c>, in <c>deaths</c> (the NumDeaths delta) and, through its
/// purge, in <c>purged</c>. The tally is not reset after the suicide, so every counter is the run's; <c>respawn</c> reads the last arrival.</para>
/// <para><b>Failure</b> is loud: in the world the run leaves the den first (<see cref="DenExit"/>, whatever <c>endSession</c> is: the game's leg logs off as soon as the scenario returns Failed, and a mage logged off in the den
/// meets the tuskers at its next login), then logs off (<c>endSession: true</c>, as the fight scenario) or ends Failed where it stands. The causes: a lifestone that is not near, a settle off the floor, a suicide that is refused
/// or leaves enchantments, a walk that fails, the <see cref="DeathOptions.Seconds"/> limit (with the hits, evades and the lowest health share, D17), no teleport within <see cref="ArrivalSeconds"/> s of the death, an arrival
/// away from the lifestone the bind used (or, when the run did not bind, with no lifestone near: <c>respawn=elsewhere</c>, the arrival cell named) and a re-gear that leaves the kit not wielded.</para>
/// <para>Per-step code allocates nothing but log lines on events: the handlers are subscribed once and the health share is formatted only when it crosses a 10% step.</para>
/// </remarks>
public sealed partial class DeathScenario : IScenario, IDisposable
{
    /// <summary>The scenario's token (<c>--scenario=death-and-regear</c>).</summary>
    public const string ScenarioName = "death-and-regear";

    /// <summary>The log category of the scenario's lines.</summary>
    public const string LogCategory = "death";

    /// <summary>How long the player is present before the next step, in seconds (the fight scenario's settle).</summary>
    public const double SettleSeconds = 3;

    /// <summary>The limit on a teleport, in seconds (portal space takes 5 s or more).</summary>
    public const double TeleportSeconds = RoutePlay.TeleportSeconds;

    /// <summary>How far from the player in Holtburg the lifestone is looked for, in metres (D34).</summary>
    public const float LifestoneSearchMetres = 60f;

    /// <summary>The limit on the lifestone's Use (the server walks the player to it), in seconds.</summary>
    public const double BindUseSeconds = 60;

    /// <summary>
    /// The limit on the attune after the Use is sent, in seconds of session time. ACE's lifestone chain (<c>Lifestone.cs</c> 44-75) is the stance change, the sound, the Sanctuary motion, THEN the within-use-radius check that
    /// sets the sanctuary, sends the UseMessage as a Magic chat line and halves the stamina (<c>UpdateVital(Stamina, current / 2)</c>, a 0x02E9), or sends WeenieError YouHaveMovedTooFar 0x0498. The UseDone's own timer
    /// (<c>Player_Use.cs</c> 235-238, <c>TryUseItem</c>) runs for the same stance and motion time, so the UseDone can come before or after that action; the bind is done on the line and the halving together, never on the
    /// UseDone alone, and a Magic line that is one of ACE's other Magic lines (<see cref="IsNonAttuneMagicLine"/>) is not the attune.
    /// </summary>
    public const double BindLineSeconds = 15;

    /// <summary>Seconds without a 0x01B2, 0x01B4 or lifestone-protection notice after which the wait moves to the next tour cell.</summary>
    public const double IdleLegSeconds = 60;

    /// <summary>The limit on a death's teleport to arrive, in seconds from the death signal (and from the suicide's confirmation).</summary>
    public const double ArrivalSeconds = 60;

    /// <summary>Seconds the scenario waits after the arrival is present for ACE's 3 s of vitals (<c>Player_Death.cs</c> 262-282) and for the arrival's objects.</summary>
    public const double VitalsSeconds = 5;

    /// <summary>The seconds <see cref="DeathOptions.StopAfterHits"/> waits after the first 0x01B2 with the health share under 100%, so the health updates and the health share are logged.</summary>
    public const double StopAfterHitsSeconds = 3;

    /// <summary>The stop radius of the approach to the lifestone, in metres from its centre, when the lifestone's CreateObject carries no UseRadius: a placeholder (<c>TODO(m3-lifestone-radius)</c>), ACE's own check is a cylinder distance.</summary>
    public const float LifestoneApproachMetres = 1.5f;

    /// <summary>The least stop radius of the approach, in metres (a UseRadius below it is stopped at this).</summary>
    public const float MinApproachMetres = 0.8f;

    /// <summary>The limit on the approach walk, in seconds; past it the Use is sent from where the player stands (ACE's check is the judge).</summary>
    public const double ApproachSeconds = 30;

    /// <summary>How long the re-gear waits for held items with no world object to get one, in seconds, before it fails naming them.</summary>
    public const double RegearSettleSeconds = 5;

    /// <summary>The limit on the spell bars to be known and sent, in seconds.</summary>
    public const double BarsSeconds = MageSpellBars.Seconds;

    /// <summary>The most seconds the vitae stage waits for the vitae to leave the registry after the grant (ACE removes it 2 s after the penalty reaches 1.0, <c>Player_Xp.cs</c> 166-171).</summary>
    public const double VitaeSeconds = 10;

    /// <summary>
    /// The total experience a character needs for a level (null at or above the cap): the vitae stage's level-crossing log (D42: a grant may cross a level; M3 D37's refusal is replaced). When null the session's own table (<see cref="IGameSessionView.XpTable"/>) is used, and when neither is loaded the stage
    /// fails loudly. Hosts set it from <see cref="LevelXpOf"/>.
    /// </summary>
    public Func<int, ulong?>? LevelXp { get; set; }

    /// <summary>The level guard's function over a loaded XpTable.</summary>
    /// <param name="table">The XpTable.</param>
    public static Func<int, ulong?> LevelXpOf(XpTableData table)
    {
        ArgumentNullException.ThrowIfNull(table);
        return level => (uint)level < (uint)table.CharacterLevelXpList.Count ? table.XpForLevel(level) : null;
    }

    private readonly GearKit _kit;
    private readonly GearScenario? _gear;
    private GearScenario? _regear;
    private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
    private readonly DeathOptions _options;
    private readonly IFightWalk? _walk;
    private readonly IApproachWalk? _approach;
    private readonly bool _endSession;
    private readonly DeathStages _stages;
    private readonly double _arrivalSeconds;
    private readonly ILogSink _log;
    private readonly DeathTally _tally = new();
    private readonly TeleportDestination _holtburg;
    private readonly TeleportDestination _den = TeleportCatalog.FromInterior(InteriorLocations.BlackSpawnDenAluvian);
    private MageSpellBars? _bars;
    private ActionHandle? _pending;
    private IDisposable? _movedSubscription;
    private IDisposable? _chatSubscription;
    private IDisposable? _qualitiesSubscription;
    private IGameSessionView? _view;
    private bool _failLogOff;
    private bool _subscribed;
    private bool _disposed;
    private bool _unbuffDone;
    private bool _afterSuicide;
    private int _suicides;
    private int _selfTeleports;
    private int _selfBase;
    private int _teleportMark;
    private int _deathTeleportBase;
    private int _bindLines;
    private int _bindNoise;
    private bool _staminaHalved;
    private uint _staminaBefore;
    private int _blowMark;
    private int _healthBucket = -1;
    private int _legs;
    private double _stageStart;
    private double _settleUntil = double.NaN;
    private double _vitalsUntil = double.NaN;
    private double _useAt = double.NaN;
    private double _stopAt = double.NaN;
    private uint _bindError;
    private bool _useDone;
    private bool _approachLive;
    private DeathRespawn? _respawn;
    private uint _respawnCell;
    private bool _lateChecked;
    private DeathLegMeasures? _frozen;
    private bool _vitaeDone;
    private bool _enteredHere;
    private readonly DenExit _exit;
    private readonly SeamLeg _seam;
    private bool _failLeave;
    private bool _failVitae;
    private bool _vitaeSent;
    private long? _vitaePaid;
    private int? _vitaeAfter;
    private long _vitaeAsked;
    private double _vitaeUntil = double.NaN;
    private ToolHandle<XpResult>? _vitaeHandle;
    private bool _unsettledLogged;
    private readonly List<uint> _scratch = [];
    private float _stopWithin;
    private bool _diedEarly;
    private double _deathStart = double.NaN;
    private double _deathEnd = double.NaN;
    private double _lastBlowAt;
    private double _lastClock;
    private uint _lifestone;
    private CellFrame? _lifestonePosition;
    private bool _bound;
    private bool _gearRestored;
    private string _gearFields = "gear=n/a worn=0";
    private string _regearFields = "gear=n/a worn=0";
    private string _barsNote = "n/a";
    private string _unbuffNote = "n/a";
    private bool _stopped;

    /// <summary>The death scenario.</summary>
    /// <param name="spells">The SpellTable's spells (<c>SpellTableData.Spells</c>): the kit's components are those of the profile's spells.</param>
    /// <param name="account">The account name the kit is built for (hashed by the kit, never stored, logged or printed).</param>
    /// <param name="death">The death loop's options.</param>
    /// <param name="walk">The walk along the den's floor (<see cref="DenWalk"/>; the game's <c>GameFightWalk</c>), or null: the death stage then fails loudly.</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival of the regear's gear scenario.</param>
    /// <param name="endSession">True: log off and quit when done. False: end at <c>done</c> in the world.</param>
    /// <param name="stages">The stages to run (<see cref="DeathStages.All"/>).</param>
    /// <param name="approach">The walk that takes the player to the lifestone before the Use (<see cref="HoltburgApproach"/> in the headless host, dd's game walk in the game), or null: the Use is sent from where the player stands, which ACE answers only when the player is already in range.</param>
    /// <param name="buff">M4 3.1 d1: the buff cycle's options for the rebuff stage, or null for the cycle's defaults.</param>
    /// <param name="fight">M4 3.1 d1: the fight's options for the fight stage, or null for <see cref="FightOptions"/> with <see cref="DeathOptions.FightKills"/> and <see cref="DeathOptions.FightSeconds"/>.</param>
    /// <param name="fightFactory">M4 3.1 d1: how the composed inner fight scenario is made (default <see cref="DefaultFightLeg"/>, the real <see cref="FightScenario"/>); the tests give a scripted one.</param>
    public DeathScenario(IReadOnlyDictionary<uint, SpellInfo> spells, string account, DeathOptions death, IFightWalk? walk, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, DeathStages stages = DeathStages.All, IApproachWalk? approach = null, BuffOptions? buff = null, FightOptions? fight = null, FightLegFactory? fightFactory = null)
        : this(GearKit.ForDeathLoop(spells ?? throw new ArgumentNullException(nameof(spells)), account, death ?? throw new ArgumentNullException(nameof(death))), spells, death, walk, log, arrivalSeconds, endSession, stages, approach, buff, fight, fightFactory)
    {
    }

    /// <summary>The death scenario over a kit already built (the tests').</summary>
    /// <param name="kit">The kit.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="death">The death loop's options.</param>
    /// <param name="walk">The walk along the den's floor, or null.</param>
    /// <param name="log">Where the scenario logs.</param>
    /// <param name="arrivalSeconds">The limit on the arrival of the gear scenarios.</param>
    /// <param name="endSession">True: log off and quit when done. False: end at <c>done</c> in the world.</param>
    /// <param name="stages">The stages to run.</param>
    /// <param name="approach">The walk that takes the player to the lifestone before the Use, or null.</param>
    /// <param name="buff">M4 3.1 d1: the buff cycle's options for the rebuff stage, or null for the cycle's defaults.</param>
    /// <param name="fight">M4 3.1 d1: the fight's options for the fight stage, or null for <see cref="FightOptions"/> with <see cref="DeathOptions.FightKills"/> and <see cref="DeathOptions.FightSeconds"/>.</param>
    /// <param name="fightFactory">M4 3.1 d1: how the composed inner fight scenario is made (default <see cref="DefaultFightLeg"/>), or null.</param>
    public DeathScenario(GearKit kit, IReadOnlyDictionary<uint, SpellInfo> spells, DeathOptions death, IFightWalk? walk, ILogSink log, double arrivalSeconds = LoginScenario.DefaultArrivalSeconds, bool endSession = true, DeathStages stages = DeathStages.All, IApproachWalk? approach = null, BuffOptions? buff = null, FightOptions? fight = null, FightLegFactory? fightFactory = null)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentNullException.ThrowIfNull(death);
        ArgumentNullException.ThrowIfNull(log);
        (_kit, _spells, _options, _walk, _endSession, _stages, _arrivalSeconds, _log) = (kit, spells, death, walk, endSession, stages, arrivalSeconds, log);
        _approach = approach;
        _regearKit = kit.HasDecoys ? kit.WithDecoys([]) : kit; // M4 3.1 d1: the decoys the death dropped are on the corpse; a regear that spawned them would carry a second copy of each
        _buffOptions = buff ?? new BuffOptions();
        var travel = death.Travel ?? new PlaceTravel(); // M4 P5b: one seam per run, shared by the places, the composed fight and the den exit
        _seam = new SeamLeg(travel);
        _fightOptions = fight ?? new FightOptions { Kills = death.FightKills, Seconds = death.FightSeconds };
        if (death.Travel is not null || !death.LeaveOnFailure)
        {
            // An embedded run (a journey): the composed fight goes through the same seam, back to the den after the respawn (Primary Portal Recall), and stays in the den at its end (the corpse trip follows on
            // foot). Every other run keeps its options as given (the fight then makes its own PlaceTravel: today's place).
            _fightOptions = _fightOptions with { Travel = travel, PlacePurpose = TravelPurpose.Ground, LeaveOnFailure = death.LeaveOnFailure, LeaveAtEnd = death.LeaveOnFailure };
        }
        _fightFactory = fightFactory ?? DefaultFightLeg;
        _gear = (stages & DeathStages.Gear) != 0 ? new GearScenario(kit, log, arrivalSeconds, endSession: false, cast: false, spells) : null;
        _exit = new DenExit(log, LogCategory, travel);
        _holtburg = _exit.Destination;
    }

    /// <inheritdoc/>
    public string Name => ScenarioName;

    /// <inheritdoc/>
    public string Stage { get; private set; } = "start";

    /// <inheritdoc/>
    public string? Failure { get; private set; }

    /// <inheritdoc/>
    public double? WhereMaxMetres => null;

    /// <inheritdoc/>
    /// <remarks>Always 0: the scenario's own walker sends movement, but its audit is <c>TODO(m3-fight-walk-audit)</c>.</remarks>
    public long Unmatched => 0;

    /// <summary>What the death did (the victim notices, the vitae, the corpse, the arrival, the blows taken).</summary>
    public DeathTally Tally => _tally;

    /// <summary>The suicides that were seen complete (0 or 1).</summary>
    public int Suicides => _suicides;

    /// <summary>True once the bind took (D34): the lifestone's UseMessage line, the stamina halving and the UseDone all followed the Use.</summary>
    public bool Bound => _bound;

    /// <summary>The guid of the lifestone the bind used, or 0.</summary>
    public uint LifestoneGuid => _lifestone;

    /// <summary>The position the lifestone had when the bind used it, or null.</summary>
    public CellFrame? LifestonePosition => _lifestonePosition;

    /// <summary>The position of the lifestone this run bound at, or null when the run did not bind (the bind stage was off or has not taken): what <c>respawn=</c> is classified against.</summary>
    public CellFrame? BoundLifestone => _bound ? _lifestonePosition : null;

    /// <summary>True when the re-gear left the wand and the five pieces wielded (<c>gear_restored=yes</c>).</summary>
    public bool GearRestored => _gearRestored;

    /// <summary>The spell bar note: <c>set:&lt;n&gt;</c>, <c>ok</c> or <c>n/a</c>.</summary>
    public string BarsNote => _barsNote;

    /// <summary>The bars step (null before the bars stage), for its tabs as the read model held them at its end.</summary>
    public MageSpellBars? Bars => _bars;

    /// <summary>The unbuff note: <c>suicide</c>, <c>none</c> (the mage held only the vitae) or <c>n/a</c>.</summary>
    public string UnbuffNote => _unbuffNote;

    /// <summary>True when <see cref="DeathOptions.StopAfterHits"/> ended the wait (nothing died).</summary>
    public bool StoppedAfterHits => _stopped;

    /// <summary>True when the mage died before <see cref="DeathOptions.StopAfterHits"/> stopped the wait (the respawn is then report-only).</summary>
    public bool DiedBeforeStop => _diedEarly;

    /// <summary>The seconds from the start of the wait among the tuskers to the death (or to now), or NaN before the wait began.</summary>
    public double DeathSeconds => double.IsNaN(_deathStart) ? double.NaN : (double.IsNaN(_deathEnd) ? _lastClock : _deathEnd) - _deathStart;

    /// <summary>The legs the wait walked (the first to the nearest tusker cell, then one a quiet minute).</summary>
    public int Legs => _legs;

    /// <summary>The inner gear scenario of the gear stage (null when <see cref="DeathStages.Gear"/> is off).</summary>
    public GearScenario? Gear => _gear;

    /// <summary>The inner gear scenario of the regear stage (null until that stage began).</summary>
    public GearScenario? Regear => _regear;

    /// <summary>The verdict fields: <see cref="DeathLegVerdict"/>'s (<c>ok= died= suicides= ...</c>) and, at the end, <c>bars= unbuff= bound=</c>. No account or character name is in it.</summary>
    public string VerdictFields()
    {
        var line = Outcome().Line;
        const string Prefix = "bsd-death done ";
        var fields = line.StartsWith(Prefix, StringComparison.Ordinal) ? line[Prefix.Length..] : line;
        return string.Create(CultureInfo.InvariantCulture, $"{fields} bars={_barsNote} unbuff={_unbuffNote} bound={(_bound ? "yes" : "no")}");
    }

    /// <summary>The leg's verdict from the scenario's failure and what the tally saw (<see cref="DeathLegVerdict.Compose"/>); the game's leg passes its frame figures, the headless host has none (<c>n/a</c>).</summary>
    /// <param name="deathFrameMaxMs">The slowest frame from the Dead motion to the first frame after the arrival, or null.</param>
    /// <param name="fightFrameMaxMs">The slowest frame of the unbuffed wait among the tuskers, or null.</param>
    public DeathLegOutcome Outcome(double? deathFrameMaxMs = null, double? fightFrameMaxMs = null)
    {
        if (_frozen is null && _view is { } view && view.State.Phase == SessionPhase.InWorld)
        {
            LogLateRespawnDifference(view); // only in the world: out of it (after the log off) the object table is empty, and a classification against it means nothing
        }

        return OutcomeFromTally(deathFrameMaxMs, fightFrameMaxMs);
    }

    /// <summary>
    /// Takes the death measures once, in the world, when the scenario concludes (<c>Conclude</c>) or fails: the verdict reads these afterwards, never a re-measure. The log off's <c>LeaveWorld</c> clears the object table
    /// before the game's leg (or the headless <c>quitting</c> stage) asks for <see cref="Outcome"/>, so a re-measure after it finds no lifestone object and reads <c>respawn=elsewhere</c> (M3 3.2 review R3: the table is not
    /// forgetting the lifestone; the measure was late). A no-op when the measures are already frozen or the session is not in the world. The frame figures are not part of it (the game passes them to <see cref="Outcome"/>).
    /// </summary>
    /// <param name="session">The session.</param>
    public void FreezeMeasures(IGameSessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (_frozen is not null || session.State.Phase != SessionPhase.InWorld)
        {
            return;
        }

        LogLateRespawnDifference(session);
        _frozen = MeasuresNow(null, null);
        _log.Info(LogCategory, "verdict measures frozen in the world" + (Failure is null ? string.Empty : " (the run failed)"));
    }

    private DeathLegOutcome OutcomeFromTally(double? deathFrameMaxMs, double? fightFrameMaxMs)
    {
        var measures = _frozen is { } frozen
            ? frozen with { DeathFrameMaxMs = deathFrameMaxMs, FightFrameMaxMs = fightFrameMaxMs, VitaePaid = _vitaePaid, VitaeAfter = _vitaeAfter } // the vitae stage runs after the freeze
            : MeasuresNow(deathFrameMaxMs, fightFrameMaxMs);
        return DeathLegVerdict.Compose(Failure, in measures, (_stages & DeathStages.Death) != 0);
    }

    private DeathLegMeasures MeasuresNow(double? deathFrameMaxMs, double? fightFrameMaxMs)
    {
        var worn = ParseWorn(_regearFields);
        var seconds = double.IsNaN(_deathStart) ? (double?)null : DeathSeconds;
        var measures = _view is null
            ? new DeathLegMeasures(_tally.VictimNotices, _suicides, _tally.Deaths, _tally.VitaePercent, DeathRespawn.None, _tally.CorpsesSeen, _tally.Purges, _tally.ProtectedHits, null, _gearRestored, worn, _tally.HitsTaken, _tally.DamageTaken, _tally.Evades, null, seconds, deathFrameMaxMs, fightFrameMaxMs, _tally.StaleCorpses, _tally.Protection, _vitaePaid, _vitaeAfter)
            : DeathLegVerdict.Measure(_tally, _view.State.Objects, _suicides, _gearRestored, worn, seconds, deathFrameMaxMs, fightFrameMaxMs, BoundLifestone);
        if (_atRespawn is { } atRespawn)
        {
            // M4 2.2 s1 and 3.1 d1: the stages after the respawn ran (the regear, the rebuff, the fight, the corpse trip), so M3's tally-derived measures stay as the respawn's vitals wait left them (their blows, teleports and old corpses
            // must not move died=, deaths=, vitae=, vitals_pct=, hits_taken=, health_min_pct= or protection=).
            measures = atRespawn with { GearRestored = _gearRestored, Worn = worn, DeathSeconds = seconds, DeathFrameMaxMs = deathFrameMaxMs, FightFrameMaxMs = fightFrameMaxMs };
        }

        if (_respawn is { } kept)
        {
            measures = measures with { Respawn = kept }; // the respawn stage's classification stands (it was taken at the arrival plus the vitals wait, in the world)
        }

        return measures with { CorpseLooted = CorpseLooted, ItemsRecovered = ItemsRecovered, TripSeconds = TripElapsedSeconds, TripAbort = _tripAbort, Rebuffed = _rebuffed, DenKills = _denKills, Decoys = DecoysCarried, DecoySafe = DecoySafeField, DecoysDropped = DecoysDropped, GearDropped = GearDropped, DecoysRecovered = DecoysRecovered, PretripHeals = _pretripHeals, GearLeft = GearLeft, Undressed = _undressed, LieDown = _lieDown, LieEcho = LieEcho };
    }

    /// <summary>
    /// Once, in the world: if a re-measure now would classify the respawn differently from the respawn stage, say so and why (the tally's arrival cell overwritten by a later position, or, when the run did not bind, no
    /// lifestone object within <see cref="DeathLegVerdict.LifestoneRadiusMetres"/> of the arrival in the object table). Only called in the world: after the log off the table is empty by <c>LeaveWorld</c>, so the check
    /// would always differ. The verdict uses the stage's classification either way.
    /// </summary>
    private void LogLateRespawnDifference(IGameSessionView session)
    {
        if (_lateChecked || _respawn is not { } kept || session.State.Phase != SessionPhase.InWorld)
        {
            return;
        }

        var now = DeathLegVerdict.ClassifyRespawn(_tally, session.State.Objects, BoundLifestone);
        if (now == kept)
        {
            return;
        }

        _lateChecked = true;

        var cause = _tally.ArrivalCell != _respawnCell
            ? $"the tally's arrival changed from cell 0x{_respawnCell:X8} to 0x{_tally.ArrivalCell:X8} (a later position was taken as an arrival; DeathTally.ArrivalFrame was overwritten)"
            : $"the arrival is unchanged (cell 0x{_tally.ArrivalCell:X8}) but, in the world, no lifestone object is within {DeathLegVerdict.LifestoneRadiusMetres:0} m of it in the object table now";
        _log.Warn(LogCategory, $"verdict: a re-measure now would read respawn={now.ToString().ToLowerInvariant()}, the respawn stage read {kept.ToString().ToLowerInvariant()}; the verdict keeps the stage's. Cause: {cause}");
    }

    /// <inheritdoc/>
    public ScenarioStatus Step(IGameSessionView session, double now)
    {
        ArgumentNullException.ThrowIfNull(session);
        _lastClock = session.Clock.Now;
        if (Failure is not null)
        {
            if (_failLeave)
            {
                return FailLeaveStep(session);
            }

            if (_failVitae)
            {
                return VitaeStep(session, session.Clock.Now);
            }

            if (_failLogOff && _pending is { Outcome: { } left })
            {
                _pending = null;
                _failLogOff = false;
                _log.Info(LogCategory, "the failed run logged off: " + left.Kind + (left.Detail is null ? string.Empty : ": " + left.Detail));
            }

            if (_failLogOff)
            {
                return ScenarioStatus.Running;
            }

            Dispose();
            return ScenarioStatus.Failed;
        }

        if (Stage == "done")
        {
            return ScenarioStatus.Succeeded;
        }

        var clock = session.Clock.Now;
        if (_approachLive && Stage != "bind-approach")
        {
            _approach!.Tick(session, now); // follows the server's positions (the teleport places the walker); the leg is ticked by its own stage
        }

        // The walk is ticked in the den stages only (WalksTheDen): a tick after a death's teleport would drive the dead body, or the respawned one at Holtburg, and read a leg's Stuck or Failed as the run's failure in
        // respawn; a tick after the StopAfterHits stop would read the abandoned leg's status. Every exit from the den stages abandons the leg first (DeathWaitStep, UnbuffDyingStep, Fail).
        var walkStatus = _subscribed && WalksTheDen(Stage) ? _walk?.Tick(session, now) ?? FightWalkStatus.Idle : FightWalkStatus.Idle;
        if (walkStatus == FightWalkStatus.Failed)
        {
            return Fail(session, "the walk failed: " + _walk!.Failure);
        }

        switch (Stage)
        {
            case "start":
                if (StartAtProblem() is { } startProblem)
                {
                    return Fail(session, startProblem);
                }

                if ((_stages & DeathStages.Gear) == 0 || _options.StartAt > DeathStages.Gear)
                {
                    // Without the gear stage nothing else enters the world or waits for the player: a headless run of other stages alone (--death-stages=vitae) logs in and enters the mage itself, as the gear scenario's
                    // start does, and every such run waits for the unhidden player and its description before its first stage.
                    if (session.State.Phase == SessionPhase.InWorld && session.State.Self.Guid != 0)
                    {
                        Enter("arriving", clock);
                        return ScenarioStatus.Running;
                    }

                    _pending = session.Actions.Login(ActionSource.Host);
                    Enter("login", clock);
                    return ScenarioStatus.Running;
                }

                return Begin(DeathStages.Gear, session, clock);

            case "login":
                if (_pending is { Outcome: { } login })
                {
                    _pending = null;
                    return login.Kind == ActionOutcomeKind.Confirmed ? ChooseCharacter(session, clock) : Fail(session, "login: " + Describe(login));
                }

                return ScenarioStatus.Running;

            case "entering":
                if (_pending is { Outcome: { } entered })
                {
                    _pending = null;
                    if (entered.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "entering the world: " + Describe(entered));
                    }

                    _enteredHere = true;
                    Enter("arriving", clock);
                }

                return ScenarioStatus.Running;

            case "arriving":
                // The player unhidden (LoginComplete sent and answered) and its description (the qualities, the vitae registry) in: what the gear stage's start gives the other stages.
                // A session already in the world is waited for its description only (the offline stand-in never leaves SelfTravel.TeleportArrived); one entered here also for the unhidden player.
                if (session.State.Self.Qualities.IsKnown && (!_enteredHere || session.State.Self.Travel == SelfTravel.Present))
                {
                    return Begin(DeathStages.Gear, session, clock);
                }

                return clock - _stageStart > _arrivalSeconds
                    ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"arriving: the player was not unhidden with its description within {_arrivalSeconds:0.#} s (travel {session.State.Self.Travel}, description known {session.State.Self.Qualities.IsKnown})"))
                    : ScenarioStatus.Running;

            case "gear":
                switch (_gear!.Step(session, now))
                {
                    case ScenarioStatus.Failed:
                        return Fail(session, "gear: " + _gear.Failure);
                    case ScenarioStatus.Succeeded:
                        _gearFields = _gear.VerdictFields();
                        _log.Info(LogCategory, "gear done (" + _gearFields + ")");
                        RunDecoyCheck(session);
                        Enter("bars", clock);
                        break;
                }

                return ScenarioStatus.Running;

            case "bars":
                return BarsStep(session, clock);

            case "bind-place":
                return PlaceStep(session, clock, _holtburg, "bind-teleporting");
            case "bind-teleporting":
                return TeleportingStep(session, clock, "bind-settle", "bind: ");
            case "bind-settle":
                return BindSettleStep(session, clock);
            case "bind-approach":
                return BindApproachStep(session, now, clock);
            case "bind-use":
                return BindUseStep(session, clock);
            case "bind-line":
                return BindUseStep(session, clock);

            case "den-place":
                return PlaceStep(session, clock, _den, "den-teleporting");
            case "den-teleporting":
                return TeleportingStep(session, clock, "den-settle", "den: ");
            case "den-settle":
                return DenSettleStep(session, clock);

            case "unbuff":
                return UnbuffStep(session, clock);
            case "unbuff-dying":
                return UnbuffDyingStep(session, clock);
            case "unbuff-arrival":
                return ArrivalStep(session, clock, "unbuff: ", "unbuff-vitals");
            case "unbuff-vitals":
                return UnbuffVitalsStep(session, clock);
            case "unbuff-decoys":
                return UnbuffDecoysStep(session, clock);

            case "death-mode":
                return _undressing ? UndressStep(session, clock) : DeathModeStep(session, clock);
            case "death-wait":
                return DeathWaitStep(session, clock, walkStatus);

            case "respawn":
                return ArrivalStep(session, clock, "respawn: ", "respawn-vitals");
            case "respawn-vitals":
                return RespawnVitalsStep(session, clock);

            case "corpse-teleporting":
                return CorpseTeleportingStep(session, clock);
            case "corpse-settle":
                return CorpseSettleStep(session, clock);
            case "corpse-seek":
                return CorpseSeekStep(session, clock);
            case "corpse-opening":
                return CorpseOpeningStep(session, clock);
            case "corpse-looting":
                return CorpseLootingStep(session, clock);
            case "corpse-closing":
                return CorpseClosingStep(session, clock);
            case "corpse-return":
                return CorpseReturnStep(session, clock);
            case "corpse-return-settle":
                return CorpseReturnSettleStep(session, clock);

            case "regear-settle":
                return RegearSettleStep(session, clock);
            case "regear":
                return RegearStep(session, now, clock);

            case "pretrip-heal":
                return PretripHealStep(session, clock);

            case "rebuff":
                return RebuffStep(session, now);
            case "rebuff-peace":
                return RebuffPeaceStep(session, clock);
            case "fight":
                return FightStep(session, now);

            case "leave-den":
                return LeaveDenStep(session, clock);

            case "vitae":
                return VitaeStep(session, clock);

            case "logging-off":
                if (_pending is { Outcome: { } loggedOff })
                {
                    _pending = null;
                    if (loggedOff.Kind != ActionOutcomeKind.Confirmed)
                    {
                        return Fail(session, "logging off: " + Describe(loggedOff));
                    }

                    _pending = session.Actions.Quit(ActionSource.Host);
                    Enter("quitting", clock);
                }

                return ScenarioStatus.Running;

            case "quitting":
                if (_pending is { Outcome: { } quit })
                {
                    _pending = null;
                    if (quit.Kind != ActionOutcomeKind.Sent)
                    {
                        return Fail(session, "quitting: " + Describe(quit));
                    }

                    _log.Info(LogCategory, "verdict " + VerdictFields());
                    Stage = "done";
                    Dispose();
                    return ScenarioStatus.Succeeded;
                }

                return ScenarioStatus.Running;

            default:
                return Fail(session, $"TODO(state): unknown death stage '{Stage}'");
        }
    }

    /// <summary>Stops following the session (the tally, the handlers, the walk).</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tally.Dispose();
        _movedSubscription?.Dispose();
        _chatSubscription?.Dispose();
        _qualitiesSubscription?.Dispose();
        _motionSubscription?.Dispose();
        _seam.Abandon();
        _approach?.Dispose();
        _inner?.Dispose();
        _walk?.Dispose();
    }

    // ------------------------------------------------------------------------------------------------ order

    /// <summary>Enters the first enabled stage at or after <paramref name="from"/>; past the last one the run concludes.</summary>
    private ScenarioStatus Begin(DeathStages from, IGameSessionView session, double clock)
    {
        if (_options.StartAt is { } startAt && from < startAt)
        {
            from = startAt; // M4 P5b: an embedded run begins at its stage (the stages before it were done by the journey's own stations)
        }

        if (from <= DeathStages.Gear && (_stages & DeathStages.Gear) != 0)
        {
            Enter("gear", clock);
            return ScenarioStatus.Running;
        }

        if (from <= DeathStages.Bind && (_stages & DeathStages.Bind) != 0)
        {
            Enter("bind-place", clock);
            return ScenarioStatus.Running;
        }

        if (from <= DeathStages.Den && (_stages & DeathStages.Den) != 0)
        {
            Enter("den-place", clock);
            return ScenarioStatus.Running;
        }

        if (from <= DeathStages.Unbuff && (_stages & DeathStages.Unbuff) != 0 && !_unbuffDone)
        {
            _unbuffDone = true;
            Enter("unbuff", clock);
            return ScenarioStatus.Running;
        }

        if (from <= DeathStages.Death && (_stages & DeathStages.Death) != 0)
        {
            Subscribe(session);
            SubscribeMotion(session);
            if (_options.StartAt is not null)
            {
                _walk?.Prepare(session, clock); // the den's place did not run (an embedded run): the walk is made here; a walker that needs a teleport to be placed says so at its first leg
                if (_walk?.Failure is { } prepareFailure)
                {
                    return Fail(session, "death: " + prepareFailure);
                }
            }

            _pending = null;
            Enter("death-mode", clock);
            return ScenarioStatus.Running;
        }

        if (from <= DeathStages.Regear && RegearOn)
        {
            StartRegear(clock);
            return ScenarioStatus.Running;
        }

        return Conclude(session, clock);
    }

    /// <summary>M4 P5b: a <see cref="DeathOptions.StartAt"/> that is not one of the linear stages (gear, bind, den, death, regear), or one the stage mask leaves out, is refused loudly; null when it is fine or not given.</summary>
    private string? StartAtProblem() =>
        _options.StartAt switch
        {
            null => null,
            DeathStages.Gear or DeathStages.Bind or DeathStages.Den or DeathStages.Death or DeathStages.Regear when (_stages & _options.StartAt.Value) != 0 => null,
            DeathStages.Gear or DeathStages.Bind or DeathStages.Den or DeathStages.Death or DeathStages.Regear => $"start_at: the stage {_options.StartAt.Value.ToString().ToLowerInvariant()} is not in this run's stages ({_stages.ToString().ToLowerInvariant()})",
            _ => $"start_at: {_options.StartAt.Value.ToString().ToLowerInvariant()} is not a stage a run can begin at (gear, bind, den, death, regear); TODO(m4-death-start-at)",
        };

    // ------------------------------------------------------------------------------------------------ the spell bars

    private ScenarioStatus BarsStep(IGameSessionView session, double clock)
    {
        _bars ??= new MageSpellBars(_log);
        switch (_bars.Step(session, clock))
        {
            case ScenarioStatus.Failed:
                return Fail(session, "bars: " + _bars.Failure);
            case ScenarioStatus.Succeeded:
                _barsNote = _bars.Note;
                return Begin(DeathStages.Bind, session, clock);
            default:
                return ScenarioStatus.Running;
        }
    }

    // ------------------------------------------------------------------------------------------------ teleports

    private ScenarioStatus PlaceStep(IGameSessionView session, double clock, TeleportDestination destination, string next)
    {
        Subscribe(session);
        if (ReferenceEquals(destination, _holtburg))
        {
            _approach?.Prepare(session, clock);
            if (_approach?.Failure is { } approachFailure)
            {
                _log.Warn(LogCategory, "bind: the approach walk could not be prepared (" + approachFailure + "); the Use will be sent from where the player stands");
            }
            else
            {
                _approachLive = _approach is not null;
            }
        }

        if (ReferenceEquals(destination, _den))
        {
            _walk?.Prepare(session, clock);
            if (_walk?.Failure is { } walkFailure)
            {
                return Fail(session, "den: " + walkFailure);
            }
        }

        _teleportMark = _selfTeleports;
        _pending = null;
        _seam.Start(session, new TravelRequest(destination, ReferenceEquals(destination, _holtburg) ? TravelPurpose.Bind : TravelPurpose.Ground), clock); // M4 P5b: the travel seam (PlaceTravel by default)
        Enter(next, clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus TeleportingStep(IGameSessionView session, double clock, string next, string what)
    {
        var travel = _seam.Step(session, clock);
        if (travel == TravelStatus.Failed)
        {
            return Fail(session, what + "sending the player there: " + _seam.Failure);
        }

        // The arrival contract (IJourneyTravel): a place's arrival is today's check (the teleport seen, present); a run's, a recall's or a None's is authoritative.
        if (travel == TravelStatus.Arrived && (_seam.Authoritative || (_selfTeleports > _teleportMark && session.State.Self.Travel == SelfTravel.Present)))
        {
            _settleUntil = clock + SettleSeconds;
            Enter(next, clock);
            return ScenarioStatus.Running;
        }

        return _seam.Means == TravelMeans.Place && clock - _stageStart > TeleportSeconds
            ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"{what}the player did not arrive and become present within {TeleportSeconds:0.#} s (teleports {_selfTeleports - _teleportMark}, travel {session.State.Self.Travel})"))
            : ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ the bind (D34)

    private ScenarioStatus BindSettleStep(IGameSessionView session, double clock)
    {
        if (clock < _settleUntil)
        {
            return ScenarioStatus.Running;
        }

        if (session.State.Self.Position is not { } here)
        {
            return Fail(session, "bind: the player has no position");
        }

        if (!session.State.Objects.NearestWithFlags(WorldObjectTable.LifestoneFlag, in here, LifestoneSearchMetres, out var guid))
        {
            return Fail(session, string.Create(CultureInfo.InvariantCulture, $"bind: no lifestone (an object with the BF_LIFESTONE description flag) within {LifestoneSearchMetres:0} m of the player in cell 0x{here.CellId:X8}"));
        }

        _lifestone = guid;
        _lifestonePosition = session.State.Objects.TryGet(guid, out var stone) ? stone.Position : null;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"bind: using the lifestone 0x{guid:X8} (cell {(_lifestonePosition is { } p ? "0x" + p.CellId.ToString("X8", CultureInfo.InvariantCulture) : "unknown")})"));
        var radius = stone is null ? null : stone.Weenie.UseRadius;
        _stopWithin = Math.Max(radius ?? LifestoneApproachMetres, MinApproachMetres);
        var distance = Distance(session);
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"bind: the player is {distance:0.0} m from the lifestone; its UseRadius is {(radius is { } r ? r.ToString("0.0##", CultureInfo.InvariantCulture) + " (from its CreateObject)" : "not sent (placeholder " + LifestoneApproachMetres.ToString("0.0", CultureInfo.InvariantCulture) + " m, TODO(m3-lifestone-radius))")}; the approach stops within {_stopWithin:0.0##} m of its centre"));
        if (!(distance > _stopWithin) || _lifestonePosition is null)
        {
            return SendUse(session, clock);
        }

        if (_approach is null)
        {
            _log.Warn(LogCategory, "bind: no approach walk was given (TODO(m3-bind-approach)): the Use is sent from where the player stands; ACE answers it only when the player follows its MoveToObject or is in range");
            return SendUse(session, clock);
        }

        if (!_approach.Begin(session, clock, _lifestonePosition.Value, _stopWithin, out var leg))
        {
            // Not a failure of the bind: ACE's answer decides, and the 15 s line check says so loudly if the player was out of range.
            _log.Warn(LogCategory, "bind: no approach walk could start (" + _approach.Failure + "); the Use is sent from where the player stands");
            return SendUse(session, clock);
        }

        _log.Info(LogCategory, "bind: walking to the lifestone: " + leg);
        Enter("bind-approach", clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus BindApproachStep(IGameSessionView session, double now, double clock)
    {
        var status = _approach!.Tick(session, now);
        var distance = Distance(session);
        if (status == FightWalkStatus.Walking && clock - _stageStart <= ApproachSeconds)
        {
            return ScenarioStatus.Running;
        }

        _approach.Abandon();
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"bind: the approach ended ({(status == FightWalkStatus.Walking ? "after the " + ApproachSeconds.ToString("0", CultureInfo.InvariantCulture) + " s limit" : status.ToString().ToLowerInvariant())}); the player is {distance:0.0} m from the lifestone{(status == FightWalkStatus.Failed ? "; the walk said: " + _approach.Failure : string.Empty)}"));
        if (status != FightWalkStatus.Arrived)
        {
            _log.Warn(LogCategory, "bind: the player did not reach the stop radius; the Use is sent from here and ACE's range check is the judge");
        }

        return SendUse(session, clock);
    }

    private ScenarioStatus SendUse(IGameSessionView session, double clock)
    {
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"bind: using the lifestone from {Distance(session):0.0} m"));
        _useAt = clock;
        _bindError = 0;
        _bindLines = 0;
        _bindNoise = 0;
        _staminaHalved = false;
        _staminaBefore = session.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out _) ? stamina : 0;
        _useDone = false;
        _pending = session.Actions.Use(_lifestone, ActionSource.Host);
        Enter("bind-use", clock);
        return ScenarioStatus.Running;
    }

    /// <summary>
    /// Waits for ACE's two attune signals, the UseMessage line (a Magic chat line that is not one of <see cref="IsNonAttuneMagicLine"/>'s) and the stamina halving (a stamina 0x02E9 that drops the current to about half:
    /// <c>Lifestone.cs</c> 64-69 sends both in one action), up to <see cref="BindLineSeconds"/> s, and for the UseDone; a WeenieError after the Use (YouHaveMovedTooFar 0x0498 is ACE's own, <c>Lifestone.cs</c> 72) or the timeout
    /// fails loudly, the timeout naming which signal came. ACE moves a player who is out of the use radius itself (<c>Player_Use.cs</c> <c>CreateMoveToChain</c>) before it calls the lifestone's <c>ActOnUse</c>, but a player it
    /// cannot move (a client that does not follow its MoveToObject) is the case the timeout names, with the distance at the Use and now.
    /// </summary>
    private ScenarioStatus BindUseStep(IGameSessionView session, double clock)
    {
        if (_pending is { Outcome: { } used })
        {
            _pending = null;
            if (used.Kind != ActionOutcomeKind.Confirmed)
            {
                return Fail(session, "bind: using the lifestone: " + Describe(used));
            }

            _useDone = true;
            if (Stage == "bind-use")
            {
                Enter("bind-line", clock);
            }
        }

        if (_bindError != 0)
        {
            return Fail(session, string.Create(CultureInfo.InvariantCulture, $"bind: the bind did not take: the server answered WeenieError 0x{_bindError:X4}{(_bindError == YouHaveMovedTooFar ? " (YouHaveMovedTooFar: the player was outside the lifestone's use radius when ACE checked)" : string.Empty)} after the Use; the player is {Distance(session):0.0} m from the lifestone"));
        }

        var attuned = _bindLines > 0 && _staminaHalved;
        if (attuned && _useDone)
        {
            _bound = true;
            _approachLive = false;
            _approach?.Dispose();
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"bound at the lifestone 0x{_lifestone:X8} after {clock - _useAt:0.0} s (the attune line and the stamina halving followed the Use; {_bindLines} attune line(s), {_bindNoise} other Magic line(s) ignored; the player is {Distance(session):0.0} m from it)"));
            return Begin(DeathStages.Den, session, clock);
        }

        if (!attuned && clock - _useAt > BindLineSeconds)
        {
            var missing = _bindLines == 0 && !_staminaHalved
                ? "no attune line (a Magic chat line) and no stamina halving (0x02E9)"
                : _bindLines == 0
                    ? "the stamina halving came but no attune line (a Magic chat line)"
                    : "the attune line came but the stamina was not halved (ACE halves it in the same action, Lifestone.cs 64-69)";
            return Fail(session, string.Create(CultureInfo.InvariantCulture, $"bind: the bind did not take: {missing} within {BindLineSeconds:0} s of the Use (UseDone {(_useDone ? "seen" : "not seen")}; {_bindNoise} other Magic line(s) ignored); the player is {Distance(session):0.0} m from the lifestone (ACE sets the sanctuary only when the player is within the use radius after the Sanctuary motion, Lifestone.cs 44-75; it moves a player only when the client follows its MoveToObject)"));
        }

        return clock - _stageStart > BindUseSeconds
            ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"bind: the lifestone's Use did not finish within {BindUseSeconds:0} s"))
            : ScenarioStatus.Running;
    }

    /// <summary>
    /// The WeenieErrors that fail the bind when they come after the Use (ACE <c>WeenieError.cs</c>): YoureTooBusy 0x001D, ActionCancelled 0x0036, ObjectGone 0x0037, YouHaveMovedTooFar 0x0498 (the lifestone's range check, <c>Lifestone.cs</c> 66),
    /// YouWanderedTooFarToLinkWithLifestone 0x049C and YouCannotUseThatItem 0x058D. Every other WeenieError (the chat channel joins, 0x051C) is logged and ignored.
    /// </summary>
    public static readonly uint[] BindFailureCodes = [0x001D, 0x0036, 0x0037, 0x0498, 0x049C, 0x058D];

    /// <summary>The ACE WeenieError the lifestone answers when the player is out of range after the motion (<c>YouHaveMovedTooFar</c>).</summary>
    private const uint YouHaveMovedTooFar = 0x0498;

    /// <summary>The straight-line distance in metres from the player to the lifestone the bind uses, or NaN when either position is unknown.</summary>
    private double Distance(IGameSessionView session)
    {
        if (session.State.Self.Position is not { } here || !session.State.Objects.TryGet(_lifestone, out var stone) || stone.Position is not { } there)
        {
            return double.NaN;
        }

        return here.ToGlobal().DistanceTo(there.ToGlobal());
    }

    // ------------------------------------------------------------------------------------------------ the den

    private ScenarioStatus DenSettleStep(IGameSessionView session, double clock)
    {
        if (clock < _settleUntil)
        {
            return ScenarioStatus.Running;
        }

        var cell = session.State.Self.Position?.CellId ?? 0;
        if (!BlackSpawnDen.IsFloorCell(cell))
        {
            // D12: the floor rule; a settle that ends elsewhere is a loud failure, not a note.
            return Fail(session, $"den: the player settled in cell 0x{cell:X8}, which is not a floor cell of the den (floor cell no); the wait would start off the floor");
        }

        _log.Info(LogCategory, $"den: settled in cell 0x{cell:X8}; floor cell yes");
        return Begin(_afterSuicide ? DeathStages.Death : DeathStages.Unbuff, session, clock);
    }

    // ------------------------------------------------------------------------------------------------ the unbuff (D33)

    private ScenarioStatus UnbuffStep(IGameSessionView session, double clock)
    {
        var buffs = CountBuffs(session.State.Self.Qualities);
        if (buffs == 0)
        {
            _unbuffNote = "none";
            _log.Info(LogCategory, "unbuff: the mage holds no enchantment a death would purge (the vitae, an item's lasting spell and a cooldown stay); no suicide");
            return Begin(DeathStages.Death, session, clock);
        }

        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"unbuff: the mage is buffed ({buffs} enchantments); a suicide purges them as every death does (ACE Player_Death.cs 211-216)"));
        _deathTeleportBase = _tally.DeathTeleports;
        _selfBase = _selfTeleports;
        _pending = session.Actions.Suicide(ActionSource.Host);
        Enter("unbuff-dying", clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus UnbuffDyingStep(IGameSessionView session, double clock)
    {
        if (_tally.VictimNotices > 0)
        {
            // The tuskers were quicker than the suicide's five lines (ACE's 15 s, Player_Death.cs 330-345): their kill purges the enchantments as a suicide's death does (211-216), so it is the run's death and the suicide is
            // not waited for (ACE refuses it as YoureTooBusy while the player is dead or teleporting). The wait among the tuskers is skipped; the respawn is read as usual.
            _unbuffNote = "killed";
            _pending = null;
            _walk?.Abandon(); // no leg runs before the wait, but the walk is left with nothing to drive through the respawn
            _deathStart = _stageStart;
            _deathEnd = clock;
            _log.Warn(LogCategory, "unbuff: the tuskers killed the buffed mage before the suicide completed; their kill purges the enchantments and is the run's death (died=1)");
            Enter("respawn", clock);
            return ScenarioStatus.Running;
        }

        if (_pending is not { Outcome: { } suicide })
        {
            return ScenarioStatus.Running;
        }

        _pending = null;
        if (suicide.Kind != ActionOutcomeKind.Confirmed)
        {
            return Fail(session, "unbuff: the suicide did not complete: " + Describe(suicide));
        }

        _suicides++;
        _log.Info(LogCategory, "unbuff: the suicide's death is seen (health 0 and NumDeaths up); it has no victim notice and does not count as died");
        Enter("unbuff-arrival", clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus UnbuffVitalsStep(IGameSessionView session, double clock)
    {
        if (clock < _vitalsUntil)
        {
            return ScenarioStatus.Running;
        }

        var left = CountBuffs(session.State.Self.Qualities);
        if (left != 0)
        {
            return Fail(session, string.Create(CultureInfo.InvariantCulture, $"unbuff: {left} enchantments that a death purges remain after the suicide's death (ACE's RemoveAllEnchantments, EnchantmentManager.cs 358-365, keeps only the lasting item spells and the cooldowns)"));
        }

        _unbuffNote = "suicide";
        _afterSuicide = true;
        _ = _tally.SampleVitalsAfterArrival(); // the suicide's own restore, read at the end of its wait as the respawn's is (M4 2.2 fix R1)
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"unbuff: the mage is unbuffed (vitae {_tally.VitaePercent}%, {VitalsText(_tally)})"));
        return BeginDecoyTopUp(session, clock); // M4 3.1 d1 F2: the suicide dropped items too: the decoys the pack lacks are spawned again before the den stages run again
    }

    /// <summary>
    /// The vitals after an arrival for the log: the judged share (each vital at the end of the vitals wait) and, report-only, the first 0x02E9 of each vital after the arrival beside the wait-end values, so a run shows when
    /// a regeneration or drain update came before ACE's 75% (M4 2.2 fix R1). A figure not read is <c>n/a</c>.
    /// </summary>
    internal static string VitalsText(DeathTally tally) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"vitals {Share(tally.VitalsAfterArrivalPercent)}% (first health={Share(tally.FirstHealthAfterArrivalPercent)} stamina={Share(tally.FirstStaminaAfterArrivalPercent)} mana={Share(tally.FirstManaAfterArrivalPercent)}; at_wait_end health={Share(tally.HealthAfterArrivalPercent)} stamina={Share(tally.StaminaAfterArrivalPercent)} mana={Share(tally.ManaAfterArrivalPercent)})");

    private static string Share(double percent) => double.IsNaN(percent) ? "n/a" : percent.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>
    /// The enchantments in the registry that a death's purge removes. ACE's <c>Die</c> sends the 0x02C6 and calls <c>RemoveAllEnchantments</c> (<c>Player_Death.cs</c> 211-216; <c>EnchantmentManager.cs</c> 358-365), which
    /// keeps every entry with a duration of -1 (an item's spell on its wielder; the vitae, spell 666, is one) and every spell id above <c>short.MaxValue</c> (a cooldown: <c>StartCooldown</c> 284-300 numbers it from
    /// <c>SpellCategory_Cooldown</c> 0x8000); the reducer keeps the same on 0x02C6 (<see cref="EnchantmentRules.Purge"/>). Counting one of those would send a suicide that cannot clear it and then fail the run.
    /// </summary>
    private static int CountBuffs(PlayerQualities q)
    {
        var active = q.ActiveEnchantments;
        var n = 0;
        for (var i = 0; i < active.Length; i++)
        {
            var entry = active[i];
            if (entry.SpellId != DeathTally.VitaeSpell && !entry.IsPermanent && entry.SpellId <= short.MaxValue)
            {
                n++;
            }
        }

        return n;
    }

    // ------------------------------------------------------------------------------------------------ the death

    private ScenarioStatus DeathModeStep(IGameSessionView session, double clock)
    {
        if (_pending is null)
        {
            if (session.State.Self.Qualities.CombatMode == CombatModes.NonCombat)
            {
                return BeginUndress(session, clock);
            }

            _pending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
            return ScenarioStatus.Running;
        }

        if (_pending.Outcome is { } mode)
        {
            _pending = null;
            if (!mode.Succeeded)
            {
                return Fail(session, "death: the non-combat mode was not entered: " + Describe(mode));
            }

            return BeginUndress(session, clock);
        }

        return ScenarioStatus.Running;
    }

    private ScenarioStatus BeginDeathWait(IGameSessionView session, double clock)
    {
        if (_walk is null && _options.StartAt is null)
        {
            return Fail(session, "death: the wait must walk to the tuskers but has no walker; TODO(m3-fight-no-walker)");
        }

        CountDecoysAtDeath(session);
        _deathStart = clock;
        _lastBlowAt = clock;
        _blowMark = Blows();
        _healthBucket = -1;
        _deathTeleportBase = _tally.DeathTeleports;
        _selfBase = _selfTeleports;
        if (_walk is null)
        {
            // An embedded run with no walker (a journey whose den fight left the mage among the tuskers): the wait lies down where the player stands. Loud: no leg moves it on when no blow comes.
            _log.Warn(LogCategory, $"death: no walker in this embedded run: the mage lies down where it stands among the tuskers, for up to {_options.Seconds:0.#} s, and is never moved on (TODO(m4-death-walkless-wait))");
            Enter("death-wait", clock);
            return ScenarioStatus.Running;
        }

        if (!_walk.BeginLeg(session, clock, out var leg))
        {
            return Fail(session, "death: no walk could be planned to a tusker cell: " + _walk.Failure);
        }

        StoodUp();
        _legs++;
        _log.Info(LogCategory, $"death: walking to the nearest tusker cell: {leg}; then non-combat among the tuskers for up to {_options.Seconds:0.#} s");
        Enter("death-wait", clock);
        return ScenarioStatus.Running;
    }

    private int Blows() => _tally.HitsTaken + _tally.Evades + _tally.ProtectedHits;

    /// <summary>The stages that tick the walk: from the den's teleport (the teleport's position places the headless walker) to the end of the wait among the tuskers. No other stage reads a leg, and none may drive one.</summary>
    private static bool WalksTheDen(string stage) => stage is "den-teleporting" or "den-settle" or "unbuff" or "unbuff-dying" or "death-mode" or "death-wait";

    private ScenarioStatus DeathWaitStep(IGameSessionView session, double clock, FightWalkStatus walkStatus)
    {
        var elapsed = clock - _deathStart;
        LogHealth(session.State.Self.Qualities);
        if (_tally.VictimNotices > 0 || _tally.Deaths > _suicides)
        {
            _walk?.Abandon(); // a leg still running drives a dead body: its keys are released here, and the respawn stages never tick it (a death in the middle of a leg is not a walk failure)
            _deathEnd = clock;
            ReportLieEcho();
            _diedEarly = _options.StopAfterHits;
            if (_diedEarly)
            {
                _log.Warn(LogCategory, "death: the mage died before the stop (StopAfterHits): the death is reported and the respawn is not judged");
            }

            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"death: the tuskers killed the player after {elapsed:0} s (victim notices {_tally.VictimNotices}, hits {_tally.HitsTaken}, evades {_tally.Evades}, protected {_tally.ProtectedHits})"));
            if (_tally.VictimNotices == 0)
            {
                _log.Warn(LogCategory, "death: NumDeaths went up with no victim notice (0x01AC); died will read 0");
            }

            Enter("respawn", clock);
            return ScenarioStatus.Running;
        }

        if (_options.StopAfterHits && double.IsNaN(_stopAt) && _tally.HitsTaken >= 1 && HealthUnderFull(session.State.Self.Qualities))
        {
            _stopAt = clock + StopAfterHitsSeconds;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"death: the first blow landed and the health share is under 100%; stopping in {StopAfterHitsSeconds:0} s"));
        }

        if (!double.IsNaN(_stopAt) && clock >= _stopAt)
        {
            _stopped = true;
            _deathEnd = clock;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"death: stopping after {elapsed:0} s among the tuskers (hits {_tally.HitsTaken}, damage {_tally.DamageTaken}, lowest health {_tally.HealthMinPercent:0}%)"));
            _walk?.Abandon();
            return Begin(DeathStages.Regear, session, clock);
        }

        if (elapsed > _options.Seconds)
        {
            return Fail(
                session,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"death: {_options.Seconds:0.#} s passed and the tuskers did not kill the player (hits={_tally.HitsTaken} evades={_tally.Evades} protected={_tally.ProtectedHits} damage_taken={_tally.DamageTaken} health_min_pct={_tally.HealthMinPercent:0.0} legs={_legs})"));
        }

        LieStep(session, clock, walkStatus); // D37: lie down once the walk has stopped
        var blows = Blows();
        if (blows != _blowMark || walkStatus == FightWalkStatus.Walking)
        {
            _blowMark = blows;
            _lastBlowAt = clock;
        }
        else if (_walk is not null && _liePress is null && clock - _lastBlowAt >= IdleLegSeconds && walkStatus != FightWalkStatus.Walking)
        {
            _lastBlowAt = clock;
            if (!_walk.BeginLeg(session, clock, out var leg))
            {
                return Fail(session, "death: no walk could be planned to the next tusker cell: " + _walk.Failure);
            }

            StoodUp();
            _legs++;
            _log.Info(LogCategory, $"death: no blow in {IdleLegSeconds:0} s; moving on: {leg}");
        }

        return ScenarioStatus.Running;
    }

    private static bool HealthUnderFull(PlayerQualities q) => q.TryGetVitalLevels(1, out var current, out var max) && max != 0 && current < max;

    private void LogHealth(PlayerQualities q)
    {
        if (!q.TryGetVitalLevels(1, out var current, out var max) || max == 0)
        {
            return;
        }

        var bucket = (int)(current * 10.0 / max);
        if (bucket == _healthBucket)
        {
            return;
        }

        _healthBucket = bucket;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"health {current * 100.0 / max:0}% ({current} of {max})"));
    }

    // ------------------------------------------------------------------------------------------------ the arrival of a death's teleport

    /// <summary>Waits for the teleport of the death after <c>_deathTeleportBase</c> to arrive and the player to be present, then <see cref="VitalsSeconds"/> more.</summary>
    private ScenarioStatus ArrivalStep(IGameSessionView session, double clock, string what, string next)
    {
        if (_tally.DeathTeleports > _deathTeleportBase && _selfTeleports > _selfBase && session.State.Self.Travel == SelfTravel.Present)
        {
            _vitalsUntil = clock + VitalsSeconds;
            Enter(next, clock);
            return ScenarioStatus.Running;
        }

        if (_diedEarly && clock - _stageStart > ArrivalSeconds)
        {
            _log.Warn(LogCategory, "respawn: no arrival within " + ArrivalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s of the early death (not judged under StopAfterHits)");
            return Begin(DeathStages.Regear, session, clock);
        }

        return clock - _stageStart > ArrivalSeconds
            ? Fail(session, string.Create(CultureInfo.InvariantCulture, $"{what}respawn=none: no death teleport and arrival within {ArrivalSeconds:0} s (teleports announced {_tally.DeathTeleports - _deathTeleportBase}, positions {_selfTeleports - _selfBase}, travel {session.State.Self.Travel})"))
            : ScenarioStatus.Running;
    }

    private ScenarioStatus RespawnVitalsStep(IGameSessionView session, double clock)
    {
        if (clock < _vitalsUntil)
        {
            return ScenarioStatus.Running;
        }

        var bound = BoundLifestone;
        var respawn = DeathLegVerdict.ClassifyRespawn(_tally, session.State.Objects, bound);
        _respawn = respawn; // the verdict keeps this classification and this arrival: a later re-measure never replaces it (the tally's arrival can be overwritten by a later position; after the log off the object table is empty)
        _respawnCell = _tally.ArrivalCell;
        _ = _tally.SampleVitalsAfterArrival(); // M4 2.2 fix R1: the judged vitals are each vital's value at the END of this wait (ACE's 75% comes 3 s after the teleport, after any regeneration or drain tick), not the first update
        _log.Info(
            LogCategory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"respawn: {respawn.ToString().ToLowerInvariant()} (arrival cell 0x{_tally.ArrivalCell:X8}, classified against {(bound is { } b ? "the lifestone the bind used in cell 0x" + b.CellId.ToString("X8", CultureInfo.InvariantCulture) : "any lifestone object: this run did not bind")}, vitae {_tally.VitaePercent}%, {VitalsText(_tally)}, corpses {_tally.CorpsesSeen}, purges {_tally.Purges}, protected {_tally.ProtectedHits})"));
        if (_diedEarly)
        {
            _log.Info(LogCategory, "respawn: " + respawn.ToString().ToLowerInvariant() + " (report-only under StopAfterHits)");
            return Begin(DeathStages.Regear, session, clock);
        }

        if (respawn != DeathRespawn.Lifestone)
        {
            return Fail(session, string.Create(CultureInfo.InvariantCulture, $"respawn: respawn={respawn.ToString().ToLowerInvariant()}: the player arrived in cell 0x{_tally.ArrivalCell:X8} {(bound is { } at ? "more than " + DeathLegVerdict.LifestoneRadiusMetres.ToString("0", CultureInfo.InvariantCulture) + " m from the lifestone the bind used (cell 0x" + at.CellId.ToString("X8", CultureInfo.InvariantCulture) + ")" : "with no lifestone object within " + DeathLegVerdict.LifestoneRadiusMetres.ToString("0", CultureInfo.InvariantCulture) + " m")} (the bind to Holtburg's lifestone did not hold, D34)"));
        }

        return BeginPost(session, clock); // M4 3.1 d1: the measures freeze here, then the stages after the respawn in their order (DeathScenario.Order.cs)
    }

    // ------------------------------------------------------------------------------------------------ the regear

    /// <summary>
    /// Before the re-gear: the held items with no world object (the gear spawn would wait on them: "inventory-unsettled") are named, with where the read model holds them, and given <see cref="RegearSettleSeconds"/> to
    /// arrive; past that the run fails naming their guids, instead of the gear scenario's unnamed wait. A dropped item should have left the model with its F747 or 0024 (<c>GameStateReducer.ReleaseDeletedHeld</c>).
    /// </summary>
    private ScenarioStatus RegearSettleStep(IGameSessionView session, double clock)
    {
        _scratch.Clear();
        GearInventory.Collect(session.State, _scratch);
        var unsettled = 0;
        foreach (var guid in _scratch)
        {
            if (!session.State.Objects.TryGet(guid, out _))
            {
                unsettled++;
            }
        }

        if (unsettled == 0)
        {
            Enter("regear", clock);
            return ScenarioStatus.Running;
        }

        if (!_unsettledLogged)
        {
            _unsettledLogged = true;
            _log.Warn(LogCategory, $"regear: {unsettled} held item(s) have no world object: {DescribeUnsettled(session)}; waiting up to {RegearSettleSeconds:0} s for them");
        }

        return clock - _stageStart > RegearSettleSeconds
            ? Fail(session, $"regear: {unsettled} held item(s) still have no world object after {RegearSettleSeconds:0} s: {DescribeUnsettled(session)} (a dropped item should have left the inventory with its F747 or 0024)")
            : ScenarioStatus.Running;
    }

    private string DescribeUnsettled(IGameSessionView session)
    {
        var text = new System.Text.StringBuilder();
        foreach (var guid in _scratch)
        {
            if (session.State.Objects.TryGet(guid, out _))
            {
                continue;
            }

            var place = session.State.Self.Inventory.Find(guid);
            text.Append(text.Length == 0 ? string.Empty : "; ");
            text.Append(CultureInfo.InvariantCulture, $"guid 0x{guid:X8} wcid unknown (no world object) held {place.Kind} container 0x{place.Container:X8} placement {place.Placement} location 0x{place.Location:X}");
        }

        return text.ToString();
    }

    private ScenarioStatus RegearStep(IGameSessionView session, double now, double clock)
    {
        switch (_regear!.Step(session, now))
        {
            case ScenarioStatus.Failed:
                _regearFields = _regear.VerdictFields();
                return Fail(session, "regear: " + _regear.Failure);
            case ScenarioStatus.Succeeded:
                _regearFields = _regear.VerdictFields();
                _gearRestored = _regearFields.Contains("gear=wielded", StringComparison.Ordinal) && ParseWorn(_regearFields) == 5;
                _log.Info(LogCategory, $"regear done ({_regearFields}); gear_restored={(_gearRestored ? "yes" : "no")}; {RegearCorpseNote()}");
                if (!_gearRestored)
                {
                    return Fail(session, "regear: gear_restored=no (" + _regearFields + ")");
                }

                return NextPost(session, clock);
        }

        return ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ the end

    private ScenarioStatus Conclude(IGameSessionView session, double clock)
    {
        _walk?.Abandon();
        Subscribe(session); // a run of the vitae stage alone never reached the death stage that subscribes
        FreezeMeasures(session); // in the world, before the log off: the log off's LeaveWorld clears the object table (review R3)
        return AfterMeasures(session, clock);
    }

    /// <summary>After the freeze: out of the den, then the vitae stage, then the end.</summary>
    private ScenarioStatus AfterMeasures(IGameSessionView session, double clock)
    {
        if (_exit.Begin(session, clock, _selfTeleports))
        {
            Enter("leave-den", clock);
            return ScenarioStatus.Running;
        }

        if ((_stages & DeathStages.Vitae) != 0 && !_vitaeDone)
        {
            Enter("vitae", clock);
            return ScenarioStatus.Running;
        }

        return Finish(session, clock);
    }

    // ------------------------------------------------------------------------------------------------ out of the den

    /// <summary>The conclusion's leave (<see cref="DenExit"/>, best effort): waits for it, then goes on to the vitae stage and the end.</summary>
    private ScenarioStatus LeaveDenStep(IGameSessionView session, double clock) =>
        _exit.Tick(session, clock, _selfTeleports) ? AfterMeasures(session, clock) : ScenarioStatus.Running;

    /// <summary>The failed run's leave, whatever <c>endSession</c> is: waits for it, then the log off (<c>endSession: true</c>) or the end as Failed (the host logs off).</summary>
    private ScenarioStatus FailLeaveStep(IGameSessionView session)
    {
        if (!_exit.Tick(session, session.Clock.Now, _selfTeleports))
        {
            return ScenarioStatus.Running;
        }

        _failLeave = false;
        return FailInWorld(session);
    }

    /// <summary>The failed run's end in the world, out of the den: the log off when the scenario owns the session (as the fight scenario's failure, so the next login is not refused as CharacterInWorld), else Failed at once (the game's leg logs off itself).</summary>
    private ScenarioStatus FailInWorld(IGameSessionView session)
    {
        // P5a r3: the vitae stage also runs after a failure (out of the den, before the log off). Round 3's two death runs both failed in the fight, so the success-only stage never ran, the mage's vitae grew from 20% to
        // 40% across the gate and the lowered skills failed the heal and buff loops after it. The verdict logged by Fail stays the run's; the stage only pays what ACE's rule asks (D42), and its own failure is logged by name.
        if ((_stages & DeathStages.Vitae) != 0 && !_vitaeDone && session.State.Phase == SessionPhase.InWorld)
        {
            _failVitae = true;
            _log.Info(LogCategory, "vitae after the failure: the run still pays its vitae before it ends as failed (the next run must not start under this run's penalty); the verdict above stays the run's");
            Enter("vitae", session.Clock.Now);
            return ScenarioStatus.Running;
        }

        if (!_endSession)
        {
            Dispose();
            return ScenarioStatus.Failed;
        }

        _log.Info(LogCategory, "logging off before the run ends as failed");
        _pending = session.Actions.LogOff(ActionSource.Host);
        _failLogOff = true;
        return ScenarioStatus.Running;
    }

    // ------------------------------------------------------------------------------------------------ the vitae (D37)

    /// <summary>
    /// The last stage (after the freeze, before the log off): when the registry holds the vitae, asks the server mod's <c>xp</c> verb for the experience ACE's rule needs to clear it
    /// (<see cref="EffectRules.VitaeExperienceToClear"/>), refuses loudly if that would cross the next level (the XpTable), then waits up to <see cref="VitaeSeconds"/> for the vitae to leave the registry. The verdict's measures
    /// were frozen before, so the death's <c>vitae=</c> stays the penalty the death put on; <c>vitae_paid=</c> and <c>vitae_after=</c> report this stage. A failure here fails the run after the verdict, by name.
    /// </summary>
    private ScenarioStatus ChooseCharacter(IGameSessionView session, double clock)
    {
        foreach (var c in session.State.Characters.Entries)
        {
            if (string.Equals(c.Name, MageTemplate.CharacterName, StringComparison.OrdinalIgnoreCase))
            {
                if (c.PendingDeletion)
                {
                    return Fail(session, "choosing: the mage is pending deletion (restore it first)");
                }

                _log.Info(LogCategory, "entering the existing character " + MageTemplate.CharacterName);
                _pending = session.Actions.EnterWorld(c.Guid, ActionSource.Host);
                Enter("entering", clock);
                return ScenarioStatus.Running;
            }
        }

        return Fail(session, "choosing: " + MageTemplate.CharacterName + " is not in the character list (run --scenario=mage-build first)");
    }

    private ScenarioStatus VitaeStep(IGameSessionView session, double clock)
    {
        var q = session.State.Self.Qualities;
        if (_vitaeHandle is { } handle)
        {
            if (handle.Outcome is not { } outcome)
            {
                return ScenarioStatus.Running;
            }

            if (outcome.Kind != ActionOutcomeKind.Confirmed)
            {
                _vitaeHandle = null;
                return VitaeFail(session, q, "the xp grant was not confirmed: " + Describe(outcome));
            }

            if (!_vitaeSent)
            {
                _vitaeSent = true;
                _vitaeAsked = handle.Result?.Added ?? _vitaeAsked;
                _vitaeUntil = clock + VitaeSeconds;
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitae: {_vitaeAsked:N0} experience granted ({outcome.Detail}); waiting for the penalty to clear"));
            }

            if (!q.TryGetVitae(out _) || VitaePercentOf(q) == 0)
            {
                return VitaeDone(session, clock, 0);
            }

            return clock > _vitaeUntil
                ? VitaeFail(session, q, string.Create(CultureInfo.InvariantCulture, $"the vitae is still {VitaePercentOf(q)}% {VitaeSeconds:0} s after the grant of {_vitaeAsked:N0} experience (ACE's rule asked for it; the level, DeathLevel or pool may differ from the read model's)"))
                : ScenarioStatus.Running;
        }

        if (!q.IsKnown)
        {
            return clock - _stageStart > VitaeSeconds
                ? VitaeFail(session, q, "the player description has not arrived")
                : ScenarioStatus.Running;
        }

        if (!q.TryGetVitae(out var vitae))
        {
            _log.Info(LogCategory, "vitae: the registry holds none; nothing to pay");
            return VitaeDone(session, clock, 0);
        }

        var multiplier = vitae.Enchantment.StatModValue;
        var level = q.TryGetInt(EffectRules.VitaeLevelProperty, out var deathLevel) && deathLevel > 0 ? deathLevel : q.Level;
        q.TryGetInt(EffectRules.VitaeCpPoolProperty, out var pool);
        var amount = EffectRules.VitaeExperienceToClear(multiplier, level, pool);
        var levelXp = LevelXp ?? (session.XpTable is { } table ? LevelXpOf(table) : null);
        if (levelXp is null)
        {
            return VitaeFail(session, q, "no XpTable is loaded, so a level crossing by the grant cannot be named (TODO(m3-vitae-xptable))");
        }

        // D42 (the owner, 2026-10-23, replacing M3 D37's "never across a level"): the grant may cross a level, because a clear costs millions of experience against a level step of a similar size and a gate's two death runs
        // could never both fit. The grant stays bounded (exactly what clears the vitae, never more) and a crossing is logged loudly.
        if (q.Level >= 1)
        {
            var reached = (ulong)Math.Max(0, q.TotalExperience) + (ulong)amount;
            var to = q.Level;
            while (levelXp(to + 1) is { } next && reached >= next)
            {
                to++;
            }

            if (to > q.Level)
            {
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitae_level_crossed=yes level={q.Level}->{to} (clearing the {VitaePercentOf(q)}% vitae needs {amount:N0} experience; total {q.TotalExperience:N0}; D42 allows it)"));
            }
        }

        _vitaeAsked = amount;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitae: {VitaePercentOf(q)}% (level {level}, pool {pool}); asking for {amount:N0} experience to clear it"));
        _vitaeHandle = session.Tools.AddXp(amount, ActionSource.Host);
        return ScenarioStatus.Running;
    }

    private static int VitaePercentOf(PlayerQualities q) =>
        q.TryGetVitae(out var vitae) ? (int)Math.Round((1.0 - vitae.Enchantment.StatModValue) * 100.0, MidpointRounding.AwayFromZero) : 0;

    private ScenarioStatus VitaeDone(IGameSessionView session, double clock, int after)
    {
        _vitaeDone = true;
        _vitaePaid = _vitaeSent ? _vitaeAsked : 0;
        _vitaeAfter = after;
        _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"vitae: paid {_vitaePaid:N0} experience, {after}% left"));
        if (_failVitae)
        {
            _failVitae = false;
            return FailInWorld(session);
        }

        return Finish(session, clock);
    }

    private ScenarioStatus VitaeFail(IGameSessionView session, PlayerQualities q, string why)
    {
        _vitaeDone = true;
        _vitaePaid = _vitaeSent ? _vitaeAsked : 0;
        _vitaeAfter = VitaePercentOf(q);
        if (_failVitae)
        {
            _failVitae = false;
            _log.Error(LogCategory, "vitae after the failure: " + why);
            return FailInWorld(session);
        }

        return Fail(session, "vitae: " + why);
    }

    private ScenarioStatus Finish(IGameSessionView session, double clock)
    {
        if (!_endSession)
        {
            _log.Info(LogCategory, "verdict " + VerdictFields());
            Stage = "done";
            Dispose();
            return ScenarioStatus.Succeeded;
        }

        _pending = session.Actions.LogOff(ActionSource.Host);
        Enter("logging-off", clock);
        return ScenarioStatus.Running;
    }

    private ScenarioStatus Fail(IGameSessionView session, string why)
    {
        Failure = why;
        if (double.IsNaN(_deathEnd) && !double.IsNaN(_deathStart))
        {
            _deathEnd = session.Clock.Now;
        }

        _walk?.Abandon();
        _approach?.Abandon();
        _seam.Abandon(); // a travel leg still running releases its keys
        _inner?.Dispose(); // the composed fight scenario follows the session and holds the shared walk: M4 3.1 d1
        _log.Error(LogCategory, why);
        FreezeMeasures(session);
        _log.Info(LogCategory, "verdict " + VerdictFields());
        if (session.State.Phase == SessionPhase.InWorld && Stage is not ("logging-off" or "quitting"))
        {
            // Out of the den first, whatever endSession is (best effort, DenExit): a mage logged off in the den, by this scenario or by the game's leg as soon as this returns Failed, meets the tuskers at its next login. The
            // run stays Running until the leave is over, then logs off (endSession: true) or ends Failed where it stands.
            Subscribe(session); // the leave reads the player's own teleport positions: a failure before the first place (in a den a previous run left the mage in) has not subscribed yet
            if (_options.LeaveOnFailure && _exit.Begin(session, session.Clock.Now, _selfTeleports))
            {
                _failLeave = true;
                return ScenarioStatus.Running;
            }

            return FailInWorld(session);
        }

        Dispose();
        return ScenarioStatus.Failed;
    }

    private void Subscribe(IGameSessionView session)
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        _view = session;
        _tally.Start(session);
        _movedSubscription = session.Events.Subscribe<ObjectMoved>(OnMoved);
        _chatSubscription = session.Events.Subscribe<ChatReceived>(OnChat);
        _qualitiesSubscription = session.Events.Subscribe<QualitiesChanged>(OnQualities);
    }

    /// <summary>
    /// The bind's second signal: a stamina 0x02E9 after the Use that drops the current to about half of it (ACE's <c>UpdateVital(Stamina, Math.Round(current / 2f))</c>, <c>Lifestone.cs</c> 68-69; <c>Player_Vitals.cs</c> 130-139 sends it
    /// only when the value changed). "About": at most half of the last value seen plus one point, since a regeneration tick may be in flight. A rise (regeneration) only moves the comparison value on.
    /// </summary>
    private void OnQualities(in QualitiesChanged e)
    {
        if (Stage is not ("bind-use" or "bind-line") || e.Change != QualityChange.VitalCurrent || e.Key is not (MageTemplate.MaxStamina or MageTemplate.MaxStamina + 1) || _view is null
            || !_view.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out _))
        {
            return;
        }

        if (!_staminaHalved && stamina < _staminaBefore && 2 * stamina <= _staminaBefore + 2)
        {
            _staminaHalved = true;
            _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"bind: the stamina halved ({_staminaBefore} to {stamina}): ACE's second attune signal (Lifestone.cs 68-69)"));
            return;
        }

        _staminaBefore = stamina;
    }

    private void OnMoved(in ObjectMoved e)
    {
        if (e.IsSelf && e.Verdict == PositionVerdict.SelfTeleport)
        {
            _selfTeleports++;
        }
    }

    private void OnChat(in ChatReceived e)
    {
        if (Stage is not ("bind-use" or "bind-line"))
        {
            return;
        }

        if (e.Line.Kind == ChatKind.WeenieError)
        {
            if (Array.IndexOf(BindFailureCodes, e.Line.ChatType) >= 0)
            {
                _bindError = e.Line.ChatType;
                _log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"bind: a use-related WeenieError 0x{e.Line.ChatType:X4} came after the Use: {e.Line.Text}"));
            }
            else
            {
                // The chat channels' joins (0x051C General, Trade, LFG) and the like are not the lifestone's answer.
                _log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"bind: ignoring WeenieError 0x{e.Line.ChatType:X4} after the Use (not use-related): {e.Line.Text}"));
            }

            return;
        }

        if (e.Line.Kind != ChatKind.System || e.Line.ChatType != MagicChatType)
        {
            return;
        }

        if (IsNonAttuneMagicLine(e.Line.Text))
        {
            _bindNoise++;
            _log.Info(LogCategory, "bind: ignoring a Magic line that is not the attune: " + e.Line.Text);
            return;
        }

        _bindLines++;
        _log.Info(LogCategory, "bind: the lifestone's chat line: " + e.Line.Text);
    }

    /// <summary>The chat type of ACE's Magic lines (<c>ChatMessageType.Magic</c> 0x07), which the lifestone's UseMessage uses.</summary>
    private const uint MagicChatType = 7;

    /// <summary>ACE's line when an enchantment runs out, <c>EnchantmentManager.cs</c> 345: <c>The spell {spell.Name} on {WorldObject.Name} has expired.</c> (a Magic line; an item buff's expiry during the bind is one).</summary>
    public const string ExpiredLineStart = "The spell ";

    /// <summary>The end of <see cref="ExpiredLineStart"/>'s line.</summary>
    public const string ExpiredLineEnd = " has expired.";

    /// <summary>ACE's line when the lifestone protection runs out (<c>Player_Death.cs</c> 934, a Magic line).</summary>
    public const string ProtectionExpiredLine = "You're no longer protected by the Lifestone's magic!";

    /// <summary>ACE's line when the player's own action dispels the lifestone protection (<c>Player_Death.cs</c> 942, a Magic line).</summary>
    public const string ProtectionDispelledLine = "Your actions have dispelled the Lifestone's magic!";

    /// <summary>ACE's line when earned experience lowers the vitae (<c>Player_Xp.cs</c> 164, a Magic line).</summary>
    public const string VitaeReducedLine = "Your experience has reduced your Vitae penalty!";

    /// <summary>
    /// True for the Magic chat lines ACE sends that are not a lifestone's UseMessage and can fall between the Use and the attune: an enchantment's expiry (<see cref="ExpiredLineStart"/>), the two lifestone-protection
    /// lines and the vitae line. Any other Magic line is taken for the attune (the UseMessage is the world database's text for the lifestone, not a fixed string). Allocation-free.
    /// </summary>
    /// <param name="text">The line's text.</param>
    public static bool IsNonAttuneMagicLine(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return (text.StartsWith(ExpiredLineStart, StringComparison.Ordinal) && text.EndsWith(ExpiredLineEnd, StringComparison.Ordinal))
            || string.Equals(text, ProtectionExpiredLine, StringComparison.Ordinal)
            || string.Equals(text, ProtectionDispelledLine, StringComparison.Ordinal)
            || string.Equals(text, VitaeReducedLine, StringComparison.Ordinal);
    }

    private void Enter(string stage, double clock)
    {
        Stage = stage;
        _stageStart = clock;
        _log.Info(LogCategory, "stage " + stage);
    }

    private static int ParseWorn(string fields)
    {
        const string Key = "worn=";
        var at = fields.IndexOf(Key, StringComparison.Ordinal);
        if (at < 0)
        {
            return 0;
        }

        at += Key.Length;
        var end = fields.IndexOf(' ', at);
        return int.TryParse(end < 0 ? fields.AsSpan(at) : fields.AsSpan(at, end - at), NumberStyles.Integer, CultureInfo.InvariantCulture, out var worn) ? worn : 0;
    }

    private static string Describe(in ActionOutcome outcome) =>
        outcome.Kind + (outcome.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" (code 0x{outcome.Code:X})")) + (outcome.Detail is null ? string.Empty : ": " + outcome.Detail);
}
