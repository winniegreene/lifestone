using System.Globalization;
using System.Numerics;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Casting;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Quests;
using Lifestone.Client.World;
using Lifestone.Data.Magic;
using Lifestone.Data.World;
using DatReaderWriter.Enums;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 item 5 (D56, one runner, not two): the step runners a quest file's stations share, moved whole from the Academy's run (M6 item 0 a3b, <see cref="AcademyRun"/>) so the Academy and the quest station (<see cref="QuestRun"/>) run the same
/// code: the talk (Use, the reply, the stamp, the gift), the give, the floor pick-up, the single-target kill and its loot, the fight-back (M5 D88), the caster and the magic mode, peace, the walk to an object (<see cref="WalkTo"/>, the use reach,
/// M6 0 F4) with its one named recovery (<see cref="WalkWithRecovery"/>, <c>recovery=step-away</c>), the stamp check and the wear (<see cref="PathwardenWear"/>), with the reads and the PROBLEM line. Every log line and recovery name is the
/// Academy's, unchanged (<c>AcademyChestLiveTests</c> reads them). The parts that are one station's are virtual: <see cref="Cell"/> (the Academy rebases a quest file's cell to the instance it stands in), <see cref="OwnPieces"/> (the
/// pieces the wear plan keeps on) and <see cref="GiftArrived"/> (the Academy reads a gift as held; the quest station as arrived, item 5's contract).
/// </summary>
public abstract partial class QuestStepLeg : StationLeg, IDisposable
{
    /// <summary>How close the player stands to an NPC, an item or a corpse for a Use, a Give or a PickUp, in metres (the Pathwarden station's <see cref="PathwardenRun.UseRange"/>; ACE's MoveTo walks the rest).</summary>
    public const double UseRange = PathwardenRun.UseRange;

    /// <summary>The seconds a reply, a stamp or an item has to show after an act.</summary>
    public const double AfterSeconds = PathwardenRun.AfterSeconds;

    /// <summary>The range the war spell is cast from, in metres (Lifestone's own: inside a bolt's reach, and short enough for a clear line in the Academy's rooms).</summary>
    public const double CastRange = 15;

    /// <summary>How far from the go step's point the station looks for the step's creature, in metres (Lifestone's own: the room and its neighbours).</summary>
    public const double SearchMetres = 40;

    /// <summary>The seconds one kill may take, the walks to the creature included (Lifestone's own).</summary>
    public const double KillSeconds = 180;

    /// <summary>The seconds the station waits for a killed creature's kind to come back (the golem's generator is 10 s, cache/m6/0/sel-academy-8602.tsv; the olthoi's is not established).</summary>
    public const double RespawnSeconds = 60;

    /// <summary>The seconds a corpse has to show after a kill.</summary>
    public const double CorpseSeconds = 10;

    /// <summary>The seconds a portal has to teleport the player after its Use.</summary>
    public const double PortalSeconds = 30;

    /// <summary>The seconds the station rests for the next cast's mana.</summary>
    public const double RestSeconds = 90;

    /// <summary>The seconds after which an attacker that has not attacked again is forgotten (Lifestone's own, the fight's default).</summary>
    public const double ForgetSeconds = 30;

    /// <summary>M6 8 G: the fight-backs one walk makes at most before it ends by name (the run leg's bound, <c>TravelLeg.MaxFightBacks</c>).</summary>
    public const int MaxWalkFightBacks = 8;

    /// <summary>The most kills for one looted item: the kill and one named recovery (<c>recovery=kill-another</c>) when the corpse does not hold it.</summary>
    public const int MaxKillsForItem = 2;

    /// <summary>After a walk to a portal and a teleport the client saw (M6 0 F6), a player this far from the portal's point was moved by the portal on touch (Lifestone's own, in metres: the walk ends within 1.5 m of it).</summary>
    public const double TouchTeleportMetres = 10;

    /// <summary>The seconds the station waits, after a walk that arrived beside a portal the next step uses, for the touch's teleport (Lifestone's own: the F751 follows the touch by a moment; none, and the next step Uses the portal).</summary>
    public const double TouchSeconds = 3;

    /// <summary>The walker's failure name for a door that stayed shut after its two Uses (<c>NavRunner.StepDoors</c>), which the station reads as a closed door for the rest of the walk (M6 0 F6).</summary>
    public const string DoorShutFailure = "door-closed-timeout";

    /// <summary>
    /// How near an object may be for the station to send its Use, PickUp or Open without walking first, in metres (M6 0 F4): the station's use range plus a margin of 3.5 m (Lifestone's own, a straight stretch of a few steps). Beyond the range the server's own move-to
    /// walks the rest (Player_Use.cs:184-206 and Player_Move.cs:37-96 @95ef426d, the pattern of the fight's loot and the Pathwarden station), so a refused wayfinder walk of a few metres cannot end the station. Round 4: a corpse 1.79 m away, the walk refused at segment 0.
    /// </summary>
    public const double UseReachMetres = UseRange + 3.5;

    /// <summary>The nearest a point probed walkable may be for the step-away recovery to walk to it, in metres (Lifestone's own: nearer than this the player stands on it).</summary>
    public const double StepAwayMinMetres = 1.0;

    private const uint CreatureItemType = 0x10;

    private static readonly HashSet<uint> NoPieces = [];
    private readonly Dictionary<string, (double When, double Reach)> _attackers = new(StringComparer.Ordinal);
    private readonly HashSet<uint> _shotWalked = []; // M6 8 L: the step's creatures already given their one walk for a clear line
    private ShotLedger? _shotLedger;
    private readonly HashSet<string> _heard = new(StringComparer.Ordinal); // M6 8 G: the names heard within the walk's window, refilled by each read
    private IDisposable? _combatSub;
    private IDisposable? _chatSub;
    private IDisposable? _teleportSub;
    private int _killing; // M6 8 G: inside a kill: its own target order answers an attacker, so its walks are not stopped (and a defence's chase never re-enters itself)
    private (uint Cell, Vector3 At)? _walkable; // M6 0 F4: where the wayfinder last confirmed a route's end (a probed walkable point, the step-away recovery's target)
    private int _usedReach;
    private int _walkedReach;

    /// <summary>Creates the leg.</summary>
    /// <param name="name">The leg's name (the log's <c>[live name]</c>).</param>
    /// <param name="context">The journey's context.</param>
    protected QuestStepLeg(string name, JourneyContext context)
        : base(name, context)
    {
    }

    private ShotLedger _shots => _shotLedger ??= new ShotLedger(Log);

    /// <summary>The floor pieces worn.</summary>
    public int WornCount { get; protected set; }

    /// <summary>The kills.</summary>
    public int Kills { get; protected set; }

    /// <summary>The war casts sent.</summary>
    public int Casts { get; protected set; }

    /// <summary>The named recoveries taken.</summary>
    protected int Recoveries { get; set; }

    /// <summary>M6 0 F6: the accepted F751 PlayerTeleports since <see cref="Watch"/> (the server moved the player: a portal, on touch or used).</summary>
    protected int Teleports { get; private set; }

    /// <summary>The last go step's place (the room the next acts look in), or null.</summary>
    protected QuestPlace? LastPlace { get; set; }

    /// <summary>The cell of a quest file's <paramref name="fileCell"/> where the run stands: the file's own by default (the Academy rebases it to its instance).</summary>
    /// <param name="fileCell">The quest file's cell.</param>
    protected virtual uint Cell(uint fileCell) => fileCell;

    /// <summary>The quest's own pieces the wear plan keeps on (<see cref="PathwardenWear.Plan"/>'s quest pieces): none by default (the Academy's floor pieces).</summary>
    protected virtual ISet<uint> OwnPieces => NoPieces;

    /// <summary>True when the NPC's gift <paramref name="wcid"/> shows after a talk: held by default (the Academy's read, M6 0 F7); the quest station reads it as arrived (the count above <paramref name="before"/>).</summary>
    /// <param name="wcid">The gift's weenie class id.</param>
    /// <param name="before">The count held before the act (<see cref="CountHeld"/>).</param>
    protected virtual bool GiftArrived(uint wcid, int before) => InPack(wcid).Count > 0;

    /// <summary>
    /// M6 8 G (<c>m6-quest-go-fight-back</c>): true when this station's walks stop for an attacker (<see cref="TravelFightingBack"/>): false by default (the Academy's rooms, its fight-back before each step as it was); the quest station says true.
    /// </summary>
    protected virtual bool WalksFightBack => false;

    /// <summary>M6 8 G: the fight-backs this station's walks made (report-only; a go's <c>fought_back=</c>).</summary>
    protected int WalkFightBacks { get; private set; }

    /// <summary>The count of <paramref name="wcid"/> in the main pack, stacks counted by their size (item 5: an expected item "arrives" when this rises).</summary>
    /// <param name="wcid">The weenie class id.</param>
    /// <summary>
    /// M6 8 (live 2026-11-11 10:30 UTC, m6-quest-item-side-pack): a quest's item is looked for in the main pack and the side packs. The loot rule put the Female Tusker Tusk in a side pack (worth a look), the
    /// turn-in's hunt-until counted 0 held in the main pack and failed, with the tusk in her hands. A give takes an item from any pack (ACE's give reads the player's whole inventory).
    /// </summary>
    protected override List<uint> InPack(uint wcid)
    {
        var all = new List<uint>();
        Mage.GearInventory.Collect(S.State, all);
        all.RemoveAll(g => !S.State.Objects.TryGet(g, out var o) || o.Weenie.WeenieClassId != wcid);
        return all;
    }

