using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Screens;
using Lifestone.Client.RetailUi.Text;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Motion;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The inventory page (panel 7, <c>gmInventoryUI</c> 0x10000023; M3 2.2 slice b1, docs/milestones/m3-plan.md section 4.6): the main pack's items in the pack list (<c>gm3DItemsUI</c> 0x10000021's list 0x100001C6), the side packs in the
/// backpack column (<c>gmBackpackUI</c> 0x10000022: the player's own cell 0x100001C9, the side-pack list 0x100001CA, the burden text 0x100001D8 and meter 0x100001D9), the title and the layout's close button. Read-only with one
/// action (D13): a double click on a pack item that has valid locations wields it through <see cref="IActionApi.Wield"/>. The paperdoll (0x100001CD) is slice b2's and shows the layout's art until then.
/// </summary>
/// <remarks>
/// <para>
/// <b>Retail.</b> <c>gmInventoryUI::PostInit</c> (11.6096 0x004A6940) finds the title text, the paperdoll, the backpack and the 3D-items panel; <c>RecvNotice_PlayerDescReceived</c> (0x004A6BC0) sets the title to "Inventory of
/// &lt;name&gt;" and links the backpack's lists to the pack list; <c>RecvNotice_NewParentContainer</c> (0x004A6C90) titles the pack list "Contents of Backpack" for the player's own pack and "Contents of &lt;container&gt;" for a side
/// pack; <c>gm3DItemsUI::PostInit</c> (0x004A7190) sets "Contents of Backpack"; <c>gmBackpackUI::SetLoadLevel</c> (0x004A7210) puts the load divided by three (0 to 1) on the meter (property 0x69) and "&lt;n&gt;%" with n the
/// level times 300 rounded down on the text, <c>RecvNotice_PlayerDescReceived</c> (0x004A7040) and <c>RecvNotice_LoadChanged</c> (0x004A7080) calling it. The load is burden over capacity
/// (<see cref="EncumbranceSystem.Load"/>; capacity <see cref="EncumbranceSystem.EncumbranceCapacity"/> from the strength with its enchantments and the carrying augmentations, as character information has it, 0x004CC00 family),
/// so 100 per cent is a full load and the meter is full at three times that.
/// </para>
/// <para>
/// <b>Lifestone.</b> The page reads <see cref="PlayerInventory"/> (the main pack in order, the side packs, <see cref="PlayerInventory.ItemsIn"/>) and each item's <see cref="WorldObject"/> for its icon fields; it never copies
/// them. Events mark it dirty (<see cref="InventoryChanged"/>, <see cref="ObjectCreated"/> of a held item, <see cref="QualitiesChanged"/> for the burden); the redraw is once when the page shows and, while shown, once on the
/// next frame after a change, rebuilding only the cells and texts that differ. A held item whose world object has not arrived (ACE sends the container event first) is left out until it does. Clicking a side pack shows its
/// contents in the pack list as the model holds them; the model has a side pack's contents from login (ACE sends every item's CreateObject, <c>Player_Networking.cs</c> 242-254; the reducer adopts them) and from item events, so an empty list is an empty pack (the old unopened-pack warning is gone, 4.2 x4). An item with no valid locations does nothing on a double click (retail uses it: <c>TODO(m3-use-from-pack)</c>); a ring or bracelet asks for its free side (<see cref="WieldSlotChoice"/>, retail's <c>CPlayerSystem::AutoWield</c> 0x005617D0); other masks go out whole.
/// </para>
/// </remarks>
public sealed class GameInventoryPage : IGamePanel, IDisposable
{
    /// <summary>The panel id of the page.</summary>
    public const uint Panel = 7;

    /// <summary>The page's title text (0x100001D3).</summary>
    public const uint TitleId = 0x100001D3;

    /// <summary>The player's own cell (the top of the backpack column).</summary>
    public const uint TopContainerId = 0x100001C9;

    /// <summary>The side-pack list.</summary>
    public const uint SidePackListId = 0x100001CA;

    /// <summary>The burden text.</summary>
    public const uint BurdenTextId = 0x100001D8;

