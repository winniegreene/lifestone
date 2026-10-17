// Re-expresses ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Player_Death.cs (OnDeath, InflictVitaePenalty, Die, ThreadSafeTeleportOnDeath, HandleActionDie, HandleSuicide, CalculateDeathItems, GetNumItemsDropped, DropMessage,
// SetLifestoneProtection, HandleLifestoneProtection, LifestoneProtectionTick), Creature_Death.cs (CreateCorpse, GetDeathMessage), Monster_Melee.cs (MeleeAttack, GetCombatManeuver, DoSwingMotion), Monster_Combat.cs (DoAttackStance),
// Player_Combat.cs (TakeDamage, OnEvade), Entity/DamageEvent.cs (the lifestone protection), Entity/Strings.cs (the victim sentences), WorldObjects/Lifestone.cs (ActOnUse), WorldObjects/Managers/EnchantmentManager.cs (UpdateVitae, GetMinVitae),
// Player_Character.cs and ACE.Database CharacterExtensions.cs (AddSpellToBar) (AGPL-3.0) [with changes: the stand-in's tusker is the only monster and attacks in a placeholder rhythm, with placeholder damage and reach; the stand-in
// has no weenie database, so the corpse, the lifestone and the lost items are named placeholders; the stand-in's own pack is the only pack that can lose items].
using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.World;
using Lifestone.Data.Animation;
using Lifestone.Net.Dispatch;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Net.Types;

namespace Lifestone.Client.Session.StandIn;

/// <summary>Where a stand-in tusker is in its attack (<c>Monster_Melee.cs</c> <c>MeleeAttack</c>: the stance, the swing, the strike at the attack frame).</summary>
internal enum AttackPhase
{
    /// <summary>Waiting for the next attack time and a player in reach.</summary>
    Idle,

    /// <summary>The attack stance is being taken (<c>DoAttackStance</c>); the swing follows at <c>PhaseAt</c>.</summary>
    Stance,

    /// <summary>The swing is playing; the strike lands at <c>PhaseAt</c>.</summary>
    Swing,
}

/// <summary>
/// The stand-in's death (M3 3.2 db): the tusker that attacks, the player's death in ACE's order, the corpse, the teleport to the lifestone and the protection there, the suicide, the lifestone's Use and the spell bars. Every number ACE keeps in
/// its world database or its physics, which the stand-in has not, is a named, logged placeholder.
/// </summary>
/// <remarks>
/// <para><b>The tusker's attack</b> (<c>TODO(m3-standin-tusker-attack)</c>): a placed creature with <see cref="StandInObject.Attacks"/> looks at the player every <see cref="AttackPollSeconds"/>; within <see cref="AttackReachMetres"/> and
/// past its next attack time it takes the attack stance (an UpdateMotion of the creature, as <c>DoAttackStance</c> does), swings (an UpdateMotion with an attack action: high, medium or low at random, <c>GetCombatManeuver</c>) and
/// <see cref="StrikeSeconds"/> later strikes; the next attack is 2 to 3 s after the swing began (<c>MeleeAttack</c> sets <c>NextAttackTime</c> from the swing's start plus the animation plus a random delay). A strike is a hit with the chance
/// <see cref="HitChance"/> and <see cref="DamageMin"/> to <see cref="DamageMax"/> piercing damage, or an evade. The poll allocates nothing: the creatures are a list read by index, and the one delegate is made once.</para>
/// <para><b>A hit</b> (<c>Player_Combat.cs</c> <c>TakeDamage</c> 478-560): the health update 0x02E9, the stamina's when the player is in a combat mode, then (the player lives) the DefenderNotification 0x01B2, the hit sound F750 and the splatter F755,
/// and the wound sound at a tenth of the health or more. A death by a hit sends the 0x01AC victim notice and the 0x019E broadcast after those updates (<c>OnDeath</c>), then <c>Die</c>. A blow while the lifestone protection holds is the 0x028A
/// 0x0502 with the ShieldUpBlue script (<c>DamageEvent.cs</c> 184-187) and nothing else.</para>
/// <para><b>Die</b> (<c>Player_Death.cs</c> 168-244): the health to 0 (0x02E9 when it changed), NumDeaths up, the Dead motion F74C, 0x02E9 health 0, 0x02CD NumDeaths, for a suicide the WeenieError YouKilledYourself, DeathLevel and VitaeCpPool (0x02CD),
/// the vitae 0x02C2 (spell 666, 0.95 and 5% lower per death, never below the floor of the level), the purge 0x02C6; then the Dead animation plus 1 s later (<see cref="DeathAnimationSeconds"/> is the Dead animation's placeholder length) the
/// lost items, the corpse and the teleport; on arrival the stance NonCombat, the protection (60 s), and 3 s later the three vitals at 75% of their maximum.</para>
/// </remarks>
internal sealed partial class StandInWorld
{
    /// <summary>The seconds between the stand-in's looks at its attackers (a stand-in for ACE's monster tick; allocation-free).</summary>
    internal const double AttackPollSeconds = 0.1;

    /// <summary>The distance within which a tusker attacks (a placeholder: ACE's is the monster's melee range from its weenie and the bodies' radii; <c>TODO(m3-standin-tusker-attack)</c>).</summary>
    internal const float AttackReachMetres = 2f;

    /// <summary>The shortest and the longest wait from a swing's start to the next attack (a placeholder rhythm; ACE's is the animation plus up to <c>PowerupTime</c>).</summary>
    internal const double AttackIntervalMin = 2.0;

    /// <summary>See <see cref="AttackIntervalMin"/>.</summary>
    internal const double AttackIntervalMax = 3.0;

