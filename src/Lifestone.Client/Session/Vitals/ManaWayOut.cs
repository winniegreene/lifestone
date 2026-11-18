using System.Globalization;

namespace Lifestone.Client.Session.Vitals;

/// <summary>What a spend is, for <see cref="ManaWayOut.Spend"/>.</summary>
public enum WayOutSpendKind
{
    /// <summary>A bolt (and with it what the fight's picker would cast in its place).</summary>
    Bolt,

    /// <summary>A buff line's recast between the fight's casts.</summary>
    Buff,

    /// <summary>The emergency's heal: a drain or Heal Self.</summary>
    Drain,
}

/// <summary>The mana the way out holds back: <see cref="Line"/> is <see cref="RecallCost"/> plus <see cref="DrainCost"/> (<see cref="ManaWayOut.LineOf"/>).</summary>
/// <param name="Line">The line: at or under it, with attackers on her, she leaves.</param>
/// <param name="RecallCost">What Lifestone Recall needs now: its cost, or the station's own share of the mana when that is more (<c>AutopilotScenario.UrgentRecallRead</c>'s number).</param>
/// <param name="DrainCost">What one Drain Health Other I costs, the emergency heal that keeps her up while the recall is cast.</param>
public readonly record struct WayOutLine(uint Line, uint RecallCost, uint DrainCost);

/// <summary>What <see cref="ManaWayOut.Decide"/> reads.</summary>
/// <param name="Ready">The book holds Lifestone Recall, its components are held and the tie is not known false (false: under its level she keeps the old behaviour).</param>
/// <param name="Mana">The mana held.</param>
/// <param name="Line">The line.</param>
/// <param name="Attackers">A live creature attacked her in the last few seconds.</param>
/// <param name="KillPending">A kill that clears the last attacker is on its way or is the next bolt (the idea of <see cref="RetreatInput.Finishing"/>).</param>
/// <param name="Press">A Stamina to Mana press is in flight, or one can be sent that nets its worth and leaves the recall paid.</param>
/// <param name="Refused">The fight refused its next spend for the line (<see cref="ManaWayOut.Spend"/>): the mana is over the line by less than the spend.</param>
/// <param name="Potion">M6 8: a Mana Potion is being drunk, or one is held and in use: the fight drinks in the bolt's place (<c>FightScenario.WayOut.cs</c>), which lifts the mana without touching what the recall needs.</param>
/// <param name="Losing">M6 8: the hits of the last seconds would kill her within <see cref="ManaWayOut.LosingSeconds"/> at the health she has: a potion holds no leaving then.</param>
/// <param name="Dance">M6 14 fire dance: a round of the stamina dance under fire (Revitalize Self, then the press) is under way or can be sent with the recall paid (<c>FireDance</c>): it holds the leaving as a press does, but only in a
/// fight she is not losing.</param>
public readonly record struct WayOutInput(bool Ready, uint Mana, WayOutLine Line, bool Attackers, bool KillPending, bool Press, bool Refused, bool Potion = false, bool Losing = false, bool Dance = false);

/// <summary>What <see cref="ManaWayOut.Spend"/> reads.</summary>
/// <param name="Kind">What the mana would go on.</param>
/// <param name="Mana">The mana held.</param>
/// <param name="Cost">What it costs at most.</param>
/// <param name="Line">The line.</param>
/// <param name="Attackers">Attackers are on her (for a buff: or within a pull).</param>
/// <param name="KillOfLast">The bolt is expected to kill the last attacker.</param>
/// <param name="NextHitKills">The biggest hit seen takes all the health left (<see cref="IncomingRead.NextHitKills"/>).</param>
public readonly record struct WayOutSpend(WayOutSpendKind Kind, uint Mana, uint Cost, WayOutLine Line, bool Attackers, bool KillOfLast = false, bool NextHitKills = false);

/// <summary>A yes or a no from <see cref="ManaWayOut"/>, with its fixed token.</summary>
/// <param name="Yes">True to leave (<see cref="ManaWayOut.Decide"/>) or to spend (<see cref="ManaWayOut.Spend"/>).</param>
/// <param name="Why">The arm's token; never built per call.</param>
public readonly record struct WayOutAnswer(bool Yes, string Why);

