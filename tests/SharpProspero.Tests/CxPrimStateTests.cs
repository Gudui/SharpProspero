using SharpProspero.Graphics.Agc;
using Xunit;

namespace SharpProspero.Tests;

public class CxPrimStateTests
{
    [Fact]
    public void RequiredNggModeControlLimitsPixelWaveDeallocations()
    {
        Assert.Equal(2, CxPrimState.DriverMaxRegisters);
        Assert.Equal(3, CxPrimState.MaxRegisters);
        Assert.Equal((ushort)0x0314, CxPrimState.NggModeControlOffset);
        Assert.Equal(0x00000200u, CxPrimState.NggModeControlValue);
    }
}
