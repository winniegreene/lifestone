using Lifestone.Data.Animation;
using Lifestone.Data.Effects;
using Xunit.Abstractions;

namespace Lifestone.Data.Tests.Effects;

/// <summary>
/// M3 4.1 s0: the one PhysicsScript of the end-of-retail portal dat that holds a SetLight hook (<c>SetLightHook::Execute</c> 11.6096 0x00527BD0,
/// <c>CPhysicsObj::set_lights</c> 0x005107C0), pinned by id and by what it does to the lights.
/// </summary>
public sealed class SetLightScriptPinTests(DatSetFixture fixture, ITestOutputHelper output) : IClassFixture<DatSetFixture>
{
    [RequiresDatsFact]
    public void Exactly_one_physics_script_holds_a_SetLight_hook()
    {
        var source = new DatEffectSource(fixture.Dats);
        var found = new List<(uint Id, double Time, bool LightsOn, int Hooks)>();
        foreach (var id in source.AllScriptIds())
        {
            var script = source.GetScript(id)!;
            foreach (var entry in script.Hooks)
            {
                if (entry.Hook is SetLightHook h)
                {
                    found.Add((id, entry.StartTime, h.LightsOn, script.Hooks.Count));
                }
            }
        }

        foreach (var f in found)
        {
            output.WriteLine($"SetLight in 0x{f.Id:X8} at {f.Time}: LightsOn={f.LightsOn}, {f.Hooks} hooks");
        }

        // 0x33000109 switches the lights OFF at time 0 (LightsOn false), with one other hook; no script switches them on.
        var only = Assert.Single(found);
        Assert.Equal(0x33000109u, only.Id);
        Assert.Equal(0.0, only.Time);
        Assert.False(only.LightsOn);
        Assert.Equal(2, only.Hooks);
    }
}
