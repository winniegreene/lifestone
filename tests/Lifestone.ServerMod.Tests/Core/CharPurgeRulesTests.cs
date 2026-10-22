using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod.Tests.Core;

/// <summary>
/// The rules of <c>@lifestone char purge</c> (server/mod/Lifestone.ServerMod/Core/CharPurgeRules.cs, compiled here by link; owner
/// decision D59): only a character on the caller's own account that was already marked for deletion is purged, and each refusal has its
/// own code, in a fixed order. The ACE calls around them are checked by ModSourceTests and the live loopback test.
/// </summary>
public sealed class CharPurgeRulesTests
{
    private const string Caller = "Lifestone Loopback";
    private const uint CallerGuid = 0x50000001;
    private const uint ThrowawayGuid = 0x50000005;

    private static readonly AccountCharacter Throwaway = new(ThrowawayGuid, "Lifestone Throwaway", IsDeleted: false, DeleteTime: 1_700_000_000, IsOnline: false);

    private static AccountCharacter[] Account(params AccountCharacter[] extra) =>
    [
        new(CallerGuid, Caller, false, 0, true),
        new(0x50000002, "Lifestone Smoke", false, 0, false),
        .. extra,
    ];

    private static CharPurgeDecision Check(string name, params AccountCharacter[] account) =>
        CharPurgeRules.Check(new CharPurgeRequest(name), Caller, CallerGuid, account);

    [Fact]
    public void A_character_on_the_account_that_is_marked_for_deletion_and_offline_may_be_purged()
    {
        var decision = Check("Lifestone Throwaway", Account(Throwaway));
        Assert.True(decision.Allowed);
        Assert.Null(decision.Refusal);
        Assert.Equal(Throwaway, decision.Target);

        // Marked deleted by ACE's delay running out (IsDeleted, with or without a DeleteTime) counts too.
        Assert.True(Check("Lifestone Throwaway", Account(Throwaway with { IsDeleted = true, DeleteTime = 0 })).Allowed);
        Assert.True(Check("Lifestone Throwaway", Account(Throwaway with { IsDeleted = true })).Allowed);

        // The name matches ignoring case, ordinally.
        Assert.True(Check("lifestone THROWAWAY", Account(Throwaway)).Allowed);
    }

    [Theory]
    [InlineData("Lifestone Smoke")]
    [InlineData("lifestone smoke")]
    [InlineData("LIFESTONE LOOPBACK")]
    [InlineData("Lifestone Loopback")]
    [InlineData("BSD Mage")]
    [InlineData("bsd mage")]
    [InlineData("BSD MAGE")]
    [InlineData("Holtburg Hunter")]
    [InlineData("holtburg hunter")]
    [InlineData("HOLTBURG HUNTER")]
    public void The_graduated_test_characters_are_refused_by_name_whatever_the_account_holds(string name)
    {
        // Even a pending-deletion copy of a graduated name is refused: they are the baseline.
        var pendingSmoke = new AccountCharacter(0x50000009, "Lifestone Smoke", false, 1_700_000_000, false);
        foreach (var account in new[] { Account(), Account(pendingSmoke), Array.Empty<AccountCharacter>() })
        {
            var decision = Check(name, account);
            Assert.False(decision.Allowed);
            Assert.Equal(ToolErrors.Graduated, decision.Refusal!.Code);
            Assert.EndsWith(" is a graduated test character and stays as it is", decision.Refusal.Message, StringComparison.Ordinal);
            Assert.Contains(CharPurgeRules.Graduated, g => string.Equals(g, name, StringComparison.OrdinalIgnoreCase) && decision.Refusal.Message.StartsWith(g, StringComparison.Ordinal));
        }

        Assert.Equal(["Lifestone Smoke", "Lifestone Loopback", "BSD Mage", "Holtburg Hunter"], CharPurgeRules.Graduated);
    }

