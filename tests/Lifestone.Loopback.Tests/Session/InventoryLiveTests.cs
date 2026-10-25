using System.Globalization;
using System.Security.Cryptography;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Hosting;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Tests;
using Lifestone.Data.Dat;
using Lifestone.Net.Messages;
using Lifestone.Net.Session;
using Lifestone.Tests.Shared.Loopback;
using Xunit.Abstractions;

namespace Lifestone.Loopback.Tests.Session;

/// <summary>
/// M3 1.2 s2: the inventory read model against the bundled ACE. WRITTEN AND COMPILED, RUN BY THE LEAD. On <c>Lifestone Throwaway</c> only (found on the test account or created with the headless
/// character intent; never Smoke, Loopback or BSD Mage) it asks the mod for Pyreal (<see cref="PlayerTools.SpawnItem"/>, wcid 273), twice, and watches the player's <see cref="PlayerInventory"/>.
/// </summary>
/// <remarks>
/// <para><b>What it proves.</b> (1) The CreateObject, 02DA Container and GameEvent 0x0022 that ACE sends from <c>TryCreateInInventoryWithNetworking</c> (<c>Player_Inventory.cs</c> 90-114) put the
/// granted item in the player's main pack (<see cref="PlayerInventory.Pack"/>) or in a side pack that has room (the mod's item verb adds it where ACE does), and its world object holds it. (2) After the login the description's lists are in
/// (<see cref="PlayerInventory.IsKnown"/>); the run prints the pack, side-pack and equipped counts and the burden read from the player's quality, for the lead to compare with the character.
/// (3) Each grant raises <see cref="PlayerInventory.Count"/> by exactly one (a grant that ACE merged into an existing stack would show as a failure here: the throwaway starts without Pyreal, and the
/// mod creates a new item per request).</para>
/// <para><b>Touches</b> only the throwaway (items given to it, never removed: they go with a purge). Since M4 P4 the run is over the throwaway's shared session (one login per test process; the throwaway is put in peace mode first) and does not log off at the end; the lists after the login are those of the session's entry.</para>
/// </remarks>
[Trait("Category", LoopbackCollection.Category)]
[Collection(LoopbackCollection.Name)]
[Trait("Account", "test-tools")]
[LoopbackCharacter("Lifestone Throwaway")]
[LiveSharedSession]
public sealed class InventoryLiveTests(ITestOutputHelper output)
{
    private const string Throwaway = "Lifestone Throwaway";

    [LoopbackFact]
    public void An_item_the_mod_grants_arrives_in_the_inventory_model_as_ACE_sends_it()
    {
        using var dats = DatSet.Open();
        var leg = new InventoryLeg(HeadlessCharacter.RetailSkillClasses(dats), output.WriteLine);
        var result = Run(leg, 300);
        Assert.True(result.Ok, result.Failure);
        Assert.Equal("done", leg.Stage);
        Assert.True(leg.FirstGrantInPack, "the first grant did not land in the main pack with its world object");
        Assert.True(leg.SecondGrantInPack, "the second grant did not land in the main pack with its world object");
    }

    private HeadlessRunResult Run(IScenario scenario, double seconds) =>
        LiveRun.Run(output, scenario, "inventory", seconds, item: "p4", character: Throwaway);

    /// <summary>The whole leg in one session, written as a script that yields a handle to wait for it (or null to wait a frame).</summary>
    internal sealed class InventoryLeg : IScenario
    {
        private const double ArrivalSeconds = 90;
        private const double SettleSeconds = 10;
        private const double BailSeconds = 30;

        private readonly IReadOnlyList<uint> _skillClasses;
        private readonly Action<string> _log;
        private readonly IEnumerator<ActionHandle?> _script;
        private ActionHandle? _wait;
        private double _since;
        private IGameSessionView? _session;

        public InventoryLeg(IReadOnlyList<uint> skillClasses, Action<string> log)
        {
            _skillClasses = skillClasses;
            _log = log;
            _script = Script().GetEnumerator();
        }

        public string Name => "inventory-leg";

        public string Stage { get; private set; } = "start";

        public string? Failure { get; private set; }

        public double? WhereMaxMetres => null;

        public long Unmatched => 0;

        public bool FirstGrantInPack { get; private set; }

        public bool SecondGrantInPack { get; private set; }

