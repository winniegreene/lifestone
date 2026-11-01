using System.Globalization;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// The fight loop's options (M3 3.1 lane fa; registered in <c>OptionCatalog.Fight.cs</c>): the kills that end the fight, the seconds it may take, and (M5 1 F1) the clean duration stop <c>--fight-minutes</c>,
/// the bolt's level cap <c>--fight-max-level</c> (D26) and the mana order's <c>--cast-mana-*</c> options (<see cref="ManaOrderOptions"/>, D15 and D27). Immutable;
/// <see cref="FromArguments"/> is the parser both hosts call (the game's <c>bsd-fight</c> leg passes the record to <see cref="FightScenario"/>).
/// </summary>
public sealed record FightOptions
{
    /// <summary>The default of <c>--fight-kills</c>.</summary>
    public const int DefaultKills = 1;

    /// <summary>The most kills a finite count elsewhere accepts (the death loop's <c>--death-fight-kills</c>, a journey plan's kills); <c>--fight-kills</c> itself has no upper bound since M5 1 F1.</summary>
    public const int MaxKills = 1000;

    /// <summary><c>--fight-kills 0</c>: no kill limit (M5 1 F1); the fight then ends at <see cref="Minutes"/> (cleanly) or <see cref="Seconds"/> (a failure).</summary>
    public const int UnlimitedKills = 0;

    /// <summary>The default of <c>--fight-max-level</c>: Flame Bolt VII (BSD Mage at level 127 keeps VII, D26; the profile's level rule is item 2's).</summary>
    public const int DefaultMaxLevel = 7;

    /// <summary>The most <c>--fight-minutes</c> accepts (Lifestone's own bound: a day, the M5 spec's run).</summary>
    public const double MaxMinutes = 24 * 60;

    /// <summary>The default of <c>--fight-seconds</c> (session-clock seconds from the fight's start).</summary>
    public const double DefaultSeconds = 300;

    /// <summary>
    /// The default of <c>--fight-ground</c>: the key of the hunting ground the fight, the walks, the death's checks and the journeys run in (M5 item 2 g2b, D46). The one place the code names a ground: every other
    /// part takes the <see cref="HuntingGround"/> this key resolves to (<see cref="GroundProfile"/>), so another dungeon is a profile file and a key.
    /// </summary>
    public const string DefaultGround = "black-spawn-den-aluvian";

    /// <summary>The key of the hunting ground (<c>--fight-ground</c>, default <see cref="DefaultGround"/>): a dungeon profile of <see cref="GroundCatalog"/>.</summary>
    public string Ground { get; init; } = DefaultGround;

    /// <summary>The hunting ground <see cref="Ground"/> names.</summary>
    /// <exception cref="KeyNotFoundException">No profile has the key (naming the known keys); <see cref="FromArguments"/> reports it as an error first.</exception>
    public HuntingGround GroundProfile => GroundCatalog.Get(Ground);

    /// <summary>The kills that end the fight (<c>--fight-kills</c>; <see cref="UnlimitedKills"/>, 0, is no kill limit).</summary>
    public int Kills { get; init; } = DefaultKills;

    /// <summary>The seconds after which the fight fails loudly (<c>--fight-seconds</c>); with <see cref="Minutes"/> set the failure limit is <see cref="FailSeconds"/>.</summary>
    public double Seconds { get; init; } = DefaultSeconds;

    /// <summary>
    /// The minutes after which the fight ends CLEANLY (<c>--fight-minutes</c>, M5 1 F1; 0, the default, is off): the scenario succeeds with the kills it made, unlike <see cref="Seconds"/>, which stays a failure limit.
    /// </summary>
    public double Minutes { get; init; }

    /// <summary>The highest Flame Bolt level the fight may choose (<c>--fight-max-level</c>, 1 to 7, default <see cref="DefaultMaxLevel"/>; D26).</summary>
    public int MaxLevel { get; init; } = DefaultMaxLevel;

    /// <summary>The default of <c>--fight-storeys-min</c>: 1, the tour leaves a storey only when it has no standing hunted slot (M5 item 2 g6, D40).</summary>
    public const int DefaultStoreysMin = 1;

    /// <summary>The most storeys <c>--fight-storeys-min</c> accepts (Lifestone's own bound; a ground with fewer storeys simply never reaches it).</summary>
    public const int MaxStoreysMin = 16;

    /// <summary>
    /// <c>--fight-storeys-min</c> (M5 item 2 g6, 1 to <see cref="MaxStoreysMin"/>, default <see cref="DefaultStoreysMin"/>): after one kill on a storey the ground's tour (<see cref="GroundTour"/>) leaves it for the next storey until this many
    /// distinct storeys are visited; the mage journey's den-fight station sets 2 so the station tours two storeys (k1).
    /// </summary>
    public int StoreysMin { get; init; } = DefaultStoreysMin;

    /// <summary>The default of <c>--fight-defend-forget-s</c>: 20 s (Lifestone's own number; retail has no such bound, a player simply stops fighting what stopped hitting him).</summary>
    public const double DefaultDefendForgetSeconds = 20;

