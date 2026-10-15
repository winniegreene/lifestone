using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Dialogs;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Dialogs;

/// <summary>
/// The dialogs (m2-plan.md R15): the type-to-element map of UILAYOUT 2, opening, the queues and the topmost displacement,
/// closing with each class's response, the duration countdown, the wait dialog's key, NotInM2, and the retail dialog
/// layout.
/// </summary>
public sealed class DialogFactoryTests
{
    private const uint DialogLayout = 0x2100003C;
    private const uint F = UiClassIds.Field;

    [Theory]
    [InlineData(UiDialogType.Confirmation, 0x15u)]
    [InlineData(UiDialogType.Wait, 0x31u)]
    [InlineData(UiDialogType.Message, 0x24u)]
    [InlineData(UiDialogType.TextInput, 0x28u)]
    [InlineData(UiDialogType.ConfirmationTextInput, 0x2Cu)]
    [InlineData(UiDialogType.Menu, 0x1Bu)]
    [InlineData(UiDialogType.ConfirmationMenu, 0x1Fu)]
    [InlineData((UiDialogType)8, 0u)]
    public void Each_dialog_type_is_its_element_of_the_dialog_layout(UiDialogType type, uint element) =>
        Assert.Equal(element, DialogFactory.ElementOf(type));

    [Fact]
    public void A_message_dialog_opens_with_its_text_and_OK_closes_it_with_its_context()
    {
        var scene = Scene();
        var opened = new List<UiDialogOpened>();
        var closed = new List<UiDialogClosed>();
        scene.Manager.Dialogs.DialogOpened += opened.Add;
        scene.Manager.Dialogs.DialogClosed += closed.Add;

        var context = scene.Manager.Dialogs.MakeDialog(Message("Hello").WithFlag(UiPropertyIds.DialogModal, true));

        var dialog = Assert.IsType<UiMessageDialog>(scene.Manager.Dialogs.Find(context));
        Assert.Equal([new UiDialogOpened(context)], opened);
        Assert.Equal("Hello", Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text);
        Assert.True(dialog.BlocksClicks);
        Assert.True(scene.Manager.Dialogs.IsDialogOpen());

        scene.Click(350, 310);

        var done = Assert.Single(closed);
        Assert.Equal(context, done.Context);
        Assert.False(scene.Manager.Dialogs.IsDialogOpen());
        Assert.DoesNotContain(dialog, scene.Manager.Elements);
        Assert.Equal(0u, dialog.Context);
    }

    [Fact]
    public void Closing_a_dialog_logs_the_unconfirmed_hide_of_retails_virtual_once()
    {
        var scene = Scene();
        for (var i = 0; i < 2; i++)
        {
            scene.Manager.Dialogs.MakeDialog(Message("Hello").WithFlag(UiPropertyIds.DialogModal, true));
            scene.Click(350, 310);
        }

        Assert.Single(scene.Log.Lines, l => l.Contains("TODO(retailui-runtime): DialogFactory::DialogDone", StringComparison.Ordinal));
    }

    [Fact]
    public void A_queue_shows_one_dialog_at_a_time_in_order_and_the_immediate_queue_shows_at_once()
    {
        var scene = Scene();
        var dialogs = scene.Manager.Dialogs;

        var first = dialogs.MakeDialog(Message("1"));
        var second = dialogs.MakeDialog(Message("2"));
        var third = dialogs.MakeDialog(Message("3"));
        var now = dialogs.MakeDialog(Message("now").WithQueue(DialogFactory.ImmediateQueue));

        Assert.Equal([first, now], dialogs.OpenDialogs.Select(d => d.Context).Order());
        Assert.Equal(UiStateIds.DialogPendingTrue, dialogs.Find(first)!.GetChildRecursive(0x33)!.State);

        dialogs.CloseDialog(first);
        Assert.NotNull(dialogs.Find(second));
        dialogs.CloseDialog(second);
        Assert.Equal(UiStateIds.DialogPendingFalse, dialogs.Find(third)!.GetChildRecursive(0x33)!.State);
        dialogs.CloseDialog(now);

        Assert.Equal([third], dialogs.OpenDialogs.Select(d => d.Context));
        Assert.True(dialogs.IsDialogOpen(DialogFactory.DefaultQueue));
        Assert.False(dialogs.IsDialogOpen(DialogFactory.ImmediateQueue));
    }

