using System.Numerics;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Data.Dat;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 2.1 s4b, the item-move actions against the bundled ACE, on Lifestone Throwaway only. WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD (it needs the backend with the Lifestone server mod ready).
/// One session: an Arrow 300 x 50 by the <c>item</c> verb (PlayerTools.SpawnItem), then every step through <see cref="IActionApi"/>, each waiting for the previous one's handle (the Action API's lane rule: no
/// move is sent into a busy chain, and the test asserts that no handle drew ACE's 0x001D). The chain, with what each assertion proves:
/// <list type="bullet">
/// <item><b>split_container</b>: <c>SplitToContainer</c> 10 into the main pack is Confirmed, a new guid of wcid 300 is held with 10 and the old stack reads 40.</item>
/// <item><b>split_3d</b>: <c>SplitTo3D</c> 5 is Confirmed, the old stack reads 35 and a new ground stack of 5 exists that the player does not hold.</item>
/// <item><b>pickup</b>: <c>PickUp</c> of that ground stack is Confirmed and the stack is held (the pack).</item>
/// <item><b>drop</b>: <c>Drop</c> is Confirmed, the stack is no longer held and is on the ground.</item>
/// <item>The ground stack is picked up again (Confirmed, held), then <b>split_wield</b>: the ammo slot is cleared first (a wielded ammo is moved to the pack by <c>MoveToPack</c>; the capture's second run showed ACE refusing a
/// split onto an occupied slot), then <c>SplitToWield</c> 5 at <see cref="EquipMask.MissileAmmo"/> is Confirmed, the slot holds a new stack of 5 and the old stack reads 30.</item>
/// <item><b>give_accept</b>: at Alfrin (a <c>place</c> and 8 s of settle: a give within about 3 s of a teleport is refused busy) a Bag of Rye Seed 30488 is given: Confirmed, and the seed has left the inventory.</item>
/// <item><b>give_refuse</b>: one Arrow given to Alfrin and one to Gawain Cooper (also after a place and the settle) both end as a <see cref="ActionOutcomeKind.ServerError"/> with code 0x046A (ACE's answer for an item
/// the NPC has no Give emote for, whether or not it has other Give emotes), and the arrow is still held.</item>
/// </list>
/// The verdict line is <c>moves split_container=&lt;yes|no&gt; split_3d= pickup= drop= split_wield= give_accept= give_refuse=&lt;code&gt;</c> (the code of the Alfrin refusal, <c>0x046A</c> expected, and
/// <c>+0x046A</c> appended for Gawain Cooper's). The throwaway keeps its items and its experience (the seed's AwardXP emote levels it); it is never purged by the test.
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test")]
[LiveSharedSession]
public sealed class ItemMovesLiveTests(ITestOutputHelper output)
{
    [LoopbackFact]
    public void The_item_moves_and_gives_run_through_the_action_api()
    {
        using var dats = DatSet.Open();
        var classes = HeadlessCharacter.RetailSkillClasses(dats);
        var leg = new MovesLeg(classes, output.WriteLine, dats);
        LiveRun.Run(output, leg, "moves", 400);

        var line = $"moves split_container={leg.SplitContainer} split_3d={leg.Split3D} pickup={leg.PickedUp} drop={leg.Dropped} split_wield={leg.SplitWield} give_accept={leg.GiveAccept} give_refuse={leg.GiveRefuse}";
        output.WriteLine(line);
        Assert.True(leg.Problems.Count == 0, "the checks that failed: " + string.Join("; ", leg.Problems) + " | " + line);
        Assert.Equal("yes", leg.SplitContainer);
        Assert.Equal("yes", leg.Split3D);
        Assert.Equal("yes", leg.PickedUp);
        Assert.Equal("yes", leg.Dropped);
        Assert.Equal("yes", leg.SplitWield);
        Assert.Equal("yes", leg.GiveAccept);
        Assert.Equal("0x046A+0x046A", leg.GiveRefuse);
    }

    private sealed class MovesLeg(IReadOnlyList<uint> classes, Action<string> log, DatSet dats) : LiveLeg("moves", classes, log, dats)
    {
        private const uint ArrowWcid = 300;
        private const uint RyeSeedWcid = 30488;

        public string SplitContainer { get; private set; } = "no";

        public string Split3D { get; private set; } = "no";

        public string PickedUp { get; private set; } = "no";

        public string Dropped { get; private set; } = "no";

        public string SplitWield { get; private set; } = "no";

        public string GiveAccept { get; private set; } = "no";

        public string GiveRefuse { get; private set; } = "none";

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in EnterThrowaway())
            {
                yield return h;
            }

