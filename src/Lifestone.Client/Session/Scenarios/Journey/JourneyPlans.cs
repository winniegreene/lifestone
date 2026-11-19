using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// One station of a journey plan, as data (M4 P5b 3.4, lanes.md contract 2): the station's name (the verdict line's <c>station=</c>), its kind (the
/// catalog's key: <see cref="StationKinds"/>), its needs, the seconds it is expected to take (the gate's limit is the plan's sum) and free arguments.
/// </summary>
/// <param name="Name">The station's name (lower-case, hyphens).</param>
/// <param name="Kind">The station kind (<see cref="StationKinds"/>): the catalog (<c>JourneyStations</c>, J4) maps it to an <see cref="IStation"/>.</param>
/// <param name="NeedsOverride">Null (the station's own needs, <see cref="IStation.Needs"/>, are what the journey asserts and travels to) or an explicit override of what the station cannot know (its place on this journey's route); a real override is logged as a warning.</param>
/// <param name="ExpectedSeconds">The seconds the station is expected to take, travel and vitals passes included (Lifestone's own estimate from the P5a legs; the journey's limit is sized from the sum).</param>
/// <param name="Args">Free <c>key=value</c> arguments for the station's factory (empty when none).</param>
public sealed record StationSpec(string Name, string Kind, StationNeeds? NeedsOverride, double ExpectedSeconds, IReadOnlyDictionary<string, string>? Args = null)
{
    /// <summary>The arguments, never null.</summary>
    public IReadOnlyDictionary<string, string> Arguments => Args ?? Empty;

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>Makes a station from its spec (the catalog J4 builds; tests script it).</summary>
public interface IStationFactory
{
    /// <summary>The station for <paramref name="spec"/>; a kind the factory does not know throws <see cref="NotSupportedException"/> naming it (a loud stub).</summary>
    /// <param name="spec">The station's spec.</param>
    IStation Create(StationSpec spec);

    /// <summary>The station's limit in seconds before the journey ends it <c>timeout:&lt;stage&gt;</c> (M5 1 fix B: a factory that knows the fight options raises the den fight's; the default is <see cref="JourneyLimits.DefaultStationSeconds"/>).</summary>
    /// <param name="spec">The station's spec.</param>
    double StationLimitSeconds(StationSpec spec) => JourneyLimits.DefaultStationSeconds(spec);
}

/// <summary>The station kinds (lanes.md contract 2's table; each kind's owner builds the station class, J4 the catalog).</summary>
public static class StationKinds
{
    /// <summary>The build in-world, verify only (J3b).</summary>
    public const string BuildVerify = "build-verify";

    /// <summary>GearScenario's embedded half (J4 wires it).</summary>
    public const string Gear = "gear";

    /// <summary>BuffScenario's embedded half (J4 wires it).</summary>
    public const string Buff = "buff";

    /// <summary>CastLiveTests' facts (J3a).</summary>
    public const string CastFacts = "cast-facts";

    /// <summary>The mage's kit refusal 0x04FC (J3a).</summary>
    public const string KitRefused = "kit-refused";

    /// <summary>HarmHealScript as a station (J3a).</summary>
    public const string HarmHeal = "harm-heal";

    /// <summary>Holtburg's lifestone on foot: Use, then Lifestone Tie (J3b).</summary>
    public const string Bind = "bind";

    /// <summary>M6 item 14: the stop at the buff bot by Holtburg's lifestone (<c>BuffBotStation</c>).</summary>
    public const string BuffBot = "buff-bot";

    /// <summary>Into the den: Primary Portal Recall, or the hop, the tie and the portal on foot (J3b).</summary>
    public const string DenTravel = "den-travel";

    /// <summary>FightScenario (J4 wires it over J2b's flags).</summary>
    public const string DenFight = "den-fight";

    /// <summary>DeathScenario (J4 wires it over J2b's flags; D37).</summary>
    public const string Death = "death";

    /// <summary>HuntScenario's Swap, Hunt and Restore (J4 wires it over J2a's stages).</summary>
    public const string Hunt = "hunt";

    /// <summary>HuntScenario's Vendor stage (J4 wires it over J2a's stage).</summary>
    public const string Vendor = "vendor";

    /// <summary>Lifestone Recall home (J3b).</summary>
    public const string RecallHome = "recall-home";

