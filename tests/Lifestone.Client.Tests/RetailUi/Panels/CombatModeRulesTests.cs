using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.Ui;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// M4 4.1 lane v3, the stances: every wielded weapon kind gives its retail stance (ACE's <c>GetCombatStance</c>, <see cref="CombatStances"/>), no way into a combat mode shows a caster in a melee stance (the toolbar's rule, the key, the guard), a weapon
/// swap in a combat mode moves to the new weapon's mode (ACE's <c>TryShuffleStance</c>), and the offline stand-in's player stands in the stance by weapon, as ACE's server would send it.
/// </summary>
public sealed class CombatModeRulesTests
{
    private static readonly InboundContext Context = new(0, MessageQueue.SmartBox, 0);

    // A wielded setup in a read model, with the stack limit of the pieces that stack (thrown weapons: the description says so).
    private static GameState State(params (Piece Piece, ushort? Stack)[] items)
    {
        var harness = new ReducerHarness(track: false);
        harness.EnterWorld();
        harness.Reducer.OnPlayerDescription(
            harness.Context,
            new GameEventHeader(ReducerHarness.PlayerGuid, 1, GameEventTypes.PlayerDescription),
            new PlayerDescriptionMsg { Equipped = [.. items.Select(i => new EquippedDesc(i.Piece.Guid, (uint)i.Piece.Slot, 0))] });
        foreach (var (piece, stack) in items.Where(i => i.Piece.Present))
        {
            var basis = ReducerHarness.Create(piece.Guid, ReducerHarness.Stamps(), null, name: piece.Name, itemType: piece.ItemType);
            var weenie = piece.CombatUse is { } use ? basis.Weenie with { CombatUse = use, Header = basis.Weenie.Header | WeenieHeaderFlags.CombatUse } : basis.Weenie;
            if (stack is { } max)
            {
                weenie = weenie with { StackSize = max, MaxStackSize = max, Header = weenie.Header | WeenieHeaderFlags.StackSize | WeenieHeaderFlags.MaxStackSize };
            }

            harness.Reducer.OnCreateObject(harness.Context, basis with { Weenie = weenie }, isUpdate: false);
        }

        return harness.State;
    }

    private static (Piece, ushort?) P(Piece piece) => (piece, null);

    private static List<ActionLogEntry> ModeAsks(GameSession session)
    {
        var asks = new List<ActionLogEntry>();
        for (var i = 0; i < session.ActionLog.Count; i++)
        {
            if (session.ActionLog[i].Kind == ActionKind.SetCombatMode)
            {
                asks.Add(session.ActionLog[i]);
            }
        }

        return asks;
    }

    private static readonly Piece StackedDarts = WieldedSetups.Darts;

    private static readonly Piece GreatswordInMeleeSlot = new(0x80000B01, "Greatsword", 0x1, 5, EquipMask.MeleeWeapon);

    private static readonly Piece Dagger = new(0x80000B02, "Dagger", 0x1, 1, EquipMask.Shield);

    // ---- the stance of every wielded kind (GetCombatStance, Creature_Combat.cs 265-358) ----

    public static IEnumerable<object[]> StanceRows() =>
    [
        ["nothing in hand", CombatStances.HandCombat, false],
        ["a mace", CombatStances.SwordCombat, false],
        ["a two-handed weapon in its slot", CombatStances.TwoHandedSwordCombat, false],
        ["a two-handed weapon in the melee slot (CombatUse 5)", CombatStances.TwoHandedSwordCombat, false],
        ["a bow with arrows", CombatStances.BowCombat, true],
        ["arrows alone", CombatStances.HandCombat, false],
        ["stacked thrown weapons", CombatStances.ThrownWeaponCombat, false],
        ["a wand", CombatStances.Magic, false],
        ["a mace and a shield", CombatStances.SwordShieldCombat, false],
        ["thrown weapons and a shield", CombatStances.ThrownShieldCombat, false],
        ["a bow and a shield", CombatStances.BowCombat, true],
        ["a wand and a shield", CombatStances.Magic, false],
        ["a mace and a second weapon in the off hand", CombatStances.DualWieldCombat, false],
        ["an unknown mace", CombatStances.SwordCombat, false],
        ["a helm", CombatStances.HandCombat, false],
    ];

