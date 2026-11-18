using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.PlayerRules;
using Lifestone.Data.Magic;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.Session.PlayerRules;

/// <summary>
/// The player's rules at the Action API (M6 item 15, the owner's order), over the real Action API and a recorded wire: an automation cast of a spell on no bar sends AddSpellFavorite first and the cast after it, once; a
/// higher level sends RemoveSpellFavorite for the lower one and takes its place; a spell that cannot go on a bar is refused by name and nothing is sent; the panel is told which bar the cast is from; a person's cast and a
/// session with the rule off are left alone. The same for a drink and the shortcut bar: AddShortcut before the first drink, the drink is the shortcut's stack, the slot is kept when a stack runs out, a full bar refuses by name.
/// </summary>
public sealed class PlayerRulesGateTests
{
    private const uint BoltI = 27, BoltII = 81, HealI = 6, StrengthI = 2, Unknown = 4000;
    private const uint Monster = 0x80000042;

    private static readonly SpellInfo[] Table =
    [
        WorldActionRig.Spell(BoltI, SpellType.Projectile, SpellFlags.Projectile | SpellFlags.Resistable) with { Name = "Flame Bolt I", Category = 121, Power = 1 },
        WorldActionRig.Spell(BoltII, SpellType.Projectile, SpellFlags.Projectile | SpellFlags.Resistable) with { Name = "Flame Bolt II", Category = 121, Power = 50 },
        WorldActionRig.Spell(HealI, SpellType.Boost, SpellFlags.SelfTargeted | SpellFlags.Beneficial) with { Name = "Heal Self I", School = MagicSchool.LifeMagic, Category = 67 },
        WorldActionRig.Spell(StrengthI, SpellType.Enchantment, SpellFlags.SelfTargeted | SpellFlags.Beneficial) with { Name = "Strength Self I", School = MagicSchool.CreatureEnchantment, Category = 1 },
        WorldActionRig.Spell(Unknown, SpellType.Projectile, SpellFlags.Projectile) with { Name = "Frost Bolt I", Category = 122 },
    ];

    private static WorldActionRig Rig(uint[] known, params uint[][] bars)
    {
        var r = new WorldActionRig();
        var all = ImmutableArray.CreateBuilder<ImmutableArray<uint>>();
        for (var b = 0; b < 8; b++)
        {
            all.Add(b < bars.Length ? [.. bars[b]] : []);
        }

        r.H.Describe(new PlayerDescriptionMsg { Spells = known.ToImmutableDictionary(id => id, _ => 2f), SpellBars = all.ToImmutable(), Vitals = [new VitalDesc(5, 100, 0, 0, 100)] });
        r.Spells(Table);
        r.CombatModeIs(CombatModes.Magic);
        r.H.R.Reducer.OnCreateObject(r.H.R.Context, ReducerHarness.Create(Monster, ReducerHarness.Stamps(), null, name: "Drudge"), isUpdate: false);
        r.Tick();
        var shown = Shown.GetOrCreateValue(r);
        r.H.R.Bus.Subscribe<SpellBarCast>((in SpellBarCast e) => shown.Add(e)); // the shared harness does not track this event
        return r;
    }

    // What the spellcasting panel was told, by rig.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<WorldActionRig, List<SpellBarCast>> Shown = [];

    private static List<SpellBarCast> ShownBy(WorldActionRig r) => Shown.GetOrCreateValue(r);

    private static List<FakeSendKind> Wire(WorldActionRig r) =>
        [.. r.H.Net.Kinds().Where(k => k is FakeSendKind.AddSpellFavorite or FakeSendKind.RemoveSpellFavorite or FakeSendKind.CastTargetedSpell or FakeSendKind.CastUntargetedSpell or FakeSendKind.AddShortcut or FakeSendKind.RemoveShortcut or FakeSendKind.Use)];

    private static List<string> RuleLog(WorldActionRig r, LogSeverity severity) =>
        [.. r.H.Log.Entries.Where(e => e.Category == PlayerRulesGate.LogCategory && e.Severity == severity).Select(e => e.Message)];

