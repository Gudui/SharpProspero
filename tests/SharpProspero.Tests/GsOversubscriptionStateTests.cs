using SharpProspero.Graphics.Agc;
using Xunit;

namespace SharpProspero.Tests;

public class GsOversubscriptionStateTests
{
    [Fact]
    public void RegisterContractUsesCorrectSpacesAndFullMasks()
    {
        Assert.Equal((ushort)0x0260, GsOversubscriptionState.GePcAllocOffset);
        Assert.Equal((ushort)0x0081, GsOversubscriptionState.SpiShaderPgmRsrc4GsOffset);
        Assert.Equal(0x000007FFu, GsOversubscriptionState.FullGePcAllocMask);
        Assert.Equal(0x007F0000u, GsOversubscriptionState.FullRsrc4GsMask);
    }
}
