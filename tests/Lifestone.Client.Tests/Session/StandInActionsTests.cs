using Lifestone.Client.Session.Actions.Trackers;
using System.Collections.Immutable;
using DatReaderWriter.Enums;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Magic;
using Lifestone.Data.Tests;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 1.2 s4c: the stand-in answers every new action as ACE does (<c>StandInWorld.Actions.cs</c>), so the trackers complete against an offline session: a self cast (the 02C2 then UseDone),
/// a projectile cast (a Missile CreateObject then UseDone), the fizzle (028A 0x0402, UseDone), a combat-mode change (02CD for property 40), a wield (02DA, 02CE, 0023), an unwield and a move to the pack
/// (the zeros of a worn item, 02DA, 0022).
/// </summary>
public sealed class StandInActionsTests
{
    private const uint Strength = 2;
    private const uint FlameBolt = 27;
    private const uint Fizzler = 4325;
    private const uint Sword = 100;

    private static readonly SpellInfo StrengthSelf = WorldActionRig.Spell(Strength, SpellType.Enchantment) with { Name = "Strength Self I", Category = 7, Power = 1, Duration = 1800.0 };
    private static readonly SpellInfo Bolt = WorldActionRig.Spell(FlameBolt, SpellType.Projectile) with { Name = "Flame Bolt I" };
    private static readonly SpellInfo Level8 = WorldActionRig.Spell(Fizzler, SpellType.Enchantment) with { Name = "Strength Self VIII", Category = 7, Power = 400, Duration = 1800.0 };

