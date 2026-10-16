using System.Numerics;
using Lifestone.Client.Audio;
using Lifestone.Client.Avatars;
using Lifestone.Data.Effects;

namespace Lifestone.Client.Tests.Avatars;

/// <summary>
/// M3 1.3 s1: where an avatar's sound comes from. The set's origin is <see cref="SoundOrigin.Between"/>, retail's source-to-listener bearing
/// (<c>SoundManager::PlaySoundInternal(SoundBufRef*, Position*, float, int)</c>, 11.6096 0x00550D80, over <c>Position::heading</c> 0x005AA5D0): before the fix
/// the set measured the listener-to-source bearing, which put every object on the listener's right in the left speaker.
/// </summary>
public sealed class AvatarSoundOriginTests
{
    private const uint Table = 0x20000001;
    private const uint Guid = 0x80000007;

    [Fact]
    public void An_object_to_the_listeners_right_pans_right_and_one_to_its_left_pans_left()
    {
        // The listener faces north (heading 0) at (96, 96); +X is east.
        var east = Hear(new Vector3(106, 96, 0), listenerHeading: 0f);
        var west = Hear(new Vector3(86, 96, 0), listenerHeading: 0f);

        Assert.Equal(10f, east.Distance, 3);
        Assert.Equal(270f, Normalised(east.BearingDegrees), 2); // source-to-listener heading 270 (west), minus the listener's 0
        Assert.Equal(90f, Normalised(west.BearingDegrees), 2);
        Assert.Equal(14, RetailSoundRules.PanIndex(east.Distance, east.BearingDegrees, mono: false)); // positive: the left channel is lowered, heard on the right
        Assert.Equal(-14, RetailSoundRules.PanIndex(west.Distance, west.BearingDegrees, mono: false));
    }

    [Fact]
    public void The_pan_follows_the_listeners_heading()
    {
        // Facing east, an object to the north is on the left and one to the south on the right; behind and ahead are centred.
        var north = Hear(new Vector3(96, 106, 0), listenerHeading: 90f);
        var south = Hear(new Vector3(96, 86, 0), listenerHeading: 90f);
        var ahead = Hear(new Vector3(106, 96, 0), listenerHeading: 90f);

        Assert.Equal(-14, RetailSoundRules.PanIndex(north.Distance, north.BearingDegrees, mono: false));
        Assert.Equal(14, RetailSoundRules.PanIndex(south.Distance, south.BearingDegrees, mono: false));
        Assert.Equal(0, RetailSoundRules.PanIndex(ahead.Distance, ahead.BearingDegrees, mono: false));
    }

    [Fact]
    public void An_object_across_a_landblock_seam_is_heard_from_its_true_side()
    {
        // The listener at the east edge of its landblock, the object just over the seam in the next block east (cell id + 0x0100 0000 in the X byte).
        var cell = AvatarRig.HoltburgCell;
        var neighbour = cell + 0x01000000;
        var set = new AvatarSet(AvatarRig.Sources());
        var create = AvatarRig.HumanAt(Guid, new Vector3(4, 96, 0), 0f, neighbour) with { SoundTableId = Table };
        set.Create(create, now: 0);
        var sink = new OriginSink();
        set.EffectEvents = sink;
        set.PlaySound(Guid, 90, 1f);

        set.Advance(0.1, new AvatarViewer(AvatarRig.At(new Vector3(188, 96, 0), 0f, cell)), long.MaxValue);

        var origin = Assert.Single(sink.Origins);
        Assert.Equal(8f, origin.Distance, 2); // 192 - 188 + 4 metres east
        Assert.Equal(14, RetailSoundRules.PanIndex(origin.Distance, origin.BearingDegrees, mono: false));
    }

    [Fact]
    public void The_set_tells_the_sink_which_events_the_players_own_body_fired()
    {
        // M3 1.3 fix B (R2): the player-only footstep counter needs the firing body; another object's event, and a sink that only has the three-argument form, are not the player's.
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(Guid, new Vector3(100, 96, 0)) with { SoundTableId = Table, IsSelf = true }, now: 0);
        set.Create(AvatarRig.HumanAt(Guid + 1, new Vector3(101, 96, 0)) with { SoundTableId = Table }, now: 0);
        var sink = new FlagSink();
        set.EffectEvents = sink;
        Assert.Equal(AvatarApply.Applied, set.PlaySound(Guid, 90, 1f));
        Assert.Equal(AvatarApply.Applied, set.PlaySound(Guid + 1, 90, 1f));

        set.Advance(0.1, new AvatarViewer(AvatarRig.At(new Vector3(96, 96, 0), 0f)), long.MaxValue);

        Assert.Equal(2, sink.FromPlayer.Count);
        Assert.Equal(1, sink.FromPlayer.Count(f => f));
        Assert.Equal(0, sink.ThreeArgumentCalls);
    }

    private sealed class FlagSink : IEffectEventSink
    {
        public List<bool> FromPlayer { get; } = new(4);

        public int ThreeArgumentCalls { get; private set; }

        public void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId) => ThreeArgumentCalls++;

        public void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId, bool fromPlayer) => FromPlayer.Add(fromPlayer);
    }

    private static float Normalised(float degrees) => ((degrees % 360f) + 360f) % 360f;

    private static SoundOrigin Hear(Vector3 objectAt, float listenerHeading)
    {
        var set = new AvatarSet(AvatarRig.Sources());
        set.Create(AvatarRig.HumanAt(Guid, objectAt) with { SoundTableId = Table }, now: 0);
        var sink = new OriginSink();
        set.EffectEvents = sink;
        Assert.Equal(AvatarApply.Applied, set.PlaySound(Guid, 90, 1f));

        set.Advance(0.1, new AvatarViewer(AvatarRig.At(new Vector3(96, 96, 0), listenerHeading)), long.MaxValue);

        Assert.Equal(Table, Assert.Single(sink.Tables));
        return Assert.Single(sink.Origins);
    }

    private sealed class OriginSink : IEffectEventSink
    {
        public List<SoundOrigin> Origins { get; } = new(4);

        public List<uint> Tables { get; } = new(4);

        public void OnEffectEvent(in EffectEvent effectEvent, in SoundOrigin origin, uint soundTableId)
        {
            Origins.Add(origin);
            Tables.Add(soundTableId);
        }
    }
}
