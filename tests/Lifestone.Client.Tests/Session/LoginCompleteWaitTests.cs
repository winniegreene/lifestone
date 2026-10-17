using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Actions;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Lifestone.Net.Dispatch;
using Lifestone.Tests.Shared;
using Lifestone.Tests.Shared.Net;
using Lifestone.Client.Session.Actions.Trackers;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 1.2 s5: LoginComplete waits as retail's <c>CPlayerSystem::SendLoginCompleteNotification</c> (11.6096 0x00563C30) does: it goes out when the player's weenie exists, the description has arrived and every object
/// the description names (pack, side packs, equipped) is in the object table; otherwise <c>AttemptSendLoginCompleteNotification</c> (0x00563CC0) sends it when the last of them arrives. A safety cap on the session
/// clock sends it anyway, with a WARNING naming how many objects were missing.
/// </summary>
public sealed class LoginCompleteWaitTests
{
    private const uint PackA = 0x80000A01;
    private const uint PackB = 0x80000A02;
    private const uint Worn = 0x80000A03;

    private static PlayerDescriptionMsg Description() => new()
    {
        Inventory = [new ContentDesc(PackA, 0), new ContentDesc(PackB, 0)],
        Equipped = [new EquippedDesc(Worn, 0x100000, 1)],
    };

