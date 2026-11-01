using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Client.Session.Vitals;
using Lifestone.Client.Tests.Session.Fighter;
using Lifestone.Client.Tests.Session.Journey;
using Lifestone.Client.Tests.Session.Vitals;

namespace Lifestone.Client.Tests.Session.Casting
{
    /// <summary>
    /// The second adoption of the cast policy (M5 item 1 lane K4, D13): the vitals keeper, the harm-heal script, the recall, the pre-trip heal and the journey's script legs send their casts, mode changes, uses and drinks as
    /// <see cref="ActionSource.Automation"/> and send every cast through a <see cref="CastAttempt"/> (the action log's source and the attempt's own log lines name both), with the fizzle rule each had before; the kill
    /// switch stops a keeper pass and a recall with nothing sent after it and a named end; dry run answers DryRun for a keeper cast. The pre-trip heal's two tests are in the partial class below (its rig is private to
    /// <c>DeathScenarioTests</c>). The heal station's casts have no offline rig (its gear scenario and kit are the live character's): TODO(k4-heal-station-offline), compiled here and run by the loopback station host.
    /// </summary>
    public sealed class CastAdoptionTests
    {
        private const uint HealSelfIII = 1158;
        private static readonly uint[] HealSelfToIII = [6, 1157, HealSelfIII];
        private static readonly uint[] HarmAndHeal = [.. VitalsMeans.HarmSelf, .. VitalsMeans.HealSelf, .. VitalsMeans.StaminaToManaSelf];

        private static List<ActionLogEntry> Entries(ActionLog log)
        {
            var entries = new List<ActionLogEntry>();
            for (var i = 0; i < log.Count; i++)
            {
                entries.Add(log[i]);
            }

            return entries;
        }

        private static bool CastLine(string line, string rules, string contains) =>
            line.Contains("[cast]", StringComparison.Ordinal) && line.Contains("(" + rules + ", Automation)", StringComparison.Ordinal) && line.Contains(contains, StringComparison.Ordinal);

        private static void AssertAutomation(ActionLog log, params ActionKind[] kinds)
        {
            var entries = Entries(log);
            foreach (var kind in kinds)
            {
                Assert.Contains(entries, e => e.Kind == kind);
            }

            Assert.All(entries.Where(e => kinds.Contains(e.Kind)), e => Assert.True(e.Source == ActionSource.Automation, $"{e.Kind} was sent as {e.Source}: {e.Summary}"));
        }

        // ------------------------------------------------------------------------------------------------ the vitals keeper

        [Fact]
        public void The_keepers_cast_and_mode_changes_are_automation_and_the_cast_goes_through_a_Utility_3_attempt()
        {
            var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 100 });
            rig.Keeper.NeedsFor(new VitalsNeeds());

            Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

