// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Net;

/// <summary>The network memory pool the HTTP and SSL services draw from.</summary>
public static partial class NetPool
{
    private const string Lib = "libSceNet";

    /// <summary>Creates a memory pool, returning its id. This is the first network call.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sceNetPoolCreate(string name, int size, int flags);

    /// <summary>Destroys a memory pool.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetPoolDestroy(int memId);
}

/// <summary>The TLS context the HTTP service uses for secure requests.</summary>
public static partial class Ssl
{
    private const string Lib = "libSceSsl";

    /// <summary>Starts the TLS service with a pool of <paramref name="poolSize"/> bytes, returning a context id.</summary>
    [LibraryImport(Lib)]
    public static partial int sceSslInit(nuint poolSize);

    /// <summary>Stops the TLS service.</summary>
    [LibraryImport(Lib)]
    public static partial int sceSslTerm(int sslCtxId);
}

/// <summary>HTTP client bindings.</summary>
public static unsafe partial class Http
{
    private const string Lib = "libSceHttp";

    /// <summary>HTTP/1.1.</summary>
    public const int Version11 = 2;

    /// <summary>The GET method.</summary>
    public const int MethodGet = 0;

    /// <summary>The POST method.</summary>
    public const int MethodPost = 1;

    /// <summary>Starts the HTTP service on a net pool and a TLS context, returning a context id.</summary>
    [LibraryImport(Lib)]
    public static partial int sceHttpInit(int netMemId, int sslCtxId, nuint poolSize);

    /// <summary>Stops the HTTP service.</summary>
    [LibraryImport(Lib)]
    public static partial int sceHttpTerm(int httpCtxId);

    /// <summary>Creates a request template, returning its id.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sceHttpCreateTemplate(int httpCtxId, string userAgent, int httpVer, int isAutoProxyConf);

    /// <summary>Deletes a template.</summary>
    [LibraryImport(Lib)]
    public static partial int sceHttpDeleteTemplate(int tmplId);

    /// <summary>Creates a connection to the host in <paramref name="url"/>, returning its id.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sceHttpCreateConnectionWithURL(int tmplId, string url, int isEnableKeepalive);

    /// <summary>Deletes a connection.</summary>
    [LibraryImport(Lib)]
    public static partial int sceHttpDeleteConnection(int connId);

    /// <summary>Creates a request on a connection, returning its id.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sceHttpCreateRequestWithURL(int connId, int method, string url, ulong contentLength);

    /// <summary>Deletes a request.</summary>
    [LibraryImport(Lib)]
    public static partial int sceHttpDeleteRequest(int reqId);

    /// <summary>Sends a request, with optional body data.</summary>
    [LibraryImport(Lib)]
    public static partial int sceHttpSendRequest(int reqId, void* postData, nuint size);

    /// <summary>Reads the response status code.</summary>
    [LibraryImport(Lib)]
    public static partial int sceHttpGetStatusCode(int reqId, int* statusCode);

    /// <summary>
    /// Reads the response body length. <paramref name="result"/> reports whether the length is known.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceHttpGetResponseContentLength(int reqId, int* result, ulong* contentLength);

    /// <summary>Reads up to <paramref name="size"/> bytes of the response body. Returns bytes read, or 0 at the end.</summary>
    [LibraryImport(Lib)]
    public static partial int sceHttpReadData(int reqId, void* data, nuint size);

    /// <summary>Sets the connect timeout, in microseconds.</summary>
    [LibraryImport(Lib)]
    public static partial int sceHttpSetConnectTimeOut(int id, uint usec);

    /// <summary>Sets the receive timeout, in microseconds.</summary>
    [LibraryImport(Lib)]
    public static partial int sceHttpSetRecvTimeOut(int id, uint usec);
}