    /// <summary>The chests on foot (J3a).</summary>
    public const string Chests = "chests";

    /// <summary>AppraiseLiveTests' items half (J3a).</summary>
    public const string AppraiseItems = "appraise-items";

    /// <summary>AppraiseLiveTests' vendor half (J3a).</summary>
    public const string AppraiseVendor = "appraise-vendor";

    /// <summary>VendorLiveTests' facts (J3a).</summary>
    public const string VendorFacts = "vendor-facts";

    /// <summary>ShortcutLiveTests without the relogs (J3a).</summary>
    public const string Shortcuts = "shortcuts";

    /// <summary>The hero plans' lower-right bar filled from the pack (the M4 hero video; <see cref="HotbarStation"/>).</summary>
    public const string Hotbar = "hotbar";

    /// <summary>The character's own leftover player corpses in its landblock set to rot (the M4 hero video; <see cref="CorpsesStation"/>, <c>@lifestone corpses rot</c>).</summary>
    public const string Corpses = "corpses";

    /// <summary>The resupply from the ground's home vendors and supplies (M5 item 3 m3): <c>ResupplyStation</c>; the logistics station runs it (no plan lists it).</summary>
    public const string Resupply = "resupply";

    /// <summary>The logistics trip (M5 item 3 c2a, DoD 6, D52): <see cref="LogisticsStation"/> reads the counters after the den fight and, when a trip is due or forced, recalls home, resupplies, spends the experience and recalls back.</summary>
    public const string Logistics = "logistics";

    /// <summary>The Pathwarden quest as a station (M6 item 0 a3a): <see cref="PathwardenStation"/> reads <c>quests/pathwarden.json</c>.</summary>
    public const string Pathwarden = "pathwarden";

    /// <summary>The Training Academy by play as a station (M6 item 0 a3b): <see cref="AcademyStation"/> reads <c>quests/academy.json</c>.</summary>
    public const string Academy = "academy";

    /// <summary>The buy-trip (M6 item 1 d, D12): <see cref="BuyTripStation"/> to the archmage who sells a carried-only scarab (no plan lists it yet; the logistics stop names it).</summary>
    public const string BuyTrip = "buy-trip";

    /// <summary>The rung change (M6 item 4 t2, D50): <see cref="RungChangeStation"/> to the journey's ground by the travel file's portals and the entry, or home (<c>direction=home</c>); no plan lists it yet (the autopilot's Travel state is T3's).</summary>
    public const string RungChange = "rung-change";

    /// <summary>M6 item 14: the den run (<c>DenRunStation</c>): the run-only road to a Black Spawn Den's entrance, the minute inside, the tie and home.</summary>
    public const string DenRun = "den-run";

    /// <summary>
    /// The quest station (M6 item 5): runs one quest file's steps by kind (args <see cref="JourneyPlans.QuestArgument"/> <c>=&lt;key&gt;</c> and
    /// <see cref="JourneyPlans.FromArgument"/> <c>=home|ground</c>, default home); its verdict line is <c>quest: key= from= steps= done= xp=+&lt;n&gt; reward_line=&lt;n&gt; timer= ok= fail=</c>.
    /// No plan lists it yet (the autopilot's errand and the kill task's round call it).
    /// </summary>
    public const string Quest = "quest";

    /// <summary>
    /// M6 villa: the villa's steps (<c>Housing.VillaStations</c>; arg <c>step=</c> one of writ-hand-in, lockpick-buy, villa-purchase, villa-storage, villa-rent). No plan lists it: the level up job's villa schedule calls it at a town
    /// trip's boundary.
    /// </summary>
    public const string Villa = "villa";

    /// <summary>Every kind, in the table's order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        BuildVerify, Gear, Buff, CastFacts, KitRefused, HarmHeal, Bind, DenTravel, DenFight, Death, Hunt, Vendor, RecallHome, Chests, AppraiseItems, AppraiseVendor, VendorFacts, Shortcuts, Hotbar, Corpses, Resupply, Logistics, Pathwarden, Academy, BuyTrip, RungChange, Quest,
    ];
}

