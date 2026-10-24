using System.Numerics;
using Lifestone.Client.Camera;
using Lifestone.Client.Play;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Animation;
using Lifestone.Data.Physics.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Tests.Camera;

/// <summary>
/// The camera rules of the M4 polish pass (P2 and P3): retail's field of view for the play camera (<c>SmartBox::RenderNormalMode</c>), the default chase camera's geometry, the hunt's framed camera and the look capture's camera, with no engine.
/// </summary>
public sealed class CameraPolishRulesTests
{
    [Fact]
    public void RetailLens_FourByThree_IsSeventyThreeDegreesVertical()
    {
        Assert.Equal(90f, RetailLens.DefaultFovDegrees);
        Assert.Equal(72.973f, RetailLens.VerticalFovDegrees(RetailLens.DefaultFovDegrees, 800f / 600f), 2);
    }

    [Fact]
    public void RetailLens_IsWiderThanTheOldSixtyDegrees_AndNarrowsForWideWindows()
    {
        Assert.True(RetailLens.VerticalFovDegrees(90f, 4f / 3f) > 60f);
        Assert.True(RetailLens.VerticalFovDegrees(90f, 16f / 9f) < RetailLens.VerticalFovDegrees(90f, 4f / 3f));
    }

    [Theory]
    [InlineData(0.1f)]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void RetailLens_AnUnusableAspect_GivesTheWidestAngle(float aspect) =>
        Assert.Equal(RetailLens.MaxVerticalDegrees, RetailLens.VerticalFovDegrees(90f, aspect));

    [Fact]
    public void RetailLens_ThePlayerSitsHigherInTheWindowThanAtSixtyDegrees()
    {
        // The default viewer is 0.75 m above the pivot and 2.5 m behind, looking level: a point 0.45 m below the camera's height at the player's distance (the head) is 10.2 degrees down.
        var down = MathF.Atan2(0.45f, 2.5f);
        float Y(float vertical) => 300f + (300f * MathF.Tan(down) / MathF.Tan(vertical * MathF.PI / 360f));
        Assert.True(Y(RetailLens.VerticalFovDegrees(90f, 4f / 3f)) < Y(60f));
    }

    private static CellFrame Player() => new(0x40400001u, new AcFrame(new Vector3(100f, 100f, 50f), Quaternion.Identity));

    [Fact]
    public void DefaultChaseCamera_IsRetails_TwoAndAHalfBehindThreeQuartersAboveAndLookingLevel()
    {
        var model = new ChaseCameraModel();
        model.SetDefaultOffsets();
        Assert.Equal(new Vector3(0f, -2.5f, 0.75f), model.ViewerOffset);
        Assert.Equal(new Vector3(0f, 0f, 1.5f), model.PivotOffset);
        Assert.NotEqual(CameraTargets.None, model.Targets & CameraTargets.LookInDirection);
        Assert.Equal(CameraTargets.None, model.Targets & CameraTargets.LookAtPivot);
        var pose = model.Update(1.0, Player(), null);
        Assert.Equal(50f + 1.5f + 0.75f, pose.Desired.Origin.Z, 3);
        Assert.Equal(97.5f, pose.Desired.Origin.Y, 3);
    }

    [Theory]
    [InlineData(1.8f)]
    [InlineData(1.2f)]
    public void FramedCameras_LookAtThePivot(float scale)
    {
        // An offset higher than 0.75 turns the camera to the pivot (CameraSet::SetTargetForOffset): the player is in the middle of the window, above the combat window, not low in it.
        var model = new ChaseCameraModel { Scale = scale };
        model.SetDefaultOffsets();
        Assert.NotEqual(CameraTargets.None, model.Targets & CameraTargets.LookAtPivot);
    }

    [Fact]
    public void HeadingOffset_CanLowerThePivot_AndTheDefaultsPutRetailsBack()
    {
        var model = new ChaseCameraModel();
        model.SetHeadingOffset(130f, 1.15f);
        Assert.Equal(1.15f, model.PivotOffset.Z);
        model.SetDefaultOffsets();
        Assert.Equal(ChaseCameraModel.DefaultPivotZ, model.PivotOffset.Z);
        model.SetHeadingOffset(130f);
        Assert.Equal(ChaseCameraModel.DefaultPivotZ, model.PivotOffset.Z);
    }