    private static StandInVisit Visit() => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        Stats = new StandInPlayerStats(),
        Items = new Dictionary<uint, StandInItemKind> { [Sword] = new("Sword", null) },
    };

    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();

        public Rig(StandInVisit? visit = null, bool lookup = true)
        {
            var v = visit ?? Visit();
            Session = GameSession.Offline(
                h =>
                {
                    StandInSessions.CharacterSelect(h);
                    StandInSessions.PlayerVisits(h, v);
                    StandInSessions.AnswerWorld(h, v);
                },
                _time,
                new RecordingLogSink());
            if (lookup)
            {
                var spells = new[] { StrengthSelf, Bolt, Level8 }.ToImmutableDictionary(s => s.Id);
                World.SpellLookup = id => spells.GetValueOrDefault(id);
                Session.ActionApi.SpellLookup = World.SpellLookup;
            }

            Session.Events.Subscribe((in ObjectCreated e) => Created.Add(e.Guid));
            Session.Events.Subscribe((in InventoryChanged e) =>
            {
                if (e.Kind != InventoryChangeKind.Reset)
                {
                    Changes.Add(e);
                }
            });
            Run(2.0);
        }

        public GameSession Session { get; }

        public StandInWorld World => Session.Reducer.LocalResponder!.World!;

        public StandInResponder Responder => Session.Reducer.LocalResponder!;

        public PlayerQualities Q => Session.State.Self.Qualities;

        public List<uint> Created { get; } = [];

        public List<InventoryChanged> Changes { get; } = [];

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
            }
        }

        /// <summary>Runs until <paramref name="handle"/> completes (the stand-in answers within a second or two) and a little more, so UseDone has gone.</summary>
        public ActionHandle Finish(ActionHandle handle, double seconds = 3.0)
        {
            Run(seconds);
            Assert.True(handle.IsCompleted, handle.Summary);
            return handle;
        }

        public void Learn(params uint[] ids)
        {
            var learn = Session.Tools.LearnSpells(ids, ActionSource.Host);
            Run(1.5);
            Assert.True(learn.IsCompleted, learn.Summary);
        }

        public void Mode(CombatMode mode)
        {
            Finish(Session.Actions.SetCombatMode(mode, ActionSource.Host));
        }

        public uint Spawn(uint wcid)
        {
            var spawn = Session.Tools.SpawnItem(wcid, null, ActionSource.Host);
            Run(1.5);
            Assert.True(spawn.IsCompleted, spawn.Summary);
            return spawn.Result!.Guid;
        }

        public uint EffectiveStrength()
        {
            var a = Q.Attributes[1];
            return EnchantmentRules.EnchantAttribute(Q.Enchantments, 1, a.Start + a.Ranks);
        }

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void A_self_cast_adds_the_enchantment_then_uses_done_and_the_registry_holds_the_spell()
    {
        using var rig = new Rig();
        rig.Learn(Strength);
        rig.Mode(CombatMode.Magic);
        var before = rig.EffectiveStrength();
        rig.World.AnswerLog.Clear();

        var cast = rig.Session.Actions.Cast(Strength, ActionSource.Host);
        rig.Finish(cast);

        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Equal(["02C2", "01C7"], rig.World.AnswerLog);
        var entry = Assert.Single(rig.Q.Enchantments, e => e.SpellId == Strength);
        Assert.Equal((1, 7, 1u, 1800.0, rig.Session.State.Self.Guid), (entry.Layer, entry.SpellCategory, entry.PowerLevel, entry.Duration, entry.Caster));
        Assert.Equal(before + 10, rig.EffectiveStrength());
    }

    [Fact]
    public void A_second_cast_of_the_same_spell_refreshes_the_layer_and_a_stronger_one_takes_the_next_layer()
    {
        using var rig = new Rig();
        rig.Learn(Strength, Fizzler - 1);
        rig.Mode(CombatMode.Magic);
        Assert.Equal(ActionOutcomeKind.Confirmed, rig.Finish(rig.Session.Actions.Cast(Strength, ActionSource.Host)).Outcome!.Value.Kind);

        var again = rig.Finish(rig.Session.Actions.Cast(Strength, ActionSource.Host));

        Assert.Equal(ActionOutcomeKind.Confirmed, again.Outcome!.Value.Kind);
        Assert.Contains("refreshed", again.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal([1], rig.Q.Enchantments.Where(e => e.SpellId == Strength).Select(e => (int)e.Layer));
    }

    [Fact]
    public void A_projectile_cast_creates_a_labelled_missile_near_the_player_then_uses_done_and_the_placeholder_goes()
    {
        using var rig = new Rig();
        rig.Learn(FlameBolt);
        rig.Mode(CombatMode.Magic);
        rig.World.AnswerLog.Clear();
        const uint target = 0x7A000001;

        var cast = rig.Session.Actions.Cast(FlameBolt, target, ActionSource.Host);
        for (var i = 0; i < 40 && !rig.Created.Exists(g => g >= StandInWorld.FirstProjectileGuid); i++)
        {
            rig.Run(0.1);
        }

        var guid = Assert.Single(rig.Created, g => g >= StandInWorld.FirstProjectileGuid);
        Assert.True(rig.Session.State.Objects.TryGet(guid, out var missile));
        Assert.Contains("(stand-in placeholder)", missile.Name, StringComparison.Ordinal);
        Assert.NotEqual(0, (int)(missile.State & Lifestone.Net.Types.PhysicsState.Missile));
        Assert.True(missile.Position is not null && rig.Session.State.Self.Position is not null);
        rig.Run(3.0);
        Assert.True(cast.IsCompleted, cast.Summary);
        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Equal(1, rig.World.Projectiles);
        Assert.Equal(["F745", "F747", "01C7"], rig.World.AnswerLog.Where(m => m is "F745" or "F747" or "01C7"));
        Assert.False(rig.Session.State.Objects.TryGet(guid, out _));
    }

    [Fact]
    public void A_spell_the_character_cannot_cast_fizzles_with_the_error_and_the_lane_frees_after_use_done()
    {
        using var rig = new Rig();
        rig.Learn(Fizzler, Strength);
        rig.Mode(CombatMode.Magic);
        rig.World.AnswerLog.Clear();

        var fizzle = rig.Session.Actions.Cast(Fizzler, ActionSource.Host);
        rig.Finish(fizzle);

        Assert.Equal(ActionOutcomeKind.ServerError, fizzle.Outcome!.Value.Kind);
        Assert.Equal(StandInWorld.FizzleError, fizzle.Outcome.Value.Code);
        Assert.Equal(["028A", "01C7"], rig.World.AnswerLog);
        Assert.Equal(1, rig.World.Fizzles);
        Assert.Empty(rig.Q.Enchantments.Where(e => e.SpellId == Fizzler));
        var next = rig.Finish(rig.Session.Actions.Cast(Strength, ActionSource.Host));
        Assert.Equal(ActionOutcomeKind.Confirmed, next.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_spell_the_character_does_not_know_ends_with_the_invalid_spell_use_done_and_a_cast_outside_magic_mode_ends_bare()
    {
        using var rig = new Rig();
        rig.Mode(CombatMode.Magic);
        var unknown = rig.Finish(rig.Session.Actions.Cast(Strength, ActionSource.Host));
        Assert.Equal((ActionOutcomeKind.ServerError, StandInWorld.MagicInvalidSpellType), (unknown.Outcome!.Value.Kind, unknown.Outcome.Value.Code));

        rig.Learn(Strength);
        rig.Mode(CombatMode.NonCombat);
        var guarded = rig.Finish(rig.Session.Actions.Cast(Strength, ActionSource.Host), 0.5); // the stance guard (M3 4.2 p3): refused at once, nothing sent
        Assert.Equal(ActionOutcomeKind.Refused, guarded.Outcome!.Value.Kind);
        rig.Session.RetailCastAnyMode = true; // retail's client sends it; the stand-in's ACE ends it bare (this rig's session only)
        var peace = rig.Finish(rig.Session.Actions.Cast(Strength, ActionSource.Host));

        Assert.Equal((ActionOutcomeKind.ServerError, 0u), (peace.Outcome!.Value.Kind, peace.Outcome.Value.Code));
        Assert.Contains("not in the magic combat mode", peace.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(rig.Q.Enchantments.Where(e => e.SpellId == Strength));
    }

    [Fact]
    public void A_combat_mode_change_answers_with_the_private_update_of_property_40_and_back()
    {
        using var rig = new Rig();
        Assert.Equal(0, rig.Q.CombatMode); // the description carries no mode: nothing has said one yet

        var magic = rig.Session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        rig.Finish(magic, 1.5);
        Assert.Equal((ActionOutcomeKind.Confirmed, CombatModes.Magic), (magic.Outcome!.Value.Kind, rig.Q.CombatMode));

        var peace = rig.Session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        rig.Finish(peace, 1.5);
        Assert.Equal((ActionOutcomeKind.Confirmed, CombatModes.NonCombat), (peace.Outcome!.Value.Kind, rig.Q.CombatMode));
        Assert.Equal(["02CD", "02CD"], rig.World.AnswerLog);
        Assert.Equal(ActionOutcomeKind.Local, rig.Finish(rig.Session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host), 0.5).Outcome!.Value.Kind);
        Assert.Equal(2, rig.World.AnswerLog.Count);
    }

    [Fact]
    public void A_wielded_wand_becomes_the_players_child_at_the_right_hand_and_armour_sends_no_parent_event()
    {
        using var rig = new Rig();
        var wand = rig.Spawn(Lifestone.Client.Session.Mage.GearKit.Wand.Wcid);
        var coat = rig.Spawn(Lifestone.Client.Session.Mage.GearKit.Armour.Single(a => a.Name == "Greater Amuli Shadow Coat").Wcid);
        rig.World.AnswerLog.Clear();

        rig.Finish(rig.Session.Actions.Wield(wand, EquipMask.Held, ActionSource.Host));

        Assert.Equal(["F749", "02DA", "02CE", "0023"], rig.World.AnswerLog);
        Assert.True(rig.Session.State.Objects.TryGet(wand, out var held));
        Assert.Equal(rig.Session.State.Self.Guid, held.Parent!.Value.ParentGuid);
        Assert.Equal(1, held.Parent.Value.Location);
        Assert.Null(held.Position);
        rig.World.AnswerLog.Clear();

        rig.Finish(rig.Session.Actions.Wield(coat, Lifestone.Client.Session.Mage.GearKit.Armour.Single(a => a.Name == "Greater Amuli Shadow Coat").ValidLocations, ActionSource.Host));

        Assert.Equal(["02DA", "02CE", "0023"], rig.World.AnswerLog);
    }

    [Fact]
    public void A_wield_sends_the_zeros_then_the_location_and_an_unwield_puts_the_item_back_in_the_pack()
    {
        using var rig = new Rig();
        var sword = rig.Spawn(Sword);
        rig.World.AnswerLog.Clear();

        var wield = rig.Finish(rig.Session.Actions.Wield(sword, EquipMask.MeleeWeapon, ActionSource.Host));

        Assert.Equal(ActionOutcomeKind.Confirmed, wield.Outcome!.Value.Kind);
        Assert.Equal(["F749", "02DA", "02CE", "0023"], rig.World.AnswerLog); // ACE broadcasts the parent event first (Creature_Equipment.cs 362-363)
        Assert.True(rig.Session.State.Objects.TryGet(sword, out var held) && held.Parent is { ParentGuid: var holder, Location: 1 } && holder == rig.Session.State.Self.Guid, "the wielded sword is the player's child at the right hand");
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.Equipped, 0, -1, (uint)EquipMask.MeleeWeapon), rig.Session.State.Self.Inventory.Find(sword));
        Assert.Empty(rig.Session.State.Self.Inventory.Pack);
        Assert.Contains(sword, rig.World.Wielded.Keys);
        rig.World.AnswerLog.Clear();

        var unwield = rig.Finish(rig.Session.Actions.Unwield(sword, ActionSource.Host));

        Assert.Equal(ActionOutcomeKind.Confirmed, unwield.Outcome!.Value.Kind);
        Assert.Equal(["02DA", "02CE", "02DA", "0022"], rig.World.AnswerLog);
        Assert.Equal(new InventoryPlace(InventoryPlaceKind.MainPack, rig.Session.State.Self.Guid, 0, 0), rig.Session.State.Self.Inventory.Find(sword));
        Assert.Equal([sword], rig.Session.State.Self.Inventory.Pack.Select(e => e.Guid));
        Assert.Contains(rig.World.Pack, e => e.Guid == sword);
        Assert.Empty(rig.World.Wielded);
    }

    [Fact]
    public void A_move_to_the_pack_of_a_worn_item_and_of_a_held_one_both_answer_the_put_with_the_container()
    {
        using var rig = new Rig();
        var first = rig.Spawn(Sword);
        var second = rig.Spawn(Sword);
        rig.Finish(rig.Session.Actions.Wield(first, EquipMask.MeleeWeapon, ActionSource.Host));
        rig.World.AnswerLog.Clear();

        var worn = rig.Finish(rig.Session.Actions.MoveToPack(first, 0, 0, ActionSource.Host));
        Assert.Equal(ActionOutcomeKind.Confirmed, worn.Outcome!.Value.Kind);
        Assert.Equal(["02DA", "02CE", "02DA", "0022"], rig.World.AnswerLog);
        Assert.Equal(rig.Session.State.Self.Guid, rig.Changes[^1].Container);

        rig.World.AnswerLog.Clear();
        var held = rig.Finish(rig.Session.Actions.MoveToPack(second, 0, 0, ActionSource.Host));
        Assert.Equal(ActionOutcomeKind.Confirmed, held.Outcome!.Value.Kind);
        Assert.Equal(["02DA", "0022"], rig.World.AnswerLog);
        Assert.Equal([second, first], rig.Session.State.Self.Inventory.Pack.Select(e => e.Guid));
    }

    [Fact]
    public void Every_action_is_recorded_and_the_five_kinds_are_answered_not_refused_with_not_supported()
    {
        using var rig = new Rig();
        rig.Learn(Strength);
        var item = rig.Spawn(Sword);
        rig.Mode(CombatMode.Magic);
        rig.Finish(rig.Session.Actions.Cast(Strength, ActionSource.Host));
        rig.Finish(rig.Session.Actions.Wield(item, EquipMask.MeleeWeapon, ActionSource.Host));
        rig.Finish(rig.Session.Actions.Unwield(item, ActionSource.Host));

        Assert.All([ActionKind.CastSpell, ActionKind.SetCombatMode, ActionKind.Wield, ActionKind.Unwield, ActionKind.MoveToPack], k => Assert.True(rig.Responder.Handles(k), k.ToString()));
        Assert.Equal(
            [GameActionTypes.ChangeCombatMode, GameActionTypes.CastUntargetedSpell, GameActionTypes.GetAndWieldItem, GameActionTypes.PutItemInContainer],
            rig.Responder.ActionSends.Select(s => s.Type));
    }

    [Fact]
    public void Without_the_worlds_answers_the_five_kinds_stay_refused_offline()
    {
        using var session = GameSession.Offline(h => StandInSessions.CharacterSelect(h), new ManualTimeProvider(), new RecordingLogSink());
        var responder = new StandInResponder(session.Reducer, 0.1);

        Assert.All([ActionKind.CastSpell, ActionKind.SetCombatMode, ActionKind.Wield, ActionKind.Unwield, ActionKind.MoveToPack], k => Assert.False(responder.Handles(k), k.ToString()));
        Assert.Throws<NotSupportedException>(() => responder.Session.SendChangeCombatMode(8));
    }

    [Fact]
    public void Without_a_spell_table_a_known_spell_is_a_placeholder_self_enchantment()
    {
        using var rig = new Rig(lookup: false);
        rig.Learn(2);
        rig.Mode(CombatMode.Magic);

        var cast = rig.Finish(rig.Session.Actions.Cast(2, ActionSource.Host));

        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Single(rig.Q.Enchantments, e => e.SpellId == 2);
    }
}

