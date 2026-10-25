using System.Globalization;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>Which weapon the hunt fights with (<c>--hunt-weapon</c>).</summary>
public enum HuntWeapon
{
    /// <summary>The Mace the fighter build wields: <c>IActionApi.Attack</c>.</summary>
    Melee,

    /// <summary>The Shortbow and the Arrows: the weapon swap of capture C2, then <c>IActionApi.MissileAttack</c>.</summary>
    Missile,
}

/// <summary>Which hunt ground the hunt goes to (<c>--hunt-ground</c>, M4 3.1 s0).</summary>
public enum HuntGroundChoice
{
    /// <summary>The Low B belt south-west of Holtburg (<see cref="HuntGround.LowB"/>): the default, levels 8 to 20.</summary>
    Belt,

    /// <summary>The Low North Forest Mix west of Holtburg (<see cref="HuntGround.LowNorthForestMix"/>): levels 30 to 40.</summary>
    Forest,
}

/// <summary>
/// The hunt's stages (M4 P5b 3.4, lane J2a): flags, not rewrites. <see cref="All"/> is today's order (<c>--scenario=hunt</c>); a journey runs <c>Swap | Hunt | Restore</c> on the ground and <see cref="Vendor"/> as its own station
/// from Holtburg's lifestone. A skipped stage is logged once and its verdict fields read <c>n/a</c>.
/// </summary>
[Flags]
public enum HuntStages
{
    /// <summary>No stage (the scenario checks the character and ends).</summary>
    None = 0,

    /// <summary>The bind at Holtburg's lifestone (the travel there and the Use); skipped: <c>bound=n/a</c>.</summary>
    Bind = 1,

    /// <summary>The recovery before the first fight (between kills it is the hunt's own); skipped: <c>health_start_pct=n/a</c>.</summary>
    Health = 2,

    /// <summary>The missile weapon swap (the launcher and the ammunition wielded); a melee hunt has none.</summary>
    Swap = 4,

    /// <summary>The ground, the acquire, the fights and the loot; skipped: <c>kills=0</c>, nothing is attacked.</summary>
    Hunt = 8,

    /// <summary>The template's weapon state again after the hunt; skipped: <c>restored=n/a</c>.</summary>
    Restore = 16,

    /// <summary>The travel home to Holtburg after the hunt (and after the shops); skipped: <c>returned=n/a</c>.</summary>
    Home = 32,

    /// <summary>The shops: the sale at Renald's and the purchase at Thelnoth's (when <c>--sell</c> or <c>--buy-kits</c> want them); skipped: <c>sell=n/a buy=n/a</c>.</summary>
    Vendor = 64,

    /// <summary>Every stage in today's order: the default.</summary>
    All = Bind | Health | Swap | Hunt | Restore | Home | Vendor,

    /// <summary>The journey's hunt station on the ground.</summary>
    Ground = Swap | Hunt | Restore,
}

/// <summary>The hunt loop's options (<c>--hunt-*</c>, M4 1.2 s4a), parsed from the command line like <see cref="FightOptions"/>.</summary>
public sealed record HuntOptions
{
    /// <summary>The default number of kills.</summary>
    public const int DefaultKills = 1;

    /// <summary>The largest <c>--hunt-kills</c>.</summary>
    public const int MaxKills = 1000;

    /// <summary>The default time the hunt may take, in seconds.</summary>
    public const double DefaultSeconds = 300;

    /// <summary>How many kills end the hunt.</summary>
    public int Kills { get; init; } = DefaultKills;

    /// <summary>The most session-clock seconds from the first teleport to the grounds to the last kill.</summary>
    public double Seconds { get; init; } = DefaultSeconds;

    /// <summary>The weapon (<c>--hunt-weapon</c>; when not given, <see cref="FighterTemplate.DefaultWeapon"/> through <see cref="ForTemplate"/>, melee for a bare record).</summary>
    public HuntWeapon Weapon { get; init; } = HuntWeapon.Melee;

    /// <summary>The ground the hunt goes to (<c>--hunt-ground belt|forest</c>, M4 3.1 s0; default the belt, so every earlier command line hunts as it did).</summary>
    public HuntGroundChoice Ground { get; init; } = HuntGroundChoice.Belt;

    /// <summary>True when <c>--hunt-weapon</c> was given (otherwise <see cref="ForTemplate"/> uses the template's default weapon).</summary>
    public bool WeaponGiven { get; init; }

