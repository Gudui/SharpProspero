// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using System;
using System.Text;

namespace SharpProspero.Platform;

/// <summary>
/// Installs a package from a file on the console. The install service is not part of the module set a
/// title links against, so it is loaded at run time and its entry points are resolved by name. Open
/// it once, install one or more packages, and dispose it to shut the service down.
/// </summary>
/// <example>
/// <code>
/// using var installer = PackageInstaller.Open();
/// installer.Install("/data/homebrew.pkg");
/// </code>
/// </example>
public sealed unsafe class PackageInstaller : IDisposable
{
    /// <summary>The module that carries the install service.</summary>
    public const string ModulePath = "/system/common/lib/libSceAppInstUtil.sprx";

    private readonly SystemLibrary _library;
    private readonly delegate* unmanaged<int> _terminate;
    private readonly delegate* unmanaged<byte*, ulong, int> _installPackage;
    private readonly delegate* unmanaged<byte*, byte*, int> _appExists;
    private readonly delegate* unmanaged<byte*, ulong*, int> _appGetSize;
    private readonly delegate* unmanaged<byte*, int> _appUninstall;
    private readonly delegate* unmanaged<byte*, uint, int> _appUninstall2;
    private bool _disposed;

    private PackageInstaller(SystemLibrary library, delegate* unmanaged<int> terminate,
        delegate* unmanaged<byte*, ulong, int> installPackage,
        delegate* unmanaged<byte*, byte*, int> appExists,
        delegate* unmanaged<byte*, ulong*, int> appGetSize,
        delegate* unmanaged<byte*, int> appUninstall,
        delegate* unmanaged<byte*, uint, int> appUninstall2)
    {
        _library = library;
        _terminate = terminate;
        _installPackage = installPackage;
        _appExists = appExists;
        _appGetSize = appGetSize;
        _appUninstall = appUninstall;
        _appUninstall2 = appUninstall2;
    }

