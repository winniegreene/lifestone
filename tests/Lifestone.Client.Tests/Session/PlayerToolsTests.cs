using System.Globalization;
using System.Net;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Interiors;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Tools;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Data.World.Data;
using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The player tools over the server mod (step ST12; DoD 10): requests built with the mod's own ToolsWire, replies
/// matched by id and read into typed results, the mod-missing and not-loopback refusals, the chat and action-log trail of
/// every use, the status probe's access check, and the invariant formatting of places and positions.
/// </summary>
public sealed class PlayerToolsTests
{
    private static readonly StatusData PlayerStatus = new(1, "0.1.0", new string('a', 40), new string('b', 64), "127.0.0.1", 9000, false, "Player", "lifestone-test", "Tester", "Open", ["status", "where", "net", "places", "xp", "place poi", "place loc", "quest stamp", "quest erase", "quest has"]);

    [Fact]
    public void ACE_rows_request_by_poi_name_curated_rows_by_location()
    {
        var catalog = TeleportCatalog.Default();
        foreach (var destination in catalog.Destinations)
        {
            var request = PlaceRequests.For(destination, "a1");
            if (PlaceRequests.TryFindAcePoint(destination, out var poi))
            {
                Assert.Equal(new PlacePoiRequest(poi.PoiNames[0], "a1"), request);
            }
            else
            {
                Assert.IsType<PlaceLocRequest>(request);
            }

            // Every destination makes a request the mod parses back to the same request.
            var text = ToolsWire.Request(request);
            Assert.True(ToolsWire.TryParseRequest(text["@lifestone ".Length..], out var parsed, out var error), error?.Message);
            Assert.Equal(request.Verb, parsed.Verb);
        }

        // Holtburg is an ACE row; the Town Network's curated interior was merged into ACE's row; the Redoubt is curated.
        Assert.Equal("@lifestone place poi \"Holtburg\" #a1", ToolsWire.Request(PlaceRequests.For(catalog.Find("Holtburg")!, "a1")));
        Assert.IsType<PlacePoiRequest>(PlaceRequests.For(catalog.Find("town-network")!));
        var redoubt = Assert.IsType<PlaceLocRequest>(PlaceRequests.For(TeleportCatalog.FromInterior(InteriorLocations.HoltburgRedoubt)));
        Assert.Equal((0x016301ACu, 140f, -40f, 0f), (redoubt.At.Cell, redoubt.At.X, redoubt.At.Y, redoubt.At.Z));

        // The rotation is about Z, from the heading: back to the same heading.
        var heading = WorldLocations.HeadingFromQuaternion(redoubt.At.Qw, redoubt.At.Qz);
        Assert.InRange(Math.Abs(heading - InteriorLocations.HoltburgRedoubt.Location.HeadingDegrees), 0, 1e-3);
        Assert.Equal((0f, 0f), (redoubt.At.Qx, redoubt.At.Qy));
        var academy = Assert.IsType<PlaceLocRequest>(PlaceRequests.For(TeleportCatalog.FromInterior(InteriorLocations.TrainingAcademy)));
        Assert.Equal(0x860201ADu, academy.At.Cell);
    }

    [Fact]
    public void ACE_rows_name_their_first_poi_name_and_keep_ace_s_rotation()
    {
        var hotel = AcePointsOfInterest.All.First(p => p.PoiNames.Count > 1);
        var destination = TeleportCatalog.FromAce(hotel);
        Assert.Equal(hotel.PoiNames[0], Assert.IsType<PlacePoiRequest>(PlaceRequests.For(destination)).Name);
        var location = PlaceRequests.LocationOf(destination);
        Assert.Equal((hotel.Rotation.W, hotel.Rotation.Z), (location.Qw, location.Qz));
    }

    [Fact]
    public void Reply_correlates_by_id_and_parses_typed_data()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        var where = h.Api.Tools.WhereAmI(ActionSource.Ui);
        h.Tick();
        var text = h.Net.ToolRequests()[^1]!;
        Assert.Equal("@lifestone where #a" + where.Id.ToString(CultureInfo.InvariantCulture), text);

