using System.Globalization;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.CharacterCreation;

namespace Lifestone.Client.Session.Vitals;

/// <summary>Whose mana order <see cref="ManaOrder.Decide"/> follows.</summary>
public enum ManaProfile : byte
{
    /// <summary>The mage (M5 D27): the dance first, a Mana Potion only when the stamina is too low for a transfer, a rest in peace for stamina only.</summary>
    Mage,

    /// <summary>The fighters (M4 D40, C7): a Mana Potion first, then the transfer (now a dance) at their stamina gate, then a rest; their stamina is an attack resource.</summary>
    Fighter,
}

/// <summary>The next step of the mana order (<see cref="ManaOrder.Decide"/>).</summary>
public enum ManaStep : byte
{
    /// <summary>No need, or the mana covers the need's ceiling: cast the real spell (it cannot be refused for mana).</summary>
    Ready,

    /// <summary>The mana is at or over the try floor and under the ceiling: cast the real spell with the Mana Conversion tries (D15; a 0x0401 inside them is expected).</summary>
    TryReal,

    /// <summary>The stamina-to-mana dance (D27): press <see cref="ManaDecision.DanceIndex"/>'s Stamina to Mana Self until a roll lets it land (<see cref="CastRules.ManaDance"/>).</summary>
    Dance,

    /// <summary>Drink a Mana Potion.</summary>
    Potion,

    /// <summary>Rest in peace for stamina (not mana: mana regenerates the same in every stance) until <see cref="ManaDecision.StaminaTarget"/>; then the dance.</summary>
    RestStamina,

    /// <summary>Nothing can be pressed or drunk usefully: wait (rest) for mana regeneration, the caller's bounded wait as today.</summary>
    Wait,

    /// <summary><c>--cast-mana-tries 0</c>: today's ceiling order, exactly; the caller runs its own pre-K5 mana code.</summary>
    CeilingOrder,

    /// <summary>
    /// The owner's refill (M5 7 F5, D88): cast <see cref="ManaDecision.DanceIndex"/>'s Revitalize Self (the levels given) until the stamina is back to <see cref="ManaDecision.StaminaTarget"/>, each cast paid from the mana
    /// held with the next bolt still paid. Only the mage with <see cref="ManaOrderInput.StaminaFullPercent"/> set asks for it.
    /// </summary>
    Revitalize,

    /// <summary>The refill with no Revitalize Self level the caller can pay for (none known, at the chance or in the pack's scarabs): drink a Stamina Potion (M5 7 F5, D88).</summary>
    StaminaPotion,
}

/// <summary>
/// The next need of a caller: the real spell's ceiling (the most it can cost, <c>GearCasts.ManaCeiling</c>) and its try floor (<see cref="ManaOrder.FloorOf"/>); <see cref="None"/> when nothing is to be cast.
/// The vitals keeper, which knows only a number, passes its station's <c>NextCastMana</c> and <c>NextCastFloor</c>.
/// </summary>
/// <param name="Ceiling">The most the cast can cost (0: no need).</param>
/// <param name="Floor">The least mana a try is sent with (at most <paramref name="Ceiling"/>; equal to it for an untrained caster).</param>
public readonly record struct ManaNeed(uint Ceiling, uint Floor)
{
    /// <summary>No need.</summary>
    public static ManaNeed None => default;

    /// <summary>True when there is no need.</summary>
    public bool IsNone => Ceiling == 0;
}

/// <summary>A Revitalize Self level the book holds and the caller may cast and the pack pays for (M5 7 F5): its id, its base mana (the most it costs) and its power (D88's mana floor).</summary>
/// <param name="SpellId">The spell.</param>
/// <param name="BaseMana">Its base mana (the most it costs).</param>
/// <param name="Power">Its power.</param>
public readonly record struct RevitalizeLevel(uint SpellId, uint BaseMana, uint Power = 0);

/// <summary>A Stamina to Mana Self level the book holds and the caller may cast (its chance reaches the caller's threshold): its id, its base mana, its power and its transfer row (<see cref="BuffStatMods.StaminaToMana"/>).</summary>
/// <param name="SpellId">The spell.</param>
/// <param name="BaseMana">Its base mana (the most it costs).</param>
/// <param name="Power">Its power.</param>
/// <param name="Row">Its transfer row (proportion 0.5, loss down to -0.75 at VII: 0.875 of the current stamina, ACE <c>WorldObject_Magic.cs:720-733</c> @95ef426d).</param>
public readonly record struct DanceLevel(uint SpellId, uint BaseMana, uint Power, TransferRow Row);

