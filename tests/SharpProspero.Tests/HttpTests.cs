// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Net;
using SharpProspero.Platform;
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

    // The numbers the request calls take for each method. A wrong one here asks the server to do
    // something else entirely - a delete where a fetch was meant - and nothing on the way out says so.
    [Theory]
    [InlineData(HttpMethod.Get, 0)]
    [InlineData(HttpMethod.Post, 1)]
    [InlineData(HttpMethod.Head, 2)]
    [InlineData(HttpMethod.Put, 4)]
    [InlineData(HttpMethod.Delete, 5)]
    public void Methods_MatchTheNumbersTheServiceTakes(HttpMethod method, int expected)
        => Assert.Equal(expected, (int)method);

    [Fact]
    public void Header_FindsAHeaderWhateverItsCase()
    {
        var response = new HttpResponse(
            200, [], "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nX-Count: 7\r\n");
        Assert.Equal("text/plain", response.Header("content-type"));
        Assert.Equal("7", response.Header("X-COUNT"));
    }

    [Fact]
    public void Header_AnswersNothingForAHeaderTheServerDidNotSend()
    {
        var response = new HttpResponse(200, [], "Content-Type: text/plain\r\n");
        Assert.Null(response.Header("Location"));
        // A status line has no colon before its first space, so it must not be read as a header, and a
        // block the server never sent must not be read at all.
        Assert.Null(new HttpResponse(200, []).Header("Content-Type"));
    }
}
