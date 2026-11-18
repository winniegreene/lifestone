using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Session.Mage;

/// <summary>Where one spell of a worn piece stands on her (<see cref="GearLight.Read"/>).</summary>
public enum SpellLightKind
{
    /// <summary>Her registry holds it, cast by the piece, and nothing stronger of its category stands: it is the top layer.</summary>
    Lit,

    /// <summary>Her registry holds it, cast by the piece, under a stronger spell of its category: lit and surpassed (ACE writes a surpassed spell to the next layer and keeps it).</summary>
    LitSurpassed,

    /// <summary>Her registry does not hold it and a stronger spell of its category stands: lighting it would put it under that one, so nothing would change on her while that one stands.</summary>
    AbsentSurpassed,

    /// <summary>Her registry does not hold it and nothing stronger of its category stands: lighting it would show on her.</summary>
    Absent,

    /// <summary>The spell is cast on the piece itself (an item enchantment such as Impenetrability or a bane): her registry never holds it, so nothing here reads it.</summary>
    OnThePiece,

    /// <summary>The SpellTable does not hold the spell: its school, category and power are not read, and nothing is decided on it.</summary>
    Unread,
}

/// <summary>One spell of a worn piece as it stands on her.</summary>
/// <param name="Spell">The spell id.</param>
/// <param name="Name">Its name (the SpellTable's), or <c>spell N</c>.</param>
/// <param name="Kind">Where it stands.</param>
/// <param name="Over">The name of the stronger spell of its category that stands on her, or null.</param>
public readonly record struct SpellLight(uint Spell, string Name, SpellLightKind Kind, string? Over);

/// <summary>A worn piece's spells as they stand on her (<see cref="GearLight.Read"/>).</summary>
/// <param name="Lit">True when her registry holds an enchantment cast by the piece (<see cref="WearScore.LitOn"/>, surpassed ones too): ACE activated its spells at its wield.</param>
/// <param name="Spells">Each spell of the piece.</param>
public sealed record PieceLight(bool Lit, IReadOnlyList<SpellLight> Spells)
{
    /// <summary>True when at least one spell of the piece is cast on her (so her registry can show whether the piece is lit).</summary>
    public bool CastsOnHer => Spells.Any(s => s.Kind is SpellLightKind.Lit or SpellLightKind.LitSurpassed or SpellLightKind.AbsentSurpassed or SpellLightKind.Absent);

    /// <summary>The spells an off-and-on would show on her: absent, and under nothing stronger.</summary>
    public IEnumerable<SpellLight> CouldLight => Spells.Where(s => s.Kind == SpellLightKind.Absent);

    /// <summary>The spells cast on her that are absent only under a stronger one.</summary>
    public IEnumerable<SpellLight> Surpassed => Spells.Where(s => s.Kind == SpellLightKind.AbsentSurpassed);

    /// <summary>
    /// The check after a re-wield, and the question "is there anything left to light": true when the piece is lit, or when every spell it casts on her stands under a stronger one of hers (nothing an off-and-on could
    /// show). False for a piece that casts nothing on her: nothing is read, so nothing is called done.
    /// </summary>
    public bool Done => Lit || (CastsOnHer && !CouldLight.Any());

    /// <summary>"Armor Self VI over Armor Self V, ...": each surpassed spell with what stands over it, for the log.</summary>
    public string SurpassedBy() => string.Join(", ", Spells.Where(s => s.Over is not null).Select(s => s.Over + " over its " + s.Name));
}

/// <summary>What the re-wield's check before decided of one worn piece (<see cref="GearLight.Relight"/>).</summary>
public enum RelightKind
{
    /// <summary>Not a piece the re-wield reads: a caster, no appraisal, no ItemDifficulty, or no spell cast on her.</summary>
    NotAsked,

    /// <summary>Its spells are on her (the registry holds one cast by it, on top or surpassed): left on.</summary>
    Lit,

    /// <summary>ACE would not activate its spells at a wield now (an activation requirement unmet, no item mana): left on, an off-and-on lights nothing.</summary>
    CannotLight,

    /// <summary>Every spell it casts on her is under a stronger one of hers: left on, an off-and-on shows nothing.</summary>
    Surpassed,

    /// <summary>ACE would refuse to wield it back (a wield requirement she does not meet now): left on, never taken off.</summary>
    CannotWieldBack,

    /// <summary>ACE would activate its spells now and at least one would show on her: off and on again.</summary>
    Relight,
}

