using System.Numerics;
using Lifestone.Client.Avatars;
using Lifestone.Client.Camera;
using Lifestone.Client.Session.Contracts;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// M3 4.1 lane f: the first-person fade (<c>CameraSet::UpdateCamera</c>, 11.6096 0x00458C00): the camera's translucency reaches every part of the player and of the items it holds
/// through one input on the player's avatar, as a floor under each part's own translucency, every frame without allocating.
/// </summary>
public sealed class FirstPersonFadeTests
{
    private const uint Player = 0x50000001;
    private const uint Wand = 9;
    private const uint Bystander = 11;
    private static readonly AvatarViewer Viewer = new(AvatarRig.At(new Vector3(96, 96, 0)));

    [Theory]
    [InlineData(0.0f, 0.0f, 0.0f)]
    [InlineData(0.0f, 0.4f, 0.4f)]
    [InlineData(0.7f, 0.4f, 0.7f)]
    [InlineData(0.3f, 1.0f, 1.0f)]
    [InlineData(1.0f, 0.0f, 1.0f)]
    public void The_cameras_translucency_is_a_floor_under_the_parts_own(float own, float camera, float drawn)
    {
        Assert.Equal(drawn, FirstPersonFade.Apply(own, camera));
    }

    [Theory]
    [InlineData(-0.5f, 0f)]
    [InlineData(0.25f, 0.25f)]
    [InlineData(3f, 1f)]
    [InlineData(float.NaN, 0f)]
    public void The_input_is_limited_to_zero_to_one(float given, float kept)
    {
        var set = NewSet();
        set.Player!.CameraTranslucency = given;
        Assert.Equal(kept, set.Player.CameraTranslucency);
    }

    [Theory]
    [InlineData(1.0f, 0f)]
    [InlineData(0.45f, 0f)]
    [InlineData(0.325f, 0.5f)]
    [InlineData(0.2f, 1f)]
    [InlineData(0.15f, 1f)]
    public void The_distance_from_the_pivot_maps_to_retails_fade_from_045_to_02_metres(float metres, float expected)
    {
        // The model's map, read through a pose: the viewer `metres` behind the pivot with the camera settled (the pose's translucency uses the viewer of the frame before).
        var model = new ChaseCameraModel { Scale = metres / MathF.Sqrt((ChaseCameraModel.DefaultOffsetYFactor * ChaseCameraModel.DefaultOffsetYFactor) + (ChaseCameraModel.DefaultOffsetZFactor * ChaseCameraModel.DefaultOffsetZFactor)) };
        model.SetDefaultOffsets();
        model.Snap();
        var player = new CellFrame(0x7F7F0101, new AcFrame(new Vector3(40, 40, 0), Quaternion.Identity));
        CameraPose pose = default;
        for (var i = 0; i < 400; i++)
        {
            pose = model.Update(i / 60.0, player, sweep: null);
        }

        Assert.Equal(expected, pose.PlayerTranslucency, 0.05f);
    }

    [Fact]
    public void Every_part_of_the_player_and_of_the_item_it_holds_is_drawn_at_the_cameras_translucency_and_a_bystander_is_not()
    {
        var set = NewSet();
        set.Advance(0.1, Viewer, long.MaxValue);
        Assert.All(PartsOf(set, Player), t => Assert.Equal(0f, t));

        set.Player!.CameraTranslucency = 0.6f;
        set.Advance(0.2, Viewer, long.MaxValue);
        Assert.All(PartsOf(set, Player), t => Assert.Equal(0.6f, t));
        Assert.All(PartsOf(set, Wand), t => Assert.Equal(0.6f, t));
        Assert.Equal(0.6f, DrawOf(set, Player).Translucency);
        Assert.Equal(0.6f, DrawOf(set, Wand).Translucency);
        Assert.All(PartsOf(set, Bystander), t => Assert.Equal(0f, t));

        set.Player.CameraTranslucency = 0f;
        set.Advance(0.3, Viewer, long.MaxValue);
        Assert.All(PartsOf(set, Wand), t => Assert.Equal(0f, t));
    }

    [Fact]
    public void A_camera_translucency_of_one_leaves_the_held_item_not_drawn_as_well()
    {
        var set = NewSet();
        set.Player!.CameraTranslucency = 1f;
        set.Advance(0.1, Viewer, long.MaxValue);
        Assert.Equal(1f, DrawOf(set, Player).Translucency);
        Assert.Equal(1f, DrawOf(set, Wand).Translucency);
    }

    [Fact]
    public void Setting_the_input_every_frame_allocates_nothing()
    {
        var set = NewSet();
        for (var i = 1; i <= 30; i++)
        {
            set.Player!.CameraTranslucency = 0.5f;
            set.Advance(i / 60.0, Viewer, long.MaxValue);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 31; i <= 130; i++)
        {
            set.Player!.CameraTranslucency = (i % 10) / 10f;
            set.Advance(i / 60.0, Viewer, long.MaxValue);
        }

        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static AvatarSet NewSet()
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(Player, new Vector3(30, 30, 0)) with { IsSelf = true }, now: 0);
        set.Create(new AvatarCreate { Guid = Wand, SetupId = AvatarRig.Sword, Position = AvatarRig.At(new Vector3(31, 30, 0)) }, now: 0);
        Assert.Equal(AvatarApply.Applied, set.Apply(new ObjectParented(Wand, Player, Location: 1, Placement: 0)));
        set.Create(AvatarRig.HumanAt(Bystander, new Vector3(34, 30, 0)), now: 0);
        return set;
    }

    private static AvatarDraw DrawOf(AvatarSet set, uint guid)
    {
        foreach (var draw in set.Frame.Avatars)
        {
            if (draw.Guid == guid)
            {
                return draw;
            }
        }

        throw new InvalidOperationException($"no draw for {guid:X}");
    }

    private static float[] PartsOf(AvatarSet set, uint guid)
    {
        var draw = DrawOf(set, guid);
        return set.Frame.PartTranslucency.Slice(draw.PartStart, draw.PartCount).ToArray();
    }
}
