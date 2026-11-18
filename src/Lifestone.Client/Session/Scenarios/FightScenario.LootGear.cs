using System.Globalization;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Session.Mage;

namespace Lifestone.Client.Session.Scenarios;

public sealed partial class FightScenario
{
    /// <summary>
    /// M6 14 capacity (live 2026-11-17 12:23:21, ladder58: the loot logged "gear: replace with 'Shoes' 0x80000B09" and no equip ever ran, because the loot step carried out a Wear alone and left every Replace to the next visit's gear
    /// phase, which a quest hunt never reached): the resupply's last wear pick (<c>JourneyFacts.WearJudge</c>), read by the loot step so a find is judged as the gear phase judges it, on her live load; null (the default, a plain fight): the
    /// loot step's own judge.
    /// </summary>
    public Func<SelfEquipJudge?>? WearJudge { get; init; }

    /// <summary>
    /// M6 14 own lore: called with the reason when the loot step finds a piece (a find, or one she wears) whose spells her Arcane Lore alone keeps off: the autopilot starts its experience spend at once, whose Arcane Lore rule raises the lore
    /// toward it (<see cref="LoreSpend"/>), not only at the next level-up. Null (the default): nothing is asked.
    /// </summary>
    public Action<string>? LoreWanted { get; init; }

    private sealed partial class LootStep
    {
        /// <summary>The seconds a replace in hand may take, its two or more server actions and the light's wait in, before the fight goes first (each action is bound by <see cref="ActionSeconds"/> of its own).</summary>
        public const double SwapSeconds = 3 * ActionSeconds;

        /// <summary>The seconds the check after waits for the new piece's spells to show on her (ACE casts them at the wield, <c>TryActivateSpells</c>, Player_Inventory.cs:313-335 @95ef426d).</summary>
        public const double LitSeconds = 3;

        private GearSwap? _swap;
        private readonly HashSet<uint> _relit = []; // the worn pieces re-wielded this run: once each
        private readonly HashSet<(uint Guid, RelightKind Kind)> _leftOnSaid = []; // M6 16 E5e4: the "left on" line, once a piece and case a run
        private readonly Dictionary<uint, WornPiece> _leftOff = []; // M6 16 E5e4: the pieces a swap could not put back on (named: gear-left-off): put on again at each corpse until they are worn
        private double _putBackTriedAt = double.NaN; // the corpse (by its start) whose recovery was tried: one put-back a corpse
        private Func<uint, Lifestone.Data.Magic.SpellInfo?>? _spellRow;

        /// <summary>One replace (or re-wield) in hand: the pieces off one by one to a pack with a free slot, the piece on, its spells looked for; a refused wield puts every old piece back.</summary>
        private sealed class GearSwap(SelfEquipChoice choice, IReadOnlyList<WornPiece> olds, uint pack, string packWhy, bool relight, double start)
        {
            public SelfEquipChoice Choice { get; } = choice;

            public IReadOnlyList<WornPiece> Olds { get; } = olds;

            public uint Pack { get; } = pack;

            public string PackWhy { get; } = packWhy;

            public bool Relight { get; } = relight;

            public double Start { get; } = start;

            public List<WornPiece> OffDone { get; } = [];

            public int Phase { get; set; } // 0..Olds.Count-1: taking off; Olds.Count: on; Olds.Count + 1: the light; Olds.Count + 2: put back

            public int BackIndex { get; set; }

            public ActionHandle? Pending { get; set; }

            public double PhaseStart { get; set; }

            /// <summary>M6 16 E5e4: true for the put-back of a piece an earlier swap left off (<see cref="PutBackLeftOff"/>): nothing comes off, the piece goes on, its spells are looked for.</summary>
            public bool PutBack { get; init; }

            public string Name => PutBack ? "put back" : Relight ? "re-wield" : "replace";
        }

