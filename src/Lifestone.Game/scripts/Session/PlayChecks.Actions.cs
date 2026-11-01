using Godot;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.StandIn;
using Lifestone.Data.Animation;
using Lifestone.Data.Magic;

namespace Lifestone.Game.Sessions;

/// <summary>
/// M3 1.2 s4c: the stand-in's answers to the magic, combat-mode and inventory actions, judged by each action's outcome and by what the read models then hold (a judged field reads what it names). Five checks,
/// each a plain <c>Check</c> that fails the step when false, as 1.1 s1b's raise check does (the convention for a new check of the play checks; no dats fails them, as the raise check does):
/// a self cast of Strength Self I completes Confirmed and the registry holds the buff, and the strength reads it (a second verdict, named for what it judges: raised, or unchanged when a stronger buff of the category is held; M3 1.2 fix R6); a wielded and then unwielded item completes both Confirmed and is back in the pack; a combat-mode
/// change to Magic reads 8 and back to NonCombat reads 1; and a spell above the character's skill fizzles (ServerError 0x0402) and leaves the registry's count and the spell's entries as they were. Offline: they prove the client's trackers and the stand-in's answers in
/// ACE's messages, not ACE's acceptance (the live loops do that).
/// </summary>
public sealed partial class PlayChecks
{
    private const uint BuffSpellId = 2; // Strength Self I
    private const uint FizzleSpellId = 4325; // Incantation of Strength Self, power 400: above the stand-in's magic skill plus 50
    private const uint PlaceholderWandWcid = 1_000_001; // a placeholder weenie of the stand-in, not a retail wcid (TODO(m3-standin-weenies))

