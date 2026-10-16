using System.Globalization;
using System.Text;
using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.RetailUi.Panels;
using Lifestone.Client.RetailUi.Widgets;

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