        /// <summary>The SpellTable's row of a spell id, or null (one delegate a loot step).</summary>
        private Func<uint, Lifestone.Data.Magic.SpellInfo?> SpellRow => _spellRow ??= id => owner._spells.TryGetValue(id, out var row) ? row : null;

        /// <summary>The loot step's judge: the resupply's wear pick on her live load when one is held (<see cref="WearJudge"/>), else the loot's own.</summary>
        private SelfEquipJudge LootJudge(IGameSessionView session)
        {
            if (owner.WearJudge?.Invoke() is not { Wear: { } terms } judge)
            {
                return GearJudge(session);
            }

            var load = LootContext.Of(session.State);
            return judge.WithWear(terms with { Capacity = load.Capacity > 0 ? load.Capacity : terms.Capacity, Carried = load.Burden, LoadLinePercent = rules.Thresholds.LoadLinePercent });
        }

        /// <summary>
        /// Check before a replace: no attacker on her (the safe moment: between kills, nothing attacking), every displaced piece still worn, a pack with a free slot to take them off into (<see cref="HeldCaster.SwapPack"/>), the find in her
        /// pack. True: the swap is in hand; false: <paramref name="why"/> says what waits.
        /// </summary>
        private bool BeginSwap(IGameSessionView session, double clock, SelfEquipChoice choice, IReadOnlyList<WornPiece> olds, bool relight, out string why)
        {
            var state = session.State;
            if (owner.AttackerNow(session, clock) is { } attacker)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"{attacker.Name} 0x{attacker.Guid:X8} attacked her {clock - attacker.LastAt:0.#} s ago: not a safe moment");
                return false;
            }

            if (olds.FirstOrDefault(o => state.Self.Inventory.Find(o.Guid).Kind != InventoryPlaceKind.Equipped) is { } gone)
            {
                why = string.Create(CultureInfo.InvariantCulture, $"the worn '{gone.Name}' 0x{gone.Guid:X8} is not worn now");
                return false;
            }

            if (!relight && (!state.Self.Inventory.Find(choice.Guid).Held || state.Self.Inventory.Find(choice.Guid).Kind == InventoryPlaceKind.Equipped))
            {
                why = string.Create(CultureInfo.InvariantCulture, $"'{choice.Name}' 0x{choice.Guid:X8} is not in her packs");
                return false;
            }

            _packRules ??= new PackRules(owner._ground);
            var layout = PackLayout.Of(state, _packRules, g => CantripRoom.IsGreatPiece(state, g, owner._spells), PackMemory.Shared.Rotation.Contains);
            var pack = HeldCaster.SwapPack(layout, out var packWhy);
            if (pack == 0 || (olds.Count > 1 && layout.FreeIn(PackJob.Main) < olds.Count && pack == layout.Owner))
            {
                why = "no pack has room for the piece(s) taken off: " + packWhy;
                return false;
            }

