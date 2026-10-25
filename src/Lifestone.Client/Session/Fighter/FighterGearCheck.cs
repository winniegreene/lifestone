using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Fighter;

/// <summary>What <see cref="FighterGearCheck.Read"/> found of a fighter's magic gear (M4 P5a C6).</summary>
/// <param name="Worn">The pieces worn or wielded (the Archer's bow, kept in the pack, counts when it is held).</param>
/// <param name="Total">The pieces of the set.</param>
/// <param name="SpellsActive">The wearer spells of the worn pieces that are active (in the enchantment registry, or surpassed by a stronger spell of their category).</param>
/// <param name="SpellsWanted">The distinct wearer spells of the worn pieces; -1 when no spell table was given.</param>
/// <param name="Silent">The worn pieces with wearer spells of which none is active: out of mana, or never activated (<see cref="GearRecharge"/> charges such a piece and wears it again).</param>
/// <param name="Short">The pieces not worn (or held, for the bow).</param>
public sealed record GearReading(int Worn, int Total, int SpellsActive, int SpellsWanted, IReadOnlyList<GearPiece> Silent, IReadOnlyList<GearPiece> Short)
{
    /// <summary>True when every piece is in place.</summary>
    public bool Complete => Worn == Total;

    /// <summary>The verdict's <c>gear_magic</c>: <c>on</c> when every wearer spell of the worn gear is active, <c>off</c> when some is not, <c>n/a</c> without a spell table or with no wearer spell to read.</summary>
    public string Magic => SpellsWanted <= 0 ? "n/a" : SpellsActive == SpellsWanted ? "on" : "off";

    /// <summary>The verdict's <c>spells_active</c> (report-only in its first gate).</summary>
    public string SpellsField => SpellsWanted < 0 ? "n/a" : $"{SpellsActive}/{SpellsWanted}";
}

/// <summary>
/// Reads a fighter's magic gear from the read model (M4 P5a C6): which pieces are worn, which spells are active, and the Arcane Lore the pieces' difficulties are compared with. Pure over the state: no action, no log.
/// </summary>
public static class FighterGearCheck
{
    /// <summary>Arcane Lore's skill id (ACE <c>Skill.ArcaneLore</c>, 14).</summary>
    public const uint ArcaneLoreSkillId = 14;

    /// <summary>Arcane Lore's formula (the SkillTable's): Focus / 3 (the SkillTable's formula has no second attribute). Pinned by <c>FighterGearDatTests</c>.</summary>
    public static readonly Lifestone.Data.CharacterCreation.SkillTableFormula ArcaneLoreFormula = new(0, 1, 0, 3, FighterTemplate.Focus, 0);

    /// <summary>The character's current Arcane Lore (attributes with their enchantments, the vitae and the skill's enchantments), or null when it is not trained or not known.</summary>
    /// <param name="q">The player's qualities.</param>
    public static int? ArcaneLore(PlayerQualities q)
    {
        ArgumentNullException.ThrowIfNull(q);
        if (!q.IsKnown || !q.TryGetSkill(ArcaneLoreSkillId, out var skill) || skill.AdvancementClass < SkillRules.TrainedClass)
        {
            return null;
        }

        return SkillRules.TryCurrent(q, ArcaneLoreSkillId, in ArcaneLoreFormula, SkillRules.TrainedClass, out var current) ? (int)Math.Min(current, int.MaxValue) : null;
    }

    /// <summary>
    /// The pieces whose ItemDifficulty is above <paramref name="arcaneLore"/> (the "named refusal": ACE equips such a piece but leaves its spells off, <c>WorldObject_Use.cs:205-209</c> at ACE 95ef426d). A null Arcane Lore (untrained or unknown) is 0.
    /// </summary>
    /// <param name="set">The set.</param>
    /// <param name="arcaneLore">The character's current Arcane Lore, or null.</param>
    public static IReadOnlyList<GearPiece> Refused(FighterGearSet set, int? arcaneLore)
    {
        ArgumentNullException.ThrowIfNull(set);
        return [.. set.Pieces.Where(p => p.ItemDifficulty > (arcaneLore ?? 0))];
    }

    /// <summary>Reads the gear.</summary>
    /// <param name="state">The read model.</param>
    /// <param name="set">The fighter's set.</param>
    /// <param name="spells">The dat's spells (for the school and the stacking category), or null.</param>
    /// <param name="scratch">A list for the held guids (cleared first).</param>
    public static GearReading Read(GameState state, FighterGearSet set, IReadOnlyDictionary<uint, SpellInfo>? spells, List<uint> scratch)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(set);
        ArgumentNullException.ThrowIfNull(scratch);
        var inventory = state.Self.Inventory;
        var enchantments = state.Self.Qualities.Enchantments;
        var worn = 0;
        var short_ = new List<GearPiece>();
        var silent = new List<GearPiece>();
        var wanted = new HashSet<uint>();
        var active = new HashSet<uint>();
        GearInventory.Collect(state, scratch);
        foreach (var piece in set.Pieces)
        {
            var held = false;
            var onBody = false;
            for (var i = 0; i < scratch.Count; i++)
            {
                if (!state.Objects.TryGet(scratch[i], out var o) || o.Weenie.WeenieClassId != piece.Wcid)
                {
                    continue;
                }

                held = true;
                if (inventory.TryGetEquipped(scratch[i], out var e) && e.Location != 0 && (e.Location & ~piece.Locations) == 0)
                {
                    onBody = true;
                    break;
                }
            }

            // The Archer's bow is kept in the pack (the hunt's swap wields it): held counts as in place.
            if (onBody || (held && !piece.IsMelee && piece.Kind == GearPieceKind.Weapon))
            {
                worn++;
            }
            else
            {
                short_.Add(piece);
            }

            if (!onBody || spells is null)
            {
                continue;
            }

            var any = false;
            var anyActive = false;
            foreach (var spell in FighterGearSet.WearerSpells(piece, id => spells.TryGetValue(id, out var info) ? info.School : null))
            {
                any = true;
                wanted.Add(spell.Id);
                if (IsActive(enchantments, spells[spell.Id]))
                {
                    active.Add(spell.Id);
                    anyActive = true;
                }
            }

            if (any && !anyActive)
            {
                silent.Add(piece);
            }
        }

        return new GearReading(worn, set.Pieces.Count, active.Count, spells is null ? -1 : wanted.Count, silent, short_);
    }

    private static bool IsActive(System.Collections.Immutable.ImmutableArray<Lifestone.Net.Messages.EnchantmentDesc> enchantments, SpellInfo info)
    {
        foreach (var e in enchantments)
        {
            // The spell itself, or a stronger spell of its category (ACE keeps only the stronger one on the wearer).
            if (e.SpellId == info.Id || (e.SpellCategory == info.Category && e.PowerLevel >= info.Power))
            {
                return true;
            }
        }

        return false;
    }
}
