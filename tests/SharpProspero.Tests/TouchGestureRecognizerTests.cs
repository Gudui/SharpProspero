using SharpProspero.Input;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

public sealed class TouchGestureRecognizerTests
{
    private static GamePadState One(long ms, ushort x, ushort y, byte id = 1)
        => GamePadState.Neutral with
        {
            Touch1 = new TouchPoint { X = x, Y = y, Id = id, IsActive = true },
            TouchCount = 1,
            TimestampMicroseconds = (ulong)(ms * 1000),
            IsConnected = true,
        };

    private static GamePadState Two(long ms, ushort x1, ushort y1, ushort x2, ushort y2)
        => GamePadState.Neutral with
        {
            Touch1 = new TouchPoint { X = x1, Y = y1, Id = 1, IsActive = true },
            Touch2 = new TouchPoint { X = x2, Y = y2, Id = 2, IsActive = true },
            TouchCount = 2,
            TimestampMicroseconds = (ulong)(ms * 1000),
            IsConnected = true,
        };

    private static GamePadState Up(long ms)
        => GamePadState.Neutral with { TouchCount = 0, TimestampMicroseconds = (ulong)(ms * 1000), IsConnected = true };

    [Fact]
    public void ShortStillContact_IsATap()
    {
        var r = new TouchGestureRecognizer();
        r.Update(One(0, 500, 500));
        r.Update(One(40, 502, 501));
        var end = r.Update(Up(80));
        Assert.Single(end);
        Assert.Equal(TouchGestureKind.Tap, end[0].Kind);
    }

    [Fact]
    public void TwoTapsCloseTogether_MakeADoubleTap()
    {
        var r = new TouchGestureRecognizer();
        r.Update(One(0, 500, 500));
        Assert.Contains(r.Update(Up(50)), g => g.Kind == TouchGestureKind.Tap);
        r.Update(One(120, 505, 498, id: 2));
        var second = r.Update(Up(160));
        Assert.Contains(second, g => g.Kind == TouchGestureKind.DoubleTap);
    }

    [Fact]
    public void StillContactPastTheHoldTime_RaisesHoldOnce()
    {
        var r = new TouchGestureRecognizer();
        r.Update(One(0, 300, 300));
        Assert.Empty(r.Update(One(200, 300, 300)));
        Assert.Contains(r.Update(One(600, 300, 300)), g => g.Kind == TouchGestureKind.Hold);
        Assert.DoesNotContain(r.Update(One(700, 300, 300)), g => g.Kind == TouchGestureKind.Hold);
    }

    [Fact]
    public void MovingContact_RaisesDrag()
    {
        var r = new TouchGestureRecognizer();
        r.Update(One(0, 100, 100));
        var g = r.Update(One(30, 300, 100));
        Assert.Contains(g, x => x.Kind == TouchGestureKind.Drag && x.Delta.X > 0);
    }

    [Fact]
    public void FastDragReleased_IsAFlick()
    {
        var r = new TouchGestureRecognizer();
        r.Update(One(0, 100, 400));
        r.Update(One(10, 500, 400));   // 400 units in 10 ms -> 40000 units/s
        var end = r.Update(Up(20));
        Assert.Contains(end, g => g.Kind == TouchGestureKind.Flick && g.Delta.X > 0);
    }

    [Fact]
    public void TwoContactsSpreadingApart_PinchScaleGrows()
    {
        var r = new TouchGestureRecognizer();
        r.Update(Two(0, 400, 500, 600, 500));      // 200 apart
        var g = r.Update(Two(30, 300, 500, 700, 500)); // 400 apart -> scale ~2
        TouchGesture pinch = g.Single(x => x.Kind == TouchGestureKind.Pinch);
        Assert.True(pinch.Scale > 1.8f, $"scale {pinch.Scale} should roughly double");
    }

    [Fact]
    public void AnInterveningGesture_BreaksTheDoubleTap()
    {
        var r = new TouchGestureRecognizer();
        r.Update(One(0, 500, 500));
        Assert.Contains(r.Update(Up(40)), g => g.Kind == TouchGestureKind.Tap);
        // A drag then flick between the taps.
        r.Update(One(60, 500, 500));
        r.Update(One(75, 560, 500));
        r.Update(Up(90));
        // A tap that falls within the double-tap gap of the FIRST tap must still be a plain tap.
        r.Update(One(120, 502, 501));
        var second = r.Update(Up(150));
        Assert.Contains(second, g => g.Kind == TouchGestureKind.Tap);
        Assert.DoesNotContain(second, g => g.Kind == TouchGestureKind.DoubleTap);
    }

    [Fact]
    public void TwoContactsRotating_ReportRotation()
    {
        var r = new TouchGestureRecognizer();
        r.Update(Two(0, 400, 500, 600, 500));       // horizontal
        var g = r.Update(Two(30, 500, 400, 500, 600)); // vertical -> ~90 degrees
        TouchGesture pinch = g.Single(x => x.Kind == TouchGestureKind.Pinch);
        Assert.True(System.MathF.Abs(pinch.Rotation) > 1.0f, $"rotation {pinch.Rotation} should be near a quarter turn");
    }
}