            _swap = new GearSwap(choice, olds, pack, packWhy, relight, clock) { PhaseStart = clock };
            why = string.Empty;
            log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: gear: {_swap.Name}: check before: no attacker on her; worn {string.Join(", ", olds.Select(o => $"'{o.Name}' 0x{o.Guid:X8} at 0x{o.Location:X}"))}; '{choice.Name}' 0x{choice.Guid:X8} {(relight ? "is worn with its spells off" : "in her pack")}; off to {packWhy}; {SelfLine(state)}"));
            return true;
        }

        /// <summary>One frame of the swap in hand (check, act, check per action); true while it runs, false when it is done.</summary>
        private bool SwapStep(IGameSessionView session, double clock)
        {
            var s = _swap!;
            var state = session.State;
            if (s.Pending is { } pending)
            {
                if (pending.Outcome is null && clock - s.PhaseStart <= ActionSeconds)
                {
                    return true;
                }

                s.Pending = null;
                if (s.Phase < s.Olds.Count)
                {
                    var old = s.Olds[s.Phase];
                    var off = state.Self.Inventory.Find(old.Guid).Kind != InventoryPlaceKind.Equipped && state.Self.Inventory.Find(old.Guid).Held;
                    log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: gear: {s.Name}: '{old.Name}' 0x{old.Guid:X8} off to 0x{s.Pack:X8}: {Outcome(pending)}; check: {(off ? "off, in her pack" : "still worn or not in her packs")}"));
                    if (!off)
                    {
                        if (state.Self.Inventory.Find(old.Guid).Kind != InventoryPlaceKind.Equipped)
                        {
                            LeftOff(s, old, "it is neither worn nor in her packs after the take-off"); // M6 16 E5e4: never without a loud line
                        }

                        s.Phase = s.Olds.Count + 2; // the recovery: the pieces already off go back on
                    }
                    else
                    {
                        s.OffDone.Add(old);
                        s.Phase++;
                    }
                }
                else if (s.Phase == s.Olds.Count)
                {
                    var on = state.Self.Inventory.Find(s.Choice.Guid).Kind == InventoryPlaceKind.Equipped;
                    log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: gear: {s.Name}: '{s.Choice.Name}' 0x{s.Choice.Guid:X8} on at 0x{s.Choice.Location:X}: {Outcome(pending)}; check: {(on ? "worn" : "not worn (refused)")}"));
                    if (on)
                    {
                        PackTally.AddEquip();
                        s.Phase++;
                    }
                    else
                    {
                        if (s.PutBack && _leftOff.TryGetValue(s.Choice.Guid, out var still))
                        {
                            LeftOff(s, still, "the wield did not land again (" + Outcome(pending) + ")"); // M6 16 E5e4: said at every corpse it stays off
                        }

                        s.Phase = s.Olds.Count + 2;
                    }
                }
                else if (s.Phase == s.Olds.Count + 2)
                {
                    var old = s.OffDone[s.BackIndex - 1];
                    var back = state.Self.Inventory.Find(old.Guid).Kind == InventoryPlaceKind.Equipped;
                    log.Write(back ? LogSeverity.Info : LogSeverity.Warning, LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: gear: {s.Name}: recovery: '{old.Name}' 0x{old.Guid:X8} back on at 0x{old.Location:X}: {Outcome(pending)}; check: {(back ? "worn again" : "NOT worn: the parts it covered are bare")}"), []);
                    if (back)
                    {
                        _leftOff.Remove(old.Guid);
                    }
                    else
                    {
                        LeftOff(s, old, "the recovery's wield did not land (" + Outcome(pending) + ")"); // M6 16 E5e4: the recovery failed too: an error line names the piece, and every corpse puts it on again
                    }
                }

                s.PhaseStart = clock;
            }

            if (s.Phase < s.Olds.Count)
            {
                var old = s.Olds[s.Phase];
                if (state.Self.Inventory.Find(old.Guid).Kind != InventoryPlaceKind.Equipped)
                {
                    log.Info(LogCategory, $"loot: gear: {s.Name}: check before failed: '{old.Name}' 0x{old.Guid:X8} is not worn now; nothing sent");
                    s.Phase = s.Olds.Count + 2;
                    return true;
                }

                s.Pending = session.Actions.MoveToPack(old.Guid, s.Pack, 0, ActionSource.Automation);
                return true;
            }

            if (s.Phase == s.Olds.Count)
            {
                // M6 16 E5e2: the one wield routine's send (its check before, the slot, the wield); the check after is this step's own, above. A send the check before refuses (the piece no longer in her packs, or worn
                // already) leaves no handle: named, and the pieces taken off go back on, where a second send would have followed on the next frame
                var on = new Quartermaster.WieldResult();
                s.Pending = Quartermaster.Wardrobe.Send(session, new Quartermaster.WieldRequest(s.Choice.Guid) { Name = s.Choice.Name, Location = s.Choice.Location }, on);
                if (s.Pending is null)
                {
                    log.Info(LogCategory, $"loot: gear: {s.Name}: check before failed: {on.Why}");
                    s.Phase = s.Olds.Count + 2;
                }

                return true;
            }

            if (s.Phase == s.Olds.Count + 1)
            {
                // the check after: her registry shows a spell the piece cast (WearScore.LitOn), the old pieces in her pack
                var lit = WearScore.LitOn(state.Self.Qualities, s.Choice.Guid);
                if (!lit && clock - s.PhaseStart <= LitSeconds)
                {
                    return true;
                }

                // M6 16 E5e4: lit or surpassed counts as done. The piece's spells are read once, here (GearLight.Read): lit under a stronger spell of hers is said so; not seen, with every spell it casts on her under a
                // stronger one of hers, is nothing left to light and no warning
                var light = state.Appraisals.TryGet(s.Choice.Guid, out var seen) && !seen.IsNotFound ? GearLight.Read(state.Self.Qualities, s.Choice.Guid, seen, SpellRow) : null;
                var under = light?.SurpassedBy() ?? string.Empty;
                var surpassed = !lit && light is { Done: true };
                var spells = lit ? "active on her" + (under.Length == 0 ? string.Empty : " (surpassed by her own " + under + ")")
                    : surpassed ? string.Create(CultureInfo.InvariantCulture, $"not seen on her within {LitSeconds:0} s, each under a stronger one of hers ({under}): surpassed, nothing left to light")
                    : string.Create(CultureInfo.InvariantCulture, $"not seen on her within {LitSeconds:0} s (named: gear-spells-not-lit)");
                var inPack = s.Relight ? "none taken off for good" : string.Join(", ", s.OffDone.Select(o => $"'{o.Name}' 0x{o.Guid:X8} {(state.Self.Inventory.Find(o.Guid).Held && state.Self.Inventory.Find(o.Guid).Kind != InventoryPlaceKind.Equipped ? "in her pack" : "NOT in her pack")}"));
                log.Write(lit || surpassed ? LogSeverity.Info : LogSeverity.Warning, LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: gear: {s.Name}: check after: '{s.Choice.Name}' 0x{s.Choice.Guid:X8} worn, its spells {spells}; {inPack}; {SelfLine(state)}"), []);
                _leftOff.Remove(s.Choice.Guid);
                foreach (var o in s.OffDone)
                {
                    _leftOff.Remove(o.Guid); // a swap cut by the fight and finished at the next corpse: the piece is on, what it displaced is off as decided, not left off
                }

                _swap = null;
                return false;
            }

            // the recovery: each piece taken off goes back on at its location
            if (s.BackIndex < s.OffDone.Count)
            {
                var old = s.OffDone[s.BackIndex++];
                var back = new Quartermaster.WieldResult();
                s.Pending = Quartermaster.Wardrobe.Send(session, new Quartermaster.WieldRequest(old.Guid) { Name = old.Name, Location = old.Location }, back); // M6 16 E5e2: the one wield routine's send; the check after is this step's own, above
                if (s.Pending is null)
                {
                    var worn = state.Self.Inventory.Find(old.Guid).Kind == InventoryPlaceKind.Equipped; // refused by the check before: worn already (nothing to put back), or no longer held
                    log.Write(worn ? LogSeverity.Info : LogSeverity.Warning, LogCategory, $"loot: gear: {s.Name}: recovery: {back.Why}{(worn ? string.Empty : ": the parts it covered are bare")}", []);
                    if (worn)
                    {
                        _leftOff.Remove(old.Guid);
                    }
                    else
                    {
                        LeftOff(s, old, "the recovery sent nothing (" + back.Why + ")");
                    }
                }

                return true;
            }

            var left = s.OffDone.Count(o => state.Self.Inventory.Find(o.Guid).Kind != InventoryPlaceKind.Equipped); // M6 16 E5e4: the count is of the pieces read worn, not of the wields sent
            log.Warn(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: gear: {s.Name} with '{s.Choice.Name}' 0x{s.Choice.Guid:X8} did not land: {s.OffDone.Count - left} piece(s) put back{(left == 0 ? string.Empty : $", {left} NOT worn (named: gear-left-off)")}; the next visit's gear phase judges it again"));
            _swap = null;
            return false;
        }

        /// <summary>
        /// M6 16 E5e4 (a failed re-wield never leaves her undressed without a loud line and a recovery): a piece a swap took off and could not put back on is named in an error line and remembered; every corpse after puts
        /// it on again (<see cref="PutBackLeftOff"/>) until it is read worn: the next corpse first, for this corpse's wields were just refused.
        /// </summary>
        private void LeftOff(GearSwap s, WornPiece piece, string why)
        {
            _leftOff[piece.Guid] = piece;
            _putBackTriedAt = _corpseStart;
            log.Error(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: gear: {s.Name}: '{piece.Name}' 0x{piece.Guid:X8} is NOT worn (named: gear-left-off): {why}; what it covered at 0x{piece.Location:X} is bare; recovery: it is put on again at every corpse until it is read worn"));
        }

        /// <summary>
        /// M6 16 E5e4: the recovery of <see cref="LeftOff"/>, asked once a corpse before any re-wield: a remembered piece that is worn again or no longer hers is forgotten (said); the first one still in her packs is put
        /// on through the wear service (the swap's own wield step with nothing to take off: check before, the one send, the worn state read, its spells looked for), one piece a corpse. True: a put-back is in hand.
        /// </summary>
        private bool PutBackLeftOff(IGameSessionView session, double clock)
        {
            if (_leftOff.Count == 0 || _putBackTriedAt == _corpseStart)
            {
                return false;
            }

            var inventory = session.State.Self.Inventory;
            foreach (var piece in _leftOff.Values.ToList())
            {
                var place = inventory.Find(piece.Guid);
                if (place.Kind == InventoryPlaceKind.Equipped || !place.Held)
                {
                    _leftOff.Remove(piece.Guid);
                    log.Write(place.Held ? LogSeverity.Info : LogSeverity.Error, LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: gear: put back: '{piece.Name}' 0x{piece.Guid:X8} {(place.Held ? "is worn again: nothing to do" : "is no longer in her packs (named: gear-left-off): it cannot be put back on; the next visit's gear phase fills what it covered")}"), []);
                    continue;
                }

                var choice = new SelfEquipChoice(SelfEquipKind.Wear, piece.Guid, piece.Name, piece.Location, 0, "a swap left it off: put back on");
                _swap = new GearSwap(choice, [], 0, "nothing comes off", relight: true, clock) { PhaseStart = clock, PutBack = true };
                _putBackTriedAt = _corpseStart;
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: gear: put back: check before: '{piece.Name}' 0x{piece.Guid:X8} is in her packs and not worn (an earlier swap left it off); on again at 0x{piece.Location:X}; {SelfLine(session.State)}"));
                return true;
            }

            return false;
        }

        /// <summary>M6 16 E5e4: the fight goes first while a swap is still in hand past <see cref="SwapSeconds"/>: every piece it has off is named in an error line and remembered; the swap's own steps resume at the next corpse.</summary>
        private void SwapCut(IGameSessionView session, string who)
        {
            if (_swap is not { } s)
            {
                return;
            }

            var inventory = session.State.Self.Inventory;
            foreach (var old in s.OffDone)
            {
                if (inventory.Find(old.Guid).Kind != InventoryPlaceKind.Equipped && (s.Relight || inventory.Find(s.Choice.Guid).Kind != InventoryPlaceKind.Equipped))
                {
                    LeftOff(s, old, string.Create(CultureInfo.InvariantCulture, $"{who} and the swap ran past {SwapSeconds:0} s: the fight goes first with it off"));
                }
            }
        }

        /// <summary>
        /// M6 14 own lore: a worn piece whose spells are off for her Arcane Lore alone, which her lore now meets: re-wielded once a run (ACE reads the lore only at the wield, Player_Inventory.cs:313-335 @95ef426d, so a raise lights
        /// nothing until the piece comes off and goes on again). A worn piece still short asks the autopilot's spend (<see cref="LoreWanted"/>). Null when none.
        /// M6 16 E5e4 (live 2026-11-18 00:00:37 to 01:00:06, ladder67: five pieces taken off and put on twice, ACE answering each wield that her Missile or Melee Defense is too low for the piece's magic): the check
        /// before is <see cref="GearLight.Relight"/>. A piece comes off only when ACE would activate its spells at a wield now (every activation requirement, the skill limit among them), she could wield it back, and at
        /// least one of its spells would show on her (not under a stronger one of hers). A piece left on says which case applied, once a piece and case a run.
        /// </summary>
        private WornPiece? RelightCandidate(IGameSessionView session, IReadOnlyList<WornPiece> worn)
        {
            var state = session.State;
            var skills = new QualitiesSkills(state.Self.Qualities);
            foreach (var w in worn)
            {
                if (WearScore.LitOn(state.Self.Qualities, w.Guid))
                {
                    continue; // lit (on top or surpassed): nothing to read further, and nothing allocated for it
                }

                var verdict = GearLight.Relight(state.Self.Qualities, w, skills, SpellRow);
                switch (verdict.Kind)
                {
                    case RelightKind.CannotLight when w.Appraisal is { } a && SelfEquipJudge.LoreShort(a, skills, out var lore, out var difficulty) is { } shortBy && WearScore.SpellsOff(a, SelfEquipJudge.WithLore(skills, lore, difficulty)) is null:
                        owner.LoreWanted?.Invoke(string.Create(CultureInfo.InvariantCulture, $"the worn '{w.Name}' 0x{w.Guid:X8} has its spells off ({verdict.Why}): {shortBy} point(s) of Arcane Lore light it"));
                        break;
                    case RelightKind.CannotLight:
                        SayLeftOn(w, verdict.Kind, "an off-and-on would light nothing: " + verdict.Why);
                        break;
                    case RelightKind.Surpassed:
                        SayLeftOn(w, verdict.Kind, "its spells are surpassed by her own " + verdict.Why);
                        break;
                    case RelightKind.CannotWieldBack:
                        SayLeftOn(w, verdict.Kind, "ACE would refuse the wield back: " + verdict.Why);
                        break;
                    case RelightKind.Relight when _relit.Add(w.Guid):
                        log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: gear: re-wield: '{w.Name}' 0x{w.Guid:X8} is not lit on her (no enchantment in her registry is cast by it): {verdict.Why}"));
                        return w;
                }
            }

            return null;
        }

        private void SayLeftOn(WornPiece w, RelightKind kind, string why)
        {
            if (_leftOnSaid.Add((w.Guid, kind)))
            {
                log.Info(LogCategory, string.Create(CultureInfo.InvariantCulture, $"loot: gear: re-wield: '{w.Name}' 0x{w.Guid:X8} left on: {why}"));
            }
        }

        private static string Outcome(ActionHandle h) => h.Outcome is { } o ? Describe(o) : "unanswered";

        private static string SelfLine(GameState state)
        {
            var load = LootContext.Of(state);
            var skills = new QualitiesSkills(state.Self.Qualities);
            return string.Create(CultureInfo.InvariantCulture, $"her burden {load.Burden} of {load.Capacity}, capacity = 150 x her current Strength {load.Capacity / GearContext.CapacityPerStrength}, Arcane Lore {skills.Current(MageTemplate.ArcaneLore)?.ToString(CultureInfo.InvariantCulture) ?? "unread"}");
        }
    }
}
