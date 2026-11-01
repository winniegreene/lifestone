using Lifestone.Client.Diagnostics;
using Lifestone.Data.Ui;

namespace Lifestone.Client.RetailUi.Core;

/// <summary>Retail's <c>Target_Mode</c>: what the next click on an object does while the pointer asks for a target.</summary>
public enum UiTargetModeKind
{
    /// <summary>No target mode: a click selects.</summary>
    None = 0,

    /// <summary><c>TARGET_MODE_USE</c>: the next object clicked is used (the toolbar's Use with nothing selected).</summary>
    Use = 1,

    /// <summary><c>TARGET_MODE_EXAMINE</c>: the next object clicked is examined (the toolbar's Examine with nothing selected).</summary>
    Examine = 2,

    /// <summary><c>TARGET_MODE_USE_TARGET</c>: the next object clicked is the target of a tool being used (<c>ItemHolder::TargetAcquired</c>).</summary>
    UseTarget = 3,
}

/// <summary>
/// The UI's target mode (M5 item 7 S6, D136), as <c>ClientUISystem</c> keeps it in <c>targetMode</c>: <c>ClientUISystem::ExamineObject</c> (11.6096 0x005657B0) examines the object it is given, or with none (nothing selected)
/// enters the examine mode; <c>SetTargetMode</c> (0x005656E0) changes the mode and the pointer (<c>UpdateCursorState</c> 0x005653D0: the examine pointer is UICURSOR 10, retail's 11 over an object, at hot spot (0, 0); the use
/// pointer 12 or 13 at (14, 14)). While a mode is on, a left click on an item in a list (<c>UIElement_ItemList::HandleTargetedUseLeftClick</c> 0x004E3160) or on an object in the world
/// (<c>UIElement_SmartBoxWrapper::HandleTargetedUseLeftClick</c> 0x004E6010, from <c>MouseDown</c> 0x004E6390) runs it on that object instead of selecting it (<c>ExecuteTargetModeForItem</c> 0x005658D0). Any mouse press while a
/// mode is on marks it to end (<c>ClientUISystem::OnAction</c> 0x00565930, the left and right buttons of the target input map), after the press has been handled; the next frame ends it (<c>ClientUISystem::UseTime</c> 0x00565F90).
/// Escape ends it at once (<c>OnAction</c>'s escape action, after a focused element's own escape).
/// </summary>
/// <remarks>
/// <para><b>Not built (named).</b> The use-target mode (3) has no tool flow in Lifestone (<c>TODO(m5-target-mode-use-target)</c>: <see cref="Execute"/> logs it and does nothing); the pointer over an object (retail's 11 and 13,
/// <c>SmartBox::get_found_object_id</c>) needs the world's hover pick, which the host does not report (<c>TODO(m5-target-cursor-over-object)</c>: the pointer is the not-over one); a busy count wins over the target pointer in retail
/// (<see cref="UiBusyCount"/>), and its picture is not drawn either (<c>TODO(g6-busy-cursor-image)</c>). Game thread only.</para>
/// </remarks>
public sealed class UiTargetMode
{
    /// <summary>The examine pointer, over no object (UICURSOR enum 10).</summary>
    public const uint ExamineCursor = 10;

    /// <summary>The use pointer, over no object (UICURSOR enum 12).</summary>
    public const uint UseCursor = 12;

    private readonly UiElementManager _manager;
    private bool _leave;

    internal UiTargetMode(UiElementManager manager) => _manager = manager;

    /// <summary>The mode now (<c>targetMode</c>).</summary>
    public UiTargetModeKind Mode { get; private set; }

    /// <summary>True while a mode is on.</summary>
    public bool Active => Mode != UiTargetModeKind.None;

    /// <summary>Opens the examination of an object (the shell sets it to the examine panel's <c>Open</c>; <c>CM_Examine::SendNotice_ExamineObject</c>). Null: an examine is logged and nothing opens.</summary>
    public Action<uint>? Examiner { get; set; }

    /// <summary>Uses an object (the shell sets it to the Action API's Use; <c>ItemHolder::UseObject</c>). Null: a use is logged and nothing is sent.</summary>
    public Action<uint>? User { get; set; }

    /// <summary>The UICURSOR table the pointer comes from (the host sets it); null: the pointer does not change (logged once).</summary>
    public Func<uint, UiCursor?>? Cursors { get; set; }

    /// <summary>How many times a mode ran on an object (<see cref="Execute"/>).</summary>
    public int Executions { get; private set; }

    /// <summary>The object the last <see cref="Execute"/> ran on, or 0.</summary>
    public uint LastTarget { get; private set; }

