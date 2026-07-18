// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Runtime.InteropServices;
using System.Text;
using SharpProspero.Interop;
using Native = SharpProspero.Interop.SystemService.SystemService;

namespace SharpProspero.Platform;

/// <summary>
/// Starts another installed application by its title id, so a module can act as a launcher. The
/// running application is replaced by the one started, so the call does not return to the caller when
/// it succeeds.
/// </summary>
/// <example>
/// <code>
/// AppLauncher.Launch("CUSA00000");
/// </code>
/// </example>
public static unsafe class AppLauncher
{
    /// <summary>The exact length of a title id.</summary>
    public const int TitleIdLength = 9;

    /// <summary>
    /// Starts the installed application with <paramref name="titleId"/> (a 9-character id), passing
    /// <paramref name="args"/> as its launch arguments. On success the current application is replaced
    /// and the call does not return.
    /// </summary>
    /// <param name="titleId">The 9-character title id to start, for example <c>CUSA00000</c>.</param>
    /// <param name="args">Launch arguments passed to the started application. May be empty.</param>
    /// <exception cref="ArgumentException"><paramref name="titleId"/> is not 9 characters.</exception>
    /// <exception cref="ProsperoException">The application could not be started.</exception>
    public static void Launch(string titleId, params string[] args)
    {
        ArgumentException.ThrowIfNullOrEmpty(titleId);
        if (titleId.Length != TitleIdLength)
            throw new ArgumentException($"A title id is {TitleIdLength} characters.", nameof(titleId));

        Span<byte> id = stackalloc byte[TitleIdLength + 1];
        int written = Encoding.UTF8.GetBytes(titleId, id);
        id[written] = 0;

        if (args is null || args.Length == 0)
        {
            int rc;
            fixed (byte* pid = id)
                rc = Native.sceSystemServiceLaunchApp(pid, null, null);
            SceResult.ThrowIfFailed(rc, nameof(Native.sceSystemServiceLaunchApp));
            return;
        }

        // Build a null-terminated array of C-string arguments on the unmanaged heap. The array is
        // zeroed so that if encoding an argument throws mid-loop, the unset slots stay null and the
        // cleanup below frees only the entries it actually allocated.
        byte** argv = (byte**)NativeMemory.AllocZeroed((nuint)((args.Length + 1) * sizeof(nint)));
        try
        {
            for (int i = 0; i < args.Length; i++)
            {
                int count = Encoding.UTF8.GetByteCount(args[i]);
                var s = (byte*)NativeMemory.Alloc((nuint)(count + 1));
                Encoding.UTF8.GetBytes(args[i], new Span<byte>(s, count));
                s[count] = 0;
                argv[i] = s;
            }
            argv[args.Length] = null;

            int rc;
            fixed (byte* pid = id)
                rc = Native.sceSystemServiceLaunchApp(pid, argv, null);
            SceResult.ThrowIfFailed(rc, nameof(Native.sceSystemServiceLaunchApp));
        }
        finally
        {
            for (int i = 0; i < args.Length; i++)
                if (argv[i] is not null)
                    NativeMemory.Free(argv[i]);
            NativeMemory.Free(argv);
        }
    }
}
