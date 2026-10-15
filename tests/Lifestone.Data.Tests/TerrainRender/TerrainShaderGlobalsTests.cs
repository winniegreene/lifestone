using System.Text.RegularExpressions;
using Lifestone.Client.Tests;

namespace Lifestone.Data.Tests.TerrainRender;

/// <summary>
/// The terrain shader reads its lighting and fog from the render contract's <em>global</em> uniforms
/// (docs/render-contract.md §1), which <c>RenderGlobals</c> drives for every Lifestone shader. A global the project does
/// not declare, or declares with another type, fails the shader compile at run time (the golden test would then fail
/// with large differences); this pins it at test time.
/// </summary>
public sealed partial class TerrainShaderGlobalsTests
{
    /// <summary>The contract's terrain inputs (render-contract.md §1, §5): sun, landscape ambient, fog, mip cap.</summary>
    private static readonly string[] TerrainGlobals =
        ["ac_sun_dir", "ac_sun_color", "ac_land_ambient", "ac_fog_color", "ac_fog_start", "ac_fog_end", "ac_fog_enabled", "ac_max_mip_level"];

    [Fact]
    public void Terrain_lighting_and_fog_are_the_project_s_global_uniforms_with_matching_types()
    {
        var shader = Code(File.ReadAllText(RepoFile("src", "Lifestone.Game", "shaders", "terrain", "terrain.gdshader")));
        var project = File.ReadAllText(RepoFile("src", "Lifestone.Game", "project.godot"));

        var declared = ProjectGlobals().Matches(project).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value);
        var used = ShaderGlobals().Matches(shader).ToDictionary(m => m.Groups[2].Value, m => m.Groups[1].Value);

        foreach (var name in TerrainGlobals)
        {
            Assert.True(used.ContainsKey(name), $"terrain.gdshader must read '{name}' as a global uniform");
        }

        foreach (var (name, type) in used)
        {
            Assert.True(declared.TryGetValue(name, out var projectType), $"'{name}' is not in project.godot [shader_globals]");
            Assert.Equal(projectType, type);
        }

        // No material-uniform fallback for the contract's inputs: one driver (RenderGlobals) lights everything.
        Assert.DoesNotMatch(new Regex(@"(?<!global\s)\buniform\s+\w+\s+ac_\w+"), shader);
        Assert.DoesNotContain("AC_GLOBAL_UNIFORMS", shader, StringComparison.Ordinal);
    }

    [Fact]
    public void Terrain_uses_the_landscape_ambient_not_the_object_ambient()
    {
        // CLandBlockStruct::calc_lighting lights terrain with ambient_color x ambient_level; ac_ambient is the object
        // ambient ambient_color x (ambient_level + 0.2 |sunlight|) (LScape::calc_object_light), brighter by design.
        var shader = Code(File.ReadAllText(RepoFile("src", "Lifestone.Game", "shaders", "terrain", "terrain.gdshader")));
        Assert.Matches(new Regex(@"ac_sun_color\s*\*\s*d\s*\+\s*ac_land_ambient"), shader);
        Assert.DoesNotMatch(new Regex(@"\bac_ambient\b"), shader);
    }

    /// <summary>The source without comment lines and trailing comments.</summary>
    private static string Code(string source) =>
        string.Join('\n', source.Split('\n').Select(l => l.Contains("//", StringComparison.Ordinal) ? l[..l.IndexOf("//", StringComparison.Ordinal)] : l));

    private static string RepoFile(params string[] parts) => RepositoryFiles.PathOf(parts);

    [GeneratedRegex(@"(?m)^(\w+)=\{\s*""type"":\s*""(\w+)""")]
    private static partial Regex ProjectGlobals();

    [GeneratedRegex(@"\bglobal\s+uniform\s+(\w+)\s+(\w+)\s*;")]
    private static partial Regex ShaderGlobals();
}