/// <summary>
/// A journey (M4 P5b 3.4): one character's whole test as stations in one session, from Holtburg's lifestone back to it, with a teleport budget.
/// </summary>
/// <param name="Name">The plan's name (<c>mage</c>, <c>melee</c>, <c>archer</c>; the done line's <c>name=</c>, <c>--journey=</c>).</param>
/// <param name="Character">The character.</param>
/// <param name="Account">The test account's key.</param>
/// <param name="Ground">The plan's hunting ground (where its one budgeted teleport goes: the den for the mage, the belt, the forest).</param>
/// <param name="TeleportBudget">The most mod teleports the journey may make (the pre-journey recovery excluded).</param>
/// <param name="Stations">The stations, in order.</param>
public sealed record JourneyPlan(string Name, string Character, string Account, StationWhere Ground, int TeleportBudget, IReadOnlyList<StationSpec> Stations)
{
    /// <summary>The sum of the stations' expected seconds (the hosts size the journey's limit from it, with the login and the margin).</summary>
    public double ExpectedSeconds => Stations.Sum(s => s.ExpectedSeconds);

    /// <summary>
    /// The cheat-free climber's rule (M6 item 0 a3b, DoD 2): the journey sends no <c>place</c> of any kind (no pre-journey recovery place, no recovery place after a failed station). When the known state is not where the first station needs it (<see cref="IKnownStart"/>, else the plan's
    /// <see cref="Ground"/>), the journey stops by name (<c>stop=known-state-unplayable:&lt;where&gt;</c>); the mod verbs are counted from the journey's start, and the climber line carries <c>granted= place= recovery_place=</c>.
    /// </summary>
    public bool CheatFree { get; init; }

    /// <summary>
    /// M6 7 H (the loopbacks' round 1): the buff profile the journey's cycles run (its buff station's, its fights' and the death station's rebuff), or null: each cycle's own choice by the character's name (<see cref="BuffCycle"/>: Winnie's lines by play, else the arena
    /// profile's 24 lines the build teaches). A Lifestone Throwaway that stands in for the climber sets <see cref="BuffProfiles.Climber"/>, the climb's own profile: it buffs the lines its book holds, each only while its school is trained, so a line the plan never bought is no
    /// failure (<c>skill 32 is not trained</c>, 0x03FC).
    /// </summary>
    public IBuffProfile? BuffProfile { get; init; }
}

/// <summary>
/// The three journeys as data (M4 P5b 3.4 with the lead's changes of 2026-10-24: the mage's expiry is Soak only and left out; the melee plan's second bind
/// is dropped, the first binds and ties). The expected seconds are Lifestone's own estimates from P5a's live legs (W0: Holtburg to the belt on foot 107 s,
/// to the forest 178 s, a recall about 10 s with its swap halves) and the design's lane totals (3.5: mage 1,100 s, melee 900 s, archer 1,000 s).
/// </summary>
public static class JourneyPlans
{
    /// <summary>The least health share, in percent, a fighting or harming station starts with (Lifestone's own, the hunt's 80% heal rule).</summary>
    public const double FightHealthPercent = 80;

    /// <summary>The least mana share a casting station starts with (Lifestone's own).</summary>
    public const double CastManaPercent = 40;

    /// <summary>The least stamina share a hunting station starts with (Lifestone's own).</summary>
    public const double HuntStaminaPercent = 50;

    /// <summary>
    /// The archer's forest hunt station's expected seconds (M5 item 2 k2, from g3's estimate: the forest hunt has 33 spots, D42, and the chance of two usable ones is 0.40 within 8 spots, 0.87 within 20 and 0.98 within 33): 400, not 320; its hunt runs
    /// <see cref="JourneyStations.ArcherHuntSeconds"/> (1,200 s) and the station's limit is three times this plus 300 s, 1,500 s, so a hunt that finds nothing ends by its own named reason before the station's timeout.
    /// </summary>
    public const double ArcherHuntExpectedSeconds = 400;

    /// <summary>
    /// The mage's den fight tours two storeys (M5 item 2 k2, D40): <see cref="KillsArgument"/> 3 and <see cref="StoreysMinArgument"/> 2 (<c>--fight-storeys-min 2</c>), so the tour leaves the arrival's floor after its first kill (g6) and the second kill is on the next storey, the third anywhere;
    /// a <c>--fight-kills</c> or <c>--fight-storeys-min</c> on the command line wins over these.
    /// </summary>
    public static IReadOnlyDictionary<string, string> MageDenFightArguments { get; } = new Dictionary<string, string>(StringComparer.Ordinal) { [KillsArgument] = "3", [StoreysMinArgument] = "2" };

