namespace Lifestone.Client.Play;

/// <summary>
/// Which avatars the selection projection (<see cref="ISelectionProjector"/>, the game's <c>WorldPicking</c>) can give a screen box (M4 3.1 p, <c>m4-hunt-frame-camera</c>). An avatar is projectable when its body is drawn (in a cell, neither
/// NoDraw nor Hidden), is not held by another object, and, for every object but the player, was drawn by the server objects layer in the last frame (the layer's own record of what it drew). <b>The player is exempt from the layer test:</b> the layer
/// skips the player's own avatar (the player is drawn by the player view, never by the layer: <c>ServerObjectsLayer._Process</c> draws only the avatars that are not <c>IsPlayer</c>), so a layer record of the player is never there. 1.3's
/// projection rule (<see cref="Session.Scenarios.HuntUiShotPlan.TargetVisible"/>) asked for the player's box through the layer test and so never got it (<c>player_box=n/a</c> on every live hunt frame); the rule's overlap and side turn never ran live.
/// </summary>
public static class SelectionProjectionRules
{
    /// <summary>What <see cref="Refusal"/> says when the body is not drawn.</summary>
    public const string NotDrawnText = "the body is not drawn (hidden, no-draw or not in a cell)";

    /// <summary>What <see cref="Refusal"/> says when the object is held.</summary>
    public const string HeldText = "the object is held (it is drawn with its holder)";

    /// <summary>What <see cref="Refusal"/> says when the layer did not draw a non-player object.</summary>
    public const string LayerText = "the server objects layer did not draw it in the last frame";

    /// <summary>The first reason an avatar's box cannot be projected, as a phrase for the log, or null when it can.</summary>
    /// <param name="isPlayer">The avatar is the player's own.</param>
    /// <param name="bodyDrawn">The body is drawn (<c>AvatarBody.IsDrawn</c>).</param>
    /// <param name="held">The body has a parent (it is held or worn).</param>
    /// <param name="layerDrawn">The server objects layer drew the object in the last frame (true when there is no layer); not asked for the player.</param>
    public static string? Refusal(bool isPlayer, bool bodyDrawn, bool held, bool layerDrawn)
    {
        if (!bodyDrawn)
        {
            return NotDrawnText;
        }

        if (held)
        {
            return HeldText;
        }

        return !isPlayer && !layerDrawn ? LayerText : null;
    }
}
