using System.Globalization;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 (the owner, 2026-11-14: at her level the potions go; with Mana Conversion trained, Revitalize Self then Stamina to Mana Self restores the mana quickly and needs no potion at all): whether the stamina dance covers her mana.
/// It does when she knows a Revitalize Self level and a Stamina to Mana Self level that the pack pays (the level floor, <see cref="LevelFloor"/>), her Mana Conversion is trained, and one round of it nets mana: the press's net
/// (<see cref="ManaOrder.BestNetLevel"/>, the give less the press's expected cost) from the stamina one Revitalize restores (its boost, cut to her maximum) is over Revitalize's own expected cost
/// (<see cref="ManaConversionModel.ExpectedCost"/>). Then no potion is bought or kept (<see cref="PackRoomMemory.DrinksDitched"/>). Pure: read from her live state, every number with it.
/// M6 14 walk dance (the owner, 2026-11-15: at her level the stamina dance replaces the potions): the round as numbers (<see cref="Round"/>: its net and its seconds, which the crossing's price and measure count) and the dance's next
/// cast toward a line (<see cref="Next"/>, which the walk's dance in a lull casts by).
/// </summary>
internal static class StaminaDance
{
    /// <summary>True when the dance covers her mana (see the class); <paramref name="why"/> says it with its numbers, either way.</summary>
    /// <param name="state">The game state.</param>
    /// <param name="spells">The spell table.</param>
    /// <param name="why">The reading, in words.</param>
    public static bool Covers(GameState state, IReadOnlyDictionary<uint, SpellInfo> spells, out string why) => Round(state, spells, out why) is not null;

    /// <summary>
    /// M6 14 walk dance: one round of the dance as her live state reads it, or null when the dance does not cover her (<paramref name="why"/> says why, as <see cref="Covers"/>): Revitalize Self at the highest level the book holds and the
    /// pack pays, its mean boost cut to her maximum stamina, its expected cost on her Mana Conversion; then the press that nets the most from that stamina (<see cref="ManaOrder.BestNetLevel"/>); the round's net (the press's net less
    /// Revitalize's cost) and its seconds (the two casts at the ladder's measured self seconds, <paramref name="policy"/>; NaN when the table is not read).
    /// </summary>
    /// <param name="state">The game state.</param>
    /// <param name="spells">The spell table.</param>
    /// <param name="why">The reading, in words.</param>
    /// <param name="policy">The ladder's vitals policy (its self cast seconds), or null: the war mage's ladder.</param>
    public static DanceRound? Round(GameState state, IReadOnlyDictionary<uint, SpellInfo> spells, out string why, LadderVitalsPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(spells);
        var c = CultureInfo.InvariantCulture;
        var q = state.Self.Qualities;
        if (!ManaOrder.TryManaConversion(q, out var conversion) || conversion == 0)
        {
            why = "Mana Conversion is not trained (or not read): every press costs its whole base; the potions stay";
            return null;
        }

        var presses = StaminaPress.Levels(state, spells);
        if (presses.Length == 0)
        {
            why = "no Stamina to Mana Self level the book holds and the pack pays: the potions stay";
            return null;
        }

        var book = state.Self.Spellbook;
        var held = LevelFloor.HeldFor(state, DatReaderWriter.Enums.MagicSchool.LifeMagic);
        SpellInfo? revitalize = null;
        var revitalizeIndex = -1;
        for (var i = 0; i < VitalsMeans.RevitalizeSelf.Count; i++)
        {
            if (book.Knows(VitalsMeans.RevitalizeSelf[i]) && spells.TryGetValue(VitalsMeans.RevitalizeSelf[i], out var info) && (held is null || !LevelFloor.ShortOf(held, info, out _)))
            {
                (revitalize, revitalizeIndex) = (info, i); // the highest level the book holds and the pack pays (the ids run lowest first)
            }
        }

        if (revitalize is null)
        {
            why = "no Revitalize Self level the book holds and the pack pays: the potions stay";
            return null;
        }

        if (!q.TryGetVitalLevels(MageTemplate.MaxStamina, out _, out var staminaMax) || staminaMax == 0 || !q.TryGetVitalLevels(MageTemplate.MaxMana, out _, out var manaMax) || manaMax == 0)
        {
            why = "her stamina or mana maximum is not read: the potions stay";
            return null;
        }

        var row = BuffStatMods.Revitalize.FirstOrDefault(r => r.Id == revitalize.Id);
        var boost = row.Id == revitalize.Id ? Math.Abs((row.Boost + row.Max) / 2.0) : 0; // the mean of ACE's roll in [Boost, Boost + Variance] (BuffStatMods.Revitalize, the world SELECT); unknown: a full stamina
        var restored = boost > 0 ? (uint)Math.Min(staminaMax, Math.Round(boost)) : staminaMax;
        var cost = ManaConversionModel.ExpectedCost(GearCasts.ManaCeiling(revitalize, 0), revitalize.Power, conversion);
        var press = ManaOrder.BestNetLevel(presses, restored, manaMax, conversion, ManaOrderOptions.DefaultTryChance, out var net);
        if (press < 0 || net <= cost)
        {
            why = string.Create(c, $"a press from the {restored} stamina {revitalize.Name} restores nets {net:0} mana, not over {revitalize.Name}'s expected {cost:0} on Mana Conversion {conversion}: the dance does not pay; the potions stay");
            return null;
        }

        var pressLevel = LevelOf(VitalsMeans.StaminaToManaSelf, presses[press].SpellId);
        var pressName = spells.TryGetValue(presses[press].SpellId, out var pressInfo) ? pressInfo.Name : "Stamina to Mana Self " + LevelFloor.Roman[Math.Clamp(pressLevel - 1, 0, LevelFloor.Roman.Length - 1)];
        var rules = policy ?? LadderLoader.WarMage.VitalsPolicy;
        var seconds = rules.On && rules.CastSecondsSelf.Count > 0 ? rules.SelfSeconds(revitalizeIndex + 1) + rules.SelfSeconds(pressLevel) : double.NaN;
        why = string.Create(c, $"the stamina dance pays: {revitalize.Name} restores {restored} stamina for an expected {cost:0} mana, then Stamina to Mana nets {net:0} from it, on Mana Conversion {conversion} ({net - cost:0} mana a round): no potion is bought or kept, and those held are sold (the owner, 2026-11-14)");
        var secondsWord = double.IsNaN(seconds) ? "seconds not read (the ladder's cast_seconds_self)" : seconds.ToString("0.#", c) + " s";
        var words = string.Create(c, $"{revitalize.Name} ({restored} stamina for {cost:0.#} mana) then {pressName} (nets {net:0.#}): {net - cost:0.#} mana a round in {secondsWord} on Mana Conversion {conversion}");
        return new DanceRound(revitalize.Id, revitalizeIndex, revitalize.Name, restored, cost, presses[press].SpellId, pressLevel, net, net - cost, seconds, conversion, words) { RevitalizeMana = GearCasts.ManaCeiling(revitalize, 0) };
    }

