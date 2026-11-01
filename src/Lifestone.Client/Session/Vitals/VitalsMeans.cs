using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Vitals;

/// <summary>A self-cast utility line the keeper reads by name (<see cref="VitalsMeans"/>).</summary>
public enum VitalsLine
{
    /// <summary>Heal Self (I 6 ... VII 2073 Adja's Intervention).</summary>
    HealSelf,

    /// <summary>Revitalize Self (I 1177 ... VII 2083 Robustification).</summary>
    RevitalizeSelf,

    /// <summary>Stamina to Mana Self (I 1676 ... VII 2345 Meditative Trance).</summary>
    StaminaToManaSelf,

    /// <summary>Harm Self (I 8 ... VII 2071): never cast by the keeper; listed for lane C3's harm-heal station.</summary>
    HarmSelf,
}

/// <summary>
/// What a character can restore its vitals with, read only from the session's server-confirmed state (M4 P5a lane C2, automation-vision R3): the known spells in the spellbook, the pack's Handy Healing Kits and Potions of
/// Healing, and the skills. The keeper's own small id table of the four utility lines (cache research section 2, from ace_world.spell; lane C2 does not depend on C1's profile type: after both merge the lead may
/// point the table at the profile). The level rule is the buff cycle's: the highest <b>known</b> level whose <see cref="GearCasts.Chance"/> from the current skill (<see cref="GearCasts.TryCurrentSkill"/>) reaches the
/// threshold; the power and the cost are the SpellTable's (a level the table given to the keeper lacks is skipped, logged by the keeper).
/// </summary>
public static class VitalsMeans
{
    /// <summary>Heal Self I to VII.</summary>
    public static IReadOnlyList<uint> HealSelf { get; } = [6, 1157, 1158, 1159, 1160, 1161, 2073];

    /// <summary>Revitalize Self I to VII.</summary>
    public static IReadOnlyList<uint> RevitalizeSelf { get; } = [1177, 1178, 1179, 1180, 1181, 1182, 2083];

    /// <summary>Stamina to Mana Self I to VII.</summary>
    public static IReadOnlyList<uint> StaminaToManaSelf { get; } = [1676, 1677, 1678, 1679, 1680, 1681, 2345];

    /// <summary>Harm Self I to VII (for C3; the keeper never casts it).</summary>
    public static IReadOnlyList<uint> HarmSelf { get; } = [8, 1167, 1168, 1169, 1170, 1171, 2071];

    /// <summary>The ids of <paramref name="line"/>, I to VII.</summary>
    public static IReadOnlyList<uint> IdsOf(VitalsLine line) => line switch
    {
        VitalsLine.HealSelf => HealSelf,
        VitalsLine.RevitalizeSelf => RevitalizeSelf,
        VitalsLine.StaminaToManaSelf => StaminaToManaSelf,
        _ => HarmSelf,
    };

    /// <summary>The line's name as the log and the verdict say it.</summary>
    public static string NameOf(VitalsLine line) => line switch
    {
        VitalsLine.HealSelf => "Heal Self",
        VitalsLine.RevitalizeSelf => "Revitalize Self",
        VitalsLine.StaminaToManaSelf => "Stamina to Mana Self",
        _ => "Harm Self",
    };

    /// <summary>
    /// The known level of <paramref name="line"/> with the best cast chance at the current skill (the higher level on a tie), whatever the chance: the harm-heal's named fallback when <see cref="Pick"/> finds no level at its threshold
    /// (P5a round 3: BSD Mage under 40% vitae read Life Magic 310 and knows Heal Self VII only above it, a chance of about 0.67). Null when no level is known, the skill cannot be read or the SpellTable lacks the spell.
    /// </summary>
    /// <param name="q">The player's qualities (the skill).</param>
    /// <param name="book">The spellbook.</param>
    /// <param name="spells">The SpellTable's spells.</param>
    /// <param name="line">The line.</param>
    /// <param name="chance">The chosen level's chance (NaN when none).</param>
    /// <param name="held">M5 4 G1: what the pack holds when the level floor applies (<see cref="LevelFloor.HeldFor"/>); a level whose foci-formula components it lacks is passed over. Null: no floor.</param>
    public static SpellInfo? PickBestChance(PlayerQualities q, Spellbook book, IReadOnlyDictionary<uint, SpellInfo> spells, VitalsLine line, out double chance, IReadOnlyDictionary<uint, int>? held = null)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(spells);
        chance = double.NaN;
        SpellInfo? best = null;
        var ids = IdsOf(line);
        for (var i = ids.Count - 1; i >= 0; i--)
        {
            if (!book.Knows(ids[i]) || !spells.TryGetValue(ids[i], out var info) || !GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(info.School), out var skill, out _))
            {
                continue;
            }

            if (held is not null && LevelFloor.ShortOf(held, info, out _))
            {
                continue; // M5 4 G1: the level floor
            }

