using Lifestone.Data.Dat;

namespace Lifestone.Client.PortalSpace;

/// <summary>
/// The enums of retail's <c>UIASSET</c> enum group (<see cref="EnumGroup.UiAsset"/>, map 0x25000010), as retail's code
/// passes them to <c>DBObj::GetDIDByEnum</c> (11.4186 0x004153A0, 11.6096 0x00415640). Resolve them through
/// <see cref="EnumIdMaps"/>; never hard-code the DIDs they name (m2-log-in.md §4.2).
/// </summary>
/// <remarks>
/// One constant per entry of the end-of-retail map (20), in enum order; a test pins each to the map's own name
/// (<c>portalspace_background</c>, ...) and fails when the map gains an entry. The DIDs in the comments are what the
/// end-of-retail dats give, pinned by the tests, for reference only. Consumers: portal space (the first three), CharGen's
/// 3D view (the animations and the Olthoi heritage variants) and the retail UI (the icons and swatches).
/// </remarks>
public static class UiAssetIds
{
    /// <summary><c>portalspace_background</c>: the tunnel Setup (0x02000306), <c>gmSmartBoxUI::PostInit</c>'s <c>teleportObj</c>.</summary>
    public const uint PortalSpaceBackground = 0x10000001;

    /// <summary><c>portalspace_animation</c>: the tunnel's spin (Animation 0x030005AC), <c>gmSmartBoxUI::UseTime</c>.</summary>
    public const uint PortalSpaceAnimation = 0x10000002;

    /// <summary><c>UISoundTable</c>: the UI sound table (SoundTable 0x2000004B), <c>ClientUISystem::GetUISoundTable</c>.</summary>
    public const uint UiSoundTable = 0x10000003;

    /// <summary><c>PlayerIcon</c> (RenderSurface 0x0600127E).</summary>
    public const uint PlayerIcon = 0x10000004;

    /// <summary><c>PaperDollAnimation</c> (Animation 0x030003C0): CharGen's preview rest pose, played at 0 fps.</summary>
    public const uint PaperDollAnimation = 0x10000005;

    /// <summary><c>CharGenAnimation</c> (Animation 0x03000001).</summary>
    public const uint CharGenAnimation = 0x10000006;

    /// <summary><c>CharGenAnimationWave</c>, the first idle emote (Animation 0x030000AC).</summary>
    public const uint CharGenAnimationWave = 0x10000007;

    /// <summary><c>CharGenAnimationYawn</c> (Animation 0x03000540).</summary>
    public const uint CharGenAnimationYawn = 0x10000008;

    /// <summary><c>CharGenAnimationShrug</c> (Animation 0x03000508).</summary>
    public const uint CharGenAnimationShrug = 0x10000009;

    /// <summary><c>CharGenAnimationAkimbo</c> (Animation 0x03000523).</summary>
    public const uint CharGenAnimationAkimbo = 0x1000000A;

    /// <summary><c>CharGenAnimationTapFoot</c>, the last idle emote (Animation 0x0300052E).</summary>
    public const uint CharGenAnimationTapFoot = 0x1000000B;

    /// <summary><c>PaperDollHitTest</c> (RenderSurface 0x06004D8B).</summary>
    public const uint PaperDollHitTest = 0x1000000C;

    /// <summary><c>ColorBullet</c> (RenderSurface 0x06004D46).</summary>
    public const uint ColorBullet = 0x1000000D;

    /// <summary><c>ColorRing</c> (RenderSurface 0x06004D45).</summary>
    public const uint ColorRing = 0x1000000E;

    /// <summary><c>ColorEmpty</c> (RenderSurface 0x06005DA5).</summary>
    public const uint ColorEmpty = 0x1000000F;

    /// <summary><c>GradientPlug</c> (RenderSurface 0x06005DA6).</summary>
    public const uint GradientPlug = 0x10000010;

    /// <summary><c>CharGenOlthoiAnimation</c> (Animation 0x03000045), the Olthoi heritage's CharGen animation.</summary>
    public const uint CharGenOlthoiAnimation = 0x10000011;

    /// <summary><c>CharGenOlthoiTwitchAnimation</c> (Animation 0x03000047).</summary>
    public const uint CharGenOlthoiTwitchAnimation = 0x10000012;

    /// <summary><c>CharGenOlthoiAcidAnimation</c> (Animation 0x03000992).</summary>
    public const uint CharGenOlthoiAcidAnimation = 0x10000013;

    /// <summary><c>CharGenOlthoiAcidTwitchAnimation</c> (Animation 0x0300098F).</summary>
    public const uint CharGenOlthoiAcidTwitchAnimation = 0x10000014;

    /// <summary>The first of the five idle emotes (<see cref="CharGenAnimationWave"/>).</summary>
    public const uint IdleEmoteFirst = CharGenAnimationWave;

    /// <summary>The last of the five idle emotes (<see cref="CharGenAnimationTapFoot"/>).</summary>
    public const uint IdleEmoteLast = CharGenAnimationTapFoot;
}

/// <summary>
/// The three dat records portal space needs, resolved the way retail finds them: the tunnel Setup, its spin Animation
/// and the UI sound table (whose <c>UI_EnterPortal</c> 0x6A and <c>UI_ExitPortal</c> 0x6B are the whooshes).
/// </summary>
/// <param name="TunnelSetupId">The tunnel object's Setup (<see cref="UiAssetIds.PortalSpaceBackground"/>).</param>
/// <param name="TunnelAnimationId">The spin (<see cref="UiAssetIds.PortalSpaceAnimation"/>).</param>
/// <param name="UiSoundTableId">The UI sound table (<see cref="UiAssetIds.UiSoundTable"/>).</param>
public sealed record PortalSpaceAssets(uint TunnelSetupId, uint TunnelAnimationId, uint UiSoundTableId)
{
    /// <summary>
    /// Resolves the three through the UIASSET group of <paramref name="maps"/>: retail <c>DBObj::GetDIDByEnum(enum, 7)</c>
    /// in <c>gmSmartBoxUI::PostInit</c> (11.4186 0x004D6B60, 11.6096 0x004D77C0) and <c>UseTime</c>, and
    /// <c>DBObj::GetByEnum(0x10000003, 7)</c> in <c>ClientUISystem::GetUISoundTable</c> (11.4186 0x00563FB0, 11.6096
    /// 0x00564D50).
    /// </summary>
    /// <exception cref="InvalidDataException">An enum does not resolve; the message names the enum and where the chain broke.</exception>
    public static PortalSpaceAssets Resolve(EnumIdMaps maps)
    {
        ArgumentNullException.ThrowIfNull(maps);
        return new PortalSpaceAssets(
            maps.Resolve(EnumGroup.UiAsset, UiAssetIds.PortalSpaceBackground),
            maps.Resolve(EnumGroup.UiAsset, UiAssetIds.PortalSpaceAnimation),
            maps.Resolve(EnumGroup.UiAsset, UiAssetIds.UiSoundTable));
    }
}