    private static void Create(ActionHarness h, uint guid) =>
        h.R.Reducer.OnCreateObject(
            h.R.Context,
            new CreateObjectMsg(
                guid,
                ModelData.Empty,
                new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.Gravity, Timestamps = default },
                new WeenieDesc { Header = WeenieHeaderFlags.None, WeenieClassId = 3, IconId = 0x06000001, ItemType = 1, ObjectDescriptionFlags = 0, Name = "Item " + guid.ToString("X", CultureInfo.InvariantCulture), ContainerId = ActionHarness.Amy }),
            isUpdate: false);

    private static int Sent(ActionHarness h) => h.Net.Of(FakeSendKind.LoginComplete).Count;

    [Fact]
    public void Contents_that_arrive_after_the_cue_send_LoginComplete_only_after_the_last_one()
    {
        var h = new ActionHarness();
        h.InWorld();
        h.Describe(Description());
        var call = h.Api.LoginComplete();

        h.Tick(0.1);
        Assert.Equal(0, Sent(h));
        Assert.False(call.IsCompleted);
        Create(h, PackA);
        h.Tick(0.1);
        Create(h, Worn);
        h.Tick(0.1);
        Assert.Equal(0, Sent(h)); // PackB is still missing: two of three are not enough

        Create(h, PackB);
        h.Tick(0.1);
        Assert.Equal(1, Sent(h));
        Assert.Equal(SelfTravel.Materialising, h.State.Self.Travel);
        Assert.DoesNotContain(h.Log.Entries, e => e.Severity >= LogSeverity.Warning);
        Assert.Contains(h.Log.Entries, e => e.Severity == LogSeverity.Info && e.Message.Contains("LoginComplete waits for the player's contents", StringComparison.Ordinal));

        h.Unhide();
        h.Tick();
        Assert.Equal(ActionOutcomeKind.Confirmed, call.Outcome!.Value.Kind);
    }

    [Fact]
    public void Contents_already_in_the_table_send_LoginComplete_at_the_first_update_with_no_wait_logged()
    {
        var h = new ActionHarness();
        h.InWorld();
        h.Describe(Description());
        Create(h, PackA);
        Create(h, PackB);
        Create(h, Worn);

        h.Api.LoginComplete();
        h.Tick();

        Assert.Equal(1, Sent(h));
        Assert.DoesNotContain(h.Log.Entries, e => e.Message.Contains("LoginComplete waits", StringComparison.Ordinal));
    }

    [Fact]
    public void A_description_that_arrives_after_the_cue_is_waited_for()
    {
        var h = new ActionHarness();
        h.InWorld();
        h.Api.LoginComplete();

        h.Tick(0.5);
        Assert.Equal(0, Sent(h));

        h.Describe(); // an empty one: nothing contained
        h.Tick(0.1);
        Assert.Equal(1, Sent(h));
    }

    [Fact]
    public void A_lost_object_is_given_the_cap_then_LoginComplete_is_sent_with_a_warning_naming_the_missing_count()
    {
        var h = new ActionHarness();
        h.InWorld();
        h.Describe(Description());
        Create(h, PackA);
        h.Api.LoginComplete();

        h.Tick(2.9);
        Assert.Equal(0, Sent(h));
        Assert.DoesNotContain(h.Log.Entries, e => e.Severity == LogSeverity.Warning);

        h.Tick(0.2); // 3.1 s on the session clock from the call: the cap is 3 s
        Assert.Equal(1, Sent(h));
        var warning = Assert.Single(h.Log.Entries, e => e.Severity == LogSeverity.Warning);
        Assert.Contains("safety cap", warning.Message, StringComparison.Ordinal);
        Assert.Contains(warning.Fields, f => f.Key == "missing" && Equals(f.Value, 2)); // PackB and the worn item
        Assert.Contains(warning.Fields, f => f.Key == "description_arrived" && Equals(f.Value, true));
    }

    [Fact]
    public void A_description_that_never_comes_is_given_the_cap_too()
    {
        var h = new ActionHarness();
        h.InWorld();
        h.Api.LoginComplete();

        h.Tick(3.1);

        Assert.Equal(1, Sent(h));
        var warning = Assert.Single(h.Log.Entries, e => e.Severity == LogSeverity.Warning);
        Assert.Contains(warning.Fields, f => f.Key == "description_arrived" && Equals(f.Value, false));
    }

    [Fact]
    public void A_wait_ends_when_the_player_leaves_the_world_the_refusal_still_applies()
    {
        var h = new ActionHarness();
        h.InWorld();
        h.Describe(Description());
        var call = h.Api.LoginComplete();
        h.Tick(0.1);
        Assert.False(call.IsCompleted);

        h.Phase(Lifestone.Net.Session.SessionPhase.InWorld, Lifestone.Net.Session.SessionPhase.LoggingOff);
        h.Tick(0.1);

        Assert.Equal(0, Sent(h));
        Assert.True(call.IsCompleted);
        Assert.Equal(ActionOutcomeKind.Refused, call.Outcome!.Value.Kind);
    }

    [Fact]
    public void R10_a_held_item_consumed_in_ACEs_order_leaves_the_inventory_model_and_the_next_LoginComplete_does_not_wait_the_cap()
    {
        // Player_Inventory.cs ~121-141, TryConsumeFromInventoryWithNetworking: 0024 InventoryRemoveObject, then the item is destroyed (F747 DeleteObject) for a whole stack, or 0197 SetStackSize for a partial one.
        var h = new ActionHarness();
        h.InWorld();
        h.Describe(Description());
        Create(h, PackA);
        Create(h, PackB);
        Create(h, Worn);

        // A whole stack: 0024, then F747.
        var removed = InboundDispatcher.Dispatch(h.R.Context, AceM3Writers.InventoryRemoveObject(PackA), h.R.Reducer);
        Assert.Equal(DispatchOutcome.Decoded, removed.Outcome);
        Assert.False(h.State.Self.Inventory.Contains(PackA));
        h.R.Reducer.OnDeleteObject(h.R.Context, new DeleteObjectMsg(PackA, 0));
        Assert.False(h.State.Objects.Contains(PackA));
        Assert.Equal(0, LoginCompleteTracker.MissingContents(h.State, out _));

        // A partial stack: 0024 is not sent; 0197 shrinks it, and the item stays held with its world object.
        var partial = InboundDispatcher.Dispatch(h.R.Context, AceM3Writers.SetStackSize(1, PackB, 4, 80), h.R.Reducer);
        Assert.Equal(DispatchOutcome.Decoded, partial.Outcome);
        Assert.True(h.State.Self.Inventory.Contains(PackB));

        h.Api.LoginComplete();
        h.Tick(0.1);
        Assert.Equal(1, Sent(h));
        Assert.DoesNotContain(h.Log.Entries, e => e.Severity >= LogSeverity.Warning);
        Assert.DoesNotContain(h.Log.Entries, e => e.Message.Contains("LoginComplete waits", StringComparison.Ordinal));
    }

    [Fact]
    public void A_delete_without_the_0024_releases_the_held_item_as_ACEs_death_drop_of_a_wielded_item_sends_it_and_LoginComplete_does_not_wait()
    {
        // Closes TODO(m3-inventory-delete-alone) (M3 3.2 live): ACE's drop of a wielded item to the corpse is 02DA, 02CE, F74A, the sound and F747 with no 0024 (Player_Inventory.cs 397-441), so the F747 itself releases a held item.
        var h = new ActionHarness();
        h.InWorld();
        h.Describe(Description());
        Create(h, PackA);
        Create(h, PackB);
        Create(h, Worn);
        h.R.Reducer.OnDeleteObject(h.R.Context, new DeleteObjectMsg(PackA, 0));

        Assert.False(h.State.Objects.Contains(PackA));
        Assert.False(h.State.Self.Inventory.Contains(PackA));
        Assert.Equal(0, LoginCompleteTracker.MissingContents(h.State, out _));
        h.Api.LoginComplete();
        h.Tick(0.5);
        Assert.Equal(1, Sent(h));
    }
}