            var c = GearCasts.Chance(skill, info.Power);
            if (best is null || c > chance)
            {
                best = info;
                chance = c;
            }
        }

        return best;
    }

    /// <summary>True when the spellbook knows any level of <paramref name="line"/>.</summary>
    public static bool KnowsAny(Spellbook book, VitalsLine line)
    {
        ArgumentNullException.ThrowIfNull(book);
        var ids = IdsOf(line);
        for (var i = 0; i < ids.Count; i++)
        {
            if (book.Knows(ids[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The highest known level of <paramref name="line"/> in <paramref name="spells"/> whose chance from the current skill reaches <paramref name="threshold"/>, or null with the reason in <paramref name="why"/>
    /// (no level known, the skill unreadable, or none reaches the chance).
    /// </summary>
    /// <param name="q">The player's qualities (the skill).</param>
    /// <param name="book">The spellbook.</param>
    /// <param name="spells">The SpellTable's spells (power, school, cost).</param>
    /// <param name="line">The line.</param>
    /// <param name="threshold">The least chance.</param>
    /// <param name="why">Why there is none (empty when one was found).</param>
    /// <param name="held">M5 4 G1: what the pack holds when the level floor applies (<see cref="LevelFloor.HeldFor"/>); a level whose foci-formula components it lacks is passed over. Null: no floor.</param>
    public static SpellInfo? Pick(PlayerQualities q, Spellbook book, IReadOnlyDictionary<uint, SpellInfo> spells, VitalsLine line, double threshold, out string why, IReadOnlyDictionary<uint, int>? held = null)
    {
        ArgumentNullException.ThrowIfNull(q);
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(spells);
        var ids = IdsOf(line);
        why = NameOf(line) + ": no level is known";
        var skillRead = false;
        var skill = 0u;
        for (var i = ids.Count - 1; i >= 0; i--)
        {
            if (!book.Knows(ids[i]))
            {
                continue;
            }

            if (!spells.TryGetValue(ids[i], out var info))
            {
                why = NameOf(line) + ": the SpellTable given to the keeper has no spell " + ids[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
                continue;
            }

            if (!skillRead)
            {
                if (!GearCasts.TryCurrentSkill(q, GearCasts.SkillOf(info.School), out skill, out var skillWhy))
                {
                    why = NameOf(line) + ": " + skillWhy;
                    return null;
                }

                skillRead = true;
            }

            if (GearCasts.Chance(skill, info.Power) >= threshold)
            {
                if (held is not null && LevelFloor.ShortOf(held, info, out var need))
                {
                    why = NameOf(line) + ": no level the pack can pay for (" + info.Name + " needs " + need + ")";
                    continue;
                }

                why = string.Empty;
                return info;
            }

            why = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{NameOf(line)}: no known level reaches a chance of {threshold} at skill {skill}");
        }

        return null;
    }

    /// <summary>The first held Handy Healing Kit with uses left (its Structure; unknown counts as one use), as the hunt's recovery finds it.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="scratch">A reusable list.</param>
    /// <param name="guid">The kit.</param>
    /// <param name="uses">Its uses left.</param>
    public static bool FindKit(GameState state, List<uint> scratch, out uint guid, out int uses)
    {
        ArgumentNullException.ThrowIfNull(state);
        GearInventory.Collect(state, scratch);
        for (var i = 0; i < scratch.Count; i++)
        {
            if (state.Objects.TryGet(scratch[i], out var o) && o.Weenie.WeenieClassId == FighterKit.HealingKitWcid && (o.Weenie.Structure ?? 1) > 0)
            {
                guid = scratch[i];
                uses = o.Weenie.Structure ?? 1;
                return true;
            }
        }

        guid = 0;
        uses = 0;
        return false;
    }

    /// <summary>The first held Potion of Healing stack and how many potions are held in all.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="scratch">A reusable list.</param>
    /// <param name="guid">The first stack.</param>
    /// <param name="count">The potions held.</param>
    public static bool FindPotions(GameState state, List<uint> scratch, out uint guid, out int count) => FindStacks(state, scratch, FighterKit.PotionWcid, out guid, out count);

    /// <summary>The first held Mana Potion stack (the main pack or a side pack) and how many are held in all (M4 P5a C7).</summary>
    /// <param name="state">The read model.</param>
    /// <param name="scratch">A reusable list.</param>
    /// <param name="guid">The first stack.</param>
    /// <param name="count">The potions held.</param>
    public static bool FindManaPotions(GameState state, List<uint> scratch, out uint guid, out int count) => FindStacks(state, scratch, FighterKit.ManaPotionWcid, out guid, out count);

    /// <summary>The first held Stamina Potion stack (the main pack or a side pack) and how many are held in all (M4 P5a C7).</summary>
    /// <param name="state">The read model.</param>
    /// <param name="scratch">A reusable list.</param>
    /// <param name="guid">The first stack.</param>
    /// <param name="count">The potions held.</param>
    public static bool FindStaminaPotions(GameState state, List<uint> scratch, out uint guid, out int count) => FindStacks(state, scratch, FighterKit.StaminaPotionWcid, out guid, out count);

    private static bool FindStacks(GameState state, List<uint> scratch, uint wcid, out uint guid, out int count)
    {
        ArgumentNullException.ThrowIfNull(state);
        GearInventory.Collect(state, scratch);
        guid = 0;
        count = 0;
        for (var i = 0; i < scratch.Count; i++)
        {
            if (state.Objects.TryGet(scratch[i], out var o) && o.Weenie.WeenieClassId == wcid)
            {
                if (guid == 0)
                {
                    guid = scratch[i];
                }

                count += o.Weenie.StackSize ?? 1;
            }
        }

        return count > 0;
    }

    /// <summary>What one Stamina to Mana Self cast of <paramref name="info"/> would give now (<see cref="BuffStatMods.Transfer"/>; 0 when the row is unknown).</summary>
    public static uint TransferGain(SpellInfo info, uint stamina, uint manaMissing)
    {
        ArgumentNullException.ThrowIfNull(info);
        if (BuffStatMods.TransferOf(info.Id) is not { } row)
        {
            return 0;
        }

        BuffStatMods.Transfer(in row, stamina, manaMissing, out _, out var given);
        return given;
    }
}
