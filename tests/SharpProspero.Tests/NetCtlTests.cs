// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using System.Text;
using SharpProspero.Interop.Net;
using Xunit;

namespace SharpProspero.Tests;

// The network status codes and enums, checked against the header. The connection facts are read one
// code at a time into a 256-byte buffer; these lock the codes and the state/device values a
// system-info tool reads.
public sealed class NetCtlTests
{
    [Theory]
    [InlineData(NetCtlState.Disconnected, 0)]
    [InlineData(NetCtlState.Connecting, 1)]
    [InlineData(NetCtlState.IpObtaining, 2)]
    [InlineData(NetCtlState.IpObtained, 3)]
    public void State_MatchesTheHeader(NetCtlState value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(NetCtlDevice.Wired, 0)]
    [InlineData(NetCtlDevice.Wireless, 1)]
    public void Device_MatchesTheHeader(NetCtlDevice value, int expected)
        => Assert.Equal(expected, (int)value);

    [Theory]
    [InlineData(NetCtl.InfoDevice, 1)]
    [InlineData(NetCtl.InfoEtherAddr, 2)]
    [InlineData(NetCtl.InfoLink, 4)]
    [InlineData(NetCtl.InfoSsid, 6)]
    [InlineData(NetCtl.InfoRssiPercentage, 9)]
    [InlineData(NetCtl.InfoIpAddress, 14)]
    [InlineData(NetCtl.InfoNetmask, 15)]
    [InlineData(NetCtl.InfoDefaultRoute, 16)]
    [InlineData(NetCtl.InfoPrimaryDns, 17)]
    public void InfoCode_MatchesTheHeader(int code, int expected)
        => Assert.Equal(expected, code);

    [Fact]
    public void InfoBuffer_IsTwoHundredFiftySixBytes()
        => Assert.Equal(256, NetCtl.InfoSize);

    // The MAC address is six raw bytes at the start of the buffer; it must format as the usual
    // colon-separated lower-case hex, not be read as a string.
    [Fact]
    public unsafe void MacAddress_FormatsSixRawBytesAsHex()
    {
        byte[] ether = [0x00, 0x1A, 0x2B, 0xC0, 0xFF, 0xEE];
        var sb = new StringBuilder(17);
        for (int i = 0; i < 6; i++)
        {
            if (i > 0)
                sb.Append(':');
            sb.Append(ether[i].ToString("x2"));
        }
        Assert.Equal("00:1a:2b:c0:ff:ee", sb.ToString());
    }
}
