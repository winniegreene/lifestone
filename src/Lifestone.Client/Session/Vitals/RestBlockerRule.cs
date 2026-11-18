using Lifestone.Client.Session.Contracts;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Vitals;

/// <summary>What stands between her and lying down (<see cref="RestBlockerRule.Read"/>).</summary>
public enum RestBlock
{
    /// <summary>Nothing that can attack her within the quiet distance: she may lie down.</summary>
    None,

    /// <summary>An attackable creature is within the fight's reach: it is shot first, by the fight's rule, and she rests after.</summary>
    ShootFirst,

    /// <summary>An attackable creature is within the quiet distance but beyond the fight's reach: the wait stands (as M6 7 AO).</summary>
    Wait,
}

/// <summary>
/// M6 7 AT (live, 2026-11-06: a Drudge Skulker within 24 m of the home point held her standing for two rests of 189 s and 174 s at 0.1 stamina a second): a creature that can attack her and stands within the fight's reach is no reason to
/// rest on her feet; the fight shoots it (the fight's own pick and cast rule), and she lies down after. Only one beyond that reach but inside the quiet distance keeps the wait as M6 7 AO wrote it. Reads the same table as the
/// vitals keeper (<see cref="WorldObjectTable.NearestAttackableCreature"/>: a town NPC is no blocker). Allocation-free.
/// M6 16 E2b (the one world picture): with the session's live positions given, a creature stands where they say (the body the client draws when it draws one and the body can be trusted, else the server's last report), as the
/// keeper's and the fight's awake reads take it; with none given, or from a session that draws no bodies, where the server last reported it.
/// </summary>
public static class RestBlockerRule
{
    /// <summary>Reads what blocks lying down at <paramref name="here"/>.</summary>
    /// <param name="objects">The world objects.</param>
    /// <param name="here">Where she stands.</param>
    /// <param name="self">Her own id (skipped).</param>
    /// <param name="quietMetres">The distance inside which a creature that can attack keeps her on her feet (<see cref="VitalsKeeper.QuietMetres"/>).</param>
    /// <param name="shootMetres">The fight's reach in metres (the aggro reach); 0 or less: no shoot-first, every blocker is a wait (today's rule).</param>
    /// <param name="guid">The blocker's id, or 0.</param>
    /// <param name="live">The session's live positions (<see cref="IGameSessionView.Live"/>), or null: the server's reports alone.</param>
    public static RestBlock Read(WorldObjectTable objects, in CellFrame here, uint self, float quietMetres, float shootMetres, out uint guid, LivePositions? live = null)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (shootMetres > 0 && objects.NearestAttackableCreature(in here, Math.Min(shootMetres, quietMetres), self, out guid, live))
        {
            return RestBlock.ShootFirst;
        }

        return objects.NearestAttackableCreature(in here, quietMetres, self, out guid, live) ? RestBlock.Wait : RestBlock.None;
    }
}
