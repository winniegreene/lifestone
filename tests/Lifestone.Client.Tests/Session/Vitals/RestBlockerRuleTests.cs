using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Vitals;
using Lifestone.Data.Animation;
using Lifestone.Data.World;
using Lifestone.Net.Messages;
using Lifestone.Net.Types;
using Xunit;

namespace Lifestone.Client.Tests.Session.Vitals;

/// <summary>M6 7 AT (live, 2026-11-06: a Drudge Skulker 17 m from the home point held her standing two rests of about three minutes): a creature that can attack her within the fight's reach is shot first; one beyond it keeps the wait; a town NPC blocks nothing.</summary>
public sealed class RestBlockerRuleTests
{
    private const uint Den = 0x01050000;
    private static readonly CellFrame Centre = At(0, 0);

    [Fact]
    public void An_attackable_creature_within_the_fights_reach_is_shot_first_and_one_beyond_it_keeps_the_wait()
    {
        var table = new WorldObjectTable();
        table.Set(Make(1, "Scrivener of Item Magic", 0, 3)); // a town NPC: no blocker whatever its distance
        Assert.Equal(RestBlock.None, RestBlockerRule.Read(table, Centre, 0, VitalsKeeper.QuietMetres, 20f, out var guid));
        Assert.Equal(0u, guid);

        table.Set(Make(2, "Drudge Skulker", WorldObjectTable.AttackableFlag, 22)); // inside the quiet 24 m, past the reach of 20 m
        Assert.Equal(RestBlock.Wait, RestBlockerRule.Read(table, Centre, 0, VitalsKeeper.QuietMetres, 20f, out guid));
        Assert.Equal(2u, guid);

        table.Set(Make(3, "Drudge Skulker", WorldObjectTable.AttackableFlag, 17)); // the live creature, 16.8 m
        Assert.Equal(RestBlock.ShootFirst, RestBlockerRule.Read(table, Centre, 0, VitalsKeeper.QuietMetres, 20f, out guid));
        Assert.Equal(3u, guid);
    }

    [Fact]
    public void With_no_reach_given_every_blocker_is_the_old_wait()
    {
        var table = new WorldObjectTable();
        table.Set(Make(3, "Drudge Skulker", WorldObjectTable.AttackableFlag, 5));

        Assert.Equal(RestBlock.Wait, RestBlockerRule.Read(table, Centre, 0, VitalsKeeper.QuietMetres, 0f, out var guid));
        Assert.Equal(3u, guid);
        Assert.Equal(RestBlock.None, RestBlockerRule.Read(table, Centre, 3, VitalsKeeper.QuietMetres, 20f, out _)); // she herself is skipped
    }

    private static CellFrame At(float x, float y) => new(Den | 0x0303, new AcFrame(new Vector3(x, y, 0f), Quaternion.Identity));

    private static WorldObject Make(uint guid, string name, uint flags, float x)
    {
        var weenie = new WeenieDesc { Header = WeenieHeaderFlags.None, Name = name, WeenieClassId = 100, IconId = 0x06000001, ItemType = 0x10, ObjectDescriptionFlags = flags };
        var physics = new PhysicsDesc { Flags = PhysicsDescFlags.None, State = PhysicsState.None, Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1) };
        return new WorldObject(new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isSelf: false, createdAt: 0) { Position = At(x, 0) };
    }
}