/// <summary>What <see cref="ManaOrder.Decide"/> reads.</summary>
public readonly record struct ManaOrderInput
{
    /// <summary>The mana held.</summary>
    public uint Mana { get; init; }

    /// <summary>The mana's maximum.</summary>
    public uint ManaMax { get; init; }

    /// <summary>The stamina held.</summary>
    public uint Stamina { get; init; }

    /// <summary>The stamina's maximum.</summary>
    public uint StaminaMax { get; init; }

    /// <summary>The caster's current Mana Conversion (0: untrained; <see cref="ManaOrder.TryManaConversion"/>).</summary>
    public uint ManaConversion { get; init; }

    /// <summary>The next need (<see cref="ManaNeed.None"/>: none).</summary>
    public ManaNeed Need { get; init; }

    /// <summary>True once the real spell's tries for this need are spent: the order then funds the ceiling, not the floor (the design's §3.4 step 3).</summary>
    public bool TriesSpent { get; init; }

    /// <summary>The Mana Potions held (0 when set aside).</summary>
    public int ManaPotions { get; init; }

    /// <summary>Whose order.</summary>
    public ManaProfile Profile { get; init; }

    /// <summary>The fighters' stamina gate, in percent (D40: <c>--vitals-stamina-until</c>, 80): a fighter transfers only at or over it.</summary>
    public int FighterGatePercent { get; init; }

    /// <summary>The fighters' transfers left this pass (D40's cap, <see cref="VitalsKeeper.FighterTransferCap"/>, less those cast).</summary>
    public int FighterTransfersLeft { get; init; }

    /// <summary>
    /// The mage's stamina share, in percent, below which the order refills it with Revitalize Self (M5 7 F5, D88: <c>--fight-stamina-full</c>, 90; Lifestone's own number). 0: no refill, today's order exactly (the vitals keeper and the
    /// buff cycle never set it).
    /// </summary>
    public int StaminaFullPercent { get; init; }

    /// <summary>
    /// D88's stamina floor (<see cref="ManaOrder.StaminaFloorOf"/>; 0: none): with the refill on, a mage whose stamina is at or over it presses Stamina to Mana whenever the mana is under the target, however the stamina falls short of the
    /// target (no Mana Potion and no rest while there is stamina to transfer; the press is repeated until it lands).
    /// </summary>
    public uint StaminaFloor { get; init; }

    /// <summary>The Stamina Potions held (0 when set aside); the refill's fallback when no Revitalize Self level is payable (<see cref="ManaStep.StaminaPotion"/>).</summary>
    public int StaminaPotions { get; init; }

    /// <summary>M6 7 AD: the dance's level is the one that nets the most (<see cref="ManaOrder.PressLevelOf"/>), not the highest that lands: the fight of a caster with no Mana Conversion, who pays each press's whole base, and the owner's ruling, 2026-11-08: pick by the sum: every press of the ladder's character at any Mana Conversion (<see cref="CharacterPolicy.ManaBySum"/>). The other callers leave it off and press their highest.</summary>
    public bool BestNetPress { get; init; }

    /// <summary>
    /// M6 stamina dance (<c>docs/session.md</c> section 121): the refill casts Revitalize Self only when the best level's whole roll fits the stamina she is missing, or when a press needs the stamina now
    /// (<see cref="ManaOrder.RevitalizeLevelOf(ReadOnlySpan{RevitalizeLevel}, uint, uint, uint, uint, uint, out RefillFit)"/>); false: the highest payable level whenever the stamina is under the share, as before. Off by default:
    /// a caller sets it only together with its hook's whole-roll test, or the two disagree and the bolt is never sent. The fight sets it in <c>FightScenario.ManaDecide</c>, and its hook before the bolt
    /// (<c>FightScenario.BoltNeedsMana</c>) asks the same <see cref="ManaOrder.RevitalizeLevelOf(ReadOnlySpan{RevitalizeLevel}, uint, uint, uint, uint, uint, out RefillFit)"/> with the order's own reserve
    /// (<see cref="ManaOrder.TargetOf"/>), the stamina, its maximum and D88's floor (closes <c>TODO(m6-stamina-dance-refill-hook)</c>).
    /// </summary>
    public bool RevitalizeWholeRoll { get; init; }
}

/// <summary>M6 stamina dance: why <see cref="ManaOrder.RevitalizeLevelOf(ReadOnlySpan{RevitalizeLevel}, uint, uint, uint, uint, uint, out RefillFit)"/> chose its level, or none.</summary>
public enum RefillFit : byte
{
    /// <summary>No level is payable with the reserve in hand.</summary>
    None,

    /// <summary>The best level's top roll fits the stamina she is missing: its whole roll lands.</summary>
    WholeRoll,

    /// <summary>The stamina is under D88's floor (<see cref="ManaOrder.StaminaFloorOf"/>): the next press needs it now, whole roll or not.</summary>
    PressNeedsIt,

    /// <summary>The best level's top roll does not fit what she is missing and no press needs the stamina: the refill waits (no top-up into a nearly full bar).</summary>
    Waits,

    /// <summary>No payable level's top roll fits her bar at all: the highest payable, as before (named).</summary>
    NoneFitsTheBar,
}

/// <summary>The decision (<see cref="ManaOrder.Decide"/>): the step, the dance's level, the targets and a fixed reason (no allocation).</summary>
/// <param name="Step">The next step.</param>
/// <param name="DanceIndex">The level of <see cref="ManaStep.Dance"/> in the dance levels given, or of <see cref="ManaStep.Revitalize"/> in the Revitalize levels given (-1 otherwise).</param>
/// <param name="Target">The mana this step works toward: the floor, or the ceiling once the tries are spent.</param>
/// <param name="StaminaTarget">The stamina <see cref="ManaStep.RestStamina"/> rests to, or <see cref="ManaStep.Revitalize"/> and <see cref="ManaStep.StaminaPotion"/> refill to (0 otherwise).</param>
/// <param name="Reason">Why, in a few words (a literal).</param>
/// <param name="NoNetLevel">M6 7 C (m6-mana-dance-net-yield): the dance level (index in the levels given) whose press the order passed over because it would net no mana at the stamina held (<see cref="ManaOrder.NetYield"/> at most 0), or -1.</param>
/// <param name="NetYield">The net mana of that level's press (the give less its expected cost); 0 when <paramref name="NoNetLevel"/> is -1.</param>
public readonly record struct ManaDecision(ManaStep Step, int DanceIndex, uint Target, uint StaminaTarget, string Reason, int NoNetLevel = -1, double NetYield = 0);

