using System.Globalization;
using System.Text;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Session.Tools;

namespace Lifestone.Client.Play;

/// <summary>
/// The engine-free half of the panel pages' review frames (M3 2.2 s0): which pages have a frame, what the files are called and where they go. The Godot half takes the pictures:
/// <c>--play-checks --play-frames</c> saves one frame per registered page under <c>&lt;dir&gt;/panels/</c> (the shell's ten frames stay ten, <see cref="ShellFramesPlan"/>), and
/// <c>--play-panel-shots=DIR</c> saves the same frames from a plain <c>--play</c> run (the lead's live frames of BSD Mage).
/// </summary>
public static class PanelFramesPlan
{
    /// <summary>The sub-folder of the shell frames' folder that holds the panel frames.</summary>
    public const string SubFolder = "panels";

    /// <summary>What the panel-shots run logs when no page has a controller yet.</summary>
    public const string NoPanelText = "no panel registered yet";

    /// <summary>The wording of the panel frames' log line, which the checks match: <c>panel frames done captured=N failures=F</c>.</summary>
    public const string VerdictText = "panel frames done";

    /// <summary>The folder of the panel frames of a frames run whose shell frames go to <paramref name="shellFolder"/> (as <see cref="ShellFramesPlan.FolderFor"/> gave it).</summary>
    public static string FolderFor(string shellFolder)
    {
        ArgumentException.ThrowIfNullOrEmpty(shellFolder);
        return shellFolder.TrimEnd('/', '\\') + "/" + SubFolder;
    }

    /// <summary>The tab buttons of the tabbed pages that have a frame per tab (M3 2.2 fix B, finding 2.2-R4: DoD 12 names the tabs as panels): page 11's attributes tab (gmAttributeUI 0x1000002A) and skills tab (gmSkillUI 0x1000002B), page 13's spellbook tab (gmSpellbookUI 0x1000002E) and components tab (gmSpellComponentUI 0x1000002F). Each is the tab button's element id (what <c>UIElement_Panel::OpenTab</c> takes), a letter for the file name and the tab's name.</summary>
    private static readonly Dictionary<uint, (uint TabButton, char Letter, string Name)[]> TabbedPages = new()
    {
        [11] = [(0x10000228, 'a', "attributes"), (0x10000229, 'b', "skills")],
        [13] = [(0x100002A9, 'a', "spellbook"), (0x100002AA, 'b', "components")],
    };

    /// <summary>
    /// The review frames of the floaty windows that are not host pages (M3 3.2 pa, M4 1.3 s1): the combat window and its power meter, each taken in melee mode, named <c>c1-combat</c> and <c>c1-powerbar</c> (the spellcasting
    /// window's <c>c2-...</c> frame is pb's, after these). <c>c1-combat</c> is taken through the UI's own mode notice (never a session action, so a live run's character is not touched). <c>c1-powerbar</c> is taken during a real attack
    /// (<see cref="WindowFrame.ByAttack"/>): a creature selected, the Medium button clicked through the UI's own element message, the shot taken while the power charges. Only the live hunt leg takes it (<c>HuntUiShotPlan</c>): the offline runs
    /// have no stand-in creature (M4 4.1 t1, D26) and skip it with one log line naming <c>m4-powerbar-frame-live</c>. The basic combat UI shows the charge in the combat
    /// window's own meter (power bar mode 1); the bottom power bar window (mode 2) is drawn only under the AdvancedCombatUI option, which Lifestone does not honour, so it is not in the frame.
    /// </summary>
    public static IReadOnlyList<WindowFrame> WindowFrames { get; } =
    [
        new("c1-combat", 0x1000005C, 2),
        new("c1-powerbar", 0x1000005C, 2, ByAttack: true),
    ];

    /// <summary>
    /// The review frame of the spellcasting window (M3 3.2 pb): <c>c2-spellcasting</c>, taken in Magic mode. It is not in <see cref="WindowFrames"/> because pa's play-check loop over that list drives the combat window and the
    /// power bar only; the window's own checks (<c>PlayChecks.PanelC2.cs</c>) and the panel shots (<see cref="AllWindowFrames"/>) take it after pa's rows.
    /// </summary>
    public static WindowFrame SpellcastingFrame { get; } = new("c2-spellcasting", 0x10000061, CombatModes.Magic, false);

