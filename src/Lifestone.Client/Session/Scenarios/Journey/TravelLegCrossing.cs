using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 approach cost (rule 2; the owner, 2026-11-14: she was fighting things with high magic resistance, so she should recall and go back to the previous ground; live <c>cache/m6/14/live/holdtrial2</c>: on the Holding's approach the
/// fight-backs took 3, 6, 20 and 28 casts for 1, 2, 4 and 3 kills, and the mana line recalled her at 13:03:17 at 111 of 387, 590 m short of the portal): check, act, check on a rung change's run out to a ground's approach.
/// <list type="bullet">
/// <item><b>Check</b>: each fight-back kill and each fight-back's end reads the crossing so far (<see cref="ApproachCost.Measured"/>): the casts a kill, the net mana a kill since the run began (the bolts, the vitals guard's heals and the
/// regeneration all in it), the kills a metre of the run gained toward the entry's front, after the fight's own seed kills (<see cref="LadderVitalsPolicy.SeedKills"/>). One line each.</item>
/// <item><b>Act</b>: when the rest of the run at that rate would take more mana than she holds over the way out's reserve, she will not reach the portal above it: the retreat is due now (<c>why=approach-abandoned</c>, the leg's
/// Lifestone Recall, <see cref="Retreat"/>), before the mana line forces it, and the crossing is given up for the rest of the run by name (<see cref="JourneyFacts.AbandonApproach"/>): the ladder's next way out holds the rung
/// <c>approach-abandoned:&lt;rung&gt;</c> and she hunts the previous rung's ground; the same crossing is never tried again in the run.</item>
/// <item><b>Check after</b>: the retreat's own (present in the home lifestone's landblock; <c>retreat:run:recall</c>), and the next way out's line names the hold.</item>
/// <item><b>M6 14 walk dance</b> (the owner, 2026-11-15; live <c>ladder22</c> 01:54:50 UTC: the Armory's crossing given up at 200 mana, "7.2 kills, 232 mana, against 136 over the reserve", with the dance learned): each measure reads the
/// dance's round on her live state (<see cref="StaminaDance.Round"/>), the mana the dances have added and whether the dance has stopped by its bounds. While the dance covers her and can still dance, a shortfall does not give the crossing
/// up: the measure asks for the dance at the next lull (<see cref="CrossingDanceDue"/>, its line the reserve plus the need, capped at her maximum; <see cref="TravelLeg.DanceOnTheWalk"/>), and the crossing is measured again after it. It is
/// given up when the dance cannot keep pace (its stop, named) or does not cover her (why, named).</item>
/// </list>
/// </summary>
internal abstract partial class TravelLeg
{
    /// <summary>The crossing a run leg measures (rule 2).</summary>
    private sealed class CrossingWatch(string ground, uint frontCell, Vector3 frontAt, uint manaStart, double metresAtStart, uint reserve, string reserveWhy, double priorPerMetre)
    {
        public double PriorPerMetre { get; } = priorPerMetre;

        public string Ground { get; } = ground;

        public uint FrontCell { get; } = frontCell;

        public Vector3 FrontAt { get; } = frontAt;

        public uint ManaStart { get; } = manaStart;

        public double MetresAtStart { get; } = metresAtStart;

        public uint Reserve { get; } = reserve;

        public string ReserveWhy { get; } = reserveWhy;

        public int Kills { get; set; }

        public int Casts { get; set; }

        public string? Logged { get; set; }

        /// <summary>M6 14 walk dance: the mana the dances have added on this run (the casts' net).</summary>
        public double Danced { get; set; }

        /// <summary>M6 14 walk dance: the dances run on this run.</summary>
        public int Dances { get; set; }

        /// <summary>M6 14 walk dance: why the dance cannot run or keep pace on this run (its bounds, named), or null.</summary>
        public string? DanceStalled { get; set; }

        /// <summary>M6 14 walk dance: the line the last measure asked her to dance to, or 0: no dance due.</summary>
        public uint DanceTo { get; set; }

        /// <summary>M6 14 walk dance: the last measure's answer in words (the dance's why).</summary>
        public string DanceWhy { get; set; } = string.Empty;
    }

    private CrossingWatch? _crossing;

    /// <summary>
    /// The start of a run out to <paramref name="ground"/>'s entry front: when the ground records an approach and her mana, her place and the way out's reserve are read, the crossing is measured from here (<see cref="CrossingGivenUp"/>);
    /// else nothing is measured and the line says why.
    /// </summary>
    /// <param name="ground">The ground the run goes to.</param>
    /// <param name="frontCell">The run's goal cell.</param>
    /// <param name="frontAt">The run's goal point.</param>
    protected void BeginCrossing(HuntingGround ground, uint frontCell, Vector3 frontAt)
    {
        ArgumentNullException.ThrowIfNull(ground);
        _crossing = null;
        if (ground.Approach is null)
        {
            return;
        }

        var reserve = ReserveLine(out var reserveWhy);
        var metres = S.State.Self.Position is { } body ? MetresBetween(body, new CellFrame(frontCell, new AcFrame(frontAt, Quaternion.Identity))) : double.NaN;
        if (ManaNow() is not { } mana || double.IsNaN(metres) || reserve is not { } line)
        {
            Log($"crossing: {ground.Key}'s approach is not measured on this run (mana {(ManaNow() is { } m ? m.ToString(CultureInfo.InvariantCulture) : "unread")}, metres to the front {(double.IsNaN(metres) ? "unread" : metres.ToString("0", CultureInfo.InvariantCulture))}, reserve {reserveWhy}) (M6 14 approach cost, rule 2)");
            return;
        }

        var prior = ApproachCost.CreaturesPerMetre(ground.Approach) ?? double.NaN;
        _crossing = new CrossingWatch(ground.Key, frontCell, frontAt, mana, metres, line.Line, reserveWhy, prior);
        var priorWhy = double.IsNaN(prior) ? "the approach's facts give no rate: the measured rate alone" : string.Create(CultureInfo.InvariantCulture, $"the approach's facts' {prior * 100:0.##} creatures a 100 m ({prior * metres:0.#} over the run) are blended with the kills measured");
        Log(string.Create(CultureInfo.InvariantCulture, $"crossing: {ground.Key}'s approach is measured from here: mana {mana}, {metres:0} m to the front, the way out's reserve {reserveWhy}; after {SeedKills} kills a projection that ends under the reserve recalls her; {priorWhy} (M6 14 approach cost, rule 2)"));
    }

    /// <summary>The end of the run leg: the crossing is no longer measured.</summary>
    protected void EndCrossing() => _crossing = null;

    private static int SeedKills => LadderLoader.WarMage.VitalsPolicy.SeedKills;

    private void CrossingCast()
    {
        if (_crossing is { } crossing)
        {
            crossing.Casts++;
        }
    }

    private void CrossingKill()
    {
        if (_crossing is { } crossing)
        {
            crossing.Kills++;
        }
    }

    /// <summary>
    /// The crossing's check (rule 2): the measured answer, logged when its numbers change; when she will not reach the portal above the reserve, the crossing is given up for the run (<see cref="JourneyFacts.AbandonApproach"/>) and the why
    /// is returned for the retreat; else null.
    /// </summary>
    private string? CrossingGivenUp()
    {
        if (_crossing is not { } crossing || ManaNow() is not { } mana || S.State.Self.Position is not { } body)
        {
            return null;
        }

        var metres = MetresBetween(body, new CellFrame(crossing.FrontCell, new AcFrame(crossing.FrontAt, Quaternion.Identity)));
        if (double.IsNaN(metres))
        {
            return null;
        }

        // M6 14 walk dance: the dance's round on her live state (its net a round), how much it has added, and whether it can still dance on this run
        var round = StaminaDance.Round(S.State, Context.Spells, out var danceWhy);
        var manaMax = S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var max) ? max : 0u;
        var measure = new CrossingMeasure(crossing.Kills, crossing.Casts, crossing.ManaStart, mana, crossing.Reserve, crossing.MetresAtStart, metres, SeedKills, crossing.PriorPerMetre, StaminaPress.NetNow(S.State, Context.Spells))
        {
            DanceNet = round?.Net ?? 0,
            DancedMana = crossing.Danced,
            DanceStalled = crossing.DanceStalled,
            DanceWhy = round is null ? danceWhy : null,
            ManaMax = manaMax,
        };
        var answer = ApproachCost.Measured(in measure);
        (crossing.DanceTo, crossing.DanceWhy) = answer.DanceDue ? (answer.DanceTo, answer.Why) : (0u, string.Empty);
        var line = $"crossing: {crossing.Ground}: {answer.Why} (M6 14 approach cost, rule 2)";
        if (!string.Equals(line, crossing.Logged, StringComparison.Ordinal))
        {
            crossing.Logged = line;
            Log(line);
        }

        if (!answer.Abandon)
        {
            return null;
        }

        var why = string.Create(CultureInfo.InvariantCulture, $"{answer.Why}; the reserve is {crossing.ReserveWhy}");
        Context.Facts.AbandonApproach(crossing.Ground, why);
        _crossing = null; // given up: the retreat follows and nothing more is measured
        return why;
    }

    /// <summary>
    /// M6 14 walk dance: the dance the last crossing measure asked for (<see cref="CrossingGivenUp"/>, <see cref="CrossingMeasureAnswer.DanceDue"/>): its line (the reserve plus the need, capped at her maximum) and the answer's words; null when none
    /// is due or the crossing is not measured. The dance itself ends at the line.
    /// </summary>
    protected (uint Line, string Why)? CrossingDanceDue()
    {
        if (_crossing is not { DanceTo: > 0 } crossing)
        {
            return null;
        }

        return (crossing.DanceTo, crossing.DanceWhy);
    }

    /// <summary>M6 14 walk dance: a dance's cast landed or not: the mana it moved (a press's give, a Revitalize's cost) is counted out of the fights' fall at once, so a measure during the dance (its fight-backs' kills) reads the fights alone.</summary>
    /// <param name="netted">The cast's change of her mana.</param>
    protected void CrossingDanceCast(double netted)
    {
        if (_crossing is { } crossing && double.IsFinite(netted))
        {
            crossing.Danced += netted;
        }
    }

    /// <summary>M6 14 walk dance: a dance ended; when it stopped by its bounds, why (the crossing's measure then says the dance cannot keep pace, and the crossing is given up when it falls short).</summary>
    /// <param name="stalled">Why it stopped by its bounds, or null.</param>
    protected void CrossingDanceEnded(string? stalled)
    {
        if (_crossing is not { } crossing)
        {
            return;
        }

        crossing.Dances++;
        crossing.DanceStalled ??= stalled;
        crossing.DanceTo = 0;
    }

    /// <summary>M6 14 walk dance: the crossing's check after a dance's fight-back (<see cref="CrossingGivenUp"/>): the retreat is set due by name when it gives the crossing up.</summary>
    protected void CrossingCheckAfterFight()
    {
        if (_retreatWhy is null && CrossingGivenUp() is { } gaveUp)
        {
            RetreatDue(ApproachCost.AbandonedPrefix, gaveUp);
        }
    }
}
