// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

namespace SharpProspero.Payload;

/// <summary>
/// Kernel data addresses and structure field offsets for firmware 10.01.
/// </summary>
/// <remarks>
/// Every value was grounded from the kernel binary and cross-verified against at least two independent
/// kernel functions. The absolute addresses sit within the kernel data section whose base is
/// <see cref="KdataBase"/>; the field offsets are relative to the start of their containing structure.
/// </remarks>
public static class KernelOffsets1001
{
    /// <summary>The kernel data section base address.</summary>
    public const ulong KdataBase = 0xffffffff_80ED0000;

    /// <summary>The <c>allproc</c> list head (the first process in the linked list).</summary>
    public const ulong Allproc = 0xffffffff_83635d70;

    /// <summary>The kernel's root vnode pointer.</summary>
    public const ulong Rootvnode = 0xffffffff_83e73510;

    /// <summary>The host prison (<c>prison0</c>), the one a non-jailed credential points to.</summary>
    public const ulong Prison0 = 0xffffffff_82cdf4e0;

    /// <summary><c>p_list.le_next</c>: offset zero, the link that chains every process.</summary>
    public const int ProcList = 0x00;

    /// <summary><c>p_ucred</c>: the process credential pointer.</summary>
    public const int ProcUcred = 0x40;

    /// <summary><c>p_fd</c>: the file descriptor table pointer.</summary>
    public const int ProcFd = 0x48;

    /// <summary><c>p_pid</c>: the process identifier (four bytes).</summary>
    public const int ProcPid = 0xBC;

    /// <summary><c>p_comm</c>: the process name, an inline seventeen-byte array.</summary>
    public const int ProcComm = 0x5DC;

    /// <summary><c>fd_rdir</c>: the root directory vnode in the file descriptor table.</summary>
    public const int FdRdir = 0x10;

    /// <summary><c>fd_jdir</c>: the jail directory vnode in the file descriptor table.</summary>
    public const int FdJdir = 0x18;

    /// <summary><c>cr_uid</c>: the effective user identifier (four bytes).</summary>
    public const int UcredUid = 0x04;

    /// <summary><c>cr_ruid</c>: the real user identifier (four bytes).</summary>
    public const int UcredRuid = 0x08;

    /// <summary><c>cr_svuid</c>: the saved user identifier (four bytes).</summary>
    public const int UcredSvuid = 0x0C;

    /// <summary><c>cr_rgid</c>: the real group identifier (four bytes).</summary>
    public const int UcredRgid = 0x14;

    /// <summary><c>cr_svgid</c>: the saved group identifier (four bytes).</summary>
    public const int UcredSvgid = 0x18;

    /// <summary><c>cr_prison</c>: the prison pointer the credential belongs to.</summary>
    public const int UcredPrison = 0x30;

    /// <summary><c>cr_sceAuthID</c>: the authorization identifier (eight bytes).</summary>
    public const int UcredSceAuthId = 0x58;

    /// <summary><c>cr_sceCaps</c>: the first eight bytes of the capability set.</summary>
    public const int UcredSceCaps = 0x60;
}
