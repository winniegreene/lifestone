using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M3 1.4 s2, the first casts against the bundled ACE (reworked in fix B after the lead's first live run). WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD. On <c>BSD Mage</c> only (the test account; it must exist: run
/// the mage build first) it runs the gear scenario first (idempotent: the wand wielded, the kit held; its own cast switched off so this test counts every cast), waits for the mana the casts need, enters the magic combat
/// mode and casts through <see cref="IActionApi"/>.
/// </summary>
/// <remarks>
/// <para><b>What it proves</b> (each step names the ACE source it relies on, <c>vendor-eval/ACE/Source/ACE.Server/WorldObjects/</c>).
/// (a) A Creature Enchantment self buff and a Life Magic self buff, each the highest level of the first <see cref="ArenaProfile"/> line whose cast chance from the read model's current skill is at least
/// <see cref="GearCasts.MinChance"/> (ACE's <c>GetCastingPreCheckStatus</c>, <c>Player_Magic.cs</c> 528-545, <c>SkillCheck.GetMagicSkillChance</c>, <c>SkillCheck.cs</c> 7-22; no retry: a check never retries silently),
/// cast with <see cref="IActionApi.Cast(uint, ActionSource)"/>: the outcome is Confirmed (the enchantment add, GameEvent 0x02C2, <c>WorldObject_Magic.cs</c> 448-452) and
/// <see cref="PlayerQualities.Enchantments"/> holds the spell with the SpellTable's duration. (b) An Item Enchantment spell of the wand lines (Hermetic Link or Defender, same chance rule), cast in the form its SpellTable target type
/// gives (<see cref="GearCasts.FormOf"/>): the end-of-retail Auras of these lines are self-targeted (ACE refuses them at the wand, <c>Player.IsInvalidTarget</c>, <c>Player_Magic.cs</c> 435-437; the first live run showed it: the server ended
/// the cast at the wand without the enchantment) and land as the player's enchantment, so they are cast as a self cast and asserted by the registry like (a); a spell that targets an item (<c>VerifyNonComponentTargetType</c>,
/// <c>Player_Magic.cs</c> 1379-1425) is cast at the wielded wand with <see cref="IActionApi.Cast(uint, uint, ActionSource)"/> and proved by the UseDone 0 (<see cref="ActionOutcomeKind.Sent"/>) together with ACE's chat line "You cast
/// &lt;spell&gt; on &lt;item&gt;" (<c>WorldObject_Magic.cs</c> 430-444). (c) A certain fizzle: the cheapest <i>known</i> (the read model's spellbook) profile spell of the creature or life lines whose pinned <c>Power</c> is above the
/// mage's current skill in its school by more than 50 (<see cref="GearCasts.PickFizzle"/>; the margin is asserted first from the read model, naming the numbers if none qualifies): ACE fails the cast outright
/// (<c>Player_Magic.cs</c> 534-535) and the outcome is a ServerError with WeenieError 0x0402. (d) Mana falls: the read model's current Mana after the successful casts is below the value before them. (e) The component burn
/// (<c>Player_Magic.cs</c> 1183-1215: 0x0197 for a partial stack, 0x0024 then F747 for the last one): at least one kit component's stack in <see cref="PlayerInventory"/> fell, <b>asserted only when the chance that nothing burns
/// is below 1e-3</b> (<see cref="GearCasts.NoBurnProbability"/>, ACE's <c>Spell.TryBurnComponents</c>, <c>Entity/Spell.cs</c> 157-189: each formula slot burns with <c>ComponentLoss x CDM x min(1, Power / skill)</c>); otherwise it is
/// report-only and the line says so (the test never casts again until something burns). (f) The top-up after the burn (fix A: <c>SpawnGear</c> merges each top-up into the held stack): the gear scenario runs again with its cast off; the run must not fail (a merge failure fails the spawn), the main pack's slot
/// count must equal the count before the casts, each component weenie must be one stack, and a rerun after that must send nothing (<c>topup=0</c>); <c>merges</c> is the number of component lines that run topped up (one merge
/// each). The mage is put back in Non-combat before it. Since M4 P4 the run is over the mage's shared session (one login per test process; the mage is put in peace mode first) and does not log off at the end; on another character it fails at once and sends nothing.</para>
/// <para><b>The mana precondition</b> (fix B). Before the casts the test adds up the most they can cost (<see cref="GearCasts.ManaCeiling"/>: the SpellTable's base mana, which ACE's Mana Conversion only lowers,
/// <c>Creature_Magic.cs</c> 14-92; plus <see cref="GearCasts.FizzleMana"/> for the fizzle, <c>Player_Magic.cs</c> 566-572) and compares it with the read model's current mana; when short it waits for regeneration, at most
/// <see cref="CastLeg.ManaWaitSeconds"/> s, logging both numbers (a named precondition wait: nothing has been cast, so it is no retry) and fails with the numbers if the mana never comes. A cast that ends with UseDone 0x0401 is named
/// as not enough mana. Every cast failure carries what the server said: the UseDone or GameEvent 0x028A code and the chat lines that arrived during the cast (<see cref="GearCasts.Explain"/>).</para>
/// <para>It prints one summary line: <c>cast_live creature=&lt;spell id&gt;:&lt;outcome&gt; life=... item=&lt;id&gt;:&lt;outcome&gt;:&lt;form&gt; fizzle=&lt;id&gt;:0x0402 mana_needed=&lt;n&gt; mana_before=&lt;n&gt; mana_after=&lt;n&gt;
/// p_no_burn=&lt;p&gt; burned=&lt;n&gt; burn_check=asserted|report-only merges=&lt;n&gt; slots=&lt;before&gt;-&gt;&lt;after&gt;</c> (n: component units burned). Casts burn components and mana every run and the mage's skills may gain proficiency experience (ACE's <c>Proficiency.OnSuccessUse</c>, ranks
/// and level only ever rise: the mage-build checks read ranks and level with at-least comparisons). The account name the kit is built for is read from the secrets file at run time and is in no output.</para>
/// <para>Skipped, with the probe's reason, unless the backend runs with the Lifestone server mod ready.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
[LiveSharedSession]
public sealed class CastLiveTests(ITestOutputHelper output)
{
    [LoopbackFact]
    public void The_first_casts_buff_the_mage_fizzle_by_the_skill_margin_spend_mana_and_burn_components()
    {
        using var dats = DatSet.Open();
        var spells = SpellTableData.Load(dats).Spells;
        var log = new RecordingLogSink();
        var leg = new CastLeg(GearKit.Build(spells, LiveMage.KitAccount()), spells, LiveMage.KitAccount(), log, output.WriteLine);
        var result = LiveMage.Run(output, leg, "cast", CastLeg.ManaWaitSeconds + 600).Result;

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
        Assert.Equal("done", leg.Stage);
        Assert.Null(leg.Failure);
        Assert.NotNull(leg.Summary);
    }

    /// <summary>The whole leg in one session, written as a script that yields a handle to wait for it (or null to wait a frame).</summary>
    internal sealed class CastLeg : IScenario
    {
        private const double BailSeconds = 30;

        /// <summary>
        /// The most the leg waits for the mana the casts need to regenerate (a named precondition wait, not a retry). 480 s, not 180: the shared mage's earlier tests leave it low, and the mage regenerates about 0.43 mana a second (the
        /// proof run: 122 to 199 of the 200 needed in 180 s, under vitae), so the worst case, from empty to the 200, takes 467 s. The 200 the casts need is not lowered.
        /// </summary>
        internal const double ManaWaitSeconds = 480;

        private readonly GearKit _kit;
        private readonly IReadOnlyDictionary<uint, SpellInfo> _spells;
        private readonly string _account;
        private readonly ILogSink _gearLog;
        private readonly Action<string> _log;
        private readonly IEnumerator<ActionHandle?> _script;
        private readonly List<(SpellInfo Info, uint Skill)> _made = [];
        private ActionHandle? _wait;
        private double _since;
        private IGameSessionView? _session;
        private uint _manaNeeded;

        public CastLeg(GearKit kit, IReadOnlyDictionary<uint, SpellInfo> spells, string account, ILogSink gearLog, Action<string> log)
        {
            _kit = kit;
            _spells = spells;
            _account = account;
            _gearLog = gearLog;
            _log = log;
            _script = Script().GetEnumerator();
        }

        public string Name => "cast-leg";

        public string Stage { get; private set; } = "start";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        /// <summary>The <c>cast_live ...</c> summary line, once every assertion of the leg held.</summary>
        public string? Summary { get; private set; }

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            ArgumentNullException.ThrowIfNull(session);
            _session = session;
            if (Stage == "start" && GearScenario.WrongCharacter(session) is { } wrong)
            {
                // Nothing was sent (no gear, no log off: the shared session is the host's).
                Failure = wrong;
                _log("FAILED: " + wrong);
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
                    if (_wait is { IsCompleted: true } || session.State.Phase != SessionPhase.InWorld || session.Clock.Now - _since > BailSeconds)
                    {
                        Stage = "failed";
                        return ScenarioStatus.Failed;
                    }

                    return ScenarioStatus.Running;
            }

            if (_wait is { IsCompleted: false })
            {
                return ScenarioStatus.Running;
            }

            try
            {
                if (_script.MoveNext())
                {
                    _wait = _script.Current;
                    return ScenarioStatus.Running;
                }

                Stage = "done";
                return ScenarioStatus.Succeeded;
            }
            catch (InvalidOperationException e)
            {
                Failure ??= Stage + ": " + e.Message;
                _log("FAILED: " + Failure);
                if (session.State.Phase == SessionPhase.InWorld)
                {
                    _since = session.Clock.Now;
                    _wait = session.Actions.LogOff(ActionSource.Host);
                    Stage = "bailing";
                    return ScenarioStatus.Running;
                }

                Stage = "failed";
                return ScenarioStatus.Failed;
            }
        }

        /// <summary>Whether the registry holds a spell of one of the profile's magic-skill mastery lines (War, Creature, Item, Life Magic Mastery Self): a buffed mage.</summary>
        private static bool HoldsMasteryBuff(PlayerQualities q)
        {
            foreach (var line in ArenaProfile.Lines)
            {
                if (line.Role != ProfileRole.CreatureBuff || !line.Name.EndsWith("Mastery Self", StringComparison.Ordinal) || line.Name.StartsWith("Mana", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var spell in line.Spells)
                {
                    foreach (var e in q.Enchantments)
                    {
                        if (e.SpellId == spell.Id)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static void Require(bool ok, string what)
        {
            if (!ok)
            {
                throw new InvalidOperationException(what);
            }
        }

        private static string Text(ActionHandle handle) =>
            handle.Outcome is { } o ? o.Kind + (o.Code == 0 ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" code 0x{o.Code:X4}")) + (o.Detail is { } d ? ": " + d : string.Empty) : "(no outcome)";

        /// <summary>What the server said during a cast that did not do its work: the outcome's code, named, and the chat lines since <paramref name="chatMark"/> (<see cref="GearCasts.Explain"/>).</summary>
        private string Said(ActionHandle handle, long chatMark) =>
            handle.Outcome is { } o ? GearCasts.Explain(o, _session!.State, chatMark, _manaNeeded) : "(no outcome)";

        private static void Confirmed(ActionHandle handle, string what) =>
            Require(handle.Outcome is { Kind: ActionOutcomeKind.Confirmed }, what + ": " + Text(handle));

        private static (uint Current, uint Max) Mana(IGameSessionView s) =>
            s.State.Self.Qualities.TryGetVitalLevels(MageTemplate.MaxMana, out var current, out var max) ? (current, max) : throw new InvalidOperationException("the read model has no mana vital");

        /// <summary>The units burned: for each kit component, how far its held count fell (a component that rose, or a top-up, counts 0), and which.</summary>
        private (int Burned, string Which) Burned(Dictionary<uint, int> before, Dictionary<uint, int> after)
        {
            var burned = 0;
            var which = new StringBuilder();
            foreach (var item in _kit.Components)
            {
                var drop = before.GetValueOrDefault(item.Wcid) - after.GetValueOrDefault(item.Wcid);
                if (drop > 0)
                {
                    burned += drop;
                    which.Append(CultureInfo.InvariantCulture, $"{item.Name} (weenie {item.Wcid}) -{drop}; ");
                }
            }

            return (burned, which.Length == 0 ? "(none)" : which.ToString());
        }

        private SpellInfo InfoOf(string label, uint spellId) =>
            _spells.TryGetValue(spellId, out var info) ? info : throw new InvalidOperationException($"{label}: spell {spellId} is not in the SpellTable");

        /// <summary>
        /// Casts <paramref name="pick"/> in the form its SpellTable target type gives and asserts it: a self cast is Confirmed by the registry (the enchantment, with the SpellTable's duration); a cast at the wand is proved by the
        /// UseDone 0 and ACE's chat line. Every refusal names what the server said. The outcome kind and form go to <paramref name="done"/>.
        /// </summary>
        private IEnumerable<ActionHandle?> CastAndAssert(string label, CastPick pick, SpellInfo info, Action<string> done)
        {
            var s = _session!;
            var form = GearCasts.FormOf(info);
            var q = s.State.Self.Qualities;
            _log($"{label}: {pick.Describe()}, cast as a {(form == CastForm.Self ? "self cast (the spell targets the caster)" : "cast at the wand (the spell targets an item)")}, NonComponentTargetType 0x{info.NonComponentTargetType:X}, base mana {info.BaseMana}");
            Stage = label;
            var chatMark = s.State.Chat.Total;
            ActionHandle cast;
            string wandName = string.Empty;
            if (form == CastForm.Self)
            {
                cast = s.Actions.Cast(pick.Spell.Id, ActionSource.Host);
            }
            else
            {
                uint wand = 0;
                foreach (var equipped in s.State.Self.Inventory.Equipped)
                {
                    if (s.State.Objects.TryGet(equipped.Guid, out var o) && o.Weenie.WeenieClassId == GearKit.Wand.Wcid)
                    {
                        wand = equipped.Guid;
                        break;
                    }
                }

                Require(wand != 0, $"{label}: the wand is not among the equipped items");
                wandName = s.State.Objects.TryGet(wand, out var wandObject) ? wandObject.Name : GearKit.Wand.Name;
                cast = s.Actions.Cast(pick.Spell.Id, wand, ActionSource.Host);
            }

            yield return cast;
            _made.Add((info, pick.Skill));
            if (form == CastForm.Self)
            {
                Require(cast.Outcome is { Kind: ActionOutcomeKind.Confirmed }, $"{label} cast of {pick.Spell.Name} ({pick.Spell.Id}): {Said(cast, chatMark)}");
                var entry = q.Enchantments.Where(e => e.SpellId == pick.Spell.Id).Select(e => (EnchantmentDesc?)e).FirstOrDefault();
                Require(entry is not null, $"{label}: the registry holds no enchantment of {pick.Spell.Name} ({pick.Spell.Id}) after a confirmed cast");
                Require(
                    Math.Abs(entry!.Value.Duration - pick.Spell.DurationSeconds) < 0.5,
                    string.Create(CultureInfo.InvariantCulture, $"{label}: the enchantment lasts {entry.Value.Duration} s, the SpellTable's duration for {pick.Spell.Name} is {pick.Spell.DurationSeconds} s"));
                _log(string.Create(CultureInfo.InvariantCulture, $"{label}: {pick.Spell.Name} in the registry, layer {entry.Value.Layer}, duration {entry.Value.Duration} s: {Text(cast)}"));
                done($"{pick.Spell.Id}:{cast.Outcome!.Value.Kind}:self");
                yield break;
            }

            // A spell on an item: ACE sends no 0x02C2 to the player, so the UseDone 0 and the chat line are the proof that exists on this server.
            Require(cast.Outcome is { Kind: ActionOutcomeKind.Sent, Code: 0 }, $"{label} cast of {pick.Spell.Name} ({pick.Spell.Id}) at the wand: {Said(cast, chatMark)}");
            var expectedLine = $"You cast {pick.Spell.Name} on {wandName}";
            var fresh = (int)Math.Min(s.State.Chat.Count, s.State.Chat.Total - chatMark);
            var seen = false;
            var tail = new StringBuilder();
            for (var i = s.State.Chat.Count - fresh; i < s.State.Chat.Count; i++)
            {
                var text = s.State.Chat[i].Text;
                tail.Append(CultureInfo.InvariantCulture, $"[{text}] ");
                seen |= text.StartsWith(expectedLine, StringComparison.Ordinal);
            }

            Require(seen, $"{label}: ACE's chat line '{expectedLine}' did not arrive; {Said(cast, chatMark)}; the {fresh} new chat lines: {tail}");
            _log($"{label}: {expectedLine} (chat) and {Text(cast)}");
            done($"{pick.Spell.Id}:{cast.Outcome!.Value.Kind}:item");
        }

        /// <summary>The gear scenario again over the same session (its cast off), stepped to its end; <paramref name="done"/> gets it.</summary>
        private IEnumerable<ActionHandle?> RerunGear(string label, Action<GearScenario> done)
        {
            var s = _session!;
            var gear = new GearScenario(_kit, _gearLog, endSession: false, cast: false) { RequireCharacter = true };
            done(gear);
            while (true)
            {
                var status = gear.Step(s, s.Clock.Now);
                if (status == ScenarioStatus.Succeeded)
                {
                    break;
                }

                Require(status != ScenarioStatus.Failed, $"{label}: the gear scenario failed: {gear.Failure}");
                yield return null;
            }
        }

        private IEnumerable<ActionHandle?> Script()
        {
            var s = _session ?? throw new InvalidOperationException("no session");

            // The gear first (login, BSD Mage, the kit, the wand wielded): ACE holds the magic mode only with a wielded caster (Player_Combat.cs 872-880). Its own cast is off.
            Stage = "gear";
            var gear = new GearScenario(_kit, _gearLog, endSession: false, cast: false) { RequireCharacter = true };
            while (true)
            {
                var status = gear.Step(s, s.Clock.Now);
                if (status == ScenarioStatus.Succeeded)
                {
                    break;
                }

                Require(status != ScenarioStatus.Failed, "the gear scenario failed: " + gear.Failure);
                yield return null;
            }

            _log("gear: " + gear.VerdictFields());
            Require(gear.VerdictFields().StartsWith("gear=wielded worn=5 ", StringComparison.Ordinal), "the gear scenario did not end wielded: " + gear.VerdictFields());
            var q = s.State.Self.Qualities;
            var inventory = s.State.Self.Inventory;

            // The plan: every cast chosen from the read model before anything is spent (the picks, their forms, the most the casts can cost).
            Stage = "plan";
            var creaturePick = GearCasts.Pick(q, ProfileRole.CreatureBuff, out var creatureWhy);
            Require(creaturePick is not null, $"creature: no spell passes the {GearCasts.MinChance} rule: {creatureWhy}");
            var lifePick = GearCasts.Pick(q, ProfileRole.LifeBuff, out var lifeWhy);
            Require(lifePick is not null, $"life: no spell passes the {GearCasts.MinChance} rule: {lifeWhy}");
            var itemPick = GearCasts.Pick(q, ProfileRole.ItemBuffWand, out var itemWhy);
            Require(itemPick is not null, $"item: no spell passes the {GearCasts.MinChance} rule: {itemWhy}");
            var fizzlePick = GearCasts.PickFizzle(q, s.State.Self.Spellbook, out var fizzleWhy);

            // A buffed mage (the buff cycle's enchantments last an hour and persist on the server) has no known spell more than 50 above its skill: the certain fizzle cannot be
            // made then. That is reported (fizzle=n/a:buffed, with the numbers), never skipped silently; the 0x0402 fizzle was proved live in M3 1.4 on the unbuffed mage and
            // offline by CastTrackerTests. Without a skill buff in the registry a missing pick still fails the test.
            var buffed = fizzlePick is null && HoldsMasteryBuff(q);
            Require(fizzlePick is not null || buffed, "fizzle: " + fizzleWhy);
            if (fizzlePick is null)
            {
                _log("fizzle: n/a: the mage holds a magic-skill mastery buff, so no known spell's power is more than 50 above its current skill (" + fizzleWhy + "); the certain fizzle is not cast in this run");
            }

            var creatureInfo = InfoOf("creature", creaturePick!.Spell.Id);
            var lifeInfo = InfoOf("life", lifePick!.Spell.Id);
            var itemInfo = InfoOf("item", itemPick!.Spell.Id);
            var fizzleInfo = fizzlePick is null ? null : InfoOf("fizzle", fizzlePick.Spell.Id);
            var worn = inventory.Equipped.Count;
            var (creatureMana, lifeMana, itemMana) = (GearCasts.ManaCeiling(creatureInfo, worn), GearCasts.ManaCeiling(lifeInfo, worn), GearCasts.ManaCeiling(itemInfo, worn));
            _manaNeeded = creatureMana + lifeMana + itemMana + (fizzlePick is null ? 0 : GearCasts.FizzleMana);
            _log($"plan: creature {creaturePick.Spell.Name} ({creatureMana}), life {lifePick.Spell.Name} ({lifeMana}), item {itemPick.Spell.Name} ({itemMana}), fizzle {(fizzlePick is null ? "n/a (buffed)" : $"{fizzlePick.Spell.Name} ({GearCasts.FizzleMana})")}; mana needed up to {_manaNeeded}");

            // The mana precondition: nothing has been cast, so waiting for regeneration is no retry. Bounded, logged with the numbers, and loud when it never comes.
            Stage = "mana";
            var (manaNow, manaMax) = Mana(s);
            Require(manaMax >= _manaNeeded, $"the casts need up to {_manaNeeded} mana and the maximum is {manaMax} (current {manaNow}): they can never all be paid");
            if (manaNow < _manaNeeded)
            {
                var waitStart = s.Clock.Now;
                _log($"mana: {manaNow} of {manaMax}, up to {_manaNeeded} needed: waiting for regeneration, at most {ManaWaitSeconds:0} s");
                while (Mana(s).Current < _manaNeeded)
                {
                    Require(s.Clock.Now - waitStart <= ManaWaitSeconds, $"the mana did not reach {_manaNeeded} within {ManaWaitSeconds:0} s: {Mana(s).Current} of {manaMax}");
                    yield return null;
                }

                _log(string.Create(CultureInfo.InvariantCulture, $"mana: {Mana(s).Current} reached the {_manaNeeded} needed after {s.Clock.Now - waitStart:0.#} s"));
            }

            Stage = "magic-mode";
            var mode = s.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
            yield return mode;
            Require(mode.Outcome is { Kind: ActionOutcomeKind.Confirmed or ActionOutcomeKind.Local }, "entering the magic combat mode: " + Text(mode));

            var manaBefore = Mana(s).Current;
            var slotsBefore = inventory.Pack.Count;
            var countsBefore = GearInventory.CountAll(s.State, out _);
            _log($"before the casts: mana {manaBefore}, needed up to {_manaNeeded}");

            string creature = string.Empty, life = string.Empty, item = string.Empty;
            foreach (var step in CastAndAssert("creature", creaturePick, creatureInfo, r => creature = r))
            {
                yield return step;
            }

            foreach (var step in CastAndAssert("life", lifePick, lifeInfo, r => life = r))
            {
                yield return step;
            }

            foreach (var step in CastAndAssert("item", itemPick, itemInfo, r => item = r))
            {
                yield return step;
            }

            // (d) Mana after the successful casts.
            var manaAfter = Mana(s).Current;
            _log($"after the successful casts: mana {manaAfter}");
            Require(manaAfter < manaBefore, $"mana did not fall over the successful casts: {manaBefore} -> {manaAfter}");

            // (c) The certain fizzle: the cheapest known self spell of the creature and life lines whose Power exceeds the current skill in its school by more than 50.
            if (fizzlePick is not null && fizzleInfo is not null)
            {
                Stage = "fizzle";
                _log(string.Create(CultureInfo.InvariantCulture, $"fizzle: {fizzlePick.Spell.Name} ({fizzlePick.Spell.Id}) has power {fizzlePick.Spell.Power} against {fizzlePick.Line.School} skill {fizzlePick.Skill}: margin {fizzlePick.Spell.Power - (long)fizzlePick.Skill}, mana {Mana(s).Current}"));
                var fizzleMark = s.State.Chat.Total;
                var fizzle = s.Actions.Cast(fizzlePick.Spell.Id, ActionSource.Host);
                yield return fizzle;
                _made.Add((fizzleInfo, fizzlePick.Skill));
                Require(
                    fizzle.Outcome is { Kind: ActionOutcomeKind.ServerError, Code: GearCasts.FizzleCode },
                    $"fizzle: expected a ServerError with WeenieError 0x0402 for {fizzlePick.Spell.Name} ({fizzlePick.Spell.Id}), got {Said(fizzle, fizzleMark)}");
                _log("fizzle: " + Text(fizzle));
            }

            // (e) The component burn, read once the last cast's inventory messages have arrived.
            Stage = "burn";
            var settle = s.Clock.Now;
            while (s.Clock.Now - settle < 2.0)
            {
                yield return null;
            }

            var countsAfter = GearInventory.CountAll(s.State, out _);
            var (burned, which) = Burned(countsBefore, countsAfter);
            var noBurn = 1.0;
            foreach (var (info, skill) in _made)
            {
                noBurn *= GearCasts.NoBurnProbability(info, _account, skill);
            }

            var asserted = noBurn < 1e-3;
            _log(string.Create(CultureInfo.InvariantCulture, $"burned: {which}; the chance that the {_made.Count} casts burn nothing was {noBurn:0.0000} (ACE Spell.TryBurnComponents), so the burn check is {(asserted ? "asserted" : "report-only")}"));
            if (asserted)
            {
                Require(burned >= 1, $"no kit component's stack fell over the casts although the chance of that was only {noBurn:0.000000} (before {countsBefore.Count} weenies, after {countsAfter.Count})");
            }

            Stage = "peace";
            var peace = s.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
            yield return peace;
            Require(peace.Outcome is { Kind: ActionOutcomeKind.Confirmed or ActionOutcomeKind.Local }, "returning to Non-combat: " + Text(peace));

            // (f) The top-up after the burn (fix A's merge: SpawnGear merges each top-up into the held stack, GameAction 0x0054): the gear scenario again (its cast off) tops the burned lines up; written over
            // PlayerInventory alone, so it holds with or without the merge's own names: the main pack's slot count is what it was before the casts, each component weenie is one stack, and a rerun after that tops up nothing.
            Stage = "topup";
            GearScenario? topUp = null;
            foreach (var step in RerunGear("topup", g => topUp = g))
            {
                yield return step;
            }

            var settleTopUp = s.Clock.Now;
            while (s.Clock.Now - settleTopUp < 2.0)
            {
                yield return null;
            }

            var merges = topUp!.TopUp; // one merge per component line topped up
            var slotsAfter = inventory.Pack.Count;
            var stacks = new Dictionary<uint, int>();
            foreach (var entry in inventory.Pack)
            {
                if (s.State.Objects.TryGet(entry.Guid, out var held))
                {
                    stacks[held.Weenie.WeenieClassId] = stacks.GetValueOrDefault(held.Weenie.WeenieClassId) + 1;
                }
            }

            var split = new StringBuilder();
            foreach (var component in _kit.Components)
            {
                if (stacks.GetValueOrDefault(component.Wcid) > 1)
                {
                    split.Append(CultureInfo.InvariantCulture, $"{component.Name} (weenie {component.Wcid}) in {stacks[component.Wcid]} stacks; ");
                }
            }

            _log($"topup: the gear scenario sent {topUp.TopUp} component requests (merges={merges}); main-pack slots {slotsBefore}->{slotsAfter}; {topUp.VerdictFields()}");
            Require(topUp.Failure is null, "the top-up run failed (a merge failure fails SpawnGear): " + topUp.Failure);
            Require(slotsAfter == slotsBefore, $"the main pack's slot count changed over the casts and the top-up: {slotsBefore} -> {slotsAfter} (a top-up that is not merged takes a slot)");
            Require(split.Length == 0, "a component is in more than one stack: " + split);
            GearScenario? again = null;
            foreach (var step in RerunGear("topup-rerun", g => again = g))
            {
                yield return step;
            }

            Require(again!.TopUp == 0 && again.GearSteps == 0, $"a rerun after the top-up still sends requests: {again.VerdictFields()}");

            Summary = string.Create(
                CultureInfo.InvariantCulture,
                $"cast_live creature={creature} life={life} item={item} fizzle={(fizzlePick is null ? "n/a:buffed" : fizzlePick.Spell.Id.ToString(CultureInfo.InvariantCulture) + ":0x0402")} mana_needed={_manaNeeded} mana_before={manaBefore} mana_after={manaAfter} p_no_burn={noBurn:0.0000} burned={burned} burn_check={(asserted ? "asserted" : "report-only")} merges={merges} slots={slotsBefore}->{slotsAfter}");

            if (LiveRun.KeepSession)
            {
                _log("the shared session stays logged in (no log off at the end of the leg)");
                yield break;
            }

            Stage = "logging-off";
            var off = s.Actions.LogOff(ActionSource.Host);
            yield return off;
            Confirmed(off, "logging off");
            Stage = "quitting";
            var quit = s.Actions.Quit(ActionSource.Host);
            yield return quit;
            Require(quit.Outcome is { Kind: ActionOutcomeKind.Sent }, "quitting: " + quit.Outcome?.Kind);
        }
    }
}
