using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The world with a player holding a Handy Healing Kit, a Potion of Healing stack, a dagger, a heap of pyreals and a bow (M4 3.2 h1), a description that lists <paramref name="bar"/> as the shortcut bar, and the server's side
/// scripted from ACE's own writers. The shortcut sends are the fake session's <see cref="FakeSendKind.AddShortcut"/> and <see cref="FakeSendKind.RemoveShortcut"/>.
/// </summary>
internal sealed class ShortcutRig
{
    public const uint Player = WorldActionRig.Player;
    public const uint Kit = 0x80000B01;
    public const uint Potion = 0x80000B02;
    public const uint Dagger = 0x80000B03;
    public const uint Pyreals = 0x80000B04;
    public const uint Bow = 0x80000B05;
    public const uint Scroll = 0x80000B06;
    public const uint Other = 0x80000B07;
    public const uint SidePack = 0x80000B20;
    public const uint InPack = 0x80000B21;

    private uint _event = 200;

    /// <summary>The rig. <paramref name="bar"/> is the description's shortcut list (slot, object).</summary>
    public ShortcutRig(params (uint Slot, uint Object)[] bar)
    {
        R = new WorldActionRig();
        R.H.R.Bus.Subscribe<ShortcutsChanged>((in ShortcutsChanged e) => Changes.Add(e));
        R.H.Describe(new PlayerDescriptionMsg
        {
            Inventory = [new ContentDesc(Kit, 0), new ContentDesc(Potion, 0), new ContentDesc(Dagger, 0), new ContentDesc(Pyreals, 0), new ContentDesc(Scroll, 0), new ContentDesc(Other, 0), new ContentDesc(SidePack, 1)],
            Equipped = [],
            Shortcuts = [.. bar.Select(b => new ShortcutDesc(b.Slot, b.Object, 0))],
        });
        Item(Kit, 628, "Handy Healing Kit", 0x80);
        Item(Potion, 377, "Potion of Healing", 0x80, 5);
        Item(Dagger, 5001, "Rusty Dagger", 0x1);
        Item(Pyreals, 273, "Pyreal", 0x40, 120);
        Item(Scroll, 5002, "Scroll of Strength Self", 0x2000);
        Item(Other, 5003, "Gem", 0x8);
        R.H.Tick();
    }

    public WorldActionRig R { get; }

    /// <summary>Every <see cref="ShortcutsChanged"/>, in order (the shared harness does not track this event: counting it in every description would change its lists).</summary>
    public List<ShortcutsChanged> Changes { get; } = [];

    public ActionApi Api => R.Api;

    public ShortcutBar Bar => R.H.State.Self.Shortcuts;

    public List<FakeSend> Adds => R.H.Net.Of(FakeSendKind.AddShortcut);

    public List<FakeSend> Removes => R.H.Net.Of(FakeSendKind.RemoveShortcut);

    /// <summary>The item's own CreateObject, held by the player (a stack when <paramref name="stack"/> is given).</summary>
    public void Item(uint guid, uint wcid, string name, uint itemType, ushort? stack = null)
    {
        var basis = ReducerHarness.Create(guid, ReducerHarness.Stamps(), null, name: name, itemType: itemType);
        var header = WeenieHeaderFlags.Container | (stack is null ? 0 : WeenieHeaderFlags.StackSize);
        R.H.R.Reducer.OnCreateObject(
            R.H.R.Context,
            basis with { Weenie = basis.Weenie with { Header = header, ContainerId = Player, WeenieClassId = wcid, StackSize = stack } },
            isUpdate: false);
    }

    /// <summary>Runs the update, then returns the handle's outcome.</summary>
    public ActionOutcome Done(ActionHandle handle)
    {
        R.Tick();
        return handle.Outcome ?? throw new InvalidOperationException("the action has not completed");
    }

    /// <summary>The slots a list of sends names, as "kind slot[:object]" in order, for one assertion over the whole wire sequence.</summary>
    public string Wire() => string.Join(
        ",",
        R.H.Net.Sends
            .Where(s => s.Kind is FakeSendKind.AddShortcut or FakeSendKind.RemoveShortcut)
            .Select(s => s.Kind == FakeSendKind.AddShortcut ? "A" + s.Index + ":" + (s.Guid == Kit ? "kit" : s.Guid == Potion ? "potion" : s.Guid == Dagger ? "dagger" : s.Guid == Scroll ? "scroll" : s.Guid == Other ? "gem" : s.Guid.ToString("X")) : "R" + s.Index));

    /// <summary>GameMessage 0x0024: the item is gone.</summary>
    public void Gone(uint item) => R.Raw(AceM3Writers.InventoryRemoveObject(item));

    /// <summary>GameEvent 0x019A: the item was dropped.</summary>
    public void Dropped(uint item) => R.Raw(AceM3Writers.PutObjectIn3D(Player, ++_event, item));

    /// <summary>GameEvent 0x0022: the item is in <paramref name="container"/>.</summary>
    public void Into(uint item, uint container, uint placement = 0) => R.Raw(AceM3Writers.PutObjInContainer(Player, ++_event, item, container, placement, 0));
}
