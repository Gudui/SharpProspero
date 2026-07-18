// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System;
using SharpProspero.Interop.Rtc;
using SharpProspero.Timing;
using Xunit;

namespace SharpProspero.Tests;

public sealed class TimingTests
{
    [Fact]
    public void ToDateTime_MapsCalendarFieldsAndMicroseconds()
    {
        var raw = new SceRtcDateTime
        {
            Year = 2026,
            Month = 7,
            Day = 17,
            Hour = 13,
            Minute = 45,
            Second = 9,
            Microsecond = 250_000,
        };

        DateTime dt = SystemClock.ToDateTime(raw, DateTimeKind.Utc);

        Assert.Equal(2026, dt.Year);
        Assert.Equal(7, dt.Month);
        Assert.Equal(17, dt.Day);
        Assert.Equal(13, dt.Hour);
        Assert.Equal(45, dt.Minute);
        Assert.Equal(9, dt.Second);
        Assert.Equal(250, dt.Millisecond);       // 250,000 microseconds -> 250 ms
        Assert.Equal(DateTimeKind.Utc, dt.Kind);
    }
}