    /// <summary>The most <c>--fight-defend-forget-s</c> accepts (Lifestone's own bound: ten minutes).</summary>
    public const double MaxDefendForgetSeconds = 600;

    /// <summary>
    /// <c>--fight-defend-forget-s</c> (M5 item 7 F4, Lifestone's own bound; 0 turns self-defence off): a creature that attacked the player stays the fight's target, before the profile's targets and whatever the profile says of it, until it is
    /// dead, gone from the object table, or has not attacked for this many session-clock seconds (<c>FightScenario.SelfDefence.cs</c>).
    /// </summary>
    public double DefendForgetSeconds { get; init; } = DefaultDefendForgetSeconds;

    /// <summary>The default of <c>--fight-defend-hold-s</c>: 30 s (Lifestone's own number, M5 item 7 F15).</summary>
    public const double DefaultDefendHoldSeconds = 30;

    /// <summary>
    /// <c>--fight-defend-hold-s</c> (M5 item 7 F15, 7-F5, Lifestone's own bound; 0: no bound): the session-clock seconds the fight holds for an attacker it cannot cast at (a no-go cell, no line) before it drops the attacker for the
    /// profile's targets.
    /// </summary>
    public double DefendHoldSeconds { get; init; } = DefaultDefendHoldSeconds;

    /// <summary>The default of <c>--fight-opener-tries</c>: 3 casts a target (Lifestone's own number: the first, and two more after a resist or a fizzle, the owner's fizzle rule's count).</summary>
    public const int DefaultOpenerTries = 3;

    /// <summary>The most <c>--fight-opener-tries</c> accepts (Lifestone's own bound).</summary>
    public const int MaxOpenerTries = 10;

    /// <summary>
    /// <c>--fight-opener-tries</c> (M5 item 7 F8, D89, Lifestone's own number; 0 turns openers off): the most casts of a target's opener (the spell its ground-profile entry names, <c>FightScenario.Opener.cs</c>) when it is resisted or fizzles;
    /// the cast then gives up and the bolts go on.
    /// </summary>
    public int OpenerTries { get; init; } = DefaultOpenerTries;

    /// <summary>
    /// <c>--fight-loot on|off</c> (M5 item 3 m2, D38; default off): after a kill the fight walks to the corpse when it lies out of reach (the leg logged <c>purpose=loot</c>, D47), opens it, keeps what the mage's rule says
    /// (<see cref="MageLootRules"/>: value per burden, bounded by the load line and the main pack's free slots; pyreals always), closes it and goes back to the tour. Off, nothing of this runs and the fight is exactly the one without it.
    /// </summary>
    public bool Loot { get; init; }

    /// <summary>The default of <c>--fight-loot-radius-m</c>: 15 m (Lifestone's own; the den's rooms are small).</summary>
    public const double DefaultLootRadiusMetres = 15;

    /// <summary>The default of <c>--fight-loot-walk-s</c>: 20 s (Lifestone's own; ACE's own MoveTo gives up at 15 s, Player_Move.cs:16 @95ef426d, and the open that follows is answered soon after).</summary>
    public const double DefaultLootWalkSeconds = 20;

    /// <summary>The most the loot options accept: 200 (metres or seconds; Lifestone's own bound).</summary>
    public const double MaxLootBound = 200;

    /// <summary>
    /// <c>--fight-loot-radius-m</c> (M5 7 F27, closes <c>m5-loot-no-walk</c>; Lifestone's own, default <see cref="DefaultLootRadiusMetres"/>): the loot step opens only a kill's corpse within this many metres of the mage with a clear line of sight to it (the fight's own shot line test);
    /// ACE's own MoveTo then walks the mage to it (Player_Use.cs:184-206, Player_Move.cs:37-96 @95ef426d).
    /// </summary>
    public double LootRadiusMetres { get; init; } = DefaultLootRadiusMetres;

    /// <summary><c>--fight-loot-walk-s</c> (M5 7 F27): the seconds the loot step waits for a corpse to open while ACE's MoveTo brings the mage; past it the corpse is left, named, and the fight resumes.</summary>
    public double LootWalkSeconds { get; init; } = DefaultLootWalkSeconds;

    /// <summary>
    /// The default of <c>--fight-loot-aggro-m</c>: 6 m (M5 7 loot retry; the owner wants the mage surrounded anyway, so a tusker somewhere in the den is no reason to skip a corpse: only one at the corpse's side is). Its own constant, no longer tied to the
    /// death trip's <see cref="DeathOptions.DefaultTripAggroMetres"/> (20 m), which stays as it was.
    /// </summary>
    public const double DefaultLootAggroMetres = 6;

