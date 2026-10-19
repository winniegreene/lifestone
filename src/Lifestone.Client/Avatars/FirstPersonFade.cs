namespace Lifestone.Client.Avatars;

/// <summary>
/// How the camera's fade of the player combines with a part's own translucency. Retail: <c>CameraSet::UpdateCamera</c> (11.6096 0x00458C00; 11.4186 0x00458AE0)
/// calls <c>CPhysicsObj::SetTranslucencyHierarchical</c> on the player, which reaches every part and every held child; <c>CPhysicsObj::SetTranslucencyInternal</c>
/// never takes a value below the object's original translucency, so the camera's value is a floor under a part's own (a materialise fade stays at least as translucent).
/// </summary>
public static class FirstPersonFade
{
    /// <summary>The translucency drawn: the larger of the part's own and the camera's.</summary>
    public static float Apply(float own, float camera) => camera > own ? camera : own;

    /// <summary>The camera's value limited to 0..1 (NaN counts as 0).</summary>
    public static float Clamp(float camera) => camera > 0f ? (camera < 1f ? camera : 1f) : 0f;
}
