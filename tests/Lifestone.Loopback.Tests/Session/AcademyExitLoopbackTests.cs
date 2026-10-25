using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Recording;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Dat;
using Lifestone.Client.Tests;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;
using static Lifestone.Loopback.Tests.Net.LivePlacement;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// DoD 8 against the bundled ACE (step ST16 part 2): the headless host logs in as <c>Lifestone Loopback</c>, is sent to the exit
/// portal's wing of the Academy, walks to "Exit to Holtburg" (searching the rooms until the portal comes into view), uses it from
/// range, follows the MoveTo the server sends, and ACE teleports it outside (F751); ACE must not correct it once (corrections 0) and
/// its where-am-I answers stay within 0.5 m. The scenario then sends the character back to the Academy start.
/// </summary>
/// <remarks>
/// <para>ACE's only Academy portal with a destination, "Exit to Holtburg" (weenie 29338, cell 0x86020169), needs the Sentry's quest
/// (SentryTaskComplete), which a fresh character earns by killing an Adolescent Olthoi deep in the hive and giving the Sentry a
/// Protection Orb; "Central Courtyard" and "Outer Courtyard" have no destination in the world database. The scenario therefore asks the
/// server mod (<c>@lifestone quest has</c>) before the Use and stamps the quest when the character lacks it (<see cref="ExitQuestMode.Stamp"/>); no test writes the
/// database. The portal then runs its emote set (weenie 29338, ACE's Portal.cs:287), which erases the quest and nine others, moves the sanctuary to
/// Holtburg and enables recalls: so after the way home the scenario asks the registry and puts the quest back as it found it (a stamp for a quest it found held),
/// and the sanctuary, the recall flag and the other nine quests are not put back (the mod has no verb for them; both test characters are graduated, and
/// graduated is the test characters' baseline, by the owner's decision (docs/session.md section 25)). The character's position is back at the Academy start. The exit's wing
/// shares no doorway with the Academy start, so the scenario is sent to a room of the wing with <c>place loc</c>
/// (<see cref="AcademyExitScenario"/>'s <c>portalCell</c>).</para>
/// <para>The refusal is its own test (<see cref="ExitQuestMode.Erase"/>): the quest is taken away first, ACE refuses the Use with WeenieError 0x0474
/// as retail does, sends no F751, and the registry is put back. Every test ends with the quest registry as it found it: a failed run's
/// <c>finally</c> puts it back (<see cref="LiveQuests.PutBackAsFound"/>), and a passing run must not have needed that.
/// <see cref="An_erase_of_a_held_quest_and_the_stamp_before_it_are_saved_across_a_relog"/> is the one test that makes ACE keep an erase of a quest the
/// character really holds and reads it back on a fresh login.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
[Trait("Lane", "solo")]
public sealed class AcademyExitLoopbackTests(ITestOutputHelper output)
{
    private const string Character = "Lifestone Loopback";

    /// <summary>
    /// The refusal, on purpose: the character is sent to the exit's wing, the quest is taken from it (it holds none, so nothing is changed),
    /// it walks to "Exit to Holtburg" with ACE accepting every move, uses it from range, follows the server's MoveTo to the portal (UseDone
    /// arrives), and ACE refuses with WeenieError 0x0474 for the missing quest and sends no F751. That is retail's behaviour for a
    /// character without the Sentry's quest.
    /// </summary>
    [LoopbackFact]
    public void A_character_without_the_quest_walks_to_the_exit_portal_uses_it_from_range_and_ACE_refuses_it()
    {
        using var dats = DatSet.Open();
        var (result, scenario) = RunScenario(dats, ExitQuestMode.Erase);
        var putBackByTheTest = false;
        try
        {
            var play = scenario.Play;
            Assert.True(result.Ok, result.Failure);
            Assert.True(play.ExitRefusedByQuest, "ACE did not refuse the Use of the portal for the quest");
            Assert.Equal("Exit to Holtburg", play.Portal!.Name);
            Assert.Equal(ActionOutcomeKind.Confirmed, play.UseOutcome!.Value.Kind);
            Assert.True(play.ServerMovements >= 1, "ACE sent no MoveTo for a Use from range");
            Assert.False(play.ExitAnnounced);
            Assert.Equal(0, play.Audit!.Corrections);
            Assert.Equal(0, result.Corrections);
            Assert.InRange(play.Audit!.WhereMaxMetres!.Value, 0.0, 0.5); // the server and the walker agree where its MoveTo left the player
            Assert.True(play.QuestFound is not null, "the scenario never asked about the quest");
            Assert.True(play.QuestPutBack || !play.QuestRequested, "the scenario changed the quest registry and did not put it back");
            Assert.Equal(play.QuestRequested ? "put-back" : "unchanged", play.QuestVerdict);
        }
        finally
        {
            putBackByTheTest = PutTheCharacterAtTheAcademyStart(dats, scenario.Play);
        }

        Assert.False(putBackByTheTest, "the run passed but the test had to put the quest registry back");
    }

