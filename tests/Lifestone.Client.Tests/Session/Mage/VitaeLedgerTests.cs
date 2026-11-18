using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Tests.PortalSpace;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session.Mage;

/// <summary>M6 6 F1: <see cref="VitaeLedger"/> counts ACE's vitae-reduced Magic line and reports the vitae.</summary>
public sealed class VitaeLedgerTests
{
    private const string Reduced = "Your experience has reduced your Vitae penalty!";

    private static ChatReceived Line(string text, uint type = 7, ChatKind kind = ChatKind.System) => new(new ChatLine(1.0, kind, type, null, 0, text));

    private static EnchantmentDesc Vitae(float value) =>
        new(666, 1, 666, 0, 0, 0.0, -1.0, 0x50000001, 0f, 0f, 0.0, EnchantmentRules.Multiplicative, 0, value, 0, EnchantmentBucket.Vitae);

    [Fact]
    public void The_reduced_line_is_counted_once_a_step()
    {
        var view = new FakeSession();
        using var ledger = new VitaeLedger();
        ledger.Start(view);
        view.Bus.Publish(Line(Reduced));
        view.Bus.Publish(Line(Reduced));
        Assert.Equal(2, ledger.Steps);
    }

    [Fact]
    public void Other_lines_are_ignored()
    {
        var view = new FakeSession();
        using var ledger = new VitaeLedger();
        ledger.Start(view);
        view.Bus.Publish(Line("You hit the Tusker for 12 points of damage."));
        view.Bus.Publish(Line(Reduced, type: 3)); // the right text in another chat type
        view.Bus.Publish(Line(Reduced, kind: ChatKind.Tell));
        view.Bus.Publish(Line(Reduced + " "));
        Assert.Equal(0, ledger.Steps);
    }

    [Fact]
    public void Dispose_stops_the_count()
    {
        var view = new FakeSession();
        var ledger = new VitaeLedger();
        ledger.Start(view);
        ledger.Dispose();
        view.Bus.Publish(Line(Reduced));
        Assert.Equal(0, ledger.Steps);
        Assert.Equal(0, view.Bus.Subscribers<ChatReceived>());
    }

    [Fact]
    public void The_fields_say_none_without_vitae_and_the_percent_with_it()
    {
        var view = new FakeSession();
        using var ledger = new VitaeLedger();
        Assert.Equal("vitae=none vitae_steps=0", ledger.Fields); // before Start
        ledger.Start(view);
        Assert.Equal("vitae=none vitae_steps=0", ledger.Fields);
        view.State.Self.Qualities.Enchantments = [Vitae(0.95f)];
        Assert.Equal("vitae=5 vitae_steps=0", ledger.Fields);
        view.Bus.Publish(Line(Reduced));
        view.State.Self.Qualities.Enchantments = [Vitae(0.96f)];
        Assert.Equal("vitae=4 vitae_steps=1", ledger.Fields);
        view.State.Self.Qualities.Enchantments = [];
        Assert.Equal("vitae=none vitae_steps=1", ledger.Fields);
    }
}
