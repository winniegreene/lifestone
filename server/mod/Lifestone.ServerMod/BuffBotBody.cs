using System.Globalization;
using System.Numerics;
using ACE.Common;
using ACE.Database;
using ACE.DatLoader;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Entity.Models;
using ACE.Server.Entity;
using ACE.Server.Entity.Actions;
using ACE.Server.Managers;
using ACE.Server.Mods;
using ACE.Server.Network.GameEvent.Events;
using ACE.Server.Network.GameMessages.Messages;
using ACE.Server.WorldObjects;
using Lifestone.ServerTools;

namespace Lifestone.ServerMod;

/// <summary>
/// D138 (m6-plan.md): the buff bot's body in the world, the mod's own subclass of ACE's creature (ACE's public API: the constructor, and <see cref="OnActivate"/>, which ACE's <c>Activate</c> emote calls with the
/// teller, WorldObjects/Managers/EmoteManager.cs 81-94 @95ef426d). It reads the hook its emote set stamped, hands it to the day (<see cref="BuffBotDesk"/>) and does the steps the day answers with: the tells, the
/// says, a round of casts by a player's rules (<see cref="BuffBotCasting"/>) and the trip to the shop (<see cref="BuffBotRules.ShopRoute"/>, <see cref="BuffBotShop"/>). Everything runs on the landblock's queue
/// (the emote manager's, and the bot's own action chains); every step is logged.
/// </summary>
/// <remarks>
/// <para><b>A cast</b>, check, act, check: the teller is online on the bot's landblock and within the spell's reach (a player's: <c>BaseRangeConstant + skill x BaseRangeMod</c>, at most 75 m, Player_Magic.cs
/// 498-505); the pack holds the foci formula (<c>SpellFormula.GetFociFormula</c>: the Platinum Scarab and four Prismatic Tapers for level VII, SpellFormula.cs 340-381); the fizzle roll and the mana (ACE's own
/// <c>CalculateManaUsage</c>); the spell words and the wind-up as a human-animated creature casts (Monster_Magic.cs 150-160, 210-260, 418-425); then the mana is spent (<c>UpdateVitalDelta</c>), the burns taken from
/// the pack, and the spell lands through ACE's own cast (<c>TryCastSpell_WithRedirects</c>, as the emote cast did) or fizzles (its play script); the follow-through, then the next. A fizzled line is cast again,
/// up to <see cref="BuffBotCasting.MaxFizzlesPerLine"/> fizzles.</para>
/// <para><b>Its mana and its own buffs</b> (an owner ruling, 2026-11-17; <see cref="BuffBotSelf"/>): short of the next cast and the dance's reserve, it casts Stamina to Mana on itself, Revitalize when its stamina is low,
/// and goes on when the mana pays (the payer hears <see cref="BuffBotWire.Dance"/> once); it rests on ACE's regeneration only when the dance cannot run, and says why with the seconds. Between rounds it renews its
/// own lines ahead of their lapse, one at a time; before a round it renews those lapsed or near it first (<see cref="BuffBotWire.Renew"/>). Each of its own casts is a cast by the same rules, logged with its
/// numbers before and after.</para>
/// <para><b>The trip</b>: ACE's creature move-to (<c>Creature.MoveTo(Position, ...)</c>, Creature_Navigation.cs 341-389) to each point of the route in turn, checked each second; a point not neared for
/// <see cref="StallSeconds"/> is asked again, twice; then, or after <see cref="TripSeconds"/>, the bot recalls to its post (Lifestone Recall's effect: the creature's blip to a place on its own landblock,
/// <c>FakeTeleport</c>) and the trip fails by name. At the shop it turns to Archmage Cindrue, checks she is a vendor who sells the taper and buys the scarab's type, and trades by her prices
/// (<c>Vendor.SellPrice</c>, <c>BuyPrice</c> and the wares' Values from ACE's weenie cache).</para>
/// </remarks>
internal sealed class BuffBotBody : Creature
{
    /// <summary>The seconds a point of the walk may go without the bot nearing it before the move-to is asked again.</summary>
    public const double StallSeconds = 8;

    /// <summary>The seconds a trip may take, there and back.</summary>
    public const double TripSeconds = 300;

    /// <summary>The seconds the trade takes at the counter.</summary>
    public const double ShopSeconds = 3;

    private readonly BuffBot keeper;
    private Position? post;

    private BuffBotRound? round;
    private int line;
    private int lineFizzles;
    private int landed;
    private int fizzled;
    private long manaSpent;
    private int tapersBurned;
    private int scarabsBurned;
    private double roundStarted;
    private bool roundSelfDone;
    private bool renewTold;
    private int renewCasts;
    private int selfCasts;
    private long selfMana;

    private bool selfCasting;
    private bool danceTold;
    private int danceCasts;
    private double upkeepAt;