    [Fact]
    public void A_topmost_dialog_displaces_the_open_one_which_comes_back_after_it()
    {
        var scene = Scene();
        var dialogs = scene.Manager.Dialogs;

        var plain = dialogs.MakeDialog(Message("plain"));
        var topmost = dialogs.MakeDialog(Message("topmost").WithFlag(UiPropertyIds.DialogTopmost, true));
        Assert.Equal([topmost], dialogs.OpenDialogs.Select(d => d.Context));

        dialogs.CloseDialog(topmost);

        var back = Assert.Single(dialogs.OpenDialogs);
        Assert.Equal(plain, back.Context);
        Assert.Equal("plain", Assert.IsType<UiTextElement>(back.GetChildRecursive(UiDialog.TextElement)).Text);
    }

    [Fact]
    public void Closing_a_waiting_dialog_removes_it_for_good()
    {
        var scene = Scene();
        var dialogs = scene.Manager.Dialogs;
        var closed = new List<uint>();
        dialogs.DialogClosed += c => closed.Add(c.Context);

        var first = dialogs.MakeDialog(Message("1"));
        var waiting = dialogs.MakeDialog(Message("2"));
        dialogs.CloseDialog(waiting);
        dialogs.CloseDialog(first);

        Assert.Equal([waiting, first], closed);
        Assert.False(dialogs.IsDialogOpen());
    }

    [Fact]
    public void A_dialog_a_close_callback_opens_in_the_same_queue_stays_closable_and_the_waiting_one_follows_it()
    {
        var scene = Scene();
        var dialogs = scene.Manager.Dialogs;
        var roots = scene.Manager.Roots.Count;
        uint deleting = 0;

        var confirm = dialogs.MakeCallbackDialog(Confirmation("Delete?"), _ => deleting = dialogs.MakeDialog(Message("Deleting...")));
        var waiting = dialogs.MakeDialog(Message("waiting"));
        scene.Click(330, 310);

        Assert.NotEqual(0u, deleting);
        Assert.NotEqual(confirm, deleting);
        Assert.Equal([deleting], dialogs.OpenDialogs.Select(d => d.Context));
        scene.Click(350, 310);
        Assert.Equal([waiting], dialogs.OpenDialogs.Select(d => d.Context));
        dialogs.CloseDialog(waiting);

        Assert.False(dialogs.IsDialogOpen());
        Assert.Equal(roots, scene.Manager.Roots.Count);
    }

    [Fact]
    public void A_dialog_a_callback_opens_during_a_reset_survives_it_with_its_own_callback()
    {
        var scene = Scene();
        var dialogs = scene.Manager.Dialogs;
        uint reopened = 0;
        var heard = 0;

        dialogs.MakeCallbackDialog(Message("1"), _ => reopened = dialogs.MakeCallbackDialog(Message("again"), _ => heard++));
        dialogs.MakeDialog(Message("2"));
        dialogs.Reset();

        Assert.Equal([reopened], dialogs.OpenDialogs.Select(d => d.Context));
        dialogs.CloseDialog(reopened);
        Assert.Equal(1, heard);
        Assert.False(dialogs.IsDialogOpen());
    }

