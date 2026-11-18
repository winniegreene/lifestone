using Lifestone.Client.Session.Contracts;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M6 1 L: the pure parts of <see cref="ThrowawayReset"/>, plain unit tests (no backend). What they prove: the list's reading (a live row wins over pending ones, names compared ignoring case, other characters ignored), and the
/// delete's decision: confirmed with the Throwaway pending goes on; ACE's hold refusal (CharacterError.Delete 0x6 with the Throwaway still listed live, @95ef426d CharacterHandler.cs:302-306) is waited out up to the bound and
/// then fails by name; a confirmed delete the list does not show pending, another code, or a 0x6 with the Throwaway no longer live fails at once.
/// </summary>
public sealed class ThrowawayResetTests
{
    private static CharacterSlot Slot(string name, bool pending, uint guid = 1) => new(guid, name, pending ? 1u : 0u);

    [Fact]
    public void The_list_reads_live_over_pending_and_ignores_case_and_other_characters()
    {
        Assert.Equal(ThrowawayListed.None, ThrowawayReset.Classify([Slot("Lifestone Tools", false), Slot("Lifestone Smoke", false)]));
        Assert.Equal(ThrowawayListed.Pending, ThrowawayReset.Classify([Slot("Lifestone Tools", false), Slot("Lifestone Throwaway", true)]));
        Assert.Equal(ThrowawayListed.Live, ThrowawayReset.Classify([Slot("Lifestone Throwaway", true, 1), Slot("lifestone throwaway", false, 2)]));
        Assert.Equal(ThrowawayListed.None, ThrowawayReset.Classify([]));
    }

    [Fact]
    public void A_confirmed_delete_goes_on_only_when_the_list_shows_the_Throwaway_pending()
    {
        var confirmed = new ActionOutcome(ActionOutcomeKind.Confirmed);
        Assert.Equal(ThrowawayDeleteStep.Done, ThrowawayReset.AfterDelete(confirmed, ThrowawayListed.Pending, 1, out _));
        Assert.Equal(ThrowawayDeleteStep.Fail, ThrowawayReset.AfterDelete(confirmed, ThrowawayListed.Live, 1, out var why));
        Assert.Contains("not pending deletion", why, StringComparison.Ordinal);
    }

    [Fact]
    public void Aces_hold_refusal_is_waited_out_to_the_bound_then_fails_by_name()
    {
        var hold = new ActionOutcome(ActionOutcomeKind.ServerError, ThrowawayReset.CharacterErrorDelete, "character error Delete");
        for (var attempt = 1; attempt < ThrowawayReset.MaxDeleteAttempts; attempt++)
        {
            Assert.Equal(ThrowawayDeleteStep.Wait, ThrowawayReset.AfterDelete(hold, ThrowawayListed.Live, attempt, out var waiting));
            Assert.Contains("CharacterHandler.cs:302-306", waiting, StringComparison.Ordinal);
        }

        Assert.Equal(ThrowawayDeleteStep.Fail, ThrowawayReset.AfterDelete(hold, ThrowawayListed.Live, ThrowawayReset.MaxDeleteAttempts, out var bound));
        Assert.Contains("refused the delete 5 times", bound, StringComparison.Ordinal);
        Assert.Contains("24 s of waits", bound, StringComparison.Ordinal);
        Assert.Equal(6, ThrowawayReset.HoldWaitSeconds);
    }

    [Fact]
    public void Any_other_refusal_fails_at_once()
    {
        Assert.Equal(ThrowawayDeleteStep.Fail, ThrowawayReset.AfterDelete(new ActionOutcome(ActionOutcomeKind.ServerError, 0x7), ThrowawayListed.Live, 1, out _));
        Assert.Equal(ThrowawayDeleteStep.Fail, ThrowawayReset.AfterDelete(new ActionOutcome(ActionOutcomeKind.ServerError, ThrowawayReset.CharacterErrorDelete), ThrowawayListed.Pending, 1, out var gone));
        Assert.Contains("not ACE's hold", gone, StringComparison.Ordinal);
        Assert.Equal(ThrowawayDeleteStep.Fail, ThrowawayReset.AfterDelete(new ActionOutcome(ActionOutcomeKind.TimedOut), ThrowawayListed.Live, 1, out _));
    }
}
