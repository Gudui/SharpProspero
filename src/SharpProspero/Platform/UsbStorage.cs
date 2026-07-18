// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpProspero.Platform;

/// <summary>A connected USB mass-storage device and where the system mounted it.</summary>
/// <param name="Id">The device id the system assigns it.</param>
/// <param name="MountPath">The path it is mounted at, for example <c>/mnt/usb0</c>, or empty.</param>
public readonly record struct UsbDevice(uint Id, string MountPath);

/// <summary>
/// Finds connected USB mass-storage devices and the path each is mounted at, so a module can browse a
/// USB drive. Read the mount path, then read the files under it with the file APIs. Open it, list the
/// devices, dispose it.
/// </summary>
/// <remarks>
/// The service is not part of the module set a title links against, so it is loaded at run time and
/// its entry points are resolved by name. Reading a mounted path still depends on the process holding
/// the permission for it.
/// </remarks>
/// <example>
/// <code>
/// using var usb = UsbStorage.Open();
/// foreach (UsbDevice device in usb.ListDevices())
///     foreach (DirectoryEntry entry in FileSystem.EnumerateDirectory(device.MountPath))
///         Console.WriteLine(entry.Name);
/// </code>
/// </example>
public sealed unsafe class UsbStorage : IDisposable
{
    /// <summary>The module that carries the USB storage service.</summary>
    public const string ModulePath = "/system/common/lib/libSceUsbStorage.sprx";

    // The service reports at most eight devices; the buffer is sized above that.
    private const int MaxDevices = 16;
    private const int MountPathLength = 128;

    private readonly SystemLibrary _library;
    private readonly delegate* unmanaged<int> _term;
    private readonly delegate* unmanaged<uint*, int*, int> _getDeviceList;
    private readonly delegate* unmanaged<uint, byte*, int> _getMountPoint;
    private readonly delegate* unmanaged<int> _requestMap;
    private readonly delegate* unmanaged<uint, byte*, int> _requestUnmap;
    private bool _disposed;

    private UsbStorage(SystemLibrary library, delegate* unmanaged<int> term,
        delegate* unmanaged<uint*, int*, int> getDeviceList,
        delegate* unmanaged<uint, byte*, int> getMountPoint,
        delegate* unmanaged<int> requestMap,
        delegate* unmanaged<uint, byte*, int> requestUnmap)
    {
        _library = library;
        _term = term;
        _getDeviceList = getDeviceList;
        _getMountPoint = getMountPoint;
        _requestMap = requestMap;
        _requestUnmap = requestUnmap;
    }

    /// <summary>Loads the USB storage service and starts it.</summary>
    /// <exception cref="ProsperoException">The service could not be loaded or started.</exception>
    public static UsbStorage Open()
    {
        SystemLibrary library = SystemLibrary.Open(ModulePath);
        try
        {
            var initialize = (delegate* unmanaged<void*, int>)library.GetFunction("sceUsbStorageInit");
            var term = (delegate* unmanaged<int>)library.GetFunction("sceUsbStorageTerm");
            var getDeviceList = (delegate* unmanaged<uint*, int*, int>)library.GetFunction("sceUsbStorageGetDeviceList");
            var getMountPoint = (delegate* unmanaged<uint, byte*, int>)library.GetFunction("sceUsbStorageGetMountPointOfShellCore");
            var requestMap = (delegate* unmanaged<int>)library.GetFunction("sceUsbStorageRequestMap");
            var requestUnmap = (delegate* unmanaged<uint, byte*, int>)library.GetFunction("sceUsbStorageRequestUnmap");

            // The parameter is a thread attribute for the service's own thread; null takes the defaults.
            SceResult.ThrowIfFailed(initialize(null), "sceUsbStorageInit");
            return new UsbStorage(library, term, getDeviceList, getMountPoint, requestMap, requestUnmap);
        }
        catch
        {
            library.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Lists the connected USB mass-storage devices and where each is mounted. An empty list means no
    /// device is connected.
    /// </summary>
    /// <exception cref="ProsperoException">The device list could not be read.</exception>
    public IReadOnlyList<UsbDevice> ListDevices()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        uint* ids = stackalloc uint[MaxDevices];
        int count = 0;
        SceResult.ThrowIfFailed(_getDeviceList(ids, &count), "sceUsbStorageGetDeviceList");
        if (count < 0)
            count = 0;
        else if (count > MaxDevices)
            count = MaxDevices;

        var devices = new List<UsbDevice>(count);
        byte* path = stackalloc byte[MountPathLength];
        for (int i = 0; i < count; i++)
        {
            new Span<byte>(path, MountPathLength).Clear();
            int rc = _getMountPoint(ids[i], path);
            string mount = rc == 0 ? ReadUtf8(path, MountPathLength) : string.Empty;
            devices.Add(new UsbDevice(ids[i], mount));
        }
        return devices;
    }

    /// <summary>
    /// Asks the system to map a connected USB mass-storage device so it appears in
    /// <see cref="ListDevices"/> at its mount path. The system also maps a device on its own, so this
    /// is needed only to request a mapping explicitly.
    /// </summary>
    /// <exception cref="ProsperoException">The request was rejected.</exception>
    public void RequestMap()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SceResult.ThrowIfFailed(_requestMap(), "sceUsbStorageRequestMap");
    }

    /// <summary>Unmaps <paramref name="device"/> from its mount path.</summary>
    /// <exception cref="ProsperoException">The request was rejected.</exception>
    public void RequestUnmap(UsbDevice device)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(device.MountPath);

        int count = Encoding.UTF8.GetByteCount(device.MountPath);
        byte* directory = stackalloc byte[count + 1];
        Encoding.UTF8.GetBytes(device.MountPath, new Span<byte>(directory, count));
        directory[count] = 0;
        SceResult.ThrowIfFailed(_requestUnmap(device.Id, directory), "sceUsbStorageRequestUnmap");
    }

    /// <summary>Stops the service and unloads the module.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _term();
        _library.Dispose();
    }

    private static string ReadUtf8(byte* start, int maxLength)
    {
        int length = 0;
        while (length < maxLength && start[length] != 0)
            length++;
        return length == 0 ? string.Empty : Encoding.UTF8.GetString(start, length);
    }
}
