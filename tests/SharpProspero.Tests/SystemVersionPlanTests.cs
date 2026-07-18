// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System;
using System.IO;
using SharpProspero.Prx;
using Xunit;

namespace SharpProspero.Tests;

// A version reads as its digits, so 11.20 is the byte pair 0x11 0x20. Reading those as ordinary
// numbers gives 17.32, and orders 09.60 above 10.01. Both mistakes are covered here.
public sealed class SystemVersionTypeTests
{
    [Theory]
    [InlineData("11.20", 0x1120)]
    [InlineData("02.00", 0x0200)]
    [InlineData("2.00", 0x0200)]
    [InlineData("1120", 0x1120)]
    [InlineData("10.01", 0x1001)]
    [InlineData("09.60", 0x0960)]
    [InlineData("0x1120000000000000", 0x1120)]
    [InlineData("0x0200000000000000", 0x0200)]
    public void TryParse_AcceptsEveryFormAVersionIsWrittenIn(string text, int expected)
    {
        Assert.True(SystemVersion.TryParse(text, out SystemVersion version));
        Assert.Equal(expected, version.Packed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("1.2.3")]
    [InlineData("11.2a")]      // not a digit
    [InlineData("1a.20")]      // not a digit
    [InlineData("0x0000000000000000")]  // the absent version is not a version
    [InlineData("0x1A20000000000000")]  // 0x1A is not a pair of digits
    [InlineData("111.20")]
    [InlineData("0xZZ")]
    public void TryParse_RejectsWhatIsNotAVersion(string? text)
    {
        Assert.False(SystemVersion.TryParse(text, out SystemVersion version));
        Assert.False(version.HasValue);
    }

    [Fact]
    public void Parse_ThrowsOnRubbish()
        => Assert.Throws<FormatException>(() => SystemVersion.Parse("nonsense"));

    [Theory]
    [InlineData(0x11200009u, "11.20", "0x1120000000000000")]
    [InlineData(0x02000009u, "02.00", "0x0200000000000000")]
    [InlineData(0x02000021u, "02.00", "0x0200000000000000")]   // the patch never reaches the requirement
    public void FromModuleSdkVersion_DropsThePatchAndKeepsTheDigits(uint sdk, string display, string packageValue)
    {
        SystemVersion version = SystemVersion.FromModuleSdkVersion(sdk);
        Assert.Equal(display, version.ToString());
        Assert.Equal(packageValue, version.ToPackageValue());
    }

    [Fact]
    public void FromModuleSdkVersion_ZeroIsTheAbsentVersion()
    {
        SystemVersion version = SystemVersion.FromModuleSdkVersion(0);
        Assert.False(version.HasValue);
        Assert.Equal("", version.ToString());
        Assert.Equal("", version.ToPackageValue());
    }

    [Fact]
    public void MajorAndMinor_ReadAsWritten()
    {
        SystemVersion version = SystemVersion.Parse("11.20");
        Assert.Equal(11, version.Major);   // not 17
        Assert.Equal(20, version.Minor);   // not 32
    }

    [Fact]
    public void Compare_OrdersByTheVersionNotTheBytes()
    {
        // 10.01 is above 09.60: read as plain numbers the bytes would say otherwise.
        Assert.True(SystemVersion.Parse("10.01") > SystemVersion.Parse("09.60"));
        Assert.True(SystemVersion.Parse("11.20") > SystemVersion.Parse("02.00"));
        Assert.True(SystemVersion.Parse("02.00") > SystemVersion.None);
        Assert.True(SystemVersion.Parse("11.20") >= SystemVersion.Parse("11.20"));
        Assert.Equal(SystemVersion.Parse("11.20"), SystemVersion.Parse("0x1120000000000000"));
    }

    [Fact]
    public void RoundTrip_ThroughThePackageValueKeepsTheVersion()
    {
        foreach (string text in new[] { "02.00", "05.50", "09.60", "10.01", "11.20" })
        {
            SystemVersion parsed = SystemVersion.Parse(text);
            Assert.Equal(parsed, SystemVersion.Parse(parsed.ToPackageValue()));
            Assert.Equal(text, parsed.ToString());
        }
    }
}

public sealed class SystemVersionPlannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sp-sysver-" + Guid.NewGuid().ToString("N"));

