using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Creatures;
using Lifestone.Client.Session.Grounds;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.World;
using Lifestone.Data.World;

namespace Lifestone.Client.Session.Scenarios;

/// <summary>
/// M6 14 want loot (live 2026-11-17 from 04:05:37 UTC, the Atlan wand's step "16 Pyreal Motes in the Crater Caves", cache/m6/14/live/ladder52/step3-levelup.log): in 15 minutes she killed 35 Nubilous Golems with Lightning Bolt VI,
/// mostly from 40 to 50 m, and every corpse past the loot radius was left where it lay ("beyond the loot radius (15 m, --fight-loot-radius-m); left, not walked to"), as was every corpse a walk back found 22 m off. A Nubilous Golem's
/// corpse holds a mote one time in 25, so every corpse left is a lost roll.
/// </summary>
/// <remarks>
/// <para><b>The drop.</b> ACE (@95ef426d) fills a creature's corpse in <c>Creature_Death.GenerateTreasure</c> (<c>WorldObjects/Creature_Death.cs:646-711</c>): the create list's Contain rows and its Treasure rows that are not Wield rows go
/// through <c>CreateListSelect</c> (<c>WorldObjects/Creature_Equipment.cs:636-679</c>), where a Treasure row with a non-zero shade is one outcome of a set that runs until the shades sum to 1, one random number a set: the row's
/// shade is its item's chance a kill. 23082's first set is the mote at 0.04 and nothing at 0.96 (rows 46840, 46841); 199's second set is the mote at 0.01 (46603, 46604). The server property <c>trophy_drop_rate</c> is 1 here, so the
/// modifier path (<c>:681-726</c>) does not run.</para>
/// <para><b>The rule.</b> In a fight that wants an item and knows its carriers (<see cref="FightOptions.WantCarriers"/>), the corpse of a carrier beyond the loot radius, or with no clear line, is walked to by the fight's own walk
/// (planned over the walkable cells, purpose <c>loot</c>), whatever its distance on the ground, and looked at again from where she stands: within reach it is opened, else ACE's own walk takes the rest. The fight's safety rules stay: the
/// aggro rule puts the corpse off while a creature attacks her or a target stands beside it, the walk rule while a hostile is in reach of her or a target is in sight, and the put-off corpse is retried when the fight has no target,
/// until its retry window ends. A carrier's corpse lost from view (the client's cull: 25 s after its cell left her visible cells, <see cref="State.ObjectCull.CullSeconds"/>; no distance) is walked back to whatever the distance,
/// while where it lay is on the ground. A walk that makes no way is given up after <see cref="WantWalkLegs"/> legs, named. The loot rule takes the wanted item (the want hook) and what the pack rules keep, as from any corpse.</para>
/// </remarks>
public sealed partial class FightScenario
{
    /// <summary>M6 14 want loot: the most legs of the fight's own walk toward one carrier's corpse in one look (Lifestone's own: the first leg goes to the corpse; a second and third cover a leg cut short).</summary>
    public const int WantWalkLegs = 3;

    /// <summary>M6 14 want loot: a leg after the first must have begun at least this many metres nearer the corpse than the last, or the walk made no way and the corpse is left (Lifestone's own).</summary>
    public const double WantWalkProgressMetres = 1;

    private const string WantWalkWhy = "the corpse's creature can carry the wanted item, so the fight's own walk goes to it whatever its distance on the ground (m6-want-loot)";

    /// <summary>M6 14 want loot: the walks of the fight's own toward a carrier's corpse (report-only).</summary>
    public int LootWantWalks => _loot?.WantWalks ?? 0;

    /// <summary>M6 14 want loot: the carriers' corpses left after their walks made no way or could not be planned (report-only).</summary>
    public int LootWantLeft => _loot?.WantLeft ?? 0;

    /// <summary>The reason a carrier's corpse is walked to, as the log says it: <c>the wanted item Pyreal Mote (6353): its create list gives 0.04</c>.</summary>
    /// <param name="carrier">The carrier.</param>
    internal static string WantWhy(WantCarrier carrier) =>
        string.Create(CultureInfo.InvariantCulture, $"the wanted item {carrier.ItemName} ({carrier.Item}): its create list gives {carrier.Shade:0.###}");