    [Fact]
    public void Closing_a_menu_dialog_deletes_its_menus_popup_root_and_its_surface()
    {
        var scene = Scene();
        var dialogs = scene.Manager.Dialogs;
        var compositor = new UiCompositor(new FakeImageSource());
        compositor.Attach(scene.Manager);
        compositor.Composite(long.MaxValue);
        var (roots, elements, surfaces) = (scene.Manager.Roots.Count, scene.Manager.Elements.Count, compositor.RootCount);
        var items = new UiArrayProperty(UiPropertyIds.DialogMenuItemArray,
        [
            new UiStringInfoProperty(UiPropertyIds.DialogMenuItemText, UiStringRef.FromLiteral("North")),
            new UiStringInfoProperty(UiPropertyIds.DialogMenuItemText, UiStringRef.FromLiteral("South")),
        ]);

        for (var round = 0; round < 2; round++)
        {
            var context = dialogs.MakeDialog(UiDialogData.Of(UiDialogType.Menu, UiStringRef.FromLiteral("Go")).Set(items));
            var menu = Assert.IsType<UiMenu>(dialogs.Find(context)!.GetChildRecursive(UiMenuDialog.MenuElement));
            var popup = Assert.IsAssignableFrom<UiElement>(menu.Popup);
            Assert.Equal(2, menu.ItemCount);
            compositor.Composite(long.MaxValue);
            Assert.Equal((roots + 2, surfaces + 2), (scene.Manager.Roots.Count, compositor.RootCount));

            dialogs.CloseDialog(context);
            compositor.Composite(long.MaxValue);

            Assert.True(popup.IsDeleted);
            Assert.DoesNotContain(popup, scene.Manager.RootElement.Children);
            Assert.Equal((roots, elements, surfaces), (scene.Manager.Roots.Count, scene.Manager.Elements.Count, compositor.RootCount));
        }
    }

    [Fact]
    public void Retail_queueing_drops_a_displaced_dialog_and_opens_a_closed_waiting_one_anyway()
    {
        var scene = Scene();
        var dialogs = scene.Manager.Dialogs;
        Assert.False(dialogs.RetailQueueing);
        dialogs.RetailQueueing = true;
        var closed = new List<uint>();
        dialogs.DialogClosed += c => closed.Add(c.Context);

        var plain = dialogs.MakeDialog(Message("plain"));
        var topmost = dialogs.MakeDialog(Message("topmost").WithFlag(UiPropertyIds.DialogTopmost, true));
        dialogs.CloseDialog(topmost);

        Assert.Empty(dialogs.OpenDialogs);
        Assert.Equal([topmost], closed);
        Assert.DoesNotContain(plain, closed);

        var first = dialogs.MakeDialog(Message("1"));
        var waiting = dialogs.MakeDialog(Message("2"));
        dialogs.CloseDialog(waiting);
        dialogs.CloseDialog(first);

        Assert.Equal([waiting], dialogs.OpenDialogs.Select(d => d.Context));
        Assert.Equal("2", Assert.IsType<UiTextElement>(dialogs.OpenDialogs.Single().GetChildRecursive(UiDialog.TextElement)).Text);
    }

    [Fact]
    public void The_hosts_retail_glitch_flag_turns_retail_queueing_on()
    {
        var manager = new UiElementManager(new UiServices { Descs = new FakeDescSource([]), RetailGlitches = UiRetailGlitches.DialogQueueing });

        Assert.True(manager.Dialogs.RetailQueueing);
    }

    [Fact]
    public void A_confirmation_answers_yes_or_no_and_its_callback_hears_the_answer()
    {
        var scene = Scene();
        var dialogs = scene.Manager.Dialogs;
        var answers = new List<bool?>();

        dialogs.MakeCallbackDialog(Confirmation("Sure?"), d => answers.Add(d.ConfirmationResponse));
        scene.Click(330, 310);
        dialogs.MakeCallbackDialog(Confirmation("Sure?"), d => answers.Add(d.ConfirmationResponse));
        scene.Click(430, 310);

        Assert.Equal([true, false], answers);
    }

