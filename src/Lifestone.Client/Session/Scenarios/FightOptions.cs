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
    public HuntingGround GroundProfile => GroundForTest ?? GroundCatalog.Get(Ground);

    /// <summary>A test's altered copy of the ground; null in every run.</summary>
    internal HuntingGround? GroundForTest { get; init; }

    /// <summary>
    /// M6 5 q4 (D57): the weenie class ids of the items this fight wants (the den-fight spec's <c>want=</c>): a corpse's item of one is kept aside under every value floor and the heavy rule (the verdict <c>quest: want &lt;wcid&gt;</c>), never sold. Empty by default.
    /// </summary>
    public IReadOnlyList<uint> Want { get; init; } = [];

    /// <summary>
    /// M6 14 want loot: the creatures whose create list can hold a wanted item (<see cref="Want"/>), each with its row's chance (the den-fight spec's <c>carriers=</c>, <see cref="WantCarrier"/>): a corpse of one is walked to and opened
    /// whatever its distance on the ground. Empty by default; a fight that wants an item with none names <c>TODO(m6-create-list-general-read)</c> once and keeps the loot radius.
    /// </summary>
    public IReadOnlyList<WantCarrier> WantCarriers { get; init; } = [];

    /// <summary>M6 7 AY: the fight holds to the ground's first slots: <see cref="Grounds.GroundHunt.DropBack"/> stays out of the hunt (<see cref="Mage.BigPackRule"/>, the den-fight spec's <c>pack-hold=</c>).</summary>
    public bool PackHold { get; init; }

    /// <summary>M6 5 q4 (D57): the fight's own end (the den-fight spec's <c>until=</c>): an item count held or a chat line seen since the fight began ends it cleanly at the clean minutes' safe point; null (the default) is none.</summary>
    public FightUntil? Until { get; init; }

    /// <summary>M6 5 q4: why the station's spec arguments could not be read (a malformed <c>ground=</c>, <c>want=</c> or <c>until=</c>), or null; the fight stops at its first step naming it (a loud stop, never a silent guess).</summary>
    public string? SpecError { get; init; }

    /// <summary>The kills that end the fight (<c>--fight-kills</c>; <see cref="UnlimitedKills"/>, 0, is no kill limit).</summary>
    public int Kills { get; init; } = DefaultKills;

    /// <summary>The seconds after which the fight fails loudly (<c>--fight-seconds</c>); with <see cref="Minutes"/> set the failure limit is <see cref="FailSeconds"/>. M6 7 D: a fight with its quota met whose <see cref="StoreysMin"/> crossing is owed and progressing goes on past it, at most <see cref="FightScenario.CrossingBudgetTimes"/> times it.</summary>
    public double Seconds { get; init; } = DefaultSeconds;

    /// <summary>
    /// The minutes after which the fight ends CLEANLY (<c>--fight-minutes</c>, M5 1 F1; 0, the default, is off): the scenario succeeds with the kills it made, unlike <see cref="Seconds"/>, which stays a failure limit.
    /// </summary>
    public double Minutes { get; init; }

    /// <summary>The highest Flame Bolt level the fight may choose (<c>--fight-max-level</c>, 1 to 7, default <see cref="DefaultMaxLevel"/>; D26).</summary>
    public int MaxLevel { get; init; } = DefaultMaxLevel;

    /// <summary>
    /// M6 14 (an owner ruling, 2026-11-13): <see cref="MaxLevel"/> is the level up job's live cap (the ladder's <c>AutopilotScenario.BoltLevel</c>: the highest level at the plan chance on her current, buffed War Magic, never under the rung's plan),
    /// so the ground's <c>spell_plan</c> row, the projection on the base skill, does not lower it (<see cref="FightScenario.PlanCap(Grounds.HuntingGround, int, FightOptions, out string)"/>; the row is named in the log line). Set by the
    /// den-fight spec's <see cref="Journey.JourneyPlans.MaxLevelArgument"/> when the command line gave no <c>--fight-max-level</c>. Not a command-line option. False (every other fight, BSD Mage's den job among them): the row caps as before.
    /// </summary>
    public bool LiveCap { get; init; }

    /// <summary>
    /// M6 14 (<c>m6-vitals-policy</c>): the climber's fills go by <see cref="Vitals.VitalsPolicy"/> while the ladder's <c>vitals_policy.on</c> holds (true, the default). Not a command-line option: the ladder's line is the switch; false
    /// keeps M6 8 H's rule by the sum in this fight (its own tests read it).
    /// </summary>
    public bool ManaPolicy { get; init; } = true;

    /// <summary>
    /// The levels above the character's own that an outdoor ground's pick still takes (M6 0 F5, review 0-R5, D26): the rung's <c>target_margin</c>, passed by the level up job as the station argument <c>target-margin</c>. Not a command-line
    /// option. Null (a fight with no rung): the pick is not banded by the character's level, and the fight logs TODO(m6-fight-no-target-margin) once.
    /// </summary>
    public int? TargetMargin { get; init; }

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
    /// M6 14 (<c>m6-lore-cantrips</c>; the owner, 2026-11-14: keep great cantrips on every ground the climber hunts): the keep-aside block the loot runs where the ground's profile has none (<see cref="KeepAsideRule.DefaultFor"/>: the climber's
    /// <see cref="KeepAsideRule.ClimberDefault"/>); a profile's own block wins. Set by the journey's stations from the plan's character; not on the command line. Null (the default): a ground without the block keeps nothing aside.
    /// </summary>
    public GroundKeepAside? KeepAsideDefault { get; init; }

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
                return JumpCapped(LootThresholds.KeepAside is null && KeepAsideDefault is { } byDefault ? LootThresholds with { KeepAside = new KeepAsideRule(byDefault) } : LootThresholds, ground); // M6 14 (m6-lore-cantrips): the climber's default
            }

            return JumpCapped(LootThresholds with
            {
                MinValuePerBurden = LootThresholds.MinValuePerBurden == MageLootThresholds.DefaultMinValuePerBurden ? setting.MinValuePerBurden : LootThresholds.MinValuePerBurden,
                SellableItemTypes = LootThresholds.SellableItemTypes != 0 ? LootThresholds.SellableItemTypes : ground.SellableItemTypes,
                MinPayout = LootThresholds.MinPayout != 0 ? LootThresholds.MinPayout : setting.MinPayout, // M5 7 F29 (D96): the payout floor, the per-item burden cap, the buyers' rates and the load line, all the profile's
                MaxItemBurden = LootThresholds.MaxItemBurden != 0 ? LootThresholds.MaxItemBurden : setting.MaxItemBurden,
                Buyers = LootThresholds.Buyers.Count != 0 ? LootThresholds.Buyers : ground.LootBuyers,
                LoadLinePercent = LootThresholds.LoadLinePercent == MageLootThresholds.DefaultLoadLinePercent && setting.LoadLinePercent > 0 ? setting.LoadLinePercent : LootThresholds.LoadLinePercent,
                KeepAside = LootThresholds.KeepAside ?? ((setting.KeepAside ?? KeepAsideDefault) is { } keepAside ? new KeepAsideRule(keepAside) : null), // M5 7 F30 (the owner): the wands and cantrip pieces kept aside and never sold; M6 14 (m6-lore-cantrips): the profile's block, else the climber's default
            }, ground);
        }
    }

    /// <summary>M6 14 pit-hold: on a ground with jumps the loot's load line is held to <see cref="MageLootThresholds.JumpLoadLinePercent"/> (100%: past it the load slows her run and lowers her jump, and the jump's need is fixed).</summary>
    private static MageLootThresholds JumpCapped(MageLootThresholds thresholds, HuntingGround ground)
    {
        var line = MageLootThresholds.JumpCapped(thresholds.LoadLinePercent, ground.Jumps.Count > 0);
        return line == thresholds.LoadLinePercent ? thresholds : thresholds with { LoadLinePercent = line };
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
    public static int DefaultDrainUnderPercent => Grounds.LadderLoader.WarMage.Vitals.DrainUnderPercent; // M6 7 AN: data, the ladder's vitals.drain_under_percent and its source sentence (every ground, not only level 80)

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
    /// M6 14 (<c>m6-life-through</c>, <c>--fight-life-through</c>; default on): an attacker no bolt can reach from where the mage stands (no clear line, or dropped by the shot rule) that is within the life spells' range is
    /// answered by Drain Health Other or Harm Other in place of the walk (<c>FightScenario.LifeThrough.cs</c>). Off: the walk, the sidestep and the tour's leg, as before.
    /// </summary>
    public bool LifeThrough { get; init; } = true;

    /// <summary>
    /// M6 16 Q6 (<c>--fight-survival-mana</c>; default off): the fight's mana loop and stamina drain take their decision from the survival layer (<see cref="Autopilot.SurvivalRule.DecideFightMana"/>: the press, the stamina drain with its
    /// target, or nothing; under the vitals policy its fills too) and the fight carries it out. Off: the loop decides by its own rule, as before. Stage 1 of <c>TODO(engine-survival-flip)</c>; the default flips after a live gate.
    /// </summary>
    public bool SurvivalMana { get; init; }

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

    /// <summary>M6 7 H: the buff profile the fight's cycles run (<see cref="Journey.JourneyPlan.BuffProfile"/>), or null: each cycle's own choice by the character's name.</summary>
    public Mage.IBuffProfile? BuffProfile { get; init; }

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
                case "--fight-life-through":
                    o = o with { LifeThrough = OptionValues.Switch(argument, errors, o.LifeThrough) };
                    break;
                case "--fight-survival-mana":
                    o = o with { SurvivalMana = OptionValues.Switch(argument, errors, o.SurvivalMana) };
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

    /// <summary>Why <paramref name="key"/> is not a ground the fight can run in (no value, no such profile, or neither a dungeon nor an outdoor ground with cells), or null when it is one (M6 item 0 a5: an outdoor ground walks its outdoor cells).</summary>
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

        return ground.Kind == GroundKind.Dungeon || ground.IsOutdoorFight() ? null : $"'{key}' is not a dungeon and has no outdoor cells: the fight loop walks a dungeon's storeys or an outdoor ground's cells";

        static string Known() => string.Join(", ", GroundCatalog.All.Where(g => g.Kind == GroundKind.Dungeon || g.IsOutdoorFight()).Select(g => g.Key));
    }

    private static bool IsDefaultMana(ManaOrderOptions m) =>
        m.Tries == ManaOrderOptions.DefaultTries && m.TryChance == ManaOrderOptions.DefaultTryChance && m.TrySpacingSeconds == ManaOrderOptions.DefaultTrySpacingSeconds && m.DancePresses == ManaOrderOptions.DefaultDancePresses;

    /// <summary>The options as the run logs them, in invariant culture.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"kills={(Kills == UnlimitedKills ? "unlimited" : Kills.ToString(CultureInfo.InvariantCulture))} seconds={Seconds}{(Minutes > 0 ? $" minutes={Minutes}" : string.Empty)}{(MaxLevel != DefaultMaxLevel ? $" max_level={MaxLevel}" : string.Empty)}{(LiveCap ? " live_cap=yes" : string.Empty)}{(StoreysMin != DefaultStoreysMin ? $" storeys_min={StoreysMin}" : string.Empty)}{(OpenerTries != DefaultOpenerTries ? $" opener_tries={OpenerTries}" : string.Empty)}{(DefendForgetSeconds != DefaultDefendForgetSeconds ? $" defend_forget_s={DefendForgetSeconds}" : string.Empty)}{(DefendHoldSeconds != DefaultDefendHoldSeconds ? $" defend_hold_s={DefendHoldSeconds}" : string.Empty)}{(StaminaFullPercent != DefaultStaminaFullPercent ? $" stamina_full={StaminaFullPercent}" : string.Empty)}{(DrainUnderPercent != DefaultDrainUnderPercent ? $" drain_under={DrainUnderPercent}" : string.Empty)}{(DrainUntilPercent != DefaultDrainUntilPercent ? $" drain_until={DrainUntilPercent}" : string.Empty)}{(IsDefaultMana(Mana) ? string.Empty : " " + Mana)}{(IgnoreLine ? " ignore_line=on" : string.Empty)}{(LifeThrough ? string.Empty : " life_through=off")}{(SurvivalMana ? " survival_mana=on" : string.Empty)}{(Loot ? " loot=on" : string.Empty)}{(LootRadiusMetres != DefaultLootRadiusMetres ? $" loot_radius_m={LootRadiusMetres}" : string.Empty)}{(LootWalkSeconds != DefaultLootWalkSeconds ? $" loot_walk_s={LootWalkSeconds}" : string.Empty)}{(LootAggroMetres != DefaultLootAggroMetres ? $" loot_aggro_m={LootAggroMetres}" : string.Empty)}{(Ground == DefaultGround ? string.Empty : " ground=" + Ground)}{(SpellId == 0 ? string.Empty : $" spell={SpellId}")}{(ArenaHere ? $" arena=here skill={ArenaSkill}" : string.Empty)}{(Travel is null or PlaceTravel ? string.Empty : " travel=journey")}{(LeaveOnFailure ? string.Empty : " leave_on_failure=no")}{(LeaveAtEnd ? string.Empty : " leave_at_end=no")}{(Want.Count == 0 ? string.Empty : " want=" + string.Join(',', Want))}{(WantCarriers.Count == 0 ? string.Empty : " carriers=" + WantCarrier.Format(WantCarriers))}{(Until is null ? string.Empty : " until=" + Until)}{(SpecError is null ? string.Empty : " spec_error=" + SpecError)}");
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

    /// <summary>M6 5 q4 (D57): the fight's <see cref="FightOptions.Until"/> was met (the quest's hunt-until item held, or its chat line seen).</summary>
    public static FightStopReason UntilMet { get; } = new("until-met");

    /// <summary>The scripted death of the gate (<c>--autopilot-test-death</c>, M4 D37: undress and lie down).</summary>
    public static FightStopReason TestDeath { get; } = new("test-death");

    /// <inheritdoc />
    public override string ToString() => Name;
}

