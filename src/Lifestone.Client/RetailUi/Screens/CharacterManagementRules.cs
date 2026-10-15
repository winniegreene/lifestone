using Lifestone.Client.RetailUi.Core;
using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.RetailUi.Screens;

/// <summary>What <c>gmCharacterManagementUI::UpdateButtons</c> sets: the Enter and Create buttons' states, and which of Delete and Restore shows.</summary>
/// <param name="Enter">The Enter button's state: <see cref="UiStateIds.Normal"/> or <see cref="UiStateIds.Ghosted"/>.</param>
/// <param name="Create">The Create button's state: <see cref="UiStateIds.Normal"/>, or <see cref="UiStateIds.Ghosted"/> when every slot is taken.</param>
/// <param name="DeleteVisible">True when the Delete button shows (it shares its place with Restore).</param>
/// <param name="RestoreVisible">True when the Restore button shows: a character pending deletion is selected.</param>
public readonly record struct CharacterButtonStates(uint Enter, uint Create, bool DeleteVisible, bool RestoreVisible);

/// <summary>
/// The decisions of the character management screen that do not touch an element, kept apart so they can be tested one by one: the button
/// matrix, the list's order, its default selection and row height, the typed delete confirmation and the restore errors.
/// </summary>
/// <remarks>
/// Re-expressed from <c>gmCharacterManagementUI::UpdateButtons</c> (11.4186 0x004EC240, 11.6096 0x004ECED0),
/// <c>RebuildCharacterList</c> (0x004EC3A0, 0x004ED030), <c>CloseDeleteCharacterDialog</c> (0x004ED4A0, 0x004EE130) and
/// <c>RecvNotice_CharGenVerificationResponse</c> (0x004EBF60, 0x004ECBF0).
/// </remarks>
public static class CharacterManagementRules
{
    /// <summary>The share of the list's height a row takes at most: <c>height / 20</c> (a 16-pixel row in the retail list).</summary>
    public const int MaximumRowsPerHeight = 20;

    /// <summary>
    /// <c>UpdateButtons</c>. Enter is ghosted when nothing is selected or the selected character is pending deletion; Restore replaces
    /// Delete when a character pending deletion is selected; Create is ghosted unless a slot is free (fewer characters than the account's
    /// slot count; a slot count of 0, which is unknown, ghosts it). Delete itself is always normal (see the remarks of
    /// <see cref="CharacterManagementScreen"/>).
    /// </summary>
    /// <param name="selectedIndex">The selected row, or -1 for none.</param>
    /// <param name="selectedPendingDeletion">True when the selected character is pending deletion (its slot is greyed out).</param>
    /// <param name="characterCount">The characters listed.</param>
    /// <param name="slotCount">The account's slots.</param>
    public static CharacterButtonStates Buttons(int selectedIndex, bool selectedPendingDeletion, int characterCount, uint slotCount) => new(
        Enter: selectedIndex == -1 || selectedPendingDeletion ? UiStateIds.Ghosted : UiStateIds.Normal,
        Create: characterCount < slotCount ? UiStateIds.Normal : UiStateIds.Ghosted,
        DeleteVisible: !(selectedPendingDeletion && selectedIndex != -1),
        RestoreVisible: selectedPendingDeletion && selectedIndex != -1);

    /// <summary>
    /// The height of a row (<c>RebuildCharacterList</c>): the list's height shared by the larger of the characters listed and the account's
    /// slots, but no more than the height divided by <see cref="MaximumRowsPerHeight"/>.
    /// </summary>
    public static int RowHeight(int listHeight, int characterCount, uint slotCount)
    {
        var shared = (long)Math.Max(characterCount, slotCount);
        var perSlot = shared <= 0 ? listHeight : (int)(listHeight / shared);
        return Math.Min(perSlot, listHeight / MaximumRowsPerHeight);
    }

    /// <summary>
    /// The list's order, as indices into <paramref name="entries"/> (server order): characters sorted by name (a wide-character comparison,
    /// <c>wcscmp</c>, so ordinal), then the ones pending deletion at the end in the same order.
    /// </summary>
    public static IReadOnlyList<int> DisplayOrder(IReadOnlyList<CharacterSlot> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return [.. Enumerable.Range(0, entries.Count)
            .OrderBy(i => entries[i].PendingDeletion ? 1 : 0)
            .ThenBy(i => entries[i].Name, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The character selected when nothing else is asked for: the first one in server order that is not pending deletion, else the first
    /// (chosen before the list is sorted, as <c>RebuildCharacterList</c> does); -1 for an empty list.
    /// </summary>
    public static int DefaultSelection(IReadOnlyList<CharacterSlot> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        for (var i = 0; i < entries.Count; i++)
        {
            if (!entries[i].PendingDeletion)
            {
                return i;
            }
        }

        return entries.Count == 0 ? -1 : 0;
    }

    /// <summary>The delete dialog's check (<c>CloseDeleteCharacterDialog</c>): what was typed equals the confirmation word, ignoring case.</summary>
    public static bool IsDeleteConfirmed(string? typed, string expected) => typed is not null && string.Equals(typed, expected, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The string token of the message a refused create or restore shows (<c>RecvNotice_CharGenVerificationResponse</c>), by the server's
    /// result code: 3 the name is in use, 5 the request was corrupt, 6 the database is down; null for any other result (the wait dialog
    /// closes and nothing is said).
    /// </summary>
    public static string? VerificationToken(uint resultCode) => resultCode switch
    {
        3 => "ID_CharacterManagement_CG_VERIFICATION_RESPONSE_NAME_IN_USE",
        5 => "ID_CharacterManagement_CG_VERIFICATION_RESPONSE_CORRUPT",
        6 => "ID_CharacterManagement_CG_VERIFICATION_RESPONSE_DATABASE_DOWN",
        _ => null,
    };
}