    [Fact]
    public void The_callers_own_character_is_refused_even_when_the_account_says_it_is_pending()
    {
        var self = CharPurgeRules.Check(new CharPurgeRequest("Ann"), "Ann", 0x50000007, [new AccountCharacter(0x50000007, "Ann", false, 0, true)]);
        Assert.Equal(ToolErrors.OwnCharacter, self.Refusal!.Code);
        Assert.Equal("you are playing Ann; log in as another character to purge it", self.Refusal.Message);

        // By name first (no database read needed) and case-insensitively; by id as well, should the names ever differ.
        Assert.Equal(ToolErrors.OwnCharacter, CharPurgeRules.EarlyRefusal(new CharPurgeRequest("ann"), "Ann")!.Code);
        var byId = CharPurgeRules.Check(new CharPurgeRequest("Ann"), "Someone else", 0x50000007, [new AccountCharacter(0x50000007, "Ann", false, 1_700_000_000, false)]);
        Assert.Equal(ToolErrors.OwnCharacter, byId.Refusal!.Code);
    }

    [Fact]
    public void A_name_with_no_character_on_the_account_is_refused_and_reveals_nothing()
    {
        // Another account's characters are never in the list the verb reads (it queries by the caller's account id), so a name that
        // lives only on another account answers exactly as a name that lives nowhere.
        var nowhere = Check("Not Here", Account(Throwaway));
        Assert.Equal(new ToolError(ToolErrors.UnknownCharacter, "no character named \"Not Here\" is on your account"), nowhere.Refusal);
        Assert.Equal(nowhere.Refusal!.Message.Replace("Not Here", "Someone Elses", StringComparison.Ordinal), Check("Someone Elses", Account(Throwaway)).Refusal!.Message);
        Assert.Equal(ToolErrors.UnknownCharacter, Check("Lifestone Throwaway", Account()).Refusal!.Code);
    }

    [Fact]
    public void A_character_that_is_online_is_refused()
    {
        var decision = Check("Lifestone Throwaway", Account(Throwaway with { IsOnline = true }));
        Assert.Equal(ToolErrors.CharacterOnline, decision.Refusal!.Code);
        Assert.Equal("Lifestone Throwaway is online; only a character that is offline can be purged", decision.Refusal.Message);
    }

    [Fact]
    public void A_character_that_is_not_marked_for_deletion_is_refused()
    {
        var decision = Check("Lifestone Throwaway", Account(Throwaway with { DeleteTime = 0, IsDeleted = false }));
        Assert.Equal(ToolErrors.NotPendingDeletion, decision.Refusal!.Code);
        Assert.Equal("Lifestone Throwaway is not marked for deletion; delete it on the character screen first", decision.Refusal.Message);
    }

