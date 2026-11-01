using System.Globalization;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Session.Mage;

/// <summary>
/// M5 7 D139 rows (k) and (l): the hands' caster, kept through a swap and after a refused move. Pure over the read model; the callers send the moves and log the lines.
/// </summary>
/// <remarks>
/// <para><b>Why a pack with room.</b> ACE never swaps the held caster out by itself: a wield into an occupied held slot is refused with "You must remove your X to wield Y" and 0x00A0 (<c>DoHandleActionGetAndWieldItem</c>,
/// <c>WieldedLocationIsAvailable</c>, ACE 95ef426d <c>Player_Inventory.cs</c> 1800-1810; <c>DoHandleActionGetAndWieldItem_DequipItemToInventory</c>, 2059-2078, is reached only from code commented out at 1693-1705 and 1727-1739).
/// So the held caster must go to a pack first, and a move of a wielded item to a full pack is the trap of round 13 (2026-10-30 03:06:37 UTC): ACE unwields it, the pack refuses ("Unable to put Wand into container"), and the
/// silent fallback puts it in the main pack, the hands empty (<c>DoHandleActionPutItemInContainer</c>, 1268-1340). <see cref="SwapPack"/> picks the pack by the client's free-slot count: the kit pack (rotation gear, D121), else
/// the main pack (the sale leaves a rotation piece alone), else the review pack; none with room is no swap.</para>
/// <para><b>Why a rearm.</b> ACE answers a magic combat mode with no caster held by setting peace (<c>Player_Combat.cs</c> 872-882), so a mage whose hands are empty cannot cast at all, not even its recall. <see cref="Rearm"/>
/// names the caster held in the packs to wield then: the self-equip's pick (<see cref="SelfEquipJudge.BestCaster"/>) when one beats nothing, else the usable one with the highest war factor and mana conversion, else any caster
/// held (not appraised: ACE judges its requirements and a refusal is logged by the caller).</para>
/// </remarks>
public static class HeldCaster
{
    /// <summary>The container to move the held caster to before a swap: the kit pack, the main pack or the review pack, the first with a free slot by the client's count; 0 when none has one. <paramref name="why"/> names it.</summary>
    /// <param name="layout">The packs now.</param>
    /// <param name="why">The pack and its free slots, or why there is none.</param>
    public static uint SwapPack(PackLayout layout, out string why)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (layout.PackOf(PackJob.Kit) is { Free: > 0 } kit)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"the kit pack 0x{kit.Guid:X8} ({kit.Free} free of {kit.Capacity})");
            return kit.Guid;
        }

        var main = layout.FreeSlots(PackJob.Main);
        var kitWhy = layout.PackOf(PackJob.Kit) is { } full ? string.Create(CultureInfo.InvariantCulture, $"the kit pack 0x{full.Guid:X8} is full ({full.Capacity} of {full.Capacity})") : "no kit pack";
        if (main > 0)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"the main pack ({main} free of {layout.MainCapacity}; {kitWhy})");
            return layout.Owner;
        }

        if (layout.PackOf(PackJob.Review) is { Free: > 0 } review)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"the review pack 0x{review.Guid:X8} ({review.Free} free; {kitWhy}, the main pack is full)");
            return review.Guid;
        }

        why = $"no pack has a free slot ({kitWhy}, the main pack is full, the review pack is full or absent)";
        return 0;
    }

    /// <summary>True when the hands hold a caster now (<see cref="CombatModeGuard.Read"/>, the guard ACE's magic mode needs).</summary>
    /// <param name="state">The read model.</param>
    public static bool Held(GameState state) => CombatModeGuard.Read(state ?? throw new ArgumentNullException(nameof(state))).Caster is not null;

    /// <summary>The caster to wield when the hands hold none (see the class remarks), or null when the packs hold no caster; <paramref name="why"/> says which and why.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="judge">The self-equip's judge (its element and useful cantrips).</param>
    /// <param name="why">The decision with the numbers.</param>
    public static GearCandidate? Rearm(GameState state, SelfEquipJudge judge, out string why)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(judge);
        var casters = new List<GearCandidate>();
        foreach (var item in PackLayout.Items(state))
        {
            if ((item.ItemType & SelfEquipJudge.CasterType) != 0 && state.Objects.TryGet(item.Guid, out var o))
            {
                casters.Add(SelfEquipJudge.CandidateOf(state, o));
            }
        }

        if (casters.Count == 0)
        {
            why = "the packs hold no caster";
            return null;
        }

        var skills = new QualitiesSkills(state.Self.Qualities);
        if (judge.BestCaster(casters, SelfEquipJudge.Worn(state), skills, out var pickWhy) is { } choice && casters.FirstOrDefault(c => c.Guid == choice.Guid) is { } best)
        {
            why = pickWhy.Replace("gear: ", string.Empty, StringComparison.Ordinal);
            return best;
        }

        var usable = casters.Where(c => c.Appraisal is { IsNotFound: false } a && SelfEquipJudge.Unmet(a, skills) is null)
            .OrderByDescending(c => judge.WarFactor(c.Appraisal)).ThenByDescending(c => SelfEquipJudge.ManaConversion(c.Appraisal)).ThenBy(c => c.Guid).FirstOrDefault();
        if (usable is not null)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"'{usable.Name}' 0x{usable.Guid:X8}, the usable caster held with the best war factor x{judge.WarFactor(usable.Appraisal):0.###} and mana conversion +{SelfEquipJudge.ManaConversion(usable.Appraisal) * 100:0.#}% of {casters.Count}");
            return usable;
        }

        var any = casters.Where(c => c.Appraisal is null || c.Appraisal.IsNotFound).OrderBy(c => c.Guid).FirstOrDefault();
        if (any is not null)
        {
            why = string.Create(CultureInfo.InvariantCulture, $"'{any.Name}' 0x{any.Guid:X8}, not appraised (none of the {casters.Count} caster(s) held is known usable): ACE judges its requirements");
            return any;
        }

        why = string.Create(CultureInfo.InvariantCulture, $"none of the {casters.Count} caster(s) held meets its requirements");
        return null;
    }
}
