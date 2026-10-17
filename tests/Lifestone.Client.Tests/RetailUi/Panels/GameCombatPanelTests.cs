using System.Collections.Immutable;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Options;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;
using Lifestone.Net.Dispatch;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The combat window (<c>gmCombatUI</c>) and the power bar (<c>gmPowerbarUI</c>) over the real gameplay layout (M3 3.2 pa, docs/milestones/m3-plan.md section 4.8): the toolbar's combat buttons change the mode through the Action
/// API, the windows show and hide by the mode, the toolbar shows the button for the mode, nothing is drawn while a window is hidden, the controls answer as retail's do, and a log off leaves no handler on the session.
/// </summary>
public sealed class GameCombatPanelTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Player = 0x50000001;
    private static readonly InboundContext Context = new(0, MessageQueue.SmartBox, 0);

    private static PlayerDescriptionMsg Description(bool recklessnessTrained = false, EquippedDesc[]? equipped = null) => new()
    {
        Options1 = StandInPlayerStats.AceDefaultOptions1,
        Options2 = PlayerOptionTable.DefaultWord2,
        IntProperties = ImmutableDictionary<uint, int>.Empty.Add(24, 5).Add(PlayerQualities.LevelProperty, 50),
        Int64Properties = ImmutableDictionary<uint, long>.Empty.Add(2, 1000).Add(1, 5_000_000),
        Attributes =
        [
            new AttributeDesc(1, 0, 90, 0), new AttributeDesc(2, 0, 80, 0), new AttributeDesc(3, 0, 80, 0),
            new AttributeDesc(4, 0, 90, 0), new AttributeDesc(5, 0, 70, 0), new AttributeDesc(6, 0, 100, 0),
        ],
        Vitals = [new VitalDesc(1, 5, 0, 0, 52), new VitalDesc(3, 0, 0, 0, 120), new VitalDesc(5, 0, 0, 0, 75)],
        Skills = recklessnessTrained ? [new SkillDesc(GameCombatPanel.RecklessnessSkill, 0, 0, 2, 0, 0, 0, 0)] : [],
        Enchantments = [],
        EnchantmentMask = 0,
        Equipped = [.. equipped ?? []],
    };

    private sealed record Setup(LoginRig Rig, GamePlayScreen Screen, IInboundMessageHandler Handler, int BaselineHandlers) : IDisposable
    {
        public GameCombatPanel Combat => Assert.IsType<GameCombatPanel>(Screen.Root!.GetChildRecursive(GameCombatPanel.WindowId));

        public GamePowerBar Bar => Assert.IsType<GamePowerBar>(Screen.Root!.GetChildRecursive(GamePowerBar.WindowId));

        public UiFloatyCombatPanel Floaty => Assert.IsType<UiFloatyCombatPanel>(Combat.Parent);

        public GameToolbar Toolbar => Screen.ToolbarUi!;

        public IGameSessionView Session => Rig.Session;

        public int Mode => Rig.Session.State.Self.Qualities.CombatMode;

        public void Press(uint buttonId)
        {
            var button = Assert.IsAssignableFrom<UiButton>(Screen.Root!.GetChildRecursive(buttonId));
            Assert.True(button.Visible, $"button 0x{buttonId:X8} is hidden");
            button.BroadcastElementMessage(UiMessageIds.ButtonClick);
        }

        public byte Sequence { get; set; }

        // ACE's answer to a mode change (0x02CD, property 40): an offline session refuses the request itself, so the test plays the server.
        public void ChangeMode(CombatMode mode)
        {
            Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, AceWireWriters.PrivateUpdatePropertyInt(++Sequence, CombatModes.Property, (int)mode), Handler).Outcome);
            Rig.Frame();
        }

        public void Dispose() => Rig.Dispose();
    }

    private Setup Enter(bool recklessnessTrained = false, EquippedDesc[]? equipped = null)
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true);
        rig.Run(2.5);
        var baseline = rig.Session.Bus.HandlerCount;
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        handler!.OnPlayerCreate(Context, new PlayerCreateMsg(Player));
        handler.OnCreateObject(Context, ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"), isUpdate: false);
        handler.OnPlayerDescription(Context, new GameEventHeader(1, 1, 0x0013), Description(recklessnessTrained, equipped));
        rig.Frame();

        // The spellcasting window (pb's, 0x10000061) shares the floaty; the layout shows it and its controller hides it in every mode but magic (M3 3.2 pb).
        Assert.False(screen.Root!.GetChildRecursive(0x10000061)!.Visible, "the spellcasting window is hidden outside magic mode");
        return new Setup(rig, screen, handler, baseline);
    }

    // The painter draws an element that is shown (its own flag and every ancestor's): a hidden window draws nothing, nor does anything inside it.
    private static bool Drawn(Setup s, UiElement window)
    {
        _ = s;
        return window.IsShown || window.Children.Any(static c => c.IsShown);
    }

    private static bool OnlyButtonShown(Setup s, uint shown) =>
        GameToolbar.CombatButtonIds.All(id => s.Screen.Root!.GetChildRecursive(id)!.Visible == (id == shown));

    [RequiresLocalDatsFact]
    public void The_windows_are_built_as_the_classes_and_hidden_in_non_combat_mode()
    {
        using var s = Enter();
        Assert.False(s.Combat.Visible);
        Assert.False(s.Floaty.Visible); // the floaty follows its windows: none shows
        Assert.False(s.Bar.Visible);
        Assert.True(OnlyButtonShown(s, 0x10000192), "the non-combat button shows");
        Assert.Equal(PowerBarMode.Undefined, s.Bar.CurrentMode);
        Assert.False(Drawn(s, s.Combat));
        Assert.False(Drawn(s, s.Floaty));
        Assert.False(Drawn(s, s.Bar));
        Assert.False(s.Combat.RecklessnessField!.Visible); // PostInit hides it
        Assert.False(s.Bar.RecklessnessField!.Visible);
    }

    [RequiresLocalDatsFact]
    public void A_combat_button_changes_the_mode_through_the_Action_API_and_back_and_the_window_and_the_toolbar_follow()
    {
        using var s = Enter();
        var expected = CombatModeRules.DefaultMode(s.Session.State, out var blocked);
        Assert.Null(blocked);
        Assert.Equal(CombatMode.Melee, expected); // nothing wielded: fists

        s.Press(0x10000192);
        Assert.Equal(1, s.Toolbar.CombatPresses);
        var asked = s.Rig.Session.ActionLog[s.Rig.Session.ActionLog.Count - 1]; // the press went through the Action API (an offline session refuses it; the test plays the server below)
        Assert.Equal(ActionKind.SetCombatMode, asked.Kind);
        Assert.Equal(ActionSource.Ui, asked.Source);
        Assert.Contains("mode=2", asked.Summary, StringComparison.Ordinal);
        s.ChangeMode(CombatMode.Melee);
        Assert.Equal(CombatModes.Melee, s.Mode);
        Assert.True(s.Combat.Visible);
        Assert.Equal(GameCombatPanel.MeleeState, s.Combat.State);
        Assert.True(s.Floaty.Visible);
        Assert.True(Drawn(s, s.Combat));
        Assert.True(OnlyButtonShown(s, 0x10000193), "the melee button shows");
        Assert.False(s.Bar.Visible); // the power bar is begun by an attack, not by the mode
        Assert.Empty(s.Rig.Manager.Dialogs.OpenDialogs); // no NotInM3 message any more

        s.Press(0x10000193);
        var back = s.Rig.Session.ActionLog[s.Rig.Session.ActionLog.Count - 1];
        Assert.Contains("mode=1", back.Summary, StringComparison.Ordinal);
        s.ChangeMode(CombatMode.NonCombat);
        Assert.Equal(CombatModes.NonCombat, s.Mode);
        Assert.False(s.Combat.Visible);
        Assert.False(s.Floaty.Visible);
        Assert.False(Drawn(s, s.Combat));
        Assert.False(Drawn(s, s.Floaty));
        Assert.True(OnlyButtonShown(s, 0x10000192));
    }

    [RequiresLocalDatsFact]
    public void A_wielded_item_the_object_table_lacks_is_named_once_and_a_press_asks_nothing()
    {
        // M3 3.2 review finding 13: the description says a melee weapon is wielded, but its CreateObject never came (an F747 alone, the case ReleaseDeletedHeld fixed, is another way there).
        const uint sword = 0x80000777;
        using var s = Enter(equipped: [new EquippedDesc(sword, (uint)EquipMask.MeleeWeapon, 0)]);
        Assert.False(s.Session.State.Objects.TryGet(sword, out _));
        Assert.Equal(CombatMode.NonCombat, CombatModeRules.DefaultMode(s.Session.State, out var blocked, out var missing));
        Assert.Null(blocked);
        Assert.Equal(sword, missing);

        var actions = s.Rig.Session.ActionLog.Count;
        s.Press(0x10000192);
        s.Press(0x10000192); // a second press: the TODO is not named again
        s.Rig.Run(2.0); // long enough for any action asked to complete and reach the log
        Assert.Equal(2, s.Toolbar.CombatPresses);
        Assert.Equal(actions, s.Rig.Session.ActionLog.Count); // nothing was asked of the Action API: no SetCombatMode for the mode the player is in
        Assert.Equal(1, s.Rig.Log.Lines.Count(l => l.Contains(CombatModeRules.MissingObjectTodo, StringComparison.Ordinal)));
        Assert.Contains(s.Rig.Log.Lines, l => l.Contains(CombatModeRules.MissingObjectTodo, StringComparison.Ordinal) && l.Contains("0x80000777", StringComparison.Ordinal));
        Assert.True(OnlyButtonShown(s, 0x10000192)); // still in peace
        Assert.False(s.Combat.Visible);
    }

    [Fact]
    public void A_toggle_whose_default_mode_is_the_mode_the_player_is_in_asks_nothing_and_names_a_missing_object_once()
    {
        // M3 3.2 review finding 13 without the layout: an offline session whose description says a melee weapon is wielded that the object table never gets, then a held item that forbids combat, then nothing wielded.
        const uint sword = 0x80000777;
        const uint lockpick = 0x80000778;
        IInboundMessageHandler? handler = null;
        var visit = new StandInVisit { EnterAfter = 0.2, UnhideAfter = 1.0 };
        var time = new ManualTimeProvider();
        using var session = GameSession.Offline(
            h =>
            {
                handler = h;
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

        void NoModeChangeAsked()
        {
            for (var i = 0; i < session.ActionLog.Count; i++)
            {
                Assert.NotEqual(ActionKind.SetCombatMode, session.ActionLog[i].Kind);
            }
        }

        Run(2.0);
        var player = session.State.Self.Guid;
        Assert.NotEqual(0u, player);
        Assert.True(session.State.Self.Qualities.CombatMode is 0 or CombatModes.NonCombat, "the stand-in's player starts in peace");
        handler!.OnPlayerDescription(Context, new GameEventHeader(player, 7, GameEventTypes.PlayerDescription), new PlayerDescriptionMsg { Equipped = [new EquippedDesc(sword, (uint)EquipMask.MeleeWeapon, 0)] });
        Assert.False(session.State.Objects.TryGet(sword, out _));
        Assert.Equal(CombatMode.NonCombat, CombatModeRules.DefaultMode(session.State, out var blocked, out var missing));
        Assert.Null(blocked);
        Assert.Equal(sword, missing);

        var scene = new UiScene([]);
        var chat = new List<string>();
        Assert.Null(CombatModeRules.Toggle(session, scene.Manager, chat.Add));
        Assert.Null(CombatModeRules.Toggle(session, scene.Manager, chat.Add)); // a second press
        Run(3.0);
        NoModeChangeAsked();
        Assert.Empty(chat);
        Assert.Equal(1, scene.Log.Lines.Count(l => l.Contains(CombatModeRules.MissingObjectTodo, StringComparison.Ordinal) && l.Contains("0x80000777", StringComparison.Ordinal)));

        // A held item that forbids combat (not a caster): retail's chat line, and nothing asked either.
        handler.OnPlayerDescription(Context, new GameEventHeader(player, 8, GameEventTypes.PlayerDescription), new PlayerDescriptionMsg { Equipped = [new EquippedDesc(lockpick, (uint)EquipMask.Held, 0)] });
        handler.OnCreateObject(Context, ReducerHarness.Create(lockpick, ReducerHarness.Stamps(), null, name: "Lockpick"), isUpdate: false);
        Assert.Equal(CombatMode.NonCombat, CombatModeRules.DefaultMode(session.State, out blocked, out missing));
        Assert.Equal("Lockpick", blocked);
        Assert.Equal(0u, missing);
        Assert.Null(CombatModeRules.Toggle(session, scene.Manager, chat.Add));
        Assert.Equal(["You can't enter combat mode while wielding the Lockpick"], chat);
        Run(3.0);
        NoModeChangeAsked();
        Assert.Equal(1, scene.Log.Lines.Count(l => l.Contains(CombatModeRules.MissingObjectTodo, StringComparison.Ordinal))); // the earlier item's line, no new one

        // Nothing wielded: the press asks for melee, as before.
        handler.OnPlayerDescription(Context, new GameEventHeader(player, 9, GameEventTypes.PlayerDescription), new PlayerDescriptionMsg());
        var melee = CombatModeRules.Toggle(session, scene.Manager, chat.Add);
        Assert.NotNull(melee);
        Assert.Equal(ActionKind.SetCombatMode, melee.Kind);
        Assert.Equal(ActionSource.Ui, melee.Source);
        Assert.Contains("mode=2", melee.Summary, StringComparison.Ordinal);
    }

    [RequiresLocalDatsFact]
    public void The_floaty_shows_while_one_of_its_panel_windows_does()
    {
        using var s = Enter();
        var cast = s.Screen.Root!.GetChildRecursive(0x10000061)!;
        Assert.False(s.Floaty.Visible);
        cast.SetVisible(true); // pb's window in magic mode
        Assert.True(s.Floaty.Visible);
        s.ChangeMode(CombatMode.Melee); // the combat window shows too
        Assert.True(s.Floaty.Visible);
        cast.SetVisible(false);
        Assert.True(s.Floaty.Visible); // the combat window still shows
        s.ChangeMode(CombatMode.NonCombat);
        Assert.False(s.Floaty.Visible);
    }

    [RequiresLocalDatsFact]
    public void Missile_mode_takes_the_missile_state_and_magic_mode_hides_the_combat_window_and_shows_its_button()
    {
        using var s = Enter();
        s.ChangeMode(CombatMode.Missile);
        Assert.True(s.Combat.Visible);
        Assert.Equal(GameCombatPanel.MissileState, s.Combat.State);
        Assert.True(OnlyButtonShown(s, 0x10000194));
        Assert.True(s.Floaty.Visible);

        s.ChangeMode(CombatMode.Magic);
        Assert.False(s.Combat.Visible);
        Assert.True(s.Floaty.Visible); // pb's spellcasting window shows in magic mode, so the floaty does
        Assert.False(Drawn(s, s.Combat));
        Assert.True(OnlyButtonShown(s, 0x10000195));

        s.ChangeMode(CombatMode.Melee);
        Assert.True(s.Combat.Visible);
        Assert.Equal(GameCombatPanel.MeleeState, s.Combat.State);
        s.ChangeMode(CombatMode.NonCombat);
        Assert.False(s.Combat.Visible);
    }

    [RequiresLocalDatsFact]
    public void The_recklessness_field_shows_in_melee_mode_only_for_a_trained_skill()
    {
        using (var untrained = Enter(recklessnessTrained: false))
        {
            untrained.ChangeMode(CombatMode.Melee);
            Assert.False(untrained.Combat.RecklessnessField!.Visible);
        }

        using var trained = Enter(recklessnessTrained: true);
        trained.ChangeMode(CombatMode.Melee);
        Assert.True(trained.Combat.RecklessnessField!.Visible);
    }

    [RequiresLocalDatsFact]
    public void Melee_to_missile_hides_the_recklessness_field_and_a_slider_move_in_missile_does_not_show_it()
    {
        // M3 3.2 review finding 14: RecvNotice_SetCombatMode (11.6096 0x004CD210) shows the field in melee only; the power slider's move must not bring it back in missile mode.
        using var s = Enter(recklessnessTrained: true);
        s.ChangeMode(CombatMode.Melee);
        var field = s.Combat.RecklessnessField!;
        Assert.True(field.Visible);

        s.ChangeMode(CombatMode.Missile);
        Assert.False(field.Visible);
        var slider = Assert.IsAssignableFrom<UiScrollbar>(s.Combat.GetChildRecursive(GameCombatPanel.SliderId));
        slider.SetScrollbarPosition(0.8f);
        Assert.Equal(0.8f, s.Combat.RequestedPower, 3);
        Assert.False(field.Visible); // the slider re-reads the rule: not melee, not shown

        s.ChangeMode(CombatMode.Melee);
        Assert.True(field.Visible);
        slider.SetScrollbarPosition(0.3f);
        Assert.True(field.Visible);
        s.ChangeMode(CombatMode.NonCombat);
        Assert.False(field.Visible);
    }

    [RequiresLocalDatsFact]
    public void The_attack_height_buttons_answer_NotInM3_and_request_their_height_on_a_mouse_down_and_the_slider_sets_the_power()
    {
        using var s = Enter();
        s.ChangeMode(CombatMode.Melee);
        var combat = s.Combat;
        Assert.Equal(2, combat.RequestedHeight); // the group box's default button is the medium one
        Assert.Equal(0x10000058u, Assert.IsAssignableFrom<UiGroupBox>(combat.GetChildRecursive(GameCombatPanel.HeightBoxId)).SelectedButtonId);

        // A mouse down on the high button requests it (SetRequestedAttackHeight); the click is the attack, which M3 does not have.
        var high = combat.HeightButton(1)!;
        high.BroadcastElementMessage(UiMessageIds.MouseDown);
        Assert.Equal(1, combat.RequestedHeight);
        high.BroadcastElementMessage(UiMessageIds.ButtonClick);
        s.Rig.Frame();
        Assert.Equal(1, combat.AttackClicks);
        var dialog = Assert.Single(s.Rig.Manager.Dialogs.OpenDialogs);
        Assert.Equal(NotInM3.Prefix + "Melee and missile attacks", Assert.IsType<Lifestone.Client.RetailUi.Text.UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text);
        Assert.Contains(s.Rig.Log.Lines, l => l.Contains("TODO(m3-melee-attack)", StringComparison.Ordinal));

        // The notices the attack code will send: the group box follows the height, the meter shows the level, the slider moves.
        combat.AttackHeightChanged(3);
        Assert.Equal(0x10000059u, Assert.IsAssignableFrom<UiGroupBox>(combat.GetChildRecursive(GameCombatPanel.HeightBoxId)).SelectedButtonId);
        combat.SetPowerbarLevel(PowerBarMode.Advanced, 0.9f); // another mode's level is the power bar's, not this meter's
        Assert.Equal(0f, Assert.IsAssignableFrom<UiMeter>(combat.GetChildRecursive(GameCombatPanel.MeterId)).Position);
        combat.SetPowerbarLevel(PowerBarMode.Combat, 0.75f);
        Assert.Equal(0.75f, Assert.IsAssignableFrom<UiMeter>(combat.GetChildRecursive(GameCombatPanel.MeterId)).Position);
        var slider = Assert.IsAssignableFrom<UiScrollbar>(combat.GetChildRecursive(GameCombatPanel.SliderId));
        slider.SetScrollbarPosition(0.25f);
        Assert.Equal(0.25f, combat.RequestedPower, 3);
        slider.SetScrollbarPosition(2f);
        Assert.Equal(1f, combat.RequestedPower, 3);
    }

    [RequiresLocalDatsFact]
    public void The_option_boxes_show_the_player_options_ghosted_and_a_click_changes_nothing()
    {
        using var s = Enter();
        s.ChangeMode(CombatMode.Melee);
        Assert.Equal(3, s.Combat.OptionCheckboxes.Count);
        foreach (var box in s.Combat.OptionCheckboxes)
        {
            Assert.NotNull(box.Option);
            Assert.False(box.Option!.Honoured);
            Assert.Equal(UiStateIds.Ghosted, box.State); // nothing in M3 reads an attack option: ghosted, as the options page has them
            Assert.Equal(s.Screen.Options!.Get(box.Option), box.Current);
            var before = box.Current;
            box.BroadcastElementMessage(UiMessageIds.ButtonClick);
            Assert.Equal(before, box.Current);
        }
    }

    [RequiresLocalDatsFact]
    public void The_power_bar_shows_on_a_begin_by_mode_and_fills_only_for_that_mode_and_hides_on_finish()
    {
        using var s = Enter();
        var bar = s.Bar;
        var meter = Assert.IsAssignableFrom<UiMeter>(bar.GetChildRecursive(GamePowerBar.MeterId));

        bar.Begin(PowerBarMode.Combat); // the combat window's own meter: the bar does nothing
        Assert.False(bar.Visible);

        bar.Begin(PowerBarMode.Advanced); // not in melee mode: the missile state
        Assert.True(bar.Visible);
        Assert.Equal(GamePowerBar.MissileState, bar.State);
        Assert.Equal(PowerBarMode.Advanced, bar.CurrentMode);
        Assert.Equal(0f, bar.Level);
        bar.SetLevel(PowerBarMode.Jump, 0.9f);
        Assert.Equal(0f, bar.Level); // not the mode that began
        bar.SetLevel(PowerBarMode.Advanced, 0.5f);
        Assert.Equal(0.5f, meter.Position);
        Assert.True(Drawn(s, bar));
        bar.Finish(PowerBarMode.Jump);
        Assert.True(bar.Visible); // another mode's finish does nothing
        bar.Finish(PowerBarMode.Advanced);
        Assert.False(bar.Visible);
        Assert.Equal(PowerBarMode.Undefined, bar.CurrentMode);
        Assert.Equal(0f, meter.Position);
        Assert.False(Drawn(s, bar));

        s.ChangeMode(CombatMode.Melee);
        bar.Begin(PowerBarMode.Advanced);
        Assert.Equal(GamePowerBar.MeleeState, bar.State);
        bar.Finish(PowerBarMode.Advanced);
        bar.Begin(PowerBarMode.Advanced, CombatModes.Missile); // a caller can name the mode it began in
        Assert.Equal(GamePowerBar.MissileState, bar.State);
        bar.Finish(PowerBarMode.Advanced);
        bar.Begin(PowerBarMode.Jump);
        Assert.Equal(GamePowerBar.JumpState, bar.State);
        bar.Finish(PowerBarMode.Jump);

        bar.DddStatus(start: true, current: 3, total: 12);
        Assert.True(bar.Visible);
        Assert.Equal(GamePowerBar.DdState, bar.State);
        Assert.Equal(0.25f, bar.Level, 3);
        Assert.NotEmpty(bar.Caption);
        bar.DddStatus(start: false, current: 0, total: 0);
        Assert.False(bar.Visible);
    }

    [RequiresLocalDatsFact]
    public void A_log_off_leaves_no_handler_on_the_session()
    {
        using var s = Enter();
        s.ChangeMode(CombatMode.Melee);
        Assert.True(s.Rig.Session.Bus.HandlerCount > s.BaselineHandlers);
        s.Rig.Flow.QueueUIMode(UiModes.CharacterManagement);
        for (var i = 0; i < 4; i++)
        {
            s.Rig.Frame();
        }

        Assert.Equal(s.BaselineHandlers, s.Rig.Session.Bus.HandlerCount);
    }

    [Fact]
    public void The_window_frames_are_named_and_the_toolbar_ids_are_the_four_combat_buttons()
    {
        Assert.Equal(["c1-combat.png", "c1-powerbar.png"], PanelFramesPlan.WindowFrames.Select(PanelFramesPlan.FileName).ToArray());
        Assert.All(PanelFramesPlan.WindowFrames, f => Assert.Equal(CombatModes.Melee, f.CombatMode));
        Assert.Equal([0x1000005Cu, 0x10000044u], PanelFramesPlan.WindowFrames.Select(f => f.WindowId).ToArray());
        Assert.Equal([true], PanelFramesPlan.WindowFrames.Select(f => f.DrivePowerBar).Where(static d => d).ToArray());
        Assert.Equal([0x10000192u, 0x10000193u, 0x10000194u, 0x10000195u], GameToolbar.CombatButtonIds.ToArray());

        // M3 3.2 review finding 10: an advanced-mode bar is drawn only with the AdvancedCombatUI option on, which hides the combat window, so the power bar's frame hides it too, and says its placeholder; the other frames have none.
        Assert.All(PanelFramesPlan.AllWindowFrames, static f => Assert.Equal(f.DrivePowerBar, f.HideCombatWindow));
        Assert.All(PanelFramesPlan.AllWindowFrames, static f => Assert.Equal(f.DrivePowerBar, f.Placeholder is not null));
        var powerBar = Assert.Single(PanelFramesPlan.WindowFrames, static f => f.DrivePowerBar);
        Assert.Equal(WindowFrame.PowerBarPlaceholder, powerBar.Placeholder);
        Assert.Contains("begun by hand at 0.6", WindowFrame.PowerBarPlaceholder, StringComparison.Ordinal);
        Assert.Contains("nothing in M3 begins it", WindowFrame.PowerBarPlaceholder, StringComparison.Ordinal);
        Assert.Equal(0.6f, WindowFrame.PowerBarLevel);
        Assert.False(PanelFramesPlan.SpellcastingFrame.HideCombatWindow);
    }
}