    /// <summary>
    /// The mage's den-fight station's expected seconds (Lifestone's own, re-set from the walk by M5 item 2 k2): the old 150 s (the place, the mode change, the vitals pass and one kill) less that kill's 15 s, plus the walk from the arrival's floor to the second storey's guards
    /// (about 330 m over the ground's room graph at about 10 m/s, 33 s) plus 3 kills at about 15 s each (45 s): 213, rounded to 215. The station's limit is three times it plus 300 s.
    /// </summary>
    public const double MageDenFightSeconds = 215;

    /// <summary>
    /// The mage plan's teleport budget (M5 3 p1, D50): 0. The build ties the ground's portal (its own budgeted place, at build time, the owner's cheat layer), so the journey reaches the ground by Primary Portal Recall and makes no place; a mage that is
    /// not tied fails the den travel <c>travel:ground:untied</c> (the first tie's walk is M6's, <c>TODO(m6-first-tie-walk)</c>) instead of placing. The fighters' plans keep their budgets (their one place to the belt or the forest).
    /// </summary>
    public const int MageTeleportBudget = 0;

    /// <summary>
    /// The mage's logistics station's expected seconds (M5 item 3 c2a, Lifestone's own; the arithmetic): Lifestone Recall home about 12 s (the cast, the teleport, the settle), the walks to the two home vendors about 120 m at 10 m/s (12 s) with about 6 s at each to
    /// meet and open him (12 s), the buys about 8 lines at about 3 s each (24 s), the experience at most 60 raises one at a time at about 0.7 s (42 s), Primary Portal Recall back about 15 s, and about 33 s of the keeper's mana pass and the settles between: 150.
    /// The station's limit is three times it plus 300 s.
    /// </summary>
    public const double MageLogisticsSeconds = 150;

    /// <summary>The argument of a <see cref="StationKinds.Logistics"/> spec that forces the trip (<see cref="TripForced"/>, D52): three kills cannot bring a stock line under its line, so the gate's trip is forced and the counters' own triggers are proved offline and by the autopilot lane.</summary>
    public const string TripArgument = "trip";

    /// <summary>The value of <see cref="TripArgument"/> that forces the trip (<c>trigger=forced</c>); any other value, or none, lets the counters decide.</summary>
    public const string TripForced = "forced";

    /// <summary>
    /// M6 14 sell first (an owner ruling, 2026-11-17): the value of <see cref="TripArgument"/> for the sale before an errand from home (<c>trigger=errand-room</c>, <c>Autopilot.ErrandRoomRule</c>): forced as <see cref="TripForced"/> is (the
    /// autopilot's room check before the errand decided it), named by its own trigger.
    /// </summary>
    public const string TripErrandRoom = "errand-room";

    /// <summary>The mage's logistics arguments: the trip is forced in the gate (D52).</summary>
    public static IReadOnlyDictionary<string, string> MageLogisticsArguments { get; } = new Dictionary<string, string>(StringComparer.Ordinal) { [TripArgument] = TripForced };

    /// <summary>The mage (<c>test</c>): build-verify, gear, buff, cast facts, kit refused, harm-heal, bind, the den travel, the den fight, the logistics trip (forced, D52), death, Lifestone Recall home.</summary>
    public static JourneyPlan Mage { get; } = new(
        "mage",
        MageTemplate.CharacterName,
        "test",
        StationWhere.Den,
        MageTeleportBudget,
        [
            new("build-verify", StationKinds.BuildVerify, null, 30),
            new("gear", StationKinds.Gear, null, 60),
            new("buff", StationKinds.Buff, null, 200),
            new("cast-facts", StationKinds.CastFacts, null, 120),
            new("kit-refused", StationKinds.KitRefused, null, 20),
            new("harm-heal", StationKinds.HarmHeal, new(StationWhere.Holtburg, Peace: true, MinHealthPercent: FightHealthPercent, MinManaPercent: CastManaPercent), 90),
            new("bind", StationKinds.Bind, null, 60),
            new("den-travel", StationKinds.DenTravel, null, 60),
            new("den-fight", StationKinds.DenFight, null, MageDenFightSeconds, MageDenFightArguments),
            new("logistics", StationKinds.Logistics, null, MageLogisticsSeconds, MageLogisticsArguments),
            new("death", StationKinds.Death, null, 280),
            new("recall-home", StationKinds.RecallHome, null, 30),
        ]);

