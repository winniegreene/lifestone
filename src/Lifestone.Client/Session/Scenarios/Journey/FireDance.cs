using System.Globalization;
using Lifestone.Client.Session.Vitals;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 fire dance (the owner, 2026-11-15: fight the way to the grounds by killing everything; at her level the stamina dance replaces the potions, and dancing under fire is ordinary retail play; live
/// <c>cache/m6/14/live/ladder23</c> 02:51:52 UTC: a pack of Reedshark Ravagers kept her in fight-backs with no lull, the crossing's measure twice said "the stamina dance at the next lull", no lull came, and she recalled on the mana line
/// at 151 of 394, health 185 of 204, hit about 2 a blow). When the next bolt would take her mana under the way out's line (<see cref="ManaWayOut.Spend"/>'s <c>bolt-refused</c>), she dances instead of leaving, one cast at a time, then
/// bolts again. Pure: every number from her live state, every answer with its numbers.
/// <list type="number">
/// <item><b>Covered</b>: the dance covers her (<see cref="StaminaDance.Round"/>); else she leaves as before (<see cref="Uncovered"/>, the dance's own why).</item>
/// <item><b>Not losing</b>: the last seconds' hits would not kill her within <see cref="ManaWayOut.LosingSeconds"/> at her health (<see cref="ManaWayOut.IsLosing"/>); else she leaves (<see cref="Losing"/>).</item>
/// <item><b>Still paying</b>: fewer than <see cref="BarrenRounds"/> rounds in a row landed nothing or netted no mana; else she leaves (<see cref="Barren"/>).</item>
/// <item><b>The cast</b>: the dance's next cast toward the bolt over the line (<see cref="StaminaDance.Next"/> to the line plus the bolt, capped at her maximum): Revitalize Self, then the best-net press; the press from the stamina held
/// when a whole Revitalize would not fit. The recall stays paid: a press only at a level whose most leaves Lifestone Recall paid (the fight's press before the line, <c>FightScenario.WayOut.cs</c>), a Revitalize only when the mana over
/// the line pays its expected cost and its most leaves the recall paid: the dance never spends the reserve. A Revitalize the line refuses gives way to the press from the stamina held; neither: the stamina and the mana have run out, and she
/// leaves (<see cref="Spent"/>).</item>
/// </list>
/// The heal is not the dance's: the survival layer's vitals guard holds the leg while it heals (the cast in flight ends first), so at the emergency or fight line the heal comes first.
/// </summary>
internal static class FireDance
{
    /// <summary>The rounds in a row that land nothing or net no mana after which the dance stops paying (the walk's dance's own two, <c>RungChangeStation.WalkDanceNoNetRounds</c>).</summary>
    public const int BarrenRounds = 2;

    /// <summary>The token of a Revitalize Self under fire.</summary>
    public const string GoRevitalize = "dance:revitalize";

    /// <summary>The token of a press under fire.</summary>
    public const string GoPress = "dance:press";

    /// <summary>The leaving's token when the dance does not cover her (the old leaving).</summary>
    public const string Uncovered = "dance-uncovered";

    /// <summary>The leaving's token when the hits begin to win.</summary>
    public const string Losing = "dance-losing";

    /// <summary>The leaving's token when <see cref="BarrenRounds"/> rounds in a row landed nothing or netted no mana.</summary>
    public const string Barren = "dance-barren";

    /// <summary>The leaving's token when the stamina and the mana have both run out for the dance.</summary>
    public const string Spent = "dance-spent";

    /// <summary>The dance's answer under fire (the class's list, in its order).</summary>
    /// <param name="i">Her live numbers.</param>
    /// <param name="round">The round on her live state, or null: the dance does not cover her.</param>
    /// <param name="coverWhy">Why the dance covers her or not (<see cref="StaminaDance.Round"/>'s words).</param>
    /// <param name="presses">The press levels (<see cref="StaminaPress.Levels"/>), lowest first.</param>
    public static FireDanceStep Decide(in FireDanceInput i, DanceRound? round, string coverWhy, ReadOnlySpan<DanceLevel> presses)
    {
        var c = CultureInfo.InvariantCulture;
        if (round is not { } r)
        {
            return Leave(Uncovered, "the stamina dance does not cover her: " + coverWhy);
        }

        if (ManaWayOut.IsLosing(i.Health, i.IncomingPerSecond))
        {
            return Leave(Losing, string.Create(c, $"the hits win: {i.IncomingPerSecond:0.#} a second over {ManaWayOut.LosingSeconds:0} s is {i.IncomingPerSecond * ManaWayOut.LosingSeconds:0} against her health {i.Health}"));
        }

        if (i.Barren >= BarrenRounds)
        {
            return Leave(Barren, string.Create(c, $"{i.Barren} rounds in a row landed nothing or netted no mana (the round's reading {r.Net:0.#}): the dance does not pay under these hits"));
        }

        var line = i.Line;
        var target = (uint)Math.Min((ulong)line.Line + i.BoltCost, i.ManaMax);
        var paid = 0;
        while (paid < presses.Length && (ulong)presses[paid].BaseMana + line.RecallCost <= i.Mana)
        {
            paid++; // the levels whose most leaves the recall paid (the levels run lowest first, their base mana rising)
        }

        var levels = presses[..paid];
        var step = StaminaDance.Next(in r, levels, i.Mana, i.ManaMax, i.Stamina, i.StaminaMax, target, i.AfterRevitalize);
        var over = i.Mana > line.Line ? i.Mana - line.Line : 0u;
        var toward = string.Create(c, $"toward {target} (the line {line.Line} and the bolt's {i.BoltCost}{(target == i.ManaMax ? string.Create(c, $", capped at her maximum {i.ManaMax}") : string.Empty)})");
        switch (step.Move)
        {
            case WalkDanceMove.Press:
                return new FireDanceStep(FireDanceMove.Press, step.Index, step.Gives, GoPress, string.Create(c, $"{step.Why}, {toward}; its most leaves Lifestone Recall's {line.RecallCost} paid"));

            case WalkDanceMove.Revitalize:
                if (r.Cost <= over && (ulong)r.RevitalizeMana + line.RecallCost <= i.Mana)
                {
                    return new FireDanceStep(FireDanceMove.Revitalize, step.Index, step.Gives, GoRevitalize, string.Create(c, $"{step.Why}, {toward}: its expected {r.Cost:0.#} is within the {over} over the line, and its most {r.RevitalizeMana} leaves Lifestone Recall's {line.RecallCost} paid"));
                }

                var refused = string.Create(c, $"{r.RevitalizeName}'s expected {r.Cost:0.#} against the {over} over the line {line.Line}, its most {r.RevitalizeMana} against the {(i.Mana > line.RecallCost ? i.Mana - line.RecallCost : 0)} over Lifestone Recall's {line.RecallCost}: the line refuses it");
                var held = StaminaDance.Next(in r, levels, i.Mana, i.ManaMax, i.Stamina, i.StaminaMax, target, afterRevitalize: true);
                return held.Move == WalkDanceMove.Press
                    ? new FireDanceStep(FireDanceMove.Press, held.Index, held.Gives, GoPress, string.Create(c, $"{refused}; the press from the stamina held: {held.Why}, {toward}"))
                    : Leave(Spent, string.Create(c, $"{refused}, and no press nets from stamina {i.Stamina} of {i.StaminaMax} at a level whose most leaves the recall paid: the stamina and the mana have run out"));

            default:
                return Leave(Spent, string.Create(c, $"{step.Why} (the levels whose most leaves Lifestone Recall's {line.RecallCost} paid, {toward}): the stamina and the mana have run out"));
        }
    }

    private static FireDanceStep Leave(string token, string why) => new(FireDanceMove.Leave, -1, 0, token, why);
}

/// <summary>What <see cref="FireDance.Decide"/> reads: her live numbers.</summary>
/// <param name="Line">The way out's line.</param>
/// <param name="BoltCost">The bolt's most mana.</param>
/// <param name="Mana">Her mana.</param>
/// <param name="ManaMax">Her maximum mana.</param>
/// <param name="Stamina">Her stamina.</param>
/// <param name="StaminaMax">Her maximum stamina.</param>
/// <param name="Health">Her health.</param>
/// <param name="IncomingPerSecond">The last seconds' hits, a second (the recent hits' read, the autopilot's own seam).</param>
/// <param name="AfterRevitalize">True when the last cast was a Revitalize that landed (the round's press is next).</param>
/// <param name="Barren">The rounds in a row that landed nothing or netted no mana.</param>
internal readonly record struct FireDanceInput(WayOutLine Line, uint BoltCost, uint Mana, uint ManaMax, uint Stamina, uint StaminaMax, uint Health, double IncomingPerSecond, bool AfterRevitalize, int Barren);

/// <summary>The fire dance's move.</summary>
internal enum FireDanceMove : byte
{
    /// <summary>She leaves (the token names why).</summary>
    Leave,

    /// <summary>Revitalize Self at the round's level.</summary>
    Revitalize,

    /// <summary>Stamina to Mana Self at <see cref="FireDanceStep.Index"/> of the press levels.</summary>
    Press,
}

/// <summary>The fire dance's answer: the move, the level's index (a press: in the press levels; a Revitalize: in <see cref="VitalsMeans.RevitalizeSelf"/>), what it is expected to give, its token and its reason.</summary>
/// <param name="Move">The move.</param>
/// <param name="Index">The level's index, or -1.</param>
/// <param name="Gives">The press's expected net mana, or the Revitalize's stamina.</param>
/// <param name="Token">The token (<see cref="FireDance"/>'s constants).</param>
/// <param name="Why">The reason with its numbers.</param>
internal readonly record struct FireDanceStep(FireDanceMove Move, int Index, double Gives, string Token, string Why);
