using System.Globalization;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Session.Fighter;

/// <summary>
/// The spell components the fighters' kit carries (M4 P5a fix F-abil, the live round 1: Harm Self refused with <c>YouDontHaveAllTheComponents</c>, 0x0400, because the Brawler's and the Archer's kit had none; the mage's
/// <see cref="GearKit"/> carries them). One <see cref="FighterRole.Component"/> line per component of the spells a <see cref="FighterProfile"/> casts, in the formula the account casts, as
/// <see cref="GearKit.Build"/> derives the mage's. Built once for a profile and an account by <see cref="For"/>; <see cref="FighterKit.WithComponents"/> adds the lines to the template's kit.
/// </summary>
/// <remarks>
/// <para><b>Which spells.</b> Of every line of the profile (the twelve buff lines, Stamina to Mana Self, Revitalize Self, Harm Self, Heal Self, Lifestone Recall), the spells whose cast chance reaches <see cref="CastChance"/>
/// (<see cref="GearCasts.Chance"/>, the buff cycle's and the keeper's level rule, 0.9) at the school's planned skill plus <see cref="SkillAllowance"/> (Lifestone's own: the Focus and Willpower buffs and the spread's later ranks raise the
/// skill a little). The planned skills are the template's: Creature Enchantment 183, Life Magic 183, Item Enchantment 161 (<see cref="FighterTemplate.MagicLevel"/>; <c>FighterMagicDatTests</c> pins them). <b>Since P5a round 3</b> the kit
/// covers every level the fighter learns (<see cref="FighterProfile.SpellsToLearn"/>) of the four utility lines (Stamina to Mana Self, Revitalize Self, Harm Self, Heal Self), not only those the planned skill reaches: their
/// pickers (the harm-heal, the keeper, the buff cycle's mana loop) choose by the 0.9 rule at the live skill, which D41's gear and the buffs raise far past the plan (the round's Brawler cast Creature Enchantment at 225 against a plan of 184,
/// chose Harm Self V and Stamina to Mana Self VI, and ACE refused both, 0x0400). The buff lines stay at the plan's levels (their cycle cast IV there and passed). The burn is sized by the worst covered spell per cast.</para>
/// <para><b>Which formula.</b> ACE's player formula (<c>SpellFormula.GetPlayerFormula</c>, Entity/SpellFormula.cs:224 at ACE 95ef426d) rewrites taper slots 1, 3 and 6 from the hash of the stored (lower-case) account name
/// (<see cref="SpellFormulaRules.CustomizeForAccount"/>), so the kit is built for the account name as the server stores it. No focus item is held (the fighters carry none and have no infusion), so the foci formula (<c>HasFoci</c>,
/// Player_Spells.cs:458-488) never applies and is not carried.</para>
/// <para><b>The stacks.</b> The burn rule is <see cref="GearKit"/>'s (<c>Spell.TryBurnComponents</c>, Entity/Spell.cs:157-189, at ACE 95ef426d): each slot of the formula burns with the chance <c>ComponentLoss × CDM × min(1, Power / skill)</c>, the
/// skill factor taken as 1 here (the worst case), and a cast needs as many of a component as the formula has slots of it (Player_Magic.cs:1234-1245). The kit is sized for one gate: a line's burn is its worst allowed spell per cast, times
/// the casts the gate makes of it: a buff line once per cycle for <see cref="BuffCycles"/> cycles; the two mana lines <see cref="ManaCastsPerLine"/> times in each of the <see cref="BuffCycles"/> cycles and in each of the
/// <see cref="HarmHealLegs"/> harm-heal legs (the harm phase tops its mana up through Stamina to Mana Self); Harm Self <see cref="HarmCastsPerLeg"/> and Heal Self <see cref="HealCastsPerLeg"/> times in each harm-heal leg; Lifestone Recall
/// <see cref="RecallCasts"/> times (a recall and the retries its fizzles take, and the next gate's). A component's stack is <see cref="Safety"/> times that, rounded up, at least <see cref="MinimumStack"/> and the most slots of it any one spell
/// has, and at most the weenie's maximum stack (100). The build grants only the missing count (<see cref="FighterBuildScenario"/>), so a rerun tops the stacks up to these numbers after casts have burned some.</para>
/// <para><b>The burden.</b> A component weighs 4, the Prismatic Taper 6 (the same SELECT as <see cref="GearKit.ComponentBurden"/>). The tests print the kit's burden against <see cref="FighterKit.Capacity"/> at Strength <see cref="FighterKit.CapacityStrength"/>.</para>
/// </remarks>
public static class FighterComponents
{
    /// <summary>The cast chance the level rule asks for (<see cref="BuffOptions.DefaultCastChance"/>, 0.9).</summary>
    public const double CastChance = BuffOptions.DefaultCastChance;

