// Full HTTP/2 lifecycle using libSceNet, libSceSsl,
// and libSceHttp2. Initialises the stack, creates a template and request, sends a GET
// to a configurable URL, reads the response body, and outputs it via klog.

using System;
using System.Runtime.InteropServices;

namespace SampleApp;

internal static unsafe partial class Program
{
    // ---- libSceNet ----
    [LibraryImport("libSceNet", EntryPoint = "sceNetInit")]
    private static partial int NetInit();
    [LibraryImport("libSceNet", EntryPoint = "sceNetPoolCreate")]
    private static partial int NetPoolCreate(byte* name, int size, int flags);
    [LibraryImport("libSceNet", EntryPoint = "sceNetPoolDestroy")]
    private static partial int NetPoolDestroy(int memId);

    // ---- libSceSsl ----
    [LibraryImport("libSceSsl", EntryPoint = "sceSslInit")]
    private static partial int SslInit(nuint poolSize);
    [LibraryImport("libSceSsl", EntryPoint = "sceSslTerm")]
    private static partial int SslTerm(int sslCtxId);

    // ---- libSceHttp2 ----
    [LibraryImport("libSceHttp2", EntryPoint = "sceHttp2Init")]
    private static partial int Http2Init(int netMemId, int sslCtxId, nuint poolSize, int flags);
    [LibraryImport("libSceHttp2", EntryPoint = "sceHttp2Term")]
    private static partial int Http2Term(int httpCtxId);
    [LibraryImport("libSceHttp2", EntryPoint = "sceHttp2CreateTemplate")]
    private static partial int Http2CreateTemplate(int httpCtxId, byte* agent, int httpVer, int isKeepalive);
    [LibraryImport("libSceHttp2", EntryPoint = "sceHttp2DeleteTemplate")]
    private static partial int Http2DeleteTemplate(int tmplId);
    [LibraryImport("libSceHttp2", EntryPoint = "sceHttp2CreateRequestWithURL")]
    private static partial int Http2CreateRequest(int tmplId, byte* method, byte* url, ulong contentLength);
    [LibraryImport("libSceHttp2", EntryPoint = "sceHttp2DeleteRequest")]
    private static partial int Http2DeleteRequest(int reqId);
    [LibraryImport("libSceHttp2", EntryPoint = "sceHttp2SendRequest")]
    private static partial int Http2SendRequest(int reqId, void* postData, nuint size);
    [LibraryImport("libSceHttp2", EntryPoint = "sceHttp2GetStatusCode")]
    private static partial int Http2GetStatusCode(int reqId, int* statusCode);
    [LibraryImport("libSceHttp2", EntryPoint = "sceHttp2ReadData")]
    private static partial int Http2ReadData(int reqId, byte* buf, nuint size);

    [LibraryImport("libScePosix", EntryPoint = "__prospero_klog")]
    private static partial void Klog(byte* message);

    private static int _netMemId = -1, _sslCtxId = -1, _httpCtxId = -1;
    private static int _tmplId = -1, _reqId = -1;

    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    public static int Main(void* args)
    {
        int error = 0;
        fixed (byte* agent = "http2_get/1.0\0"u8)
        fixed (byte* url = "http://192.168.1.1\0"u8)
        {
            error = Http2InitStack(agent, url);
        }

        if (error == 0)
            error = Http2Get();

        Http2Fini();
        return error;
    }

    private static int Http2InitStack(byte* agent, byte* url)
    {
        if (NetInit() != 0) return -1;
        fixed (byte* name = "http2_get\0"u8)
        {
            _netMemId = NetPoolCreate(name, 32 * 1024, 0);
            if (_netMemId < 0) return -1;
        }
        _sslCtxId = SslInit(256 * 1024);
        if (_sslCtxId < 0) return -1;
        _httpCtxId = Http2Init(_netMemId, _sslCtxId, 256 * 1024, 1);
        if (_httpCtxId < 0) return -1;
        _tmplId = Http2CreateTemplate(_httpCtxId, agent, 3, 1);
        if (_tmplId < 0) return -1;
        fixed (byte* method = "GET\0"u8)
        {
            _reqId = Http2CreateRequest(_tmplId, method, url, 0);
            if (_reqId < 0) return -1;
        }
        return 0;
    }

    private static int Http2Get()
    {
        if (Http2SendRequest(_reqId, null, 0) != 0) return -1;
        int status;
        if (Http2GetStatusCode(_reqId, &status) != 0) return -1;
        if (status == 200)
        {
            byte* buf = stackalloc byte[0x1000];
            int length;
            while ((length = Http2ReadData(_reqId, buf, 0x0FFF)) > 0)
            {
                buf[length] = 0;
                Klog(buf);
            }
        }
        return status;
    }

    private static void Http2Fini()
    {
        if (_reqId != -1) Http2DeleteRequest(_reqId);
        if (_tmplId != -1) Http2DeleteTemplate(_tmplId);
        if (_httpCtxId != -1) Http2Term(_httpCtxId);
        if (_sslCtxId != -1) SslTerm(_sslCtxId);
        if (_netMemId != -1) NetPoolDestroy(_netMemId);
    }
}
