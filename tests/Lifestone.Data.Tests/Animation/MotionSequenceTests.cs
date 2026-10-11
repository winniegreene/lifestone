using System.Numerics;
using Lifestone.Data.Animation;

namespace Lifestone.Data.Tests.Animation;

/// <summary>
/// Motion-table resolution on the human MotionTable (0x09000001, from CharGen) against the ACE-ported oracle
/// <see cref="AceMotionOracle"/>, plus retail-specific facts.
/// </summary>
public sealed class MotionSequenceTests(DatSetFixture fixture) : IClassFixture<DatSetFixture>
{
    private const uint HumanMotionTable = 0x09000001;

    private DatAnimationSource Source => _source ??= new DatAnimationSource(fixture.Dats);

    private DatAnimationSource? _source;

    public static TheoryData<uint[]> CommandSequences => new()
    {
        new[] { MotionCommands.Ready },
        new[] { MotionCommands.WalkForward },
        new[] { MotionCommands.RunForward },
        new[] { MotionCommands.WalkForward, MotionCommands.Ready },
        new[] { MotionCommands.Magic },
        new[] { MotionCommands.Magic, MotionCommands.MagicPowerUp01 },
        new[] { MotionCommands.Magic, MotionCommands.MagicPowerUp10 },
        new[] { MotionCommands.Magic, MotionCommands.MagicPowerUp04, MotionCommands.MagicPowerUp04 },
        new[] { MotionCommands.Magic, MotionCommands.MagicSelfHeart },
        new[] { MotionCommands.Magic, MotionCommands.MagicPowerUp10, MotionCommands.MagicRecoilMissile, MotionCommands.Ready },
        new[] { MotionCommands.Magic, MotionCommands.MagicThrowMissile, MotionCommands.Ready },
        new[] { MotionCommands.Magic, MotionCommands.CastSpell },
        new[] { MotionCommands.Magic, MotionCommands.NonCombat },
    };

    [RequiresDatsTheory]
    [MemberData(nameof(CommandSequences))]
    public void Human_sequences_match_the_ACE_oracle(uint[] commands)
    {
        var table = Source.GetMotionTable(HumanMotionTable)!;
        var oracle = new AceMotionOracle(table);
        var oState = new AceMotionOracle.State();
        var oSeq = new AceMotionOracle.Seq();
        Assert.True(oracle.SetDefaultState(oState, oSeq));

        var state = new MotionState();
        var sequence = new AnimSequence(Source);
        Assert.True(MotionTableLogic.SetDefaultState(table, state, sequence, out _));

        foreach (var command in commands)
        {
            var expected = oracle.GetObjectSequence(command, oState, oSeq, 1f);
            var actual = MotionTableLogic.DoObjectMotion(table, command, state, sequence, 1f, out _);
            Assert.Equal(expected, actual);
            Assert.Equal(oState.Style, state.Style);
            Assert.Equal(oState.Substate, state.Substate); // every human style defaults to Ready, so ACE's style-change shortcut agrees here
            Assert.Equal(oSeq.Velocity, sequence.Velocity);
            Assert.Equal(oSeq.Omega, sequence.Omega);
        }

        var expectedNodes = oSeq.Anims.Select(a => Clamp(a)).ToArray();
        var actualNodes = sequence.Nodes.Select(n => (n.AnimId, n.LowFrame, n.HighFrame, n.Framerate)).ToArray();
        Assert.Equal(expectedNodes, actualNodes);
    }

    [RequiresDatsFact]
    public void Default_state_is_NonCombat_Ready_cycle()
    {
        var table = Source.GetMotionTable(HumanMotionTable)!;
        Assert.Equal(MotionCommands.NonCombat, table.DefaultStyle);
        var player = new MotionPlayer(table, Source);
        Assert.Equal(MotionCommands.NonCombat, player.State.Style);
        Assert.Equal(MotionCommands.Ready, player.State.Substate);
        var node = Assert.Single(player.Sequence.Nodes);
        Assert.Equal(0x03000001u, node.AnimId); // ACE Sequence.PlayerIdleAnims: NonCombat idle
        Assert.Equal(30f, node.Framerate);
        Assert.Same(player.Sequence.FirstCyclic!.Value, node);
    }

    [RequiresDatsFact]
    public void Entering_Magic_stance_plays_the_style_link_then_the_Magic_idle()
    {
        var table = Source.GetMotionTable(HumanMotionTable)!;
        var player = new MotionPlayer(table, Source);
        player.ApplyInterpretedMotion(MotionCommands.Magic, MotionCommands.Ready, 1f);
        Assert.Equal(MotionCommands.Magic, player.State.Style);
        Assert.Equal(MotionCommands.Ready, player.State.Substate);

        // remove_cyclic_anims drops the NonCombat idle (it was current and cyclic); then the style link
        // Links[NonCombat<<16][Magic] = 0x03000598 @48 (style-only fallback of get_link) and the Magic Ready cycle
        // 0x0300076C (ACE PlayerIdleAnims: Magic).
        var nodes = player.Sequence.Nodes.Select(n => (n.AnimId, n.Framerate)).ToArray();
        Assert.Equal(new[] { (0x03000598u, 48f), (0x0300076Cu, 30f) }, nodes);

        for (var i = 0; i < 120; i++)
        {
            player.Update(1.0 / 30.0);
        }

        Assert.Equal(0x0300076Cu, player.Sequence.CurrentNode!.Value.AnimId);
        Assert.Single(player.Sequence.Nodes);
    }

