// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System;
using System.Runtime.InteropServices;

namespace SharpProspero.Payload.Services;

/// <summary>
/// Installs an application from a local directory in a payload context. Wraps
/// <c>sceAppInstUtilInitialize</c> and <c>sceAppInstUtilAppInstallTitleDir</c> from
/// <c>libSceAppInstUtil</c>.
/// </summary>
/// <remarks>
/// Requires <c>libSceAppInstUtil</c> and <c>libSceIpmi</c> in the payload's DT_NEEDED list.
/// The install operation reads a previously unpacked application directory at a path like
/// <c>/user/app/</c> and registers it with the system using the given title identifier.
/// </remarks>
public static unsafe partial class PayloadAppInstaller
{
    private const string Lib = "libSceAppInstUtil";

    /// <summary>
    /// Initialises the application install utility service.
    /// </summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceAppInstUtilInitialize();

    /// <summary>
    /// Installs an application from <paramref name="path"/> with the given
    /// <paramref name="titleId"/>. The path should be a directory containing the unpacked
    /// application files.
    /// </summary>
    /// <param name="titleId">A NUL-terminated UTF-8 title identifier (e.g. "FAKE02932\0").</param>
    /// <param name="path">A NUL-terminated UTF-8 filesystem path (e.g. "/user/app/\0").</param>
    /// <param name="param">Reserved parameter, pass null.</param>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceAppInstUtilAppInstallTitleDir(byte* titleId, byte* path, void* param);

    /// <summary>
    /// Initialises the install utility and installs an application in a single call.
    /// </summary>
    /// <param name="titleId">A NUL-terminated UTF-8 title identifier.</param>
    /// <param name="path">A NUL-terminated UTF-8 filesystem path.</param>
    /// <returns>Zero on success, or the first non-zero error code.</returns>
    public static int InstallFromDirectory(ReadOnlySpan<byte> titleId, ReadOnlySpan<byte> path)
    {
        int result = sceAppInstUtilInitialize();
        if (result != 0) return result;

        fixed (byte* pTitle = titleId)
        fixed (byte* pPath = path)
            return sceAppInstUtilAppInstallTitleDir(pTitle, pPath, null);
    }

    /// <summary>
    /// Uninstalls the application with the given <paramref name="titleId"/>.
    /// </summary>
    /// <param name="titleId">A NUL-terminated UTF-8 title identifier.</param>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceAppInstUtilAppUnInstall(byte* titleId);

    /// <summary>
    /// Installs all pending applications. On firmware 12.00 and later this is the required
    /// entry point; the per-directory variant may not be available.
    /// </summary>
    /// <param name="param">Reserved parameter, pass null.</param>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceAppInstUtilAppInstallAll(void* param);

    /// <summary>
    /// Convenience wrapper for <see cref="sceAppInstUtilAppUnInstall"/> that accepts a span.
    /// </summary>
    public static int Uninstall(ReadOnlySpan<byte> titleId)
    {
        fixed (byte* p = titleId)
            return sceAppInstUtilAppUnInstall(p);
    }

    /// <summary>
    /// Initialises the install utility and installs all pending applications in a single call.
    /// </summary>
    /// <returns>Zero on success, or the first non-zero error code.</returns>
    public static int InstallAll()
    {
        int result = sceAppInstUtilInitialize();
        if (result != 0) return result;
        return sceAppInstUtilAppInstallAll(null);
    }

    /// <summary>
    /// Installs an application from a package file (.pkg) with optional installation
    /// parameters. This is a different API from <see cref="sceAppInstUtilAppInstallTitleDir"/>
    /// which installs from a directory.
    /// </summary>
    /// <param name="pkgPath">A NUL-terminated UTF-8 path to the .pkg file.</param>
    /// <param name="param">Installation parameters, or null for defaults.</param>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int sceAppInstUtilInstallByPackage(byte* pkgPath, void* param);
}