    private async Task ActionChecksAsync(IGameSessionView session)
    {
        const string castName = "a-stand-in-self-cast-is-confirmed-and-the-registry-holds-the-buff";
        const string wieldName = "a-stand-in-wield-then-unwield-are-confirmed-and-the-item-is-back-in-the-pack";
        const string modeName = "a-combat-mode-change-is-confirmed-by-the-private-update-reads-magic-then-peace";
        const string fizzleName = "a-spell-above-the-characters-skill-fizzles-with-the-0402-error-and-leaves-the-registry-as-it-was";
        if (_services.Dats is not { } dats)
        {
            foreach (var name in new[] { castName, wieldName, modeName, fizzleName })
            {
                Check(name, false, "no dats: the stand-in cannot read the SpellTable");
            }

            return;
        }

        Visit.SpellTable ??= SpellTableData.Load(dats);
        Visit.ProjectileSetup ??= id => AceWorldExtract.Load().ProjectilesBySpell.TryGetValue(id, out var projectile) ? projectile.SetupId : null;

        // M3 2.1 s2a: the stand-in plays ACE's cast (the words, a motion per wind-up and the gesture, the scripts, Ready after the recoil). The plans of the two spells below are made on a worker, so the casts do not decode the
        // components' animations on the game thread. M3 2.1 s2b: the session host makes the planner on a worker as the run starts (SessionHost.InstallStandIn), so it is normally ready by now; this builds one only if it is not.
        if (Visit.CastPlanner is null)
        {
            await WaitFor(() => Visit.CastPlanner is not null, 20.0);
        }

        if (Visit.CastPlanner is null)
        {
            var spells = Visit.SpellTable.Spells;
            var planner = await Task.Run(() =>
            {
                var made = StandInCastPlanner.FromDats(dats);
                made.Plan(spells[BuffSpellId]);
                made.Plan(spells[FizzleSpellId]);
                return made;
            });
            Visit.CastPlanner = planner.Plan;
        }

        // The session decodes its SpellTable on a worker (M3 1.2 fix R5): the casts below are judged by the spell's kind, so they wait for it (a lookup before it is ready answers "Unknown").
        var tableReady = true;
        if (_host.Session is { } hosted)
        {
            var loading = hosted.SpellTableLoaded;
            tableReady = await Task.WhenAny(loading, Task.Delay(TimeSpan.FromSeconds(30))) == loading && hosted.SpellTableReady;
        }

        Visit.Items = new Dictionary<uint, StandInItemKind> { [StandInItemKind.PyrealWcid] = new("Pyreal", 25_000), [PlaceholderWandWcid] = new("Placeholder wand (stand-in)", null) };
        var q = session.State.Self.Qualities;
        var learn = session.Tools.LearnSpells([BuffSpellId, FizzleSpellId], ActionSource.Host);
        var learned = await WaitFor(() => learn.IsCompleted, 10.0);

        // The combat mode: Magic (ACE answers a cast only in it), judged by the quality the private update sets. M4 3.1 g: the combat-mode guard wants a caster in hand (one an earlier row left counts); a wand this row wields is put away at its end.
        var caster = await HoldCasterAsync(session);
        var magic = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        var magicDone = await WaitFor(() => magic.IsCompleted, 10.0);
        var modeInMagic = q.CombatMode;

        // The self buff.
        _ = q.TryGetAttribute(1, out var strength);
        var strengthBefore = EnchantmentRules.EnchantAttribute(q.Enchantments, 1, strength.Start + strength.Ranks);

        // A registry that already holds a spell of the same category at the same power or more (a graduated character wears every Incantation buff) decides the attribute: retail counts the strongest of a category, so the
        // new layer is held but the strength does not change. The check then judges "unchanged"; without such an entry it judges the rise.
        var buffInfo = Visit.SpellTable.Spells[BuffSpellId];
        var dominated = q.Enchantments.Any(e => e.SpellCategory == buffInfo.Category && e.PowerLevel >= buffInfo.Power);
        // Report-only (a new judge): the player's motion items of the cast arrive in the plan's order (a wind-up per scarab, the talisman's gesture, Ready), and the caster's script reaches the player's body.
        var selfGuid = session.State.Self.Guid;
        var motionWords = new List<ushort>();
        var scriptEvents = 0;
        using var motionSubscription = session.Events.Subscribe((in ObjectMotionChanged e) =>
        {
            if (e.IsSelf && e.Motion.Forward is { } word)
            {
                motionWords.Add(word);
            }
        });
        using var scriptSubscription = session.Events.Subscribe((in ObjectScriptPlayed e) =>
        {
            if (e.Guid == selfGuid)
            {
                scriptEvents++;
            }
        });
        var scriptsBefore = _mode.Avatars?.Stats.ServerScriptsPlayed ?? 0;
        var cast = session.Actions.Cast(BuffSpellId, ActionSource.Host);
        var castDone = await WaitFor(() => cast.IsCompleted, 10.0);
        var strengthAfter = EnchantmentRules.EnchantAttribute(q.Enchantments, 1, strength.Start + strength.Ranks);
        var held = q.Enchantments.Where(e => e.SpellId == BuffSpellId).ToArray();

        // M3 1.2 fix R6: two verdicts, each named for what it judges. The cast and the registry entry; and the strength, which rises only when no spell of the buff's category is held at this power or more (retail
        // counts the strongest of a category), so the second check's name says which of the two it judged.
        var plan = Visit.CastPlanner!(buffInfo)!;
        var expected = plan.Steps.Where(step => step.Kind != CastStepKind.SpellEffects).Select(step => MotionCommandTable.ToWire(step.Motion)).ToArray();
        var windups = plan.Steps.Count(step => step.Kind == CastStepKind.Windup);
        await WaitFor(() => motionWords.Count >= expected.Length, 5.0);
        var scriptsExpected = (buffInfo.CasterEffect != DatReaderWriter.Enums.PlayScript.Invalid ? 1 : 0) + (buffInfo.TargetEffect != DatReaderWriter.Enums.PlayScript.Invalid ? 1 : 0);
        var sequence = motionWords.SequenceEqual(expected) && scriptEvents == scriptsExpected && (_mode.Avatars?.Stats.ServerScriptsPlayed ?? 0) - scriptsBefore >= 1;
        Check(
            castName,
            learned && tableReady && castDone && cast.Outcome?.Kind == ActionOutcomeKind.Confirmed && held.Length == 1 && held[0].Duration > 0 && held[0].PowerLevel == buffInfo.Power,
            $"learn={learn.Outcome?.Kind} table_ready={tableReady} outcome={cast.Outcome?.Kind} detail={cast.Outcome?.Detail} registry_entries={held.Length} layer={held.FirstOrDefault().Layer} power={held.FirstOrDefault().PowerLevel} duration_s={held.FirstOrDefault().Duration} "
            + $"words='{plan.Words}' windups={windups} gestures={motionWords.Count}/{expected.Length} gesture_order={(motionWords.SequenceEqual(expected) ? "ok" : "bad")} cast_scripts={scriptEvents}/{scriptsExpected} server_scripts_on_body={(_mode.Avatars?.Stats.ServerScriptsPlayed ?? 0) - scriptsBefore} effect_at_s={plan.EffectTime:0.00} cast_sequence={(sequence ? "ok" : "bad")} (report-only: no judge in this slice)");
        Check(
            dominated ? "the-strength-is-unchanged-by-a-self-cast-of-a-category-already-held-at-this-power-or-more" : "the-strength-shows-the-buff-after-a-self-cast",
            held.Length == 1 && (dominated ? strengthAfter == strengthBefore : strengthAfter > strengthBefore),
            $"strength={strengthBefore}->{strengthAfter} category_already_held_at_this_power_or_more={dominated} judged={(dominated ? "unchanged" : "raised")}");

        // The fizzle (the stand-in's character casts with a fixed skill: a power-400 spell fizzles). Judged by the registry's count and the spell's entries before and after, not by the spell's absence: a
        // recorded character that already wears it (Incantation of Strength Self) is not a failed fizzle.
        var registryBefore = q.Enchantments.Length;
        var entriesBefore = q.Enchantments.Count(e => e.SpellId == FizzleSpellId);
        var fizzle = session.Actions.Cast(FizzleSpellId, ActionSource.Host);
        var fizzleDone = await WaitFor(() => fizzle.IsCompleted, 10.0);
        var registryAfter = q.Enchantments.Length;
        var entriesAfter = q.Enchantments.Count(e => e.SpellId == FizzleSpellId);
        Check(
            fizzleName,
            tableReady && fizzleDone && fizzle.Outcome is { Kind: ActionOutcomeKind.ServerError, Code: 0x0402 } && registryAfter == registryBefore && entriesAfter == entriesBefore,
            $"outcome={fizzle.Outcome?.Kind} code=0x{fizzle.Outcome?.Code:X4} detail={fizzle.Outcome?.Detail} registry={registryBefore}->{registryAfter} entries_of_the_spell={entriesBefore}->{entriesAfter}");

        // Back to peace (the Magic lane holds the cast until ACE's UseDone, so the change waits for it).
        var peace = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        var peaceDone = await WaitFor(() => peace.IsCompleted, 10.0);
        Check(
            modeName,
            magicDone && peaceDone && magic.Outcome?.Kind == ActionOutcomeKind.Confirmed && modeInMagic == CombatModes.Magic && peace.Outcome?.Kind == ActionOutcomeKind.Confirmed && q.CombatMode == CombatModes.NonCombat,
            $"magic={magic.Outcome?.Kind} mode_then={modeInMagic} peace={peace.Outcome?.Kind} mode_now={q.CombatMode}");
        await StowAsync(session, caster);

        // Wield and unwield an item the item tool granted (s2's pack wire).
        var spawn = session.Tools.SpawnItem(PlaceholderWandWcid, null, ActionSource.Host);
        var spawned = await WaitFor(() => spawn.IsCompleted, 10.0);
        var guid = spawn.Result?.Guid ?? 0;
        var inventory = session.State.Self.Inventory;
        var wield = session.Actions.Wield(guid, EquipMask.MeleeWeapon, ActionSource.Host);
        var wieldDone = await WaitFor(() => wield.IsCompleted, 10.0);
        var worn = inventory.Find(guid);
        var packWhileWorn = inventory.Pack.Count(e => e.Guid == guid);
        var unwield = session.Actions.Unwield(guid, ActionSource.Host);
        var unwieldDone = await WaitFor(() => unwield.IsCompleted, 10.0);
        var back = inventory.Find(guid);
        Check(
            wieldName,
            spawned && guid >= 0x80000000 && wieldDone && unwieldDone && wield.Outcome?.Kind == ActionOutcomeKind.Confirmed && worn is { Kind: InventoryPlaceKind.Equipped, Location: (uint)EquipMask.MeleeWeapon } && packWhileWorn == 0
                && unwield.Outcome?.Kind == ActionOutcomeKind.Confirmed && back.Kind == InventoryPlaceKind.MainPack && inventory.Pack.Any(e => e.Guid == guid),
            $"item=0x{guid:X8} wield={wield.Outcome?.Kind} worn={worn.Kind}@0x{worn.Location:X} in_pack_while_worn={packWhileWorn} unwield={unwield.Outcome?.Kind} now={back.Kind} slot={back.Placement}");
    }

