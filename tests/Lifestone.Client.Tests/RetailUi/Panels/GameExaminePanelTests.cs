using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The examine panel (M4 3.2 s4c; <c>gmExaminationUI</c>, floaty 0x100005F7 of the gameplay layout): opened by the item list's right click and by the paperdoll figure's body-part click (the colour of the click map, the worn item that covers
/// the part, the player when none does), each of which sends the Identify with the Ui source and shows the answer from a scripted 00C9 (a kit, a weapon, a creature, a failed roll); the parts the model lacks answer with a named TODO.
/// No creature is spawned (D23): the creature is a scripted answer for an NPC's guid, drawn as text.
/// </summary>
public sealed class GameExaminePanelTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint Player = 0x50000001;
    private const uint Kit = 0x80000A02;
    private const uint Mace = 0x80000A01;
    private const uint Cuirass = 0x80000A04;
    private const uint Npc = 0x7A9B4027;
    private const uint InventoryButton = 0x100001B1;

    private static AceAppraisal KitAnswer() => new()
    {
        Success = true,
        Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x8000, [AppraisalIds.Value] = 1200, [AppraisalIds.Burden] = 20, [AppraisalIds.HealingBoost] = 10, [AppraisalIds.MaxStructure] = 25, [AppraisalIds.Structure] = 24 },
        Floats = new Dictionary<uint, double> { [100] = 1.25 },
        Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Handy Healing Kit", [AppraisalIds.LongDescription] = "A kit of bandages." },
    };

    private static AceAppraisal MaceAnswer() => new()
    {
        Success = true,
        Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x1, [AppraisalIds.Value] = 450, [AppraisalIds.Burden] = 90, [AppraisalIds.WieldType] = 2, [AppraisalIds.WieldSkill] = 44, [AppraisalIds.WieldDifficulty] = 100, [AppraisalIds.Workmanship] = 6 },
        Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Mace" },
        Weapon = new AceWeaponProfile(4, 40, 44, 12, 0.25, 1.1, 0, 0, 1, 0),
    };

    private static AceAppraisal CreatureAnswer(bool success) => new()
    {
        Success = success,
        Ints = new Dictionary<uint, int> { [AppraisalIds.ItemType] = 0x10, [AppraisalIds.Level] = 8 },
        Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Thelnoth Cort the Healer" },
        Creature = success ? new AceCreatureProfile(55, 60) { Attributes = [20, 60, 60, 75, 90, 65, 70, 50, 80, 60] } : null,
    };

    private static Appraisal Answered(AceAppraisal info, uint guid)
    {
        var r = new WorldActionRig();
        r.Server((p, s) => AceVendorWriters.IdentifyResponse(p, s, guid, info));
        Assert.True(r.H.State.Appraisals.TryGet(guid, out var appraisal));
        return appraisal;
    }

    // ------------------------------------------------------------------ the fields (no shell)

    [Fact]
    public void A_kit_shows_value_burden_the_healing_boost_the_restoration_bonus_the_uses_left_and_the_description()
    {
        var log = new RecordingLogSink();
        var lines = GameExaminePanel.ItemLines(Answered(KitAnswer(), Kit), log);
        Assert.Equal(
            ["Value: 1,200", "Burden: 20", "Bonus to Healing Skill: 10", "Restoration Bonus: 125%", "Number of uses remaining: 24", "A kit of bandages."],
            lines);
    }

    [Fact]
    public void A_potion_shows_the_vital_it_restores()
    {
        var info = new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 170, [AppraisalIds.HealingBoost] = 70, [AppraisalIds.BoosterVital] = 2 } };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), new RecordingLogSink());
        Assert.Contains("Restores 70 Health when used.", lines);
        Assert.Contains("Value: 170", lines);
        Assert.Contains("Burden: ???", lines); // the table has no burden: retail's text for an unknown
    }

    [Fact]
    public void A_weapon_shows_the_skill_the_damage_range_with_its_type_the_speed_and_what_it_takes_to_wield()
    {
        var log = new RecordingLogSink();
        var lines = GameExaminePanel.ItemLines(Answered(MaceAnswer(), Mace), log);
        Assert.Equal("Value: 450", lines[0]);
        Assert.Contains(lines, l => l.StartsWith("Skill: ", StringComparison.Ordinal));
        Assert.Contains("Damage: 9 - 12, Bludgeoning", lines); // 3.2-R29: 12 less the profile's 0.25 variance share, then the greatest
        Assert.Contains("Speed: 40", lines);
        Assert.Contains(lines, l => l.StartsWith("Wield requires ", StringComparison.Ordinal) && l.EndsWith(" 100", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("TODO(m4-examine-words)", StringComparison.Ordinal)); // the speed and workmanship words are not read
        Assert.DoesNotContain(log.Lines, l => l.Contains("TODO(m4-examine-damage-range)", StringComparison.Ordinal)); // closed
    }

    [Fact]
    public void A_creature_shows_its_level_and_vitals()
    {
        var (level, vitals, note) = GameExaminePanel.CreatureLines(Answered(CreatureAnswer(success: true), Npc), new RecordingLogSink());
        Assert.Equal("8", level);
        Assert.Equal("Health: 55 / 60", vitals[0]);
        Assert.StartsWith("Stamina: ", vitals[1], StringComparison.Ordinal);
        Assert.StartsWith("Mana: ", vitals[2], StringComparison.Ordinal);
        Assert.Equal(string.Empty, note);
    }

    [Fact]
    public void A_failed_creature_roll_shows_the_tables_and_names_the_failed_roll_todo()
    {
        var log = new RecordingLogSink();
        var (level, vitals, note) = GameExaminePanel.CreatureLines(Answered(CreatureAnswer(success: false), Npc), log);
        Assert.Equal("8", level); // the int table is still sent
        Assert.Equal(["Health: ???", "Stamina: ???", "Mana: ???"], vitals);
        Assert.Contains("not in Lifestone M4", note, StringComparison.Ordinal);
        Assert.Contains(log.Lines, l => l.Contains("TODO(m4-examine-failed-roll-text)", StringComparison.Ordinal));
    }

    [Fact]
    public void What_the_model_lacks_answers_not_in_M4_with_a_named_todo_each()
    {
        var log = new RecordingLogSink();
        var info = new AceAppraisal
        {
            Success = true,
            Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 1, [307] = 5 },
            Strings = new Dictionary<uint, string> { [7] = "for Bob" },
            SpellBook = [1, 0x80000002],
        };
        var lines = GameExaminePanel.ItemLines(Answered(info, Kit), log);
        Assert.Contains("Spells: not in Lifestone M4", lines);
        Assert.Contains("Inscription: not in Lifestone M4", lines);
        Assert.Contains("Ratings and set: not in Lifestone M4", lines);
        foreach (var todo in new[] { "m4-examine-spells", "m4-examine-inscription", "m4-examine-ratings" })
        {
            Assert.Contains(log.Lines, l => l.Contains($"TODO({todo})", StringComparison.Ordinal));
        }
    }

    // ------------------------------------------------------------------ the figure's click map

    [Fact]
    public void The_nine_hit_test_colours_name_the_nine_body_parts_and_retails_masks()
    {
        Assert.Equal(1u, GamePaperDoll.MaskOfColor(0, 0, 255)); // head
        Assert.Equal(514u, GamePaperDoll.MaskOfColor(0, 255, 0)); // chest
        Assert.Equal(1028u, GamePaperDoll.MaskOfColor(255, 0, 0)); // abdomen
        Assert.Equal(2056u, GamePaperDoll.MaskOfColor(0, 255, 255)); // upper arm
        Assert.Equal(4112u, GamePaperDoll.MaskOfColor(255, 0, 255)); // lower arm
        Assert.Equal(8256u, GamePaperDoll.MaskOfColor(255, 255, 0)); // upper leg
        Assert.Equal(16512u, GamePaperDoll.MaskOfColor(0, 0, 128)); // lower leg
        Assert.Equal(32u, GamePaperDoll.MaskOfColor(0, 128, 0)); // hand
        Assert.Equal(256u, GamePaperDoll.MaskOfColor(128, 0, 0)); // foot
        Assert.Equal(0u, GamePaperDoll.MaskOfColor(12, 34, 56)); // none of them
        Assert.Equal(9, GamePaperDoll.HitTestColors.Length);
    }

    [Fact]
    public void The_upper_worn_item_is_the_one_with_the_higher_priority_and_the_player_answers_a_bare_part()
    {
        var shirt = new EquippedItem(0x80000001, 0x2, 0);
        var cuirass = new EquippedItem(0x80000002, 0x200, 0);
        var coat = new EquippedItem(0x80000003, 0x202, 8);
        Assert.Equal(0x80000002u, GamePaperDoll.UpperItemAt([shirt, cuirass], 514, Player)); // armour (0x200 counts as 127) over an undershirt (0x2, priority 0)
        Assert.Equal(0x80000002u, GamePaperDoll.UpperItemAt([cuirass, coat], 514, Player)); // 127 beats a priority of 8: the cuirass stays upper
        Assert.Equal(Player, GamePaperDoll.UpperItemAt([cuirass], 1, Player)); // nothing on the head: the player
        Assert.Equal(0u, GamePaperDoll.UpperItemAt([cuirass], 0, Player)); // retail's no-colour answer
    }

    // ------------------------------------------------------------------ the shell

    private sealed class Game(LoginRig rig, GamePlayScreen screen, IInboundMessageHandler handler) : IDisposable
    {
        private uint _event;
        private uint _sequence = 100;

        public LoginRig Rig => rig;

        public GamePlayScreen Screen => screen;

        public UiLoginContext LoginContext => screen.PanelControllers!.Context;

        public InboundContext Context => new(0, MessageQueue.SmartBox, 0);

        public GameExaminePanel Examine => Assert.IsType<GameExaminePanel>(screen.PanelHost!.Examine);

        public GameInventoryPage Page => Assert.Single(screen.PanelControllers!.ControllersOf(7).OfType<GameInventoryPage>());

        public GamePaperDoll Doll => Assert.Single(screen.PanelControllers!.ControllersOf(7).OfType<GamePaperDoll>());

        public UiButton Button => Assert.IsAssignableFrom<UiButton>(screen.Root!.GetChildRecursive(InventoryButton));

        public void Create(CreateObjectMsg message) => handler.OnCreateObject(Context, message, isUpdate: false);

        public void Wield(uint item, uint location) =>
            handler.OnWieldObject(Context, new GameEventHeader(Player, ++_event, 0x0023), new WieldObjectEvent(item, location));

        public void Answer(uint guid, AceAppraisal info) =>
            Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(Context, AceVendorWriters.IdentifyResponse(Player, ++_sequence, guid, info), handler).Outcome);

        public void Frames(int n)
        {
            for (var i = 0; i < n; i++)
            {
                rig.Frame();
            }
        }

        public void RightClick(UiElement element)
        {
            var box = element.ScreenBox;
            rig.Manager.Input.MouseMove((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
            rig.Manager.Input.MouseButton(UiMouseButton.Right, down: true);
            rig.Manager.Input.MouseButton(UiMouseButton.Right, down: false);
        }

        public void Dispose() => rig.Dispose();
    }

    private static CreateObjectMsg Item(uint guid, string name, uint icon, uint itemType, uint? container = Player, uint? valid = null)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        return basis with { Weenie = basis.Weenie with { IconId = icon, ItemType = itemType, ContainerId = container, ValidLocations = valid } };
    }

    private Game Enter(bool examineIcon = false)
    {
        IInboundMessageHandler? handler = null;
        var rig = new LoginRig(fixture, h =>
        {
            handler = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true, examineIcon: examineIcon);
        rig.Run(2.5);
        rig.Flow.QueueUIMode(UiModes.GamePlay);
        rig.Frame();
        rig.Frame();
        var screen = Assert.IsType<GamePlayScreen>(rig.Flow.CurrentScreen);
        var game = new Game(rig, screen, handler!);
        handler!.OnPlayerCreate(game.Context, new PlayerCreateMsg(Player));
        game.Create(ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"));
        game.Create(Item(Kit, "Handy Healing Kit", 0x060011D0, 0x8000));
        game.Create(Item(Mace, "Mace", 0x060011D1, 0x1, valid: 0x00100000));
        game.Create(Item(Cuirass, "Leather Cuirass", 0x060011D2, 0x2, valid: 0x200));
        game.Create(Item(Npc, "Thelnoth Cort the Healer", 0x060011D3, 0x10, container: null));
        handler.OnPlayerDescription(
            game.Context,
            new GameEventHeader(Player, 1, 0x0013),
            new PlayerDescriptionMsg { Attributes = [new AttributeDesc(1, 0, 100, 0)], Inventory = [new ContentDesc(Kit, 0), new ContentDesc(Mace, 0), new ContentDesc(Cuirass, 0)] });
        game.Frames(2);
        return game;
    }

    [RequiresLocalDatsFact]
    public void The_shell_has_the_examine_window_hidden_until_something_is_examined()
    {
        using var g = Enter();
        Assert.False(g.Examine.IsOpen);
        Assert.Equal(ExamineKind.None, g.Examine.Kind);
        Assert.Same(g.Examine, GameExaminePanel.Of(g.LoginContext));
    }

    [RequiresLocalDatsFact]
    public void A_right_click_on_a_pack_item_opens_the_panel_sends_the_identify_and_shows_the_answer()
    {
        using var g = Enter();
        Assert.True(g.Rig.Click(g.Button));
        g.Frames(14);
        var cell = Assert.Single(Enumerable.Range(0, g.Page.PackList.CellCount).Select(i => g.Page.PackList.CellAt(i)!), c => c.ItemId == Kit);
        g.RightClick(cell);
        var examine = g.Examine;
        Assert.True(examine.IsOpen);
        Assert.Equal(Kit, examine.ShownGuid);
        Assert.Equal(ExamineKind.Item, examine.Kind);
        Assert.True(examine.Pending);
        Assert.Equal("Handy Healing Kit", examine.Title); // the object's name, until the answer
        Assert.Equal(1, examine.Opens);
        var sent = Assert.IsAssignableFrom<IdentifyHandle>(examine.LastIdentify);
        Assert.Equal(ActionSource.Ui, sent.Source);
        Assert.Equal(ActionKind.Identify, sent.Kind);

        g.Answer(Kit, KitAnswer()); // the server's 00C9
        Assert.False(examine.Pending);
        Assert.Equal(1, examine.Fills);
        Assert.Equal("Handy Healing Kit", examine.Title);
        Assert.Contains("Value: 1,200", examine.Lines);
        Assert.Contains("Restoration Bonus: 125%", examine.Lines);
        Assert.Equal(0x060011D0u, examine.IconId);
        var text = Assert.IsAssignableFrom<UiTextElement>(g.Screen.Root!.GetChildRecursive(GameExaminePanel.ItemTextId));
        Assert.Contains("Value: 1,200", text.Text, StringComparison.Ordinal);
        var title = Assert.IsAssignableFrom<UiTextElement>(g.Screen.Root!.GetChildRecursive(GameExaminePanel.TitleId));
        Assert.Equal("Handy Healing Kit", title.Text);
        Assert.True(g.Screen.Root!.GetChildRecursive(GameExaminePanel.ItemFieldId)!.Visible);
        Assert.False(g.Screen.Root!.GetChildRecursive(GameExaminePanel.CreatureFieldId)!.Visible);
    }

    [RequiresLocalDatsFact]
    public void The_item_window_draws_the_objects_composed_icon_with_its_overlay_and_the_Appraisals_icon_only_when_the_table_has_none()
    {
        // M4 4.1 v4 (m4-examine-icon): the floaty's layout has no icon element, so the panel makes one from the standalone layout and composes the object's icon in it (the pack cell's path). With --examine-icon on only (review R8).
        using var g = Enter(examineIcon: true);
        var plain = Item(0x80000B01, "Orb", 0x060011D0, 0x8000);
        g.Create(plain with { Weenie = plain.Weenie with { Header = plain.Weenie.Header | WeenieHeaderFlags.IconOverlay, IconOverlayId = 0x060011D5 } });
        var bare = Item(0x80000B02, "Plain Thing", 0, 0x80);
        g.Create(bare);
        g.Frames(2);
        var examine = g.Examine;
        Assert.NotNull(examine.IconElement);
        Assert.Equal(GameExaminePanel.ItemIconId, examine.IconElement!.ElementId);
        Assert.Same(g.Screen.Root!.GetChildRecursive(GameExaminePanel.ItemFieldId), examine.IconElement.Parent);

        examine.Open(0x80000B01);
        g.Frames(3);
        Assert.Equal(0x060011D0u, examine.IconId);
        Assert.Equal(0x060011D5u, examine.Icon.OverlayId);
        Assert.Equal(0x8000u, examine.Icon.ItemType);
        Assert.False(examine.IconPending);
        Assert.True(examine.IconCompositions >= 1);
        Assert.NotNull(examine.IconElement.GeneratedImage);
        Assert.Equal(32, examine.IconElement.GeneratedImage!.Width);

        // The world table holds no icon for this object: the Appraisal's DataId 8 (property data) is the fallback it always was.
        examine.Open(0x80000B02);
        g.Answer(0x80000B02, new AceAppraisal { Success = true, Ints = new Dictionary<uint, int> { [AppraisalIds.Value] = 5 }, Strings = new Dictionary<uint, string> { [AppraisalIds.Name] = "Plain Thing" }, DataIds = new Dictionary<uint, uint> { [8] = 0x060011D6 } });
        g.Frames(3);
        Assert.Equal(0x060011D6u, examine.IconId);
        Assert.NotNull(examine.IconElement.GeneratedImage);

        examine.Close();
    }

    [RequiresLocalDatsFact]
    public void By_default_the_item_window_has_no_icon_as_retails_merged_layout_draws_it()
    {
        // Review R8: the icon is an addition to retail (SetAppraiseInfo 0x004B7ED0 skips it), so without --examine-icon on the panel makes no icon element and composes nothing.
        using var g = Enter();
        g.Frames(2);
        Assert.Null(g.Examine.IconElement);
        g.Examine.Open(Kit);
        g.Answer(Kit, KitAnswer());
        g.Frames(3);
        Assert.Equal("Handy Healing Kit", g.Examine.Title);
        Assert.Contains("Value: 1,200", g.Examine.Lines);
        Assert.Equal(0, g.Examine.IconCompositions);
        Assert.False(g.Examine.IconPending);
        Assert.DoesNotContain(g.Rig.Log.Lines, static l => l.Contains("TODO(m4-examine-icon)", StringComparison.Ordinal));
        g.Examine.Close();
    }

    [RequiresLocalDatsFact]
    public void An_answer_for_another_object_or_after_the_window_closed_changes_nothing()
    {
        using var g = Enter();
        g.Examine.Open(Kit);
        g.Answer(Mace, MaceAnswer());
        Assert.True(g.Examine.Pending);
        Assert.Equal(0, g.Examine.Fills);
        g.Examine.Close();
        Assert.False(g.Examine.IsOpen);
        g.Answer(Kit, KitAnswer());
        Assert.Equal(0, g.Examine.Fills);
    }

    [RequiresLocalDatsFact]
    public void The_close_button_closes_the_window()
    {
        using var g = Enter();
        g.Examine.Open(Kit);
        g.Frames(2);
        var close = g.Screen.Root!.GetChildRecursive(GameExaminePanel.CloseButtonId)!;
        Assert.True(g.Rig.Click(close));
        Assert.False(g.Examine.IsOpen);
    }

    [RequiresLocalDatsFact]
    public void A_creature_answer_switches_to_the_creature_window_with_the_level_and_vitals()
    {
        using var g = Enter();
        g.Examine.Open(Npc);
        g.Answer(Npc, CreatureAnswer(success: true));
        var examine = g.Examine;
        Assert.Equal(ExamineKind.Creature, examine.Kind);
        Assert.Equal("Thelnoth Cort the Healer", examine.Title);
        Assert.Equal("8", examine.LevelText);
        Assert.Equal("Health: 55 / 60", examine.VitalLines[0]);
        var level = Assert.IsAssignableFrom<UiTextElement>(g.Screen.Root!.GetChildRecursive(GameExaminePanel.LevelTextId));
        Assert.Equal("8", level.Text);
        Assert.True(g.Screen.Root!.GetChildRecursive(GameExaminePanel.CreatureFieldId)!.Visible);
        Assert.False(g.Screen.Root!.GetChildRecursive(GameExaminePanel.ItemFieldId)!.Visible);
    }

    [RequiresLocalDatsFact]
    public void The_figures_body_part_click_examines_the_worn_item_over_that_part_or_the_player()
    {
        using var g = Enter();
        g.Wield(Cuirass, 0x200);
        g.Frames(2);
        Assert.True(g.Doll.ExamineMask(514, g.Examine)); // the chest
        Assert.Equal(Cuirass, g.Examine.ShownGuid);
        Assert.Equal(ActionSource.Ui, g.Examine.LastIdentify!.Source);
        Assert.True(g.Doll.ExamineMask(1, g.Examine)); // the head: nothing there, so the player
        Assert.Equal(Player, g.Examine.ShownGuid);
        Assert.Equal(2, g.Examine.Opens);
        Assert.False(g.Doll.ExamineMask(0, g.Examine)); // no colour
        Assert.Equal(2, g.Examine.Opens);
    }

    [RequiresLocalDatsFact]
    public void The_click_map_image_exists_in_the_dats_and_holds_the_nine_hit_test_colours()
    {
        using var g = Enter();
        Assert.True(fixture.Dats.EnumMaps.TryResolve(EnumGroup.UiAsset, GamePaperDoll.ClickMapEnum, out var did)); // the same lookup Cg3DViewData.UiAssetDid makes
        Assert.NotEqual(0u, did);
        var images = g.Rig.Manager.Services.Images!;
        UiImage? image = null;
        for (var i = 0; i < 200 && images.TryGetImage(did, out image) != UiImageLookup.Ready; i++)
        {
            Thread.Sleep(10);
        }

        Assert.NotNull(image);
        var found = new HashSet<uint>();
        for (var p = 0; p < image!.Pixels.Length; p += 4)
        {
            var mask = GamePaperDoll.MaskOfColor(image.Pixels[p], image.Pixels[p + 1], image.Pixels[p + 2]);
            if (mask != 0)
            {
                found.Add(mask);
            }
        }

        Assert.Equal(9, found.Count);
    }
}