/// <summary>
/// M6 14 errand lapse (live ladder57, 2026-11-17: the Atlan wand's hunt-until in the Crater Caves ran past the hour of the buff bot's lines; every boon ran out at 10:40:43 UTC and the race was lost at 10:42:05): a station
/// that steps a fight inside it (the quest station's hunt-until and kill steps) shows that fight, so the autopilot asks it to keep her buff lines as it asks its own fight. Only the fight is shown: the station's other
/// contracts (its waits, its commits) are its own.
/// </summary>
public interface IStationSubFight
{
    /// <summary>The fight the station steps now, or null between its fights.</summary>
    IStoppableFight? SubFight { get; }
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

    /// <summary>
    /// M6 14 recall unblock (m6-loot-hold; live 2026-11-14 17:27:19 UTC: a corpse opened in the fight's last seconds held the Use lane her trip's recall waited on): from now on the fight opens no corpse. A loot in hand stands down
    /// (its walk cut, its open view closed), and the queued and put-off corpses are left for the next ground visit. Asked for a fight that stands in for a travel or recovery state (a keeper's fight-back); a stop for a trip or a
    /// retreat holds it by itself. Idempotent: the first reason is kept. The default does nothing (a fight with no loot).
    /// </summary>
    /// <param name="why">Why, for the log.</param>
    void HoldLoot(string why)
    {
    }

