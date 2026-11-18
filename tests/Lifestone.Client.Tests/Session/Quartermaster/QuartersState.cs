using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quartermaster;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.Quartermaster;

/// <summary>
/// M6 item 16, slice E5e3: the state of the live defect (ladder67, 2026-11-17 23:55 to 2026-11-18 01:02 UTC), for the tests of the element source and of the caster score: the climber at level 82 with her two casters.
/// <para>What it is built from. Her numbers: <c>tools/fight-sim.ps1 her</c> of 2026-11-18 (level 82, War Magic 379, Mana Conversion 286). The two casters: the examine panels of the live log
/// (<c>retailui examine shown</c>, 01:02:22 for the wand; the sceptre's while wielded) and the simulator's sheet for each caster's own numbers (<c>her</c> and <c>her --wield "Minor Smoldering Atlan Wand"</c>: the sceptre
/// x1.03 on pierce, mana conversion 0.07, melee defence x1.1; the wand x1.12 on fire, 0.02, x1.08, resistance cleaving 1 on fire, tough to master). Assumed, and said here: her attributes (only the skills' totals are
/// hers), her book as every bolt line I to VI with the fire line's VII and the three caster auras at VI, each bolt's base mana (ten a level), and of each caster's spells only the ones the caster score reads.</para>
/// </summary>
internal static class QuartersState
{
    public const uint Fire = 0x10, Pierce = 0x2;
    public const uint HermeticLinkVI = 1480, DefenderVI = 1605, SpiritDrinkerVI = 3258, HermeticLinkV = 1479, DefenderV = 1604, MinorHermeticLink = 3199, MinorWarMagicAptitude = 2569;
    public static readonly uint[] Powers = [1, 50, 100, 150, 200, 250, 300];
    public static readonly string[] Numerals = ["I", "II", "III", "IV", "V", "VI", "VII"];
    private static uint _event = 9000;

    /// <summary>Every bolt line's seven levels as SpellTable rows (the powers are the SpellTable's for war spells of level I to VII).</summary>
    public static Dictionary<uint, SpellInfo> Spells()
    {
        var spells = new Dictionary<uint, SpellInfo>();
        foreach (var (element, ids) in WarSpellData.BoltLineIds)
        {
            for (var i = 0; i < FightScenario.Levels && i < ids.Count; i++)
            {
                spells[ids[i]] = WorldActionRig.Spell(ids[i], SpellType.Projectile) with { Name = $"{SpellPlan.BoltLineNames[element]} {Numerals[i]}", Power = Powers[i], BaseMana = (uint)(10 * (i + 1)) };
            }
        }

        return spells;
    }

    /// <summary>Her book: every line I to VI, the fire line's VII, the three caster auras at VI (see the class).</summary>
    public static ImmutableDictionary<uint, float> Book(int levels, bool fireSeven)
    {
        var book = ImmutableDictionary<uint, float>.Empty.Add(HermeticLinkVI, 2f).Add(DefenderVI, 2f).Add(SpiritDrinkerVI, 2f);
        foreach (var (element, ids) in WarSpellData.BoltLineIds)
        {
            for (var i = 0; i < ids.Count && (i < levels || (fireSeven && element == "fire" && i == 6)); i++)
            {
                book = book.Add(ids[i], 2f);
            }
        }

        return book;
    }