    /// <summary>A profile spell that is not FastCast: Revitalize Self VI (a wind-up, the gesture, an effect at about 2 s, a target effect on the caster). The dats' profile has no spell with two scarabs and no creature buff that is not FastCast.</summary>
    private const uint WindupSpellId = 1182;

    /// <summary>The slot the cast check wields the stand-in's placeholder wand in (<see cref="PlaceholderWandWcid"/>).</summary>
    private const EquipMask WandSlot = EquipMask.Held;

    /// <summary>
    /// M3 2.1 s2b (DoD 7, "casts look and sound right"): the player's cast on the offline stand-in, wand wielded, in the Magic mode: a profile spell with a wind-up (<see cref="WindupSpellId"/>). Judged: <c>cast_scripts&gt;=1</c> and
    /// <c>cast_sounds&gt;=1</c>, the avatar set's counters inside the cast's window (the scripts played on the player, and the voices that started: the gesture animations' hooks, the scripts' hooks, the server's sounds); the gesture and
    /// wind-up counts are printed beside them. With <c>--play-frames</c> one review frame is saved between the wind-up and the effect (<c>cast-midway</c>: the gesture and the hand particles).
    /// </summary>
    private async Task CastChecksAsync(IGameSessionView session)
    {
        const string name = "a-cast-on-the-player-plays-its-wind-up-gesture-scripts-and-sounds";
        if (_services.Dats is null || Visit.SpellTable is not { } table || Visit.CastPlanner is not { } planner || !table.Spells.TryGetValue(WindupSpellId, out var info) || _mode.Avatars is not { } avatars)
        {
            Check(name, false, "no dats, no spell table, no cast planner or no avatar set: the cast cannot be planned or counted");
            return;
        }

        var plan = planner(info);
        var windups = plan?.Steps.Count(step => step.Kind == CastStepKind.Windup) ?? 0;
        var learn = session.Tools.LearnSpells([WindupSpellId], ActionSource.Host);
        var learned = await WaitFor(() => learn.IsCompleted, 10.0);
        var spawn = session.Tools.SpawnItem(PlaceholderWandWcid, null, ActionSource.Host);
        var spawned = await WaitFor(() => spawn.IsCompleted, 10.0);
        var wand = spawned ? spawn.Result?.Guid ?? 0 : 0;
        var wield = session.Actions.Wield(wand, WandSlot, ActionSource.Host);
        var wielded = await WaitFor(() => wield.IsCompleted, 10.0) && wield.Outcome?.Kind == ActionOutcomeKind.Confirmed;
        var magic = session.Actions.SetCombatMode(CombatMode.Magic, ActionSource.Host);
        var inMagic = await WaitFor(() => magic.IsCompleted, 10.0) && session.State.Self.Qualities.CombatMode == CombatModes.Magic;

        // The player's motions of the cast (the gesture and wind-up counts), and the moment the talisman's gesture starts: the review frame is taken a little after it, with the wind-up's hand particles still up.
        var gestureWord = plan is null ? (ushort)0 : MotionCommandTable.ToWire(plan.Steps.First(step => step.Kind == CastStepKind.CastGesture).Motion);
        var motionWords = new List<ushort>();
        ulong gestureAt = 0;
        using var motionSubscription = session.Events.Subscribe((in ObjectMotionChanged e) =>
        {
            if (e.IsSelf && e.Motion.Forward is { } word)
            {
                motionWords.Add(word);
                if (word == gestureWord && gestureAt == 0)
                {
                    gestureAt = Time.GetTicksMsec();
                }
            }
        });
        var stats = avatars.Stats;
        var scriptsBefore = stats.CastScripts;
        var soundsBefore = stats.CastSounds;
        var windowsBefore = stats.CastWindows;
        var cast = session.Actions.Cast(WindupSpellId, ActionSource.Host);
        var framed = false;
        if (_framesDirectory is not null && plan is not null && await WaitFor(() => gestureAt != 0, 15.0))
        {
            // Between the last wind-up and the effect: the gesture has started and the wind-up's sparks are still in the air.
            await WaitFor(() => Time.GetTicksMsec() >= gestureAt + 120, 2.0);
            SaveExtraFrame("cast-midway");
            framed = true;
        }

        var done = await WaitFor(() => cast.IsCompleted, 15.0);
        var closed = await WaitFor(() => !avatars.CastWindowOpen, 5.0);
        var scripts = stats.CastScripts - scriptsBefore;
        var sounds = stats.CastSounds - soundsBefore;
        var expected = plan?.Steps.Where(step => step.Kind != CastStepKind.SpellEffects).Select(step => MotionCommandTable.ToWire(step.Motion)).ToArray() ?? [];
        var voices = _mode.HookSounds is { CountsSounded: true };
        Check(
            name,
            learned && spawned && wielded && inMagic && done && closed && windups >= 1 && scripts >= 1 && sounds >= 1,
            $"spell='{info.Name}' learned={learned} wand={(wielded ? "wielded" : "no")} mode_magic={inMagic} outcome={cast.Outcome?.Kind} windups={windups} gestures={motionWords.Count}/{expected.Length} gesture_order={(motionWords.SequenceEqual(expected) ? "ok" : "bad")} "
            + $"cast_scripts={scripts} cast_sounds={sounds} cast_windows={stats.CastWindows - windowsBefore} window_closed={closed} voices_counted={voices} frame={(framed ? "cast-midway" : _framesDirectory is null ? "none (no --play-frames)" : "NOT TAKEN")} "
            + "(judged: cast_scripts>=1 cast_sounds>=1; the counts beside them are report-only)");

        // Back to peace and the wand away, so the checks after this one find the character as they did.
        var peace = session.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
        await WaitFor(() => peace.IsCompleted, 10.0);
        var unwield = session.Actions.Unwield(wand, ActionSource.Host);
        await WaitFor(() => unwield.IsCompleted, 10.0);
    }