    private static List<ActionLogEntry> Logged(WorldActionRig r) => [.. Enumerable.Range(0, r.H.ActionLog.Count).Select(i => r.H.ActionLog[i])];

    // ---------------------------------------------------------------------------------------------- spells

    [Fact]
    public void A_spell_on_no_bar_is_added_as_a_player_adds_it_before_its_first_cast_and_then_cast_from_the_bar()
    {
        var r = Rig([BoltI, HealI]);
        var cast = r.Api.Cast(BoltI, Monster, ActionSource.Automation);
        r.Tick();

        Assert.Equal([FakeSendKind.AddSpellFavorite, FakeSendKind.CastTargetedSpell], Wire(r)); // the add goes out first
        var add = Assert.Single(r.H.Net.Of(FakeSendKind.AddSpellFavorite));
        Assert.Equal((BoltI, 0, (uint)SpellBarRule.FightTab), (add.Guid, add.Index, add.Amount)); // spell, position, bar: tab II
        Assert.Equal<uint>([BoltI], r.H.State.Self.Spellbook.Bar(SpellBarRule.FightTab));
        Assert.False(cast.IsCompleted); // sent, waiting for the server
        Assert.Contains(RuleLog(r, LogSeverity.Info), m => m.Contains("Flame Bolt I (spell 27) goes on tab II (the fight) at position 1", StringComparison.Ordinal) && m.Contains("AddSpellFavorite 0x01E3", StringComparison.Ordinal));
        Assert.Equal(1, r.Api.PlayerRules.SpellsAdded);

        // The panel is told the bar and the position the cast is from.
        Assert.Equal(new SpellBarCast(BoltI, SpellBarRule.FightTab, 0), Assert.Single(ShownBy(r)));

        // The add was the automation's own action, on the action log by name.
        Assert.Contains(Logged(r), e => e.Kind == ActionKind.AddSpellFavorite && e.Source == ActionSource.Automation && e.Outcome.Kind == ActionOutcomeKind.Sent);
    }

    [Fact]
    public void A_spell_is_never_added_twice()
    {
        var r = Rig([BoltI, HealI]);
        r.Api.Cast(BoltI, Monster, ActionSource.Automation);
        r.Api.Cast(BoltI, Monster, ActionSource.Automation); // asked again before the first went out
        r.Tick();
        r.UseDone();
        r.Tick(3);
        r.Api.Cast(BoltI, Monster, ActionSource.Automation); // and once more when it is on the bar
        r.Tick();

        Assert.Single(r.H.Net.Of(FakeSendKind.AddSpellFavorite));
        Assert.Equal<uint>([BoltI], r.H.State.Self.Spellbook.Bar(SpellBarRule.FightTab));
        Assert.Equal(1, r.Api.PlayerRules.SpellsAdded);
    }

    [Fact]
    public void A_spell_the_server_already_holds_on_a_bar_is_cast_with_no_add_whatever_the_tab()
    {
        var r = Rig([BoltI, HealI], [HealI], [], [], [], [], [], [], [BoltI]); // the heal on tab I, the bolt on tab VIII: not the rule's tabs, and both stay
        r.Api.Cast(BoltI, Monster, ActionSource.Automation);
        r.Tick();

        Assert.Equal([FakeSendKind.CastTargetedSpell], Wire(r));
        Assert.Equal(new SpellBarCast(BoltI, 7, 0), Assert.Single(ShownBy(r)));
        Assert.Equal(0, r.Api.PlayerRules.SpellsAdded);
    }