    [Fact]
    public void The_refusals_come_in_the_documented_order()
    {
        // A request that breaks several rules at once gets the first: graduated, own, unknown, online, not pending.
        var graduatedAndOwn = CharPurgeRules.Check(new CharPurgeRequest("Lifestone Loopback"), "Lifestone Loopback", CallerGuid, Account());
        Assert.Equal(ToolErrors.Graduated, graduatedAndOwn.Refusal!.Code);

        var ownAndUnknown = CharPurgeRules.Check(new CharPurgeRequest("Ann"), "Ann", CallerGuid, Account());
        Assert.Equal(ToolErrors.OwnCharacter, ownAndUnknown.Refusal!.Code);

        // Online and not pending: the online refusal comes before the pending one.
        var onlineNotPending = Check("Lifestone Throwaway", Account(Throwaway with { DeleteTime = 0, IsOnline = true }));
        Assert.Equal(ToolErrors.CharacterOnline, onlineNotPending.Refusal!.Code);

        var codes = new[] { ToolErrors.Graduated, ToolErrors.OwnCharacter, ToolErrors.UnknownCharacter, ToolErrors.CharacterOnline, ToolErrors.NotPendingDeletion };
        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Contains(c, ToolErrors.All));
    }

    [Fact]
    public void A_live_character_of_the_same_name_never_shields_the_pending_one()
    {
        // ACE frees a name as soon as its character is pending deletion, so an account can hold both.
        var live = new AccountCharacter(0x50000004, "Lifestone Throwaway", false, 0, false);
        var decision = Check("Lifestone Throwaway", Account(live, Throwaway));
        Assert.True(decision.Allowed);
        Assert.Equal(ThrowawayGuid, decision.Target!.Value.Guid);

        // With only the live one, it is refused as not pending.
        Assert.Equal(ToolErrors.NotPendingDeletion, Check("Lifestone Throwaway", Account(live)).Refusal!.Code);
    }

    [Fact]
    public void The_visible_pending_row_is_purged_before_an_old_expired_row_of_the_same_name()
    {
        // An expired IsDeleted row is invisible in the character list; the visible pending one is the one the caller just deleted, even with a lower id.
        var expired = new AccountCharacter(0x50000003, "Lifestone Throwaway", IsDeleted: true, DeleteTime: 1_600_000_000, IsOnline: false);
        var decision = Check("Lifestone Throwaway", Account(expired, Throwaway));
        Assert.True(decision.Allowed);
        Assert.Equal(ThrowawayGuid, decision.Target!.Value.Guid);
        Assert.True(Throwaway.VisiblePending);
        Assert.False(expired.VisiblePending);
        Assert.Equal(0, Throwaway.PurgeRank);
        Assert.Equal(1, expired.PurgeRank);
        Assert.Equal(2, new AccountCharacter(1, "x", false, 0, false).PurgeRank);

        // The same whatever the order of the rows.
        Assert.Equal(ThrowawayGuid, Check("Lifestone Throwaway", Account(Throwaway, expired)).Target!.Value.Guid);

        // Several visible pending rows: the latest DeleteTime, then the lowest id.
        var later = Throwaway with { Guid = 0x50000009, DeleteTime = Throwaway.DeleteTime + 100 };
        Assert.Equal(later.Guid, Check("Lifestone Throwaway", Account(Throwaway, later)).Target!.Value.Guid);
        var tie = Throwaway with { Guid = 0x50000003 };
        Assert.Equal(tie.Guid, Check("Lifestone Throwaway", Account(Throwaway, tie)).Target!.Value.Guid);
    }

    [Fact]
    public void Expired_rows_are_purged_one_by_one_latest_first_once_no_visible_pending_row_is_left_and_a_live_row_never_before_them()
    {
        var older = new AccountCharacter(0x50000003, "Lifestone Throwaway", true, 1_600_000_000, false);
        var newer = new AccountCharacter(0x50000004, "Lifestone Throwaway", true, 1_650_000_000, false);
        var live = new AccountCharacter(0x50000001 - 1, "Lifestone Throwaway", false, 0, false);
        var first = Check("Lifestone Throwaway", Account(live, older, newer));
        Assert.True(first.Allowed);
        Assert.Equal(newer.Guid, first.Target!.Value.Guid);

        // After that one is purged the verb is asked again (the throwaway leg and the live test repeat it), and the older one goes.
        var second = Check("Lifestone Throwaway", Account(live, older));
        Assert.Equal(older.Guid, second.Target!.Value.Guid);

        // Only the live character is left: refused as not pending, never purged; and with nothing at all, unknown (the repeat's end).
        Assert.Equal(ToolErrors.NotPendingDeletion, Check("Lifestone Throwaway", Account(live)).Refusal!.Code);
        Assert.Equal(ToolErrors.UnknownCharacter, Check("Lifestone Throwaway", Account()).Refusal!.Code);
    }

    [Fact]
    public void Early_refusals_need_no_account_and_leave_other_names_to_the_full_check()
    {
        Assert.Null(CharPurgeRules.EarlyRefusal(new CharPurgeRequest("Lifestone Throwaway"), Caller));
        Assert.Equal(ToolErrors.Graduated, CharPurgeRules.EarlyRefusal(new CharPurgeRequest("Lifestone Smoke"), Caller)!.Code);
        Assert.Equal(ToolErrors.Graduated, CharPurgeRules.EarlyRefusal(new CharPurgeRequest("BSD Mage"), Caller)!.Code);
        Assert.Equal(ToolErrors.Graduated, CharPurgeRules.EarlyRefusal(new CharPurgeRequest("Holtburg Hunter"), Caller)!.Code);
        Assert.Throws<ArgumentNullException>(() => CharPurgeRules.EarlyRefusal(null!, Caller));
        Assert.Throws<ArgumentNullException>(() => CharPurgeRules.Check(null!, Caller, CallerGuid, []));
        Assert.Throws<ArgumentNullException>(() => CharPurgeRules.Check(new CharPurgeRequest("a"), Caller, CallerGuid, null!));
    }

    [Fact]
    public void The_success_path_reports_the_counts_ACE_gave()
    {
        var (data, error) = CharPurgeRules.Outcome("Lifestone Throwaway", 1, 1, 14, characterRemains: false);
        Assert.Null(error);
        Assert.Equal(new CharPurgeData("Lifestone Throwaway", 1, 1, 14), data);
        Assert.True(data!.Removed);
        Assert.Equal("purged Lifestone Throwaway: 1 character record, 1 player biota, 14 possessions", data.Describe());

        // The reply is the contract's: it round-trips through the wire text with the counts.
        var text = ToolsWire.Reply(ToolReply.Success(new CharPurgeRequest("Lifestone Throwaway", "p1"), data), data.Describe());
        Assert.True(ToolsWire.TryParseReply(text, out var reply, out var chat));
        Assert.Equal(("p1", "char", true), (reply.Id, reply.Verb, reply.Ok));
        Assert.Equal(data, reply.DataAs<CharPurgeData>());
        Assert.Equal("Lifestone: " + data.Describe(), chat);
    }

    [Fact]
    public void A_purge_that_left_the_row_or_removed_nothing_is_a_failure()
    {
        var remains = CharPurgeRules.Outcome("Lifestone Throwaway", 1, 1, 14, characterRemains: true);
        Assert.Null(remains.Data);
        Assert.Equal(new ToolError(ToolErrors.Failed, "ACE's purge did not remove Lifestone Throwaway; see ACE_Log.txt"), remains.Error);

        var nothing = CharPurgeRules.Outcome("Lifestone Throwaway", 0, 0, 0, characterRemains: false);
        Assert.Null(nothing.Data);
        Assert.Equal(ToolErrors.Failed, nothing.Error!.Code);
        Assert.Contains("already gone", nothing.Error.Message, StringComparison.Ordinal);

        // A player biota alone (the character record gone earlier) is still something removed.
        Assert.NotNull(CharPurgeRules.Outcome("X", 0, 1, 0, characterRemains: false).Data);
    }

    [Fact]
    public void A_failure_after_ACE_dropped_the_character_from_its_caches_says_ACE_needs_a_restart()
    {
        var remains = CharPurgeRules.WithRestartNote(CharPurgeRules.Outcome("Lifestone Throwaway", 1, 1, 14, characterRemains: true).Error!);
        Assert.Equal(ToolErrors.Failed, remains.Code);
        Assert.StartsWith("ACE's purge did not remove Lifestone Throwaway; see ACE_Log.txt", remains.Message, StringComparison.Ordinal);
        Assert.Contains("needs a restart for that character to be known again", remains.Message, StringComparison.Ordinal);

        var refusal = CharPurgeRules.WithRestartNote(Check("Lifestone Throwaway", Account(Throwaway with { IsOnline = true })).Refusal!);
        Assert.Equal(ToolErrors.CharacterOnline, refusal.Code);
        Assert.EndsWith(CharPurgeRules.RestartNote, refusal.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => CharPurgeRules.WithRestartNote(null!));
    }

    [Fact]
    public void The_offline_stand_in_applies_the_same_rules_with_the_same_words()
    {
        // The stand-in (Lifestone.Client) cannot compile the mod's Core, so one table of requests runs through both.
        var slots = new[]
        {
            new CharacterSlot(CallerGuid, Caller, 0),
            new CharacterSlot(0x50000002, "Lifestone Smoke", 0),
            new CharacterSlot(0x50000003, "Pending One", 1),
            new CharacterSlot(0x50000004, "Live One", 0),
            new CharacterSlot(0x50000005, "Twin", 0),
            new CharacterSlot(0x50000006, "Twin", 1),
            new CharacterSlot(0x50000007, "Pair", 5), // two pending of one name: the latest DeleteTime (the longer delay left) goes first
            new CharacterSlot(0x50000008, "Pair", 9),
            new CharacterSlot(0x50000009, "Same", 5), // equal times: the lowest id
            new CharacterSlot(0x5000000A, "Same", 5),
        };
        var account = slots.Select(s => new AccountCharacter(s.Guid, s.Name, false, s.PendingDeletion ? s.DeleteTimeout : 0UL, s.Guid == CallerGuid)).ToArray();
        var names = new[] { "Lifestone Smoke", "lifestone loopback", "BSD Mage", "bsd mage", "Holtburg Hunter", "holtburg hunter", "Pending One", "pending one", "Live One", "Twin", "Nobody", "Lifestone Throwaway", "Pair", "Same" };
        foreach (var name in names)
        {
            var request = new CharPurgeRequest(name);
            var mod = CharPurgeRules.Check(request, Caller, CallerGuid, account);
            var standIn = StandInCharPurge.Decide(request, Caller, CallerGuid, slots);
            Assert.Equal(mod.Refusal, standIn.Refusal);
            if (mod.Allowed)
            {
                Assert.Equal(mod.Target!.Value.Guid, standIn.Target.Guid);
            }
        }

        // The protected names are the same list in the same order, and BSD Mage and Holtburg Hunter are among them.
        Assert.Equal(CharPurgeRules.Graduated, StandInCharPurge.Graduated);
        Assert.Contains("BSD Mage", StandInCharPurge.Graduated);
        Assert.Contains("Holtburg Hunter", StandInCharPurge.Graduated);

        Assert.Equal(0x50000008u, StandInCharPurge.Decide(new CharPurgeRequest("Pair"), Caller, CallerGuid, slots).Target.Guid);
        Assert.Equal(0x50000009u, StandInCharPurge.Decide(new CharPurgeRequest("Same"), Caller, CallerGuid, slots).Target.Guid);

        // The table reaches every reason and the success.
        Assert.Equal(
            [ToolErrors.Graduated, ToolErrors.Graduated, ToolErrors.Graduated, ToolErrors.Graduated, ToolErrors.Graduated, ToolErrors.Graduated, null, null, ToolErrors.NotPendingDeletion, null, ToolErrors.UnknownCharacter, ToolErrors.UnknownCharacter, null, null],
            names.Select(n => StandInCharPurge.Decide(new CharPurgeRequest(n), Caller, CallerGuid, slots).Refusal?.Code));
        Assert.Equal(ToolErrors.OwnCharacter, StandInCharPurge.Decide(new CharPurgeRequest("Ann"), "Ann", CallerGuid, slots).Refusal!.Code);
        Assert.Equal(
            CharPurgeRules.Check(new CharPurgeRequest("Ann"), "Ann", CallerGuid, account).Refusal,
            StandInCharPurge.Decide(new CharPurgeRequest("Ann"), "Ann", CallerGuid, slots).Refusal);
    }
}