    /// <summary>
    /// M6 14 top-off (the owner, 2026-11-15: a top-off before the run resumes): the dance's next cast toward her full bars (pure). The mana first, a round at a time, to her maximum (<see cref="Next"/>'s choice, but a Revitalize
    /// only when it fits whole in her missing stamina, so no Revitalize is cast for a mana no press can lift); when no press nets in the mana she is missing, the stamina last: Revitalize Self when it fits whole and the mana pays its most
    /// (<see cref="DanceRound.RevitalizeMana"/>), and the round's press after it; done when the stamina is within one Revitalize of her maximum (a Revitalize would overflow, as a drink that would overflow is not drunk) and no press nets.
    /// A stop by name when the mana is short and no press lands on the mana held. Every step is the dance's own (<see cref="WalkDanceStep"/>).
    /// </summary>
    /// <param name="round">The round (<see cref="Round"/>).</param>
    /// <param name="presses">The press levels (<see cref="StaminaPress.Levels"/>), lowest first.</param>
    /// <param name="mana">Her mana.</param>
    /// <param name="manaMax">Her maximum mana.</param>
    /// <param name="stamina">Her stamina.</param>
    /// <param name="staminaMax">Her maximum stamina.</param>
    /// <param name="afterRevitalize">True when the last cast was a Revitalize that landed.</param>
    public static WalkDanceStep TopOff(in DanceRound round, ReadOnlySpan<DanceLevel> presses, uint mana, uint manaMax, uint stamina, uint staminaMax, bool afterRevitalize)
    {
        var c = CultureInfo.InvariantCulture;
        var fits = staminaMax >= stamina && staminaMax - stamina >= round.Restored;
        var step = Next(in round, presses, mana, manaMax, stamina, staminaMax, manaMax, afterRevitalize);
        if (step.Move == WalkDanceMove.Press || (step.Move == WalkDanceMove.Revitalize && fits))
        {
            return step; // the mana first, a round at a time
        }

        if (step.Move == WalkDanceMove.Revitalize && mana < manaMax)
        {
            // Next would Revitalize for a mana no press lifts now (no room for a whole Revitalize): the press from the stamina held when one nets, else the mana is as full as the dance makes it
            var held = Next(in round, presses, mana, manaMax, stamina, staminaMax, manaMax, afterRevitalize: true);
            if (held.Move == WalkDanceMove.Press)
            {
                return held;
            }
        }
        else if (step.Move == WalkDanceMove.Stop && mana < manaMax && !fits)
        {
            return step; // the mana is short and no press lands on it: the dance cannot run, by name
        }

        if (fits && mana >= round.RevitalizeMana)
        {
            return new WalkDanceStep(WalkDanceMove.Revitalize, round.RevitalizeIndex, round.Restored, string.Create(c, $"the stamina last: {round.RevitalizeName} at stamina {stamina} of {staminaMax} ({round.Restored} fits whole), mana {mana} of {manaMax} pays its most {round.RevitalizeMana}")) { ForStamina = true };
        }

        return new WalkDanceStep(WalkDanceMove.Done, -1, 0, string.Create(c, $"topped off: mana {mana} of {manaMax} (no press nets in the {manaMax - Math.Min(mana, manaMax)} missing), stamina {stamina} of {staminaMax} (within one {round.RevitalizeName} of {round.Restored} of her maximum{(fits ? string.Create(c, $", or the mana under its most {round.RevitalizeMana}") : string.Empty)})"));
    }

