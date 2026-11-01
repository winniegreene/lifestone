using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.Tests.RetailUi.TestSupport;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.Core;

/// <summary>
/// M5 6 u1 (D33), the window layout file: its name (retail's <c>CreateScreenLayoutPath</c>), its text (<c>SaveScreenLayout</c>), the round trip over a store, a missing or malformed file read as the defaults with
/// one warning, a window off the screen moved back in, and the chat masks kept beside the windows.
/// </summary>
public sealed class ScreenLayoutStoreTests
{
    private const uint Chat = 0x10000601;
    private const uint Toolbar = 0x10000603;
    private const uint Float1 = 0x10000505;

    private static UiScene Scene() => new(
    [
        UiDescs.Element(Chat, UiClassIds.Field, 10, 400, 300, 100),
        UiDescs.Element(Toolbar, UiClassIds.Field, 0, 560, 800, 40),
        UiDescs.Element(Float1, UiClassIds.Field, 500, 100, 200, 120),
    ]);

    private static ChatMaskBinding[] Masks(ulong[] values) =>
    [
        new(Chat, () => values[0], m => values[0] = m),
        new(Float1, () => values[1], m => values[1] = m),
    ];

    private static (int X, int Y) Place(UiScene scene, uint id) => (scene.Root.GetChildRecursive(id)!.Box.X0, scene.Root.GetChildRecursive(id)!.Box.Y0);

    [Fact]
    public void The_name_is_retails_UI_world_character_width_height_and_keeps_a_file_name_safe()
    {
        Assert.Equal("UI-Dereth-Holtburg Hunter-800-600.txt", ScreenLayoutFile.Name("Dereth", "Holtburg Hunter", 800, 600));
        Assert.Equal("UI-Dereth-Zo_e-1280-720.txt", ScreenLayoutFile.Name("Dereth", "Zo/e", 1280, 720));
        Assert.Equal("UI-W_-A_B-800-600.txt", ScreenLayoutFile.Name("W?", "A:B", 800, 600));
    }

    [Fact]
    public void A_window_line_is_retails_format_and_a_mask_line_is_marked_as_lifestones()
    {
        Assert.Equal("<CHAT> X:10 Y: 400 W: 300 H: 100", ScreenLayoutFile.Line("<CHAT>", 10, 400, 300, 100));
        Assert.Equal("LS-CHATMASK <FCH1> 0x00000000000000FF", ScreenLayoutFile.MaskLine("<FCH1>", 0xFF));
        var parsed = ScreenLayoutFile.Parse("<CHAT> X:10 Y: 400 W: 300 H: 100\r\nLS-CHATMASK <FCH1> 0x00000000000000FF\n\n<SBOX> X:0 Y: 0 W: 800 H: 600\n");
        Assert.Equal((10, 400, 300, 100), parsed.Windows["<CHAT>"]);
        Assert.Equal(0xFFUL, parsed.ChatMasks["<FCH1>"]);
        Assert.Equal(0, parsed.BadLines);
    }

    [Fact]
    public void A_layout_saved_is_restored_over_the_defaults_with_the_masks()
    {
        var store = new MemoryScreenLayoutStore();
        var log = new RecordingLogSink();
        var keeper = new ScreenLayoutKeeper(store, log);
        var first = Scene();
        var masks = new ulong[] { 0x11, 0x22 };
        first.Root.GetChildRecursive(Chat)!.MoveTo(50, 60);
        first.Root.GetChildRecursive(Float1)!.ResizeTo(250, 130);

        Assert.True(keeper.Save("UI-w-c-800-600.txt", first.Root, Masks(masks)));
        var text = Assert.IsType<string>(store.Read("UI-w-c-800-600.txt"));
        Assert.Contains("<CHAT> X:50 Y: 60 W: 300 H: 100\n", text, StringComparison.Ordinal);
        Assert.Contains("<FCH1> X:500 Y: 100 W: 250 H: 130\n", text, StringComparison.Ordinal);
        Assert.Contains("LS-CHATMASK <CHAT> 0x0000000000000011\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<SBOX>", text, StringComparison.Ordinal);

        var next = Scene(); // the next world entry: the layout's defaults, then the file
        var restored = new ulong[] { 0, 0 };
        var load = keeper.Load("UI-w-c-800-600.txt", next.Root, Masks(restored));

        Assert.True(load.Found);
        Assert.Equal((50, 60), Place(next, Chat));
        Assert.Equal((250, 130), (next.Root.GetChildRecursive(Float1)!.Box.Width, next.Root.GetChildRecursive(Float1)!.Box.Height));
        Assert.Equal((0, 560), Place(next, Toolbar));
        Assert.Equal([0x11UL, 0x22UL], restored);
        Assert.Equal(0, load.BadLines);
        Assert.DoesNotContain(log.Entries, static e => e.Severity >= LogSeverity.Warning);
    }

