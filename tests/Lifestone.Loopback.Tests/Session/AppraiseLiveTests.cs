using System.Globalization;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Fighter;
using Lifestone.Data.Dat;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M4 3.2 s4c: the appraisal, live, through <see cref="IActionApi.Identify"/> only. WRITTEN AND COMPILED BY THE SLICE, RUN BY THE LEAD: it needs the backend with the Lifestone server mod ready and Holtburg Hunter built
/// (<c>--scenario=fighter-build</c>); nothing is created, moved, sold or levelled. One session, about 2 minutes (the log in, one <c>place loc</c> beside Thelnoth with its settle, three Identifies of 1 to 10 ms each in the capture, the log off).
/// No creature is spawned (D23): the creature is Thelnoth Cort the Healer (an NPC is a Creature in ACE), placed beside the hunter so he is a known object.
/// <list type="bullet">
/// <item>(1) a Handy Healing Kit of the pack (capture C3: flags 0x5, success 1, no profile), (2) the wielded Mace (flags 0x25, the weapon profile) and (3) Thelnoth (flags 0x510D, the creature profile and the armour levels, or a failed roll: ACE sends the
/// tables and withholds the profiles): each <see cref="ActionOutcomeKind.Confirmed"/>, with the 00C9's flags and success printed as <c>appraise &lt;what&gt; flags=0x... success=&lt;0|1&gt; kind=... profile=...</c> and read from the model
/// (<see cref="IGameSessionView.TryGetAppraisal"/>), never from the handle's own claim.</item>
/// <item>The counter: an <see cref="AppraisalTally"/> fed the three handles says <c>appraised=3</c> when the creature roll succeeded; a failed roll (never seen yet, <c>TODO(m4-appraise-fail-live)</c>) is a finding, not a failure: the line then says
/// <c>appraised=2 failed=1</c> with the flags ACE sent, and the test passes if the tables are present and no profile came.</item>
/// </list>
/// Prints <c>appraise_live kit=... mace=... thelnoth=... appraised=&lt;n&gt; failed=&lt;n&gt;</c>. Run it alone with <c>dotnet test tests/Lifestone.Loopback.Tests --filter "FullyQualifiedName~AppraiseLiveTests" --logger "console;verbosity=detailed"</c>.
/// </summary>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
public sealed class AppraiseLiveTests(ITestOutputHelper output)
{
    private const string Item = "3-2";

    [LoopbackFact]
    public void Holtburg_Hunter_identifies_a_kit_the_wielded_Mace_and_Thelnoth_and_the_counter_says_appraised_3()
    {
        using var dats = DatSet.Open();
        var leg = new AppraiseLeg(output.WriteLine, dats);
        var result = LiveRun.Run(output, dats, leg, "appraise-hunter", 240, item: Item);
        var verdict = leg.VerdictLine();
        output.WriteLine(verdict);
        Assert.True(leg.Problems.Count == 0, "the checks that failed: " + string.Join("; ", leg.Problems) + " | " + verdict);
        Assert.True(result.Ok, result.Failure + " | " + verdict);
        Assert.Equal(3, leg.Tally.Answered);
        Assert.Equal(0, leg.Tally.NotFound);
        Assert.Equal(3, leg.Tally.Appraised + leg.Tally.Failed);
        if (leg.Tally.Appraised != 3)
        {
            // The creature roll failed: the first live sight of it (TODO(m4-appraise-fail-live)). The tables are there and no profile came.
            Assert.Equal(2, leg.Tally.Appraised);
            Assert.Equal(1, leg.Tally.Failed);
            Assert.True(leg.FailedRollHadNoProfile, "a failed roll carried a profile: " + verdict);
        }
    }

    private sealed class AppraiseLeg(Action<string> log, DatSet dats) : HunterShopLeg("appraise-hunter", log, dats)
    {
        private readonly string[] _lines = ["n/a", "n/a", "n/a"];

        public AppraisalTally Tally { get; } = new();

        public bool FailedRollHadNoProfile { get; private set; } = true;

        public string VerdictLine() =>
            $"appraise_live kit={_lines[0]} mace={_lines[1]} thelnoth={_lines[2]} appraised={Tally.Appraised} failed={Tally.Failed} cooldowns={Tally.Cooldowns} not_found={Tally.NotFound}";

        protected override IEnumerable<ActionHandle?> Script()
        {
            foreach (var h in EnterHunter()) { yield return h; }

            foreach (var h in GoNear("Thelnoth", ThelnothCell, ThelnothAt, 1.5f, 3.0)) { yield return h; }

            var thelnoth = NpcGuid;
            var kit = InPack(KitWcid).FirstOrDefault();
            var mace = S.State.Self.Inventory.Equipped.Select(e => e.Guid).FirstOrDefault(g => S.State.Objects.TryGet(g, out var o) && o.Weenie.WeenieClassId == MaceWcid);
            Check(kit != 0, "no Handy Healing Kit in the pack to identify");
            Check(mace != 0, "the Mace is not wielded: nothing to identify");
            Check(thelnoth != 0, "Thelnoth never came into the object table");

            (string What, uint Guid)[] targets = [("kit", kit), ("mace", mace), ("thelnoth", thelnoth)];
            for (var i = 0; i < targets.Length; i++)
            {
                var (what, guid) = targets[i];
                if (guid == 0)
                {
                    continue;
                }

                var identify = S.Actions.Identify(guid, ActionSource.Host);
                yield return identify;
                Tally.Record(identify);
                var confirmed = Confirmed(identify, $"Identify {what}");
                var known = S.TryGetAppraisal(guid, out var appraisal);
                Check(known && appraisal is not null, $"{what}: the model holds no appraisal after the Confirmed Identify");
                if (appraisal is null)
                {
                    continue;
                }

                var profile = appraisal.Weapon is not null ? "weapon" : appraisal.Creature is not null ? "creature" : appraisal.Armor is not null ? "armor" : "none";
                _lines[i] = $"flags=0x{(uint)appraisal.Flags:X} success={(appraisal.Success ? 1 : 0)} kind={identify.Result?.Kind} profile={profile} confirmed={Yes(confirmed)}";
                if (what == "thelnoth" && !appraisal.Success)
                {
                    FailedRollHadNoProfile = appraisal.Creature is null;
                    Log($"a failed creature roll was seen: flags=0x{(uint)appraisal.Flags:X}, ints {appraisal.Ints.Count}, creature profile {(appraisal.Creature is null ? "withheld" : "PRESENT")} (TODO(m4-appraise-fail-live) can close)");
                }

                foreach (var h in Pause(0.5)) { yield return h; }
            }

            foreach (var h in LogOffAndQuit()) { yield return h; }
        }
    }
}
