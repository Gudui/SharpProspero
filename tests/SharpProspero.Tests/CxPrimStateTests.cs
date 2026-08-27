using SharpProspero.Graphics.Agc;
using Xunit;

namespace SharpProspero.Tests;

public class CxPrimStateTests
{
    [Fact]
    public void RequiredNggControlsMatchGfx10PipelineState()
    {
        Assert.Equal(2, CxPrimState.DriverMaxRegisters);
        Assert.Equal(4, CxPrimState.MaxRegisters);
        Assert.Equal((ushort)0x0314, CxPrimState.NggModeControlOffset);
        Assert.Equal(0x00000200u, CxPrimState.NggModeControlValue);
        Assert.Equal((ushort)0x020E, CxPrimState.NggClipControlOffset);
        Assert.Equal(0x0000007Au, CxPrimState.NggClipControlValue);
    }
}
