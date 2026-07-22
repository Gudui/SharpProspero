// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Device;
using SharpProspero.Platform;
using System;
using Xunit;

// The device service runs on the device; off the device these pin the values recovered from the module
// and the managed monitor's disposed-state behaviour.
public sealed class DeviceServiceTests
{
    [Fact]
    public void DeviceTypeIdsMatchTheRecoveredValues()
    {
        Assert.Equal(0x1001u, (uint)DeviceType.Type1001);
        Assert.Equal(0x4002u, (uint)DeviceType.Type4002);
        Assert.Equal(0x8001u, (uint)DeviceType.Type8001);
        Assert.Equal(0x9001u, (uint)DeviceType.Type9001);
    }

    [Fact]
    public void DeviceInfoRecordSizeMatchesTheModule()
    {
        Assert.Equal(112, DeviceService.DeviceInfoRecordSize);
    }

    [Fact]
    public void MonitorRejectsUseAfterDispose()
    {
        // The service calls touch the device, so build a monitor without starting it and mark it disposed
        // directly; the disposed guard runs before any device call, so this stays off-device.
        var monitor = (DeviceMonitor)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DeviceMonitor));
        typeof(DeviceMonitor).GetField("_disposed", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(monitor, true);
        Assert.Throws<ObjectDisposedException>(() => monitor.PeekEvents());
        Assert.Throws<ObjectDisposedException>(() => monitor.ConsumeEvents());
        Assert.Throws<ObjectDisposedException>(() => _ = monitor.Generation);
    }
}
