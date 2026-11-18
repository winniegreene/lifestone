using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Quartermaster;

/// <summary>
/// M6 16 Q10c (<c>TODO(engine-wear-terms)</c> closed): the wear pick's terms (<see cref="WearTerms"/>) are made here, for every site that judges armour and jewellery, where only the town trip made them before.
/// <para>The trip still asks for them where it always did (<c>ResupplyRun.WithWearTerms</c>, now a thin call to <see cref="WithWearTerms"/> over its own read-only sale context, and it still publishes the result for the
/// lore rule). The worn restorer's wearable pick and the corpse plan's verdict take <see cref="JudgeWithTerms"/>: the same terms of her now, the sale context of the session (<see cref="SaleContext.For"/>). Before, outside a
/// trip's published pick, those two judged armour and jewellery by the rule of before the pick (a useful cantrip of the list's names in a free slot, armour by <see cref="GearScore"/>), so a piece the trip would wear could be
/// passed over by them, and the reverse.</para>
/// <para><b>What stays the same.</b> The terms read the ladder's weights, the rung's hitter and gate lines, the ground's Magic Defense, her load and the ground's jumps, as the trip read them. The kept burden (the load stop of a
/// ground with jumps; asked of the sale verdict) is read for a judge of a site other than the trip only on a ground with jumps, the one place it decides anything (<see cref="WearTerms.KeptBurden"/> feeds the jump stop alone).
/// Nothing is sold, dropped or given by the terms: they only score a piece, and a verdict that takes a worn piece off names its reason in the pick's line (<see cref="WearPick.Why"/>: "cantrips first", "beats what it
/// displaces", the guard's words).</para>
/// </summary>
public sealed partial class Wardrobe
{
    /// <summary>
    /// The session's wear judge on <paramref name="ground"/> with the wear pick's terms of her now (see the class): for the worn restorer's wearable pick and the corpse plan's verdict. The terms' own sale context is
    /// the session's (<see cref="SaleContext.For"/>), made at the first piece that asks it.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="inputs">What the wardrobe read.</param>
    public SelfEquipJudge JudgeWithTerms(HuntingGround ground, IReadOnlyDictionary<uint, SpellInfo> spells, out WearInputs inputs)
    {
        var judge = JudgeOn(ground, spells, out inputs);
        var character = _state.Self.Name;
        return WithWearTerms(ground, judge, () => SaleContext.For(_state, ground, character, spells), keptBurden: ground.Jumps.Count > 0, out _);
    }

    /// <summary>
    /// <paramref name="judge"/> with the wear pick's terms of her now: the ladder's weights, the caster terms, the ground's Magic Defense, the rung's hitter and gate lines, her capacity and kept load, the ground's jumps.
    /// Moved from the town trip (M6 14 <c>m6-armour</c>) unchanged; the trip logs the terms and publishes the judge itself.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="judge">The judge to carry the terms.</param>
    /// <param name="book">The sale verdict's context the kept burden and the kept pieces read (made at each use; the trip's read-only one).</param>
    /// <param name="keptBurden">True to read the kept burden (the trip always does).</param>
    /// <param name="terms">The terms, for the asker's log line.</param>
    public SelfEquipJudge WithWearTerms(HuntingGround ground, SelfEquipJudge judge, Func<SaleContext> book, bool keptBurden, out WearTerms terms)
    {
        ArgumentNullException.ThrowIfNull(ground);
        ArgumentNullException.ThrowIfNull(judge);
        ArgumentNullException.ThrowIfNull(book);
        var ladder = LadderLoader.WarMage;
        var level = _state.Self.Qualities.Level;
        // the rung of the ground she is given (chooses none: RungOwner decides the ground), else the hardest hitter and the last gate of the rungs at or below her level
        var rung = ladder.Rungs.FirstOrDefault(r => string.Equals(r.GroundKey, ground.Key, StringComparison.Ordinal) || r.Alternates.Any(a => string.Equals(a.GroundKey, ground.Key, StringComparison.Ordinal)));
        var hitter = rung?.Health?.Hitter;
        var hitterWhy = hitter is null ? "no rung of this ground names one" : $"the {rung!.Key} rung's";
        if (hitter is null && ladder.Rungs.Where(r => r.Band.From <= level && r.Health is not null).Select(r => r.Health!.Hitter).MaxBy(h => h.Hit) is { } below)
        {
            (hitter, hitterWhy) = (below, $"the hardest hitter of the rungs at or below her level {level} ({(rung is null ? ground.Key + " is no rung's ground" : rung.Key + " names none")})");
        }

        var gate = (rung is null ? null : ladder.GateOf(rung.Key)) ?? ladder.Gates.LastOrDefault(g => ladder.Rungs.FirstOrDefault(r => r.Key == g.Rung) is { } gr && gr.Band.From <= level);
        var magicDefence = ground.Targets.Where(t => t.Role == GroundRole.Target).Select(t => t.MagicDefence).DefaultIfEmpty(0).Max();
        var load = LootContext.Of(_state); // the loot's own capacity: 150 x her current Strength, the bot's buff in
        terms = new WearTerms(ladder.WearScore, judge.Caster, magicDefence, hitter, ArmourLines.Of(gate))
        {
            Capacity = load.Capacity > 0 ? load.Capacity : null,
            KeptBurden = keptBurden ? KeptBurden(ground, load.Burden, book) : null,
            Carried = load.Burden > 0 ? load.Burden : null, // M6 14 capacity: her live load, which a Strength cantrip's capacity is valued on
            LoadLinePercent = MageLootThresholds.LoadLineOf(ground),
            Jumps = ground.Jumps.Count > 0,
            StaysInPack = StaysInPackWhenOff(book),
            Source = string.Create(CultureInfo.InvariantCulture, $"the hitter: {hitterWhy}; the lines: {(gate is null ? "no gate: none" : $"the {gate.Rung} gate's floor (the bot's lines are higher, so the armour term is an upper bound)")}; the Magic Defense: the ground's targets' highest"),
        };
        return judge.WithWear(terms);
    }

