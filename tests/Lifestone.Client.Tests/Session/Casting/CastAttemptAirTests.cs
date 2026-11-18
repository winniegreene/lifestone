using Lifestone.Client.Movement;
using Lifestone.Client.Session.Casting;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Casting;

/// <summary>
/// M6 14 airborne: the cast policy's two checks of the air (<see cref="CastAttempt"/>; live 2026-11-13, three Flame Bolt VI casts sent in the air and refused 0x04EB). Before: no try leaves while the bound body is in the air, the hold
/// counts nothing and is left out of the rules' cap, and the try goes out once the body stands. After: ACE's 0x04EB is a wait, not a try, not a fizzle and not a refusal of the spell, so the same level goes again; a hold past its
/// bound ends <see cref="CastEnd.InTheAir"/>.
/// </summary>
public sealed class CastAttemptAirTests
{
    [Fact]
    public void No_try_is_sent_while_the_body_is_in_the_air_and_it_goes_out_once_the_body_stands()
    {
        var t = new CastAttemptTests.Rig();
        var body = new AirBody { InTheAir = true };
        t.R.Api.Move.Bind(body, false);
        t.A.Start(new CastRequest { Levels = CastAttemptTests.Bolts, Index = 2, Target = CastAttemptTests.Monster, Rules = CastRules.BoltD14, Purpose = "test" }, t.Now);
        t.Pump();
        for (var i = 0; i < 8; i++)
        {
            t.R.Tick(0.25);
            Assert.Equal(CastEnd.Running, t.Step());
            t.Pump();
        }

        Assert.Equal(0, t.Sends);
        Assert.Equal(0, t.A.Report.Tries);
        Assert.Single(t.R.H.Log.Entries, e => e.Message.Contains("test: the body is in the air: the cast of", StringComparison.Ordinal));

        body.InTheAir = false;
        Assert.Equal(CastEnd.Running, t.Step());
        t.Pump();
        Assert.Equal(1, t.Sends);
        Assert.Equal(CastAttemptTests.BoltIII, t.LastSpell);
        Assert.Contains("the body stands, after 2.0 s held for the air", t.LastLog, StringComparison.Ordinal);
        Assert.Equal(CastEnd.Landed, t.Projectile());
        var report = t.A.Report;
        Assert.Equal((1, 0, 2), (report.Tries, report.AirRefusals, report.LevelIndex));
        Assert.Equal(2.0, report.AirSeconds, 3);
    }

    [Fact]
    public void ACE_s_0x04EB_is_a_wait_not_a_try_and_the_same_level_goes_again_once_the_body_stands()
    {
        var t = new CastAttemptTests.Rig();
        var body = new AirBody();
        t.R.Api.Move.Bind(body, false);
        t.A.Start(new CastRequest { Levels = CastAttemptTests.Bolts, Index = 2, Target = CastAttemptTests.Monster, Rules = CastRules.BoltD14, Purpose = "test" }, t.Now);
        t.Pump();
        for (var answer = 1; answer <= 4; answer++)
        {
            Assert.Equal(CastEnd.Running, t.Answer(CastAttempt.InTheAirCode)); // never a ServerError end: the fight's refused-in-a-row rule never sees it
            Assert.Equal((0, answer), (t.A.Report.Tries, t.A.Report.AirRefusals)); // not a try
            Assert.Equal(answer, t.Sends);
            body.InTheAir = answer == 2; // the second answer finds her still in the air: no resend until she stands
            t.R.Tick(CastAttempt.AirRetrySeconds);
            t.Step();
            t.Pump();
            Assert.Equal(answer + (body.InTheAir ? 0 : 1), t.Sends);
            if (body.InTheAir)
            {
                body.InTheAir = false;
                t.Step();
                t.Pump();
                Assert.Equal(answer + 1, t.Sends);
            }

            Assert.Equal(CastAttemptTests.BoltIII, t.LastSpell); // the same level every time: no level down, no fizzle
        }

        Assert.Equal(CastEnd.Landed, t.Projectile());
        var report = t.A.Report;
        Assert.Equal((1, 4, 0, 0, 2), (report.Tries, report.AirRefusals, report.Fizzles, report.LevelsDownSpent, report.LevelIndex));
        Assert.Contains("in the air 4 (", t.LastLog, StringComparison.Ordinal);
        Assert.Equal(4, t.R.H.Log.Entries.Count(e => e.Message.Contains("ACE answered 0x04EB YouCantDoThatWhileInTheAir", StringComparison.Ordinal)));
        var tally = new CastTally();
        tally.Add(report);
        Assert.Equal((1, 4), (tally.Tries, tally.AirRefusals));
    }

    [Fact]
    public void The_air_is_left_out_of_the_cap_and_a_hold_past_its_bound_ends_InTheAir_with_nothing_sent()
    {
        var t = new CastAttemptTests.Rig();
        var body = new AirBody { InTheAir = true };
        t.R.Api.Move.Bind(body, false);
        t.A.Start(new CastRequest { Levels = [CastAttemptTests.BuffI], Rules = CastRules.Once.WithMaxSeconds(1.5), Purpose = "test" }, t.Now);
        t.Pump();
        t.R.Tick(3.0);
        Assert.Equal(CastEnd.Running, t.Step()); // twice the cap in the air: the cap counts none of it
        t.R.Tick(CastAttempt.MaxAirSeconds - 3.0);
        Assert.Equal(CastEnd.InTheAir, t.Step());
        Assert.Equal(0, t.Sends);
        Assert.Equal("in-the-air", CastEnd.InTheAir.Name());
        Assert.False(CastEnd.InTheAir.Succeeded());
        Assert.Contains("the moment's, not the spell's", t.A.Report.Why, StringComparison.Ordinal);
        Assert.Equal(CastAttempt.MaxAirSeconds, t.A.Report.AirSeconds, 3);

        var standing = new CastAttemptTests.Rig(); // no body bound: nothing is in the air, the try goes at once
        standing.Start(CastRules.Once, [CastAttemptTests.BuffI], 0);
        Assert.Equal(1, standing.Sends);
    }

    /// <summary>A body that says whether it is in the air; every movement intent is ignored.</summary>
    private sealed class AirBody : IPlayerMovementIntents
    {
        public bool InTheAir { get; set; }

        public CellFrame Position => default;

        public bool OnGround => !InTheAir;

        public void HandleCommand(uint command, bool start, float speed = 1f)
        {
        }

        public void SetHoldRun(bool on)
        {
        }

        public void SetHoldSidestep(bool on)
        {
        }

        public void ToggleAutoRun()
        {
        }

        public void CommenceJump(double now)
        {
        }

        public void DoJump(double now)
        {
        }
    }
}
