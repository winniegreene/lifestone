using System.Globalization;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Session.Mage;

/// <summary>Where the armour ledger's files live (M4 4.1 lane c): the host reads and writes the text of a named file in the game's user data (the client does no file I/O of its own: hosts supply bytes and streams).</summary>
public interface IArmourLedgerFiles
{
    /// <summary>The text of the file, or null when there is none.</summary>
    /// <param name="name">The file's name (no folder).</param>
    string? ReadText(string name);

    /// <summary>Writes the text, replacing an earlier file of the name (creating the folder when it is missing).</summary>
    /// <param name="name">The file's name (no folder).</param>
    /// <param name="text">The text.</param>
    void WriteText(string name, string text);
}

/// <summary>
/// The armour ledger's store over the game's user data (M4 4.1 lane c; closes <c>TODO(m4-buff-armour-relog-time)</c> with <see cref="RebuffHostLogic"/>): one small JSON file per character, named by the character's
/// guid (<c>armour-&lt;guid, 8 hex digits&gt;.json</c>, never the character's name). The text is <see cref="ArmourBuffLedger.Export"/>'s: the seconds left on each armour line at the last log-off. A file that cannot be read or written
/// is logged as a warning and treated as absent (the conservative rule runs: a line found on a piece with no known time is due at once); it never throws into a frame. The host (<c>RebuffDriverHost</c>) supplies the files.
/// </summary>
public sealed class ArmourLedgerFileStore : IArmourLedgerStore
{
    private readonly IArmourLedgerFiles _files;
    private readonly ILogSink _log;

    /// <summary>A store over <paramref name="files"/>.</summary>
    /// <param name="files">The host's files.</param>
    /// <param name="log">Where a failure is logged.</param>
    public ArmourLedgerFileStore(IArmourLedgerFiles files, ILogSink log)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(log);
        _files = files;
        _log = log;
    }

    /// <summary>The file name of a character.</summary>
    /// <param name="characterGuid">The character's guid.</param>
    public static string NameOf(uint characterGuid) => string.Create(CultureInfo.InvariantCulture, $"armour-{characterGuid:X8}.json");

    /// <inheritdoc/>
    public string? Load(uint characterGuid)
    {
        try
        {
            return _files.ReadText(NameOf(characterGuid));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warn(BuffCycle.LogCategory, "armour memory: the saved ledger could not be read (" + e.GetType().Name + "); the conservative rule runs");
            return null;
        }
    }

    /// <inheritdoc/>
    public void Save(uint characterGuid, string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            _files.WriteText(NameOf(characterGuid), json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Warn(BuffCycle.LogCategory, "armour memory: the ledger could not be saved (" + e.GetType().Name + "); the next login runs the conservative rule");
        }
    }
}
