// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Net;
using SharpProspero.Prx;
using System.Linq;
using Xunit;

namespace SharpProspero.Tests;

// The HTTP method and version constants from the headers, and the TLS module's unusual version.
public sealed class HttpTests
{
    [Theory]
    [InlineData(Http.MethodGet, 0)]
    [InlineData(Http.MethodPost, 1)]
    [InlineData(Http.Version11, 2)]
    public void Constants_MatchTheHeader(int value, int expected)
        => Assert.Equal(expected, value);

    // libSceSsl publishes module version 2.1; a 1.1 stub would install and then fail to bind.
    [Fact]
    public void Ssl_StubRecordsModuleVersionTwoPointOne()
    {
        StubCatalog.Entry entry = StubCatalog.Core.Single(e => e.Library == "libSceSsl");
        Assert.Equal((ushort)0x0201, entry.ModuleVersion);
    }

    [Fact]
    public void HttpAndNet_UseTheUsualVersion()
    {
        Assert.Equal((ushort)0x0101, StubCatalog.Core.Single(e => e.Library == "libSceHttp").ModuleVersion);
        Assert.Equal((ushort)0x0101, StubCatalog.Core.Single(e => e.Library == "libSceNet").ModuleVersion);
    }
}
