// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/Physics/Animation/MotionTable.cs, MotionState.cs and
// Sequence.cs (AGPL-3.0) [with changes: reduced to the anim-list/physics bookkeeping of GetObjectSequence and
// SetDefaultState; works on Lifestone's MotionTableData; ACE's Math.Sign speed comparison and its style-change
// substate choice are kept as ACE has them, so tests can show where retail differs].

using System.Numerics;
using Lifestone.Data.Animation;

namespace Lifestone.Data.Tests.Animation;

/// <summary>
/// An independent oracle for motion-table resolution: ACE's server-side port of <c>CMotionTable</c>. It only records
/// what <c>add_motion</c> appends (anim id, frame range, framerate) and the resulting velocity/omega.
/// </summary>
internal sealed class AceMotionOracle(MotionTableData table)
{
    internal sealed class State
    {
        public uint Style;
        public uint Substate;
        public float SubstateMod = 1f;
        public LinkedList<(uint Id, float Speed)> Modifiers = new();
        public LinkedList<(uint Id, float Speed)> Actions = new();
    }

    internal sealed class Seq
    {
        public readonly List<AnimDataRef> Anims = []; // everything appended (links and cycles) since the last clear
        public int FirstCyclic = -1;
        public Vector3 Velocity;
        public Vector3 Omega;

        public bool HasAnims => Anims.Count > 0;

        public void Append(AnimDataRef a)
        {
            Anims.Add(a);
            FirstCyclic = Anims.Count - 1;
        }

        public void RemoveCyclic()
        {
            if (FirstCyclic >= 0)
            {
                Anims.RemoveRange(FirstCyclic, Anims.Count - FirstCyclic);
            }

            FirstCyclic = Anims.Count - 1;
        }
    }

    public bool SetDefaultState(State state, Seq seq)
    {
        if (!table.StyleDefaults.TryGetValue(table.DefaultStyle, out var defaultSubstate))
        {
            return false;
        }

        state.Modifiers.Clear();
        state.Actions.Clear();
        if (!table.Cycles.TryGetValue((table.DefaultStyle << 16) | (defaultSubstate & 0xFFFFFF), out var data))
        {
            return false;
        }

        state.Style = table.DefaultStyle;
        state.Substate = defaultSubstate;
        state.SubstateMod = 1f;
        seq.Velocity = Vector3.Zero;
        seq.Omega = Vector3.Zero;
        seq.Anims.Clear();
        seq.FirstCyclic = -1;
        AddMotion(seq, data, 1f);
        return true;
    }