    public SystemVersionPlannerTests() => Directory.CreateDirectory(Path.Combine(_root, "sce_module"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    // A module carries its requirement in a parameter block reached through a program header. The
    // smallest file that a reader accepts is built here rather than shipped, so the test states the
    // format it depends on.
    private void WriteModule(string relativePath, uint sdkVersion)
    {
        string path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        const int phoff = 0x40;
        const int phentsize = 0x38;
        const int paramOffset = 0x100;
        var data = new byte[0x200];

        // ELF header: 64-bit, little endian, x86-64, one program header at 0x40.
        data[0] = 0x7F; data[1] = (byte)'E'; data[2] = (byte)'L'; data[3] = (byte)'F';
        data[4] = 2;    // 64-bit
        data[5] = 1;    // little endian
        WriteU16(data, 0x10, 0xFE10);   // an executable module
        WriteU16(data, 0x12, 0x3E);     // x86-64
        WriteU64(data, 0x20, phoff);
        WriteU16(data, 0x36, phentsize);
        WriteU16(data, 0x38, 2);

        // The module parameter block's program header.
        WriteU32(data, phoff, 0x61000002);
        WriteU64(data, phoff + 0x08, paramOffset);
        WriteU64(data, phoff + 0x20, 0x20);

        // The dynamic segment's program header. The reader needs one to accept the file at all; it is
        // pointed at a single terminator entry.
        WriteU32(data, phoff + phentsize, 0x00000002);
        WriteU64(data, phoff + phentsize + 0x08, 0x1E0);
        WriteU64(data, phoff + phentsize + 0x20, 0x10);

        // The parameter block: size, magic, its own version, attributes, then the system version.
        WriteU64(data, paramOffset, 0x20);
        WriteU32(data, paramOffset + 8, 0x3C13F4BF);
        WriteU32(data, paramOffset + 12, 2);
        WriteU32(data, paramOffset + 16, 0);
        WriteU32(data, paramOffset + 20, sdkVersion);
        WriteU32(data, paramOffset + 24, 1);

        File.WriteAllBytes(path, data);
    }

    private static void WriteU16(byte[] d, int o, ushort v) { d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); }
    private static void WriteU32(byte[] d, int o, uint v)
    {
        for (int i = 0; i < 4; i++) d[o + i] = (byte)(v >> (i * 8));
    }
    private static void WriteU64(byte[] d, int o, ulong v)
    {
        for (int i = 0; i < 8; i++) d[o + i] = (byte)(v >> (i * 8));
    }

    [Fact]
    public void ScanModules_ReadsWhatEachModuleNeeds()
    {
        WriteModule(@"sce_module\a.prx", 0x11200009);
        WriteModule(@"sce_module\b.prx", 0x02000009);

        ModuleScan scan = SystemVersionPlanner.ScanModules(_root);

        Assert.Equal(2, scan.Modules.Count);
        Assert.Equal("a.prx", scan.Modules[0].FileName);
        Assert.Equal("11.20", scan.Modules[0].Version.ToString());
        Assert.Equal("02.00", scan.Modules[1].Version.ToString());
        Assert.Empty(scan.Unreadable);
    }

    // A module whose requirement cannot be read must be named, not passed over: passing over it lets
    // the application require less than the module needs, which is what this check exists to catch.
    [Fact]
    public void ScanModules_ReportsWhatItCannotRead()
    {
        WriteModule(@"sce_module\real.prx", 0x02000009);
        File.WriteAllText(Path.Combine(_root, "sce_module", "notes.prx"), "this is not a module");

        ModuleScan scan = SystemVersionPlanner.ScanModules(_root);

        Assert.Single(scan.Modules);
        Assert.Equal("real.prx", scan.Modules[0].FileName);
        Assert.Equal("notes.prx", Assert.Single(scan.Unreadable));
    }

    [Fact]
    public void Plan_CarriesTheUnreadableModuleThroughToTheUser()
    {
        File.WriteAllText(Path.Combine(_root, "sce_module", "broken.prx"), "not a module");

        SystemVersionPlan plan = SystemVersionPlanner.Plan(
            _root, "0x0200000000000000", SystemVersionPolicy.Match);

        Assert.Equal("broken.prx", Assert.Single(plan.Unreadable));
        Assert.Contains(plan.Messages, m => m.Contains("broken.prx", StringComparison.Ordinal));
    }

    [Fact]
    public void ScanModules_OnAnEmptyFolderFindsNothing()
    {
        ModuleScan scan = SystemVersionPlanner.ScanModules(Path.Combine(_root, "does-not-exist"));
        Assert.Empty(scan.Modules);
        Assert.Empty(scan.Unreadable);
    }

    // A crafted program-header offset near the top of the range must not wrap a bounds check and read
    // out of range; the reader reports no version rather than throwing an unexpected exception.
    [Fact]
    public void ParseSdkVersion_HandlesAnOutOfRangeProgramHeaderOffset()
    {
        var data = new byte[0x80];
        data[0] = 0x7F; data[1] = (byte)'E'; data[2] = (byte)'L'; data[3] = (byte)'F';
        data[4] = 2; data[5] = 1;
        WriteU16(data, 0x12, 0x3E);                 // x86-64
        WriteU64(data, 0x20, 0x7FFFFFFFFFFFFF00);   // e_phoff far past the end
        WriteU16(data, 0x36, 0x38);
        WriteU16(data, 0x38, 4);

        Assert.Equal(0u, PrxImage.ParseSdkVersion(data));
    }

    // The requirement lives in a program header, so it reads even when the module's exports do not.
    [Fact]
    public void ParseSdkVersion_ReadsAModuleWithNoSymbolTable()
    {
        WriteModule(@"sce_module\headers-only.prx", 0x11200009);
        byte[] data = File.ReadAllBytes(Path.Combine(_root, "sce_module", "headers-only.prx"));

        Assert.Throws<PrxFormatException>(() => PrxImage.Parse(data));
        Assert.Equal(0x11200009u, PrxImage.ParseSdkVersion(data));
    }

    // The case the whole feature exists for: a supplied library needs more than the application asks
    // for, so the package installs and then fails to load it.
    [Fact]
    public void Match_RaisesTheRequirementToTheSuppliedModule()
    {
        WriteModule(@"sce_module\vendor.prx", 0x11200009);

        SystemVersionPlan plan = SystemVersionPlanner.Plan(
            _root, "0x0200000000000000", SystemVersionPolicy.Match);

        Assert.Equal("02.00", plan.Current.ToString());
        Assert.Equal("11.20", plan.Needed.ToString());
        Assert.Equal("11.20", plan.Result.ToString());
        Assert.True(plan.Changed);
        Assert.Empty(plan.Unloadable);
        Assert.Contains(plan.Messages, m => m.Contains("vendor.prx", StringComparison.Ordinal));
    }

    [Fact]
    public void Match_NeverLowersTheRequirement()
    {
        WriteModule(@"sce_module\old.prx", 0x02000009);

        SystemVersionPlan plan = SystemVersionPlanner.Plan(
            _root, "0x1120000000000000", SystemVersionPolicy.Match);

        Assert.Equal("11.20", plan.Result.ToString());
        Assert.False(plan.Changed);
        Assert.Empty(plan.Unloadable);
    }

    [Fact]
    public void Match_WithNoModulesKeepsWhatTheApplicationCarries()
    {
        SystemVersionPlan plan = SystemVersionPlanner.Plan(
            _root, "0x0200000000000000", SystemVersionPolicy.Match);

        Assert.Equal("02.00", plan.Result.ToString());
        Assert.False(plan.Changed);
    }

    [Fact]
    public void Upgrade_RaisesToTheVersionNamed()
    {
        SystemVersionPlan plan = SystemVersionPlanner.Plan(
            _root, "0x0200000000000000", SystemVersionPolicy.Upgrade, SystemVersion.Parse("11.20"));

        Assert.Equal("11.20", plan.Result.ToString());
        Assert.True(plan.Changed);
    }

    [Fact]
    public void Upgrade_RefusesToLower()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => SystemVersionPlanner.Plan(
            _root, "0x1120000000000000", SystemVersionPolicy.Upgrade, SystemVersion.Parse("02.00")));
        Assert.Contains("downgrade", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Upgrade_NeedsAVersion()
        => Assert.Throws<ArgumentException>(() => SystemVersionPlanner.Plan(
            _root, "0x0200000000000000", SystemVersionPolicy.Upgrade));

    [Fact]
    public void Downgrade_LowersAndNamesEveryModuleThatStopsLoading()
    {
        WriteModule(@"sce_module\vendor.prx", 0x11200009);
        WriteModule(@"sce_module\plain.prx", 0x02000009);

        SystemVersionPlan plan = SystemVersionPlanner.Plan(
            _root, "0x1120000000000000", SystemVersionPolicy.Downgrade, SystemVersion.Parse("02.00"));

        Assert.Equal("02.00", plan.Result.ToString());
        Assert.True(plan.Changed);

        ModuleRequirement broken = Assert.Single(plan.Unloadable);
        Assert.Equal("vendor.prx", broken.FileName);
        Assert.Contains(plan.Messages, m =>
            m.Contains("vendor.prx", StringComparison.Ordinal) && m.Contains("will not load", StringComparison.Ordinal));
    }

    [Fact]
    public void Downgrade_RefusesToRaise()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => SystemVersionPlanner.Plan(
            _root, "0x0200000000000000", SystemVersionPolicy.Downgrade, SystemVersion.Parse("11.20")));
        Assert.Contains("upgrade", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Downgrade_NeedsAVersion()
        => Assert.Throws<ArgumentException>(() => SystemVersionPlanner.Plan(
            _root, "0x1120000000000000", SystemVersionPolicy.Downgrade));

    [Fact]
    public void Keep_LeavesTheRequirementButStillReportsTheBreakage()
    {
        WriteModule(@"sce_module\vendor.prx", 0x11200009);

        SystemVersionPlan plan = SystemVersionPlanner.Plan(
            _root, "0x0200000000000000", SystemVersionPolicy.Keep);

        Assert.Equal("02.00", plan.Result.ToString());
        Assert.False(plan.Changed);
        Assert.Single(plan.Unloadable);
    }

    [Fact]
    public void ModuleWithNoRequirementNeverForcesTheVersionUp()
    {
        WriteModule(@"sce_module\quiet.prx", 0);

        SystemVersionPlan plan = SystemVersionPlanner.Plan(
            _root, "0x0200000000000000", SystemVersionPolicy.Match);

        Assert.False(plan.Needed.HasValue);
        Assert.Equal("02.00", plan.Result.ToString());
        Assert.Empty(plan.Unloadable);
    }

    [Fact]
    public void ApplicationWithNoRequirementTakesTheModulesRequirement()
    {
        WriteModule(@"sce_module\vendor.prx", 0x11200009);

        SystemVersionPlan plan = SystemVersionPlanner.Plan(_root, current: null, SystemVersionPolicy.Match);

        Assert.False(plan.Current.HasValue);
        Assert.Equal("11.20", plan.Result.ToString());
        Assert.True(plan.Changed);
    }

    [Fact]
    public void TheApplicationsOwnModuleTakesPart()
    {
        WriteModule("eboot.bin", 0x11200009);

        SystemVersionPlan plan = SystemVersionPlanner.Plan(
            _root, "0x0200000000000000", SystemVersionPolicy.Match);

        Assert.Equal("11.20", plan.Result.ToString());
        Assert.Contains(plan.Modules, m => m.FileName == "eboot.bin");
    }

    [Fact]
    public void TheHighestOfSeveralModulesWins()
    {
        WriteModule(@"sce_module\a.prx", 0x02000009);
        WriteModule(@"sce_module\b.prx", 0x11200009);
        WriteModule(@"sce_module\c.prx", 0x10010000);

        SystemVersionPlan plan = SystemVersionPlanner.Plan(
            _root, "0x0200000000000000", SystemVersionPolicy.Match);

        Assert.Equal("11.20", plan.Result.ToString());
    }
}