    [Fact]
    public void A_dialog_with_a_duration_counts_down_in_its_text_and_cancels_when_the_time_is_up()
    {
        var strings = new VariableStringSource();
        var scene = Scene(strings);
        var dialogs = scene.Manager.Dialogs;
        var answers = new List<bool?>();
        scene.Frame(1);

        var context = dialogs.MakeCallbackDialog(Confirmation(new UiStringRef(0x1234, 0x23000002)).WithDuration(2.5f), d => answers.Add(d.ConfirmationResponse));
        var text = Assert.IsType<UiTextElement>(dialogs.Find(context)!.GetChildRecursive(UiDialog.TextElement));
        Assert.Equal("2 left", text.Text);
        scene.Frame(1.2);
        Assert.Equal("1 left", text.Text);
        scene.Frame(1.2);
        Assert.Equal("0 left", text.Text);
        Assert.Empty(answers);
        scene.Frame(0.2);

        Assert.Equal([false], answers);
        Assert.False(dialogs.IsDialogOpen());
    }

    [Fact]
    public void A_counting_down_dialog_renders_its_text_only_when_the_whole_seconds_change()
    {
        var strings = new VariableStringSource();
        var scene = Scene(strings);
        var dialogs = scene.Manager.Dialogs;
        scene.Frame(1);

        var context = dialogs.MakeDialog(Confirmation(new UiStringRef(0x1234, 0x23000002)).WithDuration(5f));
        var text = Assert.IsType<UiTextElement>(dialogs.Find(context)!.GetChildRecursive(UiDialog.TextElement));
        Assert.Equal("5 left", text.Text);
        var renders = strings.Calls;
        for (var i = 0; i < 9; i++)
        {
            scene.Frame(0.1);
        }

        Assert.Equal("4 left", text.Text);
        Assert.Equal(renders + 1, strings.Calls);
    }

    [Fact]
    public void A_wait_dialog_closes_when_its_key_action_starts_unclaimed()
    {
        var scene = Scene();
        var dialogs = scene.Manager.Dialogs;

        dialogs.MakeDialog(UiDialogData.Of(UiDialogType.Wait, UiStringRef.FromLiteral("Wait")).Set(new UiEnumProperty(UiPropertyIds.DialogWaitKey, 0x27)));
        Assert.True(dialogs.IsDialogOpen());
        scene.Tap(DikScanCodes.Escape);
        Assert.True(dialogs.IsDialogOpen());

        var escape = new RecordingActionSink(takes: false);
        scene.Input.RegisterInputMap(UiInputRouter.AcceptEscapeMap, escape, 1000);
        scene.Tap(DikScanCodes.Escape);

        Assert.False(dialogs.IsDialogOpen());
    }

    [Fact]
    public void A_type_the_layout_has_no_element_for_opens_nothing_loudly_but_still_gets_a_context()
    {
        var scene = Scene();

        var context = scene.Manager.Dialogs.MakeDialog(UiDialogData.Of(UiDialogType.TextInput, UiStringRef.FromLiteral("?")));

        Assert.NotEqual(0u, context);
        Assert.Empty(scene.Manager.Dialogs.OpenDialogs);
        Assert.Contains(scene.Log.Lines, l => l.Contains("is not created", StringComparison.Ordinal) || l.Contains("does not", StringComparison.Ordinal));
    }

