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
        Assert.Equal(0x00005204u, d.Word3); // exact pinned upstream e24e25e Structured encoding
    }

    [Fact]
    public void Structured_RejectsForkAndMixedWord3NearMisses()
    {
        uint actual = AgcBufferDescriptor.Structured(0x1000, 16, 3).Word3;
        Assert.NotEqual(0x30008204u, actual); // tested CS fork value
        Assert.NotEqual(0x30005204u, actual); // misleading upstream-format plus fork type/OOB bits
        Assert.Equal(0x00005204u, actual);
    }

    [Fact]
    public void Constant_UsesSixteenByteRecordsAndFloat4Format()
    {
        var d = AgcBufferDescriptor.Constant(0x0000_0000_1000UL, sizeInBytes: 128);
        Assert.Equal(0x1000u, d.Word0);
        Assert.Equal(0u, d.Word1);                    // high address 0, no stride for constant buffers
        Assert.Equal(128u, d.Word2);                  // size in bytes
        Assert.Equal(0xfacu | (7u << 12) | (14u << 15) | (3u << 28), d.Word3); // RDNA2 Constant
    }

    [Fact]
    public void Structured_RejectsTooLargeStride()
    {
        Assert.Throws<System.ArgumentOutOfRangeException>(() => AgcBufferDescriptor.Structured(0, 1 << 14, 1));
    }

    [Fact]
    public void WriteShaderRegisters_WritesFourConsecutiveUserDataWords()
    {
        var descriptor = AgcBufferDescriptor.Structured(0x1234_5678_9ABCUL, 16, 3);
        var registers = new CxRegister[4];

        Assert.Equal(4, descriptor.WriteShaderRegisters(registers, 0x008C, 8));
        Assert.Equal((ushort)0x0094, registers[0].Offset);
        Assert.Equal((ushort)0x0095, registers[1].Offset);
        Assert.Equal((ushort)0x0096, registers[2].Offset);
        Assert.Equal((ushort)0x0097, registers[3].Offset);
        Assert.Equal(descriptor.Word0, registers[0].Value);
        Assert.Equal(descriptor.Word1, registers[1].Value);
        Assert.Equal(descriptor.Word2, registers[2].Value);
        Assert.Equal(descriptor.Word3, registers[3].Value);
    }

    [Fact]
    public void WriteShaderRegisters_RejectsShortDestinationAndNegativeOffset()
    {
        var descriptor = AgcBufferDescriptor.Structured(0x1000, 16, 3);
        Assert.Throws<System.ArgumentException>(() => descriptor.WriteShaderRegisters(new CxRegister[3], 0x008C, 8));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => descriptor.WriteShaderRegisters(new CxRegister[4], 0x008C, -1));
    }
}
