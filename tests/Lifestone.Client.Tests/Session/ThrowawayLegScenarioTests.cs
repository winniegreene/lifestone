using Lifestone.Client.Play;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// The throwaway leg's script (<see cref="ThrowawayLegScenario"/>, <c>--login-check=throwaway</c>; m2-plan section 6.9) against a scripted account and screen seam
/// (<see cref="ThrowawayLegFakeAccount"/>): the whole path, a leftover from a failed run, the Add XP guard, the list comparison, and every stage's failure with the
/// recovery that leaves the throwaway pending deletion and the session logged off.
/// </summary>
public sealed class ThrowawayLegScenarioTests
{
    private const string Throwaway = ThrowawayLegScenario.Throwaway;

    private static (ThrowawayLegScenario Scenario, ScenarioStatus Status, List<string> Log) Run(ThrowawayLegFakeAccount account, double limitSeconds = 600)
    {
        var log = new List<string>();
        var scenario = new ThrowawayLegScenario(account, requestLogOff: null, log: log.Add, purge: account.Purge);
        var status = ScenarioStatus.Running;
        for (var elapsed = 0.0; elapsed < limitSeconds && status == ScenarioStatus.Running; elapsed += 0.05)
        {
            account.Tick(0.05);
            status = scenario.Step(account, elapsed);
        }

        scenario.Dispose();
        return (scenario, status, log);
    }