    /// <summary>
    /// M6 16 Q10d, moved here by Q10c: for the wear pick's burden term, whether a worn piece stays in her pack once it is taken off: the sale verdict (<see cref="SaleVerdict.Of"/>, read-only) keeps it before the table is
    /// reached (a kept-aside match, a quest piece, a review or noted piece, rotation gear, a leather-set piece, a caster her gear rules need). The table's own rows are not asked: a worn piece reads <c>gear-worn</c> there
    /// whatever it would be off. Asking changes nothing she holds.
    /// </summary>
    /// <param name="makeBook">The verdict's context, made at the first ask.</param>
    internal Func<WornPiece, bool> StaysInPackWhenOff(Func<SaleContext> makeBook)
    {
        SaleContext? book = null;
        return piece => _state.Objects.TryGet(piece.Guid, out var o)
            && SaleVerdict.Of(piece.Guid, o, false, book ??= makeBook()).Step is SaleStep.KeptAside or SaleStep.Rotation or SaleStep.Leather or SaleStep.StoneKept;
    }

    /// <summary>
    /// M6 14 (<c>m6-armour</c>), moved here by Q10c: the burden she keeps after a sale, for the load stop: her burden now less every held piece of gear that is not worn (the wear pick weighs each one itself) and less every pack
    /// item the sale verdict sells or drops (the loot); the components are kept. The verdict is the rule book's one (<see cref="SaleVerdict.Of"/> over <paramref name="makeBook"/>). The answer feeds the wear pick's load stop only:
    /// nothing is sold, dropped, given or taken off by it.
    /// </summary>
    /// <param name="ground">The ground.</param>
    /// <param name="burden">Her burden now.</param>
    /// <param name="makeBook">The verdict's context, made at the first item that asks.</param>
    internal int? KeptBurden(HuntingGround ground, int burden, Func<SaleContext> makeBook)
    {
        ArgumentNullException.ThrowIfNull(ground);
        if (burden <= 0)
        {
            return null;
        }

        var shed = 0;
        SaleContext? book = null;
        foreach (var item in PackLayout.Items(_state))
        {
            if (!_state.Objects.TryGet(item.Guid, out var o) || (o.Weenie.WielderId is { } w && w != 0) || FociComponents.IsComponent(o.Weenie.WeenieClassId, o.ItemType))
            {
                continue;
            }

            var gear = (o.ItemType & (KeepAsideRule.WearableTypes | SelfEquipJudge.CasterType | 0x1 | 0x100)) != 0;
            if (gear || SaleVerdict.Of(item.Guid, o, false, book ??= makeBook()).Action is PackAction.Sell or PackAction.Drop)
            {
                shed += o.Weenie.Burden ?? 0;
            }
        }

        return Math.Max(0, burden - shed);
    }
}
