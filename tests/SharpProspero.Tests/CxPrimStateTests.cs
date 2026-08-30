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
        Assert.Equal(0x00000078u, CxPrimState.NggClipControlValue);
        Assert.Equal(0u, (CxPrimState.NggClipControlValue >> 1) & 1u);
        Assert.Equal(30u, (CxPrimState.NggClipControlValue >> 2) & 0xFFu);
    }
}
