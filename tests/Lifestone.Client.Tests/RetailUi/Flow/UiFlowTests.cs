using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Tests.RetailUi.Dialogs;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Flow;

/// <summary>
/// The UI flow (m2-plan.md R15): modes start at the next frame with their screen and queued error, closing every dialog
/// after the old screen; gmUIFlow's eight modes, first mode, default pointer and input map; the character-error routing
/// table for every code; the lost connection; the notices to the spew box.
/// </summary>
public sealed class UiFlowTests
{
    [Fact]
    public void A_queued_mode_starts_at_the_next_frame_and_the_old_screen_is_hidden_and_disposed_first()
    {
        var scene = new UiScene([]);
        var log = new List<string>();
        using var flow = new UiFlow(scene.Manager);
        flow.RegisterFrameworkClass(1, () => new RecordingScreen("A", log));
        flow.RegisterFrameworkClass(2, () => new RecordingScreen("B", log));
        Assert.False(flow.RegisterFrameworkClass(1, () => new RecordingScreen("A2", log)));

        flow.QueueUIMode(1);
        Assert.Equal(0u, flow.CurrentMode);
        scene.Frame();
        Assert.Equal(1u, flow.CurrentMode);
        flow.QueueUIModeWithError(2, UiStringRef.FromLiteral("oops"));
        flow.Update();
        scene.Frame();

        Assert.Equal(2u, flow.CurrentMode);
        Assert.Equal(["A made", "A shown", "A update", "A hidden", "A disposed", "B made", "B error \"oops\"", "B shown"], log);
    }

    [Fact]
    public void A_mode_change_closes_every_dialog_after_the_old_screen_goes_and_one_a_close_callback_opens_survives()
    {
        var scene = DialogFactoryTests.Scene();
        var dialogs = scene.Manager.Dialogs;
        var log = new List<string>();
        dialogs.DialogClosed += c => log.Add($"dialog {c.Context} closed");
        var flow = new UiFlow(scene.Manager);
        flow.RegisterFrameworkClass(1, () => new RecordingScreen("A", log));
        flow.RegisterFrameworkClass(2, () => new RecordingScreen("B", log));

        // The first mode has no old screen, so nothing resets: a dialog opened before it stays.
        var early = dialogs.MakeDialog(DialogFactoryTests.Message("early"));
        flow.QueueUIMode(1);
        scene.Frame();
        Assert.Equal([early], dialogs.OpenDialogs.Select(d => d.Context));

        uint reopened = 0;
        var now = dialogs.MakeDialog(DialogFactoryTests.Message("now").WithQueue(DialogFactory.ImmediateQueue));
        var waiting = dialogs.MakeCallbackDialog(DialogFactoryTests.Message("waiting"), _ => reopened = dialogs.MakeDialog(DialogFactoryTests.Message("after A")));
        log.Clear();
        flow.QueueUIMode(2);
        scene.Frame();

        Assert.Equal(2u, flow.CurrentMode);
        Assert.Equal(["A hidden", "A disposed", $"dialog {now} closed", $"dialog {early} closed", $"dialog {waiting} closed", "B made", "B shown"], log);
        Assert.NotEqual(0u, reopened);
        Assert.Equal([reopened], dialogs.OpenDialogs.Select(d => d.Context));
        Assert.True(dialogs.IsDialogOpen(DialogFactory.DefaultQueue));

        // Disposing the flow closes the dialogs after the last screen, too.
        log.Clear();
        flow.Dispose();
        Assert.Equal(["B disposed", $"dialog {reopened} closed"], log);
        Assert.False(dialogs.IsDialogOpen());
    }

    [Fact]
    public void A_mode_queued_while_the_old_screen_goes_stays_queued_for_the_next_frame()
    {
        var scene = DialogFactoryTests.Scene();
        var log = new List<string>();
        using var flow = new UiFlow(scene.Manager);
        flow.RegisterFrameworkClass(1, () => new RecordingScreen("A", log));
        flow.RegisterFrameworkClass(2, () => new RecordingScreen("B", log));
        flow.RegisterFrameworkClass(3, () => new RecordingScreen("C", log));
        flow.QueueUIMode(1);
        scene.Frame();
        scene.Manager.Dialogs.MakeCallbackDialog(DialogFactoryTests.Message("bye"), _ => flow.QueueUIMode(3));

        flow.QueueUIMode(2);
        scene.Frame();
        Assert.Equal((2u, 3u), (flow.CurrentMode, flow.NextMode));
        scene.Frame();

        Assert.Equal(3u, flow.CurrentMode);
        Assert.Equal(["A made", "A shown", "A hidden", "A disposed", "B made", "B shown", "B hidden", "B disposed", "C made", "C shown"], log);
    }

