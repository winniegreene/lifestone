using System.Buffers.Binary;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Tests.FakeAce;

namespace Lifestone.Net.Tests.Session;

/// <summary>
/// <see cref="IAcSession.Pump"/>'s budget (m2-plan §3.3 N5): in the world it dispatches until the budget is spent and
/// leaves the rest waiting, in order, while acks, NAKs and timers still run; before the world everything is dispatched.
/// The sink's cost is simulated by moving the virtual clock inside each callback.
/// </summary>
public sealed class PumpBudgetTests
{
    [Fact]
    public void Five_hundred_creates_are_spread_over_pumps_in_order_and_acks_still_go_out()
    {
        var script = SessionRig.DefaultScript();
        script = WithObjects(script, 500);
        using var rig = new SessionRig(script);
        rig.ToCharacterSelect();

        var acksWhileBacklogged = 0;
        rig.Harness.Network.Drop = (from, to, datagram) =>
        {
            if (to.Port == rig.Harness.ServerEndPoint.Port && BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(4)) == 0x4000
                && rig.PumpResults.Count > 0 && rig.PumpResults[^1].Pending > 0)
            {
                acksWhileBacklogged++;
            }

            return false;
        };
        rig.Budget = TimeSpan.FromMilliseconds(1);
        rig.Sink.OnAnyMessage = _ => rig.Harness.Time.Advance(TimeSpan.FromMilliseconds(1));
        var firstWorldPump = rig.PumpResults.Count;
        rig.Session.EnterWorld(SessionRig.HeroGuid);

        Assert.True(rig.RunUntil(() => rig.Sink.Opcodes.Count(o => o == MessageOpcodes.ObjectCreate) == 501, TimeSpan.FromSeconds(10)));

        var worldPumps = rig.PumpResults.Skip(firstWorldPump).ToList();
        Assert.All(worldPumps, p => Assert.InRange(p.Messages, 0, 1));
        Assert.Contains(worldPumps, p => p.Pending > 100 && p.BudgetExhausted);
        var sequences = rig.Sink.Dispatched.Select(d => d.Sequence).ToList();
        Assert.Equal(Enumerable.Range(0, sequences.Count).Select(i => (uint)i), sequences);
        Assert.True(acksWhileBacklogged > 0, "no ack went out while messages waited");
        Assert.Equal(SessionPhase.InWorld, rig.Session.Phase);
    }

    [Fact]
    public void A_zero_budget_still_dispatches_one_message_per_pump()
    {
        using var rig = new SessionRig(WithObjects(SessionRig.DefaultScript(), 20));
        rig.ToCharacterSelect();
        rig.Budget = TimeSpan.Zero;
        rig.Session.EnterWorld(SessionRig.HeroGuid);

        Assert.True(rig.RunUntil(() => rig.Sink.Opcodes.Count(o => o == MessageOpcodes.ObjectCreate) == 21, TimeSpan.FromSeconds(2)));
        Assert.Contains(rig.PumpResults, p => p.Messages == 1 && p.Pending > 0);
    }

    [Fact]
    public void Before_the_world_every_message_is_dispatched_whatever_the_budget()
    {
        using var rig = new SessionRig();
        rig.Budget = TimeSpan.Zero;
        rig.Sink.OnAnyMessage = _ => rig.Harness.Time.Advance(TimeSpan.FromMilliseconds(10));
        rig.ToCharacterSelect();

        Assert.Contains(rig.PumpResults, p => p.Messages >= 2);
        Assert.All(rig.PumpResults, p => Assert.Equal(0, p.Pending));
    }

    [Fact]
    public void Pump_is_not_reentrant()
    {
        using var rig = new SessionRig();
        Exception? thrown = null;
        rig.Sink.OnAnyMessage = _ => thrown ??= Record.Exception(() => rig.Session.Pump(TimeSpan.Zero));
        rig.ToCharacterSelect();

        Assert.IsType<InvalidOperationException>(thrown);
    }

    private static FakeAceScript WithObjects(FakeAceScript template, int count)
    {
        var script = new FakeAceScript { WorldObjectsOnEnter = count };
        foreach (var account in template.Accounts.Values)
        {
            var copy = script.AddAccount(account.Name, SessionRig.Password);
            copy.Characters.AddRange(account.Characters);
        }

        return script;
    }
}