    /// <summary>How long taking the attack stance takes (placeholder: ACE's is the motion table's).</summary>
    internal const double StanceSeconds = 0.5;

    /// <summary>From the swing's start to the strike (placeholder: ACE reads the attack frame from the motion table).</summary>
    internal const double StrikeSeconds = 0.5;

    /// <summary>The chance that a strike hits (placeholder: ACE rolls the creature's attack skill against the player's defence).</summary>
    internal const double HitChance = 0.7;

    /// <summary>The smallest and largest damage of a hit (a placeholder range: ACE's is the weenie's body part, <c>GetBaseDamage</c>).</summary>
    internal const int DamageMin = 8;

    /// <summary>See <see cref="DamageMin"/>.</summary>
    internal const int DamageMax = 16;

    /// <summary>The seed of the attacks' rolls.</summary>
    internal const int AttackSeed = 0x5EED_A77C;

    /// <summary>The seed of the death's rolls (the sentence, the lost items).</summary>
    internal const int PlayerDeathSeed = 0x5EED_D1ED;

    /// <summary>ACE's <c>DamageType.Pierce</c>: the damage type the tusker's tusks deal here (a placeholder; the weenie's is the database's).</summary>
    internal const uint PierceDamage = 0x2;

    /// <summary>ACE's <c>DamageLocation.Chest</c> (<c>Network/Enum/DamageLocation.cs</c>): the body part every hit is told to land on (the stand-in has no body part rolls).</summary>
    internal const uint ChestLocation = 0x1;

    // Low words of ACE's MotionCommand.AttackHigh1, AttackMed1 and AttackLow1 (MotionCommand.cs 105-107) and Sanctuary (94).
    private const uint AttackHigh1 = 0x10000062;
    private const uint AttackMed1 = 0x10000063;
    private const uint AttackLow1 = 0x10000064;
    private const uint SanctuaryMotion = 0x10000057;

    /// <summary>ACE's <c>WeenieError.YoureTooBusy</c> (0x001D), the answer to a suicide while dead or teleporting.</summary>
    internal const uint YoureTooBusy = 0x001D;

    /// <summary>ACE's <c>WeenieError.YouKilledYourself</c> (0x004A).</summary>
    internal const uint YouKilledYourself = 0x004A;

    /// <summary>ACE's <c>WeenieError.LifestoneMagicProtectsYou</c> (0x0502).</summary>
    internal const uint LifestoneProtectsYou = 0x0502;

    /// <summary>ACE's <c>WeenieError.YouHaveMovedTooFar</c> (0x0498): a Use of a lifestone from too far away.</summary>
    internal const uint YouHaveMovedTooFar = 0x0498;

    /// <summary>The seconds between a suicide's lines and before its death (<c>HandleSuicide</c>: five lines, each followed by 3 s).</summary>
    internal const double SuicideStepSeconds = 3.0;

    /// <summary>ACE's <c>SuicideMessages</c>.</summary>
    internal static readonly string[] SuicideLines = ["I feel faint...", "My sight is growing dim...", "My life is flashing before my eyes...", "I see a light...", "Oh cruel, cruel world!"];

    /// <summary>The Dead animation's length in seconds (the stand-in does not read the motion table: <c>TODO(m3-standin-corpse)</c>); ACE waits this plus 1 s before the corpse and the teleport.</summary>
    internal const double PlayerDeathAnimationSeconds = 2.0;

    /// <summary>The seconds after the arrival at which the vitals return to 75% (<c>ThreadSafeTeleportOnDeath</c>).</summary>
    internal const double RevivalSeconds = 3.0;

    /// <summary>The vitals' share after a death.</summary>
    internal const float RevivalShare = 0.75f;

    /// <summary>The lifestone protection's length (<c>LifestoneProtectionTime</c>, one minute).</summary>
    internal const double ProtectionSeconds = 60.0;

    /// <summary>The vitae's penalty per death and the deepest penalty ACE allows (<c>vitae_penalty</c> 0.05 and <c>vitae_penalty_max</c> 0.40: the floor of 0.60).</summary>
    internal const float VitaePenalty = 0.05f;

    /// <summary>See <see cref="VitaePenalty"/>.</summary>
    internal const float VitaePenaltyMax = 0.40f;

    /// <summary>The farthest from a lifestone a Use works (placeholder: ACE walks the player to the lifestone's use radius first).</summary>
    internal const float LifestoneUseRadiusMetres = 10f;

    /// <summary>The Magic line a lifestone Use shows: a labelled placeholder, the weenie's own UseMessage being the world database's (<c>TODO(m3-standin-lifestone-weenie)</c>).</summary>
    internal const string PlaceholderUseMessage = "You have bound yourself to the stand-in lifestone (a placeholder: the weenie's own text is the world database's).";

    /// <summary>How long the Sanctuary motion plays before the bind (placeholder: the motion table's length).</summary>
    internal const double SanctuarySeconds = 3.0;

    /// <summary>ACE's <c>PropertyInt.NumDeaths</c> (43).</summary>
    internal const uint PropertyNumDeaths = 43;