    /// <summary>
    /// M6 14 walk dance: the dance's next cast toward <paramref name="line"/> (pure). Done at the line. The press after a Revitalize that landed (the round's second cast), or from the stamina held when a Revitalize would not fit whole in
    /// her missing stamina (as a drink that would overflow is not drunk); else Revitalize Self at the round's level, the round's first cast. The press is the level that nets the most now: ACE's transfer from the stamina held, its give cut
    /// to the mana she is missing (<see cref="BuffStatMods.Transfer"/>), less its expected cost, of the levels that land at the try chance on the mana held. No press that nets and no room for a Revitalize: done when a level lands but
    /// none nets in what she is missing (her maximum), else a stop by name (no level lands: the dance cannot run).
    /// </summary>
    /// <param name="round">The round (<see cref="Round"/>).</param>
    /// <param name="presses">The press levels (<see cref="StaminaPress.Levels"/>), lowest first.</param>
    /// <param name="mana">Her mana.</param>
    /// <param name="manaMax">Her maximum mana.</param>
    /// <param name="stamina">Her stamina.</param>
    /// <param name="staminaMax">Her maximum stamina.</param>
    /// <param name="line">The mana the dance goes to (capped at her maximum by the caller).</param>
    /// <param name="afterRevitalize">True when the last cast was a Revitalize that landed.</param>
    public static WalkDanceStep Next(in DanceRound round, ReadOnlySpan<DanceLevel> presses, uint mana, uint manaMax, uint stamina, uint staminaMax, uint line, bool afterRevitalize)
    {
        var c = CultureInfo.InvariantCulture;
        if (mana >= line)
        {
            return new WalkDanceStep(WalkDanceMove.Done, -1, 0, string.Create(c, $"at the line: mana {mana} of {manaMax}, the line {line}"));
        }

        var missing = manaMax > mana ? manaMax - mana : 0u;
        var (index, capped, lands) = (-1, 0.0, false);
        for (var i = 0; i < presses.Length; i++)
        {
            if (ManaConversionModel.LandingChance(presses[i].BaseMana, presses[i].Power, round.Conversion, mana) < ManaOrderOptions.DefaultTryChance)
            {
                continue;
            }

            lands = true;
            var row = presses[i].Row;
            BuffStatMods.Transfer(in row, stamina, missing, out _, out var give);
            var net = give - ManaConversionModel.ExpectedCost(presses[i].BaseMana, presses[i].Power, round.Conversion);
            if (give > 0 && net > capped)
            {
                (index, capped) = (i, net);
            }
        }

        var press = index >= 0;
        var fits = staminaMax >= stamina && staminaMax - stamina >= round.Restored;
        if (press && (afterRevitalize || !fits))
        {
            var why = afterRevitalize
                ? string.Create(c, $"the round's press: {capped:0.#} net from stamina {stamina} of {staminaMax}")
                : string.Create(c, $"the press from the stamina held ({stamina} of {staminaMax}: a Revitalize of {round.Restored} would not fit whole): {capped:0.#} net");
            return new WalkDanceStep(WalkDanceMove.Press, index, capped, why);
        }

        if (!afterRevitalize && (fits || (!press && stamina < staminaMax)))
        {
            return new WalkDanceStep(WalkDanceMove.Revitalize, round.RevitalizeIndex, round.Restored, string.Create(c, $"{round.RevitalizeName} first: stamina {stamina} of {staminaMax}, {round.Restored} restored for an expected {round.Cost:0.#} mana"));
        }

        if (press)
        {
            return new WalkDanceStep(WalkDanceMove.Press, index, capped, string.Create(c, $"the press from stamina {stamina} of {staminaMax}: {capped:0.#} net"));
        }

        return lands
            ? new WalkDanceStep(WalkDanceMove.Done, -1, 0, string.Create(c, $"at her maximum: {missing} mana missing, no press nets in it from stamina {stamina} of {staminaMax}"))
            : new WalkDanceStep(WalkDanceMove.Stop, -1, 0, string.Create(c, $"no press lands at the try chance {ManaOrderOptions.DefaultTryChance:0.##} on mana {mana} of {manaMax} (stamina {stamina} of {staminaMax}): the dance cannot run"));
    }

