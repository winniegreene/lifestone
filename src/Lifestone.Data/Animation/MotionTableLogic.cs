namespace Lifestone.Data.Animation;

/// <summary>
/// Retail <c>CMotionTable</c> sequence resolution (acclient 11.4186 PORTAL/canim/CMTable.c):
/// <c>GetObjectSequence</c>, <c>get_link</c>, <c>is_allowed</c>, <c>re_modify</c>, <c>StopSequenceMotion</c>,
/// <c>StopObjectCompletely</c>, <c>SetDefaultState</c> and the helpers <c>add_motion</c>, <c>combine_motion</c>,
/// <c>subtract_motion</c>, <c>change_cycle_speed</c>, <c>same_sign</c>. Cross-checked with ACE
/// <c>Source/ACE.Server/Physics/Animation/MotionTable.cs</c> (AGPL-3.0, @95ef426d).
/// </summary>
/// <remarks>
/// Where ACE and retail differ this follows retail:
/// <list type="bullet">
/// <item>Style change: the new substate is <c>StyleDefaults[newStyle]</c> (ACE uses the old style's default).</item>
/// <item><c>same_sign(a, b)</c> treats 0 as positive (ACE compares <c>Math.Sign</c>, so 0 ≠ +).</item>
/// <item>The action branch's <c>num_anims</c> counts the trailing "back to substate" link (ACE counts the first link twice).</item>
/// </list>
/// A missing <c>style_defaults</c> entry leaves retail's local uninitialised; we use 0 there (as ACE does).
/// </remarks>
public static class MotionTableLogic
{
    private const float Epsilon = AcFrame.Epsilon;

    /// <summary>Retail <c>CMotionTable::DoObjectMotion</c> = <c>GetObjectSequence(..., stop_modifiers = 0)</c>.</summary>
    public static bool DoObjectMotion(MotionTableData table, uint motion, MotionState state, AnimSequence sequence, float speedMod, out uint numAnims) =>
        GetObjectSequence(table, motion, state, sequence, speedMod, out numAnims, stopModifiers: false);

    /// <summary>Retail <c>CMotionTable::StopObjectMotion</c> = <c>StopSequenceMotion</c>.</summary>
    public static bool StopObjectMotion(MotionTableData table, uint motion, float speed, MotionState state, AnimSequence sequence, out uint numAnims) =>
        StopSequenceMotion(table, motion, speed, state, sequence, out numAnims);

