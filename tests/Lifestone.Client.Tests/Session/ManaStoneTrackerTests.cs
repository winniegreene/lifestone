using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared.Net;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// <c>UseManaStone</c> (closes <c>TODO(m4-gear-mana)</c>) over the order ACE sends (<c>ManaStone.HandleActionUseOnTarget</c>, ACE 95ef426d <c>ManaStone.cs:59-233</c>; read from the source, not captured live): the "gives" line, the destroy roll's 0x0024 and
/// "The Mana Stone is destroyed.", then UseDone 0. Confirmed on the UseDone 0 with the line or the stone removed; a UseDone with a code, or an "already full" line before a UseDone 0, is a ServerError naming it.
/// </summary>
public sealed class ManaStoneTrackerTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Stone = 0x80000B01;
    private const uint Bow = 0x80000B02;

    private static WorldActionRig Rig()
    {
        var r = new WorldActionRig();
        r.Describe([new ContentDesc(Stone, 0), new ContentDesc(Bow, 0)], []);
        r.Item(Stone, Player);
        r.Item(Bow, Player);
        return r;
    }

    private static void Line(WorldActionRig r, string text) => r.H.ServerMessage(text, 0); // ChatMessageType.Broadcast

    [Fact]
    public void The_use_sends_0x0035_with_the_stone_first_and_the_target_second()
    {
        var r = Rig();
        var handle = r.Api.UseManaStone(Stone, Player);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.UseWithTarget));
        Assert.Equal(Stone, sent.Guid);
        Assert.Equal(Player.ToString("X8", CultureInfo.InvariantCulture), sent.Target);
        Assert.Equal(ActionKind.UseManaStone, handle.Kind);
        Assert.Equal(ActionStatus.Sent, handle.Status);
    }

    [Fact]
    public void A_charge_on_the_player_is_Confirmed_with_the_mana_given_and_the_stone_consumed()
    {
        var r = Rig();
        var handle = r.Api.UseManaStone(Stone, Player);
        r.Tick();

        Line(r, "The Mana Stone gives 4,496 points of mana to the following items: Fire Compound Crossbow, Qafiya, Celdon Sleeves.\nYour items are fully charged.");
        r.Raw(AceM3Writers.InventoryRemoveObject(Stone));
        Line(r, "The Mana Stone is destroyed.");
        Assert.False(handle.IsCompleted);
        r.UseDone();
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.Confirmed, outcome.Kind);
        Assert.Equal("gave 4496 mana to 3 items; fully charged; the stone was consumed", outcome.Detail);
    }

    [Fact]
    public void A_charge_that_falls_short_names_what_is_still_needed()
    {
        var r = Rig();
        var handle = r.Api.UseManaStone(Stone, Player);
        r.Tick();

        Line(r, "The Mana Stone gives 1,921 points of mana to the following items: Haebrean Girth, Chiran Helm, Ring.\nYou need 2,232 more mana to fully charge your items.");
        r.Raw(AceM3Writers.InventoryRemoveObject(Stone));
        r.UseDone();
        r.Tick();

        Assert.Equal("gave 1921 mana to 3 items; 2232 more needed; the stone was consumed", handle.Outcome!.Value.Detail);
    }

    [Fact]
    public void A_charge_on_one_item_names_the_item()
    {
        var r = Rig();
        var handle = r.Api.UseManaStone(Stone, Bow);
        r.Tick();

        Line(r, "The Mana Stone gives 1,000 points of mana to the Serpent's Flight.");
        r.Raw(AceM3Writers.InventoryRemoveObject(Stone));
        r.UseDone();
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
        Assert.StartsWith("gave 1000 mana to the Serpent's Flight;", handle.Outcome.Value.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_to_charge_is_a_ServerError_with_ACEs_code_and_line()
    {
        var r = Rig();
        var handle = r.Api.UseManaStone(Stone, Player);
        r.Tick();

        Line(r, "You have no items equipped that need mana.");
        r.UseDone(0x0036);
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Equal(0x0036u, outcome.Code);
        Assert.Contains("ActionCancelled", outcome.Detail, StringComparison.Ordinal);
        Assert.Contains("You have no items equipped that need mana.", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_already_full_is_a_ServerError_though_the_UseDone_is_0_and_the_stone_is_kept()
    {
        var r = Rig();
        var handle = r.Api.UseManaStone(Stone, Bow);
        r.Tick();

        Line(r, "The Serpent's Flight is already full of mana.");
        r.UseDone();
        r.Tick();

        var outcome = handle.Outcome!.Value;
        Assert.Equal(ActionOutcomeKind.ServerError, outcome.Kind);
        Assert.Contains("already full of mana", outcome.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_silent_UseDone_0_is_nothing_happened_and_a_stone_or_target_of_0_is_refused_at_once()
    {
        var r = Rig();
        var handle = r.Api.UseManaStone(Stone, Player);
        r.Tick();
        r.UseDone();
        r.Tick();
        Assert.Equal(ActionOutcomeKind.ServerError, handle.Outcome!.Value.Kind);
        Assert.StartsWith("nothing happened", handle.Outcome.Value.Detail, StringComparison.Ordinal);

        Assert.Equal(ActionOutcomeKind.Refused, r.Api.UseManaStone(0, Player).Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, r.Api.UseManaStone(Stone, 0).Outcome!.Value.Kind);
    }

    [Theory]
    [InlineData("The Mana Stone gives 5,000 points of mana to the following items: Helm of Leikotha's Tears, Branith's Shirt.\nYour items are fully charged.", 5000, 2, 0)]
    [InlineData("The Mana Stone gives 175 points of mana to the following items: Ring.\nYou need 12,345 more mana to fully charge your items.", 175, 1, 12345)]
    [InlineData("The Mana Stone gives 3,267 points of mana to the Protective Drudge Charm.", 3267, 1, 0)]
    public void The_gives_line_is_read_with_its_numbers(string text, int given, int items, int more)
    {
        Assert.True(Lifestone.Client.Session.Actions.Trackers.ManaStoneLine.TryParse(text, out var line));
        Assert.Equal((given, items, more), (line.Given, line.Items.Count, line.StillNeeded));
        Assert.False(Lifestone.Client.Session.Actions.Trackers.ManaStoneLine.TryParse("The Mana Stone is destroyed.", out _));
    }
}
