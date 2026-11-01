using System.Collections.Immutable;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The combat system's mode maps from the keymap dat (M5 6 s1): melee 0x10000003, missile 0x10000004 and magic 0x10000005, every binding read from <c>gmDefaultMap</c> (KEYMAP 0x14000000) and none of them on Lifestone's F7, Scroll Lock or
/// Pause or the combat toggle's accent grave; the melee and missile keys (<see cref="CombatMapKeys"/>) registered in their modes only and driving the combat window's attack side as the mouse does (the press requests the height, the
/// release sends the attack through the Action API as <see cref="ActionSource.Ui"/>), and the power keys stepping the requested power by sixths. Offline: the stand-in session, the test plays the server's mode changes.
/// </summary>
public sealed class CombatMapKeysTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Player = 0x50000001;
    private const uint Target = 0x80000B07;
    private const uint Ctrl = 0x40000000;
    private static readonly InboundContext Context = new(0, MessageQueue.SmartBox, 0);

    /// <summary>Each binding of the three mode maps in the dat: (map, action, key, meta keys). Read from the dat and pinned here, so a dat that differs fails by name.</summary>
    public static TheoryData<uint, uint, int, uint> ModeMapBindings => new()
    {
        { CombatMapKeys.MeleeMap, CombatMapKeys.MeleePowerDown, DikScanCodes.Insert, 0 },
        { CombatMapKeys.MeleeMap, CombatMapKeys.MeleePowerUp, DikScanCodes.Prior, 0 },
        { CombatMapKeys.MeleeMap, CombatMapKeys.MeleeLow, DikScanCodes.Delete, 0 },
        { CombatMapKeys.MeleeMap, CombatMapKeys.MeleeMedium, DikScanCodes.End, 0 },
        { CombatMapKeys.MeleeMap, CombatMapKeys.MeleeHigh, DikScanCodes.Next, 0 },
        { CombatMapKeys.MissileMap, CombatMapKeys.MissilePowerDown, DikScanCodes.Insert, 0 },
        { CombatMapKeys.MissileMap, CombatMapKeys.MissilePowerUp, DikScanCodes.Prior, 0 },
        { CombatMapKeys.MissileMap, CombatMapKeys.MissileLow, DikScanCodes.Delete, 0 },
        { CombatMapKeys.MissileMap, CombatMapKeys.MissileMedium, DikScanCodes.End, 0 },
        { CombatMapKeys.MissileMap, CombatMapKeys.MissileHigh, DikScanCodes.Next, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.PreviousTabAction, DikScanCodes.Insert, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.NextTabAction, DikScanCodes.Prior, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.PreviousSpellAction, DikScanCodes.Delete, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.CastCurrentAction, DikScanCodes.End, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.NextSpellAction, DikScanCodes.Next, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.FirstTabAction, DikScanCodes.Insert, Ctrl },
        { SpellBarKeys.InputMap, SpellBarKeys.LastTabAction, DikScanCodes.Prior, Ctrl },
        { SpellBarKeys.InputMap, SpellBarKeys.FirstSpellAction, DikScanCodes.Delete, Ctrl },
        { SpellBarKeys.InputMap, SpellBarKeys.LastSpellAction, DikScanCodes.Next, Ctrl },
        { SpellBarKeys.InputMap, SpellBarKeys.FirstQuickslotAction, DikScanCodes.D1, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.FirstQuickslotAction + 1, DikScanCodes.D1 + 1, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.FirstQuickslotAction + 2, DikScanCodes.D1 + 2, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.FirstQuickslotAction + 3, DikScanCodes.D1 + 3, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.FirstQuickslotAction + 4, DikScanCodes.D1 + 4, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.FirstQuickslotAction + 5, DikScanCodes.D1 + 5, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.FirstQuickslotAction + 6, DikScanCodes.D1 + 6, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.FirstQuickslotAction + 7, DikScanCodes.D1 + 7, 0 },
        { SpellBarKeys.InputMap, SpellBarKeys.FirstQuickslotAction + 8, DikScanCodes.D1 + 8, 0 },
    };

    [RequiresLocalDatsTheory]
    [MemberData(nameof(ModeMapBindings))]
    public void Each_key_of_the_three_mode_maps_is_the_dats_binding(uint map, uint action, int key, uint meta)
    {
        var bindings = RetailKeyMap.Load(fixture.Dats).Bindings.Where(b => b.InputMapId == map && b.ActionId == action).ToArray();
        var binding = Assert.Single(bindings);
        Assert.Equal(RetailKeyBindings.KeyboardDevice, binding.Control.DeviceIndex);
        Assert.Equal(key, binding.Control.Semantic);
        Assert.Equal(meta, binding.MetaMode);
    }

    [RequiresLocalDatsFact]
    public void The_dat_binds_nothing_else_in_the_three_maps_and_no_key_of_them_is_F7_Scroll_Lock_Pause_or_the_combat_toggle()
    {
        var all = RetailKeyMap.Load(fixture.Dats).Bindings;
        var mode = all.Where(b => b.InputMapId is CombatMapKeys.MeleeMap or CombatMapKeys.MissileMap or SpellBarKeys.InputMap).ToArray();
        Assert.Equal(ModeMapBindings.Count(), mode.Length); // every binding of the three maps is pinned above
        var reserved = new[] { LifestoneHotkeys.ToolsKey, DikScanCodes.Scroll, LifestoneHotkeys.AutoBuffKey, DikScanCodes.Grave };
        Assert.DoesNotContain(mode, b => reserved.Contains(b.Control.Semantic));
        Assert.DoesNotContain(all, b => b.InputMapId == CombatToggleKey.InputMap && b.ActionId != CombatToggleKey.ToggleAction); // the always-on map holds only the toggle

        // The one overlap with another retail map is reported, not resolved: keys 1 to 9 are also the shortcut bar's (map 0x1000000C); the router searches the magic map first because it registers later at the same priority.
        var numbers = Enumerable.Range(DikScanCodes.D1, 9).ToHashSet();
        var shared = all.Where(b => b.MetaMode == 0 && b.InputMapId is not (CombatMapKeys.MeleeMap or CombatMapKeys.MissileMap or SpellBarKeys.InputMap) && numbers.Contains(b.Control.Semantic)).Select(b => b.InputMapId).Distinct().ToArray();
        Assert.Contains(GameHotbar.InputMap, shared);
    }

    [Fact]
    public void The_power_keys_step_a_sixth_as_HandleCombatAction_does()
    {
        Assert.Equal(4f / 6f, CombatMapKeys.StepPower(0.5f, up: true), 5);
        Assert.Equal(2f / 6f, CombatMapKeys.StepPower(0.5f, up: false), 5);
        Assert.Equal(1f, CombatMapKeys.StepPower(1f, up: true), 5);
        Assert.Equal(0f, CombatMapKeys.StepPower(0f, up: false), 5);
        Assert.Equal(0f, CombatMapKeys.StepPower(0.1f, up: false), 5); // 0.1 is in the first sixth's step: down is 0
        Assert.Equal(1f / 6f, CombatMapKeys.StepPower(0.3f, up: false), 5); // 0.3 is in the second: down is one sixth
    }

    [RequiresLocalDatsFact]
    public void The_melee_map_is_registered_in_melee_only_and_the_missile_map_in_missile_only()
    {
        using var s = Enter();
        var keys = Assert.IsType<CombatMapKeys>(s.Combat.ToggleKey!.ModeMaps);
        Assert.Equal(0u, keys.RegisteredMap); // peace
        s.ChangeMode(CombatMode.Melee);
        Assert.Equal(CombatMapKeys.MeleeMap, keys.RegisteredMap);
        Assert.Contains(s.Rig.Manager.Input.InputMaps, m => m.InputMapId == CombatMapKeys.MeleeMap);
        s.ChangeMode(CombatMode.Missile);
        Assert.Equal(CombatMapKeys.MissileMap, keys.RegisteredMap);
        Assert.DoesNotContain(s.Rig.Manager.Input.InputMaps, m => m.InputMapId == CombatMapKeys.MeleeMap);
        s.ChangeMode(CombatMode.Magic);
        Assert.Equal(0u, keys.RegisteredMap);
        Assert.DoesNotContain(s.Rig.Manager.Input.InputMaps, m => m.InputMapId is CombatMapKeys.MeleeMap or CombatMapKeys.MissileMap);

        s.Combat.SelectedProvider = () => Target;
        var before = AttacksLogged(s).Count;
        Tap(s, DikScanCodes.Next); // a height key outside melee and missile: nothing
        s.Rig.Run(1.0);
        Assert.Equal(before, AttacksLogged(s).Count);
        Assert.Equal(0, keys.Presses);
    }

    [RequiresLocalDatsFact]
    public void Each_height_key_in_melee_presses_and_releases_as_the_mouse_does_and_sends_the_attack_through_the_Action_API()
    {
        using var s = Enter();
        s.ChangeMode(CombatMode.Melee);
        var combat = s.Combat;
        combat.SelectedProvider = () => Target;
        foreach (var (key, height) in new[] { (DikScanCodes.Next, 1), (DikScanCodes.End, 2), (DikScanCodes.Delete, 3) })
        {
            var before = AttacksLogged(s).Count;
            s.Rig.Manager.Input.KeyDown(key);
            Assert.True(combat.Attacks!.RequestInProgress); // SetRequestedAttackHeight: the request starts
            Assert.Equal((AttackHeight)height, combat.Attacks.RequestedHeight);
            Assert.Equal(HeightButtonId(height), Assert.IsAssignableFrom<Lifestone.Client.RetailUi.Widgets.UiGroupBox>(combat.GetChildRecursive(GameCombatPanel.HeightBoxId)).SelectedButtonId);
            Assert.Empty(AttacksLogged(s).Skip(before)); // nothing until the release
            s.Rig.Manager.Input.KeyUp(key); // EndAttackRequest
            s.Rig.Run(1.0);
            var entry = Assert.Single(AttacksLogged(s).Skip(before));
            Assert.Equal((ActionKind.Attack, ActionSource.Ui), (entry.Kind, entry.Source));
            Assert.Contains($"target=0x{Target:X8} height={height} power=0.5", entry.Summary, StringComparison.Ordinal);
        }
    }

    [RequiresLocalDatsFact]
    public void A_height_key_in_missile_sends_MissileAttack_and_a_key_with_nothing_selected_sends_nothing_once()
    {
        using var s = Enter();
        s.ChangeMode(CombatMode.Missile);
        var lines = new List<string>();
        s.Combat.ChatNotice = lines.Add;
        Tap(s, DikScanCodes.Delete); // nothing selected: the line, and the release does not start a second request
        s.Rig.Run(1.0);
        Assert.Empty(AttacksLogged(s));
        Assert.Equal([AttackPowerDriver.NoTargetText], lines);

        s.Combat.SelectedProvider = () => Target;
        Tap(s, DikScanCodes.Delete);
        s.Rig.Run(1.0);
        var entry = Assert.Single(AttacksLogged(s));
        Assert.Equal((ActionKind.MissileAttack, ActionSource.Ui), (entry.Kind, entry.Source));
        Assert.Contains($"target=0x{Target:X8} height=3", entry.Summary, StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void Insert_and_PgUp_step_the_requested_power_and_the_slider_follows()
    {
        using var s = Enter();
        s.ChangeMode(CombatMode.Melee);
        var combat = s.Combat;
        Assert.Equal(0.5f, combat.RequestedPower, 3);
        Tap(s, DikScanCodes.Prior);
        s.Rig.Run(0.5); // the slider moves to its goal and reports its position
        Assert.Equal(4f / 6f, combat.RequestedPower, 2);
        Tap(s, DikScanCodes.Insert);
        s.Rig.Run(0.5);
        Tap(s, DikScanCodes.Insert);
        s.Rig.Run(0.5);
        Assert.Equal(2f / 6f, combat.RequestedPower, 2);
        Assert.Equal(3, combat.ToggleKey!.ModeMaps!.Presses);
    }

    private static uint HeightButtonId(int height) => height switch { 1 => GameCombatPanel.HighButtonId, 3 => GameCombatPanel.LowButtonId, _ => GameCombatPanel.MediumButtonId };

    private static void Tap(Setup s, int key)
    {
        s.Rig.Manager.Input.KeyDown(key);
        s.Rig.Manager.Input.KeyUp(key);
    }

    private static List<ActionLogEntry> AttacksLogged(Setup s)
    {
        var log = s.Rig.Session.ActionLog;
        var list = new List<ActionLogEntry>();
        for (var i = 0; i < log.Count; i++)
        {
            if (log[i].Kind is ActionKind.Attack or ActionKind.MissileAttack)
            {
                list.Add(log[i]);
            }
        }

        return list;
    }

    private sealed record Setup(LoginRig Rig, IInboundMessageHandler Handler) : IDisposable
    {
        public GameCombatPanel Combat => Assert.IsType<GameCombatPanel>(Assert.IsType<GamePlayScreen>(Rig.Flow.CurrentScreen).Root!.GetChildRecursive(GameCombatPanel.WindowId));

        public byte Sequence { get; set; }

        public void ChangeMode(CombatMode mode)
        {
            Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, AceWireWriters.PrivateUpdatePropertyInt(++Sequence, CombatModes.Property, (int)mode), Handler).Outcome);
            Rig.Frame();
        }

        public void Dispose() => Rig.Dispose();
    }

    private Setup Enter()
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        handler!.OnPlayerCreate(Context, new PlayerCreateMsg(Player));
        handler.OnCreateObject(Context, ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"), isUpdate: false);
        handler.OnPlayerDescription(Context, new GameEventHeader(1, 1, 0x0013), new PlayerDescriptionMsg
        {
            Options1 = StandInPlayerStats.AceDefaultOptions1,
            Options2 = PlayerOptionTable.DefaultWord2,
            IntProperties = ImmutableDictionary<uint, int>.Empty.Add(24, 5).Add(PlayerQualities.LevelProperty, 50),
            Vitals = [new VitalDesc(1, 5, 0, 0, 52), new VitalDesc(3, 0, 0, 0, 120), new VitalDesc(5, 0, 0, 0, 75)],
        });
        rig.Frame();
        return new Setup(rig, handler);
    }
}
