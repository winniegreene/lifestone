using System.Numerics;
using Lifestone.Client.Audio;
using Lifestone.Data.Animation;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Audio;

/// <summary>
/// Step PS10: a positioned sound pans to the side it is on. Retail's bearing is the heading from the source to the
/// listener minus the listener's heading (<c>Position::heading(source, listener)</c> in
/// <c>SoundManager::PlaySoundInternal</c>), so a sound on the right lowers the left channel; these tests pin that
/// physical meaning, not just the numbers.
/// </summary>
public sealed class SoundOriginTests
{
    private const uint EnterWave = 0x0A000246;

    /// <summary>An outdoor cell of landblock 0xA9B4 (the cell id only matters for the landblock).</summary>
    private const uint Cell = 0xA9B40019;

    private static readonly Quaternion FacingNorth = Quaternion.Identity;

    /// <summary>Heading 90: the forward (+Y) axis turned clockwise to +X.</summary>
    private static readonly Quaternion FacingEast = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2f);

    [Fact]
    public void A_source_east_of_a_listener_facing_north_is_heard_on_the_right()
    {
        var listener = At(Cell, 96f, 96f, FacingNorth);
        var source = At(Cell, 106f, 96f, FacingNorth);

        var origin = SoundOrigin.Between(listener, source);

        Assert.Equal(10f, origin.Distance, 4);
        Assert.Equal(270f, origin.BearingDegrees, 3); // the heading from the source to the listener: west
        var pan = RetailSoundRules.PanIndex(origin.Distance, origin.BearingDegrees, mono: false);
        Assert.Equal(14, pan);
        var (left, right) = RetailSoundRules.ChannelGains(pan, 0);
        Assert.True(right > left, $"right {right} should be louder than left {left}");
    }

    [Fact]
    public void A_source_west_of_a_listener_facing_north_is_heard_on_the_left()
    {
        var listener = At(Cell, 96f, 96f, FacingNorth);
        var source = At(Cell, 86f, 96f, FacingNorth);

        var origin = SoundOrigin.Between(listener, source);

        Assert.Equal(10f, origin.Distance, 4);
        Assert.Equal(90f, origin.BearingDegrees, 3);
        var pan = RetailSoundRules.PanIndex(origin.Distance, origin.BearingDegrees, mono: false);
        Assert.Equal(-14, pan);
        var (left, right) = RetailSoundRules.ChannelGains(pan, 0);
        Assert.True(left > right, $"left {left} should be louder than right {right}");
    }

    [Fact]
    public void The_listeners_heading_turns_the_field_with_it()
    {
        // Facing east, the south is on the listener's right and the north on its left.
        var listener = At(Cell, 96f, 96f, FacingEast);
        Assert.Equal(90.0, listener.HeadingDegrees, 3);

        var south = SoundOrigin.Between(listener, At(Cell, 96f, 86f, FacingNorth));
        var north = SoundOrigin.Between(listener, At(Cell, 96f, 106f, FacingNorth));
        var ahead = SoundOrigin.Between(listener, At(Cell, 106f, 96f, FacingNorth));
        var behind = SoundOrigin.Between(listener, At(Cell, 86f, 96f, FacingNorth));

        Assert.Equal(14, RetailSoundRules.PanIndex(south.Distance, south.BearingDegrees, mono: false));
        Assert.Equal(-14, RetailSoundRules.PanIndex(north.Distance, north.BearingDegrees, mono: false));
        Assert.Equal(0, RetailSoundRules.PanIndex(ahead.Distance, ahead.BearingDegrees, mono: false));
        Assert.Equal(0, RetailSoundRules.PanIndex(behind.Distance, behind.BearingDegrees, mono: false));
    }

    [Fact]
    public void The_player_lowers_the_left_channel_for_a_sound_on_the_right()
    {
        var output = new RecordingAudioOutput();
        var player = new RetailSoundPlayer(output, RetailSoundPlayerTests.Source(), new ScriptedDice(), new ScriptedCrt());
        var listener = At(Cell, 96f, 96f, FacingNorth);

        player.PlaySound(EnterWave, SoundOrigin.Between(listener, At(Cell, 106f, 96f, FacingNorth))); // east: right
        player.PlaySound(EnterWave, SoundOrigin.Between(listener, At(Cell, 86f, 96f, FacingNorth))); // west: left

        Assert.Equal(2, output.Plays.Count);
        var (eastLeft, eastRight) = RetailSoundRules.ChannelGains(output.Plays[0].Pan, output.Plays[0].AttenuationDb);
        var (westLeft, westRight) = RetailSoundRules.ChannelGains(output.Plays[1].Pan, output.Plays[1].AttenuationDb);
        Assert.True(eastRight > eastLeft);
        Assert.True(westLeft > westRight);
        Assert.Equal(output.Plays[0].AttenuationDb, output.Plays[1].AttenuationDb);
    }

    [Fact]
    public void A_landblock_seam_is_continuous()
    {
        // The listener near the east edge of 0xA9B4; the source 10 m further east, just inside 0xAAB4.
        var listener = At(0xA9B4003D, 190f, 96f, FacingNorth);
        var source = At(0xAAB40005, 8f, 96f, FacingNorth);

        var origin = SoundOrigin.Between(listener, source);

        Assert.Equal(10f, origin.Distance, 3);
        Assert.Equal(270f, origin.BearingDegrees, 3);
        Assert.Equal(14, RetailSoundRules.PanIndex(origin.Distance, origin.BearingDegrees, mono: false));
    }

    [Fact]
    public void The_distance_counts_height_but_the_bearing_does_not()
    {
        var listener = At(Cell, 96f, 96f, FacingNorth);
        var above = new CellFrame(Cell, new AcFrame(new Vector3(102f, 96f, 8f), FacingNorth));

        var origin = SoundOrigin.Between(listener, above);

        Assert.Equal(10f, origin.Distance, 4); // (6, 0, 8)
        Assert.Equal(270f, origin.BearingDegrees, 3);
    }

    [Fact]
    public void A_source_at_the_listener_is_centred()
    {
        var listener = At(Cell, 96f, 96f, FacingEast);

        var origin = SoundOrigin.Between(listener, listener);

        Assert.Equal(0f, origin.Distance);
        Assert.Equal(0, RetailSoundRules.PanIndex(origin.Distance, origin.BearingDegrees, mono: false));
    }

    private static CellFrame At(uint cellId, float x, float y, Quaternion orientation) =>
        new(cellId, new AcFrame(new Vector3(x, y, 0f), orientation));
}