    /// <summary>True when <c>--hunt-ground</c> was given (otherwise <see cref="ForTemplate"/> uses the template's default ground).</summary>
    public bool GroundGiven { get; init; }

    /// <summary>
    /// The options with the template's defaults where the command line gave none (M4 P4, D31): the Hunter and the Brawler hunt the belt with the Mace, the Archer the forest with the Shortbow;
    /// <c>--hunt-weapon</c> and <c>--hunt-ground</c> still override. A weapon the template cannot use (a missile hunt with no bow in its kit, a melee hunt with no melee weapon) is an error line
    /// (<c>--hunt-weapon</c>, naming the template's key, never the value given).
    /// </summary>
    public HuntOptions ForTemplate(FighterTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        var o = this with
        {
            Weapon = WeaponGiven ? Weapon : template.DefaultWeapon,
            Ground = GroundGiven ? Ground : template.DefaultGround,
        };
        if (!template.CanHuntWith(o.Weapon))
        {
            o = o with { Errors = [.. o.Errors, $"--hunt-weapon: the {template.Key} fighter ({template.Name}) has no {(o.Weapon == HuntWeapon.Missile ? "bow" : "melee weapon")} in its kit, so it cannot hunt with the {o.Weapon.ToString().ToLowerInvariant()} weapon"] };
        }

        return o;
    }

    /// <summary>The chosen ground's row (<see cref="Ground"/>): what the scenario hunts and the leg reads the terrain heights of.</summary>
    public HuntGroundRow GroundRow => Ground == HuntGroundChoice.Forest ? HuntGround.LowNorthForestMix : HuntGround.LowB;

    /// <summary>True (default) when the hunt opens its kill's corpse, lists it and closes it (<c>--hunt-open-corpse</c>, M4 2.1 s4a); off restores 1.2's behaviour.</summary>
    public bool OpenCorpse { get; init; } = true;

    /// <summary>
    /// The loot rule's options (<c>--loot</c>, <c>--loot-types</c>, <c>--loot-min-value</c>, <c>--loot-max-burden</c>; M4 2.2 s0). <c>--loot off</c> restores 2.1's hunt: the corpse is opened, listed and closed, nothing is taken. They are parsed here so the
    /// headless host and the hunt leg read them with the hunt's own options.
    /// </summary>
    public LootOptions Loot { get; init; } = LootOptions.Default;

    /// <summary>
    /// The recovery rule's options (<c>--heal</c>, <c>--heal-below</c>, <c>--heal-until</c>, <c>--heal-chance</c>; M4 3.1 s3, <see cref="RecoveryRules"/>). <c>--heal off</c> runs no recovery at all (the retreat at 30% still protects). They are
    /// parsed here so the headless host and the hunt leg read them with the hunt's own options.
    /// </summary>
    public RecoveryOptions Recovery { get; init; } = RecoveryOptions.Default;

    /// <summary>The default of <c>--buy-kits</c>: the hunt buys Handy Healing Kits at Thelnoth Cort when it holds fewer than this at the loop's end (D14).</summary>
    public const int DefaultBuyKits = 2;

    /// <summary>The largest <c>--buy-kits</c>.</summary>
    public const int MaxBuyKits = 100;

    /// <summary>True (default) when the hunt sells what the shopkeeper buys at the loop's end (<c>--sell</c>, M4 3.2 s4a, <see cref="SellRules"/>); off restores 3.1's hunt, which keeps its loot.</summary>
    public bool Sell { get; init; } = true;

    /// <summary>The Handy Healing Kits the hunt keeps (<c>--buy-kits</c>, M4 3.2 s4a, D14): with fewer held at the loop's end it buys the missing ones at Thelnoth Cort; 0 is off.</summary>
    public int BuyKits { get; init; } = DefaultBuyKits;

    /// <summary>True (default) when the hunt identifies each looted item once after its pick-up (<c>--appraise-loot</c>, M4 3.2 s4a: <c>appraised=</c>); off gives 0.</summary>
    public bool AppraiseLoot { get; init; } = true;

    /// <summary>Every problem found, one line each, values never repeated.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>The stages the hunt runs (M4 P5b, lane J2a; default <see cref="HuntStages.All"/>, today's order). Not a command-line option: a journey's station sets it.</summary>
    public HuntStages Stages { get; init; } = HuntStages.All;