    /// <summary>Retail <c>CMotionTable::GetObjectSequence</c>.</summary>
    public static bool GetObjectSequence(
        MotionTableData table, uint motion, MotionState state, AnimSequence sequence, float speedMod, out uint numAnims, bool stopModifiers)
    {
        numAnims = 0;
        if (state.Style == 0 || state.Substate == 0)
        {
            return false;
        }

        var oldSubstate = state.Substate; // retail mtype2
        MotionData? link2 = null;
        var newSubstate = StyleDefault(table, state.Style);

        if (motion == newSubstate && !stopModifiers && (oldSubstate & MotionCommands.ModifierMask) != 0)
        {
            return true;
        }

        if ((motion & MotionCommands.StyleMask) != 0)
        {
            if (state.Style == motion)
            {
                return true;
            }

            MotionData? preLink = null;
            if (oldSubstate != newSubstate)
            {
                preLink = GetLink(table, state.Style, oldSubstate, state.SubstateMod, newSubstate, speedMod);
            }

            if (table.StyleDefaults.TryGetValue(motion, out var targetSubstate)
                && table.Cycles.TryGetValue(MotionTableData.Key(motion, targetSubstate), out var cycle))
            {
                if ((cycle.Bitfield & 1) != 0)
                {
                    state.ClearModifiers();
                }

                var link = GetLink(table, state.Style, newSubstate, state.SubstateMod, motion, speedMod);
                if (link is null && motion != state.Style)
                {
                    link = GetLink(table, state.Style, newSubstate, 1f, table.DefaultStyle, 1f);
                    var defaultStyleSubstate = StyleDefault(table, table.DefaultStyle);
                    link2 = GetLink(table, table.DefaultStyle, defaultStyleSubstate, 1f, motion, 1f);
                }

                sequence.ClearPhysics();
                sequence.RemoveCyclicAnims();
                AddMotion(sequence, preLink, speedMod);
                AddMotion(sequence, link, speedMod);
                AddMotion(sequence, link2, speedMod);
                AddMotion(sequence, cycle, speedMod);
                state.Substate = targetSubstate;
                state.Style = motion;
                state.SubstateMod = speedMod;
                ReModify(table, sequence, state);
                numAnims = (uint)(Count(preLink) + Count(link) + Count(link2) + cycle.Anims.Count - 1);
                return true;
            }
        }

        if ((motion & MotionCommands.SubStateMask) != 0)
        {
            var id = motion & 0xFFFFFF;
            if ((table.Cycles.TryGetValue((state.Style << 16) | id, out var cycle)
                    || table.Cycles.TryGetValue((table.DefaultStyle << 16) | id, out cycle))
                && IsAllowed(table, motion, cycle, state))
            {
                if (motion == oldSubstate && SameSign(speedMod, state.SubstateMod) && sequence.HasAnims)
                {
                    ChangeCycleSpeed(sequence, state.SubstateMod, speedMod);
                    SubtractMotion(sequence, cycle, state.SubstateMod);
                    CombineMotion(sequence, cycle, speedMod);
                    state.SubstateMod = speedMod;
                    return true;
                }

                if ((cycle.Bitfield & 1) != 0)
                {
                    state.ClearModifiers();
                }

                var link = GetLink(table, state.Style, state.Substate, state.SubstateMod, motion, speedMod);
                if (link is null || !SameSign(speedMod, state.SubstateMod))
                {
                    var styleDefault = StyleDefault(table, state.Style);
                    link = GetLink(table, state.Style, state.Substate, state.SubstateMod, styleDefault, 1f);
                    link2 = GetLink(table, state.Style, styleDefault, 1f, motion, speedMod);
                }

                sequence.ClearPhysics();
                sequence.RemoveCyclicAnims();
                if (link2 is not null)
                {
                    AddMotion(sequence, link, state.SubstateMod);
                    AddMotion(sequence, link2, speedMod);
                }
                else
                {
                    var linkSpeed = state.SubstateMod >= 0f || speedMod <= 0f ? speedMod : -speedMod;
                    AddMotion(sequence, link, linkSpeed);
                }

                AddMotion(sequence, cycle, speedMod);
                var previous = state.Substate;
                if (previous != motion && (previous & MotionCommands.ModifierMask) != 0
                    && StyleDefault(table, state.Style) != motion)
                {
                    state.AddModifierNoCheck(previous, state.SubstateMod);
                }

                state.SubstateMod = speedMod;
                state.Substate = motion;
                ReModify(table, sequence, state);
                numAnims = (uint)(Count(link) + Count(link2) + cycle.Anims.Count - 1);
                return true;
            }
        }

        if ((motion & MotionCommands.ActionMask) != 0)
        {
            var cycleKey = MotionTableData.Key(state.Style, oldSubstate);
            if (table.Cycles.TryGetValue(cycleKey, out var cycle))
            {
                var link = GetLink(table, state.Style, oldSubstate, state.SubstateMod, motion, speedMod);
                if (link is not null)
                {
                    state.AddAction(motion, speedMod);
                    sequence.ClearPhysics();
                    sequence.RemoveCyclicAnims();
                    AddMotion(sequence, link, speedMod);
                    AddMotion(sequence, cycle, state.SubstateMod);
                    ReModify(table, sequence, state);
                    numAnims = (uint)link.Anims.Count;
                    return true;
                }

                newSubstate = StyleDefault(table, state.Style);
                var toDefault = GetLink(table, state.Style, oldSubstate, state.SubstateMod, newSubstate, 1f);
                var actionLink = toDefault is null ? null : GetLink(table, state.Style, newSubstate, 1f, motion, speedMod);
                if (toDefault is not null && actionLink is not null && table.Cycles.TryGetValue(cycleKey, out var cycleAgain))
                {
                    var back = GetLink(table, state.Style, newSubstate, 1f, oldSubstate, state.SubstateMod);
                    state.AddAction(motion, speedMod);
                    sequence.ClearPhysics();
                    sequence.RemoveCyclicAnims();
                    AddMotion(sequence, toDefault, 1f);
                    AddMotion(sequence, actionLink, speedMod);
                    AddMotion(sequence, back, 1f);
                    AddMotion(sequence, cycleAgain, state.SubstateMod);
                    ReModify(table, sequence, state);
                    numAnims = (uint)(toDefault.Anims.Count + actionLink.Anims.Count + Count(back));
                    return true;
                }
            }
        }

        if ((motion & MotionCommands.ModifierMask) != 0)
        {
            var styleKey = state.Style << 16;
            if (table.Cycles.TryGetValue(styleKey | (oldSubstate & 0xFFFFFF), out var cycle) && (cycle.Bitfield & 1) == 0
                && (table.Modifiers.TryGetValue(styleKey | (motion & 0xFFFFFF), out var modifier)
                    || table.Modifiers.TryGetValue(motion & 0xFFFFFF, out modifier)))
            {
                if (state.AddModifier(motion, speedMod))
                {
                    CombineMotion(sequence, modifier, speedMod);
                    return true;
                }

                StopSequenceMotion(table, motion, 1f, state, sequence, out numAnims);
                if (state.AddModifier(motion, speedMod))
                {
                    CombineMotion(sequence, modifier, speedMod);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Retail <c>CMotionTable::get_link</c>.</summary>
    public static MotionData? GetLink(MotionTableData table, uint style, uint substate, float substateSpeed, uint motion, float speed)
    {
        if (speed < 0f || substateSpeed < 0f)
        {
            if (table.Links.TryGetValue(MotionTableData.Key(style, motion), out var byMotion) && byMotion.TryGetValue(substate, out var found))
            {
                return found;
            }

            if (table.StyleDefaults.TryGetValue(style, out var styleDefault)
                && table.Links.TryGetValue(MotionTableData.Key(style, substate), out var bySubstate))
            {
                return bySubstate.GetValueOrDefault(styleDefault);
            }
        }
        else
        {
            if (table.Links.TryGetValue(MotionTableData.Key(style, substate), out var bySubstate) && bySubstate.TryGetValue(motion, out var found))
            {
                return found;
            }

            if (table.Links.TryGetValue(style << 16, out var byStyle))
            {
                return byStyle.GetValueOrDefault(motion);
            }
        }

        return null;
    }

    /// <summary>Retail <c>CMotionTable::is_allowed</c>.</summary>
    public static bool IsAllowed(MotionTableData table, uint motion, MotionData? data, MotionState state)
    {
        if (data is null)
        {
            return false;
        }

        if ((data.Bitfield & 2) == 0 || motion == state.Substate)
        {
            return true;
        }

        return table.StyleDefaults.TryGetValue(state.Style, out var styleDefault) && styleDefault == state.Substate;
    }

    /// <summary>
    /// Retail <c>CMotionTable::re_modify</c>, including its quirk: each pass removes and re-applies the <em>current</em>
    /// list head while the copy shrinks, so with several modifiers the head is re-applied repeatedly.
    /// </summary>
    public static void ReModify(MotionTableData table, AnimSequence sequence, MotionState state)
    {
        if (state.Modifiers.Count == 0)
        {
            return;
        }

        var dummy = new MotionState(state);
        while (dummy.Modifiers.Count > 0)
        {
            var head = state.Modifiers[0];
            state.RemoveModifierAt(0);
            dummy.RemoveModifierAt(0);
            GetObjectSequence(table, head.Motion, state, sequence, head.SpeedMod, out _, stopModifiers: false);
        }
    }

    /// <summary>Retail <c>CMotionTable::StopSequenceMotion</c>.</summary>
    public static bool StopSequenceMotion(MotionTableData table, uint motion, float speed, MotionState state, AnimSequence sequence, out uint numAnims)
    {
        numAnims = 0;
        if ((motion & MotionCommands.SubStateMask) != 0 && motion == state.Substate)
        {
            GetObjectSequence(table, StyleDefault(table, state.Style), state, sequence, 1f, out numAnims, stopModifiers: true);
            return true;
        }

        if ((motion & MotionCommands.ModifierMask) == 0)
        {
            return false;
        }

        for (var i = 0; i < state.Modifiers.Count; i++)
        {
            var entry = state.Modifiers[i];
            if (entry.Motion != motion)
            {
                continue;
            }

            if (!table.Modifiers.TryGetValue((state.Style << 16) | (motion & 0xFFFFFF), out var data)
                && !table.Modifiers.TryGetValue(motion & 0xFFFFFF, out data))
            {
                return false;
            }

            SubtractMotion(sequence, data, entry.SpeedMod);
            state.RemoveModifierAt(i);
            return true;
        }

        return false;
    }

    /// <summary>Retail <c>CMotionTable::StopObjectCompletely</c>.</summary>
    public static bool StopObjectCompletely(MotionTableData table, MotionState state, AnimSequence sequence, out uint numAnims)
    {
        numAnims = 0;
        var any = false;
        while (state.Modifiers.Count > 0)
        {
            var head = state.Modifiers[0];
            var before = state.Modifiers.Count;
            if (StopSequenceMotion(table, head.Motion, head.SpeedMod, state, sequence, out numAnims))
            {
                any = true;
            }

            if (state.Modifiers.Count == before)
            {
                // Retail would loop forever on a modifier missing from the table; fail loudly instead.
                throw new InvalidOperationException(
                    $"MotionTable 0x{table.Id:X8}: modifier 0x{head.Motion:X8} cannot be stopped (no MotionData); retail would spin here.");
            }
        }

        return StopSequenceMotion(table, state.Substate, state.SubstateMod, state, sequence, out numAnims) || any;
    }

    /// <summary>Retail <c>CMotionTable::SetDefaultState</c>.</summary>
    public static bool SetDefaultState(MotionTableData table, MotionState state, AnimSequence sequence, out uint numAnims)
    {
        numAnims = 0;
        if (!table.StyleDefaults.TryGetValue(table.DefaultStyle, out var defaultSubstate))
        {
            return false;
        }

        state.ClearModifiers();
        state.ClearActions();
        if (!table.Cycles.TryGetValue(MotionTableData.Key(table.DefaultStyle, defaultSubstate), out var cycle))
        {
            return false;
        }

        state.Style = table.DefaultStyle;
        state.Substate = defaultSubstate;
        state.SubstateMod = 1f;
        numAnims = (uint)(cycle.Anims.Count - 1);
        sequence.ClearPhysics();
        sequence.ClearAnimations();
        AddMotion(sequence, cycle, state.SubstateMod);
        return true;
    }

    /// <summary>Retail <c>add_motion</c>: sets (not adds) velocity/omega and appends every animation scaled by the speed.</summary>
    public static void AddMotion(AnimSequence sequence, MotionData? data, float speedMod)
    {
        if (data is null)
        {
            return;
        }

        sequence.SetVelocity(data.Velocity * speedMod);
        sequence.SetOmega(data.Omega * speedMod);
        foreach (var anim in data.Anims)
        {
            sequence.AppendAnimation(anim.WithSpeed(speedMod));
        }
    }

    /// <summary>Retail <c>combine_motion</c>.</summary>
    public static void CombineMotion(AnimSequence sequence, MotionData? data, float speedMod)
    {
        if (data is not null)
        {
            sequence.CombinePhysics(data.Velocity * speedMod, data.Omega * speedMod);
        }
    }

    /// <summary>Retail <c>subtract_motion</c>.</summary>
    public static void SubtractMotion(AnimSequence sequence, MotionData? data, float speedMod)
    {
        if (data is not null)
        {
            sequence.SubtractPhysics(data.Velocity * speedMod, data.Omega * speedMod);
        }
    }

    /// <summary>Retail <c>change_cycle_speed</c>.</summary>
    public static void ChangeCycleSpeed(AnimSequence sequence, float oldSpeed, float newSpeed)
    {
        if (Math.Abs(oldSpeed) <= Epsilon)
        {
            if (Math.Abs(newSpeed) < Epsilon)
            {
                sequence.MultiplyCyclicAnimationFramerate(0f);
            }
        }
        else
        {
            sequence.MultiplyCyclicAnimationFramerate(newSpeed / oldSpeed);
        }
    }

    /// <summary>Retail <c>same_sign</c>: zero counts as positive.</summary>
    public static bool SameSign(float a, float b) => a < 0f ? b < 0f : b >= 0f;

    private static uint StyleDefault(MotionTableData table, uint style) => table.StyleDefaults.GetValueOrDefault(style);

    private static int Count(MotionData? data) => data?.Anims.Count ?? 0;
}