    /// <summary>The Brawler (<c>test-melee</c>): build-verify, buff, bind, hunt on the belt (the one teleport), harm-heal, recall home, vendor, vendor facts, chests.</summary>
    public static JourneyPlan Melee { get; } = new(
        "melee",
        FighterTemplate.BrawlerName,
        FighterTemplate.MeleeAccount,
        StationWhere.Belt,
        1,
        [
            new("build-verify", StationKinds.BuildVerify, null, 30),
            new("buff", StationKinds.Buff, null, 120),
            new("bind", StationKinds.Bind, null, 60),
            new("hunt", StationKinds.Hunt, new(StationWhere.Belt, MinHealthPercent: FightHealthPercent, MinStaminaPercent: HuntStaminaPercent), 300),
            new("harm-heal", StationKinds.HarmHeal, null, 90),
            new("recall-home", StationKinds.RecallHome, null, 30),
            new("vendor", StationKinds.Vendor, null, 180),
            new("vendor-facts", StationKinds.VendorFacts, null, 90), // the predicted 0062 price, the unsellable refusal, the sell-back (P5b-R3; coverage.md row 18)
            new("chests", StationKinds.Chests, null, 90),
        ]);

    /// <summary>The Archer (<c>test-archer</c>): build-verify, buff, bind, hunt in the forest (the one teleport), appraise items, harm-heal, recall home, appraise the vendor, vendor, shortcuts.</summary>
    public static JourneyPlan Archer { get; } = new(
        "archer",
        FighterTemplate.ArcherName,
        FighterTemplate.ArcherAccount,
        StationWhere.Forest,
        1,
        [
            new("build-verify", StationKinds.BuildVerify, null, 30),
            new("buff", StationKinds.Buff, null, 120),
            new("bind", StationKinds.Bind, null, 60),
            new("hunt", StationKinds.Hunt, new(StationWhere.Forest, MinHealthPercent: FightHealthPercent, MinStaminaPercent: HuntStaminaPercent), ArcherHuntExpectedSeconds),
            new("appraise-items", StationKinds.AppraiseItems, null, 40),
            new("harm-heal", StationKinds.HarmHeal, null, 90),
            new("recall-home", StationKinds.RecallHome, null, 30),
            new("appraise-vendor", StationKinds.AppraiseVendor, null, 90),
            new("vendor", StationKinds.Vendor, null, 150),
            new("shortcuts", StationKinds.Shortcuts, null, 70),
        ]);

    /// <summary>The three plans of the gate.</summary>
    public static IReadOnlyList<JourneyPlan> All { get; } = [Mage, Melee, Archer];


    /// <summary>The Archer's hero journey (<c>test-archer</c>): build-verify, the bar, buff, bind, the forest hunt (the one teleport; Greater Frost Arrows), Lifestone Recall home.</summary>
    public static JourneyPlan HeroArcher { get; } = new(
        "hero-archer",
        FighterTemplate.ArcherName,
        FighterTemplate.ArcherAccount,
        StationWhere.Forest,
        1,
        [
            new("build-verify", StationKinds.BuildVerify, null, 30),
            new("hotbar", StationKinds.Hotbar, null, 20),
            new("buff", StationKinds.Buff, null, 120),
            new("bind", StationKinds.Bind, null, 60),
            new("hunt", StationKinds.Hunt, new(StationWhere.Forest, MinHealthPercent: FightHealthPercent, MinStaminaPercent: HuntStaminaPercent), ArcherHuntExpectedSeconds),
            new("recall-home", StationKinds.RecallHome, null, 30),
        ]);

    /// <summary>The Brawler's hero journey (<c>test-melee</c>): build-verify, the bar, buff, bind, the belt hunt (the one teleport; its D41 magic staff), Lifestone Recall home, the store (the loot sold at Renald the Elder).</summary>
    public static JourneyPlan HeroBrawler { get; } = new(
        "hero-brawler",
        FighterTemplate.BrawlerName,
        FighterTemplate.MeleeAccount,
        StationWhere.Belt,
        1,
        [
            new("build-verify", StationKinds.BuildVerify, null, 30),
            new("hotbar", StationKinds.Hotbar, null, 20),
            new("buff", StationKinds.Buff, null, 120),
            new("bind", StationKinds.Bind, null, 60),
            new("hunt", StationKinds.Hunt, new(StationWhere.Belt, MinHealthPercent: FightHealthPercent, MinStaminaPercent: HuntStaminaPercent), 300),
            new("recall-home", StationKinds.RecallHome, null, 30),
            new("vendor", StationKinds.Vendor, null, 180),
        ]);

