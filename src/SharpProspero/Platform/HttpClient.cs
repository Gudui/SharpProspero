// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.Net;
using System;
using System.IO;

namespace SharpProspero.Platform;

/// <summary>A downloaded HTTP response: its status code and body.</summary>
/// <param name="StatusCode">The HTTP status code, for example 200.</param>
/// <param name="Body">The response body.</param>
public readonly record struct HttpResponse(int StatusCode, byte[] Body)
{
    /// <summary>True when the status code is in the 2xx success range.</summary>
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

/// <summary>
/// Downloads over HTTP and HTTPS, for fetching a file or a package from a URL. Create one, make as
/// many requests as needed, dispose it. Creating it brings up the network memory pool, the TLS
/// context, and the HTTP service in the order they depend on each other.
/// </summary>
/// <example>
/// <code>
/// using var http = HttpClient.Create();
/// HttpResponse response = http.Get("https://example.com/file.bin");
/// if (response.IsSuccess)
///     Save(response.Body);
/// </code>
/// </example>
public sealed unsafe class HttpClient : IDisposable
{
    // The pool the secure layer works in: its documented floor for up to three connections at once,
    // with room left for the certificates a real server presents.
    private const int SslPoolBytes = 304 * 1024;

    private readonly int _netMemId;
    private readonly int _sslCtxId;
    private readonly int _httpCtxId;
    private readonly int _templateId;
    private bool _disposed;

    private HttpClient(int netMemId, int sslCtxId, int httpCtxId, int templateId)
    {
        _netMemId = netMemId;
        _sslCtxId = sslCtxId;
        _httpCtxId = httpCtxId;
        _templateId = templateId;
    }

    /// <summary>
    /// Brings up the download services. The pool comes first (it is the network init), then the TLS
    /// context, then the HTTP service, then a request template.
    /// </summary>
    /// <exception cref="ProsperoException">A service could not be started.</exception>
    public static HttpClient Create(string userAgent = "SharpProspero/1.00")
    {
        ArgumentNullException.ThrowIfNull(userAgent);

        int netMemId = NetPool.sceNetPoolCreate("sharpprospero_http", 0x4000, 0);
        SceResult.ThrowIfFailed(netMemId, nameof(NetPool.sceNetPoolCreate));

        // The pool the secure layer works in. Its floor is 256 KiB for up to three connections at once,
        // plus 4 KiB for each certificate loaded, and half that was being asked for. Nothing checks the
        // figure when it is set: it is rounded up, mapped, and turned into a pool, and the shortfall
        // surfaces later as an allocation failure inside the layer, on a connection rather than here.
        int sslCtxId = Ssl.sceSslInit(SslPoolBytes);
        if (sslCtxId < 0)
        {
            NetPool.sceNetPoolDestroy(netMemId);
            SceResult.ThrowIfFailed(sslCtxId, nameof(Ssl.sceSslInit));
        }

        int httpCtxId = Http.sceHttpInit(netMemId, sslCtxId, 0x10000);
        if (httpCtxId < 0)
        {
            Ssl.sceSslTerm(sslCtxId);
            NetPool.sceNetPoolDestroy(netMemId);
            SceResult.ThrowIfFailed(httpCtxId, nameof(Http.sceHttpInit));
        }

        int templateId = Http.sceHttpCreateTemplate(httpCtxId, userAgent, Http.Version11, 1);
        if (templateId < 0)
        {
            Http.sceHttpTerm(httpCtxId);
            Ssl.sceSslTerm(sslCtxId);
            NetPool.sceNetPoolDestroy(netMemId);
            SceResult.ThrowIfFailed(templateId, nameof(Http.sceHttpCreateTemplate));
        }

        return new HttpClient(netMemId, sslCtxId, httpCtxId, templateId);
    }

    /// <summary>Downloads <paramref name="url"/> with a GET request.</summary>
    /// <exception cref="ProsperoException">The request failed.</exception>
    public HttpResponse Get(string url)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(url);

        int connId = Http.sceHttpCreateConnectionWithURL(_templateId, url, 1);
        SceResult.ThrowIfFailed(connId, nameof(Http.sceHttpCreateConnectionWithURL));
        try
        {
            int reqId = Http.sceHttpCreateRequestWithURL(connId, Http.MethodGet, url, 0);
            SceResult.ThrowIfFailed(reqId, nameof(Http.sceHttpCreateRequestWithURL));
            try
            {
                SceResult.ThrowIfFailed(Http.sceHttpSendRequest(reqId, null, 0), nameof(Http.sceHttpSendRequest));

                int status = 0;
                SceResult.ThrowIfFailed(Http.sceHttpGetStatusCode(reqId, &status), nameof(Http.sceHttpGetStatusCode));

                int lengthKnown = 0;
                ulong contentLength = 0;
                SceResult.ThrowIfFailed(
                    Http.sceHttpGetResponseContentLength(reqId, &lengthKnown, &contentLength),
                    nameof(Http.sceHttpGetResponseContentLength));

                byte[] body = ReadBody(reqId, lengthKnown == 0, contentLength);
                return new HttpResponse(status, body);
            }
            finally
            {
                Http.sceHttpDeleteRequest(reqId);
            }
        }
        finally
        {
            Http.sceHttpDeleteConnection(connId);
        }
    }

    // Reads the body until the read returns nothing more. When the length is known, the loop also
    // stops once that many bytes have arrived; when it is not, it stops at the first empty read. This
    // handles both, without relying on the length alone.
    private static byte[] ReadBody(int reqId, bool lengthKnown, ulong contentLength)
    {
        // Size the buffer to the content length up front when it is known, so a large download does
        // not grow the stream by repeated doubling.
        var output = lengthKnown && contentLength <= int.MaxValue
            ? new MemoryStream((int)contentLength)
            : new MemoryStream();
        byte[] chunk = new byte[64 * 1024];
        fixed (byte* buffer = chunk)
        {
            while (true)
            {
                int read = Http.sceHttpReadData(reqId, buffer, (nuint)chunk.Length);
                SceResult.ThrowIfFailed(read, nameof(Http.sceHttpReadData));
                if (read == 0)
                    break;
                output.Write(chunk, 0, read);
                if (lengthKnown && (ulong)output.Length >= contentLength)
                    break;
            }
        }
        return output.ToArray();
    }

    /// <summary>Shuts the download services down, in the reverse of the order they came up.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Http.sceHttpDeleteTemplate(_templateId);
        Http.sceHttpTerm(_httpCtxId);
        Ssl.sceSslTerm(_sslCtxId);
        NetPool.sceNetPoolDestroy(_netMemId);
    }
}