    protected int CountHeld(uint wcid) => InPack(wcid).Sum(g => Math.Max(1, StackOf(g)));

    /// <summary>Subscribes the fight-back's attackers (M5 D88) and the teleports; called once at the script's start.</summary>
    protected void Watch()
    {
        _combatSub ??= S.Events.Subscribe<CombatEvent>(OnCombat);
        _chatSub ??= S.Events.Subscribe<ChatReceived>(OnChat);
        _teleportSub ??= S.Events.Subscribe<PlayerTeleportAnnounced>(OnTeleport);
        BeginThreatShadow(); // M6 16 E2b: the threat board hears from the leg's watch at the latest (report-only)
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Ends the subscriptions.</summary>
    /// <param name="disposing">True from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        EndThreatShadow(); // M6 16 E2b: the leg's line of its shadow's asks, report-only
        _combatSub?.Dispose();
        _combatSub = null;
        _chatSub?.Dispose();
        _chatSub = null;
        _teleportSub?.Dispose();
        _teleportSub = null;
    }

    /// <param name="step">The talk step.</param>
    /// <param name="alsoEnds">M6 14 guard task: a reply other than the step's own that ends the talk well (a kill task's arming talk whose counter was already complete is the turn-in: the quest's done reply), read since the Use's mark; null: none.</param>
    protected IEnumerable<ActionHandle?> Talk(QuestStep step, Func<long, bool>? alsoEnds = null)
    {
        var wcid = step.Target!.Wcid!.Value;
        uint npc = 0;
        foreach (var h in Approach(wcid, step.Target.Name, g => npc = g)) { yield return h; }

        var giftBefore = step.Expect.Item?.Wcid is { } expected ? CountHeld(expected) : 0; // M6 5 Q3: the quest station reads the gift as arrived (GiftArrived)
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 1)
            {
                Recoveries++;
                Log($"recovery=talk-again: no reply from {step.Target.Name} {AfterSeconds:0} s after the Use; walking up again and using once more");
                foreach (var h in Approach(wcid, step.Target.Name, g => npc = g)) { yield return h; }
            }

            var mark = S.State.Chat.Total;
            var use = S.Actions.Use(npc, ActionSource.Host);
            yield return use;
            Log($"talk: Use {step.Target.Name} (0x{npc:X8}): {Describe(use)}");
            bool Armed() => step.ArmedReply is not null && QuestReads.Replied(S.State.Chat, mark, step.ArmedReply);
            bool OnTimer() => step.WaitReply is not null && QuestReads.Replied(S.State.Chat, mark, step.WaitReply);
            bool Ends() => alsoEnds?.Invoke(mark) == true;
            foreach (var h in Until(() => Replied(step.Expect, mark) || Armed() || OnTimer() || Ends(), AfterSeconds)) { yield return h; }

            if (Ends() && !Replied(step.Expect, mark) && !Armed())
            {
                Log($"talk: {step.Target.Name} answered with the reply that ends the talk another way{Said(mark)}");
                yield break;
            }

            if (OnTimer() && !Replied(step.Expect, mark))
            {
                // M6 5 F1 (review 5-R7): the task is on its 20 h wait; a named refusal, never a retry.
                Log($"talk: {step.Target.Name} says the task is on its timer{Said(mark)}");
                Require(Verify(false, $"talk: {step.Target.Name} says the task is on its timer (wait reply '{step.WaitReply}')", npc, mark), "talk: on-timer");
            }

            if (Armed() && !Replied(step.Expect, mark))
            {
                Log($"talk: {step.Target.Name} says the task is already armed (armed reply '{step.ArmedReply}'): accepted as the arm{Said(mark)}");
                yield break;
            }

            if (Replied(step.Expect, mark))
            {
                Log($"talk: {step.Target.Name} replied{Said(mark)}");
                foreach (var h in StampCheck(step)) { yield return h; }

                if (step.Expect.Item?.Wcid is { } gift)
                {
                    // M6 0 F7: the NPC's gift (Jonathan's Exit Token) is checked in the pack.
                    foreach (var h in Until(() => GiftArrived(gift, giftBefore), AfterSeconds)) { yield return h; }

                    Require(Verify(GiftArrived(gift, giftBefore), $"talk: {step.Target.Name} gave no {step.Expect.Item.Name} ({gift}) after the reply", npc, mark), "talk: no gift");
                    Log($"talk: {step.Expect.Item.Name} ({gift}) in the pack: yes");
                }

                yield break;
            }