    /// <summary>The planned Creature Enchantment skill at <see cref="FighterTemplate.MagicLevel"/>, the lower of the two fighters' (M4 P5a C5: their creation attributes differ, so their plans do; <c>FighterComponentsDatTests</c> pins it; unbuffed).</summary>
    public const uint CreatureEnchantmentSkill = 183;

    /// <summary>The planned Item Enchantment skill, the lower of the two fighters' (unbuffed).</summary>
    public const uint ItemEnchantmentSkill = 179;

    /// <summary>The planned Life Magic skill, the lower of the two fighters' (unbuffed).</summary>
    public const uint LifeMagicSkill = 183;

    /// <summary>The skill points added to the planned skill when the allowed levels are chosen. Not retail: Lifestone's own (the Focus Self and Willpower Self buffs raise the schools' skills by a few points).</summary>
    public const uint SkillAllowance = 20;

    /// <summary>The buff cycles one gate makes (the first buff, a re-buff, a spare), as <see cref="GearKit.BuffCycles"/>.</summary>
    public const int BuffCycles = GearKit.BuffCycles;

    /// <summary>The margin over the expected burns (a fizzled cast burns too), as <see cref="GearKit.Safety"/>.</summary>
    public const int Safety = GearKit.Safety;

    /// <summary>The casts of each mana line in one pass or one harm-heal leg: the fighter's pass is about 5 transfers and 7 Revitalize casts (C4's model with the real costs), the harm phase a few more.</summary>
    public const int ManaCastsPerLine = 8;

    /// <summary>The harm-heal legs one gate runs (a fighter's leg, a rerun, a spare).</summary>
    public const int HarmHealLegs = 3;

    /// <summary>The Harm Self casts one harm-heal leg makes: the health goes from full to half at 16 to 30 points a cast of Harm Self IV, 10 to 18 of III; 12 covers a 300-point pool with the retries.</summary>
    public const int HarmCastsPerLeg = 12;

    /// <summary>The Heal Self casts one harm-heal leg makes (the keeper's rests, the leg's own).</summary>
    public const int HealCastsPerLeg = 6;

    /// <summary>The Lifestone Recall casts a gate makes: a recall per journey plus the fizzles <see cref="RecallHome"/> retries (capped).</summary>
    public const int RecallCasts = 6;

    /// <summary>The least a component is given.</summary>
    public const int MinimumStack = 5;

    /// <summary>The planned skill of <paramref name="school"/> (0 for any school the fighters do not train).</summary>
    public static uint PlannedSkill(MagicSchool school) => school switch
    {
        MagicSchool.CreatureEnchantment => CreatureEnchantmentSkill,
        MagicSchool.ItemEnchantment => ItemEnchantmentSkill,
        MagicSchool.LifeMagic => LifeMagicSkill,
        _ => 0,
    };

    /// <summary>The spells of <paramref name="line"/> whose cast chance at the school's planned skill plus <see cref="SkillAllowance"/> reaches <see cref="CastChance"/>: what the unbuffed plan casts (the kit covers more, <see cref="CoveredSpells"/>).</summary>
    /// <param name="line">The profile line.</param>
    public static IReadOnlyList<ProfileSpell> AllowedSpells(ProfileLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var skill = PlannedSkill(line.School) + SkillAllowance;
        return [.. line.Spells.Where(s => GearCasts.Chance(skill, s.Power) >= CastChance)];
    }

