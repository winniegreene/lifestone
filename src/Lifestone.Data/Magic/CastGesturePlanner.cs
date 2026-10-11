// Ported from ACEmulator/ACE@95ef426d Source/ACE.Server/WorldObjects/Player_Magic.cs (CreatePlayerSpell, DoSpellWords,
// DoWindupGestures, DoCastGesture, FinishCast; CastSpeed), Source/ACE.Server/WorldObjects/WorldObject_Networking.cs
// (EnqueueMotionMagic), Source/ACE.Server/Entity/SpellFormula.cs (IsScarab, WindupGestures, CastGesture) and
// Source/ACE.DatLoader/FileTypes/MotionTable.cs (GetDefaultMotion, GetAnimData, GetAnimationLength) (AGPL-3.0)
// [with changes: produces a timed list of CastSteps instead of ACE's ActionChain; non-PK path only; works on
// Lifestone's MotionTableData and IAnimationSource; a missing animation throws with its id].
using Lifestone.Data.Animation;

namespace Lifestone.Data.Magic;

/// <summary>Kind of a step of a <see cref="CastPlan"/>.</summary>
public enum CastStepKind
{
    /// <summary>A scarab's wind-up action (<c>MagicPowerUpNN</c>), sent with the Magic stance at <see cref="CastGesturePlanner.CastSpeed"/>.</summary>
    Windup,

    /// <summary>The talisman's cast gesture (a <c>Magic*</c> substate), sent at <see cref="CastGesturePlanner.CastSpeed"/>.</summary>
    CastGesture,

    /// <summary>The spell takes effect: caster/target PlayScripts are sent and projectiles launched.</summary>
    SpellEffects,

    /// <summary>Back to the Magic-stance Ready substate at speed 1.0 (ACE <c>FinishCast</c>).</summary>
    ReturnToReady,
}

/// <summary>
/// One server action of the casting sequence. Motion steps are what ACE broadcasts as an UpdateMotion
/// (<c>new Motion(MotionStance.Magic, motion, speed)</c>); a client applies them with
/// <see cref="MotionPlayer.ApplyInterpretedMotion"/>.
/// </summary>
/// <param name="Kind">What the step is.</param>
/// <param name="StartTime">Seconds after the cast starts (the spell words are spoken at 0).</param>
/// <param name="Style">Stance sent with the motion (Magic).</param>
/// <param name="Motion">Forward command (end-of-retail id), or 0 for <see cref="CastStepKind.SpellEffects"/>.</param>
/// <param name="Speed">Forward speed.</param>
/// <param name="ServerDuration">How long ACE waits before its next action (link length ÷ speed, ACE formula).</param>
/// <param name="ComponentId">Spell component that chose the gesture (scarab or talisman), 0 otherwise.</param>
public sealed record CastStep(CastStepKind Kind, double StartTime, uint Style, uint Motion, float Speed, double ServerDuration, uint ComponentId);

/// <summary>The full player casting sequence of one spell.</summary>
public sealed record CastPlan
{
    public required SpellInfo Spell { get; init; }

    /// <summary>The decrypted base formula (8 slots, 0 = empty).</summary>
    public required IReadOnlyList<uint> BaseFormula { get; init; }

    /// <summary>The formula for <see cref="AccountName"/> (tapers randomised; what the player actually burns).</summary>
    public required IReadOnlyList<uint> PlayerFormula { get; init; }

    public required string AccountName { get; init; }

    /// <summary>The chant (ACE HearSpeech text), spoken at t = 0.</summary>
    public required string Words { get; init; }

    public required IReadOnlyList<CastStep> Steps { get; init; }

    /// <summary>When the spell lands (start of the <see cref="CastStepKind.SpellEffects"/> step).</summary>
    public required double EffectTime { get; init; }

    /// <summary>PlayScript intensity ACE sends with the caster/target effect (<see cref="SpellFormulaRules.AceEffectIntensity"/>).</summary>
    public required float EffectIntensity { get; init; }

    /// <summary>When the caster may act again (ACE keeps the player busy for 1 s of recoil after <see cref="EffectTime"/>).</summary>
    public required double EndTime { get; init; }
}

/// <summary>
/// Builds the player cast sequence exactly as ACE's non-PK path does (<c>Player.CreatePlayerSpell</c> →
/// <c>DoSpellWords</c>, <c>DoWindupGestures</c>, <c>DoCastGesture</c>, then <c>DoCastSpell</c> and <c>FinishCast</c>;
/// <c>Source/ACE.Server/WorldObjects/Player_Magic.cs</c>, <c>WorldObject_Networking.EnqueueMotionMagic</c>, AGPL-3.0
/// @95ef426d). The retail <em>server</em> is not available; ACE's speeds come "from retail pcaps" (its comment), and the
/// client-side playback of each motion is retail (<see cref="MotionPlayer"/>).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The caster is assumed to be in Magic combat mode already (ACE refuses to cast otherwise).</item>
/// <item>Wind-ups: one per scarab of the base formula, in formula order, using the scarab's gesture; none for
/// <see cref="SpellFlags.FastCast"/> spells. Lead Scarab's gesture is the retail invalid command (0x80000000): ACE
/// still sends it, it lasts 0 s and the client's motion table ignores it.</item>
/// <item>Each motion's server duration is ACE <c>MotionTable.GetAnimationLength(Magic, motion)</c> ÷ speed, i.e. the
/// Ready → motion link with <c>(high − low) / |framerate|</c> per animation (<c>high = −1</c> counts all frames).</item>
/// <item>The cast gesture is the last component of the player formula (the talisman); a non-talisman last component
/// makes ACE fall back to Ready.</item>
/// </list>
/// </remarks>
public static class CastGesturePlanner
{
    /// <summary>ACE <c>Player.CastSpeed</c>: "from retail pcaps, player animation speed for windup / first half of cast gesture".</summary>
    public const float CastSpeed = 2.0f;

