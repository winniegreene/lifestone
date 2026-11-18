using System.Globalization;
using Lifestone.Client.Diagnostics;

namespace Lifestone.Client.Session.Scenarios.Journey;

/// <summary>
/// M6 14 the Zefir: the travel leg's side of the fight-back's Life Magic. M6 16 Q7: the life strike itself moved with the fight-back into the skirmish (<c>Skirmish.LifeStrike.cs</c>); the leg keeps its named read of a creature kind
/// (<see cref="Kinds"/>, M6 16 E1e) and shows the skirmish's targets whose bolts came to nothing (<see cref="BoltBlocked"/>).
/// </summary>
internal abstract partial class TravelLeg
{
    private LegKindRead? _kinds;

    /// <summary>M6 16 E1e: the leg's read of a creature kind's maximum health and Magic Defense, naming once a kind what the bestiary does not hold (<see cref="LegKindRead"/>).</summary>
    internal LegKindRead Kinds => _kinds ??= new LegKindRead("fight-back", text => Context.Log.Warn("journey", string.Create(CultureInfo.InvariantCulture, $"[live {Name} t={S.Clock.Now:0.0}] {text}")));

    /// <summary>The attackers whose bolts came to nothing on this leg (the tests read it).</summary>
    internal IReadOnlyCollection<uint> BoltBlocked => Skirmish.BoltBlocked;
}
