namespace Lifestone.Data.World;

/// <summary>
/// Wrap-safe comparison of the sequence stamps the server puts on object updates. One shared rule instead of private
/// copies. Two counter widths exist: the 16-bit stamps of a physics object (instance, teleport, position, movement,
/// state, vector and server control) use <see cref="IsNewer"/>, <see cref="NewerEvent"/> and <see cref="NotOlder"/>;
/// the 15-bit action stamps of motion commands use <see cref="IsNewerAction"/>, never the 16-bit rule.
/// </summary>
/// <remarks>
/// <para>Re-expresses retail <c>CPhysicsObj::is_newer</c> and <c>CPhysicsObj::newer_event</c> (acclient 11.4186
/// 0x00451AD0 and 0x00451B10; function names cited, nothing copied): with the difference taken as a plain integer,
/// a distance of more than 0x7FFF means the counter wrapped, so the smaller value is the newer one; otherwise the larger
/// value is newer. Equal stamps are not newer. ACE's <c>PhysicsObj.is_newer</c> has the same rule.</para>
/// <para>At a distance of exactly 0x8000 the rule is not symmetric: 0x8000 is not newer than 0, and 0 is newer than
/// 0x8000. That is retail's behaviour and is kept.</para>
/// </remarks>
public static class Sequence16
{
    /// <summary>Half the stamp range: a larger distance means the counter wrapped (<c>abs(new - old) &gt; 0x7FFF</c>).</summary>
    public const int WrapDistance = 0x7FFF;

    /// <summary>
    /// The bits of a packed motion action word that hold the 15-bit server action stamp. Bit 15 is the autonomous flag
    /// (ACE <c>MotionItem</c>: <c>stamp = packed &amp; 0x7FFF</c>, <c>autonomous = packed &gt;&gt; 15</c>).
    /// </summary>
    public const ushort ActionStampMask = 0x7FFF;

    /// <summary>
    /// Half the 15-bit action stamp range: a larger distance means the action counter wrapped
    /// (<c>abs(new - old) &gt; 0x3FFF</c>).
    /// </summary>
    public const int ActionWrapDistance = 0x3FFF;

    /// <summary>
    /// Retail <c>CPhysicsObj::is_newer</c>: true when <paramref name="incoming"/> is strictly newer than
    /// <paramref name="stored"/>, counting 0xFFFF → 0 as a step forward.
    /// </summary>
    public static bool IsNewer(ushort stored, ushort incoming) =>
        Math.Abs(incoming - stored) > WrapDistance ? incoming < stored : stored < incoming;

    /// <summary>
    /// Retail <c>CPhysicsObj::newer_event</c>: when <paramref name="incoming"/> is newer than <paramref name="stored"/>
    /// (<see cref="IsNewer"/>), stores it and returns true; otherwise leaves <paramref name="stored"/> alone and returns
    /// false.
    /// </summary>
    public static bool NewerEvent(ref ushort stored, ushort incoming)
    {
        if (!IsNewer(stored, incoming))
        {
            return false;
        }

        stored = incoming;
        return true;
    }

    /// <summary>
    /// True when <paramref name="incoming"/> equals <paramref name="stored"/> or is newer: the "not older" test retail
    /// applies where a repeat of the current stamp is still accepted (for example the F751 teleport notice,
    /// <c>SmartBox</c>'s handling at SmartBox.c:463-490).
    /// </summary>
    public static bool NotOlder(ushort stored, ushort incoming) => incoming == stored || IsNewer(stored, incoming);

    /// <summary>
    /// The action stamp rule of retail <c>CMotionInterp::move_to_interpreted_state</c> (acclient 11.4186 0x005289C0;
    /// function name cited, nothing copied): true when the action stamp <paramref name="incoming"/> is strictly newer
    /// than the stored server action stamp <paramref name="stored"/>, counting 0x7FFF → 0 as a step forward.
    /// </summary>
    /// <remarks>
    /// <para>Both values are masked with <see cref="ActionStampMask"/> first, so a packed wire word (with the
    /// autonomous flag in bit 15) and a bare stamp compare the same. With the difference taken as a plain integer, a
    /// distance of more than 0x3FFF means the 15-bit counter wrapped, so the smaller value is the newer one; otherwise
    /// the larger value is newer. Equal stamps are not newer. ACE's <c>MotionInterp.move_to_interpreted_state</c> has
    /// the same rule.</para>
    /// <para>At a distance of exactly 0x4000 the rule is not symmetric, as in <see cref="IsNewer"/>: 0x4000 is not
    /// newer than 0, and 0 is newer than 0x4000.</para>
    /// <para>The 16-bit <see cref="IsNewer"/> must not be used for action stamps: it misses the 0x7FFF → 0 wrap, after
    /// which every later action on the object would be judged older and dropped.</para>
    /// </remarks>
    public static bool IsNewerAction(ushort stored, ushort incoming)
    {
        var storedStamp = stored & ActionStampMask;
        var incomingStamp = incoming & ActionStampMask;
        return Math.Abs(incomingStamp - storedStamp) > ActionWrapDistance
            ? incomingStamp < storedStamp
            : storedStamp < incomingStamp;
    }
}
