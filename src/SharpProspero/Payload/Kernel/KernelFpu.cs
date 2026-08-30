// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

namespace SharpProspero.Payload.Kernel;

/// <summary>
/// Kernel FPU state management. Enters and leaves the kernel FPU context so that SSE/AVX
/// instructions can run safely in kernel mode without corrupting userspace FPU state.
/// </summary>
public static class KernelFpu
{
    /// <summary>
    /// Enters the kernel FPU context. Call before any kernel-mode SSE/AVX operation.
    /// </summary>
    /// <param name="io">Kernel I/O.</param>
    /// <param name="fpuKernEnterAddr">Kernel address of <c>fpu_kern_enter</c>.</param>
    /// <param name="sysentsAddr">Kernel address of the <c>sysent</c> table.</param>
    public static void Enter(PayloadKernelIo io, ulong fpuKernEnterAddr, ulong sysentsAddr)
    {
        PayloadKfncall.Call(io, sysentsAddr, fpuKernEnterAddr, 0, 0x800);
    }

    /// <summary>
    /// Leaves the kernel FPU context. Call after kernel-mode SSE/AVX operations complete.
    /// </summary>
    /// <param name="io">Kernel I/O.</param>
    /// <param name="fpuKernLeaveAddr">Kernel address of <c>fpu_kern_leave</c>.</param>
    /// <param name="sysentsAddr">Kernel address of the <c>sysent</c> table.</param>
    public static void Leave(PayloadKernelIo io, ulong fpuKernLeaveAddr, ulong sysentsAddr)
    {
        PayloadKfncall.Call(io, sysentsAddr, fpuKernLeaveAddr, 0, 0);
    }
}
