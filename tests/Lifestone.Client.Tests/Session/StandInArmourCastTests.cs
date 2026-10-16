using DatReaderWriter.Enums;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 2.1 s2b item 3 (D23): the stand-in answers an Item Enchantment whose NonComponentTargetType is Vestements, cast at the player, as ACE does: one Magic chat line "You cast &lt;spell&gt; on &lt;item name&gt;" for each worn clothing
/// piece and shield (<c>WorldObject_Magic.cs</c> 2067-2121, 430-452), UseDone 0 and no 0x02C2 (the enchantments are on the items). Decided by the spell's school, meta type, flags and target type, never by the cast tracker's
/// <c>Classify</c>. A creature-school self buff still gets its 0x02C2.
/// </summary>
public sealed class StandInArmourCastTests
{
    private const uint Armour = 51; // Impenetrability I's id (the spell here is hand-made; only the id and the fields below matter)
    private const uint CreatureBuff = 2;
    private const uint Cap = 70001;
    private const uint Coat = 70002;
    private const uint Buckler = 70003;
    private const uint Ring = 70004;
    private const uint Wand = 70005;
    private const uint Vestements = StandInWorld.VestementsTargetType;

    private static readonly SpellInfo ArmourSpell = WorldActionRig.Spell(Armour, SpellType.Enchantment, SpellFlags.Beneficial) with
    {
        Name = "Impenetrability I",
        School = MagicSchool.ItemEnchantment,
        NonComponentTargetType = Vestements,
        Category = 160,
        Duration = 1800.0,
        TargetEffect = PlayScript.EnchantUpWhite,
    };

    private static readonly SpellInfo SelfBuff = WorldActionRig.Spell(CreatureBuff, SpellType.Enchantment, SpellFlags.Beneficial | SpellFlags.SelfTargeted) with
    {
        Name = "Strength Self I",
        School = MagicSchool.CreatureEnchantment,
        Category = 1,
        Duration = 1800.0,
        TargetEffect = PlayScript.AttribUpRed,
    };

    private static CastPlan Plan(SpellInfo spell) => new()
    {
        Spell = spell,
        BaseFormula = [],
        PlayerFormula = [],
        AccountName = StandInCastPlanner.AccountName,
        Words = "Zojak Quaguz",
        Steps =
        [
            new CastStep(CastStepKind.Windup, 0.0, MotionCommands.Magic, 0x10000103, CastGesturePlanner.CastSpeed, 0.5, 1),
            new CastStep(CastStepKind.CastGesture, 0.5, MotionCommands.Magic, MotionCommands.MagicHeal, CastGesturePlanner.CastSpeed, 0.5, 80),
            new CastStep(CastStepKind.SpellEffects, 1.0, MotionCommands.Magic, 0, 0f, 0.0, 0),
            new CastStep(CastStepKind.ReturnToReady, 1.0, MotionCommands.Magic, MotionCommands.Ready, 1f, CastGesturePlanner.RecoilSeconds, 0),
        ],
        EffectTime = 1.0,
        EffectIntensity = 0.4f,
        EndTime = 1.0 + CastGesturePlanner.RecoilSeconds,
    };

    private static CastRig Ready(params (uint Wcid, EquipMask Mask)[] worn)
    {
        var visit = new StandInVisit
        {
            EnterAfter = 0.2,
            UnhideAfter = 1.0,
            Stats = new StandInPlayerStats(),
            CastPlanner = info => Plan(info),
            Items = new Dictionary<uint, StandInItemKind>
            {
                [Cap] = new("Leather Cap", null),
                [Coat] = new("Leather Coat", null),
                [Buckler] = new("Buckler", null),
                [Ring] = new("Gold Ring", null),
                [Wand] = new("Wand", null),
            },
        };
        var rig = new CastRig(visit, [ArmourSpell, SelfBuff]);
        rig.Learn(Armour, CreatureBuff);
        foreach (var (wcid, mask) in worn)
        {
            var spawn = rig.Session.Tools.SpawnItem(wcid, null, ActionSource.Host);
            rig.Run(2.0);
            Assert.True(spawn.IsCompleted, spawn.Summary);
            var wield = rig.Session.Actions.Wield(spawn.Result!.Guid, mask, ActionSource.Host);
            rig.Run(1.5);
            Assert.True(wield.IsCompleted, wield.Summary);
        }

        rig.Mode(CombatMode.Magic);
        rig.Begin();
        return rig;
    }