            AssertAutomation(rig.R.H.ActionLog, ActionKind.CastSpell, ActionKind.SetCombatMode);
            Assert.DoesNotContain(Entries(rig.R.H.ActionLog), e => e.Source == ActionSource.Host);
            Assert.Contains(rig.R.H.Log.Lines, l => CastLine(l, "Utility(3)", "vitals Heal Self III: try 1"));
            Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("[cast]", StringComparison.Ordinal) && l.Contains("vitals Heal Self III: unproven after 1 tries", StringComparison.Ordinal));
            Assert.Equal(["mode:8", "cast:1158", "cast:1158", "mode:1"], rig.Used); // the same order as before the adoption
        }

        [Fact]
        public void The_keepers_kit_and_potion_uses_and_the_mana_potion_drink_are_automation()
        {
            var heal = new C2VitalsRig(new C2Character { KitUses = 10, Potions = 5, Health = 100 });
            heal.KitRolls(false, false);
            heal.Keeper.NeedsFor(new VitalsNeeds());
            Assert.Equal(VitalsKeeperState.Ready, heal.RunToEnd());
            Assert.Equal(["kit", "kit", "potion", "kit"], heal.Used);
            AssertAutomation(heal.R.H.ActionLog, ActionKind.UseWithTarget, ActionKind.Drink);

            var mana = new C2VitalsRig(new C2Character { ManaPotions = 2, Mana = 10 });
            mana.Keeper.NeedsFor(new VitalsNeeds(NextCastMana: 60));
            mana.RunToEnd();
            Assert.True(mana.Keeper.ManaPotions > 0, "the keeper drank no Mana Potion");
            AssertAutomation(mana.R.H.ActionLog, ActionKind.Drink);
            Assert.DoesNotContain(Entries(mana.R.H.ActionLog), e => e.Source == ActionSource.Host);
        }

        [Fact]
        public void Two_fizzles_are_retried_inside_one_attempt_and_the_count_and_fields_are_the_keepers_as_before()
        {
            var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 100 });
            rig.FizzleNext(HealSelfIII, 2);
            rig.Keeper.NeedsFor(new VitalsNeeds());

            Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

            Assert.Equal(2, rig.Keeper.Fizzles);
            Assert.Equal([(HealSelfIII, true), (HealSelfIII, true), (HealSelfIII, false)], rig.Casts.Take(3).ToArray());
            Assert.Contains(rig.R.H.Log.Lines, l => CastLine(l, "Utility(3)", "try 3"));
            Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("vitals Heal Self III: unproven after 3 tries", StringComparison.Ordinal));
            Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("Heal Self III landed after 2 fizzle(s)", StringComparison.Ordinal));
            Assert.True(rig.Keeper.HealSelfCasts >= 1);
        }

        [Fact]
        public void The_third_fizzle_in_a_row_ends_the_attempt_Fizzled_and_sets_the_line_aside_as_the_keeper_always_did()
        {
            var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Potions = 5, Spells = HealSelfToIII, Health = 100 });
            rig.FizzleNext(HealSelfIII, 3);
            rig.Keeper.NeedsFor(new VitalsNeeds());

            Assert.Equal(VitalsKeeperState.Ready, rig.RunToEnd());

            Assert.Equal(3, rig.Keeper.Fizzles);
            Assert.Equal(0, rig.Keeper.HealSelfCasts);
            Assert.Equal(3, rig.Used.Count(u => u == "cast:1158")); // three casts in all, the cap of Utility(3), never a fourth
            Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("vitals Heal Self III: fizzled after 3 tries", StringComparison.Ordinal));
            Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("fizzled 3 times in a row; Heal Self is set aside", StringComparison.Ordinal));
        }

        [Fact]
        public void The_streak_of_fizzles_stays_the_keepers_across_attempts()
        {
            // A mana of exactly the cast's ceiling: the first fizzle spends some, so the attempt ends ManaShort with the retry unsent (the policy reads the mana before a retry), and the next looks cast again as the mana
            // returns. Each attempt fizzles once; the streak of three in a row is the keeper's across them, so the third attempt has no retry left (one try, not three) and the line goes aside.
            var probe = new C2VitalsRig(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 100 });
            var ceiling = GearCasts.ManaCeiling(probe.Table[HealSelfIII], probe.R.H.State.Self.Inventory.Equipped.Count);
            var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 100, Mana = ceiling });
            rig.FizzleNext(HealSelfIII, 3);
            rig.Keeper.NeedsFor(new VitalsNeeds());

            rig.RunToEnd();

            var lines = rig.R.H.Log.Lines;
            Assert.Equal(3, rig.Keeper.Fizzles);
            Assert.Equal(3, rig.Used.Count(u => u == "cast:1158")); // three attempts of one try each
            Assert.Equal(2, lines.Count(l => l.Contains("vitals Heal Self III: mana-short after 1 tries", StringComparison.Ordinal))); // the retry under the ceiling was not sent
            Assert.Contains(lines, l => l.Contains("Heal Self III fizzled (1 in a row); the next look decides again", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("Heal Self III fizzled (2 in a row); the next look decides again", StringComparison.Ordinal));
            Assert.Contains(lines, l => l.Contains("vitals Heal Self III: fizzled after 1 tries", StringComparison.Ordinal)); // no retry left: the two before count
            Assert.Contains(lines, l => l.Contains("fizzled 3 times in a row; Heal Self is set aside", StringComparison.Ordinal));
        }

        [Fact]
        public void The_kill_switch_mid_pass_stops_the_keeper_with_nothing_sent_after_it_and_a_named_end()
        {
            var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 100 });
            rig.Keeper.NeedsFor(new VitalsNeeds());
            for (var i = 0; i < 40 && !rig.Used.Contains("mode:8"); i++)
            {
                rig.Frame();
            }

            Assert.Equal(["mode:8"], rig.Used); // the magic mode is in flight; the cast would follow it
            rig.R.Api.KillSwitch = true;
            var sends = rig.R.H.Net.Sends.Count;
            for (var i = 0; i < 40; i++)
            {
                rig.Frame();
            }

            Assert.Equal(VitalsKeeperState.Blocked, rig.Keeper.State);
            Assert.Equal(VitalsKeeper.KillSwitchWhy, rig.Keeper.Why);
            Assert.Equal(["mode:8"], rig.Used);
            Assert.Equal(sends, rig.R.H.Net.Sends.Count);
            Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("Blocked why=kill-switch", StringComparison.Ordinal));
        }

        [Fact]
        public void The_kill_switch_between_two_casts_ends_the_pass_after_the_cast_in_flight_landed()
        {
            var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 100 });
            rig.Keeper.NeedsFor(new VitalsNeeds());
            for (var i = 0; i < 80 && !rig.Used.Contains("cast:1158"); i++)
            {
                rig.Frame();
            }

            rig.R.Api.KillSwitch = true; // the first cast is on the wire: it finishes, the second is never sent
            var sends = rig.R.H.Net.Sends.Count;
            for (var i = 0; i < 40; i++)
            {
                rig.Frame();
            }

            Assert.Equal(VitalsKeeperState.Blocked, rig.Keeper.State);
            Assert.Equal(VitalsKeeper.KillSwitchWhy, rig.Keeper.Why);
            Assert.Equal(1, rig.Used.Count(u => u == "cast:1158"));
            Assert.Equal(sends, rig.R.H.Net.Sends.Count);
        }

        [Fact]
        public void Dry_run_answers_DryRun_for_the_keepers_cast_and_sends_nothing()
        {
            var rig = new C2VitalsRig(new C2Character { HealingClass = 1, Spells = HealSelfToIII, Health = 100, Mode = CombatModes.Magic });
            rig.R.Api.DryRun = true;
            rig.Keeper.NeedsFor(new VitalsNeeds());
            for (var i = 0; i < 12; i++)
            {
                rig.Frame();
            }

            var cast = Assert.Single(Entries(rig.R.H.ActionLog), e => e.Kind == ActionKind.CastSpell);
            Assert.Equal((ActionSource.Automation, ActionOutcomeKind.DryRun), (cast.Source, cast.Outcome.Kind));
            Assert.Empty(rig.Used);
            Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("[cast]", StringComparison.Ordinal) && l.Contains("vitals Heal Self III: dry-run after 1 tries", StringComparison.Ordinal));
            Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("Heal Self III was not carried out (DryRun", StringComparison.Ordinal));
        }

        // ------------------------------------------------------------------------------------------------ the harm-heal script

        [Fact]
        public void The_harm_heal_scripts_casts_are_automation_through_Timed_60_attempts_and_a_fizzle_is_retried_inside_one()
        {
            var rig = new C3HarmRig(new C2Character { HealingClass = 1, LifeMagicRanks = 600, Spells = HarmAndHeal });
            rig.Inner.FizzleNext(2071, 2);

            Assert.Equal(HarmHealState.Done, rig.RunToEnd());

            AssertAutomation(rig.R.H.ActionLog, ActionKind.CastSpell, ActionKind.SetCombatMode);
            Assert.DoesNotContain(Entries(rig.R.H.ActionLog), e => e.Source == ActionSource.Host);
            Assert.Equal(2, rig.Script.Fizzles);
            Assert.Equal([(2071u, true), (2071u, true), (2071u, false)], rig.Inner.Casts.Take(3).ToArray());
            Assert.Contains(rig.R.H.Log.Lines, l => CastLine(l, "Timed(60)", "harm-heal harm: try 1"));
            Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("harm-heal harm: unproven after 3 tries", StringComparison.Ordinal));
            Assert.Contains(rig.R.H.Log.Lines, l => CastLine(l, "Timed(60)", "harm-heal heal: try 1"));
        }

        [Fact]
        public void The_harm_heal_scripts_cap_still_ends_a_run_of_fizzles_named_and_dry_run_ends_it_at_the_mode_change()
        {
            var capped = new C3HarmRig(new C2Character { HealingClass = 1, LifeMagicRanks = 600, Spells = HarmAndHeal });
            foreach (var id in VitalsMeans.HarmSelf)
            {
                capped.Inner.FizzleNext(id, 100_000);
            }

            Assert.Equal(HarmHealState.Failed, capped.RunToEnd());
            Assert.Equal("harm-cap", capped.Script.Why);
            Assert.True(capped.Script.Fizzles > 0 && capped.Script.Harms == 0);

            var dry = new C3HarmRig(new C2Character { HealingClass = 1, LifeMagicRanks = 600, Spells = HarmAndHeal });
            dry.R.Api.DryRun = true;
            Assert.Equal(HarmHealState.Failed, dry.RunToEnd());
            Assert.Equal("mode:DryRun", dry.Script.Why);
            Assert.Empty(dry.Inner.Used);
        }

        // ------------------------------------------------------------------------------------------------ the recall

        [Fact]
        public void The_recalls_cast_is_automation_through_a_Travel_5_attempt()
        {
            var rig = new C1cRecallRig(null);

            Assert.Equal(RecallStatus.Done, rig.RunRecall());

            AssertAutomation(rig.R.H.ActionLog, ActionKind.CastSpell);
            Assert.DoesNotContain(Entries(rig.R.H.ActionLog), e => e.Source == ActionSource.Host);
            Assert.Contains(rig.R.H.Log.Lines, l => CastLine(l, "Travel(5)", "recall Lifestone Recall: try 1 spell (1635)"));
            Assert.Contains(rig.R.H.Log.Lines, l => l.Contains("recall Lifestone Recall: unproven after 1 tries", StringComparison.Ordinal));
        }

        [Fact]
        public void The_recalls_fizzle_rule_is_five_retries_in_one_attempt_and_the_sixth_fizzle_fails_the_recall()
        {
            var lands = new C1cRecallRig(null) { FizzleFirst = 5 };
            Assert.Equal(RecallStatus.Done, lands.RunRecall());
            Assert.Equal((6, 5), (lands.Casts, lands.Recall.Fizzles)); // five retried, the sixth cast landed
            Assert.Matches(@"^recalls=1 recall_s=\d+\.\d recall_fizzles=5$", lands.Recall.Fields);
            Assert.Contains(lands.R.H.Log.Lines, l => l.Contains("fizzled (fizzle 5 of at most 5); casting again", StringComparison.Ordinal));

            var fails = new C1cRecallRig(null) { Fizzle = true };
            Assert.Equal(RecallStatus.Failed, fails.RunRecall());
            Assert.Equal((6, 6, "recall:cast"), (fails.Casts, fails.Recall.Fizzles, fails.Recall.Failure));
            Assert.Contains("over the cap of 5", fails.Recall.Detail, StringComparison.Ordinal);
            Assert.Contains(fails.R.H.Log.Lines, l => l.Contains("recall Lifestone Recall: fizzled after 6 tries", StringComparison.Ordinal));
            Assert.Matches(@"^recalls=0 recall_s=\d+\.\d recall_fizzles=6 recall_why=recall:cast$", fails.Recall.Fields);
        }

        [Fact]
        public void The_kill_switch_mid_recall_stops_the_retry_with_nothing_sent_after_it_and_a_named_end()
        {
            var rig = new C1cRecallRig(null) { Fizzle = true };
            Assert.True(rig.Recall.Begin(rig.Now));
            for (var i = 0; i < 40 && rig.Casts == 0; i++)
            {
                rig.Frame();
            }

            Assert.Equal(1, rig.Casts);
            rig.R.Api.KillSwitch = true; // the first cast is in flight: it fizzles, and the retry is never sent
            var sends = rig.R.H.Net.Sends.Count;
            for (var i = 0; i < 40 && rig.Recall.Status == RecallStatus.Running; i++)
            {
                rig.Frame();
            }

            Assert.Equal(RecallStatus.Failed, rig.Recall.Status);
            Assert.Equal("recall:kill-switch", rig.Recall.Failure);
            Assert.EndsWith("recall_why=recall:kill-switch", rig.Recall.Fields, StringComparison.Ordinal);
            Assert.Equal(1, rig.Casts);
            Assert.Equal(sends, rig.R.H.Net.Sends.Count);
        }

        [Fact]
        public void The_kill_switch_before_the_recall_casts_ends_it_at_once_with_nothing_sent()
        {
            var rig = new C1cRecallRig(null);
            rig.R.Api.KillSwitch = true;
            var sends = rig.R.H.Net.Sends.Count;
            Assert.True(rig.Recall.Begin(rig.Now));
            rig.Frame();

            Assert.Equal((RecallStatus.Failed, "recall:kill-switch", 0), (rig.Recall.Status, rig.Recall.Failure, rig.Casts));
            Assert.Equal(sends, rig.R.H.Net.Sends.Count);
        }

        // ------------------------------------------------------------------------------------------------ the journey's script legs

        [Fact]
        public void The_bind_stations_tie_cast_and_mode_changes_are_automation_and_the_cast_is_a_Travel_3_attempt()
        {
            var t = new J3bRig { TieFizzles = 2 };
            var ctx = t.Context();
            var (run, status) = t.RunStation(new BindStation(), ctx);

            Assert.True(status == ScenarioStatus.Succeeded && run.Ok, run.Failure + " / " + run.VerdictFields());
            Assert.EndsWith("tie_fizzles=2", run.VerdictFields(), StringComparison.Ordinal);
            AssertAutomation(t.R.H.ActionLog, ActionKind.CastSpell, ActionKind.SetCombatMode, ActionKind.Use);
            Assert.DoesNotContain(Entries(t.R.H.ActionLog), e => e.Source == ActionSource.Host);
            Assert.Equal(3, t.Events.Count(e => e.StartsWith("cast:2644@", StringComparison.Ordinal))); // two fizzles, the third landed: one attempt
            Assert.Contains(t.R.H.Log.Lines, l => CastLine(l, "Travel(3)", "journey cast: try 1 spell (2644)"));
            Assert.Contains(t.R.H.Log.Lines, l => l.Contains("journey cast: unproven after 3 tries", StringComparison.Ordinal));
        }

        [Fact]
        public void The_script_legs_cap_is_three_retries_and_a_fourth_fizzle_is_the_named_refusal()
        {
            var t = new J3bRig { TieFizzles = 4 };
            var ctx = t.Context();
            var (run, _) = t.RunStation(new BindStation(), ctx);

            Assert.False(run.Ok);
            Assert.Null(ctx.Facts.LifestoneTied);
            Assert.EndsWith("tie_fizzles=4", run.VerdictFields(), StringComparison.Ordinal);
            Assert.Equal(4, t.Events.Count(e => e.StartsWith("cast:2644@", StringComparison.Ordinal)));
            Assert.Contains(((ScriptLeg)run).Problems, p => p.Contains("YourSpellFizzled 0x0402", StringComparison.Ordinal));
            Assert.Contains(t.R.H.Log.Lines, l => l.Contains("journey cast: fizzled after 4 tries", StringComparison.Ordinal));
        }
    }
}

namespace Lifestone.Client.Tests.Session.Mage
{
    /// <summary>The pre-trip heal's adoption tests (M5 item 1 lane K4, D13), here because the scenario's rig is private to <c>DeathScenarioTests</c>; the rest of the adoption tests are in <c>CastAdoptionTests</c>.</summary>
    public sealed partial class DeathScenarioTests
    {
    }
}
