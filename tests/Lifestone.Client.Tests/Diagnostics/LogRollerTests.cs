using System.Text;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Tests.Diagnostics;

/// <summary>M5 item 7, <c>m5-log-rotation</c> (DoD 13, logs capped): the roll rule against a fake writer, and against real files.</summary>
public sealed class LogRollerTests
{
    private sealed class FakeWriter : ILogRollWriter
    {
        public List<string> Current { get; } = [];

        public List<List<string>> Rolled { get; } = [];

        public bool FailNextRoll { get; set; }

        public void WriteLine(string line) => Current.Add(line);

        public bool Roll()
        {
            if (FailNextRoll)
            {
                FailNextRoll = false;
                return false;
            }

            Rolled.Add([.. Current]);
            Current.Clear();
            return true;
        }
    }

    private static string Header(int rolls) => $"rolled {rolls}";

    [Fact]
    public void A_file_under_the_cap_never_rolls()
    {
        var writer = new FakeWriter();
        var roller = new LogRoller(1000, writer, Header, newLineBytes: 1);

        for (var i = 0; i < 99; i++)
        {
            roller.WriteLine("123456789"); // 10 bytes with the line end
        }

        Assert.Equal(0, roller.Rolls);
        Assert.Equal(99, writer.Current.Count);
        Assert.Equal(990, roller.CurrentBytes);
    }

    [Fact]
    public void The_line_that_passes_the_cap_is_kept_in_the_old_file_and_the_new_one_begins_with_the_roll_line()
    {
        var writer = new FakeWriter();
        var roller = new LogRoller(100, writer, Header, newLineBytes: 1);

        for (var i = 0; i < 10; i++)
        {
            roller.WriteLine("123456789"); // exactly 100 bytes: not past the cap
        }

        Assert.Equal(0, roller.Rolls);
        roller.WriteLine("1234567"); // 108: past it

        Assert.Equal(1, roller.Rolls);
        Assert.Single(writer.Rolled);
        Assert.Equal(11, writer.Rolled[0].Count);
        Assert.Equal("1234567", writer.Rolled[0][^1]);
        Assert.Equal(["rolled 1"], writer.Current);
        Assert.Equal("rolled 1".Length + 1, roller.CurrentBytes);
    }

    [Fact]
    public void The_roll_count_grows_and_each_file_stays_near_the_cap()
    {
        var writer = new FakeWriter();
        var roller = new LogRoller(1000, writer, Header, newLineBytes: 2);

        for (var i = 0; i < 5000; i++)
        {
            roller.WriteLine("a line of text of some length"); // 29 + 2 bytes
        }

        Assert.Equal(writer.Rolled.Count, roller.Rolls);
        Assert.True(roller.Rolls >= 100, $"rolls={roller.Rolls}");
        Assert.Equal($"rolled {roller.Rolls}", writer.Current[0]);
        Assert.All(writer.Rolled, file => Assert.InRange(file.Sum(l => Encoding.UTF8.GetByteCount(l) + 2), 1001, 1000 + 31 + 2));
    }

    [Fact]
    public void Bytes_are_counted_as_utf8_not_as_characters()
    {
        var writer = new FakeWriter();
        var roller = new LogRoller(100, writer, Header, newLineBytes: 1);

        roller.WriteLine(new string('é', 40)); // 80 bytes + 1
        Assert.Equal(81, roller.CurrentBytes);
        roller.WriteLine(new string('€', 10)); // 30 bytes + 1: 112, past the cap

        Assert.Equal(1, roller.Rolls);
    }

    [Fact]
    public void A_cap_of_zero_is_no_cap()
    {
        var writer = new FakeWriter();
        var roller = new LogRoller(0, writer, Header, newLineBytes: 1);

        for (var i = 0; i < 10_000; i++)
        {
            roller.WriteLine("a long enough line to add up to a megabyte or so, many times over, to be sure");
        }

        Assert.Equal(0, roller.Rolls);
        Assert.Equal(10_000, writer.Current.Count);
    }

