// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Numerics;
using System;
using Xunit;

namespace SharpProspero.Tests;

public sealed class SmoothingMathTests
{
    [Fact]
    public void SmoothDamp_ConvergesOnTheTargetWithoutOvershooting()
    {
        float value = 0f, velocity = 0f;
        float previous = value;
        for (int i = 0; i < 600; i++) // ten seconds at 60 fps
        {
            value = MathUtil.SmoothDamp(value, 100f, ref velocity, 0.3f, 1f / 60f);
            Assert.True(value <= 100.001f, $"never overshoots the target ({value})");
            Assert.True(value >= previous - 0.001f, "moves monotonically toward the target");
            previous = value;
        }
        Assert.Equal(100f, value, 1);
        Assert.Equal(0f, velocity, 1);
    }

    [Fact]
    public void SmoothDamp_MoreThanHalfwayAfterOneSmoothTime()
    {
        float value = 0f, velocity = 0f;
        // Step forward roughly one smoothTime worth of frames.
        for (int i = 0; i < 18; i++)
            value = MathUtil.SmoothDamp(value, 10f, ref velocity, 0.3f, 1f / 60f);
        Assert.InRange(value, 5f, 9.5f);
    }

    [Fact]
    public void SmoothDamp_MaxSpeedLimitsTheRate()
    {
        float capped = 0f, vc = 0f;
        capped = MathUtil.SmoothDamp(capped, 1000f, ref vc, 0.1f, 1f / 60f, maxSpeed: 5f);
        Assert.True(capped <= 5f * (1f / 60f) + 0.001f, $"a step obeys the speed cap ({capped})");
    }

    [Fact]
    public void LerpAngle_TakesTheShortWayRound()
    {
        float almostFull = MathF.Tau - 0.1f;
        // Halfway from just-under-a-turn to 0.1 should cross zero, landing near 0, not near pi.
        float mid = MathUtil.LerpAngle(almostFull, 0.1f, 0.5f);
        float wrapped = MathUtil.WrapAngle(mid);
        Assert.InRange(wrapped, -0.05f, 0.05f);
    }

    [Fact]
    public void SmoothDampAngle_SettlesAtTheTargetAngle()
    {
        float angle = 0.1f, velocity = 0f;
        float target = MathF.Tau - 0.1f; // just below a full turn, i.e. -0.1 the short way
        for (int i = 0; i < 300; i++)
            angle = MathUtil.SmoothDampAngle(angle, target, ref velocity, 0.2f, 1f / 60f);
        Assert.InRange(MathUtil.WrapAngle(angle - target), -0.02f, 0.02f);
    }

    [Fact]
    public void Vector2SmoothDamp_ConvergesOnTheTarget()
    {
        Vector2 pos = Vector2.Zero, vel = Vector2.Zero;
        var target = new Vector2(30f, -40f);
        for (int i = 0; i < 400; i++)
            pos = Vector2.SmoothDamp(pos, target, ref vel, 0.25f, 1f / 60f);
        Assert.True(Vector2.Distance(pos, target) < 0.1f, $"reached the target ({pos})");
    }
}
