using System.Globalization;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Vitals;

/// <summary>What <see cref="RechargeRule.Decide"/> does next.</summary>
public enum RechargeMove : byte
{
    /// <summary>The mana and the stamina are at the recharge's targets: she goes on.</summary>
    Done,

    /// <summary>She rests (lying where nothing is awake within the ground's reach, else standing in peace) and decides again.</summary>
    Rest,

    /// <summary>Stamina to Mana Self at the level <see cref="RechargeChoice.Index"/> of <see cref="RechargeFacts.Presses"/>.</summary>
    Press,

    /// <summary>The mana drink the pack's pick names (<see cref="ManaDrinks"/>: a Mana Tincture, Potion or Draught).</summary>
    Drink,
}

/// <summary>What <see cref="RechargeRule.Decide"/> reads: every number live from her character and the ground's rule.</summary>
public readonly record struct RechargeFacts
{
    /// <summary>Her mana now.</summary>
    public uint Mana { get; init; }

    /// <summary>Her maximum mana.</summary>
    public uint ManaMax { get; init; }

    /// <summary>Her stamina now.</summary>
    public uint Stamina { get; init; }

    /// <summary>Her maximum stamina.</summary>
    public uint StaminaMax { get; init; }

    /// <summary>The mana the recharge goes on to (the ground's bar, or the potions' line while drinks are held).</summary>
    public uint ManaTarget { get; init; }

    /// <summary>The stamina the recharge goes on to.</summary>
    public uint StaminaTarget { get; init; }

    /// <summary>The mana that answers an attack (<see cref="RechargeRule.FloorOf"/>): under it the fill goes at once, whatever it costs in seconds.</summary>
    public uint ManaFloor { get; init; }

    /// <summary>Her mana regeneration a second (<see cref="VitalsPolicy.Regen"/>: no stance moves mana).</summary>
    public double ManaRegen { get; init; }

    /// <summary>Her stamina regeneration a second in the posture she rests in (lying 3, standing in peace 1: ACE's <c>GetStanceMod</c>).</summary>
    public double StaminaRegen { get; init; }

    /// <summary>Her Mana Conversion (0: untrained).</summary>
    public uint ManaConversion { get; init; }

    /// <summary>The Stamina to Mana Self levels she may cast now (in the book, paid by the pack, landing at the try chance on the mana held), lowest first; <see cref="FillLevel.Seconds"/> is the ladder's measured table.</summary>
    public ReadOnlyMemory<FillLevel> Presses { get; init; }

    /// <summary>The boost of the drink the pack's pick names when it fits whole in the missing mana (the owner's rule: a drink that would overflow is not drunk); 0: none held, none fits, or the drinks are set aside.</summary>
    public uint DrinkBoost { get; init; }

    /// <summary>The seconds a drink takes (<see cref="RechargeRule.DrinkSeconds"/>).</summary>
    public double DrinkSeconds { get; init; }

    /// <summary>The mana under which a drink may go over the floor (the ground's potion line, <see cref="GroundPullRest.PotionManaPercent"/>); 0: over the floor no drink goes.</summary>
    public uint DrinkLine { get; init; }
}

/// <summary>A choice: the move, the press level's index in <see cref="RechargeFacts.Presses"/> (-1 otherwise), what it gives, the seconds to the targets after it, those of a rest alone, and the reason.</summary>
/// <param name="Move">The move.</param>
/// <param name="Index">The press level's index, or -1.</param>
/// <param name="Give">The mana it gives (a press: its net after its expected cost).</param>
/// <param name="Taken">The stamina a press takes.</param>
/// <param name="Seconds">The seconds to the targets by this choice.</param>
/// <param name="RestSeconds">The seconds to the targets by rest alone.</param>
/// <param name="Why">A fixed reason (the log's key).</param>
public readonly record struct RechargeChoice(RechargeMove Move, int Index, double Give, double Taken, double Seconds, double RestSeconds, string Why);