    [Fact]
    public void A_roll_that_fails_is_counted_and_tried_again_after_another_cap_not_on_every_line()
    {
        var writer = new FakeWriter { FailNextRoll = true };
        var roller = new LogRoller(100, writer, Header, newLineBytes: 1);

        for (var i = 0; i < 12; i++)
        {
            roller.WriteLine("123456789"); // the 11th line passes the cap and the roll fails
        }

        Assert.Equal(1, roller.FailedRolls);
        Assert.Equal(0, roller.Rolls);
        Assert.Empty(writer.Rolled);
        Assert.Equal(12, writer.Current.Count); // nothing lost

        for (var i = 0; i < 10; i++)
        {
            roller.WriteLine("123456789");
        }

        Assert.Equal(1, roller.Rolls); // the next try, a cap's worth later, worked
    }

    [Fact]
    public void The_header_line_is_the_first_line_of_the_new_file_and_is_counted_in_its_size()
    {
        var writer = new FakeWriter();
        var roller = new LogRoller(10, writer, rolls => new string('h', 8), newLineBytes: 1);

        roller.WriteLine("01234567890"); // 12: rolls; the header (9) starts the new file

        Assert.Equal(["hhhhhhhh"], writer.Current);
        Assert.Equal(9, roller.CurrentBytes);
        roller.WriteLine("ab"); // 12 again: rolls again
        Assert.Equal(2, roller.Rolls);
    }

    [Fact]
    public void Rolling_real_files_keeps_two_files_under_twice_the_cap_and_replaces_the_older_one()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lifestone-logroll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var writer = new FileWriter(directory);
            var roller = new LogRoller(2000, writer, rolls => $"log rolled rolls={rolls}", newLineBytes: Environment.NewLine.Length);

            for (var i = 0; i < 400; i++)
            {
                roller.WriteLine($"line {i:D4} 0123456789012345678901234567890123456789");
            }