    /// <summary>The level (1 is I) of <paramref name="id"/> in <paramref name="ids"/>, or 1 when it is not there.</summary>
    private static int LevelOf(IReadOnlyList<uint> ids, uint id)
    {
        for (var i = 0; i < ids.Count; i++)
        {
            if (ids[i] == id)
            {
                return i + 1;
            }
        }

        return 1;
    }
}

/// <summary>One round of the stamina dance as her live state reads it (<see cref="StaminaDance.Round"/>).</summary>
/// <param name="RevitalizeId">The Revitalize Self level's spell id.</param>
/// <param name="RevitalizeIndex">Its index in <see cref="VitalsMeans.RevitalizeSelf"/>.</param>
/// <param name="RevitalizeName">Its name.</param>
/// <param name="Restored">The stamina it restores (its mean boost, cut to her maximum).</param>
/// <param name="Cost">Its expected mana after her Mana Conversion.</param>
/// <param name="PressId">The best-net Stamina to Mana Self level's spell id from that stamina.</param>
/// <param name="PressLevel">Its level (1 is I).</param>
/// <param name="PressNet">Its net from that stamina (the give less its expected cost).</param>
/// <param name="Net">The round's net: the press's net less Revitalize's cost.</param>
/// <param name="Seconds">The round's seconds: the two casts at the ladder's measured self seconds (NaN: not read).</param>
/// <param name="Conversion">Her Mana Conversion.</param>
/// <param name="Words">The round in words, with its numbers.</param>
internal readonly record struct DanceRound(uint RevitalizeId, int RevitalizeIndex, string RevitalizeName, uint Restored, double Cost, uint PressId, int PressLevel, double PressNet, double Net, double Seconds, uint Conversion, string Words)
{
    /// <summary>M6 14 fire dance: the most Revitalize Self's level costs (its ceiling, <see cref="GearCasts.ManaCeiling"/>: what a cast whose Mana Conversion fails takes).</summary>
    public uint RevitalizeMana { get; init; }
}

/// <summary>The dance's next move (<see cref="StaminaDance.Next"/>).</summary>
internal enum WalkDanceMove : byte
{
    /// <summary>At the line (or her maximum): the dance ends.</summary>
    Done,

    /// <summary>Revitalize Self at the round's level.</summary>
    Revitalize,

    /// <summary>Stamina to Mana Self at <see cref="WalkDanceStep.Index"/> of the press levels.</summary>
    Press,

    /// <summary>No cast nets: the dance cannot run, by name.</summary>
    Stop,
}

/// <summary>A step of the dance: the move, the level's index (a press: in the press levels; a Revitalize: in <see cref="VitalsMeans.RevitalizeSelf"/>), what it is expected to give, and the reason.</summary>
/// <param name="Move">The move.</param>
/// <param name="Index">The level's index, or -1.</param>
/// <param name="Gives">The press's expected net mana, or the Revitalize's stamina.</param>
/// <param name="Why">The reason with its numbers.</param>
internal readonly record struct WalkDanceStep(WalkDanceMove Move, int Index, double Gives, string Why)
{
    /// <summary>M6 14 top-off: true for the stamina's last Revitalize (<see cref="StaminaDance.TopOff"/>): its round is for the stamina, so its mana net is not judged.</summary>
    public bool ForStamina { get; init; }
}
