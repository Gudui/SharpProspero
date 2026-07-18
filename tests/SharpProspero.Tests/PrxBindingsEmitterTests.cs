// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Prx;
using Xunit;

namespace SharpProspero.Tests;

public sealed class PrxBindingsEmitterTests
{
    [Fact]
    public void Emit_TypedBinding_ProducesFunctionPointerField()
    {
        var bindings = new[]
        {
            new PrxBinding("sceFooDoThing", "int", ["int", "void*"]),
        };

        string source = PrxBindingsEmitter.Emit("My.Bindings", "FooLib", "foo.prx", bindings);

        Assert.Contains("namespace My.Bindings;", source);
        Assert.Contains("public sealed unsafe class FooLib : IDisposable", source);
        Assert.Contains("public readonly delegate* unmanaged<int, void*, int> sceFooDoThing;", source);
        Assert.Contains("(delegate* unmanaged<int, void*, int>)module.GetFunctionPointer(\"sceFooDoThing\")", source);
        Assert.Contains("PrxModule.LoadFromPackage(\"foo.prx\")", source);
        Assert.Contains("public void Dispose() => _module.Dispose();", source);
    }

    [Fact]
    public void Emit_AddressOnlyBinding_ProducesIntPtrField()
    {
        var bindings = new[]
        {
            new PrxBinding("sceFooData", "", []),
        };

        string source = PrxBindingsEmitter.Emit("My.Bindings", "FooLib", "foo.prx", bindings);

        Assert.Contains("public readonly IntPtr sceFooData;", source);
        Assert.Contains("module.GetExport(\"sceFooData\")", source);
    }
}
