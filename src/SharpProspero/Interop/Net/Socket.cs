// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using System.Runtime.InteropServices;

namespace SharpProspero.Interop.Net;

/// <summary>An IPv4 socket address. The port and address are held in network byte order.</summary>
/// <remarks>
/// The layout matches the service's <c>sockaddr_in</c>: a length byte, a family byte, a port, the
/// address, a virtual port, then padding, sixteen bytes in all. The higher-level
/// <see cref="Platform.SocketAddress"/> fills and reads it, so callers rarely touch it directly.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceNetSockaddrIn
{
    /// <summary>The length of this address, sixteen.</summary>
    public byte Len;

    /// <summary>The address family, <see cref="Socket.AfInet"/> for IPv4.</summary>
    public byte Family;

    /// <summary>The port, in network byte order.</summary>
    public ushort Port;

    /// <summary>The IPv4 address, in network byte order.</summary>
    public uint Addr;

    /// <summary>The virtual port, zero for an ordinary socket.</summary>
    public ushort VPort;

    private fixed byte _zero[6];
}

/// <summary>A generic socket address, the sixteen-byte form the send and receive calls take.</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SceNetSockaddr
{
    /// <summary>The length of this address.</summary>
    public byte Len;

    /// <summary>The address family.</summary>
    public byte Family;

    private fixed byte _data[14];
}

/// <summary>The user cookie carried alongside a readiness event.</summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public unsafe struct SceNetEpollData
{
    /// <summary>The cookie as a pointer.</summary>
    [FieldOffset(0)] public void* Ptr;

    /// <summary>The cookie as a 32-bit value.</summary>
    [FieldOffset(0)] public uint U32;

    /// <summary>The cookie as a 64-bit value.</summary>
    [FieldOffset(0)] public ulong U64;

    /// <summary>The cookie as a socket id.</summary>
    [FieldOffset(0)] public int Fd;
}

/// <summary>One readiness event reported by the poller: which events fired and the cookie for the socket.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct SceNetEpollEvent
{
    /// <summary>The events that fired, a combination of <see cref="Socket.EpollIn"/> and the others.</summary>
    public uint Events;

    private uint _pad;

    /// <summary>The cookie registered for the socket.</summary>
    public SceNetEpollData Data;
}

/// <summary>
/// Socket bindings: create sockets, connect or listen and accept, send and receive, and multiplex many
/// sockets in one thread through the poller. The address family is IPv4. These calls return a
/// non-negative value on success and a negative value on failure; on failure the per-thread error is
/// available through <see cref="sceNetErrnoLoc"/>. The higher-level wrappers in
/// <see cref="Platform"/> present a friendlier surface over these.
/// </summary>
public static unsafe partial class Socket
{
    private const string Lib = "libSceNet";

    /// <summary>The IPv4 address family.</summary>
    public const int AfInet = 2;

    /// <summary>A reliable byte-stream socket (TCP).</summary>
    public const int SockStream = 1;

    /// <summary>A datagram socket (UDP).</summary>
    public const int SockDgram = 2;

    /// <summary>The TCP protocol.</summary>
    public const int IpProtoTcp = 6;

    /// <summary>The UDP protocol.</summary>
    public const int IpProtoUdp = 17;

    /// <summary>The socket-level option group.</summary>
    public const int SolSocket = 0xffff;

    /// <summary>Option: allow a local address to be reused right away.</summary>
    public const int SoReuseAddr = 0x00000004;

    /// <summary>Option: keep an idle connection alive.</summary>
    public const int SoKeepAlive = 0x00000008;

    /// <summary>Option: allow broadcast datagrams.</summary>
    public const int SoBroadcast = 0x00000020;

    /// <summary>Option: the send buffer size.</summary>
    public const int SoSndBuf = 0x1001;

    /// <summary>Option: the receive buffer size.</summary>
    public const int SoRcvBuf = 0x1002;

    /// <summary>Option: the pending error, cleared by reading it.</summary>
    public const int SoError = 0x1007;

    /// <summary>Option: the send timeout, in microseconds.</summary>
    public const int SoSndTimeo = 0x1105;

    /// <summary>Option: the receive timeout, in microseconds.</summary>
    public const int SoRcvTimeo = 0x1106;

    /// <summary>Option: non-blocking mode, a non-zero value to enable.</summary>
    public const int SoNbio = 0x1200;

    /// <summary>Receive flag: read without removing the data from the queue.</summary>
    public const int MsgPeek = 0x00000002;

    /// <summary>Send or receive flag: do not block.</summary>
    public const int MsgDontWait = 0x00000080;

    /// <summary>Shutdown selector: further receives.</summary>
    public const int ShutRd = 0;

