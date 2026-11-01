using Lifestone.Client.Camera;

namespace Lifestone.Client.Tests.Camera;

/// <summary>
/// M5 6 v1 (<c>cam-fov-resize</c>, M5 D9): the vertical angle <see cref="RetailLens"/> gives the three window sizes of DoD 12, and that a resize to another aspect changes it (the game recomputes it from this function on every
/// size change of the root window, as retail recomputes it each frame in <c>SmartBox::RenderNormalMode</c>, 11.6096 0x00453B40). The angle is the 90-degree preference over the aspect less 0.1, so only the aspect matters.
/// </summary>
public sealed class RetailLensSizeTests
{
    [Theory]
    [InlineData(800, 600, 72.973f)]
    [InlineData(1280, 720, 53.642f)]
    [InlineData(1920, 1080, 53.642f)]
    public void TheThreeWindowSizes_GiveTheirVerticalAngle(int width, int height, float expectedDegrees) =>
        Assert.Equal(expectedDegrees, RetailLens.VerticalFovDegrees(RetailLens.DefaultFovDegrees, (float)width / height), 2);

    [Fact]
    public void AResizeToAnotherAspect_ChangesTheAngle_AndOneToTheSameAspectDoesNot()
    {
        float Angle(int width, int height) => RetailLens.VerticalFovDegrees(RetailLens.DefaultFovDegrees, (float)width / height);

        Assert.NotEqual(Angle(800, 600), Angle(1280, 720));
        Assert.True(Angle(1280, 720) < Angle(800, 600), "a wider window is drawn with a narrower vertical angle");
        Assert.Equal(Angle(1280, 720), Angle(1920, 1080), 3); // the same 16:9 aspect: the same lens, whatever the pixel size
        Assert.Equal(Angle(800, 600), Angle(1024, 768), 3);   // and the same 4:3
    }

    [Fact]
    public void ADragThroughManySizes_StaysInsideTheLimitsAndFallsAsTheWindowWidens()
    {
        var previous = float.MaxValue;
        for (var width = 400; width <= 3440; width += 40)
        {
            var angle = RetailLens.VerticalFovDegrees(RetailLens.DefaultFovDegrees, width / 600f);
            Assert.InRange(angle, 1f, RetailLens.MaxVerticalDegrees);
            Assert.True(angle <= previous, $"width {width}: {angle} should not exceed {previous}");
            previous = angle;
        }
    }
}
