using System.Numerics;
using Lifestone.Client.PortalSpace;

namespace Lifestone.Client.Tests.PortalSpace;

/// <summary>
/// Step PS3: <see cref="PortalSpaceScene"/> holds the float constants retail stores as bit patterns in
/// <c>gmSmartBoxUI::PostInit</c>, <c>CreatureMode::CreatureMode</c> and <c>CreatureMode::AddLight</c>, decoded exactly.
/// </summary>
public sealed class PortalSpaceSceneTests
{
    [Fact]
    public void The_camera_sits_where_PostInit_places_it()
    {
        Assert.Equal(new Vector3(Bits(0x3E75C28F), Bits(0xC02CCCCD), Bits(0x3F6147AE)), PortalSpaceScene.CameraPosition);
        Assert.Equal(new Vector3(0.24f, -2.7f, 0.88f), PortalSpaceScene.CameraPosition);
        Assert.Equal(Vector3.UnitY, PortalSpaceScene.ViewAxis);
        Assert.Equal(Vector3.Zero, PortalSpaceScene.TunnelPosition);
    }

    [Fact]
    public void The_light_is_PostInit_s_distant_light()
    {
        Assert.Equal(new Vector3(Bits(0x3E99999A), Bits(0xBFF33333), Bits(0x3F266666)), PortalSpaceScene.LightDirection);
        Assert.Equal(1f, PortalSpaceScene.LightDirectionUnit.Length(), 1e-6f);
        Assert.Equal(Vector3.Normalize(new Vector3(0.3f, -1.9f, 0.65f)), PortalSpaceScene.LightDirectionUnit);
        Assert.Equal(2f, PortalSpaceScene.LightIntensity);
        Assert.Equal(0xFFFFFFFFu, PortalSpaceScene.LightColorArgb);
        Assert.Equal(0x7F7FFFFFu, BitsOf(PortalSpaceScene.LightFalloff));
        Assert.Equal(0x43B40000u, BitsOf(PortalSpaceScene.LightConeAngle));
    }

    [Fact]
    public void Creature_mode_s_ambient_and_own_fov_are_its_constructor_s()
    {
        var ambient = Bits(0x3E99999A);
        Assert.Equal(new Vector3(ambient, ambient, ambient), PortalSpaceScene.AmbientColor);
        Assert.Equal(0x3F490FDBu, BitsOf(PortalSpaceScene.OwnFovRadians));
    }

    [Fact]
    public void The_spin_starts_at_frame_1_at_40_frames_a_second()
    {
        Assert.Equal(1, PortalSpaceScene.SpinStartFrame);
        Assert.Equal(40f, PortalSpaceScene.SpinFramerate);
        Assert.True(PortalSpaceScene.DegradesDisabled);
    }

    private static float Bits(uint pattern) => BitConverter.UInt32BitsToSingle(pattern);

    private static uint BitsOf(float value) => BitConverter.SingleToUInt32Bits(value);
}
