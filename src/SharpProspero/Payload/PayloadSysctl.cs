// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Payload;

/// <summary>
/// FreeBSD <c>sysctl</c> interface for a payload context. The SDK <c>ps</c> sample uses
/// <c>sysctl(KERN_PROC)</c> to enumerate running processes, and the <c>mntinfo</c> sample
/// uses <c>getmntinfo</c> (which internally calls <c>getvfsstat</c>, a sysctl variant) to
/// enumerate mount points. These wrappers provide direct access to the sysctl mechanism
/// through <c>libc</c>.
/// </summary>
/// <remarks>
/// <para>FreeBSD sysctl MIB values for process enumeration:</para>
/// <list type="bullet">
/// <item><description><c>CTL_KERN = 1</c></description></item>
/// <item><description><c>KERN_PROC = 14</c></description></item>
/// <item><description><c>KERN_PROC_PROC = 8</c> (all processes, one per process group leader)</description></item>
/// </list>
/// </remarks>
public static unsafe partial class PayloadSysctl
{
    private const string Lib = "libc";

    /// <summary>Top-level MIB: kernel parameters.</summary>
    public const int CtlKern = 1;

    /// <summary>Second-level MIB: process information.</summary>
    public const int KernProc = 14;

    /// <summary>Third-level MIB: all processes (one entry per process group leader).</summary>
    public const int KernProcProc = 8;

    /// <summary>Mount wait flag for <see cref="getmntinfo"/>.</summary>
    public const int MntWait = 1;

    /// <summary>Mount no-wait flag for <see cref="getmntinfo"/>.</summary>
    public const int MntNowait = 2;

    /// <summary>
    /// Reads or writes a kernel parameter identified by <paramref name="name"/> (a MIB array).
    /// </summary>
    /// <param name="name">An array of MIB integers identifying the parameter.</param>
    /// <param name="namelen">The number of integers in <paramref name="name"/>.</param>
    /// <param name="oldp">Buffer to receive the current value, or null to query the size.</param>
    /// <param name="oldlenp">On entry, the buffer size; on exit, the actual size.</param>
    /// <param name="newp">New value to set, or null for a read-only query.</param>
    /// <param name="newlen">Size of <paramref name="newp"/>.</param>
    /// <returns>Zero on success, or -1 on error (sets errno).</returns>
    [LibraryImport(Lib)]
    public static partial int sysctl(int* name, uint namelen, void* oldp, nuint* oldlenp,
        void* newp, nuint newlen);

    /// <summary>
    /// Returns the list of mounted filesystems. The buffer is allocated by the library.
    /// This is the mechanism the SDK <c>mntinfo</c> sample uses.
    /// </summary>
    /// <param name="bufp">On success, points to an array of <c>statfs</c> structures.</param>
    /// <param name="mode">Either <see cref="MntWait"/> or <see cref="MntNowait"/>.</param>
    /// <returns>The number of mounted filesystems, or -1 on error.</returns>
    [LibraryImport(Lib)]
    public static partial int getmntinfo(void** bufp, int mode);
}
