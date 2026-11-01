using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.State;
using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M5 item 8, <c>m5-8-place-not-in-world</c> (D141, D152): a player-scope tool request waits for the world before it is sent, and meets the mod's <c>not-in-world</c> (ACE has no landblock for the player) with a bounded wait and one
/// retry, all inside the one tracker that every player-scope verb shares (ToolTracker). Scripted replies over the Action API harness.
/// </summary>
public sealed class PlayerToolsWorldWaitTests
{
    private const string NotInWorldSentence = "your character is not in the world yet";

    private static readonly PlaceData Holtburg = new("poi", "Holtburg", "0xA9B40019", 84f, 7.1f, 94f, 0.996917f, 0, 0, -0.078459f);

    private static ActionHarness Ready()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick(1);
        return h;
    }

    private static string IdOf(ActionHandle handle) => "a" + handle.Id.ToString(CultureInfo.InvariantCulture);

    private static void Refuse(ActionHarness h, string id, string verb = ToolVerbs.Place) =>
        h.ServerMessage(ToolsWire.Reply(ToolReply.Failure(id, verb, new ToolError(ToolErrors.NotInWorld, NotInWorldSentence)), NotInWorldSentence));

    private static void WhereOk(ActionHarness h, string id)
    {
        var where = new WhereData("0xA9B40019", 84.5f, 7.1f, 94.005f, 1, 0, 0, 0, "0xA9B4FFFF", "0xA9B40019", false, 3, 2);
        h.ServerMessage(ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.Where, where), where.Describe()));
    }

    private static bool Logged(ActionHarness h, string text) =>
        h.Log.Entries.Any(e => e.Message.Contains(text, StringComparison.Ordinal));

    private static int PlacesSent(ActionHarness h) =>
        h.Net.ToolRequests().Count(t => t!.StartsWith("@lifestone place ", StringComparison.Ordinal));

    [Fact]
    public void M5_8_a_first_not_in_world_waits_for_the_mods_where_then_retries_once_and_counts_one_place()
    {
        var h = Ready();
        var mark = h.Api.Tools.Verbs.Counts;
        var place = h.Api.Tools.Place(TeleportCatalog.Default().Find("Holtburg")!, ActionSource.Host);
        h.Tick(1);
        var id = IdOf(place);
        var request = h.Net.ToolRequests()[^1];
        Assert.Equal("@lifestone place poi \"Holtburg\" #" + id, request);

        // The mod's refusal does not end the request: it waits, logged once, and sends nothing yet.
        var sent = h.Net.ToolRequests().Count;
        Refuse(h, id);
        h.Tick();
        Assert.False(place.IsCompleted);
        Assert.Equal(sent, h.Net.ToolRequests().Count);
        Assert.True(Logged(h, "the mod refused a player-tool request: not-in-world"));

        // About a second on, the mod's where is asked (silent, its own id); ACE still has no landblock.
        h.Tick(ActionPolicy.ToolWorldProbeSeconds);
        Assert.Equal("@lifestone where #" + id + "-w", h.Net.ToolRequests()[^1]);
        Refuse(h, id + "-w", ToolVerbs.Where);
        h.Tick();
        Assert.False(place.IsCompleted);
        Assert.Equal(sent + 1, h.Net.ToolRequests().Count);

        // The next probe is answered: the same request goes out once more, and its reply completes the one handle.
        h.Tick(ActionPolicy.ToolWorldProbeSeconds);
        Assert.Equal(sent + 2, h.Net.ToolRequests().Count);
        WhereOk(h, id + "-w");
        h.Tick();
        Assert.Equal(request, h.Net.ToolRequests()[^1]);
        Assert.Equal(sent + 3, h.Net.ToolRequests().Count);
        Assert.False(place.IsCompleted);
        Assert.True(Logged(h, "sending the player-tool request once more"));

        h.ServerMessage(ToolsWire.Reply(ToolReply.Success(id, ToolVerbs.Place, Holtburg), Holtburg.Describe()));
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, place.Outcome!.Value.Kind);
        Assert.True(Logged(h, "the retried player-tool request was carried out by the mod"));

        // The mod carried out one place, and the counter says one: the retry is not a second place and not a grant.
        var delta = h.Api.Tools.Verbs.Since(mark);
        Assert.Equal(1, delta.Place);
        Assert.Equal(0, delta.Granted);
        Assert.Equal(0, delta.RecoveryPlace);
    }

    [Fact]
    public void M5_8_two_refusals_fail_with_the_named_reason_and_a_third_request_is_never_sent()
    {
        var h = Ready();
        var mark = h.Api.Tools.Verbs.Counts;
        var place = h.Api.Tools.Place(TeleportCatalog.Default().Find("Holtburg")!, ActionSource.Host);
        h.Tick(1);
        var id = IdOf(place);
        Refuse(h, id);
        h.Tick(ActionPolicy.ToolWorldProbeSeconds);
        WhereOk(h, id + "-w");
        h.Tick();
        Assert.Equal(2, PlacesSent(h)); // the first send and the one retry

        Refuse(h, id);
        h.Tick();
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "not-in-world: " + NotInWorldSentence), place.Outcome);
        Assert.True(Logged(h, "refused again"));

        h.Tick(30);
        Assert.Equal(2, PlacesSent(h));
        Assert.Equal(1, h.Api.Tools.Verbs.Since(mark).Place);
    }

    [Fact]
    public void M5_8_a_player_that_never_gets_a_landblock_fails_named_after_the_bounded_wait_without_a_retry()
    {
        var h = Ready();
        var place = h.Api.Tools.Place(TeleportCatalog.Default().Find("Holtburg")!, ActionSource.Host);
        h.Tick(1);
        var id = IdOf(place);
        Refuse(h, id);
        var probes = 0;
        for (var i = 0; i < 40 && !place.IsCompleted; i++)
        {
            h.Tick(ActionPolicy.ToolWorldProbeSeconds);
            if (h.Net.ToolRequests()[^1] == "@lifestone where #" + id + "-w")
            {
                probes++;
                Refuse(h, id + "-w", ToolVerbs.Where);
                h.Tick();
            }
        }

        Assert.True(place.IsCompleted);
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "not-in-world: " + NotInWorldSentence), place.Outcome);
        Assert.InRange(probes, 15, 21);
        Assert.Equal(1, PlacesSent(h));
        Assert.True(Logged(h, "never got a landblock"));
    }

    [Fact]
    public void M5_8_a_client_not_yet_in_the_world_sends_nothing_until_it_is()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        var mark = h.Api.Tools.Verbs.Counts;
        var where = h.Api.Tools.WhereAmI(ActionSource.Host);
        var xp = h.Api.Tools.AddXp(1000, ActionSource.Host);
        h.Tick(1);
        h.Tick(5);
        Assert.False(where.IsCompleted);
        Assert.False(xp.IsCompleted);
        Assert.Empty(h.Net.ToolRequests());
        Assert.True(Logged(h, "waits for the world"));

        h.Present();
        h.Tick(1);
        h.Tick(1);
        var sent = h.Net.ToolRequests();
        Assert.Contains(sent, t => t!.StartsWith("@lifestone where #", StringComparison.Ordinal));
        Assert.Contains(sent, t => t!.StartsWith("@lifestone xp ", StringComparison.Ordinal));
        Assert.True(Logged(h, "the player is in the world: sending the request that waited for it"));
        Assert.Equal(1, h.Api.Tools.Verbs.Since(mark).Xp);
    }

    [Fact]
    public void M5_8_a_client_that_never_gets_into_the_world_is_refused_named_after_the_bounded_wait()
    {
        var h = new ActionHarness();
        h.AtCharacterSelect();
        var place = h.Api.Tools.Place(TeleportCatalog.Default().Find("Holtburg")!, ActionSource.Host);
        h.Tick(1);
        Assert.False(place.IsCompleted);
        h.Tick(ActionPolicy.ToolWorldWaitSeconds);
        Assert.Equal(ActionOutcomeKind.Refused, place.Outcome!.Value.Kind);
        Assert.Contains("not in the world", place.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Empty(h.Net.ToolRequests());
    }

    [Fact]
    public void M5_8_a_verb_that_needs_no_player_is_not_retried()
    {
        var h = Ready();
        var status = h.Api.Tools.Status(ActionSource.Host);
        h.Tick(1);
        Refuse(h, IdOf(status), ToolVerbs.Status);
        h.Tick();
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "not-in-world: " + NotInWorldSentence), status.Outcome);
    }
}