    [Fact]
    public void A_later_queue_replaces_an_earlier_one_and_a_mode_with_no_screen_stays_queued()
    {
        var scene = new UiScene([]);
        var log = new List<string>();
        using var flow = new UiFlow(scene.Manager);
        flow.RegisterFrameworkClass(1, () => new RecordingScreen("A", log));
        var changes = new List<UiModeChanged>();
        flow.ModeChanged += changes.Add;

        flow.QueueUIModeWithError(1, UiStringRef.FromLiteral("old"));
        flow.QueueUIMode(1);
        scene.Frame();
        flow.QueueUIMode(9);
        scene.Frame();

        Assert.Equal(["A made", "A shown"], log);
        Assert.Equal([new UiModeChanged(0, 1, null)], changes);
        Assert.Equal(9u, flow.NextMode);
    }

    [Fact]
    public void Gm_ui_flow_registers_the_eight_modes_starts_with_the_data_check_and_sets_the_default_pointer()
    {
        var scene = new UiScene([]);
        var made = new List<uint>();
        using var gm = new GmUiFlow(scene.Manager, mode => { made.Add(mode); return null; }, new UiCursor(1, "Default", 0x06001234, 0, 0));

        Assert.Equal(UiModes.RetailOrder.Order(), gm.Flow.RegisteredModes.Order());
        Assert.Equal(UiModes.DataPatch, gm.Flow.NextMode);
        Assert.Equal((0x06001234u, (0, 0)), (scene.Manager.Cursor.DefaultCursorDid, scene.Manager.Cursor.DefaultHotspot));
        Assert.Contains(new UiInputMapEntry(GmUiFlow.InputMap, gm, GmUiFlow.InputMapPriority), scene.Input.InputMaps);

        scene.Frame();
        Assert.Equal([UiModes.DataPatch], made);
        Assert.Equal(UiModes.DataPatch, gm.Flow.CurrentMode);
        Assert.Equal(
            [0x10000001u, 0x10000002u, 0x10000003u, 0x10000005u, 0x10000008u, 0x10000009u, 0x1000000Au, 0x1000000Bu],
            UiModes.RetailOrder.Order());
    }

