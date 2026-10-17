using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests.Repository;

/// <summary>
/// The projectile draw path allocates nothing per object (M3 3.2, package 2 review finding 12). <c>ServerObjectsLayer.DrawObject</c> runs for every avatar in every frame, the frames
/// <c>cast_frame_max_ms</c> and <c>fight_frame_max_ms</c> measure, so a log line in it is allowed only on a transition (here the first projectile shown), never once per object: a
/// <c>Log.Info</c> call builds a params array, boxes its fields and formats a guid. <c>ProjectileWarmUp.Poll</c> runs every frame until the warm-up is done and polls the meshes every
/// ten frames; it builds no mesh key of its own (the keys are made once, in the worker's plan). Lifestone.Game has no unit tests of its own, so the sources are pinned as text, with
/// comments and literals blanked (<see cref="CheckoutFiles.ReadCSharpCode"/>).
/// </summary>
public sealed partial class ProjectileDrawPathTests
{
    private const string LayerFile = "src/Lifestone.Game/scripts/World/ServerObjects/ServerObjectsLayer.cs";
    private const string WarmUpFile = "src/Lifestone.Game/scripts/World/ServerObjects/ProjectileWarmUp.cs";

    /// <summary>A one-argument <c>Warm(x)</c> call: the overload that builds the mesh key itself.</summary>
    [GeneratedRegex(@"\.Warm\(\s*[^,()]+\s*\)")]
    private static partial Regex WarmWithoutKey();

    /// <summary>A two-argument <c>Warm(x, key)</c> call: the overload that takes a key made beforehand.</summary>
    [GeneratedRegex(@"\.Warm\(\s*[^,()]+,\s*[^,()]+\s*\)")]
    private static partial Regex WarmWithKey();

    [Fact]
    public void The_draw_path_logs_the_first_projectile_shown_only_never_one_line_per_object()
    {
        var body = MethodBody(CheckoutFiles.ReadCSharpCode(LayerFile), "private void DrawObject(");

        Assert.Equal(1, Occurrences(body, "Log.Info("));
        var at = body.IndexOf("Log.Info(", StringComparison.Ordinal);
        var guard = body.LastIndexOf("if (ProjectilesShown == 1)", at, StringComparison.Ordinal);
        Assert.True(guard >= 0, "the line is not under the first-projectile guard");
        var between = body[guard..at];
        Assert.Equal(1, Occurrences(between, "{")); // inside the guard's own block ...
        Assert.DoesNotContain("}", between, StringComparison.Ordinal); // ... not after it
    }

    [Fact]
    public void The_warm_up_poll_builds_no_mesh_key_of_its_own()
    {
        var body = MethodBody(CheckoutFiles.ReadCSharpCode(WarmUpFile), "public void Poll(");

        Assert.DoesNotContain("ActorMeshLibrary.Key(", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(WarmWithoutKey(), body);
        Assert.Matches(WarmWithKey(), body);
    }

    /// <summary>The text of a method from its signature to its closing brace (methods are indented four spaces inside the class).</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, signature);
        var end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        Assert.True(end > start, signature);
        return source[start..end];
    }

    private static int Occurrences(string text, string part)
    {
        var count = 0;
        for (var at = text.IndexOf(part, StringComparison.Ordinal); at >= 0; at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