    /// <summary>
    /// How long, in seconds from the kill, a corpse the loot step put off is still retried: 270 s, 30 s inside the 300 s a monster corpse lasts. ACE @95ef426d: a monster corpse rots 300 s after the kill (WorldObject_Decay.cs:18 DefaultTimeToRot, 5 minutes; the
    /// corpse weenie 21 has no TimeToRot of its own; Creature_Death.cs:446-448 creates it), 15 s once it is emptied (Corpse.cs:27 EmptyDecayTime) and 900 s when it generated a rare (Corpse.cs:356). A corpse whose window passes is given up and counted as left to decay.
    /// </summary>
    public const double LootRetryWindowSeconds = 270;

    /// <summary>
    /// <c>--fight-loot-aggro-m</c> (M5 7 F27; default <see cref="DefaultLootAggroMetres"/>, 6 m; 0 is off, rule and attacker check both): a corpse is put off (kept in the queue, retried when the fight has no target and the rule passes, until
    /// <see cref="LootRetryWindowSeconds"/> after the kill) only when a creature is attacking the mage now (the fight's self-defence list) or a living target creature of the ground stands within this many metres of the corpse.
    /// </summary>
    public double LootAggroMetres { get; init; } = DefaultLootAggroMetres;

    /// <summary>The mage's loot rule's numbers (D46: options, never a dungeon's constants); not on the command line. The defaults are Lifestone's own start values (<see cref="MageLootThresholds"/>).</summary>
    public MageLootThresholds LootThresholds { get; init; } = new();

    /// <summary>
    /// The thresholds the loot step runs with (M5 7 F28, D95): <see cref="LootThresholds"/> with the ground's loot setting laid under it. The keep floor is the profile's <c>loot.min_value_per_burden</c> (<see cref="HuntingGround.Loot"/>) unless
    /// <see cref="LootThresholds"/> already sets another floor than the default (an explicit value of the run wins); a ground without the section keeps the default. The sellable item types are the types some vendor of the ground buys
    /// (<see cref="HuntingGround.SellableItemTypes"/>) when the profile has a loot section and the thresholds name none, so a ground that has not been told what its loot is for keeps every type as before.
    /// </summary>
    public MageLootThresholds EffectiveLootThresholds
    {
        get
        {
            var ground = GroundProfile;
            if (ground.Loot is not { } setting)
            {
                return LootThresholds;
            }

            return LootThresholds with
            {
                MinValuePerBurden = LootThresholds.MinValuePerBurden == MageLootThresholds.DefaultMinValuePerBurden ? setting.MinValuePerBurden : LootThresholds.MinValuePerBurden,
                SellableItemTypes = LootThresholds.SellableItemTypes != 0 ? LootThresholds.SellableItemTypes : ground.SellableItemTypes,
                MinPayout = LootThresholds.MinPayout != 0 ? LootThresholds.MinPayout : setting.MinPayout, // M5 7 F29 (D96): the payout floor, the per-item burden cap, the buyers' rates and the load line, all the profile's
                MaxItemBurden = LootThresholds.MaxItemBurden != 0 ? LootThresholds.MaxItemBurden : setting.MaxItemBurden,
                Buyers = LootThresholds.Buyers.Count != 0 ? LootThresholds.Buyers : ground.LootBuyers,
                LoadLinePercent = LootThresholds.LoadLinePercent == MageLootThresholds.DefaultLoadLinePercent && setting.LoadLinePercent > 0 ? setting.LoadLinePercent : LootThresholds.LoadLinePercent,
                KeepAside = LootThresholds.KeepAside ?? (setting.KeepAside is { } keepAside ? new KeepAsideRule(keepAside) : null), // M5 7 F30 (the owner): the wands and cantrip pieces kept aside and never sold
            };
        }
    }

    /// <summary>
    /// The default of <c>--fight-stamina-full</c> (M5 7 F5, the owner's D88: in the fight the mana may run down to nearly empty and the stamina is kept near full). Lifestone's own number: the stamina regenerates nothing at its
    /// maximum, and a Revitalize Self restores 100 to 200 at level VII, so 90% leaves one cast's room and the pre-emptive transfer (<see cref="ManaOrder.PreemptStaminaShare"/>, also 0.9) fires from the same share.
    /// </summary>
    public const int DefaultStaminaFullPercent = 90;

    /// <summary>
    /// <c>--fight-stamina-full</c> (M5 7 F5, 0 to 100, default <see cref="DefaultStaminaFullPercent"/>): the stamina share, in percent, under which the fight refills the stamina with Revitalize Self (the highest level the mana
    /// pays with the next bolt in hand and the pack's scarabs allow) once a Stamina to Mana Self has landed and between kills; 0 turns the refill off (the order of M5 item 1 exactly).
    /// </summary>
    public int StaminaFullPercent { get; init; } = DefaultStaminaFullPercent;

    /// <summary>
    /// The default of <c>--fight-drain-under</c> (M5 7 F7, the owner's D88: the soak's level-80 mage has no Melee Defense and soaks the damage; when its health runs low it drains its target instead of standing there). Lifestone's own
    /// number: half the health is the line the owner named; the mage is never put in reach of a tusker's burst by waiting for less.
    /// </summary>
    public const int DefaultDrainUnderPercent = 50;