            var spawn = S.Tools.SpawnItem(ArrowWcid, 50, ActionSource.Host);
            yield return spawn;
            Require(spawn.Result is not null, "the Arrow 300 x 50 grant failed: " + Describe(spawn));
            var arrows = spawn.Result!.Guid;
            foreach (var h in WaitHeld(arrows, "the Arrows"))
            {
                yield return h;
            }

            Check(StackOf(arrows) == 50, $"the Arrow stack reads {StackOf(arrows)}, not 50 (a merge with arrows already held?)");
            var startStack = StackOf(arrows);

            // 1. SplitToContainer 10 into the main pack: a new guid of 10, the old stack 10 less.
            var heldBefore = HeldOf(ArrowWcid);
            var split = S.Actions.SplitToContainer(arrows, 0, 0, 10, ActionSource.Host);
            yield return split;
            var splitOk = Confirmed(split, "SplitToContainer 10");
            foreach (var h in Until(() => NewIn(HeldOf(ArrowWcid), heldBefore) != 0 && StackOf(NewIn(HeldOf(ArrowWcid), heldBefore)) > 0, 4))
            {
                yield return h;
            }

            var newPackStack = NewIn(HeldOf(ArrowWcid), heldBefore);
            var containerOk = splitOk && Check(newPackStack != 0 && StackOf(newPackStack) == 10, $"split to the pack: the new stack is {newPackStack:X8} with {StackOf(newPackStack)}, wanted 10")
                && Check(StackOf(arrows) == startStack - 10, $"split to the pack: the old stack reads {StackOf(arrows)}, wanted {startStack - 10}");
            SplitContainer = containerOk ? "yes" : "no";

            // 2. SplitTo3D 5: the old stack 5 less, a new stack on the ground.
            var groundBefore = GroundOf(ArrowWcid);
            var oldBefore3D = StackOf(arrows);
            var split3D = S.Actions.SplitTo3D(arrows, 5, ActionSource.Host);
            yield return split3D;
            var split3DOk = Confirmed(split3D, "SplitTo3D 5");
            foreach (var h in Until(() => NewIn(GroundOf(ArrowWcid), groundBefore) != 0, 4))
            {
                yield return h;
            }

            var ground = NewIn(GroundOf(ArrowWcid), groundBefore);
            Split3D = split3DOk
                && Check(ground != 0 && StackOf(ground) == 5, $"split to 3D: the new ground stack is {ground:X8} with {StackOf(ground)}, wanted 5")
                && Check(StackOf(arrows) == oldBefore3D - 5, $"split to 3D: the old stack reads {StackOf(arrows)}, wanted {oldBefore3D - 5}") ? "yes" : "no";

            // 3. PickUp the ground stack (into the main pack).
            if (ground != 0)
            {
                var pick = S.Actions.PickUp(ground, 0, 0, ActionSource.Host);
                yield return pick;
                PickedUp = Confirmed(pick, "PickUp the ground stack") && Check(S.State.Self.Inventory.Contains(ground), "pick up: the stack is not in the inventory model") ? "yes" : "no";

                // 4. Drop it (on the ground).
                var drop = S.Actions.Drop(ground, ActionSource.Host);
                yield return drop;
                var dropOk = Confirmed(drop, "Drop the stack");
                foreach (var h in Until(() => GroundOf(ArrowWcid).Contains(ground), 3))
                {
                    yield return h;
                }

                Dropped = dropOk && Check(!S.State.Self.Inventory.Contains(ground), "drop: the stack is still held") && Check(GroundOf(ArrowWcid).Contains(ground), "drop: the stack is not on the ground") ? "yes" : "no";

                // Pick it up again, ready for the wield split.
                var again = S.Actions.PickUp(ground, 0, 0, ActionSource.Host);
                yield return again;
                Check(Confirmed(again, "PickUp again") && S.State.Self.Inventory.Contains(ground), "pick up again: the stack is not held");
            }

            // 5. SplitToWield 5 at the ammo slot, after clearing it (a wielded ammo is moved to the pack).
            if (S.State.Self.Inventory.EquippedAt((uint)EquipMask.MissileAmmo) is { } wielded)
            {
                Log($"the ammo slot holds {wielded:X8}: moving it to the pack first (a split onto an occupied slot is refused by ACE)");
                var clear = S.Actions.MoveToPack(wielded, 0, 0, ActionSource.Host);
                yield return clear;
                Confirmed(clear, "MoveToPack the wielded ammo");
                foreach (var h in Until(() => S.State.Self.Inventory.EquippedAt((uint)EquipMask.MissileAmmo) is null, 3))
                {
                    yield return h;
                }
            }