    /// <summary>
    /// The spells of <paramref name="line"/> the kit covers (P5a round 3): every level the fighter learns (<paramref name="known"/>, the profile's <see cref="FighterProfile.SpellsToLearn"/>), because the harm-heal, the keeper and
    /// the buff cycle pick a level by the 0.9 chance rule at the live skill, which the gear and the buffs raise past the plan; a level the fighter does not learn is never picked.
    /// </summary>
    /// <param name="line">The profile line.</param>
    /// <param name="known">The spell ids the fighter learns.</param>
    public static IReadOnlyList<ProfileSpell> CoveredSpells(ProfileLine line, IReadOnlySet<uint> known)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(known);
        return [.. line.Spells.Where(s => known.Contains(s.Id))];
    }

    /// <summary>The casts a gate makes of <paramref name="line"/> (see the remarks of the type).</summary>
    /// <param name="line">The profile line.</param>
    public static int CastsPerGate(ProfileLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Role switch
        {
            _ when line.IsBuff => BuffCycles,
            _ when line.IsManaLine => (BuffCycles + HarmHealLegs) * ManaCastsPerLine,
            ProfileRole.HarmSelf => HarmHealLegs * HarmCastsPerLeg,
            ProfileRole.HealSelf => HarmHealLegs * HealCastsPerLeg,
            ProfileRole.Travel => RecallCasts,
            _ => 0,
        };
    }

    /// <summary>
    /// The component lines of <paramref name="profile"/>'s spells as the account <paramref name="accountName"/> casts them, ascending by component id.
    /// </summary>
    /// <param name="profile">The fighter's profile.</param>
    /// <param name="spells">The SpellTable's spells (<see cref="SpellTableData.Spells"/>).</param>
    /// <param name="accountName">The account's name as the server stores it; hashed lower-cased, never stored or logged.</param>
    /// <exception cref="KeyNotFoundException">A profile spell is not in <paramref name="spells"/>.</exception>
    /// <exception cref="InvalidOperationException">A formula holds a component the pinned map (<see cref="ComponentWeenies"/>) does not.</exception>
    public static IReadOnlyList<FighterItem> For(FighterProfile profile, IReadOnlyDictionary<uint, SpellInfo> spells, string accountName)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(spells);
        ArgumentException.ThrowIfNullOrEmpty(accountName);
        var account = accountName.ToLowerInvariant();
        var known = new HashSet<uint>(profile.SpellsToLearn);

        var burn = new Dictionary<uint, double>(); // the expected burns of a gate, per component id
        var perSpell = new Dictionary<uint, int>(); // the most slots of a component any one allowed spell has
        var counts = new Dictionary<uint, int>();
        foreach (var line in profile.Lines)
        {
            var casts = CastsPerGate(line);
            if (casts == 0)
            {
                continue;
            }

            var lineBurn = new Dictionary<uint, double>();
            foreach (var profileSpell in line.IsBuff ? AllowedSpells(line) : CoveredSpells(line, known))
            {
                var info = spells.TryGetValue(profileSpell.Id, out var found)
                    ? found
                    : throw new KeyNotFoundException($"profile spell {profileSpell.Id} ({profileSpell.Name}) is not in the SpellTable");
                counts.Clear();
                foreach (var id in SpellFormulaRules.CustomizeForAccount(info.Formula, info.FormulaVersion, account))
                {
                    if (id == 0)
                    {
                        continue;
                    }

                    if (!ComponentWeenies.TryGetByComponent(id, out _))
                    {
                        throw new InvalidOperationException($"spell {profileSpell.Id} ({profileSpell.Name}) needs component {id}, which the component map does not hold");
                    }

                    counts[id] = counts.GetValueOrDefault(id) + 1;
                }

                foreach (var (id, n) in counts)
                {
                    ComponentWeenies.TryGetByComponent(id, out var weenie);
                    lineBurn[id] = Math.Max(lineBurn.GetValueOrDefault(id), n * info.ComponentLoss * (double)weenie.Cdm);
                    perSpell[id] = Math.Max(perSpell.GetValueOrDefault(id), n);
                }
            }

            foreach (var (id, perCast) in lineBurn)
            {
                burn[id] = burn.GetValueOrDefault(id) + (perCast * casts);
            }
        }

        var lines = new List<FighterItem>();
        foreach (var id in perSpell.Keys.Order())
        {
            ComponentWeenies.TryGetByComponent(id, out var weenie);
            var wanted = Math.Max(Math.Max(MinimumStack, perSpell[id]), (int)Math.Ceiling(Safety * burn.GetValueOrDefault(id)));
            var item = new GearItem(
                weenie.Wcid, weenie.Name, GearKind.Component, GearKit.WeenieTypeSpellComponent, GearKit.ItemTypeSpellComponents, EquipMask.None, Math.Min(wanted, weenie.MaxStack), weenie.MaxStack,
                weenie.Wcid == GearKit.PrismaticTaperWcid ? GearKit.PrismaticTaperBurden : GearKit.ComponentBurden);
            lines.Add(new FighterItem(item, FighterRole.Component, 0, false));
        }

        return lines;
    }

    /// <summary>One line of the numbers a test or a handoff prints: how many component lines, the pieces and their burden.</summary>
    /// <param name="components">The lines <see cref="For"/> returned.</param>
    public static string Describe(IReadOnlyList<FighterItem> components)
    {
        ArgumentNullException.ThrowIfNull(components);
        return string.Create(CultureInfo.InvariantCulture, $"components={components.Count} pieces={components.Sum(c => c.Item.Stack)} burden={components.Sum(c => c.Item.Burden)}");
    }
}
