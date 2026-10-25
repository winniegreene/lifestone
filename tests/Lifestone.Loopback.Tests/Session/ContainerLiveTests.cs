using System.Globalization;
using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Fighter;
using Lifestone.Client.Tests;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.Scenarios.Journey;
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
/// <item>The corpse and chest facts were retired into the melee journey's container stations (<c>HuntScenario</c>'s loot and <c>ChestsStation</c>; M4 P5b J6); only this side-pack relog fact stays as loopback.</item>
/// </list>
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[LoopbackCharacter("Lifestone Throwaway")]
[LiveSharedSession]
public sealed class ContainerLiveTests(ITestOutputHelper output)
{
    private const uint PackWcid = 136;

    [LoopbackFact]
    public void A_side_pack_at_login_lists_its_contents()
    {
        using var dats = DatSet.Open();
        var classes = HeadlessCharacter.RetailSkillClasses(dats);
        var a = new EnsurePackLeg(classes, output.WriteLine, dats);
        LiveRun.Run(output, a, "containers-pack-a", 300, ownLogin: true); // a relog is the point: session A makes the pack, session B logs in and reads it
        output.WriteLine("containers pack ensured=" + a.Ensured.ToString().ToLowerInvariant());
        Assert.True(a.Problems.Count == 0, "session A (the pack and its item): " + string.Join("; ", a.Problems));
        var b = new LoginLeg(classes, output.WriteLine, dats);
        var logLines = new List<string>();
        LiveRun.Run(output, b, "containers-pack-b", 300, logLines, ownLogin: true);
        var tolerated = logLines.Count(l => l.Contains("a side-pack item joined the inventory model from its own CreateObject", StringComparison.Ordinal));
        output.WriteLine("containers pack adoption_tolerance_lines=" + tolerated.ToString(CultureInfo.InvariantCulture));
        Assert.True(tolerated == 0, "a side-pack item was adopted from its own CreateObject (the tolerance), so the contents did not come from the 0196 alone");
        var line = $"containers side_packs={b.SidePacks} side_pack_items={b.SidePackItems} login_complete={b.LoginComplete.ToString().ToLowerInvariant()}";
        output.WriteLine(line);
        Assert.True(b.Problems.Count == 0, "the checks that failed: " + string.Join("; ", b.Problems) + " | " + line);
        Assert.True(b.LoginComplete, line);
        Assert.True(b.SidePacks >= 1 && b.SidePackItems >= 1, line);
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
}
