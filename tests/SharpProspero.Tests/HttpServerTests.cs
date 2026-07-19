// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Platform;
using Xunit;

namespace SharpProspero.Tests;

public sealed class HttpServerTests
{
    [Fact]
    public void ResponseBuilders_SetStatusAndContentType()
    {
        HttpServerResponse text = HttpServerResponse.Text("hi");
        Assert.Equal(200, text.StatusCode);
        Assert.StartsWith("text/plain", text.ContentType);

        HttpServerResponse html = HttpServerResponse.Html("<b>x</b>");
        Assert.StartsWith("text/html", html.ContentType);

        HttpServerResponse json = HttpServerResponse.Json("{}");
        Assert.StartsWith("application/json", json.ContentType);

        HttpServerResponse missing = HttpServerResponse.NotFound();
        Assert.Equal(404, missing.StatusCode);

        HttpServerResponse redirect = HttpServerResponse.Redirect("/home");
        Assert.Equal(302, redirect.StatusCode);
        Assert.Equal("/home", redirect.Headers["Location"]);
    }

    [Fact]
    public void BuildResponseHead_HasStatusLineAndHeaders()
    {
        HttpServerResponse response = HttpServerResponse.Json("{\"ok\":true}");
        response.Headers["X-Test"] = "1";
        string head = HttpServer.BuildResponseHead(response);

        Assert.StartsWith("HTTP/1.1 200 OK\r\n", head);
        Assert.Contains("Content-Type: application/json; charset=utf-8\r\n", head);
        Assert.Contains("Content-Length: 11\r\n", head);
        Assert.Contains("Connection: close\r\n", head);
        Assert.Contains("X-Test: 1\r\n", head);
        Assert.EndsWith("\r\n\r\n", head);
    }

    [Theory]
    [InlineData("/plain", "/plain")]
    [InlineData("/a%20b", "/a b")]
    [InlineData("/%2Fusb0%2FMy%20File.txt", "//usb0/My File.txt")]
    [InlineData("/caf%C3%A9", "/café")]
    [InlineData("/bad%zz", "/bad%zz")]
    public void PercentDecode_DecodesEscapes(string input, string expected)
    {
        Assert.Equal(expected, HttpServer.PercentDecode(input));
    }
}
