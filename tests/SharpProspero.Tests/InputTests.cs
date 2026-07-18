// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System;
using System.Buffers.Binary;
using System.Numerics;
using SharpProspero.Input;
using SharpProspero.Interop.Pad;
using Xunit;

namespace SharpProspero.Tests;

public sealed class InputTests
{
    [Fact]
    public void FromSample_ShortBuffer_ReturnsNeutral()
    {
        GamePadState state = GamePadState.FromSample(new byte[16]);
        Assert.Equal(128, state.LeftStickX);
        Assert.Equal(Quaternion.Identity, state.Orientation);
    }

    [Fact]
    public void FromSample_DecodesButtonsSticksTriggers()
    {
        byte[] s = BuildSample();
        GamePadState state = GamePadState.FromSample(s);

        Assert.True(state.IsPressed(ScePadButton.Cross));
        Assert.Equal(200, state.LeftStickX);
        Assert.Equal(50, state.LeftStickY);
        Assert.Equal(128, state.RightStickX);
        Assert.Equal(100, state.LeftTrigger);
        Assert.Equal(255, state.RightTrigger);

        (float x, float y) = state.LeftStick;
        Assert.True(x > 0.5f);   // 200 is well right of center
        Assert.True(y < -0.5f);  // 50 is well above center
    }

    [Fact]
    public void FromSample_DecodesMotionAndTouchAndTimestamp()
    {
        byte[] s = BuildSample();
        GamePadState state = GamePadState.FromSample(s);

        Assert.Equal(Quaternion.Identity, state.Orientation);
        Assert.Equal(new Vector3(0f, 1f, 0f), state.Acceleration);
        Assert.Equal(Vector3.Zero, state.AngularVelocity);

        Assert.Equal(1, state.TouchCount);
        Assert.True(state.Touch1.IsActive);
        Assert.Equal(960, state.Touch1.X);
        Assert.Equal(540, state.Touch1.Y);
        Assert.Equal(7, state.Touch1.Id);
        Assert.False(state.Touch2.IsActive);

        Assert.True(state.IsConnected);
        Assert.Equal(123456ul, state.TimestampMicroseconds);
    }

    // Builds a controller sample with known values at the decoded offsets.
    private static byte[] BuildSample()
    {
        byte[] s = new byte[0x60];
        BinaryPrimitives.WriteUInt32LittleEndian(s, (uint)ScePadButton.Cross);
        s[4] = 200; s[5] = 50; s[6] = 128; s[7] = 128;   // sticks
        s[8] = 100; s[9] = 255;                           // triggers
        WriteF(s, 12, 0f); WriteF(s, 16, 0f); WriteF(s, 20, 0f); WriteF(s, 24, 1f);   // orientation identity
        WriteF(s, 28, 0f); WriteF(s, 32, 1f); WriteF(s, 36, 0f);                      // acceleration
        WriteF(s, 40, 0f); WriteF(s, 44, 0f); WriteF(s, 48, 0f);                      // angular velocity
        s[52] = 1;                                         // one touch
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(60), 960);   // touch[0].x
        BinaryPrimitives.WriteUInt16LittleEndian(s.AsSpan(62), 540);   // touch[0].y
        s[64] = 7;                                         // touch[0].id
        s[76] = 1;                                         // connected
        BinaryPrimitives.WriteUInt64LittleEndian(s.AsSpan(80), 123456);
        return s;
    }

    private static void WriteF(byte[] s, int offset, float value)
        => BinaryPrimitives.WriteSingleLittleEndian(s.AsSpan(offset), value);
}
