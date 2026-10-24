using Lifestone.Client.Audio;
using Lifestone.Data.Effects;

namespace Lifestone.Client.Tests.Audio;

/// <summary>Step PS10: retail's variant choice, probability roll, attenuation and pan (m2-log-in.md §4.10).</summary>
public sealed class RetailSoundRulesTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_variant_roll_reaches_every_variant_but_the_last(int count)
    {
        var dice = new RetailRandom(17);
        var hits = new int[count];
        for (var i = 0; i < 50_000; i++)
        {
            var index = RetailSoundRules.PickVariant(count, dice.RollDice(0f, 1f));
            Assert.InRange(index, 0, count - 1);
            hits[index]++;
        }

        if (count == 1)
        {
            Assert.Equal(50_000, hits[0]);
            return;
        }

        // Indices 0 .. n−2 are uniform; the last is dead data.
        Assert.Equal(0, hits[count - 1]);
        for (var i = 0; i < count - 1; i++)
        {
            Assert.InRange(hits[i], (50_000 / (count - 1)) * 0.95, (50_000 / (count - 1)) * 1.05);
        }
    }

    [Fact]
    public void The_variant_index_truncates_and_rejects_nothing_to_pick()
    {
        Assert.Equal(-1, RetailSoundRules.PickVariant(0, 0.5));
        Assert.Equal(0, RetailSoundRules.PickVariant(1, 0.99999988));
        Assert.Equal(1, RetailSoundRules.PickVariant(3, 0.99999988)); // 2 · 0.99999988 = 1.99999976
        Assert.Equal(1, RetailSoundRules.PickVariant(3, 0.5));
        Assert.Equal(-1, RetailSoundRules.PickVariant(3, 1.6)); // past the end: retail's bounds check
    }

    [Fact]
    public void The_probability_scale_is_the_float_nearest_one_over_32767()
    {
        var scale = RetailSoundRules.PlayProbabilityScale;
        Assert.Equal(1f / 32767f, scale);
        Assert.Equal(Math.ScaleB(1.0 + Math.ScaleB(1.0, -15), -15), (double)scale);
        Assert.Equal(1.0 - Math.ScaleB(1.0, -30), 32767 * (double)scale);
    }

    [Fact]
    public void Probability_1_always_plays_0_never_and_a_half_plays_half_the_rolls()
    {
        var always = 0;
        var never = 0;
        var half = 0;
        for (var rand = 0; rand <= MsvcCrtRandom.RandMax; rand++)
        {
            always += RetailSoundRules.PlayProbability(rand, 1f) ? 1 : 0;
            never += RetailSoundRules.PlayProbability(rand, 0f) ? 1 : 0;
            half += RetailSoundRules.PlayProbability(rand, 0.5f) ? 1 : 0;
        }

        Assert.Equal(32768, always);
        Assert.Equal(0, never);
        Assert.Equal(16384, half); // rand() 0 .. 16383
        Assert.True(RetailSoundRules.PlayProbability(16383, 0.5f));
        Assert.False(RetailSoundRules.PlayProbability(16384, 0.5f));
    }

    [Theory]
    [InlineData(1.0f, 0)]
    [InlineData(0.5f, -6)] // −6.0206, rounded up
    [InlineData(0.3f, -10)] // the tunnel hum: −10.46
    [InlineData(0.25f, -12)]
    [InlineData(0.01f, -40)] // −40.0000008: 6.0206 is a hair over 20·log₁₀ 2, so −40 dB stays −40
    [InlineData(0.0029f, -50)]
    public void Attenuation_is_whole_decibels_rounded_up(float volume, int decibels)
    {
        Assert.True(RetailSoundRules.TryAttenuation(0f, volume, 1f, out var db));
        Assert.Equal(decibels, db);
    }

    [Theory]
    [InlineData(0.0028f)] // −51.02: below VOL_MIN
    [InlineData(0f)]
    [InlineData(-1f)]
    public void A_sound_quieter_than_minus_50_db_or_without_gain_does_not_play(float volume)
    {
        Assert.False(RetailSoundRules.TryAttenuation(0f, volume, 1f, out var db));
        Assert.Equal(RetailSoundRules.VolMin, db);
    }

    [Fact]
    public void Beyond_5_m_the_gain_falls_with_the_square_of_the_distance()
    {
        Assert.True(RetailSoundRules.TryAttenuation(4.99f, 1f, 1f, out var near));
        Assert.True(RetailSoundRules.TryAttenuation(5f, 1f, 1f, out var five));
        Assert.True(RetailSoundRules.TryAttenuation(10f, 1f, 1f, out var ten)); // 25 / 100 = 0.25
        Assert.True(RetailSoundRules.TryAttenuation(50f, 1f, 1f, out var fifty)); // 0.01
        Assert.False(RetailSoundRules.TryAttenuation(100f, 1f, 1f, out _)); // 0.0025: −52

        Assert.Equal((0, 0, -12, -40), (near, five, ten, fifty));
    }

    [Fact]
    public void The_slider_multiplies_after_the_cap_inside_the_logarithm()
    {
        // A volume of 4 at 10 m is a gain of 1 after the cap; the slider then halves it.
        Assert.True(RetailSoundRules.TryAttenuation(10f, 4f, 0.5f, out var capped));
        Assert.Equal(-6, capped);

        Assert.True(RetailSoundRules.TryAttenuation(0f, 0.3f, 0.5f, out var both)); // 0.15: −16.48
        Assert.Equal(-16, both);

        Assert.False(RetailSoundRules.TryAttenuation(0f, 1f, 0f, out _));
    }

    [Theory]
    [InlineData(10f, 90f, -14)] // sin 90° · −15 = −14.999999999999998, truncated toward 0
    [InlineData(10f, -90f, 14)]
    [InlineData(10f, 30f, -7)] // −7.49999995
    [InlineData(10f, 60f, -12)]
    [InlineData(10f, 0f, 0)]
    [InlineData(10f, 180f, 0)]
    [InlineData(10f, 450f, -14)] // modulo 360
    [InlineData(10f, 270f, 14)] // into (−180, 180]: −90°
    [InlineData(10f, -270f, -14)] // fmod keeps the sign: −270°, sin ≈ 1
    [InlineData(5f, 90f, -14)] // from 5 m on
    [InlineData(4.99f, 90f, 0)] // nearer: centred
    public void The_pan_is_sin_of_the_bearing_times_minus_15_from_5_m(float distance, float bearing, int pan)
    {
        Assert.Equal(pan, RetailSoundRules.PanIndex(distance, bearing, mono: false));
    }

    [Fact]
    public void The_pan_never_reaches_15_because_retails_degree_factor_is_a_hair_small()
    {
        // 0.017453292f is 1.4e-10 under π/180, so sin never reaches 1 and the truncation stops at 14.
        Assert.True(RetailSoundRules.DegreesToRadians < Math.PI / 180.0);
        var largest = 0;
        for (var tenths = -7200; tenths <= 7200; tenths++)
        {
            largest = Math.Max(largest, Math.Abs(RetailSoundRules.PanIndex(10f, tenths / 10f, mono: false)));
        }

        Assert.Equal(14, largest);
    }

    [Fact]
    public void Mono_centres_everything()
    {
        Assert.Equal(0, RetailSoundRules.PanIndex(10f, 90f, mono: true));
    }

    [Fact]
    public void Channel_gains_follow_directsound_pan_and_volume()
    {
        var (left, right) = RetailSoundRules.ChannelGains(0, 0);
        Assert.Equal((1f, 1f), (left, right));

        // Pan −15: the right channel is 15 dB down; the volume lowers both.
        (left, right) = RetailSoundRules.ChannelGains(-15, -10);
        Assert.Equal(Math.Pow(10, -10 / 20.0), left, 1e-6);
        Assert.Equal(Math.Pow(10, -25 / 20.0), right, 1e-6);

        (left, right) = RetailSoundRules.ChannelGains(40, -80); // clamped to 15 and −50
        Assert.Equal(Math.Pow(10, -65 / 20.0), left, 1e-7);
        Assert.Equal(Math.Pow(10, -50 / 20.0), right, 1e-7);
    }
}
