using System.Globalization;
using System.Text;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Widgets;
using Lifestone.Client.Session.Contracts;

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
    /// The review frames of the floaty windows that are not host pages (M3 3.2 pa): the combat window and the power bar, each taken in melee mode, named <c>c1-combat</c> and <c>c1-powerbar</c> (the spellcasting window's
    /// <c>c2-...</c> frame is pb's, after these). The caller changes the mode (the UI notice, not a session action, so a live run's character is not touched), drives the power bar (<see cref="WindowFrame.DrivePowerBar"/>: M3 has no attack to
    /// begin it, so the frame shows it begun by hand at <see cref="WindowFrame.PowerBarLevel"/>, a placeholder), takes the shot and puts the mode back.
    /// </summary>
    public static IReadOnlyList<WindowFrame> WindowFrames { get; } =
    [
        new("c1-combat", 0x1000005C, 2, false),
        new("c1-powerbar", 0x10000044, 2, true),
    ];

    /// <summary>
    /// The review frame of the spellcasting window (M3 3.2 pb): <c>c2-spellcasting</c>, taken in Magic mode. It is not in <see cref="WindowFrames"/> because pa's play-check loop over that list drives the combat window and the
    /// power bar only; the window's own checks (<c>PlayChecks.PanelC2.cs</c>) and the panel shots (<see cref="AllWindowFrames"/>) take it after pa's rows.
    /// </summary>
    public static WindowFrame SpellcastingFrame { get; } = new("c2-spellcasting", 0x10000061, CombatModes.Magic, false);

    /// <summary>Every floaty window's frame in the order the panel shots take them: pa's <see cref="WindowFrames"/>, then <see cref="SpellcastingFrame"/>.</summary>
    public static IReadOnlyList<WindowFrame> AllWindowFrames { get; } = [.. WindowFrames, SpellcastingFrame];

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
/// <param name="DrivePowerBar">True when the frame begins the power bar by hand first (an advanced-mode bar at <see cref="PowerBarLevel"/>) and finishes it after.</param>
public sealed record WindowFrame(string Name, uint WindowId, int CombatMode, bool DrivePowerBar)
{
    /// <summary>The level the power bar shows in its frame (a placeholder: nothing in M3 charges a bar).</summary>
    public const float PowerBarLevel = 0.6f;
}

/// <summary>The tab the spellcasting frame shows (<see cref="PanelFramesPlan.OpenSpellcastingFrameTab"/>).</summary>
/// <param name="Tab">The tab (0 to 7) that is open for the shot.</param>
/// <param name="HoldsSpells">True when the tab was chosen because its bar holds spells; false when no bar does (the open tab stays).</param>
/// <param name="Opened">True when the tab is the open one.</param>
public readonly record struct SpellcastingTabChoice(int Tab, bool HoldsSpells, bool Opened);