    /// <summary>
    /// The held branch of the exit, live: the character is given the Sentry's quest through the mod before the run, so the scenario finds it held and stamps nothing for the Use;
    /// the portal's emote set erases it after the teleport (as it does for every exit), so after the way home the scenario stamps it again, and the character ends holding it as it
    /// was found. The test then erases it, so that neither test character is left holding the quest (the baseline the end-state test reads).
    /// </summary>
    [LoopbackFact]
    public void A_character_that_holds_the_exit_quest_has_it_stamped_again_after_the_portal_took_it()
    {
        using var dats = DatSet.Open();
        const string quest = LiveQuests.SentryTaskComplete;
        var putBackByTheTest = true;
        InASession(dats, client =>
        {
            Assert.False(LiveQuests.Has(client, quest, output).Held, $"{Character} holds {quest} before the test: '@lifestone quest erase {quest}' puts it back");
            var stamped = LiveQuests.Stamp(client, quest, output);
            Assert.True(stamped is { HeldBefore: false, Held: true }, "the stamp did not make the character hold the quest: " + stamped.Describe());
        });

        try
        {
            var (result, scenario) = RunScenario(dats, ExitQuestMode.Stamp);
            try
            {
                var play = scenario.Play;
                Assert.True(result.Ok, result.Failure);
                Assert.True(play.ExitAnnounced, "no F751 followed the portal Use");
                Assert.True(play.QuestFound is { Held: true, Completions: 1 }, "the scenario did not find the quest held once");
                Assert.False(play.QuestRequested); // held already: nothing was changed for the Use
                Assert.True(play.QuestPutBack, "the scenario did not stamp the quest again after the portal took it");
                Assert.Equal("put-back", play.QuestVerdict);
            }
            finally
            {
                putBackByTheTest = PutTheCharacterAtTheAcademyStart(dats, scenario.Play);
            }
        }
        finally
        {
            InASession(dats, client => _ = LiveQuests.Erase(client, quest, output)); // the baseline: neither test character holds the quest
        }

        Assert.False(putBackByTheTest, "the run passed but the character did not hold the quest after it, so the test had to put the registry back");
    }

    /// <summary>
    /// The one live test of an erase that removes a row the character really holds, and of ACE keeping the registry across a relog (the open question of the quest verb's
    /// slice): on <c>Lifestone Loopback</c>, <c>has</c> is false, <c>stamp</c> makes it held, a fresh login still holds it (the stamp was saved), <c>erase</c> answers
    /// <c>HeldBefore</c> true and <c>Held</c> false, and a second fresh login holds nothing (the erase was saved). Nothing here writes the database: the mod's verbs
    /// and ACE's own save at log off. The erase is in a <c>finally</c> so that the character is never left holding the quest.
    /// </summary>
    [LoopbackFact]
    public void An_erase_of_a_held_quest_and_the_stamp_before_it_are_saved_across_a_relog()
    {
        using var dats = DatSet.Open();
        const string quest = LiveQuests.SentryTaskComplete;
        var mayHold = false; // true from the moment a stamp is sent until a fresh login has shown the erase kept
        try
        {
            InASession(dats, client =>
            {
                var first = LiveQuests.Has(client, quest, output);
                Assert.False(first.Held, $"{Character} holds {quest} before the test: '@lifestone quest erase {quest}' puts it back");
                mayHold = true;
                var stamped = LiveQuests.Stamp(client, quest, output);
                Assert.True(stamped is { HeldBefore: false, Held: true }, "the stamp did not make the character hold the quest: " + stamped.Describe());
            });

            InASession(dats, client =>
            {
                var saved = LiveQuests.Has(client, quest, output);
                Assert.True(saved.Held, "the stamp was not saved: a fresh login does not hold the quest");
                var erased = LiveQuests.Erase(client, quest, output);
                Assert.True(erased is { HeldBefore: true, Held: false }, "the erase of the held quest did not remove it: " + erased.Describe());
                Assert.Equal(saved.After, erased.Before);
            });

            InASession(dats, client =>
            {
                var gone = LiveQuests.Has(client, quest, output);
                Assert.False(gone.Held, "the erase was not saved: a fresh login holds the quest again");
                mayHold = false;
            });
        }
        finally
        {
            if (mayHold)
            {
                InASession(dats, client => _ = LiveQuests.Erase(client, quest, output));
            }
        }
    }

