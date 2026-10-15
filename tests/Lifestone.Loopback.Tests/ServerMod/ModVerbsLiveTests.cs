using System.Numerics;
using System.Text.RegularExpressions;
using Lifestone.Client.Interiors;
using Lifestone.Data.Dat;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Net.Types;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;
using static Lifestone.Loopback.Tests.Net.LivePlacement;
using static Lifestone.Loopback.Tests.Net.LiveSteps;

namespace Lifestone.Loopback.Tests.ServerMod;

/// <summary>
/// SM9 (m2-plan section 3, "server mod"; wave 3 fix dod-10-11-14-15-1): the <c>@lifestone</c> verbs the Tools panel (G7) and
/// <c>PlayerTools</c> depend on, driven through the real mod on the bundled ACE on 127.0.0.1 with the test account and the loopback
/// character (<see cref="LiveNetClient.LoopbackCharacter"/>): <c>status</c>, <c>places</c> (56 destinations under 62 names), every error
/// reply that can be asked from a client (<c>not-found</c>, <c>invalid-cell</c>, <c>busy</c> while teleporting, <c>bad-args</c>,
/// <c>unknown-verb</c>), and one <c>place loc</c> to a curated interior row (the Holtburg shop). Every test that moves the character ends with
/// it back at its start (the Training Academy), so the other live tests find it where they expect it. Skipped, with the probe's reason, unless
/// the backend runs with the mod ready.
/// </summary>
/// <remarks>
/// <para>Nothing here levels the character: the <c>xp</c> verb is in <see cref="ModXpLiveTests"/>, which carries a fixed Skip because it
/// permanently raises a character's level and needs the owner's go-ahead. The error replies for <c>xp</c> (an amount of 0 or past the limit)
/// are safe and are here.</para>
/// <para>The tests run in step order after the net track's (<see cref="LiveStepAttribute"/>, steps 1 to 20).</para>
/// <para><b>Not covered, and why.</b> <c>not-in-world</c>, <c>not-loopback</c> and <c>needs-player</c> need a session that is not in a landblock, one from
/// another computer, or ACE's console; a client on this computer cannot make them. The mod's routing tests (<c>ToolRoutingTests</c>) cover those.
/// <c>status</c> at ACE's console is the same reply with no account and no character; it cannot be asked from here either.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[TestCaseOrderer("Lifestone.Loopback.Tests.Net.LiveStepOrderer", "Lifestone.Loopback.Tests")]
public sealed partial class ModVerbsLiveTests(ITestOutputHelper output)
{
    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex Commit();

    [GeneratedRegex("^[0-9A-Fa-f]{64}$")]
    private static partial Regex Sha256();