    /// <summary>The account name the kit check hashes: made up, never a real account's (the real name is not in any file).</summary>
    private const string KitCheckAccount = "play-checks-account";

    /// <summary>
    /// M3 1.4 s0: <c>PlayerTools.SpawnGear</c> against the stand-in. The kit for a made-up account (the wand, the leather set, the profile's components) is requested through the <c>item</c> verb (the tools lane's 2 a second, so about half a minute),
    /// and the first verdict reads the inventory model itself, not the spawn's own flags: <c>kit=&lt;lines held with their world object&gt;/&lt;lines&gt;</c>. The second run asks again and must request nothing: <c>kit_topup=0</c>. Offline: it proves the client's counting and the stand-in's
    /// kit weenies in ACE's wire order, not ACE's acceptance (the live gear scenario does that).
    /// </summary>
    private async Task KitChecksAsync(IGameSessionView session)
    {
        const string arrivedName = "a-stand-in-gear-kit-arrives-in-the-pack-with-every-line-held-as-a-world-object";
        const string rerunName = "a-second-spawn-gear-asks-for-nothing";
        if (_services.Dats is null || Visit.SpellTable is not { } table)
        {
            Check(arrivedName, false, "no dats: the kit's components come from the SpellTable");
            Check(rerunName, false, "no dats: skipped with the kit check");
            return;
        }

        var kit = GearKit.Build(table.Spells, KitCheckAccount);
        var first = session.Tools.SpawnGear(kit, ActionSource.Host);
        var done = await WaitFor(() => first.IsDone, 150.0);
        var held = KitLinesHeld(session, kit);
        // M5 7 D154: a component line wider than one stack is asked for in several requests (F3, D87: PlayerTools.SpawnGear), so the lines requested, not the requests, add up with the lines already held.
        var linesRequested = KitLinesRequested(first);
        Check(
            arrivedName,
            done && first.Succeeded && first.Arrived && held == kit.Items.Count && linesRequested + first.AlreadyHeld == kit.Items.Count,
            $"kit={held}/{kit.Items.Count} lines_requested={linesRequested} requests={first.Requested} {first.Describe()} {kit.Describe()}");

        var second = session.Tools.SpawnGear(kit, ActionSource.Host);
        var doneAgain = await WaitFor(() => second.IsDone, 10.0);
        var heldAgain = KitLinesHeld(session, kit);
        Check(
            rerunName,
            doneAgain && second.Succeeded && second.Requested == 0 && second.AlreadyHeld == kit.Items.Count && heldAgain == kit.Items.Count,
            $"kit_topup={second.Requested} kit={heldAgain}/{kit.Items.Count} already_held={second.AlreadyHeld}");
    }

