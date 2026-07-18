// SharpProspero.Tests
// Copyright (C) 2026 SvenGDK

using SharpProspero.Prx;
using Xunit;

namespace SharpProspero.Tests;

// Every identifier below was read out of a real media-playback module's export table and paired with
// the plain name the headers declare. A module keys its exports by identifier only, so this is the
// check that a name the SDK binds actually reaches the export it means to.
public sealed class SceNidGroundTruthTests
{
    [Theory]
    [InlineData("sceAvPlayerInit", "aS66RI0gGgo")]
    [InlineData("sceAvPlayerInitEx", "o9eWRkSL+M4")]
    [InlineData("sceAvPlayerPostInit", "HD1YKVU26-M")]
    [InlineData("sceAvPlayerAddSource", "KMcEa+rHsIo")]
    [InlineData("sceAvPlayerStart", "ET4Gr-Uu07s")]
    [InlineData("sceAvPlayerStop", "ZC17w3vB5Lo")]
    [InlineData("sceAvPlayerPause", "9y5v+fGN4Wk")]
    [InlineData("sceAvPlayerResume", "w5moABNwnRY")]
    [InlineData("sceAvPlayerIsActive", "UbQoYawOsfY")]
    [InlineData("sceAvPlayerSetLooping", "OVths0xGfho")]
    [InlineData("sceAvPlayerGetAudioData", "Wnp1OVcrZgk")]
    [InlineData("sceAvPlayerCurrentTime", "wwM99gjFf1Y")]
    [InlineData("sceAvPlayerJumpToTime", "XC9wM+xULz8")]
    [InlineData("sceAvPlayerStreamCount", "hdTyRzCXQeQ")]
    [InlineData("sceAvPlayerGetStreamInfo", "d8FcbzfAdQw")]
    [InlineData("sceAvPlayerEnableStream", "ODJK2sn9w4A")]
    [InlineData("sceAvPlayerDisableStream", "BOVKAzRmuTQ")]
    [InlineData("sceAvPlayerChangeStream", "buMCiJftcfw")]
    [InlineData("sceAvPlayerSetLogCallback", "eBTreZ84JFY")]
    [InlineData("sceAvPlayerClose", "NkJwDzKmIlw")]
    public void Compute_MatchesTheIdentifierTheModuleExports(string name, string identifier)
        => Assert.Equal(identifier, SceNid.Compute(name));
}