    /// <summary>
    /// What it proves: the mod answers <c>status</c> with the wire version this client speaks, the ACE it was built for, the pinned loopback host,
    /// a plain Player session (so ACE's first-account promotion cannot have made the test account an Admin), the character's name, an open world and
    /// every verb of <see cref="ToolsWire.Verbs"/>; and the chat line the player sees is the mod's sentence.
    /// </summary>
    [LoopbackFact]
    [LiveStep(30)]
    public void Status_describes_the_mod_the_pinned_host_a_plain_Player_and_all_verbs()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        try
        {
            EnterTheWorld(client);
            Unhide(client);
            var id = NextId();
            var parse = AskText(client, ToolsWire.Request(new StatusRequest(id)), ToolVerbs.Status, id);
            Assert.True(parse.Reply!.Ok, parse.Reply.Message);
            var status = parse.Reply.DataAs<StatusData>()!;
            output.WriteLine(status.Describe());

            Assert.Equal(ToolsWire.Version, status.Wire);
            Assert.False(string.IsNullOrWhiteSpace(status.Mod));
            Assert.Matches(Commit(), status.AceCommit);
            Assert.Matches(Sha256(), status.AceServerDllSha256);
            Assert.Equal(LoopbackPolicyHost, status.Host);
            Assert.Equal(BackendProbe.Probe().AceEndPoint?.Port, status.Port);
            Assert.Equal("Player", status.Access);
            Assert.False(status.AutoPromotion, "ACE's first-account Admin auto-promotion is on");
            Assert.False(string.IsNullOrWhiteSpace(status.Account));
            Assert.Equal(LiveNetClient.LoopbackCharacter, status.Character);
            Assert.Equal("Open", status.World);
            Assert.Equal(ToolsWire.Verbs.Order(StringComparer.Ordinal), status.Verbs.Order(StringComparer.Ordinal));

            Assert.StartsWith(ToolsWire.ChatPrefix, parse.ChatLine, StringComparison.Ordinal);
            Assert.Equal(ToolsWire.ChatPrefix + status.Describe(), parse.ChatLine);

            LogOffAndQuit(client);
        }
        finally
        {
            LeaveTheWorld(client);
        }
    }

    /// <summary>
    /// What it proves: <c>places</c> lists ACE's 56 destinations under 62 names (DoD 10), each with a portal weenie, a parseable destination cell and
    /// finite coordinates; every name is one <c>place poi</c> accepts by the request rules (the picker can send any of them); Holtburg is among them;
    /// and the chat sentence says so. The worst-case reply length is the unit tests' (<c>PlacesReplySizeTests</c>); here the real reply parses.
    /// </summary>
    [LoopbackFact]
    [LiveStep(31)]
    public void Places_lists_56_destinations_under_62_names_that_place_poi_accepts()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        try
        {
            EnterTheWorld(client);
            Unhide(client);
            var id = NextId();
            var parse = AskText(client, ToolsWire.Request(new PlacesRequest(id)), ToolVerbs.Places, id);
            Assert.True(parse.Reply!.Ok, parse.Reply.Message);
            var places = parse.Reply.DataAs<PlacesData>()!;
            output.WriteLine(places.Describe());

            Assert.Equal(56, places.Count);
            Assert.Equal(62, places.Names);
            Assert.Equal(56, places.Places.Count);
            Assert.Equal(62, places.Places.Sum(p => p.Names.Count));
            Assert.All(places.Places, p =>
            {
                Assert.NotEmpty(p.Names);
                Assert.True(p.Wcid > 0, "a destination without a portal weenie");
                Assert.True(ToolsWire.TryParseCell(p.Cell, out _), $"'{p.Cell}' is not a cell id");
                Assert.All(new[] { p.X, p.Y, p.Z, p.Qw, p.Qx, p.Qy, p.Qz }, v => Assert.True(float.IsFinite(v)));
                Assert.All(p.Names, name => Assert.True(ToolRequestParser.IsValidPoiName(name), $"place poi would refuse the name '{name}'"));
            });

            var names = places.Places.SelectMany(p => p.Names).ToList();
            Assert.Contains(names, n => string.Equals(n, "Holtburg", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(ToolsWire.ChatPrefix + places.Describe(), parse.ChatLine);

            LogOffAndQuit(client);
        }
        finally
        {
            LeaveTheWorld(client);
        }
    }

    /// <summary>
    /// What it proves: each refusal a client can provoke comes back as a failed reply with its own error code, the request's verb and id and no data,
    /// and changes nothing (the server still has the player where it was): an unknown point of interest (<c>not-found</c>), a cell that is not in ACE's
    /// cell dat (<c>invalid-cell</c>), arguments the parser refuses (<c>bad-args</c>, including an <c>xp</c> of 0 and of one past the limit) and an unknown
    /// verb (<c>unknown-verb</c>, echoing the lower-cased word).
    /// </summary>
    [LoopbackFact]
    [LiveStep(32)]
    public void Refusals_come_back_as_failed_replies_with_their_error_codes_and_change_nothing()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        var start = default(AcPosition);
        var restored = false;
        try
        {
            start = EnterReadyAtStart(client, output);
            var before = AskData<WhereData>(client, new WhereRequest(NextId()));

            var cases = new (string Text, string Verb, string Error)[]
            {
                // A point of interest nobody named; a cell no dat holds: 0x0001FFFD passes the parser and PlaceRules (an EnvCell index, the last one,
                // in a landblock on the map) but no retail landblock has 65,000 EnvCells (PlaceRulesTests and PlaceLocRefusalCellTests pin both halves).
                // Not 0x00010150: that EnvCell is in the dat, and ACE carried the teleport out.
                ("place poi \"No Such Place In Dereth\"", ToolVerbs.Place, ToolErrors.NotFound),
                ("place loc 0x0001FFFD 10 10 0", ToolVerbs.Place, ToolErrors.InvalidCell),
                // The parser's refusals.
                ("xp 0", ToolVerbs.Xp, ToolErrors.BadArgs),
                ("xp 1000000000000", ToolVerbs.Xp, ToolErrors.BadArgs),
                ("xp -5", ToolVerbs.Xp, ToolErrors.BadArgs),
                ("xp lots", ToolVerbs.Xp, ToolErrors.BadArgs),
                ("place loc nonsense", ToolVerbs.Place, ToolErrors.BadArgs),
                ("place poi", ToolVerbs.Place, ToolErrors.BadArgs),
                ("where now", ToolVerbs.Where, ToolErrors.BadArgs),
                // Not a verb.
                ("frobnicate", "frobnicate", ToolErrors.UnknownVerb),
            };

            var asked = cases.Select(c =>
            {
                var id = NextId();
                return (Text: $"@{ToolsWire.Command} {c.Text} #{id}", c.Verb, Id: id);
            }).ToList();

            var replies = AskTexts(client, asked);
            for (var i = 0; i < cases.Length; i++)
            {
                var reply = replies[i].Reply!;
                output.WriteLine($"{cases[i].Text} -> {reply.Error}: {reply.Message}");
                Assert.False(reply.Ok, $"'{cases[i].Text}' was carried out");
                Assert.Equal(cases[i].Error, reply.Error);
                Assert.Equal(cases[i].Verb, reply.Verb);
                Assert.Equal(asked[i].Id, reply.Id);
                Assert.Null(reply.Data);
                Assert.False(string.IsNullOrWhiteSpace(reply.Message));
                Assert.StartsWith(ToolsWire.ChatPrefix, replies[i].ChatLine, StringComparison.Ordinal);
                Assert.Contains(ToolErrors.All, e => e == reply.Error);
            }

            var after = AskData<WhereData>(client, new WhereRequest(NextId()));
            Assert.Equal(before.Cell, after.Cell);
            Assert.Equal((before.X, before.Y, before.Z), (after.X, after.Y, after.Z));
            Assert.False(after.Teleporting);
            Assert.Equal(before.TeleportSeq, after.TeleportSeq);
            restored = true;
            LogOffAndQuit(client);
        }
        finally
        {
            if (!restored)
            {
                RestoreBestEffort(client, start, output);
            }

            LeaveTheWorld(client);
        }
    }

    /// <summary>
    /// What it proves: while the server still has the player teleporting (after the F751, before LoginComplete) both <c>place</c> forms are refused
    /// <c>busy</c> and do not start a second teleport; after LoginComplete the server leaves Teleporting and the next <c>place</c> works (the character goes
    /// back to its start).
    /// </summary>
    [LoopbackFact]
    [LiveStep(33)]
    public void Place_is_refused_busy_while_the_player_is_teleporting_and_works_again_after_LoginComplete()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        var start = default(AcPosition);
        var back = false;
        try
        {
            start = EnterReadyAtStart(client, output);
            var started = StartTeleport(client, new PlacePoiRequest("Holtburg", NextId()));
            var teleports = client.Sink.Teleports;

            var mid = AskData<WhereData>(client, new WhereRequest(NextId()));
            Assert.True(mid.Teleporting, "the server does not have the player teleporting between the F751 and LoginComplete");

            var poi = Ask(client, new PlacePoiRequest("Holtburg", NextId()));
            var loc = Ask(client, new PlaceLocRequest(ToLocation(start), NextId()));
            foreach (var reply in new[] { poi, loc })
            {
                output.WriteLine($"place while teleporting -> {reply.Error}: {reply.Message}");
                Assert.False(reply.Ok);
                Assert.Equal(ToolErrors.Busy, reply.Error);
                Assert.Equal(ToolVerbs.Place, reply.Verb);
                Assert.Null(reply.Data);
            }

            client.PumpUntil(() => false, TimeSpan.FromSeconds(2));
            Assert.Equal(teleports, client.Sink.Teleports);

            var arrival = FinishArrival(client, started);
            Assert.Equal(0xA9B4u, LandblockOf(arrival.Where.Cell));
            Assert.False(arrival.Where.Teleporting);

            ReturnToStart(client, start);
            back = true;
            LogOffAndQuit(client);
        }
        finally
        {
            if (!back)
            {
                RestoreBestEffort(client, start, output);
            }

            LeaveTheWorld(client);
        }
    }

    /// <summary>
    /// What it proves (the Places panel's curated interior rows, DoD 10): <c>place loc</c> to the Holtburg shop's interior cell
    /// (<see cref="InteriorLocations.HoltburgShop"/>, an EnvCell inside a town building, not a dungeon) is carried out; the reply names the cell, there
    /// is an F751 and an F748 in Holtburg's landblock, and after LoginComplete the server has the player in that indoor cell within half a metre. The
    /// character then goes back to its start. The client's <c>PlayerTools.Place</c> and the panel's picker send exactly this request; they are tested
    /// against the stand-in (<c>ToolsModelTests</c>) and, live, by the login check's tools leg.
    /// </summary>
    [LoopbackFact]
    [LiveStep(34)]
    public void Place_loc_to_a_curated_interior_cell_puts_the_player_inside_the_Holtburg_shop()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        var start = default(AcPosition);
        var back = false;
        try
        {
            start = EnterReadyAtStart(client, output);
            var shop = InteriorLocations.HoltburgShop;
            var target = new AcPosition(shop.CellId, shop.Location.Position.Local, Quaternion.Identity);
            var arrival = PlaceAndArrive(client, new PlaceLocRequest(ToLocation(target), NextId()));
            output.WriteLine($"place loc {shop.Name}: {arrival.Place.Describe()}; where {arrival.Where.Cell} ({arrival.Where.X:F2}, {arrival.Where.Y:F2}, {arrival.Where.Z:F2})");

            Assert.Equal(ToolVerbs.PlaceLoc, arrival.Place.Kind);
            Assert.Null(arrival.Place.Name);
            Assert.Equal(ToolsWire.FormatCell(shop.CellId), arrival.Place.Cell);
            Assert.True(arrival.Teleports >= 1, "no F751 PlayerTeleport");
            Assert.Equal(shop.CellId >> 16, arrival.Position.Position.CellId >> 16);
            Assert.True(ToolsWire.TryParseCell(arrival.Where.Cell, out var cell));
            Assert.Equal(shop.CellId >> 16, cell >> 16);
            Assert.True(PlaceRules.IsEnvCell(cell), $"the server has the player in 0x{cell:X8}, which is not an indoor cell");
            Assert.True(Distance(arrival.Where, target) <= 0.5f, $"where is {Distance(arrival.Where, target):F2} m from the interior point");

            ReturnToStart(client, start);
            back = true;
            LogOffAndQuit(client);
        }
        finally
        {
            if (!back)
            {
                RestoreBestEffort(client, start, output);
            }

            LeaveTheWorld(client);
        }
    }

    /// <summary>The host the mod reports (the pinned loopback host, 127.0.0.1).</summary>
    private const string LoopbackPolicyHost = Lifestone.Net.Sockets.LoopbackPolicy.PinnedHost;
}
