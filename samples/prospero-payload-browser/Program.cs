// Initialises UserService, calls
// sceSystemServiceLaunchWebBrowser to open a URL, then tears down UserService.

using System;
using System.Runtime.InteropServices;

namespace SampleApp;

internal static unsafe partial class Program
{
    private const string BrowserUrl = "http://192.168.1.1";

    [LibraryImport("libSceUserService", EntryPoint = "sceUserServiceInitialize")]
    private static partial int UserServiceInitialize(void* param);

    [LibraryImport("libSceUserService", EntryPoint = "sceUserServiceTerminate")]
    private static partial int UserServiceTerminate();

    [LibraryImport("libSceSystemService", EntryPoint = "sceSystemServiceLaunchWebBrowser")]
    private static partial int LaunchWebBrowser(byte* uri, void* param);

    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    public static int Main(void* args)
    {
        int ret = UserServiceInitialize(null);
        if (ret != 0)
            return -1;

        Span<byte> url = stackalloc byte[BrowserUrl.Length + 1];
        for (int i = 0; i < BrowserUrl.Length; i++)
            url[i] = (byte)BrowserUrl[i];
        url[BrowserUrl.Length] = 0;

        fixed (byte* pUrl = url)
        {
            ret = LaunchWebBrowser(pUrl, null);
        }

        UserServiceTerminate();
        return ret != 0 ? -2 : 0;
    }
}