    /// <summary>The default of <c>--fight-drain-until</c> (M5 7 F7, D88): Lifestone's own number, three quarters of the health, the safe line at which the bolts begin again (a gap over <see cref="DefaultDrainUnderPercent"/> so the rule does not flip on every hit).</summary>
    public const int DefaultDrainUntilPercent = 75;

    /// <summary>
    /// <c>--fight-drain-under</c> (M5 7 F7, 0 to 100, default <see cref="DefaultDrainUnderPercent"/>): the health share, in percent, under which the fight casts Drain Health Other on its target (or Heal Self when no target is in reach)
    /// instead of a bolt; 0 turns the rule off.
    /// </summary>
    public int DrainUnderPercent { get; init; } = DefaultDrainUnderPercent;

    /// <summary><c>--fight-drain-until</c> (M5 7 F7, 0 to 100, default <see cref="DefaultDrainUntilPercent"/>): the health share, in percent, at or over which the drain rule ends and the bolts begin again.</summary>
    public int DrainUntilPercent { get; init; } = DefaultDrainUntilPercent;

    /// <summary>The default of <c>--fight-emergency-floor</c> (M5 7 F16, D91): Lifestone's own number, three fifths of the health, the least value the emergency line takes (the line itself is live: <see cref="Lifestone.Client.Session.Vitals.FightEmergency"/>).</summary>
    public const int DefaultEmergencyFloorPercent = 60;

    /// <summary>The default of <c>--fight-emergency-safe</c> (M5 7 F16, D91): Lifestone's own number, the health share at which the emergency drain ends and the fight's normal choices begin again.</summary>
    public const int DefaultEmergencySafePercent = 85;

    /// <summary>The default of <c>--fight-emergency-margin</c> (M5 7 F16, D91): Lifestone's own number, a tenth of the maximum health added to the damage expected during three casts of the emergency drain.</summary>
    public const int DefaultEmergencyMarginPercent = 10;

    /// <summary><c>--fight-emergency-floor</c> (M5 7 F16, 0 to 100, default <see cref="DefaultEmergencyFloorPercent"/>): the least health share, in percent, of the emergency line; 0 turns the emergency drain off.</summary>
    public int EmergencyFloorPercent { get; init; } = DefaultEmergencyFloorPercent;

    /// <summary><c>--fight-emergency-safe</c> (M5 7 F16, 0 to 100, default <see cref="DefaultEmergencySafePercent"/>): the health share, in percent, at or over which the emergency drain ends (never under the floor).</summary>
    public int EmergencySafePercent { get; init; } = DefaultEmergencySafePercent;

    /// <summary><c>--fight-emergency-margin</c> (M5 7 F16, 0 to 100, default <see cref="DefaultEmergencyMarginPercent"/>): the margin, in percent of the maximum health, added to the expected damage of three emergency casts.</summary>
    public int EmergencyMarginPercent { get; init; } = DefaultEmergencyMarginPercent;

    /// <summary><c>--fight-heal-damage-weight</c> (M5 7 F21, D92, 0 to 1, default <see cref="Lifestone.Client.Session.Vitals.HealPick.DefaultDamageWeight"/>): the share of the damage a drain does to its target that counts as health a second when the drain is weighed against Heal Self.</summary>
    public double HealDamageWeight { get; init; } = Lifestone.Client.Session.Vitals.HealPick.DefaultDamageWeight;

    /// <summary>The mana order's options (<c>--cast-mana-*</c>, <see cref="ManaOrderOptions"/>; <c>--cast-mana-tries 0</c> is today's ceiling wait exactly).</summary>
    public ManaOrderOptions Mana { get; init; } = ManaOrderOptions.Default;

    /// <summary>True for <c>--fight-kills 0</c>: no kill limit.</summary>
    public bool UnlimitedKillCount => Kills == UnlimitedKills;

    /// <summary>The fight's failure limit in seconds: <see cref="Seconds"/>, or with <see cref="Minutes"/> set the larger of it and the minutes (the clean stop is judged first, so it is never a failure).</summary>
    public double FailSeconds => Minutes > 0 ? Math.Max(Seconds, Minutes * 60) : Seconds;

    /// <summary>The default of <see cref="ArenaSkill"/>: the stand-in's fixed magic skill (<c>TODO(m3-standin-magic-skill)</c>, <c>StandInWorld.PlaceholderMagicSkill</c>).</summary>
    public const uint DefaultArenaSkill = 300;

    /// <summary>
    /// True: the arena is where the player already stands (M3 3.1 fd; not on the command line): no gear, no buff top-up, no teleport to the ground, and the floor rule is off (any cell, not only the ground's walkable cells),
    /// so the offline stand-in's tusker, placed anywhere, is a target. The Flame Bolt level and the cast range then come from <see cref="ArenaSkill"/> alone (every level counts as known), not from the read model's War Magic skill and spellbook (the stand-in has no such skill, and its book is not updated by a spell learn).
    /// </summary>
    public bool ArenaHere { get; init; }