    private const uint PropertyDeathLevel = 139;
    private const uint PropertyVitaeCpPool = 129;
    private const uint SoundLifestoneOn = 0x51;
    private const uint SoundHitFlesh1 = 0x30;
    private const uint SoundWound3 = 0x0E; // Wound3 (Ooh!): slashing, piercing and undefined attacks above a tenth of the health (Player_Combat.cs 538-553)
    private const uint SoundUnwieldObject = 0x8D;
    private const uint ScriptShieldUpBlue = 0x33;
    private const uint ScriptSplatterFirst = 0x5B; // SplatterLowLeftBack; the twelve follow in ACE's order: height (Low, Mid, Up), side (Left, Right), facing (Back, Front)
    private const uint CorpseWeenieClass = 21; // TODO(m3-standin-corpse-weenie): the corpse weenie's class id is the world database's
    private const uint CorpseItemType = 0x200; // Container
    private const uint FirstCorpseGuid = 0x80030000;
    private const int LevelDropsWielded = 35;

    private static readonly string[] PierceVictimLines =
    [
        "{1} runs you through!",
        "You are fatally punctured by {1}!",
        "Your perforated corpse falls before {1}!",
        "Your death is preceded by a sharp, stabbing pain, courtesy of {1}!",
    ];

    private static readonly string[] PierceBroadcastLines =
    [
        "{1} runs {0} through!",
        "{0} is fatally punctured by {1}!",
        "{0}'s perforated corpse falls before {1}!",
        "{0}'s death is preceded by a sharp, stabbing pain, courtesy of {1}!",
    ];

    private readonly Random _attackRandom = new(AttackSeed);
    private readonly Random _playerDeathRandom = new(PlayerDeathSeed);
    private readonly List<StandInCreature> _attackers = [];
    private readonly Dictionary<uint, StandInObject> _placed = [];
    private Action<IAcSessionSink>? _attackPoll;
    private bool _pollScheduled;
    private uint _nextCorpseGuid = FirstCorpseGuid;
    private bool _dying;
    private bool _suicideInProgress;
    private bool _protected;
    private int _protectionSerial;
    private (uint Cell, Vector3 Origin)? _sanctuary;
    private List<List<uint>>? _bars;

    /// <summary>Blows the tuskers have landed on the player.</summary>
    internal int TuskerHits { get; private set; }

    /// <summary>Strikes the player evaded.</summary>
    internal int TuskerEvades { get; private set; }

    /// <summary>Strikes the lifestone protection turned away.</summary>
    internal int ProtectedBlows { get; private set; }

    /// <summary>Deaths of the player the stand-in has played (a suicide counts).</summary>
    internal int PlayerDeaths { get; private set; }

    /// <summary>The corpses of the player the stand-in has created.</summary>
    internal int Corpses { get; private set; }

    /// <summary>True from the player's death until the vitals come back (ACE's <c>IsInDeathProcess</c> and a dead player).</summary>
    internal bool Dying => _dying;

    /// <summary>True while the lifestone protection holds.</summary>
    internal bool UnderProtection => _protected;

    /// <summary>Where the player's death teleport goes, once a lifestone bound it (null before: the visit's start).</summary>
    internal (uint Cell, Vector3 Origin)? Sanctuary => _sanctuary;

    /// <summary>The items that left the stand-in's pack for the last corpse, by name and count (the tests read it).</summary>
    internal List<(string Name, int Count)> LastDrops { get; } = [];

    // --- the tusker's attack ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private void StartAttacker(StandInCreature creature)
    {
        Placeholder("m3-standin-tusker-attack", "a placed tusker attacks a player within " + AttackReachMetres + " m every " + AttackIntervalMin + " to " + AttackIntervalMax + " s, hitting " + (int)(HitChance * 100) + "% of the time for " + DamageMin + " to " + DamageMax
            + " piercing damage: ACE's monster rolls its skill against the player's defence, takes its damage from the weenie's body part and times the attack by its motion table");
        _attackers.Add(creature);
        if (_pollScheduled)
        {
            return;
        }

        _pollScheduled = true;
        _attackPoll ??= PollAttackers;
        _reducer.StandIn!.At(_reducer.Clock.Now + AttackPollSeconds, _attackPoll);
    }

    private void PollAttackers(IAcSessionSink sink)
    {
        var now = _reducer.Clock.Now;
        var live = false;
        for (var i = _attackers.Count - 1; i >= 0; i--)
        {
            var creature = _attackers[i];
            if (creature.Dead)
            {
                _attackers.RemoveAt(i);
                continue;
            }

            live = true;
            Step(sink, creature, now);
        }

        if (live)
        {
            _reducer.StandIn!.At(now + AttackPollSeconds, _attackPoll!);
        }
        else
        {
            _pollScheduled = false;
        }
    }

    private void Step(IAcSessionSink sink, StandInCreature creature, double now)
    {
        switch (creature.Phase)
        {
            case AttackPhase.Idle:
                if (now >= creature.NextAttackAt && PlayerCanBeAttacked && InReach(creature))
                {
                    if (!creature.InStance)
                    {
                        // DoAttackStance: the stance motion first; the swing waits for it.
                        creature.InStance = true;
                        creature.Phase = AttackPhase.Stance;
                        creature.PhaseAt = now + StanceSeconds;
                        SendCreatureStance(sink, creature.Guid);
                    }
                    else
                    {
                        Swing(sink, creature, now);
                    }
                }

                break;
            case AttackPhase.Stance:
                if (now >= creature.PhaseAt)
                {
                    Swing(sink, creature, now);
                }

                break;
            default:
                if (now >= creature.PhaseAt)
                {
                    creature.Phase = AttackPhase.Idle;
                    Strike(sink, creature);
                }

                break;
        }
    }

    private bool PlayerCanBeAttacked => !_dying && _reducer.State.Self.Qualities.TryGetVitalLevels(1, out var health, out _) && health > 0;

