using Lifestone.Client.Session.Fighter;
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

    /// <summary>Every kind, in the table's order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        BuildVerify, Gear, Buff, CastFacts, KitRefused, HarmHeal, Bind, DenTravel, DenFight, Death, Hunt, Vendor, RecallHome, Chests, AppraiseItems, AppraiseVendor, VendorFacts, Shortcuts, Hotbar, Corpses, Resupply, Logistics,
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

    /// <summary>The argument of a <see cref="StationKinds.Gear"/> spec whose kit carries Cassius' Ring of Fire's components (the hero mage's only; P5b-R12).</summary>
    public const string RingArgument = "ring";

    /// <summary>The argument of a <see cref="StationKinds.DenTravel"/> spec that steps off the lifestone before Primary Portal Recall's cast (the hero mage's only; <see cref="DenTravelStation.StepOff"/>).</summary>
    public const string StepOffArgument = "step-off";

    /// <summary>
    /// The mage's hero journey (<c>test</c>): build-verify (teaches 1785), the bar, gear (the ring's components), buff, bind, den-travel (a step off the lifestone, then Primary Portal Recall once the portal is tied), the den's leftover corpses of
    /// earlier generations set to rot (the owner's word), the den fight with Cassius' Ring of Fire, the gate's den death (retail's death strips the buffs, so the re-buff after the respawn raises the stats on screen: the owner's choice for the hero video), Lifestone Recall home.
    /// </summary>
    public static JourneyPlan HeroMage { get; } = new(
        "hero-mage",
        MageTemplate.CharacterName,
        "test",
        StationWhere.Den,
        1, // TODO(m5-hero-budget): the hero mage keeps its video-era budget; its tied den-travel makes no place and an untied one is refused (travel:ground:untied), so the budget is never spent
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

    /// <summary>The plan named <paramref name="name"/> (<c>mage</c>, <c>melee</c>, <c>archer</c>, or a hero plan; case-insensitive), or null.</summary>
    /// <param name="name">The plan's name (<c>--journey=</c>).</param>
    public static JourneyPlan? ForName(string? name) =>
        All.Concat(Hero).Concat(Tools).FirstOrDefault(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));
}
