using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The player tools' <c>corpses rot</c> (M4 hero step): the request is built with the mod's own ToolsWire, the reply is matched by id and read into a typed
/// <see cref="CorpsesResult"/>, a refusal by the mod is a server error with its code and sentence and no result, and data that does not add up is a failure the
/// caller sees, not a result.
/// </summary>
public sealed class PlayerToolsCorpsesTests
{
    private const uint BlackSpawnDen = 0x0105FFFF;

    private static string IdOf(ActionHandle handle) => "a" + handle.Id.ToString(CultureInfo.InvariantCulture);

    private static ActionHarness Ready()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        return h;
    }

    private static void Reply(ActionHarness h, string id, CorpsesData data)
    {
        h.ServerMessage(ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.Corpses, data), data.Describe()));
        h.Tick();
    }

    [Fact]
    public void Rotting_corpses_sends_the_mods_request_and_confirms_with_the_typed_counts_and_landblock()
    {
        var h = Ready();
        var rot = h.Api.Tools.RotCorpses(ActionSource.Host);
        h.Tick();
        var id = IdOf(rot);
        Assert.Equal("@lifestone corpses rot #" + id, h.Net.ToolRequests()[^1]);
        Assert.Equal(ActionKind.ToolCorpsesRot, rot.Kind);
        Assert.Contains(Enumerable.Range(0, h.State.Chat.Count), i => h.State.Chat[i].Text == "Lifestone: asking the server to rot this character's corpses in the landblock...");

        // A reply for another id does not complete it; its own does.
        var data = new CorpsesData(16, 16, "0x0105FFFF");
        Reply(h, "a999", data);
        Assert.False(rot.IsCompleted);
        Reply(h, id, data);
        Assert.Equal(ActionOutcomeKind.Confirmed, rot.Outcome!.Value.Kind);
        Assert.Equal(new CorpsesResult(16, 16, BlackSpawnDen), rot.Result);
        Assert.Contains(Enumerable.Range(0, h.State.Chat.Count), i => h.State.Chat[i].Text == "Lifestone: " + data.Describe());
        Assert.Equal(ToolAvailability.Ready, h.Api.Tools.Availability);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 1)]
    [InlineData(2, 0)]
    public void None_found_and_some_already_rotting_are_confirmed_answers_not_errors(int found, int rotted)
    {
        var h = Ready();
        var rot = h.Api.Tools.RotCorpses(ActionSource.Host);
        h.Tick();
        Reply(h, IdOf(rot), new CorpsesData(found, rotted, "0xA9B4FFFF"));
        Assert.Equal(ActionOutcomeKind.Confirmed, rot.Outcome!.Value.Kind);
        Assert.Equal(new CorpsesResult(found, rotted, 0xA9B4FFFF), rot.Result);
    }

    [Theory]
    [InlineData(ToolErrors.NotInWorld, "your character is not in the world yet")]
    [InlineData(ToolErrors.NotLoopback, "Lifestone's tools answer sessions from this computer (127.0.0.1) only")]
    [InlineData(ToolErrors.Failed, "the server mod failed (InvalidOperationException); see ACE_Log.txt")]
    [InlineData(ToolErrors.BadArgs, "corpses rot takes no arguments")]
    public void A_refusal_by_the_mod_is_a_server_error_with_its_code_and_sentence_and_no_result(string code, string sentence)
    {
        var h = Ready();
        var rot = h.Api.Tools.RotCorpses(ActionSource.Host);
        h.Tick();
        h.ServerMessage(ToolsWire.Reply(ToolReply.Failure(IdOf(rot), ToolVerbs.Corpses, new ToolError(code, sentence)), sentence));
        h.Tick();
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0, code + ": " + sentence), rot.Outcome);
        Assert.Null(rot.Result);
    }

    [Theory]
    [InlineData(1, 2, "0x0105FFFF", "failed: the reply's corpse counts do not add up")]
    [InlineData(-1, 0, "0x0105FFFF", "failed: the reply's corpse counts do not add up")]
    [InlineData(2, -1, "0x0105FFFF", "failed: the reply's corpse counts do not add up")]
    [InlineData(2, 2, "0x0105", "failed: the reply's landblock is not a cell id")]
    [InlineData(2, 2, "BSD", "failed: the reply's landblock is not a cell id")]
    public void Data_that_does_not_add_up_is_a_failure_the_caller_sees_and_no_result(int found, int rotted, string landblock, string detail)
    {
        var h = Ready();
        var rot = h.Api.Tools.RotCorpses(ActionSource.Host);
        h.Tick();
        Reply(h, IdOf(rot), new CorpsesData(found, rotted, landblock));
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0, detail), rot.Outcome);
        Assert.Null(rot.Result);
        Assert.Contains(h.Log.Entries, e => e.Severity == LogSeverity.Warning && e.Message.Contains("a Lifestone tool reply was not understood", StringComparison.Ordinal));
    }

    [Fact]
    public void A_corpses_request_the_mod_never_answers_times_out()
    {
        var h = Ready();
        var rot = h.Api.Tools.RotCorpses(ActionSource.Host);
        h.Tick();
        Assert.Equal(ActionStatus.Sent, rot.Status);
        h.Tick(ActionPolicy.ToolSeconds);
        Assert.Equal(ActionOutcomeKind.TimedOut, rot.Outcome!.Value.Kind);
        Assert.Null(rot.Result);
    }

    [Fact]
    public void Corpses_are_refused_without_the_mod_offline_and_against_a_server_that_is_not_loopback_and_never_sent()
    {
        var h = Ready();
        var sent = h.Net.ToolRequests().Count;
        h.ServerMessage(ToolsWire.UnknownCommandText, ToolsWire.UnknownCommandChatType);
        h.Tick();
        var missing = h.Api.Tools.RotCorpses(ActionSource.Host);
        Assert.Equal(ActionOutcomeKind.Refused, missing.Outcome!.Value.Kind);
        Assert.Contains("not loaded", missing.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Equal(sent, h.Net.ToolRequests().Count);

        var offline = new ActionHarness(offline: true);
        var off = offline.Api.Tools.RotCorpses(ActionSource.Host);
        Assert.Equal(ActionOutcomeKind.Refused, off.Outcome!.Value.Kind);
        Assert.Contains("offline", off.Outcome.Value.Detail, StringComparison.Ordinal);

        var remote = new ActionHarness(server: new System.Net.IPEndPoint(new System.Net.IPAddress([192, 0, 2, 10]), 9000));
        remote.Present();
        var far = remote.Api.Tools.RotCorpses(ActionSource.Host);
        remote.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, far.Outcome!.Value.Kind);
        Assert.Empty(remote.Net.ToolRequests());
    }

    [Fact]
    public void The_new_kind_is_appended_so_the_existing_values_stay_stable()
    {
        Assert.Equal(ActionKind.UseManaStone + 1, ActionKind.ToolCorpsesRot);
        Assert.Equal(ActionKind.ToolCorpsesRot, Enum.GetValues<ActionKind>().Max());
    }
}
