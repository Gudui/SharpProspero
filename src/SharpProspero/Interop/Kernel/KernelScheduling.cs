// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Kernel;

/// <summary>
/// A scheduling priority. The policy calls take and return this one-field block rather than a bare
/// integer.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public struct SceKernelSchedParam
{
    /// <summary>
    /// The priority. A smaller number is served first, between
    /// <see cref="KernelThread.PriorityHighest"/> and <see cref="KernelThread.PriorityLowest"/>.
    /// </summary>
    public int Priority;
}

/// <summary>
/// Scheduling and thread-attribute bindings. A thread's policy and priority can be changed while it
/// runs through the calls that take a thread handle, or settled before it starts through an attribute
/// block. The block is a handle rather than a structure the caller lays out: prepare it with
/// <see cref="scePthreadAttrInit"/>, set what is wanted on it, hand it to the thread-creation call, and
/// release it with <see cref="scePthreadAttrDestroy"/>.
/// </summary>
/// <remarks>
/// A block is read when the thread starts, so changing it afterwards does not reach a running thread.
/// A block also has to be told to use its own settings rather than inherit the creator's - see
/// <see cref="scePthreadAttrSetinheritsched"/> - or its policy and priority are ignored.
/// </remarks>
public static unsafe partial class KernelScheduling
{
    private const string Lib = "libkernel";

    /// <summary>First-in first-out within a priority: a thread runs until it blocks or yields. Value 1.</summary>
    public const int SchedFifo = 1;

    /// <summary>The time-shared policy threads run under by default. Value 2.</summary>
    public const int SchedOther = 2;

    /// <summary>Round-robin within a priority: equal-priority threads take turns. Value 3.</summary>
    public const int SchedRoundRobin = 3;

    /// <summary>A thread created from the block can be joined. Value 0.</summary>
    public const int CreateJoinable = 0;

    /// <summary>A thread created from the block cleans itself up and cannot be joined. Value 1.</summary>
    public const int CreateDetached = 1;

    /// <summary>The block's own policy and priority are used. Value 0.</summary>
    public const int ExplicitSched = 0;

    /// <summary>The creating thread's policy and priority are used, and the block's are ignored. Value 4.</summary>
    public const int InheritSched = 4;

    /// <summary>Reads the policy and priority <paramref name="thread"/> is running under.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int scePthreadGetschedparam(nint thread, int* policy, SceKernelSchedParam* param);

    /// <summary>Sets the policy and priority of <paramref name="thread"/> in one call.</summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int scePthreadSetschedparam(nint thread, int policy, SceKernelSchedParam* param);

    /// <summary>
    /// Reads the identifier of the clock that counts only the processor time
    /// <paramref name="thread"/> has consumed, which
    /// <see cref="KernelClock.sceKernelClockGettime"/> then reads.
    /// </summary>
    /// <returns>Zero on success, or a negative error code.</returns>
    [LibraryImport(Lib)]
    public static partial int scePthreadGetcpuclockid(nint thread, int* clockId);

    /// <summary>Reports whether two handles name the same thread.</summary>
    /// <returns>Non-zero when they do.</returns>
    [LibraryImport(Lib)]
    public static partial int scePthreadEqual(nint first, nint second);

    /// <summary>
    /// Detaches <paramref name="thread"/>, so it releases itself when it ends and can no longer be
    /// joined.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadDetach(nint thread);

    /// <summary>Prepares an attribute block and writes its handle to <paramref name="attr"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrInit(nint* attr);

    /// <summary>Releases an attribute block.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrDestroy(nint* attr);

    /// <summary>Fills <paramref name="attr"/> with the settings <paramref name="thread"/> is running under.</summary>
    /// <remarks>
    /// The block must already be prepared. This is the route to reading a running thread's stack bounds
    /// and detach state, which have no call of their own.
    /// </remarks>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrGet(nint thread, nint* attr);

    /// <summary>Sets the stack size a thread created from the block is given.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrSetstacksize(nint* attr, nuint stackSize);

    /// <summary>Reads the stack size the block asks for.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrGetstacksize(nint* attr, nuint* stackSize);

    /// <summary>Reads the base and size of the stack the block describes.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrGetstack(nint* attr, void** stackAddress, nuint* stackSize);

    /// <summary>Sets the untouched region placed past the end of the stack to catch an overrun.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrSetguardsize(nint* attr, nuint guardSize);

    /// <summary>Reads the size of the region placed past the end of the stack.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrGetguardsize(nint* attr, nuint* guardSize);

    /// <summary>Chooses whether a thread created from the block can be joined.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrSetdetachstate(nint* attr, int state);

    /// <summary>Reads whether a thread created from the block can be joined.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrGetdetachstate(nint* attr, int* state);

    /// <summary>Confines a thread created from the block to the processors named by <paramref name="mask"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrSetaffinity(nint* attr, ulong mask);

    /// <summary>Reads the processors a thread created from the block would be allowed to run on.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrGetaffinity(nint* attr, ulong* mask);

    /// <summary>Sets the priority a thread created from the block starts at.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrSetschedparam(nint* attr, SceKernelSchedParam* param);

    /// <summary>Reads the priority a thread created from the block would start at.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrGetschedparam(nint* attr, SceKernelSchedParam* param);

    /// <summary>Sets the policy a thread created from the block runs under.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrSetschedpolicy(nint* attr, int policy);

    /// <summary>Reads the policy a thread created from the block would run under.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrGetschedpolicy(nint* attr, int* policy);

    /// <summary>
    /// Chooses between <see cref="InheritSched"/> and <see cref="ExplicitSched"/>. A block left at the
    /// former ignores whatever policy and priority were set on it.
    /// </summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrSetinheritsched(nint* attr, int inheritSched);

    /// <summary>Reads where a thread created from the block takes its policy and priority from.</summary>
    [LibraryImport(Lib)]
    public static partial int scePthreadAttrGetinheritsched(nint* attr, int* inheritSched);

    /// <summary>
    /// The highest priority number <paramref name="policy"/> accepts, or -1 with an error number set.
    /// </summary>
    /// <remarks>
    /// This is the libc-style call, so it fails by returning -1 rather than by returning a service code.
    /// It reports the numeric bounds of the policy; which end of that range runs first is the thread
    /// priority convention, where the smaller number is served first.
    /// </remarks>
    [LibraryImport(Lib)]
    public static partial int sched_get_priority_max(int policy);

    /// <summary>The lowest priority number <paramref name="policy"/> accepts, or -1 with an error number set.</summary>
    [LibraryImport(Lib)]
    public static partial int sched_get_priority_min(int policy);

    /// <summary>Gives up the rest of the calling thread's slice. Returns zero, or -1 with an error number set.</summary>
    [LibraryImport(Lib)]
    public static partial int sched_yield();
}