/// <summary>
/// One mana order for every caller (M5 item 1 lane K5; D15, D27, the P5b design's §3.3-§3.4): a pure, allocation-free decision with no I/O. The vitals keeper and the buff cycle's mana loop call it now, the fight
/// (F1) next. The order:
/// <list type="number">
/// <item>No need, or the mana at or over the ceiling: <see cref="ManaStep.Ready"/>.</item>
/// <item><c>--cast-mana-tries 0</c>: <see cref="ManaStep.CeilingOrder"/> (today's order, exactly).</item>
/// <item>At or over the try floor (and the tries not spent): <see cref="ManaStep.TryReal"/>.</item>
/// <item>Under it, the mage: the dance where a known level can land at the mana held and the stamina funds the target (<see cref="Funds"/>); else, the stamina too low, a Mana Potion; with none a rest in peace
/// for stamina to <see cref="StaminaFor"/>; with the stamina enough but no press able to land (no mana at all), the wait. With no Stamina to Mana Self known: a potion, else the wait.</item>
/// <item>Under it, the fighters (D40): a Mana Potion; else the dance at their stamina gate within their cap; else the wait (a rest in peace).</item>
/// </list>
/// M6 7 AD: the dance needs no Mana Conversion (ACE only discounts the cost with it, <c>Creature_Magic.cs:41-44</c> @95ef426d). A mage with none pays each press's whole base, so the level is <see cref="BestNetLevel"/>, and the fight keeps one press in hand (<see cref="KeepPress"/>).
/// The owner's order for the mage (M5 7 F5, D88: in the fight the mana may run down to nearly empty and the stamina is kept near full): with <see cref="ManaOrderInput.StaminaFullPercent"/> set, whenever the mana is at or
/// over the target (the next bolt can be paid: after a transfer landed, and between kills) and the stamina is under that share, a Revitalize Self level the mana pays for with that bolt still paid
/// (<see cref="RevitalizeLevelOf(ReadOnlySpan{RevitalizeLevel}, uint, uint)"/>, the highest; M6 stamina dance: by whole rolls, <see cref="RevitalizeLevelOf(ReadOnlySpan{RevitalizeLevel}, uint, uint, uint, uint, uint, out RefillFit)"/>, when the caller asks), or, with no payable level at all, a Stamina Potion. The transfer still comes first, when the bolt cannot be paid.
/// </summary>
public static class ManaOrder
{
    /// <summary>
    /// The share of the stamina's maximum at or over which F1's pre-emptive transfer fires (D27, "transfer before stamina reaches its maximum"). Lifestone's own: stamina regenerates nothing at its maximum (item 0:
    /// 0.66 a second in magic stance), so a transfer near it converts what would be lost; 0.9 leaves about 40 s of regeneration room at the mage's 275.
    /// </summary>
    public const double PreemptStaminaShare = 0.9;

    /// <summary>
    /// The missing mana at or over which F1's pre-emptive transfer fires. Lifestone's own: the transfer's give is capped at the missing mana and the drain scaled down with it (ACE <c>WorldObject_Magic.cs:737-746</c>
    /// @95ef426d); item 0's landings gave 197 to 277 mana for 109 to 158 stamina, so under 100 missing a press (mean cost 14 at Mana Conversion 311) buys under half a landing.
    /// </summary>
    public const uint PreemptMinMissingMana = 100;

    /// <summary>The Mana Conversion skill's formula, (Focus + Self) / 6 (the SkillTable's; the scripted caster's <c>ManaConversionDivisor</c>).</summary>
    private static readonly SkillTableFormula ManaConversionFormula = new(0, 1, 1, 6, MageTemplate.Focus, MageTemplate.Self);

    /// <summary>
    /// The caster's current Mana Conversion from the read model (skill 16), or 0 when untrained or unknown (the model then charges the base, ACE <c>Creature_Magic.cs:41-51</c> @95ef426d). The weapon's
    /// <c>ManaConversionMod</c> is not read (1.0, the conservative choice, the design's §3.3). Reads enchantments, so it may allocate: callers read it once a need, not once a frame.
    /// </summary>
    /// <param name="q">The player's qualities.</param>
    /// <param name="value">The current skill (0 when untrained).</param>
    /// <returns>True when the skill is trained and read.</returns>
    public static bool TryManaConversion(PlayerQualities q, out uint value)
    {
        ArgumentNullException.ThrowIfNull(q);
        value = 0;
        if (!q.IsKnown || !q.TryGetSkill(MageTemplate.ManaConversion, out var skill) || skill.AdvancementClass < SkillRules.TrainedClass)
        {
            return false;
        }

        if (!SkillRules.TryCurrent(q, MageTemplate.ManaConversion, ManaConversionFormula, SkillRules.TrainedClass, out value))
        {
            value = 0;
            return false;
        }

        return true;
    }

