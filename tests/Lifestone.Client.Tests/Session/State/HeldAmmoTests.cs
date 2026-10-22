using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session.State;

/// <summary>M4 1.2 s1: <see cref="HeldAmmo"/> / <see cref="IGameSessionView.Ammo"/> follow the wield, 0x0197 and the unwield of the ammunition slot (0x800000).</summary>
public sealed class HeldAmmoTests
{
    private const uint Player = ReducerHarness.PlayerGuid;
    private const uint Arrows = 0x80000422;
    private const uint Mace = 0x80000010;

    private static void Send(ReducerHarness h, byte[] message) => Assert.Equal(DispatchOutcome.Decoded, InboundDispatcher.Dispatch(h.Context, message, h.Reducer).Outcome);

    [Fact]
    public void Ammo_is_null_without_ammo_then_follows_the_wield_the_stack_and_the_unwield()
    {
        var h = new ReducerHarness(track: false);
        h.EnterWorld();
        Assert.Null(HeldAmmo.Of(h.State));

        var basis = ReducerHarness.Create(Arrows, ReducerHarness.Stamps(), null, name: "Arrow");
        h.Reducer.OnCreateObject(h.Context, basis with { Weenie = basis.Weenie with { WeenieClassId = 300, StackSize = 100, Header = WeenieHeaderFlags.StackSize } }, isUpdate: false);
        Send(h, AceM3Writers.WieldObject(Player, 1, Arrows, (int)HeldAmmo.Location));
        Assert.Equal(new HeldAmmo(300, Arrows, 100), HeldAmmo.Of(h.State));

        Send(h, AceM3Writers.SetStackSize(1, Arrows, 99, 0));
        Assert.Equal(new HeldAmmo(300, Arrows, 99), HeldAmmo.Of(h.State));

        Send(h, AceM3Writers.PutObjInContainer(Player, 2, Arrows, Player, 0, 0)); // unwield: back to the main pack (C2 restore, 0x0019)
        Assert.Null(HeldAmmo.Of(h.State));
    }

    [Fact]
    public void Something_wielded_elsewhere_is_not_ammo()
    {
        var h = new ReducerHarness(track: false);
        h.EnterWorld();
        h.Reducer.OnCreateObject(h.Context, ReducerHarness.Create(Mace, ReducerHarness.Stamps(), null, name: "Mace"), isUpdate: false);
        Send(h, AceM3Writers.WieldObject(Player, 1, Mace, 0x00100000));

        Assert.Null(HeldAmmo.Of(h.State));
    }
}
