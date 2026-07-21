// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Graphics;
using SharpProspero.Input;
using SharpProspero.Interop.Pad;
using SharpProspero.Numerics;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed unsafe class GameFrameworkTests
{
    // --- MathUtil ---

    [Fact]
    public void MathUtil_BlendingAndRanges()
    {
        Assert.Equal(5f, MathUtil.Lerp(0f, 10f, 0.5f));
        Assert.Equal(10f, MathUtil.LerpClamped(0f, 10f, 2f));   // t clamped to 1
        Assert.Equal(0.5f, MathUtil.InverseLerp(0f, 10f, 5f));
        Assert.Equal(0f, MathUtil.InverseLerp(4f, 4f, 4f));     // no range
        Assert.Equal(50f, MathUtil.Remap(5f, 0f, 10f, 0f, 100f));
        Assert.Equal(0.5f, MathUtil.Clamp01(0.5f));
        Assert.Equal(1f, MathUtil.Clamp01(9f));
        Assert.Equal(3f, MathUtil.Clamp(3f, 0f, 5f));
        Assert.Equal(5f, MathUtil.Clamp(9f, 0f, 5f));
    }

    [Fact]
    public void MathUtil_SmoothStepMoveTowardsApproximately()
    {
        Assert.Equal(0f, MathUtil.SmoothStep(0f, 1f, 0f));
        Assert.Equal(1f, MathUtil.SmoothStep(0f, 1f, 1f));
        Assert.Equal(0.5f, MathUtil.SmoothStep(0f, 1f, 0.5f), 4);

        Assert.Equal(3f, MathUtil.MoveTowards(0f, 10f, 3f));
        Assert.Equal(10f, MathUtil.MoveTowards(0f, 10f, 100f)); // does not overshoot

        Assert.True(MathUtil.Approximately(1f, 1f + 1e-7f));
        Assert.False(MathUtil.Approximately(1f, 1.01f));
    }

    [Fact]
    public void MathUtil_RepeatPingPongAngles()
    {
        Assert.Equal(2f, MathUtil.Repeat(7f, 5f), 4);
        Assert.Equal(4f, MathUtil.Repeat(-1f, 5f), 4);         // wraps positive
        Assert.Equal(3f, MathUtil.PingPong(7f, 5f), 4);
        Assert.Equal(0f, MathUtil.PingPong(10f, 5f), 4);

        Assert.Equal(MathF.PI, MathUtil.DegreesToRadians(180f), 4);
        Assert.Equal(180f, MathUtil.RadiansToDegrees(MathF.PI), 3);
        Assert.Equal(0f, MathUtil.WrapAngle(0f), 4);
        Assert.Equal(-MathF.PI, MathUtil.WrapAngle(3f * MathF.PI), 4); // folds into [-pi, pi)
        Assert.Equal(1f, MathUtil.WrapAngle((2f * MathF.PI) + 1f), 4);
    }

    // --- InputMap ---

    private static GamePadState Pad(ScePadButton buttons) => new() { Buttons = buttons };

    [Fact]
    public void InputMap_PressHoldRelease()
    {
        var map = new InputMap().Bind("Jump", ScePadButton.Cross);

        map.Update(GamePadState.Neutral);
        Assert.False(map.WasPressed("Jump"));

        map.Update(Pad(ScePadButton.Cross));
        Assert.True(map.WasPressed("Jump"));
        Assert.True(map.IsHeld("Jump"));
        Assert.False(map.WasReleased("Jump"));

        map.Update(Pad(ScePadButton.Cross));
        Assert.False(map.WasPressed("Jump")); // still held, not a fresh press
        Assert.True(map.IsHeld("Jump"));

        map.Update(GamePadState.Neutral);
        Assert.True(map.WasReleased("Jump"));
        Assert.False(map.IsHeld("Jump"));
    }

    [Fact]
    public void InputMap_ChordNeedsEveryButton()
    {
        var map = new InputMap().Bind("Special", ScePadButton.L1 | ScePadButton.R1);

        map.Update(Pad(ScePadButton.L1));
        Assert.False(map.IsHeld("Special")); // only one of the two

        map.Update(Pad(ScePadButton.L1 | ScePadButton.R1));
        Assert.True(map.WasPressed("Special"));
        Assert.True(map.IsHeld("Special"));
    }

    [Fact]
    public void InputMap_AlternativesAndUnbound()
    {
        var map = new InputMap().Bind("Confirm", ScePadButton.Cross).Bind("Confirm", ScePadButton.Options);

        map.Update(Pad(ScePadButton.Options));
        Assert.True(map.IsHeld("Confirm")); // the second binding triggers it

        Assert.False(map.IsHeld("Missing")); // unbound is never active
        Assert.True(map.IsBound("Confirm"));
        map.Unbind("Confirm");
        Assert.False(map.IsBound("Confirm"));
    }

    // --- AnimatedSprite ---

    private static void WithSheet(Action<SpriteSheet> action)
    {
        uint[] pixels = new uint[40 * 10]; // 4 frames of 10x10 across one row
        fixed (uint* p = pixels)
            action(new SpriteSheet(new Surface(p, 40, 10), 10, 10));
    }

    [Fact]
    public void AnimatedSprite_LoopsThroughFrames()
    {
        WithSheet(sheet =>
        {
            var sprite = new AnimatedSprite(sheet, framesPerSecond: 1f); // 4 frames, 1 per second
            Assert.Equal(4, sprite.FrameCount);
            Assert.Equal(0, sprite.CurrentFrame);

            sprite.Update(1f); Assert.Equal(1, sprite.LocalFrame);
            sprite.Update(1f); Assert.Equal(2, sprite.LocalFrame);
            sprite.Update(1f); Assert.Equal(3, sprite.LocalFrame);
            sprite.Update(1f); Assert.Equal(0, sprite.LocalFrame); // wraps
        });
    }

    [Fact]
    public void AnimatedSprite_OnceStopsAndCompletes()
    {
        WithSheet(sheet =>
        {
            var sprite = new AnimatedSprite(sheet, 1f, firstFrame: 0, frameCount: -1, SpriteMode.Once);
            sprite.Update(10f); // more than enough time to run out
            Assert.True(sprite.IsComplete);
            Assert.Equal(3, sprite.LocalFrame); // holds the last frame
        });
    }

    [Fact]
    public void AnimatedSprite_PingPongBounces()
    {
        WithSheet(sheet =>
        {
            var sprite = new AnimatedSprite(sheet, 1f, 0, -1, SpriteMode.PingPong);
            int[] seen = new int[6];
            for (int i = 0; i < 6; i++)
            {
                sprite.Update(1f);
                seen[i] = sprite.LocalFrame;
            }
            Assert.Equal(new[] { 1, 2, 3, 2, 1, 0 }, seen);
        });
    }

    [Fact]
    public void AnimatedSprite_RangeResetAndNoOp()
    {
        WithSheet(sheet =>
        {
            var sprite = new AnimatedSprite(sheet, 1f, firstFrame: 2, frameCount: 2);
            Assert.Equal(2, sprite.FrameCount);
            Assert.Equal(2, sprite.CurrentFrame); // first + local(0)

            sprite.Update(1f);
            Assert.Equal(3, sprite.CurrentFrame); // first(2) + local(1)

            sprite.Update(0f);      // no time
            Assert.Equal(3, sprite.CurrentFrame);
            sprite.FramesPerSecond = 0f;
            sprite.Update(5f);      // no rate
            Assert.Equal(3, sprite.CurrentFrame);

            sprite.Reset();
            Assert.Equal(2, sprite.CurrentFrame);
        });
    }

    [Fact]
    public void AnimatedSprite_SingleFrameOnceReadsComplete()
    {
        uint[] pixels = new uint[10 * 10]; // one 10x10 frame
        fixed (uint* p = pixels)
        {
            var sheet = new SpriteSheet(new Surface(p, 10, 10), 10, 10);
            Assert.Equal(1, sheet.Count);

            var once = new AnimatedSprite(sheet, mode: SpriteMode.Once);
            Assert.True(once.IsComplete);   // already at its only frame
            once.Reset();
            Assert.True(once.IsComplete);

            var loop = new AnimatedSprite(sheet, mode: SpriteMode.Loop);
            Assert.False(loop.IsComplete);  // a looping single frame never completes
        }
    }
}
