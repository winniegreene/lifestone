using Lifestone.Client.Session.Autopilot;
using Lifestone.Headless;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M5 4 h3: the headless host's checkpoint store (<c>AutopilotFileCheckpointStore</c>; a plain unit test in this project, in a temporary folder). The client does no file I/O, so the store is the host's: a write is a temporary file moved over the old
/// checkpoint (a reader sees the old text or the new, and no temporary file stays), and a set-aside is a rename that never deletes. The game's <c>UserDataCheckpointStore</c> has the same steps and is run by the play-check.
/// </summary>
public sealed class AutopilotFileCheckpointStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lifestone-autopilot-store-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void A_missing_checkpoint_reads_as_none_and_a_write_makes_the_folder_and_replaces_the_old_text_in_one_step()
    {
        var store = new AutopilotFileCheckpointStore(Path.Combine(_folder, "deeper", "bsd-mage.json"));
        Assert.Null(store.Read());
        Assert.Equal("bsd-mage.json", store.Describe);

        store.Write("{\"schema\":1}");
        store.Write("{\"schema\":1,\"cycle\":2}");
        Assert.Equal("{\"schema\":1,\"cycle\":2}", store.Read());
        Assert.Equal(["bsd-mage.json"], Directory.GetFiles(Path.GetDirectoryName(store.Path)!).Select(Path.GetFileName)); // no .tmp left behind
    }

    [Fact]
    public void A_set_aside_renames_the_file_aside_then_aside2_and_never_deletes_it()
    {
        var store = new AutopilotFileCheckpointStore(Path.Combine(_folder, "a.json"));
        store.Write("first");
        var one = store.SetAside();
        Assert.EndsWith("a.json.aside", one, StringComparison.Ordinal);
        Assert.Null(store.Read()); // the next write starts a new one
        store.Write("second");
        var two = store.SetAside();
        Assert.EndsWith("a.json.aside2", two, StringComparison.Ordinal);
        Assert.Equal(("first", "second"), (File.ReadAllText(one), File.ReadAllText(two)));
    }

    [Fact]
    public void A_write_that_cannot_happen_is_an_IOException_the_autopilot_names()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "is-a-folder.json"));
        var store = new AutopilotFileCheckpointStore(Path.Combine(_folder, "is-a-folder.json"));
        Assert.ThrowsAny<IOException>(() => store.Write("x"));
    }

    [Fact]
    public void The_headless_host_takes_the_checkpoint_folder_option_and_keeps_the_arguments_for_the_scenario()
    {
        var o = HeadlessOptions.Parse(["--scenario=autopilot", "--autopilot-checkpoint-dir=cache/runs/autopilot", "--autopilot-minutes=30"]);
        Assert.Empty(o.Errors);
        Assert.Contains("--autopilot-checkpoint-dir=cache/runs/autopilot", o.Arguments);
        Assert.Equal("cache/runs/autopilot", AutopilotHostOptions.FromArguments(Lifestone.Client.Options.CommandLineArgument.ParseAll(o.Arguments)).CheckpointDirectory);
    }
}
