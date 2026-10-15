namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The account's characters (<see cref="GameState.Characters"/>), kept in the server's order because a delete names a
/// character by its index in that order. F658 replaces the list; an F643 <c>Ok</c> appends the new character locally
/// (ACE does not resend F658 after a create); a restore clears the pending deletion.
/// </summary>
public sealed class CharacterListState
{
    private readonly List<CharacterSlot> _entries = [];

    /// <summary>An empty list. Only the session creates one.</summary>
    internal CharacterListState()
    {
    }

    /// <summary>The characters, in server order.</summary>
    public IReadOnlyList<CharacterSlot> Entries => _entries;

    /// <summary>How many characters the account may have (F658's slot count).</summary>
    public uint SlotCount { get; internal set; }

    /// <summary>The selected character (<see cref="IActionApi.SelectCharacter"/>), or 0 for none.</summary>
    public uint SelectedGuid { get; internal set; }

    /// <summary>Rises by one with every change; 0 until the first F658 arrived.</summary>
    public int Version { get; internal set; }

    /// <summary>The reducer's list.</summary>
    internal List<CharacterSlot> Mutable => _entries;

    /// <summary>The index of <paramref name="guid"/> in <see cref="Entries"/>, or -1.</summary>
    /// <param name="guid">The character.</param>
    /// <returns>Its index in server order, or -1 when it is not in the list.</returns>
    public int IndexOf(uint guid)
    {
        for (var i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Guid == guid)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>One character of the list (F658's entry).</summary>
/// <param name="Guid">The character's object id.</param>
/// <param name="Name">The character's name.</param>
/// <param name="DeleteTimeout">The server's deletion field, raw: 0 for a normal character; anything else means pending
/// deletion (ACE always reports at least 1, so retail shows no countdown; owner decision D52).</param>
public readonly record struct CharacterSlot(uint Guid, string Name, uint DeleteTimeout)
{
    /// <summary>True when the character is pending deletion: it can be restored, not entered.</summary>
    public bool PendingDeletion => DeleteTimeout != 0;
}