    /// <summary>
    /// The review frame of the spellcasting window with a favourites bar that holds no spell (M4 3.2 lane e): <c>c2-spellcasting-empty</c>, taken in Magic mode on the first tab whose bar the character has no spell on
    /// (<see cref="OpenSpellcastingEmptyFrameTab"/>). It shows the bar's empty slot boxes. Like <see cref="SpellcastingFrame"/> it is the window's own checks' (<c>PlayChecks.PanelC2.cs</c>); it is not in <see cref="AllWindowFrames"/>.
    /// </summary>
    public static WindowFrame SpellcastingEmptyFrame { get; } = new("c2-spellcasting-empty", 0x10000061, CombatModes.Magic, false);

    /// <summary>Every floaty window's frame in the order the panel shots take them: pa's <see cref="WindowFrames"/>, then <see cref="SpellcastingFrame"/>.</summary>
    public static IReadOnlyList<WindowFrame> AllWindowFrames { get; } = [.. WindowFrames, SpellcastingFrame];

    /// <summary>
    /// The offline review frame of the inventory page with a side pack selected and its contents listed (M4 2.2 s2): <c>inventory-sidepack</c>, saved in the frames folder's <c>extra</c> sub-folder by the play-checks. <b>Placeholder
    /// content:</b> the stand-in announces a "Pack (stand-in placeholder)" holding one Pyreal stack (<c>TODO(m4-standin-side-pack-item)</c>), so the frame shows the page's drawing of a side pack's contents, not a real character's pack.
    /// </summary>
    public const string InventorySidePackFrame = "inventory-sidepack";

    /// <summary>
    /// The offline review frame of the container window over the world view (M4 2.2 s2): <c>container-window</c>, saved in the <c>extra</c> sub-folder by the play-checks' container rows, taken while the stand-in's chest
    /// is open, in peace (M4 4.1 t1: no creature, no corpse). <b>Placeholder content:</b> the chest holds the stand-in's Bowl and Smock (<c>TODO(m4-standin-corpse-contents)</c>) with the stand-in's placeholder icon. The live corpse frame of a real kill is 2.2 s4's.
    /// </summary>
    public const string ContainerWindowFrame = "container-window";

    /// <summary>
    /// The offline review frame of the container window over a stand-in CHEST (M4 3.2 lane e, D23: no creature corpse): <c>container-chest</c>, saved in the <c>extra</c> sub-folder by the play-checks' loot-rule row, taken while the chest
    /// (<c>TODO(m4-standin-chest)</c>: a labelled placeholder holding a Bowl, a Pyreal stack and an unvalued item) is open. It shows the item row's three entries and then its empty slot boxes (the empty-slot fill, <see cref="Lifestone.Client.RetailUi.Widgets.UiItemList.UpdateEmptySlots"/>).
    /// </summary>
    public const string ContainerChestFrame = "container-chest";

    /// <summary>
    /// The review frame of the hotbar (M4 3.2 h2): <c>hotbar</c>, the toolbar's lower-right shortcut bar with a Potion of Healing on slot 1 and a Handy Healing Kit on slot 2 and the nine slots' numbers. The play-checks' <c>hotbar</c> row saves it in the
    /// <c>extra</c> sub-folder (the stand-in's own items, dragged onto the bar through the real router); <c>--play-panel-shots</c> saves <see cref="HotbarShotFile"/> beside the pages' frames (the live character's own bar, whatever it holds).
    /// </summary>
    public const string HotbarFrame = "hotbar";

    /// <summary>The review frame of the hotbar while a use runs (M4 3.2 h2): <c>hotbar-cooldown</c>, taken by the play-checks' <c>hotbar</c> row a few frames after the key 1 drank the potion, so slot 1 shows the cooldown overlay (the dats' last step image).</summary>
    public const string HotbarCooldownFrame = "hotbar-cooldown";

    /// <summary>The panel shots' file for the hotbar (<c>hotbar.png</c>); no page, window or extra frame has the name.</summary>
    public const string HotbarShotFile = HotbarFrame + ".png";

