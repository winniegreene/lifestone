using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// The Stamina to Mana Self levels her book holds and the pack pays (the owner's rule of picking by the sum, 2026-11-08: a climber's levels are those whose components the pack holds, <see cref="LevelFloor.HeldFor"/>), and what one press nets now
/// (M6 14 crossing stamina: the walk's crossing measure counts it as mana she has).
/// </summary>
internal static class StaminaPress
{
    /// <summary>The Stamina to Mana Self levels the book holds, lowest first (for a climber, those the pack pays).</summary>
    /// <param name="state">The game state.</param>
    /// <param name="spells">The spell table.</param>
    public static DanceLevel[] Levels(GameState state, IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(spells);
        var book = state.Self.Spellbook;
        var levels = new List<DanceLevel>();
        var ids = VitalsMeans.StaminaToManaSelf;
        var held = CharacterPolicy.For(state.Self.Name).ManaBySum ? LevelFloor.HeldFor(state, DatReaderWriter.Enums.MagicSchool.LifeMagic) : null;
        for (var i = 0; i < ids.Count && i < BuffStatMods.StaminaToManaBaseMana.Count; i++)
        {
            if (book.Knows(ids[i]) && spells.TryGetValue(ids[i], out var info) && BuffStatMods.TransferOf(ids[i]) is { } row && (held is null || !LevelFloor.ShortOf(held, info, out _)))
            {
                levels.Add(new DanceLevel(ids[i], BuffStatMods.StaminaToManaBaseMana[i], info.Power, row));
            }
        }

        return [.. levels];
    }

    /// <summary>
    /// The mana one press nets now: the best level by net (<see cref="ManaOrder.BestNetLevel"/>) at the stamina and the mana held, at her Mana Conversion and the vitals policy's landing chance, capped at the mana she is missing; 0 when
    /// no level lands, the vitals are unread or the net is not positive.
    /// </summary>
    /// <param name="state">The game state.</param>
    /// <param name="spells">The spell table.</param>
    public static double NetNow(GameState state, IReadOnlyDictionary<uint, SpellInfo> spells)
    {
        ArgumentNullException.ThrowIfNull(state);
        var q = state.Self.Qualities;
        if (!q.TryGetVitalLevels(MageTemplate.MaxStamina, out var stamina, out _) || !q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var manaMax) || manaMax <= mana)
        {
            return 0;
        }

        var levels = Levels(state, spells);
        if (levels.Length == 0)
        {
            return 0;
        }

        ManaOrder.TryManaConversion(q, out var conversion);
        if (ManaOrder.BestNetLevel(levels, stamina, mana, conversion, ManaOrderOptions.DefaultTryChance, out var net) < 0 || net <= 0)
        {
            return 0;
        }

        return Math.Min(net, manaMax - mana);
    }
}
