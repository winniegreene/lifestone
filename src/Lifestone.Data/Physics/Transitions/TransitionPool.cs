using Lifestone.Data.Physics.World;

namespace Lifestone.Data.Physics.Transitions;

/// <summary>
/// The ten transitions retail keeps for the whole client (<c>CTransition::makeTransition</c>, 11.4186 0x0050B150, 11.6096
/// 0x0050BC20: a static array of 10 and a level counter; <c>CTransition::cleanupTransition</c>, 0x00509DC0, 0x0050A890,
/// lowers the level). Each <see cref="PhysicsWorld"/> has its own.
/// </summary>
/// <remarks>
/// Retail answers null when all ten are out, and the move then fails quietly; Lifestone fails loudly instead, since only
/// a transition that was never returned can exhaust the pool. Transitions come back in the reverse order they were
/// taken, as retail's level counter assumes. Not thread-safe (the world's thread only).
/// </remarks>
internal sealed class TransitionPool
{
    /// <summary>Retail's pool size.</summary>
    public const int Capacity = 10;

    private readonly Transition[] _transitions = new Transition[Capacity];

    /// <summary>A pool of transitions that move through <paramref name="cells"/>.</summary>
    public TransitionPool(IPhysicsCellSource cells, PhysicsCompat compat)
    {
        for (var i = 0; i < Capacity; i++)
        {
            _transitions[i] = new Transition(cells, compat);
        }
    }

    /// <summary>How many transitions are out (retail <c>transition_level</c>).</summary>
    public int InUse { get; private set; }

    /// <summary>Retail <c>CTransition::makeTransition</c>: the next transition, initialised.</summary>
    /// <exception cref="InvalidOperationException">All ten are out.</exception>
    public Transition Rent()
    {
        if (InUse >= Capacity)
        {
            throw new InvalidOperationException(
                $"All {Capacity} physics transitions are in use (CTransition::makeTransition): one was not returned to the pool.");
        }

        var transition = _transitions[InUse++];
        transition.Init();
        return transition;
    }

    /// <summary>Retail <c>CTransition::cleanupTransition</c>: gives back the transition taken last.</summary>
    /// <exception cref="InvalidOperationException"><paramref name="transition"/> is not the one taken last.</exception>
    public void Return(Transition transition)
    {
        if (InUse == 0 || !ReferenceEquals(_transitions[InUse - 1], transition))
        {
            throw new InvalidOperationException("Physics transitions must be returned in the reverse order they were taken (CTransition::cleanupTransition).");
        }

        InUse--;
    }
}
