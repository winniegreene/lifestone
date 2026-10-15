using System.Globalization;
using System.Net;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Interiors;
using Lifestone.Client.Navigation;
using Lifestone.Client.RetailUi.LifestonePanels;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Client.Session.Tools;
using Lifestone.Client.Tests.Session;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Data.World.Data;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.RetailUi.LifestonePanelTests;

/// <summary>
/// The tools panel's logic (G7 lane A; docs/retail-ui.md section 22.1) with no element in sight: the Places search, the request a pick builds for an ACE row and for a curated row, Where am I
/// with the client's position, Add XP, the chat and action-log trail of every use, and the two refusals (a server that is not on loopback, a server without the mod). Over the offline stand-in
/// session that answers the world (a labelled placeholder) and over the Action API's test harness.
/// </summary>
public sealed class ToolsModelTests
{
    private const uint Holtburg = 0xA9B40019;

    private static readonly StandInVisit Visit = new() { EnterAfter = 0.2, UnhideAfter = 1.0, LogOffDelay = 1.0 };

    private static List<ChatLine> Lines(GameSession session)
    {
        var lines = new List<ChatLine>();
        for (var i = 0; i < session.State.Chat.Count; i++)
        {
            lines.Add(session.State.Chat[i]);
        }

        return lines;
    }

    /// <summary>The model over an offline stand-in session in the world, with a recording chat.</summary>
    private sealed class Rig : IDisposable
    {
        private readonly ManualTimeProvider _time = new();
        private CellFrame? _client;

        public Rig()
        {
            Session = GameSession.Offline(
                h =>
                {
                    StandInSessions.CharacterSelect(h);
                    StandInSessions.PlayerVisits(h, Visit);
                    StandInSessions.AnswerWorld(h, Visit);
                },
                _time,
                new RecordingLogSink());
            Run(2.0); // the visit enters at 0.2 s and unhides at 1.2 s
            Model = new ToolsModel(Session.Tools, () => _client ?? Session.State.Self.Position, Chat.Add, logError: Errors.Add); // the test double for the physics body: the stand-in server answers with this same position
        }

        public GameSession Session { get; }

        public ToolsModel Model { get; }

        public List<string> Chat { get; } = [];

        public List<string> Errors { get; } = [];

        public void ClientIsAt(CellFrame? position) => _client = position;

        public void Run(double seconds)
        {
            for (var t = 0.0; t < seconds; t += 0.1)
            {
                _time.Advance(TimeSpan.FromSeconds(0.1));
                Session.Tick(Session.Clock.Now);
            }
        }

        public void Dispose() => Session.Dispose();
    }

    [Fact]
    public void Search_lists_ACE_s_points_and_the_curated_places_best_match_first()
    {
        using var rig = new Rig();
        var model = rig.Model;

        Assert.Equal(TeleportCatalog.Default().Destinations.Count, model.Results.Count);
        Assert.True(model.Results.Count > AcePointsOfInterest.All.Count, "ACE's points of interest and the curated places are all listed by default");
        Assert.Equal(ToolsModel.PageSize, model.Page.Count);
        Assert.Equal("1-8 of " + model.Results.Count.ToString(CultureInfo.InvariantCulture), model.Counter);

        model.SetQuery("holtburg");
        Assert.Equal("Holtburg", model.Results[0].Name);
        Assert.Equal(0, model.Top);
        Assert.Contains(model.Results, d => d.Kind == DestinationKind.Indoor); // the shop and the Redoubt are Holtburg places too

        model.SetQuery("dungeon");
        Assert.NotEmpty(model.Results);
        Assert.All(model.Results, d => Assert.Equal(DestinationKind.Indoor, d.Kind));

        model.SetQuery("zzzzqq");
        Assert.Empty(model.Results);
        Assert.Equal("no places match", model.Counter);
        Assert.Empty(model.Page);
    }