/// <summary>
/// M6 14 entry recharge (live 2026-11-13 19:36 UTC, the climber Qwinnie at level 51 at the Tusker Barracks' entry front: mana 81 of 362, stamina 210 of 252, the old recharge stood still for a stamina share of 90% before a press, a Tusker
/// Guard hit her for 2 and the whole rung change failed <c>enter-recharge:not-full</c>): the recharge's choice, pure, by the vitals policy's currency (<see cref="VitalsPolicy"/>: seconds). A recharge is a fight's pull with no
/// creature to kill: what it buys is the seconds she stands at the door, so every fill is priced by the seconds to the targets it leaves.
/// <list type="bullet">
/// <item><b>The floor first.</b> Under <see cref="RechargeFacts.ManaFloor"/> (the mana that answers an attack: the fight-back's bolt, the guard's Heal Self and the cheapest press, at their base mana: <see cref="FloorOf"/>, the policy's
/// <see cref="VitalsLines.ManaFloor"/> read for a door) the fill with the fewest seconds a point goes at once: the press that nets the most a second from the stamina she has (<see cref="VitalsPolicy.BestTransfer"/>) or a drink that fits,
/// else the rest: she never waits for a fuller stamina bar with too little mana to answer an attacker.</item>
/// <item><b>Then the fewest seconds to the targets.</b> Each way is priced by the seconds it leaves to both targets, mana and stamina regenerating side by side (<see cref="SecondsTo"/>, ACE's <c>Creature_Vitals.cs:102-236</c>: mana takes no
/// stance, stamina takes the posture's): a rest alone; a press now (its cast seconds and what is left after its net and its take, ACE's transfer <see cref="BuffStatMods.Transfer"/>, its expected cost
/// <see cref="ManaConversionModel.ExpectedCost"/>); a press from a full stamina bar after the rest that fills it (a press gives half the stamina it takes from, so a fuller bar is fewer presses: when this wins she rests, "the stamina
/// first"); a drink under the ground's potion line (<see cref="RechargeFacts.DrinkLine"/>, the drinks the reserve over it as in the fight). A tie goes to the rest, which costs nothing.</item>
/// <item><b>No drain.</b> The fight's policy drains the creatures it fights; a recharge fights none: a drain at a creature that is not on her pulls it (a Drain is a hostile spell), and one that is on her is fought back first
/// (M5 D88, <c>RungChangeStation</c>'s recharge), after which the recharge runs again.</item>
/// </list>
/// </summary>
public static class RechargeRule
{
    /// <summary>The seconds a drink takes (Lifestone's own measure: live, a drink from the magic stance ends 2.5 s after its send, <c>FightScenario.Hunt.cs</c>'s emergency drink note).</summary>
    public const double DrinkSeconds = 2.5;

    /// <summary>The reason of a recharge at its targets.</summary>
    public const string AtTargets = "at the targets";

    /// <summary>
    /// The mana that answers an attack at a door: the fight-back's bolt, the guard's Heal Self and the cheapest press, each at its base mana (the most it costs): the terms of <see cref="VitalsLines.ManaFloor"/> (a bolt, the fight's heal by
    /// drain and a press), the heal being the guard's at a door. A term the book does not hold is 0.
    /// </summary>
    /// <param name="boltBaseMana">The fight-back's bolt's base mana (0: no bolt).</param>
    /// <param name="healBaseMana">The Heal Self the guard casts, its base mana (0: none in the book).</param>
    /// <param name="presses">The presses she may cast.</param>
    public static uint FloorOf(uint boltBaseMana, uint healBaseMana, ReadOnlySpan<FillLevel> presses)
    {
        var cheapest = uint.MaxValue;
        foreach (var p in presses)
        {
            cheapest = Math.Min(cheapest, p.BaseMana);
        }

        return boltBaseMana + healBaseMana + (cheapest == uint.MaxValue ? 0 : cheapest);
    }

