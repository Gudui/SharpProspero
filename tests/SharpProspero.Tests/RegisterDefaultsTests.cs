using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using SharpProspero.Graphics.Agc;
using Xunit;

namespace SharpProspero.Tests;

[CollectionDefinition("Register defaults", DisableParallelization = true)]
public sealed class RegisterDefaultsCollection { }

// The production API is exercised with an independently constructed native descriptor.
// Padded block-pointer storage keeps the OLD incorrect traversal safe during the red control;
// its later entries deliberately do not enumerate the records.
[Collection("Register defaults")]
public sealed unsafe class RegisterDefaultsTests : IDisposable
{
    private static readonly ushort[] Offsets =
    [0x0318, 0x031B, 0x031C, 0x031D, 0x031E, 0x031F, 0x0321, 0x0323,
     0x0324, 0x0325, 0x0390, 0x0398, 0x03A0, 0x03A8, 0x03B0, 0x03B8,
     0x000D, 0x0082, 0x0105, 0x0106];
    private readonly byte* _descriptor = (byte*)NativeMemory.AllocZeroed(0x30);
    private readonly CxRegister* _records = (CxRegister*)NativeMemory.AllocZeroed((nuint)(Offsets.Length * sizeof(CxRegister)));
    private readonly CxRegister** _blocks = (CxRegister**)NativeMemory.AllocZeroed((nuint)(Offsets.Length * sizeof(nint)));
    private static FieldInfo Field(string name) => typeof(RegisterDefaults).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    public RegisterDefaultsTests()
    {
        for (int i = 0; i < Offsets.Length; i++) _records[i] = new CxRegister(Offsets[i], (uint)(0xA000 + i));
        _blocks[0] = _records;
        _blocks[1] = _records + 16;
        *(CxRegister***)_descriptor = _blocks;
        *(uint*)(_descriptor + 0x20) = (uint)Offsets.Length;
        Field("_descriptor").SetValue(null, Pointer.Box(_descriptor, typeof(byte*)));
        Field("_traced").SetValue(null, false);
        Field("_tracedAll").SetValue(null, false);
    }

    [Theory]
    [InlineData(1)] [InlineData(4)] [InlineData(15)] [InlineData(19)]
    public void LookupReachesRecordsInsideBlocks(int index) =>
        Assert.Equal((uint)(0xA000 + index), RegisterDefaults.GetContextValue(Offsets[index]));

    [Fact]
    public void MissingLookupReturnsZero() => Assert.Equal(0u, RegisterDefaults.GetContextValue(0xFFFF));

    [Fact]
    public void AllRecordsPreserveFlatOrderAndValues()
    {
        var actual = RegisterDefaults.AllContextDefaults();
        Assert.Equal(Offsets.Length, actual.Length);
        for (int i = 0; i < actual.Length; i++)
        {
            Assert.Equal(Offsets[i], actual[i].Offset);
            Assert.Equal((uint)(0xA000 + i), actual[i].Value);
        }
    }

    [Fact]
    public void RenderTargetIncludesAllSixteenDefaults()
    {
        var block = RegisterDefaults.RenderTargetBlock();
        Assert.Equal(16, block.Length);
        for (int i = 0; i < block.Length; i++)
        {
            Assert.Equal(Offsets[i], block[i].Offset);
            Assert.Equal((uint)(0xA000 + i), block[i].Value);
        }
    }

    [Fact]
    public void TraceDescribesFlatRecordsAndActualMatches()
    {
        var trace = new List<string>();
        RegisterDefaults.RenderTargetBlock(trace.Add);
        RegisterDefaults.AllContextDefaults(trace.Add);
        Assert.Contains(trace, line => line.Contains("layout=flat_records") && line.Contains("render_target_matches=16/16"));
        Assert.Contains(trace, line => line.Contains("unique_offsets=20") && line.Contains("valid=20"));
        Assert.Equal(16, trace.FindAll(line => line.StartsWith("AGC_DEFAULT_RT ", StringComparison.Ordinal)).Count);
    }

    [Fact]
    public void NullTableIsRejectedBeforeReading()
    {
        *(CxRegister***)_descriptor = null;
        Assert.Throws<InvalidOperationException>(() => RegisterDefaults.GetContextValue(0));
    }

    [Fact]
    public void NullFirstBlockIsRejectedBeforeWalking()
    {
        _blocks[0] = null;
        Assert.Throws<InvalidOperationException>(() => RegisterDefaults.AllContextDefaults());
    }

    [Fact]
    public void ExcessiveCountIsRejectedBeforeWalking()
    {
        *(uint*)(_descriptor + 0x20) = 0x10001;
        Assert.Throws<InvalidOperationException>(() => RegisterDefaults.AllContextDefaults());
    }

    [Fact]
    public void EmptyDescriptorNeedsNoBlockDereference()
    {
        *(uint*)(_descriptor + 0x20) = 0;
        *(CxRegister***)_descriptor = null;
        Assert.Empty(RegisterDefaults.AllContextDefaults());
        Assert.Equal(0u, RegisterDefaults.GetContextValue(0x0318));
        Assert.All(RegisterDefaults.RenderTargetBlock(), record => Assert.Equal(0u, record.Value));
    }

    public void Dispose()
    {
        Field("_descriptor").SetValue(null, Pointer.Box(null, typeof(byte*)));
        Field("_traced").SetValue(null, false);
        Field("_tracedAll").SetValue(null, false);
        NativeMemory.Free(_descriptor);
        NativeMemory.Free(_blocks);
        NativeMemory.Free(_records);
    }
}