/// <summary>The re-wield's check before on one worn piece.</summary>
/// <param name="Kind">The case.</param>
/// <param name="Why">The reason with its names and numbers, for the log.</param>
/// <param name="Light">The piece's spells as read, or null when the piece was not asked.</param>
public sealed record RelightVerdict(RelightKind Kind, string Why, PieceLight? Light);

/// <summary>
/// M6 16 E5e4: the one read of "are this worn piece's spells on her", for the re-wield (the fight's loot gear step, <c>FightScenario.LootGear.cs</c>) and the wear pick (<see cref="GearContext.LitBy"/> is
/// <see cref="WearScore.LitOn"/>, which <see cref="Read"/> asks too: one function).
/// </summary>
/// <remarks>
/// <para><b>What ACE does</b> (@95ef426d). At a wield, a piece with item mana above 0 has its activation requirements checked once (<c>TryEquipObjectWithNetworking</c>, Player_Inventory.cs:313-315;
/// <c>TryActivateSpells</c>, :324-365; <c>CheckUseRequirements</c>, WorldObject_Use.cs:191-324). When they are met every spell of the piece is cast: a Creature or Life spell on the wearer, an Item spell on the piece
/// itself unless its category is one of the six aura categories (<c>CreateItemSpell</c>, Creature_Magic.cs:98-134; <c>Spell.HasItemCategory</c>, Spell.cs:337-353). Each cast adds an entry to the target's registry with
/// the piece as its caster (<c>EnchantmentManager.Add</c>, EnchantmentManager.cs:134-203): a spell weaker than one standing in its category is still written, to the next layer (:157-175, where the code's own comment says so; <c>AddEnchantmentResult.BuildStack</c>, AddEnchantmentResult.cs:62-130), and the player is sent it (<c>CreateEnchantment</c>, WorldObject_Magic.cs:451). Only the top
/// layer of a category acts on her: the highest PowerLevel (<c>GetEnchantmentsTopLayer</c>, PropertiesEnchantmentRegistryExtensions.cs:141-163).</para>
/// <para><b>So</b> a piece whose spells ACE activated is in her registry by its guid whether her own buffs surpass its spells or not (the client keeps every (spell, layer) entry: <see cref="EnchantmentRules"/>), and
/// <see cref="WearScore.LitOn"/> reads it lit. A piece with no entry was not activated at its wield. An off-and-on can show something only when ACE would activate it now (<see cref="WearScore.SpellsOff"/>) and at least
/// one spell it casts on her has nothing stronger standing in its category; a spell that would only go under a stronger one of hers changes nothing while that one stands.</para>
/// </remarks>
public static class GearLight
{
    /// <summary>ACE's SpellCategory values of the item spells that are cast on the wearer as auras (Spell.cs:337-353; SpellCategory.cs:157-163, 200, 699 @95ef426d).</summary>
    private const uint AttackModRaising = 152, DamageRaising = 154, DefenseModRaising = 156, WeaponTimeRaising = 158, ManaConversionModRaising = 195, SpellDamageRaising = 695;

    /// <summary>True when ACE casts <paramref name="spell"/> of a worn piece on the wearer (Creature_Magic.cs:113-131 @95ef426d): a Creature or Life spell, or an Item spell of an aura category.</summary>
    /// <param name="spell">The spell's row.</param>
    public static bool CastOnWearer(SpellInfo spell)
    {
        ArgumentNullException.ThrowIfNull(spell);
        return spell.School is MagicSchool.CreatureEnchantment or MagicSchool.LifeMagic
            || (spell.School == MagicSchool.ItemEnchantment && spell.Category is AttackModRaising or DamageRaising or DefenseModRaising or WeaponTimeRaising or ManaConversionModRaising or SpellDamageRaising);
    }

