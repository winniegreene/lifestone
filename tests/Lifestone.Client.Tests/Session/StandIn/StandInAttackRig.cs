using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 1.2 s3: a stand-in session with a mace, a bow and arrows in the pack, for the melee, missile, container and item-move tests. The player's sends go through the responder's session (the face the Action API will use), so the tests
/// drive the three attack sends as s2's Action API will. M4 4.1 t2 (D23, D26): the stand-in holds no creature, so the rig places none; a target is a statue (a non-creature placeholder) or a guid the stand-in does not hold.
/// </summary>
internal sealed class StandInAttackRig : IDisposable
{
    public const uint Mace = 200;
    public const uint Bow = 201;
    public const uint Arrows = 202;
    public const uint Statue = 0x80000D01;

    /// <summary>A guid the stand-in does not hold.</summary>
    public const uint Missing = 0x80009999;

    public StandInAttackRig()
    {
        var visit = new StandInVisit
        {
            EnterAfter = 0.2,
            UnhideAfter = 1.0,
            Stats = new StandInPlayerStats { Health = 1000, Level = 30 },
            Items = new Dictionary<uint, StandInItemKind>
            {
                [Mace] = new("Mace", null),
                [Bow] = new("Short Bow", null),
                [Arrows] = new("Arrow", 250),
            },
        };
        Cast = new CastRig(visit);
        Server = StandInSessions.Server(Cast.Session.Reducer);
    }

    public CastRig Cast { get; }

    public StandInServer Server { get; }

    public StandInWorld World => Cast.World;

    public IAcSession Net => Cast.Session.Reducer.LocalResponder!.Session;

    public string[] Tags => Cast.Tags;

    public void Run(double seconds) => Cast.Run(seconds);

    public uint Spawn(uint wcid, int? stack = null)
    {
        var spawn = Cast.Session.Tools.SpawnItem(wcid, stack, ActionSource.Host);
        Run(1.5);
        Assert.True(spawn.IsCompleted, spawn.Summary);
        return spawn.Result!.Guid;
    }

    public ActionHandle Wield(uint item, EquipMask mask)
    {
        var wield = Cast.Session.Actions.Wield(item, mask, ActionSource.Host);
        Run(1.5);
        return wield;
    }

    public void Mode(CombatMode mode) => Cast.Mode(mode);

    /// <summary>Places a statue (a non-creature placeholder object) <paramref name="metres"/> in front of the player.</summary>
    public void PlaceStatue(double metres)
    {
        Assert.NotNull(Server.PlaceNear(new StandInObject(Statue, "Stand-in Statue", default, MotionTable: 0), new Vector3(0f, (float)metres, 0f)));
        Run(0.5);
        Assert.True(Cast.Session.State.Objects.Contains(Statue));
    }

    /// <summary>The melee kit: the mace wielded, the Melee mode.</summary>
    public void MeleeKit()
    {
        Wield(Spawn(Mace), EquipMask.MeleeWeapon);
        Mode(CombatMode.Melee);
        Cast.Begin();
    }

    /// <summary>The missile kit: the bow and <paramref name="arrows"/> arrows wielded, the Missile mode.</summary>
    public void MissileKit(int arrows = 5)
    {
        Wield(Spawn(Bow), EquipMask.MissileWeapon);
        Wield(Spawn(Arrows, arrows), EquipMask.MissileAmmo);
        Mode(CombatMode.Missile);
        Cast.Begin();
    }

    public void Dispose() => Cast.Dispose();
}