    /// <summary>
    /// <c>ClientUISystem::ExamineObject</c>: examines <paramref name="guid"/> through <see cref="Examiner"/>; with 0 (nothing selected) it enters the examine mode instead. True when something happened (an examine or the mode).
    /// </summary>
    public bool ExamineObject(uint guid)
    {
        if (guid == 0)
        {
            Set(UiTargetModeKind.Examine);
            return true;
        }

        if (Examiner is not { } examine)
        {
            _manager.LogOnce("target:examiner", LogSeverity.Warning, "ClientUISystem::ExamineObject: no examine panel is attached to the UI (the shell sets it); the object is not examined");
            return false;
        }

        examine(guid);
        return true;
    }

    /// <summary><c>ClientUISystem::SetTargetMode</c>: the mode changes and the pointer follows; leaving a mode clears a pending end.</summary>
    public void Set(UiTargetModeKind mode)
    {
        if (mode == Mode)
        {
            return;
        }

        Mode = mode;
        _leave = false;
        _manager.Services.Log.Info("retailui", $"target mode mode={mode}");
        UpdateCursor();
    }

    /// <summary>
    /// <c>ClientUISystem::ExecuteTargetModeForItem</c>: runs the mode on <paramref name="guid"/> (examine or use). False with no mode or no object, so the click does what it does without one. The mode stays on until the press
    /// that made the click ends it (<see cref="MousePressed"/>).
    /// </summary>
    public bool Execute(uint guid)
    {
        if (Mode == UiTargetModeKind.None || guid == 0)
        {
            return false;
        }

        Executions++;
        LastTarget = guid;
        _manager.Services.Log.Info("retailui", $"target mode click mode={Mode} guid=0x{guid:X8}");
        switch (Mode)
        {
            case UiTargetModeKind.Examine:
                ExamineObject(guid);
                break;
            case UiTargetModeKind.Use:
                if (User is { } use)
                {
                    use(guid);
                }
                else
                {
                    _manager.LogOnce("target:user", LogSeverity.Warning, "ClientUISystem::ExecuteTargetModeForItem: no Use is attached to the UI (the shell sets it); the object is not used");
                }

                break;
            default:
                _manager.LogOnce("target:use-target", LogSeverity.Warning, "TODO(m5-target-mode-use-target): the use-target mode runs ItemHolder::TargetAcquired in retail (a tool used on the clicked object); Lifestone has no tool flow, so nothing happens");
                break;
        }

        return true;
    }

    /// <summary>A mouse button went down (<c>ClientUISystem::OnAction</c>'s left and right buttons while a mode is on): the mode ends on the next frame, after the press was handled.</summary>
    public void MousePressed()
    {
        if (Active)
        {
            _leave = true;
        }
    }

    /// <summary>Escape (<c>OnAction</c>'s escape action): ends the mode at once. True when a mode was on (the key is taken).</summary>
    public bool Escape()
    {
        if (!Active)
        {
            return false;
        }

        Set(UiTargetModeKind.None);
        return true;
    }

    /// <summary><c>ClientUISystem::UseTime</c>: a mode marked to end by a press ends now.</summary>
    public void UseTime()
    {
        if (_leave)
        {
            _leave = false;
            Set(UiTargetModeKind.None);
        }
    }

    private void UpdateCursor()
    {
        var cursorEnum = Mode switch
        {
            UiTargetModeKind.Examine => ExamineCursor,
            UiTargetModeKind.Use => UseCursor,
            UiTargetModeKind.UseTarget => 39u, // UpdateCursorState: 39 over no object
            _ => UiCursors.DefaultEnum,
        };
        var hot = Mode is UiTargetModeKind.Use or UiTargetModeKind.UseTarget ? 14 : 0;
        if (Cursors?.Invoke(cursorEnum) is not { } cursor)
        {
            if (Mode != UiTargetModeKind.None)
            {
                _manager.LogOnce("target:cursor", LogSeverity.Warning, $"ClientUISystem::UpdateCursorState: UICURSOR {cursorEnum} has no image here (no cursor table attached, or the map lacks it); the pointer does not change for the target mode");
            }

            return;
        }

        if (Mode != UiTargetModeKind.None)
        {
            _manager.LogOnce("target:cursor-over", LogSeverity.Warning, "TODO(m5-target-cursor-over-object): retail's target pointer changes over an object in the world (UICURSOR 11 and 13); the host does not report what the pointer is over, so the pointer is the not-over one");
        }

        _manager.Cursor.SetDefaultCursor(cursor.ImageId, hot, hot); // UIElementManager::SetCursor with the default flag, as UpdateCursorState passes it
    }
}