/// <summary>M3 1.2 s4c: the SpellTable host wiring and the stand-in's answers against the retail table (skipped without the dats).</summary>
public sealed class StandInActionsDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void A_session_given_the_dats_looks_a_spell_up_in_the_retail_table_and_one_without_knows_none()
    {
        using var without = GameSession.Offline(h => StandInSessions.CharacterSelect(h), new ManualTimeProvider(), new RecordingLogSink());
        Assert.Null(without.ActionApi.FindSpell(2));

        using var with = GameSession.Offline(h => StandInSessions.CharacterSelect(h), new ManualTimeProvider(), new RecordingLogSink());
        with.UseSpellsFrom(fixture.Dats);
        with.SpellTableLoaded.Wait(); // decoded on a worker (M3 1.2 fix R5)

        Assert.Equal("Strength Self I", with.ActionApi.FindSpell(2)?.Name);
        Assert.Null(with.ActionApi.FindSpell(99_999_999));
    }

    [RequiresDatsFact]
    public void The_retail_table_decides_the_stand_ins_cast_a_self_buff_a_bolt_and_a_level_eight_that_fizzles()
    {
        var table = SpellTableData.Load(fixture.Dats);
        uint Id(string name) => table.Spells.Values.Where(s => s.Name == name).OrderBy(s => s.Id).First().Id;
        var visit = Visit(table);
        using var session = GameSession.Offline(
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, visit);
                StandInSessions.AnswerWorld(h, visit);
            },
            clock,
            new RecordingLogSink());
        session.UseSpellsFrom(fixture.Dats);
        session.SpellTableLoaded.Wait(); // decoded on a worker (M3 1.2 fix R5)
        Run(session, 2.0);
        var buff = Id("Strength Self I");
        var bolt = Id("Flame Bolt I");
        const uint eight = 4325; // Incantation of Strength Self, power 400 (the arena profile pins it)
        Assert.Equal(400u, table.Spells[eight].Power);
        var learn = session.Tools.LearnSpells([buff, bolt, eight], ActionSource.Host);
        Run(session, 1.5);
        Assert.True(learn.IsCompleted, learn.Summary);
        var mode = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        Run(session, 1.5);
        Assert.Equal(ActionOutcomeKind.Confirmed, mode.Outcome!.Value.Kind);

        var cast = session.Actions.Cast(buff, ActionSource.Host);
        Run(session, 3.0);
        var shot = session.Actions.Cast(bolt, 0x7A000001u, ActionSource.Host);
        Run(session, 3.0);
        var fizzle = session.Actions.Cast(eight, ActionSource.Host);
        Run(session, 3.0);

        Assert.Equal(ActionOutcomeKind.Confirmed, cast.Outcome!.Value.Kind);
        Assert.Contains(session.State.Self.Qualities.Enchantments, e => e.SpellId == buff && e.Duration > 0 && e.PowerLevel == table.Spells[buff].Power);
        Assert.Equal(ActionOutcomeKind.Confirmed, shot.Outcome!.Value.Kind);
        Assert.Equal((ActionOutcomeKind.ServerError, StandInWorld.FizzleError), (fizzle.Outcome!.Value.Kind, fizzle.Outcome.Value.Code));
    }

    private readonly ManualTimeProvider clock = new();

    private static StandInVisit Visit(SpellTableData table) => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        Stats = new StandInPlayerStats(),
        SpellTable = table,
        ProjectileSetup = id => AceWorldExtract.Load().ProjectilesBySpell.TryGetValue(id, out var projectile) ? projectile.SetupId : null,
    };

    private void Run(GameSession session, double seconds)
    {
        for (var t = 0.0; t < seconds; t += 0.1)
        {
            clock.Advance(TimeSpan.FromSeconds(0.1));
            session.Tick(session.Clock.Now);
        }
    }
}
