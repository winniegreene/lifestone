using System.Numerics;
using Lifestone.Client.Audio;
using Lifestone.Client.Avatars;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Lifestone.Data.Motion;
using Lifestone.Data.Terrain;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// M3 4.2 x2, item 4 (closes <c>TODO(avatars-listener-camera)</c>): the avatars' sounds are heard from the camera, as retail's <c>SmartBox::set_viewer</c> (0x00452C80) hands the camera's position to
/// <c>SoundManager::SetPlayerPosition</c>. In third person the camera is behind the player, so a source just left of the player is heard from the camera's side and distance, not the body's.
/// </summary>
public sealed class AvatarListenerTests
{
    private const uint Table = 0x20000001;
    private const uint Guid = 0x80000007;
    private static readonly LandblockCoord Holtburg = new(0xA9, 0xB4);

    [Fact]
    public void In_third_person_a_source_left_of_the_player_pans_left_from_the_camera_and_centre_from_the_body()
    {
        // The player at (96, 96) facing north; the camera 8 m behind it (south), looking north too; the source 3 m west of the player.
        var source = new Vector3(93, 96, 0);
        var body = Hear(source, listener: null);
        Assert.Equal(0, RetailSoundRules.PanIndex(body.Distance, body.BearingDegrees, mono: false)); // 3 m from the body: inside the 5 m where nothing pans

        var camera = new CameraAt(Frame(new Vector3(96, 88, 2), 0.0));
        var heard = Hear(source, camera);
        Assert.InRange(heard.Distance, 8.5f, 9.0f); // from the camera: 3 west, 8 north
        Assert.True(RetailSoundRules.PanIndex(heard.Distance, heard.BearingDegrees, mono: false) < 0, "a source left of the camera pans left");

        // A camera west of the source facing east has it ahead and to the right (south of east is the right-hand side): the listener, not the body, decides.
        var other = new CameraAt(Frame(new Vector3(86, 100, 2), 90.0));
        var flipped = Hear(source, other);
        Assert.True(RetailSoundRules.PanIndex(flipped.Distance, flipped.BearingDegrees, mono: false) > 0, "a source right of the camera pans right");
    }

    [Fact]
    public void A_camera_frame_is_the_outdoor_cell_under_it_or_the_envcell_it_is_in_and_a_missing_listener_falls_back_to_the_viewer()
    {
        var origin = AcCoordinates.LandblockOrigin(Holtburg);
        var outdoors = new GlobalPosition(origin.X + 100.0, origin.Y + 50.0, 40.0);
        Assert.True(CameraListener.TryFrame(outdoors, 90.0, 0u, out var frame));
        Assert.Equal(Holtburg, LandblockCoord.FromId(frame.CellId));
        Assert.True(LandDefs.IsOutdoorCell(frame.CellId));
        Assert.Equal(100f, frame.Origin.X, 2);
        Assert.Equal(50f, frame.Origin.Y, 2);
        Assert.Equal(90f, PositionFrames.Heading(frame.Orientation), 1);

        Assert.True(CameraListener.TryFrame(outdoors, 0.0, 0xA9B40105u, out var indoors));
        Assert.Equal(0xA9B40105u, indoors.CellId);
        Assert.Equal(40f, indoors.Origin.Z, 2);

        Assert.False(CameraListener.TryFrame(new GlobalPosition(-5.0, -5.0, 0.0), 0.0, 0u, out _)); // off the map

        var none = Hear(new Vector3(106, 96, 0), new CameraAt(default, has: false));
        var viewerOnly = Hear(new Vector3(106, 96, 0), listener: null);
        Assert.Equal(viewerOnly, none);
    }

    private static CellFrame Frame(Vector3 local, double heading)
    {
        var origin = AcCoordinates.LandblockOrigin(Holtburg);
        Assert.True(CameraListener.TryFrame(new GlobalPosition(origin.X + local.X, origin.Y + local.Y, local.Z), heading, 0u, out var frame));
        return frame;
    }

    /// <summary>The origin of a sound from an object at <paramref name="source"/> (Holtburg landblock) with the player's body, the viewer, at (96, 96) facing north.</summary>
    private static SoundOrigin Hear(Vector3 source, IAvatarListener? listener)
    {
        var set = new AvatarSet(AvatarRig.Sources()) { Listener = listener };
        set.Create(AvatarRig.HumanAt(Guid, source) with { SoundTableId = Table }, now: 0);
        var sink = new OriginSink();
        set.EffectEvents = sink;
        set.PlaySound(Guid, 90, 1f);
        set.Advance(0.1, new AvatarViewer(AvatarRig.At(new Vector3(96, 96, 0), 0f)), long.MaxValue);
        return Assert.Single(sink.Origins);
    }

    private sealed class CameraAt(CellFrame frame, bool has = true) : IAvatarListener
    {
        public bool TryGetListener(out CellFrame result)
        {
            result = frame;
            return has;
        }
    }

    private sealed class OriginSink : IEffectEventSink
    {
        public List<SoundOrigin> Origins { get; } = new(4);

        public void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId) => Origins.Add(origin);
    }
}
