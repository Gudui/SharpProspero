// Escapes the sandbox jail by rewriting the process
// rootdir vnode to the kernel's real root, then recursively enumerates "/" with opendir/
// readdir and outputs the listing via klog. Restores the original rootdir when done.

using System;
using System.Runtime.InteropServices;
using SharpProspero.Payload;

namespace SampleApp;

internal static unsafe partial class Program
{
    [LibraryImport("libScePosix", EntryPoint = "__prospero_klog")]
    private static partial void Klog(byte* message);

    [LibraryImport("libc", EntryPoint = "getpid")]
    private static partial int GetPid();

    [LibraryImport("libc", EntryPoint = "opendir")]
    private static partial void* OpenDir(byte* path);

    [LibraryImport("libc", EntryPoint = "closedir")]
    private static partial int CloseDir(void* dir);

    // FreeBSD readdir returns a pointer to struct dirent; d_name starts at offset 8.
    [LibraryImport("libc", EntryPoint = "readdir")]
    private static partial byte* ReadDir(void* dir);

    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    public static int Main(void* args)
    {
        PayloadArgs* pargs = PayloadEntryPoint.Args;
        if (pargs == null)
            return -1;

        var io = new PayloadKernelIo(pargs);

        // Save the current rootdir, then set it to the kernel root vnode.
        ulong rootvnode = io.ReadU64(KernelOffsets1001.Rootvnode);
        int pid = GetPid();
        ulong proc = PayloadKernel.FindProcessByPid(io, pid);
        if (proc == 0)
            return -2;

        ulong filedesc = io.ReadU64(proc + (ulong)KernelOffsets1001.ProcFd);
        ulong savedRootdir = io.ReadU64(filedesc + (ulong)KernelOffsets1001.FdRdir);

        // Escape jail: set rootdir to kernel's root vnode.
        io.WriteU64(filedesc + (ulong)KernelOffsets1001.FdRdir, rootvnode);

        // Enumerate root directory.
        fixed (byte* root = "/\0"u8)
        {
            ListDir(root);
        }

        // Restore the original rootdir.
        io.WriteU64(filedesc + (ulong)KernelOffsets1001.FdRdir, savedRootdir);

        return 0;
    }

    private static void ListDir(byte* basePath)
    {
        void* dir = OpenDir(basePath);
        if (dir == null)
            return;

        byte* entry;
        while ((entry = ReadDir(dir)) != null)
        {
            // struct dirent on FreeBSD: d_fileno(8), d_off(8), d_reclen(2), d_type(1), d_namlen(1), d_name[256].
            // d_name starts at offset 20 on FreeBSD 12+.
            byte* name = entry + 20;

            // Skip "." and ".."
            if (name[0] == '.' && (name[1] == 0 || (name[1] == '.' && name[2] == 0)))
                continue;

            // Build full path and log it.
            byte* fullPath = stackalloc byte[1024];
            int pos = 0;
            for (int i = 0; basePath[i] != 0 && pos < 1000; i++)
                fullPath[pos++] = basePath[i];
            if (pos > 1 && fullPath[pos - 1] != '/')
                fullPath[pos++] = (byte)'/';
            for (int i = 0; name[i] != 0 && pos < 1020; i++)
                fullPath[pos++] = name[i];
            fullPath[pos++] = (byte)'\n';
            fullPath[pos] = 0;
            Klog(fullPath);
        }

        CloseDir(dir);
    }
}