        public ScenarioStatus Step(IGameSessionView session, double now)
        {
            ArgumentNullException.ThrowIfNull(session);
            _session = session;
            if (Stage == "start" && LiveMage.AlreadyInWorld(session) && LiveMage.WrongCharacter(session, Throwaway) is { } wrong)
            {
                // Nothing was sent (the shared session is the host's: no log off).
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

        private static void Require(bool ok, string what)
        {
            if (!ok)
            {
                throw new InvalidOperationException(what);
            }
        }

        private static void Confirmed(ActionHandle handle, string what) =>
            Require(handle.Outcome is { Kind: ActionOutcomeKind.Confirmed }, what + ": " + handle.Outcome?.Kind + (handle.Outcome?.Detail is { } d ? ": " + d : string.Empty));

        private static CharacterSlot? Find(IGameSessionView session, string name) =>
            session.State.Characters.Entries.OrderBy(c => c.PendingDeletion).ThenBy(c => c.Guid).Cast<CharacterSlot?>().FirstOrDefault(c => string.Equals(c!.Value.Name, name, StringComparison.OrdinalIgnoreCase));

        private IEnumerable<ActionHandle?> Script()
        {
            var s = _session ?? throw new InvalidOperationException("no session");
            if (LiveMage.AlreadyInWorld(s))
            {
                Stage = "running";
                _log("already in the world: the shared session's entry logged the character in");
            }
            else
            {
                Stage = "login";
                var login = s.Actions.Login(ActionSource.Host);
                yield return login;
                Confirmed(login, "login");

                Stage = "throwaway";
                var slot = Find(s, Throwaway);
                Require(slot is not { PendingDeletion: true }, $"'{Throwaway}' is pending deletion from an earlier run: purge it first (ModCharPurgeLiveTests does)");
                if (slot is null)
                {
                    var create = s.Actions.CreateCharacter(HeadlessCharacter.Intent(Throwaway, _skillClasses), ActionSource.Host);
                    yield return create;
                    Confirmed(create, "creating the throwaway");
                    slot = Find(s, Throwaway);
                    Require(slot is { PendingDeletion: false }, "creating the throwaway: the list does not show it");
                }

                Stage = "entering";
                var enter = s.Actions.EnterWorld(slot!.Value.Guid, ActionSource.Host);
                yield return enter;
                Confirmed(enter, "entering the world as " + Throwaway);
                Stage = "arriving";
                _since = s.Clock.Now;
                while (s.State.Self.Travel != SelfTravel.Present || !s.State.Self.Qualities.IsKnown)
                {
                    Require(s.Clock.Now - _since <= ArrivalSeconds, "arriving: the player was not unhidden with its qualities within " + ArrivalSeconds + " s");
                    yield return null;
                }
            }

            var inventory = s.State.Self.Inventory;
            Require(inventory.IsKnown, "the description's inventory lists were not applied");
            _log($"login: {inventory.Pack.Count} items in the pack, {inventory.SidePacks.Count} side packs, {inventory.Equipped.Count} equipped, complete lists {inventory.ListsComplete}, burden {inventory.Burden?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}");

            for (var round = 1; round <= 2; round++)
            {
                Stage = "granting-" + round;
                var before = inventory.Count;
                var grant = s.Tools.SpawnItem(273, 7, ActionSource.Host);
                yield return grant;
                Confirmed(grant, "the Pyreal grant " + round);
                var guid = (grant.Result ?? throw new InvalidOperationException("the grant gave no result")).Guid;
                _since = s.Clock.Now;
                // ACE sends the 0022 and the reply (UIQueue) before the item's CreateObject (SmartboxQueue, the next bundle: NetworkSession.cs:192-250), so the model holds the item first and its world
                // object follows: wait for both.
                while (!inventory.Contains(guid) || !s.State.Objects.TryGet(guid, out _))
                {
                    Require(s.Clock.Now - _since <= SettleSeconds, $"grant {round}: item {guid:X8} did not reach the inventory model with its world object within {SettleSeconds} s (in the model {inventory.Contains(guid)})");
                    yield return null;
                }

                var place = inventory.Find(guid);
                var held = s.State.Objects.TryGet(guid, out var item) && item.ContainerId == s.State.Self.Guid;
                _log($"grant {round}: item {guid:X8} {place.Kind} slot {place.Placement}, world object holds it: {held}, burden {inventory.Burden?.ToString(CultureInfo.InvariantCulture) ?? "(none)"}");
                var ok = place.Kind is InventoryPlaceKind.MainPack or InventoryPlaceKind.InSidePack && held && inventory.Count == before + 1;
                if (round == 1)
                {
                    FirstGrantInPack = ok;
                }
                else
                {
                    SecondGrantInPack = ok;
                }
            }

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
