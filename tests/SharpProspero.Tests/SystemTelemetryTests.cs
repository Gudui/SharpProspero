// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

// The telemetry readings decode two things: a temperature held as a whole number of tenths of a kelvin,
// and a flag word naming which thresholds a thermal zone has crossed. Both are checked here against the
// values the platform publishes, along with the names the readings are asked for under, because a
// mistyped name reports "this machine does not publish that" instead of failing loudly.
public sealed class SystemTelemetryTests
{
    [Theory]
    [InlineData(0, -273.15)]
    [InlineData(2731, -0.05)]
    [InlineData(2732, 0.05)]
    [InlineData(2981, 24.95)]
    [InlineData(3231, 49.95)]
    public void Temperature_ConvertsTenthsOfAKelvinToCelsius(int deciKelvin, double celsius)
        => Assert.Equal(celsius, Temperature.FromDeciKelvin(deciKelvin).Celsius, 6);

    [Fact]
    public void Temperature_KeepsTheRawValueTheZoneReported()
        => Assert.Equal(3123, Temperature.FromDeciKelvin(3123).DeciKelvin);

    [Fact]
    public void Temperature_KelvinIsTheRawValueOverTen()
        => Assert.Equal(312.3, Temperature.FromDeciKelvin(3123).Kelvin, 6);

    [Theory]
    [InlineData(2732, 32.09)]
    [InlineData(3132, 104.09)]
    public void Temperature_ConvertsToFahrenheit(int deciKelvin, double fahrenheit)
        => Assert.Equal(fahrenheit, Temperature.FromDeciKelvin(deciKelvin).Fahrenheit, 6);

    [Fact]
    public void Temperature_RoundTripsThroughCelsius()
        => Assert.Equal(3131, Temperature.FromCelsius(Temperature.FromDeciKelvin(3131).Celsius).DeciKelvin);

    [Fact]
    public void Temperature_RoundTripsThroughKelvin()
        => Assert.Equal(2954, Temperature.FromKelvin(Temperature.FromDeciKelvin(2954).Kelvin).DeciKelvin);

    [Fact]
    public void Temperature_OrdersColdestFirst()
    {
        Temperature cold = Temperature.FromDeciKelvin(2900);
        Temperature hot = Temperature.FromDeciKelvin(3400);
        Assert.True(cold < hot);
        Assert.True(hot > cold);
        Assert.True(cold <= Temperature.FromDeciKelvin(2900));
        Assert.True(cold >= Temperature.FromDeciKelvin(2900));
        Assert.Equal(cold, Temperature.FromDeciKelvin(2900));
        Assert.NotEqual(cold, hot);
    }

    [Fact]
    public void Temperature_ShowsCelsiusToOneDecimalPlace()
        => Assert.Equal("40.0 C", Temperature.FromDeciKelvin(3131).ToString());

    [Theory]
    [InlineData(0, ThermalAlarms.None)]
    [InlineData(1, ThermalAlarms.PassiveThresholdReached)]
    [InlineData(4, ThermalAlarms.HotThresholdReached)]
    [InlineData(8, ThermalAlarms.CriticalThresholdReached)]
    [InlineData(13, ThermalAlarms.PassiveThresholdReached | ThermalAlarms.HotThresholdReached | ThermalAlarms.CriticalThresholdReached)]
    public void DecodeAlarms_NamesTheThresholdsTheFlagWordCarries(int flags, ThermalAlarms expected)
        => Assert.Equal(expected, SystemTelemetry.DecodeAlarms(flags));

    [Fact]
    public void DecodeAlarms_DropsBitsNoThresholdClaims()
        => Assert.Equal(ThermalAlarms.PassiveThresholdReached, SystemTelemetry.DecodeAlarms(unchecked((int)0xFFFFFFF3)));

    [Fact]
    public void DecodeTripPoints_ReadsOneReadingPerFourByteEntry()
    {
        byte[] block = new byte[SystemTelemetry.ActiveTripPointCount * sizeof(int)];
        BitConverter.GetBytes(3231).CopyTo(block, 0);
        BitConverter.GetBytes(3131).CopyTo(block, 4);
        for (int i = 2; i < SystemTelemetry.ActiveTripPointCount; i++)
            BitConverter.GetBytes(SystemTelemetry.UndefinedTripPoint).CopyTo(block, i * sizeof(int));

        IReadOnlyList<Temperature?> points = SystemTelemetry.DecodeTripPoints(block);

        Assert.Equal(SystemTelemetry.ActiveTripPointCount, points.Count);
        Assert.Equal(Temperature.FromDeciKelvin(3231), points[0]);
        Assert.Equal(Temperature.FromDeciKelvin(3131), points[1]);
        Assert.All(points.Skip(2), p => Assert.Null(p));
    }

    [Fact]
    public void DecodeTripPoints_AcceptsAnEmptyBlock()
        => Assert.Empty(SystemTelemetry.DecodeTripPoints([]));

    [Fact]
    public void DecodeTripPoints_RejectsAPartialEntry()
        => Assert.Throws<ArgumentException>(() => SystemTelemetry.DecodeTripPoints(new byte[6]));

    [Theory]
    [InlineData(0, "temperature", "hw.acpi.thermal.tz0.temperature")]
    [InlineData(0, "thermal_flags", "hw.acpi.thermal.tz0.thermal_flags")]
    [InlineData(1, "_ACx", "hw.acpi.thermal.tz1._ACx")]
    [InlineData(12, "_CRT", "hw.acpi.thermal.tz12._CRT")]
    public void ThermalZoneNode_NamesTheValueTheWayTheSystemPublishesIt(int zone, string leaf, string expected)
        => Assert.Equal(expected, SystemTelemetry.ThermalZoneNode(zone, leaf));

    [Fact]
    public void ThermalZoneNode_RejectsANegativeZone()
        => Assert.Throws<ArgumentOutOfRangeException>(() => SystemTelemetry.ThermalZoneNode(-1, "temperature"));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ThermalZoneNode_RejectsAnEmptyLeaf(string? leaf)
        => Assert.ThrowsAny<ArgumentException>(() => SystemTelemetry.ThermalZoneNode(0, leaf!));

    [Fact]
    public void ThermalRoot_IsTheTreeTheZonesHangUnder()
        => Assert.Equal("hw.acpi.thermal", SystemTelemetry.ThermalRoot);
}
