using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.2 s2: the appraisal model over GameEvent 0x00C9 as ACE writes it (<see cref="AceVendorWriters.IdentifyResponse"/>, independent of Lifestone.Net's codec), through the real dispatcher into the reducer: every table, the typed
/// readers over the real property ids, a success-false answer (the tables kept, no profiles), the spell book's two kinds, the latest answer per guid and the cache bound.
/// </summary>
public sealed class AppraisalTests
{
    private const uint Mace = 0x80000A01;
    private const uint Kit = 0x80000A02;
    private const uint Bow = 0x80000A03;
    private const uint Npc = 0x7A9B4027;

    private static void Answer(WorldActionRig r, uint guid, AceAppraisal info) => r.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, guid, info));

    private static Appraisal Latest(WorldActionRig r, uint guid)
    {
        Assert.True(r.H.State.Appraisals.TryGet(guid, out var appraisal));
        return appraisal;
    }

    [Fact]
    public void Every_table_is_kept_by_property_id()
    {
        var r = new WorldActionRig();
        Answer(r, Mace, new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 1200, [AppraisalIds.Burden] = 90, [9u] = 1 },
            Int64s = new Dictionary<uint, long> { [1u] = 5_000_000_000L },
            Bools = new Dictionary<uint, bool> { [11u] = true, [22u] = false },
            Floats = new Dictionary<uint, double> { [100u] = 1.25 },
            Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Mace", [AppraisalIds.LongDescription] = "A heavy mace." },
            DataIds = new Dictionary<uint, uint> { [8u] = 0x06001234 },
        });

        var a = Latest(r, Mace);
        Assert.True(a.Success);
        Assert.False(a.IsNotFound);
        Assert.Equal(1200, a.Ints[AppraisalIds.Value]);
        Assert.Equal(3, a.Ints.Count);
        Assert.Equal(5_000_000_000L, a.Int64(1));
        Assert.True(a.Bool(11));
        Assert.False(a.Bool(22));
        Assert.Null(a.Bool(33));
        Assert.Equal(1.25, a.Float(100));
        Assert.Equal("Mace", a.Strings[AppraisalIds.Name]);
        Assert.Equal(0x06001234u, a.DataId(8));
        Assert.Null(a.Int(12345));
        Assert.Null(a.String(12345));
        Assert.Equal(
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.Int64StatsTable | IdentifyResponseFlags.BoolStatsTable | IdentifyResponseFlags.FloatStatsTable | IdentifyResponseFlags.StringStatsTable | IdentifyResponseFlags.DidStatsTable,
            a.Flags);
        Assert.False(a.HasProfile);
    }

    [Fact]
    public void The_typed_readers_read_the_ids_an_examine_panel_shows()
    {
        var r = new WorldActionRig();
        Answer(r, Mace, new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int>
            {
                [AppraisalIds.ItemType] = 0x1,
                [AppraisalIds.Burden] = 90,
                [AppraisalIds.Value] = 1200,
                [AppraisalIds.ArmorLevel] = 75,
                [AppraisalIds.Workmanship] = 6,
                [AppraisalIds.Material] = 21,
                [AppraisalIds.StackSize] = 3,
                [AppraisalIds.WieldType] = 2,
                [AppraisalIds.WieldSkill] = 0x28,
                [AppraisalIds.WieldDifficulty] = 250,
                [AppraisalIds.WieldType2] = 7,
                [AppraisalIds.WieldDifficulty2] = 40,
            },
            Strings = new Dictionary<uint, string>
            {
                [AppraisalIds.Name] = "Steel Mace",
                [AppraisalIds.UseText] = "Wield it.",
                [AppraisalIds.ShortDescription] = "short",
                [AppraisalIds.LongDescription] = "A mace of steel.",
            },
        });

        var a = Latest(r, Mace);
        Assert.Equal("Steel Mace", a.Name);
        Assert.Equal("Wield it.", a.UseText);
        Assert.Equal("short", a.ShortDescription);
        Assert.Equal("A mace of steel.", a.LongDescription);
        Assert.Equal(1200, a.Value);
        Assert.Equal(90, a.Burden);
        Assert.Equal(1u, a.ItemType);
        Assert.Equal(6, a.Workmanship);
        Assert.Equal(21, a.Material);
        Assert.Equal(3, a.StackSize);
        Assert.Equal(75, a.ArmorLevel);
        Assert.Equal([new WieldRequirement(2, 0x28, 250), new WieldRequirement(7, 0, 40)], a.WieldRequirements);
        Assert.Null(a.UsesLeft);
        Assert.Null(a.Level);
    }

    [Fact]
    public void A_healing_kit_reads_its_uses_boost_and_vital_from_the_capture_s_seven_ints()
    {
        // capture.md C3: the kit answered flags 0x5 (an int table of 7 and a float table of 1) and no profile.
        var r = new WorldActionRig();
        Answer(r, Kit, new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int>
            {
                [AppraisalIds.ItemType] = 0x1000,
                [AppraisalIds.Burden] = 5,
                [AppraisalIds.Value] = 10,
                [AppraisalIds.BoosterVital] = 2,
                [AppraisalIds.HealingBoost] = 20,
                [AppraisalIds.MaxStructure] = 25,
                [AppraisalIds.Structure] = 24,
            },
            Floats = new Dictionary<uint, double> { [100u] = 1.0 },
        });

        var a = Latest(r, Kit);
        Assert.Equal(IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable, a.Flags);
        Assert.Equal(7, a.Ints.Count);
        Assert.Equal(24, a.UsesLeft);
        Assert.Equal(25, a.MaxUses);
        Assert.Equal(20, a.HealingBoost);
        Assert.Equal(2, a.BoostedVital);
        Assert.Equal(10, a.Value);
        Assert.False(a.HasProfile);
    }

    [Fact]
    public void A_weapon_reads_damage_speed_and_skill_from_its_profile_and_from_the_table_when_it_has_no_profile()
    {
        var r = new WorldActionRig();
        Answer(r, Mace, new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [AppraisalIds.Damage] = 1, [AppraisalIds.WeaponTime] = 1, [AppraisalIds.WeaponSkill] = 1, [AppraisalIds.DamageType] = 1 },
            Weapon = new AceWeaponProfile(DamageType: 2, WeaponTime: 40, WeaponSkill: 0x29, Damage: 18, DamageVariance: 0.4, DamageMod: 1.1, WeaponLength: 0.8, MaxVelocity: 1.0, WeaponOffense: 1.15, MaxVelocityEstimated: 0),
        });
        Answer(r, Bow, new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [AppraisalIds.Damage] = 7, [AppraisalIds.WeaponTime] = 55, [AppraisalIds.WeaponSkill] = 47, [AppraisalIds.DamageType] = 4 },
        });

        var withProfile = Latest(r, Mace);
        Assert.True(withProfile.HasProfile);
        Assert.Equal(18, withProfile.WeaponDamage);
        Assert.Equal(40, withProfile.WeaponSpeed);
        Assert.Equal(0x29, withProfile.WeaponSkill);
        Assert.Equal(2, withProfile.WeaponDamageType);
        Assert.Equal(0.4, withProfile.Weapon!.DamageVariance);
        Assert.Equal(1.15, withProfile.Weapon.WeaponOffense);

        var tableOnly = Latest(r, Bow);
        Assert.False(tableOnly.HasProfile);
        Assert.Equal(7, tableOnly.WeaponDamage);
        Assert.Equal(55, tableOnly.WeaponSpeed);
        Assert.Equal(47, tableOnly.WeaponSkill);
        Assert.Equal(4, tableOnly.WeaponDamageType);
    }

    [Fact]
    public void An_armour_profile_and_the_bitfields_are_kept()
    {
        var r = new WorldActionRig();
        Answer(r, Mace, new AceAppraisal
        {
            Success = true,
            Armor = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f],
            ArmorBits = (0x0001, 0x0002),
            ResistBits = (0x0010, 0x0020),
            WeaponBits = (0x0100, 0x0200),
        });

        var a = Latest(r, Mace);
        Assert.Equal(new AppraisalArmorProfile(1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f), a.Armor);
        Assert.Equal(new AppraisalBitfield(1, 2), a.ArmorEnchantments);
        Assert.Equal(new AppraisalBitfield(0x10, 0x20), a.ResistEnchantments);
        Assert.Equal(new AppraisalBitfield(0x100, 0x200), a.WeaponEnchantments);
        Assert.True(a.HasProfile);
    }

    [Fact]
    public void A_creature_reads_its_level_vitals_and_armour_levels_as_the_capture_s_npc_does()
    {
        // capture.md C3: the NPC answered flags 0x510D: ints, floats, strings, dids, the creature profile and the armour levels.
        var r = new WorldActionRig();
        Answer(r, Npc, new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [AppraisalIds.Level] = 12 },
            Floats = new Dictionary<uint, double> { [39u] = 1.0 },
            Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Thelnoth Cort the Healer" },
            DataIds = new Dictionary<uint, uint> { [1u] = 0x02000001 },
            Creature = new AceCreatureProfile(Health: 90, HealthMax: 100) { Attributes = [10, 20, 30, 40, 50, 60, 70, 80, 90, 110], Buffs = (0, 0) },
            ArmorLevels = [1, 2, 3, 4, 5, 6, 7, 8, 9],
        });

        var a = Latest(r, Npc);
        Assert.Equal(
            IdentifyResponseFlags.IntStatsTable | IdentifyResponseFlags.FloatStatsTable | IdentifyResponseFlags.StringStatsTable | IdentifyResponseFlags.DidStatsTable
            | IdentifyResponseFlags.CreatureProfile | IdentifyResponseFlags.ArmorLevels,
            a.Flags);
        Assert.Equal("Thelnoth Cort the Healer", a.Name);
        Assert.Equal(12, a.Level);
        Assert.Equal(90u, a.CreatureHealth);
        Assert.Equal(100u, a.CreatureHealthMax);
        Assert.NotNull(a.CreatureStamina);
        Assert.NotNull(a.CreatureMana);
        Assert.Equal(new AppraisalArmorLevels(1, 2, 3, 4, 5, 6, 7, 8, 9), a.ArmorLevels);
    }

    [Fact]
    public void A_success_false_answer_keeps_the_tables_and_has_no_profiles_and_is_announced()
    {
        // spec 5.6: on a failed roll the tables are sent in full and the creature profile, the armour levels and the item enchantments are withheld.
        var r = new WorldActionRig();
        var received = new List<AppraisalReceived>();
        r.H.R.Bus.Subscribe((in AppraisalReceived e) => received.Add(e));
        Answer(r, Npc, new AceAppraisal
        {
            Success = false,
            Ints = new Dictionary<uint, int> { [AppraisalIds.Level] = 12 },
            Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Thelnoth Cort the Healer" },
        });

        var a = Latest(r, Npc);
        Assert.False(a.Success);
        Assert.False(a.IsNotFound);
        Assert.Equal("Thelnoth Cort the Healer", a.Name);
        Assert.Equal(12, a.Level);
        Assert.Null(a.Creature);
        Assert.Null(a.CreatureHealth);
        Assert.Null(a.ArmorLevels);
        Assert.False(a.HasProfile);
        Assert.Empty(a.ItemEnchantments);
        Assert.Equal(new AppraisalReceived(Npc, false, a.Flags, a.Sequence), Assert.Single(received));
        var cache = r.H.State.Appraisals;
        Assert.Equal((1, 0, 1, 0), (cache.Received, cache.Appraised, cache.Failed, cache.NotFound));
    }

    [Fact]
    public void An_answer_with_no_section_is_a_missing_object_not_a_failed_roll()
    {
        var r = new WorldActionRig();
        Answer(r, 0x1234, new AceAppraisal());

        var a = Latest(r, 0x1234);
        Assert.True(a.IsNotFound);
        Assert.False(a.Success);
        Assert.Equal(IdentifyResponseFlags.None, a.Flags);
        var cache = r.H.State.Appraisals;
        Assert.Equal((1, 0, 0, 1), (cache.Received, cache.Appraised, cache.Failed, cache.NotFound));
    }

    [Fact]
    public void The_spell_book_keeps_item_enchantments_apart_by_the_0x80000000_bit()
    {
        var r = new WorldActionRig();
        Answer(r, Mace, new AceAppraisal { Success = true, SpellBook = [0x0000012Cu, 0x80000345u, 0x00000099u, 0x80000001u] });

        var a = Latest(r, Mace);
        Assert.Equal([0x12Cu, 0x80000345u, 0x99u, 0x80000001u], a.SpellBook.ToArray());
        Assert.Equal([0x12Cu, 0x99u], a.Spells.ToArray());
        Assert.Equal([0x345u, 0x1u], a.ItemEnchantments.ToArray());
        Assert.Equal(IdentifyResponseFlags.SpellBook, a.Flags);
    }

    [Fact]
    public void A_later_answer_replaces_the_guid_s_appraisal_and_orders_after_it()
    {
        var r = new WorldActionRig();
        Answer(r, Mace, new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 1 } });
        var first = Latest(r, Mace);
        r.Tick(2);
        Answer(r, Kit, new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 2 } });
        Answer(r, Mace, new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 3 } });

        var second = Latest(r, Mace);
        Assert.Equal(3, second.Value);
        Assert.True(second.Sequence > first.Sequence);
        Assert.True(second.ReceivedAt - first.ReceivedAt >= 2);
        Assert.Equal(2, r.H.State.Appraisals.Count);
        Assert.Equal(3, r.H.State.Appraisals.Received);
        Assert.Equal(3, r.H.State.Appraisals.Sequence);
    }

    [Fact]
    public void The_cache_keeps_at_most_256_guids_and_drops_the_one_answered_longest_ago()
    {
        var r = new WorldActionRig();
        Assert.Equal(256, AppraisalCache.Capacity);
        const uint Base = 0x80010000;
        for (uint i = 0; i < 256; i++)
        {
            Answer(r, Base + i, new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = (int)i } });
        }

        Assert.Equal(256, r.H.State.Appraisals.Count);

        // The oldest guid is answered again: it becomes the newest, so the next new guid drops the second oldest.
        Answer(r, Base, new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 999 } });
        Assert.Equal(256, r.H.State.Appraisals.Count);
        Answer(r, Base + 1000, new AceAppraisal { Success = true });

        Assert.Equal(256, r.H.State.Appraisals.Count);
        Assert.True(r.H.State.Appraisals.TryGet(Base, out var kept));
        Assert.Equal(999, kept.Value);
        Assert.False(r.H.State.Appraisals.TryGet(Base + 1, out _));
        Assert.True(r.H.State.Appraisals.TryGet(Base + 2, out _));
        Assert.True(r.H.State.Appraisals.TryGet(Base + 1000, out _));
        Assert.Equal(258, r.H.State.Appraisals.Received); // the counters count every answer, whatever was dropped
        Assert.Equal(258, r.H.State.Appraisals.Appraised);
    }
}