            var oldBeforeWield = StackOf(arrows);
            var wield = S.Actions.SplitToWield(arrows, EquipMask.MissileAmmo, 5, ActionSource.Host);
            yield return wield;
            var wieldOk = Confirmed(wield, "SplitToWield 5");
            foreach (var h in Until(() => S.State.Self.Inventory.EquippedAt((uint)EquipMask.MissileAmmo) is not null && StackOf(S.State.Self.Inventory.EquippedAt((uint)EquipMask.MissileAmmo)!.Value) > 0, 4))
            {
                yield return h;
            }

            var wieldedStack = S.State.Self.Inventory.EquippedAt((uint)EquipMask.MissileAmmo) ?? 0;
            SplitWield = wieldOk
                && Check(wieldedStack != 0 && wieldedStack != arrows && StackOf(wieldedStack) == 5, $"split to wield: the ammo slot holds {wieldedStack:X8} with {StackOf(wieldedStack)}, wanted a new stack of 5")
                && Check(StackOf(arrows) == oldBeforeWield - 5, $"split to wield: the old stack reads {StackOf(arrows)}, wanted {oldBeforeWield - 5}") ? "yes" : "no";

            // 6. Give: the Bag of Rye Seed to Alfrin (accepted), then one Arrow to Alfrin and to Gawain Cooper (refused 0x046A).
            var seedSpawn = S.Tools.SpawnItem(RyeSeedWcid, null, ActionSource.Host);
            yield return seedSpawn;
            Require(seedSpawn.Result is not null, "the Bag of Rye Seed grant failed: " + Describe(seedSpawn));
            var seed = seedSpawn.Result!.Guid;
            foreach (var h in WaitHeld(seed, "the Bag of Rye Seed"))
            {
                yield return h;
            }

            foreach (var h in PlaceAndSettle(Destination("Alfrin's cell 0xA9B40169", 0xA9B40169, new Vector3(107.3f, 45.2f, 94.01f)), "Alfrin"))
            {
                yield return h;
            }

            foreach (var h in Pause(8)) // a give sent within about 3 s of a teleport draws 028A 0x001D (the capture's runs 2 and 3)
            {
                yield return h;
            }

            var alfrin = Nearest("Alfrin", 25);
            string alfrinCode = "none";
            if (Check(alfrin != 0, "no object named 'Alfrin' within 25 m of the place"))
            {
                var accept = S.Actions.Give(alfrin, seed, 1, ActionSource.Host);
                yield return accept;
                var acceptOk = Confirmed(accept, "Give the Bag of Rye Seed to Alfrin");
                foreach (var h in Until(() => !S.State.Self.Inventory.Contains(seed), 3))
                {
                    yield return h;
                }

                GiveAccept = acceptOk && Check(!S.State.Self.Inventory.Contains(seed), "give accept: the seed is still in the inventory") ? "yes" : "no";

                var refuse = S.Actions.Give(alfrin, arrows, 1, ActionSource.Host);
                yield return refuse;
                alfrinCode = Refusal(refuse, "Give one Arrow to Alfrin");
            }

            foreach (var h in PlaceAndSettle(Destination("Gawain Cooper's cell 0xA9B40162", 0xA9B40162, new Vector3(112.6f, 33.05f, 94.01f)), "Gawain Cooper"))
            {
                yield return h;
            }

            foreach (var h in Pause(8))
            {
                yield return h;
            }

            var gawain = Nearest("Gawain Cooper", 25);
            var gawainCode = "none";
            if (Check(gawain != 0, "no object named 'Gawain Cooper' within 25 m of the place"))
            {
                var refuse = S.Actions.Give(gawain, arrows, 1, ActionSource.Host);
                yield return refuse;
                gawainCode = Refusal(refuse, "Give one Arrow to Gawain Cooper");
            }

            GiveRefuse = alfrinCode + "+" + gawainCode;
            Check(S.State.Self.Inventory.Contains(arrows), "the arrows left the inventory through a refused give");
            foreach (var h in LogOffAndQuit())
            {
                yield return h;
            }
        }

        /// <summary>The 0x046A refusal of a give: ServerError with that code (the verdict reads the code; a wrong outcome is a failed check).</summary>
        private string Refusal(ActionHandle handle, string what)
        {
            Log(what + ": " + Describe(handle));
            Check(handle.Outcome is { Code: not 0x001D }, what + ": the server refused the give as busy (0x001D): the lane rule or the settle was broken");
            var code = handle.Outcome is { Kind: ActionOutcomeKind.ServerError, Code: var c } ? $"0x{c:X4}" : "none";
            Check(code == "0x046A", $"{what}: wanted a ServerError with code 0x046A, got {Describe(handle)}");
            return code;
        }

        private static uint NewIn(HashSet<uint> now, HashSet<uint> before)
        {
            foreach (var guid in now)
            {
                if (!before.Contains(guid))
                {
                    return guid;
                }
            }

            return 0;
        }
    }
}