    [Fact]
    public void A_missing_file_keeps_the_defaults_and_is_not_a_warning()
    {
        var log = new RecordingLogSink();
        var scene = Scene();
        var masks = new ulong[] { 7, 8 };
        var load = new ScreenLayoutKeeper(new MemoryScreenLayoutStore(), log).Load("UI-w-c-800-600.txt", scene.Root, Masks(masks));

        Assert.False(load.Found);
        Assert.Equal((10, 400), Place(scene, Chat));
        Assert.Equal([7UL, 8UL], masks); // a mask nothing restored keeps retail's default
        Assert.DoesNotContain(log.Entries, static e => e.Severity >= LogSeverity.Warning);
    }

    [Fact]
    public void A_malformed_file_is_read_as_the_defaults_with_one_warning()
    {
        var store = new MemoryScreenLayoutStore();
        store.Write("bad.txt", "this is not a layout\n<CHAT> X:ten Y: 5 W: 1 H: 1\nLS-CHATMASK <CHAT> nonsense\n\u0001\u0002\n");
        var log = new RecordingLogSink();
        var scene = Scene();
        var masks = new ulong[] { 7, 8 };

        var load = new ScreenLayoutKeeper(store, log).Load("bad.txt", scene.Root, Masks(masks));

        Assert.True(load.Found);
        Assert.Equal((0, 0, 4), (load.WindowsApplied, load.MasksApplied, load.BadLines));
        Assert.Equal((10, 400), Place(scene, Chat));
        Assert.Equal([7UL, 8UL], masks);
        Assert.Single(log.Entries, static e => e.Severity == LogSeverity.Warning);
    }

    [Fact]
    public void A_good_line_in_a_file_with_a_bad_one_still_applies_and_the_warning_is_one()
    {
        var store = new MemoryScreenLayoutStore();
        store.Write("mixed.txt", "<TBAR> X:0 Y: 500 W: 800 H: 40\n???\n<NOPE> X:1 Y: 1 W: 1 H: 1\n");
        var log = new RecordingLogSink();
        var scene = Scene();

        var load = new ScreenLayoutKeeper(store, log).Load("mixed.txt", scene.Root, Masks([0, 0]));

        Assert.Equal((1, 1), (load.WindowsApplied, load.BadLines)); // an unknown tag is skipped, as retail skips it
        Assert.Equal(500, scene.Root.GetChildRecursive(Toolbar)!.Box.Y0);
        Assert.Single(log.Entries, static e => e.Severity == LogSeverity.Warning);
    }

    [Fact]
    public void A_window_saved_off_the_screen_is_moved_back_inside()
    {
        var store = new MemoryScreenLayoutStore();
        store.Write("off.txt", "<CHAT> X:5000 Y: -40 W: 300 H: 100\n");
        var scene = Scene();

        var load = new ScreenLayoutKeeper(store, new RecordingLogSink()).Load("off.txt", scene.Root, Masks([0, 0]));

        Assert.Equal(1, load.Clamped);
        Assert.Equal((500, 0), Place(scene, Chat)); // 800 - 300
    }

    [Fact]
    public void A_keeper_with_no_store_writes_nothing_and_a_failing_store_is_a_warning()
    {
        var scene = Scene();
        Assert.False(new ScreenLayoutKeeper(null, new RecordingLogSink()).Save("x.txt", scene.Root, Masks([0, 0])));
        var log = new RecordingLogSink();
        Assert.False(new ScreenLayoutKeeper(new ThrowingStore(), log).Save("x.txt", scene.Root, Masks([0, 0])));
        Assert.Single(log.Entries, static e => e.Severity == LogSeverity.Warning);
    }

    private sealed class ThrowingStore : IScreenLayoutStore
    {
        public string? Read(string name) => throw new InvalidOperationException("disk");

        public void Write(string name, string text) => throw new InvalidOperationException("disk");
    }
}