            writer.Dispose(); // the readers below open the files
            var current = new FileInfo(Path.Combine(directory, "lifestone.log")).Length;
            var older = new FileInfo(Path.Combine(directory, "lifestone.1.log")).Length;
            Assert.True(roller.Rolls >= 5, $"rolls={roller.Rolls}");
            Assert.InRange(current, 1, 2000 + 100);
            Assert.InRange(older, 2001, 2000 + 100);
            Assert.True(current + older < 4000 + 200);
            Assert.Equal(["lifestone.1.log", "lifestone.log"], Directory.GetFiles(directory).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToArray());
            Assert.StartsWith($"log rolled rolls={roller.Rolls}", File.ReadLines(Path.Combine(directory, "lifestone.log")).First(), StringComparison.Ordinal);
            Assert.Contains("log rolled rolls=", File.ReadLines(Path.Combine(directory, "lifestone.1.log")).First(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FakeFolder : ILogFolder
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool HoldCurrent { get; set; }

        public IReadOnlyList<string> FileNames() => [.. Files.Keys];

        public void Move(string from, string to)
        {
            if (HoldCurrent && from == "lifestone.log")
            {
                throw new IOException("held by another program");
            }

            Files[to] = Files[from];
            Files.Remove(from);
        }

        public void Delete(string name) => Files.Remove(name);
    }

    [Theory]
    [InlineData(1, new[] { "lifestone.1.log", "lifestone.log" })]
    [InlineData(3, new[] { "lifestone.1.log", "lifestone.2.log", "lifestone.3.log", "lifestone.log" })]
    [InlineData(0, new[] { "lifestone.1.log", "lifestone.2.log", "lifestone.3.log", "lifestone.4.log", "lifestone.5.log", "lifestone.6.log", "lifestone.log" })]
    public void A_roll_with_keep_N_shifts_the_older_files_up_oldest_highest_and_deletes_past_N_M5_7_F17(int keep, string[] expectedFiles)
    {
        var folder = new FakeFolder();
        for (var roll = 1; roll <= 6; roll++)
        {
            folder.Files["lifestone.log"] = $"part {roll}";
            LogFileRotation.Roll(folder, keep);
        }

        folder.Files["lifestone.log"] = "current";
        Assert.Equal(expectedFiles, folder.Files.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("part 6", folder.Files["lifestone.1.log"]); // the newest rolled file is number 1
        var rolled = LogFileRotation.RolledFilesOldestFirst(folder.FileNames()).Select(n => folder.Files[n]).ToArray();
        var expected = Enumerable.Range(1, 6).Reverse().Take(rolled.Length).Reverse().Select(n => $"part {n}").ToArray();
        Assert.Equal(expected, rolled); // oldest first: the soak report reads them in this order, then lifestone.log
    }

    [Fact]
    public void A_roll_whose_current_file_cannot_move_changes_nothing_and_the_names_are_read_exactly_M5_7_F17()
    {
        var folder = new FakeFolder { HoldCurrent = true };
        folder.Files["lifestone.1.log"] = "older";
        folder.Files["lifestone.log"] = "current";

        Assert.Throws<IOException>(() => LogFileRotation.Roll(folder, 1)); // another program holds the file

        Assert.Equal("older", folder.Files["lifestone.1.log"]);
        Assert.Equal("current", folder.Files["lifestone.log"]);
        Assert.Equal(2, folder.Files.Count);
        Assert.Equal(3, LogFileRotation.IndexOf("lifestone.3.log"));
        Assert.Equal(0, LogFileRotation.IndexOf("lifestone.log"));
        Assert.Equal(0, LogFileRotation.IndexOf("lifestone.x.log"));
        Assert.Equal(0, LogFileRotation.IndexOf("lifestone.0.log"));
        Assert.Equal(0, LogFileRotation.IndexOf("lifestone.rolling.tmp"));
    }

    [Fact]
    public void A_failed_reopen_is_tried_again_at_once_then_at_most_once_per_interval_and_warned_once_7_R4()
    {
        var gate = new LogReopenGate(retryMs: 5000);

        Assert.True(gate.MayTry(1000)); // the first try is at once
        Assert.False(gate.MayTry(1001)); // the next write a moment later: not yet
        Assert.False(gate.MayTry(5999));
        Assert.True(gate.MayTry(6000)); // another interval on: tried again, so the log is never dropped for good
        Assert.Equal(2, gate.Tries);

        Assert.True(gate.WarnOnce());
        Assert.False(gate.WarnOnce()); // one warning for a run of failures
        gate.Reopened();
        Assert.True(gate.MayTry(6001)); // open again: a later failure is tried and warned afresh
        Assert.True(gate.WarnOnce());
    }

    [Fact]
    public void The_game_log_reopens_a_null_file_on_the_next_write_or_roll_through_the_gate_7_R4()
    {
        var log = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Core", "Log.cs")).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains("private readonly LogReopenGate _reopen = new();", log, StringComparison.Ordinal);
        Assert.Contains("if (_file is null)\n            {\n                TryReopen();", log, StringComparison.Ordinal); // the write and the roll both reopen
        Assert.Contains("new StreamWriter(path, append: true", log, StringComparison.Ordinal);
    }

    // The same rename-and-reopen the game's Log does, on a temp folder.
    private sealed class FileWriter : ILogRollWriter, IDisposable
    {
        private readonly string _path;
        private readonly string _older;
        private StreamWriter _file;

        public FileWriter(string directory)
        {
            _path = Path.Combine(directory, "lifestone.log");
            _older = Path.Combine(directory, "lifestone.1.log");
            _file = new StreamWriter(_path, append: false, new UTF8Encoding(false));
        }

        public void WriteLine(string line) => _file.WriteLine(line);

        public void Flush() => _file.Flush();

        public bool Roll()
        {
            _file.Dispose();
            File.Move(_path, _older, overwrite: true);
            _file = new StreamWriter(_path, append: false, new UTF8Encoding(false));
            return true;
        }

        public void Dispose() => _file.Dispose();
    }
}
