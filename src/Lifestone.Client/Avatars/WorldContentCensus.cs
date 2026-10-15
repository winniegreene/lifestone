using System.Globalization;
using Lifestone.Client.Session.Contracts;
using Lifestone.Net.Types;

namespace Lifestone.Client.Avatars;

/// <summary>What kind of thing a server object is, for the census (<see cref="WorldContentCensus"/>).</summary>
public enum WorldContentKind
{
    /// <summary>A creature that cannot be attacked: a quest giver, a vendor, a trainer.</summary>
    Npc,

    /// <summary>A creature that can be attacked: a monster, an animal, a training target.</summary>
    Creature,

    /// <summary>Another player (never in a single-player world; counted so the census says so).</summary>
    Player,

    /// <summary>A portal (<c>BF_PORTAL</c>).</summary>
    Portal,

    /// <summary>A door (<c>BF_DOOR</c>).</summary>
    Door,

    /// <summary>A lifestone (<c>BF_LIFESTONE</c>) or a bind stone.</summary>
    Lifestone,

    /// <summary>A sign: a fixed readable object (see <see cref="WorldContentCensus.Classify"/> for how it is told).</summary>
    Sign,

    /// <summary>A corpse.</summary>
    Corpse,

    /// <summary>Any other object with an item type: loose items, weapons, containers, chests.</summary>
    Item,

    /// <summary>Anything else (scenery objects the server places, statues, effects).</summary>
    Other,
}

/// <summary>The census of a world: how many objects of each kind the server has placed in it.</summary>
public readonly record struct WorldContent(int Npcs, int Creatures, int Players, int Portals, int Doors, int Lifestones, int Signs, int Corpses, int Items, int Others)
{
    /// <summary>Every object counted.</summary>
    public int Total => Npcs + Creatures + Players + Portals + Doors + Lifestones + Signs + Corpses + Items + Others;

    /// <summary>One line: <c>npcs=2 creatures=5 portals=1 ...</c> (zero kinds included, so the line always has the same shape).</summary>
    public string Describe() => string.Create(
        CultureInfo.InvariantCulture,
        $"npcs={Npcs} creatures={Creatures} players={Players} portals={Portals} doors={Doors} lifestones={Lifestones} signs={Signs} corpses={Corpses} items={Items} others={Others}");
}

/// <summary>
/// The census of the world content the server creates (m2-plan G5 W10): NPCs, creatures, portals, doors, signs and lifestones, from the objects the session
/// holds. It reads each object's weenie description only (the ItemType and the ObjectDescriptionFlags bitfield of its CreateObject), so it counts what the
/// server sent whether or not the avatars could draw it; the login check reports it and compares it with what is drawn.
/// </summary>
/// <remarks>
/// <para><b>What is counted.</b> Objects that are in the world (a position or a parent, <see cref="WorldObject.InWorld"/>) other than the player; the
/// contents of the player's pack have neither and are not counted. A held object (a wielded sword) has a parent and is counted as an item.</para>
/// <para><b>How the kinds are told.</b> By the bits retail's <c>PublicWeenieDesc</c> carries (the names ACE gives them are the same): Portal 0x40000, Door
/// 0x1000, LifeStone 0x4000 and BindStone 0x8000000, Corpse 0x2000, Player 0x8, Attackable 0x10, Vendor 0x200; a creature (ItemType 0x10) is an NPC unless it can
/// be attacked. A sign is the one reading of Lifestone's own: no bit says "sign", so a fixed (Stuck, 0x4) object that is not one of the kinds above and whose name
/// has the word "sign" is a sign. Counting by name is a heuristic, and it is only a diagnostic: it decides nothing the game does.</para>
/// </remarks>
public static class WorldContentCensus
{
    private const uint Openable = 0x1;
    private const uint Stuck = 0x4;
    private const uint Player = 0x8;
    private const uint Attackable = 0x10;
    private const uint Vendor = 0x200;
    private const uint Door = 0x1000;
    private const uint Corpse = 0x2000;
    private const uint LifeStone = 0x4000;
    private const uint Portal = 0x40000;
    private const uint BindStone = 0x8000000;
    private const uint CreatureItemType = 0x10;