    /// <summary>The burden meter.</summary>
    public const uint BurdenMeterId = 0x100001D9;

    /// <summary>The pack list's title (0x100001C5).</summary>
    public const uint ContentsTitleId = 0x100001C5;

    /// <summary>The pack list.</summary>
    public const uint PackListId = 0x100001C6;

    /// <summary>PropertyInt 0xE6, AugmentationIncreasedCarrying: part of the carrying capacity the burden meter is measured against.</summary>
    public const uint AugmentationCarryingProperty = 0xE6;

    private const uint StrengthAttributeId = 1;

    private readonly UiLoginContext _context;
    private readonly UiTextElement _title;
    private readonly UiTextElement _contentsTitle;
    private readonly UiTextElement _burdenText;
    private readonly UiMeter _burdenMeter;
    private readonly UiItemList _top;
    private readonly UiItemList _sidePacks;
    private readonly UiItemList _pack;
    private readonly IDisposable[] _subscriptions;
    private uint _openContainer;
    private bool _dirty = true;
    private string _titleText = string.Empty;
    private string _contentsTitleText = string.Empty;
    private int _burdenPercent = -1;
    private float _meterLevel = -1f;
    private string? _titleName; // the character name the title was built for (null: not built yet)
    private string? _contentsName; // the side pack name the contents title was built for (null: not built, or the backpack's)
    private InventoryEntry[] _ownerCell = [];
    private bool _usePackTodoLogged;

    private GameInventoryPage(UiLoginContext context, UiTextElement title, UiTextElement contentsTitle, UiTextElement burdenText, UiMeter burdenMeter, UiItemList top, UiItemList sidePacks, UiItemList pack)
    {
        _context = context;
        _title = title;
        _contentsTitle = contentsTitle;
        _burdenText = burdenText;
        _burdenMeter = burdenMeter;
        _top = top;
        _sidePacks = sidePacks;
        _pack = pack;
        _top.ItemSelected += OnContainerSelected;
        _sidePacks.ItemSelected += OnContainerSelected;
        _pack.ItemSelected += OnPackSelected;
        _pack.ItemActivated += OnPackActivated;
        var events = context.Session.Events;
        _subscriptions = [events.Subscribe<InventoryChanged>(OnInventoryChanged), events.Subscribe<ObjectCreated>(OnObjectCreated), events.Subscribe<QualitiesChanged>(OnQualitiesChanged)];
    }

    /// <summary>The panel id (7).</summary>
    public uint PanelId => Panel;

    /// <summary>How many times the page redrew (a test counts redraws).</summary>
    public int Refreshes { get; private set; }

    /// <summary>The pack list (the cells of the open container).</summary>
    public UiItemList PackList => _pack;

    /// <summary>The side-pack list.</summary>
    public UiItemList SidePackList => _sidePacks;

    /// <summary>The player's own cell.</summary>
    public UiItemList TopContainerList => _top;

    /// <summary>The container the pack list shows: the player (the main pack) or a side pack.</summary>
    public uint OpenContainer => _openContainer;

    /// <summary>The page's title text now.</summary>
    public string Title => _titleText;

    /// <summary>The pack list's title text now.</summary>
    public string ContentsTitle => _contentsTitleText;

    /// <summary>The burden text now ("&lt;n&gt;%"), or empty before the page drew.</summary>
    public string BurdenTextNow => _burdenPercent < 0 ? string.Empty : _burdenText.Text;

    /// <summary>The burden meter's level now (0 to 1).</summary>
    public float BurdenLevel => _meterLevel < 0f ? 0f : _meterLevel;

    /// <summary>
    /// The controller for the page, or null (logged) when the page or one of its elements is missing from the layout or its class is not registered.
    /// </summary>
    public static GameInventoryPage? Create(UiLoginContext context, GamePanelHost host)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(host);
        var page = host.ElementFor(Panel);
        if (page is null)
        {
            context.Manager.Services.Log.Warn("retailui", "the inventory page (panel 7) is not in the layout; no controller (TODO(m3-inventory-page))");
            return null;
        }