    /// <summary>
    /// M5 7 F30 (soak round 7: a resumed fight in the den teleported to the arrival and walked 253 m across the den): true makes the fight start from where the player stands in the ground: the place and settle stages are left out
    /// (they are the journey's way in, not the autopilot's resume), logged <c>fight: resumed on the ground: no walk to the arrival</c>. Set by the den-fight spec's <see cref="Journey.JourneyStations.ResumeOnGroundArgument"/>.
    /// </summary>
    public bool ResumeOnGround { get; init; }

    /// <summary>With <see cref="ArenaHere"/>: the War Magic skill the Flame Bolt level and the range are worked out from (<see cref="DefaultArenaSkill"/>).</summary>
    public uint ArenaSkill { get; init; } = DefaultArenaSkill;

    /// <summary>True: the loop picks the nearest tusker with no check of the line to it (<c>--fight-ignore-line</c>; default false: a clear line is required, M3 4.2 P4).</summary>
    public bool IgnoreLine { get; init; }

    /// <summary>
    /// A spell the fight casts instead of the Flame Bolt line (M4 P5b J7: the hero plan's Cassius' Ring of Fire, 1785); 0 is the Flame Bolt line, the level chosen by the cast chance. The spell must be in the spellbook and reach the cast chance at the buffed War Magic skill, or the fight fails naming it.
    /// </summary>
    public uint SpellId { get; init; }

    /// <summary>
    /// M4 P5b (3.4): the travel seam the fight's place and its den exit go through, or null for today's <c>Tools.Place</c> (a fresh <see cref="PlaceTravel"/> per scenario, so every existing run is unchanged). A journey injects
    /// its own. Not on the command line.
    /// </summary>
    public IJourneyTravel? Travel { get; init; }

    /// <summary>
    /// M4 P5b: why the fight's place travels (<see cref="TravelPurpose.Spot"/> by default: a fight spot in the den, on foot in a journey; the death loop's composed fight, after the respawn, asks for
    /// <see cref="TravelPurpose.Ground"/>: back to the den, Primary Portal Recall in a journey).
    /// </summary>
    public TravelPurpose PlacePurpose { get; init; } = TravelPurpose.Spot;

    /// <summary>
    /// M4 P5b (3.4): true (the default, every existing run) leaves the den on a failure (<see cref="GroundExit"/>) before the run ends; false (a journey's station) ends Failed where the player stands, in the world: no exit, and
    /// no log off when <c>endSession</c> is false (the journey recovers).
    /// </summary>
    public bool LeaveOnFailure { get; init; } = true;

    /// <summary>
    /// M4 P5b: true (the default, every existing run) leaves the den after the fight (<see cref="GroundExit"/>); false ends in the den (a journey's den fight is followed by the death station there, and the death loop's
    /// composed fight in a journey by the corpse trip on foot).
    /// </summary>
    public bool LeaveAtEnd { get; init; } = true;

    /// <summary>
    /// M5 7 F25 (D93, not a command-line option): true when the fight is one of the autopilot's own stations (its open-ended den fight, the death handler's corpse-trip fights): the fight's end leaves the magic mode up, because the
    /// autopilot's next station on the ground wants it again (soak round 3: 75 mode changes in 71 minutes, a peace at each fight's end and the magic mode again at once). The stance leaves the magic mode only for the stop, the log off, a
    /// vendor (a station with a Peace need) or a person's hand-over.
    /// </summary>
    public bool HoldStance { get; init; }