    /// <summary>
    /// The travel seam (M4 P5b 3.4) every one of the hunt's travels goes through; null (the default) is a fresh <see cref="PlaceTravel"/> per scenario, today's <c>Tools.Place</c>. A journey injects its own
    /// (on foot, a recall, the budgeted teleport).
    /// </summary>
    public IJourneyTravel? Travel { get; init; }

    /// <summary>
    /// The vitals keeper (M4 P5a C2) the hunt asks before an attack (<see cref="VitalsKeeper.MayAttack"/>) and for the in-fight potion (<see cref="VitalsKeeper.TryInFightPotion"/>); null (the default) is today's hunt, whose
    /// recovery runs between kills only.
    /// </summary>
    public VitalsKeeper? Keeper { get; init; }

    /// <summary>Reads the hunt's options out of the parsed arguments.</summary>
    public static HuntOptions FromArguments(IEnumerable<CommandLineArgument> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var o = new HuntOptions();
        var errors = new List<string>();
        foreach (var argument in args)
        {
            var known = errors.Count;
            switch (argument.Name)
            {
                case "--hunt-kills":
                    var kills = OptionValues.Int(argument, errors, o.Kills);
                    if (errors.Count == known && kills is >= 1 and <= MaxKills)
                    {
                        o = o with { Kills = kills };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not 1 to {MaxKills}");
                    }

                    break;
                case "--hunt-seconds":
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
                case "--hunt-weapon":
                    o = o with { WeaponGiven = true, Weapon = OptionValues.Choice(argument, errors, o.Weapon, ("melee", HuntWeapon.Melee), ("missile", HuntWeapon.Missile)) };
                    break;
                case "--hunt-ground":
                    o = o with { GroundGiven = true, Ground = OptionValues.Choice(argument, errors, o.Ground, ("belt", HuntGroundChoice.Belt), ("forest", HuntGroundChoice.Forest)) };
                    break;
                case "--hunt-open-corpse":
                    o = o with { OpenCorpse = OptionValues.Switch(argument, errors, o.OpenCorpse) };
                    break;
                case "--sell":
                    o = o with { Sell = OptionValues.Switch(argument, errors, o.Sell) };
                    break;
                case "--appraise-loot":
                    o = o with { AppraiseLoot = OptionValues.Switch(argument, errors, o.AppraiseLoot) };
                    break;
                case "--buy-kits":
                    var buy = OptionValues.Int(argument, errors, o.BuyKits);
                    if (errors.Count == known && buy is >= 0 and <= MaxBuyKits)
                    {
                        o = o with { BuyKits = buy };
                    }
                    else if (errors.Count == known)
                    {
                        errors.Add($"{argument.Name}: '{argument.Value}' is not 0 to {MaxBuyKits}");
                    }

                    break;
                default:
                    var loot = o.Loot;
                    if (LootOptions.Apply(argument, errors, ref loot))
                    {
                        o = o with { Loot = loot };
                        break;
                    }

                    var recovery = o.Recovery;
                    if (RecoveryOptions.Apply(argument, errors, ref recovery))
                    {
                        o = o with { Recovery = recovery };
                    }

                    break;
            }
        }

        RecoveryOptions.Validate(o.Recovery, errors);
        return o with { Errors = errors };
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"kills={Kills} seconds={Seconds} weapon={Weapon.ToString().ToLowerInvariant()} ground={Ground.ToString().ToLowerInvariant()} {Loot} {Recovery} sell={(Sell ? "on" : "off")} buy_kits={BuyKits} appraise_loot={(AppraiseLoot ? "on" : "off")}{(Stages == HuntStages.All ? string.Empty : " stages=" + StagesText(Stages))}");

    /// <summary>The stages as the log spells them: <c>swap|hunt|restore</c>, <c>none</c>.</summary>
    /// <param name="stages">The stages.</param>
    public static string StagesText(HuntStages stages)
    {
        if (stages == HuntStages.None)
        {
            return "none";
        }

        var names = new List<string>();
        foreach (var stage in (HuntStages[])[HuntStages.Bind, HuntStages.Health, HuntStages.Swap, HuntStages.Hunt, HuntStages.Restore, HuntStages.Home, HuntStages.Vendor])
        {
            if ((stages & stage) != 0)
            {
                names.Add(stage.ToString().ToLowerInvariant());
            }
        }

        return string.Join('|', names);
    }
}
