using System.Globalization;
using System.Security.Cryptography;
using Lifestone.Client.Diagnostics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Credentials;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Data.Magic;
using Lifestone.Loopback.Tests.Net;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M3 2.1 s1, the real expiry of a buff against the bundled ACE (decision D26, the owner's). WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD, SOAK TIER (about 31 minutes of wall time: the shortest buff lasts 1,800 s). On
/// <c>BSD Mage</c> only (the test account; it must exist: run the mage build first) the headless runner enters the mage, has the gear on, enters the magic combat mode, casts the cheapest profile self spell of the shortest duration
/// (1,800 s) the mage knows, and then waits, logged in and in the world, at most the spell's duration plus 120 s (one bounded wait, no retry) for ACE to take it away.
/// </summary>
/// <remarks>
/// <para><b>ACE's expiry path</b> (<c>vendor-eval/ACE/Source/ACE.Server/WorldObjects/Managers/EnchantmentManager.cs</c>, read before this test was written). The player's heartbeat (about every 5 s) calls <c>HeartBeat</c> (1220-1229),
/// which calls <c>HeartBeatEnchantmentsAndReturnExpired</c> (<c>PropertiesEnchantmentRegistryExtensions.cs</c> 239-264): it subtracts the interval from the <i>StartTime of every entry in the registry</i>, the weaker layers a
/// stronger spell of the category surpasses included, and returns each whose <c>StartTime</c> has reached <c>-Duration</c>; <c>Remove</c> (312-352) then deletes the entry and sends the owner <b>GameEvent 0x02C3
/// MagicRemoveEnchantment</b> {spell, layer} and <b>GameMessageSound F750 {player, Sound.SpellExpire 0x96, 1.0}</b> (<c>sound: true</c> by default; not for a cooldown). So an expiry is reported on this server for every layer, a surpassed one too,
/// which is why the cheapest spell is as good as any: whether a stronger spell of its category is active (the mage was buffed by the buff loop) or not, its own layer expires and is announced. The expiry is about the duration after the
/// cast, to within the heartbeat (an interval that runs slightly long lets it last a little longer: hence the 120 s).</para>
/// <para><b>What it proves.</b> (a) The cast lands: the outcome is Confirmed (GameEvent 0x02C2) and the registry holds the spell, layer and duration 1,800 s, as <see cref="PlayerQualities.ActiveEnchantments"/>. (b) After the duration
/// the registry no longer holds that (spell, layer): the reducer applied 0x02C3 (<see cref="EnchantmentChanged"/> with <see cref="EnchantmentChange.Removed"/> naming the spell and layer). (c) An F750 played for the player right
/// after it: the counter is the number of <see cref="ObjectSoundPlayed"/> events for the player's guid with sound 0x96 (<c>Sound.SpellExpire</c>, ACE <c>Sound.cs</c> 155) that arrived after that removal; at least 1 (an
/// expiry of another buff in the same window would add to <c>f750</c> only after the removal of this one was seen, so the removal by id is the assertion and the sound follows it in the same bundle). (d) The removal came within
/// [duration - 30 s, duration + 120 s] of the cast on the session clock. The leg prints <c>expiry_live spell=&lt;id&gt; duration_s=&lt;n&gt; removed_after_s=&lt;n&gt; f750=&lt;n&gt;</c> and, apart, the overdue count
/// (<c>TODO(m3-enchant-overdue)</c>: entries the registry holds past their end).</para>
/// <para>The mage ends in non-combat. Since M4 P4 the run is over the mage's shared session (one login per test process; the mage is put in peace mode first) and does not log off at the end; on another character it fails at once and sends nothing. The cast burns a component or two and the mana its cost. Skipped, with the probe's reason, unless the backend runs with the Lifestone server mod ready.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Trait("Tier", "Soak")]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
[LiveSharedSession]
public sealed class EnchantmentExpiryLiveTests(ITestOutputHelper output)
{
    /// <summary>The headless limit of the run, in seconds: the gear, the 1,800 s buff, 120 s of allowance and the log off.</summary>
    private const double QuitAfterSeconds = 3000;

    [LoopbackFact]
    public void The_shortest_buff_expires_and_leaves_the_registry_with_its_0x02C3_and_an_F750()
    {
        using var dats = DatSet.Open();
        var spells = SpellTableData.Load(dats).Spells;
        var log = new RecordingLogSink();
        using var leg = new ExpiryLeg(GearKit.Build(spells, LiveMage.KitAccount()), spells, log, output.WriteLine);
        var result = LiveMage.Run(output, leg, "expiry", QuitAfterSeconds).Result;

        foreach (var line in log.Lines.Where(l => l.Contains("[gear]", StringComparison.Ordinal) || l.Contains("WARN", StringComparison.Ordinal) || l.Contains("ERROR", StringComparison.Ordinal)))
        {
            output.WriteLine(line);
        }

        if (leg.Summary is { } summary)
        {
            output.WriteLine(summary);
        }

        Assert.True(result.Ok, result.Failure);
        Assert.Equal(0, result.Errors);
        Assert.Null(leg.Failure);
        Assert.Equal("done", leg.Stage);
        Assert.NotNull(leg.Summary);
    }

    /// <summary>The leg: gear, magic mode, the cast, one bounded wait for the expiry, the assertions, non-combat, log off.</summary>
    internal sealed class ExpiryLeg : IScenario, IDisposable
    {
        /// <summary>ACE's <c>Sound.SpellExpire</c> (<c>ACE.Entity/Enum/Sound.cs</c> 155).</summary>
        private const uint SpellExpireSound = 0x96;

        /// <summary>The wait after the cast beyond the spell's duration, in seconds (the heartbeat's slack and the network).</summary>
        private const double AllowanceSeconds = 120;

        /// <summary>How long the leg waits for the F750 that follows the removal in the same bundle, in seconds.</summary>
        private const double SoundSettleSeconds = 10;

        private const double ManaWaitSeconds = 180;
        private const double BailSeconds = 30;

        private readonly GearScenario _gear;
        private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
        private readonly Action<string> _write;
        private readonly List<IDisposable> _subscriptions = [];
        private ActionHandle? _pending;
        private SpellInfo? _info;
        private IGameSessionView? _session;
        private double _stageStart;
        private double _castAt = double.NaN;
        private double _removedAt = double.NaN;
        private int _layer = -1;
        private double _duration;
        private int _removals;
        private int _f750Total;
        private int _f750After;
        private long _chatMark;

        public ExpiryLeg(GearKit kit, IReadOnlyDictionary<uint, SpellInfo> spells, ILogSink log, Action<string> write)
        {
            _gear = new GearScenario(kit, log, endSession: false, cast: false, spells: spells) { RequireCharacter = true };
            _spells = spells;
            _write = write;
        }

        public string Name => "expiry-leg";

        public string Stage { get; private set; } = "start";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        /// <summary>The <c>expiry_live ...</c> line, once every assertion of the leg held.</summary>
        public string? Summary { get; private set; }

        public void Dispose()
        {
            foreach (var s in _subscriptions)
            {
                s.Dispose();
            }

            _subscriptions.Clear();
        }

        /// <summary>The profile's self spells of the shortest duration, cheapest first (<see cref="GearCasts.ManaCeiling"/> with no worn items), lowest level first, profile order after that.</summary>
        internal static IReadOnlyList<SpellInfo> Candidates(IReadOnlyDictionary<uint, SpellInfo> spells)
        {
            var found = new List<(SpellInfo Info, uint Mana, int Position, int Order)>();
            var order = 0;
            foreach (var line in ArenaProfile.Lines)
            {
                order++;
                if (!line.IsBuff || line.Role is not (ProfileRole.CreatureBuff or ProfileRole.LifeBuff))
                {
                    continue;
                }

                foreach (var spell in line.Spells)
                {
                    if (spells.TryGetValue(spell.Id, out var info) && GearCasts.FormOf(info) == CastForm.Self && info.Duration > 0)
                    {
                        found.Add((info, GearCasts.ManaCeiling(info, 0), spell.Position, order));
                    }
                }
            }

            var shortest = found.Min(f => f.Info.Duration);
            return [.. found.Where(f => f.Info.Duration == shortest).OrderBy(f => f.Mana).ThenBy(f => f.Position).ThenBy(f => f.Order).Select(f => f.Info)];
        }

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            ArgumentNullException.ThrowIfNull(session);
            _session = session;
            var clock = session.Clock.Now;
            if (Stage == "start" && GearScenario.WrongCharacter(session) is { } wrong)
            {
                // Nothing was sent (no gear, no log off: the shared session is the host's).
                Failure = wrong;
                _write("FAILED: " + wrong);
                Stage = "failed";
                return ScenarioStatus.Failed;
            }

            switch (Stage)
            {
                case "done":
                    return ScenarioStatus.Succeeded;
                case "failed":
                    return ScenarioStatus.Failed;
                case "bailing":
                    if (_pending is { IsCompleted: true } || session.State.Phase != SessionPhase.InWorld || clock - _stageStart > BailSeconds)
                    {
                        Stage = "failed";
                        return ScenarioStatus.Failed;
                    }

                    return ScenarioStatus.Running;
            }

            try
            {
                return Advance(session, clock, now);
            }
            catch (InvalidOperationException e)
            {
                Failure ??= Stage + ": " + e.Message;
                _write("FAILED: " + Failure);
                if (session.State.Phase == SessionPhase.InWorld)
                {
                    _stageStart = clock;
                    _pending = session.Actions.LogOff(ActionSource.Host);
                    Stage = "bailing";
                    return ScenarioStatus.Running;
                }

                Stage = "failed";
                return ScenarioStatus.Failed;
            }
        }

        private static void Require(bool ok, string what)
        {
            if (!ok)
            {
                throw new InvalidOperationException(what);
            }
        }

        private ScenarioStatus Advance(IGameSessionView session, double clock, double now)
        {
            var q = session.State.Self.Qualities;
            switch (Stage)
            {
                case "start":
                    Stage = "gear";
                    break;

                case "gear":
                    var gear = _gear.Step(session, now);
                    Require(gear != ScenarioStatus.Failed, "the gear scenario failed: " + _gear.Failure);
                    if (gear == ScenarioStatus.Succeeded)
                    {
                        var book = session.State.Self.Spellbook;
                        _info = Candidates(_spells).FirstOrDefault(c => book.Knows(c.Id));
                        Require(_info is not null, "the mage knows none of the profile's 1,800 s self spells (run the mage build first)");
                        _duration = _info!.Duration;
                        _write($"expiry spell: {_info.Name} ({_info.Id}), duration {_duration:0} s, up to {GearCasts.ManaCeiling(_info, session.State.Self.Inventory.Equipped.Count)} mana");
                        Subscribe(session);
                        _pending = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
                        Stage = "mode";
                    }

                    break;

                case "mode":
                    if (_pending is { Outcome: { } mode })
                    {
                        Require(mode.Succeeded, "the magic combat mode was not entered: " + mode.Kind + (mode.Detail is null ? string.Empty : ": " + mode.Detail));
                        _pending = null;
                        _stageStart = clock;
                        Stage = "mana";
                    }

                    break;

                case "mana":
                    var need = GearCasts.ManaCeiling(_info!, session.State.Self.Inventory.Equipped.Count);
                    Require(q.TryGetVitalLevels(MageTemplate.MaxMana, out var mana, out var max), "the read model has no mana vital");
                    Require(max >= need, $"{_info!.Name} costs up to {need} mana and the maximum is {max}");
                    if (mana >= need)
                    {
                        _chatMark = session.State.Chat.Total;
                        _pending = session.Actions.Cast(_info!.Id, ActionSource.Host);
                        Stage = "casting";
                    }
                    else
                    {
                        Require(clock - _stageStart <= ManaWaitSeconds, $"the mana did not reach {need} within {ManaWaitSeconds:0} s: {mana} of {max}"); // a named precondition wait: nothing was cast, so no retry
                    }

                    break;

                case "casting":
                    if (_pending is { Outcome: { } cast })
                    {
                        Require(cast.Kind == ActionOutcomeKind.Confirmed, "the cast of " + _info!.Name + " was not confirmed: " + GearCasts.Explain(cast, session.State, _chatMark, 0));
                        _pending = null;
                        ActiveEnchantment? held = null;
                        foreach (var e in q.ActiveEnchantments)
                        {
                            if (e.SpellId == _info!.Id && (held is null || e.ReceivedSeconds >= held.Value.ReceivedSeconds))
                            {
                                held = e;
                            }
                        }

                        Require(held is not null, "the registry holds no enchantment of " + _info!.Name + " after a confirmed cast");
                        Require(Math.Abs(held!.Value.Enchantment.Duration - _duration) < 0.5, string.Create(CultureInfo.InvariantCulture, $"the enchantment lasts {held.Value.Enchantment.Duration} s, the SpellTable says {_duration} s"));
                        _layer = held.Value.Layer;
                        _castAt = clock;
                        _f750After = 0;
                        _write(string.Create(CultureInfo.InvariantCulture, $"cast: {_info.Name} ({_info.Id}) in the registry at layer {_layer}, duration {_duration:0} s; waiting up to {_duration + AllowanceSeconds:0} s of session time for its expiry"));
                        Stage = "waiting";
                    }

                    break;

                case "waiting":
                    if (!double.IsNaN(_removedAt))
                    {
                        _stageStart = clock;
                        Stage = "settling";
                        break;
                    }

                    Require(clock - _castAt <= _duration + AllowanceSeconds, string.Create(CultureInfo.InvariantCulture, $"{_info!.Name} ({_info.Id}, layer {_layer}) did not expire within {_duration + AllowanceSeconds:0} s of the cast (the registry {(Holds() ? "still holds it" : "no longer holds it")}, {_removals} removals seen)"));
                    break;

                case "settling":
                    if (_f750After >= 1 || clock - _stageStart > SoundSettleSeconds)
                    {
                        var removedAfter = _removedAt - _castAt;
                        Require(!Holds(), $"the registry still holds {_info!.Name} (layer {_layer}) after its 0x02C3");
                        Require(removedAfter >= _duration - 30 && removedAfter <= _duration + AllowanceSeconds, string.Create(CultureInfo.InvariantCulture, $"the removal came {removedAfter:0.#} s after the cast, outside {_duration - 30:0} to {_duration + AllowanceSeconds:0} s"));
                        Require(_f750After >= 1, $"no F750 (ObjectSoundPlayed for the player, sound 0x{SpellExpireSound:X}) followed the removal within {SoundSettleSeconds:0} s ({_f750Total} in all)");
                        Summary = string.Create(CultureInfo.InvariantCulture, $"expiry_live spell={_info!.Id} duration_s={_duration:0} removed_after_s={removedAfter:0} f750={_f750After}");
                        _write(Summary);
                        _write($"overdue entries in the registry (TODO(m3-enchant-overdue)): {q.CountOverdueEnchantments(clock)}; F750 for the player since the cast: {_f750Total}; 0x02C3 removals of any spell since the cast: {_removals}");
                        _pending = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
                        Stage = "peace";
                    }

                    break;

                case "peace":
                    if (_pending is { Outcome: { } peace })
                    {
                        if (!peace.Succeeded)
                        {
                            _write("WARN: the mage was left in the magic combat mode: " + peace.Kind);
                        }

                        if (LiveRun.KeepSession)
                        {
                            _write("the shared session stays logged in (no log off at the end of the leg)");
                            Stage = "done";
                            return ScenarioStatus.Succeeded;
                        }

                        _pending = session.Actions.LogOff(ActionSource.Host);
                        Stage = "logging-off";
                    }

                    break;

                case "logging-off":
                    if (_pending is { Outcome: { } off })
                    {
                        Require(off.Kind == ActionOutcomeKind.Confirmed, "logging off: " + off.Kind);
                        _pending = session.Actions.Quit(ActionSource.Host);
                        Stage = "quitting";
                    }

                    break;

                case "quitting":
                    if (_pending is { Outcome: { } quit })
                    {
                        Require(quit.Kind == ActionOutcomeKind.Sent, "quitting: " + quit.Kind);
                        Stage = "done";
                        return ScenarioStatus.Succeeded;
                    }

                    break;

                default:
                    throw new InvalidOperationException("unknown stage " + Stage);
            }

            return ScenarioStatus.Running;
        }

        private bool Holds()
        {
            foreach (var e in _session!.State.Self.Qualities.ActiveEnchantments)
            {
                if (e.SpellId == _info!.Id && e.Layer == _layer)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The two counters: the reducer's <see cref="EnchantmentChanged"/> (Removed, the 0x02C3) and <see cref="ObjectSoundPlayed"/> (the F750).</summary>
        private void Subscribe(IGameSessionView session)
        {
            var player = session.State.Self.Guid;
            _subscriptions.Add(session.Events.Subscribe((in EnchantmentChanged e) =>
            {
                if (e.Change != EnchantmentChange.Removed || double.IsNaN(_castAt))
                {
                    return;
                }

                _removals++;
                foreach (var spell in e.Spells)
                {
                    if (_info is not null && spell.SpellId == _info.Id && spell.Layer == _layer && double.IsNaN(_removedAt))
                    {
                        _removedAt = session.Clock.Now;
                    }
                }
            }));
            _subscriptions.Add(session.Events.Subscribe((in ObjectSoundPlayed e) =>
            {
                if (e.Guid == player && e.SoundId == SpellExpireSound && !double.IsNaN(_castAt))
                {
                    _f750Total++;
                    if (!double.IsNaN(_removedAt))
                    {
                        _f750After++;
                    }
                }
            }));
        }
    }
}
