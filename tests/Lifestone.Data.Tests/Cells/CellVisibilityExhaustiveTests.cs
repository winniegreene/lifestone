using System.Collections.Concurrent;
using System.Diagnostics;
using Lifestone.Data.World.Cells;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Cells;

/// <summary>
/// Completeness of the indoor portal walk against the ray oracle (<see cref="PortalRayTracer"/>), over a large sample of
/// viewer cells in every landblock that has cells: every cell a ray can enter must be in the walk's result, and a ray
/// that leaves through an outside portal means the walk must report the outside as visible.
/// </summary>
[Trait("Tier", "Exhaustive")]
public sealed class CellVisibilityExhaustiveTests(CellsFixture fixture, ITestOutputHelper output) : IClassFixture<CellsFixture>
{
    [RequiresDatsFact]
    public void Walks_reach_every_cell_a_ray_reaches_in_all_directions() =>
        RayCheck(every: 4, phase: 0, withFrustum: false, rays: 400, minimumWalks: 130_000);

    [RequiresDatsFact]
    public void Walks_reach_every_cell_a_ray_reaches_inside_random_camera_frusta() =>
        RayCheck(every: 4, phase: 2, withFrustum: true, rays: 160, minimumWalks: 130_000);

    private void RayCheck(int every, int phase, bool withFrustum, int rays, int minimumWalks)
    {
        var timer = Stopwatch.StartNew();
        var walks = 0;
        var rayCells = 0L;
        var walkCells = 0L;
        var outsideByRays = 0;
        var saturated = 0L;
        var truncated = 0;
        var failures = new ConcurrentQueue<string>();
        Parallel.ForEach(CellDiscovery.LandblocksWithInfo(fixture.Dats), coord =>
        {
            var ids = CellDiscovery.EnumerateEnvCellIds(fixture.Dats, coord);
            if (ids.Count == 0)
            {
                return;
            }

            var set = LandblockCellSet.Load(fixture.Structures, coord);
            var tracer = new PortalRayTracer(set);
            for (var i = (phase + coord.X + coord.Y) % every; i < ids.Count; i += every)
            {
                var cell = set.GetCell(ids[i])!;
                if (PortalWalkOracle.EyeIn(set, cell) is not { } eye)
                {
                    continue;
                }

                var rng = new Random((int)cell.Id ^ 0x5151);
                var frustum = withFrustum ? CameraFrustum.Random(eye, rng) : null;
                var result = PortalWalkOracle.Check(set, tracer, cell, eye, frustum, rays, rng);
                Interlocked.Increment(ref walks);
                Interlocked.Add(ref rayCells, result.RayCells);
                Interlocked.Add(ref walkCells, result.Walk.Cells.Count);
                Interlocked.Add(ref saturated, result.Walk.SaturatedCells);
                if (result.Walk.Truncated)
                {
                    Interlocked.Increment(ref truncated);
                }

                if (result.RaySawOutside)
                {
                    Interlocked.Increment(ref outsideByRays);
                }

                if (result.Missed.Length > 0 || result.MissedOutside)
                {
                    failures.Enqueue(
                        $"0x{cell.Id:X8} eye ({eye.X:F3}, {eye.Y:F3}, {eye.Z:F3}){(frustum is null ? string.Empty : $" looking ({frustum.Forward.X:F3}, {frustum.Forward.Y:F3}, {frustum.Forward.Z:F3})")}: " +
                        $"walk missed {string.Join(" ", result.Missed.Select(id => $"0x{id:X8}"))}{(result.MissedOutside ? " and the outside" : string.Empty)}");
                }
            }
        });

        output.WriteLine($"{walks} walks ({rays} rays each{(withFrustum ? ", random frusta" : string.Empty)}) in {timer.Elapsed.TotalSeconds:F1} s: " +
                         $"rays entered {rayCells} cells, walks listed {walkCells}; rays saw outside from {outsideByRays} viewers; " +
                         $"saturated cells {saturated}, truncated walks {truncated}; failures {failures.Count}");
        Assert.True(failures.IsEmpty, string.Join(Environment.NewLine, failures.Take(20)));
        Assert.True(walks >= minimumWalks, $"only {walks} walks");
        Assert.Equal(0, truncated);
    }
}