    /// <summary>The offline frames of placeholder content that 2.2 s2 and s4 and 3.2 lane e add, in the order the play-checks take them (see <see cref="InventorySidePackFrame"/>, <see cref="ContainerWindowFrame"/> and <see cref="ContainerChestFrame"/>; the offline <c>corpse</c> frame went with the stand-in tusker, M4 4.1 t1: the hunt leg's own <c>corpse</c> frame is the live one).</summary>
    public static IReadOnlyList<string> OfflineContentFrames { get; } = [InventorySidePackFrame, ContainerWindowFrame, ContainerChestFrame];

    /// <summary>
    /// The offline review frame of the vendor window (M4 3.2 s4b): <c>vendor-buy-sell</c>, saved in the <c>extra</c> sub-folder by the play-checks' vendor rows, taken with the window open after a Handy Healing Kit was bought through its Buy
    /// button: Thelnoth's name, his shop's rows with the first selected and its predicted price, the purse after the buy. <b>Placeholder content:</b> the stand-in's Thelnoth and shop (<c>TODO(m4-standin-vendor-model)</c>, <c>TODO(m4-standin-vendor-stock)</c>).
    /// </summary>
    public const string VendorBuySellFrame = "vendor-buy-sell";

    /// <summary>The offline frames of the vendor window (M4 3.2 s4b), in the order the play-checks take them.</summary>
    public static IReadOnlyList<string> VendorContentFrames { get; } = [VendorBuySellFrame];

    /// <summary>How near, in metres, a container object must be for <c>--play-panel-shots</c> to open it for the <see cref="ContainerWindowFrame"/> (M4 2.2 s4).</summary>
    public const double ContainerFrameMetres = 10.0;

    /// <summary>What the panel shots log when no container object is within <see cref="ContainerFrameMetres"/> (not a failure: the frame is not taken).</summary>
    public const string NoContainerText = "container-window: n/a (no container within 10 m)";

    /// <summary>
    /// The frames <c>--play-panel-shots</c> takes after the pages and the floaty windows (M4 2.2 s4), in order: <see cref="ContainerWindowFrame"/>, the container window over a container object within <see cref="ContainerFrameMetres"/>
    /// (a "Chest", a corpse), opened through <c>IActionApi.OpenContainer</c>, saved once the window shows its entries, then closed. The inventory page (panel 7: the pack list, the backpack column with its side packs, the paperdoll) is a page
    /// frame like the others and keeps its own file; the shots select the character's first side pack on it when it has one (<see cref="FirstSidePack"/>).
    /// </summary>
    public static IReadOnlyList<string> PanelShotExtraFrames { get; } = [ContainerWindowFrame];

    // --- M4 3.2 s4c: the examine panel's frames ----------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The examine panel's frame in <c>--play-panel-shots</c> (M4 3.2 s4c; <c>examine-kit.png</c> beside <see cref="ContainerWindowFrame"/>'s file): a Handy Healing Kit in the pack examined through the same entry the item list's right click uses
    /// (<see cref="GameExaminePanel.Open"/>), saved once the appraisal's answer has filled the window, then closed. <see cref="NoKitText"/> when the pack holds no kit.
    /// </summary>
    public const string ExamineKitFrame = "examine-kit";

    /// <summary>The offline review frame of the examine panel on the stand-in (M4 3.2 s4c; <c>examine-kit-standin</c> in the <c>extra</c> sub-folder, taken by the play-checks' examine rows). <b>Placeholder content:</b> the stand-in answers the 00C9 with a labelled subset of ACE's tables (<c>TODO(m4-standin-appraise-tables)</c>).</summary>
    public const string ExamineStandInFrame = "examine-kit-standin";

    /// <summary>What the panel shots log when the pack holds no kit (not a failure: the frame is not taken).</summary>
    public const string NoKitText = "examine-kit: n/a (no healing kit in the pack)";

    /// <summary>The frames <c>--play-panel-shots</c> takes for the examine panel (after the container window's): <see cref="ExamineKitFrame"/>.</summary>
    public static IReadOnlyList<string> ExamineShotFrames { get; } = [ExamineKitFrame];

    /// <summary>The offline frames the play-checks' examine rows take: <see cref="ExamineStandInFrame"/>.</summary>
    public static IReadOnlyList<string> OfflineExamineFrames { get; } = [ExamineStandInFrame];

