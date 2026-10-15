using System.Text.RegularExpressions;
using Lifestone.Client.Movement.Routes;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Dat;
using Lifestone.Data.Tests;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;
using Lifestone.Data.World.Cells;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Xunit.Abstractions;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The Academy exit scenario (step ST16 part 2) against <see cref="ScriptedAceSession"/> playing ACE for a walk and a
/// portal: the character logs in, is sent to the Academy start with <c>place loc</c>, walks through the Academy rooms
/// on the real collision to the portal the server created, uses it from 4 m, follows the MoveToObject the server sends (the
/// walker MoveToManager, AV14), is teleported outside by the portal (F751), and is sent home again. The audit passes with
/// no corrections and where-am-I within 0.5 m at the start, at the portal and outside.
/// </summary>
public sealed class AcademyExitScenarioTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    private static readonly Regex Verdict = new(
        @"^movement route=academy-exit laps=1 corrections=0 where_max_m=\d+\.\d\d result=PASS$",
        RegexOptions.CultureInvariant);

    private DatSet Dats => fixture.Dats;

    [RequiresDatsFact]
    public void The_character_walks_to_the_portal_uses_it_from_range_follows_the_MoveTo_and_arrives_outside()
    {
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.ExitPortalAt) = (true, PortalPosition(Dats)));
        var result = run.Finish();
        var server = run.Server;
        var scenario = (AcademyExitScenario)run.Runner.Scenario;
        var play = scenario.Play;
        output.WriteLine($"walker at {play.Walker!.Position}, moving-to {play.Walker.Physics.IsMovingTo}, server-controlled {play.Walker.Controller.ControlledByServer}");
        output.WriteLine($"{result.SummaryLine("memory")} ({run.Frames} frames)");
        output.WriteLine($"{play.SummaryLine}; {server.PositionMessages} position messages; {play.ServerMovements} server movements followed; unmatched {play.Unmatched}");
        foreach (var line in run.Log.Lines.Where(l => l.Contains("scenario", StringComparison.Ordinal) && !l.Contains("DEBUG", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("done", result.Stage);
        Assert.Matches(Verdict, play.SummaryLine);
        var audit = play.Audit!;
        Assert.Equal(0, audit.Corrections);
        Assert.Equal(0, result.Corrections);
        Assert.InRange(audit.WhereMaxMetres!.Value, 0.0, 0.5);
        Assert.True(audit.WhereChecks >= 3, $"the start, the portal and outside, not {audit.WhereChecks}");
        Assert.Equal(3, audit.Teleports); // the Academy start, the portal, the return home
        Assert.Equal(0, server.IgnoredWhileTeleporting); // nothing was sent from the Academy once the portal teleported the player
        Assert.Equal(0, play.Unmatched);

        // It used the exit portal from range, and the walker followed the server MoveToObject until the portal teleported it.
        Assert.Equal(ScriptedAceSession.PortalGuid, play.Portal!.Guid);
        Assert.Equal(1, server.PortalUses);
        Assert.Equal(1, server.MoveTosSent);
        Assert.Equal(1, play.ServerMovements);
        Assert.Equal(1, server.PortalTeleports);
        Assert.True(play.ExitAnnounced);
        Assert.Equal(server.PortalDestination.Cell, play.ExitArrival!.Value.CellId);
        Assert.Equal(ActionOutcomeKind.Confirmed, play.UseOutcome!.Value.Kind);
        Assert.Contains((MessageOpcodes.GameAction, GameActionTypes.Use), server.Sent);

        // The character is left at the Academy start.
        Assert.Equal(12.32f, server.PlayerAt.X, 0.01f); // the Academy start of the CharGen dat, not the scripted login position
        Assert.Equal(-28.48f, server.PlayerAt.Y, 0.01f);
        Assert.Equal(ScriptedAceSession.AcademyCell, server.PlayerAt.Cell);
    }

    [RequiresDatsFact]
    public void A_character_sent_to_the_exit_wing_walks_to_the_portal_there_and_is_sent_home_to_the_Academy_start()
    {
        // The live server's exit portal ("Exit to Holtburg", world database) is in a wing that the Academy start cannot walk to, so
        // the scenario is sent into the wing (place loc), walks and uses the portal there, and is sent home to the Academy start.
        var portal = new AcePosition(PortalExit.ExitToHoltburgCell, 158.641f, -149.516f, -6.063f);
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.ExitPortalAt) = (true, portal), portalCell: PortalExit.ExitToHoltburgCell);
        var result = run.Finish();
        var server = run.Server;
        var scenario = (AcademyExitScenario)run.Runner.Scenario;
        var play = scenario.Play;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}; {play.SummaryLine}");

        Assert.True(result.Ok, result.Failure);
        Assert.NotEqual(AcademyExitScenario.AcademyStartCell, scenario.Route.Start!.Value.CellId);
        Assert.Equal(0, play.Audit!.Corrections);
        Assert.InRange(play.Audit.WhereMaxMetres!.Value, 0.0, 0.5);
        Assert.Equal(1, server.PortalUses);
        Assert.Equal(1, server.MoveTosSent);
        Assert.Equal(1, server.PortalTeleports);
        Assert.Equal(12.32f, server.PlayerAt.X, 0.01f); // sent home: the Academy start, not the wing
        Assert.Equal(-28.48f, server.PlayerAt.Y, 0.01f);
        Assert.Equal(ScriptedAceSession.AcademyCell, server.PlayerAt.Cell);
    }

    [RequiresDatsFact]
    public void A_use_from_inside_the_portal_reach_needs_no_MoveTo()
    {
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.ExitPortalAt, s.PortalReachMetres) = (true, PortalPosition(Dats), 5f));
        var result = run.Finish();
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(0, run.Server.MoveTosSent);
        Assert.Equal(0, ((AcademyExitScenario)run.Runner.Scenario).Play.ServerMovements);
        Assert.Equal(1, run.Server.PortalTeleports);
    }

    [RequiresDatsFact]
    public void A_landblock_without_an_exit_portal_fails_loudly()
    {
        using var run = new Run(Dats, configure: s => s.AcceptsMovement = true);
        var result = run.Finish();
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.False(result.Ok);
        Assert.Contains("no portal named 'Exit Portal' came into view", result.Failure, StringComparison.Ordinal);
        Assert.Contains("TODO(st16-which-portal)", result.Failure, StringComparison.Ordinal);
    }

    /// <summary>
    /// A portal far enough in to walk to and big enough to stop short of: the centre of the largest floor among the rooms at least
    /// three doorways from the start (the walk stops 4 m from the portal, so a closet next to the last doorway would not do).
    /// </summary>
    internal static AcePosition PortalPosition(DatSet dats)
    {
        var coord = LandblockCoord.FromId(ScriptedAceSession.AcademyCell);
        var cells = LandblockCellSet.Load(new CellStructureLibrary(dats), coord);
        var planner = new CellRoutePlanner(cells);
        EnvCellData? best = null;
        var bestArea = 0f;
        foreach (var id in cells.Ids)
        {
            if (planner.FindPath(ScriptedAceSession.AcademyCell, id) is not { Count: >= 4 })
            {
                continue;
            }

            var candidate = cells.GetCell(id)!;
            var area = cells.GetStructure(candidate).Floors.Max(f => f.Area);
            if (area > bestArea)
            {
                (best, bestArea) = (candidate, area);
            }
        }

        var floor = cells.GetStructure(best!).Floors.OrderByDescending(f => f.Area).First();
        var centre = best!.Frame.LocalToParent(floor.Centroid);
        return new AcePosition(best.Id, centre.X, centre.Y, centre.Z);
    }

    private const string Sentry = "SentryTaskComplete";

    [RequiresDatsFact]
    public void A_quest_gated_portal_is_opened_by_a_stamp_before_the_use_and_the_registry_is_found_as_it_was_after_the_way_home()
    {
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest) = (true, PortalPosition(Dats), Sentry));
        var result = run.Finish();
        var server = run.Server;
        var play = ((AcademyExitScenario)run.Runner.Scenario).Play;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}; {play.SummaryLine}; requests {string.Join(", ", server.QuestRequests)}");

        Assert.True(result.Ok, result.Failure);
        Assert.Equal("done", result.Stage);
        // Asked, stamped because it was missing, and asked again after the way home: ACE's portal runs its emote set after the teleport, which erases the quest (as live), so
        // the registry is already as found and no erase is sent.
        Assert.Equal(new[] { "has " + Sentry, "stamp " + Sentry, "has " + Sentry }, server.QuestRequests.ToArray());
        Assert.Empty(server.Quests);
        Assert.Equal(1, server.PortalQuestErasures);
        Assert.Equal(server.PortalDestination, server.Sanctuary); // the emote set's other changes are not put back: they are the characters' baseline, by the owner's decision (docs/session.md section 25)
        Assert.False(server.RecallsDisabled);
        Assert.Equal(1, server.PortalTeleports);
        Assert.Equal(0, server.PortalRefusals);
        Assert.True(play.ExitAnnounced);
        Assert.False(play.ExitRefusedByQuest);
        Assert.NotEqual(0x8602u, play.ExitArrival!.Value.CellId >> 16); // outside the Academy
        Assert.Equal(new QuestResult("has", Sentry, false, 0, false, 0), play.QuestFound);
        Assert.Equal(new QuestResult("stamp", Sentry, false, 0, true, 1), play.QuestChange);
        Assert.True(play.QuestPutBack);
        Assert.False(play.QuestPutBackAfterFailure);
        Assert.Equal("put-back", play.QuestVerdict);
        Assert.Matches(Verdict, play.SummaryLine);
        Assert.Equal(0, play.Audit!.Corrections);
        Assert.InRange(play.Audit.WhereMaxMetres!.Value, 0.0, 0.5);
        Assert.Equal(ScriptedAceSession.AcademyCell, server.PlayerAt.Cell); // and home at the Academy start
    }

    [RequiresDatsFact]
    public void A_character_that_already_holds_the_quest_is_not_stamped_for_the_use_and_the_scenario_stamps_it_again_after_the_portal_took_it()
    {
        using var run = new Run(Dats, configure: s =>
        {
            (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest) = (true, PortalPosition(Dats), Sentry);
            s.Quests[Sentry] = 1;
        });
        var result = run.Finish();
        var server = run.Server;
        var play = ((AcademyExitScenario)run.Runner.Scenario).Play;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}; requests {string.Join(", ", server.QuestRequests)}");

        Assert.True(result.Ok, result.Failure);
        // Nothing to change before the Use; the portal's emote set erases the held quest after the teleport, so the way home ends with a stamp (live ACE does the same).
        Assert.Equal(new[] { "has " + Sentry, "has " + Sentry, "stamp " + Sentry }, server.QuestRequests.ToArray());
        Assert.Equal(1, server.Quests[Sentry]);
        Assert.Equal(1, server.PortalQuestErasures);
        Assert.Equal(1, server.PortalTeleports);
        Assert.False(play.QuestRequested); // no change for the Use
        Assert.Null(play.QuestChange);
        Assert.True(play.QuestPutBack);
        Assert.False(play.QuestPutBackAfterFailure);
        Assert.Equal("put-back", play.QuestVerdict);
    }

    [RequiresDatsFact]
    public void A_held_quest_with_a_count_the_stamp_cannot_restore_fails_loudly_after_the_portal_took_it()
    {
        using var run = new Run(Dats, configure: s =>
        {
            (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest, s.QuestMaxCompletions) = (true, PortalPosition(Dats), Sentry, 3);
            s.Quests[Sentry] = 2;
        });
        var result = run.Finish();
        var server = run.Server;
        var play = ((AcademyExitScenario)run.Runner.Scenario).Play;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}; requests {string.Join(", ", server.QuestRequests)}");

        Assert.False(result.Ok);
        Assert.Contains("was held with 2 completions and the portal took it", result.Failure, StringComparison.Ordinal);
        Assert.Equal(new[] { "has " + Sentry, "has " + Sentry }, server.QuestRequests.ToArray()); // nothing was stamped blind
        Assert.False(play.QuestPutBack);
        Assert.False(play.QuestPutBackAfterFailure);
        Assert.Equal("unverified", play.QuestVerdict);
        Assert.Contains(run.Log.Lines, l => l.Contains("TODO(st16-quest-restore)", StringComparison.Ordinal) && l.Contains("nothing was sent", StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void A_portal_that_refuses_a_character_that_holds_the_quest_ends_the_run_naming_the_server_s_answer()
    {
        // The portal needs another quest than the one the scenario stamps, so ACE says no (WeenieError 0x0474, as the live server does) after the walk and the use.
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest) = (true, PortalPosition(Dats), "AnotherQuest"));
        var result = run.Finish();
        var server = run.Server;
        var play = ((AcademyExitScenario)run.Runner.Scenario).Play;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        // The walk and the use went as ACE accepts them; the portal then said no, UseDone came and no F751 followed, so the run fails once, loudly, with the answer.
        Assert.False(result.Ok);
        Assert.Contains($"ACE refused the portal for its quest although the character held {Sentry}", result.Failure, StringComparison.Ordinal);
        Assert.Contains("WeenieError 0x0474", result.Failure, StringComparison.Ordinal);
        Assert.Contains("m from the walker at the portal", result.Failure, StringComparison.Ordinal); // and the server agreed where the MoveTo left the player
        Assert.DoesNotContain("TODO(st16-exit-quest)", result.Failure, StringComparison.Ordinal);
        Assert.InRange(play.Audit!.WhereMaxMetres!.Value, 0.0, 0.5);
        Assert.Equal(1, server.PortalUses);
        Assert.Equal(1, server.PortalRefusals);
        Assert.Equal(0, server.PortalTeleports);
        Assert.False(play.ExitAnnounced);
        Assert.Equal(ActionOutcomeKind.Confirmed, play.UseOutcome!.Value.Kind);
        Assert.Equal(0, play.Audit!.Corrections);

        // The scenario stamped the quest, so the failed play sends the erase without waiting for the answer, and says so.
        Assert.True(play.QuestPutBackAfterFailure);
        Assert.False(play.QuestPutBack);
        Assert.Equal("unverified", play.QuestVerdict); // the erase was sent without waiting: nothing has confirmed it
        Assert.Contains(run.Log.Lines, l => l.Contains("TODO(st16-quest-restore)", StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void A_stamp_the_mod_refused_or_did_nothing_with_is_never_followed_by_an_erase()
    {
        foreach (var refusal in new[] { "error", "nothing" })
        {
            using var run = new Run(Dats, configure: s =>
            {
                (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest) = (true, PortalPosition(Dats), Sentry);
                (s.QuestWritesFail, s.QuestWritesDoNothing) = (refusal == "error", refusal == "nothing");
            });
            var result = run.Finish();
            var play = ((AcademyExitScenario)run.Runner.Scenario).Play;
            output.WriteLine($"{refusal}: {result.SummaryLine("memory")}: {result.Failure}; requests {string.Join(", ", run.Server.QuestRequests)}");

            Assert.False(result.Ok);
            Assert.Contains("quest-change: ", result.Failure, StringComparison.Ordinal);
            Assert.Equal(new[] { "has " + Sentry, "stamp " + Sentry }, run.Server.QuestRequests.ToArray()); // the stamp never took, so no erase is sent for it
            Assert.Equal(0, run.Server.PortalUses);
            Assert.True(play.QuestRequested);
            Assert.False(play.QuestPutBackAfterFailure);
            Assert.Equal("unchanged", play.QuestVerdict);
            Assert.DoesNotContain(run.Log.Lines, l => l.Contains("TODO(st16-quest-restore)", StringComparison.Ordinal));
        }
    }

    [RequiresDatsFact]
    public void An_erase_the_mod_refused_or_did_nothing_with_is_never_followed_by_a_stamp()
    {
        foreach (var refusal in new[] { "error", "nothing" })
        {
            using var run = new Run(
                Dats,
                configure: s =>
                {
                    (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest) = (true, PortalPosition(Dats), Sentry);
                    (s.QuestWritesFail, s.QuestWritesDoNothing) = (refusal == "error", refusal == "nothing");
                    s.Quests[Sentry] = 1;
                },
                exitQuest: ExitQuestMode.Erase);
            var result = run.Finish();
            var play = ((AcademyExitScenario)run.Runner.Scenario).Play;
            output.WriteLine($"{refusal}: {result.SummaryLine("memory")}: {result.Failure}; requests {string.Join(", ", run.Server.QuestRequests)}");

            Assert.False(result.Ok);
            Assert.Equal(new[] { "has " + Sentry, "erase " + Sentry }, run.Server.QuestRequests.ToArray()); // the erase never took, so no stamp is sent for it (it would add a completion)
            Assert.Equal(1, run.Server.Quests[Sentry]);
            Assert.False(play.QuestPutBackAfterFailure);
            Assert.Equal("unchanged", play.QuestVerdict);
        }
    }

    [RequiresDatsFact]
    public void A_failed_restore_does_not_send_its_verb_again()
    {
        // The portal takes the held quest; the restore's stamp then comes back as a success that changed nothing: the run fails once and the cleanup does not repeat the stamp.
        using var run = new Run(Dats, configure: s =>
        {
            (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest, s.QuestWritesDoNothing) = (true, PortalPosition(Dats), Sentry, true);
            s.Quests[Sentry] = 1;
        });
        var result = run.Finish();
        var play = ((AcademyExitScenario)run.Runner.Scenario).Play;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}; requests {string.Join(", ", run.Server.QuestRequests)}");

        Assert.False(result.Ok);
        Assert.Contains("quest-restore: after the stamp the server says the character does not hold quest", result.Failure, StringComparison.Ordinal);
        Assert.Equal(new[] { "has " + Sentry, "has " + Sentry, "stamp " + Sentry }, run.Server.QuestRequests.ToArray());
        Assert.False(play.QuestPutBack);
        Assert.False(play.QuestPutBackAfterFailure);
        Assert.Equal("unverified", play.QuestVerdict);
        Assert.Contains(run.Log.Lines, l => l.Contains("TODO(st16-quest-restore)", StringComparison.Ordinal));
    }

    [RequiresDatsFact]
    public void A_play_that_is_disposed_after_its_stamp_sends_the_erase_and_warns()
    {
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest) = (true, PortalPosition(Dats), Sentry));
        var play = ((AcademyExitScenario)run.Runner.Scenario).Play;
        for (var t = 0.0; t < 650 && !run.Runner.IsDone && play.QuestChange is null; t += 1.0 / 60.0)
        {
            run.Step();
        }

        Assert.NotNull(play.QuestChange); // stamped, and the run is cut off (a whole-check timeout) before the Use
        Assert.Equal("unverified", play.QuestVerdict);
        play.Dispose();
        Assert.True(play.QuestPutBackAfterFailure);
        Assert.Contains(run.Log.Lines, l => l.Contains("TODO(st16-quest-restore)", StringComparison.Ordinal) && l.Contains("'erase' was sent", StringComparison.Ordinal));
        for (var i = 0; i < 30; i++)
        {
            run.Step();
        }

        Assert.Equal("erase " + Sentry, run.Server.QuestRequests[^1]);
    }

    [RequiresDatsFact]
    public void The_refusal_path_erases_the_quest_first_takes_ACE_s_refusal_as_the_outcome_and_stamps_it_back_when_it_was_held()
    {
        foreach (var held in new[] { true, false })
        {
            using var run = new Run(
                Dats,
                configure: s =>
                {
                    (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest) = (true, PortalPosition(Dats), Sentry);
                    if (held)
                    {
                        s.Quests[Sentry] = 1;
                    }
                },
                exitQuest: ExitQuestMode.Erase);
            var result = run.Finish();
            var server = run.Server;
            var play = ((AcademyExitScenario)run.Runner.Scenario).Play;
            output.WriteLine($"held {held}: {result.SummaryLine("memory")}: {result.Failure}; {play.SummaryLine}; requests {string.Join(", ", server.QuestRequests)}");

            Assert.True(result.Ok, result.Failure);
            Assert.Equal("done", result.Stage);
            Assert.True(play.ExitRefusedByQuest);
            Assert.False(play.ExitAnnounced); // the refusal is the outcome: no teleport
            // Held: asked, erased, asked again after the way home (the refusal ran no emote set, so the quest is still gone) and stamped back; not held: asked, nothing to change.
            Assert.Equal(held ? new[] { "has " + Sentry, "erase " + Sentry, "has " + Sentry, "stamp " + Sentry } : new[] { "has " + Sentry }, server.QuestRequests.ToArray());
            Assert.Equal(held ? "put-back" : "unchanged", play.QuestVerdict);
            Assert.Equal(held ? new[] { Sentry } : Array.Empty<string>(), server.Quests.Keys.ToArray()); // as found
            Assert.Equal(held ? 1 : 0, server.Quests.GetValueOrDefault(Sentry));
            Assert.Equal(1, server.PortalRefusals);
            Assert.Equal(0, server.PortalTeleports);
            Assert.Matches(Verdict, play.SummaryLine);
            Assert.Equal(0, play.Audit!.Corrections);
            Assert.InRange(play.Audit.WhereMaxMetres!.Value, 0.0, 0.5);
            Assert.Equal(held, play.QuestPutBack);
            Assert.Equal(12.32f, server.PlayerAt.X, 0.01f); // sent home to the Academy start
            Assert.Equal(-28.48f, server.PlayerAt.Y, 0.01f);
            Assert.Equal(ScriptedAceSession.AcademyCell, server.PlayerAt.Cell);
        }
    }

    [RequiresDatsFact]
    public void The_refusal_path_fails_when_the_portal_teleports_a_character_that_had_the_quest_taken()
    {
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.ExitPortalAt) = (true, PortalPosition(Dats)), exitQuest: ExitQuestMode.Erase); // an ungated portal
        var result = run.Finish();
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.False(result.Ok);
        Assert.Contains($"the portal teleported the character", result.Failure, StringComparison.Ordinal);
        Assert.Contains($"although the scenario had taken quest {Sentry} from it", result.Failure, StringComparison.Ordinal);
    }

    [RequiresDatsFact]
    public void The_refusal_path_leaves_a_quest_it_cannot_put_back_alone()
    {
        // A quest held with two completions cannot be erased and stamped back to two: the scenario refuses before it changes anything.
        using var run = new Run(
            Dats,
            configure: s =>
            {
                (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest, s.QuestMaxCompletions) = (true, PortalPosition(Dats), Sentry, 3);
                s.Quests[Sentry] = 2;
            },
            exitQuest: ExitQuestMode.Erase);
        var result = run.Finish();
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.False(result.Ok);
        Assert.Contains("holds quest SentryTaskComplete with 2 completions", result.Failure, StringComparison.Ordinal);
        Assert.Equal(new[] { "has " + Sentry }, run.Server.QuestRequests.ToArray());
        Assert.Equal(2, run.Server.Quests[Sentry]);
    }

    [RequiresDatsFact]
    public void A_mod_that_refuses_the_quest_verb_fails_the_run_naming_the_step_before_the_portal_is_used()
    {
        using var run = new Run(Dats, configure: s => (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest, s.QuestVerbFails) = (true, PortalPosition(Dats), Sentry, true));
        var result = run.Finish();
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.False(result.Ok);
        Assert.Contains("quest-check: asking whether the character holds quest SentryTaskComplete", result.Failure, StringComparison.Ordinal);
        Assert.Contains("not-in-world", result.Failure, StringComparison.Ordinal);
        Assert.Equal(0, run.Server.PortalUses);
    }

    [RequiresDatsFact]
    public void A_server_that_is_late_with_the_walker_at_the_refused_portal_is_asked_again_and_passes_when_the_refusal_is_the_outcome()
    {
        // The server's view of the walker is 3 m behind it for the first two answers (TODO(st16-refused-where)), then agrees: the settle asks again, whichever mode
        // the refusal is read in (a refusal fails the Stamp run and passes the Erase run, both after the settle).
        foreach (var mode in new[] { ExitQuestMode.Erase, ExitQuestMode.Stamp })
        {
            var refusalPasses = mode == ExitQuestMode.Erase;
            using var run = new Run(
                Dats,
                configure: s => (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest, s.RefusedWhereOffsetMetres, s.RefusedWhereLaggedAnswers) = (true, PortalPosition(Dats), refusalPasses ? Sentry : "AnotherQuest", 3f, 2),
                exitQuest: mode);
            var result = run.Finish();
            var play = ((AcademyExitScenario)run.Runner.Scenario).Play;
            output.WriteLine($"{mode}: {result.SummaryLine("memory")}: {result.Failure}");

            Assert.Equal(2, run.Server.RefusedWhereLagged);
            Assert.Equal(refusalPasses, result.Ok);
            Assert.Equal(refusalPasses, play.ExitRefusedByQuest);
            if (!refusalPasses)
            {
                Assert.Contains("m from the walker at the portal", result.Failure, StringComparison.Ordinal);
            }

            Assert.InRange(play.Audit!.WhereMaxMetres!.Value, 0.0, 0.5); // the late answers were retried, not recorded
            Assert.Equal(0, play.Audit.Corrections);
        }
    }

    [RequiresDatsFact]
    public void A_server_that_stays_away_from_the_walker_at_the_refused_portal_fails_after_the_asks_run_out()
    {
        using var run = new Run(
            Dats,
            configure: s => (s.AcceptsMovement, s.ExitPortalAt, s.PortalQuest, s.RefusedWhereOffsetMetres) = (true, PortalPosition(Dats), Sentry, 3f),
            exitQuest: ExitQuestMode.Erase);
        var result = run.Finish();
        var play = ((AcademyExitScenario)run.Runner.Scenario).Play;
        output.WriteLine($"{result.SummaryLine("memory")}: {result.Failure}");

        Assert.False(result.Ok);
        Assert.False(play.ExitRefusedByQuest);
        Assert.Equal(RoutePlay.MaxRefusedAsks, run.Server.RefusedWhereLagged);
        Assert.Contains("m from the walker at the portal", result.Failure, StringComparison.Ordinal);
        var named = Regex.Match(result.Failure!, @"the server has the player (\d+\.\d\d) m from the walker");
        Assert.True(named.Success, result.Failure); // the disagreement is named
        Assert.InRange(double.Parse(named.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), 2.5, 3.5);
    }

    /// <summary>A headless run of the Academy scenario over the scripted server, stepped at 60 Hz on a manual clock.</summary>
    private sealed class Run : IDisposable
    {
        public Run(DatSet dats, Action<ScriptedAceSession> configure, uint? portalCell = null, ExitQuestMode exitQuest = ExitQuestMode.Stamp)
        {
            Time = new ManualTimeProvider();
            Runner = HeadlessRunner.StartWith(
                new HeadlessRunOptions { Session = new SessionOptions { Account = "test" }, QuitAfterSeconds = 600 },
                new AcademyExitScenario(dats, Log, character: "Amy", portalName: "Exit Portal", portalCell: portalCell, exitQuest: exitQuest),
                SessionTestData.SecretsV2(),
                SessionTestData.PidsReady(),
                () => SessionTestData.EndOfRetail,
                () => HeadlessTestWorld.Assets,
                _ => new HeadlessTestWorld(),
                Time,
                Log,
                new MemoryStream(),
                new SocketFactorySpy().Open,
                (_, sink, tap) =>
                {
                    Server = new ScriptedAceSession(sink, tap, Time);
                    configure(Server);
                    return Server;
                });
        }

        public ManualTimeProvider Time { get; }

        public RecordingLogSink Log { get; } = new();

        public HeadlessRunner Runner { get; }

        public ScriptedAceSession Server { get; private set; } = null!;

        public int Frames { get; private set; }

        /// <summary>One frame at 60 Hz.</summary>
        public void Step()
        {
            Time.Advance(TimeSpan.FromSeconds(1.0 / 60.0));
            Runner.Step(Runner.Session.Clock.Now);
            Frames++;
        }

        public HeadlessRunResult Finish(double maxSeconds = 650)
        {
            for (var t = 0.0; t < maxSeconds && !Runner.IsDone; t += 1.0 / 60.0)
            {
                Time.Advance(TimeSpan.FromSeconds(1.0 / 60.0));
                Runner.Step(Runner.Session.Clock.Now);
                Frames++;
            }

            Assert.True(Runner.IsDone, $"the run did not end within {maxSeconds} s (stage {Runner.Scenario.Stage})");
            return Runner.Result;
        }

        public void Dispose() => Runner.Dispose();
    }
}
