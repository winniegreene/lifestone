using Lifestone.Client.Session.Contracts;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 3.2 s1: PropertyInt 20 (CoinValue) as a reader on <see cref="PlayerQualities"/> with a <see cref="CoinValueChanged"/> from the 02CD. Capture C1: a buy of a 14-pyreal kit sent <c>02CD CoinValue 9986</c> (10000 before) and the sale
/// back <c>02CD CoinValue 9995</c>, each before the UseDone.
/// </summary>
public sealed class CoinValueTests
{
    [Fact]
    public void CoinValue_reads_PropertyInt_20_and_is_0_until_it_is_known()
    {
        var rig = new VendorRig(coins: 0);
        Assert.Equal(0, rig.State.Self.Qualities.CoinValue);
        Assert.Equal(20u, PlayerQualities.CoinValueProperty);

        rig.Coins(10000);

        Assert.Equal(10000, rig.State.Self.Qualities.CoinValue);
        Assert.True(rig.State.Self.Qualities.TryGetInt(20, out var raw));
        Assert.Equal(10000, raw);
    }

    [Fact]
    public void A_02CD_with_a_new_value_raises_CoinValueChanged_with_the_previous_total()
    {
        var rig = new VendorRig(coins: 10000);
        rig.Events.Clear();

        rig.Coins(9986); // C1: the buy
        rig.Coins(9995); // C1: the sale back

        var changes = rig.Events.OfType<CoinValueChanged>().ToList();
        Assert.Equal([new CoinValueChanged(10000, 9986), new CoinValueChanged(9986, 9995)], changes);
        Assert.Equal(9995, rig.State.Self.Qualities.CoinValue);
    }

    [Fact]
    public void The_same_total_again_raises_nothing()
    {
        var rig = new VendorRig(coins: 10000);
        rig.Events.Clear();

        rig.Coins(10000);

        Assert.Empty(rig.Events.OfType<CoinValueChanged>());
    }

    [Fact]
    public void Another_int_property_does_not_raise_CoinValueChanged()
    {
        var rig = new VendorRig(coins: 10000);
        rig.Events.Clear();

        rig.R.PrivateInt(PlayerQualities.LevelProperty, 12);

        Assert.Empty(rig.Events.OfType<CoinValueChanged>());
        Assert.Equal(12, rig.State.Self.Qualities.Level);
    }

    [Fact]
    public void The_event_comes_before_the_QualitiesChanged_every_int_raises_and_the_total_is_stored_by_then()
    {
        var rig = new VendorRig(coins: 10000);
        var order = new List<string>();
        var seenAtEvent = -1;
        rig.H.R.Bus.Subscribe((in CoinValueChanged e) =>
        {
            order.Add("coin");
            seenAtEvent = rig.State.Self.Qualities.CoinValue;
        });
        rig.H.R.Bus.Subscribe((in QualitiesChanged e) => order.Add("qualities"));

        rig.Coins(9000);

        Assert.Equal(["coin", "qualities"], order);
        Assert.Equal(9000, seenAtEvent);
    }

    [Fact]
    public void A_logoff_forgets_the_coins()
    {
        var rig = new VendorRig(coins: 10000);

        rig.H.R.Reducer.OnSessionEvent(Lifestone.Net.Session.SessionEvent.LoggedOff(rig.H.R.Time.GetTimestamp(), Lifestone.Net.Session.SessionPhase.CharacterSelect));

        Assert.Equal(0, rig.State.Self.Qualities.CoinValue);
    }
}