    /// <summary>M5 7 D154: the kit lines the spawn asked for (a line in several requests counted once).</summary>
    private static int KitLinesRequested(GearSpawn spawn)
    {
        var lines = new HashSet<GearItem>(ReferenceEqualityComparer.Instance); // a line by identity: two lines are never merged by equal fields
        foreach (var grant in spawn.Grants)
        {
            lines.Add(grant.Item);
        }

        return lines.Count;
    }

    /// <summary>How many kit lines the inventory model holds in full, each with its world object: a component's count summed over its stacks, the wand or a piece once (pack or equipped). Allocation-free except the one table.</summary>
    private static int KitLinesHeld(IGameSessionView session, GearKit kit)
    {
        var inventory = session.State.Self.Inventory;
        var counts = new Dictionary<uint, int>();
        foreach (var entry in inventory.Pack)
        {
            Count(session, inventory, counts, entry.Guid);
        }

        foreach (var worn in inventory.Equipped)
        {
            Count(session, inventory, counts, worn.Guid);
        }

        var lines = 0;
        foreach (var item in kit.Items)
        {
            if (counts.GetValueOrDefault(item.Wcid) >= item.Stack)
            {
                lines++;
            }
        }

        return lines;

        static void Count(IGameSessionView session, PlayerInventory inventory, Dictionary<uint, int> counts, uint guid)
        {
            if (session.State.Objects.TryGet(guid, out var o))
            {
                var stack = inventory.TryGetStack(guid, out var size, out _) ? (int)size : o.Weenie.StackSize ?? 1;
                counts[o.Weenie.WeenieClassId] = counts.GetValueOrDefault(o.Weenie.WeenieClassId) + stack;
            }
        }
    }
}