/// <summary>
/// M6 7 BE (gate A, level 22, the Lodge: mana 5 of 196 with tuskers on her, no spell payable, 38 seconds from 68 health to 0 without a cast; she knew Lifestone Recall and could no longer pay it): she keeps the way out. The line is the book's and the
/// live numbers', never a constant: what Lifestone Recall needs plus one Drain Health Other I. Pure and allocation-free.
/// <para><b>The order of the spending</b> (<see cref="Spend"/>), each arm before the next:</para>
/// <list type="number">
/// <item>No attackers: the rule has no opinion. Outside a fight it never runs.</item>
/// <item>The bolt that is expected to kill the last attacker goes, whatever it leaves: a dead attacker ends what the mana was held back from.</item>
/// <item>A spend that leaves the mana at or over the line goes.</item>
/// <item>A buff under that is refused, always: a buff saves nothing now (the buff passes of 04:59:27 took 176 mana to 8 with a tusker walking up).</item>
/// <item>Under what the recall needs the way out is already unpaid: a bolt or a heal goes, the old behaviour, and the rule says so (<c>recall-gone</c>).</item>
/// <item>The emergency's heal may spend into the line while the recall stays paid, or when the next hit kills: a heal lands now and a recall that comes too late saves nothing.</item>
/// <item>Anything else is refused: she leaves instead (<see cref="Decide"/>).</item>
/// </list>
/// <para><b>The leaving</b> (<see cref="Decide"/>): the recall ready, attackers on her, the mana at or under the line or the next spend refused, the mana still paying the recall, and neither a kill that clears the last attacker nor a press on its way.
/// A press holds because the mana is what she is short of and a press that nets its worth lifts the mana for less than it costs; it never spends what the recall needs (<c>FightScenario.WayOut.cs</c>).
/// M6 8 (live 2026-11-11, the Lodge: three retreats for mana in twenty minutes with 88, 58 and 28 Mana Potions in the pack, each a recall, a whole buff pass and the way back): a Mana Potion held holds the leaving as a press does. It
/// costs no mana, so the recall stays paid while she drinks; with none held, or the potions set aside, she leaves as before.</para>
/// <para>M6 8 (live 2026-11-11 18:52 UTC, the Cave, the climb's third death: the bot's buffs had lapsed, five tuskers hit her for 10 a second, the potions held the leaving at mana 52, the emergency's drains spent the recall in six
/// seconds and she died at mana 0): the potions hold the leaving only in a fight she is not losing. Losing is the hits of the last seconds killing her within <see cref="LosingSeconds"/> at the health she has
/// (<see cref="IsLosing"/>): then the recall is the way out and it is taken while the mana pays it.</para>
/// <para>M6 14 fire dance (the owner, 2026-11-15: at her level the stamina dance replaces the potions; live ladder23 02:51:52 UTC: she recalled at mana 151 of 394 with the line at 120, health 185 of 204, hit about 2 a blow, and the
/// dance covering her): a round of the stamina dance under fire holds the leaving as a press does, Revitalize Self and its press together, while she is not losing. The round never spends the reserve: a Revitalize goes only when the mana
/// over the line pays its expected cost and its most leaves the recall paid, a press only at a level whose most leaves the recall paid.</para>
/// </summary>
public static class ManaWayOut
{
    /// <summary>The seconds of the incoming hits her health must outlast for the potions to hold the leaving: a recall's cast and a potion or two.</summary>
    /// <remarks>Five, not the ten of its first day (the owner, 2026-11-11: with armour on and the bot's buffs she should never need to flee unless a mistake was made): the recall's own cast and one hit more. Longer, and she left fights she would have won.</remarks>
    public const double LosingSeconds = 5;

    /// <summary>True when the incoming hits would kill her within <see cref="LosingSeconds"/> with no heal.</summary>
    /// <param name="health">Her health.</param>
    /// <param name="incomingPerSecond">The hits of the last seconds, a second.</param>
    public static bool IsLosing(uint health, double incomingPerSecond) => incomingPerSecond > 0 && incomingPerSecond * LosingSeconds >= health;

    /// <summary>The token of a leaving: the retreat's name for a retreat for mana, on every line that says why.</summary>
    public const string Leave = "mana-line";

    /// <summary>The token of a heal refused for the recall.</summary>
    public const string DrainRefused = "drain-refused";

    private const string NoRecall = "no-recall";
    private const string NoAttackers = "no-attackers";
    private const string OverTheLine = "over-the-line";
    private const string RecallGone = "recall-gone";
    private const string HoldKill = "hold:kill-pending";
    private const string HoldPress = "hold:press";
    private const string HoldPotion = "hold:potion";
    private const string HoldDance = "hold:dance";
    private const string KillOfLast = "kill-of-last-attacker";
    private const string BuffRefused = "buff-refused";
    private const string DrainPaid = "drain-recall-paid";
    private const string DrainNextHit = "drain-next-hit-kills";
    private const string BoltRefused = "bolt-refused";

    /// <summary>True for an answer not worth a line of the log: no attackers, or the spend leaves the mana over the line.</summary>
    /// <param name="answer">The answer.</param>
    public static bool IsPlain(in WayOutAnswer answer) => ReferenceEquals(answer.Why, NoAttackers) || ReferenceEquals(answer.Why, OverTheLine);

