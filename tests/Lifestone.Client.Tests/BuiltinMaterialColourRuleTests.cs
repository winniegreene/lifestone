using System.Text.RegularExpressions;

namespace Lifestone.Client.Tests;

/// <summary>
/// Guards docs/render-contract.md §2.3's rule for built-in Godot materials (render-fidelity review fix). Godot sRGB-decodes
/// the colours of its own 3D materials (Label3D / Sprite3D modulate, StandardMaterial3D albedo, ...); in byte space the
/// tonemap's decode LUT decodes the frame once more, so such a colour must go through
/// <c>ByteSpaceBlending.BuiltinColor</c> or it shows darker and more saturated (the spell viewer's words did). This is a
/// source check: the engine behaviour itself is pinned by <c>--blend-checks</c>.
/// </summary>
public sealed partial class BuiltinMaterialColourRuleTests
{
    /// <summary>Godot's built-in 3D materials, and the nodes that create one from a colour property.</summary>
    [GeneratedRegex(@"\bnew\s+(Label3D|Sprite3D|AnimatedSprite3D|StandardMaterial3D|ORMMaterial3D)\b")]
    private static partial Regex BuiltinInstantiation();

    [GeneratedRegex(@"type=""(Label3D|Sprite3D|AnimatedSprite3D|StandardMaterial3D|ORMMaterial3D)""")]
    private static partial Regex BuiltinInScene();

    [Fact]
    public void Every_script_that_creates_a_builtin_3d_material_routes_its_colours_through_BuiltinColor()
    {
        var scripts = Path.Combine(RepositoryRoot(), "src", "Lifestone.Game", "scripts");
        var offenders = new List<string>();
        var users = 0;
        foreach (var file in Directory.EnumerateFiles(scripts, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (!BuiltinInstantiation().IsMatch(text))
            {
                continue;
            }

            users++;
            if (!text.Contains("ByteSpaceBlending.BuiltinColor", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetRelativePath(scripts, file));
            }
        }

        Assert.True(users > 0, "expected at least the spell viewer's Label3D and the blend checks' probes");
        Assert.True(offenders.Count == 0, "built-in 3D materials whose colours skip ByteSpaceBlending.BuiltinColor (render-contract §2.3): " + string.Join(", ", offenders));
    }

    [Fact]
    public void No_scene_or_resource_file_declares_a_builtin_3d_material()
    {
        // A .tscn / .tres colour cannot be pre-encoded for the blend space selected at run time: create such nodes in code.
        var game = Path.Combine(RepositoryRoot(), "src", "Lifestone.Game");
        var offenders = Directory.EnumerateFiles(game, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + ".godot" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(f => BuiltinInScene().IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetRelativePath(game, f))
            .ToList();
        Assert.True(offenders.Count == 0, "built-in 3D materials declared in scene files (render-contract §2.3): " + string.Join(", ", offenders));
    }

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Lifestone.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Lifestone.slnx not found above " + AppContext.BaseDirectory);
    }
}