    [Fact]
    public void A_dialog_that_is_not_created_holds_no_queue_and_in_its_turn_the_one_behind_it_opens()
    {
        var scene = Scene();
        var dialogs = scene.Manager.Dialogs;
        var closed = new List<uint>();
        dialogs.DialogClosed += c => closed.Add(c.Context);
        var heard = 0;

        var input = dialogs.MakeCallbackDialog(TextInput(), _ => heard++);
        Assert.False(dialogs.IsDialogOpen(DialogFactory.DefaultQueue));
        Assert.False(dialogs.IsDialogOpen());
        var message = dialogs.MakeDialog(Message("hello"));

        Assert.Equal([message], dialogs.OpenDialogs.Select(d => d.Context));
        Assert.True(dialogs.IsDialogOpen(DialogFactory.DefaultQueue));
        Assert.Equal(UiStateIds.DialogPendingFalse, dialogs.Find(message)!.GetChildRecursive(0x33)!.State);

        // Queued behind the message, it is skipped in its turn and the dialog behind it opens.
        var skipped = dialogs.MakeCallbackDialog(TextInput(), _ => heard++);
        var next = dialogs.MakeDialog(Message("next"));
        dialogs.CloseDialog(message);
        Assert.Equal([next], dialogs.OpenDialogs.Select(d => d.Context));

        // The immediate queue keeps nothing of one either.
        dialogs.MakeDialog(TextInput().WithQueue(DialogFactory.ImmediateQueue));
        Assert.False(dialogs.IsDialogOpen(DialogFactory.ImmediateQueue));

        // Nothing opened, so nothing closes or answers.
        dialogs.CloseDialog(input);
        dialogs.CloseDialog(skipped);
        dialogs.CloseDialog(next);
        Assert.Equal([message, next], closed);
        Assert.Equal(0, heard);
        Assert.False(dialogs.IsDialogOpen());
    }

    [Fact]
    public void Retail_queueing_leaves_the_dialogs_behind_one_that_is_not_created_waiting_until_the_queue_moves_again()
    {
        var scene = Scene();
        var dialogs = scene.Manager.Dialogs;
        dialogs.RetailQueueing = true;

        var first = dialogs.MakeDialog(Message("1"));
        dialogs.MakeDialog(TextInput());
        var waiting = dialogs.MakeDialog(Message("2"));
        dialogs.CloseDialog(first);

        Assert.Empty(dialogs.OpenDialogs);
        Assert.False(dialogs.IsDialogOpen(DialogFactory.DefaultQueue));

        // The queue is free, so a new dialog opens at once; when it closes, the waiting one follows.
        var later = dialogs.MakeDialog(Message("3"));
        Assert.Equal([later], dialogs.OpenDialogs.Select(d => d.Context));
        dialogs.CloseDialog(later);
        Assert.Equal([waiting], dialogs.OpenDialogs.Select(d => d.Context));
    }

    [Fact]
    public void Not_in_M2_opens_a_modal_message_dialog_with_a_lifestone_literal_and_logs_the_placeholder()
    {
        var scene = Scene();

        var context = NotInM2.Show(scene.Manager.Dialogs, scene.Log, "Credits");

        var dialog = Assert.IsType<UiMessageDialog>(scene.Manager.Dialogs.Find(context));
        Assert.Equal("Not in Lifestone M2: Credits", Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text);
        Assert.True(dialog.BlocksClicks);
        Assert.Contains(scene.Log.Lines, l => l.Contains("placeholder", StringComparison.Ordinal) && l.Contains("Credits", StringComparison.Ordinal));
    }

    internal static UiDialogData Message(string text) => UiDialogData.Of(UiDialogType.Message, UiStringRef.FromLiteral(text));

    /// <summary>A text input dialog: the test layout, like the end-of-retail one, has no element 0x28 for it.</summary>
    private static UiDialogData TextInput() => UiDialogData.Of(UiDialogType.TextInput, UiStringRef.FromLiteral("?"));

    private static UiDialogData Confirmation(string text) => Confirmation(UiStringRef.FromLiteral(text));

    private static UiDialogData Confirmation(UiStringRef text) => UiDialogData.Of(UiDialogType.Confirmation, text);

