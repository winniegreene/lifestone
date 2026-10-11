using System.Numerics;
using Lifestone.Data.World.Sky;

namespace Lifestone.Data.Tests.Sky;

/// <summary>CPhysics::AddGfxVelocity / UseTime / UpdateTexVelocity (acclient.c:311467, 311335, 311248).</summary>
public sealed class SkyTextureScrollTests(SkyFixture fixture) : IClassFixture<SkyFixture>
{
    [RequiresDatsFact]
    public void Sync_registers_the_scrolling_clouds_and_rain_of_the_Region()
    {
        var scroll = new SkyTextureScroll();
        var sky = fixture.Sky;
        scroll.Sync(sky.EvaluateAtTimeOfDay(0, 0.5f));
        Assert.Equal(1, scroll.Count); // day group 0: only the cloud layer 0x01004C36 scrolls
        scroll.Step(10f);
        Assert.Equal(new Vector2(-0.13f, 0.13f), scroll.GetOffset(0x01004C36u));

        // Rainy day group 3: clouds 0x01004C35 plus the two rain sheets 0x01004C44 / 0x01004C42 (always visible).
        scroll.Sync(sky.EvaluateAtTimeOfDay(3, 0.5f));
        Assert.Equal(3, scroll.Count);
        Assert.Equal(Vector2.Zero, scroll.GetOffset(0x01004C36u));
        scroll.Step(0.5f);
        Assert.Equal(new Vector2(0.01f, -0.85f), scroll.GetOffset(0x01004C44u)); // TexVelocity (0.02, -1.7) for 0.5 s
    }

    [Fact]
    public void Sync_refuses_to_guess_setup_parts()
    {
        var setupObject = new SkyObjectDesc(0, 0f, 0f, 0f, 0f, new Vector2(0.1f, 0f), 0x02000001u, 0u, SkyObjectProperties.None);
        var group = new SkyDayGroup(0, 5f, "Test", [setupObject], []);
        var state = SkyEvaluator.Evaluate(group, 0.5f);
        Assert.Throws<NotSupportedException>(() => new SkyTextureScroll().Sync(state));

        var scroll = new SkyTextureScroll();
        scroll.Sync(state, _ => [0x01000010u, 0x01000011u]);
        Assert.Equal(2, scroll.Count);
    }

    [Fact]
    public void Positive_offsets_wrap_once_they_reach_one_and_negative_ones_run_free()
    {
        var scroll = new SkyTextureScroll();
        scroll.AddGfxVelocity(1u, new Vector2(0.6f, -0.6f));
        float[] expectedU = [0.6f, 1.2f, 0.8f, 1.4f, 1.0f, 0.6f];
        for (var i = 0; i < expectedU.Length; i++)
        {
            scroll.Step(1f);
            Assert.Equal(expectedU[i], scroll.GetOffset(1u).X, 5);
            Assert.Equal(-0.6f * (i + 1), scroll.GetOffset(1u).Y, 5);
        }
    }

    [Fact]
    public void Update_steps_only_after_the_minimum_quantum()
    {
        var scroll = new SkyTextureScroll();
        scroll.AddGfxVelocity(7u, new Vector2(0.5f, 0f));
        Assert.False(scroll.Update(100.0)); // first call only sets the reference
        Assert.False(scroll.Update(100.02)); // < 1/30 s: accumulate
        Assert.True(scroll.Update(100.04));
        Assert.Equal(0.02f, scroll.GetOffset(7u).X, 5);
        Assert.False(scroll.Update(99.0)); // clock went backwards: reset reference only
        Assert.True(scroll.Update(99.5));
        Assert.Equal(0.27f, scroll.GetOffset(7u).X, 5);
    }

    [Fact]
    public void Re_registering_keeps_the_offset_and_zero_velocity_removes()
    {
        var scroll = new SkyTextureScroll();
        scroll.AddGfxVelocity(3u, new Vector2(0.1f, 0.2f));
        scroll.Step(2f);
        scroll.AddGfxVelocity(3u, new Vector2(1f, 1f));
        Assert.Equal(new Vector2(0.2f, 0.4f), scroll.GetOffset(3u));
        scroll.AddGfxVelocity(3u, Vector2.Zero);
        Assert.Equal(0, scroll.Count);
        Assert.Equal(Vector2.Zero, scroll.GetOffset(3u));
    }
}