    /// <summary>The line from the two costs.</summary>
    /// <param name="recallCost">What Lifestone Recall needs now.</param>
    /// <param name="drainCost">What one Drain Health Other I costs.</param>
    public static WayOutLine LineOf(uint recallCost, uint drainCost) => new(recallCost + drainCost, recallCost, drainCost);

    /// <summary>Whether she leaves for home now, by the fight's stop and the retreat's way home.</summary>
    /// <param name="i">The inputs.</param>
    public static WayOutAnswer Decide(in WayOutInput i)
    {
        if (!i.Ready)
        {
            return new(false, NoRecall);
        }

        if (!i.Attackers)
        {
            return new(false, NoAttackers);
        }

        if (i.Mana > i.Line.Line && !i.Refused)
        {
            return new(false, OverTheLine);
        }

        if (i.Mana < i.Line.RecallCost)
        {
            return new(false, RecallGone);
        }

        if (i.KillPending)
        {
            return new(false, HoldKill);
        }

        if (i.Press)
        {
            return new(false, HoldPress);
        }

        if (i.Dance && !i.Losing)
        {
            return new(false, HoldDance); // M6 14 fire dance: a Revitalize-and-press round holds as a press does, in a fight she is not losing
        }

        return i.Potion && !i.Losing ? new(false, HoldPotion) : new(true, Leave);
    }

    /// <summary>
    /// M6 8 (the owner's rule of 2026-11-11: she does not drink in a fight while she can go on casting): the bolt's answer while Mana Potions are in use. The potions are the way out's reserve then (two of them pay a recall), so
    /// the line holds back one emergency drain and no more: the bolt goes while the mana pays it and that drain; under that she cannot go on casting, and the potion is drunk.
    /// No longer the fight's rule (the climb's fourth death, 2026-11-11 19:38 UTC: at a mana of 4 to 36 she could pay no heal and no recall): the fight drinks whenever the bolt would take the mana under the line
    /// (<c>FightScenario.WayOut.cs</c>). Kept as the sum the owner's rule named, for the record and its test.
    /// </summary>
    /// <param name="mana">The mana held.</param>
    /// <param name="cost">The bolt's mana.</param>
    /// <param name="line">The line.</param>
    public static bool BoltOnPotions(uint mana, uint cost, in WayOutLine line) => (ulong)cost + line.DrainCost <= mana;

    /// <summary>Whether the fight may spend this now. The order is the class's list.</summary>
    /// <param name="s">The spend.</param>
    public static WayOutAnswer Spend(in WayOutSpend s)
    {
        if (!s.Attackers)
        {
            return new(true, NoAttackers);
        }

        if (s.Kind == WayOutSpendKind.Bolt && s.KillOfLast)
        {
            return new(true, KillOfLast);
        }

        if ((ulong)s.Cost + s.Line.Line <= s.Mana)
        {
            return new(true, OverTheLine);
        }

        if (s.Kind == WayOutSpendKind.Buff)
        {
            return new(false, BuffRefused);
        }

        if (s.Mana < s.Line.RecallCost)
        {
            return new(true, RecallGone);
        }

        if (s.Kind != WayOutSpendKind.Drain)
        {
            return new(false, BoltRefused);
        }

        if ((ulong)s.Cost + s.Line.RecallCost <= s.Mana)
        {
            return new(true, DrainPaid);
        }

        return s.NextHitKills ? new(true, DrainNextHit) : new(false, DrainRefused);
    }

    /// <summary>The line that names the reserve: <c>way out: line=&lt;n&gt; (Lifestone Recall &lt;a&gt; + Drain Health Other I &lt;b&gt;)</c>.</summary>
    /// <param name="line">The line.</param>
    public static string Named(in WayOutLine line) =>
        string.Create(CultureInfo.InvariantCulture, $"line={line.Line} (Lifestone Recall {line.RecallCost} + Drain Health Other I {line.DrainCost})");

    /// <summary>The decision's log line: <c>retreat: mana: decide mana=&lt;m&gt; line=&lt;n&gt; ... -&gt; &lt;retreat|hold&gt; (&lt;why&gt;)</c>; a retreat for mana is named apart from the health's <c>retreat: decide</c>.</summary>
    /// <param name="i">The inputs.</param>
    /// <param name="d">The decision for them.</param>
    public static string DecideLine(in WayOutInput i, in WayOutAnswer d) => string.Create(
        CultureInfo.InvariantCulture,
        $"retreat: mana: decide mana={i.Mana} {Named(i.Line)} attackers={Word(i.Attackers)} kill-pending={Word(i.KillPending)} press={Word(i.Press)} dance={Word(i.Dance)} refused={Word(i.Refused)} potion={Word(i.Potion)} losing={Word(i.Losing)} -> {(d.Yes ? "retreat" : "hold")} ({d.Why})");

    private static string Word(bool value) => value ? "yes" : "no";
}