    private bool InReach(StandInCreature creature)
    {
        var self = _reducer.State.Self;
        if (self.Position is not { } here || !_reducer.State.Objects.TryGet(creature.Guid, out var body) || body.Position is not { } there)
        {
            return false;
        }

        var a = here.ToGlobal();
        var b = there.ToGlobal();
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dy * dy) + (dz * dz) <= AttackReachMetres * AttackReachMetres;
    }

    private void Swing(IAcSessionSink sink, StandInCreature creature, double now)
    {
        // GetCombatManeuver: a random attack height (apparently by the monster's height over the player's, TODO there); DoSwingMotion: the attack action with the target.
        creature.AttackHeight = _attackRandom.Next(0, 3);
        var command = creature.AttackHeight switch { 0 => AttackLow1, 1 => AttackMed1, _ => AttackHigh1 };
        creature.ActionStamp = (ushort)((creature.ActionStamp + 1) & 0x7FFF);
        var stamps = _reducer.State.Objects.TryGet(creature.Guid, out var body) ? body.Stamps : default;
        var wire = MotionCommandTable.ToWire(MotionCommands.HandCombat);
        var state = new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = wire,
            Style = wire,
            Commands = [new MotionItem(MotionCommandTable.ToWire(command), creature.ActionStamp, false, 1f)],
        };
        AnswerLog.Add("F74C");
        sink.OnUpdateMotion(Context(sink), new UpdateMotionMsg(creature.Guid, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, state));
        creature.Phase = AttackPhase.Swing;
        creature.PhaseAt = now + StrikeSeconds;
        creature.NextAttackAt = now + AttackIntervalMin + (_attackRandom.NextDouble() * (AttackIntervalMax - AttackIntervalMin));
    }

    private void SendCreatureStance(IAcSessionSink sink, uint guid) => SendCreatureMotion(sink, guid, MotionCommands.HandCombat, MotionCommands.Ready);

    /// <summary>The strike: lifestone protection, an evade or a hit (<c>DamageEvent.CalculateDamage</c> then <c>TakeDamage</c> or <c>OnEvade</c>).</summary>
    private void Strike(IAcSessionSink sink, StandInCreature creature)
    {
        if (creature.Dead || !PlayerCanBeAttacked)
        {
            return;
        }

        var self = _reducer.State.Self;
        if (_protected)
        {
            ProtectedBlows++;
            AnswerLog.Add("028A");
            sink.OnWeenieError(Context(sink), new GameEventHeader(self.Guid, 0, 0x028A), new WeenieErrorEvent(LifestoneProtectsYou, null)); // HandleLifestoneProtection (Player_Death.cs 913-917)
            PlayEffect(sink, self.Guid, ScriptShieldUpBlue, 1f);
            return;
        }

        if (_attackRandom.NextDouble() >= HitChance)
        {
            TuskerEvades++;
            if (self.Qualities.CombatMode != CombatModes.NonCombat)
            {
                // OnEvade: a combat mode loses a stamina point (the stand-in knows no defence skill, so the untrained rule: always).
                UpdateVitalDelta(sink, Stamina, -1);
            }

            AnswerLog.Add("01B4");
            sink.OnEvasionDefenderNotification(Context(sink), new GameEventHeader(self.Guid, 0, 0x01B4), new EvasionDefenderNotificationEvent(creature.Name));
            return;
        }

        var amount = _attackRandom.Next(DamageMin, DamageMax + 1);
        if (!self.Qualities.TryGetVitalLevels(1, out var current, out var maxHealth) || maxHealth == 0)
        {
            return;
        }

        TuskerHits++;
        var percent = (float)amount / maxHealth;
        UpdateVitalDelta(sink, Health, -amount); // UpdateVitalDelta(Health, -amount): 0x02E9 with the clamped value
        if (self.Qualities.CombatMode != CombatModes.NonCombat)
        {
            UpdateVitalDelta(sink, Stamina, -1);
        }

        if (amount >= current)
        {
            // OnDeath(new DamageHistoryInfo(source), damageType) then Die().
            var pick = _playerDeathRandom.Next(PierceVictimLines.Length);
            var victim = string.Format(CultureInfo.InvariantCulture, PierceVictimLines[pick], self.Name ?? "You", creature.Name);
            var broadcast = string.Format(CultureInfo.InvariantCulture, PierceBroadcastLines[pick], self.Name ?? "You", creature.Name);
            AnswerLog.Add("01AC");
            sink.OnVictimNotification(Context(sink), new GameEventHeader(self.Guid, 0, 0x01AC), new VictimNotificationEvent(victim));
            AnswerLog.Add("019E");
            sink.OnPlayerKilled(Context(sink), new PlayerKilledMsg(broadcast, self.Guid, creature.Guid));
            Die(sink, suicide: false);
            return;
        }

        AnswerLog.Add("01B2");
        sink.OnDefenderNotification(Context(sink), new GameEventHeader(self.Guid, 0, 0x01B2), new DefenderNotificationEvent(creature.Name, PierceDamage, percent, (uint)amount, ChestLocation, false, 0));
        PlaySound(sink, self.Guid, SoundHitFlesh1, 1f);
        PlayEffect(sink, self.Guid, ScriptSplatterFirst + (uint)(creature.AttackHeight * 4) + (uint)(_attackRandom.Next(2) * 2) + 1, 1f); // Splatter<height><Left|Right>Front: the stand-in always strikes from the front
        if (percent >= 0.1f)
        {
            PlaySound(sink, self.Guid, SoundWound3, 1f);
        }
    }

    private void PlaySound(IAcSessionSink sink, uint guid, uint sound, float volume)
    {
        AnswerLog.Add("F750");
        sink.OnSound(Context(sink), new SoundMsg(guid, sound, volume));
    }

    /// <summary>Takes a placed object out of the world (a check's clean-up): a creature stops attacking at once, and the client is told with a DeleteObject.</summary>
    internal void RemoveObject(uint guid)
    {
        if (_creatures.TryGetValue(guid, out var creature))
        {
            creature.Dead = true;
        }

        _placed.Remove(guid);
        Later(sink => DeleteObject(sink, guid));
    }

    // --- the player's death -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// ACE's <c>Die</c> for the player (<c>Player_Death.cs</c> 168-244): the Dead motion and the updates, the vitae and the purge, then the corpse and the teleport after the Dead animation plus a second.
    /// </summary>
    private void Die(IAcSessionSink sink, bool suicide)
    {
        var self = _reducer.State.Self;
        _dying = true;
        _suicideInProgress = false;
        PlayerDeaths++;
        if (self.Qualities.TryGetVitalLevels(1, out var current, out _) && current > 0)
        {
            UpdateVitalDelta(sink, Health, -(int)current); // UpdateVital(Health, 0): sent only when it changed
        }

        var deaths = (self.Qualities.TryGetInt(PropertyNumDeaths, out var before) ? before : 0) + 1;
        SendPlayerMotion(sink, MotionCommands.NonCombat, MotionCommands.Dead);
        AnswerLog.Add("02E9");
        PrivateUpdate(sink, PrivateUpdateKind.VitalCurrent, Health, 0);
        AnswerLog.Add("02CD");
        PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, PropertyNumDeaths, deaths);
        if (suicide)
        {
            AnswerLog.Add("028A");
            sink.OnWeenieError(Context(sink), new GameEventHeader(self.Guid, 0, 0x028A), new WeenieErrorEvent(YouKilledYourself, null));
        }

        InflictVitaePenalty(sink);
        AnswerLog.Add("02C6");
        sink.OnMagicPurgeEnchantments(Context(sink), new GameEventHeader(self.Guid, 0, 0x02C6), default); // AugmentationSpellsRemainPastDeath == 0: every enchantment goes (the vitae is not in the lists)
        Placeholder("m3-standin-corpse", "ACE waits the Dead animation's length (the motion table's) plus a second before the corpse and the teleport: the stand-in waits " + PlayerDeathAnimationSeconds + " s plus 1");
        _reducer.StandIn!.At(_reducer.Clock.Now + PlayerDeathAnimationSeconds + 1.0, CorpseAndTeleport);
    }

    /// <summary><c>InflictVitaePenalty</c> (<c>Player_Death.cs</c> 117-132) with <c>EnchantmentManager.UpdateVitae</c> (413-448).</summary>
    private void InflictVitaePenalty(IAcSessionSink sink)
    {
        var self = _reducer.State.Self;
        var level = self.Qualities.Level;
        AnswerLog.Add("02CD");
        PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, PropertyDeathLevel, level);
        AnswerLog.Add("02CD");
        PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, PropertyVitaeCpPool, 0);
        var vitae = self.Qualities.TryGetVitae(out var old) ? old.Enchantment.StatModValue - VitaePenalty : 1f - VitaePenalty;
        vitae = Math.Clamp(vitae, MinimumVitae(level), 1f);
        Placeholder("m3-standin-vitae-spell", "the vitae spell's stat-mod type and key are ACE's world database's, not the dat's: the stand-in sends the vitae bit and the multiplicative bit, and its penalties are ACE's defaults (5% a death, no deeper than 40%)");
        var info = FindSpell(EnchantmentRules.VitaeSpellId);
        var enchantment = new EnchantmentDesc(EnchantmentRules.VitaeSpellId, 0, (ushort)(info?.Category ?? EnchantmentRules.VitaeSpellId), 1, info?.Power ?? 0u, 0.0, -1.0, self.Guid, 0f, 0f, 0.0, EnchantmentRules.VitaeKind | EnchantmentRules.Multiplicative, 0, vitae, 0, EnchantmentBucket.Vitae);
        AnswerLog.Add("02C2");
        sink.OnMagicUpdateEnchantment(Context(sink), new GameEventHeader(self.Guid, 0, 0x02C2), new MagicUpdateEnchantmentEvent(enchantment));
    }

    /// <summary><c>GetMinVitae</c> (<c>EnchantmentManager.cs</c> 391-408): the deepest penalty is 3% a level above the first, between 1% and the global maximum.</summary>
    internal static float MinimumVitae(int level)
    {
        var maxPenalty = Math.Clamp((level - 1) * 3, 1, (int)Math.Round(VitaePenaltyMax * 100));
        return (100 - maxPenalty) / 100f;
    }

    private void SendPlayerMotion(IAcSessionSink sink, uint stance, uint forward)
    {
        var guid = _reducer.State.Self.Guid;
        var stamps = _reducer.State.Objects.TryGet(guid, out var body) ? body.Stamps : default;
        var wire = MotionCommandTable.ToWire(stance);
        var state = new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = wire,
            Style = wire,
            Forward = forward == MotionCommands.Ready ? null : MotionCommandTable.ToWire(forward),
        };
        AnswerLog.Add("F74C");
        sink.OnUpdateMotion(Context(sink), new UpdateMotionMsg(guid, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, state));
    }

    private void CorpseAndTeleport(IAcSessionSink sink)
    {
        var self = _reducer.State.Self;
        var level = Math.Max(self.Qualities.Level, 1);
        var here = self.Position;
        var dropped = LoseItems(sink, level);
        if (here is { } at && AcCoordinates.IsOutdoorCell(at.CellId))
        {
            if (dropped > 0 && AcCoordinates.TryGetMapCoordinates(at.ToGlobal(), out var coordinates))
            {
                Line(sink, "Your corpse is located at (" + AcCoordinates.FormatMapCoordinates(coordinates) + ").");
            }
        }

        if (dropped == 0)
        {
            Line(sink, "You have retained all your items. You do not need to recover your corpse!");
        }

        if (here is { } place)
        {
            CreateCorpse(sink, new AcPosition(place.CellId, place.Origin, place.Orientation));
        }

        var jump = _sanctuary is { } bound ? new StandInTeleport(0, bound.Cell, bound.Origin) : new StandInTeleport(0, _visit.Cell, _visit.Origin);
        if (_sanctuary is null)
        {
            Placeholder("m3-standin-instantiation", "a player who never used a lifestone returns to ACE's Instantiation position (where the character was created): the stand-in's is the visit's start");
        }

        Teleport(sink, jump);
        ArriveAfterDeath(sink);
    }

    /// <summary>
    /// ACE's <c>CalculateDeathItems</c> (<c>Player_Death.cs</c> 347-540, <c>GetNumItemsDropped</c> 618-651) over the items the stand-in keeps: none at level 10 or below; one at most from 11 to 20; from 21 level over 20 plus a roll of 0 to 2 (14 at
    /// most); wielded items only from level 35. The order is a seeded shuffle (the stand-in has no values to rank by and no bonding: <c>TODO(m3-standin-death-items)</c>), a stack loses one unit, and the burden update and the pyreals are not sent
    /// (<c>TODO(m3-standin-death-items)</c>). Returns how many items left.
    /// </summary>
    private int LoseItems(IAcSessionSink sink, int level)
    {
        LastDrops.Clear();
        var wanted = level <= 10 ? 0 : level <= 20 ? _playerDeathRandom.Next(0, 2) : Math.Min((level / 20) + _playerDeathRandom.Next(0, 3), 14);
        if (wanted == 0 || (_pack.Count == 0 && _wielded.Count == 0))
        {
            return 0;
        }

        Placeholder("m3-standin-death-items", "ACE ranks the pack by value, halving each repeated item type, skips bonded items and halves the pyreals; the stand-in knows no values or bonding, so it takes its own items (wielded ones from level " + LevelDropsWielded
            + ") in a seeded order, leaves the pyreals alone and sends no burden update");
        var candidates = new List<(uint Guid, bool Wielded)>();
        for (var i = 0; i < _pack.Count; i++)
        {
            candidates.Add((_pack[i].Guid, false));
        }

        if (level >= LevelDropsWielded)
        {
            foreach (var guid in _wielded.Keys)
            {
                candidates.Add((guid, true));
            }
        }

        for (var i = candidates.Count - 1; i > 0; i--)
        {
            var j = _playerDeathRandom.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        var taken = Math.Min(wanted, candidates.Count);
        for (var i = 0; i < taken; i++)
        {
            LastDrops.Add(DropItem(sink, candidates[i].Guid, candidates[i].Wielded));
        }

        if (taken > 0)
        {
            Line(sink, DropMessage());
        }

        return taken;
    }

    private (string Name, int Count) DropItem(IAcSessionSink sink, uint guid, bool wielded)
    {
        if (wielded)
        {
            var entry = _wielded[guid];
            _wielded.Remove(guid);
            Zeros(sink, guid); // TryDequipObjectWithNetworking: the Wielder and CurrentWieldedLocation zeros (Player_Inventory.cs 400-405)
            PlaySound(sink, _reducer.State.Self.Guid, SoundUnwieldObject, 1f);
            DeleteObject(sink, guid); // ToCorpseOnDeath: the item is deleted from the client (the corpse holds it)
            return (entry.Name, 1);
        }

        var at = _pack.FindIndex(e => e.Guid == guid);
        var pack = _pack[at];
        if (pack.Stack > 1)
        {
            _pack[at] = pack with { Stack = pack.Stack - 1 };
            StackSize(sink, guid, pack.Stack - 1); // AdjustStack(-1) and GameMessageSetStackSize: one unit of a stack drops
            return (pack.Name, 1);
        }

        Instance(sink, guid, ContainerProperty, 0); // TryRemoveFromInventoryWithNetworking: the Container zero, then the removal (Player_Inventory.cs 217-226)
        AnswerLog.Add("0024");
        sink.OnInventoryRemoveObject(Context(sink), new InventoryRemoveObjectMsg(guid));
        _pack.RemoveAt(at);
        return (pack.Name, pack.Stack);
    }

    /// <summary>ACE's <c>DropMessage</c> (<c>Player_Death.cs</c> 688-731) for the items the last corpse took (the plural is the name and an "s": <c>TODO(m3-standin-weenies)</c>).</summary>
    private string DropMessage()
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < LastDrops.Count; i++)
        {
            text.Append(i == 0 ? "You've lost " : i == LastDrops.Count - 1 ? ", and " : ", ");
            var (name, count) = LastDrops[i];
            text.Append("your ").Append(count == 1 ? name : count.ToString("N0", CultureInfo.InvariantCulture) + " " + name + "s");
        }

        return text.Append('!').ToString();
    }

    /// <summary>The player's corpse (<c>Creature_Death.cs</c> <c>CreateCorpse</c> 428-600): "Corpse of &lt;name&gt;" with the corpse bit, the player's Setup and motion table, in the Dead pose, where the player fell.</summary>
    private void CreateCorpse(IAcSessionSink sink, in AcPosition place)
    {
        var self = _reducer.State.Self;
        Placeholder("m3-standin-corpse-weenie", "the corpse weenie's class id, look and physics are ACE's world database's; the stand-in's corpse wears the player's Setup, lies in the Dead pose and holds nothing the client can open");
        var guid = _nextCorpseGuid++;
        Corpses++;
        var dead = MotionCommandTable.ToWire(MotionCommands.NonCombat);
        var physics = new PhysicsDesc
        {
            Flags = PhysicsDescFlags.CSetup | PhysicsDescFlags.MTable | PhysicsDescFlags.Position | PhysicsDescFlags.Movement,
            State = PhysicsState.Gravity | PhysicsState.ReportCollisions | PhysicsState.EdgeSlide,
            Position = place,
            SetupId = StandInVisit.HumanSetup,
            MotionTableId = StandInVisit.HumanMotionTable,
            Movement = new ServerMotionState
            {
                MovementType = MovementType.Interpreted,
                Stance = dead,
                Style = dead,
                Forward = MotionCommandTable.ToWire(MotionCommands.Dead),
            },
            Timestamps = new PhysicsTimestamps(1, 1, 1, 1, 1, 1, 1, 1, 1),
        };
        var weenie = new WeenieDesc
        {
            Header = WeenieHeaderFlags.None,
            Name = "Corpse of " + (self.Name ?? "You"),
            WeenieClassId = CorpseWeenieClass,
            IconId = 0x06000001,
            ItemType = CorpseItemType,
            ObjectDescriptionFlags = WorldObjectTable.CorpseFlag,
        };
        AnswerLog.Add("F745");
        sink.OnCreateObject(Context(sink), new CreateObjectMsg(guid, ModelData.Empty, physics, weenie), isUpdate: false);
    }

    /// <summary>
    /// <c>ThreadSafeTeleportOnDeath</c>'s callback (<c>Player_Death.cs</c> 249-292): the stance back to NonCombat (the motion and the combat mode), the lifestone protection, and 3 s later the three vitals at 75% of their maximum (health, stamina, mana
    /// as 0x02E9 each).
    /// </summary>
    private void ArriveAfterDeath(IAcSessionSink sink)
    {
        SendPlayerMotion(sink, MotionCommands.NonCombat, MotionCommands.Ready); // SetCombatMode(NonCombat): HandleSwitchToPeaceMode
        AnswerLog.Add("02CD");
        PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, CombatModes.Property, CombatModes.NonCombat);
        SetProtection(sink);
        _reducer.StandIn!.At(_reducer.Clock.Now + RevivalSeconds, Revive);
    }

    private void Revive(IAcSessionSink sink)
    {
        var q = _reducer.State.Self.Qualities;
        SendVital(sink, Health, q, 1);
        SendVital(sink, Stamina, q, 3);
        SendVital(sink, Mana, q, 5);
        _dying = false; // IsInDeathProcess = false
    }

    private void SendVital(IAcSessionSink sink, uint vital, PlayerQualities q, uint maxId)
    {
        if (!q.TryGetVitalLevels(maxId, out _, out var max))
        {
            return;
        }

        AnswerLog.Add("02E9");
        PrivateUpdate(sink, PrivateUpdateKind.VitalCurrent, vital, (uint)Math.Round(max * RevivalShare, MidpointRounding.ToEven));
    }

    // --- the lifestone protection -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private void SetProtection(IAcSessionSink sink)
    {
        _protected = true;
        var serial = ++_protectionSerial;
        _reducer.StandIn!.At(_reducer.Clock.Now + ProtectionSeconds, later =>
        {
            if (_protected && serial == _protectionSerial)
            {
                _protected = false;
                MagicLine(later, "You're no longer protected by the Lifestone's magic!"); // LifestoneProtectionTick
            }
        });
    }

    /// <summary>A teleport dispels the protection (<c>Player_Location.cs</c> 702-703); called by <see cref="Teleport"/> before it sends.</summary>
    private void DispelProtectionOnTeleport(IAcSessionSink sink)
    {
        if (_protected)
        {
            _protected = false;
            MagicLine(sink, "Your actions have dispelled the Lifestone's magic!"); // LifestoneProtectionDispel
        }
    }

    // --- the suicide ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>A Suicide (0x0279): <c>HandleActionDie</c> and <c>HandleSuicide</c> (<c>Player_Death.cs</c> 299-345).</summary>
    internal OutboundTicket Suicide()
    {
        Later(HandleActionDie);
        return Ticket();
    }

    private void HandleActionDie(IAcSessionSink sink)
    {
        var self = _reducer.State.Self;
        var dead = _dying || (self.Qualities.TryGetVitalLevels(1, out var health, out _) && health == 0);
        if (dead)
        {
            AnswerLog.Add("028A");
            sink.OnWeenieError(Context(sink), new GameEventHeader(self.Guid, 0, 0x028A), new WeenieErrorEvent(YoureTooBusy, null));
            return;
        }

        if (_suicideInProgress)
        {
            return;
        }

        _suicideInProgress = true;
        var deaths = PlayerDeaths;
        HandleSuicide(sink, deaths, 0);
    }

    private void HandleSuicide(IAcSessionSink sink, int deaths, int step)
    {
        if (!_suicideInProgress || deaths != PlayerDeaths)
        {
            return;
        }

        if (step < SuicideLines.Length)
        {
            var self = _reducer.State.Self;
            AnswerLog.Add("02BB");
            sink.OnSpeech(Context(sink), new SpeechMsg(SuicideLines[step], self.Name ?? "You", self.Guid, 2, null));
            _reducer.StandIn!.At(_reducer.Clock.Now + SuicideStepSeconds, later => HandleSuicide(later, deaths, step + 1));
            return;
        }

        Die(sink, suicide: true);
    }

    // --- the lifestone ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    private bool IsLifestone(uint guid)
    {
        if (_placed.TryGetValue(guid, out var placed))
        {
            return (placed.DescriptionFlags & WorldObjectTable.LifestoneFlag) != 0;
        }

        var objects = _visit.Objects;
        for (var i = 0; i < objects.Length; i++)
        {
            if (objects[i].Guid == guid)
            {
                return (objects[i].DescriptionFlags & WorldObjectTable.LifestoneFlag) != 0;
            }
        }

        return false;
    }

    /// <summary>
    /// A Use of a lifestone (<c>Lifestone.cs</c> <c>ActOnUse</c> 45-75): a combat mode first goes back to NonCombat, the lifestone sound, the Sanctuary motion, then (the player still within reach) the sanctuary is the player's position, the object's
    /// UseMessage shows as a Magic line and the stamina halves; a player out of reach gets WeenieError YouHaveMovedTooFar. UseDone ends it.
    /// </summary>
    private void UseLifestone(IAcSessionSink sink, uint guid)
    {
        var self = _reducer.State.Self;
        Placeholder("m3-standin-lifestone-weenie", "the lifestone's UseMessage and the Sanctuary animation's length are ACE's world database's and motion table's: the stand-in shows a labelled placeholder line and waits " + SanctuarySeconds + " s");
        if (self.Qualities.CombatMode != CombatModes.NonCombat)
        {
            SendPlayerMotion(sink, MotionCommands.NonCombat, MotionCommands.Ready);
            AnswerLog.Add("02CD");
            PrivateUpdate(sink, PrivateUpdateKind.PropertyInt, CombatModes.Property, CombatModes.NonCombat);
        }

        PlaySound(sink, self.Guid, SoundLifestoneOn, 1f);
        var wire = MotionCommandTable.ToWire(MotionCommands.NonCombat);
        var state = new ServerMotionState
        {
            MovementType = MovementType.Interpreted,
            Stance = wire,
            Style = wire,
            Commands = [new MotionItem(MotionCommandTable.ToWire(SanctuaryMotion), 1, false, 1f)],
        };
        var stamps = _reducer.State.Objects.TryGet(self.Guid, out var body) ? body.Stamps : default;
        AnswerLog.Add("F74C");
        sink.OnUpdateMotion(Context(sink), new UpdateMotionMsg(self.Guid, stamps.Instance, unchecked((ushort)(stamps.Movement + 1)), stamps.ServerControl, false, state));
        _reducer.StandIn!.At(_reducer.Clock.Now + SanctuarySeconds, later => BindAtLifestone(later, guid));
    }

    private void BindAtLifestone(IAcSessionSink sink, uint guid)
    {
        var self = _reducer.State.Self;
        if (self.Position is { } here && _reducer.State.Objects.TryGet(guid, out var stone) && stone.Position is { } there && Distance(here.ToGlobal(), there.ToGlobal()) <= LifestoneUseRadiusMetres)
        {
            _sanctuary = (here.CellId, here.Origin);
            MagicLine(sink, PlaceholderUseMessage);
            if (self.Qualities.TryGetVitalLevels(3, out var stamina, out _))
            {
                UpdateVitalDelta(sink, Stamina, (int)Math.Round(stamina / 2f) - (int)stamina); // UpdateVital(Stamina, round(current / 2))
            }
        }
        else
        {
            AnswerLog.Add("028A");
            sink.OnWeenieError(Context(sink), new GameEventHeader(self.Guid, 0, 0x028A), new WeenieErrorEvent(YouHaveMovedTooFar, null));
        }

        UseDone(sink);
    }

    private static double Distance(GlobalPosition a, GlobalPosition b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    // --- the spell bars -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// An AddSpellFavorite (0x01E3): ACE adds the spell to the bar at the index (clamped to the bar's length; a spell already on the bar stays where it is) when the bar is 0 to 7 and the spell is known, and answers nothing
    /// (<c>Player_Character.cs</c> 298-305, <c>CharacterExtensions.AddSpellToBar</c> 533-573). The stand-in keeps the bars for its next description (<see cref="WithBars"/>).
    /// </summary>
    internal OutboundTicket AddSpellFavorite(uint spell, uint index, uint bar)
    {
        if (bar > 7 || !Knows(spell))
        {
            return Ticket();
        }

        if (_bars is null)
        {
            _bars = [];
            foreach (var existing in _reducer.State.Self.Spellbook.SpellBars)
            {
                _bars.Add(existing.IsDefault ? [] : [.. existing]);
            }
        }

        while (_bars.Count <= bar)
        {
            _bars.Add([]);
        }

        var list = _bars[(int)bar];
        if (!list.Contains(spell))
        {
            list.Insert((int)Math.Min(index, (uint)list.Count), spell);
        }

        return Ticket();
    }

    /// <summary><paramref name="description"/> with the bars the stand-in holds since an AddSpellFavorite; unchanged when none was added.</summary>
    internal PlayerDescriptionMsg WithBars(PlayerDescriptionMsg description)
    {
        if (_bars is null)
        {
            return description;
        }

        var bars = ImmutableArray.CreateBuilder<ImmutableArray<uint>>(_bars.Count);
        foreach (var list in _bars)
        {
            bars.Add([.. list]);
        }

        return description with { SpellBars = bars.MoveToImmutable() };
    }
}
