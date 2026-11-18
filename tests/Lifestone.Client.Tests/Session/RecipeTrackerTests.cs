using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Messages;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M6 item 8: <c>UseRecipe</c> over the order ACE sends (@95ef426d <c>Managers/RecipeManager.cs:43-149</c>): the handle says how the server ended the use, never what the recipe made. The cases a kit's tracker misread: a source that is kept, and a UseDone 0 with nothing moved.
/// </summary>
public sealed class RecipeTrackerTests
{
    private const uint Player = WorldActionRig.Player;
    private const uint Source = 0x80000C01;
    private const uint Target = 0x80000C02;

    private static WorldActionRig Rig()
    {
        var r = new WorldActionRig();
        r.Describe([new ContentDesc(Source, 0), new ContentDesc(Target, 0)], []);
        r.Item(Source, Player);
        r.Item(Target, Player);
        return r;
    }

    [Fact]
    public void The_use_sends_0x0035_with_the_source_first_and_the_target_second()
    {
        var r = Rig();
        var handle = r.Api.UseRecipe(Source, Target);
        r.Tick();

        var sent = Assert.Single(r.H.Net.Of(FakeSendKind.UseWithTarget));
        Assert.Equal(Source, sent.Guid);
        Assert.Equal(Target.ToString("X8", CultureInfo.InvariantCulture), sent.Target);
        Assert.Equal(ActionKind.UseRecipe, handle.Kind);
        Assert.Equal(ActionStatus.Sent, handle.Status);
    }

    [Fact]
    public void A_UseDone_0_with_the_source_kept_is_not_a_false_failure()
    {
        var r = Rig();
        var handle = r.Api.UseRecipe(Source, Target);
        r.Tick();
        r.UseDone();
        r.Tick();

        Assert.Equal(ActionOutcomeKind.Confirmed, handle.Outcome!.Value.Kind);
    }

    [Fact]
    public void A_UseDone_with_a_code_is_a_server_error_with_that_code()
    {
        var r = Rig();
        var handle = r.Api.UseRecipe(Source, Target);
        r.Tick();
        r.UseDone(0x043A);
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, handle.Outcome!.Value.Kind);
        Assert.Equal(0x043Au, handle.Outcome!.Value.Code);
    }

    [Fact]
    public void The_untrained_error_before_a_UseDone_0_is_a_server_error_with_its_code()
    {
        var r = Rig();
        var handle = r.Api.UseRecipe(Source, Target);
        r.Tick();
        r.WeenieError(0x043B);
        r.UseDone();
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, handle.Outcome!.Value.Kind);
        Assert.Equal(0x043Bu, handle.Outcome!.Value.Code);
    }

    [Fact]
    public void The_no_recipe_line_before_a_UseDone_0_is_a_server_error()
    {
        var r = Rig();
        var handle = r.Api.UseRecipe(Source, Target);
        r.Tick();
        r.H.ServerMessage("The Item cannot be used on the Item.", 0x18);
        r.UseDone();
        r.Tick();

        Assert.Equal(ActionOutcomeKind.ServerError, handle.Outcome!.Value.Kind);
        Assert.Equal(0u, handle.Outcome!.Value.Code);
        Assert.Equal(ActionReason.NoEffect, handle.Outcome!.Value.Reason);
    }

    [Fact]
    public void Refused_at_once_for_no_guid_for_one_item_on_itself_and_for_an_item_not_held()
    {
        var r = Rig();

        Assert.Equal(ActionOutcomeKind.Refused, r.Api.UseRecipe(0, Target).Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, r.Api.UseRecipe(Source, 0).Outcome!.Value.Kind);
        Assert.Equal(ActionOutcomeKind.Refused, r.Api.UseRecipe(Source, Source).Outcome!.Value.Kind);

        var notHeld = r.Api.UseRecipe(Source, 0x80000CFF);
        r.Tick();
        Assert.Equal(ActionOutcomeKind.Refused, notHeld.Outcome!.Value.Kind);
        Assert.Equal(ActionReason.NotHeld, notHeld.Outcome!.Value.Reason);
        Assert.Empty(r.H.Net.Of(FakeSendKind.UseWithTarget));
    }
}