    /// <summary>The argument of a <see cref="StationKinds.DenFight"/> spec that names the spell the fight casts instead of Flame Bolt (<see cref="FightOptions.SpellId"/>).</summary>
    public const string SpellArgument = "spell";

    /// <summary>The argument of a <see cref="StationKinds.DenFight"/> spec that names the kills the fight asks for.</summary>
    public const string KillsArgument = "kills";

    /// <summary>The argument of a <see cref="StationKinds.DenFight"/> spec that names the storeys the fight's tour visits before it stays (<see cref="FightOptions.StoreysMin"/>, <c>--fight-storeys-min</c>).</summary>
    public const string StoreysMinArgument = "storeys-min";

    /// <summary>M6 item 0 a5: the argument of a <see cref="StationKinds.DenFight"/> spec that caps the bolt level (<see cref="FightOptions.MaxLevel"/>, <c>--fight-max-level</c>; the level up job's rung cap, <c>AutopilotScenario.BoltLevel</c>).</summary>
    public const string MaxLevelArgument = "max-level";

    /// <summary>M6 0 F5 (review 0-R5): the argument of a <see cref="StationKinds.DenFight"/> spec that carries the rung's <c>target_margin</c> to the outdoor fight's pick (<see cref="FightOptions.TargetMargin"/>).</summary>
    public const string TargetMarginArgument = "target-margin";

    /// <summary>The argument of a <see cref="StationKinds.Gear"/> spec whose kit carries Cassius' Ring of Fire's components (the hero mage's only; P5b-R12).</summary>
    public const string RingArgument = "ring";

    /// <summary>The argument of a <see cref="StationKinds.DenTravel"/> spec that steps off the lifestone before Primary Portal Recall's cast (the hero mage's only; <see cref="DenTravelStation.StepOff"/>).</summary>
    public const string StepOffArgument = "step-off";

    /// <summary>The argument of a <see cref="StationKinds.Academy"/> spec that names the quest file it plays (M6 0 F7): <see cref="AcademyStation.SkipKey"/> or <see cref="AcademyStation.FullRouteKey"/> (the default, no argument).</summary>
    public const string QuestArgument = "quest";

    /// <summary>
    /// M6 item 5: the argument of a <see cref="StationKinds.Quest"/> spec that says where the run starts: <see cref="FromHome"/> (the default, no argument) or
    /// <see cref="FromGround"/> (the climber's ground, the trophy errand on the way home). The quest spec names its file with <see cref="QuestArgument"/>.
    /// </summary>
    public const string FromArgument = "from";

    /// <summary>M6 5 F2 (D62): the argument of a <see cref="StationKinds.Quest"/> spec that runs only a range of the file's steps, <c>steps=&lt;from&gt;-&lt;to&gt;</c> (0-based, inclusive); the done check and the completion run only when the range ends at the last step.</summary>
    public const string StepsArgument = "steps";

    /// <summary>M6 item 5: <see cref="FromArgument"/>'s value for a run that starts at home (Holtburg).</summary>
    public const string FromHome = "home";

    /// <summary>M6 item 5: <see cref="FromArgument"/>'s value for a run that starts on the climber's ground.</summary>
    public const string FromGround = "ground";