    private static GameState StateOf(string name) => name switch
    {
        "nothing in hand" => State(),
        "a mace" => State(P(WieldedSetups.Mace)),
        "a two-handed weapon in its slot" => State(P(WieldedSetups.TwoHanded)),
        "a two-handed weapon in the melee slot (CombatUse 5)" => State(P(GreatswordInMeleeSlot)),
        "a bow with arrows" => State(P(WieldedSetups.Bow), P(WieldedSetups.Arrows)),
        "arrows alone" => State(P(WieldedSetups.Arrows)),
        "stacked thrown weapons" => State((StackedDarts, 250)),
        "a wand" => State(P(WieldedSetups.Wand)),
        "a mace and a shield" => State(P(WieldedSetups.Mace), P(WieldedSetups.Shield)),
        "thrown weapons and a shield" => State((StackedDarts, 250), P(WieldedSetups.Shield)),
        "a bow and a shield" => State(P(WieldedSetups.Bow), P(WieldedSetups.Shield)),
        "a wand and a shield" => State(P(WieldedSetups.Wand), P(WieldedSetups.Shield)),
        "a mace and a second weapon in the off hand" => State(P(WieldedSetups.Mace), P(Dagger)),
        "an unknown mace" => State(P(WieldedSetups.UnknownMace)),
        "a helm" => State(P(WieldedSetups.UnknownHelm)),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    [Theory]
    [MemberData(nameof(StanceRows))]
    public void Every_wielded_kind_stands_in_the_stance_ACE_gives_it(string name, uint expected, bool assumed)
    {
        var state = StateOf(name);

        Assert.Equal(CombatStances.Name(expected), CombatStances.Name(CombatStances.Of(state, out var wasAssumed)));
        Assert.Equal(assumed, wasAssumed);
        Assert.Equal(CombatStances.NonCombat, CombatStances.ForMode(CombatModes.NonCombat, state, out _)); // peace is the one mode whatever is in the hands
        Assert.Equal(CombatStances.NonCombat, CombatStances.ForMode(0, state, out _));
        Assert.Equal(expected, CombatStances.ForMode(CombatModes.Melee, state, out _)); // the weapon decides, the combat mode only gates
    }

    [Fact]
    public void A_caster_in_the_hand_is_never_in_a_melee_stance_and_a_melee_weapon_is_never_in_the_magic_one()
    {
        foreach (var c in WieldedSetups.All.Where(c => c.Pieces.Any(p => p.Slot == EquipMask.Held && p.Present)))
        {
            var state = WieldedSetups.World(c.Pieces).State;
            Assert.Equal(CombatStances.Magic, CombatStances.Of(state));
        }

        foreach (var c in WieldedSetups.All.Where(c => c.Pieces.All(p => p.Slot != EquipMask.Held)))
        {
            Assert.NotEqual(CombatStances.Magic, CombatStances.Of(WieldedSetups.World(c.Pieces).State));
        }
    }

    // ---- the mode the toolbar and the key ask for, and what the server settles on (GetDefaultCombatMode, HandleActionChangeCombatMode) ----

    [Theory]
    [MemberData(nameof(WieldedSetups.Names), MemberType = typeof(WieldedSetups))]
    public void The_pick_and_the_settle_of_every_wielded_setup_fit_what_is_wielded_and_the_toggle_never_asks_what_the_guard_refuses(string name)
    {
        // One theory over the setups (the P1 fold of the pick, the settle and the toggle theories; M4 3.1 g: the toolbar never asks for a mode the combat-mode guard refuses).
        var c = WieldedSetups.Find(name);
        var state = WieldedSetups.World(c.Pieces).State;
        var unknown = CombatModeGuard.Read(state).Unknown != 0;

        // What the server settles on: it keeps the mode asked for only when the weapons fit it, and otherwise puts the player in peace.
        Assert.Equal(CombatMode.NonCombat, CombatStances.Settle(CombatMode.NonCombat, state));
        Assert.Equal(c.Melee && !unknown ? CombatMode.Melee : CombatMode.NonCombat, CombatStances.Settle(CombatMode.Melee, state));
        Assert.Equal(c.Magic && !unknown ? CombatMode.Magic : CombatMode.NonCombat, CombatStances.Settle(CombatMode.Magic, state));
        Assert.Equal(c.Missile && !unknown ? CombatMode.Missile : CombatMode.NonCombat, CombatStances.Settle(CombatMode.Missile, state));

        // What the toolbar and the key ask for.
        var pick = CombatModeRules.DefaultMode(state, out var blocked, out var missing);
        Assert.Equal(c.Pick, pick);
        Assert.Null(blocked);
        if (pick == CombatMode.NonCombat)
        {
            Assert.NotEqual(0u, missing); // peace only for want of an item's kind: the press asks nothing
            return;
        }

        Assert.Equal(0u, missing);
        Assert.Null(CombatModeGuard.Check(pick, state));
        Assert.True(CombatModeRules.Compatible(state, pick, out _)); // retail's CompatibleCombatMode agrees too
        Assert.Equal(pick, CombatStances.Settle(pick, state)); // the server would not send the player back to peace
        var stance = CombatStances.ForMode((int)pick, state, out _);
        uint[] family = pick switch
        {
            CombatMode.Magic => [CombatStances.Magic],
            CombatMode.Missile => [CombatStances.BowCombat, CombatStances.ThrownWeaponCombat, CombatStances.ThrownShieldCombat],
            _ => [CombatStances.HandCombat, CombatStances.SwordCombat, CombatStances.SwordShieldCombat, CombatStances.TwoHandedSwordCombat, CombatStances.DualWieldCombat],
        };
        Assert.Contains(stance, family);
    }

    private static (GameSession Session, Action<double> Run, UiScene Scene) OfflineWith(params Piece[] pieces)
    {
        var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0 };
        var time = new ManualTimeProvider();
        var session = GameSession.Offline(
            h =>
            {
                StandInSessions.CharacterSelect(h);
                StandInSessions.PlayerVisits(h, visit);
                StandInSessions.AnswerWorld(h, visit);
            },
            time,
            new RecordingLogSink());
        void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.1)
            {
                time.Advance(TimeSpan.FromSeconds(0.1));
                session.Tick(session.Clock.Now);
            }
        }

        Run(2.0);
        foreach (var piece in pieces)
        {
            WieldedSetups.Hold(session, piece);
        }

        return (session, Run, new UiScene([]));
    }

    [Fact]
    public void A_mace_and_a_wand_wielded_together_are_asked_no_combat_mode_by_the_toggle_and_the_chat_says_why()
    {
        // Retail's picker reads the melee slot first and gives melee; the pair is one ACE refuses to wield, but a saved character can hold it. The toggle must not ask: ACE would flash the wrong stance.
        var (session, run, scene) = OfflineWith(WieldedSetups.Mace, WieldedSetups.Wand);
        using (session)
        {
            Assert.Equal(CombatMode.Melee, CombatModeRules.DefaultMode(session.State, out _, out _));
            var chat = new List<string>();

            Assert.Null(CombatModeRules.Toggle(session, scene.Manager, chat.Add));

            run(2.0);
            Assert.Equal(["You can't enter melee mode while wielding the Wand of Flame"], chat);
            Assert.Empty(ModeAsks(session));
            Assert.True(session.State.Self.Qualities.CombatMode is 0 or CombatModes.NonCombat);
            Assert.Contains(scene.Log.Lines, l => l.Contains("MeleeWithCaster", StringComparison.Ordinal) || l.Contains("melee combat mode is refused", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("Mace", 2)]
    [InlineData("Wand", 8)]
    [InlineData("Bow", 4)]
    public void The_toggle_asks_the_mode_of_the_one_weapon_wielded_and_never_another(string which, int wireMode)
    {
        var piece = which switch { "Mace" => WieldedSetups.Mace, "Wand" => WieldedSetups.Wand, _ => WieldedSetups.Bow };
        var (session, _, scene) = OfflineWith(piece);
        using (session)
        {
            var handle = CombatModeRules.Toggle(session, scene.Manager, null);

            Assert.NotNull(handle);
            Assert.Contains("mode=" + wireMode, handle.Summary, StringComparison.Ordinal);
            Assert.Equal(ActionSource.Ui, handle.Source);
        }
    }

    // ---- the combat key (ClientCombatSystem::OnAction: input map 0x10000002, action 0x1000005A) ----

    [Fact]
    public void The_combat_key_runs_the_buttons_rule_on_a_press_only_and_leaves_the_other_keys_alone()
    {
        var (session, run, scene) = OfflineWith(WieldedSetups.Wand);
        using (session)
        using (var key = new CombatToggleKey(session, scene.Manager, () => null))
        {
            scene.Input.Bindings = new RetailKeyBindings(
                [new RetailBinding(CombatToggleKey.InputMap, CombatToggleKey.ToggleAction, RetailKeyBindings.Keyboard(DikScanCodes.Grave), 0, 3)],
                []);

            Assert.False(scene.Tap(DikScanCodes.A)); // a key nothing binds is not taken
            Assert.Equal(0, key.Presses);
            Assert.True(scene.Tap(DikScanCodes.Grave));

            Assert.Equal(1, key.Presses); // the key up is not a second press
            run(1.0);
            var asked = Assert.Single(ModeAsks(session));
            Assert.Equal(ActionSource.Ui, asked.Source);
            Assert.Contains("mode=8", asked.Summary, StringComparison.Ordinal);
        }

        Assert.Equal((0x10000002u, 0x1000005Au), (CombatToggleKey.InputMap, CombatToggleKey.ToggleAction)); // retail's map and action
    }

    // ---- the weapon swap in a combat mode (TryShuffleStance, Player_Inventory.cs 252-290; the dequip swap 436-452) ----

    [Theory]
    [InlineData(CombatModes.Melee, EquipMask.Held, "Orb", CombatMode.Magic)] // an orb wielded while in melee: ACE moves to the orb's mode
    [InlineData(CombatModes.Melee, EquipMask.Held, "Wand", CombatMode.Magic)]
    [InlineData(CombatModes.Magic, EquipMask.MeleeWeapon, "Mace", CombatMode.Melee)]
    [InlineData(CombatModes.Melee, EquipMask.MissileWeapon, "Bow", CombatMode.Missile)]
    [InlineData(CombatModes.Missile, EquipMask.MeleeWeapon, "Mace", CombatMode.Melee)]
    [InlineData(CombatModes.Missile, EquipMask.TwoHanded, "TwoHanded", CombatMode.Melee)]
    [InlineData(CombatModes.Melee, EquipMask.Shield, "MaceAndShield", CombatMode.Melee)]
    [InlineData(CombatModes.Missile, EquipMask.Shield, "DartsAndShield", CombatMode.Missile)] // a shield beside thrown weapons stays missile
    [InlineData(CombatModes.NonCombat, EquipMask.Held, "Wand", null)] // peace: nothing moves
    [InlineData(CombatModes.Melee, EquipMask.HeadWear, "Mace", null)] // armour is no weapon swap
    [InlineData(CombatModes.Melee, EquipMask.MissileAmmo, "Bow", null)] // arrows are not a selectable slot
    public void A_wield_in_a_combat_mode_moves_to_the_new_weapons_mode(int mode, EquipMask wielded, string setup, CombatMode? expected)
    {
        var state = SetupState(setup);

        Assert.Equal(expected, CombatStances.ModeAfterWield((uint)wielded, mode, state));
    }

    private static GameState SetupState(string setup) => setup switch
    {
        "Orb" => State(P(WieldedSetups.Orb)),
        "Wand" => State(P(WieldedSetups.Wand)),
        "Mace" => State(P(WieldedSetups.Mace)),
        "Bow" => State(P(WieldedSetups.Bow), P(WieldedSetups.Arrows)),
        "TwoHanded" => State(P(WieldedSetups.TwoHanded)),
        "MaceAndShield" => State(P(WieldedSetups.Mace), P(WieldedSetups.Shield)),
        "DartsAndShield" => State((StackedDarts, 250), P(WieldedSetups.Shield)),
        "Arrows" => State(P(WieldedSetups.Arrows)),
        "Darts" => State((StackedDarts, 250)),
        "Nothing" => State(),
        _ => throw new ArgumentOutOfRangeException(nameof(setup), setup, null),
    };

    [Theory]
    [InlineData(CombatModes.Melee, EquipMask.MeleeWeapon, "Nothing", CombatMode.Melee)] // the mace went to the pack: fists
    [InlineData(CombatModes.Magic, EquipMask.Held, "Nothing", CombatMode.Melee)] // the wand went to the pack: melee with hand combat, not magic
    [InlineData(CombatModes.Missile, EquipMask.MissileWeapon, "Arrows", CombatMode.Melee)]
    [InlineData(CombatModes.Missile, EquipMask.MissileAmmo, "Bow", CombatMode.NonCombat)] // the arrows left a missile stance: peace
    [InlineData(CombatModes.Melee, EquipMask.Shield, "Mace", CombatMode.Melee)]
    [InlineData(CombatModes.Missile, EquipMask.Shield, "Darts", CombatMode.Missile)]
    [InlineData(CombatModes.NonCombat, EquipMask.MeleeWeapon, "Nothing", null)]
    [InlineData(CombatModes.Melee, EquipMask.HeadWear, "Mace", null)]
    public void An_unwield_in_a_combat_mode_goes_to_melee_with_what_is_left_or_to_peace_for_the_arrows(int mode, EquipMask unwielded, string left, CombatMode? expected)
    {
        var state = SetupState(left);

        Assert.Equal(expected, CombatStances.ModeAfterUnwield((uint)unwielded, mode, state));
    }

    // ---- the stand-in's player (the offline frames): the stance motion by weapon, then the mode ----

    private sealed class StanceRig : IDisposable
    {
        public const uint MaceWcid = 90001;
        public const uint BowWcid = 90002;
        public const uint ArrowWcid = 90003;
        private readonly ManualTimeProvider _time = new();

        public StanceRig()
        {
            var visit = new StandInVisit
            {
                EnterAfter = 0.2,
                UnhideAfter = 1.0,
                Stats = new StandInPlayerStats(),
                Items = new Dictionary<uint, StandInItemKind>
                {
                    [MaceWcid] = new("Mace (stance test)", null),
                    [BowWcid] = new("Bow (stance test)", null),
                    [ArrowWcid] = new("Arrow (stance test)", 250),
                },
            };
            Session = GameSession.Offline(
                h =>
                {
                    StandInSessions.CharacterSelect(h);
                    StandInSessions.PlayerVisits(h, visit);
                    StandInSessions.AnswerWorld(h, visit);
                },
                _time,
                new RecordingLogSink());
            Run(2.0);
        }

        public GameSession Session { get; }

        public int Mode => Session.State.Self.Qualities.CombatMode;

        /// <summary>The stance word the server last sent for the player, or null.</summary>
        public ushort? Stance => Session.State.Objects.TryGet(Session.State.Self.Guid, out var player) && player.Motion is { } motion ? motion.Stance : null;

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
            }
        }

        public uint Spawn(uint wcid)
        {
            var spawn = Session.Tools.SpawnItem(wcid, null, ActionSource.Host);
            Run(1.5);
            Assert.True(spawn.IsCompleted, spawn.Summary);
            return spawn.Result!.Guid;
        }

        public void Do(ActionHandle handle)
        {
            Run(2.0);
            Assert.True(handle.IsCompleted, handle.Summary);
            Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        }

        public uint Wield(uint wcid, EquipMask slot)
        {
            var guid = Spawn(wcid);
            Do(Session.Actions.Wield(guid, slot));
            return guid;
        }

        public void Enter(CombatMode mode) => Do(Session.Actions.SetCombatMode(mode, ActionSource.Host));

        public void AssertStance(uint stance, int mode)
        {
            Assert.Equal(CombatStances.Name(stance), Stance is { } wire ? CombatStances.Name(MotionCommandTable.FromWire(wire)) : "none");
            Assert.Equal(mode, Mode);
        }

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void The_stand_ins_player_stands_in_the_stance_of_each_weapon_in_its_combat_mode()
    {
        using var rig = new StanceRig();
        Assert.Null(rig.Stance); // nothing has been said yet

        rig.Enter(CombatMode.Melee);
        rig.AssertStance(CombatStances.HandCombat, CombatModes.Melee); // HandCombat only with nothing in the hands
        rig.Enter(CombatMode.NonCombat);
        rig.AssertStance(CombatStances.NonCombat, CombatModes.NonCombat);

        var mace = rig.Wield(StanceRig.MaceWcid, EquipMask.MeleeWeapon);
        rig.AssertStance(CombatStances.NonCombat, CombatModes.NonCombat); // a wield in peace moves nothing
        rig.Enter(CombatMode.Melee);
        rig.AssertStance(CombatStances.SwordCombat, CombatModes.Melee);
        rig.Enter(CombatMode.NonCombat);
        rig.Do(rig.Session.Actions.Unwield(mace));

        rig.Wield(StanceRig.ArrowWcid, EquipMask.MissileAmmo);
        rig.Wield(StanceRig.BowWcid, EquipMask.MissileWeapon);
        rig.Enter(CombatMode.Missile);
        rig.AssertStance(CombatStances.BowCombat, CombatModes.Missile);
    }

    [Fact]
    public void The_stand_ins_wand_stands_in_the_magic_stance_and_a_melee_request_with_it_is_refused_before_any_stance_changes()
    {
        using var rig = new StanceRig();
        rig.Wield(GearKit.Wand.Wcid, EquipMask.Held);
        rig.Enter(CombatMode.Magic);
        rig.AssertStance(CombatStances.Magic, CombatModes.Magic);
        rig.Enter(CombatMode.NonCombat);

        var melee = rig.Session.Actions.SetCombatMode(CombatMode.Melee, ActionSource.Host);
        rig.Run(1.0);

        Assert.Equal(ActionOutcomeKind.Refused, melee.Outcome!.Value.Kind); // the guard: nothing is sent, so the avatar never leaves the peace stance
        rig.AssertStance(CombatStances.NonCombat, CombatModes.NonCombat);
    }

    [Fact]
    public void A_weapon_swap_in_a_combat_mode_moves_the_stand_ins_player_to_the_new_weapons_mode_and_stance()
    {
        using var rig = new StanceRig();
        var mace = rig.Wield(StanceRig.MaceWcid, EquipMask.MeleeWeapon);
        rig.Enter(CombatMode.Melee);
        rig.AssertStance(CombatStances.SwordCombat, CombatModes.Melee);

        rig.Do(rig.Session.Actions.Unwield(mace)); // the mace to the pack in melee: fists
        rig.AssertStance(CombatStances.HandCombat, CombatModes.Melee);

        var wand = rig.Wield(GearKit.Wand.Wcid, EquipMask.Held); // the wand wielded in melee: magic, not a melee stance with a wand
        rig.AssertStance(CombatStances.Magic, CombatModes.Magic);

        rig.Do(rig.Session.Actions.Unwield(wand)); // the wand to the pack in magic: melee with hand combat
        rig.AssertStance(CombatStances.HandCombat, CombatModes.Melee);

        rig.Wield(StanceRig.ArrowWcid, EquipMask.MissileAmmo); // arrows in melee: no swap
        rig.AssertStance(CombatStances.HandCombat, CombatModes.Melee);
        rig.Wield(StanceRig.BowWcid, EquipMask.MissileWeapon); // the bow wielded in melee: missile, BowCombat
        rig.AssertStance(CombatStances.BowCombat, CombatModes.Missile);
    }

    [Fact]
    public void A_mode_the_wielded_weapons_do_not_fit_ends_in_peace_in_the_stand_in_as_it_does_on_ACE()
    {
        // The stand-in's own server rule (EnterMode): a request the guard did not stop (a test or a tool) still ends in peace with the peace stance, never a melee stance with a caster.
        using var rig = new StanceRig();
        rig.Wield(GearKit.Wand.Wcid, EquipMask.Held);
        var responder = rig.Session.Reducer.LocalResponder!;

        responder.World!.ChangeCombatMode((uint)CombatMode.Melee);
        rig.Run(1.0);

        rig.AssertStance(CombatStances.NonCombat, CombatModes.NonCombat);
    }
}