    /// <summary>
    /// Loads the install service and starts it. Throws when the module is missing, an entry point is
    /// absent, or the service refuses to start.
    /// </summary>
    /// <exception cref="ProsperoException">The service could not be loaded or started.</exception>
    public static PackageInstaller Open()
    {
        SystemLibrary library = SystemLibrary.Open(ModulePath);
        try
        {
            var initialize = (delegate* unmanaged<int>)library.GetFunction("sceAppInstUtilInitialize");
            var terminate = (delegate* unmanaged<int>)library.GetFunction("sceAppInstUtilTerminate");
            var install = (delegate* unmanaged<byte*, ulong, int>)library.GetFunction("sceAppInstUtilAppInstallPkg");
            var appExists = (delegate* unmanaged<byte*, byte*, int>)library.GetFunction("sceAppInstUtilAppExists");
            var appGetSize = (delegate* unmanaged<byte*, ulong*, int>)library.GetFunction("sceAppInstUtilAppGetSize");
            var appUninstall = (delegate* unmanaged<byte*, int>)library.GetFunction("sceAppInstUtilAppUnInstall");

            // The option-taking uninstall is newer than the earliest supported system (it appears from
            // 3.00), so resolve it optionally: on a firmware that predates it, Open still succeeds and
            // the option-taking Uninstall reports it rather than the whole installer failing to load.
            library.TryGetFunction("sceAppInstUtilAppUnInstall2", out void* appUninstall2Ptr);
            var appUninstall2 = (delegate* unmanaged<byte*, uint, int>)appUninstall2Ptr;

            SceResult.ThrowIfFailed(initialize(), "sceAppInstUtilInitialize");
            return new PackageInstaller(library, terminate, install, appExists, appGetSize, appUninstall, appUninstall2);
        }
        catch
        {
            library.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Installs the package at <paramref name="path"/>, an absolute path to a file on the console.
    /// The call hands the request to the service; the install itself continues in the background.
    /// </summary>
    /// <param name="path">Absolute path of the package file.</param>
    /// <param name="option">Reserved; leave zero unless a specific value is called for.</param>
    /// <exception cref="ProsperoException">The service rejected the request.</exception>
    public void Install(string path, ulong option = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int byteCount = Encoding.UTF8.GetByteCount(path);
        Span<byte> buffer = byteCount < 512 ? stackalloc byte[byteCount + 1] : new byte[byteCount + 1];
        int written = Encoding.UTF8.GetBytes(path, buffer);
        buffer[written] = 0;

        int rc;
        fixed (byte* p = buffer)
            rc = _installPackage(p, option);
        SceResult.ThrowIfFailed(rc, "sceAppInstUtilAppInstallPkg");
    }

    /// <summary>
    /// Reports whether an application with <paramref name="titleId"/> is installed.
    /// </summary>
    /// <param name="titleId">The title id to check, for example <c>CUSA00000</c>.</param>
    /// <exception cref="ProsperoException">The service rejected the request.</exception>
    public bool AppExists(string titleId)
    {
        ArgumentException.ThrowIfNullOrEmpty(titleId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int count = Encoding.UTF8.GetByteCount(titleId);
        byte* id = stackalloc byte[count + 1];
        Encoding.UTF8.GetBytes(titleId, new Span<byte>(id, count));
        id[count] = 0;

        byte exists = 0;
        SceResult.ThrowIfFailed(_appExists(id, &exists), "sceAppInstUtilAppExists");
        return exists != 0;
    }

    /// <summary>
    /// Reads the installed size of the application with <paramref name="titleId"/>, in bytes.
    /// </summary>
    /// <exception cref="ProsperoException">The service rejected the request.</exception>
    public ulong AppGetSize(string titleId)
    {
        ArgumentException.ThrowIfNullOrEmpty(titleId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int count = Encoding.UTF8.GetByteCount(titleId);
        byte* id = stackalloc byte[count + 1];
        Encoding.UTF8.GetBytes(titleId, new Span<byte>(id, count));
        id[count] = 0;

        ulong size = 0;
        SceResult.ThrowIfFailed(_appGetSize(id, &size), "sceAppInstUtilAppGetSize");
        return size;
    }

    /// <summary>
    /// Removes the installed application with <paramref name="titleId"/>. This deletes the application
    /// and its data, so confirm the id before calling. The call hands the request to the service and
    /// returns once it is accepted; the removal itself finishes in the background.
    /// </summary>
    /// <param name="titleId">The title id to remove, for example <c>CUSA00000</c>.</param>
    /// <exception cref="ProsperoException">The service rejected the request.</exception>
    public void Uninstall(string titleId)
    {
        ArgumentException.ThrowIfNullOrEmpty(titleId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int count = Encoding.UTF8.GetByteCount(titleId);
        byte* id = stackalloc byte[count + 1];
        Encoding.UTF8.GetBytes(titleId, new Span<byte>(id, count));
        id[count] = 0;

        SceResult.ThrowIfFailed(_appUninstall(id), "sceAppInstUtilAppUnInstall");
    }

    /// <summary>
    /// Removes the installed application with <paramref name="titleId"/>, passing an
    /// <paramref name="option"/> flag to the service. This is the option-taking form of
    /// <see cref="Uninstall(string)"/>; leave <paramref name="option"/> zero for the same behavior.
    /// This deletes the application and its data, so confirm the id before calling.
    /// </summary>
    /// <param name="titleId">The title id to remove, for example <c>CUSA00000</c>.</param>
    /// <param name="option">A service option flag. Zero requests the default behavior.</param>
    /// <exception cref="ProsperoException">
    /// The service rejected the request, or a non-zero <paramref name="option"/> was passed on a system
    /// whose firmware predates the option-taking uninstall (before 3.00).
    /// </exception>
    public void Uninstall(string titleId, uint option)
    {
        ArgumentException.ThrowIfNullOrEmpty(titleId);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int count = Encoding.UTF8.GetByteCount(titleId);
        byte* id = stackalloc byte[count + 1];
        Encoding.UTF8.GetBytes(titleId, new Span<byte>(id, count));
        id[count] = 0;

        if (_appUninstall2 is null)
        {
            // The option-taking uninstall is not present on this system's firmware (it is newer). With
            // no option the plain uninstall does the same removal; with one the request cannot be
            // honored, so it is reported rather than silently dropping the option.
            if (option != 0)
                throw new ProsperoException(
                    "sceAppInstUtilAppUnInstall2 is not available on this system's firmware; "
                    + "call Uninstall(titleId) for a removal without an option.", -1);
            SceResult.ThrowIfFailed(_appUninstall(id), "sceAppInstUtilAppUnInstall");
            return;
        }

        SceResult.ThrowIfFailed(_appUninstall2(id, option), "sceAppInstUtilAppUnInstall2");
    }

    /// <summary>Shuts the service down and unloads the module.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _terminate();
        _library.Dispose();
    }
}
