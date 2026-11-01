using Lifestone.Client.RetailUi.Core;

namespace Lifestone.Client.RetailUi.Panels;

/// <summary>
/// The examine window's floaty (<c>gmFloatyExaminationUI</c>, class 0x1000004C, window 0x100005F7 of the gameplay layout: 310 by 400 at 180,128; <c>gmFloatyExaminationUI::Register</c> 11.6096 0x004D4B30). Retail's class is
/// <c>gmExaminationUI</c> with a frame: <see cref="GameExaminePanel"/> is the examination half (it finds this window by id and fills it), this element is the frame's half (M5 7 S4, D122).
/// </summary>
/// <remarks>
/// <para>
/// <b>Built.</b> <c>PostInit</c> (11.6096 0x004D4B50) reads which window it is (master property 0x1000007E, 7 for the examine window) and takes its two sets of borders, the eight resize and drag bars and the eight plain "locked" fields;
/// <c>UpdateLockedStatus</c> (0x004D4930) shows the bars and hides the locked fields while the UI is not locked, and the other way round. The close button 0x100005F3 hides the window (<c>ListenToElementMessage</c> 0x004D48F0), and the
/// message then goes on to the examination half, which stops listening (<see cref="GameExaminePanel.Close"/>). <c>MoveTo</c> (0x004D5210) keeps the window inside its parent, whoever moves it (a drag, the layout file).
/// </para>
/// <para>
/// <b>Not built, named.</b> Lifestone has no UI lock (<c>PlayerModule::LockUI</c>; the shell's <c>TODO(g6-window-locks)</c>, logged once at the world entry), so the window is always unlocked: the bars show and the locked fields stay
/// hidden. Retail also writes the window's place and size to the player module's per-window options (0x10000086 to 0x10000089) on each move and resize and reads them back in <c>UpdateFromPlayerModule</c> (0x004D4E80) when no layout
/// file was loaded; Lifestone keeps the place in the screen layout file instead (<see cref="ScreenLayoutKeeper"/>, tag <c>&lt;EXAM&gt;</c>, M5 6 D33), so those options are not written.
/// </para>
/// </remarks>
public sealed class UiFloatyExamination : UiElement
{
    /// <summary>The class id (<c>gmFloatyExaminationUI::GetUIElementType</c>).</summary>
    public const uint RetailClassId = 0x1000004C;

    /// <summary>The master property that says which floaty window this is (<c>m_eWindowID</c>, read in <c>PostInit</c>).</summary>
    public const uint WindowIdProperty = 0x1000007E;

    /// <summary>The close button (<c>ListenToElementMessage</c>'s element 0x100005F3).</summary>
    public const uint CloseButtonId = 0x100005F3;

    /// <summary>The borders shown while the UI is not locked, in <c>PostInit</c>'s order: the top drag bar, the left, bottom and right resize bars, then the four corners (top left, top right, bottom left, bottom right).</summary>
    public static readonly uint[] BorderIds = [0x1000067C, 0x1000067E, 0x10000680, 0x10000682, 0x1000067B, 0x1000067D, 0x1000067F, 0x10000681];

    /// <summary>The plain borders shown while the UI is locked, in the same order (<c>m_pTopBorder_Locked</c> ... <c>m_pBottomRightCorner_Locked</c>).</summary>
    public static readonly uint[] LockedBorderIds = [0x10000674, 0x10000676, 0x10000678, 0x1000067A, 0x10000673, 0x10000675, 0x10000677, 0x10000679];

    private readonly UiElement?[] _borders = new UiElement?[8];
    private readonly UiElement?[] _lockedBorders = new UiElement?[8];

    /// <summary>A floaty of <paramref name="init"/>.</summary>
    public UiFloatyExamination(in UiElementInit init)
        : base(init)
    {
    }

    /// <summary>Which floaty window this is (master property 0x1000007E; 0 when the layout gives none).</summary>
    public uint WindowKind { get; private set; }

    /// <summary>True when the locked borders show (never, while Lifestone has no UI lock).</summary>
    public bool Locked { get; private set; }

    /// <summary><c>gmFloatyExaminationUI::PostInit</c> (11.6096 0x004D4B50): the window's id and its two sets of borders, then the lock's look.</summary>
    protected internal override void PostInit()
    {
        base.PostInit();
        WindowKind = TryGetEnumProperty(WindowIdProperty, out var kind) ? kind : 0;
        for (var i = 0; i < BorderIds.Length; i++)
        {
            _borders[i] = GetChildRecursive(BorderIds[i]);
            _lockedBorders[i] = GetChildRecursive(LockedBorderIds[i]);
        }

        UpdateLockedStatus(locked: false); // no UI lock in Lifestone (TODO(g6-window-locks))
    }

    /// <summary><c>gmFloatyExaminationUI::UpdateLockedStatus</c> (11.6096 0x004D4930): the bars show while unlocked, the plain fields while locked.</summary>
    public void UpdateLockedStatus(bool locked)
    {
        Locked = locked;
        for (var i = 0; i < _borders.Length; i++)
        {
            _borders[i]?.SetVisible(!locked);
            _lockedBorders[i]?.SetVisible(locked);
        }
    }

    /// <summary><c>gmFloatyExaminationUI::MoveTo</c> (11.6096 0x004D5210): the place is kept inside the parent (the right and bottom edges first, then not left of or above it).</summary>
    public override void MoveTo(int x, int y)
    {
        if (Parent is { } parent)
        {
            if (x > parent.Box.Width - Box.Width)
            {
                x = parent.Box.Width - Box.Width;
            }

            if (y > parent.Box.Height - Box.Height)
            {
                y = parent.Box.Height - Box.Height;
            }

            x = Math.Max(x, 0);
            y = Math.Max(y, 0);
        }

        base.MoveTo(x, y);
    }

    /// <summary><c>gmFloatyExaminationUI::ListenToElementMessage</c> (11.6096 0x004D48F0): the close button hides the window; every message then goes on (to the examination half).</summary>
    protected internal override UiListenResult ListenToElementMessage(in UiElementMessage message)
    {
        if (message.MessageId == UiMessageIds.ButtonClick && message.ElementId == CloseButtonId)
        {
            SetVisible(false);
        }

        return base.ListenToElementMessage(message);
    }
}