    /// <summary>Shutdown selector: further sends.</summary>
    public const int ShutWr = 1;

    /// <summary>Shutdown selector: both directions.</summary>
    public const int ShutRdWr = 2;

    /// <summary>Poller readiness: readable, or a pending connection on a listening socket.</summary>
    public const uint EpollIn = 0x00000001;

    /// <summary>Poller readiness: writable, or a completed connect.</summary>
    public const uint EpollOut = 0x00000002;

    /// <summary>Poller readiness: an error occurred.</summary>
    public const uint EpollErr = 0x00000008;

    /// <summary>Poller readiness: the peer hung up.</summary>
    public const uint EpollHup = 0x00000010;

    /// <summary>Poller operation: add a socket to the set.</summary>
    public const int EpollCtlAdd = 1;

    /// <summary>Poller operation: change a socket's interest.</summary>
    public const int EpollCtlMod = 2;

    /// <summary>Poller operation: remove a socket from the set.</summary>
    public const int EpollCtlDel = 3;

    /// <summary>Creates a socket. <paramref name="name"/> is a label for diagnostics.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sceNetSocket(string name, int domain, int type, int protocol);

    /// <summary>Binds a socket to a local address.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetBind(int s, SceNetSockaddr* addr, uint addrlen);

    /// <summary>Puts a stream socket into the listening state with the given accept backlog.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetListen(int s, int backlog);

    /// <summary>Accepts one pending connection, returning a new socket id. The peer address may be null.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetAccept(int s, SceNetSockaddr* addr, uint* addrlen);

    /// <summary>Connects a socket to a remote address.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetConnect(int s, SceNetSockaddr* name, uint namelen);

    /// <summary>Sends on a connected socket, returning the number of bytes sent.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetSend(int s, void* msg, nuint len, int flags);

    /// <summary>Sends a datagram to an explicit destination.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetSendto(int s, void* msg, nuint len, int flags, SceNetSockaddr* to, uint tolen);

    /// <summary>Receives on a connected socket; returns the byte count, or zero at an orderly close.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetRecv(int s, void* buf, nuint len, int flags);

    /// <summary>Receives a datagram and reports the sender address.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetRecvfrom(int s, void* buf, nuint len, int flags, SceNetSockaddr* from, uint* fromlen);

    /// <summary>Closes a socket.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetSocketClose(int s);

    /// <summary>Half- or fully closes a connection; <paramref name="how"/> selects the direction.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetShutdown(int s, int how);

    /// <summary>Cancels a blocking call on a socket so it can be closed promptly.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetSocketAbort(int s, int flags);

    /// <summary>Sets a socket option.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetSetsockopt(int s, int level, int optname, void* optval, uint optlen);

    /// <summary>Reads a socket option.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetGetsockopt(int s, int level, int optname, void* optval, uint* optlen);

    /// <summary>Reads the local address a socket is bound to.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetGetsockname(int s, SceNetSockaddr* name, uint* namelen);

    /// <summary>Reads the remote address of a connected socket.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetGetpeername(int s, SceNetSockaddr* name, uint* namelen);

    /// <summary>Returns the per-thread error pointer read after a socket call fails.</summary>
    [LibraryImport(Lib)]
    public static partial int* sceNetErrnoLoc();

    /// <summary>Creates a poller instance, returning its id.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sceNetEpollCreate(string name, int flags);

    /// <summary>Adds, changes or removes a socket in the poller set.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetEpollControl(int eid, int op, int id, SceNetEpollEvent* @event);

    /// <summary>Waits for readiness; returns the number of events written to <paramref name="events"/>.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetEpollWait(int eid, SceNetEpollEvent* events, int maxevents, int timeout);

    /// <summary>Destroys a poller instance.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetEpollDestroy(int eid);

    /// <summary>Unblocks a thread waiting in the poller so a server can shut down cleanly.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetEpollAbort(int eid, int flags);
}

/// <summary>Resolver bindings: turn a host name into an IPv4 address. The resolver draws on a network pool.</summary>
public static unsafe partial class Resolver
{
    private const string Lib = "libSceNet";

    /// <summary>The longest host name the resolver accepts.</summary>
    public const int HostnameMax = 255;

    /// <summary>Creates a resolver over a network pool, returning its id.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sceNetResolverCreate(string name, int memId, int flags);

    /// <summary>Resolves a host name to an address written into <paramref name="addr"/>.</summary>
    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int sceNetResolverStartNtoa(int rid, string hostname, uint* addr, int timeout, int retry, int flags);

    /// <summary>Destroys a resolver.</summary>
    [LibraryImport(Lib)]
    public static partial int sceNetResolverDestroy(int rid);
}
