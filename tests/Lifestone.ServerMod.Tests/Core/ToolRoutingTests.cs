using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests.Core;

/// <summary>
/// The <c>@lifestone</c> command's guards and dispatch (server/mod/Lifestone.ServerMod/Core/ToolRouting.cs, compiled
/// here by link; m2-plan SM3): every request gets exactly one answer, the guards run in order (not-loopback, parse,
/// needs-player, not-in-world), the player is asked for only by a verb that acts on it, and every request the parser
/// makes reaches its own verb.
/// </summary>
public sealed class ToolRoutingTests
{
    private const string Player = "the player";

    /// <summary>One request of every verb form, the handler it must reach, and whether that handler takes the player.</summary>
    public static TheoryData<string, string, bool> EveryVerb => new()
    {
        { "status #a1", "Status", false },
        { "net", "Net", false },
        { "places", "Places", false },
        { "where", "Where", true },
        { "xp 1000", "Xp", true },
        { "place poi \"Holtburg\"", "PlacePoi", true },
        { "place loc 0xA9B40019 84 7.1 94", "PlaceLoc", true },
        { "quest stamp SentryTaskComplete #q1", "Quest", true },
        { "quest erase SentryTaskComplete", "Quest", true },
        { "quest has SentryTaskComplete", "Quest", true },
        { "char purge \"Lifestone Throwaway\" #c1", "CharPurge", true },
        { "spell learn 1,2,3 #s1", "SpellLearn", true },
        { "item 1628 2 #i1", "Item", true },
    };

    /// <summary>Requests that do not parse, with the error each gets.</summary>
    public static TheoryData<string, string> BadRequests => new()
    {
        { "fly", ToolErrors.UnknownVerb },
        { string.Empty, ToolErrors.BadArgs },
        { "xp abc", ToolErrors.BadArgs },
        { "place loc 0xA9B40019 84", ToolErrors.BadArgs },
        { "quest stamp", ToolErrors.BadArgs },
        { "quest stamp Bad@Name", ToolErrors.BadArgs },
        { "char", ToolErrors.BadArgs },
        { "char purge", ToolErrors.BadArgs },
        { "char delete Ann", ToolErrors.BadArgs },
        { "spell learn", ToolErrors.BadArgs },
        { "spell learn 1, 2", ToolErrors.BadArgs },
        { "item", ToolErrors.BadArgs },
        { "item 1628 0", ToolErrors.BadArgs },
    };