        if (page.GetChildRecursive(TitleId) is not UiTextElement title
            || page.GetChildRecursive(ContentsTitleId) is not UiTextElement contentsTitle
            || page.GetChildRecursive(BurdenTextId) is not UiTextElement burdenText
            || page.GetChildRecursive(BurdenMeterId) is not UiMeter meter
            || page.GetChildRecursive(TopContainerId) is not UiItemList top
            || page.GetChildRecursive(SidePackListId) is not UiItemList side
            || page.GetChildRecursive(PackListId) is not UiItemList pack)
        {
            context.Manager.Services.Log.Warn("retailui", "the inventory page lacks an element or the UIElement_ItemList class is not registered; no controller (TODO(m3-inventory-page))");
            return null;
        }

        return new GameInventoryPage(context, title, contentsTitle, burdenText, meter, top, side, pack);
    }

    /// <summary>The page just showed: the whole page is refreshed now.</summary>
    public void OnShown()
    {
        // A list is told when it, itself, is shown; its page showing tells it nothing (UiElement.SetVisible re-applies the children's flags without announcing them), so the page starts the cells.
        _top.BeginFill();
        _sidePacks.BeginFill();
        _pack.BeginFill();
        Refresh();
    }

    /// <summary>The page hid: it draws nothing until it shows again.</summary>
    public void OnHidden()
    {
    }

    /// <summary>Once per shell frame while shown: a change since the last draw is drawn once.</summary>
    public void Step(double now)
    {
        if (_dirty)
        {
            Refresh();
        }
    }

    /// <summary>Redraws the page from the session now (<c>RecvNotice_PlayerDescReceived</c> and the notices that follow it).</summary>
    public void Refresh()
    {
        _dirty = false;
        Refreshes++;
        var session = _context.Session;
        var self = session.State.Self;
        var inventory = self.Inventory;
        var objects = session.State.Objects;
        var owner = inventory.Owner != 0 ? inventory.Owner : self.Guid;
        if (_openContainer == 0 || (_openContainer != owner && !IsSidePack(inventory, _openContainer)))
        {
            _openContainer = owner;
        }

        var characterName = self.Name ?? string.Empty;
        if (_titleName != characterName)
        {
            _titleName = characterName; // the title text is built when the name changes, not on each refresh
            SetTitle(characterName.Length > 0 ? "Inventory of " + characterName : "Inventory");
        }

        // The backpack column: the player's own cell, then the side packs. The one-entry list is kept until the owner changes.
        if (owner == 0)
        {
            _ownerCell = [];
        }
        else if (_ownerCell.Length != 1 || _ownerCell[0].Guid != owner)
        {
            _ownerCell = [new InventoryEntry(owner, 1)];
        }

        Fill(_top, _ownerCell, objects, inventory, _openContainer);
        Fill(_sidePacks, inventory.SidePacks, objects, inventory, _openContainer);

        // The pack list: the open container's items.
        var contents = _openContainer == owner || _openContainer == 0 ? inventory.Pack : inventory.ItemsIn(_openContainer);
        Fill(_pack, contents, objects, inventory, 0);
        if (_openContainer == owner || _openContainer == 0)
        {
            _contentsName = null;
            SetContentsTitle("Contents of Backpack");
        }
        else
        {
            var packName = objects.TryGet(_openContainer, out var side) ? side.Name : string.Empty;
            if (_contentsName != packName)
            {
                _contentsName = packName;
                SetContentsTitle("Contents of " + packName);
            }
        }

        SetBurden(inventory, self.Qualities);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _top.ItemSelected -= OnContainerSelected;
        _sidePacks.ItemSelected -= OnContainerSelected;
        _pack.ItemSelected -= OnPackSelected;
        _pack.ItemActivated -= OnPackActivated;
    }

    /// <summary>How many items container <paramref name="guid"/> holds as the model has it: the player's pack counts the main pack and the side packs, a side pack its items (0 for an object that is no container).</summary>
    private static int ContainedIn(PlayerInventory inventory, uint guid) =>
        guid == inventory.Owner && guid != 0 ? inventory.Pack.Count + inventory.SidePacks.Count : inventory.ItemsIn(guid).Count;

    private static bool IsSidePack(PlayerInventory inventory, uint guid)
    {
        var packs = inventory.SidePacks;
        for (var i = 0; i < packs.Count; i++)
        {
            if (packs[i].Guid == guid)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Makes <paramref name="list"/> show <paramref name="entries"/> in order, changing only what differs (cells keep their picture when the item and its picture fields are the same).</summary>
    private void Fill(UiItemList list, IReadOnlyList<InventoryEntry> entries, WorldObjectTable objects, PlayerInventory inventory, uint openContainer)
    {
        list.EnsureCells(entries.Count);
        var cell = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            if (!objects.TryGet(entries[i].Guid, out var obj))
            {
                continue; // its CreateObject has not arrived (ObjectCreated marks the page dirty again)
            }

            if (list.CellAt(cell) is not { } target)
            {
                break; // a fixed list that is full
            }

            var icon = IconOf(obj, ContainedIn(inventory, obj.Guid));
            target.ShowItem(obj.Guid, icon); // the same item with the same fields changes nothing
            target.SetOpenContainer(openContainer != 0 && obj.Guid == openContainer);
            cell++;
        }

        for (; cell < list.CellCount; cell++)
        {
            if (list.CellAt(cell) is { IsEmpty: false } stale)
            {
                stale.Clear();
            }
        }

        list.SetOpenItem(openContainer);
        if (list.SelectedItemId != 0)
        {
            list.SelectItem(list.SelectedItemId);
        }
    }

    /// <summary>The cell fields of a world object: its icon fields (<see cref="WorldObjectIcon.ItemFields"/>), item type and names, and how many items a container holds.</summary>
    public static UiItemIcon IconOf(WorldObject obj, int contained = 0)
    {
        var f = obj.ItemFields();
        return new UiItemIcon(f.IconId, f.UnderlayId, f.OverlayId, obj.ItemType, f.UiEffects, f.StackSize, obj.Name, f.PluralName, f.ItemsCapacity, contained, f.Structure, f.MaxStructure);
    }

    private void SetTitle(string text)
    {
        if (text != _titleText)
        {
            _titleText = text;
            _title.SetText(text);
        }
    }

    private void SetContentsTitle(string text)
    {
        if (text != _contentsTitleText)
        {
            _contentsTitleText = text;
            _contentsTitle.SetText(text);
        }
    }

    /// <summary><c>gmBackpackUI::SetLoadLevel</c> (11.6096 0x004A7210).</summary>
    private void SetBurden(PlayerInventory inventory, PlayerQualities q)
    {
        var burden = inventory.Burden ?? 0;
        var raw = q.TryGetAttributeLevels(1, out _, out var strengthRaw) ? strengthRaw : 0u;
        var strength = EnchantmentRules.EnchantAttribute(q.Enchantments, 1, raw);
        q.TryGetInt(AugmentationCarryingProperty, out var augmentations); // AugmentationIncreasedCarrying
        var capacity = EncumbranceSystem.EncumbranceCapacity((int)strength, augmentations);
        var load = EncumbranceSystem.Load(capacity, burden);
        var level = Math.Clamp(load * 0.3333333333333333, 0.0, 1.0);
        var percent = (int)Math.Floor(level * 300.0);
        if ((float)level != _meterLevel)
        {
            _meterLevel = (float)level;
            _burdenMeter.SetFloatProperty(UiPropertyIds.MeterPosition, _meterLevel);
        }

        if (percent != _burdenPercent)
        {
            _burdenPercent = percent;
            _burdenText.SetText(percent.ToString(CultureInfo.InvariantCulture) + "%");
        }
    }

    private void MarkDirty() => _dirty = true;

    private void OnInventoryChanged(in InventoryChanged change) => MarkDirty();

    private void OnObjectCreated(in ObjectCreated created)
    {
        if (_context.Session.State.Self.Inventory.Contains(created.Guid))
        {
            MarkDirty();
        }
    }

    /// <summary>
    /// The page shows the character's name, the containers and the burden meter, which is measured against Strength (with the registry applied) and the carrying augmentations. A quality change redraws it only when it is the description, the
    /// registry, the burden (property 5), the augmentations (<see cref="AugmentationCarryingProperty"/>) or Strength: not a vital's current value or an option (as <see cref="Lifestone.Client.RetailUi.Indicators.GameIndicators"/> filters them),
    /// so a regeneration tick does not redraw it (M3 2.2 fix B, 2.2-R11).
    /// </summary>
    private void OnQualitiesChanged(in QualitiesChanged change)
    {
        if (change.Change is QualityChange.Description or QualityChange.Enchantments
            || (change.Change == QualityChange.Int && change.Key is PlayerInventory.EncumbranceProperty or AugmentationCarryingProperty)
            || (change.Change == QualityChange.Attribute && change.Key == StrengthAttributeId))
        {
            MarkDirty();
        }
    }

    /// <summary>A click on the player's cell or a side pack: the pack list shows its contents (<c>UIElement_ItemList::ListenToElementMessage</c>, container list, 11.6096 0x004E59E0; <c>gmInventoryUI::RecvNotice_NewParentContainer</c>).</summary>
    private void OnContainerSelected(UiItemList list, UiItem cell)
    {
        _pack.ClearSelection();
        if (!ReferenceEquals(list, _top))
        {
            _top.ClearSelection();
        }

        if (!ReferenceEquals(list, _sidePacks))
        {
            _sidePacks.ClearSelection();
        }

        if (cell.ItemId != 0 && cell.ItemId != _openContainer)
        {
            _openContainer = cell.ItemId;
            MarkDirty();
        }
    }

    private void OnPackSelected(UiItemList list, UiItem cell)
    {
        _top.ClearSelection();
        _sidePacks.ClearSelection();
    }

    /// <summary>The chat line of a refused wield (null: only logged); the shell routes it to the chat.</summary>
    public Action<string>? HoldNotice { get; set; }

    /// <summary>AutoWieldIsLegal's combat rule: <paramref name="valid"/> holds the Held slot (0x1000000), the mode is not peace and the item is not a caster (ItemType 0x8000).</summary>
    public static bool HoldRefused(int combatMode, uint valid, uint itemType) =>
        (valid & (uint)EquipMask.Held) != 0 && combatMode is not (0 or CombatModes.NonCombat) && (itemType & CombatModeRules.ItemTypeCaster) == 0;

    /// <summary>A double click on a pack item: wield it at its valid locations (retail's use of a wearable; the gear scenario passes the same mask).</summary>
    private void OnPackActivated(UiItemList list, UiItem cell)
    {
        var session = _context.Session;
        if (!session.State.Objects.TryGet(cell.ItemId, out var obj))
        {
            return;
        }

        var valid = obj.ItemFields().ValidLocations;
        if (valid == 0)
        {
            if (!_usePackTodoLogged)
            {
                _usePackTodoLogged = true;
                _context.Manager.Services.Log.Warn("retailui", "TODO(m3-use-from-pack): a double click uses the item in retail; in M3 only a wearable or wieldable item (valid locations) does anything, by wielding it");
            }

            return;
        }

        if (HoldRefused(session.State.Self.Qualities.CombatMode, valid, obj.ItemType))
        {
            // CPlayerSystem::AutoWieldIsLegal (11.6096 0x0055FAD0): an item that fits the held slot and is not a caster cannot be held outside peace.
            var line = "Cannot hold " + obj.Name + " while in combat";
            _context.Manager.Services.Log.Info("retailui", line);
            HoldNotice?.Invoke(line);
            _context.Manager.LogOnce("inventory:wield-held-rule-bit", LogSeverity.Warning, "TODO(m3-wield-held-rule-bit): AutoWieldIsLegal tests bit 0x8000 of the object's item-type word (read here as ItemType Caster, which agrees with GetDefaultCombatMode); not confirmed from the object's virtual getter");
            return;
        }

        // CPlayerSystem::AutoWield (11.6096 0x005617D0): the first free location, a ring or bracelet to the free side.
        valid = WieldSlotChoice.Choose(session.State.Self.Inventory, valid);
        session.Actions.Wield(obj.Guid, (EquipMask)valid, ActionSource.Ui);
    }
}
