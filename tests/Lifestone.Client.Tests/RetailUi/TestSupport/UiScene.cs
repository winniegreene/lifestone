using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Rendering;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Data.Ui;

namespace Lifestone.Client.Tests.RetailUi.TestSupport;

/// <summary>
/// A manager over one synthetic layout (0x21000001) whose root (element 1, a Field filling 800×600) holds the given
/// children, with a fake font, the retail-shaped key bindings, a recording log and a tap on every element message.
/// </summary>
internal sealed class UiScene
{
    public const uint LayoutId = 0x21000001;

    public UiScene(IEnumerable<UiElementDesc> children, IEnumerable<UiLayout>? extraLayouts = null, IReadOnlyDictionary<uint, uint>? layoutEnums = null,
        IUiStringSource? strings = null, IUiViewportHost? viewportHost = null, IEnumerable<UiProperty>? rootProperties = null, bool createRoot = true)
    {
        Font = new FakeFont();
        var root = UiDescs.Element(1, UiClassIds.Field, 0, 0, 800, 600, properties: rootProperties, children: children);
        Manager = new UiElementManager(new UiServices
        {
            Descs = new FakeDescSource([UiDescs.Layout(LayoutId, 800, 600, root), .. extraLayouts ?? []], layoutEnums),
            Fonts = FakeFontSource.With(Font),
            Strings = strings ?? new FakeStringSource(),
            Clipboard = new FakeClipboard(),
            Log = Log,
            Images = new FakeImageSource(),
            ViewportHost = viewportHost,
        });
        Manager.Input.Bindings = TestKeyBindings.RetailShaped();
        Manager.Input.WorldPointer = World;
        Manager.ElementMessage += m => Messages.Add(m);
        if (createRoot)
        {
            Root = Manager.CreateRootElementByDataId(LayoutId, 1)!;
        }
    }

    public UiElementManager Manager { get; }

    public UiElement Root { get; } = null!;

    public FakeFont Font { get; }

    public RecordingLogSink Log { get; } = new();

    public RecordingWorldPointer World { get; } = new();

    public List<UiElementMessage> Messages { get; } = [];

    public double Now { get; private set; }

    public UiInputRouter Input => Manager.Input;

    public T Get<T>(uint id)
        where T : UiElement => Assert.IsAssignableFrom<T>(Root.GetChildRecursive(id));

    /// <summary>Advances the clock by <paramref name="seconds"/> and runs a frame.</summary>
    public void Frame(double seconds = 0.01)
    {
        Now += seconds;
        Manager.Update(Now);
    }

    /// <summary>The messages of <paramref name="messageId"/> sent about element <paramref name="elementId"/>.</summary>
    public int Count(uint elementId, uint messageId) => Messages.Count(m => m.ElementId == elementId && m.MessageId == messageId);

    /// <summary>Moves the mouse to (x, y) and clicks the left button there.</summary>
    public bool Click(int x, int y, UiMouseButton button = UiMouseButton.Left)
    {
        Input.MouseMove(x, y);
        var down = Input.MouseButton(button, down: true);
        Input.MouseButton(button, down: false);
        return down;
    }

    /// <summary>Presses and releases a key.</summary>
    public bool Tap(int dik)
    {
        var down = Input.KeyDown(dik);
        Input.KeyUp(dik);
        return down;
    }
}

/// <summary>Records what goes to the world.</summary>
internal sealed class RecordingWorldPointer : IWorldPointerSink
{
    public List<string> Events { get; } = [];

    public bool TextEntryActive { get; set; }

    public void OnWorldPointer(int x, int y, UiMouseButton button, bool down, bool doubleClick) =>
        Events.Add($"{button} {(down ? "down" : "up")}{(doubleClick ? " double" : string.Empty)} at {x},{y}");

    public void OnMouseLook(float dx, float dy) => Events.Add($"look {dx},{dy}");
}