    /// <summary>Every request above, good and bad, for the one-answer sweep.</summary>
    public static TheoryData<string> AnyRequest
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var row in EveryVerb)
            {
                data.Add((string)row[0]);
            }

            foreach (var row in BadRequests)
            {
                data.Add((string)row[0]);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(EveryVerb))]
    public void Every_verb_reaches_its_own_handler(string raw, string handler, bool takesPlayer)
    {
        var parse = ToolRequestParser.Parse(raw);
        var run = Route(parse);
        Assert.Equal([takesPlayer ? handler + " " + Player : handler], run.Calls);
        Assert.Same(parse.Request, run.Verbs.Request);
        Assert.Equal(takesPlayer ? 1 : 0, run.PlayerAsks);
    }

    [Fact]
    public void Every_request_type_the_parser_makes_has_a_verb()
    {
        // The samples above cover every concrete ToolRequest type, so the theory above reaches every one of them.
        var sampled = EveryVerb.Select(row => ToolRequestParser.Parse((string)row[0]).Request!.GetType()).ToHashSet();
        var all = typeof(ToolRequest).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(ToolRequest)) && !t.IsAbstract).ToHashSet();
        Assert.Equal(11, all.Count);
        Assert.Equal(all.OrderBy(t => t.Name, StringComparer.Ordinal), sampled.OrderBy(t => t.Name, StringComparer.Ordinal));

        // Status reports every verb form as carried out.
        Assert.Equal(ToolsWire.Verbs, ToolRouting.Implemented);
        Assert.Equal(13, ToolRouting.Implemented.Count);
    }

    [Theory]
    [MemberData(nameof(AnyRequest))]
    public void Every_request_gets_exactly_one_answer(string raw)
    {
        var parse = ToolRequestParser.Parse(raw);
        foreach (var hasSession in new[] { true, false })
        {
            foreach (var isLoopback in new[] { true, false })
            {
                foreach (var inWorld in new[] { true, false })
                {
                    var run = Route(parse, hasSession, isLoopback, inWorld);
                    Assert.True(run.Calls.Count == 1, $"session={hasSession} loopback={isLoopback} inWorld={inWorld}: {string.Join(", ", run.Calls)}");
                    Assert.True(run.PlayerAsks <= 1);
                }
            }
        }
    }

    [Fact]
    public void A_session_from_another_computer_is_refused_before_anything_else()
    {
        // Before the request is read, so a bad request and a missing player both lose to it, and status too.
        foreach (var raw in new[] { "fly", "xp abc", "where", "status", "places", "quest stamp SentryTaskComplete", "quest stamp @" })
        {
            var run = Route(ToolRequestParser.Parse(raw), isLoopback: false, inWorld: false);
            Assert.Equal(["Refuse " + ToolErrors.NotLoopback], run.Calls);
            Assert.Equal(ToolRouting.NotLoopbackMessage, run.Verbs.Refused!.Message);
            Assert.Equal(0, run.PlayerAsks);
        }
    }

    [Theory]
    [MemberData(nameof(BadRequests))]
    public void A_request_that_does_not_parse_gets_the_parsers_error_before_the_player_is_asked_for(string raw, string code)
    {
        var parse = ToolRequestParser.Parse(raw);
        foreach (var hasSession in new[] { true, false })
        {
            var run = Route(parse, hasSession, isLoopback: true, inWorld: false);
            Assert.Equal(["Refuse " + code], run.Calls);
            Assert.Same(parse.Error, run.Verbs.Refused);
            Assert.Equal(0, run.PlayerAsks);
        }
    }

    [Fact]
    public void Ace_s_console_may_use_status_and_net_only()
    {
        // The console has no session, so the loopback flag is not read there.
        Assert.Equal(["Status"], Route(ToolRequestParser.Parse("status"), hasSession: false, isLoopback: false).Calls);
        Assert.Equal(["Net"], Route(ToolRequestParser.Parse("NET #x"), hasSession: false, isLoopback: false).Calls);

        foreach (var (raw, verb) in new[] { ("places", "places"), ("where", "where"), ("xp 5", "xp"), ("place poi Holtburg", "place"), ("quest stamp SentryTaskComplete", "quest"), ("quest has SentryTaskComplete", "quest"), ("char purge \"Ann\"", "char") })
        {
            var run = Route(ToolRequestParser.Parse(raw), hasSession: false, inWorld: true);
            Assert.Equal(["Refuse " + ToolErrors.NeedsPlayer], run.Calls);
            Assert.Equal(verb + " needs a player; at ACE's console only status and net work", run.Verbs.Refused!.Message);
            Assert.Equal(0, run.PlayerAsks);
        }
    }

    [Theory]
    [MemberData(nameof(EveryVerb))]
    public void Only_a_verb_that_acts_on_the_player_needs_it_in_the_world(string raw, string handler, bool takesPlayer)
    {
        var run = Route(ToolRequestParser.Parse(raw), inWorld: false);
        if (takesPlayer)
        {
            Assert.Equal(["Refuse " + ToolErrors.NotInWorld], run.Calls);
            Assert.Equal(ToolRouting.NotInWorldMessage, run.Verbs.Refused!.Message);
            Assert.Equal(1, run.PlayerAsks);
        }
        else
        {
            Assert.Equal([handler], run.Calls);
            Assert.Equal(0, run.PlayerAsks);
        }
    }

    [Fact]
    public void A_request_type_no_verb_handles_is_reported_as_unhandled()
    {
        // A newer parser with a verb this mod build lacks: a loud bug (LifestoneCommand logs it and answers failed).
        var fly = new FlyRequest();
        Assert.Null(ToolRouting.ScopeOf(fly));
        var run = Route(new ToolRequestParse(fly, null, fly.Verb, null));
        Assert.Equal(["Unhandled fly"], run.Calls);
        Assert.Equal(0, run.PlayerAsks);
    }

    [Fact]
    public void Each_request_type_has_the_scope_its_verb_needs()
    {
        Assert.Equal(ToolScope.Console, ToolRouting.ScopeOf(new StatusRequest()));
        Assert.Equal(ToolScope.Console, ToolRouting.ScopeOf(new NetRequest()));
        Assert.Equal(ToolScope.Session, ToolRouting.ScopeOf(new PlacesRequest()));
        Assert.Equal(ToolScope.Player, ToolRouting.ScopeOf(new WhereRequest()));
        Assert.Equal(ToolScope.Player, ToolRouting.ScopeOf(new XpRequest(1)));
        Assert.Equal(ToolScope.Player, ToolRouting.ScopeOf(new PlacePoiRequest("Holtburg")));
        Assert.Equal(ToolScope.Player, ToolRouting.ScopeOf(new PlaceLocRequest(new ToolLocation(0xA9B40019, 84, 7, 94))));
        foreach (var action in Enum.GetValues<QuestAction>())
        {
            Assert.Equal(ToolScope.Player, ToolRouting.ScopeOf(new QuestRequest(action, "SentryTaskComplete")));
        }

        // The purge acts on the account's characters on behalf of a player in the world: the caller must be in it.
        Assert.Equal(ToolScope.Player, ToolRouting.ScopeOf(new CharPurgeRequest("Lifestone Throwaway")));

        // The spellbook and the pack are the calling player's own: both need the player in the world.
        Assert.Equal(ToolScope.Player, ToolRouting.ScopeOf(new SpellLearnRequest([1])));
        Assert.Equal(ToolScope.Player, ToolRouting.ScopeOf(new ItemRequest(1628)));
    }

    private static (List<string> Calls, Recorder Verbs, int PlayerAsks) Route(ToolRequestParse parse, bool hasSession = true, bool isLoopback = true, bool inWorld = true)
    {
        var verbs = new Recorder();
        var asks = 0;
        ToolRouting.Route(verbs, hasSession, isLoopback, parse, () =>
        {
            asks++;
            return inWorld ? Player : null;
        });
        return (verbs.Calls, verbs, asks);
    }

    /// <summary>A request type the router does not know.</summary>
    private sealed record FlyRequest() : ToolRequest((string?)null)
    {
        public override string Verb => "fly";
    }

    /// <summary>Records which verb the router called, with what.</summary>
    private sealed class Recorder : IToolVerbs<string>
    {
        public List<string> Calls { get; } = [];

        public ToolRequest? Request { get; private set; }

        public ToolError? Refused { get; private set; }

        public void Refuse(ToolError error)
        {
            Refused = error;
            Calls.Add("Refuse " + error.Code);
        }

        public void Status(StatusRequest request) => Record("Status", request);

        public void Net(NetRequest request) => Record("Net", request);

        public void Places(PlacesRequest request) => Record("Places", request);

        public void Where(string player, WhereRequest request) => Record("Where " + player, request);

        public void PlacePoi(string player, PlacePoiRequest request) => Record("PlacePoi " + player, request);

        public void PlaceLoc(string player, PlaceLocRequest request) => Record("PlaceLoc " + player, request);

        public void Xp(string player, XpRequest request) => Record("Xp " + player, request);

        public void Quest(string player, QuestRequest request) => Record("Quest " + player, request);

        public void CharPurge(string player, CharPurgeRequest request) => Record("CharPurge " + player, request);

        public void SpellLearn(string player, SpellLearnRequest request) => Record("SpellLearn " + player, request);

        public void Item(string player, ItemRequest request) => Record("Item " + player, request);

        public void Unhandled(ToolRequest request) => Record("Unhandled " + request.Verb, request);

        private void Record(string call, ToolRequest request)
        {
            Request = request;
            Calls.Add(call);
        }
    }
}
