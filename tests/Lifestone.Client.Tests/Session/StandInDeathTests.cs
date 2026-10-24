using System.Collections.Immutable;
using System.Numerics;
using Lifestone.Client.Navigation;
using Lifestone.Client.Session;
using Lifestone.Client.Session.Contracts;
using Lifestone.Client.Session.Mage;
using Lifestone.Client.Session.Scenarios;
using Lifestone.Client.Session.StandIn;
using Lifestone.Net.Messages;
using Lifestone.Tests.Shared;

namespace Lifestone.Client.Tests.Session;

/// <summary>
/// M3 3.2 db: the stand-in's death (<c>StandInWorld.Death.cs</c>). M4 4.1 t2 (D23, D26) retired the tusker that attacked and killed the player: the stand-in holds no creature, so the only death is a suicide, which plays the order of
/// <c>Player_Death.cs</c> (the Dead motion, the health and NumDeaths, DeathLevel and VitaeCpPool, the vitae, the purge, then the corpse and the teleport after the Dead animation plus a second, the stance, the protection and the vitals at 75%
/// three seconds later), five lines three seconds apart. The lifestone binds and protects, the vitae deepens, the pack loses items at a high level and the favourite-spell bars are kept. s0's <see cref="DeathTally"/> reads the same
/// messages, so the sentences are checked against it. The blows, the defender notices and the death by a blow were deleted with the creature (proved live by the bsd-death leg). Nothing here is ACE: the offline stand-in is proved against
/// ACE's code, the live legs prove ACE.
/// </summary>
public sealed class StandInDeathTests
{
    private const uint Lifestone = 0x80000C01;

    private static StandInVisit Visit(uint health = 52, int level = 30, StandInPlayerStats? stats = null) => new()
    {
        EnterAfter = 0.2,
        UnhideAfter = 1.0,
        Stats = stats ?? new StandInPlayerStats { Health = health, Level = level },
    };

    private sealed class Rig : IDisposable
    {
        public Rig(uint health = 52, int level = 30, StandInPlayerStats? stats = null)
        {
            Cast = new CastRig(Visit(health, level, stats));
            Tally = new DeathTally();
            Tally.Start(Cast.Session);
            Server = StandInSessions.Server(Cast.Session.Reducer);
            Cast.Begin();
        }

        public CastRig Cast { get; }

        public DeathTally Tally { get; }

        public StandInServer Server { get; }

        public StandInWorld World => Cast.World;

        public GameSession Session => Cast.Session;

        public void Run(double seconds) => Cast.Run(seconds);

        public string[] Tags => Cast.Tags;

        public void PlaceLifestone(Vector3 offset)
        {
            Assert.NotNull(Server.PlaceNear(StandInLifestone.Make(Lifestone), offset));
            Run(0.5);
            Assert.True(Session.State.Objects.Contains(Lifestone));
        }

        public void Bind()
        {
            var use = Session.Actions.Use(Lifestone, ActionSource.Host);
            Run(5.0);
            Assert.True(use.IsCompleted, use.Summary);
            Assert.Equal(ActionOutcomeKind.Confirmed, use.Outcome?.Kind);
            Assert.True(Server.Bound);
        }

        public IEnumerable<string> Lines(uint chatType) => Cast.Chat.Where(l => l.ChatType == chatType).Select(l => l.Text);

        public void Dispose()
        {
            Tally.Dispose();
            Cast.Dispose();
        }
    }

    private static int IndexOf(string[] tags, string tag, int from = 0) => Array.IndexOf(tags, tag, from);