    private bool walking;
    private bool back;
    private int point;
    private int asks;
    private double tripStarted;
    private double pointSince;
    private float pointBest;
    private bool tripOk;
    private string tripWhy = string.Empty;

    public BuffBotBody(Weenie weenie, ObjectGuid guid, BuffBot keeper)
        : base(weenie, guid)
    {
        this.keeper = keeper;
    }

    private BuffBotDesk Desk => keeper.Desk;

    private static double Now => Time.GetUnixTime();

    /// <summary>The bot entered the world at <paramref name="at"/>, its post: its clock starts.</summary>
    /// <param name="at">The post.</param>
    public void Begin(Position at)
    {
        post = new Position(at);
        Clock();
    }

    /// <summary>The bot's numbers for the placing line: its skills, mana and regeneration as ACE computes them.</summary>
    public string Stats() => string.Create(
        CultureInfo.InvariantCulture,
        $"Creature Enchantment {GetCreatureSkill(Skill.CreatureEnchantment).Current}, Life Magic {GetCreatureSkill(Skill.LifeMagic).Current}, Mana Conversion {GetCreatureSkill(Skill.ManaConversion).Current}, mana {Mana.Current} of {Mana.MaxValue} at {Mana.RegenRate:0.##} a heartbeat of {HeartbeatSeconds:0.#} s before its own Mana Renewal, stamina {Stamina.Current} of {Stamina.MaxValue}; its book {BuffBotSelf.Book.Count(id => Biota.SpellIsKnown((int)id, BiotaDatabaseLock))} of {BuffBotSelf.Book.Count} spells");

    private double HeartbeatSeconds => HeartbeatInterval is { } h && h > 0 ? h : 5.0;

    /// <summary>ACE's mana regeneration a second: the rate, the enchantments' multiplier (its own Mana Renewal), over the heartbeat (Creature_Vitals.cs 120-134; no attribute or stance share for a creature, 174-216).</summary>
    private double ManaPerSecond => Mana.RegenRate * EnchantmentManager.GetRegenerationMod(Mana) / HeartbeatSeconds;

    /// <summary>ACE's <c>Activate</c> emote, from one of the bot's hooks: read the key, erase it, answer.</summary>
    /// <param name="activator">The teller or giver.</param>
    public override void OnActivate(WorldObject activator)
    {
        try
        {
            var names = QuestManager.GetQuests().Select(q => q.QuestName).ToList();
            var hook = BuffBotRules.ReadHook(names, out var why);
            foreach (var name in names.Where(n => n.StartsWith(BuffBotRules.HookPrefix, StringComparison.OrdinalIgnoreCase)))
            {
                QuestManager.Erase(name);
            }

            if (activator is not Player player)
            {
                Log($"an activation not from a player ({activator?.Name ?? "none"}): nothing answered", ModManager.LogLevel.Warn);
                return;
            }

            if (hook is not { } h)
            {
                Log($"TODO(m6-buffbot-hook): {player.Name} activated the bot and {why}: nothing answered", ModManager.LogLevel.Error);
                return;
            }

            Do(Desk.Hear(new BuffBotCaller(player.Guid.Full, player.Name, player.Level ?? 1), h, Now));
        }
        catch (Exception e)
        {
            Log("a hook threw: " + e, ModManager.LogLevel.Error);
        }
    }

    // ------------------------------------------------------------------------------------------------ the day's steps

    private void Do(List<BuffBotAct> acts)
    {
        foreach (var act in acts)
        {
            switch (act.Kind)
            {
                case BuffBotActKind.Turn:
                    if (Teller(act.Player) is { } turnTo)
                    {
                        Rotate(turnTo);
                    }

                    break;
                case BuffBotActKind.Tell:
                    if (PlayerManager.GetOnlinePlayer(act.Player) is { Session: not null } hearer)
                    {
                        hearer.Session.Network.EnqueueSend(new GameEventTell(this, act.Text, hearer, ChatMessageType.Tell));
                        Log(string.Create(CultureInfo.InvariantCulture, $"tells {hearer.Name}: {act.Text}"));
                    }
                    else
                    {
                        Log(string.Create(CultureInfo.InvariantCulture, $"0x{act.Player:X8} is not online: not told '{act.Text}'"), ModManager.LogLevel.Warn);
                    }

                    break;
                case BuffBotActKind.Say:
                    EnqueueBroadcast(new GameMessageHearSpeech(act.Text, Name, Guid.Full, ChatMessageType.Speech), LocalBroadcastRange, ChatMessageType.Speech);
                    Log("says: " + act.Text);
                    break;
                case BuffBotActKind.Round:
                    StartRound();
                    break;
                case BuffBotActKind.Leave:
                    StartTrip();
                    break;
                case BuffBotActKind.Log:
                    Log(act.Text ?? string.Empty);
                    break;
                case BuffBotActKind.Save:
                    keeper.Save("the ledger changed");
                    break;
            }
        }
    }