    /// <summary>
    /// A manager whose dialog layout (UILAYOUT 2 → 0x2100003C) holds the message (0x24), confirmation (0x15), wait (0x31)
    /// and menu (0x1B) dialogs, each a full-screen root with a 200×100 popup at (300, 250) holding the text and its
    /// buttons (the menu dialog also its menu, whose popup 0x60 and text row 0x62 are top-level elements of that layout).
    /// </summary>
    internal static UiScene Scene(IUiStringSource? strings = null)
    {
        UiElementDesc Dialog(uint id, uint type, params UiElementDesc[] buttons) =>
            UiDescs.Element(id, type, 0, 0, 800, 600, layout: DialogLayout, children:
            [
                UiDescs.Element(UiDialog.PopupElement, F, 300, 250, 200, 100, layout: DialogLayout, children:
                [
                    UiDescs.Element(UiDialog.TextElement, UiClassIds.Text, 10, 10, 180, 20, layout: DialogLayout),
                    .. buttons,
                ]),
                UiDescs.Element(0x33, F, 0, 0, 10, 10, layout: DialogLayout, states: [UiDescs.State(UiStateIds.DialogPendingTrue), UiDescs.State(UiStateIds.DialogPendingFalse)]),
            ]);
        UiElementDesc Button(uint id, int x) => UiDescs.Element(id, UiClassIds.Button, x, 50, 60, 20, layout: DialogLayout);

        var menu = UiDescs.Element(UiMenuDialog.MenuElement, UiClassIds.Menu, 20, 30, 100, 20, layout: DialogLayout, properties:
        [
            new UiDataIdProperty(UiPropertyIds.MenuPopupLayout, DialogLayout),
            new UiDataIdProperty(UiPropertyIds.MenuTextItemLayout, DialogLayout),
            new UiEnumProperty(UiPropertyIds.MenuPopup, 0x60),
            new UiEnumProperty(UiPropertyIds.MenuListbox, 0x61),
            new UiEnumProperty(UiPropertyIds.MenuTextItem, 0x62),
        ]);
        var popup = UiDescs.Element(0x60, F, 0, 0, 100, 60, layout: DialogLayout, children:
        [
            UiDescs.Element(0x61, UiClassIds.ListBox, 0, 0, 100, 60, layout: DialogLayout),
        ]);
        var layout = UiDescs.Layout(DialogLayout, 800, 600,
            Dialog(0x24, UiClassIds.MessageDialog, Button(UiMessageDialog.OkButton, 20)),
            Dialog(0x15, UiClassIds.ConfirmationDialog, Button(UiConfirmationDialog.YesButton, 20), Button(UiConfirmationDialog.NoButton, 120)),
            Dialog(0x31, UiClassIds.WaitDialog),
            Dialog(0x1B, UiClassIds.MenuDialog, menu, Button(UiMenuDialog.OkButton, 120)),
            popup,
            UiDescs.Element(0x62, UiClassIds.Text, 0, 0, 100, 20, layout: DialogLayout));
        return new UiScene([], extraLayouts: [layout], layoutEnums: new Dictionary<uint, uint> { [UiLayoutEnums.Dialog] = DialogLayout }, strings: strings);
    }

    /// <summary>String 0x1234 of table 0x23000002 is "%time% left", the variable rendered as given; counts the renders.</summary>
    private sealed class VariableStringSource : IUiStringSource
    {
        public int Calls { get; private set; }

        public bool TryGetString(in UiStringRef reference, out string text) => TryGetString(reference, null, out text);

        public bool TryGetString(in UiStringRef reference, UiStringVariables? variables, out string text)
        {
            Calls++;
            text = string.Empty;
            if (reference.StringId != 0x1234 || variables is null || !variables.TryGetValue(StringHash.Tokenize("time"), out var time))
            {
                return false;
            }

            text = time + " left";
            return true;
        }
    }
}