    /// <summary>The seconds until both targets are met by regeneration alone from <paramref name="mana"/> and <paramref name="stamina"/>: the longer of the two (they regenerate side by side); infinity for a vital short with no regeneration.</summary>
    /// <param name="f">The facts (the targets and the rates).</param>
    /// <param name="mana">The mana.</param>
    /// <param name="stamina">The stamina.</param>
    public static double SecondsTo(in RechargeFacts f, double mana, double stamina)
    {
        var manaShort = Math.Max(0, f.ManaTarget - mana);
        var staminaShort = Math.Max(0, f.StaminaTarget - stamina);
        var manaSeconds = manaShort <= 0 ? 0 : f.ManaRegen > 0 ? manaShort / f.ManaRegen : double.PositiveInfinity;
        var staminaSeconds = staminaShort <= 0 ? 0 : f.StaminaRegen > 0 ? staminaShort / f.StaminaRegen : double.PositiveInfinity;
        return Math.Max(manaSeconds, staminaSeconds);
    }

    /// <summary>The choice (the class's summary has the order).</summary>
    /// <param name="f">The facts.</param>
    public static RechargeChoice Decide(in RechargeFacts f)
    {
        var rest = SecondsTo(in f, f.Mana, f.Stamina);
        if (f.Mana >= f.ManaTarget && f.Stamina >= f.StaminaTarget)
        {
            return new RechargeChoice(RechargeMove.Done, -1, 0, 0, 0, 0, AtTargets);
        }

        var manaMissing = f.ManaMax > f.Mana ? f.ManaMax - f.Mana : 0;
        var drinkFits = f.DrinkBoost > 0 && f.DrinkBoost <= manaMissing;
        if (f.Mana < f.ManaFloor)
        {
            var now = VitalsPolicy.BestTransfer(f.Presses.Span, f.Stamina, manaMissing, 0, 0, f.ManaConversion, health: false, mana: true);
            var pressPrice = now.Index >= 0 && now.Give > now.Cost ? now.Price : double.PositiveInfinity;
            var drinkPrice = drinkFits ? f.DrinkSeconds / f.DrinkBoost : double.PositiveInfinity;
            if (!double.IsInfinity(pressPrice) && pressPrice <= drinkPrice)
            {
                var after = f.Presses.Span[IndexOf(f.Presses.Span, now.Index)].Seconds + SecondsTo(in f, f.Mana + now.Give - now.Cost, f.Stamina - now.Taken);
                return new RechargeChoice(RechargeMove.Press, now.Index, now.Give - now.Cost, now.Taken, after, rest, "under the floor: the press at once from the stamina she has");
            }

            if (!double.IsInfinity(drinkPrice))
            {
                return new RechargeChoice(RechargeMove.Drink, -1, f.DrinkBoost, 0, f.DrinkSeconds + SecondsTo(in f, f.Mana + f.DrinkBoost, f.Stamina), rest, "under the floor: the drink, fewer seconds a point than any press");
            }

            return new RechargeChoice(RechargeMove.Rest, -1, 0, 0, rest, rest, "under the floor: no press pays and no drink fits: the rest");
        }

        var best = new RechargeChoice(RechargeMove.Rest, -1, 0, 0, rest, rest, "the rest alone is the fewest seconds");
        var pressNow = BestPress(in f, f.Stamina, f.Mana, 0);
        if (pressNow.Index >= 0 && pressNow.Seconds < best.Seconds)
        {
            best = pressNow with { RestSeconds = rest, Why = "the press now is the fewest seconds" };
        }

        if (f.Stamina < f.StaminaMax && f.StaminaRegen > 0)
        {
            var wait = (f.StaminaMax - f.Stamina) / f.StaminaRegen;
            var later = BestPress(in f, f.StaminaMax, Math.Min(f.ManaMax, f.Mana + (wait * f.ManaRegen)), wait);
            if (later.Index >= 0 && later.Seconds < best.Seconds)
            {
                best = new RechargeChoice(RechargeMove.Rest, -1, 0, 0, later.Seconds, rest, "the stamina first: a press from a full bar is the fewest seconds");
            }
        }

        if (drinkFits && f.Mana < f.DrinkLine)
        {
            var drink = f.DrinkSeconds + SecondsTo(in f, f.Mana + f.DrinkBoost, f.Stamina);
            if (drink < best.Seconds)
            {
                best = new RechargeChoice(RechargeMove.Drink, -1, f.DrinkBoost, 0, drink, rest, "the drink is the fewest seconds");
            }
        }

        return best;
    }

