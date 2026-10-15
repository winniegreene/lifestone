using Lifestone.Client.PortalSpace;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>Step PS6: the SmartBox's teleport flags (m2-log-in.md §4.7).</summary>
public sealed class TeleportProgressTests
{
    [Fact]
    public void Without_a_player_nothing_is_in_progress_and_nothing_completes()
    {
        var progress = new TeleportProgress();

        progress.Tick(true);

        Assert.False(progress.InProgress);
        Assert.False(progress.HasPlayer);
        Assert.False(progress.ConsumeTeleportOccurred());
        Assert.False(progress.OnPlayerTeleport(1, 0)); // retail ignores F751 without a player
        progress.OnPlayerPositionTeleported();
        Assert.False(progress.HasPlayer);
    }

    [Fact]
    public void Login_is_in_progress_from_the_player_s_creation_until_the_destination_is_ready()
    {
        var progress = new TeleportProgress();

        progress.OnPlayerCreated();
        Assert.True(progress.InProgress);
        Assert.True(progress.HasPlayer);
        Assert.False(progress.IsLocal);
        for (var i = 0; i < 1000; i++)
        {
            progress.Tick(false);
        }

        Assert.True(progress.InProgress);
        Assert.False(progress.HasBeenTeleported);

        progress.Tick(true);
        Assert.False(progress.InProgress);
        Assert.True(progress.PositionUpdateComplete);
        Assert.True(progress.HasBeenTeleported);
    }

    [Fact]
    public void Teleport_occurred_is_consumed_exactly_once()
    {
        var progress = new TeleportProgress();
        progress.OnPlayerCreated();
        progress.Tick(true);

        Assert.True(progress.ConsumeTeleportOccurred());
        Assert.False(progress.ConsumeTeleportOccurred());
        progress.Tick(true); // already complete: nothing new to report
        Assert.False(progress.ConsumeTeleportOccurred());
    }

    [Theory]
    [InlineData((ushort)5, (ushort)5, true)] // equal counts: retail accepts a repeat
    [InlineData((ushort)6, (ushort)5, true)]
    [InlineData((ushort)4, (ushort)5, false)]
    [InlineData((ushort)0x0000, (ushort)0xFFFF, true)] // the wrap is a step forward
    [InlineData((ushort)0xFFFF, (ushort)0x0000, false)]
    [InlineData((ushort)0x7FFF, (ushort)0x0000, true)]
    [InlineData((ushort)0x8000, (ushort)0x0000, false)] // at exactly 0x8000 apart retail's rule is asymmetric
    [InlineData((ushort)0x0000, (ushort)0x8000, true)]
    public void F751_is_accepted_when_its_sequence_is_not_older_than_the_player_s_stamp(ushort sequence, ushort stamp, bool accepted)
    {
        var progress = new TeleportProgress();
        progress.OnPlayerCreated();
        progress.Tick(true);

        Assert.Equal(accepted, progress.OnPlayerTeleport(sequence, stamp));

        Assert.Equal(accepted, progress.InProgress);
        Assert.Equal(accepted, progress.WaitingForTeleport);
        Assert.Equal(!accepted, progress.HasBeenTeleported); // an accepted F751 drops the unreported completion
    }

    [Fact]
    public void An_F751_waits_for_the_teleport_position()
    {
        var progress = new TeleportProgress();
        progress.OnPlayerCreated();
        progress.Tick(true);
        progress.ConsumeTeleportOccurred();

        Assert.True(progress.OnPlayerTeleport(2, 1));
        progress.Tick(true);
        Assert.True(progress.InProgress);
        Assert.True(progress.WaitingForTeleport);

        progress.OnPlayerPositionTeleported();
        Assert.False(progress.WaitingForTeleport);
        Assert.True(progress.InProgress);
        progress.Tick(true);
        Assert.False(progress.InProgress);
        Assert.True(progress.ConsumeTeleportOccurred());
    }

    [Fact]
    public void A_state_without_Hidden_also_ends_the_wait_and_a_hidden_one_does_not()
    {
        var progress = new TeleportProgress();
        progress.OnPlayerCreated();
        progress.Tick(true);
        progress.OnPlayerTeleport(1, 1);

        progress.OnPlayerStateApplied(TeleportProgress.HiddenState | 0x408);
        progress.Tick(true);
        Assert.True(progress.WaitingForTeleport);
        Assert.True(progress.InProgress);

        progress.OnPlayerStateApplied(0x408);
        Assert.False(progress.WaitingForTeleport);
        progress.Tick(true);
        Assert.False(progress.InProgress);
    }

    [Fact]
    public void A_state_change_without_a_wait_changes_nothing()
    {
        var progress = new TeleportProgress();
        progress.OnPlayerCreated();

        progress.OnPlayerStateApplied(0);

        Assert.True(progress.InProgress);
        Assert.False(progress.WaitingForTeleport);
    }

    [Fact]
    public void Hold_keeps_the_teleport_in_progress()
    {
        var progress = new TeleportProgress();
        progress.BeginLocal();
        progress.Hold(true);

        for (var i = 0; i < 100; i++)
        {
            progress.Tick(true);
        }

        Assert.True(progress.InProgress);
        Assert.True(progress.IsHeld);
        progress.Hold(false);
        progress.Tick(true);
        Assert.False(progress.InProgress);
    }

    [Fact]
    public void Removing_the_player_ends_the_teleport_and_drops_the_report()
    {
        var progress = new TeleportProgress();
        progress.OnPlayerCreated();
        progress.Tick(true);
        progress.OnPlayerTeleport(3, 3);

        progress.OnPlayerRemoved();

        Assert.False(progress.InProgress);
        Assert.False(progress.HasPlayer);
        Assert.False(progress.ConsumeTeleportOccurred());
        Assert.True(progress.WaitingForTeleport); // SmartBox::Reset leaves it; the next player's creation clears it
        progress.Tick(true);
        Assert.False(progress.ConsumeTeleportOccurred());

        progress.OnPlayerCreated();
        Assert.False(progress.WaitingForTeleport);
        Assert.True(progress.InProgress);
    }

    [Fact]
    public void BeginLocal_starts_a_teleport_with_no_player_object()
    {
        var progress = new TeleportProgress();

        progress.BeginLocal();

        Assert.True(progress.InProgress);
        Assert.True(progress.HasPlayer);
        Assert.True(progress.IsLocal);
        progress.Tick(true);
        Assert.False(progress.InProgress);
        Assert.True(progress.ConsumeTeleportOccurred());

        progress.BeginLocal(); // the next jump
        Assert.True(progress.InProgress);
        Assert.True(progress.IsLocal);

        progress.OnPlayerCreated(); // the server's player replaces the stand-in
        Assert.False(progress.IsLocal);
        Assert.True(progress.InProgress);
    }
}
