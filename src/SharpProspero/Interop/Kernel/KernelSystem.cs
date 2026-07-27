// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Kernel;

/// <summary>
/// The system software version, as the kernel reports it. The caller sets <see cref="Size"/> to the
/// size of this block; the kernel fills the printable string and the packed value.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 40)]
public unsafe struct SceKernelSwVersion
{
    /// <summary>The size of this block, in bytes. Set before the call.</summary>
    public ulong Size;

    /// <summary>The version as text, for example "11.020.000". NUL-terminated ASCII.</summary>
    public fixed byte VersionString[28];

    /// <summary>
    /// The version packed into a word: the major byte, the minor byte, then the rest. The high half
    /// carries the major and minor a requirement compares against.
    /// </summary>
    public uint Version;
}

/// <summary>Kernel queries about the system itself.</summary>
public static unsafe partial class KernelSystem
{
    private const string Lib = "libkernel";

    // The library the console identifier is published under, which is not the one above.
    private const string OpenPsIdLib = "libSceOpenPsId";

    /// <summary>
    /// Reads the system software version. Set <see cref="SceKernelSwVersion.Size"/> to the block size
    /// first. Zero on success, or a negative error code.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceKernelGetProsperoSystemSwVersion(SceKernelSwVersion* version);

    /// <summary>
    /// Reads the highest SDK version the system will accept a module built against, into
    /// <paramref name="version"/>. The value is packed like a module's own requirement (the major and
    /// minor in the high half); the low bits are set to the maximum patch. A module built against more
    /// than this is rejected at load, so this is the ceiling a build can safely target for the running
    /// system. Zero on success, or a negative error code; <paramref name="version"/> must not be null.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int sceKernelGetAllowedSdkVersionOnSystem(uint* version);

    /// <summary>
    /// Reads the console's 16-byte open identifier into <paramref name="openId"/>. Zero on success.
    /// </summary>
    /// <remarks>
    /// The same module carries this one, but publishes it under a library of its own rather than the
    /// one every other call here belongs to, so it is asked for by that name. Naming the usual library
    /// asks for something nothing publishes, and a module whose imports do not all bind never reaches
    /// its first instruction.
    /// </remarks>
    [LibraryImport(OpenPsIdLib)]
    public static partial int sceKernelGetOpenPsId(byte* openId);

    /// <summary>
    /// Reads a system value named by a dotted string (for example <c>hw.ncpu</c>) into
    /// <paramref name="oldp"/>. This is the libc-style call: it returns -1 and sets errno on failure,
    /// unlike the sce* calls. The size in and out is <paramref name="oldlenp"/>.
    /// </summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sysctlbyname(string name, void* oldp, nuint* oldlenp, void* newp, nuint newlen);
}