            Warn(Stage, $"talk: no reply '{step.Expect.Reply}' from {step.Target.Name} after the Use (attempt {attempt + 1})", npc, mark);
        }

        Require(Verify(false, $"talk: {step.Target.Name} never replied '{step.Expect.Reply}'"), "talk: no reply");
    }

    protected IEnumerable<ActionHandle?> Give(QuestStep step)
    {
        var wcid = step.Target!.Wcid!.Value;
        var item = step.Item!.Wcid!.Value;
        var itemGuid = InPack(item).FirstOrDefault();
        Require(Verify(itemGuid != 0, $"give: the pack holds no {step.Item.Name} ({item})"), "give: no item");
        uint npc = 0;
        foreach (var h in Approach(wcid, step.Target.Name, g => npc = g)) { yield return h; }

        var levelBefore = S.State.Self.Qualities.Level;
        var xpBefore = S.State.Self.Qualities.TotalExperience;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt == 1)
            {
                Recoveries++;
                Log($"recovery=give-again: {step.Item.Name} is still in the pack and no reply came {AfterSeconds:0} s after the Give; walking up again and giving once more");
                foreach (var h in Approach(wcid, step.Target.Name, g => npc = g)) { yield return h; }
            }

            var mark = S.State.Chat.Total;
            var give = S.Actions.Give(npc, itemGuid, 1, ActionSource.Host);
            yield return give;
            Log($"give: {step.Item.Name} (0x{itemGuid:X8}) to {step.Target.Name} (0x{npc:X8}): {Describe(give)}");
            foreach (var h in Until(() => Replied(step.Expect, mark) || !InPack(item).Contains(itemGuid), AfterSeconds)) { yield return h; }

            var replied = Replied(step.Expect, mark);
            var taken = !InPack(item).Contains(itemGuid);
            Log($"give: taken={Yes(taken)} replied={Yes(replied)}{Said(mark)}");
            if (taken || replied)
            {
                if (step.Expect.Reply is not null && !replied)
                {
                    foreach (var h in Until(() => Replied(step.Expect, mark), AfterSeconds)) { yield return h; }

                    Verify(Replied(step.Expect, mark), $"give: {step.Target.Name} took {step.Item.Name} but did not reply '{step.Expect.Reply}'", itemGuid, mark);
                }

                foreach (var h in StampCheck(step)) { yield return h; }

                foreach (var h in Pause(1)) { yield return h; } // the award follows the Tell (the emote set's order)

                if (step.Expect.Item?.Wcid is { } gift)
                {
                    // M6 0 F6: the NPC's gift (the Academy Blacksmith's Academy Library Key for the Bellows) is checked in the pack.
                    foreach (var h in Until(() => InPack(gift).Count > 0, AfterSeconds)) { yield return h; }

                    Verify(InPack(gift).Count > 0, $"give: {step.Target.Name} gave no {step.Expect.Item.Name} ({gift}) after {step.Item.Name}", 0, mark);
                    Log($"give: {step.Expect.Item.Name} ({gift}) in the pack: {Yes(InPack(gift).Count > 0)}");
                }

                Log(string.Create(CultureInfo.InvariantCulture, $"give: experience {xpBefore} -> {S.State.Self.Qualities.TotalExperience} (+{S.State.Self.Qualities.TotalExperience - xpBefore}), level {levelBefore} -> {S.State.Self.Qualities.Level}"));
                yield break;
            }
        }

        Require(Verify(false, $"give: {step.Target.Name} took nothing and said nothing after the Give and the recovery", itemGuid), "give: refused");
    }

    /// <summary>M6 8 B2a: after a floor PickUp that did not arrive, before its one more try: a station that names a server refusal stops here (the quest station's <c>pick-up:too-recent</c>).</summary>
    /// <param name="step">The pick-up step.</param>
    /// <param name="mark">The chat's total before the PickUp.</param>
    protected virtual void PickUpRefusedCheck(QuestStep step, long mark)
    {
    }

    protected IEnumerable<ActionHandle?> PickUpFloor(QuestStep step)
    {
        var item = step.Item!.Wcid!.Value;
        if (InPack(item).Count > 0 || WornWcid(item))
        {
            Log($"pick-up: {step.Item.Name} ({item}) is held already");
        }
        else
        {
            for (var attempt = 0; attempt < 2 && InPack(item).Count == 0; attempt++)
            {
                if (attempt == 1)
                {
                    Recoveries++;
                    Log($"recovery=pick-up-again: {step.Item.Name} is not in the pack {AfterSeconds:0} s after the PickUp; walking up again and picking it up once more");
                }

                var floor = NearestFloor(item);
                Require(Verify(floor != 0, $"pick-up: no {step.Item.Name} ({item}) lies in view (the go step's room {LastPlace})"), "pick-up: not in view");
                foreach (var h in WalkTo(floor, step.Item.Name, UseRange)) { yield return h; }

                var mark = S.State.Chat.Total;
                var take = S.Actions.PickUp(floor, 0, 0, ActionSource.Host);
                yield return take;
                Log($"pick-up: {step.Item.Name} (0x{floor:X8}): {Describe(take)}");
                foreach (var h in Until(() => InPack(item).Count > 0, AfterSeconds)) { yield return h; }

                if (InPack(item).Count == 0)
                {
                    PickUpRefusedCheck(step, mark);
                    Warn(Stage, $"pick-up: {step.Item.Name} is not in the pack after the PickUp ({Describe(take)})", floor, mark);
                }
            }

            Require(Verify(InPack(item).Count > 0 || WornWcid(item), $"pick-up: {step.Item.Name} ({item}) is not in the pack after the recovery"), "pick-up: not taken");
        }

        foreach (var h in Wear(step.Item)) { yield return h; }
    }

    protected IEnumerable<ActionHandle?> KillAndLoot(QuestStep step)
    {
        var creature = step.Target!.Wcid!.Value;
        var item = step.Item!.Wcid!.Value;
        if (InPack(item).Count > 0)
        {
            Log($"kill: {step.Item.Name} ({item}) is in the pack already: no kill");
            yield break;
        }

        var near = LastPlace is { Point: { } point } p ? (Cell(p.Cell), point) : (S.State.Self.Position!.Value.CellId, S.State.Self.Position!.Value.Origin);
        for (var kill = 0; kill < MaxKillsForItem && InPack(item).Count == 0; kill++)
        {
            if (kill == 1)
            {
                Recoveries++;
                Log($"recovery=kill-another: the corpse did not give {step.Item.Name}; one more {step.Target.Name}");
            }

            var victim = new Victim();
            foreach (var h in KillOne(creature, step.Target.Name, near.Item1, near.Item2, victim)) { yield return h; }

            foreach (var h in Loot(victim, step.Item)) { yield return h; }
        }

        Verify(InPack(item).Count > 0, $"kill: {step.Item.Name} ({item}) is not in the pack after {MaxKillsForItem} kill(s) of {step.Target.Name}");
        foreach (var h in PeaceMode()) { yield return h; }
    }

    // ---------------------------------------------------------------------------------------------- the fight

    /// <summary>M6 6 FXD2 (6-D7): the first WeenieError code the chat carried since <paramref name="mark"/> (<c>S.State.Chat.Total</c> before the cast), or 0: a 0x0400 that arrives only as chat is a component refusal too, as the run leg reads it (<c>RefusalSince</c>).</summary>
    protected uint ChatRefusalSince(long mark)
    {
        var chat = S.State.Chat;
        var fresh = (int)Math.Min(chat.Count, chat.Total - mark);
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            if (chat[i].Kind == ChatKind.WeenieError)
            {
                return chat[i].ChatType;
            }
        }

        return 0;
    }


    /// <summary>The creature being killed: its guid, name and where it was last seen alive.</summary>
    protected sealed class Victim
    {
        public uint Guid { get; set; }

        public string Name { get; set; } = string.Empty;

        public CellFrame? LastSeen { get; set; }
    }

    /// <summary>
    /// Kills one creature of <paramref name="wcid"/> near the go step's point, or (wcid 0) the attackers only: each cast is check (the target alive, in range, the mana for the spell), act (the targeted cast), check (the outcome, then the target's death within 2 s). An attacker is the target first
    /// (M5 D88). A fizzle is cast again; a busy answer waits a second; a components refusal fails the station by name.
    /// </summary>
    protected IEnumerable<ActionHandle?> KillOne(uint wcid, string name, uint cell, Vector3 near, Victim victim)
    {
        _killing++;
        try
        {
            // M6 16 Q8: the casting and the staying alive are the skirmish's (QuestStepLeg.Skirmish.cs); the old loop is the fallback for the skirmish's first live gate (--autopilot-quest-own-kill-loop on; TODO(engine-quest-old-loop): it goes after that gate)
            foreach (var h in Context.Facts.QuestOwnKillLoop ? KillLoop(wcid, name, cell, near, victim) : KillBySkirmish(wcid, name, cell, near, victim)) { yield return h; }
        }
        finally
        {
            _killing--;
        }
    }

    private IEnumerable<ActionHandle?> KillLoop(uint wcid, string name, uint cell, Vector3 near, Victim victim)
    {
        foreach (var h in ReadyMagic()) { yield return h; }

        var spell = WarSpell(out var bolt, out var lack, wcid);
        Require(Verify(lack != BoltLack.NoComponents, $"kill: no-components: {bolt}"), "kill: no components");
        Require(Verify(spell is not null, "kill: the spellbook holds no War Magic projectile (TODO(m6-academy-war-spell))"), "kill: no war spell");
        Log($"kill: the war spell is {spell!.Name} ({spell.Id}): {bolt}");
        var deadline = S.Clock.Now + KillSeconds;
        var heldAtStart = HeldSeconds; // M6 8 (m6-survival-kill-clock): a hold by the survival layer does not count against the kill's bound
        uint target = 0;
        var defending = false;
        var turned = false;
        while (S.Clock.Now - (HeldSeconds - heldAtStart) < deadline)
        {
            if (turned)
            {
                yield return null; // M6 8 L: every turn of this loop gives the frame back: a turn that changes nothing must not spin inside one frame (live 20:20 UTC: no mana and an attacker in range hung the client)
            }

            turned = true;
            var attacker = LiveAttacker();
            var current = attacker != 0 ? attacker : wcid == 0 ? 0 : Alive(target) && !defending && !_shots.IsDropped(target, S.Clock.Now) && !PassedBy(target) ? target : NearestLiving(wcid, cell, near);
            if (current == 0)
            {
                if (wcid == 0)
                {
                    Log("defend: no living attacker left");
                    yield break;
                }

                Log($"kill: no living {name} ({wcid}) within {SearchMetres:0} m of the point; waiting up to {RespawnSeconds:0} s for one");
                foreach (var h in Until(() => NearestLiving(wcid, cell, near) != 0 || LiveAttacker() != 0, RespawnSeconds)) { yield return h; }

                if (NearestLiving(wcid, cell, near) == 0 && LiveAttacker() == 0)
                {
                    Require(Verify(false, $"kill: no living {name} ({wcid}) near the point within {RespawnSeconds:0} s"), "kill: nothing to kill");
                }

                continue;
            }

            if (current != target)
            {
                target = current;
                defending = current == attacker && attacker != 0 && WcidOf(attacker) != wcid; // an attacker of the step's own kind is the step's creature, not a defence
                Log($"kill: target {NameOf(target)} (0x{target:X8}, wcid {WcidOf(target)}) at {Distance(target):0.0} m, health {KnownHealthOf(target)}{(defending ? " (an attacker: M5 D88)" : string.Empty)}");
                if (WarSpell(out var forWhy, out _, WcidOf(target)) is { } forIt && forIt.Id != spell!.Id)
                {
                    spell = forIt; // M6 14 the quest kill: the line that hits this creature hardest, as the travel fight-back's
                    Log($"kill: the war spell for {NameOf(target)} is {spell.Name} ({spell.Id}): {forWhy}");
                }
            }

            // M6 14 the quest kill: its bolts came to nothing: Harm Other (no projectile), or she runs past it
            SpellInfo? life = null;
            if (_lifeOnly.Contains(target))
            {
                life = LifeFor(target, out var lifeWhy);
                if (life is null)
                {
                    RunPast(target, NameOf(target), "its bolts came to nothing and no life spell answers it (" + lifeWhy + ")");
                    (target, defending) = (0, false);
                    continue;
                }
            }

            var cost = life ?? spell!;

            if (S.State.Objects.TryGet(target, out var seen) && seen.Position is { } pos)
            {
                if (!defending)
                {
                    victim.Guid = target;
                    victim.Name = seen.Name ?? name;
                    victim.LastSeen = pos;
                }
            }

            // M6 8 L: the mana comes before the walk: with nothing to throw she walks after nobody (live: a 233 m chase at mana 0). An attacker does not end this wait, for she has nothing to answer it with; whether she may stand here is the survival layer's to say, above this loop.
            if (!ManaFor(cost))
            {
                // M6 14 the quest kill (M5 D88, the owner's dance): the stamina pays the mana first; the wait stands only when the dance cannot run
                Log($"kill: mana {Mana()} is below {cost.Name}'s {cost.BaseMana}: the stamina dance first (target 0x{target:X8}, {Distance(target):0.0} m)");
                foreach (var h in DanceFor(cost)) { yield return h; }

                if (!ManaFor(cost))
                {
                    Log($"kill: mana {Mana()} is still below {cost.Name}'s {cost.BaseMana}: no cast and no walk; waiting up to {RestSeconds:0} s for it");
                    foreach (var h in Until(() => ManaFor(cost), RestSeconds)) { yield return h; }

                    Require(Verify(ManaFor(cost), $"kill: the mana did not come back for {cost.Name} within {RestSeconds:0} s"), "kill: no mana");
                }

                continue;
            }

            if (Distance(target) > CastRange)
            {
                foreach (var h in WalkTo(target, NameOf(target), CastRange - 3)) { yield return h; }

                continue;
            }

            // M6 8 L: the shot is checked before it is paid for (the server's own launch test, ShotCheck.cs): a blocked shot is not cast
            var aimName = NameOf(target);
            var shot = life is not null ? default : _shots.Before(S.Actions.Shots, S, spell!.Id, target, aimName); // a life spell has no projectile: no shot to check
            if (life is null && shot.Answer == ShotAnswer.Blocked)
            {
                if (!defending && wcid != 0 && _shotWalked.Add(target))
                {
                    var closer = Math.Max(Distance(target) / 2, UseRange);
                    Log(string.Create(CultureInfo.InvariantCulture, $"kill: no shot at {aimName} (0x{target:X8}) from here: one walk to within {closer:0.0} m for a clear line"));
                    foreach (var h in WalkTo(target, aimName, closer)) { yield return h; }

                    continue;
                }

                _shots.Drop(target, aimName, S.Clock.Now, "the shot is blocked: " + shot.Why);
                (target, defending) = (0, false);
                continue;
            }

            // M6 6 R1G: the check before each cast: the magic mode (check, act, check; the shared step), not reached is the named stop
            foreach (var h in WarBoltPick.MagicBeforeCast(S, 5, Log)) { yield return h; }

            Require(Verify(S.State.Self.Qualities.CombatMode == CombatModes.Magic, $"kill: no-magic-mode: the magic mode was not reached within 5 s before a bolt (the mode is {S.State.Self.Qualities.CombatMode})"), "kill: no magic mode");
            var mark = S.State.Chat.Total;
            var cast = S.Actions.Cast(cost.Id, target, ActionSource.Automation); // M6 15: the kill's bolt is the automation's cast, so the player's rule holds for it (the spell is on a spell bar), as for the fight-back's
            yield return cast;
            Casts++;
            Log(string.Create(CultureInfo.InvariantCulture, $"cast {cost.Name} at 0x{target:X8} ({Distance(target):0.0} m, health {KnownHealthOf(target)}): {Describe(cast)}; mana {Mana()}"));
            if (WarBoltPick.IsComponentRefusal(cast, ChatRefusalSince(mark)))
            {
                // M6 6 R1G: a bolt refused for a missing component (0x0400) is dropped for this leg and the next is picked at once; none left is the named stop
                _boltsRefused.Add(cost.Id);
                Log($"kill: {cost.Name} was refused for a missing component (0x0400): dropped for this leg; the next {(life is null ? "bolt" : "spell")} is picked");
                if (life is not null)
                {
                    continue; // LifeStrike leaves the refused level out at the next pick
                }

                spell = WarSpell(out bolt, out lack, WcidOf(target));
                Require(Verify(spell is not null, $"kill: no-components: {spell?.Name ?? bolt} (0x0400 on {Casts} cast(s))", 0, mark), "kill: no components");
                Log($"kill: the war spell is now {spell!.Name} ({spell.Id}): {bolt}");
                continue;
            }

            if (cast.Outcome is { Code: CastAttempt.BusyCode })
            {
                foreach (var h in Pause(1)) { yield return h; }
            }

            // M6 8 L: the server made no bolt it let fly (the launch test failed, the mana was already spent): nothing to wait for
            if (cast.Outcome is { Kind: ActionOutcomeKind.ServerError, Reason: ActionReason.NoEffect } && Alive(target) && BoltLineSince(mark, aimName) == MagicLineKind.Unparsed)
            {
                if (_shots.NoteWasted(target, aimName, S.Clock.Now, "the cast ended with no bolt: " + Describe(cast)))
                {
                    (target, defending) = (0, false);
                }

                continue;
            }

            var dying = target;
            var dyingName = aimName; // M6 8 L: read while it lives: a deleted object's name is "?", which left the name on the attacker list for any creature of that name at any distance (the "attacker" 42 m away)
            foreach (var h in Until(() => !Alive(dying) || BoltLineSince(mark, dyingName) != MagicLineKind.Unparsed, 2.0)) { yield return h; }

            if (Alive(dying))
            {
                NoteHurt(dying, dyingName, mark, life is not null, cast.Outcome is { Kind: ActionOutcomeKind.Confirmed }); // M6 14 the quest kill: bolts that do not hurt it switch to Life Magic; life casts that do not hurt it pass it
                if (PassedBy(dying))
                {
                    (target, defending) = (0, false);
                    continue;
                }
            }

            if (!Alive(dying))
            {
                Kills++;
                _shots.NoteDamage(dying);
                Log($"kill: {dyingName} (0x{dying:X8}) is dead after {Casts} cast(s) in all{(defending ? " (an attacker)" : string.Empty)}");
                ForgetAttacker(dyingName);
                if (!defending && wcid != 0)
                {
                    yield break;
                }

                target = 0;
                defending = false;
                continue;
            }

            var landed = BoltLineSince(mark, dyingName);
            if (landed is MagicLineKind.DamageDealt or MagicLineKind.DrainDealt)
            {
                _shots.NoteDamage(dying);
            }
            else if (landed == MagicLineKind.Unparsed && cast.Outcome is { Kind: ActionOutcomeKind.Confirmed } && _shots.NoteWasted(dying, dyingName, S.Clock.Now, "the bolt flew and no damage landed within 2 s"))
            {
                (target, defending) = (0, false);
            }
        }

        Require(Verify(false, $"kill: {name} not killed within {KillSeconds:0} s"), "kill: timeout");
    }

    /// <summary>The corpse of <paramref name="victim"/>: check (it shows near where the creature died), act (walk up, open it, pick the item up), check (the item is in the pack), with one recovery (another PickUp).</summary>
    protected IEnumerable<ActionHandle?> Loot(Victim victim, QuestThing item)
    {
        var wcid = item.Wcid!.Value;
        var corpseName = MageLootRules.CorpseNameOf(victim.Name);
        foreach (var h in Until(() => CorpseNear(corpseName, victim.LastSeen) != 0, CorpseSeconds)) { yield return h; }

        var corpse = CorpseNear(corpseName, victim.LastSeen);
        if (!Verify(corpse != 0, $"loot: no '{corpseName}' within 10 m of where 0x{victim.Guid:X8} died"))
        {
            yield break;
        }

        foreach (var h in WalkTo(corpse, corpseName, UseRange)) { yield return h; }

        for (var attempt = 0; attempt < 2 && InContainer(wcid, corpse) == 0; attempt++)
        {
            if (attempt == 1)
            {
                Recoveries++;
                Log($"recovery=open-again: {corpseName} shows no {item.Name} {AfterSeconds:0} s after the Open ({Distance(corpse):0.0} m away); one more Open (the server's move-to walks the rest)");
            }

            var open = S.Actions.OpenContainer(corpse, ActionSource.Host);
            yield return open;
            Log($"loot: OpenContainer {corpseName} (0x{corpse:X8}) from {Distance(corpse):0.0} m: {Describe(open)}");
            foreach (var h in Until(() => InContainer(wcid, corpse) != 0, AfterSeconds)) { yield return h; }
        }

        var inside = InContainer(wcid, corpse);
        if (Verify(inside != 0, $"loot: {corpseName} does not hold {item.Name} ({wcid}); it holds {Contents(corpse)}"))
        {
            for (var attempt = 0; attempt < 2 && InPack(wcid).Count == 0; attempt++)
            {
                if (attempt == 1)
                {
                    Recoveries++;
                    Log($"recovery=loot-again: {item.Name} is not in the pack {AfterSeconds:0} s after the PickUp; one more PickUp");
                }

                var mark = S.State.Chat.Total;
                var take = S.Actions.PickUp(inside, 0, 0, ActionSource.Host);
                yield return take;
                Log($"loot: PickUp {item.Name} (0x{inside:X8}) from {corpseName}: {Describe(take)}");
                foreach (var h in Until(() => InPack(wcid).Count > 0, AfterSeconds)) { yield return h; }

                if (InPack(wcid).Count == 0)
                {
                    Warn(Stage, $"loot: {item.Name} is not in the pack after the PickUp ({Describe(take)})", inside, mark);
                }
            }
        }

        if (S.State.Containers.ViewedGuid == corpse)
        {
            var close = S.Actions.CloseContainer(corpse, ActionSource.Host);
            yield return close;
            Log($"loot: CloseContainer {corpseName}: {Describe(close)}");
        }
    }

    /// <summary>Kills the living attackers (M5 D88), then goes back to peace; nothing when none attacked.</summary>
    protected IEnumerable<ActionHandle?> DefendIfAttacked()
    {
        if (LiveAttacker() == 0)
        {
            yield break;
        }

        Log($"defend: attacked by {string.Join(", ", _attackers.Keys)} (M5 D88: the character fights back)");
        var nobody = new Victim();
        var here = S.State.Self.Position!.Value;
        foreach (var h in KillOne(0, "the attackers", here.CellId, here.Origin, nobody)) { yield return h; }

        foreach (var h in PeaceMode()) { yield return h; }
    }

    /// <summary>A caster in the hands and the magic combat mode: check (a caster held, the mode), act (wield the pack's caster, at the held slot as the recovery table does; set the mode), check, with one recovery (unwield what the hands hold, then wield again).</summary>
    protected IEnumerable<ActionHandle?> ReadyMagic()
    {
        if (!HeldCaster.Held(S.State))
        {
            var caster = HeldCaster.Rearm(S.State, Quartermaster.Wardrobe.For(S.State).RearmJudge(), out var why);
            Require(Verify(caster is not null, $"magic: no caster in the packs to wield ({why}); TODO(m6-academy-no-caster)"), "magic: no caster");
            for (var attempt = 0; attempt < 2 && !HeldCaster.Held(S.State); attempt++)
            {
                if (attempt == 1)
                {
                    Recoveries++;
                    var other = CombatModeGuard.Read(S.State).Other;
                    Log($"recovery=unwield-then-wield: the caster is not held after the Wield; unwielding {other?.Name ?? "nothing"} first");
                    if (other is not null)
                    {
                        var off = Quartermaster.Wardrobe.SendTakeOff(S, new Quartermaster.WieldBlocker(other.Guid, other.Name) { Source = ActionSource.Host }); // M6 16 E5e1: the wear service's take-off
                        yield return off;
                        Log($"magic: Unwield {other.Name} (0x{other.Guid:X8}): {Describe(off)}");
                    }
                }

                // M6 16 E5e1: the one wield routine: the wield at the held slot, its answer logged, then the wait for a caster in the hands
                var mark = S.State.Chat.Total;
                var wield = new Quartermaster.WieldResult();
                var host = new Quartermaster.WieldHost(Until)
                {
                    Sending = _ => mark = S.State.Chat.Total,
                    Answered = r => Log($"magic: Wield {caster!.Name} (0x{caster.Guid:X8}, {why}): {r.Outcome(Describe)}"),
                };
                foreach (var h in Quartermaster.Wardrobe.PutOn(S, new Quartermaster.WieldRequest(caster!.Guid) { Name = caster.Name, Location = SelfEquipJudge.Held, Source = ActionSource.Host, WornSeconds = AfterSeconds, Worn = () => HeldCaster.Held(S.State) }, wield, host)) { yield return h; }

                if (!HeldCaster.Held(S.State))
                {
                    Warn(Stage, $"magic: {caster.Name} is not held after the Wield ({wield.Outcome(Describe)})", caster.Guid, mark);
                }
            }

            Require(Verify(HeldCaster.Held(S.State), "magic: no caster held after the recovery"), "magic: no caster held");
        }

        if (S.State.Self.Qualities.CombatMode != CombatModes.Magic)
        {
            var mode = S.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
            yield return mode;
            Log($"magic: SetCombatMode Magic: {Describe(mode)}");
            foreach (var h in Until(() => S.State.Self.Qualities.CombatMode == CombatModes.Magic, AfterSeconds)) { yield return h; }

            Require(Verify(S.State.Self.Qualities.CombatMode == CombatModes.Magic, $"magic: the combat mode is {S.State.Self.Qualities.CombatMode} after asking for magic ({Describe(mode)})"), "magic: no magic mode");
        }
    }

    protected IEnumerable<ActionHandle?> PeaceMode()
    {
        if (S.State.Self.Qualities.CombatMode == CombatModes.NonCombat)
        {
            yield break;
        }

        var mode = S.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        yield return mode;
        foreach (var h in Until(() => S.State.Self.Qualities.CombatMode == CombatModes.NonCombat, AfterSeconds)) { yield return h; }

        Verify(S.State.Self.Qualities.CombatMode == CombatModes.NonCombat, $"peace: the combat mode is {S.State.Self.Qualities.CombatMode} after asking for peace ({Describe(mode)})");
    }

    /// <summary>
    /// The war spell (M6 6 E, <c>m6-fight-back-war-spell</c>): <see cref="WarBoltPick.Choose"/> over the book at <see cref="WarBoltPick.DefaultMinChance"/>, the run leg's rule (the lead's ruling: one rule for both; it was the highest level, then the
    /// least mana): the highest power whose chance on the current War Magic skill is at least 0.5, else the lowest; <paramref name="why"/> says which.
    /// </summary>
    protected SpellInfo? WarSpell(out string why) => WarSpell(out why, out _);

    /// <summary>
    /// M6 6 R1G (live round 1: the run leg's fight-back cast a bolt into 0x0400 four times; this station had the same blind pick): <see cref="WarBoltPick.ChooseCastable"/> over the book, the pack's components (<see cref="LevelFloor.HeldForAccount"/>, the foci formula)
    /// and the bolts ACE refused for a component on this leg left out; <paramref name="lack"/> says why a null pick is null.
    /// </summary>
    protected SpellInfo? WarSpell(out string why, out BoltLack lack) => WarSpell(out why, out lack, 0);

    /// <summary>M6 14 the quest kill: as <see cref="WarSpell(out string, out BoltLack)"/>, the line that hits <paramref name="wcid"/> hardest (<see cref="WarBoltPick.ChooseCastableFor"/>, the travel fight-back's pick; 0: no target).</summary>
    protected SpellInfo? WarSpell(out string why, out BoltLack lack, uint wcid)
    {
        var skill = GearCasts.TryCurrentSkill(S.State.Self.Qualities, MageTemplate.WarMagic, out var current, out var skillWhy) ? current : (uint?)null;
        var held = LevelFloor.HeldForAccount(S.State, MagicSchool.WarMagic, null);
        var spell = WarBoltPick.ChooseCastableFor(Context.Spells, S.State.Self.Spellbook.Spells.Keys, skill, WarBoltPick.DefaultMinChance, held, null, _boltsRefused, wcid, out why, out lack);
        if (skill is null && spell is not null)
        {
            why += " (" + skillWhy + ")";
        }

        return spell;
    }

    /// <summary>M6 6 R1G: the bolts ACE refused for a missing component on this leg (never cast again on it).</summary>
    private readonly HashSet<uint> _boltsRefused = [];

    /// <summary>M6 8 L: what the magic chat since <paramref name="mark"/> says of a bolt at <paramref name="name"/>: damage, a drain, a resist, or <see cref="MagicLineKind.Unparsed"/> for nothing yet.</summary>
    private MagicLineKind BoltLineSince(long mark, string name)
    {
        var chat = S.State.Chat;
        var fresh = (int)Math.Min(chat.Count, chat.Total - mark);
        for (var i = chat.Count - fresh; i < chat.Count; i++)
        {
            if (chat[i].ChatType == FightTally.MagicChatType && chat[i].Kind == ChatKind.System && chat[i].Text.Contains(name, StringComparison.Ordinal)
                && FightTally.Classify(chat[i].Text, out _, out _) is MagicLineKind.DamageDealt or MagicLineKind.DrainDealt or MagicLineKind.Resist)
            {
                return FightTally.Classify(chat[i].Text, out _, out _);
            }
        }

        return MagicLineKind.Unparsed;
    }

    /// <summary>M6 8 L: the casts this leg wasted and the shots it held back, the fields a round reads.</summary>
    protected string ShotFields => _shots.Fields;

    private void OnTeleport(in PlayerTeleportAnnounced e) => Teleports++;

    private void OnCombat(in CombatEvent e)
    {
        if (e.Kind is CombatEventKind.DefenseHit or CombatEventKind.DefenseEvaded && !string.IsNullOrEmpty(e.Who) && S is not null)
        {
            var first = !_attackers.ContainsKey(e.Who);
            _attackers[e.Who] = (S.Clock.Now, CastRange);
            if (first)
            {
                Log($"attacked by {e.Who} ({e.Kind}): it is fought before the next act (M5 D88)");
            }
        }
    }

    /// <summary>M6 8 G: a hostile spell line names its caster an attacker too, the run leg's and the hunt's reading (<see cref="AttackWatch"/>): a bolt from afar must stop a walk as a bite does.</summary>
    private void OnChat(in ChatReceived e)
    {
        if (S is not null && e.Line.ChatType == FightTally.MagicChatType && e.Line.Kind == ChatKind.System && FightScenario.HostileSpellSource(e.Line.Text, out var source, out var how))
        {
            var first = !_attackers.ContainsKey(source);
            _attackers[source] = (S.Clock.Now, HostileReach.DefaultMetres);
            if (first)
            {
                Log($"attacked by {source} ({how}): it is fought before the next act (M5 D88)");
            }
        }
    }

    private uint LiveAttackerOwn()
    {
        if (_attackers.Count == 0 || S.State.Self.Position is not { } here)
        {
            return 0;
        }

        foreach (var (who, heard) in _attackers.ToList())
        {
            if (S.Clock.Now - heard.When > ForgetSeconds)
            {
                _attackers.Remove(who);
                continue;
            }

            if (NearestAttacker(who, in here, heard.Reach) is var guid and not 0)
            {
                return guid;
            }
        }

        return 0;
    }

    /// <summary>
    /// M6 8 L: the living creature called <paramref name="name"/> nearest the player within <paramref name="reach"/> metres and not dropped by the shot rule, or 0. The list holds names and the notices name no guid, so the reach keeps a fought attacker to what could have attacked: <see cref="CastRange"/> for a
    /// bite or a blow (she answers it from where she stands, with no walk), <see cref="HostileReach.DefaultMetres"/> for a spell line (the walks' own reach).
    /// M6 16 E2b (the one world picture): each creature is measured where the session's <see cref="IGameSessionView.Live"/> says it stands (the body the client draws when it draws one and the body can be trusted, else the
    /// server's last report), as <see cref="AttackerStopsWalk"/> and <see cref="DroppedAndFar"/> beside it read it; a session that draws no bodies answers from the report, as before.
    /// </summary>
    private uint NearestAttacker(string name, in CellFrame here, double reach)
    {
        uint best = 0;
        var bestDistance = double.MaxValue;
        var origin = here.ToGlobal();
        var live = S.Live;
        foreach (var o in S.State.Objects.All)
        {
            if (o.Guid == S.State.Self.Guid || o.Position is null || !string.Equals(o.Name, name, StringComparison.Ordinal) || (o.ItemType & CreatureItemType) == 0 || !Alive(o.Guid) || live.At(o) is not { } p)
            {
                continue;
            }

            var d = p.ToGlobal().DistanceTo(origin);
            if (d <= reach && d < bestDistance && !_shots.IsDropped(o.Guid, S.Clock.Now, d) && !PassedBy(o.Guid))
            {
                (best, bestDistance) = (o.Guid, d);
            }
        }

        return best;
    }

    protected void ForgetAttacker(string name) => _attackers.Remove(name);

    /// <summary>Finds the NPC or object of <paramref name="wcid"/> nearest the player in its landblock (the go step before brought it in view) and walks up to it when it is out of reach.</summary>
    protected IEnumerable<ActionHandle?> Approach(uint wcid, string what, Action<uint> found)
    {
        foreach (var h in Until(() => NearestOfWcid(wcid) != 0, NpcSeconds)) { yield return h; }

        var guid = NearestOfWcid(wcid);
        Require(Verify(guid != 0, $"{what} ({wcid}) is not in the object table (the go step's room {LastPlace})"), $"{what} not in view");
        if (Distance(guid) > UseRange + 1)
        {
            foreach (var h in WalkTo(guid, what, UseRange)) { yield return h; }
        }

        found(guid);
    }

    /// <summary>
    /// Brings the player to the object <paramref name="guid"/> for the Use, PickUp or Open the caller sends next (never a place). M6 0 F4, check, act, check: within <paramref name="range"/> nothing is done; within <see cref="UseReachMetres"/> the walk is skipped and the
    /// caller's act lets the server's move-to walk the rest (<c>reach=use</c>); beyond it the journey's runner walks (<c>reach=walk</c>) with one named recovery (<see cref="WalkWithRecovery"/>). Each choice is logged with the distance.
    /// </summary>
    protected IEnumerable<ActionHandle?> WalkTo(uint guid, string what, double range)
    {
        var d = Distance(guid);
        if (ReachOf(d, range) == AcademyReach.InRange || !S.State.Objects.TryGet(guid, out var o) || o.Position is not { } p)
        {
            yield break;
        }

        if (ReachOf(d, range) == AcademyReach.Use)
        {
            _usedReach++;
            Log(string.Create(CultureInfo.InvariantCulture, $"reach=use: {what} (0x{guid:X8}) is {d:0.0} m away, within the use reach {UseReachMetres:0.0} m (range {range:0.0} m): no walk; the act that follows lets the server's move-to walk the rest"));
            yield break;
        }

        _walkedReach++;
        Log(string.Create(CultureInfo.InvariantCulture, $"reach=walk: {what} (0x{guid:X8}) is {d:0.0} m away, beyond the use reach {UseReachMetres:0.0} m: walking to within {range:0.0} m"));
        foreach (var h in WalkWithRecovery(new TravelRequest(Destination(what, p.CellId, p.Origin), TravelPurpose.Cell, guid, range), what)) { yield return h; }

        if (S.State.Objects.TryGet(guid, out var after) && Distance(guid) <= UseReachMetres && after.Position is not null)
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"reach=use: {what} is {Distance(guid):0.0} m away after the walk, within the use reach: the act that follows lets the server's move-to walk the rest"));
        }
    }

    /// <summary>
    /// The station's walk with one named recovery per step (M6 0 F4): the travel runs optional; when the wayfinder refuses it (the physics pre-check at segment 0, a block) the station logs <c>recovery=step-away</c>, takes a short step back to the last point the
    /// wayfinder confirmed walkable (<see cref="_walkable"/>; none, or under <see cref="StepAwayMinMetres"/> away, is said and skipped) and plans the walk again, which, refused again, fails the step by name (the shared TravelTo throws after its CHECK FAILED line). Never a place.
    /// </summary>
    /// <param name="request">The walk.</param>
    /// <param name="what">The step's words for the log.</param>
    /// <param name="targetMetres">How far the step's person stands from her now, null when unseen or when the step has none. Long round 4: the go to Worcer aimed at his own standing point, and his body stopped her 2.1 m short, three plans running:
    /// a refused walk that left her within <see cref="UseReachMetres"/> of the person is in reach (the give that follows lets the server's move-to walk the rest), not a failed step.</param>
    protected IEnumerable<ActionHandle?> WalkWithRecovery(TravelRequest request, string what, Func<double?>? targetMetres = null)
    {
        foreach (var h in TravelFightingBack(request, what, optional: true)) { yield return h; }

        if (LastTravelFailure is null)
        {
            RememberWalkable();
            yield break;
        }

        RequireNoShutDoor(what, LastTravelFailure);
        if (targetMetres?.Invoke() is { } near && near <= UseReachMetres)
        {
            _usedReach++;
            Log(string.Create(CultureInfo.InvariantCulture, $"reach=use: the walk to {what} was refused ({LastTravelFailure}), but its person stands {near:0.0} m away, within the use reach {UseReachMetres:0.0} m: in reach; the act that follows lets the server's move-to walk the rest"));
            yield break;
        }

        Recoveries++;
        var refused = LastTravelFailure;
        var here = S.State.Self.Position;
        if (_walkable is { } back && here is { } now && StepAwayWorthIt(now.CellId, now.Origin, back.Cell, back.At))
        {
            Log(string.Create(CultureInfo.InvariantCulture, $"recovery=step-away: the walk to {what} was refused ({refused}); stepping back {Vector3.Distance(now.Origin, back.At):0.0} m to the last walkable point ({back.At.X:0.00}, {back.At.Y:0.00}, {back.At.Z:0.00}) in cell 0x{back.Cell:X8}, then planning again"));
            foreach (var h in TravelFightingBack(new TravelRequest(Destination("the last walkable point", back.Cell, back.At), TravelPurpose.Cell), "the last walkable point", optional: true)) { yield return h; }

            if (LastTravelFailure is not null)
            {
                Log($"recovery=step-away: the step back failed too ({LastTravelFailure}); planning the walk again from where the player stands");
            }
        }
        else
        {
            Log($"recovery=step-away: the walk to {what} was refused ({refused}); no probed walkable point 1.0 m or more away on record: planning again from where the player stands");
        }

        foreach (var h in TravelFightingBack(request, what)) { yield return h; } // refused again: the step fails by name (CHECK FAILED, travel to ...)

        RememberWalkable();
    }

    /// <summary>
    /// M6 8 G (<c>m6-quest-go-fight-back</c>; M5 D88: she always fights back an attacker): the walk of <see cref="StationLeg.TravelTo"/>, stopped when she is attacked (<see cref="AttackerStopsWalk"/>), the attackers fought (<see cref="DefendIfAttacked"/>, the station's one fight-back), then the same walk to the same
    /// place walked again, one line at each side and the check after. A walk cut this way is not a failed walk. A walk the walker refuses after the fight falls to the caller's recovery (<see cref="WalkWithRecovery"/>'s step-away, then its plan again, which fails by name).
    /// <para>
    /// The fight's own answer stands. <b>An attacker the bolts cannot hurt</b> (a fire-immune creature, a mage with only a fire bolt): <see cref="KillOne"/> casts at it to <see cref="KillSeconds"/> and ends the step by name (<c>kill: timeout</c>); she does not run past it. A step cannot rest forever: no mana is
    /// <c>kill: no mana</c>, no bolt <c>kill: no war spell</c>. <see cref="MaxWalkFightBacks"/> stops an endless walk.
    /// </para>
    /// <para>
    /// <b>Where it stands down</b>, by design: a kill (<c>_killing</c>: the kill's own order fights attackers first); a body not present or at health 0 (<see cref="AttackerStopsWalk"/>: a portal hop in flight, a teleport under way, a death not yet handled); a fight during which the server moved the body (a death's respawn,
    /// a portal: the walk ends by name, never resumed from the wrong place). The urgent recall is a cast of the autopilot's own state: no walk of this station runs there.
    /// </para>
    /// </summary>
    /// <param name="request">The walk.</param>
    /// <param name="what">The walk in words (logged).</param>
    /// <param name="optional">As <see cref="StationLeg.TravelTo"/>.</param>
    protected IEnumerable<ActionHandle?> TravelFightingBack(TravelRequest request, string what, bool optional = false)
    {
        if (!WalksFightBack)
        {
            foreach (var h in TravelTo(request, what, optional)) { yield return h; }

            yield break;
        }

        for (var fights = 0; ; fights++)
        {
            foreach (var h in TravelTo(request, what, optional, AttackerStopsWalk)) { yield return h; }

            if (TravelInterrupted is not { } stopped)
            {
                if (fights > 0)
                {
                    Log(string.Create(CultureInfo.InvariantCulture, $"walk fight-back: check after: the walk to {what} went on after {fights} fight-back(s): {(LastTravelFailure is null ? "it arrived" : "the walker refused it: " + LastTravelFailure)}, cell 0x{S.State.Self.Position?.CellId ?? 0:X8}"));
                }

                yield break;
            }

            Require(Verify(fights < MaxWalkFightBacks, $"walk: fight-back-cap: the walk to {what} was stopped by attackers {fights + 1} times (at most {MaxWalkFightBacks}): {stopped}"), "walk: fight-back cap");
            WalkFightBacks++;
            var teleportsBefore = Teleports;
            Log(string.Create(CultureInfo.InvariantCulture, $"walk fight-back {fights + 1}: check before: the walk to {what} is stopped: {stopped}; health {(S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var hp, out var hpMax) ? $"{hp}/{hpMax}" : "?")}, mana {Mana()}, cell 0x{S.State.Self.Position?.CellId ?? 0:X8}"));
            foreach (var h in AsWalkFailure(DefendIfAttacked())) { yield return h; } // M6 14 the quest kill: a fight-back that fails is a failed walk (go:walk-fight:...), the errand's walk rule

            Require(Verify(Teleports == teleportsBefore, $"walk: moved-in-fight: the server moved the player during the fight-back on the walk to {what} (a death's respawn or a portal): the walk is not resumed from there"), "walk: moved in the fight");
            Log(string.Create(CultureInfo.InvariantCulture, $"walk fight-back {fights + 1}: no attacker left; the same walk to {what} goes on from cell 0x{S.State.Self.Position?.CellId ?? 0:X8}"));
        }
    }

    /// <summary>
    /// M6 8 G, what "attacked" means on a walk (the run leg's rule, <c>TravelLegSafety</c>; M5 7 P7u's reach): a creature heard attacking her (a damage or evasion notice, or a hostile spell line) within <see cref="HuntScenario.DefendHeardSeconds"/>, alive, a creature that may be attacked, within
    /// <see cref="HostileReach.DefaultMetres"/>. Null: nothing stops the walk (and always null inside a kill, with no body present, or at health 0).
    /// </summary>
    private string? AttackerStopsWalk()
    {
        if (_killing > 0 || _attackers.Count == 0 || S.State.Self.Travel != SelfTravel.Present || (S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxHealth, out var health, out _) && health == 0))
        {
            return null;
        }

        _heard.Clear();
        foreach (var (who, (when, _)) in _attackers)
        {
            if (S.Clock.Now - when <= HuntScenario.DefendHeardSeconds)
            {
                _heard.Add(who);
            }
        }

        // M6 8 (the long round): a walk stops only for an attacker the fight-back would take, or the two go round until the cap ends the errand.
        return HostileReach.TryNearest(S, HostileReach.DefaultMetres, _heard, out var guid, out var name, out var metres, DroppedAndFar) && LiveAttacker() != 0
            ? string.Create(CultureInfo.InvariantCulture, $"'{name}' 0x{guid:X8} attacked the player and stands {metres:0.0} m away (within {HostileReach.DefaultMetres:0} m): fight back (M5 D88)")
            : null;
    }

    /// <summary>True for a creature the shot rule still leaves alone from where she stands.</summary>
    private bool DroppedAndFar(uint guid) =>
        PassedBy(guid) || S.State.Self.Position is { } here && S.State.Objects.TryGet(guid, out var o) && S.Live.At(o) is { } at && _shots.IsDropped(guid, S.Clock.Now, at.ToGlobal().DistanceTo(here.ToGlobal()));

    /// <summary>
    /// True when a walk that aimed at <paramref name="goalCell"/> at <paramref name="goalAt"/> was taken by the portal on touch: the client saw a teleport since the walk began (<paramref name="teleports"/>, accepted F751 PlayerTeleports) and the player is in another
    /// landblock or <see cref="TouchTeleportMetres"/> or more from the goal. A walk that ended short with no teleport is a walk failure (M6 0 F6, live round 7: a shut door 41.4 m from the portal was taken for a touch).
    /// </summary>
    /// <param name="teleports">The teleports the client saw since the walk began.</param>
    /// <param name="cell">The player's cell after the walk.</param>
    /// <param name="at">The player's position after the walk.</param>
    /// <param name="goalCell">The walk's goal cell.</param>
    /// <param name="goalAt">The walk's goal point.</param>
    public static bool TouchTeleported(int teleports, uint cell, Vector3 at, uint goalCell, Vector3 goalAt) => teleports > 0 && (cell >> 16 != goalCell >> 16 || Vector3.Distance(at, goalAt) >= TouchTeleportMetres);

    /// <summary>The door the walker's <paramref name="failure"/> names as shut after its two Uses (<see cref="DoorShutFailure"/>: <c>door-closed-timeout: door 0x7860204D did not open ...</c>), or 0 for any other failure (M6 0 F6).</summary>
    /// <param name="failure">The travel's failure text, or null.</param>
    public static uint ShutDoorIn(string? failure)
    {
        const string Marker = DoorShutFailure + ": door 0x";
        var at = failure?.IndexOf(Marker, StringComparison.Ordinal) ?? -1;
        if (at < 0 || failure!.Length < at + Marker.Length + 8)
        {
            return 0;
        }

        return uint.TryParse(failure.AsSpan(at + Marker.Length, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var door) ? door : 0;
    }

    /// <summary>
    /// A door that stayed shut after the walker's two Uses is a closed door for the rest of this walk (M6 0 F6): the walker plans no way round it (<c>TODO(door-locked-replan)</c>, item 8), so the step-away recovery would plan the same route through the same door; the
    /// step fails by name instead, naming the door and whether the server said it is locked. No silent retry, never a teleport.
    /// </summary>
    protected void RequireNoShutDoor(string what, string? failure)
    {
        var door = ShutDoorIn(failure);
        if (door == 0)
        {
            return;
        }

        var name = S.State.Objects.TryGet(door, out var d) ? $"{d.Name} (wcid {d.Weenie.WeenieClassId})" : "not in the object table";
        Require(Verify(false, $"travel to {what}: door 0x{door:X8} ({name}, locked {LockedText(door)}) stayed shut after the walker's two Uses: a closed door for the rest of this walk, no other way planned (TODO(door-locked-replan)); {failure}", door), "travel: door shut");
    }

    protected bool Unlocked(uint door) => S.State.Objects.TryGet(door, out var d) && (d.Locked == false || (d.State & Lifestone.Net.Types.PhysicsState.Ethereal) != 0);

    protected string LockedText(uint door) => S.State.Objects.TryGet(door, out var d) && d.Locked is { } l ? (l ? "yes" : "no") + (d.LockedFrom is { } from ? " (" + from + ")" : string.Empty) : "unknown";

    /// <summary>What the station does about an object <paramref name="distance"/> metres away that its next act needs within <paramref name="range"/> (M6 0 F4): nothing, the act alone (the server walks the rest), or its own walk first.</summary>
    /// <param name="distance">The metres to the object.</param>
    /// <param name="range">The range the act is sent from.</param>
    public static AcademyReach ReachOf(double distance, double range) => distance <= range ? AcademyReach.InRange : distance <= UseReachMetres ? AcademyReach.Use : AcademyReach.Walk;

    /// <summary>True when the step-away recovery has somewhere to go: the last walkable point is in the same landblock as the player and <see cref="StepAwayMinMetres"/> or more away.</summary>
    /// <param name="cell">The player's cell.</param>
    /// <param name="at">The player's position.</param>
    /// <param name="walkableCell">The last walkable point's cell.</param>
    /// <param name="walkableAt">The last walkable point.</param>
    public static bool StepAwayWorthIt(uint cell, Vector3 at, uint walkableCell, Vector3 walkableAt) => cell >> 16 == walkableCell >> 16 && Vector3.Distance(at, walkableAt) >= StepAwayMinMetres;

    protected void RememberWalkable()
    {
        if (S.State.Self.Position is { } at)
        {
            _walkable = (at.CellId, at.Origin);
        }
    }

    protected IEnumerable<ActionHandle?> StampCheck(QuestStep step)
    {
        if (step.Expect.Stamp is not { } stamp)
        {
            yield break;
        }

        var held = false;
        for (var ask = 1; ask <= (int)AfterSeconds && !held; ask++)
        {
            var has = S.Tools.HasQuest(stamp, ActionSource.Host);
            yield return has;
            held = has.Result is { Held: true };
            Log($"{QuestStepKinds.Word(step.Kind)}: the quest stamp {stamp} asked (ask {ask}): held={Yes(held)}{(has.Outcome is { Kind: ActionOutcomeKind.Confirmed } ? string.Empty : " (" + Describe(has) + ")")}");
            if (!held)
            {
                foreach (var h in Pause(1)) { yield return h; }
            }
        }

        Verify(held, $"{QuestStepKinds.Word(step.Kind)}: the quest stamp {stamp} is not held {AfterSeconds:0} s after the act");
    }

    protected IEnumerable<ActionHandle?> Wear(QuestThing piece)
    {
        var wcid = piece.Wcid!.Value;
        if (WornWcid(wcid))
        {
            WornCount++;
            yield break;
        }

        var guid = InPack(wcid).FirstOrDefault();
        var valid = S.State.Objects.TryGet(guid, out var held) ? held.Weenie.ValidLocations ?? 0u : 0u;
        if (guid == 0 || valid == 0)
        {
            Log($"wear: {piece.Name} ({wcid}) is not a piece to wear (valid locations 0x{valid:X8})");
            yield break;
        }

        var plan = PathwardenWear.Plan(guid, WornNow(), PathwardenWear.CoverageOf(S.State, guid), valid, OwnPieces);
        if (plan.Blocker is { } blocker)
        {
            Warn("wear", $"{piece.Name} ({wcid}) is left in the pack: {blocker.Name} (0x{blocker.Guid:X8}), one of the Academy's own pieces, covers its parts 0x{plan.Coverage:X}", guid);
            yield break;
        }

        foreach (var inTheWay in plan.TakeOff)
        {
            var off = Quartermaster.Wardrobe.SendTakeOff(S, new Quartermaster.WieldBlocker(inTheWay.Guid, inTheWay.Name) { Source = ActionSource.Host }); // M6 16 E5e1: the wear service's take-off
            yield return off;
            Log($"wear: Unwield {inTheWay.Name} (0x{inTheWay.Guid:X8}, covers 0x{inTheWay.Coverage:X}) to free {piece.Name}: {Describe(off)}");
            foreach (var h in Until(() => !S.State.Self.Inventory.TryGetEquipped(inTheWay.Guid, out var e) || e.Location == 0, AfterSeconds)) { yield return h; }
        }

        for (var attempt = 0; attempt < 2 && !WornWcid(wcid); attempt++)
        {
            if (attempt == 1)
            {
                Recoveries++;
                Log($"recovery=wear-again: {piece.Name} is not worn {AfterSeconds:0} s after the Wield; one more Wield after a short pause");
                foreach (var h in Pause(1)) { yield return h; }
            }

            // M6 16 E5e1: the one wield routine: the slot by its one rule, the wield, its answer logged, then the wait for the piece's class in the worn set
            var mark = S.State.Chat.Total;
            var wield = new Quartermaster.WieldResult();
            var host = new Quartermaster.WieldHost(Until)
            {
                Sending = _ => mark = S.State.Chat.Total,
                Answered = r => Log($"wear: Wield {piece.Name} (0x{guid:X8}) at 0x{r.Location:X8} of valid 0x{valid:X8}: {r.Outcome(Describe)}"),
            };
            foreach (var h in Quartermaster.Wardrobe.PutOn(S, new Quartermaster.WieldRequest(guid) { Name = piece.Name, Valid = valid, Source = ActionSource.Host, WornSeconds = AfterSeconds, Worn = () => WornWcid(wcid) }, wield, host)) { yield return h; }

            if (!WornWcid(wcid))
            {
                Warn("wear", $"{piece.Name} is not worn after the Wield (attempt {attempt + 1}, {wield.Outcome(Describe)})", guid, mark);
            }
        }

        if (Verify(WornWcid(wcid), $"wear: {piece.Name} ({wcid}) is not worn", guid))
        {
            WornCount++;
        }
    }

    protected List<PathwardenWear.WornPiece> WornNow()
    {
        var list = new List<PathwardenWear.WornPiece>();
        foreach (var e in S.State.Self.Inventory.Equipped)
        {
            list.Add(S.State.Objects.TryGet(e.Guid, out var o)
                ? new PathwardenWear.WornPiece(e.Guid, o.Weenie.WeenieClassId, o.Name ?? "?", e.Location, e.Priority != 0 ? e.Priority : o.Weenie.Priority ?? 0)
                : new PathwardenWear.WornPiece(e.Guid, 0, "?", e.Location, e.Priority));
        }

        return list;
    }

    protected bool WornWcid(uint wcid) => HeldOf(wcid).Any(g => S.State.Self.Inventory.TryGetEquipped(g, out var e) && e.Location != 0);

    protected bool IsCreature(uint guid) => S.State.Objects.TryGet(guid, out var o) && (o.ItemType & CreatureItemType) != 0;

    protected bool Alive(uint guid) =>
        guid != 0 && S.State.Objects.TryGet(guid, out var o) && o.Health is not <= 0f && o.Motion is not { Forward: WorldObjectTable.DeadMotionLowWord };

    protected string NameOf(uint guid) => S.State.Objects.TryGet(guid, out var o) ? o.Name ?? "?" : "?";

    protected uint WcidOf(uint guid) => S.State.Objects.TryGet(guid, out var o) ? o.Weenie.WeenieClassId : 0;

    protected string HealthOf(uint guid) => S.State.Objects.TryGet(guid, out var o) && o.Health is { } f ? f.ToString("0.00", CultureInfo.InvariantCulture) : "?";

    protected string Mana() => S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var cur, out var max) ? $"{cur}/{max}" : "?";

    protected bool ManaFor(SpellInfo spell) => !S.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var cur, out _) || cur >= spell.BaseMana;

    /// <summary>
    /// The metres from her to <paramref name="guid"/> in her landblock, or <see cref="double.MaxValue"/>. M6 16 E2b (the one world picture): a creature is measured where the session's <see cref="IGameSessionView.Live"/> says
    /// it stands (the body the client draws when it draws one and the body can be trusted, else the server's last report), so the kill's range test agrees with the pick of its attacker (<see cref="NearestAttacker"/>); an
    /// object that is no creature (a corpse, a door, an item on the floor) stands where the server put it, and a session that draws no bodies answers from the report, as before.
    /// </summary>
    protected double Distance(uint guid) =>
        S.State.Self.Position is { } here && S.State.Objects.TryGet(guid, out var o) && ((o.ItemType & CreatureItemType) != 0 ? S.Live.At(o) : o.Position) is { } p && p.CellId >> 16 == here.CellId >> 16 ? Vector3.Distance(p.Origin, here.Origin) : double.MaxValue;

    /// <summary>The world object (not in a container) of <paramref name="wcid"/> nearest <paramref name="at"/> in <paramref name="cell"/>'s landblock within <paramref name="metres"/>, or 0.</summary>
    protected uint WorldObjectNear(uint wcid, uint cell, Vector3 at, double metres)
    {
        uint best = 0;
        var bestDistance = double.MaxValue;
        foreach (var o in S.State.Objects.All)
        {
            if (o.Weenie.WeenieClassId != wcid || o.Position is not { } p || p.CellId >> 16 != cell >> 16 || (o.ContainerId is { } c && c != 0))
            {
                continue;
            }

            var d = Vector3.Distance(p.Origin, at);
            if (d < bestDistance)
            {
                (best, bestDistance) = (o.Guid, d);
            }
        }

        return bestDistance <= metres ? best : 0;
    }

    protected uint NearestOfWcid(uint wcid) =>
        S.State.Self.Position is { } here ? WorldObjectNear(wcid, here.CellId, here.Origin, double.MaxValue - 1) : 0;

    protected uint NearestFloor(uint wcid) => NearestOfWcid(wcid);

    protected uint NearestLiving(uint wcid, uint cell, Vector3 near)
    {
        uint best = 0;
        var bestDistance = double.MaxValue;
        foreach (var o in S.State.Objects.All)
        {
            if (o.Weenie.WeenieClassId != wcid || o.Position is not { } p || p.CellId >> 16 != cell >> 16 || !Alive(o.Guid) || _shots.IsDropped(o.Guid, S.Clock.Now) || PassedBy(o.Guid))
            {
                continue;
            }

            var d = Vector3.Distance(p.Origin, near);
            if (d <= SearchMetres && d < bestDistance)
            {
                (best, bestDistance) = (o.Guid, d);
            }
        }

        return best;
    }

    protected uint CorpseNear(string corpseName, CellFrame? at)
    {
        if (at is not { } where)
        {
            return 0;
        }

        uint best = 0;
        var bestDistance = 10.0;
        foreach (var o in S.State.Objects.All)
        {
            if ((o.DescriptionFlags & WorldObjectTable.CorpseFlag) == 0 || !string.Equals(o.Name, corpseName, StringComparison.Ordinal) || o.Position is not { } p || p.CellId >> 16 != where.CellId >> 16)
            {
                continue;
            }

            var d = Vector3.Distance(p.Origin, where.Origin);
            if (d <= bestDistance)
            {
                (best, bestDistance) = (o.Guid, d);
            }
        }

        return best;
    }

    protected uint InContainer(uint wcid, uint container)
    {
        foreach (var o in S.State.Objects.All)
        {
            if (o.Weenie.WeenieClassId == wcid && o.ContainerId == container)
            {
                return o.Guid;
            }
        }

        return 0;
    }

    protected string Contents(uint container)
    {
        var names = S.State.Objects.All.Where(o => o.ContainerId == container).Select(o => $"{o.Name} ({o.Weenie.WeenieClassId})").ToList();
        return names.Count == 0 ? "nothing the client has seen" : string.Join(", ", names);
    }

    /// <summary>True when the step's reply (when it names one) is in a chat line since <paramref name="mark"/>; true for a step with no reply (M6 5 Q1: <see cref="QuestReads.Replied"/>).</summary>
    protected bool Replied(QuestExpect expect, long mark) => QuestReads.Replied(S.State.Chat, mark, expect.Reply);

    /// <summary>What the server said since <paramref name="mark"/>: every chat line, as <c>; said: ...</c> (M6 5 Q1: <see cref="QuestReads.Said"/>).</summary>
    protected string Said(long mark) => QuestReads.Said(S.State.Chat, mark);

    protected bool Verify(bool ok, string what, uint guid = 0, long mark = -1)
    {
        if (!ok)
        {
            Warn(Stage, what, guid, mark);
        }

        return Check(ok, what);
    }

    protected void Warn(string step, string what, uint guid = 0, long mark = -1) =>
        Context.Log.Warn("station", $"[live {Name} t={S.Clock.Now:0.0}] PROBLEM step={step}: {what}; {ItemState(guid)}; cell 0x{S.State.Self.Position?.CellId ?? 0:X8}{(mark < 0 ? string.Empty : Said(mark))}");

    protected string ItemState(uint guid) =>
        guid == 0 ? "item=none"
        : !S.State.Objects.TryGet(guid, out var o) ? $"item=0x{guid:X8} not in the object table"
        : string.Create(CultureInfo.InvariantCulture, $"item=0x{guid:X8} wcid={o.Weenie.WeenieClassId} name={o.Name} container=0x{o.ContainerId ?? 0:X8} health={HealthOf(guid)} distance={Distance(guid):0.0}");
}