    [Fact]
    public void A_higher_level_takes_its_lines_lower_levels_place_on_the_bar()
    {
        var r = Rig([BoltI, BoltII, HealI], [], [HealI, BoltI]);
        r.Api.Cast(BoltII, Monster, ActionSource.Automation);
        r.Tick();

        Assert.Equal([FakeSendKind.RemoveSpellFavorite, FakeSendKind.AddSpellFavorite, FakeSendKind.CastTargetedSpell], Wire(r));
        var remove = Assert.Single(r.H.Net.Of(FakeSendKind.RemoveSpellFavorite));
        Assert.Equal((BoltI, 1), (remove.Guid, remove.Index)); // spell, bar
        var add = Assert.Single(r.H.Net.Of(FakeSendKind.AddSpellFavorite));
        Assert.Equal((BoltII, 1, 1u), (add.Guid, add.Index, add.Amount)); // at the lower level's position on the same bar
        Assert.Equal<uint>([HealI, BoltII], r.H.State.Self.Spellbook.Bar(1));
        Assert.Contains(RuleLog(r, LogSeverity.Info), m => m.Contains("in the place of Flame Bolt I (spell 27)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_lower_level_whose_own_cast_still_waits_is_not_taken_off_the_bar_under_it()
    {
        var r = Rig([BoltI, BoltII], [], [BoltI]);
        var low = r.Api.Cast(BoltI, Monster, ActionSource.Automation); // asked, waiting for its turn
        r.Api.Cast(BoltII, Monster, ActionSource.Automation); // the higher level is asked before the lower one went out
        r.Tick();

        Assert.Empty(r.H.Net.Of(FakeSendKind.RemoveSpellFavorite)); // level I stays: its cast is from the bar
        Assert.Equal<uint>([BoltII, BoltI], r.H.State.Self.Spellbook.Bar(1)); // level II went in ahead of it
        Assert.Equal([FakeSendKind.CastTargetedSpell, FakeSendKind.AddSpellFavorite], Wire(r)); // in the order asked: level I's cast, then level II's add (its own cast waits for the lane)
        Assert.Equal(BoltI, (uint)r.H.Net.Of(FakeSendKind.CastTargetedSpell)[0].Index);
        Assert.False(low.IsCompleted);
        Assert.Empty(RuleLog(r, LogSeverity.Error));
    }

    [Fact]
    public void Under_the_kill_switch_nothing_is_put_on_a_bar()
    {
        var r = Rig([BoltI]);
        r.Api.KillSwitch = true;
        var cast = r.Api.Cast(BoltI, Monster, ActionSource.Automation);
        var drink = r.Api.Drink(0x80000B02, ActionSource.Automation);
        r.Tick();

        Assert.Equal(ActionApi.KillSwitchRefusal, cast.Outcome!.Value.Detail);
        Assert.Equal(ActionApi.KillSwitchRefusal, drink.Outcome!.Value.Detail);
        Assert.Empty(Wire(r));
        Assert.Equal((0, 0), (r.Api.PlayerRules.SpellsAdded, r.Api.PlayerRules.ShortcutsAdded));
        Assert.DoesNotContain(r.H.Log.Entries, e => e.Category == PlayerRulesGate.LogCategory);
    }

    [Fact]
    public void A_spell_that_is_not_in_the_spellbook_is_refused_by_name_and_nothing_is_sent()
    {
        var r = Rig([BoltI]);
        var cast = r.Api.Cast(Unknown, Monster, ActionSource.Automation);
        r.Tick();

        Assert.Empty(Wire(r)); // no add (retail and ACE take a known spell only) and no cast
        var outcome = cast.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Refused, outcome.Kind);
        Assert.StartsWith("player rules: Frost Bolt I (spell 4000) is not in the spellbook", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("--autopilot-player-rules=off", outcome.Detail, StringComparison.Ordinal);
        Assert.Equal(outcome.Detail, Assert.Single(RuleLog(r, LogSeverity.Error))); // loud, by name, once
        Assert.Empty(ShownBy(r));

        var again = r.Api.Cast(Unknown, Monster, ActionSource.Automation);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, again.Outcome!.Value.Kind);
        Assert.Single(RuleLog(r, LogSeverity.Error)); // the second refusal is on the handle, not a second error line
        Assert.Equal(2, r.Api.PlayerRules.Refusals);
    }

    [Fact]
    public void A_known_spell_that_is_on_no_bar_at_the_casts_turn_is_refused_by_name()
    {
        // The before-check of the cast, asked directly: the spell is known and on no bar (its add did not take).
        var r = Rig([BoltI]);
        var refusal = r.Api.PlayerRules.CastRefusal(BoltI, ActionSource.Automation);
        Assert.StartsWith("player rules: Flame Bolt I (spell 27) is on no spell bar after its add was tried", refusal, StringComparison.Ordinal);
        Assert.Null(r.Api.PlayerRules.CastRefusal(BoltI, ActionSource.Ui));
    }

    [Fact]
    public void A_persons_cast_and_the_hosts_are_not_the_automations_and_are_left_alone()
    {
        var r = Rig([BoltI, HealI]);
        r.Api.Cast(BoltI, Monster, ActionSource.Ui);
        r.Tick();
        Assert.Equal([FakeSendKind.CastTargetedSpell], Wire(r)); // a person's cast: no add (the panel put it where the person wanted), no refusal
        Assert.Empty(r.H.State.Self.Spellbook.Bar(SpellBarRule.FightTab));
        Assert.Empty(ShownBy(r));

        var host = Rig([BoltI]);
        host.Api.Cast(Unknown, Monster, ActionSource.Host);
        host.Tick();
        Assert.Equal([FakeSendKind.CastTargetedSpell], Wire(host));
    }

    [Fact]
    public void With_the_rule_off_the_automation_casts_as_before()
    {
        var r = Rig([BoltI]);
        r.Api.PlayerRules.On = false; // --autopilot-player-rules=off
        r.Api.Cast(BoltI, Monster, ActionSource.Automation);
        r.Tick();
        r.UseDone();
        r.Tick(3);
        r.Api.Cast(Unknown, Monster, ActionSource.Automation);
        r.Tick();

        Assert.Equal([FakeSendKind.CastTargetedSpell, FakeSendKind.CastTargetedSpell], Wire(r)); // no add, no refusal, even for a spell the book lacks
        Assert.Empty(r.H.State.Self.Spellbook.Bar(SpellBarRule.FightTab));
        Assert.Empty(ShownBy(r));
        Assert.DoesNotContain(r.H.Log.Entries, e => e.Category == PlayerRulesGate.LogCategory);
    }

    [Fact]
    public void The_wielded_casters_own_spell_is_cast_by_using_the_caster_and_is_on_no_bar()
    {
        const uint Wand = 0x80000300;
        var r = Rig([BoltI]);
        r.H.Describe(new PlayerDescriptionMsg { Spells = new[] { BoltI }.ToImmutableDictionary(id => id, _ => 2f), Equipped = [new EquippedDesc(Wand, WorldActionRig.Wand, 0)], Vitals = [new VitalDesc(5, 100, 0, 0, 100)] });
        var basis = ReducerHarness.Create(Wand, ReducerHarness.Stamps(), null, name: "Wand of Frost");
        r.H.R.Reducer.OnCreateObject(r.H.R.Context, basis with { Weenie = basis.Weenie with { Header = WeenieHeaderFlags.Wielder, WielderId = WorldActionRig.Player, SpellId = (ushort)Unknown } }, isUpdate: false);
        r.CombatModeIs(CombatModes.Magic);
        r.Tick();

        r.Api.Cast(Unknown, Monster, ActionSource.Automation);
        r.Tick();

        Assert.Equal([FakeSendKind.CastTargetedSpell], Wire(r));
        Assert.Contains(RuleLog(r, LogSeverity.Info), m => m.Contains("is the wielded caster's own spell", StringComparison.Ordinal));
        Assert.Empty(RuleLog(r, LogSeverity.Error));
    }

    [Fact]
    public void Before_the_description_brings_the_bars_nothing_is_judged_and_that_is_named_once()
    {
        var r = new WorldActionRig(); // in the world, no PlayerDescription: a sparse rig
        r.CombatModeIs(CombatModes.Magic);
        r.Tick();
        r.Api.Cast(BoltI, ActionSource.Automation);
        r.Tick();
        r.UseDone();
        r.Tick(3);
        r.Api.Cast(BoltI, ActionSource.Automation);
        r.Tick();

        Assert.Equal([FakeSendKind.CastUntargetedSpell, FakeSendKind.CastUntargetedSpell], Wire(r));
        Assert.Contains("TODO(m6-15-bars-unread)", Assert.Single(RuleLog(r, LogSeverity.Warning)), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------------------------- drinks

    [Fact]
    public void A_drink_with_no_shortcut_is_put_on_its_slot_first_and_then_drunk()
    {
        var rig = new ShortcutRig();
        var used = new List<ShortcutUsed>();
        rig.R.H.R.Bus.Subscribe<ShortcutUsed>((in ShortcutUsed e) => used.Add(e));
        var drink = rig.Api.Drink(ShortcutRig.Potion, ActionSource.Automation);
        rig.R.Tick();

        Assert.Equal([FakeSendKind.AddShortcut, FakeSendKind.Use], Wire(rig.R));
        Assert.Equal("A1:potion", rig.Wire()); // the Potion of Healing's slot: the key 2
        Assert.Equal(ShortcutRig.Potion, rig.Bar[1].ObjectId);
        Assert.Equal(ShortcutRig.Potion, Assert.Single(rig.R.H.Net.Of(FakeSendKind.Use)).Guid);
        Assert.False(drink.IsCompleted);
        Assert.Equal(new ShortcutUsed(1, ShortcutRig.Potion, drink), Assert.Single(used)); // the toolbar is told the slot the drink is the use of
        Assert.Contains(RuleLog(rig.R, LogSeverity.Info), m => m.Contains("the Potion of Healing (weenie class 377) goes on slot 2 before its drink (AddShortcut 0x019C)", StringComparison.Ordinal));
        Assert.Contains(Logged(rig.R), e => e.Kind == ActionKind.AddShortcut && e.Source == ActionSource.Automation && e.Outcome.Kind == ActionOutcomeKind.Sent);
    }

    [Fact]
    public void The_drink_is_the_use_of_the_shortcuts_item_whichever_stack_was_asked_for()
    {
        const uint Second = 0x80000B40;
        var rig = new ShortcutRig((4, ShortcutRig.Potion));
        rig.Item(Second, 377, "Potion of Healing", 0x80, 3);
        rig.Into(Second, ShortcutRig.Player);
        rig.R.Tick();

        var drink = rig.Api.Drink(Second, ActionSource.Automation); // the fight found the other stack
        rig.R.Tick();

        Assert.Equal(ShortcutRig.Potion, Assert.Single(rig.R.H.Net.Of(FakeSendKind.Use)).Guid); // the stack on slot 5 is the one drunk
        Assert.Empty(rig.Adds);
        Assert.Contains(ActionText.Hex(ShortcutRig.Potion), drink.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stack_that_runs_out_and_is_bought_again_goes_back_to_the_same_slot()
    {
        const uint Bought = 0x80000B41;
        var rig = new ShortcutRig((9, ShortcutRig.Potion)); // not the table's slot: the person moved it
        rig.Api.Drink(ShortcutRig.Potion, ActionSource.Automation);
        rig.R.Tick();
        Assert.Empty(rig.Adds); // on the bar already

        rig.Gone(ShortcutRig.Potion); // the last one was drunk: the row leaves with its item (OpenAC #219)
        rig.R.Tick();
        Assert.True(rig.Bar[9].IsEmpty);

        rig.Item(Bought, 377, "Potion of Healing", 0x80, 10); // bought again
        rig.Into(Bought, ShortcutRig.Player);
        rig.R.Tick(); // the automation is at work: the held kind with no shortcut is asked for...
        rig.R.Tick(); // ...and sent

        Assert.Equal(Bought, rig.Bar[9].ObjectId);
        var add = Assert.Single(rig.Adds);
        Assert.Equal((9, Bought), (add.Index, add.Guid));
        Assert.Contains(RuleLog(rig.R, LogSeverity.Info), m => m.Contains("goes on slot 10 while it is held", StringComparison.Ordinal));
    }

    [Fact]
    public void While_the_automation_is_at_work_every_held_kind_of_drink_is_on_the_bar_and_a_person_playing_by_hand_is_left_alone()
    {
        const uint Mana = 0x80000B42, Draught = 0x80000B43, Tincture = 0x80000B44;
        var rig = new ShortcutRig((3, ShortcutRig.Dagger)); // the Mana Potion's own slot holds the person's dagger
        rig.Item(Mana, 379, "Mana Potion", 0x80, 4);
        rig.Into(Mana, ShortcutRig.Player);
        rig.Item(Draught, 2457, "Health Draught", 0x80, 2);
        rig.Into(Draught, ShortcutRig.Player);
        rig.Item(Tincture, DrinkBarRule.StaminaTinctureWcid, "Stamina Tincture", 0x80, 1);
        rig.Into(Tincture, ShortcutRig.Player);
        rig.R.Tick();
        rig.R.Tick();
        Assert.Empty(rig.Adds); // no automation action yet: nothing is put on a person's bar

        rig.Api.QueryHealth(ShortcutRig.Player, ActionSource.Automation); // the automation acts
        rig.R.Tick();
        rig.R.Tick();

        var bar = rig.Bar;
        Assert.Equal((Draught, ShortcutRig.Potion, ShortcutRig.Dagger, Mana, Tincture), (bar[0].ObjectId, bar[1].ObjectId, bar[3].ObjectId, bar[4].ObjectId, bar[7].ObjectId)); // the dagger keeps its slot; the Mana Potion sits on its right
        Assert.Equal(4, rig.Adds.Count);
        Assert.Equal(4, rig.Api.PlayerRules.ShortcutsAdded);

        rig.R.Tick();
        rig.R.Tick();
        Assert.Equal(4, rig.Adds.Count); // kept, not added again
    }

    [Fact]
    public void A_drink_that_cannot_go_on_the_bar_is_refused_by_name()
    {
        var rig = new ShortcutRig([.. Enumerable.Range(0, 18).Select(i => ((uint)i, 0x80000C00 + (uint)i))]); // every slot holds something else
        var drink = rig.Api.Drink(ShortcutRig.Potion, ActionSource.Automation);
        rig.R.Tick();

        Assert.Empty(rig.Adds);
        Assert.Empty(rig.R.H.Net.Of(FakeSendKind.Use));
        var outcome = drink.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Refused, outcome.Kind);
        Assert.StartsWith("player rules: the Potion of Healing is on no shortcut slot after its add was tried (the bar has no free slot)", outcome.Detail, StringComparison.Ordinal);
        Assert.Equal(outcome.Detail, Assert.Single(RuleLog(rig.R, LogSeverity.Error)));
    }

    [Fact]
    public void A_persons_drink_and_a_session_with_the_rule_off_are_left_alone()
    {
        var person = new ShortcutRig((1, ShortcutRig.Potion));
        var used = new List<ShortcutUsed>();
        person.R.H.R.Bus.Subscribe<ShortcutUsed>((in ShortcutUsed e) => used.Add(e));
        person.Api.Drink(ShortcutRig.Potion, ActionSource.Ui);
        person.R.Tick();
        Assert.Equal([FakeSendKind.Use], Wire(person.R));
        Assert.Empty(used); // a person's use is the toolbar's own: no notice

        var off = new ShortcutRig();
        off.Api.PlayerRules.On = false;
        off.Api.Drink(ShortcutRig.Potion, ActionSource.Automation);
        off.R.Tick();
        off.R.Tick();
        Assert.Equal([FakeSendKind.Use], Wire(off.R));
        Assert.True(off.Bar[1].IsEmpty);
    }

    [Fact]
    public void The_shortcut_keys_use_by_the_automation_passes_the_rule()
    {
        var rig = new ShortcutRig((5, ShortcutRig.Potion));
        var use = rig.Api.UseShortcut(5, ActionSource.Automation);
        rig.R.Tick();

        Assert.Equal(ActionKind.Drink, use.Kind);
        Assert.Equal([FakeSendKind.Use], Wire(rig.R));
        Assert.Empty(RuleLog(rig.R, LogSeverity.Error));
    }
}
