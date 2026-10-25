using Lifestone.Client.Session.Mage;
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
/// The mod's <c>spell learn</c> and <c>item</c> verbs on Lifestone Throwaway (M4 P5b G0, D39): the throwaway lives on the <c>test-tools</c> account, so these rows moved out of
/// <see cref="ModVerbsLiveTests"/> (which stays on <c>test</c> with the Loopback character) into a class of their own. Same steps, same asserts. Skipped, with the probe's reason,
/// unless the backend runs with the mod ready and secrets.json has the <c>test-tools</c> account.
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[LoopbackCharacter("Lifestone Throwaway")]
[TestCaseOrderer("Lifestone.Loopback.Tests.Net.LiveStepOrderer", "Lifestone.Loopback.Tests")]
public sealed class ModVerbsThrowawayLiveTests(ITestOutputHelper output)
{
    /// <summary>
    /// M3 4.2 p5b C11: the three Throwaway verb rows share one session: <c>spell learn</c>, <c>item</c> and the 128-id <c>spell learn</c>. Each section keeps its
    /// asserts and runs even when an earlier one failed; a failure names the verb: <c>[spell learn] ...</c>.
    /// </summary>
    [LoopbackFact]
    [LiveStep(35)]
    public void The_spell_learn_and_item_verbs_answer_as_the_mod_says_on_the_throwaway_in_one_session()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, Throwaway);
        var failures = new List<string>();
        try
        {
            EnterTheWorld(client);
            Unhide(client);
            Section(failures, "spell learn", () => SpellLearn(client));
            Section(failures, "item", () => Item(client));
            Section(failures, "spell learn x128", () => SpellLearn128(client));
            LogOffAndQuit(client);
        }
        finally
        {
            LeaveTheWorld(client);
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>Runs one verb's section; a failure is kept with the verb's name so the one test's failure says which verb broke.</summary>
    private static void Section(List<string> failures, string verb, Action body)
    {
        try
        {
            body();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add($"[{verb}] {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>The character the M3 verb rows use: the throwaway, never a graduated character (m3-plan 7, D3). Created on the test account when missing.</summary>
    private const string Throwaway = "Lifestone Throwaway";

    /// <summary>A cheap level-one spell: ACE's SpellId enum counts StrengthSelf1 as 2 (ACE.Entity/Enum/SpellId.cs, after Undef and StrengthOther1).</summary>
    private const uint CheapSpell = 2;

    /// <summary>Not in the spell table: above a u16, which the spell-update event cannot carry, and the highest u16.</summary>
    private const uint UnknownSpellHigh = 4_000_000_000;

    private const uint UnknownSpellLow = 65_535;

    /// <summary>ACE's <c>coinstack</c> (the pyreal), a stackable weenie: ACE.Server/Factories/Enum/WeenieClassName.cs:253.</summary>
    private const uint Pyreal = 273;

    /// <summary>
    /// WRITTEN AND COMPILED, RUN BY THE LEAD (m3 1.1 s2a). What it proves, on <c>Lifestone Throwaway</c>: <c>spell learn</c> of one cheap spell is answered
    /// <c>learned</c> (a fresh throwaway) or <c>known</c> (one that has it from an earlier run) and a second request for it is answered <c>known</c>, which is how
    /// a ready mage is verified; the reply names the spell and its counts match the outcomes; an id mixed into a list that is not in ACE's spell table
    /// (above and below the u16 range) is answered <c>unknown</c> with no name while the others in the list are still answered, the whole request is
    /// <c>ok</c>, and the chat line is the mod's sentence; a list of 129 ids is refused as <c>bad-args</c> with no data.
    /// </summary>
    private void SpellLearn(LiveNetClient client)
    {
        var first = AskData<SpellLearnData>(client, new SpellLearnRequest([CheapSpell], NextId()));
        output.WriteLine("first: " + first.Describe());
        var one = Assert.Single(first.Spells);
        Assert.Equal(CheapSpell, one.Id);
        Assert.Contains(one.Outcome, new[] { SpellOutcomes.Learned, SpellOutcomes.Known });
        Assert.False(string.IsNullOrWhiteSpace(one.Name));
        Assert.Equal(one.Outcome == SpellOutcomes.Learned ? (1, 0, 0) : (0, 1, 0), (first.Learned, first.Known, first.Unknown));

        var id = NextId();
        var chat = AskText(client, ToolsWire.Request(new SpellLearnRequest([CheapSpell], id)), ToolVerbs.Spell, id);
        Assert.True(chat.Reply!.Ok, chat.Reply.Message);
        var second = chat.Reply.DataAs<SpellLearnData>()!;
        output.WriteLine("second: " + second.Describe());
        Assert.Equal([new SpellOutcome(CheapSpell, SpellOutcomes.Known, one.Name)], second.Spells);
        Assert.Equal((0, 1, 0), (second.Learned, second.Known, second.Unknown));
        Assert.Equal(ToolsWire.ChatPrefix + second.Describe(), chat.ChatLine);

        // An id the spell table lacks is marked unknown and nothing is learned for it; the ids around it are still answered.
        var mixed = AskData<SpellLearnData>(client, new SpellLearnRequest([CheapSpell, UnknownSpellLow, UnknownSpellHigh, CheapSpell], NextId()));
        output.WriteLine("mixed: " + mixed.Describe());
        Assert.Equal(
            [SpellOutcomes.Known, SpellOutcomes.Unknown, SpellOutcomes.Unknown, SpellOutcomes.Known],
            mixed.Spells.Select(s => s.Outcome));
        Assert.Equal([CheapSpell, UnknownSpellLow, UnknownSpellHigh, CheapSpell], mixed.Spells.Select(s => s.Id));
        Assert.All(mixed.Spells.Where(s => s.Outcome == SpellOutcomes.Unknown), s => Assert.Null(s.Name));
        Assert.Equal((0, 2, 2), (mixed.Learned, mixed.Known, mixed.Unknown));

        // More ids than the verb takes is refused, with no data.
        var tooMany = NextId();
        var refused = AskText(client, "@lifestone spell learn " + string.Join(',', Enumerable.Repeat(CheapSpell, ToolRequestParser.MaxSpellIds + 1)) + " #" + tooMany, ToolVerbs.Spell, tooMany);
        Assert.False(refused.Reply!.Ok);
        Assert.Equal(ToolErrors.BadArgs, refused.Reply.Error);
        Assert.Null(refused.Reply.Data);
    }

    /// <summary>
    /// WRITTEN AND COMPILED, RUN BY THE LEAD (m3 1.1 s2a). What it proves, on <c>Lifestone Throwaway</c>: <c>item</c> of a plain stackable weenie (the pyreal)
    /// creates it in the pack, and the reply names a new object id, the wcid, the name and the stack asked for (not clamped, with the weenie's own maximum);
    /// a stack above the weenie's maximum is clamped to it as ACE's <c>@ci</c> does and the reply says so (<c>clamped</c>, <c>requested</c> and <c>maxStack</c>
    /// agree with <c>stack</c>); a weenie class id the world database lacks is refused as <c>unknown-wcid</c> with no data and nothing created; and (M3 4.2 P7) the
    /// Platemail Gauntlets asked for in palette 21 and shade 0.5 come back holding both (ACE's own object, read back), the same item asked for plainly reports
    /// none, and palette 94 is refused as <c>bad-args</c>.
    /// </summary>
    private void Item(LiveNetClient client)
    {
        var id = NextId();
        var chat = AskText(client, ToolsWire.Request(new ItemRequest(Pyreal, 5, id)), ToolVerbs.Item, id);
        Assert.True(chat.Reply!.Ok, chat.Reply.Message);
        var made = chat.Reply.DataAs<ItemData>()!;
        output.WriteLine(made.Describe());
        Assert.Equal(Pyreal, made.Wcid);
        Assert.NotEqual(0u, made.Guid);
        Assert.False(string.IsNullOrWhiteSpace(made.Name));
        Assert.Equal(5, made.Stack);
        Assert.Equal(5, made.Requested);
        Assert.False(made.Clamped);
        Assert.True(made.MaxStack is >= 5, "a pyreal stack should hold at least 5");
        Assert.Equal(ToolsWire.ChatPrefix + made.Describe(), chat.ChatLine);

        var big = AskData<ItemData>(client, new ItemRequest(Pyreal, ToolRequestParser.MaxItemStack, NextId()));
        output.WriteLine(big.Describe());
        Assert.NotEqual(made.Guid, big.Guid);
        Assert.Equal(ToolRequestParser.MaxItemStack, big.Requested);
        Assert.NotNull(big.MaxStack);
        Assert.Equal(Math.Min(ToolRequestParser.MaxItemStack, big.MaxStack!.Value), big.Stack);
        Assert.Equal(big.Stack < ToolRequestParser.MaxItemStack, big.Clamped);

        // M3 4.2 P7: the Platemail Gauntlets (57) in the Gold palette: ACE's own object holds the palette and shade the request named (the reply reads them back).
        var gold = AskData<ItemData>(client, new ItemRequest(57, null, NextId(), 21, 0.5));
        output.WriteLine(gold.Describe());
        Assert.Equal(57u, gold.Wcid);
        Assert.Equal(21, gold.Palette);
        Assert.Equal(0.5, gold.Shade);
        var plain = AskData<ItemData>(client, new ItemRequest(57, null, NextId()));
        Assert.Null(plain.Palette);
        // An out-of-range palette never reaches the wire from the client (the writer throws), so the refusal is the parser's: sent as raw text it comes back bad-args.
        var raw = NextId();
        var refused = AskText(client, "@lifestone item 57 palette=94 #" + raw, ToolVerbs.Item, raw);
        Assert.False(refused.Reply!.Ok);
        Assert.Equal(ToolErrors.BadArgs, refused.Reply.Error);

        var missing = Ask(client, new ItemRequest(4_000_000_000, null, NextId()));
        Assert.False(missing.Ok);
        Assert.Equal(ToolErrors.UnknownWcid, missing.Error);
        Assert.Equal(ToolVerbs.Item, missing.Verb);
        Assert.Null(missing.Data);
    }

    /// <summary>
    /// WRITTEN AND COMPILED, RUN BY THE LEAD (m3 1.1 fix, 1.1-R9). What it proves, on <c>Lifestone Throwaway</c> only: the mage build's largest request goes out and
    /// comes back. One <c>spell learn</c> of exactly the first 128 ids of <see cref="ArenaProfile.SpellsToLearn"/> is a Talk of about 640 bytes, more than one
    /// fragment holds (448), so it leaves as a multi-fragment game message; ACE and the mod accept it, the reply carries 128 outcomes in the order asked, and
    /// every one is <c>learned</c> or <c>known</c> (none <c>unknown</c>: every id of the profile is in the spell table). It teaches only the Throwaway, a
    /// character the rules let the loops level freely; BSD Mage is not touched before its permanent build.
    /// </summary>
    private void SpellLearn128(LiveNetClient client)
    {
        var ids = ArenaProfile.SpellsToLearn.Take(ToolRequestParser.MaxSpellIds).ToArray();
        Assert.Equal(ToolRequestParser.MaxSpellIds, ids.Length);
        Assert.True(ToolsWire.Request(new SpellLearnRequest(ids, "1")).Length > 448, "the request should need more than one fragment");

        var learned = AskData<SpellLearnData>(client, new SpellLearnRequest(ids, NextId()));
        output.WriteLine("128 ids: " + learned.Describe());
        Assert.Equal(ids.Length, learned.Spells.Count);
        Assert.Equal(ids, learned.Spells.Select(s => s.Id).ToArray());
        Assert.All(learned.Spells, s => Assert.Contains(s.Outcome, new[] { SpellOutcomes.Learned, SpellOutcomes.Known }));
        Assert.Equal(0, learned.Unknown);
        Assert.Equal(ids.Length, learned.Learned + learned.Known);
    }

}