    [Fact]
    public void HuntFraming_MissileIsCloserThanMelee_AndTheDistancesFollowTheScale()
    {
        Assert.True(HuntUiShotPlan.MissileFramedScale < HuntUiShotPlan.FramedScale);
        Assert.Equal(HuntUiShotPlan.FramedMetres, HuntUiShotPlan.FramedMetresOf(HuntUiShotPlan.FramedScale));
        Assert.Equal(3.13f, HuntUiShotPlan.FramedMetresOf(HuntUiShotPlan.MissileFramedScale), 1);
        Assert.True(HuntUiShotPlan.CameraFramed(HuntUiShotPlan.FramedMetresOf(1.2f), false, 1.2f));
        Assert.False(HuntUiShotPlan.CameraFramed(HuntUiShotPlan.FramedMetresOf(1.2f), false, HuntUiShotPlan.FramedScale));
        Assert.False(HuntUiShotPlan.CameraFramed(HuntUiShotPlan.FramedMetresOf(1.2f), true, 1.2f));
        Assert.True(HuntUiShotPlan.MissileSideDegrees < 0f);
    }

    [Fact]
    public void PlayerFramed_NeedsTheBoxOnScreenWithItsMiddleAboveTheCombatWindow()
    {
        Assert.True(HuntUiShotPlan.PlayerFramed(new ScreenRect(380f, 230f, 60f, 160f), true, 400f));
        Assert.False(HuntUiShotPlan.PlayerFramed(new ScreenRect(380f, 330f, 60f, 160f), true, 400f)); // the live missile frame: the hunter low behind the window
        Assert.False(HuntUiShotPlan.PlayerFramed(new ScreenRect(380f, -20f, 60f, 100f), true, 400f)); // the head off the top
        Assert.False(HuntUiShotPlan.PlayerFramed(new ScreenRect(380f, 230f, 60f, 160f), false, 400f));
    }

    [Fact]
    public void LookCamera_TheCloseViewLowersThePivot_TheHuntViewIsTheHuntsOwn()
    {
        var close = LookShotsPlan.CameraFor(LookView.Close, LookKind.Caster);
        Assert.Equal(new LookCamera(LookShotsPlan.CameraScale, LookShotsPlan.CameraHeadingDegrees, LookShotsPlan.ClosePivotZ), close);
        Assert.True(close.PivotZ < ChaseCameraModel.DefaultPivotZ);
        var bow = LookShotsPlan.CameraFor(LookView.Hunt, LookKind.Launcher);
        Assert.Equal(HuntUiShotPlan.MissileFramedScale, bow.Scale);
        Assert.Equal(HuntUiShotPlan.MissileSideDegrees, bow.HeadingDegrees);
        var mace = LookShotsPlan.CameraFor(LookView.Hunt, LookKind.Melee);
        Assert.Equal(HuntUiShotPlan.FramedScale, mace.Scale);
        Assert.Equal(0f, mace.HeadingDegrees);
        Assert.True(LookShotsPlan.FollowsBody(LookView.Close));
        Assert.False(LookShotsPlan.FollowsBody(LookView.Hunt));
    }

    [Theory]
    [InlineData(130f, 0f, 130f)]
    [InlineData(130f, 83f, -147f)] // the bow stance turns the body 83 degrees: the camera goes round with it
    [InlineData(130f, -84.7f, 45.3f)]
    [InlineData(130f, 180f, -50f)] // a stance that turns the body to face away (the live magic stance, 4.1 section 8): the camera goes round to its front
    public void AimDegrees_FollowsTheBodyTurn(float heading, float turn, float expected) =>
        Assert.Equal(expected, LookShotsPlan.AimDegrees(heading, turn), 1);

    [Fact]
    public void ParseView_ReadsTheOptionValues()
    {
        Assert.Equal(LookView.Close, LookShotsPlan.ParseView("CLOSE"));
        Assert.Equal(LookView.Hunt, LookShotsPlan.ParseView("hunt"));
        Assert.Null(LookShotsPlan.ParseView("far"));
        Assert.Equal(LookView.Close, PlayOptions.Parse([]).LookShotsView);
        Assert.Equal(LookView.Hunt, PlayOptions.Parse(["--play-look-view=hunt"]).LookShotsView);
    }

    [Fact]
    public void AvatarFacing_HeadingAndTurn()
    {
        Assert.Equal(0f, AvatarFacing.HeadingDegrees(Quaternion.Identity), 3);
        var quarter = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, -MathF.PI / 2f); // AC headings run clockwise: a clockwise quarter turn faces +X (east, 90)
        Assert.Equal(90f, AvatarFacing.HeadingDegrees(quarter), 2);
        Assert.Equal(90f, AvatarFacing.TurnDegrees(Quaternion.Identity, quarter), 2);
        Assert.Equal(-90f, AvatarFacing.TurnDegrees(quarter, Quaternion.Identity), 2);
        var back = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI);
        Assert.Equal(180f, MathF.Abs(AvatarFacing.TurnDegrees(Quaternion.Identity, back)), 2);
        Assert.Equal(0f, AvatarFacing.TurnDegrees(back, back), 3);
        Assert.Equal(-170f, AvatarFacing.Wrap(190f), 3);
        Assert.Equal(180f, AvatarFacing.Wrap(-180f), 3);
    }
}