    /// <summary>The bot's clock: the day's tick and the walk's check, each second while the bot stands.</summary>
    private void Clock()
    {
        var chain = new ActionChain();
        chain.AddDelaySeconds(1.0);
        chain.AddAction(this, () =>
        {
            if (IsDestroyed || CurrentLandblock is null)
            {
                return;
            }

            try
            {
                Do(Desk.Tick(Now));
                if (walking)
                {
                    Walk();
                }
                else if (round is null && !selfCasting && Desk.Phase == BuffBotPhase.Idle && Now >= upkeepAt)
                {
                    Upkeep();
                }
            }
            catch (Exception e)
            {
                Log("the clock threw: " + e, ModManager.LogLevel.Error);
            }

            Clock();
        });
        chain.EnqueueChain();
    }

    // ------------------------------------------------------------------------------------------------ a round

    private void StartRound()
    {
        round = Desk.Round;
        (line, lineFizzles, landed, fizzled, manaSpent, tapersBurned, scarabsBurned, roundStarted) = (0, 0, 0, 0, 0, 0, 0, Now);
        (roundSelfDone, renewTold, renewCasts, selfCasts, selfMana, danceTold, danceCasts) = (false, false, 0, 0, 0, false, 0);
        CastNext(); // waits for a self cast in hand, which hands over at its end
    }