    [Fact]
    public void Scroll_moves_a_page_at_a_time_and_stops_at_the_ends()
    {
        using var rig = new Rig();
        var model = rig.Model;
        var total = model.Results.Count;
        var changes = 0;
        model.Changed += () => changes++;

        Assert.False(model.Scroll(down: false));
        Assert.True(model.Scroll(down: true));
        Assert.Equal(ToolsModel.PageSize, model.Top);
        while (model.Scroll(down: true))
        {
        }

        Assert.Equal(total - ToolsModel.PageSize, model.Top);
        Assert.Equal(ToolsModel.PageSize, model.Page.Count);
        Assert.True(model.Scroll(down: false));
        Assert.True(changes >= 2);
    }

    [Fact]
    public void A_pick_of_an_ACE_row_asks_for_the_poi_by_name_and_a_curated_row_by_location()
    {
        using var rig = new Rig();
        var model = rig.Model;

        model.SetQuery("holtburg");
        var ace = model.Page[0];
        Assert.True(PlaceRequests.TryFindAcePoint(ace, out var poi));
        var byName = model.Teleport(ace);
        rig.Run(1.0);
        Assert.Equal(("poi", ace.Name, ActionOutcomeKind.Confirmed), (byName.Result!.Kind, byName.Result.Name, byName.Outcome!.Value.Kind));
        Assert.Equal(poi.CellId, byName.Result.Destination.CellId);
        Assert.Contains(Lines(rig.Session), l => l.Text == "Lifestone: teleporting to " + ace.Label + "...");

        var curated = TeleportCatalog.FromInterior(InteriorLocations.HoltburgRedoubt);
        Assert.False(PlaceRequests.TryFindAcePoint(curated, out _));
        var byLocation = model.Teleport(curated);
        rig.Run(1.0);
        Assert.Equal(("loc", null, ActionOutcomeKind.Confirmed), (byLocation.Result!.Kind, byLocation.Result.Name, byLocation.Outcome!.Value.Kind));
        Assert.Equal(InteriorLocations.HoltburgRedoubt.Location.Position.CellId, byLocation.Result.Destination.CellId);
        Assert.Equal(curated.Location.Position.CellId, rig.Session.State.Self.Position!.Value.CellId);
        Assert.StartsWith("Teleport started: ", model.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_use_is_written_to_the_chat_and_the_action_log_as_a_UI_action()
    {
        using var rig = new Rig();
        var model = rig.Model;
        model.SetQuery("holtburg");

        model.Teleport(model.Page[0]);
        rig.Run(1.0);
        model.WhereAmI();
        rig.Run(1.0);
        model.AddXp("500");
        rig.Run(1.0);

        var chat = Lines(rig.Session);
        Assert.Contains(chat, l => l.Text.StartsWith("Lifestone: teleporting to Holtburg", StringComparison.Ordinal) && l.Kind == ChatKind.Notice);
        Assert.Contains(chat, l => l.Text.StartsWith("Lifestone: asking the server where you are", StringComparison.Ordinal));
        Assert.Contains(chat, l => l.Text.StartsWith("Lifestone: asking for 500 experience", StringComparison.Ordinal));
        Assert.Equal(3, chat.Count(l => l.Kind == ChatKind.ToolReply)); // the mod's reply line of each
        var log = rig.Session.ActionLog;
        var uses = Enumerable.Range(0, log.Count).Select(i => log[i]).Where(e => e.Kind is ActionKind.ToolPlace or ActionKind.ToolWhereAmI or ActionKind.ToolAddXp).ToArray();
        Assert.Equal([ActionKind.ToolPlace, ActionKind.ToolWhereAmI, ActionKind.ToolAddXp], uses.Select(e => e.Kind).ToArray());
        Assert.All(uses, e => Assert.Equal((ActionSource.Ui, ActionOutcomeKind.Confirmed), (e.Source, e.Outcome.Kind)));
    }

    [Fact]
    public void Where_am_I_shows_the_servers_position_and_whether_the_client_agrees()
    {
        using var rig = new Rig();
        var model = rig.Model;
        var client = rig.Session.State.Self.Position!.Value;

        // The stand-in answers with the session's own position as the server's (a placeholder), so the client agrees.
        var agreeing = model.WhereAmI();
        Assert.Equal("asking the server...", model.WhereText);
        rig.Run(1.0);
        Assert.Equal(ActionOutcomeKind.Confirmed, agreeing.Outcome!.Value.Kind);
        Assert.Equal(client.CellId, agreeing.Result!.Position.CellId);
        Assert.True(model.LastAgreement is { Agrees: true, Metres: < 0.01 });
        Assert.StartsWith("server: 0x" + client.CellId.ToString("X8", CultureInfo.InvariantCulture), model.WhereText, StringComparison.Ordinal);
        Assert.EndsWith("\nclient body 0.00 m away (agrees)", model.WhereText, StringComparison.Ordinal);
        Assert.Contains(rig.Chat, l => l.StartsWith("Lifestone: server 0x", StringComparison.Ordinal) && l.Contains("agrees within 0.5 m", StringComparison.Ordinal));

        // A client 3 m away from where the server has the player does not agree.
        rig.ClientIsAt(new CellFrame(client.CellId, new AcFrame(client.Frame.Origin + new Vector3(3f, 0f, 0f), client.Frame.Orientation)));
        model.WhereAmI();
        rig.Run(1.0);
        Assert.True(model.LastAgreement is { Agrees: false, Metres: > 2.9 and < 3.1 });
        Assert.Contains("does not agree", model.WhereText, StringComparison.Ordinal);
        Assert.Contains(rig.Chat, l => l.Contains("does not agree within 0.5 m", StringComparison.Ordinal));

        // No client position: the server's is shown, and the chat says nothing could be compared.
        rig.ClientIsAt(null);
        var noClient = new ToolsModel(rig.Session.Tools, () => null, rig.Chat.Add);
        noClient.WhereAmI();
        rig.Run(1.0);
        Assert.Contains("\nthe client body has no position yet", noClient.WhereText, StringComparison.Ordinal);
        Assert.Contains(rig.Chat, l => l.Contains("no position to compare yet", StringComparison.Ordinal));
        Assert.Empty(rig.Errors);
    }

    [Fact]
    public void Where_am_I_without_a_client_body_says_so_and_logs_the_TODO_once()
    {
        using var rig = new Rig();
        var errors = new List<string>();
        var model = new ToolsModel(rig.Session.Tools, null, rig.Chat.Add, logError: errors.Add);
        model.WhereAmI();
        rig.Run(1.0);
        model.WhereAmI();
        rig.Run(1.0);

        Assert.Contains("no client body in this host (TODO(g7-where-client-body))", model.WhereText, StringComparison.Ordinal);
        Assert.False(model.LastAgreement is { Agrees: true });
        Assert.Contains(rig.Chat, l => l.Contains("no client physics body to compare (TODO(g7-where-client-body))", StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Chat, l => l.Contains("agrees within", StringComparison.Ordinal));
        var logged = Assert.Single(errors);
        Assert.Contains("TODO(g7-where-client-body)", logged, StringComparison.Ordinal);
    }

    [Fact]
    public void A_detached_model_ignores_a_reply_that_arrives_late()
    {
        using var rig = new Rig();
        var chatBefore = rig.Chat.Count;
        var model = rig.Model;
        model.WhereAmI();
        model.Detach();
        var statusBefore = model.Status;
        rig.Run(1.0);

        Assert.Equal(statusBefore, model.Status);
        Assert.Equal("asking the server...", model.WhereText);
        Assert.Equal(chatBefore, rig.Chat.Count);
    }

    [Theory]
    [InlineData("1000", 1000L, true)]
    [InlineData("1,000,000", 1_000_000L, true)]
    [InlineData(" 25 ", 25L, true)]
    [InlineData("999999999999", ToolsModel.MaxXp, true)]
    [InlineData("1000000000000", 0L, false)]
    [InlineData("0", 0L, false)]
    [InlineData("-5", 0L, false)]
    [InlineData("12abc", 0L, false)]
    [InlineData("", 0L, false)]
    public void An_amount_is_a_whole_number_from_1_to_the_mods_limit(string text, long expected, bool ok)
    {
        Assert.Equal(ok, ToolsModel.TryParseAmount(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Fact]
    public void Add_XP_builds_the_request_and_the_server_confirms_it_in_the_chat()
    {
        using var rig = new Rig();
        var model = rig.Model;

        Assert.Null(model.AddXp("lots"));
        Assert.StartsWith("Type an amount of experience", model.Status, StringComparison.Ordinal);
        Assert.DoesNotContain(rig.Session.ActionLog.Count > 0 ? Enumerable.Range(0, rig.Session.ActionLog.Count).Select(i => rig.Session.ActionLog[i]) : [], e => e.Kind == ActionKind.ToolAddXp);

        var handle = model.AddXp("1,500");
        Assert.NotNull(handle);
        rig.Run(1.0);
        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        Assert.Equal(1500, handle.Result!.Requested);
        Assert.Equal(1500, handle.Result.Added);
        Assert.Equal("Added 1,500 experience; total 1,001,500, level 1.", model.Status);
        var reply = Assert.Single(Lines(rig.Session), l => l.Kind == ChatKind.ToolReply);
        Assert.StartsWith("Lifestone: added 1,500 experience", reply.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Against_a_server_that_is_not_on_loopback_the_gate_is_shut_and_every_tool_is_refused()
    {
        var h = new ActionHarness(server: new IPEndPoint(new IPAddress([192, 0, 2, 10]), 9000));
        h.Present();
        h.Tick();
        var chat = new List<string>();
        var model = new ToolsModel(h.Api.Tools, () => null, chat.Add);

        Assert.Equal(ToolsGate.NotLoopback, model.Gate);
        var place = model.Teleport(model.Catalog.Find("Holtburg")!);
        var where = model.WhereAmI();
        var xp = model.AddXp("10");
        Assert.All(new ActionHandle?[] { place, where, xp }, handle => Assert.Equal(ActionOutcomeKind.Refused, handle!.Outcome!.Value.Kind));
        Assert.Contains("not a loopback address", place.Outcome!.Value.Detail, StringComparison.Ordinal);
        Assert.StartsWith("Could not add experience: ", model.Status, StringComparison.Ordinal);
        Assert.Equal(3, chat.Count(l => l.Contains("not a loopback address", StringComparison.Ordinal)));
        Assert.Empty(h.Net.ToolRequests()); // nothing went out
    }

    [Fact]
    public void Without_the_mod_the_gate_says_so_and_nothing_more_is_sent()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        var chat = new List<string>();
        var model = new ToolsModel(h.Api.Tools, () => null, chat.Add);
        Assert.Equal(ToolsGate.Open, model.Gate);

        model.ProbeIfNeeded(); // the session's own probe is in flight already; nothing more is asked
        h.ServerMessage(ToolsWire.UnknownCommandText, ToolsWire.UnknownCommandChatType);
        h.Tick();

        Assert.Equal(ToolsGate.ModMissing, model.Gate);
        var sent = h.Net.ToolRequests().Count;
        var xp = model.AddXp("10");
        Assert.Equal(ActionOutcomeKind.Refused, xp!.Outcome!.Value.Kind);
        Assert.Contains("not loaded", xp.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(sent, h.Net.ToolRequests().Count);
    }

    [Fact]
    public void A_cancelled_use_says_so_in_the_chat_and_the_status()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        var chat = new List<string>();
        var model = new ToolsModel(h.Api.Tools, () => null, chat.Add);

        var where = model.WhereAmI();
        Assert.NotNull(where);
        Assert.False(where!.IsCompleted); // the mod's reply has not come
        h.Api.CancelAll("the session ended");

        Assert.Equal(ActionOutcomeKind.Cancelled, where.Outcome!.Value.Kind);
        Assert.Equal("Could not ask where you are: the session ended", model.Status);
        Assert.Contains(chat, l => l == "Lifestone: could not ask where you are: the session ended");
    }

    [Fact]
    public void Outside_the_world_the_gate_is_NotInWorld()
    {
        var h = new ActionHarness();
        var model = new ToolsModel(h.Api.Tools, () => null, _ => { });
        Assert.Equal(ToolsGate.NotInWorld, model.Gate);
    }
}