    /// <summary>The kind of an object from its weenie description.</summary>
    public static WorldContentKind Classify(WeenieDesc weenie)
    {
        ArgumentNullException.ThrowIfNull(weenie);
        var flags = weenie.ObjectDescriptionFlags;
        if ((flags & Portal) != 0)
        {
            return WorldContentKind.Portal;
        }

        if ((flags & (LifeStone | BindStone)) != 0)
        {
            return WorldContentKind.Lifestone;
        }

        if ((flags & Door) != 0)
        {
            return WorldContentKind.Door;
        }

        if ((flags & Corpse) != 0)
        {
            return WorldContentKind.Corpse;
        }

        if ((flags & Player) != 0)
        {
            return WorldContentKind.Player;
        }

        if ((weenie.ItemType & CreatureItemType) != 0)
        {
            return (flags & Attackable) != 0 && (flags & Vendor) == 0 ? WorldContentKind.Creature : WorldContentKind.Npc;
        }

        if ((flags & Stuck) != 0 && weenie.Name.Contains("sign", StringComparison.OrdinalIgnoreCase))
        {
            return WorldContentKind.Sign;
        }

        return weenie.ItemType != 0 || (flags & Openable) != 0 ? WorldContentKind.Item : WorldContentKind.Other;
    }

    /// <summary>Counts the objects of <paramref name="state"/> that are in the world, other than the player.</summary>
    public static WorldContent Count(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        int npcs = 0, creatures = 0, players = 0, portals = 0, doors = 0, lifestones = 0, signs = 0, corpses = 0, items = 0, others = 0;
        foreach (var obj in state.Objects.All)
        {
            if (obj.IsSelf || !obj.InWorld)
            {
                continue;
            }

            switch (Classify(obj.Weenie))
            {
                case WorldContentKind.Npc:
                    npcs++;
                    break;
                case WorldContentKind.Creature:
                    creatures++;
                    break;
                case WorldContentKind.Player:
                    players++;
                    break;
                case WorldContentKind.Portal:
                    portals++;
                    break;
                case WorldContentKind.Door:
                    doors++;
                    break;
                case WorldContentKind.Lifestone:
                    lifestones++;
                    break;
                case WorldContentKind.Sign:
                    signs++;
                    break;
                case WorldContentKind.Corpse:
                    corpses++;
                    break;
                case WorldContentKind.Item:
                    items++;
                    break;
                default:
                    others++;
                    break;
            }
        }

        return new WorldContent(npcs, creatures, players, portals, doors, lifestones, signs, corpses, items, others);
    }
}

/// <summary>Why a placed object is, or is not, expected on screen (<see cref="DrawnCensus.Compare"/>); the host works it out from the avatar set's last frame.</summary>
public enum DrawExpectation
{
    /// <summary>The object has a visible, opaque draw entry with parts in a shown cell: it must be drawn (or still waiting for its meshes).</summary>
    Drawable,

    /// <summary>Hidden or NoDraw (<c>set_hidden</c>, <c>set_nodraw</c>): not drawn on purpose, by retail's rule.</summary>
    Hidden,

    /// <summary>Translucency 1 or more: not drawn, as <c>ServerObjectsLayer</c> skips it.</summary>
    Translucent,

    /// <summary>The Setup has no part to draw (an invisible marker): nothing to see.</summary>
    NoParts,

    /// <summary>Inside an interior cell that is not shown: not drawn yet, by design.</summary>
    CellNotShown,

    /// <summary>The avatar set holds no avatar for the object (its create failed, or has not been applied): a problem, reported.</summary>
    NoAvatar,

    /// <summary>The avatar exists but has no cell, so it has no draw entry: reported, never a silent skip.</summary>
    NoCell,
}

/// <summary>A placed object that should be on screen and is not (<see cref="DrawnCensus"/>).</summary>
/// <param name="Guid">The object id.</param>
/// <param name="Kind">Its kind.</param>
/// <param name="Name">Its name.</param>
/// <param name="Reason">Why it counts as missing.</param>
public readonly record struct UndrawnObject(uint Guid, WorldContentKind Kind, string Name, DrawExpectation Reason);