    /// <summary>
    /// The try floor of a spell (D15): the least mana at which a try lands with at least <see cref="ManaOrderOptions.TryChance"/> (<see cref="ManaConversionModel.TryFloor"/>, never under 5), at most the ceiling; the
    /// ceiling itself when the tries are off or the caster is untrained.
    /// </summary>
    /// <param name="baseMana">The spell's base mana as the model takes it (the ceiling's: the base plus the armour lines' surcharge).</param>
    /// <param name="power">The spell's power.</param>
    /// <param name="ceiling">The most the cast can cost.</param>
    /// <param name="manaConversion">The caster's Mana Conversion (0: untrained).</param>
    /// <param name="options">The options.</param>
    /// <param name="ignoresManaConversion">The spell ignores Mana Conversion.</param>
    public static uint FloorOf(uint baseMana, uint power, uint ceiling, uint manaConversion, ManaOrderOptions options, bool ignoresManaConversion = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.On || manaConversion == 0 || ignoresManaConversion || ceiling == 0)
        {
            return ceiling;
        }

        return Math.Min(ceiling, ManaConversionModel.TryFloor(baseMana, power, manaConversion, options.TryChance));
    }

    /// <summary>The need of a spell: its ceiling and its floor (<see cref="FloorOf"/>).</summary>
    public static ManaNeed NeedOf(uint baseMana, uint power, uint ceiling, uint manaConversion, ManaOrderOptions options, bool ignoresManaConversion = false) =>
        new(ceiling, FloorOf(baseMana, power, ceiling, manaConversion, options, ignoresManaConversion));

    /// <summary>What a landed press of <paramref name="row"/> gives from <paramref name="stamina"/> before the cap at the missing mana (<see cref="BuffStatMods.Transfer"/>, ACE <c>HandleCastSpell_Transfer</c>).</summary>
    public static uint Give(in TransferRow row, uint stamina)
    {
        BuffStatMods.Transfer(in row, stamina, uint.MaxValue, out _, out var given);
        return given;
    }

    /// <summary>
    /// M6 7 C (m6-mana-dance-net-yield): the mana one landed press of <paramref name="level"/> nets from <paramref name="stamina"/>: the give (<see cref="Give"/>, ACE <c>HandleCastSpell_Transfer</c>) less the press's expected cost
    /// (<see cref="ManaConversionModel.ExpectedCost"/>: the base for an untrained caster, the mean of the rolled cost with Mana Conversion). Both numbers are read from the spell's row and its base mana, never a constant: a low level at a low
    /// stamina gives about what it costs (Stamina to Mana I: 10 mana for about 9), so a press can lose mana.
    /// </summary>
    /// <remarks>A landing never costs more than the mana held (ACE refuses a dearer roll and charges nothing, <c>Player_Magic.cs:566-585</c> @95ef426d), so the cost is capped at <paramref name="mana"/>.</remarks>
    public static double NetYield(in DanceLevel level, uint stamina, uint manaConversion, uint mana = uint.MaxValue) =>
        Give(level.Row, stamina) - Math.Min(ManaConversionModel.ExpectedCost(level.BaseMana, level.Power, manaConversion), mana);

    /// <summary>
    /// The named rule "stamina enough for a transfer" (Lifestone's own): after a landed press of <paramref name="row"/> from <paramref name="stamina"/>, at its worst roll, the mana reaches <paramref name="target"/>,
    /// or as much as a full stamina would bring when even that is less. The worst roll costs the level's base or the mana held, whichever is less (ACE refuses a dearer roll and charges nothing,
    /// <c>Player_Magic.cs:566-585</c> @95ef426d), so the mana after it is at least <see cref="Kept"/> plus the give, whatever the roll; item 0 measured 1.75 mana a point of stamina drained (158 stamina gave 277).
    /// </summary>
    /// <param name="row">The level's transfer row.</param>
    /// <param name="stamina">The stamina held.</param>
    /// <param name="staminaMax">The stamina's maximum.</param>
    /// <param name="target">The mana to reach.</param>
    /// <param name="mana">The mana held.</param>
    /// <param name="baseMana">The level's base mana (the most a press costs).</param>
    /// <remarks>
    /// Also enough, whatever the target: a give of at least <see cref="WorthwhileGiveBases"/> times the level's base. Lifestone's own: a landing then nets at least the base again over its dearest roll, so pressing now
    /// beats resting (stamina regenerates at a fixed 1.30 a second at rest, item 0, so converting it early loses nothing and leaves room before its maximum).
    /// </remarks>
    public static bool Funds(in TransferRow row, uint stamina, uint staminaMax, uint target, uint mana = 0, uint baseMana = 0)
    {
        var give = Give(in row, stamina);
        var kept = Kept(mana, baseMana);
        return give > 0 && (kept + give >= Math.Min(target, kept + Give(in row, staminaMax)) || (baseMana > 0 && give >= WorthwhileGiveBases * baseMana));
    }

    /// <summary>The give, in multiples of the level's base, that makes a press worth sending whatever the target (<see cref="Funds"/>).</summary>
    public const uint WorthwhileGiveBases = 2;

    /// <summary>The mana a landed press leaves at its worst roll before the give: the mana held less the level's base, or nothing.</summary>
    public static uint Kept(uint mana, uint baseMana) => mana > baseMana ? mana - baseMana : 0;

    /// <summary>The least stamina at which <see cref="Funds"/> holds (at most <paramref name="staminaMax"/>): the target of a rest for stamina. A binary search; <see cref="Funds"/> rises with the stamina.</summary>
    public static uint StaminaFor(in TransferRow row, uint staminaMax, uint target, uint mana = 0, uint baseMana = 0)
    {
        uint low = 0;
        var high = staminaMax;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (Funds(in row, mid, staminaMax, target, mana, baseMana))
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    /// <summary>
    /// The dance's level: the highest of <paramref name="levels"/> (lowest first) whose press lands with at least <paramref name="minChance"/> at the <paramref name="mana"/> held
    /// (<see cref="ManaConversionModel.LandingChance"/>; the options' try chance), since the highest level gives the most a landing; when none reaches it, the level with the best chance above 0 (the higher on a tie),
    /// so the presses are not spent on a level that is nearly always refused (at Mana Conversion 311 and mana 1, VI lands 28% a press and Meditative Trance 12%: item 0); -1 when no press can land.
    /// </summary>
    public static int DanceLevelOf(ReadOnlySpan<DanceLevel> levels, uint mana, uint manaConversion, double minChance = ManaConversionModel.DefaultTryChance)
    {
        var best = -1;
        var bestChance = 0.0;
        for (var i = levels.Length - 1; i >= 0; i--)
        {
            var chance = ManaConversionModel.LandingChance(levels[i].BaseMana, levels[i].Power, manaConversion, mana);
            if (chance >= minChance)
            {
                return i;
            }

            if (chance > bestChance)
            {
                best = i;
                bestChance = chance;
            }
        }

        return best;
    }

    /// <summary>
    /// M6 7 AD: the level of <paramref name="levels"/> whose press nets the most (<see cref="NetYield"/>) at the <paramref name="stamina"/> and <paramref name="mana"/> held, of those that land with at least <paramref name="minChance"/>; the lower on a tie; -1 when none lands. For a
    /// caster with no Mana Conversion (the base is the whole cost), where <see cref="DanceLevelOf"/>'s highest level loses: at 50 stamina Stamina to Mana I nets 12.5, II 5, III -2.5.
    /// </summary>
    /// <param name="levels">The levels, lowest first.</param>
    /// <param name="stamina">The stamina held.</param>
    /// <param name="mana">The mana held.</param>
    /// <param name="manaConversion">The caster's Mana Conversion (0: untrained).</param>
    /// <param name="minChance">The least landing chance.</param>
    /// <param name="net">The net of the level chosen (0 when none).</param>
    public static int BestNetLevel(ReadOnlySpan<DanceLevel> levels, uint stamina, uint mana, uint manaConversion, double minChance, out double net)
    {
        var best = -1;
        net = 0;
        for (var i = 0; i < levels.Length; i++)
        {
            if (ManaConversionModel.LandingChance(levels[i].BaseMana, levels[i].Power, manaConversion, mana) < minChance)
            {
                continue;
            }

            var yield = NetYield(in levels[i], stamina, manaConversion, mana);
            if (best < 0 || yield > net)
            {
                (best, net) = (i, yield);
            }
        }

        return best;
    }

    /// <summary>
    /// The owner's ruling, 2026-11-08 (pick by the sum): the level of a press: <see cref="BestNetLevel"/> (the most net mana at the stamina and the mana held, at the caster's Mana Conversion, of the levels that land at the chance); when none reaches the chance, <see cref="DanceLevelOf"/>'s
    /// fallback (the best chance above 0), so a press still happens at a Mana Conversion that makes every level a roll; -1 when no press can land.
    /// </summary>
    /// <param name="levels">The levels, lowest first.</param>
    /// <param name="stamina">The stamina held.</param>
    /// <param name="mana">The mana held.</param>
    /// <param name="manaConversion">The caster's Mana Conversion (0: untrained).</param>
    /// <param name="minChance">The least landing chance.</param>
    /// <param name="net">The net of the level chosen (0 when none).</param>
    public static int PressLevelOf(ReadOnlySpan<DanceLevel> levels, uint stamina, uint mana, uint manaConversion, double minChance, out double net)
    {
        var level = BestNetLevel(levels, stamina, mana, manaConversion, minChance, out net);
        if (level >= 0)
        {
            return level;
        }

        level = DanceLevelOf(levels, mana, manaConversion, minChance);
        net = level >= 0 ? NetYield(in levels[level], stamina, manaConversion, mana) : 0;
        return level;
    }

    /// <summary>The share of the bolt's mana a press must net to be sent before a bolt that can still be paid (<see cref="KeepPress"/>; Lifestone's own: under half a bolt the press spends its seconds for nothing a fight would notice).</summary>
    public const double KeepPressBoltShare = 0.5;

    /// <summary>
    /// M6 7 AD: the press before the bolt that would leave none. With no Mana Conversion a press costs its whole base, so a bolt that takes the mana under the base ends the dance: r3d's 8 mana could pay neither (Flame Bolt II costs 10, and so does the press). True when the bolt can be
    /// paid but the press after it could not, and the press nets at least <see cref="KeepPressBoltShare"/> of a bolt; so she presses at 10 to 19 mana with Flame Bolt II, once a refill: a second press from half the stamina nets about 1 and is not sent.
    /// </summary>
    /// <param name="mana">The mana held.</param>
    /// <param name="boltMana">The bolt's mana.</param>
    /// <param name="level">The level to press (<see cref="BestNetLevel"/>).</param>
    /// <param name="net">Its net.</param>
    public static bool KeepPress(uint mana, uint boltMana, in DanceLevel level, double net) =>
        mana >= level.BaseMana && mana < boltMana + (ulong)level.BaseMana && net > 0 && net >= KeepPressBoltShare * boltMana;

    /// <summary>
    /// F1's pre-emptive transfer test (D27: "transfer before stamina reaches its maximum"): the stamina at or over <paramref name="share"/> of its maximum and the missing mana at or over <paramref name="minMissing"/>.
    /// Off for the keeper (it never asks).
    /// </summary>
    public static bool PreemptTransfer(uint stamina, uint staminaMax, uint mana, uint manaMax, double share = PreemptStaminaShare, uint minMissing = PreemptMinMissingMana) =>
        staminaMax > 0 && stamina >= share * staminaMax && manaMax > mana && manaMax - mana >= minMissing;

    /// <summary>
    /// D88's mana floor (M5 7 F5, the owner's rule: the mana floor leaves at least one Stamina to Mana cast plus one Revitalize Self affordable): the least mana at which a press of each lands with at least
    /// <paramref name="chance"/> (<see cref="ManaConversionModel.TryFloor"/>, each never under 5), added. The bolt's own try floor comes on top of it (the fight adds them).
    /// </summary>
    /// <param name="transferBase">The Stamina to Mana Self level's base mana.</param>
    /// <param name="transferPower">Its power.</param>
    /// <param name="revitalizeBase">The Revitalize Self level's base mana.</param>
    /// <param name="revitalizePower">Its power.</param>
    /// <param name="manaConversion">The caster's Mana Conversion (0: untrained, so the bases themselves).</param>
    /// <param name="chance">The landing chance a press must have.</param>
    public static uint ManaFloorOf(uint transferBase, uint transferPower, uint revitalizeBase, uint revitalizePower, uint manaConversion, double chance = ManaConversionModel.DefaultTryChance) =>
        ManaConversionModel.TryFloor(transferBase, transferPower, manaConversion, chance) + ManaConversionModel.TryFloor(revitalizeBase, revitalizePower, manaConversion, chance);

    /// <summary>
    /// D88's stamina floor (M5 7 F5: stamina is never allowed to fall so low that the transfer cannot give mana): the least stamina at which a landed press of <paramref name="row"/> gives at least the level's base plus the
    /// Revitalize Self level's base, so one landing pays itself and the refill after it. A binary search; the give rises with the stamina; at most <paramref name="staminaMax"/> (the maximum itself when even that falls short).
    /// </summary>
    /// <param name="row">The transfer level's row.</param>
    /// <param name="staminaMax">The stamina's maximum.</param>
    /// <param name="transferBase">The transfer level's base mana.</param>
    /// <param name="revitalizeBase">The Revitalize Self level's base mana.</param>
    public static uint StaminaFloorOf(in TransferRow row, uint staminaMax, uint transferBase, uint revitalizeBase)
    {
        var want = transferBase + (ulong)revitalizeBase;
        uint low = 0;
        var high = staminaMax;
        while (low < high)
        {
            var mid = low + ((high - low) / 2);
            if (Give(in row, mid) >= want)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    /// <summary>True when <paramref name="stamina"/> is under <paramref name="percent"/> of <paramref name="staminaMax"/> (false with no maximum known or no percent).</summary>
    public static bool StaminaUnder(uint stamina, uint staminaMax, int percent) => staminaMax > 0 && percent > 0 && 100.0 * stamina < (double)percent * staminaMax;

    /// <summary>The stamina a refill goes to: <paramref name="percent"/> of <paramref name="staminaMax"/>, rounded up.</summary>
    public static uint FullStamina(uint staminaMax, int percent) => (uint)Math.Ceiling((double)percent * staminaMax / 100.0);

    /// <summary>
    /// The refill's level (M5 7 F5): the highest of <paramref name="levels"/> (lowest first) whose base mana the <paramref name="mana"/> pays with <paramref name="reserve"/> (the next bolt's) still in hand, so each cast
    /// is paid from the mana the transfer gave and the bolt after it is not starved; -1 when none.
    /// </summary>
    public static int RevitalizeLevelOf(ReadOnlySpan<RevitalizeLevel> levels, uint mana, uint reserve)
    {
        for (var i = levels.Length - 1; i >= 0; i--)
        {
            if (mana >= levels[i].BaseMana + (ulong)reserve)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// M6 stamina dance (<c>docs/session.md</c> section 121; run 9, <c>cache/m6/sprint/run9/step3-levelup.log</c> 2026-11-20 19:51 to 2026-11-21 04:58 UTC: 101 of the fight's 204 Revitalize Self VI were cast from 80 to 90% of her stamina
    /// and restored 51.0 each, of a roll of 80 to 160, <see cref="BuffStatMods.Revitalize"/>; ACE cuts a boost at the vital's maximum, <c>HandleCastSpell_Boost</c>, <c>WorldObject_Magic.cs</c> 474 @95ef426d): the refill's level by whole
    /// rolls. The best level is the highest of <paramref name="levels"/> (lowest first) that the <paramref name="mana"/> pays with <paramref name="reserve"/> in hand and whose top roll (its boost row's <see cref="BoostRow.Max"/>) fits her
    /// bar at all; it is cast when that top roll fits the stamina she is missing (<see cref="RefillFit.WholeRoll"/>), or when the stamina is under <paramref name="staminaFloor"/> (D88's floor, <see cref="StaminaFloorOf"/>: the next press
    /// needs it now, <see cref="RefillFit.PressNeedsIt"/>); else it waits (-1, <see cref="RefillFit.Waits"/>). A lower level that fits sooner is not taken: it restores fewer points a second and a mana point (Revitalize Self I's roll is
    /// 15 to 35 for 15 base mana, VI's 80 to 160 for 70). When no payable level's top roll fits her bar, or a level has no boost row, the highest payable goes as before (<see cref="RefillFit.NoneFitsTheBar"/>). Allocation-free.
    /// </summary>
    /// <param name="levels">The levels, lowest first.</param>
    /// <param name="mana">The mana held.</param>
    /// <param name="reserve">The mana kept in hand after it (the next bolt's).</param>
    /// <param name="stamina">The stamina held.</param>
    /// <param name="staminaMax">The stamina's maximum.</param>
    /// <param name="staminaFloor">D88's stamina floor (0: none).</param>
    /// <param name="fit">Why.</param>
    public static int RevitalizeLevelOf(ReadOnlySpan<RevitalizeLevel> levels, uint mana, uint reserve, uint stamina, uint staminaMax, uint staminaFloor, out RefillFit fit)
    {
        var highest = RevitalizeLevelOf(levels, mana, reserve);
        if (highest < 0)
        {
            fit = RefillFit.None;
            return -1;
        }

        var best = -1;
        var top = 0;
        for (var i = highest; i >= 0; i--)
        {
            if (mana < levels[i].BaseMana + (ulong)reserve)
            {
                continue;
            }

            if (BuffStatMods.BoostOf(levels[i].SpellId) is not { } row)
            {
                fit = RefillFit.NoneFitsTheBar;
                return highest;
            }

            if (row.Max <= staminaMax)
            {
                (best, top) = (i, row.Max);
                break;
            }
        }

        if (best < 0)
        {
            fit = RefillFit.NoneFitsTheBar;
            return highest;
        }

        var missing = staminaMax > stamina ? staminaMax - stamina : 0;
        if (top <= missing)
        {
            fit = RefillFit.WholeRoll;
            return best;
        }

        if (staminaFloor > 0 && stamina < staminaFloor)
        {
            fit = RefillFit.PressNeedsIt;
            return best;
        }

        fit = RefillFit.Waits;
        return -1;
    }

    /// <summary>
    /// The mana <see cref="Decide"/> works toward for <paramref name="need"/>: the try floor, or the ceiling once the tries are spent or with no floor. The refill (D88) keeps it in hand. M6 stamina dance: a hook that asks
    /// whether the order will refill (the fight's before the bolt) asks <see cref="RevitalizeLevelOf(ReadOnlySpan{RevitalizeLevel}, uint, uint, uint, uint, uint, out RefillFit)"/> with this reserve, because that test is not
    /// monotone in its reserve: a larger one can leave only a low level whose top roll fits, while the order's own finds a higher level whose roll does not, and waits.
    /// </summary>
    /// <param name="need">The need.</param>
    /// <param name="triesSpent">True once the real spell's tries are spent.</param>
    public static uint TargetOf(in ManaNeed need, bool triesSpent) => triesSpent ? need.Ceiling : Math.Min(need.Floor == 0 ? need.Ceiling : need.Floor, need.Ceiling);

    /// <summary>The refill (D88) when the stamina is under the share: the payable Revitalize Self, else (no level payable at all) a Stamina Potion; false when neither (the order goes on as without it).</summary>
    private static bool TryRefill(in ManaOrderInput input, ReadOnlySpan<RevitalizeLevel> levels, uint reserve, out ManaDecision decision)
    {
        var to = FullStamina(input.StaminaMax, input.StaminaFullPercent);
        if (!levels.IsEmpty)
        {
            var fit = RefillFit.NoneFitsTheBar;
            var at = input.RevitalizeWholeRoll ? RevitalizeLevelOf(levels, input.Mana, reserve, input.Stamina, input.StaminaMax, input.StaminaFloor, out fit) : RevitalizeLevelOf(levels, input.Mana, reserve);
            if (at >= 0)
            {
                decision = new(ManaStep.Revitalize, at, reserve, to, !input.RevitalizeWholeRoll ? "the stamina is under the share: Revitalize Self"
                    : fit switch
                    {
                        RefillFit.WholeRoll => "the stamina is under the share and the best level's whole roll fits: Revitalize Self",
                        RefillFit.PressNeedsIt => "the stamina is under D88's floor: the next press needs it now: Revitalize Self",
                        _ => "the stamina is under the share and no payable level's roll fits her bar whole: Revitalize Self, the highest payable",
                    });
                return true;
            }

            decision = default;
            return false; // a level is payable but the mana is short of it with the bolt in hand (the bolt first), or (M6 stamina dance) the best level's whole roll does not fit yet
        }

        if (input.StaminaPotions > 0)
        {
            decision = new(ManaStep.StaminaPotion, -1, reserve, to, "the stamina is under the share and no Revitalize Self is payable: a Stamina Potion");
            return true;
        }

        decision = default;
        return false;
    }

    /// <summary>The next step of the mana order (see the class remarks). Allocation-free.</summary>
    /// <param name="input">The vitals, the need, the skill, the potions and the profile.</param>
    /// <param name="levels">The Stamina to Mana Self levels the book holds and the caller may cast, lowest first (empty: none).</param>
    /// <param name="options">The options.</param>
    /// <param name="revitalize">The Revitalize Self levels the caller may cast and the pack pays for, lowest first (empty: none; the refill needs <see cref="ManaOrderInput.StaminaFullPercent"/> too).</param>
    public static ManaDecision Decide(in ManaOrderInput input, ReadOnlySpan<DanceLevel> levels, ManaOrderOptions options, ReadOnlySpan<RevitalizeLevel> revitalize = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var need = input.Need;
        var target = TargetOf(in need, input.TriesSpent);
        var refill = input.Profile == ManaProfile.Mage && input.StaminaFullPercent > 0 && options.On && !need.IsNone && StaminaUnder(input.Stamina, input.StaminaMax, input.StaminaFullPercent);
        if (need.IsNone || input.Mana >= need.Ceiling)
        {
            return refill && TryRefill(in input, revitalize, target, out var between)
                ? between
                : new(ManaStep.Ready, -1, need.Ceiling, 0, "the mana covers the ceiling");
        }

        if (!options.On)
        {
            return new(ManaStep.CeilingOrder, -1, need.Ceiling, 0, "--cast-mana-tries 0: today's ceiling order");
        }

        if (input.Mana >= target)
        {
            return refill && TryRefill(in input, revitalize, target, out var after)
                ? after
                : new(ManaStep.TryReal, -1, target, 0, "at or over the try floor: the real spell with the tries");
        }

        var dance = DanceLevelOf(levels, input.Mana, input.ManaConversion, options.TryChance);
        var noNet = -1;
        var net = 0.0;
        if (input.Profile == ManaProfile.Fighter)
        {
            if (input.ManaPotions > 0)
            {
                return new(ManaStep.Potion, -1, target, 0, "the fighters' order: a Mana Potion first");
            }

            if (dance >= 0 && (net = NetYield(levels[dance], input.Stamina, input.ManaConversion, input.Mana)) <= 0)
            {
                noNet = dance; // M6 7 C: a press that nets no mana is not sent
                dance = -1;
            }

            var gate = input.StaminaMax > 0 && 100.0 * input.Stamina / input.StaminaMax >= input.FighterGatePercent && input.FighterTransfersLeft > 0;
            if (dance >= 0 && gate && Give(levels[dance].Row, input.Stamina) > 0)
            {
                return new(ManaStep.Dance, dance, target, 0, "the fighters' order: the dance at the stamina gate");
            }

            return new(ManaStep.Wait, -1, target, 0, "the fighters' order: no potion, no transfer at the gate: a rest in peace", noNet, net);
        }

        if (levels.IsEmpty)
        {
            return input.ManaPotions > 0
                ? new(ManaStep.Potion, -1, target, 0, "no Stamina to Mana Self known: a Mana Potion")
                : new(ManaStep.Wait, -1, target, 0, "no Stamina to Mana Self known and no potion: the wait");
        }

        if (dance >= 0 && input.BestNetPress)
        {
            dance = PressLevelOf(levels, input.Stamina, input.Mana, input.ManaConversion, options.TryChance, out _); // M6 7 AD, the owner's ruling (pick by the sum): the level that nets the most at her own Mana Conversion, not the highest
        }

        if (dance >= 0 && input.StaminaFullPercent > 0 && input.StaminaFloor > 0 && input.Stamina >= input.StaminaFloor)
        {
            // M5 7 F5 (D88): uptime is the goal. With stamina there to transfer (at or over its floor) the mage presses until a roll lands, however far the give falls short of the target: no Mana Potion, no rest.
            return new(ManaStep.Dance, dance, target, 0, "under the floor with stamina to transfer: the dance, pressed until it lands");
        }

        // M6 7 C (m6-mana-dance-net-yield): the D88 branch above holds a stamina whose give already covers a press and a Revitalize (StaminaFloorOf); below it a press that would net no mana is not sent: the order rests (or drinks).
        if (dance >= 0 && (net = NetYield(levels[dance], input.Stamina, input.ManaConversion, input.Mana)) <= 0)
        {
            noNet = dance;
            dance = -1;
        }

        var level = levels[dance >= 0 ? dance : levels.Length - 1];
        var row = level.Row;
        if (Funds(in row, input.Stamina, input.StaminaMax, target, input.Mana, level.BaseMana))
        {
            if (dance >= 0)
            {
                return new(ManaStep.Dance, dance, target, 0, "under the floor: the dance");
            }

            // M6 8 (live 2026-11-11 18:52 UTC, the climb's third death): mana 0 with five tuskers on her and 83 Mana Potions in the pack: the stamina "funded" the target, no press could land at no mana, and the order waited
            // until she died. With a potion held the wait is never the answer: she cannot cast, so the potion is drunk (the owner's rule: no drink in a fight while she can go on casting; here she cannot).
            // The ladder's character only (BestNetPress): the den's mage keeps M5's wait, where a level 80's regeneration pays a press in a second or two.
            if (input.ManaPotions > 0 && input.BestNetPress)
            {
                return new(ManaStep.Potion, -1, target, 0, noNet >= 0 ? "the press would net no mana at this stamina: a Mana Potion" : "no press can land at the mana held: a Mana Potion", noNet, net);
            }

            return new(ManaStep.Wait, -1, target, 0, noNet >= 0 ? "the press would net no mana at this stamina: the wait" : "no press can land at the mana held: the wait", noNet, net);
        }

        if (input.ManaPotions > 0)
        {
            return new(ManaStep.Potion, -1, target, 0, "the stamina is too low for a transfer: a Mana Potion", noNet, net);
        }

        return new(ManaStep.RestStamina, -1, target, StaminaFor(in row, input.StaminaMax, target, input.Mana, level.BaseMana), "the stamina is too low for a transfer and no potion: a rest in peace for stamina", noNet, net);
    }
}