    /// <summary>A mage of <paramref name="level"/> whose War Magic reads <paramref name="war"/> and Mana Conversion 286 (Focus and Self 100: the skills' totals are the state, the split is assumed).</summary>
    public static VendorRig MageOf(int level, int war, ImmutableDictionary<uint, float> book)
    {
        var rig = new VendorRig(1000, new PlayerDescriptionMsg
        {
            IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerQualities.LevelProperty, level),
            Attributes = [new AttributeDesc(1, 0, 100, 0), new AttributeDesc(MageTemplate.Focus, 0, 100, 0), new AttributeDesc(MageTemplate.Self, 0, 100, 0)],
            Skills = [new SkillDesc(MageTemplate.WarMagic, (ushort)(war - 50), 1, 3u, 0, 0u, 0, 0), new SkillDesc(MageTemplate.ManaConversion, 253, 1, 2u, 0, 0u, 0, 0)], // (100 + 100) / 4 = 50; (100 + 100) / 6 = 33
            Spells = book,
        });
        rig.State.Self.Name = MageTemplate.ClimberName;
        return rig;
    }

    public static uint Caster(VendorRig rig, string name, bool wielded, Dictionary<uint, int> ints, Dictionary<uint, double> floats, uint[] spells, string? longDescription = null)
    {
        var guid = rig.NextGuid();
        rig.Arrive(guid, name, 2472, SelfEquipJudge.CasterType, 200, validLocations: SelfEquipJudge.Held);
        rig.Put(guid);
        if (wielded)
        {
            rig.R.Server((p, s) => Lifestone.Tests.Shared.Net.AceM3Writers.WieldObject(p, s, guid, (int)SelfEquipJudge.Held));
        }

        rig.Tick();
        rig.R.H.R.Reducer.OnIdentifyResponse(rig.R.H.R.Context, new GameEventHeader(VendorRig.Player, ++_event, 0x00C9), new IdentifyResponseMsg(
            guid,
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable | IdentifyResponseFlags.SpellBook | (longDescription is null ? 0 : IdentifyResponseFlags.StringStatsTable),
            true,
            ints.ToImmutableDictionary(),
            ImmutableDictionary<uint, long>.Empty,
            ImmutableDictionary<uint, bool>.Empty,
            floats.ToImmutableDictionary(),
            longDescription is null ? ImmutableDictionary<uint, string>.Empty : ImmutableDictionary<uint, string>.Empty.Add(AppraisalIds.LongDescription, longDescription),
            ImmutableDictionary<uint, uint>.Empty,
            [.. spells],
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null));
        return guid;
    }

    /// <summary>The wield requirement both casters state: base War Magic 290 (ACE's RawSkill 2 on skill 34).</summary>
    public static Dictionary<uint, int> Wield290(uint element) => new()
    {
        [SelfEquipJudge.DamageTypeInt] = (int)element, [AppraisalIds.WieldType] = 2, [AppraisalIds.WieldSkill] = (int)MageTemplate.WarMagic, [AppraisalIds.WieldDifficulty] = 290,
    };

    public static uint Sceptre(VendorRig rig, bool wielded = true) => Caster(rig, "Piercing Sceptre", wielded, Wield290(Pierce),
        new() { [AppraisalIds.ElementalDamageMod] = 1.03, [SelfEquipJudge.ManaConversionModFloat] = 0.07, [GearScore.WeaponDefenseFloat] = 1.10 }, [HermeticLinkV, DefenderV, MinorHermeticLink], "Piercing Sceptre of Flame Bolt");

    public static uint AtlanWand(VendorRig rig, bool wielded = false)
    {
        var ints = Wield290(Fire);
        ints[(uint)AppraisalIds.ResistanceModifierType] = (int)Fire;
        return Caster(rig, "Minor Smoldering Atlan Wand", wielded, ints,
            new() { [AppraisalIds.ElementalDamageMod] = 1.12, [SelfEquipJudge.ManaConversionModFloat] = 0.02, [GearScore.WeaponDefenseFloat] = 1.08, [AppraisalIds.ResistanceModifier] = 1.0 },
            [DefenderVI, SpiritDrinkerVI, MinorWarMagicAptitude], GearScore.ToughToMaster);
    }

    /// <summary>M6 16 Q4: a plain caster (the simulator's sheet of her Orb: no element, no wield requirement, mana conversion 0.06, melee defence x1.08, masterable), wielded.</summary>
    public static uint Orb(VendorRig rig)
    {
        return Caster(rig, "Orb", true, [], new() { [SelfEquipJudge.ManaConversionModFloat] = 0.06, [GearScore.WeaponDefenseFloat] = 1.08 }, []);
    }

    public static List<GearCandidate> PackCasters(VendorRig rig) =>
        [.. PackLayout.Items(rig.State).Where(i => (i.ItemType & SelfEquipJudge.CasterType) != 0).Select(i => SelfEquipJudge.CandidateOf(rig.State, rig.State.Objects.TryGet(i.Guid, out var o) ? o : throw new InvalidOperationException("no such caster")))];
}
