using DatReaderWriter.Enums;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Actions.Trackers;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <c>Cast(spell)</c> (0x0048) and <c>Cast(spell, target)</c> (0x004A) over ACE's own answers (M3 1.2 s4b, DoD 3): a self buff confirmed by its 0x02C2, a war spell by its projectile, a fizzle
/// (0x028A 0x0402) and both UseDone refusals (0x0400, 0x0401) as ServerError, the 10 s timeout, a buff on another target completed as Sent, and one cast in flight at a time in call order
/// (the lane stays busy until ACE's UseDone, as ACE is busy until then). The spell kinds come from the SpellTable's <c>MetaSpellType</c> and <c>Flags</c>.
/// </summary>
public sealed class CastTrackerTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint StrengthSelf = 100;
    private const uint StrengthOther = 101;
    private const uint FlameBolt = 102;
    private const uint Heal = 103;
    private const uint Target = 0x80000123;
    private const uint Projectile = 0x80000777;

    private static WorldActionRig Rig()
    {
        var r = new WorldActionRig();
        r.CombatModeIs(CombatModes.Magic);
        r.Spells(
            WorldActionRig.Spell(StrengthSelf, SpellType.Enchantment, SpellFlags.Beneficial | SpellFlags.SelfTargeted),
            WorldActionRig.Spell(StrengthOther, SpellType.Enchantment, SpellFlags.Beneficial),
            WorldActionRig.Spell(FlameBolt, SpellType.Projectile, SpellFlags.Projectile),
            WorldActionRig.Spell(Heal, SpellType.Boost, SpellFlags.Beneficial));
        return r;
    }

    [Fact]
    public void A_self_buff_sends_0x0048_and_is_confirmed_by_its_02C2_not_another_spells()
    {
        var r = Rig();
        var cast = r.Api.Cast(StrengthSelf, ActionSource.Ui);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.CastUntargetedSpell));
        Assert.Equal(StrengthSelf, sent.Guid);
        Assert.Empty(r.H.Net.Of(FakeSendKind.CastTargetedSpell));

        r.Enchant(999); // another spell's enchantment (a stale answer, a buff from elsewhere)
        r.Tick();
        Assert.False(cast.IsCompleted);

        r.Enchant((ushort)StrengthSelf);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Contains("gained the enchantment", cast.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refresh_of_a_buff_the_player_has_is_confirmed_too()
    {
        var r = Rig();
        r.Enchant((ushort)StrengthSelf);
        var cast = r.Api.Cast(StrengthSelf, ActionSource.Ui);
        r.Tick();
        r.Enchant((ushort)StrengthSelf); // the same layered id again: replaced
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Contains("refreshed", cast.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_self_spell_cast_at_the_player_s_own_guid_sends_0x004A_and_is_confirmed_by_the_enchantment()
    {
        var r = Rig();
        var cast = r.Api.Cast(StrengthOther, WorldActionRig.Player, ActionSource.Ui);
        r.Tick();
        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.CastTargetedSpell));
        Assert.Equal((WorldActionRig.Player, (int)StrengthOther), (sent.Guid, sent.Index));

        r.Enchant((ushort)StrengthOther);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_war_spell_is_confirmed_by_the_projectile_created_near_the_player_and_not_by_far_or_other_objects()
    {
        var r = Rig();
        var cast = r.Api.Cast(FlameBolt, Target, ActionSource.Ui);
        r.Tick();
        Assert.Equal(Target, Assert.Single(r.H.Net.Of(FakeSendKind.CastTargetedSpell)).Guid);

        r.Create(0x80000555, 11, missile: false); // an ordinary object
        r.Create(0x80000556, 500); // a missile far away: a monster's, not the player's
        r.Enchant(999); // a projectile spell is not confirmed by an enchantment
        r.Tick();
        Assert.False(cast.IsCompleted);

        r.Create(Projectile, 12);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Contains("TODO(m3-cast-projectile-attribution)", cast.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("0x80000777", cast.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fizzle_is_a_ServerError_with_code_0x0402_and_the_lane_waits_for_ACE_s_UseDone()
    {
        var r = Rig();
        var cast = r.Api.Cast(FlameBolt, Target, ActionSource.Ui);
        var next = r.Api.Cast(StrengthSelf, ActionSource.Ui);
        r.Tick();

        r.WeenieError(0x0402);
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, 0x0402u), (cast.Outcome!.Value.Kind, cast.Outcome.Value.Code));
        Assert.Single(r.H.Net.Sends, s => s.Kind is FakeSendKind.CastTargetedSpell or FakeSendKind.CastUntargetedSpell); // the second cast still waits: ACE is busy until its UseDone

        r.UseDone(); // FinishCast: UseDone with no error ends the fizzled cast
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.CastUntargetedSpell));
        Assert.False(next.IsCompleted);
    }

    [Theory]
    [InlineData(0x0400u, "component")]
    [InlineData(0x0401u, "mana")]
    [InlineData(0x001Du, "0x0000001D")]
    public void A_refusal_in_the_UseDone_is_a_ServerError_with_its_code_and_ends_the_cast(uint code, string detail)
    {
        var r = Rig();
        var cast = r.Api.Cast(StrengthSelf, ActionSource.Ui);
        r.Tick();

        r.UseDone(code);
        r.Tick();

        Assert.Equal((ActionOutcomeKind.ServerError, code), (cast.Outcome!.Value.Kind, cast.Outcome.Value.Code));
        Assert.Contains(detail, cast.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fizzle_UseDone_with_the_code_is_the_same_answer_as_the_0x028A()
    {
        var r = Rig();
        var cast = r.Api.Cast(FlameBolt, Target, ActionSource.Ui);
        r.Tick();
        r.UseDone(0x0402); // ACE's FailCast: UseDone(YourSpellFizzled)
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, 0x0402u), (cast.Outcome!.Value.Kind, cast.Outcome.Value.Code));
    }

    [Fact]
    public void A_cast_the_server_never_answers_times_out_at_10_seconds_on_the_session_clock_and_frees_the_lane()
    {
        var r = Rig();
        var cast = r.Api.Cast(StrengthSelf, ActionSource.Ui);
        var next = r.Api.Cast(FlameBolt, Target, ActionSource.Ui);
        r.Tick();

        r.Tick(9.9);
        Assert.False(cast.IsCompleted);
        Assert.Empty(r.H.Net.Of(FakeSendKind.CastTargetedSpell));

        r.Tick(0.2);
        Assert.Equal(ActionOutcomeKind.TimedOut, cast.Outcome!.Value.Kind);
        Assert.Single(r.H.Net.Of(FakeSendKind.CastTargetedSpell)); // the queued cast goes out once the first one's turn is over
        Assert.False(next.IsCompleted);
        Assert.Equal(10.0, ActionPolicy.CastSeconds);
    }

    [Fact]
    public void Two_casts_queue_and_leave_in_call_order_each_after_ACE_ended_the_one_before()
    {
        var r = Rig();
        var first = r.Api.Cast(StrengthSelf, ActionSource.Ui);
        var second = r.Api.Cast(FlameBolt, Target, ActionSource.Ui);
        var third = r.Api.Cast(Heal, ActionSource.Ui);
        r.Tick();
        Assert.Equal(1, r.H.Net.Sends.Count(s => s.Kind is FakeSendKind.CastUntargetedSpell or FakeSendKind.CastTargetedSpell));

        r.Enchant((ushort)StrengthSelf);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, first.Outcome!.Value.Kind);
        Assert.Empty(r.H.Net.Of(FakeSendKind.CastTargetedSpell)); // confirmed, but ACE is still in the cast's recoil

        r.UseDone();
        r.Tick();
        Assert.Single(r.H.Net.Of(FakeSendKind.CastTargetedSpell));
        Assert.False(second.IsCompleted);
        Assert.Single(r.H.Net.Of(FakeSendKind.CastUntargetedSpell)); // the third still waits

        r.Create(Projectile, 12);
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, second.Outcome!.Value.Kind);
        Assert.Equal([StrengthSelf, Heal], r.H.Net.Of(FakeSendKind.CastUntargetedSpell).Select(s => s.Guid));
        Assert.False(third.IsCompleted);
    }

    [Fact]
    public void A_held_lane_is_let_go_at_the_timeout_when_ACE_never_sends_its_UseDone()
    {
        var r = Rig();
        r.Api.Cast(StrengthSelf, ActionSource.Ui);
        var second = r.Api.Cast(Heal, ActionSource.Ui);
        r.Tick();
        r.Enchant((ushort)StrengthSelf);
        r.Tick(3);
        Assert.Single(r.H.Net.Of(FakeSendKind.CastUntargetedSpell));

        r.Tick(7.1); // 10 s from the send
        Assert.Equal(2, r.H.Net.Of(FakeSendKind.CastUntargetedSpell).Count);
        Assert.False(second.IsCompleted);
    }

    [Fact]
    public void A_buff_on_another_target_has_no_proof_on_the_wire_and_completes_as_Sent_when_ACE_ends_the_cast()
    {
        var r = Rig();
        var cast = r.Api.Cast(StrengthOther, Target, ActionSource.Ui);
        r.Tick();
        r.Enchant((ushort)StrengthOther); // not the player's: an enchantment on the player is not this cast's answer
        r.Tick();
        Assert.False(cast.IsCompleted);

        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Sent, cast.Outcome!.Value.Kind);
        Assert.Contains("TODO(m3-cast-other-target-proof)", cast.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_self_or_projectile_cast_ACE_ends_without_its_effect_is_a_ServerError_that_says_when_the_mode_is_wrong()
    {
        var r = Rig();
        var cast = r.Api.Cast(StrengthSelf, ActionSource.Ui);
        r.Tick();
        r.UseDone(); // ACE's "cannot be cast on" and its already-stronger buff end like this
        r.Tick();
        Assert.Equal((ActionOutcomeKind.ServerError, 0u), (cast.Outcome!.Value.Kind, cast.Outcome.Value.Code));
        Assert.Contains("without the enchantment", cast.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("not in the magic combat mode", cast.Outcome.Value.Detail, StringComparison.Ordinal);

        r.CombatModeIs(CombatModes.NonCombat);
        var wrong = r.Api.Cast(FlameBolt, Target, ActionSource.Ui);
        r.Tick();
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, wrong.Outcome!.Value.Kind);
        Assert.Contains("without a projectile", wrong.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Contains("not in the magic combat mode", wrong.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void The_effect_and_the_UseDone_in_one_tick_is_a_success_whatever_the_order()
    {
        var r = Rig();
        var cast = r.Api.Cast(StrengthSelf, ActionSource.Ui);
        r.Tick();
        r.UseDone();
        r.Enchant((ushort)StrengthSelf);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
    }

    [Fact]
    public void Without_a_spell_table_the_enchantment_or_a_projectile_confirms_and_UseDone_alone_is_Sent_with_a_named_TODO()
    {
        var r = new WorldActionRig();
        var first = r.Api.Cast(StrengthSelf, ActionSource.Ui);
        r.Tick();
        r.Enchant((ushort)StrengthSelf);
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, first.Outcome!.Value.Kind);

        var second = r.Api.Cast(FlameBolt, Target, ActionSource.Ui);
        r.Tick();
        r.Create(Projectile, 12);
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, second.Outcome!.Value.Kind);

        var third = r.Api.Cast(Heal, ActionSource.Ui);
        r.Tick();
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Sent, third.Outcome!.Value.Kind);
        Assert.Contains("TODO(m3-cast-spell-table-host)", third.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_literal_zero_target_is_the_refused_no_target_and_not_an_untargeted_cast_and_a_literal_target_is_a_targeted_one()
    {
        var r = Rig();

        var zero = r.Api.Cast(StrengthSelf, 0);
        var zeroWithSource = r.Api.Cast(StrengthSelf, 0, ActionSource.Ui);
        Assert.All([zero, zeroWithSource], h =>
        {
            Assert.Equal(ActionOutcomeKind.Refused, h.Outcome!.Value.Kind);
            Assert.Contains("no target", h.Outcome.Value.Detail, StringComparison.Ordinal);
        });

        var literal = r.Api.Cast(FlameBolt, 5);
        var viaEnum = r.Api.Cast(Heal, ActionSource.Ui);
        r.Tick();
        var targeted = Assert.Single(r.H.Net.Of(FakeSendKind.CastTargetedSpell));
        Assert.Equal((5u, (int)FlameBolt), (targeted.Guid, targeted.Index));
        Assert.Empty(r.H.Net.Of(FakeSendKind.CastUntargetedSpell)); // the second waits behind the first
        Assert.Equal(ActionSource.Host, literal.Source);
        Assert.Equal(ActionSource.Ui, viaEnum.Source);
    }

    [Fact]
    public void An_int_typed_guid_of_0x80000000_or_more_is_a_target_and_only_zero_is_the_refused_no_target()
    {
        // M3 1.2 review R7: every monster and every item the server creates has a guid of 0x80000000 or more, a negative int; the int overload used to refuse it as the "no target".
        var r = Rig();
        var monster = r.Api.Cast(FlameBolt, unchecked((int)0x80010000));
        r.Tick();
        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.CastTargetedSpell));
        Assert.Equal((0x80010000u, (int)FlameBolt), (sent.Guid, sent.Index));
        Assert.False(monster.Outcome is { Kind: ActionOutcomeKind.Refused });
        Assert.Contains("target=0x80010000", monster.Summary, StringComparison.Ordinal);

        var none = r.Api.Cast(FlameBolt, 0);
        Assert.Equal(ActionOutcomeKind.Refused, none.Outcome!.Value.Kind);
        Assert.Contains("no target", none.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Single(r.H.Net.Of(FakeSendKind.CastTargetedSpell)); // the refused cast sent nothing
        Assert.Empty(r.H.Net.Of(FakeSendKind.CastUntargetedSpell));
    }

    [Fact]
    public void A_cast_before_the_world_is_refused_and_a_zero_spell_is_refused_at_once()
    {
        var r = new WorldActionRig(present: false);
        var early = r.Api.Cast(StrengthSelf, ActionSource.Ui);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, early.Outcome!.Value.Kind);
        Assert.Empty(r.H.Net.Sends);

        var ready = Rig();
        Assert.Equal(ActionOutcomeKind.Refused, ready.Api.Cast(0, ActionSource.Ui).Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, ready.Api.Cast(0, Target, ActionSource.Ui).Outcome!.Value.Kind);
    }

    [Fact]
    public void The_kill_switch_refuses_an_automated_cast_before_anything_is_sent()
    {
        var r = Rig();
        r.Api.KillSwitch = true;
        var bot = r.Api.Cast(StrengthSelf, ActionSource.Automation);
        Assert.Equal(ActionOutcomeKind.Refused, bot.Outcome!.Value.Kind);
        Assert.Empty(r.H.Net.Of(FakeSendKind.CastUntargetedSpell));
    }

    [Fact]
    public void Classify_reads_the_spell_table_s_meta_type_and_flags()
    {
        const uint Self = WorldActionRig.Player;
        var selfFlag = WorldActionRig.Spell(1, SpellType.Enchantment, SpellFlags.SelfTargeted);
        var plain = WorldActionRig.Spell(2, SpellType.Enchantment);

        Assert.Equal(CastTracker.Expectation.Unknown, CastTracker.Classify(null, 0, Self));
        Assert.Equal(CastTracker.Expectation.Projectile, CastTracker.Classify(WorldActionRig.Spell(3, SpellType.Projectile), Target, Self));
        Assert.Equal(CastTracker.Expectation.Projectile, CastTracker.Classify(WorldActionRig.Spell(4, SpellType.LifeProjectile), Target, Self));
        Assert.Equal(CastTracker.Expectation.Projectile, CastTracker.Classify(WorldActionRig.Spell(5, SpellType.EnchantmentProjectile), Target, Self));
        Assert.Equal(CastTracker.Expectation.SelfEnchantment, CastTracker.Classify(selfFlag, 0, Self));
        Assert.Equal(CastTracker.Expectation.SelfEnchantment, CastTracker.Classify(selfFlag, Target, Self));
        Assert.Equal(CastTracker.Expectation.SelfEnchantment, CastTracker.Classify(plain, 0, Self));
        Assert.Equal(CastTracker.Expectation.SelfEnchantment, CastTracker.Classify(plain, Self, Self));
        Assert.Equal(CastTracker.Expectation.Other, CastTracker.Classify(plain, Target, Self));
        Assert.Equal(CastTracker.Expectation.SelfEnchantment, CastTracker.Classify(WorldActionRig.Spell(6, SpellType.FellowEnchantment), 0, Self));
        Assert.Equal(CastTracker.Expectation.Other, CastTracker.Classify(WorldActionRig.Spell(7, SpellType.Boost), 0, Self));
        Assert.Equal(CastTracker.Expectation.Other, CastTracker.Classify(WorldActionRig.Spell(8, SpellType.PortalSummon), 0, Self));
    }

    [Fact]
    public void An_armour_line_at_the_caster_is_ended_by_its_UseDone_an_Aura_by_its_enchantment_and_a_transfer_or_a_boost_by_their_UseDone()
    {
        // M3 2.1 s0b (plan D23). ACE redirects an Impenetrability or Bludgeon Bane (an Item Enchantment, NonComponentTargetType Vestements, not SelfTargeted) cast with the caster as the target to every worn
        // clothing piece (WorldObject_Magic.cs 2067-2121): "You cast <spell> on <item>" per piece, no GameEvent 0x02C2 to the player, then UseDone 0. The wand's Auras (Hermetic Link, Defender) are SelfTargeted.
        const uint Self = WorldActionRig.Player;
        var armour = WorldActionRig.Spell(10, SpellType.Enchantment) with { School = MagicSchool.ItemEnchantment, NonComponentTargetType = 0x6 };
        var aura = WorldActionRig.Spell(11, SpellType.Enchantment, SpellFlags.SelfTargeted) with { School = MagicSchool.ItemEnchantment, NonComponentTargetType = 0x8000 };
        var transfer = WorldActionRig.Spell(12, SpellType.Transfer, SpellFlags.Beneficial) with { School = MagicSchool.LifeMagic };
        var boost = WorldActionRig.Spell(13, SpellType.Boost, SpellFlags.Beneficial) with { School = MagicSchool.LifeMagic };

        Assert.Equal(CastTracker.Expectation.Other, CastTracker.Classify(armour, Self, Self)); // at the caster: no 0x02C2 ever comes
        Assert.Equal(CastTracker.Expectation.Other, CastTracker.Classify(armour, Target, Self)); // at an item (or another target): whatever its target
        Assert.Equal(CastTracker.Expectation.Other, CastTracker.Classify(armour, 0, Self));
        Assert.Equal(CastTracker.Expectation.SelfEnchantment, CastTracker.Classify(aura, 0, Self));
        Assert.Equal(CastTracker.Expectation.SelfEnchantment, CastTracker.Classify(aura, Self, Self));
        Assert.Equal(CastTracker.Expectation.Other, CastTracker.Classify(transfer, 0, Self)); // vitals change by private updates and a chat line
        Assert.Equal(CastTracker.Expectation.Other, CastTracker.Classify(boost, 0, Self));

        // The whole path: the armour cast at the caster's own guid ends as Sent on UseDone 0 (it did not wait for an enchantment ACE never sends); the Aura waits for its enchantment.
        var r = Rig();
        r.Spells(armour, aura);
        var cast = r.Api.Cast(10, Self, ActionSource.Ui);
        r.Tick();
        Assert.Equal(WorldActionRig.Player, Assert.Single(r.H.Net.Of(FakeSendKind.CastTargetedSpell)).Guid);
        Assert.False(cast.IsCompleted);
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Sent, cast.Outcome!.Value.Kind);

        var wand = r.Api.Cast(11, ActionSource.Ui);
        r.Tick();
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, wand.Outcome!.Value.Kind); // an Aura cast ends in an error without its 0x02C2, as before
    }

    [RequiresDatsFact]
    public void The_retail_spell_table_classifies_a_bolt_a_self_buff_and_an_other_buff()
    {
        var spells = SpellTableData.Load(fixture.Dats).Spells.Values;
        SpellInfo Named(string name) => spells.Where(s => s.Name == name).OrderBy(s => s.Id).First();

        Assert.Equal(CastTracker.Expectation.Projectile, CastTracker.Classify(Named("Flame Bolt I"), Target, WorldActionRig.Player));
        Assert.Equal(CastTracker.Expectation.SelfEnchantment, CastTracker.Classify(Named("Strength Self I"), 0, WorldActionRig.Player));
        Assert.Equal(CastTracker.Expectation.Other, CastTracker.Classify(Named("Strength Other I"), Target, WorldActionRig.Player));

        // M3 2.1 s0b: the profile's lines at the caster, from the retail table: the armour lines end on their UseDone, the wand's Auras and the creature and life buffs on their enchantment, and the two mana lines
        // (a Transfer and a Boost, not enchantments) on their UseDone.
        var table = SpellTableData.Load(fixture.Dats).Spells;
        foreach (var line in Lifestone.Client.Session.Mage.ArenaProfile.Lines)
        {
            var expected = line.Role switch
            {
                Lifestone.Client.Session.Mage.ProfileRole.ItemBuffArmor or Lifestone.Client.Session.Mage.ProfileRole.ManaTransfer or Lifestone.Client.Session.Mage.ProfileRole.StaminaRestore => CastTracker.Expectation.Other,
                Lifestone.Client.Session.Mage.ProfileRole.CreatureBuff or Lifestone.Client.Session.Mage.ProfileRole.LifeBuff or Lifestone.Client.Session.Mage.ProfileRole.ItemBuffWand => CastTracker.Expectation.SelfEnchantment,
                _ => CastTracker.Expectation.Projectile,
            };
            foreach (var spell in line.Spells)
            {
                if (expected != CastTracker.Expectation.Projectile)
                {
                    Assert.True(expected == CastTracker.Classify(table[spell.Id], WorldActionRig.Player, WorldActionRig.Player), $"{line.Name} ({spell.Name}, {spell.Id}) at the caster should be {expected}");
                }
            }
        }
    }
}
