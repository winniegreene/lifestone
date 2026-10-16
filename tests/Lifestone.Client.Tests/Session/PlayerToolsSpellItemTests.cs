using System.Globalization;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.ServerTools;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The player tools' <c>spell learn</c> and <c>item</c> (M3 1.1 s2b): the requests are built with the mod's own ToolsWire, the replies are matched by id and read into typed
/// results, an unknown spell id is a failure the caller sees by name, an item's refusals are typed, and the limits the mod would enforce are refused before sending.
/// </summary>
public sealed class PlayerToolsSpellItemTests
{
    private static string IdOf(ActionHandle handle) => "a" + handle.Id.ToString(CultureInfo.InvariantCulture);

    private static ActionHarness Ready()
    {
        var h = new ActionHarness();
        h.Present();
        h.Tick();
        return h;
    }

    private static void Reply<T>(ActionHarness h, string id, string verb, T data, string line)
        where T : class
    {
        h.ServerMessage(ToolsWire.Reply(ToolReply.Success(id, verb, data), line));
        h.Tick();
    }

    private static void Fail(ActionHarness h, string id, string verb, string code, string sentence)
    {
        h.ServerMessage(ToolsWire.Reply(ToolReply.Failure(id, verb, new ToolError(code, sentence)), sentence));
        h.Tick();
    }

    [Fact]
    public void Learning_spells_sends_the_mods_request_and_confirms_with_each_id_learned_or_known()
    {
        var h = Ready();
        var learn = h.Api.Tools.LearnSpells([2, 3, 5], ActionSource.Host);
        h.Tick();
        var id = IdOf(learn);
        Assert.Equal("@lifestone spell learn 2,3,5 #" + id, h.Net.ToolRequests()[^1]);
        Assert.Equal(ActionKind.ToolSpell, learn.Kind);
        Assert.Contains(Enumerable.Range(0, h.State.Chat.Count), i => h.State.Chat[i].Text == "Lifestone: asking the server to teach 3 spells...");

        // A reply for another id does not complete it; its own does, with the typed outcomes in request order.
        var data = SpellLearnData.Of([new SpellOutcome(2, SpellOutcomes.Learned, "Strength Self I"), new SpellOutcome(3, SpellOutcomes.Known, "Endurance Self I"), new SpellOutcome(5, SpellOutcomes.Learned, "Coordination Self I")]);
        Reply(h, "a999", ToolVerbs.Spell, data, data.Describe());
        Assert.False(learn.IsCompleted);
        Reply(h, id, ToolVerbs.Spell, data, data.Describe());
        Assert.Equal(ActionOutcomeKind.Confirmed, learn.Outcome!.Value.Kind);
        var result = learn.Result!;
        Assert.True(result.Ok);
        Assert.Equal((2, 1, 0), (result.Learned, result.Known, result.Unknown));
        Assert.Equal(
            [new SpellLearnEntry(2, SpellOutcomeKind.Learned, "Strength Self I"), new SpellLearnEntry(3, SpellOutcomeKind.Known, "Endurance Self I"), new SpellLearnEntry(5, SpellOutcomeKind.Learned, "Coordination Self I")],
            result.Spells);
        Assert.False(result.AllAlreadyKnown);
        Assert.Empty(result.UnknownIds);
    }

    [Fact]
    public void A_spellbook_the_character_already_has_reads_as_all_already_known()
    {
        var h = Ready();
        var learn = h.Api.Tools.LearnSpells([2, 3], ActionSource.Host);
        h.Tick();
        var data = SpellLearnData.Of([new SpellOutcome(2, SpellOutcomes.Known, "A"), new SpellOutcome(3, SpellOutcomes.Known, "B")]);
        Reply(h, IdOf(learn), ToolVerbs.Spell, data, data.Describe());
        Assert.Equal(ActionOutcomeKind.Confirmed, learn.Outcome!.Value.Kind);
        Assert.True(learn.Result!.AllAlreadyKnown);
    }