    [RequiresDatsFact]
    public void Windup_action_plays_link_then_returns_to_the_cycle_and_is_dequeued()
    {
        var table = Source.GetMotionTable(HumanMotionTable)!;
        var player = new MotionPlayer(table, Source);
        player.ApplyInterpretedMotion(MotionCommands.Magic, MotionCommands.Ready, 1f);
        for (var i = 0; i < 60; i++)
        {
            player.Update(1.0 / 30.0);
        }

        player.ApplyInterpretedMotion(MotionCommands.Magic, MotionCommands.MagicPowerUp10, 2f);

        // The PowerUp10 link asks for frames 0..75 of a 60-frame animation; set_animation_id clamps it to 59
        // (ACE GetAnimationLength: "magic windup for level 6 spells appears to be the only animation w/ bugged data").
        Assert.Contains(MotionCommands.MagicPowerUp10, player.State.Actions.Select(a => a.Motion));
        var nodes = player.Sequence.Nodes.Select(n => (n.AnimId, n.LowFrame, n.HighFrame, n.Framerate)).ToArray();
        Assert.Equal(
            new[] { (0x030005A0u, 0, 59, 48f), (0x030005A0u, 0, 59, -114f), (0x0300076Cu, 0, 29, 30f) },
            nodes);

        var hooks = new List<AnimHook>();
        for (var i = 0; i < 120; i++)
        {
            hooks.AddRange(player.Update(1.0 / 60.0).Hooks);
        }

        // 60 frames at 48 fps + 60 at 114 fps ≈ 1.78 s: done, back in the Magic idle, action removed by AnimationDone.
        Assert.Equal(0x0300076Cu, player.Sequence.CurrentNode!.Value.AnimId);
        Assert.Empty(player.State.Actions);
        Assert.Contains(hooks, h => h is CreateParticleHook { EmitterInfoId: 0x3200011E, EmitterId: 1 });
        Assert.Contains(hooks, h => h is StopParticleHook { Direction: AnimHookDirection.Backward });
        Assert.DoesNotContain(hooks, h => h is StopParticleHook { Direction: AnimHookDirection.Forward });
    }

    [RequiresDatsFact]
    public void Walking_moves_the_object_by_its_pos_frames_at_the_ACE_anim_dist_speed()
    {
        var table = Source.GetMotionTable(HumanMotionTable)!;
        var walk = table.Cycles[MotionTableData.Key(MotionCommands.NonCombat, MotionCommands.WalkForward)];
        Assert.Equal(Vector3.Zero, walk.Velocity); // human locomotion is pure root motion (pos frames)

        // Oracle: ACE MotionTable.GetAnimDist = |Σ pos-frame origins| / frame count · framerate.
        var sum = Vector3.Zero;
        var count = 0;
        foreach (var a in walk.Anims)
        {
            foreach (var pos in Source.GetAnimation(a.AnimId)!.PosFrames!)
            {
                sum += pos.Origin;
                count++;
            }
        }

        var speed = sum.Length() / count * walk.Anims[0].Framerate;
        var player = new MotionPlayer(table, Source);
        player.DoMotion(MotionCommands.WalkForward, 1f);
        for (var i = 0; i < 60; i++)
        {
            player.Update(1.0 / 60.0); // let the Ready → WalkForward link finish
        }

        var frame = AcFrame.Identity;
        for (var i = 0; i < 120; i++)
        {
            frame = AcFrame.Combine(frame, player.Update(1.0 / 60.0).RootMotion);
        }

        Assert.InRange(frame.Origin.Length() / 2.0, speed * 0.95, speed * 1.05);
        Assert.True(frame.Origin.Y > 0.9f * frame.Origin.Length(), "humans walk along +Y (forward)");
    }

    [RequiresDatsFact]
    public void Retail_and_ACE_differ_on_same_sign_with_zero_speed()
    {
        Assert.True(MotionTableLogic.SameSign(0f, 1f));   // retail same_sign: 0 counts as positive
        Assert.True(MotionTableLogic.SameSign(-1f, -0.5f));
        Assert.False(MotionTableLogic.SameSign(-1f, 0f));
        Assert.NotEqual(Math.Sign(0f), Math.Sign(1f));     // ACE compares Math.Sign, which says different
    }

    private (uint, int, int, float) Clamp(AnimDataRef a)
    {
        var n = Source.GetAnimation(a.AnimId)!.NumFrames;
        int low = a.LowFrame, high = a.HighFrame;
        if (high < 0)
        {
            high = n - 1;
        }

        if (low >= n)
        {
            low = n - 1;
        }

        if (high >= n)
        {
            high = n - 1;
        }

        if (low > high)
        {
            high = low;
        }

        return (a.AnimId, low, high, a.Framerate);
    }
}
