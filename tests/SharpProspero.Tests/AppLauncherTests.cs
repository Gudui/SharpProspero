// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System;
using SharpProspero.Platform;
using Xunit;

namespace SharpProspero.Tests;

// A title id is exactly nine characters; the launcher rejects anything else before it calls the
// service, which is the only part exercisable off the device.
public sealed class AppLauncherTests
{
    [Theory]
    [InlineData("")]
    [InlineData("SHORT")]
    [InlineData("TOOLONG123")]
    public void Launch_RejectsATitleIdThatIsNotNineCharacters(string titleId)
        => Assert.Throws<ArgumentException>(() => AppLauncher.Launch(titleId));

    // A null argument makes encoding throw while the argument vector is half-built. The vector is
    // zero-initialized, so the cleanup frees only the entries it allocated and leaves the heap intact
    // rather than freeing uninitialized slots.
    [Fact]
    public void Launch_WithANullArgument_ThrowsCleanly()
        => Assert.Throws<ArgumentNullException>(() => AppLauncher.Launch("CUSA00000", "first", null!));
}
