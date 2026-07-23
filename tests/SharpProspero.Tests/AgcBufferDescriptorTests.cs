using SharpProspero.Graphics.Agc;
using Xunit;

namespace SharpProspero.Tests;

// Pins the four buffer-descriptor words against the documented encodings: the address split across
// words 0 and 1, the stride in word 1, the record count in word 2, and the channel/format selector in
// word 3 (0x5204 for a structured buffer, 0x4dfac for a constant buffer).
public sealed class AgcBufferDescriptorTests
{
    [Fact]
    public void Structured_PacksAddressStrideCountAndFormat()
    {
        var d = AgcBufferDescriptor.Structured(0x1234_5678_9ABCUL, strideInBytes: 36, elementCount: 100);
        Assert.Equal(0x56789ABCu, d.Word0);
        Assert.Equal(0x1234u | (36u << 16), d.Word1);
        Assert.Equal(100u, d.Word2);
        Assert.Equal(0x204u | (5u << 12), d.Word3);   // 0x5204
    }

    [Fact]
    public void Constant_UsesSixteenByteRecordsAndFloat4Format()
    {
        var d = AgcBufferDescriptor.Constant(0x0000_0000_1000UL, sizeInBytes: 128);
        Assert.Equal(0x1000u, d.Word0);
        Assert.Equal(16u << 16, d.Word1);             // high address 0, stride 16
        Assert.Equal(8u, d.Word2);                     // 128 / 16
        Assert.Equal(0xfacu | (77u << 12), d.Word3);   // 0x4dfac
    }

    [Fact]
    public void Structured_RejectsTooLargeStride()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => AgcBufferDescriptor.Structured(0, 1 << 14, 1));
    }
}
