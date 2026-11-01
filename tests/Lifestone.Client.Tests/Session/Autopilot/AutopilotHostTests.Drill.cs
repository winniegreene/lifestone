using System.Numerics;
using System.Text;
using Lifestone.Client.Play;
using Lifestone.Client.RetailUi.Chat;
using Lifestone.Client.Session.Autopilot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Session.Autopilot;

/// <summary>
/// M5 7 D145: the control drill (<see cref="ControlDrill"/>) over the scripted ACE: its steps run in order, each verdict reads the state then, and its hands are the person's paths (Move Forward through the key lock, lines through the chat's
/// command table). The hands here are a fake body that walks while Move Forward is down and the lock lets it through.
/// </summary>
public sealed partial class AutopilotHostTests
{
    private sealed class FakeHands : IControlDrillHands
    {
        private readonly Rig _rig;
        private readonly ChatCommands _commands;
        private readonly StringBuilder _chat = new();
        private bool _walking;
        private double _x = 50;

        public FakeHands(Rig rig)
        {
            _rig = rig;
            _commands = new ChatCommands(rig.View.Actions, () => null, () => false) { Autopilot = new Controls(rig) };
            rig.Host.ChatNotice += line => _chat.Append(line).Append('\n');
        }

        public List<string> Frames { get; } = [];

        public List<string> Typed { get; } = [];

        public CellFrame? Position => new CellFrame(0x01D90120, new AcFrame(new Vector3((float)_x, 60, 0), Quaternion.Identity));

        public string ChatText => _chat.ToString();

        /// <summary>The body walks 4 m a second while the key is down and the lock let it through.</summary>
        public void Step(double seconds)
        {
            if (_walking)
            {
                _x += 4 * seconds;
            }
        }

        public bool Forward(bool down)
        {
            var swallowed = _rig.Moves.KeyLock.Swallow(MovementBindings.MoveForward, "MoveForward", down, _rig.World.Now);
            _walking = down && !swallowed;
            return true;
        }

        public bool Type(string line)
        {
            Typed.Add(line);
            foreach (var shown in _commands.Run(line).Lines)
            {
                _chat.Append(shown.Text).Append('\n');
            }

            return true;
        }

        public void Frame(string name) => Frames.Add(name);
    }

    private sealed class Controls(Rig rig) : IAutopilotChatControls
    {
        public string Stop() => rig.Host.StopHere(rig.View);

        public string Auto()
        {
            if (rig.Host.Mode is AutopilotMode.Running or AutopilotMode.Stopping)
            {
                return "already running";
            }

            rig.Host.Toggle(rig.View, reason: AutopilotHostLogic.ResumeReason);
            return "resumes";
        }
    }

    [Fact]
    public void M5_7_D145_the_control_drill_runs_its_steps_in_order_and_each_verdict_reads_the_state_then()
    {
        var rig = new Rig(output, Den, new AutopilotOptions { TripAfterKills = 4 });
        var hands = new FakeHands(rig);
        var drill = new ControlDrill(hands, rig.World.Trip.Context().Log);
        rig.Host.Toggle(rig.View, "--autopilot-start");
        for (var i = 0; i < 40000 && !drill.Finished; i++)
        {
            rig.Frame();
            hands.Step(0.25);
            drill.Tick(rig.Host, rig.View, rig.World.Now);
        }

        foreach (var v in drill.Verdicts)
        {
            output.WriteLine(v);
        }

        rig.Dump();
        Assert.True(drill.Finished, "the drill finished: " + string.Join(" | ", drill.Verdicts));
        string Verdict(string id) => drill.Verdicts.Single(v => v.StartsWith("step=" + id + " ", StringComparison.Ordinal));
        Assert.Contains("verdict=pass", Verdict("a"), StringComparison.Ordinal); // W mid-fight did nothing and the notice showed
        Assert.Contains("verdict=pass", Verdict("b1"), StringComparison.Ordinal); // /stop: in place, nothing held, unlocked
        Assert.Contains("verdict=pass", Verdict("b2"), StringComparison.Ordinal); // then W moved him
        Assert.Matches(@"^step=c1 verdict=(pass|fail) off_spot_m=10\.0 ", Verdict("c1")); // walked off the spot (the scripted world has no caster or stance: c1 and c2 read them and say so)
        Assert.Matches(@"^step=c2 verdict=(pass|fail) kills=\d+->\d+ .* mode=running keys=locked ", Verdict("c2")); // /auto resumed: running, keys locked, a kill
        Assert.Contains("verdict=pass", Verdict("d1"), StringComparison.Ordinal); // /stop on the trip: in place, unlocked
        Assert.Contains("verdict=pass", Verdict("d2"), StringComparison.Ordinal); // /auto 5 s later: running, keys locked
        Assert.Contains("verdict=pass", Verdict("d3"), StringComparison.Ordinal); // and the trip's end
        Assert.All(drill.Verdicts, static v => Assert.Matches(@"^step=(a|b1|b2|c1|c2|d1|d2|d3) verdict=(pass|fail) ", v));
        Assert.Equal(["/stop", "/auto"], hands.Typed.Take(2));
        Assert.Contains("control-drill-a-locked", hands.Frames);
        Assert.Contains("control-drill-b-stopped", hands.Frames);
        Assert.Contains("control-drill-b-moved", hands.Frames);
        Assert.Contains(rig.World.Lines, static l => l.Contains(ControlDrill.Prefix + " finished:", StringComparison.Ordinal));
        rig.Host.Dispose();
    }

    [Fact]
    public void M5_7_D145_a_drill_step_whose_precondition_is_gone_fails_by_name_and_ends_the_drill()
    {
        var rig = new Rig(output, Den, new AutopilotOptions());
        var hands = new FakeHands(rig);
        var drill = new ControlDrill(hands, rig.World.Trip.Context().Log);
        rig.Host.Toggle(rig.View);
        for (var i = 0; i < 40000 && !drill.Verdicts.Any(); i++)
        {
            rig.Frame();
            drill.Tick(rig.Host, rig.View, rig.World.Now);
        }

        Assert.Contains("verdict=pass", drill.Verdicts.Single(), StringComparison.Ordinal); // step a
        rig.Host.StopHere(rig.View, "a person, between the drill's steps"); // the drill must not trust step a: b reads the mode again
        drill.Tick(rig.Host, rig.View, rig.World.Now);
        Assert.True(drill.Finished);
        Assert.StartsWith("step=b1 verdict=fail precondition: the autopilot is not running (mode interrupted)", drill.Verdicts[^1], StringComparison.Ordinal);
        rig.Host.Dispose();
    }
}
