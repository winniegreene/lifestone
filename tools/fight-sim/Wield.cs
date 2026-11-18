using System.Globalization;
using ACE.Entity.Enum;
using ACE.Server.WorldObjects;

namespace Lifestone.FightSim;

/// <summary>
/// M6 16 E5e3: "what if she held that caster". The restored character swaps the caster in her hands for one she carries, in memory only, with ACE's own calls, so every command after it (her sheet, a matchup, the
/// ranking) is ACE's answer for her with the other caster: the unwield takes the old caster's spells off her and off it (Creature.TryDequipObject, Creature_Equipment.cs:378-404, and Creature.RemoveItemSpell for each
/// spell of its book, as TryDequipObjectWithBroadcasting does, :441-450 @95ef426d); the wield puts the new one in her hands (Creature.TryEquipObject, :315-337) and, when it has mana and she meets its activation
/// requirement, casts each of its spells (Creature.CreateItemSpell, Creature_Magic.cs:98-133, as Player.TryActivateSpells does, Player_Inventory.cs:324-365). Nothing is saved: no SaveChanges is ever called.
/// <para>Not checked: the wield requirement (ACE's check is in the move's handler, Player_Inventory.cs:2126-2229, which needs a world): the line printed names the requirement's numbers and hers, and the caller reads
/// them.</para>
/// </summary>
internal static class Wield
{
    /// <summary>Swaps the caster in <paramref name="her"/> hands for the one <paramref name="subject"/> names (a name, or its object id as 0x...), and returns what was done as one line.</summary>
    public static string Swap(Player her, string subject)
    {
        var casters = her.GetAllPossessions().OfType<Caster>().ToList();
        var byId = subject.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && uint.TryParse(subject.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ? id : 0;
        var found = byId != 0 ? casters.Where(c => c.Guid.Full == byId).ToList() : casters.Where(c => string.Equals(c.Name, subject, StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count != 1)
        {
            var list = string.Join("\n", casters.OrderBy(c => c.Name, StringComparer.Ordinal).Select(c => string.Create(CultureInfo.InvariantCulture, $"  0x{c.Guid.Full:X8}  {c.Name}{(c.Guid == her.GetEquippedWand()?.Guid ? "  (in her hands)" : string.Empty)}")));
            throw new ArgumentException($"--wield '{subject}' fits {found.Count} of the casters she carries; name one by its name or its id:\n{list}");
        }

        var pick = found[0];
        var held = her.GetEquippedWand();
        if (held != null && held.Guid == pick.Guid)
        {
            return $"--wield: she already holds {pick.Name}";
        }

        if (held != null)
        {
            if (!her.TryDequipObject(held.Guid, out var old, out _))
            {
                throw new InvalidOperationException($"ACE refused to unwield {held.Name}");
            }

            foreach (var spell in old.Biota.PropertiesSpellBook?.Keys.ToList() ?? [])
            {
                if (!old.HasProcSpell((uint)spell))
                {
                    her.RemoveItemSpell(old, (uint)spell, true);
                }
            }
        }

        if (!her.TryRemoveFromInventory(pick.Guid, out var item) || !her.TryEquipObject(item, EquipMask.Held))
        {
            throw new InvalidOperationException($"ACE refused to wield {pick.Name}");
        }

        var lit = "its spells are off: it holds no mana";
        if (item.ItemCurMana > 0)
        {
            var use = item.CheckUseRequirements(her);
            lit = "its spells are off: she does not meet its activation requirement";
            if (use.Success)
            {
                var cast = 0;
                foreach (var spell in item.Biota.PropertiesSpellBook?.Keys.ToList() ?? [])
                {
                    if (!item.HasProcSpell((uint)spell) && spell != item.SpellDID && her.CreateItemSpell(item, (uint)spell))
                    {
                        cast++;
                    }
                }

                lit = string.Create(CultureInfo.InvariantCulture, $"{cast} of its spells cast on the wield");
            }
        }

        var need = item.WieldRequirements == WieldRequirement.Invalid
            ? "no wield requirement"
            : string.Create(CultureInfo.InvariantCulture, $"its wield requirement is {item.WieldRequirements} {item.WieldSkillType} {item.WieldDifficulty}, not checked here");
        return string.Create(CultureInfo.InvariantCulture, $"--wield: AS IF she held {item.Name} (0x{item.Guid.Full:X8}) in place of {held?.Name ?? "nothing"}: in memory only, nothing saved; {lit}; {need}");
    }
}
