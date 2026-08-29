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

    /// <summary>Third-level MIB: all processes (one entry per pid).</summary>
    public const int KernProcPid = 0;

    /// <summary>
    /// Finds the process identifier of a running process by its command name.
    /// </summary>
    /// <param name="name">The NUL-terminated process command name to search for.</param>
    /// <returns>The pid of the first matching process, or -1 if not found.</returns>
    public static int FindPidByName(byte* name)
    {
        int* mib = stackalloc int[] { CtlKern, KernProc, KernProcProc };
        nuint len = 0;

        if (sysctl(mib, 3, null, &len, null, 0) != 0 || len == 0)
            return -1;

        byte* buf = stackalloc byte[(int)(len < 65536 ? len : 65536)];
        nuint actualLen = (nuint)(len < 65536 ? len : 65536);
        if (sysctl(mib, 3, buf, &actualLen, null, 0) != 0)
            return -1;

        // Walk the kinfo_proc array. On FreeBSD, ki_pid is at offset 72 (int32) and
        // ki_comm is at offset 447 (char[20]). Each kinfo_proc is 1088 bytes on this platform.
        const int KinfoSize = 1088;
        const int KiPidOffset = 72;
        const int KiCommOffset = 447;
        const int KiCommMax = 20;

        int count = (int)(actualLen / KinfoSize);
        for (int i = 0; i < count; i++)
        {
            byte* entry = buf + i * KinfoSize;
            byte* comm = entry + KiCommOffset;
            int nameLen = 0;
            while (nameLen < KiCommMax && name[nameLen] != 0) nameLen++;

            bool match = true;
            for (int j = 0; j < nameLen; j++)
            {
                if (comm[j] != name[j]) { match = false; break; }
            }
            if (match && comm[nameLen] == 0)
            {
                return *(int*)(entry + KiPidOffset);
            }
        }
        return -1;
    }
}
