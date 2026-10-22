using Lifestone.Net.Diagnostics;

namespace Lifestone.Client.Session.Contracts;

/// <summary>
/// The one read-and-act surface of a game session (A0, docs/session.md): what the retail screens, the portal bridge,
/// the avatars, physics, the tools panel, the inspector, the scenarios and every automation engine see. Hosts create the
/// session (<c>Lifestone.Client.Session.SessionBootstrap.CreateLive</c> or <c>GameSession.Offline</c>) and tick it;
/// everyone else holds only this view. Nothing behind it talks to the network session directly (DoD 13).
/// </summary>
/// <remarks>
/// <para><b>Threading.</b> One game thread. Read the state, subscribe, issue actions and receive events and handle
/// completions on the thread that ticks the session; nothing here is thread-safe, and no member blocks.</para>
/// <para><b>Mutation.</b> <see cref="State"/> is a read model: only the session's reducer changes it, inside
/// <c>GameSession.Tick</c>, and it publishes an event on <see cref="Events"/> after each change. Consumers act through
/// <see cref="Actions"/> and <see cref="Tools"/> only.</para>
/// </remarks>
public interface IGameSessionView
{
    /// <summary>The session's read model: phase, character list, the player, the world objects, chat, server time.</summary>
    GameState State { get; }

    /// <summary>The typed events the reducer publishes, and the raw message tap.</summary>
    IEventBus Events { get; }

    /// <summary>Every action a human or a bot can take: login, characters, world entry, use, chat, logoff, movement.</summary>
    IActionApi Actions { get; }

    /// <summary>The Lifestone server mod's player tools (places, where am I, add XP), through the same action rules.</summary>
    PlayerTools Tools { get; }

    /// <summary>The session's monotonic clock: every timeout, handle time, chat time and recording time reads it.</summary>
    SessionClock Clock { get; }

    /// <summary>The audit log of completed actions (a ring of <see cref="ActionLog.Capacity"/> entries).</summary>
    ActionLog ActionLog { get; }

    /// <summary>The player's attack sequence (M4 1.2 s1): Idle until the first swing or launch, then the server-driven run of swings or shots up to the 0x01A7 that ends it. <see cref="AttackChanged"/> follows every change.</summary>
    AttackSequence Attack => NoAttack;

    /// <summary>What <see cref="Attack"/> reads for a view that keeps none (a test fake, a session without a reducer): an Idle sequence nothing ever changes.</summary>
    private static readonly AttackSequence NoAttack = new();

    /// <summary>The ammunition the player wields (wcid, guid, stack), or null when none is (M4 1.2 s1). Read from the inventory, so it follows 0x0197 and the wield and unwield messages.</summary>
    HeldAmmo? Ammo => HeldAmmo.Of(State);

    /// <summary>A snapshot of the network link's counters (all zero for an offline session).</summary>
    LinkStats Link { get; }

    /// <summary>
    /// The spell <paramref name="id"/> as the SpellTable holds it (name, icon, school, power, duration, ...), for the panels that name spells (M3 2.2: the effects pages and the spellbook). Read-only and allocation-free
    /// (a dictionary lookup). <b>Null</b> while the SpellTable has not loaded (it decodes on a worker from <c>GameSession.UseSpellsFrom</c>: <c>GameSession.SpellTableReady</c>), for a session without dats (the
    /// default here), and for an id the table lacks: a panel shows its placeholder and asks again the next time it redraws.
    /// </summary>
    /// <param name="id">The spell id.</param>
    Lifestone.Data.Magic.SpellInfo? Spell(uint id) => null;

    /// <summary>
    /// True once the SpellTable <see cref="Spell"/> reads is loaded: a null <see cref="Spell"/> then means the table lacks the id, and a panel leaves that spell out and names it. False while the table decodes on its worker,
    /// for a session without dats (the default here) and after a load that failed: a panel waits for it (a one-shot poll) and shows nothing meanwhile, never a guess. Readiness is the table's own, never a spell's (M3 3.2
    /// review finding 9: a favourite the table lacks must not keep a tab from building).
    /// </summary>
    bool SpellTableReady => false;

    /// <summary>The XpTable (dat 0x0E000018: the experience a rank of an attribute, a vital or a skill costs), or null until the host gave the session its dats (<c>GameSession.UseSpellsFrom</c>). The skills and attributes pages price their raise buttons by it.</summary>
    Lifestone.Data.CharacterCreation.XpTableData? XpTable => null;

    /// <summary>The SkillTable (dat 0x0E000004: the skills, their names, formulae, credit costs and icons), or null until the host gave the session its dats (<c>GameSession.UseSpellsFrom</c>).</summary>
    Lifestone.Data.CharacterCreation.SkillTableData? SkillTable => null;

    /// <summary>
    /// Seconds since the last datagram arrived from the server, on <see cref="Clock"/>, or null before any did (no network link: the offline stand-in, a replay). What retail's
    /// link status reads as the time since it last heard from the server (<c>LinkStatusHolder::GetConnectionStatus</c>, 11.6096 0x004116E0).
    /// </summary>
    double? SecondsSinceLastServerPacket => Link.LastReceivedTimestamp == 0 ? null : Math.Max(0, Clock.Now - Clock.ToSeconds(Link.LastReceivedTimestamp));

    /// <summary>
    /// The latest appraisal of <paramref name="guid"/> (GameEvent 0x00C9; M4 3.2 s2), for the examine panel: the item list's examine and the paperdoll's figure click send <see cref="IActionApi.Identify"/> and show this
    /// when the handle completes (<see cref="IdentifyHandle.Result"/>). False when the player has not appraised the object this session or the bounded cache (<see cref="AppraisalCache.Capacity"/>) dropped it.
    /// </summary>
    /// <param name="guid">The object's guid.</param>
    /// <param name="appraisal">The appraisal, or null.</param>
    bool TryGetAppraisal(uint guid, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Appraisal? appraisal) => State.Appraisals.TryGet(guid, out appraisal);

    /// <summary>The vendor the player has open (M4 3.2 s1): the last 0062's stock and prices, or null when none is open. <see cref="VendorOpened"/>, <see cref="VendorStockChanged"/> and <see cref="VendorClosed"/> follow it.</summary>
    VendorStock? Vendor => State.Vendor;

    /// <summary>
    /// The player closed the vendor window: the model's open vendor closes with it, so the window and the model agree (3.2-R23; <c>gmVendorUI::RecvNotice_CloseVendor</c> 11.6096 0x004C0F40 is a client-side close too, ACE has no close message).
    /// Publishes <see cref="VendorClosed"/>; does nothing when none is open. A view without a model ignores it.
    /// </summary>
    void CloseVendorWindow()
    {
    }
}
