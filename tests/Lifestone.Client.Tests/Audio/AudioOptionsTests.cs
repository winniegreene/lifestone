using Lifestone.Client.Audio;
using Lifestone.Client.Options;

namespace Lifestone.Client.Tests.Audio;

/// <summary>The Audio option group (docs/portal-space.md §12): <c>--mute</c>, <c>--volume</c> and owner decision D8.</summary>
public sealed class AudioOptionsTests
{
    [Fact]
    public void An_interactive_run_plays_at_full_volume_by_default()
    {
        var settings = AudioOptions.Parse([]).Resolve(automated: false);
        Assert.Equal(1f, settings.MasterVolume);
        Assert.False(settings.Muted);
        Assert.Equal(0f, settings.MasterDecibels);
    }

    [Fact]
    public void An_automated_run_is_silent_unless_a_volume_is_given()
    {
        Assert.True(AudioOptions.Parse([]).Resolve(automated: true).Muted);
        Assert.Equal(AudioSettings.SilentDecibels, AudioOptions.Parse([]).Resolve(automated: true).MasterDecibels);

        var loud = AudioOptions.Parse(["--volume=0.5"]).Resolve(automated: true);
        Assert.False(loud.Muted);
        Assert.Equal(0.5f, loud.MasterVolume);
        Assert.Equal(-6.0206f, loud.MasterDecibels, 3);
    }

    [Fact]
    public void Mute_wins_over_a_volume_in_every_run()
    {
        Assert.True(AudioOptions.Parse(["--mute"]).Resolve(automated: false).Muted);
        Assert.True(AudioOptions.Parse(["--mute", "--volume=1"]).Resolve(automated: false).Muted);
        Assert.True(AudioOptions.Parse(["--volume=1", "--mute"]).Resolve(automated: true).Muted);
    }

    [Theory]
    [InlineData("--volume=0", 0f)]
    [InlineData("--volume=1", 1f)]
    public void A_volume_from_zero_to_one_is_taken(string argument, float expected)
    {
        var o = AudioOptions.Parse([argument]);
        Assert.Equal(expected, o.Volume);
        Assert.Empty(o.Errors);
    }

    [Theory]
    [InlineData("--volume=1.5")]
    [InlineData("--volume=-0.1")]
    [InlineData("--volume=loud")]
    [InlineData("--volume=NaN")]
    [InlineData("--volume")]
    public void A_bad_volume_is_reported_and_keeps_the_default(string argument)
    {
        var o = AudioOptions.Parse([argument]);
        Assert.Null(o.Volume);
        Assert.Single(o.Errors);
        Assert.False(o.Resolve(automated: false).Muted);
    }

    [Theory]
    [InlineData("--record-audio=cache/video/login.wav", "cache/video/login.wav")]
    [InlineData(@"--record-audio=cache\video\login.WAV", "cache/video/login.WAV")]
    public void A_recording_path_under_cache_is_taken(string argument, string expected)
    {
        var o = AudioOptions.Parse([argument]);
        Assert.Equal(expected, o.RecordPath);
        Assert.Empty(o.Errors);
    }

    [Theory]
    [InlineData("--record-audio")]
    [InlineData("--record-audio=")]
    [InlineData("--record-audio=login.wav")]
    [InlineData("--record-audio=cache/.wav")]
    [InlineData("--record-audio=cache/video/login.mp3")]
    [InlineData("--record-audio=docs/login.wav")]
    [InlineData("--record-audio=cache/../docs/login.wav")]
    [InlineData("--record-audio=cache/./login.wav")]
    [InlineData("--record-audio=cache//login.wav")]
    [InlineData("--record-audio=/cache/login.wav")]
    [InlineData(@"--record-audio=C:\cache\login.wav")]
    [InlineData("--record-audio=C:cache/login.wav")]
    [InlineData(@"--record-audio=\\host\share\cache\a.wav")]
    [InlineData("--record-audio=cache/NUL.wav")]
    [InlineData("--record-audio=cache/com1.wav")]
    [InlineData("--record-audio=cache/con/a.wav")]
    public void A_recording_path_outside_cache_or_not_a_wave_is_reported(string argument)
    {
        var o = AudioOptions.Parse([argument]);
        Assert.Null(o.RecordPath);
        Assert.Single(o.Errors);
    }

    [Fact]
    public void Recording_does_not_change_the_master_level()
    {
        var o = AudioOptions.Parse(["--record-audio=cache/video/login.wav"]);
        Assert.True(o.Resolve(automated: true).Muted);
        Assert.False(o.Resolve(automated: false).Muted);
    }

    [Fact]
    public void The_group_is_registered_for_every_game_scene_and_not_the_headless_host()
    {
        var registry = OptionCatalog.Registry;
        foreach (var name in new[] { "--mute", "--volume" })
        {
            Assert.Equal(OptionScenes.All, registry.ScenesOf(name));
            Assert.False(registry.AppliesTo(name, OptionScenes.Headless), name);
        }

        Assert.False(registry.EndsRun(CommandLineArgument.ParseAll(["--mute", "--volume=0.3"]), OptionScenes.Main));
    }

    [Theory]
    [InlineData("--play-screenshot=cache/x.png")]
    [InlineData("--play-panel-shots=cache/x")]
    [InlineData("--play-look-shots=cache/x")]
    public void The_capture_runs_are_automated_so_they_are_silent_P4_R15(string capture)
    {
        // P4 review R15: tools\play.ps1 -Offscreen fails a run that is not muted; the captures save and quit by themselves.
        Assert.True(OptionCatalog.Registry.EndsRun(CommandLineArgument.ParseAll([capture]), OptionScenes.Main));
    }
}