    /// <summary>Reads each spell of the worn piece <paramref name="guid"/> against her registry (see the class remarks).</summary>
    /// <param name="qualities">Her qualities (the registry).</param>
    /// <param name="guid">The worn piece.</param>
    /// <param name="appraisal">Its appraisal (its spells).</param>
    /// <param name="spells">The SpellTable's row of a spell id, or null for one it does not hold.</param>
    public static PieceLight Read(PlayerQualities qualities, uint guid, Appraisal appraisal, Func<uint, SpellInfo?> spells)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        ArgumentNullException.ThrowIfNull(appraisal);
        ArgumentNullException.ThrowIfNull(spells);
        var active = qualities.ActiveEnchantments;
        var read = new List<SpellLight>(appraisal.Spells.Length);
        foreach (var id in appraisal.Spells)
        {
            if (spells(id) is not { } row)
            {
                read.Add(new SpellLight(id, string.Create(CultureInfo.InvariantCulture, $"spell {id}"), SpellLightKind.Unread, null));
                continue;
            }

            if (!CastOnWearer(row))
            {
                read.Add(new SpellLight(id, row.Name, SpellLightKind.OnThePiece, null));
                continue;
            }

            var held = false;
            EnchantmentDesc? over = null;
            if (!active.IsDefaultOrEmpty)
            {
                foreach (var e in active)
                {
                    var d = e.Enchantment;
                    if (d.Caster == guid && d.SpellId == id)
                    {
                        held = true;
                    }
                    else if (d.SpellCategory == row.Category && d.Caster != guid && d.PowerLevel > row.Power && (over is not { } best || d.PowerLevel > best.PowerLevel))
                    {
                        over = d; // ACE's surpassed: a standing spell of the category with more power (AddEnchantmentResult.cs:119-123)
                    }
                }
            }

            var overName = over is { } o ? spells(o.SpellId)?.Name ?? string.Create(CultureInfo.InvariantCulture, $"spell {o.SpellId}") : null;
            var kind = (held, over is not null) switch
            {
                (true, false) => SpellLightKind.Lit,
                (true, true) => SpellLightKind.LitSurpassed,
                (false, true) => SpellLightKind.AbsentSurpassed,
                _ => SpellLightKind.Absent,
            };
            read.Add(new SpellLight(id, row.Name, kind, overName));
        }

        return new PieceLight(WearScore.LitOn(qualities, guid), read);
    }

    /// <summary>
    /// The re-wield's check before on one worn piece (the brief's rule, in ACE's terms): a piece is taken off only when ACE would activate its spells at a wield now and at least one of them would show on her. Lit:
    /// left on. Not lit and ACE would refuse the activation, or its mana is out: left on (<see cref="RelightKind.CannotLight"/>, with ACE's reason). Not lit and every spell it casts on her is under a stronger one of
    /// hers: left on (<see cref="RelightKind.Surpassed"/>). One she could not wield back is never taken off (<see cref="RelightKind.CannotWieldBack"/>).
    /// </summary>
    /// <param name="qualities">Her qualities (the registry).</param>
    /// <param name="piece">The worn piece.</param>
    /// <param name="skills">Her numbers now.</param>
    /// <param name="spells">The SpellTable's row of a spell id, or null.</param>
    public static RelightVerdict Relight(PlayerQualities qualities, WornPiece piece, IGearSkills skills, Func<uint, SpellInfo?> spells)
    {
        ArgumentNullException.ThrowIfNull(qualities);
        ArgumentNullException.ThrowIfNull(piece);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(spells);
        if ((piece.ItemType & SelfEquipJudge.CasterType) != 0)
        {
            return new(RelightKind.NotAsked, "a caster: the caster pick's", null);
        }

        if (piece.Appraisal is not { IsNotFound: false } a)
        {
            return new(RelightKind.NotAsked, "no appraisal of it is held", null);
        }

        if (a.Int(AppraisalIds.ArcaneLore) is not > 0)
        {
            return new(RelightKind.NotAsked, "it has no ItemDifficulty: her Arcane Lore never kept its spells off", null);
        }

        var light = Read(qualities, piece.Guid, a, spells);
        if (!light.CastsOnHer)
        {
            return new(RelightKind.NotAsked, "none of its spells is cast on her: her registry cannot show whether it is lit", light);
        }

        if (light.Lit)
        {
            var under = light.SurpassedBy();
            return new(RelightKind.Lit, under.Length == 0 ? "its spells are on her" : "its spells are on her, surpassed by her own " + under, light);
        }

        if (WearScore.SpellsOff(a, skills) is { } off)
        {
            return new(RelightKind.CannotLight, off, light);
        }

        if (SelfEquipJudge.WieldUnmet(a, skills) is { } refused)
        {
            return new(RelightKind.CannotWieldBack, refused, light); // ACE checks the wield requirements at every wield (CheckWieldRequirement, Player_Inventory.cs:2126-2229): off, it would stay off
        }

        var open = light.CouldLight.Select(s => s.Name).ToList();
        return open.Count == 0
            ? new(RelightKind.Surpassed, light.SurpassedBy(), light)
            : new(RelightKind.Relight, "ACE would activate it now and " + string.Join(", ", open) + " would show on her" + (light.Surpassed.Any() ? " (" + light.SurpassedBy() + ")" : string.Empty), light);
    }
}
