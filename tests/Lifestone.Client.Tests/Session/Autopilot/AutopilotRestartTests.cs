using System.Numerics;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Session;
using Xunit.Abstractions;
using static Lifestone.Client.Tests.Session.Autopilot.AutopilotRig;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M6 item 14, the waste at every start of the autopilot, over the scripted ACE of <see cref="AutopilotRig"/> and as pure rules. Proved: the bind is walked once and a relaunch reads its proof from the checkpoint by the character's guid
/// (a re-created character, a proof at another lifestone and a proof without the tie its book needs bind again, each by its named reason; a respawn away from the proven lifestone drops the proof loudly and she binds again when next
/// home); the move-on's restart carries a named hand-over, so the next run neither walks to the lifestone nor runs a second buff pass while the handed-over pass stands, and runs the pass when a line has lapsed; a resumed checkpoint whose
/// minutes are used fails at once by name with both numbers; and under a pending stop request a trip at home is cut at its safe point, a station away from home ends by itself, a cut trip is not begun again, and no move-on is taken.
/// </summary>
public sealed class AutopilotRestartTests(ITestOutputHelper output)
{
    private const uint Elsewhere = 0x7D640013; // an outdoor cell far from every ground's home
    private const uint LifestoneRecall = 1635;

    private static bool InFight(AutopilotScenario a, int kills = 0) => a.State == AutopilotState.Fight && a.CurrentRun is IStoppableFight f && f.Kills >= kills;

    private static AutopilotCheckpoint Stored(MemoryCheckpointStore store, string character = "BSD Mage") =>
        AutopilotCheckpoint.Parse(store.Json!, character, out var why) ?? throw new InvalidOperationException("the stored checkpoint does not parse: " + why);

    private static GroundHome Home => GroundCatalog.Get(Den).Home;

    private static BindProof AtHome(bool tied = true) => BindProof.Of(new CellFrame(Home.LifestoneCell, new AcFrame(Home.LifestonePoint + new Vector3(-2.7f, 4.1f, 0), Quaternion.Identity)), tied, "a test");

    /// <summary>A finished run's file of <paramref name="guid"/> that holds <paramref name="proof"/>.</summary>
    private static MemoryCheckpointStore FileWith(uint guid, BindProof proof) => new()
    {
        Json = new AutopilotCheckpoint { Character = "BSD Mage", CharacterGuid = guid, Ground = Den, Completed = true, Stop = "request:test", Bind = CheckpointBind.From(proof) }.ToJson(),
    };

    private ScenarioStatus RunToAFightAndStop(ApWorld w, AutopilotScenario autopilot)
    {
        w.Run(autopilot, a => InFight(a, 1));
        autopilot.RequestStop("test");
        return w.Run(autopilot);
    }

    // ------------------------------------------------------------------------------------------------ 1: the bind is proven once and stays proven

    [Fact]
    public void The_rule_proves_a_bind_at_the_home_lifestone_and_names_why_any_other_is_not()
    {
        Assert.Equal((false, BindProofRule.None), (BindProofRule.Decide(null, Home, true, "no checkpoint").Proven, BindProofRule.Decide(null, Home, true).Reason));
        Assert.Equal("no checkpoint", BindProofRule.Decide(null, Home, true, "no checkpoint").Text);

        var tied = BindProofRule.Decide(AtHome(), Home, true);
        Assert.Equal((true, BindProofRule.Proven), (tied.Proven, tied.Reason));
        Assert.Contains($"cell 0x{Home.LifestoneCell:X8}", tied.Text, StringComparison.Ordinal);

        var far = BindProofRule.Decide(BindProof.Of(new CellFrame(Elsewhere, new AcFrame(new Vector3(100, 100, 10), Quaternion.Identity)), true, "a test"), Home, false);
        Assert.Equal((false, BindProofRule.AnotherLifestone), (far.Proven, far.Reason));

        Assert.Equal((false, BindProofRule.TieUnproven), (BindProofRule.Decide(AtHome(tied: false), Home, true).Proven, BindProofRule.Decide(AtHome(tied: false), Home, true).Reason));
        Assert.Equal(BindProofRule.TieUnproven, BindProofRule.Decide(AtHome(tied: false), Home, null).Reason); // a book not read yet is a doubt, and a doubt is not proven
        Assert.True(BindProofRule.Decide(AtHome(tied: false), Home, false).Proven); // the tie serves Lifestone Recall alone: a book without it needs none
    }

    [Fact]
    public void A_respawn_agrees_within_the_lifestones_radius_and_not_beyond_it()
    {
        var proof = AtHome();
        Assert.True(BindProofRule.RespawnAgrees(proof, new CellFrame(Home.LifestoneCell, new AcFrame(Home.LifestonePoint, Quaternion.Identity)), out var near));
        Assert.InRange(near, 0, DeathLegVerdict.LifestoneRadiusMetres);
        Assert.False(BindProofRule.RespawnAgrees(proof, new CellFrame(Elsewhere, new AcFrame(new Vector3(100, 100, 10), Quaternion.Identity)), out var far));
        Assert.True(far > 1000, "metres " + far);
    }

