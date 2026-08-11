// A SharpProspero payload that removes the filesystem jail from a running application. A loader maps
// it into an already-exploited process and starts it; it walks the kernel's process list, finds the
// first process whose name matches TargetName, rewrites its root and jail directory vnodes to the
// kernel's real root, sets its credential to root with full capabilities, and returns. After this
// the target application can enumerate the full filesystem.
//
// The kernel addresses and structure offsets are for firmware 10.01. A payload reaches the kernel
// through the pipe primitive the loader hands in the payload_args block, not through a separate
// exploit - the host process is already exploited when the payload starts.

using SharpProspero.Payload;

namespace SampleApp;

internal static class Program
{
    private const int TargetNameLength = 9;

    private static int Main()
    {
        unsafe
        {
            PayloadArgs* args = PayloadEntryPoint.Args;
            if (args == null)
                return -1;
            var io = new PayloadKernelIo(args);

            byte* name = stackalloc byte[] { (byte)'e', (byte)'b', (byte)'o', (byte)'o', (byte)'t',
                (byte)'.', (byte)'b', (byte)'i', (byte)'n' };
            ulong proc = PayloadKernel.FindProcessByName(io, name, TargetNameLength);
            if (proc == 0)
                return -1;

            PayloadKernel.RemoveJail(io, proc);
            PayloadKernel.EscalateCredentials(io, proc);

            return 0;
        }
    }
}
