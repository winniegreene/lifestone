using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>The armour ledger's file store (M4 4.1 lane c, <see cref="ArmourLedgerFileStore"/>): one file per character guid, the ledger's snapshot round trip, and a store that cannot be used never throws into a frame.</summary>
public sealed class ArmourLedgerFileStoreTests
{
    private sealed class MemoryFiles : IArmourLedgerFiles
    {
        public Dictionary<string, string> Files { get; } = [];

        public bool Fail { get; set; }

        public string? ReadText(string name) => Fail ? throw new IOException("denied") : Files.GetValueOrDefault(name);

        public void WriteText(string name, string text)
        {
            if (Fail)
            {
                throw new UnauthorizedAccessException("denied");
            }

            Files[name] = text;
        }
    }

    [Fact]
    public void A_snapshot_survives_a_save_and_a_load_and_the_file_is_named_by_the_guid_alone()
    {
        var files = new MemoryFiles();
        var store = new ArmourLedgerFileStore(files, new RecordingLogSink());
        var ledger = new ArmourBuffLedger();
        ledger.Record(0x80000123, "Impenetrability", 2080, doneAt: 100, duration: 1800);

        Assert.Null(store.Load(0x50000001)); // nothing saved yet
        store.Save(0x50000001, ledger.Export(0x50000001, now: 400));

        Assert.Equal(["armour-50000001.json"], files.Files.Keys);
        var entries = ArmourBuffLedger.ReadSnapshot(store.Load(0x50000001), 0x50000001, out var why);
        Assert.Null(why);
        var entry = Assert.Single(entries);
        Assert.Equal((0x80000123u, "Impenetrability", 2080u), (entry.Piece, entry.Line, entry.Spell));
        Assert.Equal(1500.0, entry.Remaining, 1);
        Assert.Null(store.Load(0x50000002)); // another character has its own file

        store.Save(0x50000001, ledger.Export(0x50000001, now: 1000)); // a second save replaces the first
        Assert.Equal(900.0, Assert.Single(ArmourBuffLedger.ReadSnapshot(store.Load(0x50000001), 0x50000001, out _)).Remaining, 1);
        Assert.Single(files.Files);
    }

    [Fact]
    public void A_folder_that_cannot_be_used_is_a_warning_and_a_missing_file()
    {
        var log = new RecordingLogSink();
        var store = new ArmourLedgerFileStore(new MemoryFiles { Fail = true }, log);

        store.Save(7, "{}");
        Assert.Null(store.Load(7));
        Assert.Contains(log.Lines, l => l.Contains("could not be saved", StringComparison.Ordinal));
        Assert.Contains(log.Lines, l => l.Contains("could not be read", StringComparison.Ordinal));
    }
}