    /// <summary>
    /// The end state of the exit tests: neither test character holds the Sentry's quest. Reads only (<c>@lifestone quest has</c>); the characters are not moved.
    /// </summary>
    [LoopbackFact]
    public void Neither_test_character_holds_the_exit_quest()
    {
        using var dats = DatSet.Open();
        foreach (var name in new[] { LiveNetClient.Character, LiveNetClient.LoopbackCharacter })
        {
            using var client = LiveNetClient.Create(dats, retail: false, name);
            try
            {
                LiveSteps.EnterTheWorld(client);
                var has = LiveQuests.Has(client, LiveQuests.SentryTaskComplete, output);
                output.WriteLine($"{name}: quest {LiveQuests.SentryTaskComplete} held {has.Held}");
                Assert.False(has.Held, $"{name} holds {LiveQuests.SentryTaskComplete}: '@lifestone quest erase {LiveQuests.SentryTaskComplete}' puts it back");
            }
            finally
            {
                LiveSteps.LeaveTheWorld(client);
            }
        }
    }

    /// <summary>
    /// The character-keeper for the Academy runs: puts <c>Lifestone Loopback</c> at the Academy start whatever an earlier run left it
    /// doing (<see cref="LivePlacement.EnsureAtStart"/> only moves a character that left the landblock). Idempotent.
    /// </summary>
    [LoopbackFact]
    public void The_loopback_character_can_be_put_at_the_Academy_start_from_anywhere_in_the_dungeon()
    {
        using var dats = DatSet.Open();
        PutTheCharacterAtTheAcademyStart(dats);
    }

    private void PutTheCharacterAtTheAcademyStart(DatSet dats) => LivePlacement.PutTheCharacterAtTheAcademyStart(dats, output);

    /// <summary>One login of <c>Lifestone Loopback</c>: enters the world, runs <paramref name="act"/> and logs off (ACE saves the character then), whatever <paramref name="act"/> did.</summary>
    private static void InASession(DatSet dats, Action<LiveNetClient> act)
    {
        using var client = LiveNetClient.Create(dats, retail: false, LiveNetClient.LoopbackCharacter);
        try
        {
            LiveSteps.EnterTheWorld(client);
            act(client);
        }
        finally
        {
            LiveSteps.LeaveTheWorld(client);
        }
    }

    /// <summary>Puts the character at the Academy start and, in the same session, the quest registry back as the scenario found it; true when the registry needed it.</summary>
    private bool PutTheCharacterAtTheAcademyStart(DatSet dats, RoutePlay play)
    {
        var putBack = false;
        LivePlacement.PutTheCharacterAtTheAcademyStart(dats, output, client => putBack = LiveQuests.PutBackAsFound(client, play, output));
        return putBack;
    }

    /// <summary>Runs the Academy exit scenario live as <c>Lifestone Loopback</c> and prints its verdict and its scenario, warning and error lines.</summary>
    private (HeadlessRunResult Result, AcademyExitScenario Scenario) RunScenario(DatSet dats, ExitQuestMode exitQuest)
    {
        var files = BackendProbe.MainCheckoutFiles;
        var log = new RecordingLogSink();
        using var recording = new MemoryStream();
        var scenario = new AcademyExitScenario(
            dats,
            log,
            createCharacter: Character,
            skillClasses: HeadlessCharacter.RetailSkillClasses(dats),
            portalCell: PortalExit.ExitToHoltburgCell,
            exitQuest: exitQuest);
        LiveCharacter.Release(output, BackendProbe.TestAccountKey);
        var secrets = File.ReadAllBytes(files.SecretsFile);
        HeadlessRunner runner;
        try
        {
            runner = HeadlessRunner.Start(
                new HeadlessRunOptions
                {
                    Session = new SessionOptions { Account = BackendProbe.TestAccountKey, Server = BackendProbe.Probe().AceEndPoint! },
                    QuitAfterSeconds = 600,
                },
                scenario,
                secrets,
                File.ReadAllBytes(files.PidsFile),
                dats,
                TimeProvider.System,
                log,
                recording);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secrets);
        }

        HeadlessRunResult result;
        var context = new GameThreadSynchronizationContext();
        using (context.Install())
        using (runner)
        {
            result = runner.Run(context, CancellationToken.None);
        }

        output.WriteLine(result.SummaryLine("memory"));
        output.WriteLine(scenario.Play.SummaryLine ?? "no movement verdict line");
        foreach (var line in log.Lines.Where(l => l.Contains("scenario", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        return (result, scenario);
    }
}
