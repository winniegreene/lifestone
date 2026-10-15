using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.RetailUi.Core;

/// <summary>
/// The retail UI's busy count (<c>ClientUISystem::IncrementBusyCount</c> 11.6096 0x00565610 and <c>DecrementBusyCount</c> 0x00565630, docs/retail-ui.md section 21.8): a plain counter <c>m_cBusy</c>;
/// when it goes from 0 to 1 or from 1 to 0 the system re-evaluates the pointer (<c>ClientUISystem::UpdateCursorState</c>, 0x005653D0). A non-zero count wins over every other pointer: that function tests
/// the count first, before the target mode and the combat mode, and picks the busy pointer (the cursor enum's 14, or 15 over an object in the world). Portal space raises the count for its whole
/// sequence (its busy state, retail's <c>teleportInProgress</c>).
/// </summary>
/// <remarks>
/// <para>The count is built and drives <see cref="CursorIndex"/>; the busy pointer's picture is not yet drawn: the host has no mapping from the cursor enum to the dat's pointer image
/// (<c>TODO(g6-busy-cursor-image)</c>, logged once when the count first goes up). Game thread only.</para>
/// </remarks>
public sealed class UiBusyCount(ILogSink log)
{
    /// <summary>The pointer enum retail picks while busy and over no object (<c>UpdateCursorState</c>: 14).</summary>
    public const int BusyCursor = 14;

    /// <summary>The pointer enum while busy and over an object in the world (15).</summary>
    public const int BusyCursorOverObject = 15;

    private readonly ILogSink _log = log ?? throw new ArgumentNullException(nameof(log));
    private bool _imageTodoLogged;

    /// <summary>The count (<c>m_cBusy</c>).</summary>
    public int Count { get; private set; }

    /// <summary>True while the count is not zero.</summary>
    public bool IsBusy => Count > 0;

    /// <summary>True while the pointer is over an object in the world (<c>SmartBox::get_found_object_id</c> is not 0); the host sets it from picking.</summary>
    public bool OverObject { get; set; }

    /// <summary>How often the count crossed between zero and one (each time retail calls <c>UpdateCursorState</c>).</summary>
    public int CursorUpdates { get; private set; }

    /// <summary>The pointer enum <c>UpdateCursorState</c> picks for the busy state: 14, or 15 over an object; 0 while not busy (the other pointers are the elements' own).</summary>
    public int CursorIndex => IsBusy ? (OverObject ? BusyCursorOverObject : BusyCursor) : 0;

    /// <summary><c>IncrementBusyCount</c>.</summary>
    public void Increment()
    {
        Count++;
        if (Count == 1)
        {
            UpdateCursorState();
        }
    }

    /// <summary><c>DecrementBusyCount</c>: retail's counter is unsigned; a decrement at zero is a bug in the caller and is reported, not applied.</summary>
    public void Decrement()
    {
        if (Count == 0)
        {
            _log.Error("retailui", "ClientUISystem::DecrementBusyCount: the busy count is already 0; ignored (a caller decremented without incrementing)");
            return;
        }

        Count--;
        if (Count == 0)
        {
            UpdateCursorState();
        }
    }

    private void UpdateCursorState()
    {
        CursorUpdates++;
        if (IsBusy && !_imageTodoLogged)
        {
            _imageTodoLogged = true;
            _log.Warn("retailui", "TODO(g6-busy-cursor-image): the busy count is up and ClientUISystem::UpdateCursorState picks the busy pointer (cursor enum 14), but the host cannot yet draw the dat's busy pointer image; the pointer stays as it is");
        }
    }
}