    // --- the player's death -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // --- the suicide --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // --- the lifestone ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // --- the items ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    // --- the bars -----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_teleport_goes_to_the_lifestone_the_player_used_and_the_protection_there_ends_after_a_minute_with_the_line()
    {
        using var rig = new Rig(health: 5);
        rig.PlaceLifestone(new Vector3(1f, 0f, 0f));
        var boundAt = rig.Session.State.Self.Position!.Value;
        rig.Bind();
        Assert.Equal(StandInWorld.PlaceholderUseMessage, rig.Lines(FightTally.MagicChatType).Last(l => l.StartsWith("You have bound", StringComparison.Ordinal)));
        var suicide = rig.Session.Actions.Suicide(ActionSource.Host); // M4 4.1 t2: the stand-in holds no monster, so a suicide is the one death
        rig.Run(35.0);
        Assert.True(suicide.IsCompleted, suicide.Summary);

        Assert.Equal(1, rig.World.PlayerDeaths);
        Assert.True(rig.Tally.HasArrival);
        Assert.Equal(boundAt.CellId, rig.Tally.ArrivalCell);
        Assert.Equal(boundAt.Origin.X, rig.Tally.ArrivalFrame.Origin.X, 0.01);
        Assert.Equal(DeathRespawn.Lifestone, DeathLegVerdict.ClassifyRespawn(rig.Tally, rig.Session.State.Objects));
        Assert.True(rig.Server.UnderProtection);

        // A minute after the arrival the protection ends with the Magic line.
        for (var second = 0; second < 70 && !rig.Lines(FightTally.MagicChatType).Contains("You're no longer protected by the Lifestone's magic!"); second++)
        {
            rig.Run(1.0);
        }

        Assert.False(rig.Server.UnderProtection);
        Assert.Contains("You're no longer protected by the Lifestone's magic!", rig.Lines(FightTally.MagicChatType));
    }

    [Fact]
    public void AddSpellFavorite_keeps_the_bars_for_the_next_description_as_ACE_adds_them_known_spells_only_once_at_a_clamped_index()
    {
        using var rig = new Rig();
        rig.Cast.Learn(27, 81, 82);
        var empty = new PlayerDescriptionMsg();
        Assert.Same(empty, rig.World.WithBars(empty)); // nothing added: the description is as it was

        rig.Session.Actions.AddSpellFavorite(27, 0, 1, ActionSource.Host);
        rig.Session.Actions.AddSpellFavorite(81, 5, 1, ActionSource.Host); // past the end: appended
        rig.Session.Actions.AddSpellFavorite(82, 0, 1, ActionSource.Host); // inserted first
        rig.Session.Actions.AddSpellFavorite(27, 2, 1, ActionSource.Host); // already on the bar: stays where it is
        rig.Session.Actions.AddSpellFavorite(999, 0, 1, ActionSource.Host); // not known: ignored
        rig.Session.Actions.AddSpellFavorite(27, 0, 0, ActionSource.Host);
        rig.Run(1.0);

        var bars = rig.World.WithBars(empty).SpellBars;
        Assert.Equal(2, bars.Length);
        Assert.Equal<uint>([27], bars[0]);
        Assert.Equal<uint>([82, 27, 81], bars[1]);
    }

    [Fact]
    public void RemoveSpellFavorite_takes_a_spell_off_the_bar_as_ACE_does_and_ignores_one_that_is_not_there()
    {
        using var rig = new Rig();
        rig.Cast.Learn(27, 81, 82);
        rig.Session.Actions.AddSpellFavorite(27, 0, 1, ActionSource.Host);
        rig.Session.Actions.AddSpellFavorite(81, 1, 1, ActionSource.Host);
        rig.Session.Actions.AddSpellFavorite(82, 2, 1, ActionSource.Host);
        rig.Session.Actions.RemoveSpellFavorite(81, 1, ActionSource.Host);
        rig.Session.Actions.RemoveSpellFavorite(999, 1, ActionSource.Host); // not on the bar
        rig.Session.Actions.RemoveSpellFavorite(27, 0, ActionSource.Host); // not on that bar
        rig.Run(1.0);

        var bars = rig.World.WithBars(new PlayerDescriptionMsg()).SpellBars;
        Assert.Equal<uint>([27, 82], bars[1]);
    }
}
