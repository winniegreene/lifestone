using System.Collections.Immutable;
using Lifestone.Client.RetailUi;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Flow;
using Lifestone.Client.RetailUi.Input;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Toolbar;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Tests.RetailUi.Screens;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Tests;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;

namespace Lifestone.Client.Tests.RetailUi.Panels;

/// <summary>
/// The gameplay shell over the real layout with a player who holds a Handy Healing Kit, a stack of five Potions of Healing, a sword, a gem and a heap of pyreals, and a description that lists a shortcut bar (M4 3.2 h2). The
/// hotbar tests drive it with mouse and key events as the Godot feed does. An object on the ground (<see cref="Ground"/>) and a shortcut row for an object nobody created (<see cref="Unknown"/>) are there for the missing cases.
/// </summary>
internal sealed class HotbarRig : IDisposable
{
    public const uint Player = 0x50000001;
    public const uint Kit = 0x80000B01;
    public const uint Potion = 0x80000B02;
    public const uint Sword = 0x80000B03;
    public const uint Gem = 0x80000B04;
    public const uint Pyreals = 0x80000B05;
    public const uint Ground = 0x80000B06;
    public const uint Unknown = 0x80000B07;

    /// <summary>The rig; <paramref name="bar"/> is the description's shortcut list (slot, object).</summary>
    public HotbarRig(DatSetFixture fixture, params (uint Slot, uint Object)[] bar)
    {
        IInboundMessageHandler? captured = null;
        Rig = new LoginRig(fixture, h =>
        {
            captured = h;
            StandInSessions.DataCheckThenCharacterSelect(h, TimeSpan.FromSeconds(2));
            StandInSessions.AnswerLocally(h, TimeSpan.FromSeconds(0.6));
        }, liveGame: true);
        Rig.Run(2.5);
        Rig.Flow.QueueUIMode(UiModes.GamePlay);
        Rig.Frame();
        Rig.Frame();
        Screen = Assert.IsType<GamePlayScreen>(Rig.Flow.CurrentScreen);
        Handler = captured!;
        Handler.OnPlayerCreate(Context, new PlayerCreateMsg(Player));
        Create(ReducerHarness.Create(Player, ReducerHarness.Stamps(), ReducerHarness.Position(0x860201AD), state: PhysicsState.Hidden | PhysicsState.Gravity, name: "Tester"));
        Create(Item(Kit, 628, "Handy Healing Kit", 0x060011D1, 0x80));
        Create(Item(Potion, 377, "Potion of Healing", 0x060011D2, 0x80, stack: 5));
        Create(Item(Sword, 5001, "Sword", 0x060011D3, 0x100000));
        Create(Item(Gem, 5003, "Gem", 0x060011D0, 0x8));
        Create(Item(Pyreals, 273, "Pyreal", 0x060011D4, 0x40, stack: 250));
        Create(Item(Ground, 5004, "Stone", 0x060011D0, 0x8, container: null));
        Handler.OnPlayerDescription(
            Context,
            new GameEventHeader(Player, 1, 0x0013),
            new PlayerDescriptionMsg
            {
                IntProperties = ImmutableDictionary<uint, int>.Empty.Add(PlayerInventory.EncumbranceProperty, 720),
                Attributes = [new AttributeDesc(1, 0, 100, 0)],
                Inventory = [new ContentDesc(Kit, 0), new ContentDesc(Potion, 0), new ContentDesc(Sword, 0), new ContentDesc(Gem, 0), new ContentDesc(Pyreals, 0)],
                Shortcuts = [.. bar.Select(b => new ShortcutDesc(b.Slot, b.Object, 0))],
            });
        Frames(3);
    }

    public LoginRig Rig { get; }

    public GamePlayScreen Screen { get; }

    public IInboundMessageHandler Handler { get; }

    public InboundContext Context => new(0, MessageQueue.SmartBox, 0);

    public GameToolbar Toolbar => Assert.IsType<GameToolbar>(Screen.ToolbarUi);

    public GameHotbar Hotbar => Assert.IsType<GameHotbar>(Toolbar.Hotbar);

    public UiInputRouter Input => Rig.Manager.Input;

    public UiDrag Drag => Input.Drag;

    public ActionApi Actions => Assert.IsType<ActionApi>(Rig.Session.Actions);

    public ShortcutBar Bar => Rig.Session.State.Self.Shortcuts;

    public static CreateObjectMsg Item(uint guid, uint wcid, string name, uint icon, uint itemType, uint? container = Player, ushort? stack = null)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name);
        return basis with { Weenie = basis.Weenie with { WeenieClassId = wcid, IconId = icon, ItemType = itemType, ContainerId = container, StackSize = stack } };
    }

    public void Create(CreateObjectMsg message) => Handler.OnCreateObject(Context, message, isUpdate: false);

    public void Frames(int n)
    {
        for (var i = 0; i < n; i++)
        {
            Rig.Frame();
        }
    }

    /// <summary>The cell of a hotbar slot.</summary>
    public UiItem Cell(int slot) => Hotbar.CellOf(slot) ?? throw new InvalidOperationException($"slot {slot} has no cell");

    public static (int X, int Y) Center(UiElement element)
    {
        var box = element.ScreenBox;
        return ((box.X0 + box.X1) / 2, (box.Y0 + box.Y1) / 2);
    }

    public void MoveTo(UiElement element) => MoveTo(Center(element));

    public void MoveTo((int X, int Y) point) => Input.MouseMove(point.X, point.Y);

    public void Down() => Input.MouseButton(UiMouseButton.Left, down: true);

    public void Up() => Input.MouseButton(UiMouseButton.Left, down: false);

    /// <summary>Presses on <paramref name="source"/>, walks the pointer to <paramref name="target"/> in steps and releases there.</summary>
    public void DragTo(UiElement source, (int X, int Y) target)
    {
        var from = Center(source);
        MoveTo(from);
        Down();
        MoveTo((from.X + 3, from.Y));
        MoveTo((from.X + 10, from.Y - 2));
        MoveTo(target);
        Up();
        Frames(1);
    }

    public void Click(UiElement element)
    {
        MoveTo(element);
        Down();
        Up();
        Frames(1);
    }

    /// <summary>Presses and releases a key.</summary>
    public bool Key(int dik)
    {
        var taken = Input.KeyDown(dik);
        Input.KeyUp(dik);
        Frames(1);
        return taken;
    }

    /// <summary>The model's state as a one-line string: the slots with an object, "slot:object".</summary>
    public string BarText() => string.Join(",", Enumerable.Range(0, ShortcutBar.SlotCount).Where(i => Bar[i].IsItem).Select(i => $"{i}:{Bar[i].ObjectId:X}"));

    public void Dispose() => Rig.Dispose();
}