/// <summary>
/// The census of what the server placed against what the layer drew, per kind (m2 wave 3, dod-6-7-1 and dod-10-11-14-15-3): the login check counts every placed object of every kind
/// (portals, doors, lifestones, signs, corpses, items, creatures), and each one that is not hidden, translucent, part-less or in an unshown cell must be among the drawn guids.
/// </summary>
/// <param name="Placed">Every placed object (other than the player) by kind.</param>
/// <param name="Drawn">The placed objects drawn this frame, by kind.</param>
/// <param name="Missing">The objects that should be drawn and are not (<see cref="DrawExpectation.Drawable"/>, <see cref="DrawExpectation.NoAvatar"/> and <see cref="DrawExpectation.NoCell"/>).</param>
/// <param name="Excused">The objects not drawn for a reason that is not a problem, by reason.</param>
public sealed record DrawnCensus(WorldContent Placed, WorldContent Drawn, IReadOnlyList<UndrawnObject> Missing, IReadOnlyDictionary<DrawExpectation, int> Excused)
{
    /// <summary>True when every object that should be drawn is.</summary>
    public bool Matches => Missing.Count == 0;

    /// <summary>
    /// Compares the placed objects of <paramref name="state"/> (in the world, not the player) with the guids drawn.
    /// </summary>
    /// <param name="state">The session's state.</param>
    /// <param name="drawn">The guids the layer drew in its last frame.</param>
    /// <param name="expectation">Whether an object is expected on screen, from the avatar set's last frame.</param>
    public static DrawnCensus Compare(GameState state, ISet<uint> drawn, Func<uint, DrawExpectation> expectation)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(drawn);
        ArgumentNullException.ThrowIfNull(expectation);
        var placed = new int[(int)WorldContentKind.Other + 1];
        var drawnKinds = new int[placed.Length];
        var missing = new List<UndrawnObject>();
        var excused = new Dictionary<DrawExpectation, int>();
        foreach (var obj in state.Objects.All)
        {
            if (obj.IsSelf || !obj.InWorld)
            {
                continue;
            }

            var kind = WorldContentCensus.Classify(obj.Weenie);
            placed[(int)kind]++;
            if (drawn.Contains(obj.Guid))
            {
                drawnKinds[(int)kind]++;
                continue;
            }

            var why = expectation(obj.Guid);
            if (why is DrawExpectation.Drawable or DrawExpectation.NoAvatar or DrawExpectation.NoCell)
            {
                missing.Add(new UndrawnObject(obj.Guid, kind, obj.Name, why));
            }
            else
            {
                excused[why] = excused.GetValueOrDefault(why) + 1;
            }
        }

        return new DrawnCensus(ToContent(placed), ToContent(drawnKinds), missing, excused);
    }

    /// <summary>
    /// One line: the drawn counts by kind against the placed ones (<c>portals=1/1 doors=3/3 ...</c>), the excused counts by reason, then the first few missing objects by name.
    /// </summary>
    public string Describe(int missingListed = 6)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string Pair(int d, int p) => string.Create(inv, $"{d}/{p}");
        var text = string.Create(
            inv,
            $"drawn/placed npcs={Pair(Drawn.Npcs, Placed.Npcs)} creatures={Pair(Drawn.Creatures, Placed.Creatures)} portals={Pair(Drawn.Portals, Placed.Portals)} doors={Pair(Drawn.Doors, Placed.Doors)} lifestones={Pair(Drawn.Lifestones, Placed.Lifestones)} signs={Pair(Drawn.Signs, Placed.Signs)} corpses={Pair(Drawn.Corpses, Placed.Corpses)} items={Pair(Drawn.Items, Placed.Items)} others={Pair(Drawn.Others, Placed.Others)}");
        if (Excused.Count > 0)
        {
            text += " excused " + string.Join(" ", Excused.OrderBy(e => e.Key).Select(e => string.Create(inv, $"{e.Key}={e.Value}")));
        }

        if (Missing.Count > 0)
        {
            text += string.Create(inv, $" missing {Missing.Count}: ") + string.Join(", ", Missing.Take(missingListed).Select(m => string.Create(inv, $"{m.Name} ({m.Kind}, 0x{m.Guid:X8}, {m.Reason})")));
        }

        return text;
    }

    private static WorldContent ToContent(int[] counts) => new(
        counts[(int)WorldContentKind.Npc],
        counts[(int)WorldContentKind.Creature],
        counts[(int)WorldContentKind.Player],
        counts[(int)WorldContentKind.Portal],
        counts[(int)WorldContentKind.Door],
        counts[(int)WorldContentKind.Lifestone],
        counts[(int)WorldContentKind.Sign],
        counts[(int)WorldContentKind.Corpse],
        counts[(int)WorldContentKind.Item],
        counts[(int)WorldContentKind.Other]);
}