    [Fact]
    public void An_armour_enchantment_at_the_caster_is_one_magic_line_per_worn_clothing_piece_and_shield_then_use_done_and_no_02C2()
    {
        using var rig = Ready((Cap, EquipMask.HeadWear), (Coat, EquipMask.ChestArmor | EquipMask.AbdomenArmor), (Buckler, EquipMask.Shield), (Ring, EquipMask.FingerWearRight), (Wand, EquipMask.Held));
        rig.Begin();
        var cast = rig.Session.Actions.Cast(Armour, ActionSource.Host);
        rig.Run(Plan(ArmourSpell).EndTime + 1.5);

        Assert.DoesNotContain("02C2", rig.Tags);
        Assert.Equal(3, rig.Tags.Count(t => t == "F7E0"));
        Assert.Equal("01C7", rig.Tags[^1]);
        var lines = rig.Chat.Where(l => l.ChatType == StandInWorld.MagicChat && l.Text.StartsWith("You cast ", StringComparison.Ordinal)).Select(l => l.Text).ToArray();
        Assert.Equal(["You cast Impenetrability I on Leather Cap", "You cast Impenetrability I on Leather Coat", "You cast Impenetrability I on Buckler"], lines);

        // The caster's own registry is untouched, and the target effect still plays on the player (ACE's DoSpellEffects).
        Assert.DoesNotContain(rig.Q.Enchantments, e => e.SpellId == Armour);
        Assert.Contains(rig.Scripts, s => s.Script.ScriptType == (uint)PlayScript.EnchantUpWhite && s.Script.Guid == rig.Session.State.Self.Guid);
        Assert.True(cast.IsCompleted);
    }

    [Fact]
    public void With_nothing_worn_there_are_no_lines_and_a_creature_buff_still_lands_as_the_players_own_enchantment()
    {
        using var rig = Ready();
        rig.Session.Actions.Cast(Armour, ActionSource.Host);
        rig.Run(Plan(ArmourSpell).EndTime + 1.5);
        Assert.DoesNotContain("02C2", rig.Tags);
        Assert.DoesNotContain("F7E0", rig.Tags);
        Assert.Contains("01C7", rig.Tags);

        rig.Begin();
        var buff = rig.Session.Actions.Cast(CreatureBuff, ActionSource.Host);
        rig.Run(Plan(SelfBuff).EndTime + 1.5);
        Assert.Contains("02C2", rig.Tags);
        Assert.Contains(rig.Q.Enchantments, e => e.SpellId == CreatureBuff);
        Assert.True(buff.IsCompleted);
    }

    [Fact]
    public void The_rule_reads_school_meta_type_flags_and_target_type_and_a_target_other_than_the_caster_is_not_the_redirect()
    {
        const uint self = 0x50000001;
        Assert.True(StandInWorld.IsArmourEnchantmentAtCaster(ArmourSpell, 0, self));
        Assert.True(StandInWorld.IsArmourEnchantmentAtCaster(ArmourSpell, self, self));
        Assert.False(StandInWorld.IsArmourEnchantmentAtCaster(ArmourSpell, 0x80000001, self));
        Assert.False(StandInWorld.IsArmourEnchantmentAtCaster(ArmourSpell with { School = MagicSchool.CreatureEnchantment }, 0, self));
        Assert.False(StandInWorld.IsArmourEnchantmentAtCaster(ArmourSpell with { MetaSpellType = SpellType.Boost }, 0, self));
        Assert.False(StandInWorld.IsArmourEnchantmentAtCaster(ArmourSpell with { Flags = ArmourSpell.Flags | SpellFlags.SelfTargeted }, 0, self));
        Assert.False(StandInWorld.IsArmourEnchantmentAtCaster(ArmourSpell with { NonComponentTargetType = 0x100 }, 0, self));
    }
}