        // A reply for another id does not complete it; its own does, with the server's position.
        var data = new WhereData("0xA9B40019", 84.5f, 7.1f, 94.005f, 1, 0, 0, 0, "0xA9B4FFFF", "0xA9B40019", false, 3, 2);
        h.ServerMessage(ToolsWire.Reply(ToolReply.Success("a999", ToolVerbs.Where, data), data.Describe()));
        h.Tick();
        Assert.False(where.IsCompleted);
        h.ServerMessage(ToolsWire.Reply(ToolReply.Success("a" + where.Id.ToString(CultureInfo.InvariantCulture), ToolVerbs.Where, data), data.Describe()));
        h.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, where.Outcome!.Value.Kind);
        var result = where.Result!;
        Assert.Equal(CellFrame.FromWire(0xA9B40019, new Vector3(84.5f, 7.1f, 94.005f), 1, 0, 0, 0), result.Position);
        Assert.Equal((false, (ushort)3, (ushort)2), (result.Teleporting, result.ForcePositionSequence, result.TeleportSequence));
        Assert.Equal(ToolAvailability.Ready, h.Api.Tools.Availability);
    }

    [Fact]
    public void An_error_reply_is_a_server_error_and_silence_times_out()
    {
        var h = new ActionHarness();
        h.Present();
        var place = h.Api.Tools.Place(TeleportCatalog.Default().Find("Holtburg")!, ActionSource.Ui);
        var xp = h.Api.Tools.AddXp(1000, ActionSource.Ui);
        h.Tick();
        var id = "a" + place.Id.ToString(CultureInfo.InvariantCulture);
        h.ServerMessage(ToolsWire.Reply(ToolReply.Failure(id, ToolVerbs.Place, new ToolError(ToolErrors.Busy, "you are teleporting")), "you are teleporting"));
        h.Tick();
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "busy: you are teleporting"), place.Outcome);
        Assert.Null(place.Result);

        // The probe and the place took the lane's two tokens: the xp request waited for the next one.
        Assert.Equal(ActionStatus.Queued, xp.Status);
        h.Tick(0.5);
        Assert.Equal(ActionStatus.Sent, xp.Status);
        h.Tick(ActionPolicyToolSeconds);
        Assert.Equal(ActionOutcomeKind.TimedOut, xp.Outcome!.Value.Kind);
    }

    [Fact]
    public void Unknown_command_marks_mod_missing_and_fails_pending_tools()
    {
        var h = new ActionHarness();
        h.Present();
        var where = h.Api.Tools.WhereAmI(ActionSource.Ui);
        h.Tick();
        Assert.Equal(ToolAvailability.Unknown, h.Api.Tools.Availability);

        h.ServerMessage(ToolsWire.UnknownCommandText, ToolsWire.UnknownCommandChatType);
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, where.Outcome!.Value.Kind);
        Assert.Contains("not loaded", where.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(ToolAvailability.ModMissing, h.Api.Tools.Availability);

        var xp = h.Api.Tools.AddXp(5, ActionSource.Ui);
        Assert.Equal(ActionOutcomeKind.Refused, xp.Outcome!.Value.Kind);
        var sent = h.Net.ToolRequests().Count;
        h.Tick();
        Assert.Equal(sent, h.Net.ToolRequests().Count);
    }

    [Fact]
    public void Every_use_writes_chat_and_action_log()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        var chatBefore = h.State.Chat.Count;
        var place = h.Api.Tools.Place(TeleportCatalog.Default().Find("Holtburg")!, ActionSource.Ui);
        h.Tick();
        Assert.Equal("Lifestone: teleporting to Holtburg...", h.State.Chat[chatBefore].Text);
        Assert.Equal(ChatKind.Notice, h.State.Chat[chatBefore].Kind);

        var data = new PlaceData("poi", "Holtburg", "0xA9B40019", 84f, 7.1f, 94f, 0.996917f, 0, 0, -0.078459f);
        h.ServerMessage(ToolsWire.Reply(ToolReply.Success("a" + place.Id.ToString(CultureInfo.InvariantCulture), ToolVerbs.Place, data), data.Describe()));
        h.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, place.Outcome!.Value.Kind);
        Assert.Equal(("poi", "Holtburg", 0xA9B40019u), (place.Result!.Kind, place.Result.Name, place.Result.Destination.CellId));
        var reply = h.State.Chat[chatBefore + 1];
        Assert.Equal(ChatKind.ToolReply, reply.Kind);
        Assert.StartsWith("Lifestone: teleporting to Holtburg, cell 0xA9B40019", reply.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("#lifestone", reply.Text, StringComparison.Ordinal);
        var entry = Enumerable.Range(0, h.ActionLog.Count).Select(i => h.ActionLog[i]).Single(e => e.Id == place.Id);
        Assert.Equal((ActionKind.ToolPlace, "ToolPlace Holtburg #a" + place.Id.ToString(CultureInfo.InvariantCulture)), (entry.Kind, entry.Summary));
        Assert.Contains(h.Log.Lines, l => l.Contains("action #" + place.Id.ToString(CultureInfo.InvariantCulture) + " ToolPlace", StringComparison.Ordinal));
    }

    [Fact]
    public void The_probe_is_silent_and_a_player_account_passes()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        var probe = Assert.Single(h.Net.ToolRequests())!;
        Assert.StartsWith("@lifestone status #a", probe, StringComparison.Ordinal);
        var chatBefore = h.State.Chat.Count;

        h.ServerMessage(ToolsWire.Reply(ToolReply.Success(probe[(probe.IndexOf('#', StringComparison.Ordinal) + 1)..], ToolVerbs.Status, PlayerStatus), PlayerStatus.Describe()));
        h.Tick();
        Assert.Equal(chatBefore, h.State.Chat.Count);
        Assert.Equal("Player", h.Api.Tools.LastStatus!.Access);
        Assert.Equal(ToolAvailability.Ready, h.Api.Tools.Availability);
        Assert.False(h.Api.KillSwitch);
        Assert.DoesNotContain(h.Log.Entries, e => e.Severity == LogSeverity.Error);
        var entry = h.ActionLog[h.ActionLog.Count - 1];
        Assert.Equal((ActionKind.ToolStatus, ActionSource.Client, ActionOutcomeKind.Confirmed), (entry.Kind, entry.Source, entry.Outcome.Kind));
    }

    [Fact]
    public void The_probe_waits_for_the_world_phase_when_the_player_arrives_first()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        h.Phase(Lifestone.Net.Session.SessionPhase.CharacterSelect, Lifestone.Net.Session.SessionPhase.EnteringWorld);
        h.R.EnterWorld();
        Assert.Equal(SelfTravel.Arriving, h.State.Self.Travel);
        Assert.Equal(1, h.Api.QueuedCount);

        h.Phase(Lifestone.Net.Session.SessionPhase.EnteringWorld, Lifestone.Net.Session.SessionPhase.InWorld);
        h.Tick();
        Assert.StartsWith("@lifestone status #a", Assert.Single(h.Net.ToolRequests()), StringComparison.Ordinal);
        Assert.Equal(1, h.Api.InFlightCount);
        Assert.DoesNotContain(h.Log.Lines, l => l.Contains("ToolStatus", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Admin", false)]
    [InlineData("Player", true)]
    public void Admin_access_in_status_is_an_error(string access, bool autoPromotion)
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        var probe = Assert.Single(h.Net.ToolRequests())!;
        var bot = h.Api.Talk("queued bot line", ActionSource.Automation);
        var status = PlayerStatus with { Access = access, AutoPromotion = autoPromotion };
        h.ServerMessage(ToolsWire.Reply(ToolReply.Success(probe[(probe.IndexOf('#', StringComparison.Ordinal) + 1)..], ToolVerbs.Status, status), status.Describe()));
        h.Tick();

        Assert.Contains(h.Log.Entries, e => e.Severity == LogSeverity.Error && e.Message.Contains("automation is stopped", StringComparison.Ordinal));
        Assert.True(h.Api.KillSwitch);
        Assert.Equal(ActionOutcomeKind.Cancelled, bot.Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, h.Api.Talk("another", ActionSource.Automation).Outcome!.Value.Kind);
    }

    [Fact]
    public void The_tools_refuse_a_server_that_is_not_loopback()
    {
        var h = new ActionHarness(server: new IPEndPoint(new IPAddress([192, 0, 2, 10]), 9000));
        h.Present();
        var unavailable = new List<ToolsUnavailable>();
        h.R.Bus.Subscribe((in ToolsUnavailable e) => unavailable.Add(e));
        Assert.Equal(ToolAvailability.NotLoopback, h.Api.Tools.Availability);
        var where = h.Api.Tools.WhereAmI(ActionSource.Ui);
        h.Tick();

        Assert.Equal(ActionOutcomeKind.Refused, where.Outcome!.Value.Kind);
        Assert.Single(h.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("PlayerTools.WhereAmI", StringComparison.Ordinal));
        Assert.Equal(ToolAvailability.NotLoopback, Assert.Single(unavailable).Availability);
        Assert.Empty(h.Net.ToolRequests());
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("tr-TR")]
    public void Formatting_is_culture_invariant(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var redoubt = ToolsWire.Request(PlaceRequests.For(TeleportCatalog.FromInterior(InteriorLocations.HoltburgRedoubt), "a7"));
            Assert.Matches(@"^@lifestone place loc 0x016301AC 140 -40 0 -?0\.\d+ 0 0 -?0\.\d+ #a7$", redoubt);
            var frame = CellFrame.FromWire(0xA9B40019, new Vector3(84.5f, 7.25f, 94.125f), 0.996917f, 0, 0, -0.078459f);
            Assert.Equal("0xA9B40019 [84.500000 7.250000 94.125000] 0.996917 0.000000 0.000000 -0.078459", AceLocText.Format(frame));

            var h = new ActionHarness();
            h.Present();
            h.Api.Tools.AddXp(1234567, ActionSource.Ui);
            h.Tick();
            Assert.Contains(Enumerable.Range(0, h.State.Chat.Count), i => h.State.Chat[i].Text == "Lifestone: asking for 1,234,567 experience...");
            Assert.Contains("@lifestone xp 1234567 #a", h.Net.ToolRequests()[^1], StringComparison.Ordinal);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Ace_loc_text_reads_what_ace_prints_and_teleloc_accepts()
    {
        var expected = CellFrame.FromWire(0x7F0401AD, new Vector3(12.3199f, -28.482f, 0.005f), -0.338946f, 0, 0, -0.940806f);
        Assert.True(AceLocText.TryParse("0x7F0401AD [12.319900 -28.482000 0.005000] -0.338946 0.000000 0.000000 -0.940806", out var a, out _));
        Assert.True(AceLocText.TryParse("0x7F0401AD 12.319900 -28.482000 0.005000 -0.338946 0.000000 0.000000 -0.940806", out var b, out _));
        Assert.True(AceLocText.TryParse("7F0401AD 12.3199 -28.482 0.005", out var c, out _));
        Assert.Equal(expected, a);
        Assert.Equal(expected, b);
        Assert.Equal(CellFrame.FromWire(0x7F0401AD, new Vector3(12.3199f, -28.482f, 0.005f), 1, 0, 0, 0), c);
        Assert.True(AceLocText.TryParse(AceLocText.Format(expected), out var round, out _));
        Assert.Equal(expected, round);

        Assert.False(AceLocText.TryParse("Holtburg", out _, out var why));
        Assert.Contains("cell", why, StringComparison.Ordinal);
        Assert.False(AceLocText.TryParse("0xZZ 1 2 3", out _, out _));
        Assert.False(AceLocText.TryParse("0xA9B40019 1 2 NaN", out _, out _));
        Assert.False(AceLocText.TryParse("0xA9B40019 1 2 3 1 0", out _, out _));
    }

    [Fact]
    public void Position_agreement_measures_global_metres()
    {
        var server = new CellFrame(0xA9B40019, new AcFrame(new Vector3(191.8f, 10f, 94f), Quaternion.Identity));
        var sameBlock = new CellFrame(0xA9B40019, new AcFrame(new Vector3(191.5f, 10.4f, 94f), Quaternion.Identity));
        var nextBlock = new CellFrame(0xAAB40001, new AcFrame(new Vector3(0.1f, 10f, 94f), Quaternion.Identity));
        Assert.Equal(0.5, PositionAgreement.DistanceMetres(server, sameBlock), 3);
        Assert.Equal(0.3, PositionAgreement.DistanceMetres(server, nextBlock), 3);
        Assert.True(PositionAgreement.Agrees(server, nextBlock, out var metres));
        Assert.Equal(0.3, metres, 3);
        var far = new CellFrame(0xA9B40019, new AcFrame(new Vector3(100f, 10f, 94f), Quaternion.Identity));
        Assert.False(PositionAgreement.Agrees(server, far, out _));
        Assert.Contains("0.30 m apart (agrees within 0.5 m)", PositionAgreement.Describe(server, nextBlock), StringComparison.Ordinal);
    }

    [Fact]
    public void Quest_requests_go_through_the_same_verb_path_and_confirm_with_the_counts_before_and_after()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();

        var stamp = h.Api.Tools.StampQuest("SentryTaskComplete", ActionSource.Ui);
        h.Tick();
        var id = "a" + stamp.Id.ToString(CultureInfo.InvariantCulture);
        Assert.Equal("@lifestone quest stamp SentryTaskComplete #" + id, h.Net.ToolRequests()[^1]);
        Assert.Equal(ActionKind.ToolQuest, stamp.Kind);
        Assert.Contains(Enumerable.Range(0, h.State.Chat.Count), i => h.State.Chat[i].Text == "Lifestone: asking the server to stamp quest SentryTaskComplete...");

        // A reply for another id does not complete it; its own does, with the typed counts.
        var stamped = new QuestData("stamp", "SentryTaskComplete", false, 0, true, 1);
        h.ServerMessage(ToolsWire.Reply(ToolReply.Success("a999", ToolVerbs.Quest, stamped), stamped.Describe()));
        h.Tick();
        Assert.False(stamp.IsCompleted);
        h.ServerMessage(ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.Quest, stamped), stamped.Describe()));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, stamp.Outcome!.Value.Kind);
        Assert.Equal(new QuestResult("stamp", "SentryTaskComplete", false, 0, true, 1), stamp.Result);
        Assert.True(stamp.Result!.Changed);
        Assert.Contains(Enumerable.Range(0, h.State.Chat.Count), i => h.State.Chat[i].Text == "Lifestone: quest SentryTaskComplete stamped: added, completions 1");

        // Erase: the next request waits for the lane's token; the reply is typed the same way.
        var erase = h.Api.Tools.EraseQuest("SentryTaskComplete", ActionSource.Ui);
        h.Tick(0.5);
        var eraseId = "a" + erase.Id.ToString(CultureInfo.InvariantCulture);
        Assert.Equal("@lifestone quest erase SentryTaskComplete #" + eraseId, h.Net.ToolRequests()[^1]);
        var erased = new QuestData("erase", "SentryTaskComplete", true, 1, false, 0);
        h.ServerMessage(ToolsWire.Reply(ToolReply.Success(eraseId, ToolVerbs.Quest, erased), erased.Describe()));
        h.Tick();
        Assert.Equal(new QuestResult("erase", "SentryTaskComplete", true, 1, false, 0), erase.Result);

        // Has: a read; a refusal by the mod is a server error with its code and sentence, and no result.
        var has = h.Api.Tools.HasQuest("AcademyTokenGiven", ActionSource.Ui);
        h.Tick(0.5);
        var hasId = "a" + has.Id.ToString(CultureInfo.InvariantCulture);
        Assert.Equal("@lifestone quest has AcademyTokenGiven #" + hasId, h.Net.ToolRequests()[^1]);
        h.ServerMessage(ToolsWire.Reply(ToolReply.Failure(hasId, ToolVerbs.Quest, new ToolError(ToolErrors.NotInWorld, "your character is not in the world yet")), "your character is not in the world yet"));
        h.Tick();
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "not-in-world: your character is not in the world yet"), has.Outcome);
        Assert.Null(has.Result);
    }

    [Fact]
    public void A_character_purge_goes_through_the_verb_path_and_confirms_with_the_counts()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();

        var purge = h.Api.Tools.PurgeCharacter("Lifestone Throwaway", ActionSource.Host);
        h.Tick();
        var id = "a" + purge.Id.ToString(CultureInfo.InvariantCulture);
        Assert.Equal("@lifestone char purge \"Lifestone Throwaway\" #" + id, h.Net.ToolRequests()[^1]);
        Assert.Equal(ActionKind.ToolCharPurge, purge.Kind);
        Assert.Contains(Enumerable.Range(0, h.State.Chat.Count), i => h.State.Chat[i].Text == "Lifestone: asking the server to purge character Lifestone Throwaway...");

        // A reply for another id does not complete it; its own does, with the typed counts.
        var purged = new CharPurgeData("Lifestone Throwaway", 1, 1, 9);
        h.ServerMessage(ToolsWire.Reply(ToolReply.Success("a999", ToolVerbs.Char, purged), purged.Describe()));
        h.Tick();
        Assert.False(purge.IsCompleted);
        h.ServerMessage(ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.Char, purged), purged.Describe()));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, purge.Outcome!.Value.Kind);
        Assert.Equal(new CharPurgeResult("Lifestone Throwaway", 1, 1, 9), purge.Result);
        Assert.Contains(Enumerable.Range(0, h.State.Chat.Count), i => h.State.Chat[i].Text == "Lifestone: purged Lifestone Throwaway: 1 character record, 1 player biota, 9 possessions");
    }

    [Theory]
    [InlineData(ToolErrors.Graduated, "Lifestone Smoke is a graduated test character and stays as it is")]
    [InlineData(ToolErrors.OwnCharacter, "you are playing Amy; log in as another character to purge it")]
    [InlineData(ToolErrors.UnknownCharacter, "no character named \"Nobody\" is on your account")]
    [InlineData(ToolErrors.CharacterOnline, "Nobody is online; only a character that is offline can be purged")]
    [InlineData(ToolErrors.NotPendingDeletion, "Nobody is not marked for deletion; delete it on the character screen first")]
    [InlineData(ToolErrors.NotInWorld, "your character is not in the world yet")]
    public void A_character_purge_the_mod_refuses_is_a_server_error_with_its_code_and_sentence_and_no_result(string code, string sentence)
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();

        var purge = h.Api.Tools.PurgeCharacter("Nobody", ActionSource.Host);
        h.Tick();
        var id = "a" + purge.Id.ToString(CultureInfo.InvariantCulture);
        h.ServerMessage(ToolsWire.Reply(ToolReply.Failure(id, ToolVerbs.Char, new ToolError(code, sentence)), sentence));
        h.Tick();
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0, code + ": " + sentence), purge.Outcome);
        Assert.Null(purge.Result);
    }

    [Fact]
    public void A_character_name_the_mod_would_refuse_is_refused_at_once_and_never_sent()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        var sent = h.Net.ToolRequests().Count;
        foreach (var name in new[] { "Bad_Name", " Ann", string.Empty, "*", new string('c', 33), null! })
        {
            var handle = h.Api.Tools.PurgeCharacter(name, ActionSource.Host);
            h.Tick(0.5);
            Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
        }

        Assert.Equal(sent, h.Net.ToolRequests().Count);
    }

    [Fact]
    public void A_quest_name_the_mod_would_refuse_is_refused_at_once_and_never_sent()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        var sent = h.Net.ToolRequests().Count;
        foreach (var name in new[] { "Bad@Name", "Sentry Task", string.Empty, "*", new string('q', 65), null! })
        {
            foreach (var handle in new[] { h.Api.Tools.StampQuest(name, ActionSource.Ui), h.Api.Tools.EraseQuest(name, ActionSource.Ui), h.Api.Tools.HasQuest(name, ActionSource.Ui) })
            {
                Assert.Equal(ActionOutcomeKind.Refused, handle.Outcome!.Value.Kind);
                Assert.Contains("quest name", handle.Outcome.Value.Detail, StringComparison.Ordinal);
            }
        }

        h.Tick(1);
        Assert.Equal(sent, h.Net.ToolRequests().Count);
    }

    [Fact]
    public void Quest_requests_follow_the_same_availability_rules_as_every_other_tool()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        h.ServerMessage(ToolsWire.UnknownCommandText, ToolsWire.UnknownCommandChatType);
        h.Tick();
        var stamp = h.Api.Tools.StampQuest("SentryTaskComplete", ActionSource.Ui);
        Assert.Equal(ActionOutcomeKind.Refused, stamp.Outcome!.Value.Kind);
        Assert.Contains("not loaded", stamp.Outcome.Value.Detail, StringComparison.Ordinal);

        var offline = new ActionHarness(offline: true);
        var has = offline.Api.Tools.HasQuest("SentryTaskComplete", ActionSource.Ui);
        Assert.Equal(ActionOutcomeKind.Refused, has.Outcome!.Value.Kind);
        Assert.Contains("offline", has.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Bad_arguments_and_an_offline_session_are_refused_at_once()
    {
        var h = new ActionHarness();
        h.Present();
        Assert.Contains("999,999,999,999", h.Api.Tools.AddXp(0, ActionSource.Ui).Outcome!.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(ActionOutcomeKind.Refused, h.Api.Tools.Place(null!, ActionSource.Ui).Outcome!.Value.Kind);

        var offline = new ActionHarness(offline: true);
        var where = offline.Api.Tools.WhereAmI(ActionSource.Ui);
        Assert.Equal(ActionOutcomeKind.Refused, where.Outcome!.Value.Kind);
        Assert.Contains("offline", where.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    private const double ActionPolicyToolSeconds = Lifestone.Client.Session.Actions.ActionPolicy.ToolSeconds;
}
