using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Session;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M4 1.2 s3: a stand-in session with a mace, a bow and arrows in the pack, for the melee and missile tests. The player's sends go through the responder's session (the face the Action API will use), so the tests drive
/// the three attack sends as s2's Action API will.
/// </summary>
internal sealed class StandInAttackRig : IDisposable
{
    public const uint Mace = 200;
    public const uint Bow = 201;
    public const uint Arrows = 202;
    public const uint Target = 0x80000D01;

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

    /// <summary>Places a Tusker Slave <paramref name="metres"/> in front of the player; a tusker with 1 health dies to the first blow that lands.</summary>
    public void Place(double metres, uint health = 100_000, bool attacks = false)
    {
        Assert.NotNull(Server.PlaceNear(StandInTusker.Make(Target, default, maxHealth: health, attacks: attacks), new Vector3(0f, (float)metres, 0f)));
        Run(0.5);
        Assert.True(Cast.Session.State.Objects.Contains(Target));
    }

    /// <summary>The player selects <paramref name="guid"/> (a QueryHealth, ACE's selection; 0 clears it) and the answer is run out.</summary>
    public void Select(uint guid)
    {
        Net.SendQueryHealth(guid);
        Run(0.5);
    }

    /// <summary>The melee kit: the mace wielded, the Melee mode, a target.</summary>
    public void MeleeKit(double metres, uint health = 100_000, bool attacks = false, bool select = true)
    {
        Wield(Spawn(Mace), EquipMask.MeleeWeapon);
        Mode(CombatMode.Melee);
        Place(metres, health, attacks);
        World.PlayerHitRate = 1.0;
        if (select)
        {
            Select(Target);
        }

        Cast.Begin();
    }

    /// <summary>The missile kit: the bow and <paramref name="arrows"/> arrows wielded, the Missile mode, a target.</summary>
    public void MissileKit(double metres, int arrows = 5, uint health = 100_000, bool select = true)
    {
        Wield(Spawn(Bow), EquipMask.MissileWeapon);
        Wield(Spawn(Arrows, arrows), EquipMask.MissileAmmo);
        Mode(CombatMode.Missile);
        Place(metres, health);
        World.PlayerHitRate = 1.0;
        if (select)
        {
            Select(Target);
        }

        Cast.Begin();
    }

    /// <summary>The answers up to and including the first <paramref name="tag"/>, from the first of <paramref name="from"/>.</summary>
    public string[] Until(string tag, int from = 0)
    {
        var tags = Tags;
        var end = Array.IndexOf(tags, tag, from);
        Assert.True(end >= 0, "no " + tag + " in " + string.Join(" ", tags));
        return tags[from..(end + 1)];
    }

    public void Dispose() => Cast.Dispose();
}
