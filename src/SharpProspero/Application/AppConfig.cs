// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop;
using SharpProspero.Interop.VideoOut;

namespace SharpProspero.Application;

/// <summary>Startup settings for a <see cref="ProsperoApp"/>.</summary>
public sealed class AppConfig
{
    /// <summary>Framebuffer width in pixels.</summary>
    public int Width { get; set; } = 1920;

    /// <summary>Framebuffer height in pixels.</summary>
    public int Height { get; set; } = 1080;

    /// <summary>Number of framebuffers in the swap chain.</summary>
    public int BufferCount { get; set; } = 2;

    /// <summary>
    /// The user the controller opens for. The display is not opened for a user: an application of this
    /// kind owns the whole output, and the call that opens it takes only the system.
    /// </summary>
    public int UserId { get; set; } = SceUser.System;

    /// <summary>Flip timing used each frame.</summary>
    public VideoOutFlipMode FlipMode { get; set; } = VideoOutFlipMode.VSync;

    /// <summary>Remove the boot splash before the first frame.</summary>
    public bool HideSplashScreen { get; set; } = true;

    /// <summary>Open a controller for <see cref="UserId"/> at startup.</summary>
    public bool OpenGamePad { get; set; } = true;
}