    /// <summary>The reason the loot is held (<see cref="HoldLoot"/>), or null while it is not.</summary>
    string? LootHeld => null;
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
    /// M6 14 (no line she casts lapses inside a round): the line that ran out in this fight and is still down, the longest down first, with its seconds down at <paramref name="clock"/>; null when none. The fight recasts a lapsed line
    /// between its casts at once (the hard floor's rule, F25); a line still down after the hard floor's window could not be recast in the field, and the round's trip comes first (the autopilot's rule).
    /// </summary>
    /// <param name="clock">The session clock.</param>
    (string Line, double DownSeconds)? LapsedDown(double clock) => null;

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

/// <summary>
/// M6 5 q4 (D57): the fight's own end condition (<see cref="FightOptions.Until"/>): <c>item:&lt;wcid&gt;x&lt;n&gt;</c> (the pack holds at least n of the weenie) or <c>chat:&lt;text&gt;</c> (a chat line since the fight began contains the text, case-sensitive).
/// </summary>
/// <param name="ItemWcid">The item's weenie class id; 0 for a chat until.</param>
/// <param name="Count">The count that ends the fight (above 0); 0 for a chat until.</param>
/// <param name="Chat">The chat substring, or null for an item until.</param>
public sealed record FightUntil(uint ItemWcid, int Count, string? Chat)
{
    /// <summary>
    /// M6 14 combine: the quest's no-roll combine chain from <see cref="ItemWcid"/> (<see cref="Quests.QuestCombineChain"/>, the spec's <c>combine=</c>), the item first; empty for none. With a chain the until counts the item's equivalents
    /// (<see cref="Quests.QuestCombineChain.Units"/>: 16 motes as motes + 2 x slivers + 4 x nuggets + 8 x bars) and the fight combines its pairs at a safe moment.
    /// </summary>
    public IReadOnlyList<uint> Chain { get; init; } = [];