    /// <summary>Every problem found while parsing, one line each.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>Parses the options from arguments already split; an option that does not parse is reported in <see cref="Errors"/> and keeps its default.</summary>
    /// <param name="args">The user arguments.</param>
    public static FightOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new FightOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            var known = errors.Count;
            switch (argument.Name)
            {
                case "--fight-kills":
                    var kills = OptionValues.Int(argument, errors, o.Kills);
                    if (errors.Count == known && kills >= UnlimitedKills)
                    {
                        o = o with { Kills = kills };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not 0 (no kill limit) or a positive whole number");
                    }

                    break;
                case "--fight-seconds":
                    var seconds = OptionValues.Double(argument, errors, o.Seconds);
                    if (errors.Count == known && seconds > 0)
                    {
                        o = o with { Seconds = seconds };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a positive number of seconds");
                    }

                    break;
                case "--fight-minutes":
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
                case "--fight-max-level":
                    var level = OptionValues.Int(argument, errors, o.MaxLevel);
                    if (errors.Count == known && level is >= 1 and <= DefaultMaxLevel)
                    {
                        o = o with { MaxLevel = level };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a Flame Bolt level 1 to {DefaultMaxLevel}");
                    }

                    break;
                case "--fight-storeys-min":
                    var storeys = OptionValues.Int(argument, errors, o.StoreysMin);
                    if (errors.Count == known && storeys is >= 1 and <= MaxStoreysMin)
                    {
                        o = o with { StoreysMin = storeys };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a number of storeys 1 to {MaxStoreysMin}");
                    }

                    break;
                case "--fight-defend-forget-s":
                    var forget = OptionValues.Double(argument, errors, o.DefendForgetSeconds);
                    if (errors.Count == known && forget is >= 0 and <= MaxDefendForgetSeconds)
                    {
                        o = o with { DefendForgetSeconds = forget };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 (self-defence off) to {MaxDefendForgetSeconds:0} seconds"));
                    }

                    break;
                case "--fight-defend-hold-s":
                    var hold = OptionValues.Double(argument, errors, o.DefendHoldSeconds);
                    if (errors.Count == known && hold is >= 0 and <= MaxDefendForgetSeconds)
                    {
                        o = o with { DefendHoldSeconds = hold };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 (no bound) to {MaxDefendForgetSeconds:0} seconds"));
                    }

                    break;
                case "--fight-opener-tries":
                    var opener = OptionValues.Int(argument, errors, o.OpenerTries);
                    if (errors.Count == known && opener is >= 0 and <= MaxOpenerTries)
                    {
                        o = o with { OpenerTries = opener };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not 0 (openers off) to {MaxOpenerTries} casts");
                    }

                    break;
                case "--fight-ground":
                    if (GroundError(argument.Value) is { } groundError)
                    {
                        errors.Add($"{argument.Name}: {groundError}");
                    }
                    else
                    {
                        o = o with { Ground = argument.Value!.Trim() };
                    }

                    break;
                case "--fight-loot-radius-m":
                    var lootRadius = OptionValues.Double(argument, errors, o.LootRadiusMetres);
                    if (errors.Count == known && lootRadius is >= 0 and <= MaxLootBound)
                    {
                        o = o with { LootRadiusMetres = lootRadius };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 to {MaxLootBound:0} metres"));
                    }

                    break;
                case "--fight-loot-walk-s":
                    var lootWalk = OptionValues.Double(argument, errors, o.LootWalkSeconds);
                    if (errors.Count == known && lootWalk is > 0 and <= MaxLootBound)
                    {
                        o = o with { LootWalkSeconds = lootWalk };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not more than 0 to {MaxLootBound:0} seconds"));
                    }

                    break;
                case "--fight-loot-aggro-m":
                    var lootAggro = OptionValues.Double(argument, errors, o.LootAggroMetres);
                    if (errors.Count == known && lootAggro is >= 0 and <= MaxLootBound)
                    {
                        o = o with { LootAggroMetres = lootAggro };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add(string.Create(CultureInfo.InvariantCulture, $"{argument.Name}: '{argument.Value}' is not 0 (off) to {MaxLootBound:0} metres"));
                    }

                    break;
                case "--fight-loot":
                    o = o with { Loot = OptionValues.Switch(argument, errors, o.Loot) };
                    break;
                case "--fight-stamina-full":
                    var full = OptionValues.Int(argument, errors, o.StaminaFullPercent);
                    if (errors.Count == known && full is >= 0 and <= 100)
                    {
                        o = o with { StaminaFullPercent = full };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a stamina share 0 (off) to 100 percent");
                    }

                    break;
                case "--fight-drain-under":
                    var drainUnder = OptionValues.Int(argument, errors, o.DrainUnderPercent);
                    if (errors.Count == known && drainUnder is >= 0 and <= 100)
                    {
                        o = o with { DrainUnderPercent = drainUnder, DrainUntilPercent = Math.Max(o.DrainUntilPercent, drainUnder) };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a health share 0 (off) to 100 percent");
                    }

                    break;
                case "--fight-drain-until":
                    var drainUntil = OptionValues.Int(argument, errors, o.DrainUntilPercent);
                    if (errors.Count == known && drainUntil is >= 0 and <= 100)
                    {
                        o = o with { DrainUntilPercent = Math.Max(drainUntil, o.DrainUnderPercent) };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a health share 0 to 100 percent");
                    }

                    break;
                case "--fight-emergency-floor":
                    var emergencyFloor = OptionValues.Int(argument, errors, o.EmergencyFloorPercent);
                    if (errors.Count == known && emergencyFloor is >= 0 and <= 100)
                    {
                        o = o with { EmergencyFloorPercent = emergencyFloor, EmergencySafePercent = Math.Max(o.EmergencySafePercent, emergencyFloor) };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a health share 0 (off) to 100 percent");
                    }

                    break;
                case "--fight-emergency-safe":
                    var emergencySafe = OptionValues.Int(argument, errors, o.EmergencySafePercent);
                    if (errors.Count == known && emergencySafe is >= 0 and <= 100)
                    {
                        o = o with { EmergencySafePercent = Math.Max(emergencySafe, o.EmergencyFloorPercent) };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a health share 0 to 100 percent");
                    }

                    break;
                case "--fight-emergency-margin":
                    var emergencyMargin = OptionValues.Int(argument, errors, o.EmergencyMarginPercent);
                    if (errors.Count == known && emergencyMargin is >= 0 and <= 100)
                    {
                        o = o with { EmergencyMarginPercent = emergencyMargin };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a health share 0 to 100 percent");
                    }

                    break;
                case "--fight-heal-damage-weight":
                    var healWeight = OptionValues.Double(argument, errors, o.HealDamageWeight);
                    if (errors.Count == known && healWeight is >= 0 and <= 1)
                    {
                        o = o with { HealDamageWeight = healWeight };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not a share 0 to 1");
                    }

                    break;
                case "--fight-ignore-line":
                    o = o with { IgnoreLine = OptionValues.Switch(argument, errors, o.IgnoreLine) };
                    break;
                default:
                    var mana = o.Mana;
                    if (ManaOrderOptions.Apply(argument, errors, ref mana))
                    {
                        o = o with { Mana = mana };
                    }

                    break;
            }
        }

        if (o.Kills == UnlimitedKills && o.Minutes <= 0)
        {
            errors.Add("--fight-kills: 0 (no kill limit) needs --fight-minutes above 0: the fight would end only at --fight-seconds, as a failure");
            o = o with { Kills = DefaultKills }; // the bad value keeps its default, as every option's
        }

        return o with { Errors = errors };
    }

    /// <summary>Why <paramref name="key"/> is not a ground the fight can run in (no value, no such profile, or not a dungeon), or null when it is one.</summary>
    /// <param name="key">The key as given.</param>
    public static string? GroundError(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return $"needs the key of a hunting ground (known: {Known()})";
        }

        var ground = GroundCatalog.All.FirstOrDefault(g => string.Equals(g.Key, key.Trim(), StringComparison.Ordinal));
        if (ground is null)
        {
            return $"'{key}' is not a hunting ground (known: {Known()})";
        }

        return ground.Kind == GroundKind.Dungeon ? null : $"'{key}' is not a dungeon: the fight loop walks a dungeon's storeys";

        static string Known() => string.Join(", ", GroundCatalog.All.Where(g => g.Kind == GroundKind.Dungeon).Select(g => g.Key));
    }

    private static bool IsDefaultMana(ManaOrderOptions m) =>
        m.Tries == ManaOrderOptions.DefaultTries && m.TryChance == ManaOrderOptions.DefaultTryChance && m.TrySpacingSeconds == ManaOrderOptions.DefaultTrySpacingSeconds && m.DancePresses == ManaOrderOptions.DefaultDancePresses;

    /// <summary>The options as the run logs them, in invariant culture.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"kills={(Kills == UnlimitedKills ? "unlimited" : Kills.ToString(CultureInfo.InvariantCulture))} seconds={Seconds}{(Minutes > 0 ? $" minutes={Minutes}" : string.Empty)}{(MaxLevel != DefaultMaxLevel ? $" max_level={MaxLevel}" : string.Empty)}{(StoreysMin != DefaultStoreysMin ? $" storeys_min={StoreysMin}" : string.Empty)}{(OpenerTries != DefaultOpenerTries ? $" opener_tries={OpenerTries}" : string.Empty)}{(DefendForgetSeconds != DefaultDefendForgetSeconds ? $" defend_forget_s={DefendForgetSeconds}" : string.Empty)}{(DefendHoldSeconds != DefaultDefendHoldSeconds ? $" defend_hold_s={DefendHoldSeconds}" : string.Empty)}{(StaminaFullPercent != DefaultStaminaFullPercent ? $" stamina_full={StaminaFullPercent}" : string.Empty)}{(DrainUnderPercent != DefaultDrainUnderPercent ? $" drain_under={DrainUnderPercent}" : string.Empty)}{(DrainUntilPercent != DefaultDrainUntilPercent ? $" drain_until={DrainUntilPercent}" : string.Empty)}{(IsDefaultMana(Mana) ? string.Empty : " " + Mana)}{(IgnoreLine ? " ignore_line=on" : string.Empty)}{(Loot ? " loot=on" : string.Empty)}{(LootRadiusMetres != DefaultLootRadiusMetres ? $" loot_radius_m={LootRadiusMetres}" : string.Empty)}{(LootWalkSeconds != DefaultLootWalkSeconds ? $" loot_walk_s={LootWalkSeconds}" : string.Empty)}{(LootAggroMetres != DefaultLootAggroMetres ? $" loot_aggro_m={LootAggroMetres}" : string.Empty)}{(Ground == DefaultGround ? string.Empty : " ground=" + Ground)}{(SpellId == 0 ? string.Empty : $" spell={SpellId}")}{(ArenaHere ? $" arena=here skill={ArenaSkill}" : string.Empty)}{(Travel is null or PlaceTravel ? string.Empty : " travel=journey")}{(LeaveOnFailure ? string.Empty : " leave_on_failure=no")}{(LeaveAtEnd ? string.Empty : " leave_at_end=no")}");
}

