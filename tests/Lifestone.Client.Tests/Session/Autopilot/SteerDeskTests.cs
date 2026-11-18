using System.Reflection;
using System.Text;
using System.Text.Json;
using Lifestone.Client.Options;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Autopilot.Commands;
using Lifestone.Client.Session.Autopilot.Steer;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Quests;
using Lifestone.Net.Diagnostics;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>The steer desk's first slice: the order envelope, the file spool over an in-memory disk, the probes, the live picture and the event stream. The real files and the thread are the play-checks' (<c>PlayChecks.Steer.cs</c>).</summary>
public sealed class SteerDeskTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Issued = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static string Envelope(string id = "o-1", string kind = "sheet", string extra = "", string guard = "\"guard\":{\"character_guid\":\"0x50000A12\"},", int expires = 60, string issued = "2026-11-09T12:00:00Z") =>
        $"{{\"v\":1,\"id\":\"{id}\",\"kind\":\"{kind}\",\"by\":\"director\",\"why\":\"because\",\"issued_utc\":\"{issued}\",\"expires_s\":{expires},{guard}{extra}\"args\":{{\"section\":\"skills\"}}}}";

    [Fact]
    public void A_well_formed_order_parses_with_every_field()
    {
        var parsed = SteerOrder.Parse(Bytes(Envelope(extra: "\"dry_run\":true,", guard: "\"guard\":{\"character_guid\":\"0x50000A12\",\"level_min\":2,\"level_max\":9,\"ground\":\"tusker-cave\"},")));
        var order = Assert.IsType<SteerOrder>(parsed.Order);
        Assert.Equal(("o-1", "sheet", "director", "because", 60, true), (order.Id, order.Kind, order.By, order.Why, order.ExpiresSeconds, order.DryRun));
        Assert.Equal(new SteerGuard(0x50000A12, 2, 9, "tusker-cave"), order.Guard);
        Assert.Equal(Issued.AddSeconds(60), order.ExpiresUtc);
        Assert.Equal("skills", order.ArgText("section"));
    }

    [Theory]
    [InlineData("{", "bad-json")]
    [InlineData("[1]", "bad-json")]
    [InlineData("{\"v\":2}", "bad-version")]
    [InlineData("{\"v\":\"1\"}", "bad-version")]
    [InlineData("{\"v\":1,\"id\":\"a/b\",\"kind\":\"ping\"}", "bad-id")]
    [InlineData("{\"v\":1,\"id\":\"a\",\"kind\":\"fly\"}", "unknown-kind")]
    [InlineData("{\"v\":1,\"id\":\"a\",\"kind\":\"ping\",\"by\":\"a b\"}", "bad-by")]
    [InlineData("{\"v\":1,\"id\":\"a\",\"kind\":\"ping\",\"by\":\"d\",\"args\":[1]}", "bad-args")]
    [InlineData("{\"v\":1,\"id\":\"a\",\"kind\":\"ping\",\"by\":\"d\",\"issued_utc\":\"soon\"}", "bad-issued")]
    [InlineData("{\"v\":1,\"id\":\"a\",\"kind\":\"ping\",\"by\":\"d\",\"issued_utc\":\"2026-11-09T12:00:00Z\",\"expires_s\":4}", "bad-expiry")]
    [InlineData("{\"v\":1,\"id\":\"a\",\"kind\":\"ping\",\"by\":\"d\",\"issued_utc\":\"2026-11-09T12:00:00Z\",\"expires_s\":86401}", "bad-expiry")]
    [InlineData("{\"v\":1,\"id\":\"a\",\"kind\":\"ping\",\"by\":\"d\",\"issued_utc\":\"2026-11-09T12:00:00Z\",\"expires_s\":5,\"dry_run\":1}", "bad-dry-run")]
    [InlineData("{\"v\":1,\"id\":\"a\",\"kind\":\"sheet\",\"by\":\"d\",\"issued_utc\":\"2026-11-09T12:00:00Z\",\"expires_s\":5}", "bad-guard")]
    [InlineData("{\"v\":1,\"id\":\"a\",\"kind\":\"sheet\",\"by\":\"d\",\"issued_utc\":\"2026-11-09T12:00:00Z\",\"expires_s\":5,\"guard\":{\"character_guid\":\"50000A12\"}}", "bad-guard")]
    [InlineData("{\"v\":1,\"id\":\"a\",\"kind\":\"ping\",\"by\":\"d\",\"issued_utc\":\"2026-11-09T12:00:00Z\",\"expires_s\":5,\"guard\":{\"level_min\":\"low\"}}", "bad-guard")]
    public void A_bad_order_is_refused_by_name(string text, string name) => Assert.Equal(name, SteerOrder.Parse(Bytes(text)).Refusal);

    [Fact]
    public void Bad_utf8_is_named_and_not_thrown() => Assert.Equal("bad-encoding", SteerOrder.Parse([0x7B, 0xFF, 0xFE, 0x7D]).Refusal);

    [Fact]
    public void Ping_needs_no_guard()
    {
        var parsed = SteerOrder.Parse(Bytes("{\"v\":1,\"id\":\"a\",\"kind\":\"ping\",\"by\":\"d\",\"issued_utc\":\"2026-11-09T12:00:00Z\",\"expires_s\":5}"));
        Assert.Null(parsed.Refusal);
        Assert.Null(parsed.Order!.Guard.CharacterGuid);
    }

    [Fact]
    public void Why_is_cleaned_of_control_characters_and_capped()
    {
        var parsed = SteerOrder.Parse(Bytes("{\"v\":1,\"id\":\"a\",\"kind\":\"ping\",\"by\":\"d\",\"issued_utc\":\"2026-11-09T12:00:00Z\",\"expires_s\":5,\"why\":\"  one\\ntwo\\r" + new string('x', 400) + "\"}"));
        var why = parsed.Order!.Why;
        Assert.StartsWith("one two ", why, StringComparison.Ordinal);
        Assert.True(why.Length <= SteerOrder.MaxWhyLength);
        Assert.DoesNotContain(why, char.IsControl);
    }

    [Theory]
    [InlineData("o-000142", true)]
    [InlineData("a.b_c-1", true)]
    [InlineData("COMA", true)]
    [InlineData("", false)]
    [InlineData(".hidden", false)]
    [InlineData("..", false)]
    [InlineData("a\\b", false)]
    [InlineData("a/b", false)]
    [InlineData("a:b", false)]
    [InlineData("nul", false)]
    [InlineData("CON.1", false)]
    [InlineData("com7", false)]
    public void An_id_cannot_name_a_path_or_a_device(string id, bool ok) => Assert.Equal(ok, SteerOrder.IsId(id));

    [Fact]
    public void The_contract_names_29_kinds_in_four_classes_and_this_build_answers_the_probes_the_controls_and_the_command_orders()
    {
        Assert.Equal(29, SteerKinds.All.Count);
        Assert.Equal((3, 6, 12, 8), (Count(SteerClass.Probe), Count(SteerClass.Control), Count(SteerClass.Policy), Count(SteerClass.Job)));
        Assert.Equal(["cancel", "capture", "command", "leave", "ping", "resume", "run_quest", "sheet", "start", "stop_here", "stop_home", "trip"], SteerKinds.All.Keys.Where(SteerKinds.IsBuilt).Order(StringComparer.Ordinal));
        Assert.Equal(["command", "run_quest", "trip"], SteerKinds.All.Keys.Where(SteerKinds.IsCommand).Order(StringComparer.Ordinal));

        static int Count(SteerClass c) => SteerKinds.All.Values.Count(v => v == c);
    }

    [Theory]
    [InlineData("000123-o-1.json", 123, "o-1")]
    [InlineData("7-a.b.json", 7, "a.b")]
    public void A_good_file_name_reads(string name, long sequence, string id)
    {
        Assert.True(SteerFileName.TryParse(name, out var s, out var i));
        Assert.Equal((sequence, id), (s, i));
    }

    [Theory]
    [InlineData("o-1.json")]
    [InlineData("12-.json")]
    [InlineData("12-a.txt")]
    [InlineData("12-a/b.json")]
    [InlineData("12-nul.json")]
    public void A_bad_file_name_does_not(string name) => Assert.False(SteerFileName.TryParse(name, out _, out _));

    private sealed class FakeDisk : ISteerDisk
    {
        public Dictionary<string, byte[]> Inbox { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, DateTimeOffset> Written { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public List<string> Taken { get; } = [];

        public List<string> Reads { get; } = [];

        public List<string> Ops { get; } = [];

        public Func<string, bool> WriteFails { get; set; } = _ => false;

        public Func<string, bool> ReadFails { get; set; } = _ => false;

        public void Drop(string name, string text, DateTimeOffset? written = null)
        {
            Inbox[name] = Bytes(text);
            Written[name] = written ?? Issued;
        }

        public IReadOnlyList<string> InboxNames() => [.. Inbox.Keys];

        public long InboxSize(string name) => Inbox[name].Length;

        public DateTimeOffset InboxWritten(string name) => Written[name];

        public byte[] ReadInbox(string name)
        {
            if (ReadFails(name))
            {
                throw new IOException("in use");
            }

            Reads.Add(name);
            return Inbox[name];
        }

        public void TakeInbox(string name)
        {
            Guard("take " + name);
            Inbox.Remove(name);
            Taken.Add(name);
        }

        public bool HasReport(string id) => Files.ContainsKey($"outbox/{id}.json");

        public void Replace(string path, string text)
        {
            Guard("replace " + path);
            Files[path] = text;
        }

        public void Append(string path, string text)
        {
            Guard("append " + path);
            Files[path] = Files.GetValueOrDefault(path, string.Empty) + text;
        }

        public void SetAside(string path)
        {
            Guard("aside " + path);
            if (Files.Remove(path, out var text))
            {
                Files[path + ".aside"] = text;
            }
        }

        public IReadOnlyList<string> ReportIds() => [.. Files.Keys.Where(k => k.StartsWith("outbox/", StringComparison.Ordinal)).Select(k => k["outbox/".Length..^".json".Length])];

        public string ReadReport(string id) => Files[$"outbox/{id}.json"];

        private void Guard(string op)
        {
            if (WriteFails(op))
            {
                throw new UnauthorizedAccessException("read-only");
            }

            Ops.Add(op);
        }
    }

    private static SteerSpool NewSpool(FakeDisk disk) => new(disk, Issued.AddMinutes(-5));

    private static List<SteerInbound> Taken(SteerSpool spool)
    {
        var all = new List<SteerInbound>();
        while (spool.TryTake(out var inbound))
        {
            all.Add(inbound);
        }

        return all;
    }

    [Fact]
    public void A_file_over_the_limit_is_refused_unread()
    {
        var disk = new FakeDisk();
        disk.Drop("1-big.json", new string(' ', (int)SteerSpool.MaxOrderBytes + 1));
        var spool = NewSpool(disk);
        spool.Step(0);
        var inbound = Assert.Single(Taken(spool));
        Assert.Equal(("too-large", "big"), (inbound.Refusal, inbound.Id));
        Assert.Empty(disk.Reads);
    }

    [Fact]
    public void A_file_older_than_the_client_is_refused_unread_as_restarted()
    {
        var disk = new FakeDisk();
        disk.Drop("1-old.json", Envelope("old"), Issued.AddMinutes(-6));
        var spool = NewSpool(disk);
        spool.Step(0);
        Assert.Equal("restarted", Assert.Single(Taken(spool)).Refusal);
        Assert.Empty(disk.Reads);
    }

    [Fact]
    public void A_torn_file_is_refused_by_name_against_its_file_names_id()
    {
        var disk = new FakeDisk();
        disk.Drop("1-torn.json", "{\"v\":1,\"id\":");
        var spool = NewSpool(disk);
        spool.Step(0);
        var inbound = Assert.Single(Taken(spool));
        Assert.Equal(("bad-json", "torn"), (inbound.Refusal, inbound.Id));
    }

    [Fact]
    public void Orders_come_in_sequence_order_whatever_the_names_sort_as()
    {
        var disk = new FakeDisk();
        disk.Drop("10-c.json", Envelope("c"));
        disk.Drop("9-b.json", Envelope("b"));
        disk.Drop("0002-a.json", Envelope("a"));
        var spool = NewSpool(disk);
        spool.Step(0);
        Assert.Equal(["a", "b", "c"], Taken(spool).Select(i => i.Id));
    }

    [Fact]
    public void A_file_in_flight_is_read_once_and_stays_until_it_is_taken()
    {
        var disk = new FakeDisk();
        disk.Drop("1-a.json", Envelope("a"));
        var spool = NewSpool(disk);
        spool.Step(0);
        spool.Step(5);
        Assert.Single(Taken(spool));
        Assert.Single(disk.Reads);
        Assert.Contains("1-a.json", disk.Inbox.Keys);
        spool.Post(new SteerWrite(SteerWriteKind.Take, "1-a.json"));
        spool.Step(10);
        Assert.Empty(disk.Inbox);
        Assert.Equal(["1-a.json"], disk.Taken);
    }

    [Fact]
    public void A_repeated_id_is_a_duplicate_in_the_same_run_and_after_a_restart()
    {
        var disk = new FakeDisk();
        disk.Drop("1-a.json", Envelope("a"));
        disk.Drop("2-a.json", Envelope("a"));
        var spool = NewSpool(disk);
        spool.Step(0);
        var both = Taken(spool);
        Assert.Equal([false, true], both.Select(i => i.Duplicate));

        disk.Files["outbox/b.json"] = "{}"; // a report from before the restart
        disk.Drop("3-b.json", Envelope("b"));
        var restarted = NewSpool(disk);
        restarted.Step(0);
        Assert.Contains(Taken(restarted), i => i is { Id: "b", Duplicate: true });
    }

    [Fact]
    public void A_file_name_that_disagrees_with_the_id_inside_is_refused()
    {
        var disk = new FakeDisk();
        disk.Drop("1-a.json", Envelope("z"));
        var spool = NewSpool(disk);
        spool.Step(0);
        Assert.Equal("id-mismatch", Assert.Single(Taken(spool)).Refusal);
    }

    [Fact]
    public void A_failed_write_is_retried_and_nothing_overtakes_it()
    {
        var disk = new FakeDisk { WriteFails = op => op == "replace first" };
        var spool = NewSpool(disk);
        spool.Post(new SteerWrite(SteerWriteKind.Replace, "first", "1"));
        spool.Post(new SteerWrite(SteerWriteKind.Append, "second", "2"));
        spool.Step(0);
        Assert.Empty(disk.Ops);
        disk.WriteFails = _ => false;
        spool.Step(0.1);
        Assert.Equal(["replace first", "append second"], disk.Ops);
        Assert.Null(spool.Fault);
    }

    [Fact]
    public void Only_the_newest_picture_is_written()
    {
        var disk = new FakeDisk();
        var spool = NewSpool(disk);
        spool.PostStatus("old");
        spool.PostStatus("new");
        spool.Step(0);
        Assert.Equal("new", disk.Files[SteerSpool.StatusFile]);
        Assert.Single(disk.Ops);
    }

    [Fact]
    public void A_folder_that_stays_unwritable_is_named_and_a_brief_failure_is_not()
    {
        var disk = new FakeDisk { WriteFails = _ => true };
        var spool = NewSpool(disk);
        spool.Post(new SteerWrite(SteerWriteKind.Replace, "outbox/a.json", "1"));
        spool.Step(0);
        spool.Step(SteerSpool.GiveUpSeconds - 1);
        Assert.Null(spool.Fault);
        disk.WriteFails = _ => false;
        spool.Step(SteerSpool.GiveUpSeconds - 0.5); // recovered: the clock starts again

        disk.WriteFails = _ => true;
        spool.Post(new SteerWrite(SteerWriteKind.Replace, "outbox/b.json", "2"));
        spool.Step(100);
        spool.Step(100 + SteerSpool.GiveUpSeconds);
        Assert.Contains("outbox/b.json", spool.Fault, StringComparison.Ordinal);
        Assert.Contains("UnauthorizedAccessException", spool.Fault, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inbox_file_held_open_is_read_when_it_is_free_and_refused_if_it_never_is()
    {
        var disk = new FakeDisk { ReadFails = _ => true };
        disk.Drop("1-a.json", Envelope("a"));
        disk.Drop("2-b.json", Envelope("b"));
        disk.ReadFails = n => n == "2-b.json";
        var spool = NewSpool(disk);
        for (var i = 0; i < 2; i++)
        {
            spool.Step(i * 2);
        }

        Assert.Equal(["a"], Taken(spool).Select(x => x.Id)); // b was not read yet, and not called a duplicate of itself
        disk.ReadFails = _ => false;
        spool.Step(10);
        var b = Assert.Single(Taken(spool));
        Assert.Equal(("b", false, null), (b.Id, b.Duplicate, b.Refusal));

        disk.Drop("3-c.json", Envelope("c"));
        disk.ReadFails = n => n == "3-c.json";
        for (var i = 0; i < SteerSpool.FileTries; i++)
        {
            spool.Step(20 + (i * 2));
        }

        Assert.Equal("unreadable", Assert.Single(Taken(spool)).Refusal);
    }

    private sealed class DeskRig
    {
        private double _pump;

        public DeskRig(ITestOutputHelper output, Action<string>? press = null, string? overlayText = null, Action? withdraw = null, bool observeOnly = false, ISteerCamera? camera = null, bool orders = false)
        {
            World = new AutopilotRig.ApWorld(AutopilotRig.Den, output);
            var options = new AutopilotOptions();
            Logic = new AutopilotHostLogic(
                carry =>
                {
                    var scenario = new AutopilotScenario(carry.Remaining(options), World.Factory, World.Trip.Context(), null, null, carry.Seed);
                    carry.Apply(scenario);
                    return scenario;
                },
                World.Trip.Context().Log,
                options,
                World.Ground);
            Disk = new FakeDisk();
            Spool = new SteerSpool(Disk, View.Clock.Time.GetUtcNow().AddMinutes(-5));
            Orders = new CommandRunner(CommandRegistry.Default, new DialCommandTarget(Logic, () => View, person => { Logic.Toggle(View, person ? null : "steer"); return "pressed"; }, via => Logic.StopHere(View, via)), World.Trip.Context().Log);
            Desk = new OrderDesk(Spool, "run-1", "test-build", 300, () => Timers, World.Trip.Context().Log, press ?? (startedBy => Logic.Toggle(View, startedBy)), overlayText, withdraw, observeOnly, camera, Orders, orders);
            View.State.Self.Guid = Self;
        }

        public const uint Self = 0x50000A12;

        public AutopilotRig.ApWorld World { get; }

        public AutopilotHostLogic Logic { get; }

        public FakeDisk Disk { get; }

        public SteerSpool Spool { get; }

        public OrderDesk Desk { get; }

        public CommandRunner Orders { get; }

        public QuestTimers? Timers { get; set; } = new("Test");

        public IGameSessionView View => World.Trip.View;

        public string Now => SteerJson.Utc(View.Clock.Time.GetUtcNow());

        /// <summary>One frame of the world and the dial, as the play host runs them.</summary>
        public void DialFrame()
        {
            var t = World.Trip;
            t.Vendor.R.Tick(0.25);
            t.Shops.Pump();
            t.Xp.Pump();
            t.Vendor.R.Tick();
            Logic.Tick(View, World.Now);
        }

        public bool RunUntil(Func<bool> reached, int frames = 40000)
        {
            for (var i = 0; i < frames && !reached(); i++)
            {
                DialFrame();
            }

            return reached();
        }

        public bool Fighting => Logic.Scenario is { State: AutopilotState.Fight, CurrentRun: Lifestone.Client.Session.Scenarios.IStoppableFight { Kills: >= 1 } };

        public void Advance(double seconds) => World.Trip.Vendor.R.Tick(seconds);

        /// <summary>A desk frame between two turns of the pump: what was dropped is read, answered and written.</summary>
        public void Turn()
        {
            Spool.Step(_pump += 2);
            Desk.Tick(View, Logic);
            Spool.Step(_pump += 2);
        }

        public void Drop(string id, string kind, string args = "", string? guard = null, int expires = 60, string extra = "", string? issued = null, int sequence = 1)
        {
            var g = guard ?? "\"guard\":{\"character_guid\":\"0x50000A12\"},";
            Disk.Drop($"{sequence}-{id}.json", $"{{\"v\":1,\"id\":\"{id}\",\"kind\":\"{kind}\",\"by\":\"drill\",\"issued_utc\":\"{issued ?? Now}\",\"expires_s\":{expires},{g}{extra}\"args\":{{{args}}}}}", View.Clock.Time.GetUtcNow());
        }

        public JsonElement Report(string id) => JsonDocument.Parse(Disk.Files[$"outbox/{id}.json"]).RootElement;

        public (string? State, string? Name) Result(string id) => (Report(id).GetProperty("state").GetString(), Report(id).GetProperty("name").GetString());

        public string[] Events => Disk.Files.GetValueOrDefault(SteerSpool.EventsFile, string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        public JsonElement Status => JsonDocument.Parse(Disk.Files[SteerSpool.StatusFile]).RootElement;
    }

    [Fact]
    public void Ping_answers_pong_with_the_report_the_contract_names_and_needs_no_guard()
    {
        var rig = new DeskRig(output);
        rig.Drop("p", "ping", guard: string.Empty);
        rig.Turn();
        var report = rig.Report("p");
        Assert.Equal(("done", "pong"), rig.Result("p"));
        foreach (var field in new[] { "v", "id", "kind", "state", "name", "detail", "at_utc", "level", "ground", "cycle", "evidence" })
        {
            Assert.True(report.TryGetProperty(field, out _), field);
        }

        Assert.True(report.GetProperty("evidence").GetProperty("desk_open").GetBoolean());
        Assert.Equal(["1-p.json"], rig.Disk.Taken);
    }

    [Fact]
    public void Every_sheet_section_is_written_before_its_report()
    {
        var rig = new DeskRig(output);
        Assert.True(rig.View.State.Self.Qualities.IsKnown);
        foreach (var section in SteerPicture.Sections)
        {
            rig.Drop("s-" + section, "sheet", $"\"section\":\"{section}\"");
            rig.Turn();
            Assert.Equal(("done", "sheet:" + section), rig.Result("s-" + section));
            using var sheet = JsonDocument.Parse(rig.Disk.Files[$"sheets/{section}.json"]);
            Assert.Equal(section, sheet.RootElement.GetProperty("section").GetString());
            var ops = rig.Disk.Ops;
            Assert.True(ops.IndexOf($"replace sheets/{section}.json") < ops.IndexOf($"replace outbox/s-{section}.json"));
        }
    }

    [Fact]
    public void A_quest_sheet_says_when_no_timers_are_known()
    {
        var rig = new DeskRig(output) { Timers = null };
        rig.Drop("q", "sheet", "\"section\":\"quests\"");
        rig.Turn();
        using var sheet = JsonDocument.Parse(rig.Disk.Files["sheets/quests.json"]);
        Assert.False(sheet.RootElement.GetProperty("timers_known").GetBoolean());
        Assert.Equal(QuestCatalog.All.Count, sheet.RootElement.GetProperty("quests").GetArrayLength());
    }

    [Theory]
    [InlineData("\"section\":\"nope\"")]
    [InlineData("")]
    public void A_section_the_sheet_does_not_have_is_refused(string args)
    {
        var rig = new DeskRig(output);
        rig.Drop("s", "sheet", args);
        rig.Turn();
        Assert.Equal(("refused", "unknown-section"), rig.Result("s"));
    }

    [Theory]
    [InlineData("\"guard\":{\"character_guid\":\"0x50000001\"},", "guard-character")]
    [InlineData("\"guard\":{\"character_guid\":\"0x50000A12\",\"level_min\":9999},", "guard-level")]
    [InlineData("\"guard\":{\"character_guid\":\"0x50000A12\",\"level_max\":-1},", "guard-level")]
    [InlineData("\"guard\":{\"character_guid\":\"0x50000A12\",\"ground\":\"somewhere-else\"},", "guard-ground")]
    public void A_guard_that_no_longer_holds_is_refused_by_name(string guard, string name)
    {
        var rig = new DeskRig(output);
        rig.Drop("s", "sheet", "\"section\":\"skills\"", guard);
        rig.Turn();
        Assert.Equal(("refused", name), rig.Result("s"));
        Assert.False(rig.Disk.Files.ContainsKey("sheets/skills.json"));
    }

    [Fact]
    public void An_order_past_its_life_expires_and_does_nothing()
    {
        var rig = new DeskRig(output);
        var issued = rig.Now;
        rig.Advance(10);
        rig.Drop("s", "sheet", "\"section\":\"skills\"", expires: 5, issued: issued);
        rig.Turn();
        Assert.Equal(("expired", "expired"), rig.Result("s"));
        Assert.False(rig.Disk.Files.ContainsKey("sheets/skills.json"));
    }

    [Fact]
    public void Every_kind_not_built_is_refused_by_name_and_nothing_is_sent_or_changed()
    {
        var rig = new DeskRig(output);
        var sent = rig.View.ActionLog.Count;
        var n = 1;
        foreach (var kind in SteerKinds.All.Keys.Where(k => !SteerKinds.IsBuilt(k)))
        {
            rig.Drop("k" + n, kind, sequence: n);
            rig.Turn();
            Assert.Equal(("refused", "not-built"), rig.Result("k" + n));
            n++;
        }

        Assert.Equal(18, n);
        Assert.Equal(AutopilotMode.Off, rig.Logic.Mode);
        Assert.Equal(sent, rig.View.ActionLog.Count);
    }

    [Fact]
    public void A_dry_run_runs_the_checks_acts_on_nothing_and_answers_checked()
    {
        var rig = new DeskRig(output);
        rig.Drop("s", "sheet", "\"section\":\"skills\"", extra: "\"dry_run\":true,");
        rig.Drop("p", "ping", guard: string.Empty, extra: "\"dry_run\":true,", sequence: 2);
        rig.Drop("bad", "sheet", "\"section\":\"nope\"", extra: "\"dry_run\":true,", sequence: 3);
        rig.Drop("t", "trip", extra: "\"dry_run\":true,", sequence: 4);
        rig.Turn();
        Assert.Equal(("checked", "dry-run"), rig.Result("s"));
        Assert.Equal(("checked", "dry-run"), rig.Result("p"));
        Assert.Equal(("refused", "unknown-section"), rig.Result("bad"));
        Assert.Equal(("refused", "orders-off"), rig.Result("t")); // a command order needs --autopilot-steer-orders=on, dry run or not
        Assert.False(rig.Disk.Files.ContainsKey("sheets/skills.json"));
    }

    [Fact]
    public void Observe_only_refuses_every_order_that_is_not_a_probe_by_name_and_says_so()
    {
        var rig = new DeskRig(output, observeOnly: true);
        var sent = rig.View.ActionLog.Count;
        var n = 1;
        foreach (var kind in SteerKinds.All.Where(k => k.Value != SteerClass.Probe).Select(k => k.Key))
        {
            rig.Drop("k" + n, kind, sequence: n);
            rig.Turn();
            Assert.Equal(("refused", "observe-only"), rig.Result("k" + n));
            n++;
        }

        Assert.Equal(27, n);
        Assert.Equal(AutopilotMode.Off, rig.Logic.Mode);
        Assert.Equal(sent, rig.View.ActionLog.Count);
        Assert.Contains("refused:observe-only", rig.Disk.Files[SteerSpool.EventsFile], StringComparison.Ordinal);
    }

    [Fact]
    public void Observe_only_refuses_a_dry_run_too_and_an_expired_order_is_still_refused_as_observe_only()
    {
        var rig = new DeskRig(output, observeOnly: true);
        rig.Drop("d", "start", extra: "\"dry_run\":true,");
        rig.Drop("x", "stop_home", expires: 5, issued: SteerJson.Utc(rig.View.Clock.Time.GetUtcNow().AddMinutes(-5)), sequence: 2);
        rig.Turn();
        Assert.Equal(("refused", "observe-only"), rig.Result("d"));
        Assert.Equal(("refused", "observe-only"), rig.Result("x"));
    }

    [Fact]
    public void Observe_only_is_off_by_default_and_the_status_says_which_it_is()
    {
        var watching = new DeskRig(output, observeOnly: true);
        watching.Turn();
        Assert.Contains("\"observe_only\":true", watching.Disk.Files[SteerSpool.StatusFile], StringComparison.Ordinal);

        var steering = new DeskRig(output);
        steering.Drop("c", "cancel", "\"target\":\"nothing\"");
        steering.Turn();
        Assert.Equal(("refused", "unknown-order"), steering.Result("c"));
        Assert.Contains("\"observe_only\":false", steering.Disk.Files[SteerSpool.StatusFile], StringComparison.Ordinal);
    }

    [Fact]
    public void A_command_order_round_trips_through_the_desk_to_the_chats_runner_and_its_report_follows_it_to_the_end()
    {
        var rig = new DeskRig(output, observeOnly: true, orders: true); // observe-only, as the live climber runs: the command orders are taken with the orders switch on
        rig.Drop("s", "command", "\"line\":\"status\"");
        rig.Drop("q", "run_quest", "\"key\":\"no-such-quest\"", sequence: 2);
        rig.Drop("b", "command", "\"line\":\"buy scarab\"", sequence: 3);
        rig.Drop("n", "command", sequence: 4);
        rig.Drop("x", "start", sequence: 5);
        rig.Turn();
        Assert.Equal(("done", "status"), rig.Result("s"));
        Assert.Contains("autopilot off", rig.Report("s").GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(("refused", "unknown-quest"), rig.Result("q"));
        Assert.Equal(("refused", "not-built"), rig.Result("b"));
        Assert.StartsWith("TODO(m6-cmd-buy)", rig.Report("b").GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(("refused", "bad-args"), rig.Result("n"));
        Assert.Equal(("refused", "observe-only"), rig.Result("x")); // the controls stay under observe-only

        rig.Logic.Toggle(rig.View);
        Assert.True(rig.RunUntil(() => rig.Fighting));
        rig.Drop("t", "trip", sequence: 6);
        rig.Turn();
        Assert.Equal(("running", "waiting"), rig.Result("t"));
        Assert.Equal("trip", rig.Report("t").GetProperty("evidence").GetProperty("line").GetString());
        for (var i = 0; i < 40000 && rig.Result("t").State == "running"; i++)
        {
            rig.DialFrame();
            rig.Orders.Tick();
            rig.Turn();
        }

        Assert.Equal(("done", "trip-done"), rig.Result("t"));
        Assert.Equal("lull", rig.Report("t").GetProperty("evidence").GetProperty("taken_at").GetString());
        Assert.Contains(rig.Events, e => e.Contains("t trip: done:trip-done", StringComparison.Ordinal));
    }

    [Fact]
    public void Command_orders_are_refused_by_name_without_the_orders_switch_and_the_switch_parses()
    {
        var rig = new DeskRig(output);
        rig.Drop("t", "command", "\"line\":\"trip\"");
        rig.Turn();
        Assert.Equal(("refused", "orders-off"), rig.Result("t"));
        Assert.True(HostOptions("--autopilot-steer-dir=cache/avatar/x", "--autopilot-steer-orders").SteerOrders);
        Assert.False(HostOptions("--autopilot-steer-dir=cache/avatar/x").SteerOrders);
        Assert.Single(OptionCatalog.Autopilot().Options, x => x.Name == "--autopilot-steer-orders");
    }

    [Fact]
    public void The_observe_option_parses_and_is_off_unless_asked_for()
    {
        Assert.True(HostOptions("--autopilot-steer-dir=cache/avatar/x", "--autopilot-steer-observe").SteerObserveOnly);
        Assert.False(HostOptions("--autopilot-steer-dir=cache/avatar/x").SteerObserveOnly);
        Assert.Single(OptionCatalog.Autopilot().Options, x => x.Name == "--autopilot-steer-observe");
    }

    [Fact]
    public void The_recent_sheet_holds_fifteen_minutes_and_works_out_the_changes_and_rates()
    {
        var rig = new DeskRig(output);
        var q = rig.View.State.Self.Qualities;
        var startXp = q.TotalExperience;
        for (var i = 0; i < 100; i++)
        {
            rig.Turn();
            rig.Advance(SteerHistory.SampleSeconds);
        }

        rig.Drop("r", "sheet", "\"section\":\"recent\"");
        rig.Turn();
        Assert.Equal(("done", "sheet:recent"), rig.Result("r"));
        using var sheet = JsonDocument.Parse(rig.Disk.Files["sheets/recent.json"]);
        var root = sheet.RootElement;
        Assert.Equal("recent", root.GetProperty("section").GetString());
        Assert.InRange(root.GetProperty("covered_s").GetDouble(), SteerHistory.WindowSeconds - 2 * SteerHistory.SampleSeconds, SteerHistory.WindowSeconds);
        Assert.InRange(root.GetProperty("samples").GetArrayLength(), 90, 92); // older ones have gone
        var change = root.GetProperty("change");
        Assert.Equal(0, change.GetProperty("xp").GetInt64());
        Assert.Equal(0.0, change.GetProperty("xp_per_hour").GetDouble());
        var first = root.GetProperty("samples")[0];
        Assert.Equal(startXp, first.GetProperty("xp").GetInt64());
        Assert.Equal("off", first.GetProperty("mode").GetString());
    }

    [Fact]
    public void A_short_recent_sheet_gives_no_rate_and_observe_only_still_answers_it()
    {
        var rig = new DeskRig(output, observeOnly: true);
        rig.Turn();
        rig.Drop("r", "sheet", "\"section\":\"recent\"");
        rig.Turn();
        Assert.Equal(("done", "sheet:recent"), rig.Result("r"));
        using var sheet = JsonDocument.Parse(rig.Disk.Files["sheets/recent.json"]);
        Assert.Equal(JsonValueKind.Null, sheet.RootElement.GetProperty("change").GetProperty("xp_per_hour").ValueKind);
    }

    private static AutopilotHostOptions HostOptions(params string[] args) => AutopilotHostOptions.FromArguments(CommandLineArgument.ParseAll(args));

    private sealed class FakeCamera : ISteerCamera
    {
        public List<string> Taken { get; } = [];

        public bool NoFrame { get; set; }

        public SteerShot? Last { get; private set; }

        public SteerShot? Take(string name)
        {
            Taken.Add(name);
            return NoFrame ? null : Last = new SteerShot(4.5, 16.25, 800, 600);
        }
    }

    [Fact]
    public void A_capture_reports_when_the_file_is_saved_and_the_frame_that_paid_for_it_is_known()
    {
        var camera = new FakeCamera();
        var rig = new DeskRig(output, camera: camera);
        rig.Drop("c", "capture", "\"name\":\"look-1\"");
        rig.Turn();
        Assert.Equal(["look-1"], camera.Taken);
        Assert.Equal(("running", "saving"), rig.Result("c"));

        camera.Last!.MarkSaved();
        rig.Turn();
        Assert.Equal(("running", "saving"), rig.Result("c")); // the frame is not known yet

        camera.Last.FrameMilliseconds = 21.5;
        rig.Turn();
        Assert.Equal(("done", "shot:look-1"), rig.Result("c"));
        var report = rig.Disk.Files["outbox/c.json"];
        Assert.Contains("shots/look-1.png", report, StringComparison.Ordinal);
        Assert.Contains("readback_ms", report, StringComparison.Ordinal);
        Assert.Contains("frame_ms", report, StringComparison.Ordinal);
    }

    [Fact]
    public void A_capture_works_in_observe_only_and_a_failed_save_is_named()
    {
        var camera = new FakeCamera();
        var rig = new DeskRig(output, observeOnly: true, camera: camera);
        rig.Drop("c", "capture", "\"name\":\"look-1\"");
        rig.Turn();
        camera.Last!.FrameMilliseconds = 18;
        camera.Last.MarkFailed("the disk is full");
        rig.Turn();
        Assert.Equal(("failed", "not-saved"), rig.Result("c"));
    }

    [Fact]
    public void Captures_are_capped_one_at_a_time_and_a_dry_run_reads_nothing()
    {
        var camera = new FakeCamera();
        var rig = new DeskRig(output, camera: camera);
        rig.Drop("dry", "capture", "\"name\":\"d\"", extra: "\"dry_run\":true,");
        rig.Turn();
        Assert.Equal(("checked", "dry-run"), rig.Result("dry"));
        Assert.Empty(camera.Taken);

        rig.Drop("a", "capture", "\"name\":\"a\"", sequence: 2);
        rig.Turn();
        rig.Drop("b", "capture", "\"name\":\"b\"", sequence: 3);
        rig.Turn();
        Assert.Equal(("refused", "busy"), rig.Result("b"));

        camera.Last!.FrameMilliseconds = 18;
        camera.Last.MarkSaved();
        rig.Turn();
        rig.Advance(OrderDesk.MinCaptureSeconds / 2);
        rig.Drop("c", "capture", "\"name\":\"c\"", sequence: 4);
        rig.Turn();
        Assert.Equal(("refused", "too-soon"), rig.Result("c"));
        Assert.Equal(["a"], camera.Taken);

        rig.Advance(OrderDesk.MinCaptureSeconds);
        rig.Drop("e", "capture", "\"name\":\"e\"", sequence: 5);
        rig.Turn();
        Assert.Equal(("running", "saving"), rig.Result("e"));
    }

    [Theory]
    [InlineData("\"name\":\"../up\"", "bad-args")]
    [InlineData("\"section\":\"x\"", "bad-args")]
    public void A_capture_needs_a_name_that_can_be_a_file(string args, string name)
    {
        var camera = new FakeCamera();
        var rig = new DeskRig(output, camera: camera);
        rig.Drop("c", "capture", args);
        rig.Turn();
        Assert.Equal(("refused", name), rig.Result("c"));
        Assert.Empty(camera.Taken);
    }

    [Fact]
    public void A_capture_with_no_camera_or_no_frame_is_named()
    {
        var bare = new DeskRig(output);
        bare.Drop("c", "capture", "\"name\":\"a\"");
        bare.Turn();
        Assert.Equal(("refused", "no-camera"), bare.Result("c"));

        var rig = new DeskRig(output, camera: new FakeCamera { NoFrame = true });
        rig.Drop("c", "capture", "\"name\":\"a\"");
        rig.Turn();
        Assert.Equal(("failed", "no-frame"), rig.Result("c"));
    }

    [Fact]
    public void A_duplicate_leaves_the_earlier_report_alone_and_is_taken()
    {
        var rig = new DeskRig(output);
        rig.Drop("p", "ping", guard: string.Empty);
        rig.Turn();
        var first = rig.Disk.Files["outbox/p.json"];
        rig.Advance(1);
        rig.Drop("p", "ping", guard: string.Empty, sequence: 2);
        rig.Turn();
        Assert.Equal(first, rig.Disk.Files["outbox/p.json"]);
        Assert.Equal(["1-p.json", "2-p.json"], rig.Disk.Taken);
        Assert.Contains(rig.Events, e => e.Contains("duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void A_refused_file_gets_a_report_and_a_file_with_no_id_gets_an_event()
    {
        var rig = new DeskRig(output);
        rig.Disk.Drop("1-torn.json", "{", rig.View.Clock.Time.GetUtcNow());
        rig.Disk.Drop("nonsense.json", "{}", rig.View.Clock.Time.GetUtcNow());
        rig.Turn();
        Assert.Equal(("refused", "bad-json"), rig.Result("torn"));
        Assert.Equal(["1-torn.json", "nonsense.json"], rig.Disk.Taken);
        Assert.Contains(rig.Events, e => e.Contains("bad-file-name", StringComparison.Ordinal));
    }

    [Fact]
    public void A_backlog_is_answered_a_few_orders_a_frame()
    {
        var rig = new DeskRig(output);
        for (var i = 1; i <= OrderDesk.MaxOrdersPerFrame + 3; i++)
        {
            rig.Drop("p" + i, "ping", guard: string.Empty, sequence: i);
        }

        rig.Turn();
        Assert.Equal(OrderDesk.MaxOrdersPerFrame, rig.Desk.Answered);
        rig.Turn();
        Assert.Equal(OrderDesk.MaxOrdersPerFrame + 3, rig.Desk.Answered);
    }

    [Fact]
    public void The_live_picture_has_the_contracts_fields()
    {
        var rig = new DeskRig(output);
        rig.Turn();
        var status = rig.Status;
        foreach (var field in new[] { "v", "at_utc", "seq", "run", "build", "character", "session", "dial", "place", "level", "xp", "vitae", "vitals", "rung", "stock", "burden", "free_slots", "pyreals", "buffs", "overlay", "desk" })
        {
            Assert.True(status.TryGetProperty(field, out _), field);
        }

        Assert.Equal(("run-1", "test-build"), (status.GetProperty("run").GetString(), status.GetProperty("build").GetString()));
        Assert.Equal("0x50000A12", status.GetProperty("character").GetProperty("guid").GetString());
        Assert.Equal("off", status.GetProperty("dial").GetProperty("mode").GetString());
        Assert.True(status.GetProperty("desk").GetProperty("open").GetBoolean());
        Assert.Equal(JsonValueKind.Array, status.GetProperty("vitals").GetProperty("health").ValueKind);
    }

    [Fact]
    public void The_live_picture_is_written_every_2_s_when_nothing_changes()
    {
        var rig = new DeskRig(output);
        rig.Turn();
        var first = rig.Status.GetProperty("seq").GetInt64();
        rig.Advance(0.5);
        rig.Turn();
        Assert.Equal(first, rig.Status.GetProperty("seq").GetInt64());
        rig.Advance(OrderDesk.StatusSeconds);
        rig.Turn();
        Assert.True(rig.Status.GetProperty("seq").GetInt64() > first);
    }

    [Fact]
    public void A_started_run_and_a_level_rise_become_stamped_events()
    {
        var rig = new DeskRig(output);
        rig.View.State.Self.Qualities.Ints[PlayerQualities.LevelProperty] = 5;
        rig.Turn();
        Assert.True(rig.Logic.Start(rig.View));
        rig.View.State.Self.Qualities.Ints[PlayerQualities.LevelProperty] = 6;
        rig.Turn();
        var events = rig.Events.Select(e => JsonDocument.Parse(e).RootElement).ToList();
        Assert.Contains(events, e => e.GetProperty("kind").GetString() == "state" && e.GetProperty("text").GetString()!.StartsWith("running", StringComparison.Ordinal));
        Assert.Contains(events, e => e.GetProperty("kind").GetString() == "level");
        foreach (var e in events)
        {
            foreach (var field in new[] { "seq", "at_utc", "kind", "text", "level", "ground", "cycle" })
            {
                Assert.True(e.TryGetProperty(field, out _), field);
            }
        }

        Assert.Equal(Enumerable.Range(1, events.Count).Select(i => (long)i), events.Select(e => e.GetProperty("seq").GetInt64()));
    }

    [Fact]
    public void A_stream_read_the_first_time_makes_no_level_event_and_a_new_scenario_makes_no_tally_events()
    {
        var rig = new DeskRig(output);
        rig.Turn();
        Assert.DoesNotContain(rig.Events, e => e.Contains("\"kind\":\"level\"", StringComparison.Ordinal));
        Assert.True(rig.Logic.Start(rig.View));
        rig.Logic.Scenario!.CycleTally.Deaths = 2;
        rig.Turn();
        Assert.DoesNotContain(rig.Events, e => e.Contains("\"kind\":\"death\"", StringComparison.Ordinal));
        rig.Logic.Scenario!.CycleTally.Deaths = 3;
        rig.Turn();
        Assert.Contains(rig.Events, e => e.Contains("\"kind\":\"death\"", StringComparison.Ordinal) && e.Contains("2 to 3", StringComparison.Ordinal));
    }

    [Fact]
    public void A_spool_that_gives_up_closes_the_desk_and_the_dial_is_untouched()
    {
        var rig = new DeskRig(output);
        rig.Turn();
        rig.Disk.WriteFails = _ => true;
        rig.Spool.Post(new SteerWrite(SteerWriteKind.Replace, "x", "1"));
        rig.Spool.Step(1000);
        rig.Spool.Step(1000 + SteerSpool.GiveUpSeconds);
        rig.Desk.Tick(rig.View, rig.Logic);
        Assert.False(rig.Desk.Open);
        Assert.Contains("failed for", rig.Desk.ClosedWhy, StringComparison.Ordinal);
        Assert.Equal(AutopilotMode.Off, rig.Logic.Mode);

        var answered = rig.Desk.Answered;
        rig.Disk.WriteFails = _ => false;
        rig.Drop("late", "ping", guard: string.Empty);
        rig.Turn();
        Assert.Equal(answered, rig.Desk.Answered);
    }

    [Fact]
    public void Closing_says_so_in_the_events_and_the_last_picture()
    {
        var rig = new DeskRig(output);
        rig.Turn();
        rig.Desk.Close("the game is closing", rig.View, fault: false);
        rig.Spool.Step(10_000);
        Assert.False(rig.Status.GetProperty("desk").GetProperty("open").GetBoolean());
        Assert.Contains(rig.Events, e => e.Contains("\"kind\":\"notice\"", StringComparison.Ordinal) && e.Contains("desk-closed: the game is closing", StringComparison.Ordinal));
    }

    [Fact]
    public void The_desk_opens_only_against_a_server_on_this_computer()
    {
        var rig = new DeskRig(output);
        Assert.True(OrderDesk.ServerIsLocal(rig.View));
        Assert.False(OrderDesk.ServerIsLocal(new NotLocalView(rig.View)));
    }

    /// <summary>A view whose action surface is not the real one, so the loopback check cannot say yes.</summary>
    private sealed class NotLocalView(IGameSessionView inner) : IGameSessionView
    {
        public GameState State => inner.State;

        public IEventBus Events => inner.Events;

        public IActionApi Actions { get; } = DispatchProxy.Create<IActionApi, NoCalls>();

        public PlayerTools Tools => inner.Tools;

        public SessionClock Clock => inner.Clock;

        public ActionLog ActionLog => inner.ActionLog;

        public LinkStats Link => inner.Link;
    }

    /// <summary>An action surface that refuses every call.</summary>
    public class NoCalls : DispatchProxy
    {
        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new InvalidOperationException("no calls");
    }

    [Fact]
    public void Start_runs_the_dial_as_an_unattended_run_and_is_refused_when_it_already_runs()
    {
        var rig = new DeskRig(output);
        rig.Drop("s", SteerKinds.Start);
        rig.Turn();
        Assert.Equal(("done", "running"), rig.Result("s"));
        Assert.Equal(AutopilotMode.Running, rig.Logic.Mode);
        Assert.True(rig.Logic.StartedByOption);

        rig.Drop("again", SteerKinds.Start, sequence: 2);
        rig.Turn();
        Assert.Equal(("refused", "wrong-mode"), rig.Result("again"));
    }

    [Fact]
    public void A_start_that_never_runs_fails_by_name_when_its_life_ends()
    {
        var rig = new DeskRig(output, press: _ => { });
        rig.Drop("s", SteerKinds.Start, expires: 5);
        rig.Turn();
        Assert.Equal(("running", "asked"), rig.Result("s"));
        rig.Advance(6);
        rig.Turn();
        Assert.Equal(("failed", "not-started"), rig.Result("s"));
    }

    [Fact]
    public void Stop_home_stays_running_until_the_run_ends_by_that_request_and_keeps_the_game_open()
    {
        var rig = new DeskRig(output);
        rig.Drop("s", SteerKinds.Start);
        rig.Turn();
        Assert.True(rig.RunUntil(() => rig.Fighting));

        rig.Drop("h", SteerKinds.StopHome, sequence: 2);
        rig.Turn();
        Assert.Equal(("running", "asked"), rig.Result("h"));
        Assert.Equal(AutopilotMode.Stopping, rig.Logic.Mode);
        Assert.Equal("h", rig.Status.GetProperty("desk").GetProperty("running").GetString() ?? string.Empty);

        Assert.True(rig.RunUntil(() => rig.Logic.Mode == AutopilotMode.Done));
        rig.Turn();
        Assert.Equal(("done", "request:steer-h"), rig.Result("h"));
        Assert.Null(rig.Logic.UnattendedEnd);
    }

    [Fact]
    public void Stop_here_then_resume_works_for_orders()
    {
        var rig = new DeskRig(output);
        rig.Drop("s", SteerKinds.Start);
        rig.Turn();
        Assert.True(rig.RunUntil(() => rig.Fighting));

        rig.Drop("h", SteerKinds.StopHere, sequence: 2);
        rig.Turn();
        Assert.Equal(("done", "interrupted"), rig.Result("h"));
        Assert.Equal(AutopilotMode.Interrupted, rig.Logic.Mode);

        rig.Drop("r", SteerKinds.Resume, sequence: 3);
        rig.Turn();
        Assert.Equal(("done", "running"), rig.Result("r"));
        Assert.Equal(AutopilotMode.Running, rig.Logic.Mode);
    }

    [Fact]
    public void A_person_wins_their_stop_refuses_every_order_that_would_move_the_character_until_their_own_press()
    {
        var rig = new DeskRig(output);
        rig.Logic.Toggle(rig.View, "steer");
        Assert.True(rig.RunUntil(() => rig.Fighting));
        rig.Logic.StopHere(rig.View);

        var n = 1;
        foreach (var kind in new[] { SteerKinds.Resume, SteerKinds.Start, SteerKinds.StopHome, SteerKinds.StopHere, SteerKinds.Leave })
        {
            rig.Drop("k" + n, kind, sequence: n);
            rig.Turn();
            Assert.Equal(("refused", "person-stopped"), rig.Result("k" + n));
            n++;
        }

        Assert.Equal(AutopilotMode.Interrupted, rig.Logic.Mode);
        rig.Drop("p", SteerKinds.Ping, guard: string.Empty, sequence: n++);
        rig.Turn();
        Assert.Equal(("done", "pong"), rig.Result("p")); // probes still work
        Assert.True(rig.Status.GetProperty("dial").GetProperty("person_hold").GetBoolean());

        rig.Logic.PersonPressed(); // their /auto
        rig.Logic.Toggle(rig.View);
        rig.Drop("h", SteerKinds.StopHere, sequence: n);
        rig.Turn();
        Assert.Equal(("done", "interrupted"), rig.Result("h"));
    }

    [Fact]
    public void A_stop_typed_while_off_blocks_a_later_start()
    {
        var rig = new DeskRig(output);
        rig.Logic.StopHere(rig.View);
        rig.Drop("s", SteerKinds.Start);
        rig.Turn();
        Assert.Equal(("refused", "person-stopped"), rig.Result("s"));
        Assert.Equal(AutopilotMode.Off, rig.Logic.Mode);
    }

    [Fact]
    public void The_kill_switch_refuses_every_order_that_would_move_the_character_and_probes_still_work()
    {
        var rig = new DeskRig(output);
        rig.View.Actions.KillSwitch = true;
        rig.Drop("s", SteerKinds.Start);
        rig.Drop("l", SteerKinds.Leave, sequence: 2);
        rig.Drop("p", SteerKinds.Ping, guard: string.Empty, sequence: 3);
        rig.Turn();
        Assert.Equal(("refused", "kill-switch"), rig.Result("s"));
        Assert.Equal(("refused", "kill-switch"), rig.Result("l"));
        Assert.Equal(("done", "pong"), rig.Result("p"));
        Assert.Equal(AutopilotMode.Off, rig.Logic.Mode);
        Assert.True(rig.Status.GetProperty("dial").GetProperty("kill_switch").GetBoolean());
        rig.View.Actions.KillSwitch = false;
    }

    private static readonly string[] Moving = [SteerKinds.Start, SteerKinds.Resume, SteerKinds.StopHome, SteerKinds.StopHere, SteerKinds.Leave];

    [Theory]
    [InlineData("off", "start leave")]
    [InlineData("running", "stop_home stop_here leave")]
    [InlineData("stopping", "stop_here")]
    [InlineData("interrupted", "resume")]
    [InlineData("done", "start leave")]
    public void Each_control_is_legal_in_exactly_the_contracts_modes(string mode, string legal)
    {
        var rig = new DeskRig(output);
        if (mode != "off")
        {
            rig.Logic.Toggle(rig.View, OrderDesk.StartedBy);
            Assert.True(rig.RunUntil(() => rig.Fighting));
        }

        switch (mode)
        {
            case "stopping":
                rig.Logic.Stop("x");
                break;
            case "interrupted":
                Assert.True(rig.Logic.StopHereByOrder(rig.View, "x"));
                break;
            case "done":
                rig.Logic.Stop("x");
                Assert.True(rig.RunUntil(() => rig.Logic.Mode == AutopilotMode.Done));
                break;
        }

        var n = 1;
        foreach (var kind in Moving)
        {
            rig.Drop("k" + n, kind, extra: "\"dry_run\":true,", sequence: n);
            rig.Turn();
            var expected = legal.Split(' ').Contains(kind) ? ("checked", "dry-run") : ("refused", "wrong-mode");
            Assert.True(expected == rig.Result("k" + n), $"{kind} while {mode}: {rig.Result("k" + n)}");
            n++;
        }
    }

    [Fact]
    public void A_start_after_a_leave_is_refused_so_the_way_out_stands()
    {
        var rig = new DeskRig(output);
        rig.Drop("l", SteerKinds.Leave);
        rig.Drop("s", SteerKinds.Start, sequence: 2);
        rig.Turn();
        Assert.Equal(("done", "leaving"), rig.Result("l"));
        Assert.Equal(("refused", "leaving"), rig.Result("s"));
        Assert.NotNull(rig.Logic.UnattendedEnd);
    }

    [Fact]
    public void A_start_that_ends_its_life_takes_back_its_waiting_press()
    {
        var withdrawn = 0;
        var rig = new DeskRig(output, press: _ => { }, withdraw: () => withdrawn++);
        rig.Drop("s", SteerKinds.Start, expires: 5);
        rig.Turn();
        Assert.Equal(0, withdrawn);
        rig.Advance(6);
        rig.Turn();
        Assert.Equal(1, withdrawn);
    }

    [Fact]
    public void A_resume_that_brings_a_standing_stop_back_with_it_still_ends()
    {
        var rig = new DeskRig(output);
        rig.Logic.Toggle(rig.View, OrderDesk.StartedBy);
        Assert.True(rig.RunUntil(() => rig.Fighting));
        rig.Logic.Stop("x");
        Assert.True(rig.Logic.StopHereByOrder(rig.View, "y"));
        rig.Drop("r", SteerKinds.Resume);
        rig.Turn();
        Assert.Equal(AutopilotMode.Stopping, rig.Logic.Mode);
        Assert.Equal(("done", "running"), rig.Result("r"));
    }

    [Fact]
    public void Leave_while_off_sets_the_end_and_reports_it_and_leave_while_running_waits_for_it()
    {
        var off = new DeskRig(output);
        off.Drop("l", SteerKinds.Leave);
        off.Turn();
        Assert.Equal(("done", "leaving"), off.Result("l"));
        Assert.Equal(0, off.Logic.UnattendedEnd!.ExitCode);

        var running = new DeskRig(output);
        running.Logic.Toggle(running.View);
        Assert.True(running.RunUntil(() => running.Fighting));
        running.Drop("l", SteerKinds.Leave);
        running.Turn();
        Assert.Equal(("running", "asked"), running.Result("l"));
        Assert.True(running.RunUntil(() => running.Logic.Mode == AutopilotMode.Done));
        running.Turn();
        Assert.Equal(("done", "leaving"), running.Result("l"));
    }

    [Fact]
    public void A_dry_run_of_a_control_order_acts_on_nothing()
    {
        var rig = new DeskRig(output);
        rig.Drop("s", SteerKinds.Start, extra: "\"dry_run\":true,");
        rig.Drop("l", SteerKinds.Leave, extra: "\"dry_run\":true,", sequence: 2);
        rig.Turn();
        Assert.Equal(("checked", "dry-run"), rig.Result("s"));
        Assert.Equal(("checked", "dry-run"), rig.Result("l"));
        Assert.Equal(AutopilotMode.Off, rig.Logic.Mode);
        Assert.Null(rig.Logic.UnattendedEnd);
    }

    [Fact]
    public void Cancel_names_what_it_could_not_take_off_the_desk()
    {
        var rig = new DeskRig(output);
        rig.Drop("p", SteerKinds.Ping, guard: string.Empty);
        rig.Turn();
        rig.Drop("c1", SteerKinds.Cancel, "\"target\":\"p\"", sequence: 2);
        rig.Drop("c2", SteerKinds.Cancel, "\"target\":\"nope\"", sequence: 3);
        rig.Drop("c3", SteerKinds.Cancel, sequence: 4);
        rig.Turn();
        Assert.Equal(("refused", "too-late"), rig.Result("c1"));
        Assert.Equal(("refused", "unknown-order"), rig.Result("c2"));
        Assert.Equal(("refused", "bad-args"), rig.Result("c3"));
    }

    [Fact]
    public void One_torn_or_malformed_report_never_stops_the_other_unfinished_orders_being_answered()
    {
        var disk = new FakeDisk();
        disk.Files["outbox/a-torn.json"] = "{";
        disk.Files["outbox/b-number.json"] = "{\"state\":7}";
        disk.Files["outbox/c.json"] = new SteerReport("c", "leave", SteerState.Running, "asked", string.Empty, Issued, 1, null, 0).ToJson();
        var spool = NewSpool(disk);
        spool.Step(0);
        Assert.Contains("\"name\":\"restarted\"", disk.Files["outbox/c.json"], StringComparison.Ordinal);
        Assert.Null(spool.Fault);
    }

    [Fact]
    public void Nothing_is_read_from_the_inbox_until_the_unfinished_orders_are_answered()
    {
        var disk = new FakeDisk();
        disk.Files["outbox/a.json"] = new SteerReport("a", "leave", SteerState.Running, "asked", string.Empty, Issued, 1, null, 0).ToJson();
        disk.Drop("1-b.json", Envelope("b"));
        disk.WriteFails = op => op == "replace outbox/a.json";
        var spool = NewSpool(disk);
        spool.Step(0);
        Assert.Empty(Taken(spool));
        disk.WriteFails = _ => false;
        spool.Step(5);
        Assert.Single(Taken(spool));
    }

    [Fact]
    public void After_a_restart_every_unfinished_order_answers_restarted_and_a_finished_one_is_left_alone()
    {
        var disk = new FakeDisk();
        disk.Files["outbox/a.json"] = new SteerReport("a", "stop_home", SteerState.Running, "asked", string.Empty, Issued, 1, null, 0).ToJson();
        var finished = new SteerReport("b", "ping", SteerState.Done, "pong", string.Empty, Issued, 1, null, 0).ToJson();
        disk.Files["outbox/b.json"] = finished;
        NewSpool(disk).Step(0);
        using var a = JsonDocument.Parse(disk.Files["outbox/a.json"]);
        Assert.Equal(("refused", "restarted", "stop_home"), (a.RootElement.GetProperty("state").GetString(), a.RootElement.GetProperty("name").GetString(), a.RootElement.GetProperty("kind").GetString()));
        Assert.Equal(finished, disk.Files["outbox/b.json"]);
    }

    private static readonly Dictionary<string, string> OverlayRules = new(StringComparer.Ordinal) { ["hold_rung"] = "\"cave\"", ["deny"] = "[\"x\"]" };

    [Fact]
    public void The_overlay_round_trips_and_keeps_rules_this_build_does_not_act_on()
    {
        var text = new SteerOverlay(7, DeskRig.Self, OverlayRules).ToJson();
        var back = SteerOverlay.Parse(text, DeskRig.Self, out var problem);
        Assert.Null(problem);
        Assert.Equal(7, back!.Version);
        Assert.Equal(OverlayRules, back.Rules);
        Assert.Equal(text, back.ToJson());
    }

    [Theory]
    [InlineData("{", "not readable")]
    [InlineData("{\"v\":2,\"version\":1,\"character_guid\":\"0x50000A12\"}", "schema")]
    [InlineData("{\"v\":1,\"character_guid\":\"0x50000A12\"}", "no version")]
    [InlineData("{\"v\":1,\"version\":1,\"character_guid\":\"0x50000001\"}", "another character")]
    public void An_overlay_that_cannot_be_used_is_named(string text, string why)
    {
        Assert.Null(SteerOverlay.Parse(text, DeskRig.Self, out var problem));
        Assert.Contains(why, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_desk_loads_the_overlay_shows_its_version_and_says_when_a_run_seated_it()
    {
        var rig = new DeskRig(output, overlayText: new SteerOverlay(7, DeskRig.Self, OverlayRules).ToJson());
        rig.Turn();
        Assert.Equal(7, rig.Status.GetProperty("overlay").GetProperty("version").GetInt32());
        Assert.Equal(JsonValueKind.Null, rig.Status.GetProperty("dial").GetProperty("seated_overlay").ValueKind);

        rig.Drop("s", SteerKinds.Start);
        rig.Turn();
        rig.Advance(3);
        rig.Turn();
        Assert.Equal(7, rig.Status.GetProperty("dial").GetProperty("seated_overlay").GetInt32());
    }

    [Fact]
    public void Another_characters_overlay_is_set_aside_and_the_rules_start_empty()
    {
        var rig = new DeskRig(output, overlayText: new SteerOverlay(7, 0x50000001, OverlayRules).ToJson());
        rig.Disk.Files[SteerOverlay.FileName] = "old";
        rig.Turn();
        Assert.Contains("aside overlay.json", rig.Disk.Ops);
        Assert.True(rig.Disk.Files.ContainsKey(SteerOverlay.FileName + ".aside"));
        Assert.Equal(0, rig.Status.GetProperty("overlay").GetProperty("version").GetInt32());
        Assert.Contains(rig.Events, e => e.Contains("overlay set aside", StringComparison.Ordinal));
    }

    [Fact]
    public void The_host_source_has_the_stop_file_leave_an_idle_client_only_with_an_open_desk()
    {
        var unattended = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.Unattended.cs"));
        Assert.Contains("else if (_desk is { Open: true } && _logic.Mode is AutopilotMode.Off or AutopilotMode.Done)", unattended, StringComparison.Ordinal);
        Assert.Contains("_logic.Leave(AutopilotHostLogic.StopFileReason);", unattended, StringComparison.Ordinal);
    }

    [Fact]
    public void The_option_parses_a_folder_an_empty_one_is_an_error_and_absent_means_no_desk()
    {
        static AutopilotHostOptions Parse(params string[] args) => AutopilotHostOptions.FromArguments(CommandLineArgument.ParseAll(args));
        var o = Parse("--autopilot-steer-dir=cache/avatar/run-1");
        Assert.Empty(o.Errors);
        Assert.Equal("cache/avatar/run-1", o.SteerDirectory);
        Assert.Contains("steer_dir=on", o.ToString(), StringComparison.Ordinal);
        Assert.Null(Parse().SteerDirectory);
        Assert.Contains("steer_dir=off", Parse().ToString(), StringComparison.Ordinal);
        Assert.StartsWith("--autopilot-steer-dir:", Assert.Single(Parse("--autopilot-steer-dir=").Errors), StringComparison.Ordinal);
        Assert.Equal("DIR", Assert.Single(OptionCatalog.Autopilot().Options, x => x.Name == "--autopilot-steer-dir").Value);
    }

    [Fact]
    public void The_host_ticks_the_desk_between_the_stop_file_and_the_dial_and_does_nothing_without_the_option()
    {
        var host = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.cs"));
        var steer = File.ReadAllText(RepositoryFiles.PathOf("src", "Lifestone.Game", "scripts", "Session", "AutopilotHost.Steer.cs"));
        var (stop, desk, dial) = (host.IndexOf("TickStopFile(delta);", StringComparison.Ordinal), host.IndexOf("TickSteer(session, delta);", StringComparison.Ordinal), host.IndexOf("_logic.Tick(session,", StringComparison.Ordinal));
        Assert.True(stop > 0 && stop < desk && desk < dial, $"stop file {stop}, desk {desk}, dial {dial}");
        Assert.Contains("if (hostOptions.SteerDirectory is not { } path)", steer, StringComparison.Ordinal);
        var tick = steer.IndexOf("private void TickSteer(", StringComparison.Ordinal);
        Assert.True(steer.IndexOf("if (_steerFolder is null)", tick, StringComparison.Ordinal) < steer.IndexOf("try", tick, StringComparison.Ordinal), "the option's absence returns before anything else");
        Assert.Contains("catch (Exception e) when (LegFaultGuard.Guards(e))", steer[tick..], StringComparison.Ordinal); // rule 7: its own try, so a desk throw cannot dispose the dial
        Assert.Contains("IsBackground = true", steer, StringComparison.Ordinal);
    }
}
