namespace Lifestone.Data.Animation;

/// <summary>
/// Retail <c>MotionTableManager</c> (acclient 11.4186 PORTAL/cphysobj/CMTableManager.c): owns the
/// <see cref="MotionState"/>, applies movements through <see cref="MotionTableLogic"/>, and keeps the queue of pending
/// motions whose animations have not finished. Its <c>remove_redundant_links</c> trims link animations from the
/// <see cref="AnimSequence"/> when a motion is re-issued while an earlier copy is still pending, so it affects the pose.
/// </summary>
public sealed class MotionTableManager(MotionTableData table)
{
    /// <summary>Retail movement types handled by <c>MotionTableManager::PerformMovement</c>.</summary>
    public enum MovementType
    {
        /// <summary><c>mvs.type == 2</c>: <c>CMotionTable::DoObjectMotion</c>.</summary>
        InterpretedCommand = 2,

        /// <summary><c>mvs.type == 4</c>: <c>CMotionTable::StopObjectMotion</c>.</summary>
        StopInterpretedCommand = 4,

        /// <summary><c>mvs.type == 5</c>: <c>CMotionTable::StopObjectCompletely</c>.</summary>
        StopCompletely = 5,
    }

    private sealed class PendingMotion(uint motion, uint numAnims)
    {
        public uint Motion { get; } = motion;

        public uint NumAnims { get; set; } = numAnims;
    }

    private readonly LinkedList<PendingMotion> _pending = new();
    private int _animationCounter;

    public MotionTableData Table { get; } = table;

    public MotionState State { get; } = new();

    /// <summary>Raised for each completed motion (retail <c>CPhysicsObj::MotionDone(motion, success)</c>).</summary>
    public event Action<uint, bool>? MotionDone;

    /// <summary>Pending (motion, remaining animation count) pairs, oldest first.</summary>
    public IEnumerable<(uint Motion, uint NumAnims)> Pending => _pending.Select(p => (p.Motion, p.NumAnims));

    /// <summary>Retail <c>initialize_state</c>: default style/substate, queued as a Ready motion.</summary>
    public void InitializeState(AnimSequence sequence)
    {
        MotionTableLogic.SetDefaultState(Table, State, sequence, out var numAnims);
        AddToQueue(MotionCommands.Ready, numAnims, sequence);
    }

    /// <summary>Retail <c>PerformMovement</c>; returns true on success (retail error 67 otherwise).</summary>
    public bool PerformMovement(MovementType type, uint motion, float speed, AnimSequence sequence)
    {
        switch (type)
        {
            case MovementType.InterpretedCommand:
                if (MotionTableLogic.DoObjectMotion(Table, motion, State, sequence, speed, out var numAnims))
                {
                    AddToQueue(motion, numAnims, sequence);
                    return true;
                }

                return false;
            case MovementType.StopInterpretedCommand:
                if (MotionTableLogic.StopObjectMotion(Table, motion, speed, State, sequence, out numAnims))
                {
                    AddToQueue(MotionCommands.Ready, numAnims, sequence);
                    return true;
                }

                return false;
            case MovementType.StopCompletely:
                MotionTableLogic.StopObjectCompletely(Table, State, sequence, out numAnims);
                AddToQueue(MotionCommands.Ready, numAnims, sequence);
                return true;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Retail PerformMovement handles types 2, 4 and 5 only.");
        }
    }

    /// <summary>Retail <c>AnimationDone</c>: called for every animation-done hook of the sequence.</summary>
    public void AnimationDone(bool success)
    {
        if (_pending.First is null)
        {
            return;
        }

        _animationCounter++;
        while (_pending.First is { } head)
        {
            if (head.Value.NumAnims > (uint)_animationCounter)
            {
                break;
            }

            Complete(head, success);
            _animationCounter -= (int)head.Value.NumAnims;
        }

        if (_animationCounter != 0 && _pending.First is null)
        {
            _animationCounter = 0;
        }
    }

    /// <summary>Retail <c>CheckForCompletedMotions</c>: completes queued motions that have no animations.</summary>
    public void CheckForCompletedMotions()
    {
        while (_pending.First is { } head && head.Value.NumAnims == 0)
        {
            Complete(head, success: true);
        }
    }

    private void Complete(LinkedListNode<PendingMotion> node, bool success)
    {
        if ((node.Value.Motion & MotionCommands.ActionMask) != 0)
        {
            State.RemoveActionHead();
        }

        _pending.Remove(node);
        MotionDone?.Invoke(node.Value.Motion, success);
    }

    /// <summary>Retail <c>add_to_queue</c>.</summary>
    private void AddToQueue(uint motion, uint numAnims, AnimSequence sequence)
    {
        _pending.AddLast(new PendingMotion(motion, numAnims));
        RemoveRedundantLinks(sequence);
    }

    /// <summary>Retail <c>remove_redundant_links</c>.</summary>
    private void RemoveRedundantLinks(AnimSequence sequence)
    {
        var last = _pending.Last;
        while (last is not null && last.Value.NumAnims == 0)
        {
            last = last.Previous;
        }

        if (last is null)
        {
            return;
        }

        var motion = last.Value.Motion;
        if ((motion & MotionCommands.SubStateMask) != 0 && (motion & MotionCommands.ModifierMask) == 0)
        {
            for (var node = last.Previous; node is not null; node = node.Previous)
            {
                var other = node.Value.Motion;
                if (other == motion && node.Value.NumAnims != 0)
                {
                    TruncateAnimationList(node, sequence);
                    return;
                }

                // Keep walking only past finished nodes and plain substates (no style/modifier/action bits).
                if (node.Value.NumAnims != 0 && (other & 0xB0000000) != 0)
                {
                    return;
                }
            }
        }
        else if ((motion & MotionCommands.StyleMask) != 0)
        {
            for (var node = last.Previous; node is not null; node = node.Previous)
            {
                var other = node.Value.Motion;
                if (other == motion)
                {
                    TruncateAnimationList(node, sequence);
                    return;
                }

                if (node.Value.NumAnims != 0 && (other & 0x70000000) != 0)
                {
                    return;
                }
            }
        }
    }

    /// <summary>Retail <c>truncate_animation_list</c>: zeroes the counts after <paramref name="node"/> and drops that many links.</summary>
    private void TruncateAnimationList(LinkedListNode<PendingMotion> node, AnimSequence sequence)
    {
        uint removed = 0;
        for (var current = _pending.Last; current is not null; current = current.Previous)
        {
            if (current == node)
            {
                sequence.RemoveLinkAnimations(removed);
                return;
            }

            removed += current.Value.NumAnims;
            current.Value.NumAnims = 0;
        }
    }
}