    [Fact]
    public void The_whole_leg_creates_uses_deletes_restores_and_purges_and_the_list_ends_as_it_began()
    {
        var account = new ThrowawayLegFakeAccount();
        var start = account.Listed;
        var (scenario, status, log) = Run(account);

        Assert.True(scenario.Failure is null, scenario.Failure);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(scenario.Ok);
        Assert.Equal("done", scenario.Stage);
        Assert.Equal("throwaway=purged name_errors=2 xp=added where_m=0.12 place=teleported deletes=2 restores=1 list=same leftover=no", scenario.VerdictFields());
        Assert.DoesNotContain("Lifestone", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal([ThrowawayLegScenario.Smoke, ThrowawayLegScenario.InvalidName, Throwaway], account.Finished);
        Assert.Equal(2, scenario.NameErrorTexts.Count);
        Assert.Equal(Throwaway, account.XpTarget);
        Assert.Equal(1, account.XpPresses);
        Assert.Equal(2, account.ScreenDeletes);
        Assert.Equal(1, account.ScreenRestores);
        Assert.Equal([ThrowawayLegScenario.Throwaway, ThrowawayLegScenario.Smoke], account.Entered);
        Assert.Equal(1, account.ConfirmedPurges);
        Assert.Equal(2, account.Purges); // the purge, then the question that ends it: the verb answers unknown-character
        Assert.Equal(0, scenario.StalePurges);
        Assert.Equal(1, account.Quits);
        Assert.Equal(SessionPhase.CharacterSelect, account.State.Phase);
        Assert.Equal(start, account.Listed);
        Assert.Equal(start, scenario.Baseline);
        Assert.True(scenario.ListSame);
        Assert.NotNull(scenario.Purged);
        Assert.False(scenario.Leftover);
        Assert.False(scenario.Recovered);
        Assert.False(double.IsNaN(scenario.CreatedAt));
        Assert.True(scenario.EnterAt >= scenario.CreatedAt && scenario.PresentAt > scenario.EnterAt && scenario.SettledAt - scenario.PresentAt >= ThrowawayLegScenario.SettleSeconds);
        Assert.True(scenario.PlaceArrivedAt > scenario.PlaceAskedAt);
        Assert.Contains(log, l => l.Contains("ACE refused it", StringComparison.Ordinal));
    }

    [Fact]
    public void The_leg_enters_the_world_only_as_the_new_character_and_then_as_the_graduated_one()
    {
        var account = new ThrowawayLegFakeAccount();
        var (scenario, _, _) = Run(account);

        Assert.True(scenario.Ok);
        Assert.Equal(2, account.Entered.Count);
        Assert.Equal(Throwaway, account.Entered[0]);
        Assert.Equal(ThrowawayLegScenario.Smoke, account.Entered[1]);
        Assert.DoesNotContain(ThrowawayLegFakeAccount.Loopback, account.Entered);
        Assert.DoesNotContain(ThrowawayLegFakeAccount.Loopback, account.Deleted);
        Assert.DoesNotContain(ThrowawayLegScenario.Smoke, account.Deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_leftover_throwaway_is_deleted_if_needed_purged_from_smoke_logged_and_the_leg_goes_on(bool pending)
    {
        var account = new ThrowawayLegFakeAccount();
        var start = account.Listed;
        account.Add(0x50000003, Throwaway, pending);
        var (scenario, status, log) = Run(account);

        Assert.True(scenario.Failure is null, scenario.Failure);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(scenario.Leftover);
        Assert.NotNull(scenario.LeftoverPurged);
        Assert.NotNull(scenario.Purged);
        Assert.Equal(start, scenario.Baseline); // the baseline is the list without the leftover
        Assert.Equal(start, account.Listed);
        Assert.EndsWith("leftover=yes", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Equal(2, account.ConfirmedPurges);
        Assert.Equal(pending ? 2 : 3, account.ScreenDeletes); // the leftover is deleted through the screen only when it is not pending already
        Assert.Equal(3, account.Entered.Count(n => n == ThrowawayLegScenario.Smoke) + account.Entered.Count(n => n == Throwaway));
        Assert.Contains(log, l => l.StartsWith("leftover:", StringComparison.Ordinal));
        Assert.Equal(2, scenario.Deletes); // the leftover's delete is not counted in the leg's own
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_stray_character_under_the_invalid_name_is_deleted_if_live_and_purged_like_the_throwaway(bool pending)
    {
        var account = new ThrowawayLegFakeAccount();
        var start = account.Listed;
        account.Add(0x50000004, ThrowawayLegScenario.InvalidName, pending);
        var (scenario, status, log) = Run(account);

        Assert.True(scenario.Failure is null, scenario.Failure);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.True(scenario.Leftover);
        Assert.Equal(start, scenario.Baseline);
        Assert.Equal(start, account.Listed);
        Assert.Equal(2, account.ConfirmedPurges); // the stray in the leftover pass, the throwaway at the end
        Assert.Equal(pending ? 2 : 3, account.ScreenDeletes);
        Assert.Contains(log, l => l.Contains("leftover: '" + ThrowawayLegScenario.InvalidName + "'", StringComparison.Ordinal));
        Assert.DoesNotContain(ThrowawayLegScenario.Smoke, account.Deleted);
    }

    [Fact]
    public void Both_strays_at_once_are_deleted_and_purged_before_the_leg_goes_on()
    {
        var account = new ThrowawayLegFakeAccount();
        var start = account.Listed;
        account.Add(0x50000003, Throwaway, false);
        account.Add(0x50000004, ThrowawayLegScenario.InvalidName, false);
        var (scenario, status, _) = Run(account);

        Assert.True(scenario.Failure is null, scenario.Failure);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(start, scenario.Baseline);
        Assert.Equal(start, account.Listed);
        Assert.Equal(3, account.ConfirmedPurges);
        Assert.Equal(4, account.ScreenDeletes); // two strays, then the leg's own two
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void A_name_is_purged_until_the_verb_answers_unknown_character_so_stale_rows_the_list_never_shows_go_too(int stale)
    {
        var account = new ThrowawayLegFakeAccount { StaleRows = stale };
        var (scenario, status, _) = Run(account);

        Assert.True(scenario.Failure is null, scenario.Failure);
        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(1 + stale, account.ConfirmedPurges);
        Assert.Equal(2 + stale, account.Purges);
        Assert.Equal(stale, scenario.StalePurges);
        Assert.NotNull(scenario.Purged);
    }

    [Fact]
    public void The_purging_is_bounded_when_the_verb_never_answers_unknown_character()
    {
        var account = new ThrowawayLegFakeAccount { PurgeNeverEnds = true };
        var (scenario, status, _) = Run(account);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(ThrowawayLegScenario.MaxPurges, account.ConfirmedPurges);
        Assert.Contains("still does not answer unknown-character", scenario.Failure, StringComparison.Ordinal);
        Assert.StartsWith("purging:", scenario.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Add_xp_is_refused_unless_the_character_in_the_world_is_the_throwaway()
    {
        var account = new ThrowawayLegFakeAccount { RenameAfterArrival = true };
        var (scenario, status, _) = Run(account);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(0, account.XpPresses);
        Assert.Contains("tools-", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("Add XP", scenario.Failure, StringComparison.Ordinal);
        Assert.False(scenario.XpAdded);
        AssertRecovered(account);
    }

    [Fact]
    public void The_scenario_itself_refuses_add_xp_when_the_in_world_name_differs_from_the_throwaway()
    {
        // The seam would press the button (it is told nothing about the guard): the scenario must not ask it to.
        var account = new ThrowawayLegFakeAccount { EnterAsOtherName = true };
        var (scenario, status, _) = Run(account);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal(0, account.XpPresses);
        Assert.StartsWith("arriving:", scenario.Failure, StringComparison.Ordinal);
        AssertRecovered(account);
    }

    [Fact]
    public void The_list_comparison_ignores_order_and_case_and_sees_a_pending_flag_a_missing_or_an_extra_character()
    {
        ListedCharacter[] a = [new("Alpha", false), new("Beta", true)];

        Assert.True(ThrowawayLegScenario.SameList(a, [new("beta", true), new("ALPHA", false)], out var none));
        Assert.Equal(string.Empty, none);
        Assert.False(ThrowawayLegScenario.SameList(a, [new("Alpha", false), new("Beta", false)], out var pendingDifference));
        Assert.Contains("Beta", pendingDifference, StringComparison.Ordinal);
        Assert.False(ThrowawayLegScenario.SameList(a, [new("Alpha", false)], out var missing));
        Assert.Contains("missing [Beta (pending deletion)]", missing, StringComparison.Ordinal);
        Assert.False(ThrowawayLegScenario.SameList(a, [.. a, new("Gamma", false)], out var extra));
        Assert.Contains("extra [Gamma]", extra, StringComparison.Ordinal);
        Assert.False(ThrowawayLegScenario.SameList(a, [new("Alpha", false), new("Alpha", false)], out _));
    }

    [Fact]
    public void A_list_that_differs_at_the_end_fails_the_leg_and_says_what_differs()
    {
        var account = new ThrowawayLegFakeAccount { PurgeRemovesNothingFromTheList = true };
        var (scenario, status, log) = Run(account);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(scenario.Ok);
        Assert.False(scenario.ListSame);
        Assert.Contains("differs from the list at the start", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains("list=different", scenario.VerdictFields(), StringComparison.Ordinal);
        Assert.Contains(log, l => l.StartsWith("end list:", StringComparison.Ordinal));
        Assert.Equal(0, account.Quits);
    }

    [Fact]
    public void Verified_needs_every_field()
    {
        var account = new ThrowawayLegFakeAccount();
        var (scenario, _, _) = Run(account);
        Assert.True(scenario.Verified);

        var farther = new ThrowawayLegFakeAccount { WhereMetres = 0.9 };
        var (far, status, _) = Run(farther);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Null(far.WhereMetres);
        Assert.False(far.Verified);
    }

    // ------------------------------------------------------------------------------------------------ failures, stage by stage

    public static TheoryData<string, string, string> Failures => new()
    {
        { "no-ready-smoke", "inspect", "no ready" },
        { "open-chargen-hangs", "chargen-open", "did not finish within" },
        { "taken-name-accepted", "name-taken", "accepted" },
        { "invalid-name-accepted", "name-invalid", "accepted" },
        { "throwaway-refused", "name-throwaway", "ACE refused it" },
        { "never-unhides", "arriving", "not unhidden within" },
        { "where-too-far", "tools-where", "m apart" },
        { "xp-wrong-amount", "tools-xp", "not 2500" },
        { "tool-op-fails", "tools-open", "set to fail" },
        { "no-teleport", "place-leaving", "no teleport" },
        { "wrong-landblock", "place-arriving", "not in Holtburg" },
        { "log-off-hangs", "logging-off", "did not return the character list" },
        { "delete-does-nothing", "char-delete", "does not show it pending" },
        { "restore-does-nothing", "char-restore", "ready again" },
        { "invalid-name-says-in-use", "name-invalid", "same as for a taken name" },
        { "purge-never-ends", "purging", "unknown-character" },
        { "purge-refused", "purging", "not-pending-deletion" },
        { "purge-unknown-verb", "purging", "no purge verb" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void Each_stage_names_itself_when_it_fails_and_the_account_is_left_safe(string fault, string stage, string words)
    {
        var account = new ThrowawayLegFakeAccount(smoke: fault != "no-ready-smoke");
        switch (fault)
        {
            case "open-chargen-hangs": account.OpenCharGenNeverEnds = true; break;
            case "taken-name-accepted": account.AcceptTakenName = true; break;
            case "invalid-name-accepted": account.AcceptInvalidName = true; break;
            case "throwaway-refused": account.RefuseThrowawayName = true; break;
            case "never-unhides": account.NeverUnhides = true; break;
            case "where-too-far": account.WhereMetres = 3.0; break;
            case "xp-wrong-amount": account.XpReturned = 10; break;
            case "tool-op-fails": account.FailToolOps = true; break;
            case "no-teleport": account.PlaceSendsNoTeleport = true; break;
            case "wrong-landblock": account.PlaceCell = ThrowawayLegFakeAccount.AcademyCell; break;
            case "log-off-hangs": account.LogOffNeverReturns = true; break;
            case "delete-does-nothing": account.DeleteDoesNothing = true; break;
            case "restore-does-nothing": account.RestoreDoesNothing = true; break;
            case "invalid-name-says-in-use": account.InvalidNameSaysInUse = true; break;
            case "purge-never-ends": account.PurgeNeverEnds = true; break;
            case "purge-refused": account.PurgeRefused = true; break;
            case "purge-unknown-verb": account.PurgeUnknownVerb = true; break;
        }

        var (scenario, status, _) = Run(account);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(scenario.Ok);
        Assert.NotNull(scenario.Failure);
        Assert.StartsWith(stage + ":", scenario.Failure, StringComparison.Ordinal);
        Assert.Contains(words, scenario.Failure, StringComparison.Ordinal);
        if (fault != "log-off-hangs")
        {
            AssertRecovered(account);
        }
    }

    [Fact]
    public void A_failure_before_the_leg_changes_anything_touches_nothing()
    {
        var account = new ThrowawayLegFakeAccount { OpenCharGenNeverEnds = true };
        var start = account.Listed;
        var (scenario, status, _) = Run(account);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.False(scenario.Recovered);
        Assert.Equal(start, account.Listed);
        Assert.Empty(account.Deleted);
        Assert.Equal(0, account.LogOffs);
    }

    [Fact]
    public void A_throwaway_that_made_it_into_the_world_is_logged_off_and_deleted_after_a_failure_and_the_next_run_purges_it()
    {
        var account = new ThrowawayLegFakeAccount { WhereMetres = 5 };
        var (scenario, status, _) = Run(account);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.True(scenario.Recovered);
        AssertRecovered(account);
        Assert.Equal(1, account.LogOffs);
        Assert.Equal([Throwaway], account.Deleted);

        // The next run finds it pending: the leftover path purges it from Smoke and goes on to a clean pass.
        account.WhereMetres = 0.12; // the fault is fixed
        var next = new ThrowawayLegScenario(account, null, null, account.Purge);
        var again = ScenarioStatus.Running;
        for (var elapsed = 0.0; elapsed < 600 && again == ScenarioStatus.Running; elapsed += 0.05)
        {
            account.Tick(0.05);
            again = next.Step(account, elapsed);
        }

        Assert.True(next.Leftover);
        Assert.True(next.Failure is null, next.Failure);
        Assert.Equal(ScenarioStatus.Succeeded, again);
        Assert.True(next.Ok, next.Failure);
    }

    [Fact]
    public void An_invalid_name_that_ACE_accepted_is_deleted_by_the_recovery_and_no_other_character_is_touched()
    {
        var account = new ThrowawayLegFakeAccount { AcceptInvalidName = true };
        var (_, status, _) = Run(account);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains(ThrowawayLegScenario.InvalidName, account.Deleted);
        Assert.DoesNotContain(ThrowawayLegScenario.Smoke, account.Deleted);
        Assert.DoesNotContain(ThrowawayLegFakeAccount.Loopback, account.Deleted);
        Assert.Equal(SessionPhase.CharacterSelect, account.State.Phase);
    }

    [Fact]
    public void Stopping_the_leg_from_outside_runs_the_same_recovery()
    {
        var account = new ThrowawayLegFakeAccount();
        var scenario = new ThrowawayLegScenario(account, null, null, account.Purge);
        var status = ScenarioStatus.Running;
        var aborted = false;
        for (var elapsed = 0.0; elapsed < 600 && status == ScenarioStatus.Running; elapsed += 0.05)
        {
            account.Tick(0.05);
            if (!aborted && scenario.Stage == "tools-open")
            {
                aborted = true;
                scenario.Abort(account, "the host's time limit");
            }

            status = scenario.Step(account, elapsed);
        }

        Assert.True(aborted);
        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Contains("the host's time limit", scenario.Failure, StringComparison.Ordinal);
        AssertRecovered(account);
    }

    [Fact]
    public void A_login_that_never_reaches_the_list_fails_the_login_stage()
    {
        var account = new ThrowawayLegFakeAccount();
        account.State.Phase = SessionPhase.Idle;
        var (scenario, status, _) = Run(account);

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.StartsWith("login:", scenario.Failure, StringComparison.Ordinal);
        Assert.Equal(0, account.LogOffs);
    }

    [Fact]
    public void The_login_check_option_names_the_throwaway_leg_and_the_bare_form_stays_all()
    {
        Assert.Equal(LoginCheckKind.Throwaway, PlayOptions.Parse(["--login-check=throwaway"]).LoginCheck);
        Assert.Empty(PlayOptions.Parse(["--login-check=throwaway"]).Errors);
        Assert.Equal(LoginCheckKind.All, PlayOptions.Parse(["--login-check"]).LoginCheck);
        Assert.Equal(LoginCheckKind.All, PlayOptions.Parse(["--login-check=all"]).LoginCheck);
    }

    [Fact]
    public void The_screen_operations_have_the_shape_the_scenario_reads()
    {
        var done = ScreenOp.Completed("text", 2.5);
        Assert.True(done.Done && done.Succeeded);
        Assert.Equal("text", done.Text);
        Assert.Equal(2.5, done.Number);
        var failed = ScreenOp.Failed("why");
        Assert.True(failed.Done);
        Assert.False(failed.Succeeded);
        Assert.Equal("why", failed.Failure);
        Assert.True(double.IsNaN(failed.Number));
    }

    /// <summary>After a failure with the throwaway created: logged off at the list, the throwaway pending (or gone), every other character as it was.</summary>
    private static void AssertRecovered(ThrowawayLegFakeAccount account)
    {
        Assert.Equal(SessionPhase.CharacterSelect, account.State.Phase);
        if (account.Find(Throwaway) is { } throwaway)
        {
            Assert.True(throwaway.PendingDeletion, "the throwaway is left pending deletion");
        }

        Assert.True(account.Find(ThrowawayLegFakeAccount.Loopback) is { PendingDeletion: false });
        if (account.Find(ThrowawayLegScenario.Smoke) is { } smoke)
        {
            Assert.False(smoke.PendingDeletion);
        }

        Assert.DoesNotContain(ThrowawayLegScenario.Smoke, account.Deleted);
        Assert.DoesNotContain(ThrowawayLegFakeAccount.Loopback, account.Deleted);
    }
}