    /// <summary>ACE <c>FinishCast</c> (non-PK path): busy for 1 s after the spell ("TODO: get actual recoil timing").</summary>
    public const double RecoilSeconds = 1.0;

    /// <summary>Talisman component type (retail/ACE <c>SpellComponentsTable.Type.Talisman</c>).</summary>
    private const uint TalismanType = 5;

    public static CastPlan Plan(
        SpellInfo spell, SpellComponentTableData components, MotionTableData casterTable, IAnimationSource animations, string accountName = "")
    {
        ArgumentNullException.ThrowIfNull(spell);
        var playerFormula = SpellFormulaRules.CustomizeForAccount(spell.Formula, spell.FormulaVersion, accountName);
        var steps = new List<CastStep>();
        var t = 0.0;

        if ((spell.Flags & SpellFlags.FastCast) == 0)
        {
            foreach (var comp in spell.Components)
            {
                if (!IsAceScarab(comp))
                {
                    continue;
                }

                if (!components.Components.TryGetValue(comp, out var scarab))
                {
                    throw new KeyNotFoundException($"Spell {spell.Id}: scarab {comp} is not in the SpellComponentTable.");
                }

                var duration = AceAnimationLength(casterTable, animations, MotionCommands.Magic, scarab.GestureMotion) / CastSpeed;
                steps.Add(new CastStep(CastStepKind.Windup, t, MotionCommands.Magic, scarab.GestureMotion, CastSpeed, duration, comp));
                t += duration;
            }
        }

        var castGesture = MotionCommands.Ready;
        uint talismanId = 0;
        var last = playerFormula.LastOrDefault(c => c != 0);
        if (last != 0 && components.Components.TryGetValue(last, out var talisman) && (uint)talisman.Type == TalismanType)
        {
            castGesture = talisman.GestureMotion;
            talismanId = last;
        }

        var castDuration = AceAnimationLength(casterTable, animations, MotionCommands.Magic, castGesture) / CastSpeed;
        steps.Add(new CastStep(CastStepKind.CastGesture, t, MotionCommands.Magic, castGesture, CastSpeed, castDuration, talismanId));
        t += castDuration;

        var effectTime = t;
        steps.Add(new CastStep(CastStepKind.SpellEffects, t, MotionCommands.Magic, 0, 0f, 0.0, 0));
        steps.Add(new CastStep(CastStepKind.ReturnToReady, t, MotionCommands.Magic, MotionCommands.Ready, 1f, RecoilSeconds, 0));

        return new CastPlan
        {
            Spell = spell,
            BaseFormula = spell.Formula,
            PlayerFormula = playerFormula,
            AccountName = accountName,
            Words = components.GetSpellWords(spell.Formula),
            Steps = steps,
            EffectTime = effectTime,
            EffectIntensity = SpellFormulaRules.AceEffectIntensity(spell.Formula) ?? 1f,
            EndTime = effectTime + RecoilSeconds,
        };
    }

    /// <summary>ACE <c>SpellFormula.IsScarab</c>: the ids of its <c>Scarab</c> enum.</summary>
    public static bool IsAceScarab(uint componentId) => componentId is 1 or 2 or 3 or 4 or 5 or 6 or 110 or 112 or 192 or 193;

    /// <summary>
    /// ACE <c>DatLoader.FileTypes.MotionTable.GetAnimationLength(stance, motion, currentMotion = GetDefaultMotion(stance))</c>
    /// (<c>GetDefaultMotion</c> is <c>StyleDefaults[stance]</c>, else ACE's <c>MotionCommand.Invalid</c> = 0): see the
    /// overload taking the current motion. 0 when there is no link.
    /// </summary>
    public static double AceAnimationLength(MotionTableData table, IAnimationSource animations, uint stance, uint motion) =>
        AceAnimationLength(table, animations, stance, motion, table.StyleDefaults.GetValueOrDefault(stance));

    /// <summary>
    /// ACE <c>MotionTable.GetAnimationLength(stance, motion, currentMotion)</c> over <c>GetAnimData</c>: the link set
    /// <c>Links[(stance &lt;&lt; 16) | (current &amp; 0xFFFFF)]</c> must exist (else 0); its entry for
    /// <paramref name="motion"/>, else <c>Links[stance &lt;&lt; 16][motion]</c>, else 0. Sums
    /// <c>(high − low) / |framerate|</c> with <c>high = −1</c> (or past the end) meaning the frame count.
    /// </summary>
    public static double AceAnimationLength(MotionTableData table, IAnimationSource animations, uint stance, uint motion, uint currentMotion)
    {
        // ACE GetAnimData: no fallback when the outer (stance, current) key itself is missing.
        if (!table.Links.TryGetValue((stance << 16) | (currentMotion & 0xFFFFF), out var byMotion))
        {
            return 0.0;
        }

        if (!byMotion.TryGetValue(motion, out var data))
        {
            if (!table.Links.TryGetValue(stance << 16, out byMotion) || !byMotion.TryGetValue(motion, out data))
            {
                return 0.0;
            }
        }

        var length = 0.0f;
        foreach (var anim in data.Anims)
        {
            var frames = animations.GetAnimation(anim.AnimId)?.NumFrames
                ?? throw new KeyNotFoundException($"Animation 0x{anim.AnimId:X8} referenced by MotionTable 0x{table.Id:X8} is missing.");
            var high = anim.HighFrame == -1 || anim.HighFrame > frames ? frames : anim.HighFrame;
            length += (high - anim.LowFrame) / Math.Abs(anim.Framerate);
        }

        return length;
    }
}