    [Fact]
    public void The_checkpoint_gives_the_proof_back_to_the_same_guid_only_and_says_why_otherwise()
    {
        var proof = AtHome();
        var store = FileWith(0x50000001, proof);

        var read = AutopilotCheckpoint.ReadBindProof(store, "BSD Mage", 0x50000001, out var why);
        Assert.Equal((proof.LifestoneCell, proof.X, proof.Y, proof.Z, proof.Tied, proof.How), (read!.LifestoneCell, read.X, read.Y, read.Z, read.Tied, read.How));
        Assert.StartsWith("the checkpoint of character 0x50000001", why, StringComparison.Ordinal);

        Assert.Null(AutopilotCheckpoint.ReadBindProof(store, "BSD Mage", 0x50000002, out why)); // a character re-created under the same name
        Assert.Contains("another character's", why, StringComparison.Ordinal);
        Assert.Null(AutopilotCheckpoint.ReadBindProof(store, "BSD Mage", 0, out why)); // an unknown guid proves nothing
        Assert.Contains("another character's", why, StringComparison.Ordinal);
        Assert.Null(AutopilotCheckpoint.ReadBindProof(store, "Winnie", 0x50000001, out _));

        Assert.Null(AutopilotCheckpoint.ReadBindProof(new MemoryCheckpointStore(), "BSD Mage", 0x50000001, out why));
        Assert.StartsWith("no checkpoint file is there", why, StringComparison.Ordinal);
        var older = new MemoryCheckpointStore { Json = new AutopilotCheckpoint { Character = "BSD Mage", CharacterGuid = 0x50000001 }.ToJson() }; // a file written before the proof was kept
        Assert.Null(AutopilotCheckpoint.ReadBindProof(older, "BSD Mage", 0x50000001, out why));
        Assert.StartsWith("the checkpoint holds no bind proof", why, StringComparison.Ordinal);
        Assert.Null(AutopilotCheckpoint.ReadBindProof(new MemoryCheckpointStore { Json = "{ \"schema\": 1, \"bind\": {" }, "BSD Mage", 0x50000001, out why));
        Assert.StartsWith("the checkpoint is unreadable", why, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void The_bind_is_walked_once_and_a_relaunch_reads_its_proof_from_the_checkpoint(string key)
    {
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(key, output);
        var guid = w.Trip.Vendor.State.Self.Guid;
        var first = w.Autopilot(new AutopilotOptions(), store);
        Assert.Equal(ScenarioStatus.Succeeded, RunToAFightAndStop(w, first));

        Assert.Equal(1, w.Factory.BindStations); // the first start has no proof: it walks to the lifestone, once
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot bind: no proof is carried: no checkpoint file is there", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit bind: not proven this session", StringComparison.Ordinal) && l.Contains("no kept proof stands (none)", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot bind: proof kept: the Use confirmed at the lifestone in cell 0x", StringComparison.Ordinal));
        Assert.Equal((w.Ground.Home.LifestoneCell, true), (first.Bind!.LifestoneCell, first.Bind.Tied));
        var saved = Stored(store);
        Assert.True(saved.Completed);
        Assert.Equal((guid, w.Ground.Home.LifestoneCell, true), (saved.CharacterGuid, saved.Bind!.Cell, saved.Bind.Tied));

        // the relaunch: a new scenario over a new journey context (nothing of the session's facts), the same file
        var made = w.Factory.Made.Count;
        var lines = w.Lines.Count();
        var second = w.Autopilot(new AutopilotOptions(), store);
        Assert.Equal(ScenarioStatus.Succeeded, RunToAFightAndStop(w, second));

        Assert.Equal(1, w.Factory.BindStations); // no second walk to the lifestone
        Assert.Equal(["buff", "den-travel", "den-fight", "recall-home"], Kinds(w, made));
        var after = w.Lines.Skip(lines).ToList();
        Assert.Contains(after, l => l.StartsWith("checkpoint: the previous run finished (stop=request:test): starting fresh", StringComparison.Ordinal)); // the run is finished; the bind is the character's and is still read
        var carried = after.FindIndex(l => l.StartsWith($"autopilot bind: proof carried from the checkpoint of character 0x{guid:X8}", StringComparison.Ordinal));
        var skipped = after.FindIndex(l => l.StartsWith("autopilot audit bind: proven by the proof carried from the checkpoint of character 0x", StringComparison.Ordinal) && l.Contains("fix=none: no walk to the lifestone", StringComparison.Ordinal));
        Assert.True(carried >= 0 && skipped > carried, $"check before the skip: carried at {carried}, skipped at {skipped}");
        Assert.StartsWith("autopilot audit: place=home bind=proven ", second.LastAudit, StringComparison.Ordinal);
        Assert.NotNull(Stored(store).Bind); // and the second run's own file keeps it
    }

    [Fact]
    public void A_character_re_created_under_the_same_name_is_bound_nowhere_and_binds_again()
    {
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(Den, output);
        var guid = w.Trip.Vendor.State.Self.Guid;
        Assert.Equal(ScenarioStatus.Succeeded, RunToAFightAndStop(w, w.Autopilot(new AutopilotOptions(), store)));
        store.Json = store.Json!.Replace($"\"characterGuid\": {guid}", "\"characterGuid\": 1342177999", StringComparison.Ordinal); // the file is the old character's

        var recreated = new ApWorld(Den, output);
        var second = recreated.Autopilot(new AutopilotOptions(), store);
        Assert.Equal(ScenarioStatus.Succeeded, RunToAFightAndStop(recreated, second));

        Assert.Equal(1, recreated.Factory.BindStations);
        Assert.Contains(recreated.Lines, l => l.StartsWith("autopilot bind: no proof is carried: the checkpoint's bind proof is another character's (the file's BSD Mage 0x500002CF", StringComparison.Ordinal));
        Assert.Contains(recreated.Lines, l => l.StartsWith("autopilot audit bind: not proven this session", StringComparison.Ordinal) && l.Contains("no kept proof stands (none): the checkpoint's bind proof is another character's", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("another-lifestone", true)]
    [InlineData("tie-unproven", true)]
    [InlineData("no-recall-in-the-book", false)]
    public void A_kept_proof_is_checked_against_the_home_lifestone_and_the_tie_before_the_bind_is_skipped(string what, bool binds)
    {
        var w = new ApWorld(Den, output);
        var guid = w.Trip.Vendor.State.Self.Guid;
        var book = w.Trip.Vendor.State.Self.Spellbook;
        book.IsKnown = true;
        if (what == "tie-unproven")
        {
            book.Learn(LifestoneRecall);
        }

        var proof = what == "another-lifestone" ? BindProof.Of(new CellFrame(Elsewhere, new AcFrame(new Vector3(100, 100, 10), Quaternion.Identity)), true, "a test") : AtHome(tied: false);
        var autopilot = w.Autopilot(new AutopilotOptions(), FileWith(guid, proof));
        w.Run(autopilot, a => a.Audits >= 1); // the audit's bind check, and its bind when the proof does not stand
        autopilot.Dispose();

        Assert.Equal(binds ? 1 : 0, w.Factory.BindStations);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot bind: proof carried from the checkpoint of character 0x", StringComparison.Ordinal)); // carried either way; the audit's check decides
        if (binds)
        {
            Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit bind: not proven this session", StringComparison.Ordinal) && l.Contains($"no kept proof stands ({what})", StringComparison.Ordinal));
            Assert.Equal(Home.LifestoneCell, autopilot.Bind!.LifestoneCell); // the bind she made replaces what was kept
        }
        else
        {
            Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit bind: proven by the proof carried from", StringComparison.Ordinal) && l.Contains("not needed: the spellbook holds no Lifestone Recall", StringComparison.Ordinal));
        }
    }

    /// <summary>A death the autopilot did not script, whose teleport arrives at <paramref name="cell"/>: ACE's order (the notice, the teleport, the new position).</summary>
    private static void DieAndRespawnAt(ApWorld w, uint cell, Vector3 at)
    {
        w.Trip.Vendor.H.R.Bus.Publish(new PlayerDied("You have been slain by a scripted blow!", 0, DeathNoticeSource.VictimNotification));
        w.Trip.Vendor.TeleportTo(cell, at);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_respawn_at_the_proven_lifestone_keeps_the_proof_and_one_away_from_it_drops_it_loudly(bool away)
    {
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(Den, output);
        var autopilot = w.Autopilot(new AutopilotOptions(), store);
        w.Run(autopilot, a => InFight(a, 1));
        Assert.NotNull(autopilot.Bind);

        if (away)
        {
            DieAndRespawnAt(w, Elsewhere, new Vector3(100, 100, 10));
        }
        else
        {
            DieAndRespawnAt(w, w.Ground.Home.LifestoneCell, w.Ground.Home.LifestonePoint);
        }

        w.Run(autopilot, a => a.Cycles == 1 && InFight(a, 1)); // the death path ran and the fight goes on
        autopilot.RequestStop("test");
        Assert.Equal(ScenarioStatus.Succeeded, w.Run(autopilot));

        if (!away)
        {
            Assert.Contains(w.Lines, l => l.StartsWith("autopilot bind: the respawn agrees with the proof: it arrived in cell 0x", StringComparison.Ordinal));
            Assert.DoesNotContain(w.Lines, l => l.Contains("PROVEN WRONG", StringComparison.Ordinal));
            Assert.NotNull(Stored(store).Bind);
            return;
        }

        var wrong = Assert.Single(w.Lines, l => l.StartsWith("autopilot bind: PROVEN WRONG (respawn-elsewhere): the death's respawn arrived in cell 0x7D640013", StringComparison.Ordinal));
        Assert.Contains("The proof is dropped", wrong, StringComparison.Ordinal);
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot chat: Autopilot: the last death did not send her to the Holtburg lifestone", StringComparison.Ordinal));
        Assert.Null(autopilot.Bind);
        Assert.Null(Stored(store).Bind); // the file no longer proves it: the next start binds
        var next = w.Autopilot(new AutopilotOptions(), store);
        Assert.Equal(ScenarioStatus.Succeeded, RunToAFightAndStop(w, next));
        Assert.Equal(2, w.Factory.BindStations);
    }

    [Fact]
    public void A_bind_a_respawn_proved_wrong_is_made_again_when_she_is_next_home()
    {
        var w = new ApWorld(Den, output);
        var home = w.Ground.Home;
        w.Factory.Script = spec => spec.Kind == StationKinds.Death
            ? new ScriptedRun(spec.Name, _ => { w.Trip.Vendor.StandAt(home.LifestoneCell, home.LifestonePoint); return ScenarioStatus.Succeeded; }, () => "deaths=1 corpse_recovered=yes route=recall rebuffed=yes") // the death path ends at home
            : null;
        var autopilot = w.Autopilot(new AutopilotOptions(), FileWith(w.Trip.Vendor.State.Self.Guid, AtHome()));
        w.Run(autopilot, a => InFight(a, 1));
        Assert.Equal(0, w.Factory.BindStations); // the kept proof: no walk at the start
        var made = w.Factory.Made.Count;

        DieAndRespawnAt(w, Elsewhere, new Vector3(100, 100, 10));
        w.Run(autopilot, a => a.Cycles == 1 && InFight(a, 1));

        Assert.Equal(["death", "bind", "buff", "den-travel", "den-fight"], Kinds(w, made));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot bind: a respawn proved the bind wrong: she is home now and binds again at the Holtburg lifestone", StringComparison.Ordinal));
        Assert.Equal(home.LifestoneCell, autopilot.Bind!.LifestoneCell); // proven again, by her own bind
    }

    // ------------------------------------------------------------------------------------------------ 2 and 3: the hand-over

    private static BuffCycle.LineState Line(string name, bool active, double remaining) =>
        new(new ProfileLine(name, name, default, default, []), active, remaining);

    [Fact]
    public void A_handed_over_pass_stands_only_while_fresh_on_the_same_book_with_every_line_up()
    {
        var handOver = new AutopilotHandOver(0x50000001, Den, "rung-moved:cave", EndedAt: 1000, PassBook: 97, PassEndedAt: 996);
        BuffCycle.LineState[] up = [Line("Focus Self", true, 3504), Line("Impenetrability", true, 1731)];

        var stands = HandOverPassRule.Decide(handOver, 1000, 97, false, up, [], 300);
        Assert.True(stands.Stands);
        Assert.Contains("2 of 2 wanted line(s) up, the least 1731 s left (Impenetrability), the book still 97 spells", stands.Why, StringComparison.Ordinal);

        Assert.StartsWith("the ended run handed over no whole pass", HandOverPassRule.Decide(null, 1000, 97, false, up, [], 300).Why, StringComparison.Ordinal);
        Assert.StartsWith("the ended run handed over no whole pass", HandOverPassRule.Decide(handOver with { PassBook = null }, 1000, 97, false, up, [], 300).Why, StringComparison.Ordinal);
        var old = HandOverPassRule.Decide(handOver, 996 + HandOverPassRule.FreshSeconds + 1, 97, false, up, [], 300);
        Assert.Equal((false, true), (old.Stands, old.Why.StartsWith("stale: the pass ended 301 s ago", StringComparison.Ordinal)));
        Assert.False(HandOverPassRule.Decide(handOver, 990, 97, false, up, [], 300).Stands); // a clock behind the pass: another session
        Assert.StartsWith("stale: the spellbook changed since the pass (97 spells then, 98 now)", HandOverPassRule.Decide(handOver, 1000, 98, false, up, [], 300).Why, StringComparison.Ordinal);
        Assert.StartsWith("in doubt: the armour memory is not seeded", HandOverPassRule.Decide(handOver, 1000, 97, true, [], [], 300).Why, StringComparison.Ordinal);
        Assert.StartsWith("stale: a line has lapsed: Focus Self", HandOverPassRule.Decide(handOver, 1000, 97, false, [Line("Focus Self", false, 0), up[1]], [], 300).Why, StringComparison.Ordinal);
        Assert.StartsWith("stale: under the 300 s margin: Impenetrability", HandOverPassRule.Decide(handOver, 1000, 97, false, up, ["Impenetrability"], 300).Why, StringComparison.Ordinal);
    }

    /// <summary>Every wanted line of the mage's profile up on the player, as the server's enchantment list shows them after a pass (the scripted buff station casts nothing), and the armour memory seeded as the real pass seeds it.</summary>
    private static void EveryLineUp(ApWorld w)
    {
        foreach (var line in BuffCycle.OrderedLines.Where(l => !l.ByPlay))
        {
            w.Trip.Vendor.R.Enchant((ushort)line.Spells[0].Id);
        }

        w.Trip.Vendor.R.Tick();
        ArmourBuffLedger.For(w.Trip.View.State).MarkSeeded();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_run_started_with_a_hand_over_neither_binds_nor_buffs_again_while_the_pass_stands_and_buffs_when_a_line_has_lapsed(bool linesUp)
    {
        var w = new ApWorld(Den, output);
        var first = w.Autopilot(new AutopilotOptions()); // no checkpoint is kept: the hand-over alone carries what the run proved
        Assert.Equal(ScenarioStatus.Succeeded, RunToAFightAndStop(w, first));
        var handOver = first.HandOver;
        Assert.Equal((w.Trip.Vendor.State.Self.Guid, Den, "request:test"), (handOver.CharacterGuid, handOver.Ground, handOver.Stop));
        Assert.NotNull(handOver.Bind);
        Assert.True(handOver.PassBook is not null && double.IsFinite(handOver.PassEndedAt) && handOver.PassEndedAt <= handOver.EndedAt);
        Assert.NotNull(handOver.Kit);
        if (linesUp)
        {
            EveryLineUp(w);
        }
        else
        {
            ArmourBuffLedger.For(w.Trip.View.State).MarkSeeded(); // the lines are read one by one, and none is up in the scripted world
        }

        var made = w.Factory.Made.Count;
        var second = new AutopilotScenario(new AutopilotOptions(), w.Factory, w.Context(), handOver: handOver);
        Assert.Equal(ScenarioStatus.Succeeded, RunToAFightAndStop(w, second));

        Assert.Equal(1, w.Factory.BindStations);
        Assert.Equal(linesUp ? ["den-travel", "den-fight"] : ["buff", "den-travel", "den-fight"], Kinds(w, made).Take(linesUp ? 2 : 3)); // with a line down her own pass runs first, as at any start
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot hand-over: received from the run on {Den} that ended ", StringComparison.Ordinal) && l.Contains("(stop=request:test, character 0x", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith($"autopilot bind: proof carried from the hand-over of the run on {Den} that ended ", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot audit bind: proven by the proof carried from the hand-over of the run on", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot hand-over: stock: the ended run's last audit read kit=", StringComparison.Ordinal) && l.Contains("read again from the pack now: ", StringComparison.Ordinal));
        if (linesUp)
        {
            Assert.Contains(w.Lines, l => l.StartsWith("autopilot hand-over: buffs: check before: the whole pass that ended ", StringComparison.Ordinal) && l.EndsWith("her own pass is not run again (the fight keeps the lines up between kills as ever)", StringComparison.Ordinal));
            Assert.Contains(w.Lines, l => l.Contains("autopilot state known-state -> travel (cycle 1): the handed-over pass stands", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains(w.Lines, l => l.StartsWith("autopilot hand-over: buffs: check before: stale: a line has lapsed: ", StringComparison.Ordinal) && l.EndsWith(": her own pass runs", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_hand_over_of_another_character_or_of_another_sessions_clock_is_refused_by_name_and_the_start_is_cold()
    {
        var w = new ApWorld(Den, output);
        var guid = w.Trip.Vendor.State.Self.Guid;
        var other = new AutopilotScenario(new AutopilotOptions(), w.Factory, w.Context(), handOver: new AutopilotHandOver(guid + 1, Den, "rung-moved:cave", 0, AtHome()));
        w.Run(other, a => InFight(a));
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot hand-over: refused: it is character 0x", StringComparison.Ordinal) && l.EndsWith(": a cold start", StringComparison.Ordinal));
        Assert.Equal(1, w.Factory.BindStations);

        var later = new ApWorld(Den, output);
        var ahead = new AutopilotScenario(new AutopilotOptions(), later.Factory, later.Context(), handOver: new AutopilotHandOver(later.Trip.Vendor.State.Self.Guid, Den, "rung-moved:cave", later.Now + 1e6, AtHome()));
        later.Run(ahead, a => InFight(a));
        Assert.Contains(later.Lines, l => l.StartsWith("autopilot hand-over: refused: it was taken at ", StringComparison.Ordinal) && l.EndsWith("(another session's): a cold start", StringComparison.Ordinal));
        Assert.Equal(1, later.Factory.BindStations);
    }

    /// <summary>The dial over the scripted world, as <see cref="ClimberHostTests"/> builds it, with the hand-over passed on as the play host's kit does (<c>AutopilotKit.Create</c>): a level past the ladder's top ends each run ok at its first cycle, standing in for the move-on's stop.</summary>
    private sealed class Dial
    {
        public Dial(ITestOutputHelper output, Func<int, MoveOnAnswer> answer, bool atHome = true)
        {
            World = new ApWorld(Den, output, atHome: atHome);
            World.Level(127);
            var options = new AutopilotOptions { Job = AutopilotJob.LevelUp };
            Logic = new AutopilotHostLogic(
                carry =>
                {
                    HandOvers.Add(carry.HandOver);
                    var scenario = new AutopilotScenario(carry.Remaining(options), World.Factory, World.Context(), null, null, carry.Seed, carry.HandOver);
                    carry.Apply(scenario);
                    return scenario;
                },
                World.Trip.Context().Log,
                options,
                World.Ground)
            {
                MoveOnStop = stop => stop == "ladder-top",
                MoveOn = _ => answer(++Asked),
            };
            Logic.Stopped += Stopped.Add;
        }

        public ApWorld World { get; }

        public AutopilotHostLogic Logic { get; }

        public int Asked { get; private set; }

        public List<string> Stopped { get; } = [];

        public List<AutopilotHandOver?> HandOvers { get; } = [];

        public void RunUntilDone(int frames = 4000)
        {
            for (var i = 0; i < frames && Logic.Mode != AutopilotMode.Done; i++)
            {
                var t = World.Trip;
                t.Vendor.R.Tick(0.25);
                t.Shops.Pump();
                t.Xp.Pump();
                t.Vendor.R.Tick();
                Logic.Tick(World.Trip.View, World.Now);
            }

            World.Output.WriteLine(string.Join(Environment.NewLine, World.Lines.Where(l => l.StartsWith("autopilot", StringComparison.Ordinal) || l.StartsWith("ladder", StringComparison.Ordinal))));
        }
    }

    [Fact]
    public void The_move_ons_restart_hands_the_ended_runs_proofs_to_the_next_run_and_the_bind_is_walked_once()
    {
        var dial = new Dial(output, _ => MoveOnAnswer.Go);
        dial.Logic.Toggle(dial.World.Trip.View, reason: AutopilotHostLogic.ResumeReason);
        dial.RunUntilDone();

        Assert.Equal((1, 1), (dial.Asked, dial.Logic.MoveOns)); // the restarted run's same stop straight after is the named loop (AutopilotMoveOnLoopTests): the dial ends there
        Assert.Equal(2, dial.HandOvers.Count);
        Assert.Null(dial.HandOvers[0]); // the first press is a cold start
        var handOver = dial.HandOvers[1]!; // the restart is not
        Assert.Equal(("ladder-top", dial.World.Trip.Vendor.State.Self.Guid), (handOver.Stop, handOver.CharacterGuid));
        Assert.NotNull(handOver.Bind);
        Assert.Equal(1, dial.World.Factory.BindStations); // the first run bound; the restarted run read the proof
        var lines = dial.World.Lines.ToList();
        var taken = lines.FindIndex(l => l.StartsWith("ladder: move-on restart: hand-over taken from the run on", StringComparison.Ordinal));
        var carried = lines.FindIndex(l => l.StartsWith("autopilot host: the start carries the hand-over of the run that just ended (ladder-top on", StringComparison.Ordinal));
        var received = lines.FindIndex(l => l.StartsWith("autopilot hand-over: received from the run on", StringComparison.Ordinal));
        var proven = lines.FindIndex(l => l.StartsWith("autopilot audit bind: proven by the proof carried from the hand-over of the run on", StringComparison.Ordinal));
        Assert.True(taken >= 0 && carried > taken && received > carried && proven > received, $"taken {taken}, carried {carried}, received {received}, proven {proven}");
    }

    [Fact]
    public void A_refused_move_on_and_a_plain_start_carry_no_hand_over()
    {
        var dial = new Dial(output, _ => MoveOnAnswer.Refused("failed:rung-restart-refused: test says no"));
        dial.Logic.Toggle(dial.World.Trip.View, reason: AutopilotHostLogic.ResumeReason);
        dial.RunUntilDone();
        Assert.Equal(AutopilotMode.Done, dial.Logic.Mode);

        dial.Logic.MoveOnStop = _ => false;
        dial.Logic.Toggle(dial.World.Trip.View, reason: AutopilotHostLogic.ResumeReason); // a person's /auto after the refusal: a cold start
        dial.RunUntilDone();

        Assert.Equal([null, null], dial.HandOvers);
        Assert.DoesNotContain(dial.World.Lines, l => l.StartsWith("autopilot hand-over: received", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ 4: the minutes budget of a resumed checkpoint

    [Fact]
    public void The_minutes_rule_refuses_a_budget_at_or_under_the_minutes_used_and_says_what_to_pass()
    {
        Assert.Null(MinutesBudgetRule.Refusal(0, 500)); // no minutes rule
        Assert.Null(MinutesBudgetRule.Refusal(60, 59.9));
        Assert.Null(MinutesBudgetRule.Refusal(60, 0));
        var equal = MinutesBudgetRule.Refusal(60, 60);
        var under = MinutesBudgetRule.Refusal(60, 75.34);
        Assert.NotNull(equal);
        Assert.StartsWith("the checkpoint being resumed has already used 75.3 minutes and --autopilot-minutes asks 60 in all", under, StringComparison.Ordinal);
        Assert.Contains("Pass --autopilot-minutes over 75.3 (for 60 more minutes: --autopilot-minutes=136), or 0 for no minutes rule", under, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1.0, true)] // under the minutes used
    [InlineData(0.0, false)] // no minutes rule: it resumes
    [InlineData(600.0, false)] // minutes left: it resumes
    public void A_resumed_checkpoint_whose_minutes_are_used_fails_at_once_by_name_and_is_kept_for_the_next_start(double minutes, bool refused)
    {
        var store = new MemoryCheckpointStore();
        var w = new ApWorld(Den, output);
        var first = w.Autopilot(new AutopilotOptions { Minutes = 600 }, store);
        w.Run(first, a => InFight(a, 40)); // 80 s of fight and more: over a minute used
        first.Dispose(); // the process ends: the file holds an unfinished run
        var saved = Stored(store);
        Assert.False(saved.Completed);
        Assert.True(saved.MinutesUsed > 1, "minutes used " + saved.MinutesUsed);
        var made = w.Factory.Made.Count;
        var before = w.Now;

        var second = w.Autopilot(new AutopilotOptions { Minutes = minutes }, store);
        var status = refused ? w.Run(second) : w.Run(second, a => InFight(a, 1));

        if (!refused)
        {
            Assert.Equal(ScenarioStatus.Running, status);
            Assert.Null(second.StopName);
            return;
        }

        Assert.Equal(ScenarioStatus.Failed, status);
        Assert.Equal((AutopilotScenario.MinutesBudgetStop, false), (second.StopName, second.Ok));
        Assert.Equal(made, w.Factory.Made.Count); // nothing was started: no station, no recall
        Assert.True(w.Now - before < 1, "it fails in its first frame");
        var line = Assert.Single(w.Lines, l => l.StartsWith("autopilot stop=failed:minutes-budget-used: ", StringComparison.Ordinal));
        Assert.Contains(FormattableString.Invariant($"has already used {saved.MinutesUsed:0.#} minutes and --autopilot-minutes asks 1 in all"), line, StringComparison.Ordinal);
        Assert.Contains("Pass --autopilot-minutes over ", line, StringComparison.Ordinal);
        Assert.StartsWith("autopilot done ok=no ", second.Lines[^1], StringComparison.Ordinal);
        var kept = Stored(store);
        Assert.False(kept.Completed); // not a finished run: the next start, with minutes left, resumes it
        Assert.Equal(saved.MinutesUsed, kept.MinutesUsed, 2);
        Assert.Equal(saved.CycleTally.Kills + saved.Totals.Kills, kept.CycleTally.Kills + kept.Totals.Kills);

        var third = w.Autopilot(new AutopilotOptions { Minutes = 600 }, store);
        w.Run(third, a => InFight(a, 1));
        Assert.Null(third.StopName);
        Assert.Contains(w.Lines, l => l.StartsWith("checkpoint: read: resuming cycle 1", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------------------------------------ 5: a pending stop starts nothing new

    [Theory]
    [InlineData(Den)]
    [InlineData(Temple)]
    public void A_stop_request_during_a_trip_cuts_it_at_home_at_its_safe_point_and_nothing_is_bought(string key)
    {
        // The long round, 23:41 UTC: the stop was asked 9 s into a trip; the trip ran its 238 s, bought 81 items, walked back out and home again. Now: cut at home, nothing new.
        var w = new ApWorld(key, output, held: l => l.Name == "Prismatic Taper" ? l.TripUnder / 2 : l.Keep) { Prime = c => { c.Facts.LifestoneTied = true; c.Facts.SetPortalTie(key, PortalTie.Tied); } };
        w.OwnKit();
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Trip && a.CurrentRun is not null); // the audit's supply trip has begun, at home
        autopilot.RequestStop("stop-file");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(("request:stop-file", true), (autopilot.StopName, autopilot.Ok));
        Assert.Equal(["logistics"], Kinds(w)); // nothing was started after it: no buff, no travel, no way home (she is home)
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot stop: pending (request:stop-file): check before: the logistics station of the trip state is running, she is home", StringComparison.Ordinal) && l.Contains("it is cut here", StringComparison.Ordinal));
        Assert.Equal(0, Int(autopilot.Lines[^1], "bought")); // the supply trip of Short_of_tapers_at_the_start buys; this one was cut before its purchase
        Assert.Equal(0, Int(autopilot.Lines[^1], "trips"));
        Assert.StartsWith("autopilot done ok=yes cycles=0 stop=request:stop-file ", autopilot.Lines[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_stop_request_during_a_trip_that_began_in_the_ground_ends_at_home_with_no_way_back_and_no_second_way_home()
    {
        var w = new ApWorld(Den, output, atHome: false);
        var autopilot = w.Autopilot(new AutopilotOptions { TripAfterKills = 1 });
        w.Run(autopilot, a => a.State == AutopilotState.Trip && a.CurrentRun is not null);
        autopilot.RequestStop("test");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("request:test", autopilot.StopName);
        Assert.Equal(StationKinds.Logistics, w.Factory.Made[^1].Kind); // before item 14 the trip ran to its end in the ground and a recall home followed it
        Assert.DoesNotContain("recall-home", Kinds(w));
        Assert.Equal(AutopilotPlace.Home, AutopilotScenario.Classify(w.Trip.Vendor.State, w.Ground));
    }

    [Fact]
    public void A_stop_request_away_from_home_does_not_cut_the_station_which_ends_by_itself_then_the_way_home_and_the_stop()
    {
        var w = new ApWorld(Den, output) { Prime = c => c.Facts.LifestoneTied = true };
        var arrival = w.Ground.ArrivalDestination().Location.Position;
        var steps = 0;
        w.Factory.Script = spec => spec.Kind == StationKinds.DenTravel
            ? new ScriptedRun(spec.Name, step => { w.Trip.Vendor.StandAt(arrival.CellId, arrival.Local); steps = step; return step >= 20 ? ScenarioStatus.Succeeded : null; }, () => "route=recall") // a travel that takes frames, away from home from its first
            : null;
        var autopilot = w.Autopilot(new AutopilotOptions());
        w.Run(autopilot, a => a.State == AutopilotState.Travel && a.CurrentRun is not null && steps >= 2);
        autopilot.RequestStop("stop-file");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal(20, steps); // the travel in flight ended at its own end
        Assert.Equal(["buff", "den-travel", "recall-home"], Kinds(w)); // no fight was begun; the way home, then the stop
        Assert.Single(w.Lines, l => l.StartsWith("autopilot stop: pending (request:stop-file): the den-travel station of the travel state is not cut: she is away from home", StringComparison.Ordinal));
        Assert.Equal("request:stop-file", autopilot.StopName);
    }

    [Fact]
    public void Under_a_pending_stop_request_a_cut_trip_is_not_begun_again()
    {
        var w = new ApWorld(Den, output) { Prime = c => { c.Facts.LifestoneTied = true; c.Facts.SetPortalTie(Den, PortalTie.Tied); } };
        w.OwnKit();
        var autopilot = w.Autopilot(new AutopilotOptions(), seed: new AutopilotSeed(1, 0, PendingTrip: JourneyStations.TripByCounters)); // a hand-back with a trip that was cut
        autopilot.RequestStop("stop-file");
        var status = w.Run(autopilot);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Empty(Kinds(w)); // before item 14 the cut trip ran again from its start, whole, and then the stop came
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot stop=request:stop-file: a stop request is pending: the logistics station is not begun again", StringComparison.Ordinal));
    }

    [Fact]
    public void Under_a_pending_stop_request_the_move_on_is_not_taken_and_the_run_stops_by_the_request()
    {
        var (w, autopilot) = TrophyQuestTests.MakeClimber(output, tusk: false);
        w.Run(autopilot, a => InFight(a), 20000); // the run's first check kept her on the Lodge
        w.Run(autopilot, a => a.State == AutopilotState.Trip, 20000); // the trip after the first kill has begun, in the ground (away from home: it is not cut)
        Assert.Equal(AutopilotState.Trip, autopilot.State);
        w.Level(22); // past the Lodge's band: the cycle's check after the trip says rung-moved:cave
        autopilot.RequestStop("stop-file");
        var status = w.Run(autopilot, null, 20000);

        Assert.Equal(ScenarioStatus.Succeeded, status);
        Assert.Equal("request:stop-file", autopilot.StopName); // rung-moved:cave would have had the dial start the next rung's run
        // The move-on is refused by the pending stop (the engine line), or (the long run: the order desk's known-state reads her on the ground after the recall) the stop rule is due before the
        // buff station, so the move-on never arises; either way the run stops by the request and the dial below is never asked for a new run.
        Assert.Contains(w.Lines, l => l.StartsWith("autopilot stop: pending (request:stop-file): the move-on (rung-moved:cave) is not taken", StringComparison.Ordinal)
            || l.StartsWith("autopilot stop=request:stop-file: a stop rule is due before the buff station", StringComparison.Ordinal));
        Assert.Contains(w.Lines, l => l.Contains("autopilot: recalling home before the stop (stop=request:stop-file", StringComparison.Ordinal)); // she stood on the ground: home first
        Assert.Equal(AutopilotPlace.Home, AutopilotScenario.Classify(w.Trip.Vendor.State, w.Ground));
    }

    [Fact]
    public void The_dial_makes_no_move_on_restart_when_a_stop_was_asked()
    {
        // The scenario stops by the request itself when it sees one (the test above); this is the dial's own check, for a stop its rule calls a move-on while a stop was asked of the dial: the rule is widened to the run's stop to reach it.
        var dial = new Dial(output, _ => MoveOnAnswer.Go);
        dial.Logic.MoveOnStop = _ => true;
        dial.Logic.Toggle(dial.World.Trip.View, reason: AutopilotHostLogic.ResumeReason);
        dial.Logic.Stop(AutopilotHostLogic.StopFileReason);
        dial.RunUntilDone();

        Assert.Equal(AutopilotMode.Done, dial.Logic.Mode);
        Assert.Equal((0, 0), (dial.Asked, dial.Logic.MoveOns)); // the host's move-on hook is never asked: no new run
        Assert.Single(dial.HandOvers);
        Assert.Equal(["request:stop-file"], dial.Stopped);
        Assert.Contains(dial.World.Lines, l => l.Contains("ladder: move-on restart: NOT made after request:stop-file: a stop was asked (stop-file)", StringComparison.Ordinal));
    }
}
