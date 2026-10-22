using System.Globalization;
using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Tests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Dat;
using Lifestone.Data.World;
using Lifestone.Data.World.Data;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 2.1 s4b, the container model and the container actions against the bundled ACE. WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD (they need the backend with the Lifestone server mod ready; the hunter test also
/// needs Holtburg Hunter built by <c>--scenario=fighter-build</c>). Every step goes through <see cref="IActionApi"/>, never a raw send; each test prints one <c>containers ...</c> verdict line and then asserts. A hunter test
/// opens the corpse itself, by the hunt's first kill (the hunt's own corpse logic is s4a's and is not needed here: an already open corpse is a Confirmed "already open").
/// <list type="bullet">
/// <item><b>A_side_pack_at_login_lists_its_contents</b> (Lifestone Throwaway; two sessions): session A makes sure a Pack 136 holding an item exists (Pack 136 and Pyreal 273 x 7 by the <c>item</c> verb and a
/// <c>MoveToPack</c>, as the capture's C1 did, only when absent), logs off; session B logs in, and proves that the side pack's <see cref="ContainerView"/> (<c>State.Containers</c>) lists the item with its world object
/// (the 0196 came before the F745s and was held until they landed), that the Pack 136 is among <c>Inventory.SidePacks</c> and that LoginComplete completed (the player is Present with its qualities).
/// Verdict <c>containers side_packs=&lt;n&gt; side_pack_items=&lt;n&gt; login_complete=&lt;bool&gt;</c>.</item>
/// <item><b>The_hunters_own_kill_corpse_opens_and_closes</b> (Holtburg Hunter; one session): the hunt's first kill, then <c>OpenContainer</c> on its corpse is Confirmed with the entries listed in the view (all with
/// world objects), <c>CloseContainer</c> is Confirmed (the 0052) and the view is gone, and the hunter is put back (no attack, NonCombat, the Mace wielded, five other items worn: the end state
/// <c>wielded=331 worn=5</c> that FighterBuildLoopbackTests reads). Verdict <c>containers corpse_opened= corpse_items= closed= restored=</c>. It levels the hunter by one kill (owner decision D1).</item>
/// <item><b>A_chest_opens_and_closes_and_a_locked_chest_refuses</b> (Lifestone Throwaway): Chest 143 at cell 0xA9B4016A is opened (Confirmed, the view names it) and closed (Confirmed, the view is gone); the Aluvian
/// Pathwarden Chest at 0xA9B40022 is a <see cref="ActionOutcomeKind.ServerError"/> whose detail says "locked" (the F750 0x94 sound and the UseDone, no text), and no view is open. Verdict
/// <c>containers chest_opened= closed= locked=refused</c>.</item>
/// </list>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
public sealed class ContainerLiveTests(ITestOutputHelper output)
{
    private const uint HoltburgChestCell = 0xA9B4016A;
    private const uint PathwardenCell = 0xA9B40022;
    private const uint PackWcid = 136;

    [LoopbackFact]
    public void A_side_pack_at_login_lists_its_contents()
    {
        using var dats = DatSet.Open();
        var classes = HeadlessCharacter.RetailSkillClasses(dats);
        var a = new EnsurePackLeg(classes, output.WriteLine, dats);
        LiveRun.Run(output, dats, a, "containers-pack-a", 300);
        output.WriteLine("containers pack ensured=" + a.Ensured.ToString().ToLowerInvariant());
        Assert.True(a.Problems.Count == 0, "session A (the pack and its item): " + string.Join("; ", a.Problems));
        LiveRun.Pause();

        var b = new LoginLeg(classes, output.WriteLine, dats);
        var logLines = new List<string>();
        LiveRun.Run(output, dats, b, "containers-pack-b", 300, logLines);
        var tolerated = logLines.Count(l => l.Contains("a side-pack item joined the inventory model from its own CreateObject", StringComparison.Ordinal));
        output.WriteLine("containers pack adoption_tolerance_lines=" + tolerated.ToString(CultureInfo.InvariantCulture));
        Assert.True(tolerated == 0, "a side-pack item was adopted from its own CreateObject (the tolerance), so the contents did not come from the 0196 alone");
        var line = $"containers side_packs={b.SidePacks} side_pack_items={b.SidePackItems} login_complete={b.LoginComplete.ToString().ToLowerInvariant()}";
        output.WriteLine(line);
        Assert.True(b.Problems.Count == 0, "the checks that failed: " + string.Join("; ", b.Problems) + " | " + line);
        Assert.True(b.LoginComplete, line);
        Assert.True(b.SidePacks >= 1 && b.SidePackItems >= 1, line);
    }

    [LoopbackFact]
    public void The_hunters_own_kill_corpse_opens_and_closes()
    {
        using var dats = DatSet.Open();
        var leg = new CorpseLeg(output.WriteLine, dats);
        LiveRun.Run(output, dats, leg, "containers-corpse", HuntLiveRun.QuitAfterSeconds);
        var line = $"containers corpse_opened={leg.Opened.ToString().ToLowerInvariant()} corpse_items={leg.Items} closed={leg.Closed.ToString().ToLowerInvariant()} restored={leg.Restored.ToString().ToLowerInvariant()}";
        output.WriteLine(line);
        Assert.True(leg.Problems.Count == 0, "the checks that failed: " + string.Join("; ", leg.Problems) + " | " + line);
        Assert.True(leg.Opened, line);
        Assert.True(leg.Closed, line);
        Assert.True(leg.Restored, line);
    }

    [LoopbackFact]
    public void A_chest_opens_and_closes_and_a_locked_chest_refuses()
    {
        using var dats = DatSet.Open();
        var classes = HeadlessCharacter.RetailSkillClasses(dats);
        var leg = new ChestLeg(classes, output.WriteLine, dats);
        LiveRun.Run(output, dats, leg, "containers-chest", 300);
        var line = $"containers chest_opened={leg.Opened.ToString().ToLowerInvariant()} closed={leg.Closed.ToString().ToLowerInvariant()} locked={leg.Locked}";
        output.WriteLine(line);
        Assert.True(leg.Problems.Count == 0, "the checks that failed: " + string.Join("; ", leg.Problems) + " | " + line);
        Assert.True(leg.Opened, line);
        Assert.True(leg.Closed, line);
        Assert.Equal("refused", leg.Locked);
    }

    // ------------------------------------------------------------------------------------------------ the legs

    /// <summary>The pack of weenie class 136 among the side packs that holds something, or 0.</summary>
    private static uint FilledPack(IGameSessionView s)
    {
        foreach (var pack in s.State.Self.Inventory.SidePacks)
        {
            if (s.State.Objects.TryGet(pack.Guid, out var o) && o.Weenie.WeenieClassId == PackWcid && s.State.Self.Inventory.ItemsIn(pack.Guid).Count > 0)
            {
                return pack.Guid;
            }
        }

        return 0;
    }

    private sealed class EnsurePackLeg(IReadOnlyList<uint> classes, Action<string> log, DatSet dats) : LiveLeg("containers-pack-a", classes, log, dats)
    {
        public bool Ensured { get; private set; }

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in EnterThrowaway())
            {
                yield return h;
            }

            foreach (var h in Pause(3))
            {
                yield return h;
            }

            if (FilledPack(S) != 0)
            {
                Log("a Pack 136 holding an item exists already: nothing to create");
                Ensured = true;
            }
            else
            {
                var existing = S.State.Self.Inventory.SidePacks.Select(p => p.Guid).FirstOrDefault(g => S.State.Objects.TryGet(g, out var o) && o.Weenie.WeenieClassId == PackWcid);
                var pack = existing;
                if (pack == 0)
                {
                    var packSpawn = S.Tools.SpawnItem(PackWcid, null, ActionSource.Host);
                    yield return packSpawn;
                    Require(packSpawn.Result is not null, "the Pack 136 grant failed: " + Describe(packSpawn));
                    pack = packSpawn.Result!.Guid;
                    foreach (var h in WaitHeld(pack, "the Pack"))
                    {
                        yield return h;
                    }
                }

                var pyrealSpawn = S.Tools.SpawnItem(273, 7, ActionSource.Host);
                yield return pyrealSpawn;
                Require(pyrealSpawn.Result is not null, "the Pyreal grant failed: " + Describe(pyrealSpawn));
                var pyreal = pyrealSpawn.Result!.Guid;
                foreach (var h in WaitHeld(pyreal, "the Pyreals"))
                {
                    yield return h;
                }

                var move = S.Actions.MoveToPack(pyreal, pack, 0, ActionSource.Host);
                yield return move;
                Confirmed(move, "MoveToPack the Pyreals into the Pack");
                foreach (var h in Until(() => S.State.Self.Inventory.Find(pyreal).Container == pack, 5))
                {
                    yield return h;
                }

                Ensured = Check(S.State.Self.Inventory.Find(pyreal).Container == pack, "the Pyreals are not in the Pack after the move");
                foreach (var h in Pause(2))
                {
                    yield return h;
                }
            }

            foreach (var h in LogOffAndQuit())
            {
                yield return h;
            }
        }
    }

    private sealed class LoginLeg(IReadOnlyList<uint> classes, Action<string> log, DatSet dats) : LiveLeg("containers-pack-b", classes, log, dats)
    {
        public int SidePacks { get; private set; }

        public int SidePackItems { get; private set; }

        public bool LoginComplete { get; private set; }

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in EnterThrowaway())
            {
                yield return h;
            }

            // The session's own LoginComplete action must have been Confirmed (read from the action log, 2.1-R3), with the player Present and its qualities known.
            LoginComplete = LoginCompleteConfirmedSince(0) && S.State.Self.Travel == SelfTravel.Present && S.State.Self.Qualities.IsKnown;
            foreach (var h in Pause(6)) // the 0196s come first in the description burst; the contents' F745s follow within the burst
            {
                yield return h;
            }

            SidePacks = S.State.Self.Inventory.SidePacks.Count;
            var packFound = false;
            foreach (var pack in S.State.Self.Inventory.SidePacks)
            {
                var isPack = S.State.Objects.TryGet(pack.Guid, out var o) && o.Weenie.WeenieClassId == PackWcid;
                if (!S.State.Containers.TryGet(pack.Guid, out var view))
                {
                    Log($"side pack {pack.Guid:X8}: no container view");
                    continue;
                }

                var withObject = view.Entries.Count(e => e.Resolved && S.State.Objects.TryGet(e.Guid, out _));
                Log($"side pack {pack.Guid:X8} (wcid 136: {isPack}): {view.Entries.Count} entries, {withObject} with a world object, resolved={view.IsResolved}, contents named={view.ContentsNamed}");
                SidePackItems += withObject;
                Check(!isPack || view.ContentsNamed, $"side pack {pack.Guid:X8}: its contents were not named by a 0196 (ContentsNamed is false)");
                if (isPack && withObject > 0 && view.IsResolved && view.ContentsNamed)
                {
                    packFound = true;
                }
            }

            Check(LoginComplete, "LoginComplete did not complete: the player is not Present with its qualities");
            Check(packFound, "no Pack 136 among the side packs lists an item with its world object");
            foreach (var h in LogOffAndQuit())
            {
                yield return h;
            }
        }
    }

    private sealed class CorpseLeg(Action<string> log, DatSet dats) : LiveLeg("containers-corpse", [], log, dats)
    {
        private readonly RecordingLogSink _huntLog = new();
        private HuntScenario? _hunt;
        private uint _lastTarget;
        private string _lastName = string.Empty;
        private CellFrame? _lastPosition;

        public bool Opened { get; private set; }

        public int Items { get; private set; }

        public bool Closed { get; private set; }

        public bool Restored { get; private set; }

        protected override IEnumerable<ActionHandle?> Script()
        {
            var heights = new Dictionary<uint, float?>();
            Follow = new HuntFollow(Dats, _huntLog);
            _hunt = new HuntScenario(
                new HuntOptions { Kills = 1, Weapon = HuntWeapon.Melee, OpenCorpse = false }, // the test opens and closes the corpse itself (2.1-R12)
                _huntLog,
                cell =>
                {
                    if (!heights.TryGetValue(cell.CellId, out var height))
                    {
                        heights[cell.CellId] = height = HuntGround.TerrainHeight(Dats, cell);
                    }

                    return height;
                },
                follow: Follow);
            FollowIsTheHunts = true;
            for (;;)
            {
                var status = _hunt.Step(S, Now);
                if (_hunt.Target != 0)
                {
                    _lastTarget = _hunt.Target;
                    _lastName = _hunt.TargetName;
                    if (S.State.Objects.TryGet(_lastTarget, out var t) && t.Position is { } p)
                    {
                        _lastPosition = p;
                    }
                }

                if (_hunt.Tally.Kills >= 1)
                {
                    break;
                }

                if (status != ScenarioStatus.Running)
                {
                    Failure = _hunt.Failure ?? "the hunt ended without a kill";
                    Check(false, "the hunt ended without a kill: " + Failure + " (it wound itself down); the corpse steps are skipped");
                    yield break;
                }

                yield return null;
            }

            FollowIsTheHunts = false;
            Log($"the kill came: target '{_lastName}' {_lastTarget:X8}; looking for its corpse");
            var corpse = 0u;
            var since = S.Clock.Now;
            while (corpse == 0 && S.Clock.Now - since < 8)
            {
                corpse = FindCorpse();
                if (corpse == 0)
                {
                    yield return null;
                }
            }

            if (Check(corpse != 0, "no corpse object appeared within 8 s of the kill"))
            {
                // At once: an empty corpse rots within 15 s. The open is accepted in the melee stance (the capture's C2).
                var open = S.Actions.OpenContainer(corpse, ActionSource.Host);
                yield return open;
                Log("OpenContainer: " + Describe(open));
                var view = S.State.Containers.Viewed;
                Opened = Check(open.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "OpenContainer: " + Describe(open))
                    && Check(view is { } v && v.Guid == corpse, "OpenContainer: the viewed container is not the corpse");
                if (view is not null)
                {
                    Items = view.Entries.Count;
                    var named = view.Entries.Count(e => e.Resolved && S.State.Objects.TryGet(e.Guid, out _));
                    Log($"the corpse lists {Items} entries, {named} with a world object");
                    Check(Items == named, $"the corpse lists {Items} entries but only {named} have a world object");
                }

                var close = S.Actions.CloseContainer(corpse, ActionSource.Host);
                yield return close;
                Log("CloseContainer: " + Describe(close));
                foreach (var h in Until(() => S.State.Containers.Viewed is null, 2))
                {
                    yield return h;
                }

                Closed = Check(close.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "CloseContainer: " + Describe(close))
                    && Check(S.State.Containers.Viewed is null, "CloseContainer: the container view is still open");
            }

            foreach (var h in Restore())
            {
                yield return h;
            }
        }

        private uint FindCorpse()
        {
            var wanted = "Corpse of " + _lastName;
            var origin = _lastPosition ?? S.State.Self.Position;
            if (origin is not { } from)
            {
                return 0;
            }

            uint best = 0;
            var bestDistance = 12f; // a few metres of the target's last position; the corpse of the hunt's own target
            foreach (var o in S.State.Objects.All)
            {
                if (o.Position is not { } p || !o.Name.StartsWith("Corpse of", StringComparison.Ordinal) || (p.CellId >> 16) != (from.CellId >> 16))
                {
                    continue;
                }

                var d = Vector3.Distance(p.Origin, from.Origin) + (string.Equals(o.Name, wanted, StringComparison.Ordinal) ? 0f : 5f);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = o.Guid;
                }
            }

            return best;
        }

        private IEnumerable<ActionHandle?> Restore()
        {
            // The end state of the fighter, as the hunt's wind-down leaves it: no attack, NonCombat, the Mace wielded, back in Holtburg.
            if (S.Attack.Active)
            {
                var cancel = S.Actions.CancelAttack(ActionSource.Host);
                foreach (var h in Until(() => cancel.IsCompleted, 10))
                {
                    yield return h;
                }
            }

            if (S.State.Self.Qualities.CombatMode is not (0 or (int)CombatMode.NonCombat))
            {
                var peace = S.Actions.SetCombatMode(CombatMode.NonCombat, ActionSource.Host);
                foreach (var h in Until(() => peace.IsCompleted, 10))
                {
                    yield return h;
                }

                Log("NonCombat: " + Describe(peace));
            }

            if (!MaceWielded(out var mace) && mace != 0)
            {
                var wield = S.Actions.Wield(mace, EquipMask.MeleeWeapon, ActionSource.Host);
                foreach (var h in Until(() => wield.IsCompleted, 10))
                {
                    yield return h;
                }

                Log("wield the Mace again: " + Describe(wield));
            }

            var home = TeleportCatalog.FromAce(AcePointsOfInterest.All.Single(p => p.DisplayName == "Holtburg"));
            foreach (var h in PlaceAndSettle(home, "back to Holtburg"))
            {
                yield return h;
            }

            var wornWcids = new List<uint>();
            var maceWielded = MaceWielded(out _);
            foreach (var e in S.State.Self.Inventory.Equipped)
            {
                if (S.State.Objects.TryGet(e.Guid, out var o))
                {
                    wornWcids.Add(o.Weenie.WeenieClassId);
                }
            }

            // The fighter check's own count (FighterBuildScenario's worn=): the kit's leather armour lines, through the one shared helper (2.1-R1).
            var worn = FighterKit.CountWornArmour(wornWcids);

            Restored = maceWielded && worn == 5;
            Log($"end state: wielded={(maceWielded ? FighterKit.MaceWcid : 0)} worn={worn} (the template reads wielded={FighterKit.MaceWcid.ToString(CultureInfo.InvariantCulture)} worn=5)");
            Check(Restored, $"the hunter is not restored: wielded={(maceWielded ? FighterKit.MaceWcid : 0)} worn={worn}");
            foreach (var h in LogOffAndQuit())
            {
                yield return h;
            }
        }

        private bool MaceWielded(out uint mace)
        {
            mace = 0;
            var inventory = S.State.Self.Inventory;
            foreach (var e in inventory.Equipped)
            {
                if (S.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == FighterKit.MaceWcid)
                {
                    mace = e.Guid;
                    return true;
                }
            }

            foreach (var e in inventory.Pack)
            {
                if (S.State.Objects.TryGet(e.Guid, out var o) && o.Weenie.WeenieClassId == FighterKit.MaceWcid)
                {
                    mace = e.Guid;
                    return false;
                }
            }

            return false;
        }
    }

    private sealed class ChestLeg(IReadOnlyList<uint> classes, Action<string> log, DatSet dats) : LiveLeg("containers-chest", classes, log, dats)
    {
        public bool Opened { get; private set; }

        public bool Closed { get; private set; }

        public string Locked { get; private set; } = "none";

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in EnterThrowaway())
            {
                yield return h;
            }

            var chest = 0u;
            Vector3[] offsets = [Vector3.Zero, new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];
            foreach (var offset in offsets)
            {
                var spot = new Vector3(75.82f, 38.39f, 94.01f) + offset;
                if (offset != Vector3.Zero)
                {
                    Log($"no Chest in reach of the previous arrival (a wall?): trying {offset} away");
                }

                foreach (var h in PlaceAndSettle(Destination("chest 0xA9B4016A", HoltburgChestCell, spot), "chest 143"))
                {
                    yield return h;
                }

                chest = Nearest("Chest", 8);
                if (chest != 0)
                {
                    break;
                }
            }

            if (Check(chest != 0, "no object named 'Chest' within 8 m of any arrival"))
            {
                var open = S.Actions.OpenContainer(chest, ActionSource.Host);
                yield return open;
                Log("OpenContainer: " + Describe(open));
                Opened = Check(open.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "OpenContainer: " + Describe(open))
                    && Check(S.State.Containers.Viewed is { } v && v.Guid == chest, "OpenContainer: the viewed container is not the chest");

                var close = S.Actions.CloseContainer(chest, ActionSource.Host);
                yield return close;
                Log("CloseContainer: " + Describe(close));
                foreach (var h in Until(() => S.State.Containers.Viewed is null, 2))
                {
                    yield return h;
                }

                Closed = Check(close.Outcome is { Kind: ActionOutcomeKind.Confirmed }, "CloseContainer: " + Describe(close))
                    && Check(S.State.Containers.Viewed is null, "CloseContainer: the chest view is still open");
            }

            foreach (var h in PlaceAndSettle(Destination("pathwarden 0xA9B40022", PathwardenCell, new Vector3(99, 31, 94)), "the Aluvian Pathwarden Chest"))
            {
                yield return h;
            }

            var locked = Nearest("Aluvian Pathwarden Chest", 15);
            if (Check(locked != 0, "no 'Aluvian Pathwarden Chest' within 15 m of the arrival"))
            {
                var use = S.Actions.OpenContainer(locked, ActionSource.Host);
                yield return use;
                Log($"OpenContainer of the locked chest {locked:X8}: " + Describe(use) + " (the F750 0x94 sound and the UseDone expected, no text)");
                var refused = use.Outcome is { Kind: ActionOutcomeKind.ServerError, Detail: { } detail } && detail.Contains("locked", StringComparison.OrdinalIgnoreCase);
                Locked = refused ? "refused" : "not_refused";
                Check(refused, "the locked chest: wanted a ServerError whose detail says locked, got " + Describe(use));
                Check(S.State.Containers.Viewed is null, "the locked chest left a container view open");
            }

            foreach (var h in LogOffAndQuit())
            {
                yield return h;
            }
        }
    }
}