    private void CastNext()
    {
        if (IsDestroyed || round is null || selfCasting)
        {
            return; // a self cast in hand hands over to the round at its end (Upkeep)
        }

        if (line >= round.Profile.Lines.Count)
        {
            EndRound(BuffBotRoundEnd.Completed, null);
            return;
        }

        var target = Teller(round.Caller.Guid);
        if (target is null)
        {
            EndRound(BuffBotRoundEnd.Gone, $"{round.Caller.Name} is not online within {TellerMetres:0} m of the bot");
            return;
        }

        var c = CultureInfo.InvariantCulture;

        // The owner (2026-11-17): before the round's first line, its own lines lapsed or near their lapse are renewed (the payer hears it once).
        if (!roundSelfDone)
        {
            if (renewCasts < BuffBotSelf.Lines.Count * BuffBotCasting.MaxFizzlesPerLine && SelfDue() is { } due)
            {
                if (!renewTold)
                {
                    renewTold = true;
                    Do([new BuffBotAct(BuffBotActKind.Tell, round.Caller.Guid, BuffBotWire.Renew)]);
                }

                renewCasts++;
                Renew(due, round.Caller.Guid, CastNext, ok =>
                {
                    roundSelfDone |= !ok;
                    CastNext();
                });
                return;
            }

            roundSelfDone = true;
            if (renewCasts > 0)
            {
                Log(string.Create(c, $"round: its own lines renewed in {renewCasts} cast(s) before the first line: {Numbers()}"));
            }
        }

        var bot = round.Profile.Lines[line];
        var spell = new Spell(bot.SpellId);
        if (spell.NotFound)
        {
            Log(string.Create(c, $"TODO(m6-buffbot-spell): the spell {bot.SpellId} ({bot.Spell}) is not in ACE's spell table: the line is passed over"), ModManager.LogLevel.Error);
            line++;
            CastNext();
            return;
        }

        var skill = GetCreatureSkill(spell.GetMagicSkill()).Current;
        var reach = Math.Min(spell.BaseRangeConstant + (skill * spell.BaseRangeMod), Player.MaxRadarRange_Outdoors);
        var distance = GetDistance(target);
        if (distance > reach)
        {
            EndRound(BuffBotRoundEnd.OutOfReach, string.Create(c, $"{round.Caller.Name} stands {distance:0.#} m off, the reach is {reach:0.#} m"));
            return;
        }

        var slots = Slots(spell);
        if (BuffBotCasting.Missing(Desk.Ledger, slots) is { } missing)
        {
            EndRound(BuffBotRoundEnd.OutOfComponents, missing);
            return;
        }

        var cost = CalculateManaUsage(this, spell, target);
        if (Short(cost, bot.Spell, round.Caller.Guid, CastNext))
        {
            return;
        }

        var result = BuffBotCasting.Attempt(Desk.Ledger, slots, skill, spell.Power, spell.ComponentLoss, Mana.Current, cost, ManaPerSecond, HeartbeatSeconds, () => ThreadSafeRandom.Next(0.0f, 1.0f));
        switch (result.Kind)
        {
            case BuffBotCastKind.NoComponent:
                EndRound(BuffBotRoundEnd.OutOfComponents, result.Missing);
                return;
            case BuffBotCastKind.NoMana:
                Log(string.Create(c, $"rest: {bot.Spell} costs {result.Mana} and the bot holds {Mana.Current} of {Mana.MaxValue}: {result.RestSeconds} s at {ManaPerSecond:0.###} a second"));
                Do([new BuffBotAct(BuffBotActKind.Tell, round.Caller.Guid, BuffBotWire.Rest(result.RestSeconds))]);
                After(result.RestSeconds, CastNext);
                return;
        }

        // The wind-up as a human-animated creature casts: the magic stance, the spell words, the scarabs' gestures.
        CheckForHumanPreCast(spell);
        var words = spell._spellBase.GetSpellWords(DatManager.PortalDat.SpellComponentsTable);
        if (!string.IsNullOrWhiteSpace(words))
        {
            EnqueueBroadcast(new GameMessageHearSpeech(words, Name, Guid.Full, ChatMessageType.Spellcasting), LocalBroadcastRange);
        }

        var windup = PreCastMotion(target);
        After(windup, () =>
        {
            if (IsDestroyed || round is null)
            {
                return;
            }

            UpdateVitalDelta(Mana, -(int)result.Mana);
            BuffBotCasting.Apply(Desk.Ledger, result);
            manaSpent += result.Mana;
            tapersBurned += result.Burned.Count(s => s.Wcid == BuffBotRules.TaperWcid);
            scarabsBurned += result.Burned.Count(s => s.Wcid == BuffBotWire.FeeWcid);
            var on = Teller(round.Caller.Guid);
            string outcome;
            if (result.Kind == BuffBotCastKind.Landed && on is not null)
            {
                TryCastSpell_WithRedirects(spell, on, this);
                landed++;
                line++;
                lineFizzles = 0;
                outcome = "landed";
            }
            else
            {
                EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.Fizzle, 0.5f));
                fizzled++;
                lineFizzles++;
                outcome = on is null ? "the teller left during the wind-up" : "fizzled";
                if (lineFizzles >= BuffBotCasting.MaxFizzlesPerLine)
                {
                    Log(string.Create(c, $"{bot.Spell} fizzled {lineFizzles} times: the line is passed over"), ModManager.LogLevel.Warn);
                    line++;
                    lineFizzles = 0;
                }
            }

            PostCastMotion();
            Log(string.Create(c, $"cast {bot.Spell} ({bot.SpellId}) on {round.Caller.Name}: {outcome} (chance {result.Chance:P2}, skill {skill} vs {spell.Power}); mana {result.Mana}, {Mana.Current} of {Mana.MaxValue} left; burned {(result.Burned.Count == 0 ? "nothing" : string.Join(", ", result.Burned.Select(s => s.Name)))}; the pack tapers={Desk.Ledger.Tapers} scarabs={Desk.Ledger.Scarabs}"));
            After(GetPostCastTime(spell), CastNext);
        });
    }

    // ------------------------------------------------------------------------------------------------ its own casts: the dance and its buffs

    /// <summary>
    /// Before a cast of <paramref name="need"/> mana: the dance's next step (<see cref="BuffBotSelf.Next"/>) on the bot's book, vitals, skills, pack and ACE's costs. True when a step is under way (a Stamina to Mana,
    /// a Revitalize or, when the dance cannot run, a rest), which calls <paramref name="retry"/> at its end; false when the mana pays the cast now.
    /// </summary>
    private bool Short(uint need, string forWhat, uint? payer, Action retry)
    {
        var c = CultureInfo.InvariantCulture;
        var tranceSpell = new Spell(BuffBotSelf.StaminaToManaSpell);
        var reviveSpell = new Spell(BuffBotSelf.RevitalizeSpell);
        var trance = DanceSpell(tranceSpell);
        var revive = DanceSpell(reviveSpell);
        var state = new BuffBotDanceState(Mana.Current, Mana.MaxValue, Stamina.Current, Stamina.MaxValue, need, tranceSpell.NotFound ? 0 : tranceSpell.BaseMana, trance, revive, ManaPerSecond, HeartbeatSeconds);
        var step = BuffBotSelf.Next(state);
        if (step.Kind is BuffBotDanceKind.StaminaToMana or BuffBotDanceKind.Revitalize && danceCasts >= BuffBotSelf.MaxDanceCasts)
        {
            Log(string.Create(c, $"TODO(m6-buffbot-dance): {danceCasts} dance casts did not pay {need} for {forWhat} (mana {Mana.Current}, stamina {Stamina.Current}): the bot rests instead"), ModManager.LogLevel.Error);
            var seconds = BuffBotCasting.RestSeconds(need - Math.Min(need, Mana.Current), ManaPerSecond, HeartbeatSeconds);
            step = new BuffBotDanceStep(BuffBotDanceKind.Rest, $"the dance's cap of {BuffBotSelf.MaxDanceCasts} casts", seconds, "My stamina is spent.");
        }

        switch (step.Kind)
        {
            case BuffBotDanceKind.Paid:
                if (danceCasts > 0)
                {
                    Log(string.Create(c, $"dance: paid {forWhat} after {danceCasts} cast(s): {step.Why}"));
                }

                danceCasts = 0;
                danceTold = false;
                return false;
            case BuffBotDanceKind.StaminaToMana:
            case BuffBotDanceKind.Revitalize:
                if (payer is { } told && !danceTold)
                {
                    danceTold = true;
                    Do([new BuffBotAct(BuffBotActKind.Tell, told, BuffBotWire.Dance)]);
                }

                danceCasts++;
                var spell = step.Kind == BuffBotDanceKind.StaminaToMana ? tranceSpell : reviveSpell;
                var cost = step.Kind == BuffBotDanceKind.StaminaToMana ? trance!.Cost : revive!.Cost;
                Log(string.Create(c, $"dance {danceCasts} for {forWhat}: {step.Why}"));
                SelfCast(spell, cost, "dance", _ => retry());
                return true;
            default:
                Log(string.Create(c, $"rest: {forWhat} costs {need} and the bot holds {Mana.Current} of {Mana.MaxValue}, stamina {Stamina.Current} of {Stamina.MaxValue}: {step.Why}"));
                if (payer is { } hearer)
                {
                    Do([new BuffBotAct(BuffBotActKind.Tell, hearer, BuffBotWire.Rest(step.RestSeconds, step.Told))]);
                }

                danceCasts = 0;
                After(step.RestSeconds, retry);
                return true;
        }
    }

    /// <summary>A dance spell as the bot holds it now: in its book, the component its pack lacks, its chance on its skill, ACE's cost of this cast and the spell table's numbers; null when ACE has no such spell.</summary>
    private BuffBotDanceSpell? DanceSpell(Spell spell)
    {
        if (spell.NotFound)
        {
            return null;
        }

        var known = Biota.SpellIsKnown((int)spell.Id, BiotaDatabaseLock);
        var skill = GetCreatureSkill(spell.GetMagicSkill()).Current;
        return new BuffBotDanceSpell(
            known,
            BuffBotCasting.Missing(Desk.Ledger, Slots(spell)),
            BuffBotCasting.Chance(skill, spell.Power),
            known ? CalculateManaUsage(this, spell, this) : 0,
            spell.Proportion,
            spell.LossPercent,
            Math.Min(spell.Boost, spell.MaxBoost));
    }

    /// <summary>The self line to renew now (<see cref="BuffBotSelf.Due"/>), or null.</summary>
    private BuffBotSelfStatus? SelfDue() => BuffBotSelf.Due(SelfStatus());

    /// <summary>The bot's wanted self lines as it stands: in its book, the chance on its skill, ACE's time left on each (<c>Duration + StartTime</c>, 0 when none).</summary>
    private List<BuffBotSelfStatus> SelfStatus()
    {
        var list = new List<BuffBotSelfStatus>();
        foreach (var wanted in BuffBotSelf.Wanted(Schools))
        {
            var spell = new Spell(wanted.SpellId);
            if (spell.NotFound)
            {
                continue;
            }

            var on = EnchantmentManager.GetEnchantment(wanted.SpellId);
            var left = on is null ? 0 : Math.Max(0, on.Duration + on.StartTime);
            list.Add(new BuffBotSelfStatus(wanted, Biota.SpellIsKnown((int)wanted.SpellId, BiotaDatabaseLock), BuffBotCasting.Chance(GetCreatureSkill(spell.GetMagicSkill()).Current, spell.Power), left));
        }

        return list;
    }

    private static IReadOnlyList<BuffBotSchool>? schools;

    /// <summary>The schools the bot casts in: those of every profile's lines and of its dance (read once from ACE's spell table).</summary>
    private static IReadOnlyList<BuffBotSchool> Schools => schools ??= [.. BuffBotWire.Profiles.SelectMany(p => p.Lines).Select(l => l.SpellId)
        .Concat([BuffBotSelf.StaminaToManaSpell, BuffBotSelf.RevitalizeSpell])
        .Select(id => new Spell(id))
        .Where(s => !s.NotFound)
        .Select(s => s.School switch { MagicSchool.CreatureEnchantment => BuffBotSchool.Creature, MagicSchool.ItemEnchantment => BuffBotSchool.Item, MagicSchool.LifeMagic => BuffBotSchool.Life, _ => (BuffBotSchool?)null })
        .OfType<BuffBotSchool>()
        .Distinct()];

    /// <summary>One renewal of a self line: its components, its mana (the dance pays: <see cref="Short"/>), then the cast; <paramref name="done"/> with false when it could not be cast.</summary>
    private void Renew(BuffBotSelfStatus due, uint? payer, Action retry, Action<bool> done)
    {
        var c = CultureInfo.InvariantCulture;
        var spell = new Spell(due.Line.SpellId);
        if (BuffBotCasting.Missing(Desk.Ledger, Slots(spell)) is { } missing)
        {
            Log(string.Create(c, $"self: {due.Line.Spell} is not renewed: the pack holds no {missing}"), ModManager.LogLevel.Warn);
            done(false);
            return;
        }

        var cost = CalculateManaUsage(this, spell, this);
        if (Short(cost, string.Create(c, $"{due.Line.Spell} on itself ({due.SecondsLeft:0} s left)"), payer, retry))
        {
            return;
        }

        SelfCast(spell, cost, "renew", done);
    }

    /// <summary>
    /// One cast on itself by the same rules as a line (<see cref="BuffBotCasting.Attempt"/>: the components, the fizzle roll on its skill, the mana, the burns), the same wind-up and follow-through, and ACE's own cast
    /// on the bot; its numbers before and after go to the log. <paramref name="done"/> runs after the follow-through with true when it was cast (landed or fizzled).
    /// </summary>
    private void SelfCast(Spell spell, uint cost, string kind, Action<bool> done)
    {
        var c = CultureInfo.InvariantCulture;
        var skill = GetCreatureSkill(spell.GetMagicSkill()).Current;
        var result = BuffBotCasting.Attempt(Desk.Ledger, Slots(spell), skill, spell.Power, spell.ComponentLoss, Mana.Current, cost, ManaPerSecond, HeartbeatSeconds, () => ThreadSafeRandom.Next(0.0f, 1.0f));
        if (result.Kind is BuffBotCastKind.NoComponent or BuffBotCastKind.NoMana)
        {
            Log(string.Create(c, $"self: {kind} {spell.Name} not cast: {(result.Kind == BuffBotCastKind.NoComponent ? "no " + result.Missing : $"{result.Mana} mana and {Mana.Current} held")}"), ModManager.LogLevel.Warn);
            done(false);
            return;
        }

        CheckForHumanPreCast(spell);
        var words = spell._spellBase.GetSpellWords(DatManager.PortalDat.SpellComponentsTable);
        if (!string.IsNullOrWhiteSpace(words))
        {
            EnqueueBroadcast(new GameMessageHearSpeech(words, Name, Guid.Full, ChatMessageType.Spellcasting), LocalBroadcastRange);
        }

        var windup = PreCastMotion(this);
        After(windup, () =>
        {
            if (IsDestroyed)
            {
                return;
            }

            var before = Numbers();
            UpdateVitalDelta(Mana, -(int)result.Mana);
            BuffBotCasting.Apply(Desk.Ledger, result);
            if (round is not null)
            {
                selfMana += result.Mana;
                selfCasts++;
                tapersBurned += result.Burned.Count(s => s.Wcid == BuffBotRules.TaperWcid);
                scarabsBurned += result.Burned.Count(s => s.Wcid == BuffBotWire.FeeWcid);
            }

            var cast = result.Kind == BuffBotCastKind.Landed;
            if (cast)
            {
                TryCastSpell_WithRedirects(spell, this, this);
            }
            else
            {
                EnqueueBroadcast(new GameMessageScript(Guid, PlayScript.Fizzle, 0.5f));
            }

            PostCastMotion();
            Log(string.Create(c, $"self: {kind} {spell.Name} ({spell.Id}) on itself: {(cast ? "landed" : "fizzled")} (chance {result.Chance:P2}, skill {skill} vs {spell.Power}); mana {result.Mana} (ACE's cost {cost}); before: {before}; after: {Numbers()}; burned {(result.Burned.Count == 0 ? "nothing" : string.Join(", ", result.Burned.Select(s => s.Name)))}; the pack tapers={Desk.Ledger.Tapers} scarabs={Desk.Ledger.Scarabs}"));
            After(GetPostCastTime(spell), () => done(true));
        });
    }

    /// <summary>The numbers its own casts change: its three skills, Focus and Self, mana and stamina with their regeneration multipliers.</summary>
    private string Numbers() => string.Create(
        CultureInfo.InvariantCulture,
        $"Creature Enchantment {GetCreatureSkill(Skill.CreatureEnchantment).Current}, Life Magic {GetCreatureSkill(Skill.LifeMagic).Current}, Mana Conversion {GetCreatureSkill(Skill.ManaConversion).Current}, Focus {Focus.Current}, Self {Self.Current}, mana {Mana.Current} of {Mana.MaxValue} (regeneration x{EnchantmentManager.GetRegenerationMod(Mana):0.##}), stamina {Stamina.Current} of {Stamina.MaxValue} (x{EnchantmentManager.GetRegenerationMod(Stamina):0.##})");

    /// <summary>
    /// Between rounds, at its post: the self line due (<see cref="SelfDue"/>) is renewed, one at a time, <see cref="BuffBotSelf.SpreadSeconds"/> apart, the dance paying its mana; nobody hears it. A round that comes in
    /// meanwhile goes first once the cast in hand ends (it checks its own lines itself).
    /// </summary>
    private void Upkeep()
    {
        if (round is not null || IsDestroyed || walking || Desk.Phase != BuffBotPhase.Idle)
        {
            EndUpkeep();
            if (round is not null)
            {
                CastNext();
            }

            return;
        }

        if (SelfDue() is not { } due)
        {
            EndUpkeep();
            upkeepAt = Now + HeartbeatSeconds; // nothing due: look again a heartbeat on (ACE moves an enchantment's time once a heartbeat)
            return;
        }

        selfCasting = true;
        Renew(due, null, Upkeep, ok =>
        {
            EndUpkeep();
            upkeepAt = Now + (ok ? BuffBotSelf.SpreadSeconds : BuffBotSelf.BlockedSeconds);
            if (round is not null)
            {
                CastNext();
            }
        });
    }

    private void EndUpkeep()
    {
        if (selfCasting)
        {
            selfCasting = false;
            if (round is null)
            {
                SetCombatMode(CombatMode.NonCombat);
            }
        }
    }

    private void EndRound(BuffBotRoundEnd end, string? detail)
    {
        if (round is null)
        {
            return;
        }

        var r = round;
        round = null;
        SetCombatMode(CombatMode.NonCombat);
        Log(string.Create(CultureInfo.InvariantCulture, $"round '{r.Profile.Key}' for {r.Caller.Name}: {end}; {landed} of {r.Profile.Lines.Count} landed, {fizzled} fizzle(s), {manaSpent} mana; its own casts {selfCasts} ({selfMana} mana: renewals and the dance); {tapersBurned} taper(s) and {scarabsBurned} scarab(s) burned in {Now - roundStarted:0} s"));
        keeper.Save("a round's burns");
        Do(Desk.RoundEnded(end, detail, Now));
    }

    /// <summary>The foci formula of <paramref name="spell"/> as the pack holds it: each component's weenie (the dat's map), plural name and CDM.</summary>
    private static List<BuffBotSlot> Slots(Spell spell)
    {
        var slots = new List<BuffBotSlot>();
        foreach (var component in spell.Formula.GetFociFormula())
        {
            var wcid = Spell.GetComponentWCID(component);
            var cdm = SpellFormula.SpellComponentsTable.SpellComponents.TryGetValue(component, out var dat) ? dat.CDM : 1.0f;
            var name = wcid switch
            {
                BuffBotRules.TaperWcid => BuffBotRules.TaperName,
                BuffBotWire.FeeWcid => BuffBotRules.ScarabName,
                _ => dat?.Name ?? component.ToString(CultureInfo.InvariantCulture),
            };
            slots.Add(new BuffBotSlot(wcid, name, cdm));
        }

        return slots;
    }

    // ------------------------------------------------------------------------------------------------ the trip

    private void StartTrip()
    {
        SetCombatMode(CombatMode.NonCombat);
        (walking, back, point, asks, tripStarted) = (true, false, 1, 0, Now);
        Log(string.Create(CultureInfo.InvariantCulture, $"restock: walks to Archmage Cindrue ({BuffBotRules.ShopRoute.Count - 1} legs, about 156 m); the pack {Desk.Ledger.Summary()}"));
        Ask();
    }

    private void Ask()
    {
        var p = BuffBotRules.ShopRoute[point];
        (pointSince, pointBest) = (Now, Off(p));
        MoveTo(new Position(p.Cell, p.X, p.Y, p.Z, 0f, 0f, 0f, 1f), GetRunRate());
    }

    private float Off(BuffBotWaypoint p) => Vector2.Distance(new Vector2(Location.PositionX, Location.PositionY), new Vector2(p.X, p.Y));

    /// <summary>Each second of a trip: the point reached, the walk stalled, or the trip's time up.</summary>
    private void Walk()
    {
        var c = CultureInfo.InvariantCulture;
        var route = BuffBotRules.ShopRoute;
        var p = route[point];
        var off = Off(p);
        if (off <= BuffBotRules.ArriveMetres)
        {
            asks = 0;
            if (!back && point == route.Count - 1)
            {
                walking = false;
                Log(string.Create(c, $"restock: at the shop (cell 0x{Location.Cell:X8}) after {Now - tripStarted:0} s"));
                Shop();
                return;
            }

            if (back && point == 0)
            {
                walking = false;
                Done(true, string.Create(c, $"there and back in {Now - tripStarted:0} s"));
                return;
            }

            point += back ? -1 : 1;
            Ask();
            return;
        }

        if (off < pointBest - 0.3f)
        {
            (pointBest, pointSince) = (off, Now);
        }

        if (Now - tripStarted > TripSeconds)
        {
            Lost(string.Create(c, $"the trip took over {TripSeconds:0} s ({off:0.#} m from point {point})"));
        }
        else if (Now - pointSince > StallSeconds)
        {
            if (++asks > 2)
            {
                Lost(string.Create(c, $"stuck {off:0.#} m from point {point} (0x{p.Cell:X8} {p.X:0.#}, {p.Y:0.#}) after {asks - 1} more asks"));
                return;
            }

            Log(string.Create(c, $"restock: no nearer to point {point} in {StallSeconds:0} s ({off:0.#} m off): the move-to is asked again ({asks} of 2)"), ModManager.LogLevel.Warn);
            Ask();
        }
    }

    private void Shop()
    {
        var c = CultureInfo.InvariantCulture;
        var vendor = CurrentLandblock?.GetObject(new ObjectGuid(BuffBotRules.CindrueGuid)) as Vendor;
        var taper = DatabaseManager.World.GetCachedWeenie(BuffBotRules.TaperWcid);
        var scarab = DatabaseManager.World.GetCachedWeenie(BuffBotWire.FeeWcid);
        string? refusal = vendor is null ? $"Archmage Cindrue (0x{BuffBotRules.CindrueGuid:X8}) is not a vendor on the landblock"
            : vendor.WeenieClassId != BuffBotRules.CindrueWcid ? $"0x{BuffBotRules.CindrueGuid:X8} is weenie {vendor.WeenieClassId}, not {BuffBotRules.CindrueWcid}"
            : vendor.Biota.PropertiesCreateList?.Any(r => r.DestinationType == DestinationType.Shop && r.WeenieClassId == BuffBotRules.TaperWcid) != true ? "her shop lists no Prismatic Taper"
            : taper is null || scarab is null ? "ACE's weenie cache holds no taper or no scarab"
            : null;
        if (refusal is not null)
        {
            Log("restock: no trade: " + refusal, ModManager.LogLevel.Error);
            WalkBack(false, refusal);
            return;
        }

        Rotate(vendor!);
        var buysScarabs = ((ItemType)(vendor!.MerchandiseItemTypes ?? 0) & scarab!.GetItemType()) != 0;
        var pack = Desk.Ledger;
        var trade = BuffBotShop.Plan(pack, vendor.SellPrice ?? 1.0, taper!.GetValue() ?? 0, vendor.BuyPrice ?? 1.0, scarab.GetValue() ?? 0, buysScarabs);
        var before = pack.Summary();
        BuffBotShop.Apply(pack, trade);
        if (trade.ScarabsSold > 0)
        {
            Log(string.Create(c, $"restock: sold {trade.ScarabsSold} {BuffBotRules.ScarabName} (over the keep {BuffBotRules.ScarabKeep}) to {vendor.Name} for {trade.SaleTotal} pyreals at her buy price {vendor.BuyPrice:0.##} x Value {scarab.GetValue()}"));
        }

        Log(string.Create(c, $"restock: bought {trade.TapersBought} {BuffBotRules.TaperName} from {vendor.Name} for {trade.BuyTotal} pyreals at her sell price {vendor.SellPrice:0.##} x Value {taper.GetValue()}{(trade.Short ? $"; the purse did not reach {BuffBotRules.RestockTo}" : string.Empty)}; the pack {before} -> {pack.Summary()}"));
        keeper.Save("the trade");
        After(ShopSeconds, () => WalkBack(trade.TapersBought > 0, string.Create(c, $"bought {trade.TapersBought} tapers, sold {trade.ScarabsSold} scarabs")));
    }

    private void WalkBack(bool ok, string why)
    {
        (walking, back, point, asks) = (true, true, BuffBotRules.ShopRoute.Count - 2, 0);
        tripOk = ok;
        tripWhy = why;
        Ask();
    }

    private void Done(bool reached, string how)
    {
        Do(Desk.TripEnded(reached && tripOk, $"{tripWhy}; {how}", Now));
    }

    private void Lost(string why)
    {
        walking = false;
        Log("restock: lost: " + why + ": the bot recalls to its post", ModManager.LogLevel.Warn);
        EnqueueBroadcast(new GameMessageSystemChat($"{Name} is recalling to the lifestone.", ChatMessageType.Recall), LocalBroadcastRange, ChatMessageType.Recall);
        if (post is not null)
        {
            FakeTeleport(post);
        }

        tripOk = false;
        tripWhy = why;
        Done(false, "recalled");
    }

    // ------------------------------------------------------------------------------------------------ helpers

    /// <summary>The online player <paramref name="guid"/> in the world within <see cref="TellerMetres"/> of the bot (its landblock or one beside it, as ACE finds a direct talk's target), or null.</summary>
    private Player? Teller(uint guid) =>
        PlayerManager.GetOnlinePlayer(guid) is { CurrentLandblock: not null, IsDestroyed: false } p && GetDistance(p) <= TellerMetres ? p : null;

    /// <summary>The metres within which a teller is still with the bot (a landblock's width); the spell's own reach is checked at each cast.</summary>
    private const float TellerMetres = 192f;

    private void After(double seconds, Action what)
    {
        var chain = new ActionChain();
        chain.AddDelaySeconds(Math.Max(0, seconds));
        chain.AddAction(this, () =>
        {
            try
            {
                what();
            }
            catch (Exception e)
            {
                Log("a step threw: " + e, ModManager.LogLevel.Error);
            }
        });
        chain.EnqueueChain();
    }

    private static void Log(string text, ModManager.LogLevel level = ModManager.LogLevel.Info) => ModManager.Log(ModLog.BuffBot + text, level);
}
