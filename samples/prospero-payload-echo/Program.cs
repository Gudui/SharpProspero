// The smallest payload the toolchain builds: one call to the kernel log helper and a zero
// return. Useful as a bring-up probe when a larger payload is failing before its own code
// runs.

using System.Runtime.InteropServices;

namespace SampleApp;

internal static unsafe partial class Program
{
    // The print helper is a plain C symbol emitted by the payload start object in the same
    // image, so the linker binds the call at link time. The library name on the attribute is a
    // placeholder the source generator needs and never reaches the loader.
    [LibraryImport("libScePosix", EntryPoint = "__prospero_klog")]
    private static partial void Klog(byte* message);

    [UnmanagedCallersOnly(EntryPoint = "__managed__Main")]
    public static int Main(void* args)
    {
        fixed (byte* p = "sp:echo:hello\n"u8)
            Klog(p);
        return 0;
    }
}
