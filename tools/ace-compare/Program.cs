using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using ACE.DatLoader;
using ACE.Server.Physics;
using ACE.Server.Physics.Animation;
using ACE.Server.Physics.Common;

namespace Lifestone.AceCompare;

/// <summary>Runs the world walk's hard moves through ACE's physics. Arguments: the ACE build's folder, the dats' folder, the moves run's results.jsonl, the output folder.</summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length != 4)
        {
            Console.Error.WriteLine("usage: AceCompare <ace build dir> <dat dir> <moves results.jsonl> <out dir>");
            return 2;
        }

        // ACE's own dependencies live beside its build, not beside this tool; the handler must be in place before a method that names an ACE type is compiled.
        var aceDir = args[0];
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(aceDir, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };

        return Comparison.Run(args[1], args[2], args[3]);
    }
}

/// <summary>The comparison: each move placed and stepped through ACE, judged against what Lifestone did.</summary>
internal static class Comparison
{
    /// <summary>The player's Setup.</summary>
    private const uint HumanSetup = 0x02000001;

    /// <summary>How far apart two ends may be and still be the same, in metres. Not retail: the comparison's own; well above float noise, well below anything a walk would notice.</summary>
    private const float SameMetres = 0.05f;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run(string datDir, string movesFile, string outDir)
    {
        // ACE reads the dats' strings in code page 1252, which .NET does not carry by default (the server registers it at start).
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        DatManager.Initialize(datDir, keepOpen: true, loadCell: true);
        _ = new PhysicsEngine(new ObjectMaint(), new SmartBox());
        Directory.CreateDirectory(outDir);
        using var differences = new StreamWriter(Path.Combine(outDir, "differences.jsonl"));
        var tally = new Dictionary<(string Kind, string Verdict), int>();
        // Not a player id: ACE then skips the housing-barrier check, which needs a live server object and which our world does not enforce either (docs/world-walk.md, limits).
        var nextId = 0x80000100u;
        foreach (var line in File.ReadLines(movesFile))
        {
            using var row = JsonDocument.Parse(line);
            var id = row.RootElement.GetProperty("id").GetString()!;
            if (!row.RootElement.TryGetProperty("moves", out var moves))
            {
                continue;
            }

            foreach (var move in moves.EnumerateArray())
            {
                var kind = move.GetProperty("kind").GetString()!;
                string verdict;
                string detail;
                try
                {
                    (verdict, detail) = Judge(move, nextId++);
                }
                catch (Exception e)
                {
                    verdict = "ace-threw:" + e.GetType().Name;
                    detail = e.ToString();
                }

                tally[(kind, verdict)] = tally.GetValueOrDefault((kind, verdict)) + 1;
                if (verdict != "same")
                {
                    differences.WriteLine(JsonSerializer.Serialize(new { id, kind, verdict, detail, move }));
                }
            }
        }

        foreach (var ((kind, verdict), count) in tally.OrderBy(t => t.Key.Kind, StringComparer.Ordinal).ThenByDescending(t => t.Value))
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{count,8} {kind,-16} {verdict}"));
        }