    /// <summary>
    /// M6 14 want loot: after each corpse looted in a fight with an item until, the count held of the until's item, one line: <c>hunt-until: N of 16 Pyreal Mote (6353) held ...</c>, with this fight's kills a minute and, for a
    /// carrier's corpse, the items an hour that rate gives at the carrier's shade.
    /// </summary>
    private void NoteUntilCount(IGameSessionView session, string name, uint corpse, WantCarrier? want)
    {
        if (_options.Until is not { Chat: null } until || until.ItemWcid == 0)
        {
            return;
        }

        var c = CultureInfo.InvariantCulture;
        var held = UntilUnits(session.State, until); // M6 14 combine2: the until's own count, its item's equivalents along the chain (live 13:30:46 read 0 of 16 with 12 motes' worth combined)
        var itemName = want?.ItemName ?? _options.WantCarriers.FirstOrDefault(x => x.Item == until.ItemWcid)?.ItemName ?? "item";
        var minutes = (session.Clock.Now - _fightStart) / 60;
        var perMinute = minutes > 0 ? _tally.Kills / minutes : 0;
        var rate = string.Create(c, $"this fight: {_tally.Kills} kill(s) in {minutes:0.#} min ({perMinute:0.##} a minute)");
        var expected = want is null || perMinute <= 0 ? string.Empty : string.Create(c, $", {perMinute * 60 * want.Shade:0.##} {itemName} an hour at that rate if every {name} corpse is looted");
        _log.Info(LogCategory, string.Create(c, $"hunt-until: {held} of {until.Count} {itemName} ({until.ItemWcid}){(until.Chain.Count > 1 ? " equivalents" : string.Empty)} held after the corpse 0x{corpse:X8} of '{name}'{(want is null ? string.Empty : " (" + WantWhy(want) + ")")}; {rate}{expected}"));
    }

    private sealed partial class LootStep
    {
        private int _wantLegs; // the legs of the fight's own walk toward the corpse in hand in this look
        private double _wantLegFrom; // how far the corpse was when the last leg began
        private bool _wantWalkStarted;
        private CellFrame _wantWalkSpot;
        private bool _carriersTodoLogged;

        /// <summary>See <see cref="FightScenario.LootWantWalks"/>.</summary>
        public int WantWalks { get; private set; }

        /// <summary>See <see cref="FightScenario.LootWantLeft"/>.</summary>
        public int WantLeft { get; private set; }

        /// <summary>
        /// The carrier <paramref name="k"/>'s creature is, for an item this fight still wants (not refused by the server), or null: matched by the creature's weenie class when the kill knew it, else by its name (the creature table's
        /// name of the carrier). A fight that wants an item and knows no carriers names <c>TODO(m6-create-list-general-read)</c> once and keeps the loot radius.
        /// </summary>
        private WantCarrier? WantedBy(LootKill k)
        {
            var options = owner._options;
            if (options.Want.Count == 0)
            {
                return null;
            }

            if (options.WantCarriers.Count == 0)
            {
                if (!_carriersTodoLogged)
                {
                    _carriersTodoLogged = true;
                    log.Warn(LogCategory, $"loot: TODO(m6-create-list-general-read): the fight wants {string.Join(", ", options.Want)} but knows no creature whose create list holds it (no carriers=); every corpse keeps the loot radius ({owner.LootRadius:0.#} m)");
                }

                return null;
            }

            foreach (var carrier in options.WantCarriers)
            {
                if (!options.Want.Contains(carrier.Item) || owner._wantRefused.Contains(carrier.Item))
                {
                    continue;
                }

                if (k.Wcid != 0 ? k.Wcid == carrier.Creature : string.Equals(Bestiary.Find(carrier.Creature)?.Identity.Name, k.Name, StringComparison.Ordinal))
                {
                    return carrier;
                }
            }

            return null;
        }