    /// <summary>
    /// The mage's hero journey (<c>test</c>): build-verify (teaches 1785), the bar, gear (the ring's components), buff, bind, den-travel (a step off the lifestone, then Primary Portal Recall once the portal is tied), the den's leftover corpses of
    /// earlier generations set to rot (the owner's word), the den fight with Cassius' Ring of Fire, the gate's den death (retail's death strips the buffs, so the re-buff after the respawn raises the stats on screen: the owner's choice for the hero video), Lifestone Recall home.
    /// </summary>
    public static JourneyPlan HeroMage { get; } = new(
        "hero-mage",
        MageTemplate.CharacterName,
        "test",
        StationWhere.Den,
        1, // m5-hero-budget (closed): the hero mage keeps its video-era budget; its tied den-travel makes no place and an untied one is refused (travel:ground:untied), so the budget is never spent
        [
            new("build-verify", StationKinds.BuildVerify, null, 30),
            new("hotbar", StationKinds.Hotbar, null, 20),
            new("gear", StationKinds.Gear, null, 60, new Dictionary<string, string>(StringComparer.Ordinal) { [RingArgument] = "1" }),
            new("buff", StationKinds.Buff, null, 200),
            new("bind", StationKinds.Bind, null, 60),
            new("den-travel", StationKinds.DenTravel, null, 60, new Dictionary<string, string>(StringComparer.Ordinal) { [StepOffArgument] = "1" }),
            new("corpses", StationKinds.Corpses, null, 400), // the first run waits out the dropped contents (CorpsesStation.ItemRotSeconds); later runs pass at once
            new("den-fight", StationKinds.DenFight, null, 150, new Dictionary<string, string>(StringComparer.Ordinal) { [SpellArgument] = ArenaProfile.RingOfFireId.ToString(System.Globalization.CultureInfo.InvariantCulture), [KillsArgument] = "3" }),
            new("death", StationKinds.Death, null, 280),
            new("recall-home", StationKinds.RecallHome, null, 30),
        ]);

    /// <summary>
    /// The hero hook (M4 P5b J7, 3.6 of the design, owner decision D32): the Archer, the Brawler and the mage are three journeys on their own accounts (one character per account), each cut to its showpiece and run alone in one Godot
    /// window at the hero video's size with <c>--journey-shots</c>, never part of the gate. The Archer: <c>hero-archer</c> runs to the forest and looses Greater Frost Arrows (<see cref="ShowpieceEvents.FrostArrow"/>), seen from the side with retail's
    /// camera turned. The Brawler: <c>hero-brawler</c> runs the belt and fights with the Mace (<see cref="ShowpieceEvents.MaceHit"/>). The mage: <c>hero-mage</c> casts Primary Portal Recall (<see cref="ShowpieceEvents.PortalRecall"/>; the portal tied, the recall
    /// lands inside the den, <see cref="ShowpieceEvents.PortalArrival"/>) and casts Cassius' Ring of Fire (1785, <see cref="ShowpieceEvents.RingOfFire"/>) among the den's real tuskers; its build-verify teaches 1785 and its gear station tops the kit up
    /// with the ring's components.
    /// </summary>
    public static IReadOnlyList<JourneyPlan> Hero { get; } = [HeroArcher, HeroBrawler, HeroMage];

    /// <summary>The corpses station's switch: no wait for the rotted corpses' contents (the hero's <see cref="CorpsesStation.ItemRotSeconds"/>); the corpses only leave the view.</summary>
    public const string NoItemWaitArgument = "no-item-wait";

    /// <summary>
    /// M5 7 lead (the owner, 2026-10-29: clear out all the mage corpses, they are everywhere): BSD Mage's own player corpses in the den set to rot, run once before a soak, outside its judged window. Buff (it lands among the
    /// tuskers), den-travel (the hero's: Primary Portal Recall once the portal is tied), <c>@lifestone corpses rot</c> in the den's landblock (the verb reaches the caller's landblock only), then the recall home. The run's
    /// <c>granted=</c> is its own (one verb), not the soak's.
    /// </summary>
    public static JourneyPlan MageCorpses { get; } = new(
        "mage-corpses",
        MageTemplate.CharacterName,
        "test",
        StationWhere.Den,
        1,
        [
            new("buff", StationKinds.Buff, null, 200),
            new("den-travel", StationKinds.DenTravel, null, 60, new Dictionary<string, string>(StringComparer.Ordinal) { [StepOffArgument] = "1" }),
            new("corpses", StationKinds.Corpses, null, 120, new Dictionary<string, string>(StringComparer.Ordinal) { [NoItemWaitArgument] = "1" }),
            new("recall-home", StationKinds.RecallHome, null, 30),
        ]);

    /// <summary>The one-off tool plans (M5 7 lead): not part of a gate.</summary>
    public static IReadOnlyList<JourneyPlan> Tools { get; } = [MageCorpses];

