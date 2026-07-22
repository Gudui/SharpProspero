// SharpProspero - a C# SDK for on-device application modules.
// Copyright (C) 2026 SvenGDK

using SharpProspero.Interop.Agc;

namespace SharpProspero.Graphics.Agc;

/// <summary>
/// The graphics device: one-time setup, command-buffer submission, and the frame boundary. A frame is
/// recorded into a <see cref="DrawCommandBuffer"/> (wait for the target, set state and draw, set the
/// flip), then handed to the GPU with <see cref="Submit"/> and closed with <see cref="SuspendPoint"/>.
/// </summary>
public static unsafe class AgcDevice
{
    /// <summary>Prepares the graphics API. Call once before any other graphics work.</summary>
    public static void Initialize() => SceAgc.sceAgcInit(0);

    /// <summary>
    /// Submits a recorded command buffer to the graphics queue. The buffer's recorded contents run on
    /// the GPU; keep the buffer and everything it references alive until the GPU has finished.
    /// </summary>
    public static void Submit(DrawCommandBuffer commandBuffer) =>
        SceAgcDriver.sceAgcDriverSubmitDcb(commandBuffer.Handle);

    /// <summary>
    /// Marks the end of a frame's submission. If the application is being suspended, it happens here.
    /// Returns the API status.
    /// </summary>
    public static int SuspendPoint() => SceAgc.sceAgcSuspendPoint();
}
