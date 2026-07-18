// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Platform;
using SharpProspero.Prx;
using System.IO;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

// The registry is the single source of truth for what the SDK expects of the system: the supported
// range, the version its run-time surfaces were last confirmed on, and the libraries it resolves by
// name. These lock its shape so an entry cannot go in without a path, exports and provenance.
public sealed class FirmwareRegistryTests
{
    [Fact]
    public void SupportedRange_IsOpenEndedFromTwoOh()
    {
        FirmwareRange range = FirmwareRegistry.SupportedRange;
        Assert.Equal(FirmwareVersion.FromMajorMinor(2, 0), range.Minimum);
        Assert.True(range.IsOpenEnded);
        Assert.True(range.Contains(FirmwareVersion.FromMajorMinor(2, 0)));
        Assert.True(range.Contains(FirmwareVersion.FromMajorMinor(10, 1)));
        Assert.False(range.Contains(FirmwareVersion.FromMajorMinor(1, 50)));
        Assert.False(range.Contains(FirmwareVersion.None));
    }

    [Fact]
    public void LastValidated_IsWithinTheSupportedRange()
        => Assert.True(FirmwareRegistry.SupportedRange.Contains(FirmwareRegistry.LastValidatedOn));

    [Fact]
    public void EveryDynamicLibrary_CarriesPathExportsAndProvenance()
    {
        Assert.NotEmpty(FirmwareRegistry.DynamicLibraries);
        foreach (SystemLibraryDescriptor library in FirmwareRegistry.DynamicLibraries)
        {
            Assert.False(string.IsNullOrWhiteSpace(library.Name));
            Assert.StartsWith("/", library.Path);
            Assert.NotEmpty(library.RequiredExports);
            Assert.All(library.RequiredExports, e => Assert.False(string.IsNullOrWhiteSpace(e)));
            Assert.True(library.TestedOn.HasValue);
            Assert.False(string.IsNullOrWhiteSpace(library.Notes));
        }
    }

    [Fact]
    public void DynamicLibraries_HaveDistinctPaths()
    {
        var paths = FirmwareRegistry.DynamicLibraries.Select(l => l.Path).ToList();
        Assert.Equal(paths.Count, paths.Distinct().Count());
    }

    [Fact]
    public void RegistryPaths_MatchTheSurfaceConstants()
    {
        // The paths in the registry are the same ones the wrappers load, so the two cannot drift.
        Assert.Contains(FirmwareRegistry.DynamicLibraries, l => l.Path == PackageInstaller.ModulePath);
        Assert.Contains(FirmwareRegistry.DynamicLibraries, l => l.Path == UsbStorage.ModulePath);
    }

    [Fact]
    public void FindLibrary_ResolvesByName_AndIsNullForUnknown()
    {
        Assert.NotNull(FirmwareRegistry.FindLibrary("USB mass storage"));
        Assert.Null(FirmwareRegistry.FindLibrary("no such service"));
    }

    [Fact]
    public void ClosedRange_ExcludesAboveTheMaximum()
    {
        var range = new FirmwareRange(FirmwareVersion.FromMajorMinor(2, 0), FirmwareVersion.FromMajorMinor(9, 0));
        Assert.False(range.IsOpenEnded);
        Assert.True(range.Contains(FirmwareVersion.FromMajorMinor(5, 0)));
        Assert.False(range.Contains(FirmwareVersion.FromMajorMinor(10, 0)));
        Assert.Equal("02.00 to 09.00", range.ToString());
    }

    [Fact]
    public void OpenRange_ReadsAsAndLater()
        => Assert.Equal("02.00 and later", FirmwareRegistry.SupportedRange.ToString());

    // The offline offsets tool matches a module against StubCatalog.RuntimeResolved; the on-device
    // validator uses FirmwareRegistry.DynamicLibraries. They live in different assemblies (the toolchain
    // and the device SDK), so this ties them together: every name the runtime validator requires is one
    // the coverage catalog knows about, so the two cannot drift apart. The coverage catalog may list
    // more (an optional, newer export the runtime does not require), so this is a subset check.
    [Fact]
    public void RegistryRequiredExports_AreCoveredByTheToolchainCatalog()
    {
        foreach (SystemLibraryDescriptor descriptor in FirmwareRegistry.DynamicLibraries)
        {
            string library = Path.GetFileNameWithoutExtension(descriptor.Path);
            StubCatalog.Entry entry = StubCatalog.RuntimeResolved.Single(e => e.Library == library);
            Assert.All(descriptor.RequiredExports, name => Assert.Contains(name, entry.Exports));
        }
    }
}
