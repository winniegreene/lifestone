using Lifestone.Client.Session.Scenarios;
using Lifestone.ServerTools;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;
using static Lifestone.Loopback.Tests.Net.LiveSteps;

namespace Lifestone.Loopback.Tests.Net;

/// <summary>
/// The quest steps of the live tests: the server mod's <c>@lifestone quest stamp|erase|has &lt;name&gt;</c> for the calling
/// player's own character, sent the way <see cref="LivePlacement"/> sends <c>place</c> (the mod's own wire, one reply matched by
/// id). A test that changes a quest puts it back as it found it (<see cref="PutBackAsFound"/> after an exit scenario; an erase in a <c>finally</c> after a stamp).
/// </summary>
/// <remarks>
/// The Academy's "Exit to Holtburg" portal (weenie 29338) needs the quest <see cref="SentryTaskComplete"/> held and not
/// solvable again (ACE's Portal.cs:246-255); the quest allows one solve, so one stamp opens the portal and an erase closes it.
/// The portal then runs its own emote set (weenie 29338, EmoteCategory 4, emote 86996; ACE's Portal.cs:287), which erases the quest again and ten
/// others, so after a taken exit the character no longer holds it whatever it held before. A character never gets a quest row by any other path
/// in these tests: no test writes the database.
/// </remarks>
internal static class LiveQuests
{
    /// <summary>The quest the Holtburg Sentry stamps and the Academy's exit portal tests (world database v0.9.296, quest 4554: one solve, no delay).</summary>
    public const string SentryTaskComplete = "SentryTaskComplete";

    /// <summary>Stamps <paramref name="name"/> for the character; a refusal by the mod fails the test with its code and message.</summary>
    public static QuestData Stamp(LiveNetClient client, string name, ITestOutputHelper? output = null) => Ask(client, QuestAction.Stamp, name, output);

    /// <summary>Erases <paramref name="name"/> from the character; a quest it did not hold is an answer (<see cref="QuestData.HeldBefore"/> false), not a failure.</summary>
    public static QuestData Erase(LiveNetClient client, string name, ITestOutputHelper? output = null) => Ask(client, QuestAction.Erase, name, output);

    /// <summary>Reads whether the character holds <paramref name="name"/>; the registry is not changed.</summary>
    public static QuestData Has(LiveNetClient client, string name, ITestOutputHelper? output = null) => Ask(client, QuestAction.Has, name, output);

    /// <summary>
    /// Puts the registry back as an exit scenario found it (<see cref="RoutePlay.QuestFound"/>), whatever a failed run left: asks, and when the
    /// character holds the quest differently stamps or erases it. A scenario that passed has already done this, so a true answer from a passing
    /// run means the scenario did not.
    /// </summary>
    /// <param name="client">The client of the character the scenario played.</param>
    /// <param name="play">The scenario's play (the registry as it found it, or null when it never asked: nothing was changed).</param>
    /// <param name="output">Where the test prints.</param>
    /// <returns>True when the registry had to be changed.</returns>
    public static bool PutBackAsFound(LiveNetClient client, RoutePlay play, ITestOutputHelper? output = null)
    {
        ArgumentNullException.ThrowIfNull(play);
        if (play.QuestFound is not { } found)
        {
            output?.WriteLine("the scenario never asked about the quest: nothing to put back");
            return false;
        }

        var now = Has(client, found.Name, output);
        if (now.Held == found.Held)
        {
            return false;
        }

        output?.WriteLine($"the quest registry differs from how the scenario found it (held {found.Held}, now {now.Held}): putting it back");
        _ = found.Held ? Stamp(client, found.Name, output) : Erase(client, found.Name, output);
        return true;
    }

    private static QuestData Ask(LiveNetClient client, QuestAction action, string name, ITestOutputHelper? output)
    {
        var data = AskData<QuestData>(client, new QuestRequest(action, name, NextId()));
        output?.WriteLine($"quest {data.Action} {data.Name}: held {data.HeldBefore} -> {data.Held}, completions {data.Before} -> {data.After}");
        return data;
    }
}