    [Fact]
    public void Without_a_default_pointer_gm_ui_flow_says_so()
    {
        var scene = new UiScene([]);

        using var gm = new GmUiFlow(scene.Manager, _ => null, null);

        Assert.Contains(scene.Log.Lines, l => l.Contains("no default pointer", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1u, UiModes.Disconnected, "ID_CHAR_ERROR_LOGON")]
    [InlineData(3u, UiModes.Disconnected, "ID_CHAR_ERROR_ACCOUNT_LOGON")]
    [InlineData(4u, UiModes.Disconnected, "ID_CHAR_ERROR_SERVER_CRASH")]
    [InlineData(8u, UiModes.Disconnected, "ID_CHAR_ERROR_SERVER_CRASH")]
    [InlineData(9u, UiModes.Disconnected, "ID_CHAR_ERROR_ACCOUNT_INVALID")]
    [InlineData(10u, UiModes.Disconnected, "ID_CHAR_ERROR_ACCOUNT_DOESNT_EXIST")]
    [InlineData(14u, UiModes.Disconnected, "ID_CHAR_ERROR_ENTER_GAME_PLAYER_ACCOUNT_MISSING")]
    [InlineData(21u, UiModes.Disconnected, "ID_CHAR_ERROR_LOGON_SERVER_FULL")]
    [InlineData(24u, UiModes.Disconnected, "ID_CHAR_ERROR_SUBSCRIPTION_EXPIRED")]
    [InlineData(5u, UiModes.CharacterManagement, "ID_CHAR_ERROR_LOGOFF")]
    [InlineData(6u, UiModes.CharacterManagement, "ID_CHAR_ERROR_DELETE")]
    [InlineData(11u, UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_GENERIC")]
    [InlineData(12u, UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_STRESS_ACCOUNT")]
    [InlineData(13u, UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_CHARACTER_IN_WORLD")]
    [InlineData(15u, UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_CHARACTER_NOT_OWNED")]
    [InlineData(16u, UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_CHARACTER_IN_WORLD_SERVER")]
    [InlineData(17u, UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_OLD_CHARACTER")]
    [InlineData(18u, UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_CORRUPT_CHARACTER")]
    [InlineData(19u, UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_START_SERVER_DOWN")]
    [InlineData(20u, UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_COULDNT_PLACE_CHARACTER")]
    [InlineData(23u, UiModes.CharacterManagement, "ID_CHAR_ERROR_ENTER_GAME_CHARACTER_LOCKED")]
    public void A_character_error_queues_its_mode_with_its_pregame_string(uint code, uint mode, string token)
    {
        var scene = new UiScene([]);
        using var gm = new GmUiFlow(scene.Manager, _ => null, null);

        Assert.True(gm.RecvNoticeCharacterError(code));

        Assert.Equal(mode, gm.Flow.NextMode);
        Assert.Equal(new UiStringRef(StringHash.Tokenize(token), 0x23000002), gm.Flow.NextError);
    }

    [Fact]
    public void Every_other_character_error_code_changes_nothing_and_the_table_has_exactly_the_retail_codes()
    {
        var scene = new UiScene([]);
        using var gm = new GmUiFlow(scene.Manager, _ => null, null);

        foreach (var code in new uint[] { 0, 2, 7, 22, 25, 100 })
        {
            Assert.False(gm.RecvNoticeCharacterError(code));
        }

        Assert.Equal(UiModes.DataPatch, gm.Flow.NextMode);
        Assert.Equal(new uint[] { 1, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 23, 24 }, GmUiFlow.Routes.Keys.Order());
    }

    [Fact]
    public void A_session_character_error_reaches_the_flow_through_the_event_bus()
    {
        var scene = new UiScene([]);
        using var gm = new GmUiFlow(scene.Manager, _ => null, null);
        var bus = new FakeEventBus();

        using (gm.Attach(bus))
        {
            bus.Publish(new CharacterErrorReceived(5));
        }

        bus.Publish(new CharacterErrorReceived(1));

        Assert.Equal(UiModes.CharacterManagement, gm.Flow.NextMode);
    }

    [Fact]
    public void A_lost_connection_queues_the_disconnected_screen_unless_it_shows_or_is_queued()
    {
        var scene = new UiScene([]);
        var log = new List<string>();
        using var gm = new GmUiFlow(scene.Manager, mode => new RecordingScreen($"{mode:X}", log), null);
        scene.Frame();

        Assert.True(gm.RecvNoticeServerDied());
        Assert.Equal(new UiStringRef(StringHash.Tokenize(GmUiFlow.ConnectionLostToken), 0x23000002), gm.Flow.NextError);
        Assert.False(gm.RecvNoticeServerDied());
        scene.Frame();
        Assert.Equal(UiModes.Disconnected, gm.Flow.CurrentMode);
        Assert.False(gm.RecvNoticeServerDied());
        Assert.Contains($"10000002 error {new UiStringRef(StringHash.Tokenize(GmUiFlow.ConnectionLostToken), 0x23000002)}", log);
    }

    [Fact]
    public void Type_26_goes_to_the_spew_box_and_the_rest_to_chat_and_an_unheard_notice_is_a_labelled_placeholder()
    {
        var log = new RecordingLogSink();
        var router = new UiNoticeRouter(log);
        var unheard = new List<UiNotice>();
        router.Unheard += unheard.Add;
        var portal = UiStringRef.FromLiteral("In Portal Space - Please Wait...");

        router.DisplayStringInfo(UiNoticeRouter.SpewBoxTextType, portal);
        var spew = new RecordingNoticeSink();
        var chat = new RecordingNoticeSink();
        router.SpewBox = spew;
        router.Chat = chat;
        router.DisplayStringInfo(UiNoticeRouter.SpewBoxTextType, portal);
        router.DisplayStringInfo(0, UiStringRef.FromLiteral("hello"));

        Assert.Equal([new UiNotice(26, portal)], unheard);
        Assert.Contains(log.Lines, l => l.Contains("placeholder", StringComparison.Ordinal) && l.Contains("spew box", StringComparison.Ordinal));
        Assert.Equal([new UiNotice(26, portal)], spew.Notices);
        Assert.Equal([new UiNotice(0, UiStringRef.FromLiteral("hello"))], chat.Notices);
        Assert.True(UiNoticeRouter.IsForSpewBox(0x1A));
        Assert.False(UiNoticeRouter.IsForSpewBox(1));
    }

    [Fact]
    public void A_notice_is_held_while_the_windows_are_expected_and_shown_when_they_register_and_unheard_if_they_never_do()
    {
        var router = new UiNoticeRouter(new RecordingLogSink());
        var unheard = new List<UiNotice>();
        router.Unheard += unheard.Add;
        var portal = UiStringRef.FromLiteral("In Portal Space - Please Wait...");

        router.ExpectWindows = true;
        router.DisplayStringInfo(UiNoticeRouter.SpewBoxTextType, portal);
        router.DisplayStringInfo(0, UiStringRef.FromLiteral("hello"));
        Assert.Equal(2, router.HeldCount);
        Assert.Empty(unheard);

        var spew = new RecordingNoticeSink();
        router.SpewBox = spew;
        Assert.Equal([new UiNotice(26, portal)], spew.Notices);
        Assert.Equal(1, router.HeldCount);

        router.ExpectWindows = false; // the chat window never came: what is left is unheard, as before
        Assert.Equal([new UiNotice(0, UiStringRef.FromLiteral("hello"))], unheard);
        Assert.Equal(0, router.HeldCount);

        for (var i = 0; i < UiNoticeRouter.HeldLimit + 5; i++)
        {
            router.ExpectWindows = true;
            router.SpewBox = null;
            router.DisplayStringInfo(UiNoticeRouter.SpewBoxTextType, UiStringRef.FromLiteral($"n{i}"));
        }

        Assert.Equal(UiNoticeRouter.HeldLimit, router.HeldCount);
    }

    [Fact]
    public void Gm_ui_flow_logs_an_action_on_its_input_map_as_unknown_and_passes_it_on()
    {
        var scene = new UiScene([]);
        using var gm = new GmUiFlow(scene.Manager, _ => null, null);

        Assert.False(gm.OnInputAction(GmUiFlow.InputMap, 0x55, new RetailActivation(true, 1, RetailToggleType.Impulse, 0, 0)));

        Assert.Contains(scene.Log.Lines, l => l.Contains("TODO(retailui-runtime): gmUIFlow", StringComparison.Ordinal));
    }

    private sealed class RecordingScreen(string name, List<string> log) : IUiScreen
    {
        private readonly string _name = Record(name, log);

        public void DisplayError(in UiStringRef text) => log.Add($"{_name} error {text}");

        public void Update() => log.Add($"{_name} update");

        public void Show(bool visible) => log.Add($"{_name} {(visible ? "shown" : "hidden")}");

        public void Dispose() => log.Add($"{_name} disposed");

        private static string Record(string name, List<string> log)
        {
            log.Add($"{name} made");
            return name;
        }
    }

    private sealed class RecordingNoticeSink : IUiNoticeSink
    {
        public List<UiNotice> Notices { get; } = [];

        public void DisplayStringInfo(int textType, in UiStringRef text) => Notices.Add(new UiNotice(textType, text));
    }

    private sealed class FakeEventBus : IEventBus
    {
        private readonly List<Delegate> _handlers = [];

        public IDisposable Subscribe<T>(BusHandler<T> handler)
            where T : struct, IGameEvent
        {
            _handlers.Add(handler);
            return new Subscription(() => _handlers.Remove(handler));
        }

        public IDisposable SubscribeRaw(RawMessageHandler handler) => throw new NotSupportedException();

        public long Count<T>()
            where T : struct, IGameEvent => 0;

        public void Publish<T>(T e)
            where T : struct, IGameEvent
        {
            foreach (var handler in _handlers.OfType<BusHandler<T>>().ToArray())
            {
                handler(in e);
            }
        }

        private sealed class Subscription(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}

/// <summary>The pregame strings the flow names, in the user's dats.</summary>
public sealed class UiFlowDatTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresLocalDatsFact]
    public void The_pregame_table_enum_is_table_0x23000002_and_holds_every_error_the_flow_shows_but_one()
    {
        var tables = new UiStringTables(fixture.Dats);
        Assert.True(tables.TryResolveTableEnum(UiStringTables.UiPregameEnum, out var tableId));
        Assert.Equal(GmUiFlow.PregameTableId, tableId);
        var table = tables.GetTable(tableId);

        var missing = GmUiFlow.Routes.Values.Select(r => r.Token).Append(GmUiFlow.ConnectionLostToken).Distinct()
            .Where(token => !table.TryGetEntry(StringHash.Tokenize(token), out _)).ToArray();

        // Retail's table has no string for code 17 (an old character): retail shows its string-table error text there
        // (docs/retail-ui.md, R15 decisions).
        Assert.Equal(["ID_CHAR_ERROR_ENTER_GAME_OLD_CHARACTER"], missing);
    }

    [RequiresLocalDatsFact]
    public void The_default_pointer_is_UICURSOR_enum_1()
    {
        var cursors = new UiCursors(fixture.Dats);

        Assert.Equal(UiCursors.DefaultEnum, cursors.Default.Enum);
        Assert.NotEqual(0u, cursors.Default.ImageId);
    }
}
