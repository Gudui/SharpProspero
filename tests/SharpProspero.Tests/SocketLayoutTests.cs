// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Net;
using SharpProspero.Platform;
using System.Runtime.InteropServices;
using Xunit;

namespace SharpProspero.Tests;

// The socket address structures must match the service's layout, or a bind or connect reads the wrong
// bytes. The offsets are computed from the header for the x86-64 target.
public sealed class SocketLayoutTests
{
    [Fact]
    public void SockaddrIn_MatchesTheHeader()
    {
        Assert.Equal(16, Marshal.SizeOf<SceNetSockaddrIn>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceNetSockaddrIn>(nameof(SceNetSockaddrIn.Len)));
        Assert.Equal(1, (int)Marshal.OffsetOf<SceNetSockaddrIn>(nameof(SceNetSockaddrIn.Family)));
        Assert.Equal(2, (int)Marshal.OffsetOf<SceNetSockaddrIn>(nameof(SceNetSockaddrIn.Port)));
        Assert.Equal(4, (int)Marshal.OffsetOf<SceNetSockaddrIn>(nameof(SceNetSockaddrIn.Addr)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceNetSockaddrIn>(nameof(SceNetSockaddrIn.VPort)));
    }

    [Fact]
    public void Sockaddr_IsSixteenBytes() => Assert.Equal(16, Marshal.SizeOf<SceNetSockaddr>());

    [Fact]
    public void EpollData_IsEightBytes() => Assert.Equal(8, Marshal.SizeOf<SceNetEpollData>());

    [Fact]
    public void EpollEvent_MatchesTheHeader()
    {
        // events at 0, then the union data at 8 after four bytes of padding.
        Assert.Equal(16, Marshal.SizeOf<SceNetEpollEvent>());
        Assert.Equal(0, (int)Marshal.OffsetOf<SceNetEpollEvent>(nameof(SceNetEpollEvent.Events)));
        Assert.Equal(8, (int)Marshal.OffsetOf<SceNetEpollEvent>(nameof(SceNetEpollEvent.Data)));
    }
}

// SocketAddress is pure managed logic, so its parsing and byte-order conversions are checked directly.
public sealed class SocketAddressTests
{
    [Fact]
    public void Parse_ReadsTheFourOctets()
    {
        var address = SocketAddress.Parse("192.168.1.10", 8080);
        Assert.Equal(192, address.A);
        Assert.Equal(168, address.B);
        Assert.Equal(1, address.C);
        Assert.Equal(10, address.D);
        Assert.Equal(8080, address.Port);
        Assert.Equal("192.168.1.10", address.IpString);
        Assert.Equal("192.168.1.10:8080", address.ToString());
    }

    [Theory]
    [InlineData("192.168.1")]        // too few octets
    [InlineData("1.2.3.4.5")]        // too many octets
    [InlineData("256.0.0.1")]        // octet out of range
    [InlineData("1.2.3.")]           // trailing dot
    [InlineData("a.b.c.d")]          // not numeric
    [InlineData("")]                 // empty
    public void TryParse_RejectsBadAddresses(string text)
    {
        Assert.False(SocketAddress.TryParse(text, 80, out _));
    }

    [Fact]
    public void AnyAndLoopback_HaveTheExpectedAddresses()
    {
        Assert.Equal("0.0.0.0", SocketAddress.Any(0).IpString);
        Assert.Equal("127.0.0.1", SocketAddress.Loopback(22).IpString);
        Assert.Equal(22, SocketAddress.Loopback(22).Port);
    }

    [Fact]
    public void ToNative_PacksTheAddressAndPortInNetworkOrder()
    {
        var address = SocketAddress.Parse("192.168.1.10", 8080);
        SceNetSockaddrIn native = address.ToNative();

        Assert.Equal(16, native.Len);
        Assert.Equal(Socket.AfInet, native.Family);
        // The address bytes on the wire are 192,168,1,10; as a little-endian word that is 0x0A01A8C0.
        Assert.Equal(0x0A01A8C0u, native.Addr);
        // Port 8080 is 0x1F90; in network order the bytes are 0x1F,0x90, a little-endian word of 0x901F.
        Assert.Equal(0x901F, native.Port);
    }

    [Fact]
    public void FromNative_RoundTripsToNative()
    {
        var original = SocketAddress.Parse("10.0.53.200", 443);
        SocketAddress restored = SocketAddress.FromNative(original.ToNative());
        Assert.Equal(original, restored);
    }

    [Fact]
    public void FromNetworkAddress_UnpacksOctets()
    {
        SocketAddress address = SocketAddress.FromNetworkAddress(0x0A01A8C0u, 8080);
        Assert.Equal("192.168.1.10", address.IpString);
    }

    [Fact]
    public void Ctor_RejectsAPortOutOfRange()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => new SocketAddress(1, 2, 3, 4, 70000));
    }
}