        /// <summary>
        /// The seek found a carrier's corpse beyond reach, past the loot radius or with no clear line: check (a no-go cell, the aggro rule, the walk rule, the walk's progress), then act (a leg of the fight's own walk to it). True: the
        /// step used the frame.
        /// </summary>
        private bool WantWalkFrom(IGameSessionView session, double clock, uint corpse, in CellFrame own, in CellFrame lies, double away, WantCarrier want)
        {
            var c = CultureInfo.InvariantCulture;
            var why = WantWhy(want);
            var ground = owner._ground;
            if (!owner._options.ArenaHere && ground.IsNoGo(lies.CellId))
            {
                SkipNoGo(session, string.Create(c, $"it lies in the no-go cell 0x{lies.CellId:X8} of {ground.Name}, {away:0.#} m away ({why}; no walk goes into a no-go cell)"));
                return true;
            }

            if (Blocked(session, clock, lies.ToGlobal(), _kill.Guid, out var blockedBy))
            {
                Requeue(session, clock, blockedBy + "; " + why + ", so it is walked to once nothing is in the way");
                return true;
            }

            if (WalkHeld(session, clock, in own, away, _kill.Guid, out var heldBy))
            {
                WalkWaits++;
                Requeue(session, clock, heldBy + "; " + why + ", so it is walked to once nothing is in the way");
                return true;
            }

            var los = owner.LineIsClearTo(in own, in lies);
            if (_wantLegs >= WantWalkLegs || (_wantLegs > 0 && away > _wantLegFrom - WantWalkProgressMetres))
            {
                SkippedUnreached++;
                LootTally.AddLeftUnreached();
                Left++;
                WantLeft++;
                log.Warn(LogCategory, string.Create(c, $"loot: the corpse 0x{corpse:X8} of '{_kill.Name}' is still {away:0.#} m away{(los ? string.Empty : " with no clear line")} after {_wantLegs} walk(s) of the fight's own toward it (the last began {_wantLegFrom:0.#} m off): {why}, but the walks made no way (at most {WantWalkLegs} legs, each beginning at least {WantWalkProgressMetres:0} m nearer); left, the fight goes on (m6-want-loot)"));
                Finish(session, false);
                return true;
            }

            _wantLegs++;
            _wantLegFrom = away;
            (_wantWalkSpot, _wantWalkStarted) = (lies, false);
            _stage = Stage.WantWalk;
            _stageStart = clock;
            var radius = owner.LootRadius;
            log.Info(LogCategory, string.Create(c, $"loot: the corpse 0x{corpse:X8} of '{_kill.Name}' is {away:0.#} m away{(away > radius ? $", beyond the loot radius ({radius:0.#} m)" : string.Empty)}{(los ? string.Empty : ", with no clear line of sight to it")}: {why}; the fight's own walk goes to it whatever its distance on the ground (leg {_wantLegs} of at most {WantWalkLegs}), then it is looked at again (m6-want-loot)"));
            return true;
        }

        /// <summary>
        /// The fight's own walk to a carrier's corpse, as <see cref="OwnWalk"/>: the first frame begins the leg (the Walk phase runs it; an attacker, a heal or a stuck give-up may drop it); the next frame the step is given looks at the
        /// corpse again from where she stands. A leg that cannot be planned leaves the corpse, named.
        /// </summary>
        private bool WantWalk(IGameSessionView session, double clock)
        {
            if (!_wantWalkStarted)
            {
                _wantWalkStarted = true;
                var note = string.Create(CultureInfo.InvariantCulture, $"the corpse 0x{_corpse:X8} of '{_kill.Name}' at 0x{_wantWalkSpot.CellId:X8} [{_wantWalkSpot.Origin.X:0.#} {_wantWalkSpot.Origin.Y:0.#} {_wantWalkSpot.Origin.Z:0.#}]");
                if (owner.BeginLootWalk(session, clock, _corpse, in _wantWalkSpot, note, WantWalkWhy, out var why))
                {
                    WantWalks++;
                }
                else
                {
                    SkippedUnreached++;
                    LootTally.AddLeftUnreached();
                    Left++;
                    WantLeft++;
                    log.Warn(LogCategory, $"loot: the corpse 0x{_corpse:X8} of '{_kill.Name}' can carry the wanted item, but no walk of the fight's own could be planned to it ({why}); left, the fight goes on (m6-want-loot)");
                    Finish(session, false);
                }

                return true;
            }

            log.Info(LogCategory, $"loot: the fight's own walk toward the corpse 0x{_corpse:X8} of '{_kill.Name}' ended; it is looked at again from where the mage stands (m6-want-loot)");
            _stage = Stage.Seek;
            _stageStart = clock;
            return Seek(session, clock);
        }
    }
}
