namespace Lifestone.Loopback.Tests.Session;

/// <summary>M6 7 B: <see cref="CasterWield.Decide"/>, the pure decision of the shared stand-in journeys' wield step (no backend): a caster in hand needs none, the pack's caster is wielded once, the wait is bounded and ends in a named failure.</summary>
public sealed class CasterWieldTests
{
    [Fact]
    public void A_caster_in_hand_is_done_whatever_else_is_true()
    {
        Assert.Equal(WieldDecision.Done, CasterWield.Decide(inHand: true, packCaster: false, issued: false, elapsed: 0));
        Assert.Equal(WieldDecision.Done, CasterWield.Decide(inHand: true, packCaster: true, issued: true, elapsed: 99));
    }

    [Fact]
    public void The_pack_caster_is_wielded_once()
    {
        Assert.Equal(WieldDecision.Issue, CasterWield.Decide(false, true, issued: false, elapsed: 1));
        Assert.Equal(WieldDecision.Wait, CasterWield.Decide(false, true, issued: true, elapsed: 2));
    }

    [Fact]
    public void No_caster_waits_for_the_pack_then_fails_after_the_bound()
    {
        Assert.Equal(WieldDecision.Wait, CasterWield.Decide(false, false, false, CasterWield.WieldSeconds));
        Assert.Equal(WieldDecision.Fail, CasterWield.Decide(false, false, false, CasterWield.WieldSeconds + 0.1));
        Assert.Equal(WieldDecision.Fail, CasterWield.Decide(false, true, issued: true, elapsed: CasterWield.WieldSeconds + 0.1));
    }
}