/// <summary>Records the actions of the input maps it registers.</summary>
internal sealed class RecordingActionSink(bool takes = true) : IInputActionSink
{
    public List<UiInputAction> Actions { get; } = [];

    public IEnumerable<string> Lines => Actions.Select(a => $"{a.InputMapId:X}:{a.ActionId:X}:{(a.Activation.Start ? "start" : "end")}");

    public bool OnInputAction(uint inputMapId, uint actionId, RetailActivation activation)
    {
        Actions.Add(new UiInputAction(inputMapId, actionId, activation));
        return takes;
    }
}

/// <summary>
/// Key bindings shaped like the retail engine and game maps (the actions and activations of KEYMAP 0x14000002 and
/// 0x14000000 for the keys the tests use), so the router tests run without dats. RealKeyMapTests checks the same
/// bindings in the user's dats.
/// </summary>
internal static class TestKeyBindings
{
    public const uint ShiftMeta = 0x80000000;
    public const uint CtrlMeta = 0x40000000;

    private const uint DownUp = 3;
    private const uint MouseDoubleClick = 0x60;

    public static RetailKeyBindings RetailShaped()
    {
        var mouse = RetailKeyBindings.Mouse;
        var key = RetailKeyBindings.Keyboard;
        RetailBinding[] bindings =
        [
            new(3, 7, mouse(UiMouseButton.Left), 0, DownUp),
            new(3, 8, mouse(UiMouseButton.Right), 0, DownUp),
            new(3, 9, mouse(UiMouseButton.Middle), 0, DownUp),
            new(3, 0xA, mouse(UiMouseButton.Left), 0, MouseDoubleClick),
            new(3, 0xB, mouse(UiMouseButton.Right), 0, MouseDoubleClick),
            new(3, 0xC, mouse(UiMouseButton.Middle), 0, MouseDoubleClick),
            new(0xA, 5, RetailKeyBindings.Wheel(up: true), 0, DownUp),
            new(0xA, 6, RetailKeyBindings.Wheel(up: false), 0, DownUp),
            new(7, 0x16, key(DikScanCodes.Left), 0, DownUp),
            new(7, 0x25, key(DikScanCodes.Return), 0, DownUp),
            new(7, 0x27, key(DikScanCodes.Escape), 0, DownUp),
            new(7, 0x28, key(DikScanCodes.Back), 0, DownUp),
            new(8, 0x22, key(DikScanCodes.C), CtrlMeta, DownUp),
            new(9, 0x27, key(DikScanCodes.Escape), 0, DownUp),
            new(9, 0x25, key(DikScanCodes.Return), 0, DownUp),
            new(4, 0x29, key(DikScanCodes.W), 0, DownUp),
            new(4, 0x31, key(DikScanCodes.Space), 0, DownUp),
            new(4, 0x30, key(DikScanCodes.Q), 0, DownUp),
            new(4, 0x2B, key(DikScanCodes.S), 0, DownUp),
            new(0x1000000A, 0x10000023, key(DikScanCodes.Return), 0, DownUp),
            new(0x10000009, 0x27, key(DikScanCodes.Escape), 0, DownUp),
        ];
        RetailMetaKey[] metas =
        [
            new(key(DikScanCodes.LShift), ShiftMeta),
            new(key(DikScanCodes.LControl), CtrlMeta),
        ];
        return new RetailKeyBindings(bindings, metas, Toggle);
    }

    private static RetailToggleType Toggle(uint map, uint action) => (map, action) switch
    {
        (3, _) or (0xA, _) => RetailToggleType.Momentary,
        (4, 0x30) => RetailToggleType.Toggle,
        (4, 0x2B) => RetailToggleType.Impulse,
        (4, _) => RetailToggleType.Momentary,
        (7, 0x16) or (7, 0x28) => RetailToggleType.AutoRepeat,
        _ => RetailToggleType.Impulse,
    };
}