    public bool GetObjectSequence(uint motion, State s, Seq seq, float speedMod, bool stopModifiers = false)
    {
        if (s.Style == 0 || s.Substate == 0)
        {
            return false;
        }

        MotionData? motionData = null;
        MotionData? motionData2 = null;
        table.StyleDefaults.TryGetValue(s.Style, out var substate);
        if (motion == substate && !stopModifiers && (s.Substate & 0x20000000) != 0)
        {
            return true;
        }

        if ((motion & 0x80000000) != 0)
        {
            if (s.Style == motion)
            {
                return true;
            }

            if (substate != s.Substate)
            {
                motionData = GetLink(s.Style, s.Substate, s.SubstateMod, substate, speedMod);
            }

            if (substate != 0 && table.Cycles.TryGetValue((motion << 16) | (substate & 0xFFFFFF), out var cycles))
            {
                if ((cycles.Bitfield & 1) != 0)
                {
                    s.Modifiers.Clear();
                }

                var link = GetLink(s.Style, substate, s.SubstateMod, motion, speedMod);
                if (link is null && s.Style != motion)
                {
                    link = GetLink(s.Style, substate, 1f, table.DefaultStyle, 1f);
                    table.StyleDefaults.TryGetValue(table.DefaultStyle, out var defaultStyle);
                    motionData2 = GetLink(table.DefaultStyle, defaultStyle, 1f, motion, 1f);
                }

                ClearPhysics(seq);
                seq.RemoveCyclic();
                AddMotion(seq, motionData, speedMod);
                AddMotion(seq, link, speedMod);
                AddMotion(seq, motionData2, speedMod);
                AddMotion(seq, cycles, speedMod);
                s.Substate = substate; // ACE: the OLD style's default (retail: the new style's)
                s.Style = motion;
                s.SubstateMod = speedMod;
                return true;
            }
        }

        if ((motion & 0x40000000) != 0)
        {
            var motionId = motion & 0xFFFFFF;
            if (!table.Cycles.TryGetValue((s.Style << 16) | motionId, out motionData))
            {
                table.Cycles.TryGetValue((table.DefaultStyle << 16) | motionId, out motionData);
            }

            if (motionData is not null && IsAllowed(motion, motionData, s))
            {
                if (motion == s.Substate && seq.HasAnims && Math.Sign(speedMod) == Math.Sign(s.SubstateMod))
                {
                    seq.Velocity -= motionData.Velocity * s.SubstateMod;
                    seq.Omega -= motionData.Omega * s.SubstateMod;
                    seq.Velocity += motionData.Velocity * speedMod;
                    seq.Omega += motionData.Omega * speedMod;
                    s.SubstateMod = speedMod;
                    return true;
                }

                if ((motionData.Bitfield & 1) != 0)
                {
                    s.Modifiers.Clear();
                }

                var link = GetLink(s.Style, s.Substate, s.SubstateMod, motion, speedMod);
                if (link is null || Math.Sign(speedMod) != Math.Sign(s.SubstateMod))
                {
                    table.StyleDefaults.TryGetValue(s.Style, out var defaultMotion);
                    link = GetLink(s.Style, s.Substate, s.SubstateMod, defaultMotion, 1f);
                    motionData2 = GetLink(s.Style, defaultMotion, 1f, motion, speedMod);
                }

                ClearPhysics(seq);
                seq.RemoveCyclic();
                if (motionData2 is not null)
                {
                    AddMotion(seq, link, s.SubstateMod);
                    AddMotion(seq, motionData2, speedMod);
                }
                else
                {
                    var newSpeedMod = speedMod;
                    if (s.SubstateMod < 0f && speedMod > 0f)
                    {
                        newSpeedMod *= -1f;
                    }

                    AddMotion(seq, link, newSpeedMod);
                }

                AddMotion(seq, motionData, speedMod);
                s.SubstateMod = speedMod;
                s.Substate = motion;
                return true;
            }
        }

        if ((motion & 0x10000000) != 0)
        {
            var cycleKey = (s.Style << 16) | (s.Substate & 0xFFFFFF);
            if (table.Cycles.TryGetValue(cycleKey, out motionData))
            {
                var link = GetLink(s.Style, s.Substate, s.SubstateMod, motion, speedMod);
                if (link is not null)
                {
                    s.Actions.AddLast((motion, speedMod));
                    ClearPhysics(seq);
                    seq.RemoveCyclic();
                    AddMotion(seq, link, speedMod);
                    AddMotion(seq, motionData, s.SubstateMod);
                    return true;
                }
            }
        }

        return false;
    }

    private MotionData? GetLink(uint style, uint substate, float substateSpeed, uint motion, float speed)
    {
        if (speed < 0f || substateSpeed < 0f)
        {
            if (table.Links.TryGetValue((style << 16) | (motion & 0xFFFFFF), out var link) && link.TryGetValue(substate, out var result))
            {
                return result;
            }

            if (table.StyleDefaults.TryGetValue(style, out var defaultMotion) && table.Links.TryGetValue((style << 16) | (substate & 0xFFFFFF), out var sublink))
            {
                sublink.TryGetValue(defaultMotion, out var r2);
                return r2;
            }
        }
        else
        {
            if (table.Links.TryGetValue((style << 16) | (substate & 0xFFFFFF), out var link) && link.TryGetValue(motion, out var result))
            {
                return result;
            }

            if (table.Links.TryGetValue(style << 16, out var sublink))
            {
                sublink.TryGetValue(motion, out var r2);
                return r2;
            }
        }

        return null;
    }

    private bool IsAllowed(uint motion, MotionData data, State state)
    {
        if ((data.Bitfield & 2) == 0 || motion == state.Substate)
        {
            return true;
        }

        table.StyleDefaults.TryGetValue(state.Style, out var style);
        return style == state.Substate;
    }

    private static void ClearPhysics(Seq seq)
    {
        seq.Velocity = Vector3.Zero;
        seq.Omega = Vector3.Zero;
    }

    private static void AddMotion(Seq seq, MotionData? data, float speed)
    {
        if (data is null)
        {
            return;
        }

        seq.Velocity = data.Velocity * speed;
        seq.Omega = data.Omega * speed;
        foreach (var anim in data.Anims)
        {
            seq.Append(anim with { Framerate = anim.Framerate * speed });
        }
    }
}
