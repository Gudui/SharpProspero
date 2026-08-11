// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.SystemService;
using SharpProspero.Platform;
using SharpProspero.Prx;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The power surface an application module can reach, pinned against the toolchain: the two blocks the
// service writes, the numbers it defines, and - just as important - the requests it never offers, so
// that a later round cannot quietly add a shutdown call that no module could ever link.
public sealed unsafe class PowerControlTests
{
    [Fact]
    public void GpuLoadEmulationModeMatchesTheDefinedNumbers()
    {
        Assert.Equal(SystemService.GpuLoadEmulationModeOff, (int)GpuLoadEmulationMode.Off);
        Assert.Equal(SystemService.GpuLoadEmulationModeNormal, (int)GpuLoadEmulationMode.Normal);
        Assert.Equal(0, (int)GpuLoadEmulationMode.Off);
        Assert.Equal(1, (int)GpuLoadEmulationMode.Normal);
    }

    [Theory]
    [InlineData(GpuLoadEmulationMode.Off)]
    [InlineData(GpuLoadEmulationMode.Normal)]
    public void EveryDefinedModeIsAccepted(GpuLoadEmulationMode mode)
        => Assert.True(PowerControl.IsDefined(mode));

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(0x7FFFFFFF)]
    public void AModeTheSystemDoesNotDefineIsRefusedBeforeTheCall(int raw)
    {
        var mode = (GpuLoadEmulationMode)raw;
        Assert.False(PowerControl.IsDefined(mode));
        // Refused here rather than on the device: the platform answers an undefined mode with a
        // parameter error, and a caller learns that far sooner from the argument check.
        Assert.Throws<ArgumentOutOfRangeException>(() => PowerControl.TrySetGpuLoadEmulation(mode));
    }

    [Fact]
    public void TheServiceErrorNumbersMatchWhatTheServiceReports()
    {
        Assert.Equal(unchecked((int)0x80A10001), SystemService.ErrorInternal);
        Assert.Equal(unchecked((int)0x80A10002), SystemService.ErrorUnavailable);
        Assert.Equal(unchecked((int)0x80A10003), SystemService.ErrorParameter);
        Assert.Equal(unchecked((int)0x80A10004), SystemService.ErrorNoEvent);
        Assert.Equal(unchecked((int)0x80A10005), SystemService.ErrorRejected);
        Assert.Equal(unchecked((int)0x80A10006), SystemService.ErrorNeedDisplaySafeAreaSettings);
    }

    [Fact]
    public void TheStatusBlockIsTheSizeTheServiceZeroes()
        // A four-byte count, two flags, then the reserved tail, rounded to the count's alignment.
        => Assert.Equal(136, sizeof(SceSystemServiceStatus));

    [Theory]
    [InlineData("EventNum", 0)]
    [InlineData("IsSystemUiOverlaid", 4)]
    [InlineData("IsInBackgroundExecution", 5)]
    public void TheStatusFieldsSitWhereTheServiceWritesThem(string field, int offset)
        => Assert.Equal(offset, (int)Marshal.OffsetOf<SceSystemServiceStatus>(field));

    [Fact]
    public void TheEventBlockIsTheSizeTheServiceFills()
        // A four-byte type followed by an 0x2000-byte payload the service clears on every read.
        => Assert.Equal(4 + 0x2000, sizeof(SceSystemServiceEvent));

    [Fact]
    public void TheEventTypeSitsFirstInTheEventBlock()
        => Assert.Equal(0, (int)Marshal.OffsetOf<SceSystemServiceEvent>("EventType"));

    [Theory]
    [InlineData(SystemEventType.Invalid, -1)]
    [InlineData(SystemEventType.Resume, 0x10000000)]
    [InlineData(SystemEventType.EntitlementUpdate, 0x10000003)]
    [InlineData(SystemEventType.AppLaunched, 0x10000007)]
    [InlineData(SystemEventType.AddContentInstalled, 0x10000009)]
    [InlineData(SystemEventType.PlayGoLocusUpdate, 0x1000000C)]
    [InlineData(SystemEventType.ServiceEntitlementUpdate, 0x1000000E)]
    [InlineData(SystemEventType.GameIntent, 0x10000017)]
    [InlineData(SystemEventType.UnifiedEntitlementUpdate, 0x10000018)]
    [InlineData(SystemEventType.PlayGoChunkAdded, 0x10000019)]
    public void EveryEventCarriesTheNumberTheServiceSends(SystemEventType type, int value)
        => Assert.Equal(value, (int)type);

    [Fact]
    public void TheResumeEventKeepsTheNumberTheInteropAlreadyNames()
    {
        Assert.Equal(SystemService.EventOnResume, (int)SystemEventType.Resume);
        Assert.Equal(SystemService.EventLaunchApp, (int)SystemEventType.AppLaunched);
    }

    [Fact]
    public void ThePowerCallsAreNamedByACatalogEntry()
    {
        var named = StubCatalog.Core.SelectMany(e => e.Exports).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("sceSystemServicePowerTick", named);
        Assert.Contains("sceSystemServiceSetGpuLoadEmulationMode", named);
        Assert.Contains("sceSystemServiceGetGpuLoadEmulationMode", named);
        Assert.Contains("sceSystemServiceReportAbnormalTermination", named);
    }

    // The names an application would need to turn the machine off, restart it, or send it to standby.
    // Every one of them exists on the device; none is offered to an application at link time.
    private static readonly string[] PowerStateRequests =
    [
        "sceSystemServiceRequestPowerOff",
        "sceSystemServiceRequestReboot",
        "sceSystemServiceDeclareReadyForSuspend",
        "sceSystemServiceEnableSuspendNotification",
        "sceSystemServiceDisableSuspendNotification",
        "sceSystemStateMgrTurnOff",
        "sceSystemStateMgrReboot",
        "sceSystemStateMgrEnterStandby",
        "sceSystemStateMgrWakeUp",
        "sceSystemStateMgrGetCurrentState",
        "sceSystemStateMgrGetRebootCause",
        "sceShellCoreUtilRequestShutdown",
        "sceShellCoreUtilRequestRebootApp",
    ];

    [Fact]
    public void NoCatalogEntryNamesAPowerStateRequest()
    {
        // A name listed here would produce an import nothing can bind, and a module whose imports do
        // not all bind never reaches its first instruction - so adding one would not fail at the call,
        // it would stop every module built with the SDK from starting at all.
        var named = StubCatalog.Core.SelectMany(e => e.Exports).ToHashSet(StringComparer.Ordinal);
        string[] present = [.. PowerStateRequests.Where(named.Contains).Order()];
        Assert.True(present.Length == 0,
            "These change the machine's power state and no library offers them to an application, so a " +
            "module naming one could never load:\n  " + string.Join("\n  ", present));
    }

    [Fact]
    public void TheToolchainOffersAnApplicationNoWayToChangeThePowerState()
    {
        // The measurement behind the check above, taken from the toolchain rather than asserted: the
        // link-time libraries decide what an application may reference, and not one of them carries a
        // name that turns the machine off, restarts it, or suspends it.
        string? dir = LinkLibraryDirectory();
        if (dir is null)
            return;                                         // toolchain not installed on this machine

        var offered = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(dir, "*_stub_weak.a"))
            offered.UnionWith(PublishedNames(path));

        // The set is real: the one power call an application does get is in it.
        Assert.Contains("sceSystemServicePowerTick", offered);

        string[] reachable = [.. PowerStateRequests.Where(offered.Contains).Order()];
        Assert.True(reachable.Length == 0,
            "The toolchain now offers these, so the power surface can be widened:\n  " +
            string.Join("\n  ", reachable));
    }

    [Fact]
    public void EveryPowerNameTheSdkBindsIsOneTheSystemServiceLibraryOffers()
    {
        string? dir = LinkLibraryDirectory();
        if (dir is null)
            return;                                         // toolchain not installed on this machine

        var offered = PublishedNames(Path.Combine(dir, "libSceSystemService_stub_weak.a"))
            .ToHashSet(StringComparer.Ordinal);
        foreach (string name in new[]
        {
            "sceSystemServicePowerTick",
            "sceSystemServiceSetGpuLoadEmulationMode",
            "sceSystemServiceGetGpuLoadEmulationMode",
            "sceSystemServiceReportAbnormalTermination",
            "sceSystemServiceReceiveEvent",
            "sceSystemServiceGetStatus",
        })
            Assert.Contains(name, offered);
    }

    private static string? LinkLibraryDirectory()
    {
        string? root = Environment.GetEnvironmentVariable("PROSPERO_SDK_DIR");
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            const string Installed = @"C:\Program Files (x86)\SCE\Prospero SDKs\2.000";
            root = Directory.Exists(Installed) ? Installed : null;
        }
        if (root is null)
            return null;
        string dir = Path.Combine(root, "target", "lib");
        return Directory.Exists(dir) ? dir : null;
    }

    // The names a link-time library publishes, read out of its dynamic symbol table.
    private static IEnumerable<string> PublishedNames(string path)
    {
        if (!File.Exists(path))
            yield break;
        byte[] f = File.ReadAllBytes(path);
        if (f.Length < 0x40 || f[0] != 0x7F || f[1] != (byte)'E' || f[2] != (byte)'L' || f[3] != (byte)'F')
            yield break;
        ulong shoff = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(f.AsSpan(0x28));
        int shnum = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(0x3C));
        for (int i = 0; i < shnum; i++)
        {
            int sh = (int)shoff + i * 64;
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(sh + 4)) != 11)
                continue;                                   // SHT_DYNSYM
            ulong off = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(f.AsSpan(sh + 0x18));
            ulong size = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(f.AsSpan(sh + 0x20));
            uint link = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(sh + 0x28));
            ulong strOff = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(
                f.AsSpan((int)shoff + (int)link * 64 + 0x18));
            for (ulong e = 24; e < size; e += 24)
            {
                uint nameOff = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan((int)(off + e)));
                int at = (int)strOff + (int)nameOff;
                int end = Array.IndexOf(f, (byte)0, at);
                if (end > at)
                    yield return System.Text.Encoding.ASCII.GetString(f, at, end - at);
            }
        }
    }
}