    [Fact]
    public void An_unknown_spell_id_fails_the_request_by_name_and_keeps_the_result_with_the_others_carried_out()
    {
        var h = Ready();
        var learn = h.Api.Tools.LearnSpells([2, 99999, 77777], ActionSource.Host);
        h.Tick();
        var data = SpellLearnData.Of([new SpellOutcome(2, SpellOutcomes.Learned, "Strength Self I"), new SpellOutcome(99999, SpellOutcomes.Unknown), new SpellOutcome(77777, SpellOutcomes.Unknown)]);
        Reply(h, IdOf(learn), ToolVerbs.Spell, data, data.Describe());

        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "unknown-spell: not in the spell table: 99999, 77777"), learn.Outcome);
        var result = learn.Result!;
        Assert.False(result.Ok);
        Assert.Equal(new uint[] { 99999, 77777 }, result.UnknownIds);
        Assert.Equal(SpellOutcomeKind.Learned, result.Spells[0].Outcome);
        Assert.Null(result.Spells[1].Name);
    }

    [Fact]
    public void A_spell_outcome_word_the_client_does_not_know_is_a_failed_reply_not_a_guess()
    {
        var h = Ready();
        var learn = h.Api.Tools.LearnSpells([2], ActionSource.Host);
        h.Tick();
        var data = new SpellLearnData([new SpellOutcome(2, "maybe")], 0, 0, 0);
        Reply(h, IdOf(learn), ToolVerbs.Spell, data, "spells");
        Assert.Equal(ActionOutcomeKind.ServerError, learn.Outcome!.Value.Kind);
        Assert.StartsWith("failed: ", learn.Outcome.Value.Detail, StringComparison.Ordinal);
        Assert.Null(learn.Result);
    }

    [Fact]
    public void The_mods_refusals_of_a_spell_learn_are_server_errors_with_their_code_and_sentence_and_no_result()
    {
        var h = Ready();
        var learn = h.Api.Tools.LearnSpells([2], ActionSource.Host);
        h.Tick();
        Fail(h, IdOf(learn), ToolVerbs.Spell, ToolErrors.NotInWorld, "your character is not in the world yet");
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0, "not-in-world: your character is not in the world yet"), learn.Outcome);
        Assert.Null(learn.Result);
    }

    [Fact]
    public void A_list_of_no_ids_or_more_than_128_is_refused_at_once_and_never_sent_and_128_is_sent()
    {
        var h = Ready();
        var sent = h.Net.ToolRequests().Count;
        var none = h.Api.Tools.LearnSpells([], ActionSource.Host);
        var many = h.Api.Tools.LearnSpells(Enumerable.Range(1, PlayerTools.MaxSpellIds + 1).Select(i => (uint)i).ToArray(), ActionSource.Host);
        var missing = h.Api.Tools.LearnSpells(null!, ActionSource.Host);
        h.Tick(0.5);
        foreach (var refused in new[] { none, many, missing })
        {
            Assert.Equal(ActionOutcomeKind.Refused, refused.Outcome!.Value.Kind);
            Assert.Null(refused.Result);
        }

        Assert.Equal(sent, h.Net.ToolRequests().Count);

        var edge = h.Api.Tools.LearnSpells(Enumerable.Range(1, PlayerTools.MaxSpellIds).Select(i => (uint)i).ToArray(), ActionSource.Host);
        h.Tick(0.5);
        Assert.Equal(ActionStatus.Sent, edge.Status);
        Assert.Equal(sent + 1, h.Net.ToolRequests().Count);
    }

    [Fact]
    public void A_spell_learn_the_mod_never_answers_times_out()
    {
        var h = Ready();
        var learn = h.Api.Tools.LearnSpells([2], ActionSource.Host);
        h.Tick();
        Assert.Equal(ActionStatus.Sent, learn.Status);
        h.Tick(ActionPolicy.ToolSeconds);
        Assert.Equal(ActionOutcomeKind.TimedOut, learn.Outcome!.Value.Kind);
        Assert.Null(learn.Result);
    }

    [Fact]
    public void Spawning_an_item_sends_the_mods_request_and_confirms_with_the_typed_result()
    {
        var h = Ready();
        var item = h.Api.Tools.SpawnItem(273, 5, ActionSource.Host);
        h.Tick();
        var id = IdOf(item);
        Assert.Equal("@lifestone item 273 5 #" + id, h.Net.ToolRequests()[^1]);
        Assert.Equal(ActionKind.ToolItem, item.Kind);
        Assert.Equal(ItemOutcome.Pending, ItemOutcomes.Of(item));
        Assert.Contains(Enumerable.Range(0, h.State.Chat.Count), i => h.State.Chat[i].Text == "Lifestone: asking the server for item 273 x 5...");

        var data = new ItemData(2147483939, 273, "Pyreal", 5, 5, 25000, false);
        Reply(h, "a999", ToolVerbs.Item, data, data.Describe());
        Assert.False(item.IsCompleted);
        Reply(h, id, ToolVerbs.Item, data, data.Describe());
        Assert.Equal(ActionOutcomeKind.Confirmed, item.Outcome!.Value.Kind);
        Assert.Equal(new ItemResult(2147483939, 273, "Pyreal", 5, 5, 25000, false), item.Result);
        Assert.Equal(ItemOutcome.Created, ItemOutcomes.Of(item));
    }

    [Fact]
    public void An_item_without_a_stack_is_requested_without_one_and_a_clamped_stack_is_a_result_not_an_error()
    {
        var h = Ready();
        var plain = h.Api.Tools.SpawnItem(273, null, ActionSource.Host);
        h.Tick();
        Assert.Equal("@lifestone item 273 #" + IdOf(plain), h.Net.ToolRequests()[^1]);
        var one = new ItemData(2147483940, 273, "Pyreal", 1, null, 25000, false);
        Reply(h, IdOf(plain), ToolVerbs.Item, one, one.Describe());
        Assert.Equal(new ItemResult(2147483940, 273, "Pyreal", 1, null, 25000, false), plain.Result);

        var big = h.Api.Tools.SpawnItem(273, 60000, ActionSource.Host);
        h.Tick(0.5);
        Assert.Equal("@lifestone item 273 60000 #" + IdOf(big), h.Net.ToolRequests()[^1]);
        var clamped = new ItemData(2147483941, 273, "Pyreal", 25000, 60000, 25000, true);
        Reply(h, IdOf(big), ToolVerbs.Item, clamped, clamped.Describe());
        Assert.Equal(ActionOutcomeKind.Confirmed, big.Outcome!.Value.Kind);
        Assert.Equal((25000, 60000, 25000, true), (big.Result!.Stack, big.Result.Requested, big.Result.MaxStack, big.Result.Clamped));
        Assert.Equal(ItemOutcome.Created, ItemOutcomes.Of(big));
    }

    [Theory]
    [InlineData(ToolErrors.UnknownWcid, "no weenie has class id 99999999", ItemOutcome.UnknownWcid)]
    [InlineData(ToolErrors.NotCreatable, "weenie 5 is a Portal, which ACE never spawns", ItemOutcome.NotCreatable)]
    [InlineData(ToolErrors.NoRoom, "your pack has no room for Pyreal; make space or lighten your load", ItemOutcome.NoRoom)]
    [InlineData(ToolErrors.Busy, "you are logging out", ItemOutcome.Failed)]
    public void The_mods_item_refusals_are_typed_server_errors_with_no_result(string code, string sentence, ItemOutcome expected)
    {
        var h = Ready();
        var item = h.Api.Tools.SpawnItem(99999999, 1, ActionSource.Host);
        h.Tick();
        Fail(h, IdOf(item), ToolVerbs.Item, code, sentence);
        Assert.Equal(new ActionOutcome(ActionOutcomeKind.ServerError, 0, code + ": " + sentence), item.Outcome);
        Assert.Null(item.Result);
        Assert.Equal(expected, ItemOutcomes.Of(item));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void A_stack_outside_1_to_65535_is_refused_at_once_and_never_sent(int stack)
    {
        var h = Ready();
        var sent = h.Net.ToolRequests().Count;
        var item = h.Api.Tools.SpawnItem(273, stack, ActionSource.Host);
        h.Tick(0.5);
        Assert.Equal(ActionOutcomeKind.Refused, item.Outcome!.Value.Kind);
        Assert.Equal(ItemOutcome.Failed, ItemOutcomes.Of(item));
        Assert.Equal(sent, h.Net.ToolRequests().Count);
    }

    [Fact]
    public void An_item_request_the_mod_never_answers_times_out()
    {
        var h = Ready();
        var item = h.Api.Tools.SpawnItem(273, 1, ActionSource.Host);
        h.Tick();
        h.Tick(ActionPolicy.ToolSeconds);
        Assert.Equal(ActionOutcomeKind.TimedOut, item.Outcome!.Value.Kind);
        Assert.Null(item.Result);
        Assert.Equal(ItemOutcome.Failed, ItemOutcomes.Of(item));
    }

    [Fact]
    public void The_two_new_kinds_are_appended_so_the_existing_values_stay_stable()
    {
        Assert.Equal(ActionKind.TrainSkill + 1, ActionKind.ToolSpell);
        Assert.Equal(ActionKind.ToolSpell + 1, ActionKind.ToolItem);
        // M3 1.2 s4a appends the five world actions after ToolItem (WorldActionStubTests pins them); nothing is inserted before it.
        Assert.Equal(ActionKind.ToolItem + 1, ActionKind.CastSpell);
    }
}
