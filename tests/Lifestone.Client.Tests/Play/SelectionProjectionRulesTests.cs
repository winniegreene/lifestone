using Lifestone.Client.Play;
using Lifestone.Client.Tests.Repository;

namespace Lifestone.Client.Tests.Play;

/// <summary>
/// M4 3.1 p, <c>m4-hunt-frame-camera</c>: why the live hunt frames printed <c>player_box=n/a</c> and what feeds the projection rule now. The cause was an input of the projection, the server objects layer's record of what it drew,
/// which can never hold the player (the layer draws every avatar but the player). The rule is pinned engine-free (<see cref="SelectionProjectionRules"/>); the two Godot facts it rests on are pinned in source, as the other
/// Godot-side rows are (Lifestone.Game has no test project).
/// </summary>
public sealed class SelectionProjectionRulesTests
{
    private const string LayerFile = "src/Lifestone.Game/scripts/World/ServerObjects/ServerObjectsLayer.cs";
    private const string PickingFile = "src/Lifestone.Game/scripts/Player/WorldPicking.cs";
    private const string RunnerFile = "src/Lifestone.Game/scripts/Session/HuntLegRunner.cs";

    [Fact]
    public void The_player_is_projectable_without_a_layer_record_because_the_layer_never_draws_it_and_every_other_object_needs_one()
    {
        // The live case: the player's body is drawn, nobody holds it, and the layer has no record of it (the layer skips the player): 1.3's rule refused this, so the player's box was n/a on every frame.
        Assert.Null(SelectionProjectionRules.Refusal(isPlayer: true, bodyDrawn: true, held: false, layerDrawn: false));

        // The target (a creature the layer draws) is projectable only while the layer drew it in the last frame.
        Assert.Null(SelectionProjectionRules.Refusal(isPlayer: false, bodyDrawn: true, held: false, layerDrawn: true));
        Assert.Equal(SelectionProjectionRules.LayerText, SelectionProjectionRules.Refusal(isPlayer: false, bodyDrawn: true, held: false, layerDrawn: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_body_that_is_not_drawn_or_is_held_is_refused_for_the_player_and_for_any_object_and_the_phrase_names_the_input(bool isPlayer)
    {
        Assert.Equal(SelectionProjectionRules.NotDrawnText, SelectionProjectionRules.Refusal(isPlayer, bodyDrawn: false, held: false, layerDrawn: true));
        Assert.Equal(SelectionProjectionRules.HeldText, SelectionProjectionRules.Refusal(isPlayer, bodyDrawn: true, held: true, layerDrawn: true));
        Assert.Equal(SelectionProjectionRules.NotDrawnText, SelectionProjectionRules.Refusal(isPlayer, bodyDrawn: false, held: true, layerDrawn: false)); // the first input that fails is named
        Assert.All([SelectionProjectionRules.NotDrawnText, SelectionProjectionRules.HeldText, SelectionProjectionRules.LayerText], text => Assert.False(string.IsNullOrWhiteSpace(text)));
    }

    [Fact]
    public void The_layer_skips_the_player_and_the_picking_asks_the_rule_with_the_avatars_IsPlayer_which_is_why_the_old_inline_layer_test_could_never_pass_for_the_player()
    {
        var layer = CheckoutFiles.ReadCSharpCode(LayerFile);
        var skip = layer.IndexOf("if (!draw.IsPlayer)", StringComparison.Ordinal);
        var draw = layer.IndexOf("DrawObject(avatars, frame, draw);", StringComparison.Ordinal);
        Assert.True(skip >= 0 && draw > skip, "the layer draws only the avatars that are not the player, so its IsDrawn record never holds the player");

        var picking = CheckoutFiles.ReadCSharpCode(PickingFile);
        Assert.Contains("SelectionProjectionRules.Refusal(avatar.IsPlayer, avatar.Body.IsDrawn, avatar.Body.Parent is not null, layer is null || layer.IsDrawn(avatar.Guid))", picking, StringComparison.Ordinal);
        Assert.DoesNotContain("avatar.Body is { IsDrawn: true, Parent: null } && (layer is null || layer.IsDrawn(avatar.Guid))", picking, StringComparison.Ordinal); // the old inline test
        Assert.Contains("public string? WhyNotProjected(uint guid, bool allowPlayer)", picking, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hunt_names_the_refused_input_once_at_the_first_frame_whose_player_box_is_missing_and_still_judges_through_the_unchanged_rule()
    {
        var runner = CheckoutFiles.ReadCSharpCodeAndStrings(RunnerFile);
        var project = runner.IndexOf("playerKnown = _mode.Picking.TryProjectIncludingPlayer(session.State.Self.Guid, out playerBox, out _);", StringComparison.Ordinal);
        var named = runner.IndexOf("_mode.Picking.WhyNotProjected(session.State.Self.Guid, allowPlayer: true)", StringComparison.Ordinal);
        var rule = runner.IndexOf("HuntUiShotPlan.TargetVisible(in targetBox, targetOnScreen, in playerBox, playerKnown, combat.ScreenBox.Y0, out overlap)", StringComparison.Ordinal);
        Assert.True(project >= 0 && named > project && rule > named, "the player's box is projected, a missing box is named, then the rule judges");
        Assert.Contains("if (!playerKnown && !_playerBoxRefusalLogged)", runner, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(runner, "_playerBoxRefusalLogged = true;"));
    }
}