    /// <summary>
    /// M6 item 0 a3a (D7) and a3b: the climber's first journey (<c>--scenario=journey --journey=climber</c>, <c>test</c>): Winnie, created by <c>--scenario=climber-create --fresh-character</c>, takes the Training Academy's own skip from its start cell by play (<see cref="AcademyStation"/> on <c>quests/academy-skip.json</c>, M6 0 F7, D37: Jonathan's Exit Token given back, the server's teleport to
    /// Holtburg; the full route <c>academy</c> waits for item 4's ramps), then does the Pathwarden quest in Holtburg and wears its four pieces. No build-verify (the climber is built by what it earns, never by a grant), no teleport budget and no place of any kind (<see cref="JourneyPlan.CheatFree"/>: a known state that is not the
    /// Academy's start stops by name); not part of the gate's three plans. The <see cref="AcademyExitScenario"/> bridge stays for the other characters' baselines and is no longer used for Winnie.
    /// </summary>
    public static JourneyPlan Climber { get; } = new(
        "climber",
        MageTemplate.ClimberName,
        "test",
        StationWhere.Holtburg,
        0,
        [
            new("academy", StationKinds.Academy, null, AcademySkipSeconds, new Dictionary<string, string>(StringComparer.Ordinal) { [QuestArgument] = AcademyStation.SkipKey }),
            new(BuffBotStation.StationName, StationKinds.BuffBot, null, BotFirstSeconds, new Dictionary<string, string>(StringComparer.Ordinal) { [BuffBotStation.WhenArgument] = BuffBotStation.WhenBotOn }), // M6 sprint (owner ruling 2026-11-19): the bot first, as she arrives beside it from the Academy
            new("pathwarden", StationKinds.Pathwarden, null, PathwardenSeconds),
        ])
    {
        CheatFree = true,
    };

    /// <summary>
    /// The start-up's stop at the buff bot (Lifestone's own budget): run 5's first round took 84 s with a 43 s walk (2026-11-19 14:27:28 to 14:28:52 UTC, step3-levelup.log); from the Academy's exit she arrives at the Holtburg
    /// lifestone, in the bot's own cell (0xA9B40019).
    /// </summary>
    public const double BotFirstSeconds = 300;

    /// <summary>The skip's expected seconds (Lifestone's own, M6 0 F7): five walks between the start rooms of about 10 s each, the greeter's give, the three pick-ups and wears, Jonathan's talk and give with their settles about 60 s, the server's teleport, and the margin.</summary>
    public const double AcademySkipSeconds = 150;

    /// <summary>The full route's expected seconds (Lifestone's own): about fifteen walks between rooms of about 10 s each, two kills of a few casts with their loot about 60 s, the talks and gives with their settles about 60 s, two portals about 20 s, and the margin.</summary>
    public const double AcademySeconds = 400;

    /// <summary>The Pathwarden station's expected seconds (Lifestone's own): the walk to Thorolf and the chest in one cell about 10 s, the talk, the give and the unlock with their settles about 30 s, four pick-ups and four wields about 40 s, and the margin (120 s); M6 item 14: and the stop at the buff bot that now comes before the chest (a cast a line of the bot's profile at a caster's pace, about four seconds each; the autopilot's own stop expects 180 s).</summary>
    public const double PathwardenSeconds = 300;

    /// <summary>The climber's plans (M6): not part of the gate's three, run by <c>--journey=climber</c>.</summary>
    public static JourneyPlan ModelClimber { get; } = Climber with { Name = "qwinnie", Character = MageTemplate.ModelClimberName, Account = TestAccounts.Qwinnie };

    /// <summary>The journey plan of a climber (<see cref="Climber"/> or <see cref="ModelClimber"/>).</summary>
    /// <param name="climber">The climber's name.</param>
    public static JourneyPlan ClimberFor(string climber) => string.Equals(climber, MageTemplate.ModelClimberName, StringComparison.OrdinalIgnoreCase) ? ModelClimber : Climber;

    /// <summary>The climbers' journeys (<c>--journey=climber</c>, <c>--journey=qwinnie</c>).</summary>
    public static IReadOnlyList<JourneyPlan> Climbers { get; } = [Climber, ModelClimber];

    /// <summary>The plan named <paramref name="name"/> (<c>mage</c>, <c>melee</c>, <c>archer</c>, a hero plan or <c>climber</c>; case-insensitive), or null.</summary>
    /// <param name="name">The plan's name (<c>--journey=</c>).</param>
    public static JourneyPlan? ForName(string? name) =>
        All.Concat(Hero).Concat(Tools).Concat(Climbers).FirstOrDefault(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}