    /// <summary>The first object of the player's main pack whose name says it is a healing kit ("Handy Healing Kit", "Adept Healing Kit") or whose weenie class is one (628, 629), or 0 (M4 3.2 s4c).</summary>
    public static uint KitInPack(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (var entry in state.Self.Inventory.Pack)
        {
            if (state.Objects.TryGet(entry.Guid, out var item) && (item.Name.Contains("Healing Kit", StringComparison.OrdinalIgnoreCase) || item.Weenie.WeenieClassId is 628 or 629))
            {
                return entry.Guid;
            }
        }

        return 0;
    }

    /// <summary>
    /// The container object nearest the player that is within <paramref name="metres"/> metres, or 0 when there is none (M4 2.2 s4). A container object is one in the world (it has a position and no holder) whose weenie ItemType has the Container bit (0x200: a
    /// corpse, a chest, a pack on the ground), or that carries the corpse description bit, or that is openable and not a door (the stand-in's placeholder chest announces only the openable bit). The player's own object and other players are not.
    /// </summary>
    /// <param name="state">The session's read model.</param>
    /// <param name="metres">The greatest distance.</param>
    public static uint ContainerWithin(GameState state, double metres)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Self.Position is not { } mine)
        {
            return 0;
        }

        var best = 0u;
        var bestMetres = double.MaxValue;
        foreach (var o in state.Objects.All)
        {
            if (o.IsSelf || o.Position is not { } theirs || (o.ContainerId is { } holder && holder != 0) || (o.WielderId is { } wielder && wielder != 0) || (o.DescriptionFlags & PlayerBit) != 0)
            {
                continue;
            }

            var container = (o.ItemType & ContainerItemType) != 0 || (o.DescriptionFlags & WorldObjectTable.CorpseFlag) != 0 || ((o.DescriptionFlags & StandInChest.OpenableFlag) != 0 && (o.DescriptionFlags & DoorBit) == 0);
            if (!container)
            {
                continue;
            }

            var d = PositionAgreement.DistanceMetres(in mine, in theirs);
            if (d <= metres && d < bestMetres)
            {
                best = o.Guid;
                bestMetres = d;
            }
        }

        return best;
    }

    /// <summary>The weenie description's ItemType bit of a container (<c>ITEM_TYPE::TYPE_CONTAINER</c>, 0x200).</summary>
    private const uint ContainerItemType = 0x200;

    /// <summary>The object description bit of a player (<c>BF_PLAYER</c>, 0x8).</summary>
    private const uint PlayerBit = 0x8;

    /// <summary>The object description bit of a door (<c>BF_DOOR</c>, 0x1000).</summary>
    private const uint DoorBit = 0x1000;

    /// <summary>
    /// The side pack the inventory page's frame selects when the character has one (M4 2.2 s4): the first non-empty cell of the page's side-pack column, or 0 when the column is empty (the page then shows the backpack, as it opens).
    /// </summary>
    /// <param name="page">The inventory page's controller.</param>
    public static uint FirstSidePack(GameInventoryPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        foreach (var cell in page.SidePackList.Items)
        {
            if (cell is UiItem { IsEmpty: false } item)
            {
                return item.ItemId;
            }
        }

        return 0;
    }

    /// <summary>The ending of the main-pack shot's file name (M4 3.1 p, <c>m4-panelshot-mainpack</c>): the inventory page's frame is taken twice, with the first side pack selected (<c>7-inventory-backpack-and-paperdoll.png</c>, unchanged) and with the main pack selected (<c>7-inventory-backpack-and-paperdoll-mainpack.png</c>), so a hunter's looted items show in the pack list.</summary>
    public const string MainPackSuffix = "-mainpack";

    /// <summary>True when <paramref name="frame"/> has a main-pack shot beside its own: the inventory page's (panel 7).</summary>
    public static bool HasMainPackShot(PanelFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.Page.PanelId == GameInventoryPage.Panel;
    }

    /// <summary>The file name of the main-pack shot of <paramref name="frame"/>: its <see cref="FileName(PanelFrame)"/> with <see cref="MainPackSuffix"/> before the extension. Distinct from every page's frame (the pages' names end in their own title).</summary>
    public static string MainPackFileName(PanelFrame frame)
    {
        var name = FileName(frame);
        return name[..^".png".Length] + MainPackSuffix + ".png";
    }

    /// <summary>
    /// True when the inventory page lists the main pack as the shot needs it (M4 3.1 p): the page's open container is the player's own pack and its pack list holds one cell for each of the pack's items whose object has landed
    /// (an item whose F745 is not in yet is left out by the page, so it is not waited for). The shot is taken once this holds, or after a bounded wait said by name.
    /// </summary>
    /// <param name="page">The inventory page's controller.</param>
    /// <param name="state">The session's read model.</param>
    public static bool MainPackListed(GameInventoryPage page, GameState state)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(state);
        var inventory = state.Self.Inventory;
        var owner = inventory.Owner != 0 ? inventory.Owner : state.Self.Guid;
        return owner != 0 && page.OpenContainer == owner && page.PackList.OccupiedCount == inventory.Pack.Count(e => state.Objects.Contains(e.Guid));
    }

    /// <summary>The player's own pack id as the inventory page takes it (<see cref="GameInventoryPage.ShowContents"/>): the inventory's owner, or the player's guid before the owner is named.</summary>
    /// <param name="state">The session's read model.</param>
    public static uint MainPackOwner(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Self.Inventory.Owner != 0 ? state.Self.Inventory.Owner : state.Self.Guid;
    }

    /// <summary>The file name of <paramref name="frame"/>: <c>&lt;name&gt;.png</c>.</summary>
    public static string FileName(WindowFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.Name + ".png";
    }

    /// <summary>The file name of the frame of <paramref name="page"/> (the whole page, no tab): <c>&lt;panelId&gt;-&lt;name&gt;.png</c>, the name in lower case with each run of other characters as one hyphen ("7-inventory-backpack-and-paperdoll.png").</summary>
    public static string FileName(PanelPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return string.Create(CultureInfo.InvariantCulture, $"{page.PanelId}-{Slug(page.Name)}.png");
    }

    /// <summary>The file name of <paramref name="frame"/>: a tabbed page's frame says its tab ("11a-attributes.png", "11b-skills.png", "13a-spellbook.png", "13b-components.png"), any other page's is <see cref="FileName(PanelPage)"/>.</summary>
    public static string FileName(PanelFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.TabButton == 0
            ? FileName(frame.Page)
            : string.Create(CultureInfo.InvariantCulture, $"{frame.Page.PanelId}{frame.Letter}-{frame.TabName}.png");
    }

    /// <summary>The pages that have a frame, in the order of <see cref="GamePanelHost.Pages"/>: those with a controller among <paramref name="registered"/> (panel ids).</summary>
    public static IReadOnlyList<PanelPage> PagesWithFrames(IEnumerable<uint> registered)
    {
        ArgumentNullException.ThrowIfNull(registered);
        var ids = new HashSet<uint>(registered);
        var pages = new List<PanelPage>();
        foreach (var page in GamePanelHost.Pages)
        {
            if (ids.Contains(page.PanelId))
            {
                pages.Add(page);
            }
        }

        return pages;
    }

    /// <summary>
    /// The frames to take, in the order of <see cref="GamePanelHost.Pages"/>: one for each (page, tab) pair, that is one for each page with a controller among <paramref name="registered"/> (panel ids), and a tabbed page (11, 13) has one for each of its two tabs.
    /// The caller opens the tab (<see cref="PanelFrame.TabButton"/>) before the shot.
    /// </summary>
    public static IReadOnlyList<PanelFrame> FramesFor(IEnumerable<uint> registered)
    {
        var frames = new List<PanelFrame>();
        foreach (var page in PagesWithFrames(registered))
        {
            if (TabbedPages.TryGetValue(page.PanelId, out var tabs))
            {
                foreach (var (button, letter, name) in tabs)
                {
                    frames.Add(new PanelFrame(page, button, letter, name));
                }
            }
            else
            {
                frames.Add(new PanelFrame(page, 0, '\u0000', string.Empty));
            }
        }

        return frames;
    }

    /// <summary>
    /// Opens the tab of <paramref name="frame"/> on its page element as a click on the tab button does (<c>UIElement_Panel::OpenTab</c>). True when the frame has no tab, or the tab is the open one afterwards.
    /// </summary>
    public static bool OpenTab(PanelFrame frame, UiElement? pageElement)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.TabButton == 0)
        {
            return true;
        }

        return pageElement is UiPanel panel && (panel.OpenTabId == frame.TabButton || panel.OpenTab(frame.TabButton)) && panel.OpenTabId == frame.TabButton;
    }

    /// <summary>
    /// Opens, for the <c>c2-spellcasting</c> frame, the first tab of <paramref name="window"/> whose bar holds spells (a click on its tab button, as <see cref="OpenTab"/> does a page's tab): the live character's spells sit on bars 1 and 2, so
    /// retail's own opening tab (the first) would be an empty list in the shot (M3 3.2 fix B, 3.2-R7). When no bar holds spells the open tab stays. The caller logs <see cref="SpellcastingTabChoice"/> and puts the earlier tab back
    /// with <see cref="GameSpellcastingPanel.OpenTabByClick"/> after the shot.
    /// </summary>
    public static SpellcastingTabChoice OpenSpellcastingFrameTab(GameSpellcastingPanel window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var tab = window.FirstTabWithSpells();
        if (tab < 0)
        {
            return new SpellcastingTabChoice(window.OpenTab, false, true);
        }

        return new SpellcastingTabChoice(tab, true, window.OpenTabByClick(tab));
    }

    /// <summary>
    /// Opens, for the <c>c2-spellcasting-empty</c> frame, the first tab of <paramref name="window"/> whose bar holds no spell the character knows (a click on its tab button). When every bar holds spells the open tab stays
    /// (<see cref="SpellcastingTabChoice.HoldsSpells"/> is then true: the frame is not an empty bar and the caller says so).
    /// </summary>
    public static SpellcastingTabChoice OpenSpellcastingEmptyFrameTab(GameSpellcastingPanel window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var tab = window.FirstTabWithoutSpells();
        if (tab < 0)
        {
            return new SpellcastingTabChoice(window.OpenTab, true, true);
        }

        return new SpellcastingTabChoice(tab, false, window.OpenTabByClick(tab));
    }

    /// <summary>The verdict line of a panel frames run: <c>panel frames done captured=N failures=F</c>.</summary>
    public static string Verdict(int captured, int failures) =>
        string.Create(CultureInfo.InvariantCulture, $"{VerdictText} captured={captured} failures={failures}");

    private static string Slug(string name)
    {
        var slug = new StringBuilder(name.Length);
        var pendingHyphen = false;
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                if (pendingHyphen && slug.Length > 0)
                {
                    slug.Append('-');
                }

                pendingHyphen = false;
                slug.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingHyphen = true;
            }
        }

        return slug.ToString();
    }
}