/// <summary>The retail dialog layout (UILAYOUT 2, 0x2100003C) in the user's dats.</summary>
public sealed class RetailDialogTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    [RequiresLocalDatsFact]
    public void Every_dialog_type_but_text_input_opens_as_its_class_from_the_retail_layout()
    {
        var manager = Manager(out var log);
        var dialogs = manager.Dialogs;

        foreach (var type in Enum.GetValues<UiDialogType>())
        {
            var context = dialogs.MakeDialog(UiDialogData.Of(type, UiStringRef.FromLiteral(type.ToString())).WithQueue(DialogFactory.ImmediateQueue));
            var dialog = dialogs.Find(context);
            if (type == UiDialogType.TextInput)
            {
                // The end-of-retail layout has no element 0x28: no text input dialog opens, as retail.
                Assert.Null(dialog);
                continue;
            }

            Assert.NotNull(dialog);
            Assert.Equal(type switch
            {
                UiDialogType.Confirmation => typeof(UiConfirmationDialog),
                UiDialogType.Wait => typeof(UiWaitDialog),
                UiDialogType.Message => typeof(UiMessageDialog),
                UiDialogType.ConfirmationTextInput => typeof(UiConfirmationTextInputDialog),
                UiDialogType.Menu => typeof(UiMenuDialog),
                _ => typeof(UiConfirmationMenuDialog),
            }, dialog.GetType());
            Assert.Equal(type.ToString(), Assert.IsType<UiTextElement>(dialog.GetChildRecursive(UiDialog.TextElement)).Text);
            Assert.NotNull(dialog.GetChildRecursive(UiDialog.PopupElement));
        }

        Assert.Equal(6, dialogs.OpenDialogs.Count());
        Assert.DoesNotContain(log.Entries, e => e.Message.Contains("stands in", StringComparison.Ordinal));
    }

    [RequiresLocalDatsFact]
    public void The_retail_dialog_layout_holds_the_six_dialog_elements_and_no_text_input()
    {
        var layouts = new UiLayouts(fixture.Dats);
        Assert.True(layouts.TryResolveLayoutEnum(UiLayoutEnums.Dialog, out var layoutId));
        Assert.Equal(0x2100003Cu, layoutId);
        var layout = layouts.GetLayout(layoutId);

        foreach (var type in Enum.GetValues<UiDialogType>())
        {
            var element = DialogFactory.ElementOf(type);
            Assert.Equal(type != UiDialogType.TextInput, layout.Elements.ContainsKey(element));
        }
    }

    [RequiresLocalDatsFact]
    public void A_retail_menu_dialog_lists_its_items_and_answers_the_chosen_index()
    {
        var manager = Manager(out _);
        var items = new UiArrayProperty(UiPropertyIds.DialogMenuItemArray,
        [
            new UiStringInfoProperty(UiPropertyIds.DialogMenuItemText, UiStringRef.FromLiteral("North")),
            new UiStringInfoProperty(UiPropertyIds.DialogMenuItemText, UiStringRef.FromLiteral("South")),
        ]);
        int? answer = null;

        var context = manager.Dialogs.MakeCallbackDialog(UiDialogData.Of(UiDialogType.Menu, UiStringRef.FromLiteral("Go")).Set(items), d => answer = d.MenuChoice);
        var dialog = Assert.IsType<UiMenuDialog>(manager.Dialogs.Find(context));
        var menu = Assert.IsType<UiMenu>(dialog.GetChildRecursive(UiMenuDialog.MenuElement));
        Assert.Equal(2, menu.ItemCount);
        Assert.Equal(0, menu.SelectedIndex);
        menu.SetSelectedItem(menu.GetItem(1), broadcast: true);
        dialog.CancelDialog();

        Assert.Equal(-1, answer);
    }

    private UiElementManager Manager(out RecordingLogSink log)
    {
        log = new RecordingLogSink();
        return new UiElementManager(new UiServices
        {
            Descs = UiDescSource.From(new UiLayouts(fixture.Dats)),
            Fonts = new UiFontsSource(fixture.Dats, log),
            Strings = new UiStringsSource(new UiStrings(new UiStringTables(fixture.Dats)), UiLanguageInfo.Load(fixture.Dats)),
            Log = log,
        });
    }
}