    /// <summary>The spec argument's item form prefix.</summary>
    public const string ItemPrefix = "item:";

    /// <summary>The spec argument's chat form prefix.</summary>
    public const string ChatPrefix = "chat:";

    /// <summary>The until the argument's <paramref name="text"/> names, or null with the <paramref name="error"/>.</summary>
    /// <param name="text">The argument: <c>item:&lt;wcid&gt;x&lt;n&gt;</c> or <c>chat:&lt;text to the end&gt;</c>.</param>
    /// <param name="error">Why it is malformed.</param>
    public static FightUntil? Parse(string? text, out string error)
    {
        error = string.Empty;
        if (text is not null && text.StartsWith(ChatPrefix, StringComparison.Ordinal))
        {
            var chat = text[ChatPrefix.Length..];
            if (chat.Length == 0)
            {
                error = "until=chat: names no text";
                return null;
            }

            return new FightUntil(0, 0, chat);
        }

        if (text is not null && text.StartsWith(ItemPrefix, StringComparison.Ordinal))
        {
            var body = text[ItemPrefix.Length..];
            var x = body.IndexOf('x', StringComparison.Ordinal);
            if (x > 0 && uint.TryParse(body.AsSpan(0, x), NumberStyles.None, CultureInfo.InvariantCulture, out var wcid) && wcid > 0
                && int.TryParse(body.AsSpan(x + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0)
            {
                return new FightUntil(wcid, count, null);
            }
        }

        error = $"until='{text}' is neither item:<wcid>x<n> nor chat:<text>";
        return null;
    }

    /// <summary>The argument form: <c>item:42x1</c> or <c>chat:text</c>.</summary>
    public override string ToString() => Chat is not null ? ChatPrefix + Chat : string.Create(CultureInfo.InvariantCulture, $"{ItemPrefix}{ItemWcid}x{Count}");
}

/// <summary>
/// M6 14 want loot (live 2026-11-17 04:05, the Atlan wand's 16 Pyreal Motes: 35 golems killed in 15 minutes, none looted beyond the 15 m loot radius): a creature whose create list can hold a wanted item, and the chance a row gives it.
/// The fact comes from the quest file's hunt-until step (its <c>carriers</c>, each citing the pinned world SQL's <c>weenie_properties_create_list</c> row), passed to the fight as the den-fight spec's <c>carriers=</c>
/// (TODO(m6-create-list-general-read): the client carries no create lists yet; a general read would take them from the creature table). A corpse of such a creature is walked to and opened whatever its distance on the ground
/// (<see cref="FightScenario"/>'s loot step), under the fight's own safety rules.
/// </summary>
/// <remarks>
/// ACE's drop (@95ef426d): <c>Creature_Death.GenerateTreasure</c> (<c>WorldObjects/Creature_Death.cs:646-711</c>) rolls the create list's Contain and non-wield Treasure rows through <c>CreateListSelect</c>
/// (<c>WorldObjects/Creature_Equipment.cs:636-679</c>): a Treasure row with a non-zero shade is one outcome of a set that runs until the shades sum to 1, one roll a set, so the item's shade is its chance a kill (the server
/// property <c>trophy_drop_rate</c>, 1 on this server, scales it through the modifier path, <c>:681-726</c>).
/// </remarks>
/// <param name="Creature">The creature's weenie class id.</param>
/// <param name="Item">The wanted item's weenie class id.</param>
/// <param name="Shade">The create-list row's shade: the item's chance a kill (above 0, at most 1).</param>
/// <param name="ItemName">The wanted item's name (the logs say it).</param>
public sealed record WantCarrier(uint Creature, uint Item, double Shade, string ItemName)
{
    /// <summary>The argument form of one carrier: <c>&lt;creature&gt;:&lt;item&gt;:&lt;shade&gt;:&lt;item name&gt;</c> (the name last, with no comma).</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Creature}:{Item}:{Shade:0.####}:{ItemName}");

    /// <summary>The spec argument of <paramref name="carriers"/>: each one's <see cref="ToString"/>, comma-separated.</summary>
    /// <param name="carriers">The carriers.</param>
    public static string Format(IEnumerable<WantCarrier> carriers) => string.Join(",", carriers.Select(c => c.ToString()));

    /// <summary>The carriers the argument's <paramref name="text"/> names, or null with the <paramref name="error"/>.</summary>
    /// <param name="text">The argument: a comma list of <c>&lt;creature&gt;:&lt;item&gt;:&lt;shade&gt;:&lt;item name&gt;</c>.</param>
    /// <param name="error">Why it is malformed.</param>
    public static IReadOnlyList<WantCarrier>? Parse(string? text, out string error)
    {
        error = string.Empty;
        var list = new List<WantCarrier>();
        foreach (var part in (text ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split(':', 4);
            if (f.Length == 4 && uint.TryParse(f[0], NumberStyles.None, CultureInfo.InvariantCulture, out var creature) && creature > 0
                && uint.TryParse(f[1], NumberStyles.None, CultureInfo.InvariantCulture, out var item) && item > 0
                && double.TryParse(f[2], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var shade) && shade is > 0 and <= 1 && f[3].Trim().Length > 0)
            {
                list.Add(new WantCarrier(creature, item, shade, f[3].Trim()));
                continue;
            }

            error = $"carriers='{text}' is not a comma list of <creature wcid>:<item wcid>:<shade in (0, 1]>:<item name>";
            return null;
        }

        if (list.Count == 0)
        {
            error = "carriers= names no carrier";
            return null;
        }

        return list;
    }
}