/// <summary>One frame of the panel review frames: a page, and (for a tabbed page) the tab button to open first (<see cref="TabButton"/> 0: the page as it opens).</summary>
/// <param name="Page">The page.</param>
/// <param name="TabButton">The tab button's element id, or 0 for a page with no tab frames.</param>
/// <param name="Letter">The tab's letter in the file name ('a', 'b'), or '\u0000'.</param>
/// <param name="TabName">The tab's name in the file name, or empty.</param>
public sealed record PanelFrame(PanelPage Page, uint TabButton, char Letter, string TabName);

/// <summary>One review frame of a floaty window that is not a host page (<see cref="PanelFramesPlan.WindowFrames"/>).</summary>
/// <param name="Name">The file name without extension (<c>c1-combat</c>).</param>
/// <param name="WindowId">The window's element id in the gameplay layout; the frame is taken when it shows.</param>
/// <param name="CombatMode">The combat mode (<c>CombatModes</c>: 2 melee) the window is shown in.</param>
/// <param name="ByAttack">True when the frame is taken during a real attack on a creature (M4 1.3 s1; only the live hunt leg takes it since M4 4.1 t1, the offline runs skip it): the session is put in the mode, the creature selected and the Medium button clicked, and the shot is taken while the power charges. Nothing is begun by hand.</param>
public sealed record WindowFrame(string Name, uint WindowId, int CombatMode, bool ByAttack = false);

/// <summary>The tab the spellcasting frame shows (<see cref="PanelFramesPlan.OpenSpellcastingFrameTab"/>).</summary>
/// <param name="Tab">The tab (0 to 7) that is open for the shot.</param>
/// <param name="HoldsSpells">True when the tab was chosen because its bar holds spells; false when no bar does (the open tab stays).</param>
/// <param name="Opened">True when the tab is the open one.</param>
public readonly record struct SpellcastingTabChoice(int Tab, bool HoldsSpells, bool Opened);