        return 0;
    }

    /// <summary>ACE's sliding placement (its enter_world) without the check at the top of its SetPosition that refuses a body with no server-side game object: a physics-only body has none, and the check decides nothing about the ground.</summary>
    private static SetPositionError Place(PhysicsObj body, Position at)
    {
        var transition = Transition.MakeTransition();
        transition.InitObject(body, ObjectInfoState.Default);
        transition.InitSphere(body.PartArray.GetNumSphere(), body.PartArray.GetSphere(), body.Scale);
        body.entering_world = true;
        body.store_position(at);
        var result = body.SetPositionInternal(at, new SetPosition { Pos = at, Flags = SetPositionFlags.Placement | SetPositionFlags.Slide }, transition);
        transition.CleanupTransition();
        return result;
    }

    private static float F(JsonElement e, string name) => e.GetProperty(name).GetSingle();

    private static (string Verdict, string Detail) Judge(JsonElement move, uint guid)
    {
        var body = PhysicsObj.makeObject(HumanSetup, guid, true);
        try
        {
            return JudgeWith(body, move);
        }
        finally
        {
            // A body left in the world is an obstacle to every later move near it (and the moves come in pairs at the same doorway).
            body.leave_cell(false);
            body.remove_shadows_from_cells();
        }
    }

    private static (string Verdict, string Detail) JudgeWith(PhysicsObj body, JsonElement move)
    {
        var at = new Vector3(F(move, "x"), F(move, "y"), F(move, "z"));
        var placing = new Position(move.GetProperty("cell").GetUInt32(), new AFrame(at, Quaternion.Identity));
        var placedAs = Place(body, placing);
        var placed = placedAs == SetPositionError.OK;
        var ourPlaced = move.GetProperty("placed").GetBoolean();
        if (placed != ourPlaced)
        {
            return (ourPlaced ? "only-ours-placed" : "only-ace-placed", placedAs.ToString());
        }

        if (!placed)
        {
            return ("same", string.Empty);
        }

        var ourStart = new Vector3(F(move, "sx"), F(move, "sy"), F(move, "sz"));
        var ourStartCell = move.GetProperty("start_cell").GetUInt32();
        var start = body.Position;
        if (start.ObjCellID != ourStartCell || Vector3.Distance(start.Frame.Origin, ourStart) > SameMetres)
        {
            return ("placed-elsewhere", string.Create(CultureInfo.InvariantCulture, $"ace {start.ObjCellID:X8} {start.Frame.Origin} ours {ourStartCell:X8} {ourStart}"));
        }

        var step = new Vector3(F(move, "dx"), F(move, "dy"), 0f) * 1.4f;
        var from = new Position(start);
        var to = new Position(from.ObjCellID, new AFrame(from.Frame.Origin + step, from.Frame.Orientation));
        var result = body.transition(from, to, false);
        var ourOk = move.GetProperty("ok").GetBoolean();
        if ((result is not null) != ourOk)
        {
            return (ourOk ? "only-ours-moved" : "only-ace-moved", string.Empty);
        }

        if (result is null)
        {
            return ("same", string.Empty);
        }

        var end = result.SpherePath.CurPos;
        var ourEnd = new Vector3(F(move, "ex"), F(move, "ey"), F(move, "ez"));
        var ourEndCell = move.GetProperty("end_cell").GetUInt32();
        var delta = end.Frame.Origin - ourEnd;
        var ground = result.CollisionInfo.ContactPlaneValid && result.CollisionInfo.ContactPlane.Normal.Z >= PhysicsGlobals.FloorZ;
        var text = string.Create(CultureInfo.InvariantCulture, $"ace {end.ObjCellID:X8} {end.Frame.Origin} ground {ground}; ours {ourEndCell:X8} {ourEnd} ground {move.GetProperty("ground").GetBoolean()}; ace hit env {result.CollisionInfo.CollidedWithEnvironment} normal {(result.CollisionInfo.CollisionNormalValid ? result.CollisionInfo.CollisionNormal.ToString() : "none")} sliding {(result.CollisionInfo.SlidingNormalValid ? result.CollisionInfo.SlidingNormal.ToString() : "none")}");
        if (end.ObjCellID != ourEndCell)
        {
            return (delta.Length() <= SameMetres ? "other-cell-same-place" : "other-cell-other-place", text);
        }

        if (delta.Length() > SameMetres)
        {
            return (MathF.Abs(delta.Z) > SameMetres && new Vector2(delta.X, delta.Y).Length() <= SameMetres ? "other-height" : "other-place", text);
        }

        return (ground != move.GetProperty("ground").GetBoolean() ? "other-ground-state" : "same", text);
    }
}