/// <summary>
/// M5 4 a1 (D57): why an open-ended fight was asked to end (<see cref="IStoppableFight.RequestStop"/>). An open set: a name, never an enum, so a later slice adds its own reasons (a3: a buff line under its margin, the health floor)
/// without editing the fight. The fight's verdict line names it (<c>stop=&lt;name&gt;</c>); the autopilot routes by it (a trip, a stop, the scripted death; any other name is the upkeep's).
/// </summary>
/// <param name="Name">The reason's token (lower case, hyphens: <c>logistics-trip</c>).</param>
public readonly record struct FightStopReason(string Name)
{
    /// <summary>The logistics counters say a trip is due (<c>LogisticsCounters.Check</c>, <see cref="Mage.LogisticsKind.Trip"/>).</summary>
    public static FightStopReason LogisticsTrip { get; } = new("logistics-trip");

    /// <summary>The logistics counters say stop (<see cref="Mage.LogisticsKind.Stop"/>: a line no vendor restocks, or the purse is spent).</summary>
    public static FightStopReason LogisticsStop { get; } = new("logistics-stop");

    /// <summary>A stop rule of the autopilot (its minutes, its level) or a stop request (the key, the host).</summary>
    public static FightStopReason StopRule { get; } = new("stop-rule");

    /// <summary>The forced trip after <c>--autopilot-trip-after-kills</c> kills (D52's live half).</summary>
    public static FightStopReason ForcedTrip { get; } = new("forced-trip");

    /// <summary>The scripted death of the gate (<c>--autopilot-test-death</c>, M4 D37: undress and lie down).</summary>
    public static FightStopReason TestDeath { get; } = new("test-death");

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// M5 4 a1 (D57): a fight that an outer loop ends from outside at its next safe point (the same point as the clean <c>--fight-minutes</c> stop: the casts and the dance in flight are stopped and counted, the walk abandoned, the peace mode asked
/// for, then the fight succeeds with the kills it made). <see cref="FightScenario"/> is the real one; the autopilot's tests script one.
/// </summary>
public interface IStoppableFight
{
    /// <summary>The kills counted so far (the server's 0x01AD notices).</summary>
    int Kills { get; }

    /// <summary>The biggest single hit the player took in this fight (M5 7 F16, the soak line's <c>hit_max=</c>); 0 when the implementation does not count.</summary>
    long HitMax => 0;

    /// <summary>The casts whose target died or left during the wind-up in this fight (M5 7 F28, the soak line's <c>target_died_mid_cast=</c>); 0 when the implementation does not count.</summary>
    long TargetDiedMidCast => 0;

    /// <summary>The reason asked for, or null while none was.</summary>
    FightStopReason? StopReason { get; }

    /// <summary>Asks the fight to end cleanly at its next safe point; the first reason asked for is kept (a later one is logged and ignored).</summary>
    /// <param name="reason">Why.</param>
    void RequestStop(FightStopReason reason);
}

/// <summary>
/// M5 7 F23: a fight that refreshes the buff lines under a margin itself, between kills, when no hostile is in reach, so the autopilot never stops it for a buff margin with a creature on the mage. <see cref="FightScenario"/> is the real one.
/// </summary>
public interface IBuffRefreshFight
{
    /// <summary>True while a refresh is asked and not yet done.</summary>
    bool BuffRefreshAsked { get; }

    /// <summary>The refresh passes done in this fight.</summary>
    int BuffRefreshes { get; }

    /// <summary>Asks for one refresh pass of the lines under <paramref name="marginSeconds"/>, run at the fight's next Choose point with no hostile within <paramref name="reachMetres"/>; a second ask while one is pending does nothing.</summary>
    /// <param name="marginSeconds">The re-buff margin.</param>
    /// <param name="reachMetres">The aggro reach.</param>
    /// <param name="detail">The due lines, for the log.</param>
    void RequestBuffRefresh(double marginSeconds, double reachMetres, string detail);

    /// <summary>M5 7 F25: true once the fight keeps the buff lines itself (armed by the first ask: the lookahead at every lull, the hard floor under fire), so the autopilot does not ask again.</summary>
    bool BuffsKeptAhead => false;

    /// <summary>M5 7 F29 (D96): the line passes that cast something in this fight; the autopilot counts a buff cycle for each full set of <see cref="BuffLineCount"/> lines recast.</summary>
    int BuffLinePasses => 0;

    /// <summary>M5 7 F29: the buff lines the fight watches (a full cycle recasts each once); 0 before it has read them.</summary>
    int BuffLineCount => 0;

    /// <summary>D126 (a): true while a stop waits for the lines due within a trip's length (<see cref="RequestLeaveRefresh"/>).</summary>
    bool LeaveRefreshAsked => false;

    /// <summary>
    /// D126 (a): asks the fight, before a stop for a trip, to recast every line with less than <paramref name="horizonSeconds"/> left (the trip's expected length plus a margin) and the rest of its batch, between its casts whatever is in
    /// reach; the stop asked after it waits for that (bounded). Arms the fight's own upkeep. A second ask while one is pending does nothing.
    /// </summary>
    /// <param name="horizonSeconds">The seconds ahead a line must last.</param>
    /// <param name="reachMetres">The aggro reach (for the fight's lull, when not yet set).</param>
    /// <param name="detail">Why, for the log.</param>
    void RequestLeaveRefresh(double horizonSeconds, double reachMetres, string detail)
    {
    }
}
