using Lifestone.Data.Dat;
using Lifestone.Loopback.Tests.Net;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;
using static Lifestone.Loopback.Tests.Net.LivePlacement;
using static Lifestone.Loopback.Tests.Net.LiveSteps;

namespace Lifestone.Loopback.Tests.ServerMod;

/// <summary>
/// SM9's <c>xp</c> scenario: WRITTEN, NOT RUN BY DEFAULT. <c>@lifestone xp</c> adds real experience to the character, which raises its level for good
/// (the last step asks for 999,999,999,999, which takes a character to the level cap), so it runs only with the owner's go-ahead on a character the
/// owner agrees to spend (wave 3 plan, section 4, L3 and owner decision 4). The test carries a fixed <c>Skip</c> reason for that: to run it, remove the
/// <c>Skip</c> argument in the source, in a working copy, after the owner's yes; it is never run by <c>--filter</c> alone. It also carries
/// <c>Tier=OwnerApproved</c> so a run can be filtered with <c>Tier!=OwnerApproved</c> either way.
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Trait("Tier", "OwnerApproved")]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
[TestCaseOrderer("Lifestone.Loopback.Tests.Net.LiveStepOrderer", "Lifestone.Loopback.Tests")]
public sealed class ModXpLiveTests(ITestOutputHelper output)
{
    /// <summary>Why the test is skipped.</summary>
    public const string NeedsOwner = "Adds experience and so raises the loopback character's level for good: runs only with the owner's go-ahead (remove this Skip in the source). See the class comment.";

    /// <summary>
    /// What it proves, in one session on the loopback character:
    /// <list type="bullet">
    /// <item>a small <c>xp</c> is carried out: <c>Added == Requested</c> below the cap, the level and the experience are consistent, and the chat line the
    /// player sees says how much was added;</item>
    /// <item>two <c>xp</c> requests sent back to back (ACE reads them in the same world tick) each report their own <c>Added == Requested</c>, and the totals
    /// differ by exactly the later one's amount (the mod measures around its own update, however much else is queued);</item>
    /// <item>the lowest amount (1) and the highest the parser accepts (999,999,999,999) are carried out: the high one reports what ACE added (at most what
    /// was asked, at most what the level cap leaves), the total grows by exactly that, and the level never falls.</item>
    /// </list>
    /// The over-limit refusals (0 and 1,000,000,000,000) do not change anything and are in <see cref="ModVerbsLiveTests"/>.
    /// </summary>
    [LoopbackFact(Skip = NeedsOwner)]
    [LiveStep(40)]
    public void Xp_adds_what_was_asked_in_one_tick_or_two_and_at_the_limits()
    {
        using var dats = DatSet.Open();
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        var start = default(Lifestone.Net.Types.AcPosition);
        try
        {
            start = EnterReadyAtStart(client, output);

            var first = AskData<XpData>(client, new XpRequest(1, NextId()));
            output.WriteLine(first.Describe());
            Assert.Equal(1, first.Requested);
            Assert.Equal(1, first.Added);
            Assert.True(first.Total >= 1 && first.Available >= 0 && first.Level >= 1);

            // The chat reply: the first line of the message the player sees.
            var id = NextId();
            var chat = AskText(client, ToolsWire.Request(new XpRequest(5, id)), ToolVerbs.Xp, id);
            var five = chat.Reply!.DataAs<XpData>()!;
            Assert.Equal(5, five.Added);
            Assert.Equal(first.Total + 5, five.Total);
            Assert.Equal(ToolsWire.ChatPrefix + five.Describe(), chat.ChatLine);

            // Two requests in the same tick.
            var (idA, idB) = (NextId(), NextId());
            var both = AskTexts(
                client,
                [
                    (ToolsWire.Request(new XpRequest(10, idA)), ToolVerbs.Xp, idA),
                    (ToolsWire.Request(new XpRequest(20, idB)), ToolVerbs.Xp, idB),
                ]);
            var a = both[0].Reply!.DataAs<XpData>()!;
            var b = both[1].Reply!.DataAs<XpData>()!;
            output.WriteLine($"same tick: {a.Describe()} | {b.Describe()}");
            Assert.Equal((10, 10), (a.Requested, a.Added));
            Assert.Equal((20, 20), (b.Requested, b.Added));
            var (low, high) = a.Total <= b.Total ? (a, b) : (b, a);
            Assert.Equal(high.Added, high.Total - low.Total);
            Assert.Equal(five.Total + 30, high.Total);

            // The limits.
            var most = AskData<XpData>(client, new XpRequest(ToolRequestParser.MaxXp, NextId()));
            output.WriteLine(most.Describe());
            Assert.Equal(ToolRequestParser.MaxXp, most.Requested);
            Assert.InRange(most.Added, 1, most.Requested);
            Assert.Equal(high.Total + most.Added, most.Total);
            Assert.True(most.Level >= first.Level);

            LogOffAndQuit(client);
        }
        finally
        {
            RestoreBestEffort(client, start, output);
            LeaveTheWorld(client);
        }
    }
}
