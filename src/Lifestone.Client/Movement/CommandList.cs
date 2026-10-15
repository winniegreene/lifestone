namespace Lifestone.Client.Movement;

/// <summary>
/// Retail <c>CommandList</c> (11.4186 PORTAL/cmdinterp/CmdInterpDefs.c; names and addresses cited, nothing copied): the
/// movement commands of one kind (forward and backward, turns, or sidesteps) whose keys are down, newest first. The
/// newest is the one the player moves with; releasing it falls back to the one pressed before.
/// </summary>
/// <remarks>
/// <para>Keyboard commands only: retail also keeps one mouse command per list (<c>mouse_command</c>), which the M2 keymap
/// does not produce (<see cref="PlayerMovementController"/>, docs/physics.md §12).</para>
/// <para>Retail allocates a list element per key press; this list reuses its storage, so a press allocates nothing once
/// the list has grown.</para>
/// </remarks>
internal sealed class CommandList
{
    private readonly List<Entry> _entries = new(8); // oldest first; the head is the last entry

    /// <summary>One key down: the command, its speed and the run key it was pressed with.</summary>
    public readonly record struct Entry(uint Command, float Speed, bool HoldRun);

    /// <summary>True when a key of this list is down (retail <c>CommandList::GetHead</c>, 11.4186 0x006B4A40, 11.6096 0x006B5980, is non-null).</summary>
    public bool HasHead => _entries.Count != 0;

    /// <summary>The newest command (retail <c>GetHead</c>); only valid when <see cref="HasHead"/>.</summary>
    public Entry Head => _entries[^1];

    /// <summary>How many commands are held.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Retail <c>CommandList::AddCommand</c> (11.4186 0x006B4B80, 11.6096 0x006B5AC0) for a keyboard command: the command
    /// becomes the head. A key pressed twice is held twice, as in retail.
    /// </summary>
    public void Add(uint command, float speed, bool holdRun) => _entries.Add(new Entry(command, speed, holdRun));

    /// <summary>
    /// Retail <c>CommandList::RemoveCommand</c> (11.4186 0x006B4960, 11.6096 0x006B58A0) for a keyboard command: removes the
    /// newest entry of <paramref name="command"/>.
    /// </summary>
    /// <returns>True only when the removed entry was the head (so the player's motion must change).</returns>
    public bool Remove(uint command)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].Command == command)
            {
                var wasHead = i == _entries.Count - 1;
                _entries.RemoveAt(i);
                return wasHead;
            }
        }

        return false;
    }

    /// <summary>
    /// Retail <c>CommandList::ClearAllCommands</c> (11.4186 0x006B4A70, 11.6096 0x006B59B0) and, with no mouse commands,
    /// <c>ClearKeyboardCommands</c> (11.4186 0x006B4AD0, 11.6096 0x006B5A10).
    /// </summary>
    public void Clear() => _entries.Clear();
}