    /// <summary>The line of a choice: the move and every number it read.</summary>
    /// <param name="f">The facts.</param>
    /// <param name="c">The choice.</param>
    public static string Line(in RechargeFacts f, in RechargeChoice c) => string.Create(
        CultureInfo.InvariantCulture,
        $"recharge policy: {c.Move.ToString().ToLowerInvariant()}{(c.Index >= 0 ? $" level {f.Presses.Span[IndexOf(f.Presses.Span, c.Index)].Level}" : string.Empty)} ({c.Why}{(c.Move is RechargeMove.Press or RechargeMove.Drink ? string.Create(CultureInfo.InvariantCulture, $"; gives {c.Give:0.#}{(c.Taken > 0 ? string.Create(CultureInfo.InvariantCulture, $" for {c.Taken:0} stamina") : string.Empty)}") : string.Empty)}): mana {f.Mana}/{f.ManaMax} to {f.ManaTarget}, stamina {f.Stamina}/{f.StaminaMax} to {f.StaminaTarget}; floor {f.ManaFloor}; seconds to the targets {Seconds(c.Seconds)} (a rest alone {Seconds(c.RestSeconds)}); regen a second mana {f.ManaRegen:0.###} stamina {f.StaminaRegen:0.###}; presses {f.Presses.Length}; drink {(f.DrinkBoost > 0 ? string.Create(CultureInfo.InvariantCulture, $"{f.DrinkBoost} under {f.DrinkLine}") : "none")}; Mana Conversion {f.ManaConversion}");

    private static string Seconds(double s) => double.IsInfinity(s) ? "never" : s.ToString("0", CultureInfo.InvariantCulture);

    /// <summary>The press with the fewest seconds to the targets from <paramref name="stamina"/> and <paramref name="mana"/>, <paramref name="lead"/> seconds from now; Index -1 when none nets mana.</summary>
    private static RechargeChoice BestPress(in RechargeFacts f, double stamina, double mana, double lead)
    {
        var best = new RechargeChoice(RechargeMove.Press, -1, 0, 0, double.PositiveInfinity, 0, string.Empty);
        var missing = f.ManaMax > mana ? (uint)Math.Floor(f.ManaMax - mana) : 0u;
        foreach (var level in f.Presses.Span)
        {
            var row = level.Row;
            BuffStatMods.Transfer(in row, (uint)Math.Max(0, stamina), missing, out var taken, out var give);
            var net = give - ManaConversionModel.ExpectedCost(level.BaseMana, level.Power, f.ManaConversion);
            if (give == 0 || net <= 0)
            {
                continue;
            }

            var seconds = lead + level.Seconds + SecondsTo(in f, mana + net, stamina - taken);
            if (seconds < best.Seconds)
            {
                best = new RechargeChoice(RechargeMove.Press, level.Index, net, taken, seconds, 0, string.Empty);
            }
        }

        return best;
    }

    private static int IndexOf(ReadOnlySpan<FillLevel> levels, int index)
    {
        for (var i = 0; i < levels.Length; i++)
        {
            if (levels[i].Index == index)
            {
                return i;
            }
        }

        return 0;
    }
}
